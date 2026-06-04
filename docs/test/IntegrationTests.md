# Integration tests

Prepared: 2026-06-01.

Integration tests verify end-to-end delivery of sensor data from `DataCollector` to a real HSM server running in Docker. Tests use `HsmServerFixture` for container management, `CollectorOptionsHelper` for unique sensor paths, and `ServerVerificationHelper` to read data from the server via REST API.

All test classes are marked `[Trait("Category", "Integration")]` and `[Collection("HSM Server")]` — xUnit runs them sequentially within a single collection.

## Test location

```
src/collector/HSMDataCollector.IntegrationTests/Tests/
├── LifecycleTests.cs
├── SensorDataSendingTests.cs
├── BatchSendingTests.cs
├── ConcurrencyTests.cs
├── QueueBehaviorTests.cs
└── ConnectivityTests.cs
```

Project: `src/collector/HSMDataCollector.IntegrationTests/HSMDataCollector.IntegrationTests.csproj`

Requirements: Docker Desktop running with the HSM server image (managed via `HsmServerFixture`).

## Lifecycle

Purpose: verify correct `CollectorStatus` state transitions and lifecycle event ordering.

| Test | What it checks | Success criteria |
| --- | --- | --- |
| `Start_SetsStatusToRunning` | Initial status is `Stopped`, after `Start()` it becomes `Running` | `collector.Status` transitions `Stopped → Running` |
| `Stop_SetsStatusToStopped` | After `Stop()` the status returns to `Stopped` | `collector.Status` transitions `Running → Stopped` |
| `LifecycleEvents_FireInCorrectOrder` | Event ordering during `Start()` + `Stop()` | Events fire in exact order: `Starting, Running, Stopping, Stopped` |
| `Restart_SendsDataSuccessfullyAfterRestart` | After `Start → Stop → Start`, data reaches the server | Int sensor with value `99` — server receives exactly 1 value `"99"` |
| `Dispose_StopsCollector` | `Dispose()` on a running collector transitions to `Stopped` | `collector.Status == Stopped` after `Dispose()` |

## Sensor data sending

Purpose: verify correct delivery of different sensor value types to the server and that read-back data matches what was sent.

| Test | Sensor type | What it checks | Success criteria |
| --- | --- | --- | --- |
| `SendBoolValue_ServerReceivesCorrectData` | Bool | `true` reaches the server | 1 value `"True"` |
| `SendIntValue_ServerReceivesCorrectData` | Int | `42` reaches the server | 1 value `"42"` |
| `SendDoubleValue_ServerReceivesCorrectData` | Double | `3.14` reaches the server | 1 value `"3.14"` |
| `SendStringValue_ServerReceivesCorrectData` | String | `"hello world"` reaches the server | 1 value `"hello world"` |
| `SendTimeSpanValue_ServerReceivesCorrectData` | Time | `TimeSpan.FromMinutes(5)` reaches the server | **SKIPPED**: Server bug #1068 |
| `SendVersionValue_ServerReceivesCorrectData` | Version | `new Version(1, 2, 3)` reaches the server | **SKIPPED**: Server bug #1068 |
| `SendRateValue_ServerReceivesCorrectData` | Rate | `100.0` reaches the server | Value found via `WaitForValueAsync` |
| `SendIntBarValue_ServerReceivesCorrectData` | IntBar | Bar with values `10, 20, 30` | 1 bar: Min=`"10"`, Max=`"30"`, Mean=`"20"` |
| `SendDoubleBarValue_ServerReceivesCorrectData` | DoubleBar | Bar with values `1.5, 2.5, 3.5` | 1 bar: Min=`"1.5"`, Max=`"3.5"`, Mean=`"2.5"` |
| `SendEnumValue_ServerReceivesCorrectData` | Enum | `2` reaches the server | 1 value `"2"` |
| `SendFileValue_ServerReceivesCorrectData` | File | File with name `"test_file"`, extension `"txt"` | `FileName == "test_file"`, `Extension == "txt"` |

