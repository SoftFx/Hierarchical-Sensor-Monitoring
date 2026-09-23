# HSM Server

## Alerts and schedules
* TTL alerts bound to a schedule gate on the schedule at evaluation time instead of the stale value's timestamp — fixes scheduled TTL alerts firing and re-sending overnight after a restart. Out-of-window repeats are cancelled per policy, window-caused resolutions stay silent, and a genuine recovery still sends its Ok.
* Deleting an alert schedule now clears its references from sensor policies, product TTL policies and alert templates (persisted, restart-safe) instead of leaving dangling ids; missing-schedule lookups still fail open and are reported at most hourly per id. Migration note: creating, editing and deleting alert schedules is now admin-only; viewing stays open to all users.
* Fixed a database race that could silently drop a policy from the index after concurrent template applies — alerts no longer vanish after a restart; orphaned rows self-heal at boot.

## API tokens
* Per-token usage monitoring for `/api/v1` and `/mcp`: rate and request-duration sensors per token (self-monitoring) and the token EntityId in Profile.

## Linux probe
* First Linux host + Docker Compose probe: Linux metric sources, per-product Linux probe download bundle, native bar-period and stop-drain fixes, `.module` start/stop markers in both collectors.

## Infrastructure
* docker-compose ships Caddy with automatic Let's Encrypt certificates (explicit certificate modes, startup gating); native collector 0.7.4, HsmAgent 0.5.33.

## Chats
* Slack/Mattermost webhook URLs are masked in EditChat (partial edits of the masked value are rejected); the configured Telegram bot name shows on the Add Chat Telegram tab; FromParent chat routing now stops at the first non-inheriting ancestor.

## Sensors
* Self-destroy defers its decision for uninitialized sensors and isolates per-sensor failures in the sweep; metric-source read errors are reported and the disk-space prediction reports the truth.

## Dependencies
* Bundled `HSMDataCollector` 3.5.0.
