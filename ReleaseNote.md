# HSM Server

## Alerts and schedules
* A Const target on a targetless condition (New data, Status, Is changed) is now evaluated as the sensor's last value instead of failing template reconstruction with a generic "condition is not supported for this sensor type"; the templates API reports the actual reconstruction reason, and the stored target echoes the input unchanged.
* Migration note: creating, editing and deleting alert templates is now admin-only, aligned with alert schedules; viewing stays open to all users. The REST API is unchanged (own token grants).
* Removing a chat from a folder no longer wipes TTL intervals and schedule/template references from unaffected policies — the policy re-assertion on chat removal is no longer lossy.
* The newDataArrived witness that decides whether a TTL resolution sends its recovery Ok is hardened (queue-order discriminator, atomic expiry stamp) — a stale witness can no longer suppress or double-send the recovery.

## Management API
* REST administration of alerts: data-alert policies and TTL policies on sensors and products (create, update, delete) for non-interactive clients — authenticated with a personal API token and authorized per folder through the token's alerts grants.

## API tokens
* The Profile page becomes "Personal tokens" in the user menu, and the token name is now an optional note (a blank note gets a generated default).
* Migration note: the API tokens section is gone from Settings → Server — the kill switch is a server-configuration-file setting only (`ApiTokens.Disabled`), and API tokens are on by default. A settings save can no longer silently revert a hand-edited kill switch when the configuration reload lags.

## Security
* Removing a product now requires Manager rights on the product (or admin) and a POST with an antiforgery token, and is journaled as the user — previously any signed-in user could remove any product over a GET link.

## Data storage
* Out-of-order values inside a dense batch are stored instead of being silently dropped, and every dropped-value shape is now reported by the add-value result.

## Infrastructure
* The server Docker image declares its own HEALTHCHECK — health no longer depends on this repo's compose file; the compose healthcheck runs every 5 minutes instead of 30 seconds (2 880 anonymous testConnection requests a day → 288).
* docker-compose can ship server logs to VictoriaLogs: a vlagent shipper with a VictoriaLogs single-node, Caddy path exposure and env-gated JSON logging.
* Ready-made Caddy image (`hsmonitoring/hsm-caddy`) with Cloudflare and dynv6 DNS-01 modules — DNS-01 certificates no longer require a self-built Caddy; certificate modes letsencrypt-dns, letsencrypt-http, custom PEM and self-signed.

## Linux probe
* The server ships the pinned Linux probe 0.8.2 `.deb` in the per-product download: Docker Compose metrics (per-container CPU, memory, network, disk write volume), host and per-disk sensors for every mounted disk (free space, inodes, read/write speed, read and write volume per day), Top CPU processes, alert rules in the Rust wrapper, and the `.probe` module node layout under the product; Docker stats and CPU temperature are sampled once a minute.

## Dependencies
* Bundled `HSMDataCollector` 3.5.0 and HsmAgent 0.5.28 (both unchanged since 3.41.5).
