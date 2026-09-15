# Feature: MCP server (AI-agent tools)

> Owner: server | Last reviewed: 2026-09-14 | Canonical: yes
> Scope: the read-only Model Context Protocol surface at `/mcp` (#1391) — nine snake_case tools rendering the landed management-API read surface for MCP-native agents (Claude, Cursor, …). REST stays the canonical surface; MCP is a v1 read-only adapter over the same implementation.

---

## Overview

Issue #1391 exposes the sensor-tree + alert read surface (merged via #1387/#1390, epic #1347) through the Model Context Protocol, so an MCP-native agent discovers the same capabilities a REST client has without hand-writing HTTP. The server is hosted by HSMServer itself via the official `ModelContextProtocol` C# SDK (NuGet `ModelContextProtocol.AspNetCore` 2.2.0), Streamable HTTP transport, **stateless** sessions (the tools are pure reads; no session affinity needed). Nine read-only tools, snake_case, camelCase parameters; item tools return the REST DTOs verbatim (same JSON shape, camelCase on both transports), list tools carry a `limit` (default 20, cap 200) + `totalFound` envelope — **no pagination cursors** by design: an agent narrows instead of chasing pages. Every list tool also takes a `page` (clamp semantics as REST — a page past the end serves the LAST page, and the Skip arithmetic is ClampPage-bounded so a huge page number can never wrap int into "page 1 labeled as page N"): narrowing is preferred, but not guaranteed (get_node's sensors list caps at 200 unpaged; uniformly-named sensors cannot be partitioned by search), so `page` is the guaranteed reachability past the cap.

The sensor-tree tools are a thin rendering of `SensorTreeReadService` — the same implementation the REST controllers run on since #1391 (their suites are the regression net). Alert tools use the existing thin providers directly, mirroring the REST controllers' list/get logic. Expected failures surface as tool errors (`isError=true` with a text message — `McpException`), never as protocol-level crashes, so the calling agent can self-correct.

## Invariants

- **Same credential, same sight, no second credential kind**: the endpoint requires the SAME `HsmApiToken` bearer scheme and `HsmApiTokenDefaults.ManagementPolicy` as `/api/v1` (`RequireAuthorization` on `app.MapMcp("/mcp")`). Read-write tokens are admitted — the v1 tools are read-only regardless. No anonymous access. Visibility follows the token owner's sight through the exact REST decision paths (per-root-product sensor sight, per-folder template sight, caller-wide schedule gate).
- **Outside the area guard, inside the credential family**: `/mcp` is NOT under `ManagementApiGuardMiddleware` (no `[ManagementApi]` marker; the SDK owns the route and errors follow MCP's JSON-RPC semantics, not the uniform JSON contract). `LegacyBearerGuardMiddleware` exempts it (`IsTokenRoutePath`) — the hsm_pat_ credential is expected there, not misplaced. The one carry-over from the JSON world: whatever ESCAPES the SDK handler (a transport-level failure, a serialization error) is caught by `ApiExceptionJsonMiddleware` and answers a **JSON-RPC error envelope** (`-32603 Internal error` with `data.traceId`) — machine-parseable, never the Razor HTML page (#1392 review, round 3).
- **SitePort-only, probing-blind, fail-closed over the endpoint family**: `McpSitePortOnlyMiddleware` (before authentication, with the other guards) answers `/mcp` off the SitePort with the area's uniform 404 — an unauthenticated probe on the sensor port never gets a 401 that confirms the endpoint exists. It also carries the area guard's rule over `/mcp`'s endpoints: a matched endpoint under `/mcp` must require `ManagementPolicy` and never be anonymous, so a future route mapped there is unreachable without the credential, exactly like an unmarked route under `/api/v1`.
- **Always on, no config toggle**: like `/api/v1`, the endpoint ships enabled behind its auth; there is no feature flag to drift.
- **Read-only tool metadata**: every tool declares `ReadOnly=true, Idempotent=true, OpenWorld=false` — clients can surface the safety hints.
- **Compactness rule**: only `get_sensor` embeds the current value (`lastValue`); `find_sensors` returns compact summaries (id, path, type, status, unit). No tool serializes file content (the REST File-value metadata rule holds).
- **Anti-enumeration carried into MCP**: unknown and invisible ids answer the SAME tool-error text (`"The requested resource was not found."`); out-of-sight items are silently absent from lists; the schedule gate denies before the provider is queried.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | MCP client initializes a session (HTTP POST `initialize` at `/mcp`) | MCP client (bearer token) |
| 2 | `tools/list` → the nine tools with generated schemas | MCP client |
| 3 | The read-analysis scenario: `list_products` → `find_sensors` → `get_sensor_history` | AI agent |
| 4 | Alert review: `list_alert_templates` / `get_alert_template`, `list_alert_schedules` / `get_alert_schedule` | AI agent |

### The read-analysis scenario (#1391 acceptance)

"Analyze network problems in product X" with an MCP client and a read-only token:

1. `list_products` → the visible root products; find X by name.
2. `find_sensors(search="network", productId={X})` → compact matching summaries + `totalFound`.
3. `get_sensor(sensorId)` when the current value matters (the only tool embedding it).
4. `get_sensor_history(sensorId, from, to)` → the newest points oldest-first with `truncated`/`readUnavailable` (the #1389/#1390 contract unchanged).

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `POST /mcp` (Streamable HTTP, stateless) | `Program.cs` (`MapMcp`), SDK `StreamableHttpHandler` | JSON-RPC envelope per the MCP spec; the SDK validates protocol headers/versions; 401 through the HsmApiToken challenge before the handler on a bad credential |
| `list_products(limit?, page?)` | `SensorTreeMcpTools.ListProducts` | `{products: ProductDto[], totalFound}` — REST DTO verbatim, name-ordered; `page` walks past the limit (products have no narrowing dimension, #1392 review r3) |
| `get_node(nodeId, foldersPage?)` | `SensorTreeMcpTools.GetNode` | `NodeDto` verbatim (folders paginated × 200, sensors capped 200 — REST `get_node` shape; `foldersPage` keeps folder ids beyond page 1 reachable) |
| `find_sensors(search?, searchMode?, productId?, type?, limit?, page?)` | `SensorTreeMcpTools.FindSensors` | `{sensors: McpSensorSummary[], totalFound}` — compact summaries ordered by path; `searchMode` = `contains`\|`regex` with the REST bounds (100 ms per-match, 2 s scan budget — exhaustion is a tool error naming the remedy); `page` is the last resort past the cap — get_node's own sensors list caps at 200 unpaged and uniform names cannot be partitioned (#1392 r4) |
| `get_sensor(sensorId)` | `SensorTreeMcpTools.GetSensor` | `SensorDto` verbatim — `lastValue` embedded |
| `get_sensor_history(sensorId, from?, to?, maxPoints?)` | `SensorTreeMcpTools.GetSensorHistoryAsync` | `SensorHistoryDto` verbatim — newest-N oldest-first, `truncated`, `readUnavailable`; MCP default `maxPoints` 200 and MCP ceiling 2000 (the result feeds the model's context — both tighter than the REST twin's 1000/10000; a String sensor's per-point payload is unbounded, so the ceiling bounds the naive agent too, #1392 review r3); File cap 100 |
| `list_alert_templates(limit?, page?)` / `get_alert_template(templateId)` | `AlertsMcpTools` | `{templates: AlertTemplateDto[], totalFound}` / `AlertTemplateDto` — folder-sighted like REST; `page` walks past the limit (no narrowing dimension) |
| `list_alert_schedules(limit?, page?)` / `get_alert_schedule(scheduleId)` | `AlertsMcpTools` | `{schedules: AlertScheduleDto[], totalFound}` / `AlertScheduleDto` — caller-wide gate, sensor paths filtered to the owner's sight; `page` as above |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Mcp/HsmMcp.cs` | Endpoint path + list-limit constants (`/mcp`, 20/200) |
| `src/server/HSMServer/Mcp/HsmMcpServiceCollectionExtensions.cs` | `AddHsmMcpServer()` — server + tool registration in one testable place |
| `src/server/HSMServer/Mcp/SensorTreeMcpTools.cs` | The five sensor-tree tools — rendering of `SensorTreeReadService` |
| `src/server/HSMServer/Mcp/AlertsMcpTools.cs` | The four alert tools — direct over the thin providers, mirroring the REST controllers |
| `src/server/HSMServer/Mcp/McpToolContext.cs` | The shared ambient-principal accessor behind `RequireAuthorization` |
| `src/server/HSMServer/Mcp/McpToolResults.cs` | List envelopes + the compact `McpSensorSummary`; item DTOs are the REST ones |
| `src/server/HSMServer/Middleware/McpSitePortOnlyMiddleware.cs` | Uniform 404 for `/mcp` off the SitePort, before authentication; fail-closed policy check on matched `/mcp` endpoints |
| `src/server/HSMServer/Middleware/LegacyBearerGuardMiddleware.cs` | `IsTokenRoutePath`: `/api/v1` + `/mcp` — the bearer credential family |
| `src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs` | `AddMcpServer().WithHttpTransport().WithTools<…>()` + `MapMcp` wiring context |
| `src/server/HSMServer/Model/ManagementApi/SensorTree/SensorTreeReadService.cs` | The shared sensor-tree read implementation (REST + MCP; extracted #1391) |

## Data Flow

```
bearer token ──► McpSitePortOnlyMiddleware (SitePort check, uniform 404 otherwise)
             ──► UseAuthentication/UseAuthorization: RequireAuthorization(ManagementPolicy)
             │   → HsmApiToken challenge = plain 401 (no redirect)
             ──► SDK StreamableHttpHandler (JSON-RPC validate, stateless session)
                  ──► tools/call ──► tool class (DI, scoped)
                       ├── SensorTreeMcpTools → SensorTreeReadService → ITreeValuesCache
                       │     failures → McpException → isError result (text message)
                       └── AlertsMcpTools → IAlertScheduleProvider / ITreeValuesCache
                             (folder sight / caller-wide gate, as REST)
```

The tools resolve the caller through `IHttpContextAccessor` — the stateless transport executes a `tools/call` inline within its POST, so the ambient context flows; the principal is the one `RequireAuthorization` already admitted. Cancellations flow to the service (`CancellationToken` parameters, same abort semantics as REST `RequestAborted`).

## Storage / Persistence

None — read-only over the live cache and the schedule provider.

## UI / Operator Visibility

No UI. Operators provision the same API tokens as for REST (`/api/v1/api-tokens` family); an agent config entry needs the server URL + the credential.

## Dependencies

- Depends on: api-tokens feature (scheme, management policy, evaluator), `SensorTreeReadService` + the sensor-tree/alert DTOs of the management-api feature, `IAlertScheduleProvider`, NuGet `ModelContextProtocol.AspNetCore` 2.2.0 (net8.0).
- Used by: MCP-native AI agents; the REST surface remains canonical for non-MCP clients.

## Tests

`tests.md` next to this file.

## Notes

- Wire casing is camelCase on both transports: MVC JSON options vs the SDK's `McpJsonUtilities.DefaultOptions` (documented to enable `JsonSerializerDefaults.Web`); a registration test pins the camelCase rendering of the tool result records, so an SDK default change cannot silently diverge the surfaces.
- Server identity: `Implementation { Name="HSMServer", Version=ServerConfig.Version }`; stateless sessions set explicitly (`WithHttpTransport(options => options.Stateless = true)` — the 2.2.0 default since the 2026-07-28 protocol revision) and pinned by a registration test: the tools resolve the caller through the ambient HTTP context, which only flows in stateless mode, so an SDK default change must fail a test rather than silently degrade every `tools/call`.

## Known Issues / Limitations

- v1 is read-only (write tools are phase 2 of AI-agent access); no MCP resources/prompts primitives.
- The alert tools mirror the REST controllers' list/get logic directly (the #1391 spec scoped the shared-service extraction to the sensor tree): the per-folder template sight, the schedules gate and the sensor-path visibility filter exist in two places. Recorded follow-up: extract an `AlertReadService` mirroring `SensorTreeReadService` before the alert read surface grows, so a future change to those visibility rules cannot drift between REST and MCP unreviewed (#1392 review).
- No `Origin`-header validation (the MCP spec's DNS-rebinding defence for Streamable HTTP servers). Deliberate: the endpoint admits only the HsmApiToken bearer — a browser page can neither attach that credential nor satisfy `ManagementPolicy` with cookies — so the browser-based rebinding vector has no credential to ride. Revisit if a browser-reachable credential kind is ever admitted to `/mcp`.
- No E2E/Playwright coverage yet; the wire-level MCP handshake (protocol headers, JSON-RPC envelope) is the SDK's tested surface, ours starts at the tool classes.
