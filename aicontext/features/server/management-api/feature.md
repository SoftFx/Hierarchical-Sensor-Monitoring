# Feature: Management REST API (resource controllers)

> Owner: server | Last reviewed: 2026-09-03 | Canonical: yes
> Scope: the `/api/v1` REST resource controllers for non-interactive management clients (bearer-token authenticated); the conventions every controller in the area follows. First — and so far only — controller: alert templates CRUD (#1351).

---

## Overview

Epic #1347 gives non-interactive clients (AI agents, scripts, resident services) a machine-friendly REST management API under `/api/v1`. Authentication (the `HsmApiToken` bearer scheme), the fail-closed area guard, the security-event sink and the effective-rights evaluator are owned by the api-tokens feature; this feature owns the resource controllers built on top of them. Alert templates are the first resource (#1351, full CRUD); alert schedules follow (#1352, read-only); OpenAPI publication and the formal JSON error contract are #1353.

## Invariants

- **Area conventions (every controller, enforced by `ManagementApiGuardMiddleware` before authentication)**: attribute-routed `ControllerBase` (never the cookie-world `BaseController`), route `api/v1/<resource>` (camelCase plural mirroring the controller resource name, e.g. `alertTemplates`), class-level `[ManagementApi]` + `[Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]`, no `[AllowAnonymous]`. SitePort-only; a 404 (never 403/redirect) answers anything non-conforming in the area. A reflection test pins the attribute set on every controller.
- **Authorization is per request** through `IApiTokenAuthorizationService` at the resource's own boundary, operations from the `ApiTokenOperations` catalog (alert templates: `alerts:read` / `alerts:write` at the template's **Folder**). The evaluator's 403/404 anti-enumeration split is preserved verbatim: invisible/out-of-reach → 404, in reach but not granted → 403 — returned as explicit `Problem` results (never `Forbid()`, which would engage the cookie scheme's redirect handling). **Authorization precedes body validation**, so error ordering leaks nothing about existence or rights; an unknown id is a plain 404 with no evaluator call. **List endpoints filter per item by the operation the item endpoint demands** (`IsVisible(principal, operation, resource)` — the same conjunction as `Authorize`, minus security-event recording): an item is listed only when the token's grants cover that operation at the item's boundary, so a token granted only e.g. `history:read` at a folder — which gets a 403 on `GET {id}` — sees nothing in the list either. Never 403-per-item: ungranted folders are simply not listed. The decision is memoized per **distinct** folder within one list request; no snapshot API exists yet.
- **Server-assigned identity on create**: POST ignores any client-sent id (the cache Add is an upsert by id — honoring a client id would let a scoped token overwrite a template it cannot see). PUT requires the body id to equal the route id or be absent.
- **Folder moves need write on both sides**: updating a template into a different folder requires `alerts:write` on the CURRENT folder (the move destroys its per-sensor policies) and on the NEW one (the template injects policies into its sensors).
- **JSON in, JSON out, never HTML**: `[ApiController]` automatic ValidationProblemDetails for binding failures; manual 400s are `ValidationProblem` with field-keyed errors; state-dependent write failures from the cache (folder without products, partial per-sensor policy removal) are 409 + ProblemDetails. Nothing throws out of an action — the global exception handler renders Razor.
- **Validation parity with the web UI** (same order, same error strings): global case-sensitive name uniqueness excluding self, at-least-one-path, path/type mismatch check (#1210; AnyType templates skip it), chat availability (a destination chat must be global or bound to the template's folder — the UI dropdown rule). Plus API-side structural validation the UI gets for free from its widgets: enum membership of every byte field (Operation/Property/Combination/TargetType/RepeateMode/SensorStatus/SensorType) BEFORE the domain casts, parallel-list length equality for TTL policies/intervals, schedule tick range, chat keys parseable as Guids, **null list elements rejected** (`"policies": [null]` is a 400 — System.Text.Json materialises it, and the structural pass runs outside the reconstruction try, so a null dereference there would be a 500), and explicit size bounds the widgets impose implicitly (name ≤ 200 chars, ≤ 100 paths, ≤ 100 policies regular+TTL combined). `"destination": null` / `"schedule": null` mean "omitted" (the mapper substitutes the defaults), not a domain parse failure. A missing/empty `folderId` is a 400 (never routed through the evaluator's 404 — an all-zero Guid references no folder and leaks nothing); on PUT that 400 is issued only AFTER authorization, so a body-shape error cannot reveal that a template exists to a caller outside its reach. Domain reconstruction runs inside a try — `Policy.Apply` throws for condition properties a sensor type does not support, and that is a 400, never a 500.
- **Write-side normalizations (observable in the echoed DTO)**: server-generated template id; `Guid.Empty` policy ids regenerated (empty ids would collide in per-sensor policy collections at apply time); destination chat display names overwritten with the manager's current names.
- **Client cancellation answers 499, never a 500**: the cache rethrows `OperationCanceledException` from its reconciliation loops; the actions catch it and return 499 so nothing escapes to the (Razor-rendering) global handler. The write is not transactional at the cancellation point — a cancelled reconcile can leave the template persisted with only some sensors reconciled (pre-existing cache behavior, same for the web UI).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | List templates (paginated, visible folders only) | API client (bearer token) |
| 2 | Get one template by id | API client |
| 3 | Create template (server id, 201 + Location) | API client |
| 4 | Update template (upsert semantics, optional folder move) | API client |
| 5 | Delete template (204; 409 when per-sensor policy removal fails) | API client |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `GET /api/v1/alertTemplates?page&pageSize` | `AlertTemplatesApiController.GetTemplates` | 200 `{items, page, pageSize, totalCount, totalPages}`; clamps page≥1 and page≤totalPages (a page beyond the end returns the last page, never wrapped-int duplicates), 1≤pageSize≤200 (default 50); order: name (OrdinalIgnoreCase), then id |
| `GET /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.GetTemplate` | 200 DTO / 404 |
| `POST /api/v1/alertTemplates` | `AlertTemplatesApiController.CreateTemplate` | 201 + Location + canonical DTO; client id ignored |
| `PUT /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.UpdateTemplate` | 200 + canonical stored DTO; body id must match route; folder move gated on both folders |
| `DELETE /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.DeleteTemplate` | 204 / 404 / 403 / 409 |
| `AlertTemplateDto` (+ nested policy/condition/destination/schedule/interval DTOs) | `Model/ManagementApi/AlertTemplates/AlertTemplateDto.cs` | Mirrors the durable entity one field per field, Guid ids as strings. Deliberately omits dead entity fields: `Destination.Kind` (legacy, never read/written), `PolicyEntity.TTL` on TTL policies (written, never read — the interval is authoritative in `ttls[i]`), legacy single `Path`/`TTLPolicy`/`TTL`. Byte fields are the domain enum values (PolicyOperation/PolicyProperty/PolicyCombination/TargetType/SensorStatus/AlertRepeatMode/SensorType; 100 = AnyType). camelCase JSON via MVC defaults. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Controllers/AlertTemplatesApiController.cs` | The first `/api/v1` resource controller; the conventions reference for #1352 |
| `src/server/HSMServer/Model/ManagementApi/AlertTemplates/AlertTemplateDto.cs` | Wire DTOs |
| `src/server/HSMServer/Model/ManagementApi/AlertTemplates/AlertTemplateDtoMapper.cs` | DTO ↔ entity mapping + write-side normalizations |
| `src/server/HSMServer/Model/DataAlertTemplates/AlertTemplatePathValidation.cs` | #1210 path/type mismatch rule shared by the cookie UI controller and this API controller so the two surfaces cannot drift |
| `src/tests/HSMServer.Core.Tests/Controllers/AlertTemplatesApiControllerTests.cs` | Conventions pin, authorization mapping, validation, round-trip |

## Data Flow

```
bearer token ──► ManagementApiGuard (SitePort, [ManagementApi]+policy, else 404)
             ──► HsmApiToken scheme (generic 401 on any failure)
             ──► controller action
                  ──► IApiTokenAuthorizationService.Authorize(User, op, Folder(template or dto folder))
                  │     Allowed → proceed; Forbidden → 403 Problem; NotFound → 404
                  ──► structural validation (enums, list parity, ids) ── 400 ValidationProblem
                  ──► entity reconstruction (try) ── 400 on unsupported domain input
                  ──► semantic validation (name/path/mismatch/chats) ── 400
                  ──► TreeValuesCache.Add/RemoveAlertTemplateAsync ── 409 on (false, error)
                  └─► 201+Location / 200 + stored DTO / 204
```

## Storage / Persistence

None of its own — reads and writes go through `TreeValuesCache` (`AddAlertTemplateAsync` upsert-by-id, `RemoveAlertTemplateAsync`, `GetAlertTemplate(s)`); durable rows are the alert-templates feature's.

## UI / Operator Visibility

No UI. The cookie web UI (`AlertTemplatesController`) remains the human surface; the REST API mirrors its validation rules.

## Dependencies

- Depends on: api-tokens feature (scheme, area guard, evaluator, security events), alert templates domain (`TreeValuesCache`, `AlertTemplateModel`).
- Used by: non-interactive management clients; #1352 (schedules read-only) copies these conventions; #1353 documents the area with OpenAPI.

## Tests

`tests.md` next to this file.

## Notes

- **Echo is canonical**: POST/PUT return the stored shape, not the request — normalizations (ids, chat names) and mode collapse are visible to the caller.
- **Mode collapse (documented)**: a "custom destination with zero chats" reconstructs as `NotInitialized` (the domain derives the mode from flags + chat count); echo-back of GET output is always stable because the DTO carries only the flags.

## Known Issues / Limitations

- `AddAlertTemplateAsync` swallows per-sensor reconciliation failures (logged; returns success) — a 200/201 write can leave sensors unapplied. Pre-existing behavior, identical for the web UI.
- **Name uniqueness is a cross-folder existence oracle** (deliberate, parity with the web UI): the uniqueness check spans all folders, so a folder-A-scoped token can probe whether a template named X exists anywhere on the server — including folders it gets a 404 for — through the duplicate-name 400. Scoping uniqueness to reachable folders would diverge from the UI rule and allow same-name templates the UI considers invalid.
- **PUT racing a DELETE resurrects the template**: the existence check and the upsert `AddAlertTemplateAsync` are not atomic; a PUT concurrent with a successful DELETE re-creates the template under the route id. Same upsert semantics the web UI relies on.
- List re-resolves the caller per **distinct** folder within a request (memoized); templates sharing a folder cannot straddle a mid-request grant/role change, but two folders in one page still can. A snapshot API in the evaluator is a possible follow-up.
- The ported `TryApplyPathTemplates` semantic check is effectively unreachable through string input (the converter builds the pattern lazily; a malformed pattern surfaces at match time, not registration) — kept for parity with the web UI.
- No OpenAPI document for the area yet (#1353); error bodies follow `[ApiController]` ProblemDetails defaults, to be formalized there.
