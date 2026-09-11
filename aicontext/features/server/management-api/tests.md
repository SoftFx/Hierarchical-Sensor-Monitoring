# Tests: Management REST API (resource controllers)

> Owner: server | Last reviewed: 2026-09-11 | Canonical: yes

Coverage for the `/api/v1` resource controllers: alert templates (`AlertTemplatesApiControllerTests`), alert schedules (`AlertSchedulesApiControllerTests` + the caller-wide gate matrix in `ApiTokenAuthorizationServiceTests`), the sensor-tree read surface (`ProductsApiControllerTests`, `NodesApiControllerTests`, `SensorsApiControllerTests`, `SensorTreeDtoMapperTests`), the uniform JSON error contract (`ManagementApiErrorContractTests`, `ApiJsonErrorContractTests`, the challenge test in `HsmApiTokenHandlerTests`), and the OpenAPI publication (`ManagementApiSwaggerTests`).

## Conventions (area admission)

- Reflection pin on the controller class: `[ManagementApi]`, `[Authorize(Policy = ManagementPolicy)]`, no `[AllowAnonymous]`, route `api/v1/alertTemplates`, derives `ControllerBase` (not the cookie `BaseController`) — exactly what `ManagementApiGuardMiddleware` admits; a refactor into the cookie world fails the pin.

## Alert templates (`AlertTemplatesApiControllerTests`, controller level)

Harness: Moq, controller constructed directly with a token principal (owner + token id claims, the shape `HsmApiTokenHandler` produces); the `ITreeValuesCache` mock is backed by a `ConcurrentDictionary` reproducing the cache's **upsert-by-id** semantics, so round-trips run against real storage behavior.

