# Feature: Collector Public API

> Owner: collector | Last reviewed: 2026-09-30 | Canonical: yes
> Scope: Collector - construction, options, lifecycle methods, and the full sensor-creation surface integrators program against.

---

## Overview

Everything a host application touches: `DataCollector` construction, `CollectorOptions`, Start/Stop/Dispose, sensor `Create*` factories, fluent builders, logging hookup. Breaking changes here affect every integrator (.NET consumers, the C++/CLI wrapper, and the future native port — see `docs/initiatives/cpp-collector-port-functional-inventory.md`).

## Construction & configuration

- `DataCollector(CollectorOptions)` — validates options immediately (`Validate()` throws).
- `DataCollector(productKey, address = "localhost", port = 44330, clientName = null)` — convenience wrapper.
- `TestConnection()` → `ConnectionResult { Code, Error, IsOk, Result (= Error empty), static Ok }`; callable in any lifecycle state.
- `IDataSender` (`Core/IDataSender.cs`) is the transport seam: `TestConnectionAsync`, `SendDataAsync`, `SendPriorityDataAsync`, `SendCommandAsync`, `SendFileAsync`, `Dispose`. Tests and embedders substitute it via `CollectorOptions.DataSender`.

`CollectorOptions` (defaults + validation):

| Option | Type | Default | Validation |
|---|---|---|---|
| `AccessKey` | string | — | required, non-whitespace |
| `ServerAddress` | string | `"localhost"` | required, non-whitespace |
| `Port` | int | `44330` | 1..65535 |
| `ClientName` | string | null | — |
| `ComputerName` / `Module` | string | null | — (path hierarchy, see sensors/options docs) |
| `MaxQueueSize` | int | `20000` | > 0, per queue |
| `MaxValuesInPackage` | int | `1000` | > 0 |
| `PackageCollectPeriod` | TimeSpan | 15 s | > 0 |
| `RequestTimeout` | TimeSpan | 30 s | > 0 |
| `DataSender` | IDataSender | `HsmHttpsClient` | — |
| `AllowUntrustedServerCertificate` | bool | false | — |
| `AllowPlaintextTransport` | bool | false | — |
| `ExceptionDeduplicatorWindow` | TimeSpan | 1 h | >= 0 (zero = log immediately) |
| `MaxDeduplicatedMessages` | int | `1000` | > 0 |
| `MaxSensors` | int | `100000` | > 0 |

