# C++ Collector Port — Functional Inventory (Spike Step 1)

> Owner: collector/integrations | Created: 2026-06-10 | Status: living checklist
> Companion to [`cpp-collector-port-spike.md`](cpp-collector-port-spike.md).

This is the complete functional inventory of the managed `HSMDataCollector`
(`src/collector/HSMDataCollector/`, targets `net6.0;net472`) plus its wire
contract (`src/api/HSMSensorDataObjects/`). Every item is a checkbox so the
native port can track coverage; tick items as they land in the C++ core.

Legend: **[wire]** = byte-for-byte compatibility required. **[decide]** = port
decision needed, not automatically in scope. File paths are the source of truth
— re-verify details against them at port time.

---

## 1. Collector construction & configuration

`Core/DataCollector.cs`, `Options/CollectorOptions.cs`

- [ ] `DataCollector(CollectorOptions)` ctor with immediate `options.Validate()`
- [ ] Convenience ctor `DataCollector(productKey, address = "localhost", port = 44330, clientName = null)`
- [ ] `TestConnection()` → `ConnectionResult { Code, Error, IsOk }` (works in any lifecycle state)

`CollectorOptions` (every property + default + validation):

| Option | Type | Default | Validation |
|---|---|---|---|
| `AccessKey` | string | — | required, non-whitespace |
| `ServerAddress` | string | `"localhost"` | required, non-whitespace |
| `Port` | int | `44330` | 1..65535 |
| `ClientName` | string | null | — |
| `ComputerName` | string | null | — (path hierarchy) |
| `Module` | string | null | — (path hierarchy) |
| `MaxQueueSize` | int | `20000` | > 0 (per queue) |
| `MaxValuesInPackage` | int | `1000` | > 0 |
| `PackageCollectPeriod` | TimeSpan | 15 s | > 0 |
| `RequestTimeout` | TimeSpan | 30 s | > 0 |
| `DataSender` | IDataSender | `HsmHttpsClient` | pluggable transport seam |
| `AllowUntrustedServerCertificate` | bool | false | — |
| `AllowPlaintextTransport` | bool | false | — |
| `ExceptionDeduplicatorWindow` | TimeSpan | 1 h | >= 0 (0 = log immediately) |
| `MaxDeduplicatedMessages` | int | `1000` | > 0 |
| `MaxSensors` | int | `100000` | > 0 |

- [ ] All options above with defaults and validation rules
- [ ] `IDataSender` abstraction (`Core/IDataSender.cs`): `TestConnectionAsync`, `SendDataAsync`, `SendPriorityDataAsync`, `SendCommandAsync`, `SendFileAsync`, `Dispose` — the seam used by all tests/fakes

## 2. Lifecycle

`Core/DataCollector.cs`, `Core/CollectorLifecycle.cs`, `Core/SensorsStorage.cs`

- [ ] `CollectorStatus` state machine: `Starting → Running → Stopping → Stopped → Disposed`
- [ ] Gates: `CanAcceptData` (Starting/Running/Stopping), `CanRegisterSensors` (Stopped/Starting/Running), `CanStartNewSensors` (Starting/Running), `IsAcceptingRegistrations`
- [ ] `Task Start()` / `Start(Task customStartingTask)` — idempotent; rollback to Stopped on failure (queues stopped via `ShutdownMode.StartRollback`)
- [ ] `Task Stop()` / `Stop(Task customStoppingTask)` — idempotent; awaits in-flight dynamic sensor-start tasks; custom-task failure logged but stop proceeds
- [ ] `Dispose()` — idempotent, terminal; `ShutdownMode.TerminalDispose`; joins an in-flight `Stop()` (dispose-vs-stop race: exactly one ToStopped, terminal mode wins on queues)
- [ ] Restart support: `Start(); Stop(); Start()` cycles re-init and restart all registered sensors
- [ ] Lifecycle events `ToStarting/ToRunning/ToStopping/ToStopped` + portable `ILifecycleListener` (`OnStarting/OnRunning/OnStopping/OnStopped`) via `AddLifecycleListener` — fired under lifecycle lock, handler exceptions swallowed+logged
- [ ] Disposal order: DataProcessor → DataSender → CollectorScheduler → global exception handler unhook
- [ ] **[decide]** Obsolete sync `Initialize()` overloads, `InitializeSystemMonitoring`/`InitializeProcessMonitoring`/`InitializeOsMonitoring`/`MonitorServiceAlive`/`InitializeWindowsUpdateMonitoring`, `ValuesQueueOverflow` event (never fires) — recommend NOT porting

