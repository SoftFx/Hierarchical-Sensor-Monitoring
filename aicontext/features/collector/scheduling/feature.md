# Feature: CollectorScheduler

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
> Scope: Collector - centralized timer wheel for all periodic sensor actions

---

## Description

`CollectorScheduler` is a static singleton that replaces per-sensor `PeriodicTask` + `CancellationTokenSource` pairs with a single background thread polling a shared task list. It fires sensor read callbacks, bar tick callbacks, disk prediction updates, and deduplicator cleanup.

---

## Business Rules / Invariants

- `CollectorScheduler` is a process-wide singleton (static class). It survives DataCollector restarts.
- Each `ScheduledTask` has a delay (initial wait), period (repeat interval), and error callback.
- Period must be > 0 (or `Timeout.InfiniteTimeSpan` for one-shot tasks).
- Delay cannot be negative.
- If a callback is still running when the next tick arrives, the tick is skipped (no overlapping runs).
- `ScheduledTask.StopAsync(waitForCurrentRun)` removes the task and optionally waits for the current execution to finish.
- `ScheduledTask.Dispose()` calls `StopAsync(waitForCurrentRun: false)` synchronously.
- Time source: `Stopwatch.GetTimestamp()` (monotonic, no TickCount overflow).
- One-shot tasks (`InfiniteTimeSpan` period) auto-dispose after execution.

---

## Key Files

| File | Purpose |
|---|---|
| `Threading/CollectorScheduler.cs` | Static scheduler loop + `ScheduledTask` class |

---

## Data Flow

1. Sensor calls `CollectorScheduler.Schedule(action, delay, period, onError)`
2. Scheduler adds `ScheduledTask` to internal list, signals worker thread
3. Worker thread checks `NextRunMilliseconds` against `GetTickCountMilliseconds()`
4. Due tasks are fired via `Task.Run(ExecuteAsync)` — callbacks run on ThreadPool
5. After execution, `_isRunning` flag is reset (allows next tick)
6. On sensor stop: `ScheduledTask.StopAsync()` sets `_disposed=true`, removes from list

---

## Design Decisions

**Why static singleton?** All DataCollector instances in the same process share one timer thread instead of N threads (one per PeriodicTask). Reduces thread count from O(sensors) to O(1).

**Why Stopwatch instead of Environment.TickCount64?** `Stopwatch.GetTimestamp()` is always monotonic and available on net472. Avoids the 24.9-day TickCount32 wraparound bug that previously crashed sensors.

**Why `Interlocked.Exchange` for `_isRunning`?** Prevents overlapping runs if a callback takes longer than the period. Cheaper than a lock for the common case.

---

## Known Issues / Limitations

- The scheduler thread is never shut down (no `CancellationTokenSource.Cancel()` is ever called in production). This is acceptable because it sleeps when no tasks are registered.
- Task list is a `List<ScheduledTask>` scanned linearly. O(N) per tick where N = active tasks. Acceptable for typical collector with <100 sensors.
