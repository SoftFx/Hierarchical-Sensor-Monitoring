# Feature: Management REST API (resource controllers)

> Owner: server | Last reviewed: 2026-09-04 | Canonical: yes
> Scope: the `/api/v1` REST resource controllers for non-interactive management clients (bearer-token authenticated); the conventions every controller in the area follows — including the uniform JSON error contract and the OpenAPI publication (#1353). Controllers: alert templates CRUD (#1351), alert schedules read-only (#1352).

---

## Overview

Epic #1347 gives non-interactive clients (AI agents, scripts, resident services) a machine-friendly REST management API under `/api/v1`. Authentication (the `HsmApiToken` bearer scheme), the fail-closed area guard, the security-event sink and the effective-rights evaluator are owned by the api-tokens feature; this feature owns the resource controllers built on top of them. Alert templates (#1351, full CRUD) and alert schedules (#1352, read-only) are landed; #1353 added the area's uniform JSON error contract and its OpenAPI publication: every operation of both controllers is annotated in the server's single swagger document (`/api/swagger`, `HSMSwaggerComments.xml` doc comments + `ProducesResponseType`), carries the `HsmApiToken` bearer security requirement (scoped by `ManagementApiSecuritySwaggerFilter` — the doc also contains the sensor-data API, which keeps its Key header), and documents the domain enum values of every byte field in the DTO XML remarks, so an agent that reads only the spec can authenticate and perform every operation.

## Invariants

- **Area conventions (every controller, enforced by `ManagementApiGuardMiddleware` before authentication)**: attribute-routed `ControllerBase` (never the cookie-world `BaseController`), route `api/v1/<resource>` (camelCase plural mirroring the controller resource name, e.g. `alertTemplates`), class-level `[ManagementApi]` + `[Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]`, no `[AllowAnonymous]`. SitePort-only; a 404 (never 403/redirect) answers anything non-conforming in the area. A reflection test pins the attribute set on every controller.
- **Authorization is per request** through `IApiTokenAuthorizationService` at the resource's own boundary, operations from the `ApiTokenOperations` catalog (alert templates: `alerts:read` / `alerts:write` at the template's **Folder**). The evaluator's 403/404 anti-enumeration split is preserved verbatim: invisible/out-of-reach → 404, in reach but not granted → 403 — returned as explicit uniform-error results (never `Forbid()`, which would engage the cookie scheme's redirect handling). **Authorization precedes body validation**, so error ordering leaks nothing about existence or rights; an unknown id is a plain 404 with no evaluator call. **List endpoints filter per item by the operation the item endpoint demands** (`IsVisible(principal, operation, resource)` — the same conjunction as `Authorize`, minus security-event recording): an item is listed only when the token's grants cover that operation at the item's boundary, so a token granted only e.g. `history:read` at a folder — which gets a 403 on `GET {id}` — sees nothing in the list either. Never 403-per-item: ungranted folders are simply not listed. The decision is memoized per **distinct** folder within one list request; no snapshot API exists yet.
- **Server-assigned identity on create**: POST ignores any client-sent id (the cache Add is an upsert by id — honoring a client id would let a scoped token overwrite a template it cannot see). PUT requires the body id to equal the route id or be absent.
- **Folder moves need write on both sides**: updating a template into a different folder requires `alerts:write` on the CURRENT folder (the move destroys its per-sensor policies) and on the NEW one (the template injects policies into its sensors).
- **Uniform JSON error contract (#1353)**: every error response of every error path in the area — controller results, `[ApiController]` automatic binding failures (`WrapBindingFailureFactory` in Program.cs: management actions get the uniform body, every OTHER ApiController — sensor-data, Grafana — delegates to the **captured framework default** verbatim, so their `problem+json` wire shape is untouched), the `HsmApiToken` 401 challenge, `ManagementApiGuardMiddleware` rejections, `LegacyBearerGuardMiddleware`'s 401 for a misplaced token, and unhandled exceptions on ANY `/api` path (`ApiExceptionJsonMiddleware` sits between the global `/Error` handler and `LoggingExceptionMiddleware`, so non-API paths still get the Razor page and the failure is still logged; it resets only status + content headers, never `Response.Clear()` — headers set by outer middleware such as HSTS survive) — is `application/json` with the SAME three-field body `ManagementApiErrorDto` (`ManagementApiErrors` result factory for MVC paths, `ManagementApiErrorResponses` for pipeline paths): `error` (stable machine code, one per status: `validation_failed`/`unauthorized`/`forbidden`/`not_found`/`conflict`/`internal_error`, append-only), `message` (human summary), `details` (field-keyed messages on 400s — **item-indexed** keys like `policies[2].conditions[0].operation`, empty binder messages get the framework's fallback wording, `{"traceId": …}` on 500s — correlated with the ASP log layout (`aspnet-TraceIdentifier`) — explicit `null` otherwise; the three fields are always present). All 404s of the area — unknown id, invisible folder, unmatched route, wrong port, off-SitePort swagger — render the SAME generic body (`ManagementApiErrors.NotFoundMessage`), preserving anti-enumeration at the routing layer too. Never HTML on `/api/*`; nothing throws out of an action. The contract's scope is `/api/v1` responses plus 500s anywhere under `/api`; unrouted non-v1 `/api` paths still answer the framework's bare 404.
- **JSON in, JSON out, never HTML**: manual 400s are `ManagementApiErrors.Validation` with **item-indexed** field keys (`policies[2].conditions[0].operation` — the shape `[ApiController]` itself produces for binding failures, so multi-policy payloads have locatable errors); state-dependent write failures from the cache are 409 — POST/PUT only on "folder without products" (the apply loop swallows per-sensor failures), DELETE also on partial per-sensor policy removal.
- **Validation parity with the web UI** (same order, same error strings): global case-sensitive name uniqueness excluding self, at-least-one-path, path/type mismatch check (#1210; AnyType templates skip it), chat availability (a destination chat must be global or bound to the template's folder — the UI dropdown rule), schedule references must resolve (`scheduleId` is validated against the schedule provider — the UI dropdown cannot produce a dangling id, and at evaluation an unknown id is silently treated as always-in-working-time). Plus API-side structural validation the UI gets for free from its widgets: enum membership of every byte field (Operation/Property/Combination/TargetType/RepeateMode/SensorStatus/SensorType) BEFORE the domain casts — and of the **sparse long `TimeInterval` enum** for `ttls[].interval` (an undefined value persists but throws `NotImplementedException` in the timeout-scan loop, outside the controller's try; ticks-authoritative intervals also keep `now + ticks` inside the DateTime range — `AddTicks` throws in the same loop), parallel-list length equality for TTL policies/intervals, schedule tick range, chat keys parseable as Guids, **null list elements rejected** (`"policies": [null]` is a 400 — System.Text.Json materialises it, and the structural pass runs outside the reconstruction try, so a null dereference there would be a 500), **duplicate non-empty policy ids rejected** (the id becomes the per-sensor `TemplateAlertId` — the apply-time matching key — so duplicates silently collapse two policies into one), and explicit size bounds the widgets impose implicitly (name ≤ 200 chars, ≤ 100 paths, ≤ 100 policies regular+TTL combined — collection counts and name only; individual strings stay bounded by the request body limit, see Known Issues). `"destination": null` / `"schedule": null` mean "omitted" (the mapper substitutes the defaults), not a domain parse failure. A missing/empty `folderId` is a 400 (never routed through the evaluator's 404 — an all-zero Guid references no folder and leaks nothing); on PUT that 400 is issued only AFTER authorization, so a body-shape error cannot reveal that a template exists to a caller outside its reach. Domain reconstruction runs inside a try — `Policy.Apply` throws for condition properties a sensor type does not support, and that is a 400, never a 500.
- **Write-side normalizations (observable in the echoed DTO)**: server-generated template id; `Guid.Empty` policy ids regenerated (empty ids would collide in per-sensor policy collections at apply time); destination chat display names overwritten with the manager's current names.
- **Writes are never client-cancellable**: `RequestAborted` is deliberately NOT forwarded into the cache. `AddAlertTemplateAsync` persists the template BEFORE reconciling sensors, and `RemoveAlertTemplateAsync` strips per-sensor template policies BEFORE removing the template — a client-triggered cancellation mid-reconcile would leave half-applied state, and a cancelled DELETE would silently leave the template alive with some sensors already disarmed (nobody to report to — the client is gone). The web UI passes no token either: a write completes once accepted.
- **Global read-only resources (alert schedules, #1352)**: schedules are not folder-scoped, and the web UI shows them to every logged-in user. The gate is therefore caller-wide, not per-resource, and lives **inside the evaluator** (`HasOperationAtAnyVisibleBoundary`): the caller may read schedules when ANY of the token's `alerts:read` grants sits at a boundary that currently passes the evaluator's list predicate — candidate boundaries are enumerated from the token's own grants inside the evaluator (no grant logic in the controller), with caller resolution, token liveness and owner visibility/capability all inside the same conjunction. An unentitled caller gets 403 for every id (the provider is never queried — schedule existence is not per-caller scoped, so nothing leaks), an entitled one a plain 404 for an unknown id. One denial audit record per denied request is written by the gate itself, as `AuthorizationDenied` — the plain 403 scope-denial kind, deliberately NOT the `AuthorizationNotFound` enumeration-probe signal, because the caller-wide gate discloses nothing about any concrete target. Sensor references in the DTO (full paths, the UI list page shows them as a tooltip) are filtered per sensor through the evaluator at the sensor's **product** boundary under the same `alerts:read` operation — mere reach is not enough, a token granted only e.g. `dashboards:read` at a product must not learn its sensor paths from an alerts response — so a folder-scoped token never learns paths outside its alerts:read grants; parentless sensors fail closed (dropped). A Global `alerts:read` grant under an admin owner (the token-side "everywhere" shape — it can pass the gate through the Global boundary) short-circuits the per-product filter once per request (`HasOperationAtGlobalScope`): the per-product predicate deliberately never treats a Global grant as a wildcard, so without the short-circuit the broadest token would get every schedule with an empty `sensors` list. On the list path the decision is memoized per **distinct** product within one request (sensors cluster into few products; the evaluator re-resolves caller + grants on every call), and the page's sensor references are resolved in ONE bulk cache pass (`GetSensorsByAlertSchedules` — the per-id lookup scans every sensor, so per-item calls would be a full scan per schedule; the cookie UI list uses the same bulk method).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | List templates (paginated, visible folders only) | API client (bearer token) |
| 2 | Get one template by id | API client |
| 3 | Create template (server id, 201 + Location) | API client |
| 4 | Update template (upsert semantics, optional folder move) | API client |
| 5 | Delete template (204; 409 when per-sensor policy removal fails) | API client |
| 6 | List schedules (paginated; caller-wide alerts:read gate; sensors filtered per visibility) | API client |
| 7 | Get one schedule by id (same gate) | API client |

### The AI-agent scenario ("manages alerts via key", #1347/#1353 acceptance)

The acceptance bar of the epic: an AI agent that starts from NOTHING but the OpenAPI document can manage alert templates end to end.

1. **Credential**: the agent's operator provisions a personal API token (an operator action today — a self-service issuance flow is #1356 step 4) and hands over the full credential `hsm_pat_v1_<token id>.<secret>`, disclosed once.
2. **Discovery**: the agent reads the swagger document (`/api/swagger` → `/swagger/{version}/swagger.json`). The `HsmApiToken` security scheme tells it the Authorization header shape (`Bearer hsm_pat_v1_…`) and the credential lifecycle; management operations are marked with the scheme's security requirement; the doc-level description states the error contract; every operation declares its statuses with the `ManagementApiErrorDto` schema.
3. **Act**: it calls `GET /api/v1/alertTemplates` (or `/api/v1/alertSchedules`) on the web-UI port with the bearer header. Missing/invalid token → 401 `unauthorized`; grants insufficient → 403 `forbidden`; both bodies machine-parseable.
4. **Write**: it creates/updates/deletes templates built purely from the DTO schemas — enum byte fields carry their full value tables in the field remarks, round-trip traps (id handling, parallel TTL lists, chat availability) are documented in the DTO summary. Malformed payloads → 400 `validation_failed` with field-keyed `details` the agent can act on mechanically.
5. **Trust the errors**: every failure — down to a server 500 (`internal_error` + `details.traceId` to quote in a bug report) — is JSON with a stable `error` code; the agent never has to parse HTML or distinguish empty bodies.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `GET /api/v1/alertTemplates?page&pageSize` | `AlertTemplatesApiController.GetTemplates` | 200 `{items, page, pageSize, totalCount, totalPages}`; clamps page≥1 and page≤totalPages (a page beyond the end returns the last page, never wrapped-int duplicates), 1≤pageSize≤200 (default 50); order: name (OrdinalIgnoreCase), then id |
| `GET /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.GetTemplate` | 200 DTO / 404 / 403 / 401 |
| `POST /api/v1/alertTemplates` | `AlertTemplatesApiController.CreateTemplate` | 201 + Location + canonical DTO; client id ignored; 400 / 403 / 404 / 409 on failure |
| `PUT /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.UpdateTemplate` | 200 + canonical stored DTO; body id must match route; folder move gated on both folders; 400 / 403 / 404 / 409 on failure |
| `DELETE /api/v1/alertTemplates/{id}` | `AlertTemplatesApiController.DeleteTemplate` | 204 / 404 / 403 / 409 |
| `GET /api/v1/alertSchedules?page&pageSize` | `AlertSchedulesApiController.GetSchedules` | Read-only; 200 page; same envelope and clamps as templates; order name-then-id; 403 without an accessible `alerts:read` grant |
| `GET /api/v1/alertSchedules/{id}` | `AlertSchedulesApiController.GetSchedule` | 200 DTO / 404 (entitled caller) / 403 (no accessible `alerts:read` grant) |
| `ManagementApiErrorDto` | `Model/ManagementApi/ManagementApiErrorDto.cs` | The uniform error body of EVERY error path in the area: `{error, message, details}` — codes `validation_failed`/`unauthorized`/`forbidden`/`not_found`/`conflict`/`internal_error`; `details` is field→messages on 400s, `{traceId}` on 500s, explicit null otherwise |
| OpenAPI document | `/api/swagger` (UI) → `/swagger/{ServerConfig.Version}/swagger.json`, **SitePort only** (`SwaggerSitePortOnlyMiddleware` — off-SitePort swagger paths get the uniform 404, so the doc never publishes the management map on the listener the area guard hides it from) | The server's single doc; management operations carry the `HsmApiToken` bearer security requirement, `ProducesResponseType` for every documented status (500 included), XML doc summaries, and enum-value tables in the DTO field remarks. Non-management operations keep the Key/ClientName header advertisement (the Grafana JSON datasource authenticates through the Key header exclusively) |
| `AlertTemplateDto` (+ nested policy/condition/destination/schedule/interval DTOs) | `Model/ManagementApi/AlertTemplates/AlertTemplateDto.cs` | Mirrors the durable entity one field per field, Guid ids as strings. Deliberately omits dead entity fields: `Destination.Kind` (legacy, never read/written), `PolicyEntity.TTL` on TTL policies (written, never read — the interval is authoritative in `ttls[i]`), legacy single `Path`/`TTLPolicy`/`TTL`. Byte fields are the domain enum values (PolicyOperation/PolicyProperty/PolicyCombination/TargetType/SensorStatus/AlertRepeatMode/SensorType; 100 = AnyType) — the valid values are documented per field in the XML remarks and thus in the OpenAPI spec. camelCase JSON via MVC defaults. |
| `AlertScheduleDto` | `Model/ManagementApi/AlertSchedules/AlertScheduleDto.cs` | `{id, name, timezone, schedule(YAML), sensors: [paths]}` — the durable fields the UI editor shows plus visibility-filtered sensor references |
| `ApiPageDto<T>` | `Model/ManagementApi/ApiPageDto.cs` | Shared list envelope: `{items, page, pageSize, totalCount, totalPages}` |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Controllers/AlertTemplatesApiController.cs` | The first `/api/v1` resource controller; the conventions reference |
| `src/server/HSMServer/Controllers/AlertSchedulesApiController.cs` | Read-only schedules controller; the caller-wide global-resource gate |
| `src/server/HSMServer/Model/ManagementApi/AlertTemplates/AlertTemplateDto.cs` | Wire DTOs |
| `src/server/HSMServer/Model/ManagementApi/AlertTemplates/AlertTemplateDtoMapper.cs` | DTO ↔ entity mapping + write-side normalizations |
| `src/server/HSMServer/Model/DataAlertTemplates/AlertTemplatePathValidation.cs` | #1210 path/type mismatch rule shared by the cookie UI controller and this API controller so the two surfaces cannot drift |
| `src/server/HSMServer/Model/ManagementApi/AlertSchedules/AlertScheduleDto.cs` | Schedule wire DTO |
| `src/server/HSMServer/Model/ManagementApi/AlertSchedules/AlertScheduleDtoMapper.cs` | Credential-free mapping of the durable schedule fields |
| `src/server/HSMServer/Model/ManagementApi/ManagementApiErrorDto.cs` | The uniform error wire shape |
| `src/server/HSMServer/Model/ManagementApi/ManagementApiErrors.cs` | Error codes, the MVC result factory, the binding-failure factory (`ApiBehaviorOptions.InvalidModelStateResponseFactory`) |
| `src/server/HSMServer/Middleware/ManagementApiErrorResponses.cs` | The single pipeline-side JSON writer (guard 404, legacy 401, challenge, exception handler) |
| `src/server/HSMServer/Middleware/ApiExceptionJsonMiddleware.cs` | JSON 500 with `{traceId}` on `/api` paths instead of the Razor error page |
| `src/server/HSMServer/Middleware/SwaggerSitePortOnlyMiddleware.cs` | Keeps the OpenAPI doc/UI off the sensor port |
| `src/server/HSMServer/Filters/ManagementApiSecuritySwaggerFilter.cs` | Attaches the `HsmApiToken` bearer requirement to management operations only |
| `src/server/HSMServer/Filters/DataRequestHeaderSwaggerFilter.cs` | Key/ClientName headers — scoped (#1353) to sensor-data actions so management operations advertise none |
| `src/tests/HSMServer.Core.Tests/Controllers/AlertTemplatesApiControllerTests.cs` | Conventions pin, authorization mapping, validation, round-trip |
| `src/tests/HSMServer.Core.Tests/Controllers/AlertSchedulesApiControllerTests.cs` | Schedules gate, sensor filtering, pagination |
| `src/tests/HSMServer.Core.Tests/Controllers/ManagementApiErrorContractTests.cs` | Controller-side uniform error bodies (400/403/404/409, 404 indistinguishability) |
| `src/tests/HSMServer.Core.Tests/Middleware/ApiJsonErrorContractTests.cs` | Pipeline-side contract: writer wire shape, guard/legacy 404/401 bodies, binding-failure factory, exception middleware |
| `src/tests/HSMServer.Core.Tests/Swagger/ManagementApiSwaggerTests.cs` | Filter scoping (Key header vs bearer requirement) + the per-action response-annotations conventions pin |

## Data Flow

```
bearer token ──► ApiExceptionJsonMiddleware (catches what's left on /api, else rethrows
             │   to the global /Error Razor handler; LoggingExceptionMiddleware, inner,
             │   has already logged)
             ──► ManagementApiGuard (SitePort, [ManagementApi]+policy, else uniform-JSON 404)
             ──► HsmApiToken scheme (generic 401 on any failure, uniform JSON + WWW-Authenticate)
             ──► binding ([ApiController]) ── invalid → 400 uniform error via the factory
             ──► controller action
                  ──► IApiTokenAuthorizationService.Authorize(User, op, Folder(template or dto folder))
                  │     Allowed → proceed; Forbidden → 403 uniform error; NotFound → 404 uniform error
                  ├── alertSchedules (global, read-only): HasOperationAtAnyVisibleBoundary(User, alerts:read)
                  │     true → proceed (list: one bulk sensor pass + visibility memoized per
                  │             distinct product); false → 403 uniform error + one AuthorizationDenied
                  │             audit record (never the enumeration-probe kind)
                  ├── structural validation (enums, list parity, ids) ── 400 uniform error w/ details
                  ├── entity reconstruction (try) ── 400 on unsupported domain input
                  ├── semantic validation (name/path/mismatch/chats) ── 400
                  ├── TreeValuesCache.Add/RemoveAlertTemplateAsync ── 409 on (false, error)
                  └── 201+Location / 200 + stored DTO / 204
```

## Storage / Persistence

None of its own — reads and writes go through `TreeValuesCache` (`AddAlertTemplateAsync` upsert-by-id, `RemoveAlertTemplateAsync`, `GetAlertTemplate(s)`); durable rows are the alert-templates feature's.

## UI / Operator Visibility

No UI. The cookie web UI (`AlertTemplatesController`) remains the human surface; the REST API mirrors its validation rules.

## Dependencies

- Depends on: api-tokens feature (scheme, area guard, evaluator, security events), alert templates domain (`TreeValuesCache`, `AlertTemplateModel`), alert schedules domain (`IAlertScheduleProvider`), Swashbuckle (the server's single OpenAPI doc).
- Used by: non-interactive management clients; the OpenAPI document at `/api/swagger` is their self-describing entry point.

## Tests

`tests.md` next to this file.

## Notes

- **Echo is canonical**: POST/PUT return the stored shape, not the request — normalizations (ids, chat names) and mode collapse are visible to the caller.
- **Mode collapse (documented)**: a "custom destination with zero chats" reconstructs as `NotInitialized` (the domain derives the mode from flags + chat count); echo-back of GET output is always stable because the DTO carries only the flags.

## Known Issues / Limitations

- `AddAlertTemplateAsync` swallows per-sensor reconciliation failures (logged; returns success) — a 200/201 write can leave sensors unapplied. Pre-existing behavior, identical for the web UI.
- **Name uniqueness is a cross-folder existence oracle** (deliberate, parity with the web UI): the uniqueness check spans all folders, so a folder-A-scoped token can probe whether a template named X exists anywhere on the server — including folders it gets a 404 for — through the duplicate-name 400. Scoping uniqueness to reachable folders would diverge from the UI rule and allow same-name templates the UI considers invalid.
- **PUT racing a DELETE resurrects the template**: the existence check and the upsert `AddAlertTemplateAsync` are not atomic; a PUT concurrent with a successful DELETE re-creates the template under the route id. Same upsert semantics the web UI relies on.
- **Concurrent POSTs with the same name both persist**: the name-uniqueness check reads `GetAlertTemplateModels()` and the cache Add does not enforce uniqueness, so two racing creates can both land. Same race the web UI has; uniqueness is advisory, not a constraint.
- **Per-item string sizes are unbounded**: the guard rails bound collection counts and the name only; path strings, message templates, icons, condition target values and per-policy condition counts are limited by the request body limit alone. A multi-megabyte template would be copied onto every matching sensor and re-serialized on every sensor write. Tighter per-item bounds are a deliberate open decision (the web UI has none either).
- **`sensors` in the schedules LIST response is unbounded** (deliberate, parity with the UI list tooltip): a schedule referenced by tens of thousands of sensors emits every visible full path, for up to `MaxPageSize` schedules in one response. The per-product visibility memoization bounds the CPU cost, not the response size; a count-plus-item-fetch split is a possible follow-up if machine clients prove it necessary.
- **A future schedule WRITE must not reuse the caller-wide gate**: `HasOperationAtAnyVisibleBoundary` is the read-side equivalent of "the UI shows schedules to every logged-in user" — writes at the global boundary are admin-only in the evaluator (`OwnerCanPerform`), and the write endpoint (#1352 follow-up) must go through the regular `Authorize(User, op, Global)` path instead.
- List re-resolves the caller per **distinct** folder within a request (memoized); templates sharing a folder cannot straddle a mid-request grant/role change, but two folders in one page still can. A snapshot API in the evaluator is a possible follow-up.
- The ported `TryApplyPathTemplates` semantic check is effectively unreachable through string input (the converter builds the pattern lazily; a malformed pattern surfaces at match time, not registration) — kept for parity with the web UI.
- **Enum tables in the OpenAPI spec are XML remarks, not generated schemas** (deliberate: typing the DTO byte fields as the domain enums would move their membership check into model binding — BEFORE the in-action folder authorization — and break the authorization-precedes-validation invariant, leaking existence through a 400). The value tables in the DTO XML remarks can drift from the domain enums; the swagger conventions test pins response annotations, not enum tables. Re-verify the remarks when a domain enum gains members.
- **The reserved cookie-only family (`/api/v1/api-tokens`, #1356 step 4) is outside the uniform contract until it lands**: it authorizes through the cookie scheme, whose 401 challenge redirects to the login page — a bearer-shaped JSON 401 there needs that PR's own decision (the guard 404s every unmatched route in the family today, with the uniform body).
- **HSMSwaggerComments.xml is build output that is tracked in git** (pre-existing repo policy, predating #1353): every PR that adds XML doc comments carries a regenerated diff. Changing that policy (gitignore + untrack) is a separate housekeeping decision.
