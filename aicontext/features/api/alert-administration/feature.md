# Feature: Alert Administration API

> Owner: server | Last reviewed: 2026-09-30 | Canonical: yes
> Scope: the `/api/v1` REST surface for administering alerts (#1500, phase 2 of the sensor-tree API): data-policy and TTL-policy CRUD on sensors and products. Configuration only — data (values) stays the collector's job.

---

## Overview

`/api/v1` endpoints under `api/v1/sensors/{id}` and `api/v1/products/{id}` let an AI agent or an operator script list, create, replace and delete alert policies:

```
GET    /api/v1/sensors/{id}/policies            list the sensor's data policies
GET    /api/v1/sensors/{id}/policies/{policyId} one data policy
POST   /api/v1/sensors/{id}/policies            create (201 + Location, server id)
PATCH  /api/v1/sensors/{id}/policies/{policyId} replace the policy's content
DELETE /api/v1/sensors/{id}/policies/{policyId} remove it

GET/POST/PATCH/DELETE  /api/v1/sensors/{id}/ttl-policies[/{policyId}]   same, TTL (inactivity) policies

GET    /api/v1/products/{id}/policies            the product's data-policy AGGREGATE
                                                  (every data policy of every sensor in the subtree,
                                                   each item naming its owning sensor)
GET    /api/v1/products/{id}/policies/{policyId}  one of them, with its owner
POST   /api/v1/products/{id}/policies             always 422 — a product owns no data policies
PATCH  /api/v1/products/{id}/policies/{policyId}  replace it, applied on the OWNING sensor's list
DELETE /api/v1/products/{id}/policies/{policyId}  remove it, through the owning sensor's list

GET/POST/PATCH/DELETE  /api/v1/products/{id}/ttl-policies[/{policyId}]  the product's OWN TTL policies
```

All lists are `ApiPageDto<T>` pages (the area envelope), ordered by policy id (products' data aggregate by sensor path then policy id).

## Design: item CRUD over one atomic full-list update

The server never mutates a policy collection directly. Every write:

1. reads the target node's current policy list,
2. applies the ONE requested item change (create = append, patch = replace the item, delete = drop it),
3. sends a single atomic FULL-LIST `SensorUpdate`/`ProductUpdate` through `ITreeValuesCache.UpdateSensorAsync`/`UpdateProductAsync`.

This is exactly the machinery the web editor (`HomeController.UpdateSensorInfo`) drives — last-write-wins per request, no new core paths. Consequences:

- every surviving policy is re-asserted on each write (a partial list would DROP everything not re-asserted — full-list semantics);
- re-asserted TTL policies carry their interval EXPLICITLY (`CopyTtlUpdate`: a null `TTL` is the core's explicit reset-to-parent, not "keep" — the #1409/#1451 lesson) and PRESERVE the change-table ownership of the untouched sibling (`PolicyUpdate.PreserveChangeOwnership` — a re-assert is not an edit; without it one API call would re-stamp a template-applied sibling as the calling user and the next template apply would fail the node's CanChange pre-check for the whole node. Only the CHANGED item takes normal ownership stamping);
- POST/PATCH bodies are FULL-REPLACE at item granularity: the body specifies the policy's complete desired content; fields left out fall back to defaults; the id is server-generated on create and taken from the route on PATCH;
- API-to-API writes to the SAME node are serialized by a per-node write gate in `PolicyAdministrationService` (a reference-counted registry: a gate entry exists only while a writer holds or waits on it — authorization and existence resolve BEFORE any gate exists, so unknown ids allocate nothing, and an idle gate leaves with its last user). The gate spans resolve → merge → send → the post-write verification read, with the merge-snapshot read taken UNDER the gate. Keyed by the node whose list is replaced — the SENSOR id for sensor data policies, sensor TTL policies and product-routed data policies (the OWNING sensor's key, not the product's), the PRODUCT id for product TTL. Two concurrent API writes to one node therefore both land; last-write-wins remains only versus the web editor, which does not go through the service.

Writes are not client-cancellable (no `RequestAborted` into the cache — the area rule).

## Products own no data policies

Per-node (Folder/Product) alert creation was removed in #1142; `ProductEntity.Policies` is dead at runtime. The product data-policy surface is therefore the subtree AGGREGATE: reads walk the product's sensors (nested products included), and PATCH/DELETE resolve the OWNING sensor and route the write through that sensor's own full-list update (authorized at the sensor's boundary). Creation is not expressible — `POST /api/v1/products/{id}/policies` always answers 422 with the sensor endpoint to use instead. The product TTL policies ARE product-owned (`ProductUpdate.TTLPolicies`); on a product, `inherit: true` resolves against the product's OWN Inactivity Period setting (bounded — no parent-chain climb).

## The wire contract

Clean versioned DTOs (`Model/ManagementApi/Alerts/PolicyDto.cs`), NOT the UI view models and NOT the durable entity. Enum-valued fields carry the DOMAIN ENUM NAME as a string (value tables in the DTO XML remarks and the OpenAPI spec).

### Data policy (`PolicyDto`)

```json
{
  "id": "0f0b...",
  "conditions": [
    { "property": "Value", "operation": "GreaterThan", "target": 20, "combination": "And" }
  ],
  "notification": {
    "template": "[$product]$path $operation $target",
    "repeat": "Hourly",
    "instantSend": false,
    "startAt": "2026-10-01T00:00:00Z"
  },
  "destination": { "mode": "Custom", "chats": ["<chat id>"] },
  "icon": "🔥",
  "status": "Error",
  "confirmationPeriod": "00:05:00",
  "isDisabled": false,
  "scheduleId": "<schedule id>",
  "templateId": null,
  "templateAlertId": null
}
```

- `conditions` (1..10): `property`/`operation` must be a pair the TARGET SENSOR'S TYPE offers — the same surface the web editor's dropdowns render (see the table below). `target` is a typed JSON value whose type follows the property: a number for numeric properties (integer properties require an integer), a string for Comment and String `Value`, a TimeSpan string (`"00:30:00"`) for TimeSpan `Value`, a version string (`"1.2.3"`) for Version `Value`. Target-less operations take `null`; the server fills the core's `LastValue(self)` target for them. `combination` joins the condition with the PREVIOUS one (`And` default).
- `notification`: the message effect; `null` = none. `repeat` is the AlertRepeatMode name (`Immediately` default, `FiveMinutes`, `TenMinutes`, `FifteenMinutes`, `ThirtyMinutes`, `Hourly`, `Daily`, `Weekly`).
- `destination`: `FromParent` | `Empty` | `AllChats` | `Custom` (chats only with `Custom`, 1..20); `NotInitialized` is accepted only as an echo of a read (unconfigured destination).
- `icon`: any non-empty string; `status`: `"Error"` (the fired status action) or null; at least ONE effect (notification, icon, Error status) is required.
- `confirmationPeriod`/TTL-interval strings are TimeSpan values (`TimeSpan.TryParse`, invariant).
- `templateId`/`templateAlertId` are server-owned, output-only: values sent in request bodies are ignored — create always writes `null` (a body claiming template ownership would otherwise hit the core's add gate and answer a misleading 409), and PATCH carries the STORED policy's linkage (template ownership cannot be minted or un-minted through this API; it is managed by the alert-templates surface).

### TTL policy (`TtlPolicyDto`)

Same action fields, plus the interval — and the interval is EXPLICIT:

- `interval`: a TimeSpan string, or
- `inherit: true`: take the interval from the parent node (the explicit reset-to-parent switch).

Exactly one of the two on every write; both or neither is a 422. The client never expresses "reset to parent" via a null — the server owns that translation (`PolicyUpdate.TTL = null` in full-list semantics).

### Per-sensor-type condition table (validated server-side, mismatch → 422)

| Sensor type | Properties (beyond the common set) |
|---|---|
| Integer, Double, Rate, Enum | `Value` (numeric), `EmaValue` (numeric) |
| TimeSpan | `Value` (numeric, TimeSpan target) |
| String | `Value` (text ops), `Length` (numeric) |
| Version | `Value` (text ops, version target) |
| Boolean | — (common set only) |
| File | `OriginalSize` (numeric, long) |
| IntegerBar, DoubleBar | `Min`, `Max`, `Mean`, `Count`, `FirstValue`, `LastValue` (numeric), `EmaMin`, `EmaMax`, `EmaMean`, `EmaCount` (numeric) |

Common set (every type): `Status` (IsChanged, IsChangedToOk, IsChangedToError, IsOk, IsError — no target), `Comment` (Equal, NotEqual, Contains, StartsWith, EndsWith — string target; IsChanged — no target), `NewSensorData` (ReceivedNewValue — no target). Numeric properties take `<=`, `<`, `>`, `>=`, `=`, `≠` (LessThanOrEqual…NotEqual); String/Version `Value` takes Equal/NotEqual/Contains/StartsWith/EndsWith. Target-less-ness is a property of the OPERATION, not the property (`IsChanged`, `IsChangedToOk`, `IsChangedToError`, `IsOk`, `IsError`, `ReceivedNewValue` wherever they appear): no target — the comparison is against the sensor's own previous value (the server stores the core's `LastValue(self)` target, what the web editor submits for the same condition); an explicit target is a 422. The table mirrors the web editor's condition view models (`Model/DataAlerts/ConditionViewModels/`) — the same surface an operator sees.

### Error model

The area's uniform JSON contract (`ManagementApiErrorDto`: `error`/`message`/`details`):

| Status | Code | When |
|---|---|---|
| 400 | `validation_failed` | binding failures (malformed JSON, wrong types) — the `[ApiController]` factory |
| 401 | `unauthorized` | missing/invalid bearer token |
| 403 | `forbidden` | read-only token (also blocked by the method-shaped policy backstop before the action) or the owner cannot write at the boundary |
| 404 | `not_found` | unknown or invisible sensor/product/policy — indistinguishable |
| 409 | `conflict` | template-owned policy edited beyond its disable toggle or deleted; cache write failure; a write the node's change ownership silently refused |
| 422 | `unprocessable_entity` | NEW with this surface: semantic validation — condition-vs-type mismatch, operation-vs-property mismatch, untyped/unparseable target, unavailable chat, unknown schedule reference, interval/inherit contradictions, no effect at all. Field-keyed `details` like a 400 |

## Auth / roles

HsmApiToken bearer tokens only (`HsmApiTokenDefaults.ManagementPolicy`), served on the web-UI port. Since #1384 a token mirrors its owner: reads follow the owner's sight, writes need the owner's Manager role at the boundary AND a read-write token (a read-only token never passes an unsafe method — the `HsmApiTokenOnlyAuthorizationHandler` backstop denies it before the action, with the uniform 403 body). Product data-policy writes authorize at the product boundary AND at the owning sensor's boundary. Authorization precedes existence checks and validation (anti-enumeration: an unknown id and an invisible one answer the same 404, and no body-shape error reveals a target). The write initiator is the token's OWNER as a user (`InitiatorInfo.AsUser(ownerName)`) — the API equivalent of the UI's `CurrentInitiator`.

## Semantics cautions (all inherited from the core, deliberately unchanged)

- A `SensorUpdate.Policies` list REPLACES the sensor's list (full-list semantics) — that is why every write re-asserts the siblings.
- Template-owned policies (`templateId != null`): the core lets a user initiator change only `isDisabled` and preserves them on drops. The API surfaces this as 409 (edit the template instead) instead of a silent no-op; a request that differs from the stored content in ANYTHING but `isDisabled` is a 409. A pure-toggle PATCH is detected by rendering the stored policy through the same DTO mapper and comparing — and once detected it skips content validation entirely and sends the STORED content with only `isDisabled` from the body (template-minted content can legitimately sit outside the API's expression range — properties/operations the editor lacks, non-Const targets reading as `target: null`, no-effect bodies — and the core never writes that content on a user-initiated toggle anyway).
- TTL schedule gates (#1404/#1447): `scheduleId` on a TTL policy gates expiry at evaluation time; references must resolve (422 otherwise).
- Schedule-id detach on schedule delete (#1409) and folder chat removal (#1451) re-assert full lists themselves — orthogonal to this surface.
- Change-ownership: a policy owned (change-table) by a higher-priority initiator can be silently skipped by the core; the API detects the missing delta after the update and answers 409 ("accepted but not applied") instead of echoing a ghost.
- Concurrent writes: API-to-API writes to the same node are serialized by the service's per-node gate (see Design) — both acknowledged writes land. Last-write-wins remains versus the web editor (two browser tabs, or a tab racing an API call), which bypasses the gate.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Controllers/SensorPoliciesApiController.cs` | Thin REST rendering: sensor data + TTL policy CRUD |
| `src/server/HSMServer/Controllers/ProductPoliciesApiController.cs` | Thin REST rendering: product aggregate + product TTL CRUD |
| `src/server/HSMServer/Model/ManagementApi/Alerts/PolicyAdministrationService.cs` | The write engine: validation, merge, one atomic full-list update |
| `src/server/HSMServer/Model/ManagementApi/Alerts/AlertReadService.cs` (+ policy reads) | The read surface — the #1393 single visibility source |
| `src/server/HSMServer/Model/ManagementApi/Alerts/PolicyDto.cs` | Wire DTOs (typed conditions, destination, notification, TTL interval/inherit) |
| `src/server/HSMServer/Model/ManagementApi/Alerts/AlertPolicyConditionRules.cs` | The per-type condition table + target validation (the 422 engine) |
| `src/server/HSMServer/Model/ManagementApi/Alerts/AlertPolicyDtoMapper.cs` | Model ↔ DTO mapping, write-side normalizations |
| `src/server/HSMServer/Model/ManagementApi/Alerts/PolicyWriteResult.cs` | Transport-agnostic write envelope (REST today, MCP phase 2) |
| `src/tests/HSMServer.Core.Tests/Model/ManagementApi/PolicyAdministrationServiceTests.cs` | Merge semantics on the real cache |
| `src/tests/HSMServer.Core.Tests/Controllers/SensorPoliciesApiControllerTests.cs` | Conventions, auth mapping, 422/409, round-trip |
| `src/tests/HSMServer.Core.Tests/Controllers/ProductPoliciesApiControllerTests.cs` | Aggregate read, routed writes, product TTL round-trip |

## Tests

- Service seam (real `TreeValuesCache`, `MonitoringCoreTestsBase` pattern): create/update/delete leaves sibling policies and TTL intervals untouched; a TTL write does not re-stamp sibling change-table ownership and the next template apply still lands (PreserveChangeOwnership on re-asserts); the inherit switch resets to parent; template-owned content changes and deletions answer conflict while pure toggles pass (including toggles whose echoed content sits outside the API's write surface); template fields in request bodies are ignored (create succeeds, PATCH cannot mint ownership); two concurrent creates on one sensor both survive (the per-node write gate); the write-gate registry stays bounded (unknown-id 404s and forbidden writes allocate no gate; an idle gate is removed after the write); the product aggregate routes writes to the owning sensor; product TTL full CRUD.
- Controller seam (mocked cache dispatching onto live sensor models): area-attribute conventions; the 403/404 authorization mapping (404-first, body validation never runs for an unauthorized caller); 422s for condition-vs-type, operation-vs-property, untyped targets, an explicit target on a target-less operation, unavailable/available chats, unknown schedule references, missing effects, interval/inherit contradictions; target-less operations decided per operation (Comment/IsChanged with no target stores LastValue(self); the GET echo PATCHes back); 409 on cache failure; full create → get → patch → delete round-trips for all four resources.
- Swagger conventions (`ManagementApiSwaggerTests`): every action of both controllers is in the response-annotations map; `POST /products/{id}/policies` is the area's single documented no-success action.

## Known Issues / Limitations

- PATCH is full-replace at item granularity (documented): there is no field-level merge. A "toggle isDisabled" PATCH carries the policy's whole content — `GET` then modify then `PATCH` round-trips cleanly.
- The pure-toggle detection compares on the API's expression of the content; a stored policy holding fields outside the API's range never reads equal to an API-shaped body, so it answers 409 conservatively.
- Enum-typed fields parse case-insensitively; canonical casing is what reads and the spec publish.
- MCP write tools (phase 2), sensor-settings CRUD and schedules/templates CRUD (phase 3) are out of scope.
