# HSM Server

## Breaking
* **Ports 44330 and 44333 are removed (#1696):** 443 serves everything. Upgrade the image first while the old ports still answer (HSM Agent 0.5.50+ moves itself to 443), point the other clients at 443, then replace the compose files.
* **Collectors default to the collector session (ADR-0019):** HSMDataCollector 4.0.0-preview, HSM Agent 0.6.0 and Linux probe 0.13.0 talk over one WebSocket on 443 and need this server or later. For an older server or a proxy without WebSockets set the transport to `http`.

## Collector sessions
* New endpoint `GET /api/sensors/session`: values, registrations, files and directives on one WebSocket, each frame acknowledged. At most 1 000 sessions per server and 20 per client address (`CollectorSessions`); opens, closes and refusals are logged (#1664, #1668, #1660).
* `Service alive` alerts after 5 minutes off, so a restart or an update pages nobody (#1666).

## Sensor move
* Move sensors from the web UI, `/api/v1` and MCP (with a dry-run preview): a moved sensor keeps its id and history, its old path becomes an alias; a move onto an occupied path merges the two (#1718-#1724).
* Collectors can declare previous paths so history follows a layout change.

## Sensor API
* A rejected access key gets one 406 instead of three ERROR entries (#1649); a client that aborts its request is not logged as an error (#1648).
* A malformed command answers 400, a server fault 500, and one faulty command no longer fails the rest of its batch (#1749, #1756).

## Web UI
* The journal shows the operation type (#1638); bar-chart hover dates carry a UTC label (#1647); the template preview of affected sensors refills after a blocked Save (#1650).

## Dependencies
* Bundled HsmAgent 0.6.0 (was 0.5.28): session by default, moves itself off a retired 44330/44333 (#1655).
