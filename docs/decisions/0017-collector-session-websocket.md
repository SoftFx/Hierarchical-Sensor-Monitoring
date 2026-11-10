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
- **The managed collector is a library used by third parties.** Existing deployments must keep working without any change for as long as the transition lasts, including against servers and proxies that know nothing about the new transport. Ending it is a breaking change and is made as one: a major version and a deprecation notice ahead of it (decision 4).

## Decision

1. **Collectors open one long-lived WebSocket session** to the server at `GET /api/sensors/session`, on the same host and port as the HTTP Sensor API, authenticated by the same `Key` / `ClientName` headers on the upgrade request.
2. **Frames carry the existing JSON wire unchanged.** A request frame is a small JSON envelope (`id`, `op`, `body`); the body is byte for byte what the collector would send as the HTTP body of the matching route. The server answers each request with an acknowledgement (`ack`, `status`, then `body` or `error`) whose status is the HTTP status the same request would have received.
3. **The session is bidirectional.** The server pushes directives on it (`op`, `body`): the full state when the session opens, a directive when it changes, and a periodic re-announcement, instead of repeating them on every response.
4. **The session replaces the HTTP Sensor API for collectors; HTTP is kept only for a transition period** (owner decision, 2026-11-10, replacing the first text of this point, which kept HTTP for good as the automatic fallback). A collector uses one transport or the other, chosen by a setting. No session logic depends on HTTP: there is no fallback to it and no discovery through it. HTTP is retired for collectors once the session carries everything, file values included, on a date the owner sets.
5. **The server keeps a registry of live sessions**; it is the source of truth for which collectors are connected. The number of sessions is capped by one server-wide setting, and one client address may hold at most 20 of them (owner decision, 2026-11-10): with no fallback, a refused collector cannot send, so one misbehaving host must not be able to take every slot.
6. **A collector that disconnects raises an alert through the mechanism that already exists**: the `Service alive` sensor's inactivity alert. The session introduces no new alert type, no node-level alert (ADR-0001 stands) and no second sensor for the same incident. When that alert fires is not this ADR's decision: the owner asked for it only after more than 5 minutes off, which changes a default of every collector, session or not, and is carried and recorded by #1666.
7. **The session is off by default.** A collector opens one only when its option says so. Turning the default on is a separate decision.
8. **The managed collector handles directives internally**; it gains no public directive surface.
9. **Versioning is by WebSocket subprotocol** (`hsm.sensors.v1`). Within a version the envelope grows additively; both sides ignore unknown fields.
10. **There is no discovery.** A collector set to use a session connects; it does not learn about sessions from HTTP answers. (The first text of this point had the server advertise the capability in an HTTP response header. It went with the fallback.)

Protocol details, rollout and work breakdown: `docs/initiatives/collector-session-websocket.md`.

This ADR quotes results of steps 1 and 2 (the libcurl version floor, the measured cost of a session). They come from PRs #1663 and #1665, which were still open when this was written; if either changes before it merges, the figures here change with it.

## Consequences

- The server gains a push channel and real-time knowledge of connected collectors. A directive reaches a collector when it changes instead of on its next POST. Directives stay level-triggered (re-announced periodically), so a client-side retry that depends on repetition, such as the agent's update retry, keeps working. It gets slower on a session: about once a minute today, once per re-announcement (5 minutes) there.
- Collectors carry two transports for the length of the transition, and one after it. The cost is contained by using one payload format and the HTTP status as the only result vocabulary for both, and by pinning session behavior with scenarios in the shared conformance corpus.
- A collector on a session has no second path. When no session can be opened it delivers nothing, and what it loses is bounded by its queues, as with an unreachable server today. New causes of that state exist: a proxy that breaks upgrades, a server with sessions off, the session cap, a libcurl too old for the native client.
- Retiring HTTP stops every collector that has not moved, older library versions in third-party applications above all. Existing deployments keep working unchanged only until then.
- The server holds one connection per collector. The number of sessions is capped by configuration; step 2 (#1664) measured about 22 KiB of managed memory per idle session.
- The disconnect alert does not get faster because of the session. Using a session close to bring it forward was considered and not taken: the owner wants it later, not sooner (#1666).
- A server restart or a proxy configuration reload drops every session; collectors reconnect with jitter and deliver what queued up meanwhile.
- Delivery remains at-least-once: a frame whose acknowledgement is lost is sent again, as a request whose response is lost is today.
- **Amended twice on 2026-11-10, before this ADR merged, with the result of step 1 (#1662).** The native collector uses libcurl's own WebSocket implementation and carries no RFC 6455 code. Step 1 first went the other way: it framed RFC 6455 itself over a plain libcurl connection, because distribution builds of libcurl on some current LTS releases lack WebSocket and the Linux probe links the distribution's libcurl. The owner rejected self-written protocol code on security grounds, and accepted the price: a native session exists only with libcurl 8.14.0 or newer built with WebSocket. That is the vcpkg builds of this repository (the agent and the CI lanes) and a distribution as recent as Debian 13; it is not yet the published registry port or the Conan recipe, which the initiative's step 4 has to bring along. On any other libcurl the native collector has no session; while HTTP lasts it can stay on that. The server and the wire are unaffected by this choice.
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