## 3. Sensor registration semantics

`Core/SensorsStorage.cs`

- [ ] Identity = full normalized path; duplicate Create at same path with same type/IsLastValue returns the existing instance (new instance disposed)
- [ ] Same path + different type/IsLastValue → `InvalidOperationException`
- [ ] `MaxSensors` cap enforced atomically; exceeding throws and unregisters the offender
- [ ] Registration vs lifecycle: Stopped → queued for next Start; Starting/Running → immediate async `InitAndStart()` tracked in `_dynamicStartTasks` (awaited by Stop); Stopping/Disposed → rejected (logged, sensor returned inert)
- [ ] `InitAsync()` of every sensor sends `AddOrUpdateSensorRequest` (the "register on server" command) before first value

## 4. Sensor creation API (public surface)

`Core/DataCollector.cs`, `Core/Builders/SensorBuilders.cs`, `PublicAPI/SensorsAPI/*`

Instant: `IInstantValueSensor<T>` with `AddValue(value)`, `AddValue(value, comment)`, `AddValue(value, status, comment)`

- [ ] `CreateBoolSensor`, `CreateIntSensor`, `CreateDoubleSensor`, `CreateStringSensor`, `CreateVersionSensor`, `CreateTimeSensor` — each with `(path, description)` and `(path, InstantSensorOptions)` overloads
- [ ] `CreateEnumSensor(path, description | EnumSensorOptions)` (int-valued + `EnumOptions` metadata)
- [ ] Last-value: `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)` + generic `CreateLastValueSensor<T>(path, options, defaultValue)`; `ILastValueSensor<T>` — holds latest value, sends once on stop
- [ ] Rate: `CreateRateSensor(path, RateSensorOptions)`, `CreateM1RateSensor`, `CreateM5RateSensor`; `IMonitoringRateSensor` — lock-free sum, sends `sum / PostDataPeriod.TotalSeconds` then resets
- [ ] Bar int: `CreateIntBarSensor(path, barPeriod = 300000 ms, postPeriod = 15000 ms, description)` + options overload + presets `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor`
- [ ] Bar double: `CreateDoubleBarSensor(..., precision = 2, ...)` + options overload + same five presets; `IBarSensor<T>`: `AddValue`, `AddValues(IEnumerable<T>)`, `AddPartial(min,max,mean,first,last,count)`
- [ ] File: `CreateFileSensor(path, fileName, extension = "txt", description)` / `(path, FileSensorOptions)`; `IFileSensor.SendFile(filePath, status, comment) → Task<bool>`; collector-level `SendFileAsync(sensorPath, filePath, status, comment)`
- [ ] Function (no params): `CreateNoParamsFuncSensor<T>(path, descr, Func<T>, interval ms|TimeSpan)`, `Create{1Min,5Min}NoParamsFuncSensor`, `CreateFunctionSensor<T>(path, func, FunctionSensorOptions)`
- [ ] Function (params): `CreateParamsFuncSensor<T,U>(path, descr, Func<List<U>,T>, interval)`, `Create{1Min,5Min}ParamsFuncSensor`, `CreateValuesFunctionSensor<T,U>(...)`; `IParamsFuncSensor.AddValue(U)` buffers into capped cache
- [ ] `CreateServiceCommandsSensor()`; `IServiceCommandsSensor`: `SendCustomCommand(command, initiator)`, `SendUpdate(initiator[, newVersion[, oldVersion]])`, `SendRestart/SendStart/SendStop(initiator)`
- [ ] Fluent builders: `InstantSensor<T>(path)`, `BarSensor<T>(path)`, `RateSensor(path)` with `.Description/.Ttl/.KeepHistory/.Priority/.BarPeriod/.PostPeriod/.TickPeriod/.Precision/.Configure(...).Build()`
- [ ] Properties: `Status`, `ComputerName`, `Module`, `Windows`, `Unix`
- [ ] Logging config: `AddNLog(LoggerOptions)`, `AddCustomLogger(ICollectorLogger)` (fluent, chainable)

## 5. Sensor machinery (behavior per kind)

`Sensors/**`

