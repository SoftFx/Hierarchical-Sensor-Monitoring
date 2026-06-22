# C++ Collector Port — Functional Checklist (Spike Step 1)

![checklist resolved](https://img.shields.io/badge/checklist%20resolved-261%2F261-brightgreen)

> Owner: collector/integrations | Created: 2026-06-10 | Status: **FROZEN INDEX — source of truth = the conformance corpus** (epic #1093 retired this checklist in #1101)
> Companion to [`cpp-collector-port-spike.md`](cpp-collector-port-spike.md).

**Historical index — the conformance corpus is now canonical.** This file was
the spike's living "what must the port cover" list. Epic #1093 has since moved
the source of truth into the shared conformance corpus
([`tests/conformance/collector/*.hsmtest`](../../tests/conformance/collector))
run against BOTH collectors. Every line below now carries a permanent
disposition and the file is **frozen** as a cross-reference: it is no longer
edited box-by-box as the port evolves — new collector behavior is governed by
AGENTS rule #9 (a scenario + both drivers in the same PR), not by ticking a box
here. Behavior details live in the maintained
[`aicontext/`](../../aicontext/README.md) feature docs (linked per section).
Adding a third-language driver requires no new test content — only a driver and
a CI lane.

**Coverage / disposition gate:** every one of the 261 lines is resolved.
[`scripts/conformance-coverage.ps1 -Strict`](../../scripts/conformance-coverage.ps1)
is the CI gate — it fails if any line lacks a disposition and validates that
every `— conformance:` reference still resolves to a real corpus case (stale
annotation → non-zero exit). `-ShowUnticked` prints the lines not owned by the
in-proc corpus (i.e. resolved by platform/unit/decide instead).

Every line carries exactly one disposition:
- **`[x] — conformance: <fixture>:<case>[, …]`** — owned by the portable corpus
  (use `<fixture>:*` when a whole fixture owns the line, e.g. bar aggregation
  math).
- **`[ ] — platform: <win|unix|http|tls>; …`** — platform-bound (Windows/Unix
  default-sensor live PDH/WMI/registry/EventLog/procfs reads, HTTP/TLS
  transport); the registration payload is corpus-pinned, the live read is
  per-platform smoke / a #1099 live-value follow-up.
- **`[ ] — unit: <test>`** — reachable only by a language-local unit test
  (native `native_*` or a C# `*Tests`): scheduler/clock seam, wire byte-golden
  locks, logger/dedup, lifecycle-listener isolation, option-field validation.
- **`[ ] — [decide]: <rationale>`** — explicitly out of port scope: obsolete
  managed-only API / compat shims, C#-internal QoS or observability details not
  visible on the wire (a native equivalent is documented inline), or dropped
  features (time-in-GC).

Parity bug rule (epic #1093, "Parity bug policy"): a bug found in EITHER
collector (.NET or native C++) is triaged against the other before closing —
reproducing conformance scenario first, run on both, fix every implementation
it is red on; one issue (`bug` + `cpp-port`) records the per-implementation
verdict.

Legend: **[wire]** = byte-for-byte compatibility. **[decide]** = explicit port
decision (now resolved inline, per line).

---

## 1. Construction & configuration

Details: [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [x] `DataCollector(CollectorOptions)` ctor + immediate `Validate()` — conformance: lifecycle_int_contract:blank_access_key_is_rejected, lifecycle_int_contract:zero_port_is_rejected
- [ ] Convenience ctor `(productKey, address="localhost", port=44330, clientName=null)` — [decide]: C#-only convenience ctor; native takes the full `hsm_collector_options_t` (every field explicit), so the defaulting overload is not ported
- [ ] `TestConnection()` → `ConnectionResult { Code, Error, IsOk, Result (= Error empty), static Ok }`, callable in any state — unit: native_test_connection_reports_reachable
- [ ] `IDataSender` transport seam (`TestConnectionAsync/SendDataAsync/SendPriorityDataAsync/SendCommandAsync/SendFileAsync/Dispose`) — [decide]: C#-internal DI seam, not wire-observable; native sender is internal and the HTTP transport arrives with #1096
- [x] Option `AccessKey` (required, non-whitespace) — conformance: lifecycle_int_contract:blank_access_key_is_rejected
- [ ] Option `ServerAddress` (default `localhost`, required) — unit: native_create_rejects_null_server_address, native_create_rejects_blank_server_address
- [x] Option `Port` (default 44330, 1..65535) — conformance: lifecycle_int_contract:zero_port_is_rejected
- [ ] Option `ClientName` (default null) — [decide]: optional passthrough option (`hsm_collector_options_t.client_name`); no distinct validation/behavior to pin
- [x] Option `ComputerName` / `Module` (path hierarchy) — conformance: instant_int_contract:running_collector_stores_int_payload, instant_int_contract:blank_computer_name_is_omitted_from_payload_path
- [x] Option `MaxQueueSize` (default 20000, > 0, per queue) — conformance: queue_overflow_contract:overflow_evicts_oldest_keeps_fifo_suffix, queue_overflow_contract:overflow_exact_capacity_no_eviction
- [ ] Option `MaxValuesInPackage` (default 1000, > 0) — [decide]: batch-size tuning option (`hsm_collector_options_t.max_values_in_package`, validated > 0); not a distinct wire-observable config behavior
- [x] Option `PackageCollectPeriod` (default 15 s, > 0) — conformance: flush_contract:stop_flushes_all_pending_before_returning, file_contract:file_dispatches_promptly_despite_collect_period
- [ ] Option `RequestTimeout` (default 30 s, > 0) — [decide]: shutdown/timeout tuning option (`hsm_collector_options_t.request_timeout_ms`, validated > 0); not wire-observable
- [ ] Option `DataSender` (default `HsmHttpsClient`) — [decide]: C#-only DI override of the transport seam; native sender selection is internal (HTTP lands with #1096)
- [ ] Option `AllowUntrustedServerCertificate` (default false) — platform: tls; smoke: `hsm_collector_options_t.allow_untrusted_server_certificate` consumed by the libcurl transport (#1096), exercised by the HTTP/TLS smoke lane
- [ ] Option `AllowPlaintextTransport` (default false) — platform: http; smoke: `hsm_collector_options_t.allow_plaintext_transport` consumed by the libcurl transport (#1096), exercised by the HTTP smoke lane
- [ ] Option `ExceptionDeduplicatorWindow` (default 1 h, >= 0, zero = immediate log) — [decide]: error-dedup tuning (`hsm_collector_options_t.exception_deduplicator_window_ms`); dedup behavior itself lives in §14
- [ ] Option `MaxDeduplicatedMessages` (default 1000, > 0) — [decide]: error-dedup capacity tuning (`hsm_collector_options_t.max_deduplicated_messages`); dedup behavior lives in §14
- [ ] Option `MaxSensors` (default 100000, > 0) — unit: native_create_rejects_negative_option_fields

## 2. Lifecycle

Details: [`overview.md`](../../aicontext/features/collector/overview.md), [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [ ] `CollectorStatus`: Starting → Running → Stopping → Stopped → Disposed (terminal) — unit: native_status_tracks_lifecycle
- [ ] Gate `CanAcceptData` (Starting/Running/Stopping) — [decide]: C#-internal lifecycle detail, not wire-observable; native lifecycle is one collector-level state machine
- [ ] Gate `CanRegisterSensors` (Stopped/Starting/Running) — [decide]: C#-internal lifecycle detail, not wire-observable; native lifecycle is one collector-level state machine
- [ ] Gate `CanStartNewSensors` (Starting/Running) — [decide]: C#-internal lifecycle detail, not wire-observable; native lifecycle is one collector-level state machine
- [ ] Public `IsAcceptingRegistrations` / `ICollectorRegistrationState` — [decide]: C#-internal registration-gate surface, not wire-observable; native exposes only `hsm_collector_status`
- [x] `Start()` / `Start(customStartingTask)` — idempotent; custom task between processor start and sensor init — conformance: lifecycle_int_contract:start_twice_is_noop
- [ ] Start failure → rollback to Stopped (queues via `StartRollback` mode) — [decide]: C#-internal start-rollback path, not wire-observable; native leaves nothing queued on a failed Start (see §11 StartRollback)
- [x] `Stop()` / `Stop(customStoppingTask)` — idempotent; awaits dynamic sensor-start tasks; custom-task failure logged, stop proceeds — conformance: lifecycle_int_contract:stop_twice_is_noop, lifecycle_int_contract:stop_before_start_is_noop
- [ ] `Dispose()` — idempotent, terminal, never throws; from any state — unit: native_dispose_is_terminal_and_idempotent
- [ ] Dispose-vs-Stop race: joins in-flight stop, exactly one ToStopped, terminal mode wins on queues — unit: native_dispose_from_running_stops
- [ ] Stop/Dispose racing Start: waits `_currentStartInitTask`, not the pre-init custom task — [decide]: C#-internal start/stop task plumbing, not wire-observable; native serializes lifecycle on one state machine
- [x] Restart support: Start→Stop→Start re-inits and restarts all registered sensors — conformance: lifecycle_int_contract:restart_after_stop_sends_next_values, registration_contract:restart_reregisters_sensor
- [ ] Events `ToStarting/ToRunning/ToStopping/ToStopped` (fired under lock, per-handler exception isolation) — unit: native_lifecycle_listener_receives_transitions, native_lifecycle_listener_exception_is_isolated
- [ ] `ILifecycleListener` (`OnStarting/OnRunning/OnStopping/OnStopped`) + `AddLifecycleListener(...)`; no replay of current state — unit: native_lifecycle_listener_receives_transitions, native_lifecycle_listener_can_register_another_listener
- [ ] Disposal order: DataProcessor → DataSender → CollectorScheduler → global exception handler unhook — [decide]: C#-internal component teardown order, not wire-observable; native disposes its internal components inside `hsm_collector_dispose`
- [ ] Status vs CanAcceptData asymmetry after Dispose (flush window until CompleteStop) — [decide]: C#-internal lifecycle detail, not wire-observable; native lifecycle is one collector-level state machine
- [ ] **[decide]** Obsolete: sync `Initialize()` overloads, `InitializeSystemMonitoring`/`InitializeProcessMonitoring`/`InitializeOsMonitoring`/`MonitorServiceAlive`/`InitializeWindowsUpdateMonitoring`, `ValuesQueueOverflow` event — recommend NOT porting — [decide]: not ported (obsolete managed-only API/compat shim)
- [ ] **[decide]** Obsolete (wire/options compat shims): `SensorOptions.DefaultChats` + `DefaultChatsMode`, `BaseRequest.Key` (key-in-body), `BarSensorValueBase.Percentiles`, `AddOrUpdateSensorRequest.TTL`/`TtlAlert` set-only shims, `AlertDestinationMode.DefaultChats` — [decide]: not ported (obsolete managed-only API/compat shim)

## 3. Sensor registration

Details: [`overview.md`](../../aicontext/features/collector/overview.md) §Sensor registration

- [x] Path validation: every `Create*` throws `ArgumentException` for null/whitespace/slash-only paths — conformance: value_int_contract:slash_only_path_is_rejected
- [x] Identity = full normalized path; duplicate Create (same type + IsLastValue) returns existing instance, new one disposed — conformance: instant_int_contract:duplicate_sensor_path_is_idempotent, cardinality_int_contract:duplicate_sensor_handles_share_path_under_load
- [x] Same path + different type/IsLastValue → `InvalidOperationException` — conformance: last_value_contract:instant_then_last_same_path_is_rejected, stress_mixed_contract:mixed_duplicate_type_registration_stress_rejects_conflicts
- [ ] `MaxSensors` cap enforced atomically; offender removed and disposed — unit: native_max_sensors_cap_rejects_beyond_limit
- [x] Stopped phase → sensor queued for next Start — conformance: lifecycle_int_contract:register_before_start_many_sends_after_start, registration_contract:plain_int_sensor_registers_on_start
- [x] Starting/Running → immediate async `InitAndStart`, tracked, awaited by Stop — conformance: lifecycle_int_contract:register_during_running_sends_immediately, registration_contract:sensor_created_while_running_registers
- [ ] Stopping/Disposed → rejected non-throwing (logged, disposed, returned inert) — unit: native_add_after_collector_destroy_is_rejected
- [x] `InitAsync` of every sensor sends `AddOrUpdateSensorRequest` before first value — conformance: registration_contract:plain_int_sensor_registers_on_start, registration_contract:each_sensor_registers_once

## 4. Sensor creation API

Details: [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md)

- [x] `Create{Bool,Int,Double,String,Version,Time}Sensor(path, description)` + `(path, InstantSensorOptions)` — native instant create for every type incl. TimeSpan(7)/Version(8); conformance: instant_mixed_contract, timespan_version_contract:timespan_instant_value / version_full_value (options overload exercised via create_int_sensor_with_alerts)
- [x] `CreateEnumSensor(path, description | EnumSensorOptions)` — conformance: enum_contract:enum_zero_payload, registration_contract:enum_sensor_registers_enum_options
- [x] `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)` + generic `CreateLastValueSensor<T>(path, options, defaultValue)` — conformance: last_value_contract:last_int_flushes_latest_on_stop, last_value_contract:last_bool_flushes_latest_on_stop, last_value_contract:last_double_flushes_latest_on_stop, last_value_contract:last_string_flushes_latest_on_stop
- [x] `CreateRateSensor(path, RateSensorOptions)` + `CreateM1RateSensor` + `CreateM5RateSensor` — conformance: rate_contract:rate_posts_positive_value_after_adds (M1/M5 presets are C#-only convenience over the single double native rate)
- [x] `CreateIntBarSensor(path, barPeriod=300000, postPeriod=15000, descr)` + options overload — conformance: bar_int_contract:int_bar_basic_aggregation_flushes_on_stop, bar_rollover_contract:bar_rolls_on_add_after_close_strict
- [ ] DataCollector-only TimeSpan overloads `Create{Int,Double}BarSensor(path, TimeSpan barPeriod, TimeSpan postPeriod[, precision], descr)` — [decide]: C#-only chrono-sugar; native takes period as ms in BarOptions (migration guide)
- [ ] `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor` presets — [decide]: C#-only preset sugar over the same BarOptions ABI; native takes period as ms (migration guide)
- [x] `CreateDoubleBarSensor(..., precision=2, ...)` + options overload + same five presets — conformance: bar_double_contract:double_bar_basic_aggregation, bar_double_contract:double_bar_precision_rounding (the TimeSpan-overload & {1Hr..1Min} presets are C#-only chrono-sugar over the same ABI)
- [x] `CreateFileSensor(path, fileName, extension="txt", descr)` / `(path, FileSensorOptions)` — conformance: file_contract:file_add_value_utf8_roundtrip
- [ ] Collector-level `SendFileAsync(sensorPath, filePath, status, comment)` — [decide]: disk read not in portable contract; file content path is FileSensor::AddContent (migration guide)
- [ ] `CreateNoParamsFuncSensor<T>(path, descr, Func<T>, interval ms|TimeSpan)` + `Create{1Min,5Min}NoParamsFuncSensor` + `CreateFunctionSensor<T>(path, func, options)` — [decide]: C ABI exposes int function only; templated T + Min presets not ported (migration guide); the int realization is conformance-pinned by function_contract:function_posts_constant_periodically
- [ ] `CreateParamsFuncSensor<T,U>(path, descr, Func<List<U>,T>, interval)` + `Create{1Min,5Min}ParamsFuncSensor` + `CreateValuesFunctionSensor<T,U>` — [decide]: C ABI exposes int-values function only; templated T/U + Min presets not ported (migration guide); the int realization is conformance-pinned by function_contract:values_function_cache_is_sliding_window
- [x] `CreateServiceCommandsSensor()` — native `hsm_collector_create_service_commands_sensor`; conformance: service_commands_contract
- [x] Interface `IInstantValueSensor<T>`: `AddValue(v)` / `(v, comment)` / `(v, status, comment)` — conformance: instant_mixed_contract:string_payload, instant_mixed_contract:string_long_comment_is_trimmed, instant_mixed_contract:double_invalid_status_is_rejected
- [x] Interface `ILastValueSensor<T>` (latest value, sends once on stop, default if none) — conformance: last_value_contract:last_int_flushes_latest_on_stop, last_value_contract:last_int_default_flushes_on_stop
- [x] Interface `IBarSensor<T>`: `AddValue`, `AddValues(IEnumerable)`, `AddPartial(min,max,mean,first,last,count)` — conformance: bar_int_contract:int_bar_basic_aggregation_flushes_on_stop, bar_partial_contract:int_partial_single_passthrough, bar_partial_contract:int_partial_then_values_merge
- [ ] Interface `IFileSensor`: + `Task<bool> SendFile(filePath, status, comment)` — [decide]: SendFile(disk path) not ported; AddContent pinned by file_contract:file_add_value_utf8_roundtrip (migration guide)
- [ ] Interface `IMonitoringRateSensor` (pure alias of `IInstantValueSensor<double>`; defining file is misleadingly named `IMonitoringCounterSensor.cs`) — [decide]: pure C# alias of IInstantValueSensor<double>; native rate is double (migration guide)
- [x] Interface `IServiceCommandsSensor`: `SendCustomCommand(cmd, initiator)`, `SendUpdate(initiator[, new[, old]])`, `SendRestart/SendStart/SendStop(initiator)` — native `hsm_service_commands_send_*`; conformance: service_commands_contract:service_commands_values (fixed command strings + `Initiator:` comment)
- [ ] Interface `IBaseFuncSensor` (in `Obsolete` folder but NOT `[Obsolete]` — current return type): `GetInterval()`, `RestartTimer(TimeSpan)`, `GetFunc()`; params variant `AddValue(U)` — [decide]: C#-only sensor-handle introspection; not in C ABI
- [x] Last-value null-default throws: `CreateLastValueStringSensor(path)` / `CreateLastValueVersionSensor(path)` with implicit null default → `ArgumentException` at creation — conformance: last_value_contract:last_string_null_default_is_rejected
- [ ] Fluent builders (per-type setters): `InstantSensor<T>` `.Description/.Ttl/.KeepHistory/.Priority/.Configure`; `BarSensor<T>` `.BarPeriod/.PostPeriod/.TickPeriod/.Precision/.Description/.Configure`; `RateSensor` `.PostPeriod/.Description/.Configure`; `.Build()` throws `NotSupportedException` for unsupported `T` — [decide]: C#-only fluent setters; native uses SensorOptions/BarOptions/RateOptions structs + AlertBuilder (#1100, migration guide)
- [ ] Properties `Status`, `ComputerName`, `Module`, `Windows`, `Unix` — [decide]: C#-only collector introspection surface; not in C ABI
- [ ] Property `DefaultSensors` (`IEnumerable<ISensor>`; public `ISensor`: `SensorPath/InitAsync/StartAsync/StopAsync/Dispose`; `SensorBase` adds `SendValue(SensorValueBase)` + `ExceptionThrowing` event) — [decide]: C#-only collector/sensor introspection surface; not in C ABI
- [ ] `IWindowsCollection`/`IUnixCollection` are `IDisposable` — [decide]: C#-only collection introspection surface; not in C ABI
- [ ] `AddNLog(LoggerOptions { ConfigPath, WriteDebug })` + embedded config fallback — [decide]: NLog is a managed logging backend; native uses the pluggable logger callback (SetLogger)
- [ ] `AddCustomLogger(ICollectorLogger { Debug, Info, Error(string), Error(Exception) })` — unit: native_logger_sink_can_be_set_and_cleared

## 5. Sensor mechanics

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md)

- [x] Validation: null rejected (strings allowed); double/float NaN/±Infinity rejected — conformance: instant_mixed_contract:string_null_value_is_rejected, instant_mixed_contract:double_nan_is_rejected
- [x] Validation: status must be defined `SensorStatus` member — conformance: value_int_contract:int_invalid_status_is_rejected, instant_mixed_contract:enum_invalid_status_is_rejected
- [x] Validation: comment trimmed to 1024 chars — conformance: instant_int_contract:long_comment_is_trimmed, last_value_contract:last_string_comment_is_trimmed_on_flush
- [x] Validation: rejected values logged (Debug), never enqueued — conformance: instant_mixed_contract:double_nan_is_rejected, last_value_contract:last_int_invalid_status_preserves_previous
- [ ] Instant flow: validate → enqueue immediately; `IsPrioritySensor` routes to priority queue — [decide]: priority-queue routing is C#-internal QoS, not wire-observable; validate→enqueue covered by instant_mixed_contract
- [x] Last-value flow: store latest, single enqueue on StopAsync, `IsLastValue=true` identity — conformance: last_value_contract:last_int_flushes_latest_on_stop, last_value_contract:instant_then_last_same_path_is_rejected
- [x] Bar: min/max/mean(sum/count)/count/first/last under lock — conformance: bar_int_contract:*
- [x] Bar `AddPartial` merge + consistency check (int strict; double tolerance `max(1e-12, |max-min|*1e-9)`) — conformance: bar_partial_contract:*
- [x] Bar `Complete()` rounding (double: `Round(v, Precision, AwayFromZero)` on all stats; int: mean only) — conformance: bar_double_contract:double_bar_precision_rounding, bar_int_contract:int_bar_mean_rounds_half_to_even_up
- [x] Bar roll only after confirmed send (`if (TrySendValue()) BuildNewBar()`) — no-roll-without-send invariant — conformance: bar_rollover_contract:bar_rollover_no_value_lost_invariants
- [x] Bar UTC-epoch alignment: `OpenTime = floor(now/period)*period`, `CloseTime = OpenTime + BarPeriod` — conformance: bar_int_contract:int_bar_basic_aggregation_flushes_on_stop, bar_rollover_contract:bar_rollover_no_value_lost_invariants
- [ ] Bar periods: `BarPeriod` 5 min / `BarTickPeriod` 5 s / `PostDataPeriod` 15 s / `Precision` 2 (0..15), all validated — unit: native_create_rejects_negative_option_fields
- [x] Monitoring base: periodic send loop via `ScheduledTaskHandle`, virtuals `GetValue/GetStatus/GetComment/GetDefaultValue` — conformance: function_contract:function_posts_constant_periodically, rate_contract:rate_posts_zero_when_idle
- [ ] Monitoring base: `_sendValueInProgress` reentrancy guard — [decide]: C#-internal scheduler reentrancy guard; not wire-observable (native scheduler is single-threaded per sensor)
- [ ] Monitoring base: lifecycle epoch — capture, revalidate before send, drop stale (init/restart/stop bump) — [decide]: C#-internal monitoring-base epoch; native equivalent is the per-item send epoch, not a separately observable monitoring contract
- [ ] Monitoring base: `GetValue` exception → value with `status=Error, comment=ex.Message` + deduped log — [decide]: C#-internal monitoring-base error wrapping; native dedup pinned by native_logger_deduplicates_repeated_errors_within_window
- [ ] Monitoring base: `RestartTimerAsync(newPeriod)` = bounded stop → epoch bump → reschedule — [decide]: C#-only sensor-handle timer control; not in C ABI
- [x] Rate: lock-free CAS accumulation; `GetValue` = `Interlocked.Exchange(sum,0) / period.TotalSeconds` — conformance: rate_contract:rate_posts_positive_value_after_adds
- [x] Rate: sticky status/comment from last AddValue; default PostDataPeriod 1 min — conformance: rate_contract:rate_status_and_comment_are_sticky
- [ ] File: async read (81920 buffer, `FileShare.ReadWrite`); `MaxFileSizeBytes` (10 MB default) + `int.MaxValue` caps — [decide]: disk IO not in portable contract; native FileSensor takes string content via AddContent (migration guide)
- [x] File: name/extension from path else options defaults; `AddValue(string)` = UTF-8 bytes — conformance: file_contract:file_add_value_utf8_roundtrip
- [ ] File: `SendFile` false on invalid status / `CanAcceptData==false` / missing / oversize — [decide]: SendFile(disk path) not ported; disk IO out of portable contract (migration guide)
- [x] Function no-params: invoke func each period — conformance: function_contract:function_posts_initial_value_immediately, function_contract:function_posts_constant_periodically
- [x] Function params: `ConcurrentQueue` cache, FIFO eviction at `MaxCacheSize` (10000), snapshot under lock, func outside lock — conformance: function_contract:values_function_cache_evicts_oldest, function_contract:values_function_cache_is_sliding_window
- [ ] All sensors: `HandleException` → `AddException` (dedup) + `ExceptionThrowing` event; never crash host — unit: native_logger_deduplicates_repeated_errors_within_window

## 6. Options / prototypes / paths

Details: [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md) §Options & path model

- [x] `SensorOptions` common: `Description`, `SensorUnit`, `TTLs`, `KeepHistory`, `SelfDestroy`, `EnableForGrafana`, `IsSingletonSensor`, `AggregateData`, `Statistics(EMA)`, `IsComputerSensor`, `SensorLocation(Module|Product)`, `TtlAlerts` — native `hsm_collector_create_sensor_with_options` + `hsm_sensor_options_t`; conformance: options_surface_contract:full_options_register_in_payload + paired golden `native_wire_registration_full_options_*`. (`DefaultAlertsOptions/IsForceUpdate/IsPrioritySensor` remain wire-default 0/false — diag/QoS, #1099.)
- [x] Singular conveniences `SensorOptions.TTL` (→ `TTLs`, ttl_ms) and `TtlAlert` (→ `TtlAlerts`, the alert builder)
- [x] `DisplayUnit` per options type → wire `DisplayUnit (int?)` — `hsm_sensor_options_t.display_unit`; pinned by `native_wire_registration_full_options_*` (the `RateDisplayUnit` enum values arrive with rate options, #1100)
- [x] `InstantSensorOptions(+Alerts)`; `MonitoringInstantSensorOptions(+PostDataPeriod 15 s)` — conformance: registration_contract:default_fields_int_sensor, options_surface_contract:full_options_register_in_payload
- [x] `BarSensorOptions(+BarPeriod/BarTickPeriod/Precision/BarAlerts)` — conformance: registration_contract:default_fields_bar_sensor
- [x] `RateSensorOptions(PostDataPeriod 1 min, Unit=ValueInSecond)` — conformance: registration_contract:default_fields_rate_sensor
- [ ] `FunctionSensorOptions` / `ValuesFunctionSensorOptions(+MaxCacheSize)` — both default `PostDataPeriod` 1 min (ms-param factory overloads default 15 s instead) — [decide]: C ABI exposes int / int-values function options only; templated overloads not ported (migration guide); behavior pinned by function_contract:*
- [ ] `FileSensorOptions(+DefaultFileName/Extension/MaxFileSizeBytes)` — [decide]: `MaxFileSizeBytes` governs a disk read not in the portable contract; DefaultFileName/Extension pinned by file_contract:file_add_value_utf8_roundtrip
- [x] `EnumSensorOptions(+EnumOptions, AggregateData=true, GenerateEnumOptionsDecription())` — conformance: registration_contract:enum_sensor_registers_enum_options
- [ ] `DiskSensorOptions { TargetPath default C:\, CalibrationRequests default 6, PostDataPeriod 5 min }`; `DiskBarSensorOptions { TargetPath }` — pure config for the #1099 disk sensors; ships with them — [decide]: config for #1099 default disk sensors; ships with them
- [~] `VersionSensorOptions { Version, StartTime }`; `ServiceSensorOptions { ServiceName, IsHostService → .module placement, SensorPath }` — the `IsHostService` → `.module/...` placement is ported and pinned by service_commands_contract (`.module/Service commands`); the option structs themselves carry the #1099 default product-version / service-status sensors
- [ ] `NetworkSensorOptions`; `WindowsInfoSensorOptions { PostDataPeriod default 12 h }`; `CollectorMonitoringInfoOptions` — config for the #1099 default sensors; ships with them — [decide]: config for #1099 default sensors; ships with them
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

> **#1099 native port status:** the **registration payload** of every default sensor below is ported
> and conformance-pinned (`hsm_collector_add_default_sensor` ↔ the real managed prototype) — corpus:
> `default_sensors_contract:*`; byte goldens: `WireFormatGoldenLockTests.Default_sensor_registrations_match_*`
> ↔ `NativeDefaultSensorWireMatchesNet`. The boxes below stay `[ ]` because their **live values** are
> per-platform smoke, not the portable corpus. **#1164 delivered the double/PDH live-value subset on
> Windows** (`hsm_collector_install_windows_metric_sources` — a PDH/Win32 factory for Total CPU, Free
> RAM, LogicalDisk active-time/queue/write-speed, free disk, process CPU/mem/threads, TCP connections
> established): the metric-source seam now binds a reader at Start and posts each period (DoubleBar →
> one-sample bar, Double/Int → value; recreate-on-error, dispose-on-stop). Pinned by the platform-
> agnostic plumbing unit `native_metric_source_drives_default_bar_sensor` + the Windows smoke
> `native_windows_metric_sources_produce_live_value`, and proven end-to-end against a real server by
> `examples/windows-monitor`. Still follow-up: WMI (last restart/update/install), registry (Windows
> version), EventLog (logs), free-disk **prediction** (TimeSpan EMA), ThreadPool count, network
> failure/reset deltas — those need non-double seams.

- [ ] `AddProcessCpu` (`Process \ % Processor Time`, instance = process) — registration: `default_sensors_contract:process_cpu_registers_empty_alerts` — platform: win; registration: default_sensors_contract:process_cpu_registers_empty_alerts; live-read: #1099 follow-up (PDH)
- [ ] `AddProcessMemory` (`Process \ Working set` → MB) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddProcessThreadCount` (`Process \ Thread Count`) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddProcessThreadPoolThreadCount` (ThreadPool API) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddProcessTimeInGC` (perf counter net472 / EventListener net6+) — **DROPPED in the native port (#1099):** no managed GC in a native host — [decide]: dropped in native port — no managed GC in a native host (#1099)
- [ ] `AddProcessMonitoringSensors` bulk — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddTotalCpu` (`Processor \ % Processor Time \ _Total`) — platform: win; registration: default_sensors_contract:total_cpu_registers; live-read: #1099 follow-up (PDH)
- [ ] `AddFreeRamMemory` (`Memory \ Available MBytes`) — platform: win; registration: default_sensors_contract:free_ram_registers_none_statistics; live-read: #1099 follow-up (PDH)
- [ ] `AddGlobalTimeInGC` (`.NET CLR Memory \ % Time in GC \ _Global_`) — **DROPPED in the native port (#1099):** no managed GC in a native host — [decide]: dropped in native port — no managed GC in a native host (#1099)
- [ ] `AddSystemMonitoringSensors` bulk — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddFreeDiskSpace` / `AddFreeDisksSpace` (DriveInfo, instant MB, 5 min) — platform: win; registration: default_sensors_contract:free_disk_space_registers; live-read: #1099 follow-up (DriveInfo)
- [ ] `AddFreeDiskSpacePrediction` / `AddFreeDisksSpacePrediction` (EMA 0.9/0.1, 30 s sampling, calibration first 6 requests — `CalibrationRequests` default, OffTime on growth) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (DriveInfo)
- [ ] `AddActiveDiskTime` / `AddActiveDisksTime` (`LogicalDisk \ % Disk Time`) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddDiskQueueLength` / `AddDisksQueueLength` (`LogicalDisk \ Avg. Disk Queue Length`) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddDiskAverageWriteSpeed` / `AddDisksAverageWriteSpeed` (`Disk Write Bytes/sec` → MB/s) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] Disk fan-out: `DriveInfo.GetDrives()` filtered `DriveType.Fixed`, letter in name + counter instance — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (DriveInfo)
- [ ] `AddDiskMonitoringSensors` / `AddAllDisksMonitoringSensors` bulks — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddWindowsLastRestart` (WMI LastBootUpTime → TimeSpan, 12 h) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (WMI)
- [ ] `AddWindowsLastUpdate` (WMI QuickFixEngineering max InstalledOn) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (WMI)
- [ ] `AddWindowsInstallDate` (WMI InstallDate; default alert > 4 y) — platform: win; registration: default_sensors_contract:windows_install_date_registers; live-read: #1099 follow-up (WMI)
- [ ] `AddWindowsVersion` (registry → Version sensor) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (registry)
- [ ] `AddWindowsApplicationErrorLogs` / `AddWindowsSystemErrorLogs` (EventLog subscription, value=EventID, comment=Source+Message) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (EventLog)
- [ ] `AddWindowsApplicationWarningLogs` / `AddWindowsSystemWarningLogs` — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (EventLog)
- [ ] `AddErrorWindowsLogs` / `AddWarningWindowsLogs` / `AddAllWindowsLogs` bulks — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (EventLog)
- [ ] `AddWindowsInfoMonitoringSensors` bulk — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (WMI)
- [ ] `AddNetworkConnectionsEstablished` (TCPv4+v6 gauge, 1 min) — platform: win; registration: default_sensors_contract:network_established_registers; live-read: #1099 follow-up (PDH)
- [ ] `AddNetworkConnectionFailures` / `AddNetworkConnectionsReset` (deltas) — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `AddAllNetworkSensors` bulk — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (PDH)
- [ ] `SubscribeToWindowsServiceStatus(name | options)` (enum of `ServiceControllerStatus`, 5 s poll, send-on-change, alert ≠Running w/ 5 min confirmation, 1 h re-resolve backoff) — platform: win; registration: default_sensors_contract:service_status_registers_enum_options; live-read: #1099 follow-up (ServiceController)
- [x] Service-status registration payload: `EnumOptions` for 7 `ServiceControllerStatus` members with fixed ARGB colors + generated markdown description; `IsHostService` placement — conformance: default_sensors_contract:service_status_registers_enum_options
- [ ] `UnsubscribeWindowsServiceStatus` — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (ServiceController)
- [ ] ServiceCommands sensor: fixed strings "Service start/stop/restart", "Service update [from X] to Y" + implicit `IfReceivedNewValue → notification` alert — platform: win; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (ServiceController)
- [ ] Perf-counter seam: `IPerformanceCounterFactory`/`IPerformanceCounter`, recreate on `InvalidOperationException`, dispose on stop — unit: native_metric_source_seam_lifecycle
- [ ] `AddAllComputerSensors()` / `AddAllModuleSensors(version)` / `AddAllDefaultSensors(version)` bulks — group composition (incl. the 4 event-log sensors in windows-info) — native unit: `native_default_sensor_group_composition` — unit: native_default_sensor_group_composition

## 9. Default sensors — Unix

Details: [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md)

- [ ] `AddProcessCpu` (`Process.TotalProcessorTime` delta / wall time) — platform: unix; registration: default_sensors_contract:process_cpu_registers_empty_alerts; live-read: #1099 follow-up (Process.TotalProcessorTime)
- [ ] `AddProcessMemory` (`WorkingSet64` → MB) — platform: unix; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (/proc)
- [ ] `AddProcessThreadCount` (`Process.Threads.Count`) — platform: unix; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (/proc)
- [ ] `AddProcessThreadPoolThreadCount` — platform: unix; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (/proc)
- [ ] `AddTotalCpu` (`/proc/stat` jiffies, `ProcStat` parser) — platform: unix; registration: default_sensors_contract:total_cpu_registers; live-read: #1099 follow-up (/proc)
- [ ] `AddFreeRamMemory` (`/proc/meminfo` MemAvailable, `ProcMeminfo` parser) — platform: unix; registration: default_sensors_contract:free_ram_registers_none_statistics; live-read: #1099 follow-up (/proc)
- [ ] `AddFreeDiskSpace` + prediction (root `/` only, DriveInfo/statvfs) — platform: unix; registration: default_sensors_contract:free_disk_space_registers; live-read: #1099 follow-up (statvfs)
- [ ] Bulks: process / system / disk / computer / module / default — unit: native_default_sensor_group_composition
- [ ] No external process spawning (kernel files + managed APIs only) — platform: unix; registration: default_sensors_contract (catalog-pinned); live-read: #1099 follow-up (/proc)
- [x] **[decide]** Unix gaps vs Windows: GC time, network, OS info, event logs, service status — **RESOLVED (#1099):** keep the managed parity subset (process / total CPU / free RAM / root free-disk + prediction); no native systemd/journald/network extensions

> **#1099:** the Unix registration payloads (process/system/root-disk) share the same conformance-pinned
> catalog rows as their Windows counterparts; the procfs/statvfs **live readers** are the live-value follow-up.

## 10. Module & diagnostic sensors (cross-platform)

Details: [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md)

> **#1099 native port status:** registration payloads ported and conformance-pinned
> (`default_sensors_contract:*`, e.g. `collector_alive_registers_ttl_alert`, `collector_version_registers`,
> `queue_overflow_registers`); the live feeds (heartbeat scheduler, `MessageDeduplicator` errors, the
> queue-stats pipeline taps) are the live-value follow-up under #1099.

- [ ] `AddCollectorAlive` (bool heartbeat, 15 s, first=false, TTL 1 min, KeepHistory 180 d) — registration: `default_sensors_contract:collector_alive_registers_ttl_alert` — platform: cross; registration: default_sensors_contract:collector_alive_registers_ttl_alert; live-read: #1099 live-feed follow-up (heartbeat scheduler)
- [ ] `AddCollectorVersion` (assembly version + start time, KeepHistory ~5 y) — platform: cross; registration: default_sensors_contract:collector_version_registers; live-read: #1099 live-feed follow-up
- [ ] `AddCollectorErrors` (string, fed by MessageDeduplicator) — platform: cross; registration: default_sensors_contract (catalog-pinned); live-read: #1099 live-feed follow-up (MessageDeduplicator)
- [ ] `AddProductVersion(VersionSensorOptions)` — platform: cross; registration: default_sensors_contract:collector_version_registers; live-read: #1099 live-feed follow-up
- [ ] `AddCollectorMonitoringSensors` bulk — unit: native_default_sensor_group_composition
- [ ] `AddQueueOverflow` (int bar; includes retry-path drops; never suppressed) — platform: cross; registration: default_sensors_contract:queue_overflow_registers; live-read: #1099 live-feed follow-up (queue-stats tap)
- [ ] `AddQueuePackageValuesCount` (int bar per package) — platform: cross; registration: default_sensors_contract (catalog-pinned); live-read: #1099 live-feed follow-up (queue-stats tap)
- [ ] `AddQueuePackageProcessTime` (double bar, avg time-in-queue) — platform: cross; registration: default_sensors_contract (catalog-pinned); live-read: #1099 live-feed follow-up (queue-stats tap)
- [ ] `AddQueuePackageContentSize` (double bar, chars → MB) — platform: cross; registration: default_sensors_contract (catalog-pinned); live-read: #1099 live-feed follow-up (queue-stats tap)
- [ ] `AddAllQueueDiagnosticSensors` bulk (all priority sensors) — unit: native_default_sensor_group_composition

## 11. Queues & pipeline

Details: [`data-pipeline/feature.md`](../../aicontext/features/collector/data-pipeline/feature.md)

- [ ] Four queues over unbounded Channel: Data (periodic batch), Priority (reactive batch), File (single-item), Command (reactive batch) — native (#1097): one worker queue + reactive file kick; the QoS split is C#-internal, not observable in value delivery (see data-pipeline/feature.md Native port) — [decide]: C#-internal QoS split, not observable in value delivery (native single worker queue — data-pipeline/feature.md)
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
- [ ] `ShutdownMode.TerminalDispose`: flush, drop-canceled, wait `min(RequestTimeout, 1 s)` — native (#1097): Dispose reuses the bounded stop drain; the finer timeout matrix is C#-internal, not observable — [decide]: C#-internal shutdown-mode timeout matrix, not wire-observable (native reuses the bounded stop drain)
- [ ] `ShutdownMode.StartRollback`: clear immediately, no flush — native (#1097): a failed Start leaves nothing queued (data gate closed); the explicit rollback mode is C#-internal — [decide]: C#-internal shutdown-mode detail, not wire-observable (a failed native Start leaves nothing queued behind the closed data gate)
- [x] Drain order: stop scheduler → flush last-values/bars → stop dispatcher → bounded drain — native (#1097): single-queue FIFO drain; the Priority→Data→[suppress]→File→Command ordering is a per-queue C# concern
- [x] Flush timeout clamped [1 s, 5 s] — conformance: flush_contract:stop_with_hanging_sender_is_bounded_and_drops_pending, flush_contract:stop_with_hanging_sender_drops_pending_bar
- [ ] Diagnostics suppression after data-drain boundary (#1075); overflow exempt; reset on Start — native: deferred to #1099 (needs the diagnostic sensors that would receive the telemetry) — [decide]: deferred to #1099 (needs the diagnostic sensors that would receive the telemetry)
- [x] Failure-log honesty — native (#1097): the bounded stop logs "Collector stop dropped N pending value(s)"; the C# flush-context "queued for clear" vs "preserved" wording is internal to the four-queue flush

## 12. HTTP transport

Details: [`http-client/feature.md`](../../aicontext/features/collector/http-client/feature.md)

- [ ] **[wire]** Routes under `/api/sensors/`: bool, int, double, string, timespan, version, rate, enum, intBar, doubleBar, list, file, commands, addOrUpdate, testConnection(GET) — unit: native_http_endpoint_routing_matches_net
- [ ] **[wire]** Headers `Key`, `ClientName`; base `{scheme}://{address}:{port}` — unit: native_http_endpoint_routing_matches_net
- [ ] HTTPS default; plaintext only with `AllowPlaintextTransport`; `AllowUntrustedServerCertificate` skips TLS validation — platform: tls; smoke: http-transport lane (native_http_transport_posts_to_capture_server)
- [ ] Polly: data/priority/file — 10 attempts exponential 1 s → 2 min; commands — `int.MaxValue` linear — unit: native_http_retry_policy_matches_net
- [ ] **[decide]** No `ShouldHandle` for 4xx/5xx (poison retries until eviction) — reproduce or fix consciously — [decide]: RESOLVED in #1096 — `BaseHandlers.ShouldRetry` now retries 5xx only on the bounded data/priority/file pipelines (commands stay exceptions-only, 4xx never); pinned by native_http_retry_policy_matches_net and C# Retry5xxParityTests
- [ ] `CancelPendingRequests`: cancel token + fresh source, NEVER dispose HttpClient — [decide]: C#-internal HttpClient lifecycle, not wire-observable (native mirrors via the HttpTransport cancel/reset xfer-abort — http-client/feature.md)
- [ ] `PackageSendingInfo { ContentSize(chars), IsSuccess, Error }` — [decide]: C#-internal self-diagnostics struct, not wire-observable (native re-enqueues on the equivalent send-failure signal — http-client/feature.md)
- [x] JSON: System.Text.Json, NaN/Infinity literals allowed, runtime-polymorphic converter — conformance: number_format_contract:double_wire_text_matrix
- [ ] Per-command response parsing for commands/addOrUpdate (error dictionary keyed by sensor **path**) — [decide]: per-command error-dictionary parse is a remaining native integration step (http-client/feature.md Native port); no collector-observable behavior until wired

## 13. Scheduler

Details: [`scheduling/feature.md`](../../aicontext/features/collector/scheduling/feature.md)

- [ ] Per-collector instance (no process-global state), disposed by DataCollector last — [decide]: C#-internal scheduler mechanic; the native scheduler is a per-collector internal owned by the collector lifecycle (no process-global state)
- [ ] Bucketed timer wheel, single worker, ThreadPool dispatch — [decide]: C#-internal scheduler mechanic; the native timer/worker is internal, its periodic firing is pinned by native_scheduler_clock_seam_drives_periodic_posts
- [ ] Monotonic Stopwatch-based clock (never `Environment.TickCount`) — unit: native_scheduler_clock_seam_drives_periodic_posts
- [ ] `Schedule(Action|Func<Task>, delay, period, onError)`; period > 0 or Infinite (one-shot, auto-dispose) — [decide]: C#-internal scheduler API surface; native periodic scheduling is pinned by native_scheduler_clock_seam_drives_periodic_posts
- [ ] onError: action exceptions routed to callback, loop never dies — unit: native_scheduler_on_error_isolates_throwing_callback
- [ ] No overlapping runs of one task (skip tick while running) — [decide]: C#-internal scheduler mechanic; the native single worker serializes ticks so overlap cannot occur (clock seam pinned by native_scheduler_clock_seam_drives_periodic_posts)
- [ ] Catch-up: overdue tasks advance by whole periods into the future — [decide]: C#-internal scheduler mechanic (native scheduler is internal; the deterministic clock seam is exercised by native_scheduler_clock_seam_drives_periodic_posts)
- [ ] `ScheduledTask.StopAsync(waitForCurrentRun)` bounded ~1 s — [decide]: C#-internal scheduler mechanic; native stop-boundedness is covered at the collector level (bounded stop drain — flush_contract), no separate per-task StopAsync surface
- [ ] `ScheduledTaskHandle`: idempotent Start/StopAsync composition wrapper — [decide]: C#-internal scheduler API surface, not wire-observable (native has no separate handle wrapper; collector lifecycle is idempotent — native_dispose_is_terminal_and_idempotent)
- [ ] Worker shutdown grace 5 s on dispose — [decide]: C#-internal scheduler mechanic; native worker shutdown is bounded by the collector stop drain (flush_contract), not a separate scheduler grace timer

## 14. Error handling / dedup / logging

Details: [`error-handling/feature.md`](../../aicontext/features/collector/error-handling/feature.md)

- [ ] MessageDeduplicator: window dedup, capacity + oldest-expiry eviction, count-suffix flush — unit: native_logger_deduplicates_repeated_errors_within_window
- [ ] Zero window = invoke immediately AND return (no double log) — unit: native_logger_zero_window_logs_every_error
- [ ] Routing: sensor ex → AddException; queue loop → AddQueueLoopError; validation → LogDroppedValue(Debug); shutdown discard → LogDiscardedItems(Error) — [decide]: C#-internal error-routing taxonomy, not wire-observable (native routes through a single logger sink — native_logger_sink_can_be_set_and_cleared)
- [ ] CollectorErrorsSensor fed from dedup callback — [decide]: deferred to #1099 (needs the diagnostic CollectorErrorsSensor surface that would receive the deduped telemetry)
- [ ] LoggerManager swallows logger exceptions — unit: native_logger_sink_can_be_set_and_cleared
- [ ] Lifecycle event/listener exceptions isolated per handler — unit: native_lifecycle_listener_exception_is_isolated
- [ ] Dispose failures isolated per component — [decide]: C#-internal per-component dispose isolation, not wire-observable (native dispose is terminal/idempotent — native_dispose_is_terminal_and_idempotent)

## 15. Wire contract — ALL [wire]

Details: [`api/wire-contract/feature.md`](../../aicontext/features/api/wire-contract/feature.md)

- [x] `SensorType`: Boolean=0 Int=1 Double=2 String=3 IntegerBar=4 DoubleBar=5 File=6 TimeSpan=7 Version=8 Rate=9 Enum=10 — conformance: instant_int_contract:running_collector_stores_int_payload, enum_contract:enum_zero_payload, timespan_version_contract:timespan_instant_value
- [x] `SensorStatus`: OffTime=0 Ok=1 Warning=2 Error=3 — conformance: value_int_contract:status_off_time_numeric_value, value_int_contract:status_error_numeric_value
- [ ] `Unit` sparse values (bits=0…GB=4, Percents=100, Ticks=1000, ms=1010, s=1011, min=1012, Count=1100, Requests=1101, Responses=1102, rates 2100–2103, ValueInSecond=3000) — unit: native_wire_registration_full_options_matches_net_byte_layout
- [x] `AlertOperation` (LE=0 LT=1 GT=2 GE=3 Eq=4 Ne=5 IsChanged=20 IsError=21 IsOk=22 →Error=23 →Ok=24 Contains=30 StartsWith=31 EndsWith=32 ReceivedNewValue=50) — conformance: alert_registration_contract:instant_alert_registers_in_payload, alert_registration_contract:multi_condition_alert_combines_or
- [ ] `AlertProperty` (Status=0 Comment=1 Value=20 Min=101 Max=102 Mean=103 Count=104 Last=105 First=106 Length=120 OriginalSize=151 NewSensorData=200 Ema*=210–214) — unit: native_wire_registration_with_alerts_matches_net_byte_layout
- [x] `AlertCombination` And=0 Or=1; `TargetType` Const=0 LastValue=1; `AlertRepeatMode` 5/6/7/10/20/50/100 — conformance: alert_registration_contract:multi_condition_alert_combines_or
- [ ] `AlertDestinationMode`: DefaultChats=0(obs) NotInitialized=1 Empty=2 FromParent=3 AllChats=200 — unit: native_wire_registration_with_alerts_matches_net_byte_layout
- [ ] Display units: `NoDisplayUnit`; `RateDisplayUnit` PerSecond=0…PerMonth=5 → `DisplayUnit (int?)` — unit: native_wire_registration_full_options_matches_net_byte_layout
- [ ] Flags `StatisticsOptions{EMA=1}`, `DefaultAlertsOptions{DisableTtl=1, DisableStatusChange=2}` — unit: native_wire_registration_full_options_matches_net_byte_layout
- [ ] `SensorValueBase` { Path, Comment?, Time(UTC now), Status(Ok) } + typed `Value` per DTO — unit: native_wire_value_json_matches_net_byte_layout
- [ ] Bar DTOs: Min/Max/Mean/Count/FirstValue?/LastValue/OpenTime/CloseTime (obsolete `Percentiles` never populated but serialized as null) — unit: native_wire_bar_json_matches_net_byte_layout
- [ ] `FileSensorValue`: Value = `List<byte>` → **numeric JSON array, NOT base64**; Name; Extension. No Counter DTO exists (`CounterSensorValue.cs` contains `RateSensorValue`) — unit: native_wire_file_json_matches_net_byte_layout
- [x] `EnumOption` { Key:int, Value:string, Description:string, Color:int ARGB } — conformance: registration_contract:enum_sensor_registers_enum_options
- [x] `AddOrUpdateSensorRequest`: full property set incl. EnumOptions, Alerts, TtlAlerts, DefaultAlertsOptions — conformance: registration_contract:int_sensor_registers_ttl_unit_description, options_surface_contract:full_options_register_in_payload
- [x] Registration time fields (`TTLs/KeepHistory/SelfDestroy/ConfirmationPeriod`) on the wire as **long ticks**; `TtlAlerts[*].TtlValue` overrides `options.TTLs`; `IsSingletonSensor` OR-ed with `IsComputerSensor` — conformance: alert_registration_contract:instant_alert_registers_in_payload
- [x] JSON: **PascalCase** property names, **nulls/defaults emitted** (default System.Text.Json — `[DefaultValue]` attrs have no effect), enums as numbers, DateTime ISO-8601 `Z`, TimeSpan .NET "c" format `[-][d.]hh:mm:ss[.fffffff]`, Version `a.b[.c[.d]]` — conformance: number_format_contract:double_wire_text_matrix, timespan_version_contract:timespan_instant_value, instant_mixed_contract:string_json_special_characters_are_escaped
- [x] Batch `list` polymorphism: discriminated by the **numeric `Type` property** (server scans for `Type`, switches on `SensorType` int) — no string discriminator — conformance: instant_int_contract:running_collector_stores_int_payload, enum_contract:enum_zero_payload
- [ ] **[decide]** History DTOs `HistoryRequest{Path,From,To?,Count?,Options(IncludeTtl=1)}`, `FileHistoryRequest{+FileName,Extension,IsZipArchive}` (collector doesn't query history today) — [decide]: collector is send-only; history is a server query (no collector surface)

## 16. Wrapper parity gaps — ALL [decide]

Reference: `src/wrapper/include/` (C++/CLI wrapper as minimal-API oracle). The public C++ RAII API
(#1100, `hsm::collector`) is the supported successor; the migration audit is in
`docs/native-collector-migration.md`.

- [x] TimeSpan sensor — C ABI `hsm_collector_create_timespan_sensor` + `hsm_sensor_add_timespan` (#1098); C++ `TimeSpanSensor` (#1100)
- [x] Version sensor — C ABI `hsm_collector_create_version_sensor` + `hsm_sensor_add_version` (#1098); C++ `VersionSensor` (#1100)
- [x] Enum sensor — C ABI `hsm_collector_create_enum_sensor[_with_options]` (#1098); C++ `Collector::CreateEnumSensor` + `EnumOption` (#1100)
- [x] Service-commands sensor — C ABI `hsm_collector_create_service_commands_sensor` (#1098); C++ `ServiceCommandsSensor` (#1100)
- [x] Lifecycle listeners/events — C ABI `hsm_collector_add_lifecycle_listener` (#1095); C++ `AddLifecycleListener(std::function)` (#1100)
- [x] Fluent builders — C++ `AlertBuilder` + `SensorOptions`/`BarOptions`/`RateOptions` builders (#1100)
- [x] History queries — N/A: the collector is send-only; history is a server-side query (no collector surface in .NET either). Resolved not-applicable.
- [x] Rate-sensor type asymmetry — resolved double-only: C++ `CreateRateSensor` is double (matches .NET); the C++/CLI wrapper's int-rate convenience is intentionally dropped (`native-collector-migration.md`).

## 17. Cross-cutting invariants (gate for every slice)

- [x] Values before Start / after Stop silently rejected — conformance: instant_int_contract:before_start_drops_value, stress_mixed_contract:mixed_instant_stress_drops_values_after_stop
- [x] Start/Stop/Dispose idempotent + race-safe; exactly one ToStopped per cycle — conformance: lifecycle_int_contract:start_twice_is_noop, lifecycle_int_contract:stop_twice_is_noop, lifecycle_int_contract:repeated_start_stop_cycles_send_once_per_cycle (+ native unit: native_dispose_is_terminal_and_idempotent)
- [x] Path dedup transparent; type conflict throws — conformance: instant_int_contract:duplicate_sensor_path_is_idempotent, last_value_contract:instant_then_last_same_path_is_rejected
- [x] All validation pre-enqueue — conformance: instant_mixed_contract:double_nan_is_rejected, value_int_contract:int_invalid_status_is_rejected
- [x] Bars never roll without confirmed send; UTC-aligned windows — conformance: bar_rollover_contract:bar_rollover_no_value_lost_invariants
- [ ] Stale callbacks invalidated by lifecycle epoch — [decide]: C#-internal epoch mechanic, not wire-observable (native invalidates queued work per-item by dispatch-epoch token; restart-cycle correctness pinned by lifecycle_int_contract:repeated_start_stop_cycles_send_once_per_cycle)
- [x] FIFO at-least-once; retry-forever + overflow backstop — native (#1097): retry kept below capacity, dropped only when the buffer is full — native unit: native_retry_below_capacity_is_always_redelivered, native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values (native intentionally does NOT port the C# #1090 below-capacity drop)
- [x] Graceful stop flushes accepted work; terminal dispose bounded under broken transport — conformance: flush_contract:stop_flushes_all_pending_before_returning, flush_contract:stop_with_hanging_sender_is_bounded_and_drops_pending
- [ ] Diagnostics suppressed past drain boundary; overflow exempt — [decide]: deferred to #1099 (needs the diagnostic sensors that would receive the telemetry)
- [ ] Scheduler loop never dies; errors to onError — unit: native_scheduler_on_error_isolates_throwing_callback
- [ ] Logger/listener exceptions always swallowed — unit: native_logger_sink_can_be_set_and_cleared, native_lifecycle_listener_exception_is_isolated
- [x] Wire values/names/formats frozen — conformance: number_format_contract:*, instant_mixed_contract:string_json_special_characters_are_escaped
