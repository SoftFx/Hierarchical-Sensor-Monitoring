# Feature tests: MCP server (AI-agent tools)

> Owner: server | Last reviewed: 2026-09-14 | Canonical: yes

## Unit — `src/tests/HSMServer.Core.Tests/Mcp/`

`SensorTreeMcpToolsTests` — the tool rendering of `SensorTreeReadService` (the service's own behavior is pinned by the REST controller suites — the shared regression net — so these tests pin only the tool contracts):

- `NormalizeLimit_FallsBackAndCaps` — `limit` 0/negative → default 20; >200 → 200 (the theory also pins the in-range pass-through).
- `ListProducts_ReturnsFirstLimit_WithTotalFound` — first `limit` items travel; `totalFound` carries the full count.
- `ListProducts_InvisibleProduct_AbsentFromBothCounters` — owner sight holds on the tool path.
- `FindSensors_CompactShape_WithTotalFound` — the summary carries id/path/type/status; values are NOT embedded.
- `FindSensors_InvisibleSubtree_SilentlyAbsent` — per-root-product sight.
- `FindSensors_UnknownProduct_IsToolError` — the uniform not-found text.
- `FindSensors_InvalidSearchMode_FlattensFieldErrors` — field-keyed validation details flatten into the tool error text with keys preserved.
- `GetSensor_EmbedsLastValue` — the only tool embedding the current value.
- `GetSensor_UnknownId_IsToolError`.
- `GetSensorHistory_NewestPointsOldestFirst_WithTruncatedFlag` — the #1389/#1390 direction contract through the tool (page generator's count bound honored, surplus-oldest dropped, reversed, `truncated` set).
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

## Regression — REST suites are the shared net

The three sensor-tree controller suites (`SensorsApiControllerTests`, `NodesApiControllerTests`, `ProductsApiControllerTests`) construct the REAL `SensorTreeReadService` around the same mocks — they pin the service behavior that both transports now share (the #1391 extraction was behavior-neutral: all 47 controller tests + 10 swagger pins green unchanged).

## Not covered (deliberate)

- The MCP wire layer (JSON-RPC envelope, protocol-version validation, stateless session handling) is the SDK's tested surface, not ours; a wire-level smoke test would re-test the transport. If drift is ever suspected, an E2E test against a booted server with a real `initialize`/`tools/list`/`tools/call` exchange is the follow-up.
- camelCase wire parity between MVC and the SDK serializer is covered by design (shared DTOs), not by an assertion; flagged as residual risk in feature.md Notes.
