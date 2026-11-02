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
* Non-container upgrade note (#1590): the deprecation aliases exist only at the edge (Caddy sites / compose port mappings) — a server run directly (Windows service, `dotnet HSMServer.dll`) has no alias layer. If clients reach such a server on 44330/44333 today, set `Kestrel.Port` to that port **before** upgrading: otherwise the listener moves to the default 443 (on Linux that bind also needs root/`CAP_NET_BIND_SERVICE`) and those clients stop connecting. A server pinned this way keeps serving its old address (bundles bake it, and it is not warned as a straggler).
* Mandatory upgrade step (#1590): replace the compose files (docker-compose.yml — which pins hsm-caddy 2.11.4-6 — and docker-compose.direct.yml) before pulling the new app image. The deprecated aliases keep answering only with the new edge files: the old compose healthcheck probes `https://127.0.0.1:44330` and the old Caddy image dials `app:44330`/`app:44333`, ports the new image no longer listens on, so old edge files plus the new image leave the stack unhealthy (502s, ingestion included). In the other direction, the new compose files probe and dial `app:443`, so they need the new image — deploy the files and the image together.
* Security note (#1590): a firewall setup that exposed only 44330 (ingestion) while keeping 44333 internal no longer isolates the management surface — the 44330 alias reaches the full address (login page, management API, MCP) exactly like 443. Filter at the firewall if you relied on the old split.
* DataCollector default port: the convenience `DataCollector` constructor and `CollectorOptions` now default to 443 (was 44330); code that relies on the default must point at a server that serves 443. Callers compiled against a pre-#1590 package keep the old 44330 value baked into the optional `port` parameter until they are recompiled.
* hsm-caddy 2.11.4-3 passes the client Host header through to the server upstream — redirects no longer break behind the proxy.
* hsm-caddy 2.11.4-6 forwards every site — the primary 443 address and the deprecated-port aliases — to the app's single 443 listener.
* The VictoriaLogs log-storage stack is removed (#1594, ADR-0015): the compose `logs` profile (victorialogs + vlagent), the `HSM_STRUCTURED_LOGS` structured JSON log, and the `/logs/` read routes are gone, and nothing replaces them — the text logs under `Logs/` (with `Logs/Archives/`) remain the source of truth. Migration for existing installs: after upgrading the compose files, stop and remove the two containers (`docker rm -f hsm-victorialogs hsm-vlagent`); both named volumes may be deleted to reclaim disk — data in `victorialogs-data` is lost by design. Delete the structured-log JSON files too: nothing rotates them anymore, so `rm -f Logs/HSM-structured-log-current.json Logs/Archives/HSM-structured-log-*.json` (up to ~6 GiB otherwise left on disk permanently). Leftover `.env` keys (`COMPOSE_PROFILES`, `VL_RETENTION_*`, `HSM_STRUCTURED_LOGS`) are inert and can be dropped.

## Dependencies
* Bundled HSMDataCollector 3.5.0 and HsmAgent 0.5.28 (both unchanged since 3.41.6).
