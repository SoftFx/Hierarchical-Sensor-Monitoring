# ADR-0011: VictoriaLogs read path is the HSM admin session at /logs/

**Status:** Accepted
**Date:** 2026-10-20
**Supersedes:** the read-path/auth portion of ADR-0008 (its storage, shipping, retention, and redaction decisions stand)

---

## Context

ADR-0008 exposed VictoriaLogs' UI and query API through Caddy behind a separate basic-auth credential (`VL_UI_USER`/`VL_UI_PASSWORD`): access equaled knowledge of one shared password, rotation meant editing `.env` and recreating the container, and the credential was deliberately disconnected from HSM users and roles. Operators asked for log search to follow the HSM login instead — admins only — at a predictable path, with no second credential to maintain (#1580). A design session the same day fixed the shape: prefix mount under `/logs/`, Caddy `forward_auth` against a tiny app endpoint (the log data plane must not flow through the app), full removal of the basic-auth pair, and browser-redirect/plain-401 semantics at the gate.

## Decision

- VictoriaLogs runs with `-http.pathPrefix=/logs`, so its built-in UI (`/logs/select/vmui`) and the LogSQL query API (`/logs/select/logsql/*`) are served natively under the prefix and vmui generates its own correct URLs; vlagent ships to `http://victorialogs:9428/logs/insert/native`. Caddy proxies `/logs/select/*` pass-through — no path rewriting — on the web listeners, and only that subtree: `/logs/insert/*`, `/logs/delete/*`, and internal endpoints stay compose-network-only.
- The gate is Caddy `forward_auth` to a new app endpoint `GET /logs-auth`, reached with the same upstream transport treatment as the HSM proxy (TLS verification skipped, Host passthrough, wait-for-start). The endpoint decides by principal shape — a `UserProcessorMiddleware` user model, never `Identity.IsAuthenticated`, because the stored `User` carries no identity and an HsmApiToken principal is never a `User`:
  - authenticated admin → `204`; Caddy admits the request;
  - anonymous browser navigation (`Accept: text/html`) → `302` to the HSM login page, which Caddy passes back to the client as-is;
  - anonymous anything else (vmui fetches, curl) → plain `401`, mirroring the `/api/v1` 401-not-redirect convention;
  - authenticated non-admin → `403`.
- The basic-auth credential pair is removed entirely: `VL_UI_USER`/`VL_UI_PASSWORD`, the entrypoint's bcrypt generation and its validation (placeholder, minimum length, username charset), and the credential-selected Caddyfile snippet (`victorialogs-off`) are gone. Log routes are unconditional in the Caddyfile; their availability tracks the `logs` compose profile (a profile-off deployment gets a 502 — Caddy cannot see profiles). The endpoint is excluded from the OpenAPI document like `/mcp`.
- Ships as the pinned image `hsmonitoring/hsm-caddy:2.11.4-4` under ADR-0007's immutable-tag policy.

## Consequences

- One credential system: an HSM admin login is the only key to the logs; rotation, revocation, and user management reuse the HSM user machinery. Non-admins and anonymous clients are locked out by construction, not by password hygiene.
- Version coupling: the new Caddy image requires an app image carrying `/logs-auth`; with an older app, `/logs/*` fails closed (refused) until the app is updated. Deploy flows update both images together.
- Browsing logs requires the app to be up (it performs the auth check), but log data traffic bypasses the app process entirely: log search adds no load to Kestrel, and an app restart degrades the routes to refusals for its own duration — the same availability as the HSM UI itself.
- ADR-0008's default-off property is gone: a fresh install with the `logs` profile serves the admin-gated routes immediately. Accepted, because the gate is a real auth boundary (an HSM admin session), not a shared secret.
- A session expiring mid-browsing turns vmui's requests into `401`s until re-login (the UI was loaded before the redirect, so only its background calls fail).
- Same-origin residual, unchanged in kind from ADR-0008: an XSS in vmui rides the admin cookie. A compromised admin session implies log access — but such a session already implies far more of HSM.
- Unauthenticated probes now cost one internal GET to the app per request instead of a bcrypt check — strictly cheaper than the cost-10 basic auth it replaces.
- Headless query access through the public routes is gone with the shared credential; the planned agent path is the HsmApiToken-authenticated MCP log tools (ADR-0008 Phase 2, #1391), which call the same query API through the app.

## Alternatives Considered

- **App-level proxying (YARP) of `/logs/*` under `[AuthorizeIsAdmin]`:** rejected — all log-viewing traffic would flow through the app process, and the moments an operator needs logs most (app restarts/updates) are exactly when the app cannot serve them; also far more .NET code than a one-action controller.
- **Keep basic auth and add an admin check on top:** rejected — keeps the second credential this change exists to remove.
- **Caddy path rewriting (`handle_path`) instead of `-http.pathPrefix`:** rejected — vmui must generate its own asset/API URLs; the server-side prefix flag is the vendor-documented mechanism for proxied deployments, while rewriting would depend on vmui's internal path assumptions.

## References

- #1580 — the spec issue (user stories, testing seams, out-of-scope)
- ADR-0008 — the log-storage decision this builds on
- ADR-0007 — the pinned Caddy image policy
