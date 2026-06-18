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

Coverage convention: a covered line is `[x]` and carries
`— conformance: <fixture>:<case>[, <fixture>:<case>]` (use `<fixture>:*` when a
whole fixture owns the line, e.g. bar aggregation math). Run
[`scripts/conformance-coverage.ps1`](../../scripts/conformance-coverage.ps1)
for per-section counts and to validate that every annotation still resolves to
a real corpus case (stale annotation → non-zero exit); `-ShowUnticked` prints
the remaining backlog. Current coverage: 49/261 lines are owned by the
in-proc conformance corpus. The rest are either platform-bound (Windows/Unix
default sensors, WMI, HTTP/TLS transport, NLog — covered by per-platform smoke,
not the portable corpus), `[decide]` items, or behaviors only reachable by
language-local unit tests (priority routing, rate elapsed-time math, the
dispose/ToStopped race, #1088/#1090 retry-vs-head).

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

- [x] `DataCollector(CollectorOptions)` ctor + immediate `Validate()` — conformance: lifecycle_int_contract:blank_access_key_is_rejected, lifecycle_int_contract:zero_port_is_rejected
- [ ] Convenience ctor `(productKey, address="localhost", port=44330, clientName=null)`
- [ ] `TestConnection()` → `ConnectionResult { Code, Error, IsOk, Result (= Error empty), static Ok }`, callable in any state
- [ ] `IDataSender` transport seam (`TestConnectionAsync/SendDataAsync/SendPriorityDataAsync/SendCommandAsync/SendFileAsync/Dispose`)
- [x] Option `AccessKey` (required, non-whitespace) — conformance: lifecycle_int_contract:blank_access_key_is_rejected
- [ ] Option `ServerAddress` (default `localhost`, required)
- [x] Option `Port` (default 44330, 1..65535) — conformance: lifecycle_int_contract:zero_port_is_rejected
- [ ] Option `ClientName` (default null)
- [x] Option `ComputerName` / `Module` (path hierarchy) — conformance: instant_int_contract:running_collector_stores_int_payload, instant_int_contract:blank_computer_name_is_omitted_from_payload_path
- [x] Option `MaxQueueSize` (default 20000, > 0, per queue) — conformance: queue_overflow_contract:overflow_evicts_oldest_keeps_fifo_suffix, queue_overflow_contract:overflow_exact_capacity_no_eviction
- [ ] Option `MaxValuesInPackage` (default 1000, > 0)
- [x] Option `PackageCollectPeriod` (default 15 s, > 0) — conformance: flush_contract:stop_flushes_all_pending_before_returning, file_contract:file_dispatches_promptly_despite_collect_period
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
- [x] `Start()` / `Start(customStartingTask)` — idempotent; custom task between processor start and sensor init — conformance: lifecycle_int_contract:start_twice_is_noop
- [ ] Start failure → rollback to Stopped (queues via `StartRollback` mode)
- [x] `Stop()` / `Stop(customStoppingTask)` — idempotent; awaits dynamic sensor-start tasks; custom-task failure logged, stop proceeds — conformance: lifecycle_int_contract:stop_twice_is_noop, lifecycle_int_contract:stop_before_start_is_noop
- [ ] `Dispose()` — idempotent, terminal, never throws; from any state
- [ ] Dispose-vs-Stop race: joins in-flight stop, exactly one ToStopped, terminal mode wins on queues
- [ ] Stop/Dispose racing Start: waits `_currentStartInitTask`, not the pre-init custom task
- [x] Restart support: Start→Stop→Start re-inits and restarts all registered sensors — conformance: lifecycle_int_contract:restart_after_stop_sends_next_values, registration_contract:restart_reregisters_sensor
- [ ] Events `ToStarting/ToRunning/ToStopping/ToStopped` (fired under lock, per-handler exception isolation)
- [ ] `ILifecycleListener` (`OnStarting/OnRunning/OnStopping/OnStopped`) + `AddLifecycleListener(...)`; no replay of current state
- [ ] Disposal order: DataProcessor → DataSender → CollectorScheduler → global exception handler unhook
- [ ] Status vs CanAcceptData asymmetry after Dispose (flush window until CompleteStop)
- [ ] **[decide]** Obsolete: sync `Initialize()` overloads, `InitializeSystemMonitoring`/`InitializeProcessMonitoring`/`InitializeOsMonitoring`/`MonitorServiceAlive`/`InitializeWindowsUpdateMonitoring`, `ValuesQueueOverflow` event — recommend NOT porting
- [ ] **[decide]** Obsolete (wire/options compat shims): `SensorOptions.DefaultChats` + `DefaultChatsMode`, `BaseRequest.Key` (key-in-body), `BarSensorValueBase.Percentiles`, `AddOrUpdateSensorRequest.TTL`/`TtlAlert` set-only shims, `AlertDestinationMode.DefaultChats`

## 3. Sensor registration

Details: [`overview.md`](../../aicontext/features/collector/overview.md) §Sensor registration

- [x] Path validation: every `Create*` throws `ArgumentException` for null/whitespace/slash-only paths — conformance: value_int_contract:slash_only_path_is_rejected
- [x] Identity = full normalized path; duplicate Create (same type + IsLastValue) returns existing instance, new one disposed — conformance: instant_int_contract:duplicate_sensor_path_is_idempotent, cardinality_int_contract:duplicate_sensor_handles_share_path_under_load
- [x] Same path + different type/IsLastValue → `InvalidOperationException` — conformance: last_value_contract:instant_then_last_same_path_is_rejected, stress_mixed_contract:mixed_duplicate_type_registration_stress_rejects_conflicts
- [ ] `MaxSensors` cap enforced atomically; offender removed and disposed
- [x] Stopped phase → sensor queued for next Start — conformance: lifecycle_int_contract:register_before_start_many_sends_after_start, registration_contract:plain_int_sensor_registers_on_start
- [x] Starting/Running → immediate async `InitAndStart`, tracked, awaited by Stop — conformance: lifecycle_int_contract:register_during_running_sends_immediately, registration_contract:sensor_created_while_running_registers
- [ ] Stopping/Disposed → rejected non-throwing (logged, disposed, returned inert)
- [x] `InitAsync` of every sensor sends `AddOrUpdateSensorRequest` before first value — conformance: registration_contract:plain_int_sensor_registers_on_start, registration_contract:each_sensor_registers_once

## 4. Sensor creation API

Details: [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [x] `Create{Bool,Int,Double,String,Version,Time}Sensor(path, description)` + `(path, InstantSensorOptions)` — native instant create for every type incl. TimeSpan(7)/Version(8); conformance: instant_mixed_contract, timespan_version_contract:timespan_instant_value / version_full_value (options overload exercised via create_int_sensor_with_alerts)
- [x] `CreateEnumSensor(path, description | EnumSensorOptions)` — conformance: enum_contract:enum_zero_payload, registration_contract:enum_sensor_registers_enum_options
- [ ] `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)` + generic `CreateLastValueSensor<T>(path, options, defaultValue)`
- [ ] `CreateRateSensor(path, RateSensorOptions)` + `CreateM1RateSensor` + `CreateM5RateSensor`
- [x] `CreateIntBarSensor(path, barPeriod=300000, postPeriod=15000, descr)` + options overload — conformance: bar_int_contract:int_bar_basic_aggregation_flushes_on_stop, bar_rollover_contract:bar_rolls_on_add_after_close_strict
- [ ] DataCollector-only TimeSpan overloads `Create{Int,Double}BarSensor(path, TimeSpan barPeriod, TimeSpan postPeriod[, precision], descr)`
- [ ] `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor` presets
- [ ] `CreateDoubleBarSensor(..., precision=2, ...)` + options overload + same five presets
- [x] `CreateFileSensor(path, fileName, extension="txt", descr)` / `(path, FileSensorOptions)` — conformance: file_contract:file_add_value_utf8_roundtrip
- [ ] Collector-level `SendFileAsync(sensorPath, filePath, status, comment)`
- [ ] `CreateNoParamsFuncSensor<T>(path, descr, Func<T>, interval ms|TimeSpan)` + `Create{1Min,5Min}NoParamsFuncSensor` + `CreateFunctionSensor<T>(path, func, options)`
- [ ] `CreateParamsFuncSensor<T,U>(path, descr, Func<List<U>,T>, interval)` + `Create{1Min,5Min}ParamsFuncSensor` + `CreateValuesFunctionSensor<T,U>`
- [x] `CreateServiceCommandsSensor()` — native `hsm_collector_create_service_commands_sensor`; conformance: service_commands_contract
- [ ] Interface `IInstantValueSensor<T>`: `AddValue(v)` / `(v, comment)` / `(v, status, comment)`
- [x] Interface `ILastValueSensor<T>` (latest value, sends once on stop, default if none) — conformance: last_value_contract:last_int_flushes_latest_on_stop, last_value_contract:last_int_default_flushes_on_stop
- [ ] Interface `IBarSensor<T>`: `AddValue`, `AddValues(IEnumerable)`, `AddPartial(min,max,mean,first,last,count)`
- [ ] Interface `IFileSensor`: + `Task<bool> SendFile(filePath, status, comment)`
- [ ] Interface `IMonitoringRateSensor` (pure alias of `IInstantValueSensor<double>`; defining file is misleadingly named `IMonitoringCounterSensor.cs`)
- [x] Interface `IServiceCommandsSensor`: `SendCustomCommand(cmd, initiator)`, `SendUpdate(initiator[, new[, old]])`, `SendRestart/SendStart/SendStop(initiator)` — native `hsm_service_commands_send_*`; conformance: service_commands_contract:service_commands_values (fixed command strings + `Initiator:` comment)
- [ ] Interface `IBaseFuncSensor` (in `Obsolete` folder but NOT `[Obsolete]` — current return type): `GetInterval()`, `RestartTimer(TimeSpan)`, `GetFunc()`; params variant `AddValue(U)`
- [x] Last-value null-default throws: `CreateLastValueStringSensor(path)` / `CreateLastValueVersionSensor(path)` with implicit null default → `ArgumentException` at creation — conformance: last_value_contract:last_string_null_default_is_rejected
- [ ] Fluent builders (per-type setters): `InstantSensor<T>` `.Description/.Ttl/.KeepHistory/.Priority/.Configure`; `BarSensor<T>` `.BarPeriod/.PostPeriod/.TickPeriod/.Precision/.Description/.Configure`; `RateSensor` `.PostPeriod/.Description/.Configure`; `.Build()` throws `NotSupportedException` for unsupported `T`
- [ ] Properties `Status`, `ComputerName`, `Module`, `Windows`, `Unix`
- [ ] Property `DefaultSensors` (`IEnumerable<ISensor>`; public `ISensor`: `SensorPath/InitAsync/StartAsync/StopAsync/Dispose`; `SensorBase` adds `SendValue(SensorValueBase)` + `ExceptionThrowing` event)
- [ ] `IWindowsCollection`/`IUnixCollection` are `IDisposable`
- [ ] `AddNLog(LoggerOptions { ConfigPath, WriteDebug })` + embedded config fallback
- [ ] `AddCustomLogger(ICollectorLogger { Debug, Info, Error(string), Error(Exception) })`

## 5. Sensor mechanics

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md)

- [x] Validation: null rejected (strings allowed); double/float NaN/±Infinity rejected — conformance: instant_mixed_contract:string_null_value_is_rejected, instant_mixed_contract:double_nan_is_rejected
- [x] Validation: status must be defined `SensorStatus` member — conformance: value_int_contract:int_invalid_status_is_rejected, instant_mixed_contract:enum_invalid_status_is_rejected
- [x] Validation: comment trimmed to 1024 chars — conformance: instant_int_contract:long_comment_is_trimmed, last_value_contract:last_string_comment_is_trimmed_on_flush
- [x] Validation: rejected values logged (Debug), never enqueued — conformance: instant_mixed_contract:double_nan_is_rejected, last_value_contract:last_int_invalid_status_preserves_previous
- [ ] Instant flow: validate → enqueue immediately; `IsPrioritySensor` routes to priority queue
- [x] Last-value flow: store latest, single enqueue on StopAsync, `IsLastValue=true` identity — conformance: last_value_contract:last_int_flushes_latest_on_stop, last_value_contract:instant_then_last_same_path_is_rejected
- [x] Bar: min/max/mean(sum/count)/count/first/last under lock — conformance: bar_int_contract:*
- [x] Bar `AddPartial` merge + consistency check (int strict; double tolerance `max(1e-12, |max-min|*1e-9)`) — conformance: bar_partial_contract:*
- [x] Bar `Complete()` rounding (double: `Round(v, Precision, AwayFromZero)` on all stats; int: mean only) — conformance: bar_double_contract:double_bar_precision_rounding, bar_int_contract:int_bar_mean_rounds_half_to_even_up
- [ ] Bar roll only after confirmed send (`if (TrySendValue()) BuildNewBar()`) — no-roll-without-send invariant
- [x] Bar UTC-epoch alignment: `OpenTime = floor(now/period)*period`, `CloseTime = OpenTime + BarPeriod` — conformance: bar_int_contract:int_bar_basic_aggregation_flushes_on_stop, bar_rollover_contract:bar_rollover_no_value_lost_invariants
- [ ] Bar periods: `BarPeriod` 5 min / `BarTickPeriod` 5 s / `PostDataPeriod` 15 s / `Precision` 2 (0..15), all validated
- [x] Monitoring base: periodic send loop via `ScheduledTaskHandle`, virtuals `GetValue/GetStatus/GetComment/GetDefaultValue` — conformance: function_contract:function_posts_constant_periodically, rate_contract:rate_posts_zero_when_idle
- [ ] Monitoring base: `_sendValueInProgress` reentrancy guard
- [ ] Monitoring base: lifecycle epoch — capture, revalidate before send, drop stale (init/restart/stop bump)
- [ ] Monitoring base: `GetValue` exception → value with `status=Error, comment=ex.Message` + deduped log
- [ ] Monitoring base: `RestartTimerAsync(newPeriod)` = bounded stop → epoch bump → reschedule
- [ ] Rate: lock-free CAS accumulation; `GetValue` = `Interlocked.Exchange(sum,0) / period.TotalSeconds`
- [x] Rate: sticky status/comment from last AddValue; default PostDataPeriod 1 min — conformance: rate_contract:rate_status_and_comment_are_sticky
- [ ] File: async read (81920 buffer, `FileShare.ReadWrite`); `MaxFileSizeBytes` (10 MB default) + `int.MaxValue` caps
- [x] File: name/extension from path else options defaults; `AddValue(string)` = UTF-8 bytes — conformance: file_contract:file_add_value_utf8_roundtrip
- [ ] File: `SendFile` false on invalid status / `CanAcceptData==false` / missing / oversize
- [x] Function no-params: invoke func each period — conformance: function_contract:function_posts_initial_value_immediately, function_contract:function_posts_constant_periodically
- [x] Function params: `ConcurrentQueue` cache, FIFO eviction at `MaxCacheSize` (10000), snapshot under lock, func outside lock — conformance: function_contract:values_function_cache_evicts_oldest, function_contract:values_function_cache_is_sliding_window
- [ ] All sensors: `HandleException` → `AddException` (dedup) + `ExceptionThrowing` event; never crash host

## 6. Options / prototypes / paths

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md) §Options & path model

