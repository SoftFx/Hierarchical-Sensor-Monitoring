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
* Ships probe 0.10.0 (from 0.8.2): bar StdDev posting; .NET Core double payload layout; hardened registration retry and recovery log; full process names in top CPU where the 15-byte kernel name was cut, e.g. `valheim_server.x86_64` (#1573); an idle container's `Disk written per hour` posts 0 instead of a gap (#1574).

## Infrastructure
* hsm-caddy 2.11.4-3 passes the client Host header through to the server upstream — redirects no longer break behind the proxy.

## Dependencies
* Bundled HSMDataCollector 3.5.0 and HsmAgent 0.5.28 (both unchanged since 3.41.6).