Skipped tests:
- `SendTimeSpanValue_ServerReceivesCorrectData` — server bug #1068: History API returns empty result for TimeSpan sensors.
- `SendVersionValue_ServerReceivesCorrectData` — server bug #1068: History API returns empty result for Version sensors.

## Batch sending

Purpose: verify sending multiple values in a single pass and correct splitting of large packages.

| Test | What it checks | Success criteria |
| --- | --- | --- |
| `SendMultipleValuesInList_ServerReceivesAll` | 4 sensors of different types (bool, int, double, string) each send one value | Each sensor: 1 value with correct string representation |
| `SendLargeBatch_ExceedingMaxValuesInPackage_SentAsMultiplePackages` | 12 values with `MaxValuesInPackage = 5` — data is split into multiple packages | All 12 values delivered in order `["0", "1", ..., "11"]` |

## Concurrency

Purpose: verify correct data sending when multiple sensors work in parallel and under high load from a single sensor.

| Test | What it checks | Success criteria |
| --- | --- | --- |
| `MultipleSensorsSendingConcurrently_AllDataReceived` | 10 int sensors send values concurrently via `Task.Run` | Each sensor: 1 value `(i * 10).ToString()` |
| `HighVolumeSending_NoDataLoss` | 100 sequential values (0..99) from one sensor | All 100 values delivered in exact order `["0", "1", ..., "99"]` within 120 sec |

## Queue behavior

Purpose: verify queue behavior — dropping values before start, collection period, overflow, and priority sensors.

| Test | What it checks | Success criteria |
| --- | --- | --- |
| `ValuesDroppedBeforeStart_NotDeliveredAfterStart` | Values added before `Start()` are not delivered before or after start | Empty result on server before and after `Start()` |
| `PackageCollectPeriod_WaitingPeriodIsRespected` | Value is not sent before `PackageCollectPeriod = 5 sec` elapses | Empty after 2 sec; 1 value `"10"` after 10 sec |
| `MaxQueueSize_OldestValuesDroppedOnOverflow` | With `MaxQueueSize = 5` and 10 values, the oldest (0-4) are dropped | Newest values (5-9) delivered |
| `PrioritySensor_DataSentImmediately` | Priority sensor (`IsPrioritySensor = true`) bypasses timer-based sending | 1 value `"777"` after 10 sec |

## Connectivity

Purpose: verify the `TestConnection()` method — valid and invalid configurations.

| Test | What it checks | Success criteria |
| --- | --- | --- |
| `TestConnection_WithValidServer_ReturnsOk` | Correct connection parameters | `result.IsOk == true` |
| `TestConnection_WithWrongPort_ReturnsError` | Wrong port (fixture port + 1) | `result.IsOk == false`, `result.Error != null` |
| `TestConnection_WithInvalidAccessKey_ReturnsError` | Random GUID instead of access key | `result.IsOk == false` |
| `TestConnection_AfterServerRestart_ReturnsOk` | Connection after stopping and restarting the Docker container | **SKIPPED**: Docker Desktop WSL2 does not preserve port mappings after restart |

## Running locally

```powershell
dotnet test .\src\collector\HSMDataCollector.IntegrationTests\HSMDataCollector.IntegrationTests.csproj --no-restore --logger "console;verbosity=detailed"
```

## Summary

| File | Tests | Active | Skipped |
| --- | ---: | ---: | ---: |
| LifecycleTests | 5 | 5 | 0 |
| SensorDataSendingTests | 11 | 9 | 2 (server bug #1068) |
| BatchSendingTests | 2 | 2 | 0 |
| ConcurrencyTests | 2 | 2 | 0 |
| QueueBehaviorTests | 4 | 4 | 0 |
| ConnectivityTests | 4 | 3 | 1 (Docker WSL2) |
| **Total** | **28** | **25** | **3** |