- **List**: `IsVisible` filtering **under the read operation** (evaluator asked for `alerts:read`, not any-operation reach — a token whose grants reach the folder without `alerts:read` sees an empty page; out-of-reach folder's template not listed), decision memoized per distinct folder (6 templates over 2 folders → exactly 2 evaluator calls), ordering name-then-id, pagination math (`page=2&pageSize=2` over 5 → items 3-4, `totalPages=3`), clamps (`page=0` → 1, `pageSize=9999` → 200, huge page number → clamped to `totalPages`, never a wrapped-int first-page echo).
- **Authorization mapping**: evaluator decision → status Theory (Allowed/Forbidden/NotFound → 200/403/404); authorize-before-validate pinned (invalid payload in an unreachable folder → 404, cache untouched); absent id → 404 with the evaluator never called.
- **Create**: client id ignored (server id stored, 201 + Location; nothing lands under the client id — the upsert-overwrite regression pin); AnyType accepted (policies stored Boolean-shaped, so the AnyType payload carries none); duplicate name → 400; missing/whitespace/null name Theory → 400; no non-whitespace paths → 400; missing/empty `folderId` → 400 (evaluator never called — an all-zero Guid references no folder, so the 400 leaks nothing); null list elements Theory (`policies` / `ttlPolicies` / `ttls` with a `[null]` entry) → 400, never a 500 (the structural pass runs outside the reconstruction try); undefined `ttls[].interval` (sparse long enum, e.g. 60) → 400 — it would throw `NotImplementedException` in the timeout-scan loop; ticks-authoritative interval with `long.MaxValue`/negative ticks → 400 (`AddTicks` overflow in the same loop); duplicate non-empty policy ids across policies+ttlPolicies → 400 (they collapse into one at apply time); unknown `scheduleId` → 400, accepted once the schedule exists; `"destination": null` / `"schedule": null` treated as omitted (201, defaults stored — not the misleading "condition not supported" 400); size bounds (101 paths / 101 policies / over-length name) → 400; unknown SensorType (42, 11) → 400; path/type mismatch (Integer template matching a Double sensor) → 400 with one `GetSensors` call per path, AnyType skips the check entirely; chat rules — unknown chat id → 400, chat bound to another folder → 400, global chat and folder-bound chat accepted; display name canonicalized to the manager's current name on write; `Guid.Empty` policy ids regenerated; unsupported condition property for the sensor type (Integer + `Min`) → 400, never 500 (domain throws during reconstruction); mismatched TTL list lengths → 400; schedule `timeTicks` out of range → 400; cache `(false, "No products found…")` → 409 ProblemDetails; the cache write receives a non-cancellable token (RequestAborted is never forwarded — pinned in `Create_WriteIsNotClientCancellable_TheReconcileAlwaysCompletes`).
- **Update**: round-trip fidelity canary — PUT echoed DTO equals the subsequent GET DTO field-by-field; folder move requires write on BOTH folders (new-folder Forbidden → 403 with the stored folder unchanged; both allowed → move happens); missing `folderId` in the body → 400, but only AFTER authorization (an out-of-reach caller gets the 403/404 first — a body-shape error must not leak existence); body id ≠ route id → 400; keeping own name passes uniqueness.
- **Delete**: 204 with the template gone (follow-up GET 404); cache failure → 409 with the detail.
- **Lifecycle** (issue acceptance criterion): create (201) → get (200, equal) → update rename + extra path (200) → get reflects it → delete (204) → get 404.

## Alert schedules

**Gate decision matrix** (`ApiTokenAuthorizationServiceTests`, evaluator level — `HasOperationAtAnyVisibleBoundary`):

- A grant at a visible boundary → true, and allowed decisions record NO event; a grant at an INVISIBLE boundary (owner lost it) → false — the intersection decides; a Global grant → true only for an admin owner; no matching grant anywhere → false.
- `HasOperationAtGlobalScope` (the schedules sensor-filter short-circuit): admin owner + matching Global grant → true; non-admin owner, a grant for ANOTHER operation, or a scoped grant → false.
- An unresolvable token (absent from the index) → false, fail closed.
- The denial event is pinned to `AuthorizationDenied` — the 403 scope-denial kind, exactly ONE record; the gate must never feed the `AuthorizationNotFound` enumeration-probe signal.
- Non-matching operation grants are never probed (a `products:read` grant's boundary is never even resolved — `TryGetProduct` never called); a malformed boundary id (unreachable through canonicalization) is skipped fail-closed, not thrown.

**Controller** (`AlertSchedulesApiControllerTests`, controller level — the gate is mocked):

- Conventions reflection pin (same attribute set, route `api/v1/alertSchedules`, `ControllerBase`).
- Denied gate → 403 with the provider and the sensor cache never queried (list), and 403 for ANY id on get-by-id — no existence leak for an unentitled caller.
- **List**: pagination math and clamps (same envelope and constants as templates); ordering name-then-id; the page's sensor references resolved in ONE bulk cache call (`GetSensorsByAlertSchedules`, never the per-id lookup on the list path); sensor paths filtered per product visibility — same leak surface as get-by-id, pinned on the list path too; the visibility decision memoized per DISTINCT product (3 sensors over 2 products → exactly 2 `IsVisible` calls); the Global-grant short-circuit (admin + `alerts:read@Global` → sensors of ALL products, per-product predicate never consulted).
- **Get by id**: DTO maps the durable fields (id/name/timezone/schedule YAML); sensor references carry only the sensors whose PRODUCT boundary is visible to the caller (hidden product's sensor dropped, paths of the visible one kept); absent id → 404 for an entitled caller.

## Sensor-tree read surface (#1386)

Harness: Moq over `ITreeValuesCache` (backed by real `ProductModel`/`BaseSensorModel` graphs built through the entities factory) and `IApiTokenAuthorizationService`; controllers constructed directly with a token principal.

**Products** (`ProductsApiControllerTests`):

- Conventions reflection pin (route `api/v1/products`).
- Root-only listing: nested folders excluded, invisible roots excluded by `IsVisible`; an owner who sees nothing gets an EMPTY list (200), never a 403.
- Ordering name-then-id; pagination math (`page=2&pageSize=2` over 5 → items 3-4); page-beyond-end → the (possibly partial) last page; field mapping incl. the UTC-pinned `creationDate`.

**Nodes** (`NodesApiControllerTests`):

- Conventions reflection pin (route `api/v1/nodes`).
- Unknown id → plain 404 with the evaluator NEVER called; invisible id → the SAME uniform body as unknown (anti-enumeration, asserted at body level).
- Root product: `type=product`, empty `parent`, children split folders/sensors ordered by name, sensor type names; nested folder: `type=folder`, parent ref, path `root/folder`; a node's `path` is its FULL path (a root's path is its name, unlike the model's empty `Path`).

**Sensors** (`SensorsApiControllerTests`):

- Conventions reflection pin (route `api/v1/sensors`).
- **List/visibility**: no-filter listing over visible subtrees only; parentless sensors dropped (fail closed); per-ROOT-product sight memoized per distinct product (6 sensors over 2 roots → exactly 2 `IsVisible` calls).
- **Subtree filter**: `product={root}` searches recursively (nested folder's sensors included); `product={folderId}` addresses the folder's own subtree; unknown vs invisible `product` → the SAME 404, with the unknown id never reaching the evaluator.
- **Search**: contains matches name, description and path independently, case-insensitive; regex alternation works; invalid regex → 400 with `details.search`; unknown `searchMode` → 400; over-length search → 400; a catastrophic pattern (`(a+)+$` against 40 a's) → bounded 400, never a hang or 500 (the per-match regex timeout plus the whole-scan evaluation budget map to the same 400).
- **Type filter**: valid names case-insensitive; unknown name → 400 with the full value table in `details.type`.
- **Pagination**: ordering by full path then id; the same clamps as the area (page≥1, `pageSize=0` → default, page-beyond-end → last page, possibly partial).
- **Item**: unknown → 404 (evaluator never called); invisible → the same 404 as unknown; visible → metadata mapping (path/name/description/type, product+parent refs, null lastValue/lastUpdate/enumOptions for a valueless sensor).
- **History**: unknown/invisible sensor → 404; `from` > `to` → 400 with `details.from`; LOCAL-kind timestamps CONVERT to their instant (not relabeled — the window does not shift on a non-UTC server); the NEWEST maxPoints returned with `truncated=true` when excess was dropped (10 values, `maxPoints=5` → values 5-9; `maxPoints=50` → all, `truncated=false`); defaults (24 h window exactly, `maxPoints` default, UTC-kind echo); `maxPoints` clamped to the 10 000 cap; the cache is asked for the UNBOUNDED window (`int.MaxValue`) with the `IncludeTtl` flag (OffTime markers included) — the newest-N selection is endpoint-side.

**Wire shapes** (`SensorTreeDtoMapperTests`):

- The typed value envelope for every sensor type: scalars native (bool/int/double/string, TimeSpan "7.02:03:04", Version "1.2.3.4", Rate double); Enum `{value, label}` with the label resolved from registered options and NULL for unregistered; Bar `{min,max,mean,count}` for IntegerBar and DoubleBar; File metadata only (`{name, extension, size}` — never a `byte[]`, asserted by type).
- Envelope carries time/status/comment with the UTC kind pinned.
- Unit resolution: `OriginalUnit` display name ("%"), the Rate default denominator ("# per sec"), null otherwise.
- Enum options mapped ordered by value with label+description; null `enumOptions` for non-Enum sensors.

## Uniform JSON error contract (#1353)

**Controller side** (`ManagementApiErrorContractTests`, controllers constructed directly):

- Every error status carries the `ManagementApiErrorDto` body with the right machine code: 403 `forbidden` (message names the operation), 404 `not_found` (generic message, `details: null`), 400 `validation_failed` (`details` is the field→messages map — `folderId`, `name`, `paths`), 409 `conflict` (message carries the cache error).
- **404 indistinguishability**: unknown id and the evaluator's invisible-folder decision produce the SAME code AND message (anti-enumeration, now observable at the body level).

**Pipeline side** (`ApiJsonErrorContractTests` — where no action ever runs):

- The shared writer's wire shape: camelCase `{error, message, details}`, `application/json`, and `details` an explicit JSON `null` when absent (the three fields are always present).
- Area-guard rejection (unmatched `/api/v1` route) → 404 with the SAME generic body as the controllers' unknown id.
- Legacy bearer guard (hsm_pat_ outside the area) → 401 `unauthorized` JSON.
- Exception middleware: an exception on an `/api` path → 500 `internal_error` with `details.traceId == context.TraceIdentifier` and no exception text on the wire; a non-API path rethrows (Razor error page keeps serving the UI); a started response rethrows.
- Binding-failure factory: a `[ManagementApi]` action's invalid ModelState → the uniform 400 with MVC's own field keys (`name`, `$.policies[1]`); an empty binder message gets the framework's fallback wording; the WIRED factory (`WrapBindingFailureFactory`) delegates non-management actions to the captured framework default **verbatim** (spy assertion — the sensor-data/Grafana `problem+json` shape is delegated to, never reimplemented) and never calls it for management actions.
- Swagger port gate: swagger paths off the SitePort (both `/swagger/*` and `/api/swagger/*`) → the uniform 404 body; on the SitePort and for non-swagger paths on any port → pass-through.

**Challenge** (`HsmApiTokenHandlerTests.Challenge_BodyIsTheUniformJsonErrorContract`): the 401 keeps `WWW-Authenticate: Bearer`, no redirect, and adds the uniform JSON body. **Forbid** (`Forbid_BodyIsTheUniformJsonErrorContract`, review round 2): the scheme's 403 twin carries the uniform body too — defense-in-depth for the `HsmApiTokenOnlyRequirement` path. The exception middleware rethrows **cancellations** untouched (`ExceptionMiddleware_Cancellation_RethrowsUntouched`) — an aborted request never becomes a failed 500 write.

## OpenAPI publication (#1353)

`ManagementApiSwaggerTests`:

- **Filter scoping**: `DataRequestHeaderSwaggerFilter` adds the Key/ClientName headers to NON-management actions (a NEGATIVE rule — skip `[ManagementApi]` controllers; pinned on a sensor-data action AND on a Grafana JSON-datasource action, whose request types do not derive from `BaseRequest` but whose only credential is the Key header) and NONE to management actions; `ManagementApiSecuritySwaggerFilter` attaches the `HsmApiToken` bearer requirement to management actions only. Both membership checks use `inherit: true`, matching the runtime guard's endpoint-metadata view.
- **Response-annotations conventions pin**: every HTTP action (`HttpMethodAttribute`-bearing, INHERITED ones included) of every `[ManagementApi]` controller must be in the explicit per-action map (400/401/500 everywhere; 403/404 where authorization can deny; 409 on cache-conflicting writes — 500 because the `/api` exception handler can answer any of them) and declare at least one 2xx — adding a management endpoint without documenting its outcomes fails the suite; dead map entries fail too.
- **Enum-table drift pin** (`DocumentedEnumTables_MatchTheDomainEnums`, review round 2): every `N=Name` pair in the DTO XML remarks is parsed from the source and pinned — full-table equality, values AND names — against the domain enum the controllers validate with (`HSMCommon` SensorType incl. the deliberate `100=AnyType` extra, `HSMCommon` SensorStatus (the wrong-enum inversion this test was written after), PolicyOperation/Property/Combination/TargetType, `PolicySchedule` AlertRepeatMode, sparse TimeInterval).

## Negative coverage checklist

- [x] Unknown/invisible/out-of-reach targets 404, indistinguishable (anti-enumeration)
- [x] In-reach but ungranted operation → 403 (uniform JSON error, never a cookie redirect)
- [x] Client-chosen id on POST cannot overwrite an existing template (upsert hole closed)
- [x] Folder move gated on both source and destination folders
- [x] Every enum byte validated before the domain casts; unsupported domain input → 400, never 500
- [x] Malformed TTL parallel lists, out-of-range ticks, non-Guid chat keys rejected structurally
- [x] Null list elements (`[null]` in policies/ttlPolicies/ttls) and null destination/schedule rejected or defaulted — never a null-deref 500
- [x] No value that would throw inside the monitoring/evaluation loops is persistable (undefined TTL interval, out-of-range ticks)
- [x] Duplicate policy ids rejected (apply-time collapse); scheduleId resolves to a real schedule
- [x] The cache write is never tied to RequestAborted (partial reconcile/partial disarm impossible)
- [x] List discloses nothing beyond what the operation allows (list predicate == item-endpoint operation, not mere reach)
- [x] Authorization precedes validation (no information leak through error ordering)
- [x] Global resource: unentitled caller learns nothing (403 for every id, provider never queried); sensor paths filtered per-caller visibility
- [x] Every error path — controller 400/403/404/409, binding-failure 400, challenge 401, guard 404, legacy-guard 401, /api exception 500 — answers the uniform JSON body; never HTML. Scope: `/api/v1` responses plus 500s anywhere under `/api`; unrouted non-v1 `/api` paths answer the framework's bare 404 (unchanged, deliberate)
- [x] Unknown id, invisible folder and unmatched route render the SAME 404 body (anti-enumeration at routing level too)
- [x] 500 bodies carry `details.traceId` and never exception text; non-/api paths still get the Razor error page
- [x] Swagger: management actions carry the bearer security requirement and no Key header; sensor-data actions the reverse; every management action documents its error statuses
- [x] Agent-supplied regex is bounded on both axes — invalid grammar and catastrophic backtracking are 400s, never a hang or a 500 (#1386)
- [x] File sensor content is never serialized — lastValue and history points carry metadata only (asserted by wire-shape type)
- [x] Sensor-tree visibility: parentless sensors dropped; the `product` filter's unknown/invisible 404s are indistinguishable and the unknown path skips the evaluator
- [x] History truncation is honest: `truncated=true` exactly when older values were dropped by the newest-N selection
