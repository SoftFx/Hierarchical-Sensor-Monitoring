# DataCollector Overview

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes

## Purpose

`HSMDataCollector` is a NuGet library embedded into .NET applications to collect monitoring data and send it to HSMServer. It targets both `net8.0` and `net472`.

## Architecture

```
User Application
    |
    v
DataCollector (public API)
    |
    +-- SensorsStorage (creates/manages sensors)
    |       |
    |       +-- MonitoringSensorBase<T> (bar, rate, function sensors)
    |       +-- SensorInstant<T> (instant, file sensors)
    |       +-- Default sensors (CPU, RAM, disk, threads, GC, service status)
    |
    +-- DataProcessor (data pipeline orchestrator)
    |       |
    |       +-- DataQueueProcessor (regular sensor values)
    |       +-- PriorityDataQueueProcessor (priority values)
    |       +-- FileQueueProcessor (file sensor values)
    |       +-- CommandQueueProcessor (server commands)
    |       +-- MessageDeduplicator (error deduplication)
    |
    +-- ICollectorScheduler (per-collector instance, bucketed timer wheel)
    |       |
    |       +-- ScheduledTask (per-sensor periodic action)
    |
    +-- HsmHttpsClient (HTTP transport)
            |
            +-- Polly retry pipeline
            +-- DataHandlers / CommandHandler (endpoint routing)
```

## Lifecycle

```
[Stopped] --Start()--> [Starting] --InitAsync()--> [Running]
[Starting] --error--> [Stopped]
[Starting] --Stop()--> [Stopping]
[Running] --Stop()---> [Stopping] --StopAsync()--> [Stopped]
[Any non-Disposed] --Dispose()--> [Disposed] (terminal)
```

Allowed transitions:

| From | To | Trigger |
|---|---|---|
| Stopped | Starting | `Start()` |
| Starting | Running | start completed |
| Starting | Stopping | `Stop()` during start |
| Starting | Stopped | error during start |
| Running | Stopping | `Stop()` |
| Stopping | Stopped | stop completed |
| Any except Disposed | Disposed | `Dispose()` |

State is managed by `CollectorLifecycle` (internal), shared between `DataCollector` and `DataProcessor`.

Key rules:
- `Dispose()` works from any state, is idempotent, never throws. Terminal state is `Disposed`.
- `Start()` is rejected if already running or disposed
- `Stop()` can be called during Starting (cancels initialization)
- Values are dropped (not queued) while collector is stopped
- Values CAN be enqueued during Stopping (sensors flush their last values)
- Lifecycle events (`ToStarting`, `ToRunning`, etc.) isolate subscriber exceptions
- `Dispose()` from active states fires `ToStopping`/`ToStopped` for backward compatibility

### Sensor registration (two-phase contract)

Sensor registration is gated by `CollectorLifecycle.CanRegisterSensors` and follows two phases:

| Phase | Status | `CanRegisterSensors` | Behavior of `Register` |
|---|---|---|---|
| Configuration | Stopped | true | Sensor is **queued**. It is initialized and started by `SensorsStorage.InitAsync`/`StartAsync` on the next `Start()`. |
| Operational | Starting / Running | true | Sensor is added and **started immediately** (fire-and-forget `InitAndStart`). |
| Shutdown | Stopping | false | **Rejected** — logged, disposed, not added. |
| Terminal | Disposed | false | **Rejected** — logged, disposed, not added. |

`CanRegisterSensors` = `!disposed && status != Stopping` (i.e. the union of configuration and operational phases). `CanStartNewSensors` = `!disposed && (Starting || Running)` — the narrower gate that decides immediate start vs. deferred queueing. The registration decision is serialized with collector stop/dispose transitions, and dynamically started sensors are tracked so shutdown waits for their init/start path before stopping sensor storage.

Rejection is non-throwing: the rejected sensor is disposed and returned inert, so late `collector.CreateXxxSensor(...)` / `collector.Windows.AddXxx(...)` calls during shutdown do not crash the host. Consumers can pre-check `DataCollector.IsAcceptingRegistrations` (or the optional `ICollectorRegistrationState` capability), which mirrors `CanRegisterSensors`.

Registration is idempotent on path: registering a path that already exists returns the existing sensor without starting a duplicate.

### Public API surface (portability-oriented)

Two additive, portable-friendly APIs sit alongside the legacy surface (nothing removed):

