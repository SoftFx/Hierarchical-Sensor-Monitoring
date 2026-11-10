# ADR-0017: Collectors hold a persistent session with the server over WebSocket on 443

**Status:** Accepted
**Date:** 2026-11-10
**Supersedes:** —

---

## Context

A collector delivers data with one HTTP request per batch. Keeping the underlying connection between requests (the managed collector does; the native one will with #1653) does not change the shape of the exchange. There is no session: the server cannot send anything to a collector except by piggybacking `X-Hsm-Directive` headers on a data response (#1198), and it has no notion of a connected collector: it cannot list or address one and infers absence from sensor TTLs.

The owner asked for a persistent connection in a current, standard form (2026-11-10). The constraints that shape the choice:

- **One port.** Everything is served on 443 behind the bundled Caddy (ADR-0014). A second port or a broker is not acceptable.
- **Two collector implementations plus consumers.** The managed collector targets `net6.0` and `net472`; the native collector is C++17 over libcurl and is embedded in the Windows agent, the Linux probe (Rust over the C ABI) and HSMCppWrapper.
- **The JSON wire is a frozen, byte-identical contract** pinned by the conformance corpus and by golden tests in both languages (`aicontext/features/api/wire-contract/feature.md`).
- **The managed collector is a library used by third parties.** Existing deployments must keep working without any change, including against servers and proxies that know nothing about the new transport.

## Decision

1. **Collectors open one long-lived WebSocket session** to the server at `GET /api/sensors/session`, on the same host and port as the HTTP Sensor API, authenticated by the same `Key` / `ClientName` headers on the upgrade request.
2. **Frames carry the existing JSON wire unchanged.** A request frame is a small JSON envelope (`id`, `op`, `body`); the body is byte for byte what the collector would send as the HTTP body of the matching route. The server answers each request with an acknowledgement (`ack`, `status`, then `body` or `error`) whose status is the HTTP status the same request would have received.
3. **The session is bidirectional.** The server pushes directives on it (`op`, `body`): the full state when the session opens, a directive when it changes, and a periodic re-announcement, instead of repeating them on every response.
4. **The HTTP Sensor API stays, unchanged and not deprecated.** It is the fallback whenever a session cannot be opened or drops, the transport for file values, and the only transport of older collectors. Falling back is automatic and needs no operator action.
5. **The server keeps a registry of live sessions**; it is the source of truth for which collectors are connected. The number of sessions is capped by one server-wide setting.
6. **A collector that disconnects raises an alert through the mechanism that already exists**: the `Service alive` sensor's inactivity alert. The session introduces no new alert type, no node-level alert (ADR-0001 stands) and no second sensor for the same incident. The alert fires only when the collector has been off for more than 5 minutes (owner decision, 2026-11-10): the inactivity period of `Service alive` moves from 1 to 5 minutes in both collectors (#1666).
7. **The session is off by default.** A collector opens one only when its option says so. Turning the default on is a separate decision.
8. **The managed collector handles directives internally**; it gains no public directive surface.
9. **Versioning is by WebSocket subprotocol** (`hsm.sensors.v1`). Within a version the envelope grows additively; both sides ignore unknown fields.
10. **A server advertises the capability** with a response header on the HTTP Sensor API, and a collector attempts a session only after seeing it. A collector never probes a server that has not said it supports sessions.

Protocol details, rollout and work breakdown: `docs/initiatives/collector-session-websocket.md`.

## Consequences

- The server gains a push channel and real-time knowledge of connected collectors. A directive reaches a collector when it changes instead of on its next POST. Directives stay level-triggered (re-announced periodically), so a client-side retry that depends on repetition, such as the agent's update retry and its debounce, keeps working unchanged.
- Collectors carry two transports. The cost is contained by using one payload format and the HTTP status as the only result vocabulary for both, and by pinning session behavior with scenarios in the shared conformance corpus.
- The server holds one connection per collector. Per-session cost is measured before rollout and the number of sessions is capped by configuration.
- The disconnect alert does not get faster because of the session, and it gets later on purpose: 5 minutes of silence instead of 1, so a restart or an update pages nobody. Using a session close to bring it forward was considered and not taken.
- A server restart or a proxy configuration reload drops every session; collectors continue over HTTP and reconnect with jitter.
- Delivery remains at-least-once: a frame whose acknowledgement is lost is sent again, as a request whose response is lost is today.
- **Amended twice on 2026-11-10, before this ADR merged, with the result of step 1 (#1662).** The native collector uses libcurl's own WebSocket implementation and carries no RFC 6455 code. Step 1 first went the other way: it framed RFC 6455 itself over a plain libcurl connection, because distribution builds of libcurl on some current LTS releases lack WebSocket and the Linux probe links the distribution's libcurl. The owner rejected self-written protocol code on security grounds, and accepted the price: a native session exists only with libcurl 8.14.0 or newer built with WebSocket. That is the vcpkg builds of this repository (the agent and the CI lanes) and a distribution as recent as Debian 13; it is not yet the published registry port or the Conan recipe, which the initiative's step 4 has to bring along. On any other libcurl the native collector stays on HTTP. The server and the wire are unaffected by this choice.
- No public signature changes in `HSMDataCollector`; the session is turned on by an additive option and is off by default.

## Alternatives Considered

- **Stay with kept HTTP connections (#1653) only**: removes the handshake cost but gives no push and no presence, which is what was asked for.
- **gRPC bidirectional streaming**: the same capabilities, but a heavy native dependency (gRPC, protobuf, abseil) in every consumer of the native collector, weak client support on `net472`, and a schema-first binary wire that would replace the JSON contract and invalidate the byte-identity corpus.
- **SharpRpc** (SoftFx's own RPC library, raised by the owner on 2026-11-10): fits the managed collector (`net472`, `netstandard2.0`, `net8.0`), but it is C# only with no wire specification besides its source, so the native collector, the agent and the Linux probe would need a self-written C++ client, the opposite of the goal. Its transports are raw TCP, TLS over TCP and Unix sockets, so it cannot share port 443 behind the reverse proxy (ADR-0014).
- **Rewriting the native collector in Rust** (same discussion): would give memory safety and ready WebSocket libraries, but it replaces a just-finished core (epic #1093) and changes how the library is delivered to C++ consumers. It is a separate decision, not a transport choice, and is not needed once the protocol code is libcurl's.
- **Server-Sent Events for push plus HTTP POST for data**: one-directional, so acknowledgements and data stay on separate requests; saves little over today and still needs a second mechanism.
- **HTTP long polling for directives**: a request permanently parked per collector with none of the framing or liveness WebSocket provides.
- **SignalR**: a good managed client, but no maintained C++ client; the native side would re-implement the SignalR protocol on top of WebSocket anyway.
- **MQTT or another broker protocol**: requires a broker and a second port, against ADR-0014.
- **HTTP/3 or raw QUIC streams**: not served by the bundled proxy configuration (`h1 h2`), and not available to `net472`.

## References

- `docs/initiatives/collector-session-websocket.md`
- ADR-0001 (alerts belong to sensors), ADR-0014 (single port 443), ADR-0016 (expected failures are not operator errors)
- #1653 / PR #1658 (kept HTTP connection in the native collector; open when this ADR was written)
- #1198 (directive channel), #1659 (managed collector catch-up)
- `aicontext/features/api/wire-contract/feature.md`, `aicontext/features/collector/http-client/feature.md`
