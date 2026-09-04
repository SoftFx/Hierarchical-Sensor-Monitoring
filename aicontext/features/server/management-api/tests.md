# Tests: Management REST API (resource controllers)

> Owner: server | Last reviewed: 2026-09-04 | Canonical: yes

Coverage for the `/api/v1` resource controllers: alert templates (`AlertTemplatesApiControllerTests`), alert schedules (`AlertSchedulesApiControllerTests` + the caller-wide gate matrix in `ApiTokenAuthorizationServiceTests`), the uniform JSON error contract (`ManagementApiErrorContractTests`, `ApiJsonErrorContractTests`, the challenge test in `HsmApiTokenHandlerTests`), and the OpenAPI publication (`ManagementApiSwaggerTests`).

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

## Uniform JSON error contract (#1353)

**Controller side** (`ManagementApiErrorContractTests`, controllers constructed directly):

- Every error status carries the `ManagementApiErrorDto` body with the right machine code: 403 `forbidden` (message names the operation), 404 `not_found` (generic message, `details: null`), 400 `validation_failed` (`details` is the field→messages map — `folderId`, `name`, `paths`), 409 `conflict` (message carries the cache error).
- **404 indistinguishability**: unknown id and the evaluator's invisible-folder decision produce the SAME code AND message (anti-enumeration, now observable at the body level).

**Pipeline side** (`ApiJsonErrorContractTests` — where no action ever runs):

- The shared writer's wire shape: camelCase `{error, message, details}`, `application/json`, and `details` an explicit JSON `null` when absent (the three fields are always present).
- Area-guard rejection (unmatched `/api/v1` route) → 404 with the SAME generic body as the controllers' unknown id.
- Legacy bearer guard (hsm_pat_ outside the area) → 401 `unauthorized` JSON.
- Exception middleware: an exception on an `/api` path → 500 `internal_error` with `details.traceId == context.TraceIdentifier` and no exception text on the wire; a non-API path rethrows (Razor error page keeps serving the UI); a started response rethrows.
- Binding-failure factory: a `[ManagementApi]` action's invalid ModelState → the uniform 400 with MVC's own field keys (`name`, `$.policies[1]`); a non-management `[ApiController]` (SensorsController) keeps the framework `ValidationProblemDetails` shape.

**Challenge** (`HsmApiTokenHandlerTests.Challenge_BodyIsTheUniformJsonErrorContract`): the 401 keeps `WWW-Authenticate: Bearer`, no redirect, and adds the uniform JSON body.

## OpenAPI publication (#1353)

`ManagementApiSwaggerTests`:

- **Filter scoping**: `DataRequestHeaderSwaggerFilter` adds the Key/ClientName headers to sensor-data actions (a `BaseRequest`-derived body parameter) and NONE to management actions; `ManagementApiSecuritySwaggerFilter` attaches the `HsmApiToken` bearer requirement to management actions only.
- **Response-annotations conventions pin**: every public action of every `[ManagementApi]` controller must be in the explicit per-action map (400/401 everywhere; 403/404 where authorization can deny; 409 on cache-conflicting writes) and declare at least one 2xx — adding a management endpoint without documenting its outcomes fails the suite; dead map entries fail too.

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
- [x] Every error path — controller 400/403/404/409, binding-failure 400, challenge 401, guard 404, legacy-guard 401, /api exception 500 — answers the uniform JSON body; never HTML, never an empty body
- [x] Unknown id, invisible folder and unmatched route render the SAME 404 body (anti-enumeration at routing level too)
- [x] 500 bodies carry `details.traceId` and never exception text; non-/api paths still get the Razor error page
- [x] Swagger: management actions carry the bearer security requirement and no Key header; sensor-data actions the reverse; every management action documents its error statuses
