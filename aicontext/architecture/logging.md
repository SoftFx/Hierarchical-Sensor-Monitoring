# Logging And Diagnostics

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

## Goals

Logs should help diagnose collector, server, storage, alerting, notification,
and integration issues without leaking sensitive data or flooding production
logs.

## Levels

- `Trace`/`Debug`: verbose diagnostics, queue internals, per-item details.
- `Info`: lifecycle events, startup/shutdown, important successful operations.
- `Warn`: expected refusals, transient failures, degraded but recoverable states.
- `Error`: unexpected failures, lost work, failed delivery, storage exceptions.

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
