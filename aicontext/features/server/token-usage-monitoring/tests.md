# Feature tests: Token usage monitoring

> Owner: server | Last reviewed: 2026-09-16 | Canonical: yes

## Unit — `src/tests/HSMServer.Core.Tests/Middleware/ApiTokenUsageMiddlewareTests.cs`

The measurement middleware against a mocked `IApiTokenUsageMonitor` (the sensors registry lives inside the booted collector and is not unit-constructible):

- `TokenRequest_OnApiV1_AttributesToTheToken` — login + EntityId, REST channel, no MCP, no failure.
- `TokenIdentity_MaterializingDuringNext_AttributesToTheToken` — the REAL pipeline shape (#1402 review blocker pin): HsmApiToken is not the default scheme, the principal appears inside `next` (AuthorizationMiddleware replaces `context.User`); the observation-time resolution must see it.
- `TokenRequest_OnMcp_UsesTheMcpChannel`.
- `McpPath_BeyondTheEndpoint_IsNotMeasured` — `/mcp` is the exact endpoint, not the prefix (the bearer guard's semantics); `/mcp/other` passes through uncounted.
- `TokenRequest_PathIsCaseInsensitiveForTheChannel` — `/MCP` counts as MCP, agreeing with endpoint routing.
- `TokenRequest_EveryStatusCodeCounts` — a 404 response still attributes (403/404/409 are work).
- `NoToken_401_CountsAuthenticationFailureOnly`.
- `CookiePrincipal_200_NothingCounted` — the cookie-only token-lifecycle family is neither usage nor failure.
- `RevokedMidRequest_NoAttributionNoFailure` — the revocation race.
- `UnmeasuredPath_PassesThroughUncounted`.
- `SensorThrow_NeverBreaksTheRequest` — the never-break contract.
- `NextThrows_ExceptionPropagatesAndStillAttributed` — the finally contract's other half: a throwing `next` is still attributed, and the ORIGINAL exception propagates unchanged.
- `SanitizeLogin` theory — separators collapse, whitespace trims, a missing name stays one `_` segment; the tree stays one level per intended level.

`ManagementPipelineOrderTests` pins the middleware between `UseAuthentication` and `UseAuthorization` — the position is load-bearing (401 visibility).

## Unit — `src/tests/HSMServer.Core.Tests/BackgroundServices/ApiTokenUsageSensorsTests.cs`

The eviction state machine against a mocked `IDataCollector` (the created sensor mocks carry `IDisposable` exactly like the concrete monitoring sensors — this suite is where the round-5 no-op-re-disposal regression lives and is caught):

- `Evict_EveryLaterSweep_RestopsTheTombstonedSensors` — the anti-resurrection contract: evict + every later sweep re-stops the tombstoned INSTANCES (the node's terminal `_disposed` guard must not gate the sweep's stop).
- `EvictedToken_IsNeverRecreated` — the occupied-path guard: a straggler value for a tombstoned token is dropped (logged), never rebuilt into the dead instances.
- `LiveToken_IsNeverEvicted`.

## Not covered (deliberate)

- The real collector's storage/dedup behaviour (occupied paths, restart re-initialization) — the unit suite pins the registry's side of the contract with mocks; the collector side is its own library's tests plus a running server.
- E2E: a live request producing a visible subtree — a Playwright/manual check for the acceptance walkthrough; no local harness boots the collector + server together today.
