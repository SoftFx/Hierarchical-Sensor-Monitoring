# ADR-0016: Expected MCP tool failures are not operator errors

**Status:** Accepted
**Date:** 2026-11-04 (amended 2026-11-05, #1632: cancelled requests; amended 2026-11-10, #1648: HTTP/2 stream-reset IOException; amended 2026-11-11, #1648 review: top-level `ConnectionResetException`, IOException token caveat)
**Supersedes:** —

---

## Context

The MCP surface (#1391 and phase-2 writes) signals every expected failure — validation, not found, conflict — by throwing `McpException`; the SDK relays it to the caller as `isError=true` and ALSO logs it at `ERROR` as `"\"{tool}\" threw an unhandled exception"` from its `ModelContextProtocol.Server.McpServer` logger (#1613). The SDK's argument binder adds a second arm: caller arguments it cannot bind surface as `ArgumentException` from the same logger, logged the same way. One MCP test session (3.41.8) produced 39 + 5 such entries — every error in the log, all client mistakes answered correctly to the caller. Since ADR-0015 the three text files under `Logs/` are the only log source and `HSM-error-log.txt` is the operator surface (download and grep); a retrying agent fills it with entries needing no operator attention, hiding real errors.

The SDK (NuGet `ModelContextProtocol` 2.2.0–2.3.1) offers no knob for this logging; the alternatives that keep the wire unchanged are a config-level filter or intercepting the logging pipeline in code.

A second path to the same flooding surfaced in production (3.41.9, #1632): a polling MCP client dropping its `POST /mcp` before the response finishes throws `TaskCanceledException` out of the SDK transport once the response has started, and `LoggingExceptionMiddleware` caught it and logged ERROR ("Exception occurred, but response was already started") — ~300 entries per 24h on one install, all client disconnects needing no operator attention, and duplicates: the SDK already records each cancellation once at INFO (`message processing canceled`). The errorfile `when`-filter cannot catch this arm — different exception type, different logger — and classifying by exception type alone would also demote internal cancellations that are genuine server faults.

## Decision

`HSM-error-log.txt` holds failures an operator must look at. Expected MCP tool failures are excluded from it by a `when`-filter on the errorfile rule in `src/server/HSMServer/nlog.config`: it skips `ERROR` events whose exception is `McpException` (the tools' validation/not-found/conflict arm) or any `System.Argument*` type (the SDK argument-binder arm) — in both cases only from the `ModelContextProtocol.Server.McpServer` logger. The entries still reach `HSM-all-log.txt` (the Warn+ rule), so expected-failure debugging keeps its full record. Everything else — any other exception type, or these types from any other logger — is treated as a genuine error and keeps landing in the error log. The caller-visible `isError` result is unchanged.

Cancelled requests (#1632) are handled in code, where the cause is observable: `LoggingExceptionMiddleware` logs at WARN instead of ERROR whenever `ClientAbortDetection` (`src/server/HSMServer/Middleware/ClientAbortDetection.cs`, shared with `ApiExceptionJsonMiddleware` since #1371) recognizes the client going away — an `OperationCanceledException` (incl. `TaskCanceledException`) caught while `HttpContext.RequestAborted.IsCancellationRequested`, or a `ConnectionAbortedException`, the transport itself reporting the dead connection before the token fires (a Kestrel race). The same race exists on the body read (#1648): before the token fires, the transport can kill the request pipe — on HTTP/2 Kestrel aborts it with `IOException("The client reset the request stream.")` (an `RST_STREAM`), and on any HTTP version the socket transport completes the input pipe with `ConnectionResetException` ("Connection reset by peer"), which the body read rethrows top-level (it is itself an `IOException`). So these IOExceptions are client aborts too: the transport's `ConnectionResetException` (top-level or wrapped in another `IOException`), an `IOException` wrapping a `ConnectionAbortedException`, the one matching Kestrel's constant wording, and one caught while the token is cancelled. Every other `IOException` with the request token intact (e.g. a failing disk) stays at ERROR. This applies on any endpoint, with or without the response started. An `OperationCanceledException` with the request token intact (an internal timeout or cancellation) and every other exception type stay at ERROR.

## Consequences

- The error log stops flooding on agent retry loops and client disconnects; genuinely unexpected failures stand out.
- The filter couples to SDK internals by string (logger category + exception type names). An SDK rename silently reverts the routing to the pre-#1613 state — fail-safe (noise returns, nothing is lost), and `NlogConfigTests.ExpectedMcpToolFailuresStayOutOfTheErrorLog` pins the routing end-to-end (real rules, real events, file targets swapped for in-memory capture) so a config regression fails a test.
- The cancelled-request arm is classified by cause (the aborted request token, or the transport's `ConnectionAbortedException`), not by endpoint or logger, so it needs no SDK-internal coupling; `LoggingExceptionMiddlewareTests` pins the edges (WARN only on a client abort; internal cancellations and other exceptions stay ERROR). The #1648 message-string arm matches one Kestrel constant — non-localized and stable since the HTTP/2 transport shipped; an ASP.NET Core rewrite of that wording would fail safe (the entry returns to ERROR, nothing is lost), pinned by the same tests. The transport-exception arms (top-level or wrapped `ConnectionResetException`, wrapped `ConnectionAbortedException`) are type matches with no such coupling.
- A hypothetical HSM bug surfacing as `ArgumentException` from that one logger would be demoted to the general log only — accepted: the logger exists solely for tool dispatch, and its argument failures are client input by construction. Likewise a server fault that races a client disconnect and surfaces as an `OperationCanceledException` — or, on the token arm, as any `IOException` (e.g. a failing disk) — after the token fired would land at WARN — accepted: the disconnect is the likely cause, and the event is still recorded in `HSM-all-log.txt`.

## Alternatives Considered

- Return `isError=true` results instead of throwing: would need an error arm in every typed tool result (breaking the structured-output/output-schema contract of #1553/#1605/#1615) and would replicate the SDK's internal error-text prefix to keep caller messages identical.
- Demote in code (MEL filter or a wrapping `ILoggerProvider`): MEL filters cannot see the exception type, and a provider cannot suppress what the NLog provider logs; both would fight the app's NLog-first logging philosophy (ADR-0011/0015).
- SDK upgrade: 2.3.0–2.3.1 changed nothing about tool-exception logging.

## References

- Issue #1613 (the 3.41.8 test-pass report)
- Issue #1632 (the 3.41.9 cancelled-request flood report)
- `aicontext/features/server/mcp/feature.md` (invariant "Expected tool failures are not operator errors")
- `aicontext/architecture/logging.md` (Error level)
- ADR-0011, ADR-0015 (the `Logs/` text-file contract)
