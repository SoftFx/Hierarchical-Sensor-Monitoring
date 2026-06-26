# Feature: Data Pipeline

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - queuing, batching, retry/overflow policy, shutdown draining, and sending sensor values to the server

---

## Description

The data pipeline moves sensor values from producers to the HTTP transport. Four queue processors (over unbounded `Channel<QueueItem<T>>`) handle different value types; `DataProcessor` orchestrates lifecycle, routing, flushing, and self-diagnostics.

Rewritten in the #1071–#1075 lifecycle refactor (PR #1080) and hardened by #1088/#1090 (PRs #1089, #1091). The pre-2026-06 description (ConcurrentQueue + `IsStarted` flag + clear-on-stop) is obsolete.

---

## Queue processors

| Queue | Item type | Dispatch | Wait strategy |
|---|---|---|---|
| Data | `SensorValueBase` | batches of `MaxValuesInPackage` | periodic `PackageCollectPeriod` (100 ms floor); keeps draining while a full batch remains |
| Priority data | `SensorValueBase` | batches | reactive `WaitToReadAsync` |
| File | `FileSensorValue` | single item | reactive |
| Command | `CommandRequestBase` | batches | reactive |

- `QueueItem<T>.BuildDate = DateTime.UtcNow` captured at enqueue; `DataPackage` aggregates time-in-queue for diagnostics.
- Bar values with `Count <= 0` are filtered out at package build (`GetPackage`).
- Each queue has its own `QueueState` (Stopped/Running/Stopping) state machine with restart-after-unexpected-exit handling; `_acceptingWritesFlag` closes public writes the moment StopAsync commits.

## Enqueue results

