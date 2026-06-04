# Feature: Sensor Types

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
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
