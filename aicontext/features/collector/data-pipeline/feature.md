# Feature: Data Pipeline

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
> Scope: Collector - queuing, batching, and sending sensor values to the server

---

## Description

The data pipeline manages the flow of sensor values from production to the HTTP transport layer. Values are enqueued into concurrent queues, batched, and sent periodically. Four separate queue processors handle different value types with different priorities.

---

## Business Rules / Invariants

- Values are **dropped** (not queued) while `DataProcessor.IsStarted == false`. This prevents stale data accumulation during stop/restart.
- `IsStarted` uses `Volatile.Read/Write` for thread-safe access without locks.
- Each queue has a max size (`MaxQueueSize`, default 20000). When exceeded, oldest items are dequeued and counted as overflow.
- Queue count is tracked via `Interlocked.Increment/Decrement` on `_queueCount` — never via `ConcurrentQueue.Count` (which is O(N)).
- Batch size is limited to `MaxValuesInPackage` (default 1000) per HTTP request.
- `DataQueueProcessor` sends on a timer (`PackageCollectPeriod`, default 15s). After sending, if queue still has >= MaxValuesInPackage items, it sends again immediately.
- `FileQueueProcessor` sends on event (ManualResetEventSlim) — each file sent individually.
- On `StopAsync()`, queues are drained (cleared) after the processing loop exits.
- Bar values with `Count <= 0` are filtered out in `Validate()` (empty bars not sent).

---

## Key Files

| File | Purpose |
|---|---|
| `Core/DataProcessor.cs` | Orchestrator: owns all queues, routes values, manages start/stop |
| `SyncQueue/SpecificQueue/QueueProcessorBase.cs` | Base class: ConcurrentQueue, Enqueue with overflow, TryDequeue with count tracking |
| `SyncQueue/SpecificQueue/DataQueueProcessor.cs` | Timer-based batch sender for regular sensor values |
| `SyncQueue/SpecificQueue/FileQueueProcessor.cs` | Event-based sender for file sensor values |

---

## Queue Processors

| Queue | Trigger | Batch Size | Values |
|---|---|---|---|
| Data | Timer (PackageCollectPeriod) | MaxValuesInPackage | Regular sensor values |
| Priority | Timer (PackageCollectPeriod) | MaxValuesInPackage | Priority sensor values |
| File | Event (ManualResetEventSlim) | 1 per send | File sensor values |
| Command | Event | MaxValuesInPackage | Server commands |

---

## Known Issues / Limitations

- Queue overflow silently drops oldest items. Only aggregate overflow count is reported to the diagnostic sensor — individual dropped values are not logged.
- No backpressure from the HTTP layer to the queues. If the server is slow, queues fill up and overflow.