- **`ILifecycleListener`** — observer interface (`OnStarting`/`OnRunning`/`OnStopping`/`OnStopped`) registered via `DataCollector.AddLifecycleListener(...)`, the `IDataCollector` extension method, or the optional `ILifecycleObservableCollector` capability. The extension delegates only when the collector exposes that optional capability; custom `IDataCollector` implementations without it are left unchanged. This is the portable equivalent of the `ToStarting`/`ToRunning`/`ToStopping`/`ToStopped` C# events (which still fire). Listeners are invoked from `LogAndRaise` under `_opLock` with per-listener exception isolation; only transitions after registration are delivered (no replay).
- **Fluent sensor builders** — `collector.InstantSensor<T>(path)`, `BarSensor<T>(path)`, `RateSensor(path)` extension methods returning fluent builders whose `Build()` dispatches to the existing options-based `CreateXxx(path, options)` factory methods. Implemented as extension methods, so the `IDataCollector` interface is unchanged; the legacy per-type overloads remain. The builders give ports a single `path → type → kind → options → Build` mental model instead of 100+ overloads.

### Data gating

`CanAcceptData` returns true during `Starting`, `Running`, and `Stopping` states. This allows sensors to flush pending values during stop. `CanStartNewSensors` returns true only during `Starting` and `Running` (and not when disposed).

Note the asymmetry between `Status` and `CanAcceptData` after `Dispose()`:
- `Status` reports `Disposed` immediately when `Dispose()` is entered.
- `CanAcceptData` is computed from the *internal* (pre-dispose) status. While the stop is still in flight it returns true so sensors can flush final values. Once `CompleteStop` fires (`internal _status = Stopped`), `CanAcceptData` returns false.
- Values enqueued after the sender is disposed are unreachable. The window is small (between `_lifecycle.CompleteStop()` and `_dataSender.Dispose()`); sensors should not call `SendValue` after subscribing to `ToStopped`.

### Lifecycle event ordering

`DataCollector` serializes the `(state-transition, lifecycle-event-raise)` pair under `_opLock` so that subscribers observe events in the same order as the underlying status changes. Subscribers should not block in handlers — `_opLock` is held while handlers run, which can stall concurrent `Start`/`Stop`/`Dispose` calls.

### Dispose racing Stop

If `Dispose()` is called while a `Stop()` is in flight, the disposer captures the in-flight processor stop task (`_currentProcessorStopTask`) and awaits it instead of issuing a duplicate `StopAsync`. The original `Stop()` is responsible for firing `ToStopped` and calling `CompleteStop`. `_dataSender.Dispose()` is only invoked after the in-flight stop completes, preventing `ObjectDisposedException` in queue processors that are still draining.

If `Stop()` or `Dispose()` races with `Start()` while sensor initialization is already in flight, the stopping path waits for `_currentStartInitTask` before stopping and disposing components. It does not wait for the user-supplied pre-init `customStartingTask`; if stop/dispose happens while that task is pending, the later `Start()` continuation observes that lifecycle is no longer starting/running and exits without entering initialization.

### Queue state machine

Each `QueueProcessorBase` maintains its own `QueueState` (Stopped/Running/Stopping). If `StopAsync` times out (IDataSender ignores cancellation), the queue stays in `Stopping` and blocks subsequent `Start()` until the background task completes. As a defensive measure, `Start()` will reset and restart a queue whose `_task` exited unexpectedly while `_state` is still `Running` — this only fires if a subclass overrides `ProcessingLoop` and breaks the "loop until cancellation" contract.

### Scheduler ownership

Each `DataCollector` owns its own `CollectorScheduler` instance (implementing `ICollectorScheduler`). The scheduler is constructed in `DataCollector`'s constructor, threaded through `DataProcessor.Scheduler`, and disposed at the end of `Dispose()` after `_dataProcessor` and `_dataSender`. Sensors and `MessageDeduplicator` schedule periodic work through this injected instance — there is no process-global scheduler. Two collectors in the same process have independent timer wheels and worker tasks.

### Sensor scheduling via composition

Sensors do not own scheduling boilerplate inline. The "schedule one periodic action; start/stop/restart it once" lifecycle is extracted into `ScheduledTaskHandle` (a composable wrapper over a single `ScheduledTask`). Sensors *compose* one handle per periodic action rather than inheriting the timer plumbing:
- `MonitoringSensorBase` composes a send-loop handle (the periodic `SendValueAction`).
- `BarMonitoringSensorBase` composes a second handle for the bar-collect loop, on top of the inherited send handle.
- `FreeDiskSpacePredictionBase` composes a handle for its disk-speed sampling loop.

