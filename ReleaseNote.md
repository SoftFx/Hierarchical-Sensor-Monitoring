# HSM Server

## Breaking
* **Ports 44330 and 44333 are removed (#1696):** 443 serves everything. Upgrade the image first while the old ports still answer (HSM Agent 0.5.50+ moves itself to 443), point other clients (collectors, probes, Grafana, scripts) at 443, then replace the compose files. A client that cannot move yet: map `'44330:443'` on `caddy` in `docker-compose.override.yml`.
* **Collectors default to the collector session (ADR-0019):** HSMDataCollector 4.0.0-preview, HSM Agent 0.6.0 and Linux probe 0.13.0 use one WebSocket on 443, no HTTP fallback; agents switch when they self-update. Before upgrading: behind a proxy that drops WebSockets set `"transport": "http"` in agent/probe configs (or turn auto-update off); behind your own reverse proxy set `Kestrel__TrustedProxies`, or only 20 collectors connect.

## Collector sessions
* `GET /api/sensors/session`: values, registrations, files, directives on one WebSocket. Max 1 000 sessions, 20 per client address (`CollectorSessions`); opens, closes, refusals logged (#1664).
* Collectors of these versions register a 5-minute `Service alive` alert; before, it never fired by default (#1666).

## Sensor move
* Move sensors in the web UI, `/api/v1`, MCP (dry run): id and history stay, the old path becomes an alias; a move onto an occupied path merges (#1718).
* Collectors can declare previous paths: history follows a layout change.

## Sensor API
* A rejected key gets one 406, not three ERRORs (#1649); a client abort is not an error (#1648).
* Malformed command: 400, server fault: 500; one bad command no longer fails its batch (#1749, #1756).

## Web UI
* Journal: operation type (#1638); bar-chart hover: UTC label (#1647); template preview refills after a blocked Save (#1650).

## Dependencies
* Bundled HsmAgent 0.6.0 (was 0.5.28): session by default, leaves a retired 44330/44333 (#1655).
* Bundled Linux probe 0.13.0 (was 0.10.0): session by default.
* Server self-monitoring: HSMDataCollector 3.5.0 over HTTP.
