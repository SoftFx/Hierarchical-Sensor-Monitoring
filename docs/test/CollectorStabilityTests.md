# Collector stability regression tests

Prepared: 2026-06-01.

Purpose: regression tests for specific bugs found during `DataCollector` development. Each test reproduces a condition where the collector broke — hung, lost data, sent empty packages, or threw an exception.

Test code:

`src/collector/HSMDataCollector.Tests/CollectorStabilityTests.cs`

Infrastructure: internal `StabilityDataSender` (an `IDataSender` implementation with configurable failure modes), reflection access to private fields (`_dataProcessor`, `_dataQueue`, `QueueCount`).

## What is tested

| Test | Bug | How it is triggered | Success criteria |
| --- | --- | --- | --- |
| `Scheduler_loop_survives_unexpected_exception` | `CollectorScheduler.Loop()` only catches `OperationCanceledException`; any other exception kills the scheduler thread, all timers stop ticking | Function sensor throws `InvalidOperationException("boom")` every 50 ms; after 300 ms a normal sensor is created | `throwCount > 0` (throwing sensor fired) and `goodCount > 0` (normal sensor continues working after the error) |
| `DoubleMonitoringBar_CountAvr_computes_correct_average` | `DoubleMonitoringBar.CountAvr`: `first + second / 2` computes `first + (second / 2)` instead of `(first + second) / 2` | Reflection call `CountAvr(10.0, 20.0)` | Result is `15.0`, not `20.0` |
| `Register_after_start_does_not_create_unobserved_task_exception` | `SensorsStorage.Register` calls `_ = AddAndStart(sensor)` when `IsStarted = true`; fire-and-forget task doesn't handle exceptions | `ThrowOnCommand = true`; sensor created after `Start()`, triggering fire-and-forget; then `GC.Collect` + `GC.WaitForPendingFinalizers` × 2 | `UnobservedTaskException` did not fire |
| `Processing_loop_recovers_after_send_failure` | `GetPackage()` dequeues items before `SendDataAsync`; on send failure data is lost, but the loop must continue | `FailFirstNSends = 1`; 5 values before failure, 5 values after | `FailedSends >= 1` and `TotalDataValuesSent >= 5` |
| `Empty_package_not_sent_when_all_items_fail_validation` | `GetPackage()` dequeues, filters via `Validate()`, returns the filtered list; if all items are invalid — an empty collection is sent | Reflection access to `_dataQueue`; 5 `IntBarSensorValue` with `Count = 0` (fail `Validate()`) | `ReceivedEmptyDataPackage == false` |
| `DefaultSensorsCollection_Dispose_does_not_throw_on_partial_registration` | `DefaultSensorsCollection.Dispose()` calls `QueueOverflowSensor.Dispose()` and `CollectorErrors.Dispose()` without null-conditional; `NullReferenceException` on partial registration | `TestDefaultSensorsCollection` with `base(null, null)`, `IsCorrectOs => false` | `Dispose()` does not throw `NullReferenceException` |
| `Queue_count_stays_consistent_under_concurrent_access` | `_queueCount` is tracked manually via `Interlocked` alongside `ConcurrentQueue`; under concurrent enqueue + GetPackage drain, the counter may diverge from the actual queue size | `MaxQueueSize = 50`, `MaxValuesInPackage = 10`, `DataSendDelay = 10 ms`; 8 tasks each sending 200 `AddValue` | `QueueCount >= 0` after drain (does not go negative) |

## Running locally

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorStabilityTests" --logger "console;verbosity=detailed"
```

## Notes

- All 7 tests cover specific regressions found during development and review.
- Tests use reflection to access internal structures, making them sensitive to refactoring but allowing verification of invariants not exposed through the public API.
- `StabilityDataSender` enables simulating various failure modes without running a real server.