`ScheduledTaskHandle.Start` and `StopAsync` are idempotent and thread-safe. `WindowsServiceStatusSensor` deliberately keeps a raw `ScheduledTask` because it needs `ScheduledTask.CurrentRun`/`IsRunning` to defer `ServiceController` disposal until the in-flight run completes — a specialization the simple handle intentionally does not expose.

### Platform metric sources

The Windows-only `System.Diagnostics.PerformanceCounter` API is isolated behind `IPerformanceCounterFactory` / `IPerformanceCounter`. `WindowsSensorBase` (CPU/RAM/disk bars) and `BaseSocketsSensor` (TCP connection counts) depend on the factory via an `internal virtual PerformanceCounterFactory` seam that defaults to `WindowsPerformanceCounterFactory` (the only place real `PerformanceCounter`/`PerformanceCounterCategory` calls live). Tests substitute a fake factory, so these sensors are now unit-testable on any OS.

Linux default sensors read metrics from the kernel directly — no external process spawning:
- `UnixTotalCpu` → `/proc/stat` (busy % from the idle/total delta across collect ticks, via `ProcStatCpuUsage`).
- `UnixFreeRamMemory` → `/proc/meminfo` (`MemAvailable`, via `ProcMeminfo`).
- `UnixDiskInfo` → managed `DriveInfo("/").AvailableFreeSpace` (statvfs) instead of `df`.
- Process sensors (`UnixProcessCpu`/`Memory`/`ThreadCount`) already used the managed `System.Diagnostics.Process` API.

The parsing logic (`ProcStat`, `ProcMeminfo`) is split from file I/O so it is unit-tested with sample text on any OS. The old `top`/`free`/`df` bash-shelling (`BashCommandExtension`) has been removed.

## Features

| Feature | Folder | Description |
|---|---|---|
| Scheduling | [`scheduling/`](./scheduling/feature.md) | CollectorScheduler timer wheel, ScheduledTask |
| Data Pipeline | [`data-pipeline/`](./data-pipeline/feature.md) | Queue processors, batching, overflow handling |
| HTTP Client | [`http-client/`](./http-client/feature.md) | HTTPS transport, Polly retry, TLS configuration |
| Sensors | [`sensors/`](./sensors/feature.md) | Sensor types: bar, rate, function, instant, file |
| Default Sensors | [`default-sensors/`](./default-sensors/feature.md) | Built-in system metrics (CPU, RAM, disk, threads, etc.) |
| Error Handling | [`error-handling/`](./error-handling/feature.md) | Exception isolation, MessageDeduplicator, diagnostic sensors |

## Thread Safety Model

- All public sensor methods (`AddValue`, `SendValue`) can be called from any thread
- `CollectorScheduler` (per-collector instance) fires callbacks on ThreadPool threads
- `CollectorLifecycle` uses a single `lock` for atomic state transitions (shared by DataCollector and DataProcessor)
- `QueueProcessorBase` uses its own `lock` for queue lifecycle (Start/Stop) + `Interlocked` for queue count
- Shared mutable state uses: `Interlocked` (counters, flags), `Volatile` (reads/writes), `lock` (complex operations)
- `Channel<T>` for value queuing; tracked count via `Interlocked` (not channel internal count)

## Configuration

`CollectorOptions` (all with sensible defaults):
- `ServerAddress`, `Port`, `AccessKey` — connection
- `MaxQueueSize` (20000) — max values in each queue before dropping
- `MaxValuesInPackage` (1000) — batch size per HTTP request
- `PackageCollectPeriod` (15s) — interval between batch sends
- `RequestTimeout` (30s) — HTTP request timeout
- `AllowUntrustedServerCertificate` (false) — opt-in for self-signed certs
- `ExceptionDeduplicatorWindow` (1h) — deduplication window for error messages
- `MaxDeduplicatedMessages` (1000) — max unique messages in deduplicator cache

## Known Issues

- Polly retry does not handle HTTP 4xx/5xx (`ShouldHandle` not configured) — data silently lost on server errors
- Queue overflow drops oldest items without per-drop logging (only aggregate overflow count)
