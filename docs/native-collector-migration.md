# Migrating from the C++/CLI wrapper to the native C++ API

The native collector ships a header-only C++ RAII API (`hsm::collector`, `#include
<hsm_collector/hsm_collector.hpp>`) that replaces the C++/CLI wrapper in `src/wrapper/`
(`DataCollector.h`, the `hsm_wrapper::DataCollectorProxy` surface). The native API has no CLR
dependency, links statically, and is consumable via `find_package(hsm_collector)`.

This is a **source-compatibility audit**, not a drop-in: the two APIs are close in spirit but differ
in spelling and in a few intentionally-dropped capabilities. The table below maps every wrapper
surface to its native equivalent.

## Quick comparison

| C++/CLI wrapper (`DataCollectorProxy`) | Native C++ (`hsm::collector::Collector`) | Status |
|---|---|---|
| `DataCollectorProxy(key, address, port, module)` | `Collector(CollectorOptions{...})` | shim — fields move into the options struct |
| `Initialize(config_path, write_debug)` | — | **dropped** — no config-file/CLR bootstrap; configure via `CollectorOptions` |
| `RedirectAssembly()` | — | **dropped** — CLR assembly-binding shim, no native analogue |
| `Start()` / `Stop()` | `Start()` / `Stop()` | unchanged |
| `StartAsync()` / `StopAsync()` | `StartAsync()` / `StopAsync()` (`std::future<void>`) | shim — returns a future instead of `void` |
| — | `TestConnection()` / `Dispose()` / `Status()` | new — exposed from the C ABI |
| — | `AddLifecycleListener(std::function<…>)` | new — portable `ILifecycleListener` |
| — | `SetLogger(std::function<…>)` | new — portable `ICollectorLogger` |
| `CreateBoolSensor(path[, options])` | `CreateBoolSensor(path[, SensorOptions])` | shim — `HSMInstantSensorOptions` → `SensorOptions` |
| `CreateIntSensor` / `CreateDoubleSensor` / `CreateStringSensor` | same names | shim — options type |
| — | `CreateEnumSensor` (+ `EnumOption` table) | new (wrapper had no enum sensor) |
| `CreateLastValue{Bool,Int,Double,String}Sensor` | `CreateLastValue{Bool,Int,Double,String}Sensor` | unchanged spelling |
| `CreateIntBarSensor` / `CreateDoubleBarSensor(timeout, small_period[, precision])` | same names, `BarOptions{bar_period, post_period, precision}` | shim — chrono in a struct |
| `CreateIntRateSensor` / `CreateDoubleRateSensor` | `CreateRateSensor` (double) | **partial** — rate is double-only (matches .NET); the wrapper's int-rate is dropped |
| `CreateNoParamsFuncSensor<T>` / `CreateParamsFuncSensor<T,U>` | `CreateFunctionSensor` / `CreateValuesFunctionSensor` (int) | **partial** — int / int-values only; no templated `T/U` (C ABI is int-only) |
| `SendFileAsync(sensor_path, file_path, …)` | `CreateFileSensor(...)` + `FileSensor::AddContent(string)` | shim — string content, not a disk read |
| `AddServiceStateMonitoring(service_name)` | `AddDefaultSensor(DefaultSensor::ServiceStatus)` | shim — via the default-sensor catalog |
| `InitializeSystemMonitoring` / `…ProcessMonitoring` / `…DiskMonitoring` / `…OsMonitoring` / `…NetworkMonitoring` / `…QueueDiagnostic` / `…CollectorMonitoring` | `AddSystemMonitoringSensors` / `AddProcessMonitoringSensors` / `AddDiskMonitoringSensors` / `AddWindowsInfoMonitoringSensors` / `AddAllNetworkSensors` / `AddAllQueueDiagnosticSensors` / `AddCollectorMonitoringSensors` (or `AddAllComputerSensors` / `AddAllModuleSensors` / `AddAllDefaultSensors`) | shim — group helpers instead of boolean flags |
| `InitializeProductVersion(version)` | `AddAllModuleSensors(product_version)` / `AddDefaultSensor(DefaultSensor::ProductVersion, …)` | shim |
| `Initialize*TimeInGC(...)` (time-in-GC) | — | **dropped** (#1099) — no managed GC in a native host |

## Errors

The wrapper surfaced failures inconsistently (CLR exceptions / return values). The native API is
uniform: **every failing call throws `hsm::collector::Error`** (a `std::runtime_error`). Wrap calls
in `try { … } catch (const hsm::collector::Error& ex) { … }`.

## Alerts

The wrapper attached alert templates through `HSM(Instant|Bar)AlertTemplate` on the options object.
The native API uses a fluent `AlertBuilder` obtained from `Collector::CreateAlert(kind)` and attached
with `Sensor::AttachAlert(...)` BEFORE the sensor's registration is emitted:

```cpp
auto alert = collector.CreateAlert(hc::AlertKind::Instant)
    .If(hc::AlertProperty::Value, hc::AlertOperation::GreaterThan, "100")
    .ThenNotify("[$product]$path $operation $target")
    .WithIcon(hc::AlertIcon::Warning);
sensor.AttachAlert(alert);
```

## Intentionally dropped (and why)

| Capability | Reason |
|---|---|
| Templated function sensors (`T/U`) | The C ABI exposes int / int-values function sensors only; supporting arbitrary `T` would be ABI growth, out of scope for the developer layer. |
| Int rate sensors | .NET rate is double-only; the wrapper's int-rate was a wrapper-local convenience. |
| `SendFileAsync` disk reads | Reading a file from disk is platform/IO policy, not part of the portable wire contract; publish string content instead. |
| `RedirectAssembly` / `Initialize(config_path)` | CLR-hosting concerns with no native analogue. |
| Time-in-GC sensors | No managed GC in a native host (#1099). |

## Building against the native API

```cmake
find_package(hsm_collector CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE hsm_collector::hsm_collector_cpp)
```

See `src/native/collector/examples/console/` for a complete example (and its `standalone/`
subproject for the `find_package` consume path). API reference: `docs/native-collector/Doxyfile`
(generated HTML in the CI `doxygen` lane). The C ABI itself is documented in
`docs/native-collector-c-abi.md`.
