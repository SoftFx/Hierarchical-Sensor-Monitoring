# Feature: Collector Public API

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - construction, options, lifecycle methods, and the full sensor-creation surface integrators program against.

---

## Overview

Everything a host application touches: `DataCollector` construction, `CollectorOptions`, Start/Stop/Dispose, sensor `Create*` factories, fluent builders, logging hookup. Breaking changes here affect every integrator (.NET consumers, the C++/CLI wrapper, and the future native port — see `docs/initiatives/cpp-collector-port-functional-inventory.md`).

## Construction & configuration

- `DataCollector(CollectorOptions)` — validates options immediately (`Validate()` throws).
- `DataCollector(productKey, address = "localhost", port = 44330, clientName = null)` — convenience wrapper.
- `TestConnection()` → `ConnectionResult { Code, Error, IsOk }`; callable in any lifecycle state.
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

## Lifecycle API

Lifecycle state machine, gates, registration phases, event ordering, and the dispose-vs-stop race are documented in [`../overview.md`](../overview.md) (sections "Lifecycle", "Sensor registration", "Data gating", "Dispose racing Stop"). Public surface:

- `Task Start()` / `Start(Task customStartingTask)` — idempotent; custom task runs between processor start and sensor init; failure rolls back to Stopped.
- `Task Stop()` / `Stop(Task customStoppingTask)` — idempotent; awaits dynamic sensor-start tasks; custom-task failure logged, stop proceeds.
- `Dispose()` — idempotent, terminal, never throws; joins an in-flight Stop and wins the shutdown-mode choice (`TerminalDispose`).
- Events `ToStarting/ToRunning/ToStopping/ToStopped` + portable `ILifecycleListener` via `AddLifecycleListener(...)`.
- Properties: `Status`, `ComputerName`, `Module`, `Windows` (IWindowsCollection), `Unix` (IUnixCollection).
- Logging: `AddNLog(LoggerOptions)` (embedded `collector.nlog.config` fallback; `ConfigPath`, `WriteDebug`), `AddCustomLogger(ICollectorLogger)`. Fluent, chainable, callable pre/post Start.

## Sensor creation surface

All factories live on `IDataCollector`/`DataCollector` (`Core/DataCollector.cs`); registration semantics (dedup by path, MaxSensors cap, lifecycle gating) in [`../overview.md`](../overview.md).

| Kind | Factories | Returns |
|---|---|---|
| Instant | `Create{Bool,Int,Double,String,Version,Time}Sensor(path, description="")` + `(path, InstantSensorOptions)` | `IInstantValueSensor<T>` |
| Enum | `CreateEnumSensor(path, description \| EnumSensorOptions)` | `IInstantValueSensor<int>` |
| Last-value | `CreateLastValue{Bool,Int,Double,String,Version,TimeSpan}Sensor(path, defaultValue, description)`; generic `CreateLastValueSensor<T>(path, options, defaultValue)` | `ILastValueSensor<T>` |
| Rate | `CreateRateSensor(path, RateSensorOptions)`, `CreateM1RateSensor`, `CreateM5RateSensor` | `IMonitoringRateSensor` |
| Bar (int) | `CreateIntBarSensor(path, barPeriod=300000 ms, postPeriod=15000 ms, descr)` + options overload + `Create{1Hr,30Min,10Min,5Min,1Min}IntBarSensor` | `IBarSensor<int>` |
| Bar (double) | same + `precision=2` parameter | `IBarSensor<double>` |
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
- `IBaseFuncSensor` (obsolete location, still supported): `GetInterval()`, `RestartTimer(TimeSpan)`; params variant adds `AddValue(U)`.

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
| `PublicAPI/SensorsAPI/*.cs` | Sensor interfaces |
| `PublicAPI/IWindowsCollection.cs`, `IUnixCollection.cs` | Default-sensor registration surface (see `default-sensors/`) |
| `Core/IDataSender.cs` | Transport seam |

## Dependencies

- Depends on: `sensors/`, `data-pipeline/`, `default-sensors/`, `scheduling/`
- Used by: integrators, `src/wrapper` (C++/CLI), native port (planned)

## Known Issues / Limitations

- The obsolete `Initialize*` family still ships; it blocks synchronously on Start.
