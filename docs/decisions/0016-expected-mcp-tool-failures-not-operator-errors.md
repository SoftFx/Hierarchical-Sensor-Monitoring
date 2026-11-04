# ADR-0016: Expected MCP tool failures are not operator errors

**Status:** Accepted
**Date:** 2026-11-04
**Supersedes:** —

---

## Context

The MCP surface (#1391 and phase-2 writes) signals every expected failure — validation, not found, conflict — by throwing `McpException`; the SDK relays it to the caller as `isError=true` and ALSO logs it at `ERROR` as `"\"{tool}\" threw an unhandled exception"` from its `ModelContextProtocol.Server.McpServer` logger (#1613). The SDK's argument binder adds a second arm: caller arguments it cannot bind surface as `ArgumentException` from the same logger, logged the same way. One MCP test session (3.41.8) produced 39 + 5 such entries — every error in the log, all client mistakes answered correctly to the caller. Since ADR-0015 the three text files under `Logs/` are the only log source and `HSM-error-log.txt` is the operator surface (download and grep); a retrying agent fills it with entries needing no operator attention, hiding real errors.

The SDK (NuGet `ModelContextProtocol` 2.2.0–2.3.1) offers no knob for this logging; the alternatives that keep the wire unchanged are a config-level filter or intercepting the logging pipeline in code.

## Decision

`HSM-error-log.txt` holds failures an operator must look at. Expected MCP tool failures are excluded from it by a `when`-filter on the errorfile rule in `src/server/HSMServer/nlog.config`: it skips `ERROR` events whose exception is `McpException` (the tools' validation/not-found/conflict arm) or any `System.Argument*` type (the SDK argument-binder arm) — in both cases only from the `ModelContextProtocol.Server.McpServer` logger. The entries still reach `HSM-all-log.txt` (the Warn+ rule), so expected-failure debugging keeps its full record. Everything else — any other exception type, or these types from any other logger — is treated as a genuine error and keeps landing in the error log. The caller-visible `isError` result is unchanged.

## Consequences

- The error log stops flooding on agent retry loops; genuinely unexpected failures stand out.
- The filter couples to SDK internals by string (logger category + exception type names). An SDK rename silently reverts the routing to the pre-#1613 state — fail-safe (noise returns, nothing is lost), and `NlogConfigTests.ExpectedMcpToolFailuresStayOutOfTheErrorLog` pins the routing end-to-end (real rules, real events, file targets swapped for in-memory capture) so a config regression fails a test.
- A hypothetical HSM bug surfacing as `ArgumentException` from that one logger would be demoted to the general log only — accepted: the logger exists solely for tool dispatch, and its argument failures are client input by construction.

## Alternatives Considered

- Return `isError=true` results instead of throwing: would need an error arm in every typed tool result (breaking the structured-output/output-schema contract of #1553/#1605/#1615) and would replicate the SDK's internal error-text prefix to keep caller messages identical.
- Demote in code (MEL filter or a wrapping `ILoggerProvider`): MEL filters cannot see the exception type, and a provider cannot suppress what the NLog provider logs; both would fight the app's NLog-first logging philosophy (ADR-0011/0015).
- SDK upgrade: 2.3.0–2.3.1 changed nothing about tool-exception logging.

## References

- Issue #1613 (the 3.41.8 test-pass report)
- `aicontext/features/server/mcp/feature.md` (invariant "Expected tool failures are not operator errors")
- `aicontext/architecture/logging.md` (Error level)
- ADR-0011, ADR-0015 (the `Logs/` text-file contract)
