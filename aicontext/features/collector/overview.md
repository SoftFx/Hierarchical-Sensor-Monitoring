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
    +-- CollectorScheduler (static timer wheel)
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

### Data gating

`CanAcceptData` returns true during `Starting`, `Running`, and `Stopping` states. This allows sensors to flush pending values during stop. `CanStartNewSensors` returns true only during `Starting` and `Running` (and not when disposed).

### Queue state machine

Each `QueueProcessorBase` maintains its own `QueueState` (Stopped/Running/Stopping). If `StopAsync` times out (IDataSender ignores cancellation), the queue stays in `Stopping` and blocks subsequent `Start()` until the background task completes.

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
- `CollectorScheduler` fires callbacks on ThreadPool threads
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
