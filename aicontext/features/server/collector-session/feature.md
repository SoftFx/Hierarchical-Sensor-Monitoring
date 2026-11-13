# Feature: Collector Session (server side)

> Owner: server | Last reviewed: 2026-11-10 | Canonical: yes
> Scope: server - the WebSocket session a collector may hold with the server: the endpoint, its limits and endings, the session registry and its self-monitoring

---

## Overview

A collector may hold one persistent WebSocket with the server at `GET /api/sensors/session` and send its value batches and sensor registrations as frames on it. The server answers each frame with an ack that carries the HTTP status the same request would have got on the matching HTTP route. The frame format is part of the Sensor API wire contract: `aicontext/features/api/wire-contract/feature.md` § Collector session.

This is step 2 of the collector session (#1664, epic #1660, ADR-0017). **No collector uses it yet.** Directive push over the session is a later step.

**The session is a transport a collector can be set to; the HTTP Sensor API stays beside it** (owner decisions: 2026-11-10, no fallback between the two; 2026-11-11, the HTTP routes are not retired, because script clients such as PowerShell use them and a WebSocket is inconvenient there). The HTTP Sensor API is unchanged. Nothing in the session depends on it: a collector with a session does not fall back to HTTP, is not told about sessions through HTTP answers, and sends nothing over HTTP on the side. Three things a collector on a session cannot do yet: send file values, send an element larger than a frame, and receive agent directives, which still reach a collector only as headers on HTTP answers until directive push is built.

Which transport a collector uses is a setting of the collector (`http` or `session`), not something the server negotiates.

## Invariants

- **One ingestion path.** The `/list` and `/commands` routes and the session call the same `SensorIngestionService`, and read their bodies with the same serializer options (`SensorApiJson`). A batch is stored and counted the same way whichever transport carried it.
- **The ack status is the status of the HTTP route**, with one deliberate exception: a `list` body the server cannot parse is `400 malformed body` on the session, where the HTTP route ends as a `500` (its model binder throws). A `500` tells a collector to retry a body that can never be accepted. A `commands` body the server cannot parse is `400` on both since #1749.
- **A fault of the server while it applies a `commands` request is a `500` ack** (`failed to apply the commands`), as on the HTTP route (#1749; both answered `400` before). A collector takes a 4xx on a registration as a final refusal and drops the package; it sends a 5xx again on its next cycle. The `500` means "some commands faulted, the others were applied; send the frame again": a fault on one command does not end the batch, so a command that faults on every repeat cannot keep the commands after it from being applied. The handler logs one `Error` line for the frame (`… frame 12: 2 of 3 command(s) faulted, the others were applied. The first fault: …`), not a line per command. A registration the server refuses (it cannot convert it) is not a fault: its reason is in the body of a `200` ack under its path, and the rest of the batch is applied. The statuses of the registration routes and what they do not cover: `aicontext/features/api/wire-contract/feature.md` § Statuses of the registration routes.
- **A failure ack never echoes the request.** The HTTP `400` of `/list` does. Nor does it carry the per-path errors of the refused commands of a batch in which another command faulted, which the HTTP `500` of `/commands` does.
- **Refusals before the upgrade are ordinary HTTP statuses**; nothing is upgraded just to be closed.
- **Access is re-checked for every request frame and every ping**, as it is for every HTTP request: the key and its product are looked up again and the key must still be valid for `CanSendSensorData`. Losing it closes the session with `1008`, also for a session that only keeps itself alive. A request frame counts as a use of the key (last-used time and address), a ping does not. There is no `406` ack: a key that cannot send does not get a session in the first place.
- **A frame the server cannot acknowledge is refused, not dropped**: a request without an `id` closes the session with `1002`. Dropping it would be silent data loss.
- **One address cannot take every slot** (owner decision, 2026-11-10). Besides the server-wide cap there is a connection limit per client address, 20 by default: a host whose collector opens sessions without closing them is refused with `429` at its own limit and leaves the rest of the server alone.
- **The cap holds under a race.** A slot is reserved before the upgrade is accepted and released when the session ends, whatever way it ends.
- **A refusal that stops delivery is visible.** Besides the cap below, an upgrade that arrives while sessions are switched off is logged as a `Warn`, at most once a minute: a collector set to use a session cannot send to such a server at all.
- **Reaching the cap is visible.** A refused collector cannot send until a slot is free, so the server logs a `Warn` naming the limit, at most once a minute. The cap is a guard against a runaway fleet, not a number to run close to.
- **A collector going away is not a server error.** Only a failure inside the server is an `Error`. Every session is logged when it opens and when it ends (see Logging below); the end is `Info` when either side closed it in an orderly way (1000, 1001, or a close without a code) and `Warn` otherwise (a dropped connection, silence, a protocol violation, lost access, an internal error).
- **Silence is the collector's, never the server's.** The three-heartbeat limit runs while the server waits for a frame or for the collector to take one, and starts again with every read that brought bytes, so a large frame on a slow link is not silence. A frame still has to be complete within ten silence limits of its first byte (15 minutes by default): a frame that never ends would otherwise hold a slot and never reach the access check. The limit does not run while the server itself is storing a batch.
- **Shutdown does not wait for collectors, and does not lose an ack.** On `ApplicationStopping` every session gets a close frame (`1001`); a peer that has not answered in 3 seconds is cut off. New upgrades are refused with `503` from that moment. A session that is storing a batch at that moment sends the ack first and the close after it, so a stored batch is not sent again, provided the store finishes within those 3 seconds; past them the session is cut off like any other and the batch may arrive twice (delivery is at-least-once). A frame that arrives after the close went out is neither stored nor answered.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Open a session (upgrade, `hello`) | collector |
| 2 | Send a batch or registrations and get an ack | collector |
| 3 | Heartbeat (`ping` / `pong`), silence detection | collector / server |
| 4 | End a session: close from either side, violation, lost access, silence, shutdown | collector / server |
| 5 | See how many sessions are open | operator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `GET /api/sensors/session`, subprotocol `hsm.sensors.v1` | `Controllers/SensorsController.cs` (`Session`) | Not in the OpenAPI document: it is not an HTTP operation. The route accepts `GET` (the upgrade over HTTP/1.1) and `CONNECT` (a WebSocket over HTTP/2); the second is not tested. |
| Frames | `SensorApi/Sessions/SessionProtocol.cs` | The wire-contract doc is the contract. |
| Settings section `CollectorSessions` | `ServerConfiguration/Sections/CollectorSessionsConfig.cs` | Read at startup. |

### Refusals of the upgrade

The access key is resolved first, by the same middleware as for every Sensor API request, so an unknown or malformed key is `406` whatever else is true, also when sessions are off.

| Condition | Status |
|---|---|
| Sessions are off (`MaxSessions = 0`) | 404 |
| A `GET` that is not a WebSocket upgrade | 400 |
| Any method but `GET` and `CONNECT` | 405 |
| No `Key` header, unknown or malformed key, key blocked, expired or without `CanSendSensorData` | 406 (the Sensor API's access error) |
| `Sec-WebSocket-Protocol` does not offer `hsm.sensors.v1` | 400 |
| The address already holds `MaxSessionsPerAddress` sessions | 429, with `Retry-After` |
| The session cap is reached | 503, with `Retry-After` |
| The server is shutting down | 503 |

### Ways a session ends

| Cause | Close code | Initiator |
|---|---|---|
| The collector closes | the collector's | collector |
| A request frame without an `id` | 1002 | server |
| A binary frame | 1003 | server |
| A frame that is not a JSON object | 1007 | server |
| The key was removed, blocked, expired or lost `CanSendSensorData`; its product was removed | 1008 | server |
| A frame larger than `MaxFrameBytes` | 1009 | server |
| Server shutdown | 1001 | server |
| No inbound frame for three heartbeats, or the collector does not take a frame the server sends for as long | none, the connection is dropped | server |
| The connection is lost | none | network |

### Settings (`CollectorSessions`)

| Key | Default | Meaning |
|---|---|---|
| `MaxSessions` | 1000 | Server-wide cap on open sessions. One session is one collector process. `0` turns sessions off: the endpoint answers 404. A collector over the cap cannot send, so set it well above the number of collectors. |
| `MaxSessionsPerAddress` | 20 | Connection limit per client address; an IPv6 client is counted by its /64. Checked before the server-wide cap. `0` means no limit per address. Behind a reverse proxy the address is the client's only when `Kestrel.TrustedProxies` names the proxy; otherwise all collectors share the proxy's address and this limit. |
| `HeartbeatSeconds` | 30 | Announced in `hello`. The server drops a session after 3 × this without any inbound frame. Also the WebSocket keep-alive interval. 5..3600. |
| `MaxFrameBytes` | 4194304 | Announced in `hello`. A larger frame closes the session with `1009`. 65536..67108864. |

Startup validation names the key and the allowed range. The section is written into the settings file on the first start, like every other section.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/SensorApi/SensorIngestionService.cs` | What the Sensor API does with values and registrations once the caller is known; shared by HTTP and the session |
| `src/server/HSMServer/SensorApi/SensorApiJson.cs` | The serializer options of `/list` and `/commands` bodies, shared by the model binders and the session |
| `src/server/HSMServer/SensorApi/Sessions/CollectorSessionHandler.cs` | The upgrade checks and the frame loop |
| `src/server/HSMServer/SensorApi/Sessions/CollectorSession.cs` | One WebSocket: bounded receive, serialized sends, the two ways to close |
| `src/server/HSMServer/SensorApi/Sessions/CollectorSessionRegistry.cs` | Live sessions, the cap, the close-all at shutdown |
| `src/server/HSMServer/SensorApi/Sessions/SessionProtocol.cs` | Names, close codes and frame writers of version 1 |
| `src/server/HSMServer/Middleware/Telemetry/ISensorApiTelemetry.cs` | The `Clients/...` self-monitoring counters behind an interface |
| `src/tests/HSMServer.Core.Tests/SensorApi/*` | The tests and their host |

## Data Flow

```
upgrade request --> TelemetryMiddleware (Key -> product, key, client)  --> SensorsController.Session
                --> CollectorSessionHandler: checks, slot, accept, hello
frame           --> parse envelope --> re-check access --> SensorIngestionService --> ITreeValuesCache
                <-- ack {ack, status, body | error}
```

The handler loop is the only reader of a session's socket. Sends are serialized per session, so an ack and the close at shutdown cannot interleave.

A frame is read into a 4 KiB buffer the session owns; a larger frame borrows from the array pool only while it is being handled. The size limit is enforced as the bytes arrive, so an oversized frame is never buffered beyond one byte past the limit.

## Storage / Persistence

None. The registry lives in memory and is empty after a restart; collectors reconnect.

## Logging

NLog, logger `CollectorSessionHandler`. The key's display name is logged, never its value (the value is the key). Text the client supplies (`ClientName`, `X-Collector-Kind`/`-Version`, `X-Agent-Version`, its close reason) has control characters replaced by `?` and is cut to 100 characters (a close reason to 123).

- Opened, `Info`: `Collector session 20e46170 opened: product "TestProduct", key "DOM664hl", client "session-tests", collector native 0.12.0, from 127.0.0.1; 1 session(s) open.` The id is the first eight hex digits of the session id; the close line repeats it. The address is the one the server resolved (behind the proxy, the client's only when `Kestrel.TrustedProxies` names the proxy).
- Closed, `Info` or `Warn`: `Collector session 20e46170 closed after 168 ms: the collector closed it with 1000, reason "done". Product "TestProduct", client "session-tests", from 127.0.0.1; frames in 3 (172 bytes), out 3 (145 bytes); values accepted 1, requests refused 1; last frame from the collector 0 ms before the end; 0 session(s) open.` Who ended it and why, the first cause noted (what follows it is its consequence):
  - `the collector closed it with <code>, reason "..."` (or `no reason`);
  - `the server closed it with <code>, reason "..."`: 1001 at shutdown, 1002/1003/1007/1009 for a protocol violation (the reason names it, e.g. `a frame exceeds N bytes`), 1008 when the key was revoked or blocked;
  - `the server cut the connection without a close handshake: <reason>`: `no progress within three heartbeats` (silence, or a frame that exceeded its budget), `server is shutting down` after the shutdown budget, `internal error (<exception type>)`;
  - `the connection ended without a close handshake (<exception type>: <message>)`: the collector, or something between it and the server, dropped the connection.
  `frames` count complete messages (a close frame included on the way in, the hello and acks on the way out); `values accepted` are the values of `list` frames whose path was not refused; `requests refused` are acks with a status other than 200.
- Refused upgrades, at most one line a minute per kind (bad upgrade 400, access 406, shutdown 503), with the number of refusals of that kind that went unlogged since the previous line: `Warn A collector session upgrade is refused with 406: the key "DOM664hl" cannot send data: AccessKey doesn't have CanSendSensorData. Client "session-tests" from 127.0.0.1.` The cap (503) and the per-address limit (429) keep their own lines. A key the server does not know (or a malformed one) is refused with 406 by the Sensor API middleware before the handler, as on every HTTP route, and is not logged there. The throttle is per kind for the whole server, not per collector: while one collector is refused every second, another collector refused for a different reason of the same kind is only counted, not described. A close the server announced as orderly and could not complete (the handshake failed or timed out and the connection was cut) is reported with both facts and as `Warn`.

## UI / Operator Visibility

- Self-monitoring sensor `Clients/_Total/Open sessions` in the server's own product: the number of open sessions, sampled with the other server statistics (every 5 minutes).
- Session traffic is counted in the existing `Clients/...` sensors: a request frame is one client request with its size, an ack is traffic out, stored values are sensor updates. A collector that moves from HTTP to a session keeps its numbers.
- `CollectorSessionRegistry.GetSessions()` lists the open sessions (product, key, client name, collector kind and version, agent version, remote address, connected-at, last-frame-at). Nothing shows it in the UI yet.

## Cost

An idle session holds about **22 KiB of managed memory** on the server (measured with 400 sessions opened over raw sockets, `IdleSession_CostsTensOfKilobytes`; stable to 0.1 KiB across runs). At the default cap of 1 000 sessions that is about 22 MB; every further 1 000 costs about as much. Kernel socket buffers come on top and are not measured.

## Dependencies

- Depends on: the Sensor API authentication (`TelemetryCollector`), `ITreeValuesCache`, the server's self-monitoring collector.
- Used by: nothing yet. Planned: the managed and the native collector (epic #1660, steps 3 and 4), directive push (step 5).

## Tests

`src/tests/HSMServer.Core.Tests/SensorApi/CollectorSessionTests.cs`: a real `ClientWebSocket` and `HttpClient` against the real Sensor API (middleware, controller, ingestion service, session handler) on a real Kestrel listener over the real cache. Covered: the handshake access matrix, the required subprotocol, sessions switched off, ack status and body equal to the HTTP route for the same body (`list` and `commands`, accepted and partly refused, a registration the server cannot convert, a `commands` body it cannot read, a fault on one command of a `commands` batch with the commands around it applied and the resend after it, several faults in one frame logged once; the last two through `FaultingCache`, which makes the registration of the paths a test names throw), failure acks without echo, unknown op and unknown envelope fields, ping, the five violations with their close codes, the session cap and the limit per address with slot release, the silence limit (also with a frame arriving in pieces), lost access (on a data frame and on a ping), registry contents, shutdown (idle, and with a batch inside the store), a vanished collector, traffic counting, and the HTTP routes after the extraction of the ingestion service. The `Log_*` tests pin the log lines (opened and closed by the collector with its code and a printable reason, silence, an aborted connection, a refused upgrade throttled to one line, no key value) through `SensorApiTestHost.Log`: the host gives the handler a logger from its own `LogFactory` (`CollectorSessionHandler.UseLogger`), so the process-wide NLog configuration is untouched and the tests still run in parallel.

The silence limit runs on a `TimeProvider`. The tests replace it with a manual clock, so none of them waits for real seconds: they assert that the watch is running, that a frame starts it again, and what happens when it fires.

The test host runs only the part of the pipeline the Sensor API passes through. The full pipeline (HTTPS, the guards, cookie and token authentication in front of the endpoint) was checked by hand against a locally started server when the feature was written: upgrade, `hello`, a `list` and a `commands` frame with their acks, ping, the two refusals, and an empty error log.

Not covered at all: the bundled reverse proxy in front of the endpoint, a WebSocket over HTTP/2, a collector that stops reading (the send is bounded by the silence limit, untested), the ten-limit budget of a frame that never ends, and behaviour at thousands of sessions.

## Known Issues / Limitations

- **The limit per address is per address, not per collector.** Hosts behind one NAT share it, and so do all collectors behind a reverse proxy that the server does not trust (`Kestrel.TrustedProxies`): there the limit of 20 would cap the whole server at 20 sessions. The server names the address in its `Warn` when the limit is hit, at most once a minute for each address. Many addresses holding one key can still fill the server-wide cap together.
- The registry is not shown anywhere in the UI.
- `Open sessions` has no default alert for approaching the cap; the `Warn` above and the sensor's value are the signals.
- A connection that dies without a close keeps its slot until the silence limit (90 seconds by default). After a network event that drops many connections at once, collectors that reconnect within that time can be refused while the cap is not really reached; they keep their data and try again.
- `Open sessions` is a 5-minute sample, not a live gauge.
- `HeartbeatSeconds`, `MaxFrameBytes`, `MaxSessions` and `MaxSessionsPerAddress` are read at startup; a change needs a restart.
- A `MaxSessionsPerAddress` that is not below `MaxSessions` limits nothing, and nothing says so at startup.