- [ ] **Validation pipeline** (`Extensions/SensorValueExtensions.cs`): reject null (strings allowed), reject NaN/±Infinity for double/float, status must be a defined `SensorStatus`, comment trimmed to **1024** chars; rejected values logged via `LogDroppedValue` (Debug), never enqueued
- [ ] **Instant**: validate → `SendValue` → `DataProcessor.AddData` / `AddPriorityData` (per `IsPrioritySensor`)
- [ ] **Last-value**: stores `_lastValue/_lastStatus/_lastComment`; single enqueue on `StopAsync`; `ISensorIdentity.IsLastValue = true`
- [ ] **Bar** (`BarSensors/*`): window state `IntMonitoringBar`/`DoubleMonitoringBar` — min/max/mean(=sum/count)/count/first/last under `_lockBar`; `AddPartial` merges pre-aggregated stats (int: strict `min<=x<=max` check; double: tolerance `max(1e-12, |max-min|*1e-9)`); `Complete()` rounds (double: `Math.Round(v, Precision, AwayFromZero)`; int: mean only); bar roll when `CloseTime <= UtcNow` and ONLY after successful `TrySendValue` (no-roll-without-send invariant); periods: `BarPeriod` 5 min, `BarTickPeriod` 5 s, `PostDataPeriod` 15 s, `Precision` 2 (0..15)
- [ ] **Bar time alignment** (`Extensions/BarTimeExtensions.cs`): `OpenTime = floor(UtcNow.Ticks / period) * period` (UTC-epoch aligned), `CloseTime = OpenTime + BarPeriod`, due time = `max(CloseTime - now, 0)`
- [ ] **Monitoring (timer) base** (`MonitoringSensorBaseT.cs`): periodic send via `ScheduledTaskHandle`; virtuals `GetValue/GetStatus/GetComment/GetDefaultValue`; `_sendValueInProgress` reentrancy guard; **lifecycle epoch** (`Interlocked` long) bumped on init/restart/stop — callback captures epoch, revalidates before send, drops stale value; `GetValue` exception → `HandleException` + value sent with `status=Error, comment=ex.Message`; `RestartTimerAsync(newPeriod)` = bounded stop → epoch bump → reschedule
- [ ] **Rate**: CAS-loop accumulation (no locks), sticky status/comment from last `AddValue`, `RateDisplayUnit`, default `PostDataPeriod` 1 min
- [ ] **File**: async read (81920-byte buffer, `FileShare.ReadWrite`), limits `MaxFileSizeBytes` (default 10 MB) and `int.MaxValue` hard cap; name/extension extracted from path else options defaults; `AddValue(string)` = UTF-8 → bytes; returns false on invalid status / `CanAcceptData == false` / missing file / oversize
- [ ] **Function**: no-params invokes func each period; params variant buffers into `ConcurrentQueue<U>` with FIFO eviction at `MaxCacheSize` (default 10000), snapshots under lock, invokes func outside lock
- [ ] Exceptions in any sensor: `HandleException` → `DataProcessor.AddException(path, ex)` (deduplicated) + `ExceptionThrowing` event; never crash the host

## 6. Options, prototypes, paths

`Options/*`, `Prototypes/*`

- [ ] `SensorOptions` common surface: `Description`, `SensorUnit (Unit?)`, `TTLs (List<TimeSpan?>)`, `KeepHistory`, `SelfDestroy`, `EnableForGrafana`, `IsSingletonSensor`, `AggregateData`, `Statistics (None|EMA)`, `DefaultAlertsOptions`, `IsForceUpdate`, `IsPrioritySensor`, `IsComputerSensor`, `SensorLocation (Module|Product)`, `TtlAlerts`
- [ ] Option subclasses: `InstantSensorOptions(+Alerts)`, `MonitoringInstantSensorOptions(+PostDataPeriod 15 s)`, `BarSensorOptions(+BarPeriod/BarTickPeriod/PostDataPeriod/Precision/BarAlerts)`, `RateSensorOptions(PostDataPeriod 1 min, Unit=ValueInSecond)`, `FunctionSensorOptions`/`ValuesFunctionSensorOptions(+MaxCacheSize)`, `FileSensorOptions(+DefaultFileName/Extension/MaxFileSizeBytes)`, `EnumSensorOptions(+EnumOptions, AggregateData=true)`, special: `DiskSensorOptions`, `DiskBarSensorOptions`, `VersionSensorOptions`, `ServiceSensorOptions`, `NetworkSensorOptions`, `WindowsInfoSensorOptions`, `CollectorMonitoringInfoOptions`
- [ ] Path model: `CalculateSystemPath()` — computer sensor → `ComputerName / Path`; module location → `ComputerName / Module / Path`; product location → `Path`
- [ ] `DefaultPrototype.BuildPath`: join with `/`, drop null/empty/whitespace segments, split interior `/`, collapse `//` (contract locked by `DefaultPrototypeBuildPathTests`)
- [ ] `RevealDefaultPath` = `{.computer|.module} / Category / SensorName` (folder constants `.computer`, `.module`)
- [ ] Prototype merge (`DefaultPrototype.Merge`): custom-null-falls-back-to-default per property; defaults own Path/Type/ComputerName/Module/IsComputerSensor
- [ ] Per-prototype defaults (intervals, units, descriptions, default alerts) — see `Prototypes/Collections/*` per sensor; treat each prototype as part of the contract