- [x] `SensorOptions` common: `Description`, `SensorUnit`, `TTLs`, `KeepHistory`, `SelfDestroy`, `EnableForGrafana`, `IsSingletonSensor`, `AggregateData`, `Statistics(EMA)`, `IsComputerSensor`, `SensorLocation(Module|Product)`, `TtlAlerts` — native `hsm_collector_create_sensor_with_options` + `hsm_sensor_options_t`; conformance: options_surface_contract:full_options_register_in_payload + paired golden `native_wire_registration_full_options_*`. (`DefaultAlertsOptions/IsForceUpdate/IsPrioritySensor` remain wire-default 0/false — diag/QoS, #1099.)
- [x] Singular conveniences `SensorOptions.TTL` (→ `TTLs`, ttl_ms) and `TtlAlert` (→ `TtlAlerts`, the alert builder)
- [x] `DisplayUnit` per options type → wire `DisplayUnit (int?)` — `hsm_sensor_options_t.display_unit`; pinned by `native_wire_registration_full_options_*` (the `RateDisplayUnit` enum values arrive with rate options, #1100)
- [ ] `InstantSensorOptions(+Alerts)`; `MonitoringInstantSensorOptions(+PostDataPeriod 15 s)`
- [ ] `BarSensorOptions(+BarPeriod/BarTickPeriod/Precision/BarAlerts)`
- [ ] `RateSensorOptions(PostDataPeriod 1 min, Unit=ValueInSecond)`
- [ ] `FunctionSensorOptions` / `ValuesFunctionSensorOptions(+MaxCacheSize)` — both default `PostDataPeriod` 1 min (ms-param factory overloads default 15 s instead)
- [ ] `FileSensorOptions(+DefaultFileName/Extension/MaxFileSizeBytes)`
- [ ] `EnumSensorOptions(+EnumOptions, AggregateData=true, GenerateEnumOptionsDecription())`
- [ ] `DiskSensorOptions { TargetPath default C:\, CalibrationRequests default 6, PostDataPeriod 5 min }`; `DiskBarSensorOptions { TargetPath }` — pure config for the #1099 disk sensors; ships with them
- [~] `VersionSensorOptions { Version, StartTime }`; `ServiceSensorOptions { ServiceName, IsHostService → .module placement, SensorPath }` — the `IsHostService` → `.module/...` placement is ported and pinned by service_commands_contract (`.module/Service commands`); the option structs themselves carry the #1099 default product-version / service-status sensors
- [ ] `NetworkSensorOptions`; `WindowsInfoSensorOptions { PostDataPeriod default 12 h }`; `CollectorMonitoringInfoOptions` — config for the #1099 default sensors; ships with them
- [x] `CalculateSystemPath`: computer → `ComputerName/Path`; module → `ComputerName/Module/Path`; product → `Path` — conformance: instant_int_contract:running_collector_stores_int_payload, instant_int_contract:blank_computer_name_is_omitted_from_payload_path
- [x] `BuildPath`: join `/`, drop null/empty/whitespace, split interior `/`, collapse `//` — conformance: instant_int_contract:path_duplicate_separators_are_normalized, value_int_contract:path_leading_trailing_slashes_are_normalized
- [x] `RevealDefaultPath` = `{.computer|.module}/Category/SensorName` — native `RevealDefaultPath`; exercised by service_commands_contract (`.module/Service commands`)
- [x] Prototype merge: custom non-null wins for most properties; `Path/Type/IsComputerSensor/ComputerName/Module` pinned from prototype — native `MergeRegistrationOptions`; unit: native_prototype_merge_pins_identity_overrides_metadata

## 7. Alert DSL

Details: [`alerts/feature.md`](../../aicontext/features/collector/alerts/feature.md)

- [x] Instant conditions: `IfValue/IfComment/IfStatus/IfLenght (actual exported name — misspelled; chaining is AndLength)/IfFileSize/IfReceivedNewValue/IfEmaValue` — native ports them as the explicit `(property, operation, target)` C ABI `hsm_alert_add_condition` (the `If*` sugar that picks those values is C#-only); registration payload pinned: alert_registration_contract:instant_alert_registers_in_payload
- [x] Bar conditions: `IfMin/IfMax/IfMean/IfCount/IfFirstValue/IfLastValue/IfBarComment/IfBarStatus/IfReceivedNewBarValue` + EMA variants — same explicit-condition ABI (`alert_new bar`); properties frozen in `hsm_alert_property_t`
- [x] TTL entry point `IfInactivityPeriodIs(TimeSpan? = null)` → SpecialAlertCondition (TtlValue feeds wire TTLs) — native `HSM_ALERT_KIND_TTL` + `hsm_alert_set_inactivity_period`; conformance: alert_registration_contract:instant_alert_registers_in_payload ("TTLTicks":[600000000])
- [x] `.And*` chaining (And/Or combination) — `hsm_alert_combination_t`; conformance: alert_registration_contract:multi_condition_alert_combines_or
- [x] Actions: `ThenSendNotification(template, AlertDestinationMode = FromParent)` / `ThenSetIcon(string | AlertIcon)` / `ThenSetSensorError` — `hsm_alert_set_notification/set_icon/set_icon_raw/set_sensor_error`
- [x] `ThenSendScheduledNotification(template, time, AlertRepeatMode, instantSend, AlertDestinationMode = FromParent)` — `hsm_alert_set_scheduled_notification` (ISO-8601-Z time); byte-pinned by WireFormatGoldenLockTests capture
- [x] `AlertIcon { Ok=0 Warning=1 Error=2 Pause=3 ArrowUp=10 ArrowDown=11 Clock=100 Hourglass=101 }` → UTF-8 emoji string on the wire (`IconExtensions.ToUtf8`) — native `AlertIconUtf8`; Warning→⚠ pinned: alert_registration_contract:instant_alert_registers_in_payload + NativeWireRegistrationWithAlertsMatchesNetByteLayout
- [x] `AndConfirmationPeriod(TimeSpan)` — `hsm_alert_set_confirmation_period` (ticks); conformance: alert_registration_contract:multi_condition_alert_combines_or
- [x] `.Build()` / `.BuildAndDisable()` → Instant/Bar/Special templates — `hsm_alert_set_disabled`; the built `AlertData` attaches via `hsm_sensor_attach_alert`
- [x] TTL alerts via `TtlAlerts`; `Alerts`/`TtlAlerts`/`TTLs` coupling matches `ApiConverters`. `DefaultAlertsOptions` flags (DisableTtl=1, DisableStatusChange=2) [decide] deferred to default sensors (#1099)

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

- [ ] Four queues over unbounded Channel: Data (periodic batch), Priority (reactive batch), File (single-item), Command (reactive batch) — native (#1097): one worker queue + reactive file kick; the QoS split is C#-internal, not observable in value delivery (see data-pipeline/feature.md Native port)
- [x] Data queue: `PackageCollectPeriod` wait; keep draining while full batch remains — native (#1097): `DispatchQueuedLocked` drains while non-empty, pops up to `MaxValuesInPackage`
- [x] `QueueItem.BuildDate` ordering token at enqueue — native (#1097): realized as the deterministic dispatch-epoch token; wall-clock `DataPackage` time-in-queue stats deferred to #1099 (diagnostic sensors)
- [x] Bar `Count <= 0` filtered at package build — conformance: bar_int_contract:int_bar_empty_bar_sends_no_payload, bar_double_contract:double_bar_empty_bar_sends_no_payload
- [x] Collector state machine (Stopped/Starting/Running/Stopping/Disposed) — native (#1095/#1097): one collector-level lifecycle; the per-queue state machine is C#-internal to the four processors
- [x] Public-write gate closes at Stop commit — native (#1097): `CanAcceptDataLocked` drops producer values once not Running; the retry path runs only on the worker thread (no public bypass needed)
- [x] Enqueue rejection is silent to producers — native (#1097): enqueue is void + silent drop, matching "producers must not branch on rejection kind"; the `EnqueueResult` status enum is C#-internal test observability
- [x] Overflow: FIFO head drop while `count > MaxQueueSize`; counts → QueueOverflowSensor (self-loop guard) — conformance: queue_overflow_contract:overflow_evicts_oldest_keeps_fifo_suffix, queue_overflow_contract:overflow_massive_burst_keeps_last_capacity
- [x] Retry: failed send re-enqueues package, rethrows, retries next cycle; NO retry cap; deduped error logs — conformance: sender_retry_contract:send_failure_retries_until_success_in_order, sender_retry_contract:send_failure_multi_package_no_loss_no_duplicates
- [x] #1088: retry at full queue dropped (never evicts a queued value) — native (#1097): `ReEnqueueLocked` capacity drop — native unit: native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values
- [x] #1090 below-capacity head-drop REMOVED from both collectors (#1097) — monitoring-history contract drops only on overflow, so a failed retry is kept below capacity (at-least-once). C# `_buildDateMirror`/`IsOlderThanQueueHead` deleted; native never carried it — native unit: native_retry_below_capacity_is_always_redelivered, C# unit: ReEnqueue_below_capacity_keeps_retry_older_than_queue_head.
- [x] Retry filters bypassed once writes closed — native (#1097): the stop drain drops on failure, so no retry path runs during shutdown — the filters never apply post-stop (equivalent)
- [x] Cancellation on OCE / stop — native (#1097): bounded stop drain flushes accepted work and drops the remainder on a dead transport; the per-mode preserve-canceled distinction is C#-internal (data-loss-at-stop is the accepted native contract)
- [x] `ShutdownMode.GracefulStop`: flush, preserve-canceled, wait `RequestTimeout` — conformance: flush_contract:stop_flushes_all_pending_before_returning, flush_contract:stop_flushes_multiple_packages_in_order
- [ ] `ShutdownMode.TerminalDispose`: flush, drop-canceled, wait `min(RequestTimeout, 1 s)` — native (#1097): Dispose reuses the bounded stop drain; the finer timeout matrix is C#-internal, not observable
- [ ] `ShutdownMode.StartRollback`: clear immediately, no flush — native (#1097): a failed Start leaves nothing queued (data gate closed); the explicit rollback mode is C#-internal
- [x] Drain order: stop scheduler → flush last-values/bars → stop dispatcher → bounded drain — native (#1097): single-queue FIFO drain; the Priority→Data→[suppress]→File→Command ordering is a per-queue C# concern
- [x] Flush timeout clamped [1 s, 5 s] — conformance: flush_contract:stop_with_hanging_sender_is_bounded_and_drops_pending, flush_contract:stop_with_hanging_sender_drops_pending_bar
- [ ] Diagnostics suppression after data-drain boundary (#1075); overflow exempt; reset on Start — native: deferred to #1099 (needs the diagnostic sensors that would receive the telemetry)
- [x] Failure-log honesty — native (#1097): the bounded stop logs "Collector stop dropped N pending value(s)"; the C# flush-context "queued for clear" vs "preserved" wording is internal to the four-queue flush

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
- [x] `SensorStatus`: OffTime=0 Ok=1 Warning=2 Error=3 — conformance: value_int_contract:status_off_time_numeric_value, value_int_contract:status_error_numeric_value
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
- [x] `EnumOption` { Key:int, Value:string, Description:string, Color:int ARGB } — conformance: registration_contract:enum_sensor_registers_enum_options
- [ ] `AddOrUpdateSensorRequest`: full property set incl. EnumOptions, Alerts, TtlAlerts, DefaultAlertsOptions
- [ ] Registration time fields (`TTLs/KeepHistory/SelfDestroy/ConfirmationPeriod`) on the wire as **long ticks**; `TtlAlerts[*].TtlValue` overrides `options.TTLs`; `IsSingletonSensor` OR-ed with `IsComputerSensor`
- [ ] JSON: **PascalCase** property names, **nulls/defaults emitted** (default System.Text.Json — `[DefaultValue]` attrs have no effect), enums as numbers, DateTime ISO-8601 `Z`, TimeSpan .NET "c" format `[-][d.]hh:mm:ss[.fffffff]`, Version `a.b[.c[.d]]`
- [x] Batch `list` polymorphism: discriminated by the **numeric `Type` property** (server scans for `Type`, switches on `SensorType` int) — no string discriminator — conformance: instant_int_contract:running_collector_stores_int_payload, enum_contract:enum_zero_payload
- [ ] **[decide]** History DTOs `HistoryRequest{Path,From,To?,Count?,Options(IncludeTtl=1)}`, `FileHistoryRequest{+FileName,Extension,IsZipArchive}` (collector doesn't query history today)

## 16. Wrapper parity gaps — ALL [decide]

Reference: `src/wrapper/include/` (C++/CLI wrapper as minimal-API oracle)

- [x] TimeSpan sensor — native `hsm_collector_create_timespan_sensor` + `hsm_sensor_add_timespan` (#1098)
- [x] Version sensor — native `hsm_collector_create_version_sensor` + `hsm_sensor_add_version` (#1098)
- [ ] Enum sensor (absent)
- [ ] Service-commands sensor (absent)
- [ ] Lifecycle listeners/events (absent)
- [ ] Fluent builders (absent)
- [ ] History queries (absent)
- [ ] Rate-sensor type asymmetry: wrapper exposes int AND double rate sensors; .NET rate is double-only

## 17. Cross-cutting invariants (gate for every slice)

- [x] Values before Start / after Stop silently rejected — conformance: instant_int_contract:before_start_drops_value, stress_mixed_contract:mixed_instant_stress_drops_values_after_stop
- [ ] Start/Stop/Dispose idempotent + race-safe; exactly one ToStopped per cycle
- [x] Path dedup transparent; type conflict throws — conformance: instant_int_contract:duplicate_sensor_path_is_idempotent, last_value_contract:instant_then_last_same_path_is_rejected
- [x] All validation pre-enqueue — conformance: instant_mixed_contract:double_nan_is_rejected, value_int_contract:int_invalid_status_is_rejected
- [ ] Bars never roll without confirmed send; UTC-aligned windows
- [ ] Stale callbacks invalidated by lifecycle epoch
- [x] FIFO at-least-once; retry-forever + overflow backstop — native (#1097): retry kept below capacity, dropped only when the buffer is full — native unit: native_retry_below_capacity_is_always_redelivered, native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values (native intentionally does NOT port the C# #1090 below-capacity drop)
- [x] Graceful stop flushes accepted work; terminal dispose bounded under broken transport — conformance: flush_contract:stop_flushes_all_pending_before_returning, flush_contract:stop_with_hanging_sender_is_bounded_and_drops_pending
- [ ] Diagnostics suppressed past drain boundary; overflow exempt
- [ ] Scheduler loop never dies; errors to onError
- [ ] Logger/listener exceptions always swallowed
- [x] Wire values/names/formats frozen — conformance: number_format_contract:*, instant_mixed_contract:string_json_special_characters_are_escaped
