# C++ Collector Port — Functional Checklist (Spike Step 1)

> Owner: collector/integrations | Created: 2026-06-10 | Status: living checklist
> Companion to [`cpp-collector-port-spike.md`](cpp-collector-port-spike.md).

**The single source for "what must the port cover".** One line per functional
item; tick as the native core covers it. Behavior details live in the
maintained [`aicontext/`](../../aicontext/README.md) feature docs (linked per
section) — this file stays a flat list so porting can be verified against ONE
place.

Maintenance contract: any PR that adds/changes collector functionality adds or
updates a line here AND the matching aicontext feature doc. End state: each
line maps to a shared conformance test (see spike "Shared conformance script");
once a line is test-covered, mark it `[x]` with the test name. The checklist
retires when the conformance suite owns every line.

Parity bug rule (epic #1093, "Parity bug policy"): a bug found in EITHER
collector (.NET or native C++) is triaged against the other before closing —
reproducing conformance scenario first, run on both, fix every implementation
it is red on; one issue (`bug` + `cpp-port`) records the per-implementation
verdict.

Legend: **[wire]** = byte-for-byte compatibility. **[decide]** = explicit port
decision needed (not automatically in scope).

---

## 1. Construction & configuration

Details: [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [ ] `DataCollector(CollectorOptions)` ctor + immediate `Validate()`
- [ ] Convenience ctor `(productKey, address="localhost", port=44330, clientName=null)`
- [ ] `TestConnection()` → `ConnectionResult { Code, Error, IsOk, Result (= Error empty), static Ok }`, callable in any state
- [ ] `IDataSender` transport seam (`TestConnectionAsync/SendDataAsync/SendPriorityDataAsync/SendCommandAsync/SendFileAsync/Dispose`)
- [ ] Option `AccessKey` (required, non-whitespace)
- [ ] Option `ServerAddress` (default `localhost`, required)
- [ ] Option `Port` (default 44330, 1..65535)
- [ ] Option `ClientName` (default null)
- [ ] Option `ComputerName` / `Module` (path hierarchy)
- [ ] Option `MaxQueueSize` (default 20000, > 0, per queue)
- [ ] Option `MaxValuesInPackage` (default 1000, > 0)
- [ ] Option `PackageCollectPeriod` (default 15 s, > 0)
- [ ] Option `RequestTimeout` (default 30 s, > 0)
- [ ] Option `DataSender` (default `HsmHttpsClient`)
- [ ] Option `AllowUntrustedServerCertificate` (default false)
- [ ] Option `AllowPlaintextTransport` (default false)
- [ ] Option `ExceptionDeduplicatorWindow` (default 1 h, >= 0, zero = immediate log)
- [ ] Option `MaxDeduplicatedMessages` (default 1000, > 0)
- [ ] Option `MaxSensors` (default 100000, > 0)

## 2. Lifecycle

Details: [`overview.md`](../../aicontext/features/collector/overview.md), [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [ ] `CollectorStatus`: Starting → Running → Stopping → Stopped → Disposed (terminal)
- [ ] Gate `CanAcceptData` (Starting/Running/Stopping)
- [ ] Gate `CanRegisterSensors` (Stopped/Starting/Running)
- [ ] Gate `CanStartNewSensors` (Starting/Running)
- [ ] Public `IsAcceptingRegistrations` / `ICollectorRegistrationState`
- [ ] `Start()` / `Start(customStartingTask)` — idempotent; custom task between processor start and sensor init
- [ ] Start failure → rollback to Stopped (queues via `StartRollback` mode)
- [ ] `Stop()` / `Stop(customStoppingTask)` — idempotent; awaits dynamic sensor-start tasks; custom-task failure logged, stop proceeds
- [ ] `Dispose()` — idempotent, terminal, never throws; from any state
- [ ] Dispose-vs-Stop race: joins in-flight stop, exactly one ToStopped, terminal mode wins on queues
- [ ] Stop/Dispose racing Start: waits `_currentStartInitTask`, not the pre-init custom task
- [ ] Restart support: Start→Stop→Start re-inits and restarts all registered sensors
- [ ] Events `ToStarting/ToRunning/ToStopping/ToStopped` (fired under lock, per-handler exception isolation)
- [ ] `ILifecycleListener` (`OnStarting/OnRunning/OnStopping/OnStopped`) + `AddLifecycleListener(...)`; no replay of current state
- [ ] Disposal order: DataProcessor → DataSender → CollectorScheduler → global exception handler unhook
- [ ] Status vs CanAcceptData asymmetry after Dispose (flush window until CompleteStop)
- [ ] **[decide]** Obsolete: sync `Initialize()` overloads, `InitializeSystemMonitoring`/`InitializeProcessMonitoring`/`InitializeOsMonitoring`/`MonitorServiceAlive`/`InitializeWindowsUpdateMonitoring`, `ValuesQueueOverflow` event — recommend NOT porting
- [ ] **[decide]** Obsolete (wire/options compat shims): `SensorOptions.DefaultChats` + `DefaultChatsMode`, `BaseRequest.Key` (key-in-body), `BarSensorValueBase.Percentiles`, `AddOrUpdateSensorRequest.TTL`/`TtlAlert` set-only shims, `AlertDestinationMode.DefaultChats`

## 3. Sensor registration

Details: [`overview.md`](../../aicontext/features/collector/overview.md) §Sensor registration

- [ ] Path validation: every `Create*` throws `ArgumentException` for null/whitespace/slash-only paths
- [ ] Identity = full normalized path; duplicate Create (same type + IsLastValue) returns existing instance, new one disposed
- [ ] Same path + different type/IsLastValue → `InvalidOperationException`
- [ ] `MaxSensors` cap enforced atomically; offender removed and disposed
- [ ] Stopped phase → sensor queued for next Start
- [ ] Starting/Running → immediate async `InitAndStart`, tracked, awaited by Stop
- [ ] Stopping/Disposed → rejected non-throwing (logged, disposed, returned inert)
- [ ] `InitAsync` of every sensor sends `AddOrUpdateSensorRequest` before first value

## 4. Sensor creation API

Details: [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [ ] `Create{Bool,Int,Double,String,Version,Time}Sensor(path, description)` + `(path, InstantSensorOptions)`
- [ ] `CreateEnumSensor(path, description | EnumSensorOptions)`
- [ ] `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)` + generic `CreateLastValueSensor<T>(path, options, defaultValue)`
- [ ] `CreateRateSensor(path, RateSensorOptions)` + `CreateM1RateSensor` + `CreateM5RateSensor`
- [ ] `CreateIntBarSensor(path, barPeriod=300000, postPeriod=15000, descr)` + options overload
- [ ] DataCollector-only TimeSpan overloads `Create{Int,Double}BarSensor(path, TimeSpan barPeriod, TimeSpan postPeriod[, precision], descr)`
- [ ] `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor` presets
- [ ] `CreateDoubleBarSensor(..., precision=2, ...)` + options overload + same five presets
- [ ] `CreateFileSensor(path, fileName, extension="txt", descr)` / `(path, FileSensorOptions)`
- [ ] Collector-level `SendFileAsync(sensorPath, filePath, status, comment)`
- [ ] `CreateNoParamsFuncSensor<T>(path, descr, Func<T>, interval ms|TimeSpan)` + `Create{1Min,5Min}NoParamsFuncSensor` + `CreateFunctionSensor<T>(path, func, options)`
- [ ] `CreateParamsFuncSensor<T,U>(path, descr, Func<List<U>,T>, interval)` + `Create{1Min,5Min}ParamsFuncSensor` + `CreateValuesFunctionSensor<T,U>`
- [ ] `CreateServiceCommandsSensor()`
- [ ] Interface `IInstantValueSensor<T>`: `AddValue(v)` / `(v, comment)` / `(v, status, comment)`
- [ ] Interface `ILastValueSensor<T>` (latest value, sends once on stop, default if none)
- [ ] Interface `IBarSensor<T>`: `AddValue`, `AddValues(IEnumerable)`, `AddPartial(min,max,mean,first,last,count)`
- [ ] Interface `IFileSensor`: + `Task<bool> SendFile(filePath, status, comment)`
- [ ] Interface `IMonitoringRateSensor` (pure alias of `IInstantValueSensor<double>`; defining file is misleadingly named `IMonitoringCounterSensor.cs`)
- [ ] Interface `IServiceCommandsSensor`: `SendCustomCommand(cmd, initiator)`, `SendUpdate(initiator[, new[, old]])`, `SendRestart/SendStart/SendStop(initiator)`
- [ ] Interface `IBaseFuncSensor` (in `Obsolete` folder but NOT `[Obsolete]` — current return type): `GetInterval()`, `RestartTimer(TimeSpan)`, `GetFunc()`; params variant `AddValue(U)`
- [ ] Last-value null-default throws: `CreateLastValueStringSensor(path)` / `CreateLastValueVersionSensor(path)` with implicit null default → `ArgumentException` at creation
- [ ] Fluent builders (per-type setters): `InstantSensor<T>` `.Description/.Ttl/.KeepHistory/.Priority/.Configure`; `BarSensor<T>` `.BarPeriod/.PostPeriod/.TickPeriod/.Precision/.Description/.Configure`; `RateSensor` `.PostPeriod/.Description/.Configure`; `.Build()` throws `NotSupportedException` for unsupported `T`
- [ ] Properties `Status`, `ComputerName`, `Module`, `Windows`, `Unix`
- [ ] Property `DefaultSensors` (`IEnumerable<ISensor>`; public `ISensor`: `SensorPath/InitAsync/StartAsync/StopAsync/Dispose`; `SensorBase` adds `SendValue(SensorValueBase)` + `ExceptionThrowing` event)
- [ ] `IWindowsCollection`/`IUnixCollection` are `IDisposable`
- [ ] `AddNLog(LoggerOptions { ConfigPath, WriteDebug })` + embedded config fallback
- [ ] `AddCustomLogger(ICollectorLogger { Debug, Info, Error(string), Error(Exception) })`

## 5. Sensor mechanics

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md)

- [ ] Validation: null rejected (strings allowed); double/float NaN/±Infinity rejected
- [ ] Validation: status must be defined `SensorStatus` member
- [ ] Validation: comment trimmed to 1024 chars
- [ ] Validation: rejected values logged (Debug), never enqueued
- [ ] Instant flow: validate → enqueue immediately; `IsPrioritySensor` routes to priority queue
- [ ] Last-value flow: store latest, single enqueue on StopAsync, `IsLastValue=true` identity
- [ ] Bar: min/max/mean(sum/count)/count/first/last under lock
- [ ] Bar `AddPartial` merge + consistency check (int strict; double tolerance `max(1e-12, |max-min|*1e-9)`)
- [ ] Bar `Complete()` rounding (double: `Round(v, Precision, AwayFromZero)` on all stats; int: mean only)
- [ ] Bar roll only after confirmed send (`if (TrySendValue()) BuildNewBar()`) — no-roll-without-send invariant
- [ ] Bar UTC-epoch alignment: `OpenTime = floor(now/period)*period`, `CloseTime = OpenTime + BarPeriod`
- [ ] Bar periods: `BarPeriod` 5 min / `BarTickPeriod` 5 s / `PostDataPeriod` 15 s / `Precision` 2 (0..15), all validated
- [ ] Monitoring base: periodic send loop via `ScheduledTaskHandle`, virtuals `GetValue/GetStatus/GetComment/GetDefaultValue`
- [ ] Monitoring base: `_sendValueInProgress` reentrancy guard
- [ ] Monitoring base: lifecycle epoch — capture, revalidate before send, drop stale (init/restart/stop bump)
- [ ] Monitoring base: `GetValue` exception → value with `status=Error, comment=ex.Message` + deduped log
- [ ] Monitoring base: `RestartTimerAsync(newPeriod)` = bounded stop → epoch bump → reschedule
- [ ] Rate: lock-free CAS accumulation; `GetValue` = `Interlocked.Exchange(sum,0) / period.TotalSeconds`
- [ ] Rate: sticky status/comment from last AddValue; default PostDataPeriod 1 min
- [ ] File: async read (81920 buffer, `FileShare.ReadWrite`); `MaxFileSizeBytes` (10 MB default) + `int.MaxValue` caps
- [ ] File: name/extension from path else options defaults; `AddValue(string)` = UTF-8 bytes
- [ ] File: `SendFile` false on invalid status / `CanAcceptData==false` / missing / oversize
- [ ] Function no-params: invoke func each period
- [ ] Function params: `ConcurrentQueue` cache, FIFO eviction at `MaxCacheSize` (10000), snapshot under lock, func outside lock
- [ ] All sensors: `HandleException` → `AddException` (dedup) + `ExceptionThrowing` event; never crash host

## 6. Options / prototypes / paths

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md) §Options & path model

- [ ] `SensorOptions` common: `Description`, `SensorUnit`, `TTLs`, `KeepHistory`, `SelfDestroy`, `EnableForGrafana`, `IsSingletonSensor`, `AggregateData`, `Statistics(EMA)`, `DefaultAlertsOptions`, `IsForceUpdate`, `IsPrioritySensor`, `IsComputerSensor`, `SensorLocation(Module|Product)`, `TtlAlerts`
- [ ] Singular conveniences `SensorOptions.TTL` (→ `TTLs`) and `TtlAlert` (→ `TtlAlerts`)
- [ ] `DisplayUnit` per options type: `NoDisplayUnit`, `RateDisplayUnit { PerSecond=0 … PerMonth=5 }` → wire `DisplayUnit (int?)`
- [ ] `InstantSensorOptions(+Alerts)`; `MonitoringInstantSensorOptions(+PostDataPeriod 15 s)`
- [ ] `BarSensorOptions(+BarPeriod/BarTickPeriod/Precision/BarAlerts)`
- [ ] `RateSensorOptions(PostDataPeriod 1 min, Unit=ValueInSecond)`
- [ ] `FunctionSensorOptions` / `ValuesFunctionSensorOptions(+MaxCacheSize)` — both default `PostDataPeriod` 1 min (ms-param factory overloads default 15 s instead)
- [ ] `FileSensorOptions(+DefaultFileName/Extension/MaxFileSizeBytes)`
- [ ] `EnumSensorOptions(+EnumOptions, AggregateData=true, GenerateEnumOptionsDecription())`
- [ ] `DiskSensorOptions { TargetPath default C:\, CalibrationRequests default 6, PostDataPeriod 5 min }`; `DiskBarSensorOptions { TargetPath }`
- [ ] `VersionSensorOptions { Version, StartTime }`; `ServiceSensorOptions { ServiceName, IsHostService default true → .module placement, SensorPath }`
- [ ] `NetworkSensorOptions`; `WindowsInfoSensorOptions { PostDataPeriod default 12 h }`; `CollectorMonitoringInfoOptions`
- [ ] `CalculateSystemPath`: computer → `ComputerName/Path`; module → `ComputerName/Module/Path`; product → `Path`
- [ ] `BuildPath`: join `/`, drop null/empty/whitespace, split interior `/`, collapse `//`
- [ ] `RevealDefaultPath` = `{.computer|.module}/Category/SensorName`
- [ ] Prototype merge: custom non-null wins for most properties; `Path/Type/IsComputerSensor/ComputerName/Module` pinned from prototype; custom `DefaultAlertsOptions/IsPrioritySensor/IsForceUpdate` dropped for default sensors

## 7. Alert DSL

Details: [`alerts/feature.md`](../../aicontext/features/collector/alerts/feature.md)

- [ ] Instant conditions: `IfValue/IfComment/IfStatus/IfLenght (actual exported name — misspelled; chaining is AndLength)/IfFileSize/IfReceivedNewValue/IfEmaValue`
- [ ] Bar conditions: `IfMin/IfMax/IfMean/IfCount/IfFirstValue/IfLastValue/IfBarComment/IfBarStatus/IfReceivedNewBarValue` + EMA variants
- [ ] TTL entry point `IfInactivityPeriodIs(TimeSpan? = null)` → SpecialAlertCondition (TtlValue feeds wire TTLs)
- [ ] `.And*` chaining (And-combination)
- [ ] Actions: `ThenSendNotification(template, AlertDestinationMode = FromParent)` / `ThenSetIcon(string | AlertIcon)` / `ThenSetSensorError`
- [ ] `ThenSendScheduledNotification(template, time, AlertRepeatMode, instantSend, AlertDestinationMode = FromParent)` + chaining `AndSendScheduledNotification`
- [ ] `AlertIcon { Ok=0 Warning=1 Error=2 Pause=3 ArrowUp=10 ArrowDown=11 Clock=100 Hourglass=101 }` → UTF-8 emoji string on the wire (`IconExtensions.ToUtf8`)
- [ ] `AndConfirmationPeriod(TimeSpan)`
- [ ] `.Build()` / `.BuildAndDisable()` → Instant/Bar/Special templates
- [ ] TTL alerts via `TtlAlerts` (or singular `TtlAlert`); `DefaultAlertsOptions` flags (DisableTtl=1, DisableStatusChange=2)

## 8. Default sensors — Windows

Details: [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md)

- [ ] `AddProcessCpu` (`Process \ % Processor Time`, instance = process)
- [ ] `AddProcessMemory` (`Process \ Working set` → MB)
- [ ] `AddProcessThreadCount` (`Process \ Thread Count`)
- [ ] `AddProcessThreadPoolThreadCount` (ThreadPool API)
- [ ] `AddProcessTimeInGC` (perf counter net472 / EventListener net6+)
- [ ] `AddProcessMonitoringSensors` bulk
- [ ] `AddTotalCpu` (`Processor \ % Processor Time \ _Total`)
- [ ] `AddFreeRamMemory` (`Memory \ Available MBytes`)
- [ ] `AddGlobalTimeInGC` (`.NET CLR Memory \ % Time in GC \ _Global_`)
- [ ] `AddSystemMonitoringSensors` bulk
- [ ] `AddFreeDiskSpace` / `AddFreeDisksSpace` (DriveInfo, instant MB, 5 min)
- [ ] `AddFreeDiskSpacePrediction` / `AddFreeDisksSpacePrediction` (EMA 0.9/0.1, 30 s sampling, calibration first 6 requests — `CalibrationRequests` default, OffTime on growth)
- [ ] `AddActiveDiskTime` / `AddActiveDisksTime` (`LogicalDisk \ % Disk Time`)
- [ ] `AddDiskQueueLength` / `AddDisksQueueLength` (`LogicalDisk \ Avg. Disk Queue Length`)
- [ ] `AddDiskAverageWriteSpeed` / `AddDisksAverageWriteSpeed` (`Disk Write Bytes/sec` → MB/s)
- [ ] Disk fan-out: `DriveInfo.GetDrives()` filtered `DriveType.Fixed`, letter in name + counter instance
- [ ] `AddDiskMonitoringSensors` / `AddAllDisksMonitoringSensors` bulks
- [ ] `AddWindowsLastRestart` (WMI LastBootUpTime → TimeSpan, 12 h)
- [ ] `AddWindowsLastUpdate` (WMI QuickFixEngineering max InstalledOn)
- [ ] `AddWindowsInstallDate` (WMI InstallDate; default alert > 4 y)
- [ ] `AddWindowsVersion` (registry → Version sensor)
- [ ] `AddWindowsApplicationErrorLogs` / `AddWindowsSystemErrorLogs` (EventLog subscription, value=EventID, comment=Source+Message)
- [ ] `AddWindowsApplicationWarningLogs` / `AddWindowsSystemWarningLogs`
- [ ] `AddErrorWindowsLogs` / `AddWarningWindowsLogs` / `AddAllWindowsLogs` bulks
- [ ] `AddWindowsInfoMonitoringSensors` bulk
- [ ] `AddNetworkConnectionsEstablished` (TCPv4+v6 gauge, 1 min)
- [ ] `AddNetworkConnectionFailures` / `AddNetworkConnectionsReset` (deltas)
- [ ] `AddAllNetworkSensors` bulk
- [ ] `SubscribeToWindowsServiceStatus(name | options)` (enum of `ServiceControllerStatus`, 5 s poll, send-on-change, alert ≠Running w/ 5 min confirmation, 1 h re-resolve backoff)
- [ ] Service-status registration payload: `EnumOptions` for 7 `ServiceControllerStatus` members with fixed ARGB colors + generated markdown description; `IsHostService` placement
- [ ] `UnsubscribeWindowsServiceStatus`
- [ ] ServiceCommands sensor: fixed strings "Service start/stop/restart", "Service update [from X] to Y" + implicit `IfReceivedNewValue → notification` alert
- [ ] Perf-counter seam: `IPerformanceCounterFactory`/`IPerformanceCounter`, recreate on `InvalidOperationException`, dispose on stop
- [ ] `AddAllComputerSensors()` / `AddAllModuleSensors(version)` / `AddAllDefaultSensors(version)` bulks

## 9. Default sensors — Unix

Details: [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md)

- [ ] `AddProcessCpu` (`Process.TotalProcessorTime` delta / wall time)
- [ ] `AddProcessMemory` (`WorkingSet64` → MB)
- [ ] `AddProcessThreadCount` (`Process.Threads.Count`)
- [ ] `AddProcessThreadPoolThreadCount`
- [ ] `AddTotalCpu` (`/proc/stat` jiffies, `ProcStat` parser)
- [ ] `AddFreeRamMemory` (`/proc/meminfo` MemAvailable, `ProcMeminfo` parser)
- [ ] `AddFreeDiskSpace` + prediction (root `/` only, DriveInfo/statvfs)
- [ ] Bulks: process / system / disk / computer / module / default
- [ ] No external process spawning (kernel files + managed APIs only)
- [ ] **[decide]** Unix gaps vs Windows: GC time, network, OS info, event logs, service status

## 10. Module & diagnostic sensors (cross-platform)

Details: [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md)

- [ ] `AddCollectorAlive` (bool heartbeat, 15 s, first=false, TTL 1 min, KeepHistory 180 d)
- [ ] `AddCollectorVersion` (assembly version + start time, KeepHistory ~5 y)
- [ ] `AddCollectorErrors` (string, fed by MessageDeduplicator)
- [ ] `AddProductVersion(VersionSensorOptions)`
- [ ] `AddCollectorMonitoringSensors` bulk
- [ ] `AddQueueOverflow` (int bar; includes retry-path drops; never suppressed)
- [ ] `AddQueuePackageValuesCount` (int bar per package)
- [ ] `AddQueuePackageProcessTime` (double bar, avg time-in-queue)
- [ ] `AddQueuePackageContentSize` (double bar, chars → MB)
- [ ] `AddAllQueueDiagnosticSensors` bulk (all priority sensors)

## 11. Queues & pipeline

Details: [`data-pipeline/feature.md`](../../aicontext/features/collector/data-pipeline/feature.md)

- [ ] Four queues over unbounded Channel: Data (periodic batch), Priority (reactive batch), File (single-item), Command (reactive batch)
- [ ] Data queue: `PackageCollectPeriod` wait (100 ms floor); keep draining while full batch remains
- [ ] `QueueItem.BuildDate = UtcNow` at enqueue; `DataPackage` time-in-queue stats
- [ ] Bar `Count <= 0` filtered at package build
- [ ] Per-queue state machine (Stopped/Running/Stopping) + restart-after-unexpected-exit
- [ ] `_acceptingWritesFlag` closes public writes at StopAsync commit; internal retry bypasses
- [ ] `EnqueueResult`: Accepted(+DroppedCount) / RejectedCollectorNotAcceptingData / RejectedQueueStopped (distinction = test-only)
- [ ] Overflow: FIFO head drop while `count > MaxQueueSize`; counts → QueueOverflowSensor (self-loop guard)
- [ ] Retry: failed send re-enqueues package, rethrows, retries next cycle; NO retry cap; deduped error logs
- [ ] #1088: retry at full queue dropped (never evicts fresher head), reported per item
- [ ] #1090: BuildDate mirror — retry older than current FIFO head dropped even below capacity
- [ ] Retry filters bypassed once writes closed (shutdown preserves cancelled in-flight work)
- [ ] Cancellation: re-enqueue on OCE only when mode preserves (`GracefulStop` yes / `TerminalDispose` no)
- [ ] `ShutdownMode.GracefulStop`: flush, preserve-canceled, wait `RequestTimeout`
- [ ] `ShutdownMode.TerminalDispose`: flush, drop-canceled, wait `min(RequestTimeout, 1 s)`
- [ ] `ShutdownMode.StartRollback`: clear immediately, no flush
- [ ] Drain order: stop all → flush Priority → Data → [suppression flag] → File → Command → ClearQueue + LogDiscardedItems
- [ ] Flush timeout clamped [1 s, 5 s]
- [ ] Diagnostics suppression after data-drain boundary (#1075); overflow exempt; reset on Start
- [ ] Flush-context failure wording: "queued for clear" vs "preserved", "+N dropped" (#1087 A)

## 12. HTTP transport

Details: [`http-client/feature.md`](../../aicontext/features/collector/http-client/feature.md)

- [ ] **[wire]** Routes under `/api/sensors/`: bool, int, double, string, timespan, version, rate, enum, intBar, doubleBar, list, file, commands, addOrUpdate, testConnection(GET)
- [ ] **[wire]** Headers `Key`, `ClientName`; base `{scheme}://{address}:{port}`
- [ ] HTTPS default; plaintext only with `AllowPlaintextTransport`; `AllowUntrustedServerCertificate` skips TLS validation
- [ ] Polly: data/priority/file — 10 attempts exponential 1 s → 2 min; commands — `int.MaxValue` linear
- [ ] **[decide]** No `ShouldHandle` for 4xx/5xx (poison retries until eviction) — reproduce or fix consciously
- [ ] `CancelPendingRequests`: cancel token + fresh source, NEVER dispose HttpClient
- [ ] `PackageSendingInfo { ContentSize(chars), IsSuccess, Error }`
- [ ] JSON: System.Text.Json, NaN/Infinity literals allowed, runtime-polymorphic converter
- [ ] Per-command response parsing for commands/addOrUpdate (error dictionary keyed by sensor **path**)

## 13. Scheduler

Details: [`scheduling/feature.md`](../../aicontext/features/collector/scheduling/feature.md)

- [ ] Per-collector instance (no process-global state), disposed by DataCollector last
- [ ] Bucketed timer wheel, single worker, ThreadPool dispatch
- [ ] Monotonic Stopwatch-based clock (never `Environment.TickCount`)
- [ ] `Schedule(Action|Func<Task>, delay, period, onError)`; period > 0 or Infinite (one-shot, auto-dispose)
- [ ] onError: action exceptions routed to callback, loop never dies
- [ ] No overlapping runs of one task (skip tick while running)
- [ ] Catch-up: overdue tasks advance by whole periods into the future
- [ ] `ScheduledTask.StopAsync(waitForCurrentRun)` bounded ~1 s
- [ ] `ScheduledTaskHandle`: idempotent Start/StopAsync composition wrapper
- [ ] Worker shutdown grace 5 s on dispose

## 14. Error handling / dedup / logging

Details: [`error-handling/feature.md`](../../aicontext/features/collector/error-handling/feature.md)

- [ ] MessageDeduplicator: window dedup, capacity + oldest-expiry eviction, count-suffix flush
- [ ] Zero window = invoke immediately AND return (no double log)
- [ ] Routing: sensor ex → AddException; queue loop → AddQueueLoopError; validation → LogDroppedValue(Debug); shutdown discard → LogDiscardedItems(Error)
- [ ] CollectorErrorsSensor fed from dedup callback
- [ ] LoggerManager swallows logger exceptions
- [ ] Lifecycle event/listener exceptions isolated per handler
- [ ] Dispose failures isolated per component

## 15. Wire contract — ALL [wire]

Details: [`api/wire-contract/feature.md`](../../aicontext/features/api/wire-contract/feature.md)

- [ ] `SensorType`: Boolean=0 Int=1 Double=2 String=3 IntegerBar=4 DoubleBar=5 File=6 TimeSpan=7 Version=8 Rate=9 Enum=10
- [ ] `SensorStatus`: OffTime=0 Ok=1 Warning=2 Error=3
- [ ] `Unit` sparse values (bits=0…GB=4, Percents=100, Ticks=1000, ms=1010, s=1011, min=1012, Count=1100, Requests=1101, Responses=1102, rates 2100–2103, ValueInSecond=3000)
- [ ] `AlertOperation` (LE=0 LT=1 GT=2 GE=3 Eq=4 Ne=5 IsChanged=20 IsError=21 IsOk=22 →Error=23 →Ok=24 Contains=30 StartsWith=31 EndsWith=32 ReceivedNewValue=50)
- [ ] `AlertProperty` (Status=0 Comment=1 Value=20 Min=101 Max=102 Mean=103 Count=104 Last=105 First=106 Length=120 OriginalSize=151 NewSensorData=200 Ema*=210–214)
- [ ] `AlertCombination` And=0 Or=1; `TargetType` Const=0 LastValue=1; `AlertRepeatMode` 5/6/7/10/20/50/100
- [ ] `AlertDestinationMode`: DefaultChats=0(obs) NotInitialized=1 Empty=2 FromParent=3 AllChats=200
- [ ] Display units: `NoDisplayUnit`; `RateDisplayUnit` PerSecond=0…PerMonth=5 → `DisplayUnit (int?)`
- [ ] Flags `StatisticsOptions{EMA=1}`, `DefaultAlertsOptions{DisableTtl=1, DisableStatusChange=2}`
- [ ] `SensorValueBase` { Path, Comment?, Time(UTC now), Status(Ok) } + typed `Value` per DTO
- [ ] Bar DTOs: Min/Max/Mean/Count/FirstValue?/LastValue/OpenTime/CloseTime (obsolete `Percentiles` never populated but serialized as null)
- [ ] `FileSensorValue`: Value = `List<byte>` → **numeric JSON array, NOT base64**; Name; Extension. No Counter DTO exists (`CounterSensorValue.cs` contains `RateSensorValue`)
- [ ] `EnumOption` { Key:int, Value:string, Description:string, Color:int ARGB }
- [ ] `AddOrUpdateSensorRequest`: full property set incl. EnumOptions, Alerts, TtlAlerts, DefaultAlertsOptions
- [ ] Registration time fields (`TTLs/KeepHistory/SelfDestroy/ConfirmationPeriod`) on the wire as **long ticks**; `TtlAlerts[*].TtlValue` overrides `options.TTLs`; `IsSingletonSensor` OR-ed with `IsComputerSensor`
- [ ] JSON: **PascalCase** property names, **nulls/defaults emitted** (default System.Text.Json — `[DefaultValue]` attrs have no effect), enums as numbers, DateTime ISO-8601 `Z`, TimeSpan .NET "c" format `[-][d.]hh:mm:ss[.fffffff]`, Version `a.b[.c[.d]]`
- [ ] Batch `list` polymorphism: discriminated by the **numeric `Type` property** (server scans for `Type`, switches on `SensorType` int) — no string discriminator
- [ ] **[decide]** History DTOs `HistoryRequest{Path,From,To?,Count?,Options(IncludeTtl=1)}`, `FileHistoryRequest{+FileName,Extension,IsZipArchive}` (collector doesn't query history today)

## 16. Wrapper parity gaps — ALL [decide]

Reference: `src/wrapper/include/` (C++/CLI wrapper as minimal-API oracle)

- [ ] TimeSpan sensor (absent in wrapper)
- [ ] Version sensor (absent)
- [ ] Enum sensor (absent)
- [ ] Service-commands sensor (absent)
- [ ] Lifecycle listeners/events (absent)
- [ ] Fluent builders (absent)
- [ ] History queries (absent)
- [ ] Rate-sensor type asymmetry: wrapper exposes int AND double rate sensors; .NET rate is double-only

## 17. Cross-cutting invariants (gate for every slice)

- [ ] Values before Start / after Stop silently rejected
- [ ] Start/Stop/Dispose idempotent + race-safe; exactly one ToStopped per cycle
- [ ] Path dedup transparent; type conflict throws
- [ ] All validation pre-enqueue
- [ ] Bars never roll without confirmed send; UTC-aligned windows
- [ ] Stale callbacks invalidated by lifecycle epoch
- [ ] FIFO at-least-once; retry-forever + overflow backstop; newest-data-wins (#1088/#1090)
- [ ] Graceful stop flushes accepted work; terminal dispose bounded under broken transport
- [ ] Diagnostics suppressed past drain boundary; overflow exempt
- [ ] Scheduler loop never dies; errors to onError
- [ ] Logger/listener exceptions always swallowed
- [ ] Wire values/names/formats frozen