## 7. Alert DSL

`Alerts/*` (+ wire enums in §11)

- [ ] `AlertsFactory` instant conditions: `IfValue<T>(op, target)`, `IfComment(op, target)`, `IfStatus(op)`, `IfLength(op, n)`, `IfFileSize(op, n)`, `IfReceivedNewValue()`, `IfEmaValue(op, target)`
- [ ] Bar conditions: `IfMin/IfMax/IfMean/IfCount/IfFirstValue/IfLastValue(op, value)`, `IfBarComment/IfBarStatus`, `IfReceivedNewBarValue`, EMA variants (`IfEmaMin/IfEmaMax/IfEmaMean/IfEmaCount`)
- [ ] Chaining `.And*` (And-combination), then actions: `.ThenSendNotification(template)`, `.ThenSetIcon(icon)`, `.ThenSetSensorError()`, `.AndConfirmationPeriod(TimeSpan)`, schedule modifiers (`AndScheduledNotificationTime`, repeat mode, instant send)
- [ ] `.Build()` / `.BuildAndDisable()` → `InstantAlertTemplate` / `BarAlertTemplate` / `SpecialAlertTemplate` (TTL alerts)
- [ ] TTL alerts via `TtlAlerts` + `DefaultAlertsOptions` flags (`DisableTtl=1`, `DisableStatusChange=2`)

## 8. Default sensors — Windows

`Collections/WindowsSensorsCollection.cs`, `DefaultSensors/Windows/**`, `Prototypes/Collections/**`

Per-process (paths under `.module/Process <name>/...`, bar 1 s tick / 10 s bars unless noted):

- [ ] `AddProcessCpu` — PerfCounter `Process \ % Processor Time` (instance = process)
- [ ] `AddProcessMemory` — `Process \ Working set` → MB
- [ ] `AddProcessThreadCount` — `Process \ Thread Count`
- [ ] `AddProcessThreadPoolThreadCount` — .NET ThreadPool API (cross-platform impl)
- [ ] `AddProcessTimeInGC` — perf counter (net472) / `System.Runtime` EventListener (net6+)
- [ ] `AddProcessMonitoringSensors` (bulk)

System (`.computer/System/...`):

- [ ] `AddTotalCpu` — `Processor \ % Processor Time \ _Total`
- [ ] `AddFreeRamMemory` — `Memory \ Available MBytes`
- [ ] `AddGlobalTimeInGC` — `.NET CLR Memory \ % Time in GC \ _Global_`
- [ ] `AddSystemMonitoringSensors` (bulk)

Disks (`.computer/Disks monitoring/...`; single-disk and all-fixed-drives variants; enumeration = `DriveInfo.GetDrives()` filtered to `DriveType.Fixed`):

- [ ] `AddFreeDiskSpace` / `AddFreeDisksSpace` — `DriveInfo.AvailableFreeSpace`, instant double MB, 5 min period
- [ ] `AddFreeDiskSpacePrediction` / `AddFreeDisksSpacePrediction` — TimeSpan-until-full; EMA speed `0.9*old + 0.1*new`, 30 s sampling, calibration first 10 requests (OffTime), increase → keep previous prediction + OffTime
- [ ] `AddActiveDiskTime` / `AddActiveDisksTime` — `LogicalDisk \ % Disk Time` per instance
- [ ] `AddDiskQueueLength` / `AddDisksQueueLength` — `LogicalDisk \ Avg. Disk Queue Length`
- [ ] `AddDiskAverageWriteSpeed` / `AddDisksAverageWriteSpeed` — `LogicalDisk \ Disk Write Bytes/sec` → MB/s
- [ ] `AddDiskMonitoringSensors` / `AddAllDisksMonitoringSensors` (bulk)

