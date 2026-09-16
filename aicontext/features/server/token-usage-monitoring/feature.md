# Feature: Token usage monitoring (self-monitoring)

> Owner: server | Last reviewed: 2026-09-16 | Canonical: yes
> Scope: per-token self-monitoring of management-API access (#1402) — request rate and request duration per API token, REST (/api/v1) and MCP (/mcp), plus the aggregate authentication-failure counter. The operator's performance picture of token traffic.

---

## Overview

Issue #1402 answers "which tokens are used, how much, and how slow" inside the existing self-monitoring product (`HSM Server Monitoring`): a middleware measures every `/api/v1` + `/mcp` request and attributes it to the API token that authenticated it; the values flow through the embedded collector exactly like every other self-monitoring sensor. The path key is `<owner-login>/<entityId>` — deliberately NOT the token name (non-unique, mutable) and never the TokenId (the authentication lookup key management responses never disclose); ADR-0006 (`docs/decisions/0006-token-usage-sensors-keyed-by-owner-and-entityid.md`) records the trade-off, and the Profile token card displays the EntityId so the tree↔token correlation is a glance.

## Invariants

- **Attribution**: a request counts for the token whose HsmApiToken identity authenticated it — owner login + EntityId resolved per request from the authoritative stores (`IApiTokenManager.GetToken`, `IUserManager` indexer; index lookups + a projection copy per request; the token principal stays minimal). Every status code counts (403/404/409 are work too); a revocation race (principal authenticated, record gone) attributes nothing and is not an auth failure.
- **The tree never carries the TokenId or the token name.** The login is sanitized into a single path segment (separators collapse to `_`).
- **Authentication failures are aggregate-only**: a 401 on a measured path with no token identity ticks `API tokens/Authentication failures` — there is no token to attribute a bad credential to. The reserved cookie-only family (`/api/v1/api-tokens`) is excluded from measurement entirely: its 401s (expired browser sessions — the cookie events answer 401, never a redirect, under `/api/v1`) are cookie failures, not token-credential ones (#1403 review r2). The aggregate sensor is eager and carries no retention options — it is permanent by design, unlike the per-token sensors.
- **Measurement never breaks traffic**: the observation runs in `finally`, wrapped in its own try/catch — a dead collector costs the metrics, not the request. The measured duration is full server-side handling from after authentication to the response (authorization included — it is all the caller waits for).
- **Pipeline position is load-bearing**: between `UseAuthentication` and `UseAuthorization` — a rejected request never reaches middleware registered after authorization, and the 401s are the failure signal. The token identity is resolved AT OBSERVATION TIME (after the handler): HsmApiToken is not the default scheme, so `UseAuthentication` runs only the cookie default and the token principal materializes INSIDE `UseAuthorization` (policy-scheme authentication replaces `context.User`) — capturing it before the handler would always see null (#1402 review). Pinned in `ManagementPipelineOrderTests` and `TokenIdentity_MaterializingDuringNext_AttributesToTheToken`.
- **Lazy subtrees with swept retention**: a token's node is created on the token's first use, and each channel's sensor pair on THAT channel's first use (an unused token — or an unused channel — adds no sensors). Retention is TWO mechanisms: the periodic statistics sweep (`DataCollectorWrapper.UpdateStatictics`) evicts and DISPOSES a node whose token record is gone (revocation, rotation, owner deletion) — necessary because rate sensors are monitoring sensors that post `0` every minute forever and never idle on their own — and the sensors' `SelfDestroy` (30 idle days) + `KeepHistory` (7 days) then retire the disposed sensors and bound their history. Requests cannot drive the eviction: a revoked credential fails authentication before any middleware sees its id. Restarts lose nothing (stable paths, re-registration on next use).
- **No aggregates beyond the auth-failure counter** (user decision): no `_Total` nodes; per-endpoint breakdowns are a non-goal (find the endpoint by traceId once the token is identified).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Agent/script calls `/api/v1` with a token → rate + duration points appear under its `REST/` node | API client |
| 2 | MCP client drives `/mcp` → the same under `MCP/` | MCP client |
| 3 | Bad/missing credential on a measured path → the aggregate failure counter ticks | anyone |
| 4 | Operator correlates a slow subtree with a token via the Profile EntityId display | Operator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `HSM Server Monitoring/API tokens/By owner/<owner-login>/<entityId>/REST/{Request rate, Request duration}` | `ApiTokenUsageNode` | Rate sensor (req/sec) + Double BAR (ms): min/max/mean/count per bar window. The dedicated `By owner` segment guarantees no login can collide with the aggregate sensor's name; login sanitization is display-only (two logins may share a grouping segment — the EntityIds keep the leaves unique) |
| `…/MCP/{Request rate, Request duration}` | `ApiTokenUsageNode` | same shapes |
| `API tokens/Authentication failures` | `ApiTokenUsageSensors` | Rate, aggregate, eager |
| Profile token card | `Views/Profile/Index.cshtml` | EntityId displayed under the name (mono, secondary) |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Middleware/ApiTokenUsageMiddleware.cs` | The measurement: path match, token-identity detection, stopwatch, attribution/failure observation |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/TokenUsage/ApiTokenUsageNode.cs` | The per-token four sensors |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/TokenUsage/ApiTokenUsageSensors.cs` | The lazy registry + the failure counter (the `ClientStatisticsSensors` pattern) |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/TokenUsage/IApiTokenUsageMonitor.cs` | The surface the middleware depends on (mockable in tests) |
| `src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs` | Middleware registration + the interface→wrapper forwarding |

## Data Flow

```
request (/api/v1 | /mcp)
  ──► UseAuthentication (token principal exists)
  ──► ApiTokenUsageMiddleware: stopwatch ON ──► next(...) ──► observe:
        token identity + GetToken(tokenId) + users[owner]
          ├─ resolved ──► ApiTokenUsageSensors.Add{Rest,Mcp}Request(login, entityId, ms)
          └─ none + 401 ──► AddAuthenticationFailure()
  ──► DataCollectorWrapper's embedded collector ──► the sensor tree
```

## Storage / Persistence

None of its own — sensors and history are the standard collector pipeline inside the self-monitoring product.

## UI / Operator Visibility

The subtree itself — readable by anyone with access rights on the self-monitoring product (typically admins; no per-user filtering, same as the rest of that product — ADR-0006 acknowledges the login/entity-id disclosure to that audience); the Profile token card shows the EntityId as the join key.

## Dependencies

- Depends on: api-tokens feature (`IApiTokenManager`, the HsmApiToken scheme), `IUserManager`, the embedded `DataCollector` (`WebRequestNode` precedent for the rate sensors, `CreateDoubleBarSensor` for the durations — the collector aggregates min/max/mean/count per bar window, so a hot token stores one point per window, and slow requests surface as the bar's max; exact per-request tails are the deliberate trade-off, traceId in the logs covers the single-request drill-down).
- Used by: operators watching management-API performance.

## Tests

`tests.md` next to this file.

## Notes

- `EnableForGrafana = false` on the new sensors: the Grafana integration is deprecated; the flag mirrors what a fresh surface would set today.

## Known Issues / Limitations

- Duration history is bar-aggregated (user decision, #1402 follow-up): the min/max/mean/count shape shows slow requests as the bar's max, but exact per-request tails (raw-sample percentile slicing) are gone — drill into a single slow request by traceId in the logs.
- The registry keeps one dormant record per token seen since startup (revoked/renamed-away); it resets on restart.
