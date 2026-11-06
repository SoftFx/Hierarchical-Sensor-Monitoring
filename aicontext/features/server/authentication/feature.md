# Feature: Persisted Data Protection key ring (cookie sessions + antiforgery)

> Owner: server | Last reviewed: 2026-11-05 | Canonical: yes
> Scope: the ASP.NET Core Data Protection key ring that protects the auth cookie and antiforgery tokens — where it is stored, what must never change about that, and what operators see across restarts.

---

## Overview

The server's cookie sessions and antiforgery tokens are protected payloads keyed by the ASP.NET Core Data Protection key ring (`IDataProtectionProvider`). Until #1633 the ring was never persisted: it lived inside the container filesystem, so every container **recreate** (image upgrade, compose change, host rebuild with a recreated container) threw the ring away. Users saw forced re-logins for every cookie session issued before the restart, and every form opened before the restart failed antiforgery validation on submit (`HSM-error-log.txt`: `The key {...} was not found in the key ring`). A plain restart of the same container was fine — only a recreate loses the container's filesystem.

Since #1633 the ring is persisted to `Config/DataProtection-Keys/` (`AddHsmDataProtection`, wired from `Program.cs`). `Config/` is bind-mounted in both compose variants and survives a recreate, so sessions and pending forms survive with it.

## Invariants

- The key ring MUST live under `Config/` (the mounted settings volume): `Path.Combine(ServerConfig.ConfigPath, "DataProtection-Keys")` in `Program.cs`, folder name pinned by `HsmDataProtectionServiceCollectionExtensions.KeysFolderName`. Moving it silently abandons every existing session and antiforgery token — the same loss #1633 fixed.
- The application name is pinned (`SetApplicationName("HSMServer")`), never left to the environment default (entry assembly / content root): the ring stays scoped to this application even if the entry point or working directory changes, and can never collide with another application sharing the directory.
- A payload protected by one provider must validate with a fresh provider over the same directory (`DataProtectionKeyRingTests`) — the ring is a property of the **directory**, not of the process.
- At the `TicketDataFormat` level a lost ring does not throw: `SecureDataFormat` swallows the underlying `CryptographicException` into a null ticket (unauthenticated request, forced re-login); the raw exception surfaces only in the error log. Both observable behaviors are the #1633 symptom.
- Key rotation is the framework default: a new key is created when the current one approaches expiry (90-day default lifetime); old key files stay in the directory and keep decrypting existing payloads. Do not "clean up" `DataProtection-Keys/` — deleting key files invalidates sessions minted under them, exactly as the original bug did.

## Keys at rest (decision)

Key files on the volume are **not encrypted at rest** (no `ProtectKeysWithCertificate`/`ProtectKeysWithDpai`). The deliberate trade-off:

- The same `Config/` volume already stores the TLS server certificate (PFX, no password in the default self-signed setup), and the adjacent mounted `Databases/` stores all sensor history and access keys unencrypted. Whoever can read the volume already owns equivalent secrets; encrypting only the key ring protects nothing the install doesn't already disclose.
- Certificate-based protection ties key availability to the server PFX lifecycle: the default install uses the bundled self-signed certificate which can be regenerated, and a missing/rotated certificate would break cookie validation — reintroducing the failure mode #1633 fixed, now keyed to certificate handling instead of container recreation.
- Containers run as `user: '0'`; file permissions on the volume are the operator's boundary, same as for the PFX next to the keys.

If a deployment requires encrypted keys at rest, that is a future opt-in (e.g. `ProtectKeysWithCertificate` with a dedicated certificate), not a silent default change.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Container recreated (upgrade/compose change) → server reads the existing ring from `Config/DataProtection-Keys/` → previously issued cookies and antiforgery tokens keep validating | operator |
| 2 | No valid current key → a new key file is created in the directory on first use and becomes the default key | server |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `AddHsmDataProtection(services, keysDirectory)` | `src/server/HSMServer/Authentication/HsmDataProtectionServiceCollectionExtensions.cs` | The single registration point: creates the directory, persists to it, pins the application name |
| `Config/DataProtection-Keys/` | runtime layout | Key ring files (`key-*.xml`); deleting them invalidates sessions minted under them |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Authentication/HsmDataProtectionServiceCollectionExtensions.cs` | Registration (persist + application name) |
| `src/server/HSMServer/Program.cs` | Chooses the production location under `Config/` |

## Storage / Persistence

`Config/DataProtection-Keys/` — framework-managed XML key files (`key-*.xml`), one new file per rotation (90-day default), all past files retained by the framework and required to keep decrypting older payloads. The directory is created at startup if missing (`FileManager.SafeCreateDirectory`, same discipline as `Config/` itself).

## UI / Operator Visibility

Not operator-visible. The observable signals of a lost/rotated ring are behavioral: forced re-login, antiforgery failures on forms opened before the restart, and `CryptographicException ... was not found in the key ring` in `Logs/HSM-error-log.txt`.

## Dependencies

- Depends on: ASP.NET Core Data Protection (shared framework)
- Used by: cookie authentication (`Authentication`), antiforgery (MVC/Razor forms)

## Tests

`DataProtectionKeyRingTests` (server unit tests, two ServiceProviders over the same/different directories stand in for two container lives, payloads produced by the framework's own wire formats — `CookieAuthenticationOptions.TicketDataFormat` for the cookie, `IAntiforgery.GetTokens`/`ValidateRequestAsync` for the form token):

- happy path: cookie ticket from provider A validates with a fresh provider B over the same key directory, and the key file materialized in the directory;
- happy path: an antiforgery token pair issued by provider A passes `ValidateRequestAsync` on a fresh provider B over the same key directory (the exact validation whose failure produced the #1633 error log);
- negative path: the same cookie ticket fails (null, never a false validation) over a different key directory — the #1633 symptom at the API the cookie handler consumes.

## Notes

- **Upgrade note.** The first start on a version with this change performs one more forced re-login: the first key in the new location is generated then, and the ring that produced the old sessions died with the old container. Every later recreate is session-preserving.
- Non-Docker development runs get `Config/DataProtection-Keys/` under the working directory too — harmless, consistent with `appsettings.Development.json` living there.