Windows info (`.computer/Windows OS Info/...`, 12 h period):

- [ ] `AddWindowsLastRestart` — WMI `Win32_OperatingSystem.LastBootUpTime` → TimeSpan
- [ ] `AddWindowsLastUpdate` — WMI `Win32_QuickFixEngineering` max InstalledOn → TimeSpan
- [ ] `AddWindowsInstallDate` — WMI `Win32_OperatingSystem.InstallDate` → TimeSpan (default alert > 4 years)
- [ ] `AddWindowsVersion` — registry `ProductName/DisplayVersion/Build` → Version sensor
- [ ] Event logs: `AddWindowsApplicationErrorLogs`, `AddWindowsSystemErrorLogs`, `AddWindowsApplicationWarningLogs`, `AddWindowsSystemWarningLogs` + bulks `AddErrorWindowsLogs`/`AddWarningWindowsLogs`/`AddAllWindowsLogs` — `EventLog.EntryWritten` subscription, value = EventID string, comment = `Source + Message`, event time → UTC
- [ ] `AddWindowsInfoMonitoringSensors` (bulk)

Network (`.computer/Network/...`, TCPv4+TCPv6 counters summed, 1 min period):

- [ ] `AddNetworkConnectionsEstablished` (gauge), `AddNetworkConnectionFailures` (delta), `AddNetworkConnectionsReset` (delta), `AddAllNetworkSensors` (bulk)

Service status:

- [ ] `SubscribeToWindowsServiceStatus(serviceName | ServiceSensorOptions)` / `UnsubscribeWindowsServiceStatus` — `ServiceController` poll every 5 s, enum sensor of `ServiceControllerStatus` with per-state colors, send on change, default alert ≠ Running with 5 min confirmation; missing service → error value + 1 h retry backoff
- [ ] Perf-counter infrastructure: `WindowsPerformanceCounterFactory` — counter create/retry/recreate on `InvalidOperationException`, dispose in StopAsync

Bulk roots:

- [ ] `AddAllComputerSensors()` = system + all-disks + windows-info + network
- [ ] `AddAllModuleSensors(productVersion)` = process + collector-monitoring + queue diagnostics + product version (if given)
- [ ] `AddAllDefaultSensors(productVersion)` = both

## 9. Default sensors — Unix

`Collections/UnixSensorsCollection.cs`, `DefaultSensors/Unix/**`

- [ ] `AddProcessCpu` — `Process.TotalProcessorTime` delta / wall time
- [ ] `AddProcessMemory` — `Process.WorkingSet64` → MB
- [ ] `AddProcessThreadCount` — `Process.Threads.Count`
- [ ] `AddProcessThreadPoolThreadCount` — ThreadPool API
- [ ] `AddTotalCpu` — `/proc/stat` jiffy parsing (`ProcStat`)
- [ ] `AddFreeRamMemory` — `/proc/meminfo` `MemAvailable` (`ProcMeminfo`)
- [ ] `AddFreeDiskSpace` + `AddFreeDiskSpacePrediction` — root `/` via `DriveInfo` (no multi-disk fan-out on Unix)
- [ ] Bulks: `AddProcessMonitoringSensors`, `AddSystemMonitoringSensors`, `AddDiskMonitoringSensors`, `AddAllComputerSensors`, `AddAllModuleSensors`, `AddAllDefaultSensors`
- [ ] **[decide]** Unix gaps vs Windows (no GC-time, network, OS-info, event logs, service status) — port as-is or extend natively

## 10. Cross-platform module & diagnostic sensors

`DefaultSensors/Other/*`, `DefaultSensors/Diagnostic/*`, `Prototypes/Collections/ModuleInfoCollections.cs`, `QueueDiagnosticCollection.cs`

Module info (paths directly under `.module/`):

- [ ] `AddCollectorAlive` — bool heartbeat, 15 s period, first value `false` then `true`, TTL 1 min, KeepHistory 180 d
- [ ] `AddCollectorVersion` — collector assembly version + start time (KeepHistory ~5 y)
- [ ] `AddCollectorErrors` — string sensor fed by `MessageDeduplicator` callback (framework errors)
- [ ] `AddProductVersion(VersionSensorOptions)` — user-supplied version + start time
- [ ] `CreateServiceCommandsSensor` ("Service commands" path)
- [ ] `AddCollectorMonitoringSensors` (bulk: alive + version + errors)

