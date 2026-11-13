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
- **The commands of a package are sent in the order they were issued (#1659 A3, 3.6.3)**: a failed command package returns to the tail of the queue, behind the commands issued during its send, and the server applies a package in order. `CommandQueueProcessor` therefore puts the package itself in the order of issue before the send (`DataPackage.SortByIssue`, by `QueueItem.Sequence`, a counter taken when the item is created and kept through a re-enqueue; not `BuildDate`, because two commands issued within one tick of the clock carry one timestamp, which on .NET Framework can be milliseconds long, and a wall clock that is set back reorders them; the package, not a sorted copy: a sender that delivers a package in parts answers with the positions it delivered, and they are read against the package, so with a copy a delivered registration went out again and one that never arrived was dropped, `CommandOrderTests`), so inside a package an older registration of a sensor cannot follow, and overwrite, a newer one (`CommandOrderTests`). Across packages the order is not kept: with more commands queued than `MaxValuesInPackage` a failed package rotates behind the packages issued after it, and an older registration can still reach the server last (#1750).
- **A rejected registration is final (#1659 A2, 3.6.2)**: on the **command** queue a package the server answered with a **4xx** (`PackageSendingInfo.IsRejected`, from the new `StatusCode`) is not re-enqueued. The same payload and key are refused again on every repeat, so before 3.6.2 such a package came back every `PackageCollectPeriod` until overflow evicted it. It is reported once through the deduplicator (log + `Collector errors`): `Queue: Command, the server rejected 12 command(s) with status 400; not sent again. …`, a lone registration by its path (`the command for <path>`). A missing answer and a 5xx are still re-sent. The boundary is the native one (`status <= 0 || status >= 500` is retried), so every 4xx is final, 408 and 429 included, and so is a 3xx that reaches the queue (a redirect the client could not follow). The rule is per queue (`QueueProcessorBase.RejectionIsFinal`, on for `CommandQueueProcessor` only): a **value** package answered 4xx still returns to the queue, which is what the native collector does with a value batch. A sensor whose registration was rejected is registered again by the next Start.
- **What a sender may tell the queue beyond pass or fail (3.9.0, internal fields of `PackageSendingInfo`)**, used by the collector session sender: `Settled`, the items of a failed package that are settled all the same (delivered in an earlier frame, or dropped), so that only the others are re-enqueued; `DroppedCount`, items that can never be sent (a value larger than a session frame), counted as overflow; `IsTransportDown`, the request got no answer from the transport: there is no session, the write failed, no ack came in time, or a stop cancelled it. The transport reports each of those itself (it ends the session and says why). For the last one the queue keeps the package, logs at Debug instead of reporting an error per cycle (the transport reports its own state), and waits for `ICollectorTransport.WaitForConnectionAsync` or the collect period, whichever comes first, then dispatches at once.
- **A file value on a transport that cannot carry one is refused before the queue** (`DataProcessor.AddFile`): reported and counted as overflow. The file queue re-enqueues whatever a sender fails and would loop on it. Today that transport is the collector session (`../http-client/feature.md`).
- **#1088 (the single retry-drop)**: a retry that meets an already-**full** queue is dropped instead of evicting a queued value via the tail-write + overflow path — the retry is the oldest data (newest-wins). Reported via `DataProcessor.ReportRequeueEviction` → `QueueOverflowSensor` (reporting lives in per-item `ReEnqueueItem`, so FileQueueProcessor's single-item retries are covered too).
- **#1090 removed (2026-06):** the earlier below-capacity head-drop — a `_buildDateMirror` head comparison that dropped a retry older than the FIFO head even with room — was removed from **both** collectors. For a large history buffer it discarded values that did not need to be lost; the monitoring-history contract drops only on overflow. (`QueueItem.BuildDate` survives for `DataPackage` time-in-queue stats; the mirror and `IsOlderThanQueueHead` are gone.)
- **Cancellation**: on `OperationCanceledException`, the package is re-enqueued when the shutdown mode preserves it (`PreserveCanceledPackages`): GracefulStop and, since 3.6.5 (#1659 A6), TerminalDispose. The drain below then sends it again. Before 3.6.5 a `Dispose()` dropped the package it had cancelled in flight, the oldest accepted data, even against a healthy server. The price is the one a Stop already paid: a request the server had taken before the cancel is sent again, so the server may store those values twice (delivery is at-least-once). StartRollback still discards.

## Shutdown modes & drain order

`ShutdownMode` (`SyncQueue/Data/ShutdownMode.cs`):

| Mode | Clear on stop | Flush accepted work | Preserve canceled | Stop wait timeout |
|---|---|---|---|---|
| GracefulStop | no | yes | yes | `min(RequestTimeout, 5 s)` |
| TerminalDispose | no | yes | yes (3.6.5) | `min(RequestTimeout, 1 s)` |
| StartRollback | immediately | no | no | `min(RequestTimeout, 1 s)` |

Drain in `DataProcessor.StopWithFlushAsync` (#1659 A7, 3.6.5):

1. **Stop the four queue loops together.** Each wait is bounded by the stop wait timeout above; they run concurrently, so a sender that ignores cancellation costs one such wait, not four in a row.
2. **One drain budget for all queues**: `RequestTimeout` clamped to [1 s, 5 s] on a graceful stop, `min(that, 1 s)` on a terminal dispose. This is the native `StopDrainBudgetMs`. A queue that finds the budget spent starts no send; its items are discarded and counted (`LogDiscardedItems`). Before 3.6.5 every queue had a budget of its own, about 20 s in the worst case of a hung server.
3. **Order: Command → Priority → Data → File**, each followed by `ClearQueue()` + `LogDiscardedItems`. Registrations go first, as the native drain posts pending registrations before the values (`DrainQueueOnStop` → `FlushRuntimeRegistrations`): values are of little use to a server that was never told what their sensors are. Before 3.6.5 commands were last.

4. **One attempt per package, and the first failed send ends the drain (#1747).** This is the native drain (`DrainQueueOnStop` → `DispatchQueuedLocked(clear_remainder_on_failure = true)`): against a server that is down a stop returns after one failed send, where it used to spend the whole budget and discard the queue anyway. Two parts:
   - *The queues' rule, for any sender, a host's own `IDataSender` included.* `QueueProcessorBase.FlushAsync` leaves its loop at the first send that returns an error or throws, and says so (`FlushResult.SendFailed`). `DataProcessor` then starts no send for the value queues behind it: **Priority, Data and File are one value stream here, as they are one queue in the native collector**, so a failed priority send means no data and no file send. What those queues hold is discarded and counted as a discarded remainder always was (`ClearQueue()` + `LogDiscardedItems`, one Info line per queue). With a sender that fails every send, a stop therefore makes exactly one value send attempt, whatever the number of packages and queues.
   - *The built-in HTTP client does not repeat a request inside a drain send.* A running collector repeats a failed value request inside the send (Polly, 10 retries, `../http-client/feature.md`); in the drain those repeats were what spent the budget. `IDataSender` is public and cannot carry "this is the drain", so the client offers a view of itself, `IStopDrainSender.StopDrain` (internal), whose value sends are single attempts, and the queues send through it while they flush (`QueueProcessorBase.Sender`). A view and not a mode of the client: a client can be shared by two collectors, and the stop of one must not change the sends of the other. A sender without that interface is used as it is.
   - **A failed registration send ends the drain of the registrations only.** The rest of the command queue is discarded and counted, and the values are still tried, once. That is what the native drain does (it posts the pending registrations once and drains the values whatever that post returned); most values of a stop belong to sensors the server has known for long, and the attempt runs inside the same budget, so it cannot make the stop longer. A registration the server refused with a 4xx is final as always and does not end anything: the server answered.
   - The collector session needs nothing of its own: it never repeats inside a send. Without a session a send fails at once (`PackageSendingInfo.NotSent`), which is a failed send like any other; a send waiting for an ack is cancelled by the budget.
   - What the old behaviour could do and this cannot: deliver the last values to a server that comes back within the budget. The native collector drops them too.

Worst case for a stop is therefore the stop wait plus the drain budget: 10 s graceful, 2 s terminal. What the single budget costs: registrations that are slow to land (many packages, a slow `/commands`) can use it up, and the values behind them are then discarded where they used to have a budget of their own. That is the bounded-stop contract (a stop must not hold up a host restart; loss at stop is accepted) and the native order.

No conformance scenario pins the drain order, the single budget, the end of the drain at the first failed send or the re-send of the in-flight package: the DSL has no verb that makes them observable without a wall-clock margin (#1751). They are pinned per language: managed `StopDrainTests`, `StopDrainFailedSendTests` and `Transport/StopDrainSingleAttemptTests` (the last two on both TFMs), the native tests of the stop drain (`native_http_stop_drain_*`). Known gap of the managed drain: a package cancelled when the budget fires can be re-enqueued after the clear (#1748).

Diagnostics suppression: `AddPackageInfo`/`AddPackageSendingInfo` are no-ops during the command flush and from the data-drain boundary on, so the command and file flushes cannot enqueue stale self-diagnostics into stopped data queues (would survive restart as stale telemetry). **Overflow reporting is exempt** — data loss must stay visible even during shutdown. Flag resets on every Start.

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
- Graceful stop and terminal dispose both hand accepted work, the package cancelled in flight included, to one bounded drain; both stay bounded even when the sender ignores cancellation.
- Values are dropped (not queued) while the collector is stopped; values CAN be enqueued during Stopping (final flushes).

## Tests

`src/collector/HSMDataCollector.Tests/CollectorQueueShutdownTests.cs` (lifecycle, #1088/#1090 regressions, flush wording), `RegistrationRejectionTests.cs` (a 4xx on a registration is final, a 5xx / no answer / a value 4xx is re-sent), `StopDrainTests.cs` (drain order, the single budget, the in-flight package on Stop and Dispose), `StopDrainFailedSendTests.cs` (the first failed value send ends the drain, across the value queues; a failed registration send does not; both TFMs), `Transport/StopDrainSingleAttemptTests.cs` (no in-send repeat of the HTTP client inside the drain; both TFMs), `CollectorStabilityTests.cs` (stop-race file flush), `CollectorTransportChaosTests.cs`, `CollectorStressTests.cs` (gated soak/stress).

## Native port (C++) — #1097

The native collector (`src/native/collector`) ports the **observable** pipeline behavior. It runs a single FIFO worker queue (`std::deque<QueuedItem>` + one dispatcher thread) rather than four `Channel`-backed processors, and reproduces the load-bearing invariants:

- **Overflow**: position-based FIFO head-eviction on enqueue (`while size > MaxQueueSize: pop_front`) — identical newest-wins policy.
- **Batching/dispatch**: pops up to `MaxValuesInPackage` per cycle, keeps draining while the queue is non-empty, waits `PackageCollectPeriod` (file enqueues kick the worker, mirroring the reactive file queue).
- **Retry**: a failed send re-enqueues at the tail and retries next cycle, **no retry cap** — overflow eviction is the only backstop (same contract as `RunLoopAsync`).
- **Registration retry (#1515, collector 0.10.3)**: a sensor registration (`AddOrUpdate` batch to `/commands`) that gets **no HTTP response or a 5xx** is posted again on the worker's next cycle (`PackageCollectPeriod`) until the server accepts it, the same contract as the value queue. This covers the Start batch, a built-in source's lazily created sensor (top-CPU process, network interface, service status, TCP failure rate) and a runtime registration. Managed equivalent: registrations ride the command queue, whose `DispatchPackageAsync` re-enqueues a package with `PackageSendingInfo.Error != null` and `RunLoopAsync` re-sends it after `DelayAfterFailureAsync` (`PackageCollectPeriod`); a command send is one attempt, with no retry inside it (3.6.3, #1659 A3; before that Polly retried exceptions inside the send without a limit). Before 0.10.3 native posted the Start batch **once**: a server restarting behind Caddy (`Failed to register 121 sensor(s) on connect: HTTP 502`, Linux probe 0.8.2) received values for sensors it had never been told about (no descriptions, units, TTLs or alerts) until the next collector restart. A sensor is queued once however long the outage lasts, and one the server had already seen (a restart re-registers everything) is marked unposted so the retry does not skip it. Logged: the failure at Error, deduplicated (`…: HTTP 502; retrying on the next send cycle.`), the recovery at Info (`Registered N sensor(s) after an earlier failed attempt.`; since collector 0.11.1 N counts only the sensors whose earlier post failed — a runtime registration that shares the flush, or follows a retry the next Start batch already landed, is not credited with a recovery, #1528). A **4xx** is final (logged once) and is not posted again: a rejected payload or key never succeeds on repeat, and values with the same key report it. Managed has the same rule since 3.6.2 (#1659 A2, `RegistrationRejectionTests`, `FakeServerE2ETests.StartRegistration_RejectedWith4xx_IsPostedOnce`); before that it re-sent a rejected registration every period until the command queue evicted it. Pinned by `native_http_connect_registration_is_retried_after_5xx`, `native_http_restart_registration_is_retried_after_5xx` and `native_http_recovery_log_credits_only_retried_registrations` (native, the recording server's injected 502). No conformance scenario: the recording harness never POSTs registrations (`send_wire_` is HTTP-only) and `set_sender_fail_next` fails value sends only, so no portable verb observes it.
- **Monitoring-history retry policy (drop only on overflow)**: every value is history and must survive, so a failed retry is **never dropped below capacity** — it is re-enqueued at the tail (`ReEnqueueLocked`) and retried (at-least-once). The **single** drop path is the #1088 backstop: when the buffer is already **full**, the retry (the oldest data — it was popped from the head while everything queued behind it arrived during its send) is dropped rather than evicting a queued value, matching normal overflow's newest-wins. This matches the C# `QueueProcessorBase` exactly — #1097 also removed C#'s earlier #1090 below-capacity head-drop, so both collectors drop only on overflow.
- **Bounded graceful stop**: `StopWorker` cancels in-flight sends (hang/transport abort) and `DrainQueueOnStop` flushes what it can, dropping the remainder on a dead transport — stop never blocks the host restart (the accepted data-loss-at-stop trade-off).
- **The stop drain delivers on the HTTP transport (#1432, collector 0.7.2)**: the transport `Cancel()` that interrupts a hung in-flight POST also aborts every send started while it is set, so `StopWorker` now **re-arms it (`ResetCancel`) right after the worker join** — the cancel is scoped to the join. Before 0.7.2 the flag stayed set and the drain, the flushed partial bars, the last values, the `.module/Version` `Stop:` post and even the **next Start's `/commands` registration** all failed inside the process against a perfectly healthy server (the Linux probe logged `Collector stop dropped 6 pending value(s)`). Boundedness is kept by the drain itself, not by a cancelled transport: every drain send gets only what remains of the stop budget below as its per-request timeout, and an exhausted budget drops the rest without opening a connection. A batch cancelled in flight is re-enqueued and re-sent by the drain, so delivery stays at-least-once (a server that processed the cancelled POST may see it twice).
- **Stop-budget matrix (collector 0.7.3)** — `StopDrainBudgetMs(terminal)`, mirroring the managed timeout matrix above:

| Shutdown | Native drain budget | Managed equivalent |
|---|---|---|
| Graceful `Stop()` | `clamp(RequestTimeout, 1 s, 5 s)` | `DataProcessor._stopFlushTimeout` |
| Terminal `Dispose()` | `min(that, 1 s)` | `ShutdownMode.TerminalDispose.StopWaitTimeout` |

  `StopCore(terminal)` carries the shutdown kind, so a disposing host — the agent's self-update restart — is never held for the graceful ceiling by a hung server. Pinned by `native_http_stop_is_bounded_against_hung_server` and `native_http_dispose_is_bounded_against_hung_server`.
- **Stop-time loss is loud (rule #8, collector 0.7.3)**: because the drain now really sends, a failure during it is a genuine failure — `HttpSendBatch` keeps the **Error** level for it and logs Debug (`send cancelled by the collector stop`) only for a POST aborted *before* the worker join, while the `Collector stop dropped N pending value(s)` breadcrumb is **Info** (the default file-sink level; the C ABI has no Warning), one line per restart.

**Coverage**: portable behavior is pinned by the shared corpus (`queue_overflow_contract`, `sender_retry_contract`, `flush_contract`) — `flush_contract` pins "Stop delivers what is pending" and "a hung sender still returns bounded" on the in-memory sender, so the #1432 regression was transport-level and is pinned by the native HTTP tests instead: `native_http_stop_drain_delivers_to_healthy_server`, `native_http_stop_is_bounded_against_hung_server`, `native_http_stop_start_stop_delivers`. The retry policy is pinned by native **unit** tests that stage the in-flight window with the hang seam: `native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values` (the full-buffer #1088 backstop) and `native_retry_below_capacity_is_always_redelivered` (at-least-once below capacity — a failed value is redelivered even when fresher values arrived during its send). They stay native-only because they need control over enqueue/in-flight ordering, not just the value stream.

**Deliberate simplifications (not gaps):**
- One worker queue instead of the four `QueueProcessor`s — the Data/Priority/File/Command split is a C# QoS-internal structure, not observable in value delivery (File already dispatches reactively via the kick).
- `EnqueueResult` statuses and the `ShutdownMode` (GracefulStop/TerminalDispose/StartRollback) timeout matrix are C#-internal; the only observable shutdown contract — "stop never hangs; may drop on a dead transport" — already holds via the bounded stop drain, and producers must not branch on rejection kind either way.
- Time-in-queue diagnostics (`DataPackage` stats), the diagnostics-suppression boundary (#1075), and overflow telemetry (`QueueOverflowSensor`) all need the default **diagnostic sensors** → deferred to #1099, which adds them.

## Known Issues / Limitations

- No backpressure from HTTP to producers; a slow server fills queues until overflow.
- Per-drop logging is aggregate-only (QueueOverflowSensor counts, not per-value logs).