`EnqueueResult` (`SyncQueue/Data/EnqueueResult.cs`): `Accepted(+DroppedCount)`, `RejectedCollectorNotAcceptingData` (collector lifecycle gate), `RejectedQueueStopped` (queue gate). The two rejection statuses exist for test observability only — producers must not branch on the distinction (#1087 B). `DataProcessor.HandleEnqueueResult` treats both as silent rejection and feeds `DroppedCount` into `QueueOverflowSensor` (with a self-loop guard when the sender IS the overflow sensor).

## Overflow & retry policy

- **Overflow**: after a successful write, the head is dropped while `QueueCount > MaxQueueSize` (FIFO eviction, newest data wins). Counts surface via `QueueOverflowSensor`. This is the **only** place a value is dropped on the happy path — the queue is monitoring history and keeps every value until the buffer overflows.
- **Retry**: a failed send (exception or `PackageSendingInfo.Error != null`) re-enqueues the whole package via `ReEnqueueItems` and rethrows; the loop retries next cycle. There is **no retry cap** — overflow eviction is the only backstop; error logs are deduplicated (`AddQueueLoopError`). A failed retry is **never dropped below capacity** (at-least-once).
- **#1088 (the single retry-drop)**: a retry that meets an already-**full** queue is dropped instead of evicting a queued value via the tail-write + overflow path — the retry is the oldest data (newest-wins). Reported via `DataProcessor.ReportRequeueEviction` → `QueueOverflowSensor` (reporting lives in per-item `ReEnqueueItem`, so FileQueueProcessor's single-item retries are covered too).
- **#1090 removed (2026-06):** the earlier below-capacity head-drop — a `_buildDateMirror` head comparison that dropped a retry older than the FIFO head even with room — was removed from **both** collectors. For a large history buffer it discarded values that did not need to be lost; the monitoring-history contract drops only on overflow. (`QueueItem.BuildDate` survives for `DataPackage` time-in-queue stats; the mirror and `IsOlderThanQueueHead` are gone.)
- **Cancellation**: on `OperationCanceledException`, packages are re-enqueued only when the shutdown mode preserves them (`PreserveCanceledPackages` — GracefulStop yes, TerminalDispose no).

## Shutdown modes & drain order

`ShutdownMode` (`SyncQueue/Data/ShutdownMode.cs`):

| Mode | Clear on stop | Flush accepted work | Preserve canceled | Stop wait timeout |
|---|---|---|---|---|
| GracefulStop | no | yes | yes | `RequestTimeout` |
| TerminalDispose | no | yes | no | `min(RequestTimeout, 1 s)` |
| StartRollback | immediately | no | no | `min(RequestTimeout, 1 s)` |

Drain order in `DataProcessor.StopWithFlushAsync`: stop all queues → flush Priority → flush Data → **set diagnostics-suppression flag (#1075)** → flush File → flush Command → per-queue `ClearQueue()` + `LogDiscardedItems`. Flush timeout is `RequestTimeout` clamped to [1 s, 5 s].

Diagnostics suppression: after the data-drain boundary, `AddPackageInfo`/`AddPackageSendingInfo` become no-ops so late file/command flushes cannot enqueue stale self-diagnostics into stopped data queues (would survive restart as stale telemetry). **Overflow reporting is exempt** — data loss must stay visible even during shutdown. Flag resets on every Start.

Failure log honesty (#1087 A): `DispatchPackageAsync`'s failure exception says "N preserved" in the run loop but "N queued for clear" when running inside `FlushAsync` (`IsFlushing`), because ClearQueue discards those items moments later; "+M dropped" counts retry-filter drops.

## Key Files

| File | Purpose |
|---|---|
| `Core/DataProcessor.cs` | Orchestrator: queues, lifecycle gates, flush phases, diagnostics routing |
| `SyncQueue/SpecificQueue/QueueProcessorBase.cs` | Channel, state machine, overflow eviction, #1088 full-buffer retry backstop, flush |
| `SyncQueue/SpecificQueue/DataQueueProcessor.cs` | Periodic batch dispatch |
| `SyncQueue/SpecificQueue/PriorityDataQueueProcessor.cs` | Reactive batch dispatch |
| `SyncQueue/SpecificQueue/FileQueueProcessor.cs` | Single-item file dispatch |
| `SyncQueue/SpecificQueue/CommandQueueProcessor.cs` | Command batch dispatch |
| `SyncQueue/Data/*.cs` | QueueItem, DataPackage, PackageInfo, PackageSendingInfo, EnqueueResult, ShutdownMode |

## Invariants

- FIFO per queue; at-least-once delivery; no cross-queue ordering guarantees.
- Monitoring history: every value is kept until the buffer overflows. A failed retry is re-enqueued and retried (at-least-once) and is dropped only when the buffer is already full (#1088) — never below capacity.
- Graceful stop preserves accepted work via bounded flush; terminal dispose stays bounded even when the sender ignores cancellation.
- Values are dropped (not queued) while the collector is stopped; values CAN be enqueued during Stopping (final flushes).

## Tests

`src/collector/HSMDataCollector.Tests/CollectorQueueShutdownTests.cs` (lifecycle, #1088/#1090 regressions, flush wording), `CollectorStabilityTests.cs` (stop-race file flush), `CollectorTransportChaosTests.cs`, `CollectorStressTests.cs` (gated soak/stress).

## Native port (C++) — #1097

The native collector (`src/native/collector`) ports the **observable** pipeline behavior. It runs a single FIFO worker queue (`std::deque<QueuedItem>` + one dispatcher thread) rather than four `Channel`-backed processors, and reproduces the load-bearing invariants:

- **Overflow**: position-based FIFO head-eviction on enqueue (`while size > MaxQueueSize: pop_front`) — identical newest-wins policy.
- **Batching/dispatch**: pops up to `MaxValuesInPackage` per cycle, keeps draining while the queue is non-empty, waits `PackageCollectPeriod` (file enqueues kick the worker, mirroring the reactive file queue).
- **Retry**: a failed send re-enqueues at the tail and retries next cycle, **no retry cap** — overflow eviction is the only backstop (same contract as `RunLoopAsync`).
- **Monitoring-history retry policy (drop only on overflow)**: every value is history and must survive, so a failed retry is **never dropped below capacity** — it is re-enqueued at the tail (`ReEnqueueLocked`) and retried (at-least-once). The **single** drop path is the #1088 backstop: when the buffer is already **full**, the retry (the oldest data — it was popped from the head while everything queued behind it arrived during its send) is dropped rather than evicting a queued value, matching normal overflow's newest-wins. This matches the C# `QueueProcessorBase` exactly — #1097 also removed C#'s earlier #1090 below-capacity head-drop, so both collectors drop only on overflow.
- **Bounded graceful stop**: `StopWorker` cancels in-flight sends (hang/transport abort) and `DrainQueueOnStop` flushes what it can, dropping the remainder on a dead transport — stop never blocks the host restart (the accepted data-loss-at-stop trade-off).

**Coverage**: portable behavior is pinned by the shared corpus (`queue_overflow_contract`, `sender_retry_contract`, `flush_contract`). The retry policy is pinned by native **unit** tests that stage the in-flight window with the hang seam: `native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values` (the full-buffer #1088 backstop) and `native_retry_below_capacity_is_always_redelivered` (at-least-once below capacity — a failed value is redelivered even when fresher values arrived during its send). They stay native-only because they need control over enqueue/in-flight ordering, not just the value stream.

**Deliberate simplifications (not gaps):**
- One worker queue instead of the four `QueueProcessor`s — the Data/Priority/File/Command split is a C# QoS-internal structure, not observable in value delivery (File already dispatches reactively via the kick).
- `EnqueueResult` statuses and the `ShutdownMode` (GracefulStop/TerminalDispose/StartRollback) timeout matrix are C#-internal; the only observable shutdown contract — "stop never hangs; may drop on a dead transport" — already holds via the bounded stop drain, and producers must not branch on rejection kind either way.
- Time-in-queue diagnostics (`DataPackage` stats), the diagnostics-suppression boundary (#1075), and overflow telemetry (`QueueOverflowSensor`) all need the default **diagnostic sensors** → deferred to #1099, which adds them.

## Known Issues / Limitations

- No backpressure from HTTP to producers; a slow server fills queues until overflow.
- Per-drop logging is aggregate-only (QueueOverflowSensor counts, not per-value logs).