Queue self-diagnostics (`.module/Collector queue stats/...`, all `IsPrioritySensor=true`):

- [ ] `AddQueueOverflow` — int bar of dropped/evicted item counts per queue
- [ ] `AddQueuePackageValuesCount` — int bar of values per sent package
- [ ] `AddQueuePackageProcessTime` — double bar of avg time-in-queue per package
- [ ] `AddQueuePackageContentSize` — double bar of serialized package size (chars → MB)
- [ ] `AddAllQueueDiagnosticSensors` (bulk)
- [ ] Diagnostics feed points: `AddPackageInfo`/`AddPackageSendingInfo` after each successful send; **suppressed** after the data-drain boundary during stop (#1075); overflow reporting NOT suppressed

## 11. Queues & data pipeline

`Core/DataProcessor.cs`, `SyncQueue/**`

- [ ] Four queues over unbounded `Channel<QueueItem<T>>`: **Data** (batch `MaxValuesInPackage`, waits `PackageCollectPeriod`, 100 ms floor; keeps draining while a full batch is available), **Priority data** (reactive drain), **File** (single-item dispatch), **Command** (batch, reactive)
- [ ] `QueueItem<T>.BuildDate = UtcNow` captured at enqueue; `DataPackage` aggregates time-in-queue stats
- [ ] Per-queue state machine Stopped/Running/Stopping with restart-after-crash handling, `_acceptingWritesFlag` gate closed at StopAsync entry, lifecycle lock, cleanup continuation
- [ ] `EnqueueResult` statuses: `Accepted(+DroppedCount)`, `RejectedCollectorNotAcceptingData` (lifecycle gate), `RejectedQueueStopped` — distinction is test-observability only, producers must not branch on it
- [ ] Overflow: FIFO head drop while `QueueCount > MaxQueueSize`; dropped counts → `QueueOverflowSensor` via `HandleEnqueueResult` (with QueueOverflowSensor self-loop guard)
- [ ] Retry policy: failed send (exception or `PackageSendingInfo.Error`) → re-enqueue whole package, rethrow, retry next cycle, **no retry cap** (overflow is the backstop); error logs deduplicated (`AddQueueLoopError`)
- [ ] **#1088**: retry meeting a full queue is dropped (never evicts the fresher head); reported via `ReportRequeueEviction`
- [ ] **#1090**: `_buildDateMirror` (ConcurrentQueue of in-queue BuildDate ticks, updated on every enqueue/dequeue) — retry older than current FIFO head is dropped; bypassed once writes are closed (shutdown) so cancelled in-flight work survives into bounded flush
- [ ] Cancellation semantics: `PreserveCanceledPackages` only for GracefulStop (re-enqueue on OCE), TerminalDispose drops
- [ ] `ShutdownMode` matrix: GracefulStop (flush, preserve-canceled, wait `RequestTimeout`), TerminalDispose (flush, no preserve, wait `min(RequestTimeout, 1 s)`), StartRollback (clear immediately, no flush)
- [ ] Stop drain order: stop all queues → flush Priority → flush Data → set diagnostics-suppression flag → flush File → flush Command → `ClearQueue` + `LogDiscardedItems` per queue; flush timeout clamped to [1 s, 5 s]
- [ ] `FlushAsync` + `IsFlushing` — failure log says "queued for clear" in flush context vs "preserved" in run-loop context, "+N dropped" suffix
- [ ] Bar values with `Count <= 0` filtered out at package build (`GetPackage` validation)

## 12. HTTP transport

`Client/HttpsClient/**`, `Converters/*`

- [ ] **[wire]** Endpoint routes under `/api/sensors`: `bool`, `int`, `double`, `string`, `timespan`, `version`, `rate`, `enum`, `intBar`, `doubleBar`, `list` (polymorphic batch), `file`, `commands`, `addOrUpdate`, `testConnection` (GET)
- [ ] **[wire]** Headers: `Key: <AccessKey>`, `ClientName: <ClientName>`; base URL `{scheme}://{ServerAddress}:{Port}`; scheme defaulting to HTTPS, plaintext only with `AllowPlaintextTransport`; `AllowUntrustedServerCertificate` skips TLS validation
- [ ] Polly resilience: data/file handlers — 10 attempts, exponential backoff 1 s → 2 min; command handler — `int.MaxValue` attempts, linear backoff; **known gap (keep or fix consciously): no `ShouldHandle` discrimination of 4xx/5xx — poison payloads retry until overflow evicts**
- [ ] `CancelPendingRequests()` (ICancelableDataSender) — cancels in-flight token, installs fresh source, must NOT dispose the HttpClient (PR #1080 finding #7)
- [ ] `PackageSendingInfo { ContentSize (chars), IsSuccess, Error = "Code: {status}. {content}" | exception message }`
- [ ] JSON: System.Text.Json, `AllowNamedFloatingPointLiterals` (NaN/Infinity), runtime-polymorphic serialization of `SensorValueBase`/`CommandRequestBase` (`JsonRequestConverter`), UTF-8 `application/json`
- [ ] Per-command response parsing for `commands`/`addOrUpdate` (per-key error dictionary)

## 13. Scheduler & threading

`Threading/*`

- [ ] `CollectorScheduler` — bucketed timer wheel (`SortedDictionary<long, LinkedList<ScheduledTask>>`), single worker thread dispatching due tasks to ThreadPool, `Stopwatch`-based monotonic milliseconds (immune to `Environment.TickCount` wrap — historical bug, see memory), `ManualResetEventSlim` wakeups
- [ ] `Schedule(Action|Func<Task>, delay, period, onError)` → `ScheduledTask`; one-shot via `InfiniteTimeSpan`; periodic `Advance()` catch-up loop (no skipped re-bucketing into the past)
- [ ] **onError callback contract: action exceptions must reach onError, never kill the loop** (historical PeriodicTask bug)
- [ ] `ScheduledTaskHandle` — idempotent `Start(...)`, `StopAsync(waitForCurrentRun)` with ~1 s bounded wait; consumers: monitoring sensors, bar collect/send loops, disk prediction sampling, MessageDeduplicator cleanup
- [ ] Per-collector scheduler instance, owned/disposed by DataCollector (worker stop timeout 5 s)

## 14. Error handling, logging, dedup

`Exceptions/MessageDeduplicator.cs`, `Logging/*`

- [ ] `MessageDeduplicator` — window (default 1 h), capacity (default 1000) with oldest-expiry eviction, **zero window = invoke action immediately and return** (double-log regression guard), periodic cleanup task, count-suffix on expiry flush ("… N times"); feeds both logger and `CollectorErrorsSensor`
- [ ] `ICollectorLogger { Debug, Info, Error(string), Error(Exception) }`; `LoggerManager` swallows logger exceptions; `NLogLogger` with embedded `collector.nlog.config` fallback + `LoggerOptions { ConfigPath, WriteDebug }`
- [ ] Routing: sensor exceptions → `AddException` (dedup), queue loop failures → `AddQueueLoopError` (dedup), validation rejects → `LogDroppedValue` (Debug), shutdown discards → `LogDiscardedItems` (Error)

## 15. Wire contract (HSMSensorDataObjects) — ALL [wire]

`src/api/HSMSensorDataObjects/**`

- [ ] `SensorType`: BooleanSensor=0, IntSensor=1, DoubleSensor=2, StringSensor=3, IntegerBarSensor=4, DoubleBarSensor=5, FileSensor=6, TimeSpanSensor=7, VersionSensor=8, RateSensor=9, EnumSensor=10
- [ ] `SensorStatus`: OffTime=0, Ok=1, Warning=2, Error=3 (default Ok)
- [ ] `SensorValueBase`: `Comment`, `Time (UTC now default)`, `Status`, `Path`; typed `Value` per DTO (`BoolSensorValue`, `IntSensorValue`, `DoubleSensorValue`, `StringSensorValue`, `TimeSpanSensorValue`, `VersionSensorValue`, `EnumSensorValue (int)`, `RateSensorValue (double)`, `CounterSensorValue`)
- [ ] Bar DTOs: `IntBarSensorValue`/`DoubleBarSensorValue` — `Min/Max/Mean/Count/FirstValue?/LastValue/OpenTime/CloseTime` (+obsolete `Percentiles`)
- [ ] `FileSensorValue`: `Value (List<byte> → base64)`, `Name`, `Extension`
- [ ] `AddOrUpdateSensorRequest`: `SensorType?`, `Description`, `KeepHistory?/SelfDestroy?/TTL(s)` (ticks), `Statistics (EMA=1)`, `IsSingletonSensor?`, `AggregateData?`, `EnableGrafana?`, `OriginalUnit?`, `DisplayUnit?`, `IsForceUpdate`, `EnumOptions (Key/Value/Description/Color ARGB)`, `Alerts`, `TtlAlerts`, `DefaultAlertsOptions`
- [ ] Alert wire enums: `AlertOperation` (LE=0, LT=1, GT=2, GE=3, Eq=4, Ne=5, IsChanged=20, IsError=21, IsOk=22, IsChangedToError=23, IsChangedToOk=24, Contains=30, StartsWith=31, EndsWith=32, ReceivedNewValue=50), `AlertProperty` (Status=0, Comment=1, Value=20, Min=101, Max=102, Mean=103, Count=104, LastValue=105, FirstValue=106, Length=120, OriginalSize=151, NewSensorData=200, EmaValue=210, EmaMin=211, EmaMax=212, EmaMean=213, EmaCount=214), `AlertCombination` (And=0, Or=1), `TargetType` (Const=0, LastValue=1), `AlertDestinationMode`, `AlertRepeatMode` (FiveMinutes=5 … Weekly=100)
- [ ] `Unit` enum sparse values: bits=0…GB=4, Percents=100, Ticks=1000, Milliseconds=1010, Seconds=1011, Minutes=1012, Count=1100, Requests=1101, Responses=1102, Bits_sec=2100…MBytes_sec=2103, ValueInSecond=3000
- [ ] JSON conventions: camelCase property names, nulls/defaults omitted, enums as numbers, DateTime ISO-8601 UTC `Z`, TimeSpan `hh:mm:ss.fff`, Version `a.b.c[.d]`, file bytes base64, batch discriminator field `"type"` (`"bool"`, `"int"`, …, `"intBar"`, `"doubleBar"`, `"file"`, `"counter"`, `"enum"`)
- [ ] History DTOs (`HistoryRequest`, `FileHistoryRequest`) — **[decide]** collector itself doesn't query history; port only if native client should

## 16. Existing C++/CLI wrapper parity (reference, `src/wrapper/`)

What the wrapper already exposes (use as the minimal-API oracle): `DataCollectorProxy` lifecycle + Initialize\* bulk setups, instant bool/int/double/string sensors, last-value variants, int/double bar sensors, int/double rate sensors, no-params/params func sensor templates, `SendFileAsync`, alert DSL builders, options structs, `HSMSensorStatus`/alert enums.

Wrapper gaps (= **[decide]** items for the native port): TimeSpan sensor, Version sensor, Enum sensor, Counter sensor, file sensor as a typed object, service-commands sensor, lifecycle listeners/events, queue diagnostics toggles beyond bulk init, fluent sensor builders, history queries.

## 17. Cross-cutting invariants the port MUST reproduce

1. Values added before `Start()` / after `Stop()` are silently rejected — no exception to caller.
2. Start/Stop/Dispose are idempotent and race-safe; exactly one ToStopped per cycle; terminal dispose mode wins over a racing graceful stop.
3. Path uniqueness with transparent dedup; type conflict throws.
4. Validation: NaN/Infinity/null rejected pre-queue; comment capped at 1024; bar partial stats must be consistent (with double tolerance).
5. Bars never roll without a successful send (no data loss on guard collision); bar windows are UTC-epoch aligned.
6. Lifecycle epoch invalidation: stale timer callbacks must not land values after stop/restart.
7. Queue semantics: FIFO per queue, at-least-once delivery, retry-forever with overflow eviction as the only backstop, newest-data-wins under overflow (#1088/#1090 retry rules), graceful stop preserves accepted work (bounded flush), terminal dispose stays bounded even with a broken transport.
8. Diagnostics suppression after data-drain boundary; overflow telemetry exempt.
9. Scheduler: monotonic clock, period catch-up, errors to onError callback, never kill the loop (PeriodicTask history).
10. Logger and lifecycle-listener exceptions are always swallowed and logged.
11. All enum numeric values, JSON field names/format, endpoint paths and headers are wire-frozen (§12, §15).

## 18. Out of scope / known managed-side gaps (do not blindly clone)

- Polly pipeline lacks `ShouldHandle` for HTTP 4xx/5xx — poison data retries until eviction. Decide consciously for the port (suggest: bounded retry for 4xx).
- Obsolete `Initialize*` family and `ValuesQueueOverflow` event — recommend dropping (§2).
- `Percentiles` on bar DTOs is obsolete — emit nothing.
- net472-only code paths (perf-counter GC time) are platform decisions, not contract.
