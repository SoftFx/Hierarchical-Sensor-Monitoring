# Glossary

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

Canonical terms used across HSM codebase and documentation.

| Term | Definition |
|---|---|
| **DataCollector** | Client NuGet library (`HSMDataCollector`) embedded into monitored applications. Collects sensor data and sends it to HSMServer via HTTPS. |
| **HSMServer** | Central server (ASP.NET Core MVC) that receives sensor data, stores history, provides web UI and APIs. |
| **Sensor** | A named data point that produces typed values (bool, int, double, string, timespan, version, rate, bar, file). |
| **Bar Sensor** | Aggregating sensor that collects min/max/mean/count over a time window (BarPeriod) and sends the bar on tick intervals (BarTickPeriod). |
| **Rate Sensor** | Sensor that accumulates a running sum and reports rate (sum/period) on each post interval. |
| **Function Sensor** | Sensor that periodically calls a user-provided `Func<T>` and sends the result. |
| **Instant Sensor** | Sensor where user explicitly calls `AddValue()` to send a value immediately. |
| **Default Sensors** | Built-in sensors (CPU, RAM, disk, threads, GC, service status) auto-created by DataCollector. |
| **Product** | Logical group of sensors on the server, identified by an AccessKey. |
| **Node** | Hierarchical folder in the sensor tree on the server. Path: `Computer/Module/SensorPath`. |
| **CollectorScheduler** | Static singleton timer wheel that fires periodic actions for all sensors. Replaced per-sensor `PeriodicTask`+`CancellationTokenSource`. |
| **ScheduledTask** | A single scheduled action registered with `CollectorScheduler`. Supports delay, period, stop, dispose. |
| **QueueProcessor** | Background task that dequeues sensor values from `ConcurrentQueue` and sends them to the server in batches via `IDataSender`. |
| **DataProcessor** | Orchestrator that owns all queue processors and manages the collector's data pipeline. |
| **MessageDeduplicator** | Bounded cache that deduplicates repeated error messages within a time window. Prevents log spam from recurring exceptions. |
| **Polly Pipeline** | Retry pipeline wrapping HTTP requests with exponential backoff. |
| **LevelDB** | Embedded key-value database used by HSMServer for sensor history and metadata storage. |
| **AccessKey** | API key that authenticates a DataCollector instance to HSMServer. Scoped to a Product. |
| **TTL** | Time-to-live policy on a sensor. If no value arrives within TTL, the sensor transitions to expired state. |
| **SensorStatus** | Status enum: Ok, OffTime, Error. Attached to each sensor value. |
| **CollectorStatus** | Lifecycle enum: Starting, Running, Stopping, Stopped. |
| **PackageSendingInfo** | Result of sending a batch: content size, HTTP response, optional error message. |

## Deprecated terms

| Old term | Replacement | Reason |
|---|---|---|
| `PeriodicTask` | `CollectorScheduler` + `ScheduledTask` | Replaced to fix TickCount overflow, resource leaks, and per-sensor Task allocation. |
