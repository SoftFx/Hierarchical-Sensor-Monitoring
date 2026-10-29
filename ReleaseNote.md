# HSM Server

## Alerts
* Alert templates warn at save time about per-policy validation problems — including a path pattern that matches no sensors, or a condition the matched sensor types do not offer; template path-pattern semantics are documented.

## Sensor history
* Bar standard deviation is stored, compressed and served: the history table shows a σ column and the CSV export appends StdDev (timeout rows excluded).

## MCP
* Write tools for sensor policies, alert templates and alert schedules; read tools for chats and folders (list/get).
* Tool schemas now carry field-value documentation, output schemas via structured content, and the documented alert-variable and property × sensor-type matrices.

## Management API
* New REST surface: read-only /api/v1/chats and /api/v1/folders, and CRUD on /api/v1/alertSchedules — one read envelope (ManagementReadResult) and a tightened error contract with typed Details.

## UI
* New admin Downloads page with the Windows agent and Linux probe bundles and per-product install commands.
* Personal tokens: fresh tokens point at the management API and MCP; Esc closes the token dialogs; the disabled-tokens banner is informational.

## Self-monitoring
* The server reports its own database backups: result (with TTL so a missed run times out), zip sizes and duration on the Backup node, plus daily environment/dashboards database sizes.

## Linux probe
* Ships probe 0.9.1 (from 0.8.2): top disk I/O processes via the hsm-linux-probe-io root helper enabled by install.sh; bar StdDev posting; .NET Core double payload layout; hardened registration retry and recovery log.

## Infrastructure
* One port: everything (web UI, Sensor API, management API, MCP, downloads) is served on the standard port 443; the non-standard ports 44330/44333 keep answering as deprecated aliases through the deprecation window and will be removed in a follow-up release (#1590). The server listens on a single Kestrel port (`Kestrel.Port`, default 443 — old `SensorPort`/`SitePort` settings are ignored with a startup warning); DataCollector/agent/ping-module defaults are now 443; requests that arrive by a deprecated port log a rate-limited warning so operators can find clients to repoint. Direct deployments publish 443 (plus the deprecated aliases through the window).
* hsm-caddy 2.11.4-5 forwards every site — the primary 443 address and the deprecated-port aliases — to the app's single 443 listener.
* The VictoriaLogs read path moved to https://<HSM_DOMAIN>/logs/ and is admitted only for logged-in HSM admins (Caddy forward_auth against the app): the separate VL_UI_* basic-auth credential is gone, and the log routes ship enabled with the 'logs' compose profile.

## Dependencies
* Bundled HSMDataCollector 3.5.0 and HsmAgent 0.5.28 (both unchanged since 3.41.6).
