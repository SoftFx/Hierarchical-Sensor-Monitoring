# Feature: Sensor Types

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - all sensor types, their value-flow mechanics, validation rules, and options/path model

---

## Description

DataCollector provides sensor kinds that differ in how values are produced and aggregated. Users create sensors through `IDataCollector` (see `public-api/feature.md` for the factory surface) and either push values manually or rely on periodic reads.

---

## Sensor taxonomy

| Kind | Backing class | Value flow |
|---|---|---|
| Instant (`bool/int/double/string/TimeSpan/Version/enum`) | `SensorInstant<T>` | `AddValue` → validate → enqueue immediately (priority queue if `IsPrioritySensor`) |
| Last-value | `LastValueSensorInstant<T>` (`IsLastValue=true`) | stores latest value/status/comment; single enqueue on `StopAsync`; default value if none added |
| Bar (`int`, `double`) | `BarMonitoringSensorBase` + `IntMonitoringBar`/`DoubleMonitoringBar` | aggregates min/max/mean/count/first/last over `BarPeriod`; two scheduled handles (collect + send); non-empty partial bar flushed on `StopAsync` (sensor disposal does NOT flush — `DisposeAsyncCore`) |
| Rate | `MonitoringRateSensor` | lock-free CAS sum; sends `sum / PostDataPeriod.TotalSeconds`, atomic reset; sticky status/comment |
| Function (no params) | `FunctionSensorInstant<T>` | invokes `Func<T>` every period |
| Function (params) | `ValuesFunctionSensorInstant<T,U>` | `AddValue(U)` buffers into `ConcurrentQueue` (FIFO eviction at `MaxCacheSize`, default 10000); snapshot under lock, func invoked outside lock |
| File | `FileSensorInstant` | `SendFile`: async read (81920-byte buffer, `FileShare.ReadWrite`), caps `MaxFileSizeBytes` (10 MB default) and `int.MaxValue`; name/extension from path else options; `AddValue(string)` = UTF-8 bytes |
| Service commands | `ServiceCommandsSensor` | predefined command strings + initiator comment |

Monitoring (timer) sensors — bar, rate, function, and most default sensors — share `MonitoringSensorBase<T>`: periodic send via `ScheduledTaskHandle`, virtuals `GetValue/GetStatus/GetComment/GetDefaultValue`.

