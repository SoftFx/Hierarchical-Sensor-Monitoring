# Feature: CollectorScheduler

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - per-collector timer wheel for all periodic sensor actions

---

## Description

`CollectorScheduler` (implements `ICollectorScheduler`) runs all periodic work for one collector: sensor send loops, bar tick callbacks, disk-prediction sampling, deduplicator cleanup. One background worker per collector instance dispatches due tasks onto the ThreadPool.

**Ownership**: each `DataCollector` constructs its own scheduler, threads it through `DataProcessor.Scheduler`, and disposes it at the end of `Dispose()` (after `_dataProcessor` and `_dataSender`). There is **no process-global scheduler** — two collectors in one process have independent timer wheels. (The earlier static-singleton design is obsolete.)

---

## Business Rules / Invariants

- Bucketed timer wheel: `SortedDictionary<long, LinkedList<ScheduledTask>>` keyed by `NextRunMilliseconds`; worker waits on `ManualResetEventSlim`, woken by schedule/remove or bucket expiry.
- Time source: `Stopwatch.GetTimestamp()`-derived milliseconds — monotonic, immune to the 24.9-day `Environment.TickCount` wraparound that crashed the old per-sensor `PeriodicTask` (historical critical bug; never reintroduce `TickCount`).
- `Schedule(Action | Func<Task>, delay, period, onError)` → `ScheduledTask`. Period > 0, or `Timeout.InfiniteTimeSpan` for one-shot (auto-disposes after the run). Delay >= 0.
- **onError contract**: action exceptions are caught and routed to the `onError` callback; they must never kill the worker loop (the old PeriodicTask died silently on an unhandled exception — second historical bug).
- No overlapping runs of the same task: an `_isRunning` Interlocked guard skips the tick if the previous callback is still executing.
- Catch-up: `Advance()` moves `NextRunMilliseconds` forward by whole periods until it is in the future — an overdue task does not re-bucket into the past.
- `ScheduledTask.StopAsync(waitForCurrentRun)` removes the task and optionally awaits the in-flight run (bounded ~1 s); `Dispose()` = `StopAsync(false)`.
- Worker shutdown on scheduler dispose has a 5 s grace timeout.

## ScheduledTaskHandle (composition layer)

`Threading/ScheduledTaskHandle.cs` wraps one `ScheduledTask` with idempotent `Start(action, delay, period, onError)` / `StopAsync(waitForCurrentRun)`. Sensors compose handles instead of inheriting timer plumbing:

- `MonitoringSensorBase` — send-loop handle;
- `BarMonitoringSensorBase` — additional bar-collect handle;
- `FreeDiskSpacePredictionBase` — sampling-loop handle;
- `MessageDeduplicator` — cleanup handle.

`WindowsServiceStatusSensor` intentionally keeps a raw `ScheduledTask` (needs `CurrentRun`/`IsRunning` to defer `ServiceController` disposal until the in-flight poll completes).

## Key Files

| File | Purpose |
|---|---|
| `Threading/CollectorScheduler.cs` | Timer wheel + worker loop + `ScheduledTask` |
| `Threading/ICollectorScheduler.cs` | Scheduler interface (DI seam) |
| `Threading/ScheduledTaskHandle.cs` | Idempotent start/stop wrapper for one periodic action |

## Tests

`CollectorSchedulerTests.cs`, `ScheduledTaskHandleTests.cs`, `CollectorTimerStressTests.cs` in `src/collector/HSMDataCollector.Tests/`.

## Known Issues / Limitations

- Linear scan within due buckets; fine for typical sensor counts (<1000 tasks).