**Native port (C++)**: `hsm_collector_options_t` (`src/native/collector`, #1095) mirrors this table field-for-field (`0` = managed default per field; the dedup window's `0` = log immediately). The C ABI also mirrors the lifecycle surface — status state machine, `dispose`, `TestConnection`, lifecycle listeners, `MaxSensors` cap — and a pluggable log sink. The `DataSender` seam is not exposed until the HTTP transport (#1096). Contract: [`docs/native-collector-c-abi.md`](../../../../docs/native-collector-c-abi.md).

## Lifecycle API

Lifecycle state machine, gates, registration phases, event ordering, and the dispose-vs-stop race are documented in [`../overview.md`](../overview.md) (sections "Lifecycle", "Sensor registration", "Data gating", "Dispose racing Stop"). Public surface:

- `Task Start()` / `Start(Task customStartingTask)` — idempotent; custom task runs between processor start and sensor init; failure rolls back to Stopped.
- `Task Stop()` / `Stop(Task customStoppingTask)` — idempotent; awaits dynamic sensor-start tasks; custom-task failure logged, stop proceeds.
- `Dispose()` — idempotent, terminal, never throws; joins an in-flight Stop and wins the shutdown-mode choice (`TerminalDispose`).
- Events `ToStarting/ToRunning/ToStopping/ToStopped` + portable `ILifecycleListener` via `AddLifecycleListener(...)`.
- Properties: `Status`, `ComputerName`, `Module`, `Windows` (IWindowsCollection), `Unix` (IUnixCollection), `DefaultSensors` (`IEnumerable<ISensor>` — public `ISensor` contract: `SensorPath`, `InitAsync`, `StartAsync`, `StopAsync`, `IDisposable`; `SensorBase` additionally exposes `SendValue(SensorValueBase)` and the `ExceptionThrowing` event). `IWindowsCollection`/`IUnixCollection` are themselves `IDisposable`.
- Logging: `AddNLog(LoggerOptions)` (embedded `collector.nlog.config` fallback; `ConfigPath`, `WriteDebug`), `AddCustomLogger(ICollectorLogger)`. Fluent, chainable, callable pre/post Start.
- Path validation: every `Create*` throws `ArgumentException` for null/whitespace/slash-only paths (`SensorsStorage`).

## Sensor creation surface

All factories live on `IDataCollector`/`DataCollector` (`Core/DataCollector.cs`); registration semantics (dedup by path, MaxSensors cap, lifecycle gating) in [`../overview.md`](../overview.md).

| Kind | Factories | Returns |
|---|---|---|
| Instant | `Create{Bool,Int,Double,String,Version,Time}Sensor(path, description="")` + `(path, InstantSensorOptions)` | `IInstantValueSensor<T>` |
| Enum | `CreateEnumSensor(path, description \| EnumSensorOptions)` | `IInstantValueSensor<int>` |
| Last-value | `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)`; generic `CreateLastValueSensor<T>(path, options, defaultValue)` | `ILastValueSensor<T>` |
| Rate | `CreateRateSensor(path, RateSensorOptions)`, `CreateM1RateSensor`, `CreateM5RateSensor` | `IMonitoringRateSensor` |
| Bar (int) | `CreateIntBarSensor(path, barPeriod=300000 ms, postPeriod=15000 ms, descr)` + options overload + `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor` + DataCollector-only TimeSpan overload `(path, TimeSpan barPeriod, TimeSpan postPeriod, descr)` | `IBarSensor<int>` |
| Bar (double) | same (+`precision=2` parameter, TimeSpan overload included) | `IBarSensor<double>` |
| File | `CreateFileSensor(path, fileName, extension="txt", descr)` / `(path, FileSensorOptions)`; collector-level `SendFileAsync(sensorPath, filePath, status, comment)` | `IFileSensor` |
| Function (no params) | `CreateNoParamsFuncSensor<T>(path, descr, Func<T>, interval)`, `Create{1Min,5Min}NoParamsFuncSensor`, `CreateFunctionSensor<T>(path, func, options)` | `INoParamsFuncSensor<T>` |
| Function (params) | `CreateParamsFuncSensor<T,U>(path, descr, Func<List<U>,T>, interval)`, `Create{1Min,5Min}ParamsFuncSensor`, `CreateValuesFunctionSensor<T,U>` | `IParamsFuncSensor<T,U>` |
| Service commands | `CreateServiceCommandsSensor()` | `IServiceCommandsSensor` |

Sensor interfaces (`PublicAPI/SensorsAPI/*`):

- `IInstantValueSensor<T>`: `AddValue(value)`, `AddValue(value, comment)`, `AddValue(value, status, comment)`.
- `ILastValueSensor<T>` extends instant; holds latest value, sends once on stop.
- `IBarSensor<T>`: `AddValue`, `AddValues(IEnumerable<T>)`, `AddPartial(min, max, mean, first, last, count)`.
- `IFileSensor`: instant-string surface + `Task<bool> SendFile(filePath, status, comment)`.
- `IMonitoringRateSensor`: instant-double surface.
- `IServiceCommandsSensor`: `SendCustomCommand(command, initiator)`, `SendUpdate(initiator[, newVersion[, oldVersion]])`, `SendRestart/SendStart/SendStop(initiator)`.
- `IBaseFuncSensor` (lives in the `Obsolete` folder but is NOT `[Obsolete]` — it is the current return type of `CreateFunctionSensor`/`CreateValuesFunctionSensor`): `GetInterval()`, `RestartTimer(TimeSpan)`, `GetFunc()`; params variant adds `AddValue(U)`.
- `ILastValueSensor` creation gotcha: `CreateLastValueStringSensor(path)` / `CreateLastValueVersionSensor(path)` with the implicit `null` default **throw `ArgumentException` at creation** (`ThrowIfUnsupportedValue(customDefault)`); pass a non-null default.

### Changing a sensor's description after creation (#1482)

`bool sensor.SetDescription(string description)` — the managed counterpart of the native `hsm_sensor_set_description` (collector 0.10.0), for a host whose description carries live facts.

- **Shape (additive only).** A separate capability interface `IDescribableSensor { bool SetDescription(string) }` (`PublicAPI/SensorsAPI/IDescribableSensor.cs`), implemented by `SensorBase<TDisplayUnit>` and therefore by every sensor the collector creates, plus `SetDescription` extension methods in `HSMDataCollector.Core.SensorDescriptionExtensions` on `IInstantValueSensor<T>` (covers last-value, rate, file), `IBarSensor<T>`, `IServiceCommandsSensor` and `IBaseFuncSensor`. No member was added to the sensor interfaces or to `ISensor`, so external implementations of them (test doubles, adapters) keep compiling on both TFMs — the same pattern as `ICollectorRegistrationState`/`ILifecycleObservableCollector`. The extensions live in `HSMDataCollector.Core` so a caller that constructs the collector already has them in scope. A `null` handle or a foreign implementation without the capability returns `false`.
- **When the text reaches the server** (mirrors native `NativeCollector::OnRegistrationChanged`, gated like it on Starting/Running = `CanStartNewSensors`): before Start and while Stopped the options keep the new text and the next Start's `InitAsync` registers it (no extra AddOrUpdate); while Starting/Running an AddOrUpdate built from the options (all other fields unchanged) is queued on the command queue at once; while Stopping or after Dispose nothing is sent (the call still returns `true`). Every call re-registers while running, also when the text is unchanged — native bumps its registration version on every call, so its HTTP transport re-posts too.
- **Only a sensor the collector holds (#1503 review).** `SensorsStorage` marks the instance it adds (`ICollectorOwnedSensor.MarkOwned`) and unmarks it on `TryRemove`. A sensor it does not hold — rejected at registration while the collector was Stopping (`Register` disposes it and returns it inert) or removed (`DefaultSensorsCollection.Unregister`, e.g. `UnsubscribeWindowsServiceStatus`) — is never registered again, so a description change would put a sensor on the server that no value ever reaches: the call changes nothing, sends nothing and returns `false`. Native cannot reach that state: it returns no handle for a rejected sensor (`hsm_sensor_set_description(NULL)` → `HSM_RESULT_INVALID_ARGUMENT`) and has no removal path. Disposing a handle does NOT remove the sensor (it stays in storage and the next Start registers it again, as native `hsm_sensor_release` frees only the handle), so a disposed handle still takes a new description.
- **Text.** Passed through as given: `null` → `"Description":null`, which the server reads as "unchanged" (so it only clears a description the server has not received yet); `""` clears it on the server.
- **Threading / isolation.** Callable from any thread. A per-sensor lock (`SensorBase._registrationLock`; under it only the lifecycle-state read and the command enqueue run, never the collector's lifecycle gate) covers the description write and the build+enqueue of the AddOrUpdate, and `InitAsync` builds its registration under the same lock, so concurrent calls enqueue in the order they changed the text and the last registration queued always carries the sensor's final text; a call racing Start is covered either by Start's registration or by its own re-registration. An exception is routed to `HandleException` (collector errors) and the call returns `false`; nothing escapes to the host.
- **Recorded difference from native (by design).** Native's in-memory recorded registration list replaces the run's entry in place; managed simply sends one more AddOrUpdate. On the wire both re-post once per change. A sensor created while running and re-described before its own registration went out may register twice in managed (both carrying the new text); native coalesces those into one post.
- Tests: `SensorDescriptionTests` (every lifecycle state, null/empty, every sensor kind, API shape, concurrency), `FakeServerE2ETests.DescriptionChangedWhileRunning_ReRegistersSensorOnTheWire` (real HTTP stack, net8 → the net6.0 build), corpus `registration_contract:set_description_*` (both drivers).

Fluent builders (`Core/Builders/SensorBuilders.cs`, extension methods — `IDataCollector` unchanged):

- `collector.InstantSensor<T>(path)` / `BarSensor<T>(path)` / `RateSensor(path)` with `.Description() .Ttl() .KeepHistory() .Priority() .BarPeriod() .PostPeriod() .TickPeriod() .Precision() .Configure(opts => ...)` → `.Build()` dispatches to the options-based factory.

## Obsolete surface (kept for compat, do not extend)

`Initialize()` overloads (sync-block on Start), `InitializeSystemMonitoring` / `InitializeProcessMonitoring` / `InitializeOsMonitoring` / `MonitorServiceAlive` / `InitializeWindowsUpdateMonitoring` (superseded by `Windows`/`Unix` collections), `ValuesQueueOverflow` event (never fires). New ports should not reproduce these.

## Key Files

| File | Purpose |
|---|---|
| `Core/DataCollector.cs` | Construction, lifecycle, all Create* factories |
| `Core/IDataCollector.cs` | Public interface |
| `Options/CollectorOptions.cs` | Options + `Validate()` |
| `Core/Builders/SensorBuilders.cs` | Fluent builders |
| `PublicAPI/SensorsAPI/*.cs` | Sensor interfaces, `IDescribableSensor` capability |
| `Core/SensorDescriptionExtensions.cs` | `SetDescription` extensions on the sensor handles (#1482) |
| `PublicAPI/IWindowsCollection.cs`, `IUnixCollection.cs` | Default-sensor registration surface (see `default-sensors/`) |
| `Core/IDataSender.cs` | Transport seam |

## Dependencies

- Depends on: `sensors/`, `data-pipeline/`, `default-sensors/`, `scheduling/`
- Used by: integrators, `src/wrapper` (C++/CLI), native port (planned)

## Known Issues / Limitations

- The obsolete `Initialize*` family still ships; it blocks synchronously on Start.
