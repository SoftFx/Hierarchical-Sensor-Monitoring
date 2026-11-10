# ADR-0017: Collectors hold a persistent session with the server over WebSocket on 443

**Status:** Accepted
**Date:** 2026-11-10
**Supersedes:** —

---

## Context

A collector delivers data with one HTTP request per batch. Both collectors keep the underlying connection between requests (#1653), but there is no session: the server cannot send anything to a collector except by piggybacking `X-Hsm-Directive` headers on a data response (#1198), and it learns that a collector is gone only when a sensor TTL expires.

The owner asked for a persistent connection in a current, standard form (2026-11-10). The constraints that shape the choice:

- **One port.** Everything is served on 443 behind the bundled Caddy (ADR-0014). A second port or a broker is not acceptable.
- **Two collector implementations plus consumers.** The managed collector targets `net6.0` and `net472`; the native collector is C++17 over libcurl and is embedded in the Windows agent, the Linux probe (Rust over the C ABI) and HSMCppWrapper.
- **The JSON wire is a frozen, byte-identical contract** pinned by the conformance corpus and by golden tests in both languages (`aicontext/features/api/wire-contract/feature.md`).
- **The managed collector is a library used by third parties.** Existing deployments must keep working without any change, including against servers and proxies that know nothing about the new transport.

## Decision

1. **Collectors open one long-lived WebSocket session** to the server at `GET /api/sensors/session`, on the same host and port as the HTTP Sensor API, authenticated by the same `Key` / `ClientName` headers on the upgrade request.
2. **Frames carry the existing JSON wire unchanged.** A frame is a small JSON envelope (`id`, `op`, `body`); the body is byte for byte what the collector would send as the HTTP body of the matching route. The server acknowledges each frame with the HTTP status the same request would have received.
3. **The session is bidirectional.** The server pushes directives on it when they change, instead of repeating them on every response.
4. **The HTTP Sensor API stays, unchanged and not deprecated.** It is the fallback whenever a session cannot be opened or drops, the transport for file values, and the only transport of older collectors. Falling back is automatic and needs no operator action.
5. **The server keeps a registry of live sessions**; it is the source of truth for which collectors are connected.
6. **Versioning is by WebSocket subprotocol** (`hsm.sensors.v1`). Within a version the envelope grows additively; both sides ignore unknown fields.

Protocol details, rollout and work breakdown: `docs/initiatives/collector-session-websocket.md`.

## Consequences

- The server gains a push channel and real-time knowledge of connected collectors; the directive debounce and re-send-on-every-POST workarounds become unnecessary for clients on a session.
- Collectors carry two transports. The cost is contained by using one payload format and one status-based decision table for both, and by pinning session behavior with transcripts shared by the two drivers.
- The server holds one connection per collector. Per-session cost is measured before rollout and the number of sessions is capped by configuration.
- A server restart or a proxy configuration reload drops every session; collectors continue over HTTP and reconnect with jitter.
- Delivery remains at-least-once: a frame whose acknowledgement is lost is sent again, as a request whose response is lost is today.
- The native collector's libcurl dependency grows the `websockets` feature. If that API proves unsuitable, the native side frames RFC 6455 itself over a libcurl connection; the server and the wire do not change.
- No public signature changes in `HSMDataCollector`; the session is selected by an additive option and ships opt-in first.

## Alternatives Considered

- **Stay with kept HTTP connections (#1653) only**: removes the handshake cost but gives no push and no presence, which is what was asked for.
- **gRPC bidirectional streaming**: the same capabilities, but a heavy native dependency (gRPC, protobuf, abseil) in every consumer of the native collector, weak client support on `net472`, and a schema-first binary wire that would replace the JSON contract and invalidate the byte-identity corpus.
- **Server-Sent Events for push plus HTTP POST for data**: one-directional, so acknowledgements and data stay on separate requests; saves little over today and still needs a second mechanism.
- **HTTP long polling for directives**: a request permanently parked per collector with none of the framing or liveness WebSocket provides.
- **SignalR**: a good managed client, but no maintained C++ client; the native side would re-implement the SignalR protocol on top of WebSocket anyway.
- **MQTT or another broker protocol**: requires a broker and a second port, against ADR-0014.
- **HTTP/3 or raw QUIC streams**: not served by the bundled proxy configuration (`h1 h2`), and not available to `net472`.

## References

- `docs/initiatives/collector-session-websocket.md`
- ADR-0014 (single port 443), ADR-0016 (expected failures are not operator errors)
- #1653 / PR #1658 (kept HTTP connection in the native collector)
- #1198 (directive channel), #1659 (managed collector catch-up)
- `aicontext/features/api/wire-contract/feature.md`, `aicontext/features/collector/http-client/feature.md`
