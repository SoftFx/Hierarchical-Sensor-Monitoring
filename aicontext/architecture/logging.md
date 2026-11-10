# Logging And Diagnostics

> Owner: shared | Last reviewed: 2026-11-10 | Canonical: yes

## Goals

Logs should help diagnose collector, server, storage, alerting, notification,
and integration issues without leaking sensitive data or flooding production
logs.

## Levels

- `Trace`/`Debug`: verbose diagnostics, queue internals, per-item details.
- `Info`: lifecycle events, startup/shutdown, important successful operations.
- `Warn`: expected refusals, transient failures, degraded but recoverable states,
  a client aborting a request mid-flight.
- `Error`: unexpected failures, lost work, failed delivery, storage exceptions.
  An expected client refusal the caller already receives (e.g. an MCP tool's
  validation/not-found `McpException`, or the SDK argument-binder's
  `ArgumentException` for bad tool arguments) is not an operator error: the
  MCP SDK logs both at ERROR, and `src/server/HSMServer/nlog.config` filters
  exactly those entries (either type, from
  `ModelContextProtocol.Server.McpServer`) out of `HSM-error-log.txt` while
  they still land in `HSM-all-log.txt` (#1613, ADR-0016). The same holds for
  a client dropping the connection before the response finishes (a polling
  MCP client abandoning its `POST /mcp`, surfacing as
  `TaskCanceledException` from the transport):
  `src/server/HSMServer/Middleware/LoggingExceptionMiddleware.cs` logs a
  client abort — an `OperationCanceledException` caught while
  `HttpContext.RequestAborted` is cancelled, a
  `ConnectionAbortedException` (the transport reporting the dead connection
  before the token fires), or an `IOException` of the request-body read
  (Kestrel's HTTP/2 stream reset, `The client reset the request stream.`, or
  one caught while the token is cancelled; see `ClientAbortDetection`) — at
  WARN; an internal cancellation (token intact) and every other exception
  type — including every other `IOException`, e.g. a failing disk — stay at
  ERROR (#1632, #1648, ADR-0016).

## Required Context

Include stable context where useful:

- sensor path, module, environment, or dashboard id;
- operation name;
- retry/backoff count;
- alert/notification target type, with secrets redacted;
- exception type and message for unexpected failures.

## Sensitive Data

Do not log raw:

- access keys, tokens, passwords, private URLs with credentials;
- Telegram/email credentials;
- large payloads, files, or full serialized sensor histories.

Prefer redaction, hashes, counts, and short summaries.

## Anti-Spam

- Avoid unbounded logs inside polling, retry, queue, and per-sensor loops.
- Group repeated identical failures when they can occur at high volume.
- Keep one representative exception and a count/period summary.
