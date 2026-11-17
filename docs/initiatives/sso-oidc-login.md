# Initiative: SSO sign-in for the web UI (OpenID Connect)

> Owner: server | Last reviewed: 2026-11-18 | Canonical: yes

Issue: #1782. Feature doc: `aicontext/features/server/sso-login/feature.md`.

## Problem

The HSM web UI has only its own users and passwords. The company identity provider (Authentik at `https://sso.soft-fx.net`) already signs people into Grafana (OIDC) and the VictoriaLogs servers (forward auth). HSM is the odd one out: separate passwords, manual account creation, and no central off-boarding.

## Goals

- Optional OpenID Connect sign-in next to the password login, with Authentik as the first provider and nothing provider-specific in code.
- Off by default: an install without the setting (e.g. a home server) behaves exactly as before.
- Rollout at SoftFx: HSM application in Authentik, SSO enabled on the production server.

## Non-Goals

- Mapping provider groups to product/folder roles (roles stay HSM's own; possible later step).
- Hiding the password form, or redirecting to the provider automatically.
- Provider-initiated logout or ending the provider session on HSM logout.
- SSO for the Sensor API, the management API or MCP (they keep access keys / API tokens).
- Storing the provider's `sub` (linking accounts that are renamed at the provider).

## Current Behavior

Cookie authentication with HSM's own users (`UserManager`, LevelDB `UserEntity`): `AccountController.Authenticate` checks the password hash and signs in a cookie that carries only the login; every request resolves it into the stored user (`MyCookieAuthenticationEvents`, `UserProcessorMiddleware`). Roles: `IsAdmin` plus per-product and per-folder Viewer/Manager.

## Decisions (with the owner, 2026-11-18)

| # | Question | Decision |
|---|---|---|
| 1 | SSO identity → HSM user | Claim `preferred_username` (configurable) equals the HSM login. No `sub` stored. |
| 2 | Unknown login | Auto-create (`AutoCreateUsers`, default on): no password, no roles, not admin. Access gate = Authentik policy binding. |
| 3 | Roles | Product/folder roles stay in HSM. `IsAdmin` optionally from `AdminGroup` — synced (grant and revoke) on every SSO sign-in when set. |
| 4 | Password login | Always stays (fallback); SSO button above the unchanged form. |
| 5 | Client secret | Environment only (`Oidc__ClientSecret`); the rest in the `Oidc` section of `appsettings.json`. |
| 6 | Session lifetime | Absolute `SessionLifetime`, default 12h, no sliding renewal. |
| 7 | Logout | Local only. |

Technical choices (implementation): Authorization Code + PKCE via `Microsoft.AspNetCore.Authentication.OpenIdConnect`; claims read without inbound renaming from the id_token and userinfo; an enabled but incomplete section switches SSO off with an `Error` log instead of stopping the server; an unreachable provider returns the person to the login page with a message.

## Work Breakdown

| Step | Scope | Notes |
|---|---|---|
| 1 | Server: settings, OIDC scheme, sign-in service, login page, tests, docs | #1782, this PR |
| 2 | Authentik: OAuth2/OpenID provider + application "HSM", policy binding to the allowed group, optional `hsm-admins` group | Owner; exact settings in the PR description and the feature doc ("Provider Setup") |
| 3 | Production: `HSM_OIDC_CLIENT_SECRET` in `.env`, `Oidc` section on, recreate `app`; check a sign-in and the `SSO sign-in is ON` log line | After step 1 is released |

## Risks

- **Username collision:** a provider account whose username equals a local HSM login (e.g. `admin`) signs into that HSM user. Accepted: the provider is the company's own and its usernames are administered; who may sign in is gated by the policy binding.
- **Admin sync revokes:** with `AdminGroup` set, an HSM admin who is not in the group loses `IsAdmin` on the next SSO sign-in (password sign-ins do not touch it).
- **Revocation delay:** a person disabled at the provider keeps HSM access until the cookie's `SessionLifetime` ends (deleting the HSM user ends it at once).
- **Redirect URI mismatch** behind a proxy: the server builds it from the request host; the bundled Caddy keeps the original `Host`, so it is `https://<HSM_DOMAIN>/signin-oidc`.

## Verification

- Server unit and flow tests: `src/tests/HSMServer.Core.Tests/Authentication/Sso/` (incl. a full round trip through the real OIDC handler against an in-process provider).
- Playwright: login page unchanged with SSO off (`auth/login_page_sso.spec.ts`).
- Manual: a local server with SSO pointed at an unreachable provider (button shown, friendly error, secret absent from the file); a secret put into the file is ignored and removed.
- After step 2: a real sign-in through Authentik on a test server.
