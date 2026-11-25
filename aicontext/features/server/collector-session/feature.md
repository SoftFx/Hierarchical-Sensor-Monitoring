# Feature: Collector Session (server side)

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: server - the WebSocket session a collector may hold with the server: the endpoint, its limits and endings, directive push, the session registry and its self-monitoring; the table connection (#1845) and the file connection (#1884) of a session

---

## Overview

A collector may hold one persistent WebSocket with the server at `GET /api/sensors/session` and send its value batches and sensor registrations as frames on it. The server answers each frame with an ack that carries the HTTP status the same request would have got on the matching HTTP route. The frame format is part of the Sensor API wire contract: `aicontext/features/api/wire-contract/feature.md` § Collector session.

Built in step 2 of the collector session (#1664, epic #1660, ADR-0017); directive push came with step 5. Both collectors can use it (native since 0.12.0, managed since 3.9.0); it is their default transport since ADR-0019 (managed 4.0.0, agent 0.6.0, probe 0.13.0).

**The session is a transport a collector can be set to; the HTTP Sensor API stays beside it** (owner decisions: 2026-11-10, no fallback between the two; 2026-11-11, the HTTP routes are not retired, because script clients such as PowerShell use them and a WebSocket is inconvenient there). The HTTP Sensor API is unchanged. Nothing in the session depends on it: a collector with a session does not fall back to HTTP, is not told about sessions through HTTP answers, and sends nothing over HTTP on the side. A file value travels as an element of a `list` frame (#1660 step 2b; the server needed no change for it: the `list` op reads the polymorphic batch of `/list`, which takes a file value, and stores it as `/file` would). One thing a collector on a session cannot do: send an element larger than a frame (a file of more than about 1 MiB at the default `MaxFrameBytes`). Agent directives are pushed on the session (#1660 step 5, § Directive push below).

Which transport a collector uses is a setting of the collector (`http` or `session`), not something the server negotiates.

## Invariants

- **One ingestion path.** The `/list` and `/commands` routes and the session call the same `SensorIngestionService`, and read their bodies with the same serializer options (`SensorApiJson`). A batch is stored and counted the same way whichever transport carried it.
- **The ack status is the status of the HTTP route**, with one deliberate exception: a `list` body the server cannot parse is `400 malformed body` on the session, where the HTTP route ends as a `500` (its model binder throws). A `500` tells a collector to retry a body that can never be accepted. A `commands` body the server cannot parse is `400` on both since #1749, and so is a `commands` list with a `null` element or with a command without a `Path`: `400 malformed body`, refused as a whole before anything is applied (the HTTP route answers `400` with a text that names the command).
- **A fault of the server while it applies a `commands` request is a `500` ack** (`failed to apply the commands`), as on the HTTP route (#1749; both answered `400` before). Since #1756 that includes a fault of the sensor cache and a failed write to the store while a registration is applied, which used to be answered as applied. A collector takes a 4xx on a registration as a final refusal and drops the package; it sends a 5xx again on its next cycle. The `500` means "some commands faulted, the others were applied; send the frame again": a fault on one command does not end the batch, so a command that faults on every repeat cannot keep the commands after it from being applied. The handler logs one `Error` line for the frame (`… frame 12: 2 of 3 command(s) faulted, the others were applied. The first fault: …`), not a line per command, and for a fault that repeats on every resend one line per 5 minutes for the same fault of the same key, with the number of requests that went unlogged in between (`RepeatedFaultLog`; the decision is recorded in the wire-contract doc). Only this line is bounded: the sensor cache and the update queue write their own `Error` lines for a faulted registration on every resend. A registration the server refuses (it cannot convert it) is not a fault: its reason is in the body of a `200` ack under its path, and the rest of the batch is applied. The statuses of the registration routes and what they do not cover: `aicontext/features/api/wire-contract/feature.md` § Statuses of the registration routes.
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
| 6 | Push the directives of an agent | server |
| 7 | Open a table connection of a session and send blocks on it (#1845) | collector |
| 8 | Open a file connection of a session and upload a large file on it (#1884) | collector |

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
| `MaxTableFrameBytes` | 4194304 | The largest binary frame of a table connection (#1845), announced in its hello. A real tick of a thousand tables is 25–50 KB. A larger frame closes the table connection with `1009`. 65536..67108864, or `0`: **table connections off** (their upgrade answers 404; sessions go on, reads of stored rows too). A way to stop table ingest without stopping the collectors. |
| `MaxTableBytesInFlight` | 268435456 | What all table connections together may hold for frames being received and waiting for their put (#1845). A frame within 64 KiB costs nothing; a larger one first reserves twice `MaxTableFrameBytes` plus one byte (its buffer and the put's copy) and holds it until its put is answered. When the budget is spent the connection stops reading (back-pressure through TCP; the silence limit does not run meanwhile; a `Warn` at most once a minute); a wait longer than twice the silence limit closes it with `1013`. At least one reservation (8 MiB + 1 at the default frame limit), at most 64 GiB. **Limit: one reservation per access key.** An access key (commonly one product key shared by many collector installations) holds at most one reservation, from the first byte past 64 KiB of a frame until its put is answered. N collectors of one key that each send a frame larger than 64 KiB at the same time are served one after another, and the last waits (N − 1) × the time one frame holds the reservation (its transfer past 64 KiB plus its put; at most one silence limit, 90 s, plus the put). When that passes the wait bound, twice the silence limit (180 s by default), the tail is closed with `1013`, reconnects and queues again: the bound is passed beyond about 91 collectors of one key at 2 s a frame, beyond 3 at the 90 s worst case. Frames up to 64 KiB take no reservation and are not affected, nor are collectors with keys of their own. A `1013` that had room in the budget and waited only for its own key is logged as a `Warn` ("held back only by its own access key", at most once a minute). Deliberately not redesigned for now (#1845 review): the scoping is the owner's decision. |
| `MaxFileFrameBytes` | 4194304 | The largest binary frame of a file connection (#1884): one piece of a large file with its 88-byte header, announced in its hello. The piece size the hello announces follows from it: the largest multiple of 4 KiB that fits the frame with the header and with the most zstd can add to bytes that do not compress (4,173,824 at the default). A larger frame closes the file connection with `1009`. 131072..67108864, or `0`: **file connections off** (their upgrade answers 404; sessions go on). There is no budget beside it: the memory of file frames is bounded by `LargeFiles.MaxUploadsInProgress` (see Cost). |
| `HeartbeatSeconds` | 30 | Announced in `hello`. The server drops a session after 3 × this without any inbound frame. Also the WebSocket keep-alive interval. 5..3600. |
| `MaxFrameBytes` | 4194304 | Announced in `hello`. A larger frame closes the session with `1009`. 65536..67108864. It is also the largest file a collector can send on a session: a byte of a file is up to four bytes of JSON, so the default carries a file of about 1 MiB, and the 10 MiB the collectors accept by default need about 40 MiB. The collectors drop a larger file and say so; they do not send it. |

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
| `src/server/HSMServer/SensorApi/Sessions/DirectivePush.cs` | The directive push of one session: whole set after the hello, changes every 15 s, whole set every 5 min |
| `src/server/HSMServer/SensorApi/AgentDirectives.cs` | The directive set of an agent, shared with the `X-Hsm-Directive` headers of the HTTP routes; the 15 s cache of the staged agent version |
| `src/server/HSMServer/Middleware/Telemetry/ISensorApiTelemetry.cs` | The `Clients/...` self-monitoring counters behind an interface |
| `src/server/HSMServer/SensorApi/Sessions/TableFrame.cs` | The binary frame header of a table connection |
| `src/server/HSMServer/SensorApi/Sessions/Files/FileConnection.cs` | The uploads of one file connection: begin, piece, end, the lease, the end check beside the loop |
| `src/server/HSMServer/SensorApi/Sessions/Files/FilePiece.cs` | The piece header, the piece size of a frame, the zstd checks at arrival, the bounded decompression |
| `src/server/HSMServer/SensorApi/Sessions/Files/FilePieceChecker.cs` | The end check of an upload |
| `src/server/HSMServer/SensorApi/Sessions/Files/IFileUploadStore.cs` | The seam to the store of large files (#1883) |
| `src/server/HSMServer/SensorApi/Sessions/Files/FileFrameMemory.cs`, `FileChecks.cs` | The receive buffers of a file connection; the running end checks |
| `src/tests/HSMServer.Core.Tests/SensorApi/*` | The tests and their host |

## Directive push (#1660 step 5)

A session whose upgrade stated `X-Agent-Version` gets the agent directives (#1198) as pushes, `{"op":"directive","body":"<one X-Hsm-Directive line>"}`; any other session gets none, as an HTTP client without that header gets no header.

- **The set** is `AgentDirectives.For(agentVersion, product)`, the same call the HTTP routes make for their headers (it was `SensorsController.EmitAgentDirectives`): `update-available:<staged>` when `Agent.AutoUpdateEnabled` is on and `wwwroot/agent/version.txt` names a newer agent than the stated one, then `sensor-enable:` or `sensor-disable:` for `computer`, `system`, `disk`, `network`, `module`, `process`, in that order. The staged version is read through a 15 s cache (one per server; it used to be a static of the controller). The key and its product are looked up afresh for every computation.
- **When** (`DirectivePush`): the whole set right after the `hello`; then every 15 s (`CheckPeriod`, the age of the staged-version cache) the set is computed again and every directive that was not in the previous set is pushed, in order; every 5 minutes (`ReannouncePeriod`) the whole set again, which is what retries a failed agent update. Both periods are timers of the server's `TimeProvider`.
- **How**: a push is a frame like an ack and goes through the session's serialized send. Nothing is pushed once the server is closing the session (`CollectorSession.IsClosing`, shutdown); a push that fails because the session is ending ends the push loop quietly, and the handler loop reports the session's end as always. A push that fails while the session is up does not end the loop: it is logged as an `Error`, at most one line a minute per session with the number of failures in between, the rest of that round waits, and the next check pushes everything the collector was not told yet, in order. What the collector was told is only what was actually sent: a directive whose send failed is not in it. A failure to look the key up skips that round. A round that pushed something logs one `Debug` line.
- **Cost**: two timers and one waiting task per directive-aware session; nothing for any other session.

## Table connection (#1845)

A collector with table sensors opens a second WebSocket on the same route, subprotocol `hsm.tables.v1`, naming its live session in `X-Hsm-Session` (the `sessionId` of the session's hello). The wire: `aicontext/features/api/wire-contract/feature.md`, "Table connection". ADR-0021 point 7.

- **Upgrade** (`CollectorSessionHandler.HandlePairedUpgradeAsync`, shared with the file connection): the key and the subprotocol as for a session; `404` while `MaxTableFrameBytes = 0` (a `Warn` at most once a minute: that collector cannot deliver blocks); `409` when `X-Hsm-Session` is missing, malformed or names no live session of the same key (`CollectorSessionRegistry.FindSession`; logged as a refused upgrade, throttled like the others); `503` while shutting down.
- **The pair** (`CollectorSessionRegistry.TryAttachPaired`, `DetachPaired`, `Release`): no slot is taken, so `MaxSessions` and `MaxSessionsPerAddress` count collectors and the server holds at most three times `MaxSessions` sockets (a session, its table connection and its file connection, #1884). A session holds at most one table connection; a new one replaces the old, which is closed with `1000` (`replaced by a new table connection of its session`). When the session ends (`Release`), its table connection is closed with `1000` (`its session ended`). Both closes go through `SignalCloseAsync`, so a block being stored is acknowledged first. The pair map changes under one lock, and a session leaves the registry before its pair is looked at: a table connection is never attached to a session that has ended (it is then closed with `its session ended` right after its upgrade).
- **The loop** is the session's (`RunAsync`): the hello (`WriteTableHello`: the frame limit, the heartbeat, the block limits of section `TableSensors`), the silence limit, access re-checked for every frame (a probe, like a ping, is not a use of the key), the deferred close. Text requests are answered `400 unknown op`; ping is answered. A binary frame is read by `TableFrame.TryRead`: a header it cannot trust closes with `1002`; a probe is answered `200`; a block goes to `HandleBlockAsync`.
- **A block** (`HandleBlockAsync`): an empty producer id or a negative sequence is `400`; then the bytes are copied out of the session's buffer, a turn of `TableSensors.MaxConcurrentPuts` (default 4, a server-wide `SemaphoreSlim`) is taken, and `ITableBlockService.Put(root product of the key, producer, sequence, block)` runs on the thread pool. `Stored`, `Duplicate` and `AllRefused` are `200` acks with the outcome and the refused tables (`WriteBlockAck`; a duplicate whose refusals are not known, `TableBlockPutResult.RefusalsKnown = false`, carries `"refusedKnown":false` instead of the list); `Invalid` is `400 invalid block: <code>`; `TableBlockStoreException` is `503` with `retryAfter` 60 (the store's backoff), never a refusal; any other exception is logged as an `Error` and answered `500`.
- **Back-pressure**: one block in flight per connection (the collector waits for the ack), and a block waiting for a turn of the put semaphore is simply not answered yet. The silence limit does not run while a frame is handled.
- **Two collectors with one producer id** (`CollectorSessionRegistry.NoteProducer`): each table connection is remembered for the (product, producer id) of its last block the store took (`stored` or `duplicate`; a frame that is not a block names no producer), one entry per live connection, so the map is bounded by the connections. A block of a pair another live table connection holds logs a `Warn` naming both connections, **at most once a minute for the whole server** with the count of such blocks since the last line (the producer id is the client's: a throttle per pair would let ever new ids write a line each), and is not refused. A connection of the same session (the one a reconnect replaces) and one that is closing do not count.
- **A close asked for while a block is being stored** waits for its ack; the first one asked for stands, so at shutdown a table connection closes with `1001` even when its session ends under it.
- **Log lines**: `Collector table connection 5d1c09aa of session 20e46170 opened: …; 1 table connection(s) open.` and the close line with `blocks stored, duplicate, all refused, invalid, faults; tables refused`, `Info` or `Warn` by the same rule as a session.

## File connection (#1884)

A collector sends a large file (ADR-0022) on a third WebSocket of the same route, subprotocol `hsm.files.v1`, naming its live session in `X-Hsm-Session`. The wire: `aicontext/features/api/wire-contract/feature.md`, "File connection". The store of large files is #1883's; the connection reaches it through `IFileUploadStore` (`SensorApi/Sessions/Files/IFileUploadStore.cs`), shaped one to one like the store's uploads (`ILargeFileUploads`). **Until an adapter to the store is registered, file connections are off** (`404`, the handler takes the store as an optional dependency); the tests run against `FakeFileUploadStore`.

- **Upgrade** (`HandlePairedUpgradeAsync`, shared with the table connection): `400` when both paired subprotocols are offered; `404` while `MaxFileFrameBytes = 0` or no store is registered (a `Warn` at most once a minute: that collector cannot deliver its files); `409` without a live session of the same key; `503` while shutting down. No slot; one per session; a new one replaces the old, and **the old one's loop ends, its upload given back, before the new one's hello**: the replacer waits for it (`CollectorSession.Ended`), cuts it off if the collector does not answer the close within 3 s, and waits again (at most 3 s more, then a `Warn`). Closed with its session.
- **The loop** is the session's: the hello (`WriteFileHello`: the frame limit, the heartbeat, `maxFileBytes` from the store, `pieceBytes` = `FilePiece.PieceBytesFor(MaxFileFrameBytes)`), the silence limit, access re-checked for every frame (a piece is a use of the key, a probe and a ping are not), the deferred close. Text requests: `begin` and `end` go to `FileConnection`; any other op is `400 unknown op`. Binary frames: `FilePiece.TryRead` (a header it cannot trust closes with `1002`), a probe is answered `200`, a piece goes to `FileConnection.HandlePieceAsync`.
- **One upload per connection** (`FileConnection`): the store's lease of the upload the connection holds. It goes back on a new `begin` (before the store is asked), on a discard, when the connection ends (`FileConnection.End` in the upgrade's `finally`), and after the end check, which takes the lease over from the connection and calls `Release` after `FinishAsync` whatever its outcome. The fields of `begin` are checked before the store (name 1..1,024, `source` ..256, size ≥ 0, `modified` with `Z` or an offset, `pieceBytes` 65,536..the hello's): `400`. An `ArgumentException` of the store is `400`, a `FileUploadStoreException` `503` with `retryAfter` 60.
- **A piece** is checked without decompressing it, in the order of the contract (upload id, number, lengths, CRC-32, the zstd frame through ZstdSharp's `ZSTD_getFrameHeader` and `ZSTD_findFrameCompressedSize`), then appended on the thread pool (the store makes a synced write). A size rule broken: `DiscardAsync` and `400`. A wrong CRC: `409` with `next`, nothing discarded. A fault of the store: `503` with `retryAfter` 60, nothing acknowledged.
- **`end`** starts the end check (`FilePieceChecker`, one per check) on `FileChecks` and answers `202` with `retryAfter` 5; a repeated `end` is answered from the check the connection started (`202` while it runs, then its outcome), until a new `begin` of that file. The checker reads every stored piece, checks its frame, decompresses it into a pooled buffer of exactly its length with `ZSTD_d_windowLogMax` set to the piece size rounded up to a power of two, compares its SHA-256 with the digest stored with it, and folds the digests into the wire hash and the original bytes into the content hash. Every defect of the content is a non-match naming the first bad piece (`piece 2: its bytes do not match its digest`), which the answer carries whatever text the store gives its mismatch; only a read fault throws, and becomes `503`.
- **The checks of the server** (`FileChecks`): every running check is tracked; at shutdown (`ApplicationStopping`, after the sessions were closed) they are cancelled and awaited, at most 10 s, before the store and the database close. A cancelled check changes nothing on the disk.
- **Log lines**: `Collector file connection 5d1c09aa of session 20e46170 opened: …; 1 file connection(s) open.`; per upload, `Info`: `… upload <32 hex> of "app.dmp" (123456789 bytes, 30 pieces of 4173824) to "app/dumps" begun` (or `continued at piece N`), `… is stored.`, `… is discarded: <reason>.`; a failed end check is a `Warn` (`… is discarded, its end check failed: <defect>`); the close line counts `pieces stored N (B bytes), repeated, refused; uploads begun, stored, discarded; requests refused`. Telemetry: every frame in and every answer out are counted in the `Clients/...` sensors, as for the table connection.

## Data Flow

```
upgrade request --> TelemetryMiddleware (Key -> product, key, client)  --> SensorsController.Session
                --> CollectorSessionHandler: checks, slot, accept, hello
frame           --> parse envelope --> re-check access --> SensorIngestionService --> ITreeValuesCache
                <-- ack {ack, status, body | error}
agent session   <-- {op: directive} pushes: after hello, on change (15 s check), whole set every 5 min (DirectivePush)
table upgrade   --> checks, X-Hsm-Session -> live session of the key, attach (replace), hello with block limits
binary frame    --> TableFrame header --> re-check access --> put semaphore --> ITableBlockService.Put
                <-- ack {ack, status, body {outcome, tables, refused} | error [, retryAfter]}
file upgrade    --> checks, X-Hsm-Session -> live session of the key, attach (replace: the old one ends first), hello with file limits
begin           --> fields checked --> IFileUploadStore.BeginAsync (the lease) --> ack {upload, next, pieces, pieceBytes}
piece           --> FilePiece header --> re-check access --> lengths, CRC-32, zstd frame header --> IFileUploadStore.AppendAsync
                <-- ack {next} | {next, reason} | error [, retryAfter]
end             --> FileChecks: IFileUploadStore.FinishAsync(FilePieceChecker) beside the loop --> ack 202, then the outcome on a repeat
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

A table connection: the 4 KiB receive buffer and the session object, as an idle session (about 22 KiB). Its frames (`TableFrameMemory`):

- **Up to 64 KiB** (the free allowance; the usual block of tens of kilobytes): buffers from the shared pool, as for a session, and no budget.
- **Larger**: before its buffer grows past 64 KiB the connection reserves twice `MaxTableFrameBytes` plus one byte from the server-wide `MaxTableBytesInFlight` (its buffer, at most `MaxTableFrameBytes` + 1, and the exact copy the put takes, at most `MaxTableFrameBytes`), waiting first come first served while it holds nothing else, so waiting connections never hold each other up. The buffers above 64 KiB are exact allocations, never the shared pool: **after a large frame nothing stays allocated** for it; its arrays (and the buffers it outgrew while it arrived) are garbage for the next collection (the large-object heap). The reservation goes back when the frame's put is answered, and whatever a connection holds goes back when its loop ends, whatever ended it (a drop mid-frame, a frame over the limit, a put that throws, a close by its session, shutdown, the 1013 of a wait past its bound, a reserved frame past its deadline). **A reserved frame has a deadline**: one silence limit from the reservation (`ReservedFrameDeadlineInSilenceLimits`), which arriving bytes do not move (an ordinary frame may take ten limits while its bytes arrive): past it the connection is cut and the reservation goes back, so a slow sender cannot hold a reservation for a quarter of an hour. **One reservation per access key**: a second large frame of the same key waits like any other waiter until the first is done; it does not hold back the waiters of other keys behind it, nor a frame of another key that arrives later while there is room (taken at once). A waiter of a free key that waits for room is never overtaken by a later arrival (`TableFrameBudget.CanTakeNow`; `TableFrameBudgetTests`).
- **The put** allocates the block's decompressed length (up to `TableSensors.MaxDecompressedBytes`, 64 MiB) while it checks the directory: at most `MaxConcurrentPuts` at once (4 × 64 MiB by default).

A file connection (#1884): the same idle cost. Its frames (`FileFrameMemory`): up to 64 KiB from the shared pool; a larger one, a piece, only while the connection holds an upload, as an exact allocation per frame (the garbage collector's afterwards, never kept per lease). A connection holds one upload and the store bounds the uploads in progress (`LargeFiles.MaxUploadsInProgress`, 4 by default), so file frames hold at most that many frames of `MaxFileFrameBytes` + 1 (about 16 MiB at the defaults). An end check holds the upload's lease while it runs (also after its connection closed), a pooled buffer of one piece and one stored piece the store reads (about 8 MiB at the defaults) plus the decoder's window (at most the piece size rounded up to a power of two, 4 MiB): about 48 MiB for 4 checks. **Together about 64 MiB at the defaults**; in general `MaxUploadsInProgress × (MaxFileFrameBytes + 2 × piece size + window)`, whatever the number of connections, plus 64 KiB per idle file connection. There is no per-key rule: one key can hold every upload slot (the owner's decision, ADR-0022 point 6). Argued from the code, not measured.

So what clients can make the server hold for table frames is `MaxTableBytesInFlight` (256 MiB by default, 31 large frames at once) plus the small frames (64 KiB per connection, twice with its copy) plus the puts' decompression, whatever the number of connections. Argued from the code; the budget's accounting is tested on every exit path, the memory itself is not measured.


An idle session holds about **22 KiB of managed memory** on the server (measured with 400 sessions opened over raw sockets, `IdleSession_CostsTensOfKilobytes`; stable to 0.1 KiB across runs). At the default cap of 1 000 sessions that is about 22 MB; every further 1 000 costs about as much. Kernel socket buffers come on top and are not measured.

## Dependencies

- Depends on: the Sensor API authentication (`TelemetryCollector`), `ITreeValuesCache`, the server's self-monitoring collector.
- Used by: the managed collector (`CollectorOptions.Transport = Session`, 3.9.0; the default since 4.0.0) and the native collector (`hsm_collector_use_session_transport`, 0.12.0; the agent's and the probe's `transport` setting). Directive push (step 5) reaches the agent through the native collector's directive callback.

## Tests

`src/tests/HSMServer.Core.Tests/SensorApi/CollectorSessionTests.cs`: a real `ClientWebSocket` and `HttpClient` against the real Sensor API (middleware, controller, ingestion service, session handler) on a real Kestrel listener over the real cache. Covered: the handshake access matrix, the required subprotocol, sessions switched off, ack status and body equal to the HTTP route for the same body (`list` and `commands`, accepted and partly refused, a registration the server cannot convert, a `commands` body it cannot read, a `commands` list with a null element or a command without a path, a fault on one command of a `commands` batch with the commands around it applied and the resend after it, several faults in one frame logged once, the same fault on every resend logged once; the last three through `FaultingCache`, which makes the registration of the paths a test names throw), a file value in a `list` frame stored as the same value over `/file` and `/list`, a file the server refuses an entry of the ack as on `/list` (`FileValue_*`, #1660 step 2b), failure acks without echo, unknown op and unknown envelope fields, ping, the five violations with their close codes, the session cap and the limit per address with slot release, the silence limit (also with a frame arriving in pieces), lost access (on a data frame and on a ping), registry contents, shutdown (idle, and with a batch inside the store), a vanished collector, traffic counting, the HTTP routes after the extraction of the ingestion service, and directive push (`Directives_*`: the set after the hello equals the headers of an HTTP answer in text and order; none, and no push timer, for a session without `X-Agent-Version`; a switched group pushed alone at the next check, and nothing at a check without a change; the whole set at the re-announcement; a newly staged agent once the staged-version cache is 15 s old). The `Log_*` tests pin the log lines (opened and closed by the collector with its code and a printable reason, silence, an aborted connection, a refused upgrade throttled to one line, no key value) through `SensorApiTestHost.Log`: the host gives the handler a logger from its own `LogFactory` (`CollectorSessionHandler.UseLogger`), so the process-wide NLog configuration is untouched and the tests still run in parallel.

The silence limit and the two directive timers run on a `TimeProvider`. The tests replace it with a manual clock, so none of them waits for real seconds: they assert that the watch is running, that a frame starts it again, and what happens when it fires; they fire the directive timers by hand, and move the clock's readings forward (`ManualTimeProvider.Advance`) to age the staged-version cache.

The test host runs only the part of the pipeline the Sensor API passes through. The full pipeline (HTTPS, the guards, cookie and token authentication in front of the endpoint) was checked by hand against a locally started server when the feature was written: upgrade, `hello`, a `list` and a `commands` frame with their acks, ping, the two refusals, and an empty error log.

`src/tests/HSMServer.Core.Tests/SensorApi/TableConnectionTests.cs` (#1845), the same host with the real block service and store underneath and a minimal table client of the test project (`TableConnectionTestClient`): the hello and its limits, `409` for no, a malformed, an unknown, another key's and an ended session, `406`, `404` with table connections off while the session works, no slot taken, a replacement, the close with the session and the close line, the probe, a block stored and read back value for value (null, NaN, −0.0, extreme integers), a refused table and every table refused with names and reasons, four `400`s and the connection going on, five headers closing with `1002`, `1009`, text on a table connection, `1003` still on the session, `1008` for a key that lost access, a duplicate after a lost ack, a duplicate after its hour was merged and its blocks deleted (`refusedKnown: false`, the merge run with a clock a day ahead), a replaced connection's put racing the resend inside the store (stored once), the session's ack not delayed by a held put, `503` with `retryAfter` and the resend stored once, shutdown during a put (ack, then `1001`), `MaxConcurrentPuts`, and the producer warning. `TableConnectionProtocolTests`: the ack forms (`refusedKnown: false` included), the hello with its `sessionId`, the frame header. `TableFrameBudgetTests`: the grant order of the frame budget without a server (a waiter of a key that holds a take does not hold back another key; a waiter that waits for room is not overtaken). The puts are held at the block service's own hook after the reservation (`TableBlockService.AfterReserve`); nothing waits for time.

`src/tests/HSMServer.Core.Tests/SensorApi/FileConnectionTests.cs` (#1884), the same host with `FakeFileUploadStore` (in memory, the rules of #1883's interface: one lease per part, the bound on leases, a repeat stored once, a part without pieces gone with its lease, gates to hold an append or a finish, faults on demand) and `FileConnectionTestClient` (the header byte by byte, pieces compressed with ZstdSharp at level 3): the hello and its limits, the piece size against the frame, `409` for no, an unknown and another key's session, `406`, `404` with file connections off and with no store while the session works, `400` for both paired subprotocols, three sockets of one session and no slot, a replacement (answered, and not answered: cut off) with the upload free at the new hello, the close with the session and the close line; a file begun, sent and stored with the value's hash of the original bytes, the lease given back after the check, a repeated `end`, a `begin` of a stored file; an empty file; a repeated piece, a piece ahead, `end` with pieces missing, a second `begin`, another piece size; a cut after the begin, after each piece and in the middle of a piece, each continued on a new connection; a cut during the check and `begin` telling its result; a restart of the server (a new host over the same store); a wrong CRC (kept), six size violations (discarded), a frame that decompresses short found by the end check, a wrong hash, a wrong digest named by its piece, a piece of another upload, a large frame without an upload (`1002`), five untrusted headers (`1002`); seven malformed begins refused before the store, a `modified` with an offset; `413`, `422`, `503` for no room, `400` for a piece size over the hello's, an unknown op, two forms of a refused `end`; busy; the bound on uploads in progress (`503`, and a large frame of the refused connection not let in); a write fault and the resend, a check fault and the end again; lost access in the middle (`1008`, the part stays), a check that goes on after the key lost access; shutdown in the middle of a piece, during a write (answer, then `1001`), during a check (cancelled, awaited, ended again after the restart). The issue's test "the budget and its `1013`" is replaced on purpose by the bound on uploads in progress: a file connection never waits for memory. `FilePieceTests`: the header field by field, the upload id's hex, piece lengths, the window bound, a frame of zstd, a window too large, a skippable frame, the bounded decompression, the end check matching with the content hash and five defects each naming its piece, a wrong whole hash. Not covered: the real store of #1883 (the adapter comes when it is merged into the integration branch), a file of real size.

Not covered at all: that nothing is pushed once a session is being closed at shutdown, the bundled reverse proxy in front of the endpoint, a WebSocket over HTTP/2, a collector that stops reading (the send is bounded by the silence limit, untested), the ten-limit budget of a frame that never ends, and behaviour at thousands of sessions.

## Known Issues / Limitations

- **The limit per address is per address, not per collector.** Hosts behind one NAT share it, and so do all collectors behind a reverse proxy that the server does not trust (`Kestrel.TrustedProxies`): there the limit of 20 would cap the whole server at 20 sessions. The server names the address in its `Warn` when the limit is hit, at most once a minute for each address. Many addresses holding one key can still fill the server-wide cap together.
- The registry is not shown anywhere in the UI.
- `Open sessions` has no default alert for approaching the cap; the `Warn` above and the sensor's value are the signals.
- A connection that dies without a close keeps its slot until the silence limit (90 seconds by default). After a network event that drops many connections at once, collectors that reconnect within that time can be refused while the cap is not really reached; they keep their data and try again.
- `Open sessions` is a 5-minute sample, not a live gauge.
- `HeartbeatSeconds`, `MaxFrameBytes`, `MaxSessions` and `MaxSessionsPerAddress` are read at startup; a change needs a restart.
- **File connections answer `404` in production until the store of #1883 is merged and its adapter to `IFileUploadStore` is registered** (#1884 is coded against the seam; its tests run against a fake).
- A lease the store lets expire (`LargeFiles.LeaseTimeout`, 180 s without an append) while the connection still believes it holds it lets the connection take frames above 64 KiB until its next append answers `404`: one frame beyond the bound at most for each such connection, and only for a collector that pings for three minutes without sending a piece.
- A `MaxSessionsPerAddress` that is not below `MaxSessions` limits nothing, and nothing says so at startup.
