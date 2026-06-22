# Feature: Native Collector C++ API

> Owner: integrations | Last reviewed: 2026-06-23 | Canonical: yes
> Scope: The public, developer-facing C++ RAII API over the native collector C ABI, plus its `find_package` packaging, console example, and migration story from the C++/CLI wrapper.

---

## Description

The native collector (epic #1093) is layered: a C++ core, a stable **C ABI** (`include/hsm_collector/hsm_collector.h`), and — this feature — a header-only **C++ RAII wrapper** (`include/hsm_collector/*.hpp`, `namespace hsm::collector`) that gives application code a modern, exception-based, strongly-typed surface without touching raw C handles. It is the C++ analogue of the .NET `HSMDataCollector` API and the successor to the C++/CLI wrapper in `src/wrapper/` (`DataCollector.h`).

The wrapper is a **thin convenience layer**: it adds no wire behavior. Every registration/value payload it produces is byte-identical to the raw C ABI, pinned by the `native_wrapper_*` twin tests (wrapper sensor vs raw-ABI sensor → identical `hsm_sensor_test_wire_registration_json`). All wire/parity contracts live in `collector/` features; this feature owns only the ergonomics, lifetime management, and distribution.

**Error strategy (resolved #1100):** exceptions. Every failing call throws `hsm::collector::Error` (carrying the collector's `last_error` for collector-scoped calls, or a static message + C result-code name for sensor-scoped calls). This matches the .NET wrapper's throwing style.

---

## Invariants

- **No wire distortion.** A sensor created/posted through the wrapper produces the exact same bytes as the equivalent raw C ABI call. Enforced by `native_wrapper_registration_matches_abi` / `native_wrapper_alert_builder_matches_abi`.
- **RAII ownership.** `Collector` and every `Sensor` are move-only; the destructor frees the underlying handle (`hsm_collector_destroy` / `hsm_sensor_release`). Releasing a sensor handle does NOT unregister it — the collector keeps it.
- **Callback lifetime (the trampoline rule).** `std::function` callbacks (lifecycle listener, logger, function-sensor body, metric-source factory) are heap-allocated and owned by the `Collector`; their address is passed to the C ABI as `void* user_data`, which **must outlive the collector**. The `Collector` destructor calls `hsm_collector_destroy` BEFORE the `std::function` storage is freed, and `std::unique_ptr` keeps each callable at a stable address across `Collector` moves.
- **Attach alerts before registration is emitted** (pre-Start, or pre-create while the collector is already running) — attaching rebuilds the payload.
- **Threading.** Drive `Start`/`Stop` from one thread (or serialize externally). Callbacks fire on the scheduler/lifecycle thread and must not call a lifecycle method (`Start`/`Stop`/`Dispose`) from within.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Build a `Collector` from `CollectorOptions`, register sensors, `Start`/post/`Stop` | collector integrator |
| 2 | Attach a threshold/TTL alert via the fluent `AlertBuilder` | collector integrator |
| 3 | Register the built-in catalog via `AddAll*` / `AddDefaultSensor` | collector integrator |
| 4 | Consume the installed package from a clean machine via `find_package(hsm_collector)` | downstream build |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `hsm::collector::Collector` + `CollectorOptions` | `include/hsm_collector/collector.hpp` | lifecycle (`Start`/`Stop`/`StartAsync`/`StopAsync`/`TestConnection`/`Dispose`/`Status`), observers, every sensor factory, default-sensor surface, introspection |
| RAII sensor types | `include/hsm_collector/sensors.hpp` | `Bool/Int/Double/String/Enum/TimeSpan/Version`, `IntBar/DoubleBar`, `Rate`, `Function`/`ValuesFunction`, `File`, `ServiceCommands` |
| `SensorOptions` / `BarOptions` / `RateOptions` / `EnumOption` | `include/hsm_collector/options.hpp` | chrono/optional builders lowering to `hsm_sensor_options_t` |
| `AlertBuilder` / `Alert` | `include/hsm_collector/alerts.hpp` | fluent `If/IfLastValue/ThenNotify/ThenScheduledNotify/WithIcon/AsSensorError/WithConfirmationPeriod/WithInactivityPeriod` |
| `DefaultSensor` / `DefaultSensorParams` | `include/hsm_collector/default_sensors.hpp` | mirror of `hsm_default_sensor_t` |
| `IMetricSource` / `MetricSourceFactory` | `include/hsm_collector/detail/callbacks.hpp` | the `IPerformanceCounter`-equivalent seam (live readers are the #1099 follow-up) |
| `find_package(hsm_collector)` package | `cmake/hsm_collectorConfig.cmake.in`, `CMakeLists.txt` | exports `hsm_collector::hsm_collector_core` + `hsm_collector::hsm_collector_cpp` |

## Key Files

| File | Purpose |
|---|---|
| `src/native/collector/include/hsm_collector/hsm_collector.hpp` | umbrella header (include this) |
| `src/native/collector/include/hsm_collector/*.hpp` | error/enums/options/alerts/sensors/collector/default_sensors split |
| `src/native/collector/include/hsm_collector/detail/callbacks.hpp` | std::function → C-callback trampolines + `IMetricSource` |
| `src/native/collector/examples/console/main.cpp` | console example (WrapperConsole equivalent, in-memory sender) |
| `src/native/collector/examples/server-monitor/main.cpp` | live example — `UseHttpTransport()` streams custom sensors to a real server (#1165, HTTP build) |
| `src/native/collector/examples/console/standalone/CMakeLists.txt` | clean-machine `find_package` consumer |
| `src/native/collector/cmake/hsm_collectorConfig.cmake.in` | package config template |
| `src/native/collector/conanfile.py` + `test_package/` | Conan recipe + clean-consumer test |
| `src/native/collector/vcpkg-port/` | vcpkg overlay port (portfile + manifest + copyright) |
| `docs/native-collector-migration.md` | source-compat audit vs the C++/CLI wrapper |
| `docs/native-collector/Doxyfile` | API-reference generation (CI doxygen lane) |

## Integration guide

- **Live server transport (#1165).** `Collector::UseHttpTransport()` (C ABI `hsm_collector_use_http_transport`) switches the collector from the in-memory recording sender to a real libcurl POST. Call it BEFORE `Start()`. It (a) registers every sensor on the server — a wire `AddOrUpdate` batch POSTed to `/commands` at Start — and (b) streams values to `/list` in the **.NET server wire format** (`Time` ISO-8601, bar `OpenTime`/`CloseTime`). It throws / returns `HSM_RESULT_INVALID_STATE` if the library was built without `HSM_COLLECTOR_HTTP`. The conformance corpus stays on the canonical/in-memory recorder (`send_wire_` is false there), so wire vs canonical never cross. End-to-end proven against a Dockerized HSM server by `examples/server-monitor`.
- **Live default-sensor values + value-source plugin (#1164).** `Collector::InstallWindowsMetricSources()` (C ABI `hsm_collector_install_windows_metric_sources`) installs a ready-made Windows PDH/Win32 factory so the value-typed default sensors (Total CPU, Free RAM, disk gauges, free disk, process, TCP connections) read live each post period. The seam is also a **plugin point**: `SetMetricSourceFactory` + an `IMetricSource` (`Read() → std::optional<double>`) supplies any custom source — `Collector::CreateMetricSensor(path, period)` makes a Double sensor the factory drives. `examples/windows-monitor` streams live host metrics; `examples/crypto-monitor` is a libcurl BTCUSD plugin — both proven against a real server. Still follow-up: non-double default sensors (WMI/registry/EventLog/prediction/ThreadPool/network deltas) and Linux procfs readers.
- **TLS / transport.** `CollectorOptions::allow_untrusted_server_certificate` and `allow_plaintext_transport` map to the C options of the same name; they take effect once `UseHttpTransport()` installs the libcurl transport (`HSM_COLLECTOR_HTTP` build). Default builds use the in-memory recording sender (no network), which is what lets the console example and tests run with no server.
- **Logger hookup.** `SetLogger(std::function<void(LogLevel, const std::string&)>)`. Errors pass through the deduplicator (`exception_deduplicator_window_ms` / `max_deduplicated_messages`); a window of 0 logs every message. A throwing logger is swallowed.
- **Threading expectations.** `Start`/`Stop` single-threaded; sensor `AddValue` is thread-safe; callbacks run on the scheduler thread and must not re-enter a lifecycle method. `StartAsync`/`StopAsync` wrap the sync calls in `std::async` (note `std::future`'s destructor blocks until the task completes).
- **Shutdown semantics.** `Stop` is bounded (it will not hang on a dead transport — pending data is dropped rather than blocking host restart). `Dispose` is terminal and idempotent; after it, `Status()` is `Disposed` and further lifecycle/registration calls are rejected.

## Packaging & versioning

Three consumption paths, all built on the one set of CMake `install()` rules; the package version tracks the C ABI semver (`HSM_COLLECTOR_VERSION`, currently 0.4.0):

- **CMake `find_package`** (baseline). `cmake --install` emits the headers, the static core lib, and `hsm_collectorConfig.cmake` + version file + exported namespaced targets (`hsm_collector::hsm_collector_core` / `::hsm_collector_cpp`). `find_package(hsm_collector 0.4)` version checks work. Proven by the CI **install-consume** lane (build → install → standalone `find_package` build+run, Win+Linux).
- **Conan** (`conanfile.py` + `test_package/`). `conan create` builds from source with tests/examples gated off; Conan owns the CMake integration (the project's own config is dropped in `package()`, components re-declared). An optional `http` Conan option pulls `libcurl`. Proven by the **conan-consume** lane (`conan create` runs the `test_package` consumer).
- **vcpkg** (overlay port `vcpkg-port/`). `portfile.cmake` + manifest build the library from the adjacent source (`SOURCE_PATH=../`), with an `http` feature for curl; for registry publication the `SOURCE_PATH` line is swapped for a `vcpkg_from_github` block at a release tag. Proven by the **vcpkg-consume** lane (install the overlay port → build+run a consumer through the vcpkg toolchain, Win+Linux).

CMake gates: `HSM_COLLECTOR_INSTALL`, `HSM_COLLECTOR_BUILD_EXAMPLES`, `HSM_COLLECTOR_BUILD_TESTS` (each defaults to `PROJECT_IS_TOP_LEVEL`), and `HSM_COLLECTOR_HTTP`. Package builds set tests/examples OFF.

**NuGet native assets** were intentionally **dropped** — there is no concrete mixed managed/native consumer to justify the `.nuspec` + native-asset layout; .NET consumers use the managed `HSMDataCollector` package, native consumers use one of the three paths above.

## Divergences from the C++/CLI wrapper (`DataCollector.h`)

Tracked in full in `docs/native-collector-migration.md`. Headlines:

- **Function sensors are int / int-values only** — the C ABI is int-only; the .NET wrapper's templated `T/U` function sensors are not reproduced (would require ABI growth).
- **`SendFileAsync(disk path)` → `FileSensor::AddContent(string)`** — disk reads are not part of the portable contract.
- **No `RedirectAssembly` / `Initialize(config_path)`** — those are CLR-hosting concerns with no native analogue.
- **Time-in-GC sensors dropped** (#1099) — no managed GC in a native host.

## Tests

- `native_wrapper_registration_matches_abi` — twin byte-equality across every sensor type.
- `native_wrapper_alert_builder_matches_abi` — the fluent builder lowers identically to the raw alert ABI.
- `native_wrapper_value_path_posts_through` — the value-add path forwards 1:1.
- `native_wrapper_lifetime_move_semantics` — move + Dispose correctness.
- `native_wrapper_callbacks_bridge_std_function` — lifecycle listener, logger, function sensor, and metric-source factory all fire through their trampolines.
- CI `install-consume` (Win+Linux) and `doxygen` lanes in `native-collector-conformance.yml`.
- Parity/process lanes (#1101): the per-PR `checklist-gate` job (strict checklist-disposition + unsupported-marker triage), the scheduled `native-collector-soak.yml` (heavier endurance fixtures on both drivers, drop-counter parity), and the scheduled alert-only `native-collector-benchmark.yml` (enqueue throughput + peak RSS vs `bench/baseline.json`).
