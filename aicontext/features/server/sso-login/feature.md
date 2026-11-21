# Feature: SSO sign-in (OpenID Connect) for the web UI

> Owner: server | Last reviewed: 2026-11-18 | Canonical: yes
> Scope: the optional OpenID Connect sign-in next to the password login (#1782): settings, how a provider identity becomes an HSM user, the cookie it issues, and what the provider must be configured with. Initiative: `docs/initiatives/sso-oidc-login.md`.

---

## Overview

The web UI can sign people in through an external OpenID Connect provider (Authentik is the first one; nothing in code is Authentik-specific). It is **off by default**: without an enabled, complete `Oidc` section the server registers no OIDC scheme, the login page has no SSO button, and `/Account/Sso` answers 404 — the same behavior as before #1782.

When on, the login page shows **Sign in with &lt;DisplayName&gt;** above the unchanged password form. Password login always stays available as the fallback.

## Business Rules

1. **Identity mapping.** The value of the claim `Oidc.UsernameClaim` (default `preferred_username`) is the HSM login, compared exactly (case-sensitive, like password login) after trimming. The provider's `sub` is not stored; the storage format did not change. Renaming a person at the provider therefore means a different HSM user. **Trust assumption:** a provider account signs into the existing HSM user of the same login, password users included, so the provider must not let people choose or change their own username (Authentik: System → Settings → "Allow users to change username" stays off, the default). The one exception is the built-in admin `default` (created with password `default` on a fresh database): SSO never signs it in or creates it (`ReservedUsername`).
2. **Auto-create.** A login with no HSM user is created on its first SSO sign-in when `Oidc.AutoCreateUsers` is true (default): no password, no product or folder roles, not admin unless rule 3 says so. It sees an empty tree until an admin grants roles. A user without a password cannot use password login (`UserManager.TryAuthenticate` rejects an empty hash) until an admin sets one. With `AutoCreateUsers` false an unknown login is rejected with a message on the login page.
3. **Admin from a group.** When `Oidc.AdminGroup` is set, every SSO sign-in sets `IsAdmin` to membership in that group (claim `Oidc.GroupsClaim`, default `groups`; group names compared ignoring case) — it revokes, and it grants **only to users without a password** (the ones SSO created): a password user may have been created by someone else under that login before SSO was on, and password sign-ins never re-sync `IsAdmin`, so such users get admin from an HSM admin (a warning is logged). A sign-in whose token and userinfo have **no** groups key at all leaves `IsAdmin` unchanged (warning in the log): a missing claim is not "not a member". An **empty** list (`"groups": []` in userinfo) is "in no groups" and revokes: the handler turns a JSON array into one claim per element, so a claim action marks the key's presence (`hsm:groups-present`, never stored in the cookie). Empty `AdminGroup`: SSO never touches `IsAdmin`. Password sign-ins never touch it. Product and folder roles (Viewer/Manager) are always HSM's own and are granted in the UI.
4. **Who may sign in** is decided at the provider (policy binding of the application, e.g. to a group). HSM has no group gate of its own. While SSO is on, anonymous self-registration (`/Account/Registration`, `/Account/Registrate`) answers 404 and its link is hidden: otherwise an outsider could create the login of a future SSO user first, with a password of their own. Admins still create users on the Users page. **Before turning SSO on, audit the existing users:** a self-registered account whose login matches a provider username is linked to that person (rule 1) and keeps whatever password its creator chose — delete or re-password unknown ones.
4a. **Login rules.** An SSO login must follow the rules of the user forms — at most 64 characters of `a-z A-Z 0-9 . _ @ + -` — or the sign-in is rejected (`InvalidUsername`).
5. **Session lifetime.** An SSO sign-in issues the same auth cookie as a password sign-in (one name claim, resolved into the stored user per request), persistent, with an **absolute** lifetime `Oidc.SessionLifetime` (default 12h, allowed 5 min…30 days) and no sliding renewal (`AllowRefresh = false`). When it ends, the person goes through the provider again (usually without typing a password), which also re-applies rule 3. Deleting the HSM user ends the session at once (cookie validation finds no user), but with `AutoCreateUsers` on the person's next SSO sign-in creates it again (no roles; admin again if in `AdminGroup`). **To remove access, remove the person at the provider** (policy binding / group), or run with `AutoCreateUsers` off. A user that also has a password keeps password login (and any admin rights given in HSM) until an HSM admin removes them. The provider's tokens are not kept.
6. **Logout** is local: the HSM cookie is removed; the provider session (shared with Grafana and the log servers) stays.
7. **Failures** return to the login page with `?ssoError=<code>` and a fixed message: `MissingUsername`, `ReservedUsername`, `InvalidUsername`, `UnknownUser`, `CreateFailed` (also a storage error while creating or updating the user), `ProviderFailure` (the provider answered with an error, or the callback failed validation), `ProviderUnavailable` (the challenge could not start, typically the discovery document is unreachable; the exception is logged). Nothing is signed in.
8. **Logout is local only, also at the protocol level:** the handler's front-channel sign-out endpoint (`/signout-oidc`) is switched off, otherwise any site could clear the HSM cookie with a GET.

## Configuration

The `Oidc` section of `Config/appsettings.json` (written with defaults by the startup resave on every server, `Enabled: false`):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Turns SSO on. |
| `Authority` | — | Issuer URL, absolute `https`. Discovery: `<Authority>/.well-known/openid-configuration`. Authentik: `https://<authentik host>/application/o/<application slug>/`. |
| `ClientId` | — | Client ID of the HSM application at the provider. |
| `Scopes` | `openid profile` | Space-separated; must contain `openid`. |
| `UsernameClaim` | `preferred_username` | Claim holding the HSM login. |
| `GroupsClaim` | `groups` | Claim listing the groups (one claim per group or a JSON array). |
| `AdminGroup` | empty | Group whose members are HSM admins (rule 3). |
| `AutoCreateUsers` | `true` | Rule 2. |
| `DisplayName` | `SSO` | Text on the login button. |
| `SessionLifetime` | `12:00:00` | Rule 5. |

**Client secret — environment only:** `Oidc__ClientSecret` (compose passes `HSM_OIDC_CLIENT_SECRET` from `.env`). `ServerConfig` rewrites the settings file on every start and the file is registered after the environment provider, so the secret is `[JsonIgnore]` and read with `OidcConfig.ReadClientSecret` from the environment, the same discipline as `Kestrel.TrustedProxies`. A secret found in the file is ignored, removed by the same start's resave and reported at `Error` level **without its value**.

**Invalid section = SSO off, server up.** The section is bound key by key (`OidcConfig.Bind`), so a value of the wrong type (`"SessionLifetime": "12h"`, `"Enabled": "yes"`, a list where one value belongs) keeps its default, is reported, and switches SSO off instead of failing startup; the valid values survive the resave. `OidcConfig.Validate` does not throw either: an enabled section with a missing secret, a non-https authority, scopes without `openid`, an empty client ID or username claim, `AdminGroup` without `GroupsClaim`, or a lifetime out of range logs one `Error` line listing every problem and leaves SSO off; password login keeps working. A valid section logs `SSO sign-in is ON: provider …, client …`.

## Provider Setup (Authentik)

An OAuth2/OpenID provider plus an application for HSM:

- Client type **Confidential**; the client ID and secret go to `Oidc.ClientId` and `Oidc__ClientSecret`.
- **Redirect URI** (strict): `https://<HSM address>/signin-oidc`, exactly the address users open (behind the bundled Caddy the original `Host` is kept, so it is `https://<HSM_DOMAIN>/signin-oidc`).
- Scopes: `openid`, `profile` (Authentik's `profile` scope mapping carries `preferred_username` and `groups`). Signing key: any (RS256 by default).
- The token endpoint is called with `client_secret_post`; PKCE (`S256`) is always sent.
- Application policy binding: the group(s) allowed into HSM.
- Users must not be able to change their own username (rule 1).
- Claims needed: `sub`, `preferred_username` (or the configured `UsernameClaim`), and `groups` when `AdminGroup` is used. They may come in the id_token or from the userinfo endpoint — HSM reads both.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Login page → **Sign in with …** → `GET /Account/Sso` → challenge (code + PKCE) → provider → `POST /signin-oidc` → code exchange, id_token + userinfo validation → `SsoSignInService.ResolveUser` → HSM cookie → `/Home` | user |
| 2 | First SSO sign-in of an unknown login → user created without password/roles | user |
| 3 | SSO sign-in with `AdminGroup` set → `IsAdmin` synced to membership | user |
| 4 | Cookie lifetime ends → next request redirects to the login page → workflow 1 | server |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/ServerConfiguration/Sections/OidcConfig.cs` | Settings, environment-only secret, validation |
| `src/server/HSMServer/Authentication/Sso/SsoSignInService.cs` | Rules 1–3: provider identity → HSM user |
| `src/server/HSMServer/Authentication/Sso/HsmOidc.cs` | Scheme registration (`AddHsmOidc`), `HsmOidcEvents` (cookie principal, lifetime, failure redirects), the shared `HsmCookiePrincipal` |
| `src/server/HSMServer/Controllers/AccountController.cs` | `Index` (button, error text), `Sso` (challenge) |
| `src/server/HSMServer/Views/Account/Index.cshtml` | Login page |

## Invariants

- The user administration actions (`AccountController.CreateUser`, `UpdateUser`, `RemoveUser`) are admin-only (`[AuthorizeIsAdmin]`), like the Users page: an auto-created SSO user must not be able to grant itself rights.

- With SSO off the authentication schemes are exactly cookie + `HsmApiToken`, and the default challenge stays the cookie scheme (unauthenticated browsers land on the HSM login page, never at the provider).
- The client secret is never written to `Config/appsettings.json` and never logged.
- The HSM cookie carries only the login claim, whatever signed the person in.
- SSO never changes product or folder roles.

## Tests

`src/tests/HSMServer.Core.Tests/Authentication/Sso/`:

- `OidcConfigTests` — defaults, every validation error, key-by-key binding of malformed values, env-only secret (read, blank, detected in a file, never serialized), resave stability of `Scopes`.
- `SsoSignInServiceTests` — rules 1–3: mapping, the reserved `default` admin, trimming, configurable claims, case-sensitive logins, auto-create on/off, storage refusal, parallel creation, admin grant/revoke/no-op, case-insensitive groups, product roles untouched.
- `HsmOidcTests` — scheme absent when off or incomplete, default challenge unchanged, options (code flow, PKCE, no token saving, no inbound claim mapping, callback path, scopes, claim actions), `TicketReceived` principal/lifetime/redirects, `RemoteFailure`, login page model and `Sso` action.
- `OidcSignInFlowTests` — the full round trip through the real OIDC handler against an in-process provider (discovery, token and userinfo endpoints, RS256 id_token): PKCE verifier checked at the token endpoint, redirect URI and scopes, the issued cookie authenticates and expires after `SessionLifetime`, `/signout-oidc` is not handled, missing username and provider error return to the login page without a cookie.

Playwright: the default E2E server runs with SSO off; `auth/login_page_sso.spec.ts` pins that the login page then has no SSO button.
