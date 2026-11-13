# ADR-0019: The collector session is the collectors' default transport

**Status:** Accepted
**Date:** 2026-11-13
**Supersedes:** ADR-0017, point 7 ("The session is off by default")

---

## Context

ADR-0017 brought the collector session (one WebSocket per collector on 443) and kept it off by default: a collector opened one only when its transport setting said so, and turning the default on was left as a decision of its own (step 8 of epic #1660). By 2026-11-13 the session carries everything a collector sends: values, registrations and commands (step 3), file values (step 2b), server directives (step 5), with the `Service alive` alert after 5 minutes off as the disconnect alert (step 6). Both collectors, the agent and the Linux probe can use it.

On 2026-11-13 the owner decided to make it the default now, with the 3.43.0 server release, rather than after the garage soak of step 7.

## Decision

1. **`session` is the default transport** of the managed collector (`CollectorOptions.Transport`, from 4.0.0), the HSM Agent (`server.transport`, from 0.6.0) and the Linux probe (`hsm.transport`, from 0.13.0). The native collector library has no default of its own (a host calls `UseHttpTransport` or `UseSessionTransport`); its hosts above carry the default.
2. **`http` stays selectable** and the HTTP Sensor API stays on the server (ADR-0017 point 4). The no-fallback rule stands: a collector on a session does not switch to HTTP by itself.
3. **The flip is a breaking change** and ships as one: a major version of the managed collector (4.0.0), a minor version of the agent (0.6.0), and a notice in the release notes.
4. **The managed collector's default is HTTP on a host where no session can exist** (owner decision, 2026-11-13): .NET Framework with `AllowUntrustedServerCertificate = true` (ClientWebSocket has no per-connection certificate callback there) or an OS without a WebSocket client (Windows 7). The collector picks this once, when it is created, and says why at every Start. It is the choice of a default, not a fallback: a collector whose caller set `Session` stays on a session and reports why it cannot run, and a collector on a session never switches to HTTP.
5. The legacy C++ wrapper ABI (`src/wrapper`, the aggregator) has no transport setting and stays on HTTP.

## Consequences

- A collector with the default needs a server with sessions: **HSMServer 3.43.0 or later**, with `CollectorSessions.MaxSessions` above zero. Against an older server it sends nothing; it must be set to `http` explicitly (or the server upgraded first).
- A proxy between collector and server must pass WebSocket upgrades. Caddy in the shipped compose does.
- The server admits at most 20 sessions per client address (ADR-0017 point 5). Collectors behind one NAT address beyond that are refused and, with no fallback, send nothing until a slot frees; such sites set `http` or raise the setting.
- The managed collector on .NET Framework cannot open a session with `AllowUntrustedServerCertificate = true` nor on an OS without a WebSocket client (Windows 7); with the default it sends over HTTP there (point 4) and says why at every Start.
- The native session needs libcurl 8.14.0 or newer with WebSocket (vcpkg builds and Debian 13 have it; Ubuntu 24.04 and the Conan build do not, and say so at Start).
- An agent updated by its server bundle comes from a 3.43.0+ server, so it always meets a server with sessions. The configs that the server's bundle writes carry no `transport` key and so take the default.
- A collector with a custom `DataSender` is unaffected; Start notes an ignored setting only when `Session` was set explicitly.

## Alternatives Considered

- Keep the default `http` until the garage soak (step 7) is done: the original plan; the owner chose not to wait.
- Strict `session` everywhere, including hosts where none can exist: such hosts would send nothing after the upgrade until someone set `http` by hand. Rejected by the owner in favour of point 4.

## References

- ADR-0017, epic #1660 (step 8), `docs/initiatives/collector-session-websocket.md`
- Issue #1733 (the 3.43.0 server release)
