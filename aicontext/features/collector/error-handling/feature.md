# Feature: Error Handling & Diagnostics

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
> Scope: Collector - exception isolation, deduplication, and diagnostic reporting

---

## Description

DataCollector must never crash the host application. All exceptions from sensor callbacks, timer ticks, and lifecycle events are caught, deduplicated, and routed to logging and diagnostic sensors. This feature covers the isolation and reporting mechanisms.

---

## Business Rules / Invariants

### Exception Isolation
- Every `PeriodicTask` / `ScheduledTask` callback is wrapped in try-catch. Exceptions invoke `onError` callback, never propagate.
- Lifecycle events (`ToStarting`, `ToRunning`, etc.) iterate `GetInvocationList()` and catch per-handler. One failing handler doesn't prevent others from running.
- `Dispose()` wraps each component's dispose in `DisposeComponent()` which catches and logs.
- Data sender dispose failures are isolated — one handler failing doesn't prevent others from disposing.

### MessageDeduplicator
- Groups identical error messages within a time window (`ExceptionDeduplicatorWindow`, default 1h).
- First occurrence: logged immediately with full message.
- Subsequent occurrences within window: counted silently.
- On window expiry: logged once with count ("message N times").
- Cache bounded by `MaxDeduplicatedMessages` (default 1000). When full, oldest message is evicted.
- Cleanup runs on a periodic timer (same window interval).
- `_messagesToDelete` list is reused (cleared before each scan) to avoid allocation.
- Lock protects `_messageCache` dictionary. Cleanup timer and `AddMessage` both acquire the lock.

### Diagnostic Sensors
- `CollectorErrors` sensor: receives deduplicated error messages.
- `QueueOverflow` sensor: reports overflow counts per queue.
- `PackageProcessTime`, `PackageDataCount`, `PackageSize` sensors: report send statistics.

---

## Key Files

| File | Purpose |
|---|---|
| `Exceptions/MessageDeduplicator.cs` | Bounded deduplication cache with periodic cleanup |
| `Core/DataCollector.cs` | Lifecycle event isolation, dispose isolation |
| `Core/DataProcessor.cs` | IsStarted guard, exception routing via AddException |

---

## Known Issues / Limitations

- `_messagesToDelete` is a shared field reused between `AddMessage` (called under lock) and `Cleanup` (called under lock). Safe because lock is held, but the field being shared is a subtle coupling.
- Deduplication uses exact string match. Two messages differing only in a timestamp or memory address will not be deduplicated.
