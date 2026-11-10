# Initiative: Collector session — a persistent WebSocket connection on 443

> Owner: shared | Last reviewed: 2026-11-10 | Canonical: yes
> Status: **design for owner review**. Transport choice is decided ([ADR-0017](../decisions/0017-collector-session-websocket.md)); the protocol details below are proposed and marked where a choice is still open (§ Open Questions).

## Problem

A collector talks to the server with one HTTP request per batch. The managed collector keeps its TCP+TLS connection between requests; the native collector opens a new one per request until #1653 (PR #1658, open at the time of writing) lands. Either way that is a transport detail: there is **no session** between a collector and the server. Three consequences:

1. **The server cannot talk to a collector.** The only server-to-collector path is the `X-Hsm-Directive` response header piggybacked on data POSTs (#1198). A directive waits for the next POST and is repeated on every POST while its condition holds.
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

- **Server.** `SensorsController` (`src/server/HSMServer/Controllers/SensorsController.cs`) exposes `POST /api/sensors/{bool,int,…,list,file,commands,addOrUpdate}` and `GET testConnection`. `TelemetryMiddleware` authenticates any request carrying a `Key` header and attaches `PublicApiRequestInfo` (product, access key, client name, remote IP). `/list` answers a `Dictionary<path, error>` on success and echoes the request batch on a 400; `/commands` answers the same dictionary shape for registrations. `EmitAgentDirectives` appends `X-Hsm-Directive` headers to data responses for clients that send `X-Agent-Version`; the staged agent version behind `update-available` is a file read through a 15 s cache on the request path. Kestrel serves HTTP/1.1 and HTTP/2 on one port; the bundled Caddy terminates TLS on 443 and proxies to it.
- **Managed collector.** All sends go through the public `IDataSender` seam (`Core/IDataSender.cs`): `SendDataAsync`, `SendPriorityDataAsync`, `SendCommandAsync`, `SendFileAsync`, each returning `PackageSendingInfo`. The default implementation is `HsmHttpsClient`. Queue processors re-enqueue a package whose `PackageSendingInfo.Error` is set, whatever the status.
- **Native collector.** Value batches go through the `sender_` seam to `HttpTransport::Post` on `/list`; registrations are posted to `/commands` at Start and by `FlushRuntimeRegistrations`. A failed value send is re-enqueued by the dispatcher; a registration rejected with 4xx is final. Response headers are handed to the directive callback.
- **The agent** retries a failed update because the server repeats `update-available` on every response; a 60 s debounce in `update_checker.cpp` keeps that from re-downloading on every batch.
- **Consumers of the native collector**: HsmAgent (Windows service, self-update, directives), the Linux probe (Rust over the C ABI), HSMCppWrapper.

## Proposed Direction

### Discovery

A collector must not guess whether a server has sessions. The WebSocket client libraries in play cannot report why an upgrade was refused (`ClientWebSocket` exposes the handshake status only from .NET 7; the collector targets `net6.0` and `net472`), so "404 means old server" is not an implementable rule.

- A server that supports sessions adds one response header to every HTTP Sensor API response: `X-Hsm-Session: hsm.sensors.v1`.
- A collector attempts a session **only after it has seen that header** on one of its own HTTP responses. Against an old server it never tries at all.
- Every later upgrade failure is then, by construction, transient (restart, cap reached, proxy trouble) and is handled by one backoff rule.

### Endpoint and handshake

`GET /api/sensors/session` with a WebSocket upgrade, on the same origin as the HTTP Sensor API.

- **Auth is the existing one**: `Key` and `ClientName` request headers on the upgrade request, validated by the same path as HTTP requests. A missing or invalid key is refused before the upgrade. Access-key permissions (`CanSendSensorData`) are checked per frame, as they are per request today.
- **Subprotocol** `hsm.sensors.v1` in `Sec-WebSocket-Protocol`. The server refuses an upgrade without a subprotocol it knows. A future incompatible framing is `hsm.sensors.v2`, negotiated the standard way and advertised by the discovery header.
- **Client facts** ride the upgrade as headers: `X-Agent-Version` (existing), plus `X-Collector-Version` and `X-Collector-Kind` (`managed` / `native`) for the registry.
- **Scheme** follows the collector's existing rule: `wss` by default; `ws` only when `AllowPlaintextTransport` is set and the address says `http://`.
- **Session cap**: a configurable server limit on concurrent sessions. Over the cap the upgrade is refused and the collector stays on HTTP.

### Frames

Text frames, one JSON object each. One envelope shape per direction:

| Direction | Fields | Meaning |
|---|---|---|
| collector to server | `id`, `op`, `body` | a request; `id` is a per-session counter chosen by the collector |
| server to collector | `ack`, `status`, then `body` or `error` | the answer to request `ack` |
| server to collector | `op`, `body` | a push; no `id`, no answer expected |
| either | `op` = `ping` / `pong` | heartbeat (see lifecycle) |

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
- **Frame limit.** `maxFrameBytes` (4 MiB in v1). The collector checks the size of a body **before** choosing the transport: a body over the limit goes over HTTP, where the request limit is far higher. A collector therefore never sends an oversized frame; a server that receives one closes the session with 1009.
- **Ops in v1**: `list` (values and bars, the polymorphic batch) and `commands` (registrations). The typed single-value routes are not mirrored: both collectors batch.
- **Files stay on HTTP.** A file value is a numeric JSON array, about 40 MB of JSON for a 10 MiB file, far over the frame limit by design.
- **Unknown fields and ops**: both sides ignore unknown envelope fields. An unknown request `op` gets `{"ack":id,"status":400,"error":"unknown op"}`; an unknown push `op` is ignored by the collector. This is what lets v1 grow without a v2.

### Delivery semantics

- One request in flight per queue kind, as today: the queue processor sends a batch and waits for its ack up to `RequestTimeout`. No pipelining in v1.
- **Ack timeout.** A request without an ack after `RequestTimeout` is a failed send, and the collector **closes the session**. Closing is what makes a late ack impossible, so both drivers behave identically and nothing has to correlate stale ids. The batch goes back to the queue; its retry goes over HTTP or over the next session, whichever is up.
- **Session drop** with a request outstanding is the same failed send.
- The server may have stored a batch whose ack was lost. Delivery is at-least-once, as it is over HTTP today.
- The stop drain uses the session when it is up, under the same bounded budget as now, and never waits for a reconnect.

### Session lifecycle and fallback

```
   [ HTTP only, capability unknown ] -- sees X-Hsm-Session --> [ connecting ] -- upgraded + hello --> [ session up ]
                                                                 ^      |                                  |
                                                                 |      +-- refused / timeout --+          |
                                                                 +------- backoff elapsed <-----+----------+
                                                                                          drop / ack timeout / silence
```

- The collector **starts on HTTP** and opens a session in the background once the server has advertised the capability. Start never waits for it.
- While the session is up, `list` and `commands` bodies within the frame limit go on it. Everything else, and everything while the session is down, goes over HTTP. The choice is per send; nothing queues "for the session".
- **Reconnect**: exponential backoff with full jitter, 1 s up to 5 min.
- **Heartbeat is at the application level.** WebSocket ping/pong frames are not usable for this: ASP.NET Core's keep-alive sends unsolicited pongs, and neither `ClientWebSocket` nor libcurl's default mode surfaces control frames to the application. Instead the collector sends `{"op":"ping"}` when it has sent nothing for `heartbeatSeconds`; the server answers `{"op":"pong"}`. The collector closes and reconnects after 2.5 heartbeats without any inbound frame; the server drops a session after 3 heartbeats without any inbound frame. Protocol-level keep-alive stays enabled as well, for the benefit of NAT and proxy idle timers.
- **Server restart or proxy reload** closes sessions (Caddy drops upgraded connections on a config reload). Collectors continue over HTTP at once and reconnect with jitter, so a restart does not produce a synchronized reconnect wave.
- **Key revoked or blocked while connected**: the server closes with 1008; the collector continues over HTTP, where it gets the ordinary rejection.

### Push: directives

Directives are **state, not events**, and the push keeps the level-triggered behavior the agent relies on.

- On session open, after `hello`, the server sends the **full desired directive state** for that client: the same set `EmitAgentDirectives` computes.
- It sends a directive again **when its value changes** (a product's sensor groups toggled, a newer agent staged) and **re-announces the full state every 5 minutes** while the session lasts. The re-announcement is what retries a failed agent update, which per-response repetition does today; the agent's debounce stays as it is.
- The staged agent version has no change event (it is a file). The session service samples it on the same 15 s cache the request path uses and pushes when the sampled value changes.
- Directive strings are unchanged (`update-available:<version>`, `sensor-disable:<group>`, `sensor-enable:<group>`), so the agent's handling is unchanged.
- The header path stays for HTTP-only clients. A client with a session up gets no directive headers on its HTTP responses, so it never sees a directive twice.

What improves is latency and cost: a directive reaches the collector when it changes, not on the next POST, and an unchanged state costs one small frame per 5 minutes instead of six headers on every response.

### Session registry (presence)

A server service holding the live sessions: product, access key, client name, collector kind and version, remote IP, connected-at, last-frame-at. It is the single place that answers "is this collector connected", and the single place push goes through. First consumers: a self-monitoring sensor with the session count, and the directive push. What the UI shows and whether a disconnect raises an alert is step 6.

### Code shape

- **Server**: the ingestion logic now inside `SensorsController` actions (`/list`, `/commands`) moves into a service that both the controller and the session handler call. This is also what rule #4 asks for. The session handler is a thin loop: read frame, dispatch to the service, write ack. A client disconnect is a normal end of the loop, never an `Error` log (the #1632 class of noise).
- **Managed**: a `SessionDataSender` that implements `IDataSender`, owns the WebSocket and delegates to the existing `HsmHttpsClient` whenever the session is down, the body is over the frame limit, or the op is `file`. No queue changes. `System.Net.WebSockets.ClientWebSocket` exists on both `net6.0` and `net472`.
- **Native**: a session transport next to `HttpTransport`, behind the same `sender_` seam and the registration path, with one owned thread. Exposed through one additive C ABI option; the agent and the probe get it by enabling the option.

## Compatibility

| Collector | Server | Result |
|---|---|---|
| old | new | HTTP, as today. The session endpoint is never called; the extra response header is ignored. |
| new | old | No capability header, so no upgrade is ever attempted. HTTP. |
| new | new, proxy blocks upgrades | Upgrade fails. HTTP, retries with backoff up to once per 5 min. |
| new | new | Session for `list` / `commands` / directives; HTTP for files, oversized bodies and as fallback. |

- **Public API**: additive only. A new `CollectorOptions` member selects the session mode; `IDataSender` is unchanged, so a custom `DataSender` keeps working and simply has no session.
- **Rollout default**: the first collector releases ship the session **opt-in**. The default flips to "auto" after a soak (garage probe, then internal products). See Open Questions.
- **net472**: `ClientWebSocket` needs Windows 8 / Server 2012 or newer. On an older OS the collector logs once and stays on HTTP. `ClientWebSocketOptions` has no per-connection certificate callback on .NET Framework, so `AllowUntrustedServerCertificate` on net472 means HTTP only; the process-wide callback is not touched.
- **Wire contract**: the frame envelope and the discovery header are new and become part of `wire-contract/feature.md`. The value and registration bytes inside a frame do not change.

## Work Breakdown

One PR per step unless noted.

| Step | Scope | Notes |
|---|---|---|
| 0 | This initiative, ADR-0017, glossary terms | Owner review gates everything below. |
| 1 | Native WebSocket capability | Enable the `websockets` feature of the vcpkg `curl` dependency (manifest, port, Conan recipe), build it in the Windows and Linux lanes and in the static probe build, and prove `curl_ws_send`/`curl_ws_recv` against an in-process WebSocket test server. Today's build reports `supported_protocols="HTTP HTTPS"`. If libcurl's WebSocket API does not hold up, the fallback is RFC 6455 framing over a `CONNECT_ONLY` handle; the decision is recorded in the step's PR. |
| 2 | Server: session endpoint | Extract ingestion from `SensorsController` into a service; discovery header; `GET /api/sensors/session`; `hello`, frame dispatch, acks, heartbeat; session cap; registry; self-monitoring session count. Tests with a real WebSocket client against the test host. Wire-contract doc gets the envelope. |
| 3 | Session conformance harness + first collector | New DSL verbs for a scripted session server and the `.hsmtest` scenarios, with harness support in **both** drivers, plus the session sender of the first collector (managed: `SessionDataSender`, both TFMs). The other driver marks the session verbs `CONFORMANCE-UNSUPPORTED` against the step 4 issue, which is the sanctioned parity flow (rule #9, epic #1093). Depends on #1659 A2/A3 for a sane fallback path. |
| 4 | Second collector | Native session transport behind the existing seams, C ABI option (MINOR bump), agent and probe bumps. Removes every `CONFORMANCE-UNSUPPORTED` marker step 3 added. |
| 5 | Directive push | Server pushes on open, on change and on the 5-minute re-announcement; native routes pushed directives to the existing callback; managed gains the directive surface it lacks today (#1659 B1). Scenarios and both drivers in the same PR. |
| 6 | Presence in the product | UI / management API / MCP surface for connected collectors; optional disconnect alert. Separate product decision; not started without one. |
| 7 | Rollout | Soak on the garage probe, flip the default, wiki pages, release notes. |

## Risks

- **Two transports to keep correct.** Mitigation: one payload format, the HTTP status as the only result vocabulary, and session scenarios in the shared conformance corpus so the two collectors cannot drift.
- **Intermediaries.** Some corporate proxies break WebSocket upgrades or kill idle connections. Mitigation: automatic HTTP fallback, application heartbeat, and opt-in rollout.
- **Reconnect storms** after a server restart. Mitigation: jittered backoff, HTTP in the gap, and the server refusing upgrades until it is ready to ingest.
- **Server resources.** Every collector holds a connection. Kestrel handles this at the scale HSM runs at, but large installs are memory-sensitive; step 2 measures per-session cost and the cap is configurable.
- **Head-of-line blocking** by a large frame. Mitigation: the frame limit is enforced by the sender's choice of transport, files stay on HTTP, failure acks do not echo the request, one request in flight per queue.
- **Duplicates** when an ack is lost. Not new: a lost HTTP response has the same effect today.
- **Native dependency risk** (libcurl WebSocket API maturity, static linking in the probe). Mitigation: step 1 is a gate, with a stated fallback.
- **Log noise from disconnects.** A dropped session must not become an `ERROR` line per drop (ADR-0016, #1632).
- **The conformance DSL has no network today.** A scripted session server in both drivers is new harness surface. Mitigation: it lands once, in step 3, with the DSL spec (`tests/conformance/README.md`) versioned accordingly.

## Verification

- **Server**: integration tests over a real WebSocket against the test host: handshake auth matrix, ack status parity with the HTTP route for the same body, failure acks without echo, frame limit, session cap, heartbeat timeout, registry contents, clean close on shutdown.
- **Parity between collectors (rule #9)**: session behavior is portable, observable collector behavior, so it lives in the CI-gated corpus: `tests/conformance/collector/session_*.hsmtest`, driven by new verbs that script the server side of a session (advertise capability, accept or refuse the upgrade, ack with a status, stay silent, drop, push a directive) and assert the collector's frames and its choice of transport. Covered at least: no attempt without the capability header, fallback on refusal, oversized body goes to HTTP, ack timeout closes the session and re-sends, heartbeat silence reconnects, directive delivery. The checklist gate and the unsupported-marker triage apply to them like to any other scenario.
- **Byte identity**: the `body` of a `list` / `commands` frame equals the HTTP body for the same input in each collector (golden tests reuse the existing wire goldens).
- **End to end**: the Docker integration lane runs a real server behind Caddy with a managed and a native collector on sessions, asserts values arrive, then restarts the server and asserts HTTP fallback and reconnection with no gap in a bar series.
- **Soak**: the garage probe on a session for at least a week before the default flips; the acceptance signals are the same as for the 0.10.0 soak (no restarts, complete bar series, `Queue overflow` silent).
- **Docs**: `wire-contract/feature.md`, `collector/http-client/feature.md` (becomes transport), `server/overview.md`, `architecture/docker.md` (proxy notes), agent feature doc, wiki.

## Open Questions

1. **Default mode at first release**: opt-in (proposed) or auto?
2. **Presence in the product** (step 6): is "collector disconnected" an alert, a tree state, or only a list on the product page?
3. **Managed directive surface** (step 5): an event on the collector, or only internal handling of known directives?
4. **Session cap default** and whether it is per server or per product.
