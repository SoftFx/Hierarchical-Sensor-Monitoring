# Feature tests: MCP server (AI-agent tools)

> Owner: server | Last reviewed: 2026-09-14 | Canonical: yes

## Unit — `src/tests/HSMServer.Core.Tests/Mcp/`

`HsmMcpWireTests` — the wire-level smoke test over a minimal in-memory host (#1392 review, round 3): the REAL `MapMcp` + `RequireAuthorization(ManagementPolicy)`, `McpSitePortOnlyMiddleware`, the real `HsmApiToken` scheme (over mocked token/user managers) and `AddHsmMcpServer`'s registrations, driven by a real SDK `McpClient` (initialize → tools/list → tools/call). Closes the three load-bearing integration assumptions at once: the ambient principal flows (a `tools/call` returns data, not the `McpToolContext` backstop error), tool instances resolve in the REQUEST scope (the host runs in Development — ValidateScopes + ValidateOnBuild), and the guard reads the metadata `MapMcp` actually emits (a mismatch fail-closes to the uniform 404). Plus: no credential → the transport is rejected before any tool, and a valid credential on the SensorPort gets the uniform 404. No Kestrel/LevelDB/fixed listeners — the in-memory TestServer assembles only the pieces `AddHsmMcpServer` already groups.

`HsmMcpServerRegistrationTests` — the wiring (#1391 review): `AddHsmMcpServer` surfaces EXACTLY the nine spec tools (the SDK builds their schemas at registration — a bad tool signature throws), the server identity, the pinned **stateless** HTTP transport (the ambient-principal flow of every tool depends on it), and the camelCase rendering of the tool result records under the SDK's `McpJsonUtilities.DefaultOptions` (Web defaults) — the parity pin that keeps MCP and REST JSON keys identical.

`SensorTreeMcpToolsTests` and `AlertsMcpToolsTests` — the tool renderings:

- `NormalizeLimit_FallsBackAndCaps` — `limit` 0/negative → default 20; >200 → 200 (the theory also pins the in-range pass-through).
- `ListProducts_ReturnsFirstLimit_WithTotalFound` — first `limit` items travel; `totalFound` carries the full count.
- `ListProducts_InvisibleProduct_AbsentFromBothCounters` — owner sight holds on the tool path.
- `ListProducts_PageServesBeyondTheLimit` — products have no narrowing dimension, so `page` walks past the cap (#1392 review r3).
- `GetNode_ReturnsDirectChildren_AndPagesFolders` — >200 direct subfolders: page 1 caps at 200 with totals, `foldersPage: 2` serves the rest (folder ids stay reachable).
- `GetNode_UnknownId_IsToolError` — the shared area 404 constant.
- `GetSensorHistory_ExplicitMaxPoints_IsCappedAtTheMcpCeiling` — a naive explicit 10000 clamps to the MCP ceiling 2000 before the shared service's REST rules (context-window bound, #1392 review r3).
- `FindSensors_CompactShape_WithTotalFound` — the summary carries id/path/type/status; values are NOT embedded.
- `FindSensors_InvisibleSubtree_SilentlyAbsent` — per-root-product sight.
- `FindSensors_UnknownProduct_IsToolError` — the uniform not-found text.
- `FindSensors_InvalidSearchMode_FlattensFieldErrors` — field-keyed validation details flatten into the tool error text with keys preserved; the key names the FAILING parameter (`searchMode:`, not `search:`), so the agent corrects the right argument (#1392 review; the matcher keys the mode error under `searchMode` on both transports).
- `GetSensor_EmbedsLastValue` — the only tool embedding the current value.
- `GetSensor_UnknownId_IsToolError`.
- `GetSensorHistory_NewestPointsOldestFirst_WithTruncatedFlag` — the #1389/#1390 direction contract through the tool (page generator's count bound honored, surplus-oldest dropped, reversed, `truncated` set).
- `GetSensorHistory_OmittedMaxPoints_DefaultsToTheMcpDefault` — an omitted `maxPoints` asks for the MCP default 200 (context-window bound), not the REST twin's 1000.
- `GetSensorHistory_NonPositiveMaxPoints_IsTheMcpDefault_NotTheRestFallback` — an explicit zero/negative `maxPoints` (a common agent rendering of "no preference") also normalizes to 200 BEFORE the shared service's REST fallback could turn it into 1000 (#1392 review).
- `GetSensorHistory_FileSensorBusy_ReportsReadUnavailable` — busy file lock → `readUnavailable=true`, no points.

`AlertsMcpToolsTests` — the alert tools over the thin providers:

- `ListAlertTemplates_OrdersByName_ReturnsFirstLimitWithTotalFound`.
- `ListAlertTemplates_OutOfSightFolders_SilentlyAbsent`.
- `ListAlertTemplates_MemoizesVisibility_PerDistinctFolder` — the evaluator is resolved once per distinct folder (Times.Exactly(2)).
- `GetAlertTemplate_Visible_MapsDto`.
- `GetAlertTemplate_UnknownId_IsToolError`.
- `GetAlertTemplate_InvisibleFolder_SameToolErrorAsUnknown` — anti-enumeration carried into MCP.
- `ListAlertSchedules_DeniedGate_IsToolError_ProviderNeverQueried`.
- `ListAlertSchedules_ReturnsFirstLimitWithTotalFound`.
- `ListAlertSchedules_FiltersSensorPaths_ByProductVisibility`.
- `GetAlertSchedule_MapsDto_AndFiltersSensorsByVisibility`.
- `GetAlertSchedule_Absent_IsToolError`.

The tools take `IHttpContextAccessor` (ambient principal — behind `RequireAuthorization` it is always present); the tests fake it with `HttpContextAccessor { HttpContext = DefaultHttpContext { User = … } }`, the same principal shape the controller suites build.

`ApiTokenRouteGuardsTests` (`src/tests/HSMServer.Core.Tests/Authentication/ApiTokens/`) pins the two MCP guards: the bearer pass-through is case-insensitive (`/mcp` and `/MCP` — endpoint routing matches segments case-insensitively, so a mixed-case POST must not read as a misplaced token), the off-SitePort uniform 404 fires before authentication, and the fail-closed policy check rejects a matched `/mcp` endpoint without `ManagementPolicy` or with `[AllowAnonymous]` while admitting the policy-carrying endpoint.

`ApiJsonErrorContractTests` (`src/tests/HSMServer.Core.Tests/Middleware/`) additionally pins that an exception escaping the handler on `/mcp` answers the uniform JSON 500 with the trace id — never the Razor error page (#1392 review).

## Regression — REST suites are the shared net

The three sensor-tree controller suites (`SensorsApiControllerTests`, `NodesApiControllerTests`, `ProductsApiControllerTests`) construct the REAL `SensorTreeReadService` around the same mocks — they pin the service behavior that both transports now share (the #1391 extraction was behavior-neutral: all 47 controller tests + 10 swagger pins green unchanged).

## Not covered (deliberate)

- The MCP wire layer (protocol-version validation, JSON-RPC envelope details) is the SDK's tested surface; `HsmMcpWireTests` covers the integration of OUR pieces with it (auth policy, guards, ambient principal, scope) and stops there.
- `HsmMcpWireTests` assembles its own minimal host — it does not execute Program.cs itself, so the production pipeline's exact middleware ORDER around /mcp stays covered by `ApiTokenRouteGuardsTests` and the exception-contract tests, not by the wire test.
- Alert-tool `page` tests pin the Skip/Take slicing on the tool path; the underlying visibility semantics are the controller suites' (mirrored code — see feature.md Known Issues for the recorded `AlertReadService` follow-up).