Periodic-post contract (pinned cross-language by `rate_contract.hsmtest` / `function_contract.hsmtest`): the FIRST post fires immediately on Start (`TimerDueTime` = 0 → schedule delay 0), then every `PostDataPeriod`. Rate specifics: value = sum / measured elapsed (#1102-E2), status/comment sticky from the last accepted increment, invalid/NaN increments silently dropped, and Stop does NOT flush the pending sum (deliberate — a partial-window rate is alert-noise risk; data preservation at stop applies to bars, not rates). Values-function specifics: the buffer is a sliding window (snapshot per post, never drained; oldest evicted past `MaxCacheSize`; accepts values before Start). File specifics (`file_contract.hsmtest`): `AddValue(string)` → UTF-8 file payload with options' name/extension; null content ignored; the file queue flushes pending payloads on graceful Stop; disk-based `SendFile` is not part of the portable contract.

## Validation & normalization (all sensors)

`Extensions/SensorValueExtensions.cs`:

- null values rejected (strings allowed); `double`/`float` NaN and ±Infinity rejected;
- status must be a defined `SensorStatus` member;
- comments trimmed to **1024** chars;
- rejected values are logged via `DataProcessor.LogDroppedValue` (Debug level) and never enqueued;
- bar `AddPartial` consistency: int — strict `min <= {mean,first,last} <= max`; double — tolerance `max(1e-12, |max-min| * 1e-9)`;
- bar values with `Count <= 0` are additionally filtered at package build.

## Lifecycle epoch (stale-callback invalidation)

`MonitoringSensorBase` keeps an `Interlocked` long `_lifecycleEpoch`, bumped on init/restart/stop. `TrySendValue` captures the epoch, builds the value, revalidates the epoch before `SendValue`, and drops the value if the sensor was stopped/restarted underneath. `_sendValueInProgress` (Interlocked flag) prevents concurrent snapshots. `GetValue` exceptions are caught → `HandleException` → value sent with `status=Error, comment=ex.Message`. `RestartTimerAsync(newPeriod)` = bounded stop → epoch bump → reschedule.

## Bar mechanics

- Window state under `_lockBar`; `Complete()` rounds: double — `Math.Round(v, Precision, AwayFromZero)` on min/max/mean/first/last; int — mean only (`(int)Math.Round(sum/count)` — note: default `MidpointRounding.ToEven`, pinned cross-language by `bar_int_contract.hsmtest`).
- Time alignment (`Extensions/BarTimeExtensions.cs`): `OpenTime = floor(UtcNow.Ticks / period) * period` (UTC-epoch aligned), `CloseTime = OpenTime + BarPeriod`, timer due time = `max(CloseTime - now, 0)`.
- Periods: `BarPeriod` 5 min, `BarTickPeriod` 5 s, `PostDataPeriod` 15 s, `Precision` 2 (0–15); all validated > 0.
- **Flush on stop**: `StopAsync` publishes a non-empty partial bar (`Count > 0`) before the epoch bump and queue drain, rolling only on a confirmed send (`if (TrySendValue()) BuildNewBar()`), so the in-progress bar is not lost at shutdown and a stop→restart→stop cycle never resends it. Sensor disposal goes through the non-flushing `DisposeAsyncCore` path (releasing a handle is not a data point); the data still flushes when the collector itself stops. Conformance: `bar_int_contract.hsmtest` / `bar_double_contract.hsmtest` / `bar_partial_contract.hsmtest` / `bar_rollover_contract.hsmtest`.

### Bar sensor: do not roll the bar without confirming the send happened

Bar sensors have **two** scheduled handles that BOTH publish the current bar through a shared `SendValueAction` codepath protected by an `_sendValueInProgress` reentrancy guard:

- `_sendHandle` runs `SendValueAction` every `PostDataPeriod` (periodic snapshot).
- `_collectHandle` runs `CollectBar` → `CheckCurrentBar`, which on `bar.CloseTime < now` sends the closed bar and then calls `BuildNewBar()` to start the next aggregation window.

If `CheckCurrentBar` rolls the bar **unconditionally** after calling `SendValueAction`, this interleaving silently loses the closed bar's aggregated data:

1. Periodic send enters `SendValueAction`, sets `_sendValueInProgress=1`, but is not yet at the `_lockBar` snapshot step inside `BuildSensorValue → GetValue`.
2. Collect tick acquires `_lockBar`, sees the bar past `CloseTime`, calls `SendValueAction` → guard is held → returns immediately, then `BuildNewBar()` resets `_internalBar`, releases the lock.
3. Periodic send finally takes `_lockBar`, finds the freshly-reset bar (`Count == 0`), returns null. Nothing is sent.

**Invariant**: the roll (whatever resets the aggregator state — `BuildNewBar`, mean/min/max reset, count zeroing) must be conditional on the send actually happening. Two correct shapes:

- **Conditional roll**: `SendValueAction` returns a `bool` (or `TrySendValue`) indicating whether the snapshot was published, and the collect path only rolls on `true`. When `false` is returned (guard held), the roll is deferred — either the periodic send finishes its in-flight snapshot first, or the next collect tick rolls cleanly after the guard releases.
- **Inline snapshot+roll under the lock, no shared guard**: the collect path snapshots the bar and rolls atomically inside the bar lock, then publishes the snapshot outside the lock. The periodic send remains gated only against same-handle reentrancy.

C# reference: `Sensors/MonitoringSensorBaseT.TrySendValue()` returns `bool`; `Sensors/BarSensors/BarMonitoringSensorBase.CheckCurrentBar` uses `if (TrySendValue()) BuildNewBar();`. Regression test: `CheckCurrentBar_defers_roll_when_send_guard_is_held` in `CollectorQueueShutdownTests.cs`.

C++ analogue: the native spike's bar sensors (`src/native/collector_spike/src/hsm_collector.cpp`) use the second shape — `AccumulateBar` snapshots the closed bar and re-inits it atomically under the sensor lock, then publishes outside the lock (also required there to keep the collector→sensor lock order one-way). The shared aggregation math is exercised by the `bar_*_contract.hsmtest` fixtures in both languages; the guard-interleaving scenario itself stays a managed-only regression test.

## Options & path model

- `SensorOptions` common surface: `Description`, `SensorUnit (Unit?)`, `TTLs`, `KeepHistory`, `SelfDestroy`, `EnableForGrafana`, `IsSingletonSensor`, `AggregateData`, `Statistics (None|EMA)`, `DefaultAlertsOptions`, `IsForceUpdate`, `IsPrioritySensor`, `IsComputerSensor`, `SensorLocation (Module|Product)`, `TtlAlerts`.
- Subclasses: `InstantSensorOptions(+Alerts)`, `MonitoringInstantSensorOptions(+PostDataPeriod 15 s)`, `BarSensorOptions(+BarPeriod/BarTickPeriod/Precision/BarAlerts)`, `RateSensorOptions(PostDataPeriod 1 min, Unit=ValueInSecond)`, `FunctionSensorOptions` / `ValuesFunctionSensorOptions(+MaxCacheSize)` — **both override PostDataPeriod to 1 min** (the ms-parameter overloads of `CreateNoParamsFuncSensor` default to 15 s instead), `FileSensorOptions(+DefaultFileName/Extension/MaxFileSizeBytes)`, `EnumSensorOptions(+EnumOptions, AggregateData=true, GenerateEnumOptionsDecription())`.
- Singular conveniences: `SensorOptions.TTL` (maps into `TTLs`) and `TtlAlert` (maps into `TtlAlerts`) — used by the fluent builders.
- `DisplayUnit` (per-options `TDisplayUnit`: `NoDisplayUnit` or `RateDisplayUnit { PerSecond=0 … PerMonth=5 }`) → wire `DisplayUnit (int?)`.
- Special options (`Options/SpecialSensorOptions.cs`): `DiskSensorOptions { TargetPath default C:\, CalibrationRequests default 6, PostDataPeriod 5 min }`, `DiskBarSensorOptions { TargetPath }`, `VersionSensorOptions { Version, StartTime }`, `ServiceSensorOptions { ServiceName, IsHostService default true (→ .module placement), SensorPath }`, `NetworkSensorOptions`, `WindowsInfoSensorOptions { PostDataPeriod default 12 h }`, `CollectorMonitoringInfoOptions`.
- Path: `CalculateSystemPath()` — computer sensor → `ComputerName / Path`; module location → `ComputerName / Module / Path`; product location → `Path`. Prototype paths = `{.computer|.module} / Category / SensorName` (`DefaultPrototype.RevealDefaultPath`).
- `DefaultPrototype.BuildPath`: joins with `/`, drops null/empty/whitespace segments, splits interior `/`, collapses `//` (contract locked by `DefaultPrototypeBuildPathTests`, #1087 E).
- Prototype merge (`DefaultPrototype.Merge`): custom non-null wins for most properties, BUT `Path`/`Type`/`IsComputerSensor`/`ComputerName`/`Module` are always pinned from the prototype, and custom `DefaultAlertsOptions`/`IsPrioritySensor`/`IsForceUpdate` are dropped for default sensors; bar prototypes merge period fields separately.
- Last-value default validation: the construction-time default goes through `ThrowIfUnsupportedValue` — a `null` default (e.g. parameterless `CreateLastValueStringSensor`) throws `ArgumentException`.
- On `InitAsync` every sensor sends its `AddOrUpdateSensorRequest` (registration command) built from options — see `../../api/wire-contract/feature.md`.

## Key Files

| File | Purpose |
|---|---|
| `Sensors/SensorBase.cs`, `SensorBaseT.cs`, `SensorInstantT.cs` | Base classes, validation, SendValue routing |
| `Sensors/MonitoringSensorBaseT.cs` | Timer sensors: epoch, TrySendValue, restart |
| `Sensors/BarSensors/*.cs` | Bar aggregation, roll invariant |
| `Sensors/InstantSensors/*.cs` | Rate, function, file, last-value sensors |
| `Sensors/SensorValuesFactory.cs` | T → SensorValueBase DTO builders |
| `Options/SensorOptions.cs`, `SpecialSensorOptions.cs`, `SensorLocation.cs` | Options hierarchy |
| `Prototypes/Bases/DefaultPrototype.cs` | Merge, BuildPath, RevealDefaultPath |
| `Extensions/SensorValueExtensions.cs`, `BarTimeExtensions.cs` | Validation, comment trim, bar time alignment |

## Known Issues / Limitations

- Rate sensor `_lastComment`/`_lastStatus` are plain field writes (benign race: read only on the timer thread).
