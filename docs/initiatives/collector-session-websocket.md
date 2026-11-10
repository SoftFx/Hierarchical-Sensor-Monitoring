# Initiative: Collector session — a persistent WebSocket connection on 443

> Owner: shared | Last reviewed: 2026-11-10 | Canonical: yes
> Status: **design, owner decisions recorded 2026-11-10** (§ Owner Decisions). Transport choice: [ADR-0017](../decisions/0017-collector-session-websocket.md). Implemented so far: step 1 in PR #1663 and step 2 in PR #1665, both open at the time of writing. What this document quotes from them (class and test names, the dependency manifests, the libcurl version floor, test counts, the measured cost of a session) is theirs and exists on no merged branch yet. Nothing is wired into a collector's send path.

## Problem

A collector talks to the server with one HTTP request per batch. The managed collector keeps its TCP+TLS connection between requests; the native collector opens a new one per request until #1653 (PR #1658, open at the time of writing) lands. Either way that is a transport detail: there is **no session** between a collector and the server. Three consequences:

1. **The server cannot talk to a collector.** The only server-to-collector path is the `X-Hsm-Directive` response header piggybacked on data POSTs (#1198). A directive waits for the next POST and is repeated on every POST while its condition holds.
2. **The server has no notion of a connected collector.** It cannot list the collectors that are talking to it, cannot address one of them, and infers that one is gone from sensor TTLs: about a minute for `Service alive`, longer for everything else, and nothing at all for a collector that does not post `Service alive`.
3. **Delivery is confirmed by an HTTP status per request.** Each batch costs a full request/response cycle with its headers, and the two directions of the conversation (data up, directives down) are tied to that cycle.

## Goals

- One long-lived, authenticated **collector session** per collector over WebSocket, on the same port and host as everything else (ADR-0014).
- Sensor values and registrations travel as **frames** on the session; the server acknowledges each frame.
- The frame **body is the existing JSON wire, byte for byte** (`aicontext/features/api/wire-contract/feature.md`). No second serialization, no change to DTOs.
- The server can **push** to a collector: the existing directives first.
- The server keeps a **session registry**: which collector is connected, since when, from where, which version.
- **The session replaces the HTTP routes for collectors** (owner decision, 2026-11-10). HTTP stays through a transition period, for collectors that have not moved yet. A collector uses one transport or the other; **no session logic depends on HTTP**: no fallback to it, no discovery through it, no side traffic over it.
- **Everything a collector sends can travel on the session**, file values included. Until that is true HTTP cannot be removed.
- **Nothing breaks during the transition.** Old collectors and old servers keep working with new ones over HTTP, which is unchanged until it is retired.

## Non-Goals

- Removing the HTTP routes within this initiative. It ends with every collector able to run on a session and HTTP deprecated; the removal itself is a later release, on a date the owner sets.
- A binary or schema-first wire (protobuf, MessagePack). The JSON wire is the contract the conformance corpus pins.
- A fallback from the session to HTTP, in any form.
- A general remote-control channel. Push carries the directives that exist today; new directive kinds are separate decisions.
- Exactly-once delivery. Delivery stays at-least-once, as it is over HTTP.
- A new kind of alert or a presence UI. A lost collector is alerted through the sensor-alert mechanism HSM already has (§ Disconnect alert); a page listing connected collectors is not part of this initiative.

## Current Behavior

- **Server.** `SensorsController` (`src/server/HSMServer/Controllers/SensorsController.cs`) exposes `POST /api/sensors/{bool,int,…,list,file,commands,addOrUpdate}` and `GET testConnection`. `TelemetryMiddleware` authenticates any request carrying a `Key` header and attaches `PublicApiRequestInfo` (product, access key, client name, remote IP). `/list` answers a `Dictionary<path, error>` on success and echoes the request batch on a 400; `/commands` answers the same dictionary shape for registrations. `EmitAgentDirectives` appends `X-Hsm-Directive` headers to data responses for clients that send `X-Agent-Version`; the staged agent version behind `update-available` is a file read through a 15 s cache on the request path. Kestrel serves HTTP/1.1 and HTTP/2 on one port; the bundled Caddy terminates TLS on 443 and proxies to it.
- **Managed collector.** All sends go through the public `IDataSender` seam (`Core/IDataSender.cs`): `SendDataAsync`, `SendPriorityDataAsync`, `SendCommandAsync`, `SendFileAsync`, each returning `PackageSendingInfo`. The default implementation is `HsmHttpsClient`. Queue processors re-enqueue a package whose `PackageSendingInfo.Error` is set, whatever the status.
- **Native collector.** Value batches go through the `sender_` seam to `HttpTransport::Post` on `/list`; registrations are posted to `/commands` at Start and by `FlushRuntimeRegistrations`. A failed value send is re-enqueued by the dispatcher; a registration rejected with 4xx is final. Response headers are handed to the directive callback.
- **The agent** retries a failed update because the server repeats `update-available` on every response; a 60 s debounce in `update_checker.cpp` keeps that from re-downloading on every batch.
- **Consumers of the native collector**: HsmAgent (Windows service, self-update, directives), the Linux probe (Rust over the C ABI), HSMCppWrapper.

## Proposed Direction

### Choosing the transport

A collector's transport is a setting, the **transport setting**, not something it works out at run time. It has two values: `http` (the default while the transition lasts) and `session`.

- With `session` the collector connects at Start, in the background; Start never waits for it. It does not ask the server over HTTP first and does not watch HTTP answers for hints.
- Until the session is up, and whenever it is down, values wait in the collector's queues exactly as they do today while the server is unreachable: bounded queues, the overflow reported by the `Queue overflow` sensor. **Nothing is sent over HTTP.**
- Against a server that has no sessions (an older server, or one with sessions switched off) the upgrade is refused every time. That is a configuration error, not a state to recover from: the collector logs it and keeps retrying on the backoff rule.
- **What a collector can say about a refusal differs.** The native client sees the HTTP status of a refused upgrade and logs it. The managed client cannot on `net6.0` and `net472` (`ClientWebSocket` exposes the handshake status only from .NET 7), so it logs that the upgrade was refused and no more. The reason is then on the server's side: the session cap is logged there, and an access error is what the same key would get over HTTP.
- **When the session cannot exist at all** on this host (a libcurl without WebSocket, an operating system too old for `ClientWebSocket`, a setting the session cannot honour), the rule is the same in both collectors: Start completes and does not throw, one error names the cause, and nothing is delivered until the setting is changed. A host process is never broken by its collector, and a collector never changes transport on its own.

### Endpoint and handshake

`GET /api/sensors/session` with a WebSocket upgrade, on the same origin as the HTTP Sensor API.

- **Auth is the existing one**: `Key` and `ClientName` request headers on the upgrade request, validated by the same path as HTTP requests. A missing or invalid key is refused before the upgrade. Access-key permissions (`CanSendSensorData`) are checked per frame, as they are per request today.
- **Subprotocol** `hsm.sensors.v1` in `Sec-WebSocket-Protocol`. The server refuses an upgrade without a subprotocol it knows. A future incompatible framing is `hsm.sensors.v2`, negotiated the standard way.
- **Client facts** ride the upgrade as headers: `X-Agent-Version` (existing), plus `X-Collector-Version` and `X-Collector-Kind` (`managed` / `native`) for the registry.
- **Scheme** follows the collector's existing rule: `wss` by default; `ws` only when `AllowPlaintextTransport` is set and the address says `http://`.
- **Session cap**: a configurable server limit on concurrent sessions. Over the cap the upgrade is refused, and that collector cannot send until a slot is free. The cap is therefore a guard against a runaway fleet, set well above the number of collectors, and the server logs when it is reached.

### Frames

Text frames, one JSON object each. One envelope shape per direction:

| Direction | Fields | Meaning |
|---|---|---|
| collector to server | `id`, `op`, `body` | a request; `id` is a per-session counter chosen by the collector |
| server to collector | `ack`, `status`, then `body` or `error` | the answer to request `ack` |
| server to collector | `op`, `body` | a push; no `id`, no answer expected |
| either | `op` = `ping` / `pong` | heartbeat (see lifecycle) |

Request ops in v1: `list`, `commands`.

The `body` of a `list` or `commands` request is **exactly the bytes the collector would have sent as the HTTP body of the matching route**.

```json
{"id":17,"op":"list","body":[{"Type":1,"Value":42,"Comment":null,"Time":"2026-11-10T10:00:00Z","Status":1,"Key":null,"Path":"p/int"}]}
{"ack":17,"status":200,"body":{}}

{"id":18,"op":"commands","body":[{"Type":0,"Path":"p/int","SensorType":1}]}
{"ack":18,"status":200,"body":{"p/int":"error text for this sensor"}}

{"id":19,"op":"list","body":[]}
{"ack":19,"status":400,"error":"empty batch"}

{"op":"hello","body":{"maxFrameBytes":4194304,"heartbeatSeconds":30}}
{"op":"directive","body":"sensor-disable:network"}
```

- **`status` is the HTTP status the same request would have got** (200, 400, 406, 5xx). Each collector feeds it into the decision it already makes for an HTTP status, so a session never needs a second rule set. The two collectors do not make the same decision on 4xx today (native treats a rejected registration as final, managed re-enqueues it; #1659 A2). The session inherits that as it stands; converging it is #1659's job and comes before the session scenarios pin 4xx behavior.
- **A success ack carries the route's success body** (the path-to-error dictionary). **A failure ack carries a short `error` string and never echoes the request**, unlike the HTTP 400 of `/list`.
- **`hello`** is the first frame the server sends. It states the limits the collector must respect.
- **Frame limit.** `maxFrameBytes` (4 MiB in v1). `list` and `commands` bodies are arrays, so a collector **splits a batch** until each frame fits; it never sends an oversized frame, and a server that receives one closes the session with 1009. The limit counts the whole frame, envelope included. An element that does not fit a frame on its own (a very long string value, in practice) is not sent: the collector drops that one element, logs it and counts it as a dropped value, and the rest of the batch goes on. Sending it would close the session, bring the batch back and block the queue for good.
- **Data ops in v1**: `list` (values and bars, the polymorphic batch) and `commands` (registrations). The typed single-value routes are not mirrored: both collectors batch.
- **File values need their own op, and it is not designed yet.** A file value is a numeric JSON array, about 40 MB of JSON for a 10 MiB file, far over the frame limit. It cannot go to HTTP, so the session has to carry it: either a `file` op whose frames may be larger, or a file sent as a sequence of frames, with the content in a denser encoding than a JSON number per byte. This is step 2b. Until it exists, a collector on a session refuses a file value where it is sent: the send fails with an error that is logged and counted, and the value is not queued for a retry that could never succeed. Collectors that have file sensors are therefore moved to a session only after step 2b.
- **Unknown fields and ops**: both sides ignore unknown envelope fields. An unknown request `op` gets `{"ack":id,"status":400,"error":"unknown op"}`; an unknown push `op` is ignored by the collector. This is what lets v1 grow without a v2.

### Delivery semantics

- One request in flight per queue kind, as today: the queue processor sends a batch and waits for its ack up to `RequestTimeout`. No pipelining in v1.
- **Ack timeout.** A request without an ack after `RequestTimeout` is a failed send, and the collector **closes the session**. Closing is what makes a late ack impossible, so both drivers behave identically and nothing has to correlate stale ids. The batch goes back to the queue and is sent again on the next session.
- **Session drop** with a request outstanding is the same failed send.
- The server may have stored a batch whose ack was lost. Delivery is at-least-once, as it is over HTTP today.
- The stop drain uses the session when it is up, under the same bounded budget as now, and never waits for a reconnect.

### Session lifecycle

```
   [ connecting ] -- upgraded + hello --> [ session up ]
        ^   |                                  |
        |   +-- refused / timeout --+          |
        +------- backoff elapsed <--+----------+
                                    drop / ack timeout / silence
```

- The collector connects in the background from Start. While there is no session, values wait in its queues; there is no other path.
- **Reconnect**: exponential backoff with full jitter, 1 s up to 1 min. The ceiling is deliberately well under the 5 minutes after which a silent collector is alerted (#1666): a session that can be reopened is reopened long before anyone is paged.
- **Heartbeat is at the application level.** WebSocket ping/pong frames are not usable for this on the managed side: ASP.NET Core's keep-alive sends unsolicited pongs, and `ClientWebSocket` never shows control frames to the application, so a managed collector can neither see a server ping nor wait for a pong. (The native client does see protocol pings through libcurl and answers them, as RFC 6455 requires, but one heartbeat rule has to work for both collectors.) Instead the collector sends `{"op":"ping"}` when it has sent nothing for `heartbeatSeconds`; the server answers `{"op":"pong"}`. The collector closes and reconnects after 2.5 heartbeats without any inbound frame; the server drops a session after 3 heartbeats without any inbound frame. Protocol-level keep-alive stays enabled as well, for the benefit of NAT and proxy idle timers.
- **Server restart or proxy reload** closes sessions (Caddy drops upgraded connections on a config reload). Collectors reconnect with jitter, so a restart does not produce a synchronized wave, and deliver what queued up meanwhile.
- **Key revoked or blocked while connected**: the server closes with 1008. The next upgrades are refused with the access error; the collector logs it and its queues fill, as they do today when HTTP requests are rejected.
- **What an outage costs** is what it costs today: a session that cannot be opened for longer than the queues hold loses the oldest values, and the collector reports it. The session adds no second path, and removes none that will exist once HTTP is gone.

### Push: directives

Directives are **state, not events**, and the push keeps the level-triggered behavior the agent relies on.

- On session open, after `hello`, the server sends the **full desired directive state** for that client: the same set `EmitAgentDirectives` computes.
- It sends a directive again **when its value changes** (a product's sensor groups toggled, a newer agent staged) and **re-announces the full state every 5 minutes** while the session lasts. The re-announcement is what retries a failed agent update, which per-response repetition does today; the agent's debounce stays as it is.
- The staged agent version has no change event (it is a file). The session service samples it on the same 15 s cache the request path uses and pushes when the sampled value changes.
- Directive strings are unchanged (`update-available:<version>`, `sensor-disable:<group>`, `sensor-enable:<group>`), so the agent's handling is unchanged.
- **Managed collector** (owner decision): directives are handled **inside** the collector; there is no public event. The managed library acts on none of today's directives (they are agent matters), so in v1 it receives them, logs unknown ones at debug level once, and exposes nothing. A directive the library should act on later is added to its internal table, still without a public surface.
- A collector in `http` mode keeps getting directives as response headers, as today, for as long as HTTP lasts. A collector in `session` mode makes no HTTP requests, so it cannot receive a directive twice.

What improves is latency and cost: a directive reaches the collector when it changes, not on the next POST, and an unchanged state costs one small frame per 5 minutes instead of six headers on every response.

### Session registry (presence)

A server service holding the live sessions: product, access key, client name, collector kind and version, remote IP, connected-at, last-frame-at. It is the single place that answers "which sessions are open", and the single place push goes through. Consumers: a self-monitoring sensor with the session count, and the directive push.

**Session cap.** One limit for the whole server (owner decision), a server setting. Its default is 1 000 sessions, proposed after step 2 measured the cost (#1664, PR #1665): an idle session holds about 22 KiB of managed memory on the server, so that cap costs about 22 MB when reached. The first draft said 10 000; the owner asked why so many. One session is one collector process, and no known installation has a thousand of them. A larger one raises the setting: collectors over the cap are refused every time they try and deliver nothing until a slot is free, and the server says so in its log.

### Disconnect alert

Owner decision: a collector that disconnects must raise an **alert**.

**HSM already raises it.** Both collectors post `.module/Service alive` every 15 s. The sensor is registered with a 1-minute TTL and a default inactivity alert that notifies and sets the sensor to Error (`ServiceAlivePrototype` in `Prototypes/Collections/ModuleInfoCollections.cs`; the native collector mirrors it). A collector that dies is therefore alerted about one minute after its last beat, with an ordinary sensor alert that templates, schedules, destinations and muting apply to. Alerts belong to sensors (ADR-0001), and this is the sensor.

**What a session can and cannot add.** A session tells the server at once when a process dies (the OS closes the socket), and within three heartbeats when a host loses power or network. A separate "connection" sensor driven by the session was designed and dropped during review: it would page a second time for the same incident and need an identity that survives HTTP fallback and server restarts. Since the owner's decision below there is a simpler reason as well: the session would notice sooner, and sooner is not what is wanted.

**So the alert stays where it is, and the session adds no second one.** What changes is its timing (owner decision, 2026-11-10): **the alert must fire only when a collector has been off for more than 5 minutes.** A restart, an update or a short network loss must not page anyone. Today the inactivity period of `Service alive` is 1 minute; #1666 makes it 5 minutes in both collectors, with the beat period unchanged at 15 seconds.

This is a change for every collector, whether it ever opens a session or not, and it is #1666's to carry out and to document; it is recorded here because the question came up here. It is the default a collector registers, so #1666 also has to settle what happens to a `Service alive` sensor that is already registered with 1 minute and to collectors of older versions: until then the 5 minutes hold only for sensors registered by an updated collector.

Two more things follow for this initiative:

- A collector with the session option on must also have `Service alive`, or it has no disconnect alert. Both collectors log a warning at Start when the session is enabled and `Service alive` is not.
- The beat travels on the session like any other value. While no session is up no beat arrives, so a collector that cannot reach the server through its session for more than 5 minutes is alerted like one that is off, which is what it is to the server. A session that drops and can be reopened is back within the reconnect ceiling of a minute, well inside that period.

**Not taken**: using an abnormal session close to bring the alert forward. The owner wants a later alert, not an earlier one.

**Not decided**: telling a planned stop from a loss, so that a deliberate stop longer than 5 minutes stays quiet. The 5-minute period covers ordinary restarts and updates; a longer planned stop still alerts.

### Code shape

- **Server**: the ingestion logic now inside `SensorsController` actions (`/list`, `/commands`) moves into a service that both the controller and the session handler call. This is also what rule #4 asks for. The session handler is a thin loop: read frame, dispatch to the service, write ack. A client disconnect is a normal end of the loop, never an `Error` log (the #1632 class of noise).
- **Managed**: a `SessionDataSender` that implements `IDataSender` and owns the WebSocket. It is selected by the transport setting and does not know the HTTP client: when there is no session a send fails, and the queue keeps the batch, as it does for a failed HTTP send. No queue changes. `System.Net.WebSockets.ClientWebSocket` exists on both `net6.0` and `net472`.
- **Native**: a session transport next to `HttpTransport`, behind the same `sender_` seam and the registration path, with one owned thread. It starts from `WebSocketConnection` (step 1), which is deliberately only a connection: single owner thread, and its waits poll in 100 ms slices. The session thread must wake for outgoing batches, incoming pushes, the heartbeat and stop, so step 4 has to add a real wake-up mechanism and the interleaving of requests awaiting acks with pushed frames; that is the bulk of step 4, not plumbing. Exposed through one additive C ABI option; the agent and the probe get it by enabling the option. Where libcurl cannot carry a WebSocket (step 1) and the session is selected, Start reports that the transport is unavailable: it does not quietly use HTTP.

## Compatibility

| Collector | Server | Result |
|---|---|---|
| old, or new in `http` mode | new | HTTP, as today, until HTTP is retired. |
| new in `session` mode | new | Session for everything the session can carry. |
| new in `session` mode | old, or sessions switched off | The upgrade is refused. Nothing is delivered; the collector logs it and its queues fill. A configuration error. |
| new in `session` mode | new, proxy blocks upgrades | Same: nothing is delivered until the proxy lets the upgrade through. |
| new native in `session` mode, libcurl older than 8.14.0 or without WebSocket | any | Start reports that the transport is unavailable (step 1). The host needs a newer libcurl, or stays in `http` mode while that exists. |

**The end of the transition.** When HTTP is retired for collectors, every collector still in `http` mode stops delivering: older versions of the library in third-party applications, hosts whose libcurl is too old for a native session, deployments behind a proxy that breaks upgrades. The per-client `Clients/...` sensors show who still uses which transport, and the date is the owner's to set.

- **Public API**: additive only. A new `CollectorOptions` member is the transport setting; `IDataSender` is unchanged, so a custom `DataSender` keeps working and simply has no session.
- **Off by default** (owner decision). The session is enabled by an explicit option in the managed collector, the native collector, the agent config and the probe config. A collector with the option off never opens a session and is byte-for-byte today's collector. Turning the default on is step 8 and a decision of its own; nothing before it flips it.
- **net472**: `ClientWebSocket` needs Windows 8 / Server 2012 or newer. On an older OS session mode is unavailable and Start says so. `ClientWebSocketOptions` has no per-connection certificate callback on .NET Framework, so `AllowUntrustedServerCertificate` cannot be honoured for a session on net472: the two settings together are refused at Start, and the process-wide callback is not touched. Both are cases a deployment has to resolve before HTTP is retired.
- **Wire contract**: the frame envelope is new and becomes part of `wire-contract/feature.md`. The value and registration bytes inside a frame do not change.

## Work Breakdown

One PR per step unless noted.

| Step | Scope | Notes |
|---|---|---|
| 0 | This initiative, ADR-0017, glossary terms | Owner review gates everything below. |
| 1 | Native WebSocket capability (#1662, PR #1663) | **Outcome: the native collector uses libcurl's own WebSocket** (`CONNECT_ONLY=2`, `curl_ws_send` / `curl_ws_recv`) and carries no RFC 6455 code. **How it got there:** a first version framed RFC 6455 itself over a plain libcurl connection, so that a session would also exist on distributions whose libcurl lacks WebSocket (Ubuntu 24.04 ships 8.5.0 without it). The owner rejected self-written protocol code on 2026-11-10: the software is run on hosts we choose, with a current OS (§ Owner Decisions). **What a session therefore needs:** libcurl 8.14.0 or newer, built with WebSocket support (it lists the `ws` protocol), both as the headers the collector is compiled against and as the library it runs with: against older headers the client is compiled as a stub. Up to 8.12 libcurl reports the frames of a fragmented message without saying which is the last (`ws_frame_op2flags(dec->head[0])` in `lib/ws.c`; fixed in 8.13 by `ws_frame_firstbyte2flags`). 8.14 is the first release where the caller can answer pings (`CURLWS_NOAUTOPONG`), and that is needed: in 8.21 an automatic pong stays queued until the next send or data frame. The vcpkg manifests now ask for `curl[ssl,websockets]` (8.21.0 at the pinned baseline); Debian 13 ships 8.14.1 with WebSocket. Not covered: the Conan recipe still pins libcurl 8.10.1, and the published registry port still asks for `curl[ssl]`; both are step 4's to change. **What libcurl does not check** in the upgrade response (seen on 8.21.0): the `Sec-WebSocket-Accept` digest and the `Connection` header. Neither authenticates the server (TLS does, where TLS is used). What the digest would have caught is an intermediary that answers `101` without speaking WebSocket; that is now a residual risk, listed under Risks. **Gate status:** Windows, vcpkg libcurl 8.21.0, Schannel: all 214 native tests, the WebSocket and transport tests 25 runs in a row. Debian 13, distribution libcurl 8.14.1, OpenSSL: the WebSocket and transport tests 10 runs in a row with WebSocket required. Ubuntu 24.04 (8.5.0): builds with warnings as errors, the connection tests skip and say why. TLS by hand on Windows and Debian 13: a `wss://` upgrade through a CDN to a live server with certificate verification on returns the server's `404`, and an expired certificate is refused. CI does not cover TLS (the fixture is plaintext, like the HTTP fixtures); ASan/TSan and the remaining CI lanes run when the PR leaves draft. |
| 2 | Server: session endpoint (#1664, PR #1665) | Extract ingestion from `SensorsController` into a service; `GET /api/sensors/session`; `hello`, frame dispatch, acks, heartbeat; server-wide session cap; registry; self-monitoring session count; per-session cost measured. Tests with a real WebSocket client against the test host. Wire-contract doc gets the envelope. |
| 2b | File values on the session | The `file` op (larger frames or a sequence of frames, denser encoding), on the server and then in both collectors. Needs its own short design. Without it a collector with file sensors cannot run in `session` mode and HTTP cannot be retired. |
| 3 | Session conformance harness + first collector | New DSL verbs for a scripted session server and the `.hsmtest` scenarios, with harness support in **both** drivers, plus the session sender of the first collector (managed: `SessionDataSender`, both TFMs), selected by the transport setting, with batch splitting to the frame limit and no HTTP fallback. The other driver marks the session verbs `CONFORMANCE-UNSUPPORTED` against the step 4 issue, which is the sanctioned parity flow (rule #9, epic #1093). Depends on #1659 A2/A3: with no fallback, what the queue does with a failed or rejected send is the whole recovery story. |
| 4 | Second collector | Native session transport: the session thread with its wake-up mechanism, request/ack correlation interleaved with pushes and heartbeat, behind the existing seams; C ABI option (MINOR bump), agent and probe bumps. Where `WebSocketConnection::Supported()` is false and the session is selected, Start reports that the transport is unavailable. The registry port asks for `curl` with the `websockets` feature and a `version>=` of 8.14.0 (a feature alone does not fix the version a consumer's baseline resolves), and the Conan recipe moves to a libcurl that has WebSocket, or the PR states that Conan builds have no session. The native session scenarios run in the CI HTTP lane, whose vcpkg libcurl has WebSocket on both platforms and which sets `HSM_REQUIRE_WEBSOCKET=1`, so they cannot pass by skipping; lanes built without the HTTP transport have no transport to run them on. Removes every `CONFORMANCE-UNSUPPORTED` marker step 3 added. An integration test over real TLS belongs here. |
| 5 | Directive push | Server pushes on open, on change and on the 5-minute re-announcement; native routes pushed directives to the existing callback; managed receives them internally with no public surface (this settles #1659 B1). Scenarios and both drivers in the same PR. |
| 6 | Disconnect alert | No new mechanism: the existing `Service alive` inactivity alert is the disconnect alert. Its inactivity period moves from 1 to 5 minutes in both collectors (#1666, owner decision: alert only after more than 5 minutes off), with the conformance scenario and both drivers in the same PR. Plus the Start warning in both collectors when the session is on and `Service alive` is off (lands with steps 3 and 4). |
| 7 | Rollout | Enable on the garage probe (it has no file sensors) and soak for a week; wiki pages; release notes. The transport setting stays `http` by default. A collector with file sensors is moved only after 2b. |
| 8 | Retire HTTP for collectors | After 2b and the soak: `session` becomes the default, the HTTP data routes log their remaining users, and a later release removes them. Each of the three is the owner's call, and each is a breaking change for someone: the default flip and the removal come with a major version of the collectors and of the server, and with a deprecation notice in the release notes and the wiki a release ahead. |

## Risks

- **Two transports to keep correct, for the length of the transition.** Mitigation: one payload format, the HTTP status as the only result vocabulary, and session scenarios in the shared conformance corpus so the two collectors cannot drift.
- **No fallback.** A collector in `session` mode has one path. When it cannot open a session it delivers nothing, and what it loses is bounded only by its queues. This is the same exposure a collector has to an unreachable server today, but new causes are added: a proxy that breaks upgrades, a server with sessions switched off, the session cap, a libcurl too old. Mitigation: the setting is explicit, the bundled proxy is verified to pass the upgrade before rollout, and each cause is reported somewhere: by the native collector with the refusal's status, by the server for the cap, and by the managed collector only as "refused" (see Choosing the transport).
- **Retiring HTTP strands whoever has not moved**: older library versions in third-party applications above all. Mitigation: the per-client sensors show who is left, and the date is a decision, not a side effect.
- **Intermediaries.** Some corporate proxies break WebSocket upgrades or kill idle connections. The heartbeat handles idle timers; a proxy that refuses upgrades means no session mode there until it is fixed, and such a deployment holds back the retirement of HTTP.
- **Reconnect storms** after a server restart. Mitigation: jittered backoff, and the server refusing upgrades until it is ready to ingest.
- **Server resources.** Every collector holds a connection. Step 2 measured about 22 KiB of managed memory per idle session (kernel socket buffers not counted), and the cap is configurable. An installation with more collectors than the cap has to raise it, or the surplus delivers nothing.
- **Head-of-line blocking** by a large frame. Mitigation: the sender splits batches to the frame limit, file values get their own op with its own flow (step 2b), failure acks do not echo the request, one request in flight per queue.
- **Duplicates** when an ack is lost. Not new: a lost HTTP response has the same effect today.
- **The native session depends on the libcurl it is linked with** (step 1 outcome). The WebSocket protocol code is libcurl's, which is the point: it is maintained, fuzzed and patched upstream, and the collector parses no frames. The costs: a session exists only with libcurl 8.14.0+ built with WebSocket, so a host with an older distribution libcurl has no session mode, and neither does a binary that was built against older libcurl headers, whatever libcurl it later runs with (mitigation: `Supported()` gives the reason, which tells the two cases apart, and Start reports it); behaviour differs between libcurl versions in ways the collector had to work around once already (the queued automatic pong); and libcurl is laxer than RFC 6455 about the upgrade response: it does not verify the accept digest, so an intermediary that answers `101` without speaking WebSocket is not caught at the open. It shows up as a frame error or as heartbeat silence; the collector then reconnects through the same intermediary and nothing is delivered until that is fixed. What stays ours is small and tested: message assembly, the size limit taken from the frame header, text only, the subprotocol check, header validation.
- **Log noise from disconnects.** A dropped session must not become an `ERROR` line per drop (ADR-0016, #1632).
- **The conformance DSL has no network today.** A scripted session server in both drivers is new harness surface. Mitigation: it lands once, in step 3, with the DSL spec (`tests/conformance/README.md`) versioned accordingly.

## Verification

- **Server**: integration tests over a real WebSocket against the test host: handshake auth matrix, ack status parity with the HTTP route for the same body, failure acks without echo, frame limit, session cap, heartbeat timeout, registry contents, clean close on shutdown.
- **Parity between collectors (rule #9)**: session behavior is portable, observable collector behavior, so it lives in the CI-gated corpus: `tests/conformance/collector/session_*.hsmtest`, driven by new verbs that script the server side of a session (accept or refuse the upgrade, ack with a status, stay silent, drop, push a directive) and assert the collector's frames and its choice of transport. Covered at least: a collector in session mode makes no HTTP request at all, values queue while the upgrade is refused and arrive after it succeeds, a batch is split to the frame limit, an ack timeout closes the session and the batch is sent again on the next one, heartbeat silence reconnects, directive delivery. The checklist gate and the unsupported-marker triage apply to them like to any other scenario.
- **Byte identity**: the `body` of a `list` / `commands` frame equals the HTTP body for the same input in each collector (golden tests reuse the existing wire goldens).
- **End to end**: the Docker integration lane runs a real server behind Caddy with a managed and a native collector on sessions, asserts values arrive through the proxy, then restarts the server and asserts reconnection with no gap in a bar series.
- **Disconnect alert**: a scenario per collector asserts that `Service alive` beats arrive on the session and resume after a reconnect, and that Start warns when the session is on without `Service alive`. The 5-minute inactivity period is pinned by a registration scenario that both drivers run (#1666): a default that no scenario states can drift between the collectors unnoticed.
- **Soak**: the garage probe on a session for at least a week; the acceptance signals are the same as for the 0.10.0 soak (no restarts, complete bar series, `Queue overflow` silent), plus no `Service alive` inactivity alert while the server was reachable.
- **Docs**: `wire-contract/feature.md`, `collector/http-client/feature.md` (becomes transport), `server/overview.md`, `architecture/docker.md` (proxy notes), agent feature doc, wiki.

## Owner Decisions

Answered by the owner on 2026-11-10.

| Question | Decision | Where it lands |
|---|---|---|
| Is the session on by default in the first releases? | **No. It is an explicit option.** | § Choosing the transport, steps 3, 4 and 7 |
| Does HTTP stay beside the session? | **No. The session is to replace it; HTTP is kept only for a transition period, and no session logic may depend on it.** No fallback, no discovery through HTTP answers. | Goals, § Choosing the transport, § Session lifecycle, steps 2b and 8 |
| What does the product do when a collector disconnects? | **It raises an alert, and only after the collector has been off for more than 5 minutes.** Carried out by #1666; see there for sensors that are already registered. | § Disconnect alert, step 6, #1666 |
| Does the managed collector expose directives publicly? | **No. They are handled inside the collector.** | § Push: directives, step 5 |
| Is the session cap per server or per product? | **Per server.** | § Session registry, step 2 |
| May the native collector carry its own WebSocket protocol code? | **No. No self-written protocol code; a session is still wanted.** The owner runs the software on hosts of their choosing with a current OS, so depending on a recent libcurl is acceptable. SharpRpc, gRPC and a Rust rewrite of the collector were raised in the same discussion and not taken (ADR-0017, Alternatives). | Step 1, § Compatibility, Risks |

Proposed here and not confirmed by the owner: the default of 1 000 for the session cap. The owner's input was the question "why so many?" about the 10 000 of the first draft.

