# HSM Server

## API tokens
* Administrators now have emergency revoke levers for API tokens: a per-user "Revoke API tokens" row action on the Users page (typed username confirmation, shows the user's live token count) and a deployment-wide "Revoke all API tokens" button on Settings → Server next to the API tokens switch (typed `revoke-all` confirmation). Both require a reason, take effect immediately (every affected token stops authenticating before the answer returns), are available even while API tokens are disabled, and are recorded in the server journal and log (initiator, scope, reason, affected count, generation; a dedicated web-UI view for these records is not built yet — the server log is the practical read path). Users create replacement tokens from their profile afterwards.
* Users can now create and manage their own personal API tokens from the new Profile page (header user menu → Profile): create with an operation/boundary grant picker (a read/write table whose column headers select or clear the whole column; opens with the read grants of the boundary the picker starts on preselected — Global for admins) and one-time secret disclosure, list, restrict, rotate and revoke. Tokens are disabled by default — enable them from Settings → Server ("Enable API tokens", effective immediately) or with `ApiTokens.Enabled` in the server configuration file (`MaxTokensPerUser`, `DefaultLifetime` and `MaxLifetime` are configurable too). Every finite lifetime is capped at `MaxLifetime` (365 days by default); the "No expiration" option — offered and preselected by default, switchable off by operators with `ApiTokens.AllowNoExpiration` — is the sole way past that cap; switching it off stops new unlimited tokens only — existing ones (and rotations of them) keep their expiry until revoked. Existing tokens keep authenticating only while the feature is enabled; listing and revocation remain available for cleanup when it is off.
* The web UI header now has a user menu (avatar + name): Profile and Logout.

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
