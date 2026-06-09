# Feature: Sensor Types

> Owner: collector | Last reviewed: 2026-06-09 | Canonical: yes
> Scope: Collector - all sensor types available in the DataCollector API

---

## Description

DataCollector provides several sensor types that differ in how values are produced and aggregated. Users create sensors through the `IDataCollector` interface and either push values manually or rely on periodic automatic reads.

---

## Sensor Types

### Instant Sensors (`SensorInstant<T>`)
User explicitly calls `AddValue(value)`. Value is sent immediately (queued for next batch).
Types: bool, int, double, string, timespan, version.

### Monitoring (Periodic) Sensors (`MonitoringSensorBase<T>`)
Registered with `CollectorScheduler`. Periodically calls `GetValue()` and sends result.
`PostDataPeriod` controls the interval.

### Bar Sensors (`BarMonitoringSensorBase<BarType, T>`)
Aggregate sensors. Collect min/max/mean/count over a `BarPeriod` window.
Two timers: `BarTickPeriod` (check if bar should close) and `PostDataPeriod` (send current bar).
Types: int bar, double bar.

### Rate Sensors (`MonitoringRateSensor`)
User calls `AddValue(double)` to accumulate a sum. Periodically reports `sum / period_seconds`.
Uses atomic CAS loop (`Interlocked.CompareExchange`) for thread-safe accumulation.
`Interlocked.Exchange` to atomically read-and-reset sum on each period.

### Function Sensors (`FunctionSensorInstant<T>`)
User provides `Func<T>`. Periodically called and result sent.
`ValuesFunctionSensorInstant<T, U>` — user pushes values into a bounded `ConcurrentQueue` cache; function receives the cache contents. Cache count tracked via `Interlocked` (not `ConcurrentQueue.Count`).

### File Sensors (`FileSensorInstant`)
Send file contents as sensor values. File size bounded by `MaxFileSizeBytes` (default 10MB).
Reads file asynchronously with proper stream handling.

---

## Business Rules / Invariants

- `PostDataPeriod` must be > 0 (validated in constructor).
- `BarPeriod` and `BarTickPeriod` must be > 0.
- `MaxCacheSize` for ValuesFunctionSensor must be > 0.
- `MaxFileSizeBytes` for FileSensor must be > 0; files exceeding limit are rejected with log error.
- All sensor constructors validate options eagerly (fail-fast).
- `StopAsync()` stops the scheduled task and optionally waits for current run.
- `RestartTimerAsync()` stops then re-registers with new period. Waits for current run to finish before restarting.

### Bar sensor: do not roll the bar without confirming the send happened

Bar sensors have **two** scheduled handles that BOTH publish the current bar through a shared `SendValueAction` codepath protected by an `_sendValueInProgress` reentrancy guard:

- `_sendHandle` runs `SendValueAction` every `PostDataPeriod` (periodic snapshot).
- `_collectHandle` runs `CollectBar` → `CheckCurrentBar`, which on `bar.CloseTime < now` sends the closed bar and then calls `BuildNewBar()` to start the next aggregation window.

If `CheckCurrentBar` rolls the bar **unconditionally** after calling `SendValueAction`, this interleaving silently loses the closed bar's aggregated data:

1. Periodic send enters `SendValueAction`, sets `_sendValueInProgress=1`, but is not yet at the `_lockBar` snapshot step inside `BuildSensorValue → GetValue`.
2. Collect tick acquires `_lockBar`, sees the bar past `CloseTime`, calls `SendValueAction` → guard is held → returns immediately, then `BuildNewBar()` resets `_internalBar`, releases the lock.
3. Periodic send finally takes `_lockBar`, finds the freshly-reset bar (`Count == 0`), returns null. Nothing is sent.

**Invariant**: the roll (whatever resets the aggregator state — `BuildNewBar`, mean/min/max reset, count zeroing) must be conditional on the send actually happening. Two correct shapes:

- **Conditional roll**: `SendValueAction` returns a `bool` (or `TrySendValue`) indicating whether the snapshot was published, and the collect path only rolls on `true`. When `false` is returned (guard held), the roll is deferred — either the periodic send finishes its in-flight snapshot first, or the next collect tick rolls cleanly after the guard releases.
- **Inline snapshot+roll under the lock, no shared guard**: the collect path snapshots the bar and rolls atomically inside the bar lock, then publishes the snapshot outside the lock. The periodic send remains gated only against same-handle reentrancy.

C# reference: `Sensors/MonitoringSensorBaseT.TrySendValue()` returns `bool`; `Sensors/BarSensors/BarMonitoringSensorBase.CheckCurrentBar` uses `if (TrySendValue()) BuildNewBar();`. Regression test: `CheckCurrentBar_defers_roll_when_send_guard_is_held` in `CollectorQueueShutdownTests.cs`.

This invariant has no C++ analogue today because the native collector spike (`src/native/collector_spike/`) currently exposes only instant sensors. Any future port of bar/aggregator sensors must encode one of the two shapes above from the start.

---

## Key Files

| File | Purpose |
|---|---|
| `Sensors/MonitoringSensorBaseT.cs` | Base for periodic sensors (schedule, stop, restart) |
| `Sensors/BarSensors/BarMonitoringSensorBase.cs` | Bar aggregation logic |
| `Sensors/InstantSensors/MonitoringCounterSensor.cs` | Rate sensor with atomic accumulation |
| `Sensors/InstantSensors/FunctionSensorInstant.cs` | Function and values-function sensors |
| `Sensors/InstantSensors/FileSensorInstant.cs` | File sensor with size limits |

---

## Known Issues / Limitations

- Rate sensor `_lastComment` and `_lastStatus` are not thread-safe (simple field writes). Low risk since they're only read on the timer thread, but technically a data race.
