# Tests: Management REST API (resource controllers)

> Owner: server | Last reviewed: 2026-09-03 | Canonical: yes

Coverage for the `/api/v1` resource controllers. Alert templates (`AlertTemplatesApiControllerTests`) is the reference matrix; #1352 should mirror it.

## Conventions (area admission)

- Reflection pin on the controller class: `[ManagementApi]`, `[Authorize(Policy = ManagementPolicy)]`, no `[AllowAnonymous]`, route `api/v1/alertTemplates`, derives `ControllerBase` (not the cookie `BaseController`) — exactly what `ManagementApiGuardMiddleware` admits; a refactor into the cookie world fails the pin.

## Alert templates (`AlertTemplatesApiControllerTests`, controller level)

Harness: Moq, controller constructed directly with a token principal (owner + token id claims, the shape `HsmApiTokenHandler` produces); the `ITreeValuesCache` mock is backed by a `ConcurrentDictionary` reproducing the cache's **upsert-by-id** semantics, so round-trips run against real storage behavior.

- **List**: `IsVisible` filtering **under the read operation** (evaluator asked for `alerts:read`, not any-operation reach — a token whose grants reach the folder without `alerts:read` sees an empty page; out-of-reach folder's template not listed), decision memoized per distinct folder (6 templates over 2 folders → exactly 2 evaluator calls), ordering name-then-id, pagination math (`page=2&pageSize=2` over 5 → items 3-4, `totalPages=3`), clamps (`page=0` → 1, `pageSize=9999` → 200, huge page number → clamped to `totalPages`, never a wrapped-int first-page echo).
- **Authorization mapping**: evaluator decision → status Theory (Allowed/Forbidden/NotFound → 200/403/404); authorize-before-validate pinned (invalid payload in an unreachable folder → 404, cache untouched); absent id → 404 with the evaluator never called.
- **Create**: client id ignored (server id stored, 201 + Location; nothing lands under the client id — the upsert-overwrite regression pin); AnyType accepted (policies stored Boolean-shaped, so the AnyType payload carries none); duplicate name → 400; missing/whitespace/null name Theory → 400; no non-whitespace paths → 400; missing/empty `folderId` → 400 (evaluator never called — an all-zero Guid references no folder, so the 400 leaks nothing); null list elements Theory (`policies` / `ttlPolicies` / `ttls` with a `[null]` entry) → 400, never a 500 (the structural pass runs outside the reconstruction try); `"destination": null` / `"schedule": null` treated as omitted (201, defaults stored — not the misleading "condition not supported" 400); size bounds (101 paths / 101 policies / over-length name) → 400; unknown SensorType (42, 11) → 400; path/type mismatch (Integer template matching a Double sensor) → 400 with one `GetSensors` call per path, AnyType skips the check entirely; chat rules — unknown chat id → 400, chat bound to another folder → 400, global chat and folder-bound chat accepted; display name canonicalized to the manager's current name on write; `Guid.Empty` policy ids regenerated; unsupported condition property for the sensor type (Integer + `Min`) → 400, never 500 (domain throws during reconstruction); mismatched TTL list lengths → 400; schedule `timeTicks` out of range → 400; cache `(false, "No products found…")` → 409 ProblemDetails; client disconnect during the write (`OperationCanceledException` from the cache) → 499, never a 500 out of the action.
- **Update**: round-trip fidelity canary — PUT echoed DTO equals the subsequent GET DTO field-by-field; folder move requires write on BOTH folders (new-folder Forbidden → 403 with the stored folder unchanged; both allowed → move happens); missing `folderId` in the body → 400, but only AFTER authorization (an out-of-reach caller gets the 403/404 first — a body-shape error must not leak existence); body id ≠ route id → 400; keeping own name passes uniqueness.
- **Delete**: 204 with the template gone (follow-up GET 404); cache failure → 409 with the detail.
- **Lifecycle** (issue acceptance criterion): create (201) → get (200, equal) → update rename + extra path (200) → get reflects it → delete (204) → get 404.

## Negative coverage checklist

- [x] Unknown/invisible/out-of-reach targets 404, indistinguishable (anti-enumeration)
- [x] In-reach but ungranted operation → 403 (ProblemDetails, never a cookie redirect)
- [x] Client-chosen id on POST cannot overwrite an existing template (upsert hole closed)
- [x] Folder move gated on both source and destination folders
- [x] Every enum byte validated before the domain casts; unsupported domain input → 400, never 500
- [x] Malformed TTL parallel lists, out-of-range ticks, non-Guid chat keys rejected structurally
- [x] Null list elements (`[null]` in policies/ttlPolicies/ttls) and null destination/schedule rejected or defaulted — never a null-deref 500
- [x] List discloses nothing beyond what the operation allows (list predicate == item-endpoint operation, not mere reach)
- [x] Authorization precedes validation (no information leak through error ordering)
