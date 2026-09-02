# ADR-0003: HsmApiToken is an isolated, non-default scheme behind a fail-closed /api/v1 area

**Status:** Accepted
**Date:** 2026-09-01
**Supersedes:** —

---

## Context

Personal API tokens (#1356, epic #1347) need an ASP.NET Core authentication surface on top of the token store (merged in #1360). The server already has one default scheme — cookie — behind every legacy MVC/Razor page, plus `UserProcessorMiddleware` which, on the SitePort listener, **replaces** `HttpContext.User` with the stored HSM user resolved by `Identity.Name`. Both listeners (SitePort 44333, SensorPort 44330) currently share one routing table.

The initiative (`docs/initiatives/fine-grained-api-token-authentication.md`, normative) names the risks this decision answers: principal replacement can restore unrestricted owner rights; missing port isolation can expose management routes on the collector port; and a token credential sent to a legacy `[Authorize]` page must never render a login redirect or a `BaseController` 500.

## Decision

1. **`HsmApiToken` is never the default, forwarded, or policy scheme.** Cookie keeps `DefaultAuthenticateScheme`/`DefaultChallengeScheme`, and the `DefaultPolicy` behind bare `[Authorize]` is **explicitly pinned** to the cookie scheme (`HsmApiTokenServiceCollectionExtensions.AddHsmApiTokenAuthorization`) — a token identity can never satisfy a legacy authorization, whatever the default scheme resolves to in the future.
2. **Management endpoints select one named policy** (`HsmApiTokenDefaults.ManagementPolicy`) that authenticates through the HsmApiToken scheme only and requires the single-identity token principal shape. A cookie session without a bearer credential therefore challenges as a plain 401 (never a login redirect); mixed/multiple identities fail closed as a denial.
3. **`/api/v1` is a fail-closed area** enforced by `ManagementApiGuardMiddleware` before authentication: an endpoint is reachable only on the SitePort listener (via the immutable `HsmListenerBindings` registry, the same instance that drove `Listen`) and only when it carries `[ManagementApi]` and requires its family's authorization — the management policy, or inside the reserved cookie-only `/api/v1/api-tokens` family a cookie `[Authorize]` (the default policy); there is no fallback policy, so an endpoint without any `[Authorize]` is anonymous and unreachable, exactly like an `[AllowAnonymous]` one. Everything else in the area is a plain 404 — a newly added route without the metadata is unreachable by default, and 404 (not 403) never confirms a route's existence on the wrong port.
4. **A token credential never enters the legacy pipeline**: `LegacyBearerGuardMiddleware` answers an `hsm_pat_` bearer outside `/api/v1` with a generic non-redirecting 401, performing no token lookup.
5. **`UserProcessorMiddleware` passes token principals through untouched** (short-circuit on the HsmApiToken identity), so nothing between authentication and the controller replaces the principal with the unrestricted stored user.
6. **Authorization is a per-request intersection** (`ApiTokenAuthorizationService`): `ownerCurrentlyAllows AND tokenGrantAllows(currentBoundary(resource))`, recomputed from the authoritative stores every call, with the anti-enumeration 403/404 split baked into the returned decision.

## Consequences

- Legacy MVC/Razor, collector access-key auth, and the existing Swagger/Grafana surfaces are untouched by construction; the token handler only runs where the named policy selects it.
- Management controllers derive from `ControllerBase` (never `BaseController`) and pair `[ManagementApi]` with the management policy — the guard makes any other combination unreachable, so convention violations fail closed instead of open.
- The listener registry is immutable per process: port config changes take effect only on restart, by design ("do not independently capture mutable config values").
- `/api/v1/api-tokens` is the ONLY place cookie authorization may appear inside the area; any future exception must extend this ADR, not just the middleware constant.
- The scheme, policies, guards and evaluator are unit-pinned in `src/tests/HSMServer.Core.Tests/Authentication/ApiTokens/` (handler, isolation, guards, evaluator matrices) — changing any single piece without the others fails a named test.
