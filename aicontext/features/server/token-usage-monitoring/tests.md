# Feature tests: Token usage monitoring

> Owner: server | Last reviewed: 2026-09-16 | Canonical: yes

## Unit — `src/tests/HSMServer.Core.Tests/Middleware/ApiTokenUsageMiddlewareTests.cs`

The measurement middleware against a mocked `IApiTokenUsageMonitor` (the sensors registry lives inside the booted collector and is not unit-constructible):

- `TokenRequest_OnApiV1_AttributesToTheToken` — login + EntityId, REST channel, no MCP, no failure.
- `TokenIdentity_MaterializingDuringNext_AttributesToTheToken` — the REAL pipeline shape (#1402 review blocker pin): HsmApiToken is not the default scheme, the principal appears inside `next` (AuthorizationMiddleware replaces `context.User`); the observation-time resolution must see it.
- `TokenRequest_OnMcp_UsesTheMcpChannel`.
- `TokenRequest_PathIsCaseInsensitiveForTheChannel` — `/MCP` counts as MCP, agreeing with endpoint routing.
- `TokenRequest_EveryStatusCodeCounts` — a 404 response still attributes (403/404/409 are work).
- `NoToken_401_CountsAuthenticationFailureOnly`.
- `CookiePrincipal_200_NothingCounted` — the cookie-only token-lifecycle family is neither usage nor failure.
- `RevokedMidRequest_NoAttributionNoFailure` — the revocation race.
- `UnmeasuredPath_PassesThroughUncounted`.
- `SensorThrow_NeverBreaksTheRequest` — the never-break contract.
- `SanitizeLogin` theory — separators collapse, whitespace trims; the tree stays one level per intended level.

`ManagementPipelineOrderTests` pins the middleware between `UseAuthentication` and `UseAuthorization` — the position is load-bearing (401 visibility).

## Not covered (deliberate)

- The sensors registry itself (`ApiTokenUsageSensors`/`ApiTokenUsageNode`) — it is a thin composition over the collector's sensor factories, exercised end-to-end by a running server; unit-testing it would mock the collector into tautology.
- E2E: a live request producing a visible subtree — a Playwright/manual check for the acceptance walkthrough; no local harness boots the collector + server together today.
