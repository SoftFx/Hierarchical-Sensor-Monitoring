# Feature: Error Handling & Diagnostics

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - exception isolation, deduplication, and diagnostic reporting

---

## Description

DataCollector must never crash the host application. All exceptions from sensor callbacks, scheduler ticks, queue loops, and lifecycle events are caught, deduplicated, and routed to logging and diagnostic sensors.

---

## Business Rules / Invariants

### Exception Isolation

- Every `ScheduledTask` callback is wrapped: exceptions go to the task's `onError` callback, never propagate, never kill the scheduler loop (see `scheduling/feature.md`).
- Sensor `GetValue` exceptions → `HandleException` → deduplicated logging + a value with `status=Error, comment=ex.Message` is still sent (timer sensors).
- Lifecycle events / listeners: per-handler catch; one failing subscriber doesn't block others or the transition.
- `Dispose()` wraps each component's dispose; one failure doesn't prevent the rest.
- `LoggerManager` swallows logger exceptions themselves (try/catch around every `ICollectorLogger` call).

### Error routing map

| Source | Route |
|---|---|
| Sensor exception | `DataProcessor.AddException(path, ex)` → MessageDeduplicator → log + `CollectorErrorsSensor` |
| Queue loop failure (retry-forever) | `DataProcessor.AddQueueLoopError(queueName, ex)` → MessageDeduplicator (prevents one log per `PackageCollectPeriod` for a poison package) |
| Validation reject (NaN/null/bad status) | `DataProcessor.LogDroppedValue(path, reason)` — Debug level |
| Shutdown discard | `LogDiscardedItems(count, queueName)` — Error level |
| Queue overflow / retry drop | `QueueOverflowSensor` (never suppressed — see `data-pipeline/feature.md`) |

### MessageDeduplicator

- Window `ExceptionDeduplicatorWindow` (default 1 h); cache cap `MaxDeduplicatedMessages` (default 1000), oldest-expiry eviction when full.
- First occurrence logged immediately; repeats within the window counted; on expiry flushed once with count ("message N times").
- **Zero window** = invoke the action immediately and `return` (the missing return once caused double logging — regression-guarded).
- Cleanup runs on a `ScheduledTaskHandle` through the per-collector scheduler.
- Lock protects the cache; `_messagesToDelete` is a reused scratch list (safe under the lock).

### Diagnostic Sensors

- `CollectorErrors` — receives deduplicated error messages.
- `QueueOverflow` — overflow + retry-drop counts per queue.
- `PackageProcessTime`, `PackageDataCount`, `PackageContentSize` — send statistics; suppressed after the stop drain boundary (#1075, see `data-pipeline/feature.md`).

---

## Key Files

| File | Purpose |
|---|---|
| `Exceptions/MessageDeduplicator.cs` | Bounded dedup cache with periodic cleanup |
| `Logging/LoggerManager.cs`, `NLogLogger.cs`, `ICollectorLogger.cs`, `LoggerOptions.cs` | Logging stack (NLog default, custom loggers, swallow-all) |
| `Core/DataProcessor.cs` | Routing entry points (AddException, AddQueueLoopError, LogDroppedValue) |
| `Core/DataCollector.cs` | Lifecycle event isolation, dispose isolation |

---

## Native port (C++)

The native core (`src/native/collector`, #1095) mirrors the isolation + dedup contract:
a swallow-all `InvokeIsolated` wrapper guards every host callback (lifecycle listeners,
the pluggable log sink, function-sensor callbacks, the scheduler action) so a throwing
one neither crosses the C ABI boundary nor breaks the collector; a `MessageDeduplicator`
collapses repeated error messages within `exception_deduplicator_window_ms`
(count-suffix flush, capacity + oldest-expiry eviction), and **zero window logs
immediately** (the same regression-guarded contract). Error routing funnels validation
drops and shutdown discards through the deduplicator to the C-ABI log sink. Verified by
`native_logger_*` and `native_lifecycle_listener_exception_is_isolated` unit tests. C ABI:
[`docs/native-collector-c-abi.md`](../../../../docs/native-collector-c-abi.md).

## Known Issues / Limitations

- Deduplication is exact-string; messages differing only by timestamp/address are not grouped.
