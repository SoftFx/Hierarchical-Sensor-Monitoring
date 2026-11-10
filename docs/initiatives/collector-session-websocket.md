# Initiative: Collector session — a persistent WebSocket connection on 443

> Owner: shared | Last reviewed: 2026-11-10 | Canonical: yes
> Status: **design for owner review**. Transport choice is decided ([ADR-0017](../decisions/0017-collector-session-websocket.md)); the protocol details below are proposed and marked where a choice is still open (§ Open Questions).

## Problem

A collector talks to the server with one HTTP request per batch. Since #1653 both collectors keep the TCP+TLS connection between requests, but that is a transport detail: there is still **no session** between a collector and the server. Three consequences:

1. **The server cannot talk to a collector.** The only server-to-collector path is the `X-Hsm-Directive` response header piggybacked on data POSTs (#1198). A directive waits for the next POST, is re-sent on every POST while its condition holds, and the agent needs a 60 s debounce to survive that.
2. **The server does not know which collectors are connected.** It learns that a collector went away only when a sensor's TTL expires, minutes later, and cannot tell "host down" from "sensor stopped posting".
3. **Delivery is confirmed by an HTTP status per request.** Each batch costs a full request/response cycle with its headers, and the two directions of the conversation (data up, directives down) are tied to that cycle.

## Goals

- One long-lived, authenticated **collector session** per collector over WebSocket, on the same port and host as everything else (ADR-0014).
- Sensor values and registrations travel as **frames** on the session; the server acknowledges each frame.
- The frame **body is the existing JSON wire, byte for byte** (`aicontext/features/api/wire-contract/feature.md`). No second serialization, no change to DTOs.
- The server can **push** to a collector: the existing directives first.
- The server keeps a **session registry**: which collector is connected, since when, from where, which version.
- **Nothing breaks.** The HTTP Sensor API stays, unchanged and undeprecated. A collector that cannot open a session keeps working over HTTP without operator action. Old collectors and old servers interoperate with new ones.

## Non-Goals

- Replacing or deprecating the HTTP Sensor API.
- A binary or schema-first wire (protobuf, MessagePack). The JSON wire is the contract the conformance corpus pins.
- File sensor values on the session in the first wave (see § Frames: files stay on HTTP).
- A general remote-control channel. Push carries the directives that exist today; new directive kinds are separate decisions.
- Exactly-once delivery. Delivery stays at-least-once, as it is over HTTP.
- UI for presence and "collector disconnected" alerts. The registry is built here; what the product shows and alerts on is a follow-up product decision (step 6).

## Current Behavior

- **Server.** `SensorsController` (`src/server/HSMServer/Controllers/SensorsController.cs`) exposes `POST /api/sensors/{bool,int,…,list,file,commands,addOrUpdate}` and `GET testConnection`. `TelemetryMiddleware` authenticates any request carrying a `Key` header and attaches `PublicApiRequestInfo` (product, access key, client name, remote IP). `/list` answers a `Dictionary<path, error>`; `/commands` answers the same shape for registrations. `EmitAgentDirectives` appends `X-Hsm-Directive` headers to data responses for clients that send `X-Agent-Version`. Kestrel serves HTTP/1.1 and HTTP/2 on one port; the bundled Caddy terminates TLS on 443 and proxies to it.
- **Managed collector.** All sends go through the public `IDataSender` seam (`Core/IDataSender.cs`): `SendDataAsync`, `SendPriorityDataAsync`, `SendCommandAsync`, `SendFileAsync`, each returning `PackageSendingInfo`. The default implementation is `HsmHttpsClient`. Queue processors re-enqueue a package whose `PackageSendingInfo.Error` is set.
- **Native collector.** Value batches go through the `sender_` seam to `HttpTransport::Post` on `/list`; registrations are posted to `/commands` at Start and by `FlushRuntimeRegistrations`. A failed send is re-enqueued by the dispatcher. Response headers are handed to the directive callback.
- **Consumers of the native collector**: HsmAgent (Windows service, self-update, directives), the Linux probe (Rust over the C ABI), HSMCppWrapper.

## Proposed Direction

### Endpoint and handshake

`GET /api/sensors/session` with a WebSocket upgrade, on the same origin as the HTTP Sensor API.

- **Auth is the existing one**: `Key` and `ClientName` request headers on the upgrade request, validated by the same path as HTTP requests. A missing or invalid key is refused before the upgrade with the same status the HTTP API uses. Access-key permissions (`CanSendSensorData`) are checked per frame, as they are per request today.
- **Subprotocol** `hsm.sensors.v1` in `Sec-WebSocket-Protocol`. The server refuses an upgrade without a subprotocol it knows. A future incompatible framing is `hsm.sensors.v2`, negotiated the standard way.
- **Client facts** ride the upgrade as headers: `X-Agent-Version` (existing), plus `X-Collector-Version` and `X-Collector-Kind` (`managed` / `native`) for the registry.
- **Scheme** follows the collector's existing rule: `wss` by default; `ws` only when `AllowPlaintextTransport` is set and the address says `http://`.
- **Limits.** `Kestrel`-level: a configurable cap on concurrent sessions (server setting, default generous). Over the cap the server answers 503 and the collector stays on HTTP. Maximum frame size 4 MiB; a larger frame closes the session with 1009.

### Frames

Text frames, one JSON object each. The envelope is deliberately tiny; everything of substance is the `body`, which is **exactly the bytes the collector would have sent as the HTTP body of the matching route**.

Collector to server:

```json
{"id":17,"op":"list","body":[{"Type":1,"Value":42,"Comment":null,"Time":"2026-11-10T10:00:00Z","Status":1,"Key":null,"Path":"p/int"}]}
{"id":18,"op":"commands","body":[{"Type":0,"Path":"p/int","SensorType":1, "...":"..."}]}
```

Server to collector:

```json
{"ack":17,"status":200,"body":{}}
{"ack":18,"status":200,"body":{"p/int":"error text for this sensor"}}
{"op":"directive","value":"sensor-disable:network"}
```

- `id` is a per-session counter chosen by the collector. `ack` echoes it.
- `status` is the **HTTP status the same request would have got** (200, 400, 406, 5xx). The collectors keep one decision table for both transports: the managed `PackageSendingInfo` and the native retry policy consume a status either way, so 4xx stays final and 5xx stays retryable without a second rule set.
- `body` of an ack is the HTTP response body of the same route.
- **Ops in v1**: `list` (values and bars, the polymorphic batch) and `commands` (registrations). The typed single-value routes are not mirrored: both collectors batch.
- **Files stay on HTTP.** A file value is a numeric JSON array, up to about 40 MB of JSON for a 10 MiB file. One such frame would block every other frame behind it. File sends keep using `POST /file` over the kept HTTP connection.
- **Unknown fields and ops**: both sides ignore unknown envelope fields. An unknown `op` from the collector gets `{"ack":id,"status":400}`; an unknown `op` from the server is ignored by the collector. This is what lets v1 grow without a v2.

### Delivery semantics

- One frame in flight per queue kind, as today: the queue processor sends a batch and waits for its ack up to `RequestTimeout`. No pipelining in v1.
- A frame without an ack when the session drops is **treated as a failed send**: the batch goes back to the queue, exactly as after a failed HTTP request. The server may have stored it; delivery is at-least-once, as it is today.
- The stop drain uses the session when it is up, under the same bounded budget as now, and never waits for a reconnect.

### Session lifecycle and fallback

```
            +--------------------- upgrade fails / session drops --------------------+
            v                                                                         |
   [ HTTP only ] -- backoff elapsed --> [ connecting ] -- upgraded --> [ session up ] -+
        ^                                    |
        +---------- refused / timeout -------+
```

- The collector **starts on HTTP** and tries to open a session in the background. Start never waits for it.
- While the session is up, `list` and `commands` go on it. While it is not, they go over HTTP. The switch is per send; nothing queues "for the session".
- **Reconnect**: exponential backoff with full jitter, 1 s up to 2 min. A refusal that says "this server has no sessions" (404, 426, or a missing subprotocol) backs off to one attempt per hour: an old server will not grow the endpoint by itself.
- **Liveness**: WebSocket ping every 30 s from the server (Kestrel `KeepAliveInterval`); the collector closes and reconnects after 75 s without any inbound frame. This also keeps NAT mappings and proxy idle timers alive.
- **Server restart or proxy reload** closes sessions (Caddy drops upgraded connections on a config reload). Collectors fall back to HTTP at once and reconnect with jitter, so a restart does not produce a synchronized reconnect wave.
- **Key revoked or blocked while connected**: the server closes with 1008; the collector falls back to HTTP, where it gets the ordinary rejection.

### Push: directives

- On session open the server sends the **full desired directive state** for that client (the same set `EmitAgentDirectives` computes), then sends a directive **only when it changes** (a product's sensor groups toggled, a newer agent staged).
- Directive strings are unchanged (`update-available:<version>`, `sensor-disable:<group>`, `sensor-enable:<group>`), so the agent's handling is unchanged. Only the delivery differs: once, when it happens.
- The header path stays for HTTP-only clients. A client with a session up gets no directive headers on its HTTP responses (file sends), so it never sees a directive twice.

### Session registry (presence)

A server service holding the live sessions: product, access key, client name, collector kind and version, remote IP, connected-at, last-frame-at. It is the single place that answers "is this collector connected", and the single place push goes through. First consumers: a self-monitoring sensor with the session count, and the directive push. What the UI shows and whether a disconnect raises an alert is step 6.

### Code shape

- **Server**: the ingestion logic now inside `SensorsController` actions (`/list`, `/commands`) moves into a service that both the controller and the session handler call. This is also what rule #4 asks for. The session handler is a thin loop: read frame, dispatch to the service, write ack. A client disconnect is a normal end of the loop, never an `Error` log (the #1632 class of noise).
- **Managed**: a `SessionDataSender` that implements `IDataSender`, owns the WebSocket and delegates to the existing `HsmHttpsClient` whenever the session is down or the op is `file`. No queue changes. `System.Net.WebSockets.ClientWebSocket` exists on both `net6.0` and `net472`.
- **Native**: a session transport next to `HttpTransport`, behind the same `sender_` seam and the registration path, with one owned thread. Exposed through one additive C ABI option; the agent and the probe get it by enabling the option.

## Compatibility

| Collector | Server | Result |
|---|---|---|
| old | new | HTTP, as today. The session endpoint is never called. |
| new | old | Upgrade refused (404). HTTP, one upgrade retry per hour. |
| new | new, proxy blocks upgrades | Upgrade fails. HTTP, retries with backoff. |
| new | new | Session for `list` / `commands` / directives; HTTP for files and as fallback. |

- **Public API**: additive only. A new `CollectorOptions` member selects the session mode; `IDataSender` is unchanged, so a custom `DataSender` keeps working and simply has no session.
- **Rollout default**: the first collector releases ship the session **opt-in**. The default flips to "auto" after a soak (garage probe, then internal products). See Open Questions.
- **net472**: `ClientWebSocket` needs Windows 8 / Server 2012 or newer. On an older OS the collector logs once and stays on HTTP. `ClientWebSocketOptions` has no per-connection certificate callback on .NET Framework, so `AllowUntrustedServerCertificate` on net472 means HTTP only; the process-wide callback is not touched.
- **Wire contract**: the frame envelope is new and becomes part of `wire-contract/feature.md`. The value and registration bytes inside it do not change.

## Work Breakdown

One PR per step unless noted. Steps 3 and 4 can run in parallel after step 2.

| Step | Scope | Notes |
|---|---|---|
| 0 | This initiative, ADR-0017, glossary terms | Owner review gates everything below. |
| 1 | Native WebSocket capability | Enable the `websockets` feature of the vcpkg `curl` dependency (manifest, port, Conan recipe), build it in the Windows and Linux lanes and in the static probe build, and prove `curl_ws_send`/`curl_ws_recv` against an in-process WebSocket test server. Today's build reports `supported_protocols="HTTP HTTPS"`. If libcurl's WebSocket API does not hold up, the fallback is RFC 6455 framing over a `CONNECT_ONLY` handle; the decision is recorded in the step's PR. |
| 2 | Server: session endpoint | Extract ingestion from `SensorsController` into a service; `GET /api/sensors/session`; frame dispatch; acks; session cap; registry; self-monitoring session count. Tests with a real WebSocket client against the test host. Wire-contract doc gets the envelope. |
| 3 | Managed collector: `SessionDataSender` | Session-first with HTTP fallback, reconnect/backoff, liveness, option. Both TFMs. Depends on #1659 A2/A3 for a sane fallback path. |
| 4 | Native collector: session transport | Same behavior behind the existing seams, C ABI option (MINOR bump), agent and probe bumps. |
| 5 | Directive push | Server pushes on open and on change; native routes pushed directives to the existing callback; managed gains the directive surface it lacks today (#1659 B1). |
| 6 | Presence in the product | UI / management API / MCP surface for connected collectors; optional disconnect alert. Separate product decision; not started without one. |
| 7 | Rollout | Soak on the garage probe, flip the default, wiki pages, release notes. |

## Risks

- **Two transports to keep correct.** Mitigation: one decision table (HTTP status in the ack), one payload format, and shared transcripts (see Verification) so the two collectors cannot drift on session behavior.
- **Intermediaries.** Some corporate proxies break WebSocket upgrades or kill idle connections. Mitigation: automatic HTTP fallback, pings, and opt-in rollout.
- **Reconnect storms** after a server restart. Mitigation: jittered backoff, HTTP fallback during the gap, and the server refusing upgrades until it is ready to ingest.
- **Server resources.** Every collector holds a connection. Kestrel handles this at the scale HSM runs at, but large installs are memory-sensitive; step 2 measures per-session cost and the cap is configurable.
- **Head-of-line blocking** by a large frame. Mitigation: 4 MiB frame cap, files stay on HTTP, one frame in flight per queue.
- **Duplicates** when an ack is lost. Not new: a lost HTTP response has the same effect today.
- **Native dependency risk** (libcurl WebSocket API maturity, static linking in the probe). Mitigation: step 1 is a gate, with a stated fallback.
- **Log noise from disconnects.** A dropped session must not become an `ERROR` line per drop (ADR-0016, #1632).

## Verification

- **Server**: integration tests over a real WebSocket against the test host: handshake auth matrix, ack status parity with the HTTP route for the same body, frame cap, session cap, registry contents, clean close on shutdown.
- **Parity between collectors (rule #9)**: session behavior is portable, observable collector behavior. It is pinned by **shared session transcripts** under `tests/conformance/session/`: each scenario is a script of server frames and events (ack, 5xx ack, drop, refuse upgrade, ping silence, directive) plus the expected collector frames and transport choices, replayed by a scripted fake server in both the managed and the native driver. The transcript format is specified in step 2 or 3, whichever lands first.
- **Byte identity**: the `body` of a `list` / `commands` frame equals the HTTP body for the same input in each collector (golden tests reuse the existing wire goldens).
- **End to end**: the Docker integration lane runs a real server behind Caddy with a managed and a native collector on sessions, asserts values arrive, then restarts the server and asserts HTTP fallback and reconnection with no gap in a bar series.
- **Soak**: the garage probe on a session for at least a week before the default flips; the acceptance signals are the same as for the 0.10.0 soak (no restarts, complete bar series, `Queue overflow` silent).
- **Docs**: `wire-contract/feature.md`, `collector/http-client/feature.md` (becomes transport), `server/overview.md`, `architecture/docker.md` (proxy notes), agent feature doc, wiki.

## Open Questions

1. **Default mode at first release**: opt-in (proposed) or auto?
2. **Presence in the product** (step 6): is "collector disconnected" an alert, a tree state, or only a list on the product page?
3. **Managed directive surface** (step 5): an event on the collector, or only internal handling of known directives?
4. **Session cap default** and whether it is per server or per product.
