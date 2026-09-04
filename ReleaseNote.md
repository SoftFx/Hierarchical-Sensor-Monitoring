# HSM Server

## Management API
* Added REST CRUD for alert templates at `/api/v1/alertTemplates` (list with pagination, get, create, update, delete) for non-interactive clients — authenticated with a personal API token (`hsm_pat_` bearer) and authorized per folder through the token's `alerts:read`/`alerts:write` grants intersected with the owner's current rights. SitePort only; JSON errors.
* Added read-only REST access to alert schedules at `/api/v1/alertSchedules` (list with pagination, get by id); requires an `alerts:read` grant at any boundary accessible to the token's owner, and sensor references are filtered to the caller's visible products.
* The management API is now self-describing: full OpenAPI coverage (bearer security scheme, per-operation response schemas with documented error codes, enum value tables on every byte field), served at `/api/swagger` on the web-UI port only. Every error response of the management endpoints (`/api/v1`) carries the uniform JSON body `{error, message, details}` — and unhandled errors anywhere under `/api` answer JSON instead of an HTML error page.

## Chats
* Added per-chat sensor usage count badge so operators can see how many sensors feed each chat at a glance.

## Sensors
* Top CPU sensors are now nested under the `.computer` node, matching the parent-node convention used by the rest of the tree.
* Sensor initialization now publishes its `initialized` flag only after the history load completes, with same-thread re-entry guarded — previously a latch-on-failure could lock the sensor into an unreadable state on startup.

## Dependencies
* Bundled `HSMDataCollector` 3.5.0.
