# Migrating from the C++/CLI wrapper to the native C++ API

The native collector ships a header-only C++ RAII API (`hsm::collector`, `#include
<hsm_collector/hsm_collector.hpp>`) that replaces the C++/CLI wrapper in `src/wrapper/`
(`DataCollector.h`, the `hsm_wrapper::DataCollectorProxy` surface). The native API has no CLR
dependency, links statically, and is consumable via `find_package(hsm_collector)`.

There are two ways to leave the CLR behind:

1. **Relink the native wrapper DLL (drop-in, zero source change).** `src/wrapper/` is no longer
   C++/CLI — its implementation has been reimplemented over the native `hsm::collector` API while the
   public headers (`include/DataCollector.h`, `hsm_wrapper::DataCollectorProxy`, the sensor/options/
   alert DSL) are **unchanged**. The artifact is a pure-native `HSMCppWrapper.dll` (built by
   `src/wrapper/CMakeLists.txt`, statically bundling the collector core; libcurl HTTP transport).
   Consumers (e.g. tt-aggregator2) **relink against the new DLL and recompile nothing of their own** —
   no CLR is loaded. The behavioral table below is exactly what that native backend does (e.g. function
   sensors are int-only, time-in-GC is gone), so it doubles as the "what changed under the hood" list.
2. **Port to the native API directly.** Drop the wrapper and call `hsm::collector` yourself — the
   spellings differ (see the table) but you gain the new surface (enum sensors, lifecycle listeners,
   `find_package` packaging). This is the **source-compatibility audit** for that path.

The table below maps every wrapper surface to its native equivalent; the **Status** column applies
to both paths (a "dropped"/"partial" row is a capability the native backend — and therefore the
relinked DLL — no longer provides).

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
| `CreateIntBarSensor` / `CreateDoubleBarSensor(timeout, small_period[, precision])` | same names, `BarOptions{bar_period, post_period, precision, + full SensorOptions surface}` | shim — `BarOptions` now carries the full registration surface (TTL/unit/description/keep-history/self-destroy/statistics/singleton/aggregate/grafana/computer/location/`default_alert_options`) via `hsm_collector_create_*_bar_sensor_with_options`; bars register `DisplayUnit:null` |
| `HSMDefaultAlertsOptions` (`DisableTtl`/`DisableStatusChange`) on options | `SensorOptions` / `RateOptions` / `BarOptions::default_alert_options` (`DefaultAlertsOptions`) | shim — instant + rate + bar; the server attaches its default TTL + status-change alerts, these flags register them disabled |
| `CreateIntRateSensor` / `CreateDoubleRateSensor` | `CreateRateSensor` (double) | **partial** — rate is double-only (matches .NET); the wrapper's int-rate is dropped |
| `CreateNoParamsFuncSensor<T>` / `CreateParamsFuncSensor<T,U>` | `CreateFunctionSensor` / `CreateValuesFunctionSensor` (int) | **partial** — int / int-values only; no templated `T/U` (C ABI is int-only) |
| `SendFileAsync(sensor_path, file_path, …)` | `CreateFileSensor(...)` + `FileSensor::SendFile(path)` (or `AddContent(string)`) | shim — `SendFile` reads the file and derives `Name`/`Extension` from the path (host I/O, 10 MiB cap, UTF-8 text); `AddContent` publishes raw string content |
| `AddServiceStateMonitoring(service_name)` | `EnableServiceStatusMonitoring(service_name, scan_period)` | shim — registers `.module/Service status` and starts a live SCM poller (Win32 `OpenService`/`QueryServiceStatus`, Windows only) that posts the service's `ServiceControllerStatus` on change; registration-only via `AddDefaultSensor(DefaultSensor::ServiceStatus)` |
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
| `RedirectAssembly` / `Initialize(config_path)` | CLR-hosting concerns with no native analogue. |
| Time-in-GC sensors | No managed GC in a native host (#1099). |

## Native wrapper DLL — behavioral notes (relink path)

The relinked `HSMCppWrapper.dll` preserves the public ABI but a few runtime behaviors follow the
native backend rather than the old managed one:

- **Monitoring-init boolean sub-flags are ignored.** `InitializeSystemMonitoring(is_cpu, …)` etc. map
  to the native group helper (`AddSystemMonitoringSensors`, …), which registers the standard catalog
  for the group; individual sub-sensors can't be toggled off. The aggregator passes the defaults
  (everything on), so this is a no-op in practice.
- **Function-sensor `RestartTimer`/`GetInterval` are best-effort.** The native function sensor's post
  period is fixed at creation; `RestartTimer` records the requested interval (so `GetInterval`
  reflects it) but does not re-arm the underlying timer. Function sensors are int / int-values only.
- **Values-function buffer is a bounded sliding window** (100 000 entries) vs the managed unbounded
  buffer — sized for the aggregator's per-window accumulators.
- **`SendFileAsync` reuses one file sensor per sensor path** (cached), creating it on first send.
- **Last-value / function-sensor descriptions are dropped** — the native `CreateLastValue*` /
  `CreateFunctionSensor` factories take no description argument.
- **`Initialize(config_path, write_debug)` and `RedirectAssembly()` are no-ops** — kept for source
  compatibility; the collector is configured entirely through the constructor.

## Building against the native API

Whichever package manager you use, the CMake usage is identical:

```cmake
find_package(hsm_collector CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE hsm_collector::hsm_collector_cpp)
```

Consume the package one of three ways:

- **CMake `find_package`** — `cmake --install` the collector to a prefix, then point
  `CMAKE_PREFIX_PATH` at it.
- **Conan** — `conan create src/native/collector` (or add `hsm-collector/0.4.0` to your
  `conanfile`), with `-o hsm-collector/*:http=True` for the libcurl transport.
- **vcpkg** — add the overlay port: `vcpkg install hsm-collector
  --overlay-ports=src/native/collector/vcpkg-port` (feature `http` for curl), then build with the
  vcpkg toolchain. For a registry, replace the port's `SOURCE_PATH` with a `vcpkg_from_github` block
  at a release tag.

See `src/native/collector/examples/console/` for a complete example (and its `standalone/`
subproject for the `find_package` consume path). API reference: `docs/native-collector/Doxyfile`
(generated HTML in the CI `doxygen` lane). The C ABI itself is documented in
`docs/native-collector-c-abi.md`.
