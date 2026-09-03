# HSM Server

## Management API
* Added REST CRUD for alert templates at `/api/v1/alertTemplates` (list with pagination, get, create, update, delete) for non-interactive clients — authenticated with a personal API token (`hsm_pat_` bearer) and authorized per folder through the token's `alerts:read`/`alerts:write` grants intersected with the owner's current rights. SitePort only; JSON errors.

## Chats
* Added per-chat sensor usage count badge so operators can see how many sensors feed each chat at a glance.

## Sensors
* Top CPU sensors are now nested under the `.computer` node, matching the parent-node convention used by the rest of the tree.
* Sensor initialization now publishes its `initialized` flag only after the history load completes, with same-thread re-entry guarded — previously a latch-on-failure could lock the sensor into an unreadable state on startup.

## Dependencies
* Bundled `HSMDataCollector` 3.5.0.
