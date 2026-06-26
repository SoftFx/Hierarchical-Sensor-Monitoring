# Feature: Default Sensors

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - built-in system, process, module, and diagnostic sensors registered via `Windows`/`Unix` collections

---

## Description

Pre-built sensors enabled through `IWindowsCollection` / `IUnixCollection`. Each sensor's defaults (path, periods, units, alerts) come from a prototype (`Prototypes/Collections/*`); user options merge over the prototype (`DefaultPrototype.Merge`: custom non-null wins per property).

Path roots: `.computer/` for host metrics (`IsComputerSensor=true`), `.module/` for app/collector metrics. Full runtime path is built by `CalculateSystemPath` (see `sensors/feature.md`).

---

## Windows sensors

Per-process (`.module/Process <name>/...`; double bars, 1 s collect / 10 s bars unless noted):

| Method | Sensor | Source |
|---|---|---|
| `AddProcessCpu` | Process CPU % | PerfCounter `Process \ % Processor Time` (instance = current process) |
| `AddProcessMemory` | Process memory MB | `Process \ Working set` → MB |
| `AddProcessThreadCount` | Process thread count | `Process \ Thread Count` |
| `AddProcessThreadPoolThreadCount` | ThreadPool threads | .NET ThreadPool API (cross-platform impl) |
| `AddProcessTimeInGC` | % time in GC | perf counter (net472) / `System.Runtime` EventListener (net6+) |

System (`.computer/System/...`):

| Method | Sensor | Source |
|---|---|---|
| `AddTotalCpu` | Total CPU % | `Processor \ % Processor Time \ _Total` |
| `AddFreeRamMemory` | Available RAM MB | `Memory \ Available MBytes` |
| `AddGlobalTimeInGC` | Global % time in GC | `.NET CLR Memory \ % Time in GC \ _Global_` |

Disks (`.computer/Disks monitoring/...`; single-disk variants take `DiskSensorOptions.TargetPath` (default `C:\`), `*Disks*` variants fan out over `DriveInfo.GetDrives()` filtered to `DriveType.Fixed`, drive letter embedded in sensor name and counter instance):

| Method | Sensor | Source |
|---|---|---|
| `AddFreeDiskSpace` / `AddFreeDisksSpace` | Free space MB (instant double, 5 min period) | `DriveInfo.AvailableFreeSpace` |
| `AddFreeDiskSpacePrediction` / `AddFreeDisksSpacePrediction` | TimeSpan until disk full | see prediction algorithm below |
| `AddActiveDiskTime` / `AddActiveDisksTime` | Active time % bar | `LogicalDisk \ % Disk Time` |
| `AddDiskQueueLength` / `AddDisksQueueLength` | Queue length bar | `LogicalDisk \ Avg. Disk Queue Length` |
| `AddDiskAverageWriteSpeed` / `AddDisksAverageWriteSpeed` | Write speed MB/s bar | `LogicalDisk \ Disk Write Bytes/sec` |

Windows info (`.computer/Windows OS Info/...`, 12 h period, instant):

| Method | Sensor | Source |
|---|---|---|
| `AddWindowsLastRestart` | TimeSpan since boot | WMI `Win32_OperatingSystem.LastBootUpTime` |
| `AddWindowsLastUpdate` | TimeSpan since last KB | WMI `Win32_QuickFixEngineering` (max InstalledOn) |
| `AddWindowsInstallDate` | TimeSpan since install (default alert > 4 y) | WMI `Win32_OperatingSystem.InstallDate` |
| `AddWindowsVersion` | Version sensor | registry ProductName/DisplayVersion/Build |

Event logs (instant string sensors; `EventLog.EntryWritten` subscription, value = EventID, comment = `Source + Message`, event time → UTC): `AddWindowsApplicationErrorLogs`, `AddWindowsSystemErrorLogs`, `AddWindowsApplicationWarningLogs`, `AddWindowsSystemWarningLogs`, bulks `AddErrorWindowsLogs` / `AddWarningWindowsLogs` / `AddAllWindowsLogs`.

Network (`.computer/Network/...`, TCPv4+TCPv6 counters summed, 1 min period): `AddNetworkConnectionsEstablished` (gauge), `AddNetworkConnectionFailures` (delta), `AddNetworkConnectionsReset` (delta), bulk `AddAllNetworkSensors`.

Service status: `SubscribeToWindowsServiceStatus(serviceName | ServiceSensorOptions)` / `UnsubscribeWindowsServiceStatus` — enum sensor of `ServiceControllerStatus`, 5 s poll via `ServiceController.Refresh`, send on change, default alert "≠ Running" with 5 min confirmation. Registration carries wire-visible `EnumOptions` for all 7 `ServiceControllerStatus` members with fixed ARGB colors plus an auto-generated markdown description (`ModuleInfoCollections.cs`) — a port must reproduce that payload. `ServiceSensorOptions.IsHostService` (default true) places the sensor under `.module`, else under `SensorPath`. Service resolution failures → error value + 1 h re-resolve backoff (`_nextServiceResolveTime`, non-blocking); `ServiceController` disposed on stop/fault, deferred until in-flight run completes (raw `ScheduledTask` with `CurrentRun`).

## Unix sensors

| Method | Sensor | Source |
|---|---|---|
| `AddProcessCpu` | Process CPU % bar | `Process.TotalProcessorTime` delta / wall time |
| `AddProcessMemory` | Process memory MB bar | `Process.WorkingSet64` |
| `AddProcessThreadCount` | Thread count bar | `Process.Threads.Count` |
| `AddProcessThreadPoolThreadCount` | ThreadPool threads | ThreadPool API |
| `AddTotalCpu` | Total CPU % bar | `/proc/stat` jiffy delta (`ProcStat`, parser unit-testable) |
| `AddFreeRamMemory` | Available RAM MB bar | `/proc/meminfo` `MemAvailable` (`ProcMeminfo`) |
| `AddFreeDiskSpace` (+prediction) | Root `/` only | `DriveInfo("/")` (statvfs) — no multi-disk fan-out |

No bash/external-process execution — kernel files and managed APIs only (the old `top`/`free`/`df` shelling was removed). Unix gaps vs Windows: GC time, network, OS info, event logs, service status.

## Cross-platform module & diagnostic sensors

Module info (paths directly under `.module/`):

| Method | Sensor | Behavior |
|---|---|---|
| `AddCollectorAlive` | bool heartbeat | 15 s period; first value `false`, then `true`; TTL 1 min; KeepHistory 180 d |
| `AddCollectorVersion` | Version | collector assembly version + start time; KeepHistory ~5 y |
| `AddCollectorErrors` | string | fed by `MessageDeduplicator` callback (see `error-handling/`) |
| `AddProductVersion(VersionSensorOptions)` | Version | user-supplied product version + start time |
| `CreateServiceCommandsSensor` | string commands | "Service commands" path; fixed strings "Service start/stop/restart", "Service update [from X] to Y", custom; registers an implicit `IfReceivedNewValue → notification` alert |

Queue self-diagnostics (`.module/Collector queue stats/...`, all `IsPrioritySensor=true`; suppression boundary in `data-pipeline/feature.md`):

| Method | Sensor | Feed point |
|---|---|---|
| `AddQueueOverflow` | int bar of dropped/evicted counts per queue | `HandleEnqueueResult` + `ReportRequeueEviction` (never suppressed) |
| `AddQueuePackageValuesCount` | int bar, values per package | `AddPackageInfo` after successful send |
| `AddQueuePackageProcessTime` | double bar, avg time-in-queue | `AddPackageInfo` |
| `AddQueuePackageContentSize` | double bar, package size (chars → MB) | `AddPackageSendingInfo` |

## Group registration helpers

| Bulk | Expands to |
|---|---|
| `AddAllDefaultSensors(productVersion)` | `AddAllComputerSensors()` + `AddAllModuleSensors(productVersion)` |
| `AddAllComputerSensors()` | system + all-disks + windows-info (+network) — platform-dependent |
| `AddAllModuleSensors(version)` | process + collector monitoring + queue diagnostics + product version (if given) |
| `AddProcessMonitoringSensors` / `AddSystemMonitoringSensors` / `AddDiskMonitoringSensors` / `AddAllDisksMonitoringSensors` / `AddWindowsInfoMonitoringSensors` / `AddAllNetworkSensors` / `AddCollectorMonitoringSensors` / `AddAllQueueDiagnosticSensors` | per-category bulks |

## Free disk space prediction algorithm

`DefaultSensors/BaseTemplates/FreeDiskSpacePredictionBase.cs`: sample free space every 30 s; speed EMA `0.9*old + 0.1*new`; first **6** requests are calibration (default `DiskSensorOptions.CalibrationRequests = 6`, configurable; returns OffTime); if space is shrinking → `TimeSpan = freeSpace / speed`, status Ok; if growing → previous prediction + OffTime ("cannot be calculated"). Read failures are sensor errors (Error value with message), not lifecycle failures — sampling continues and recovers.

## Perf-counter infrastructure

`System.Diagnostics.PerformanceCounter` is isolated behind `IPerformanceCounterFactory`/`IPerformanceCounter` (`WindowsPerformanceCounterFactory` is the only place real calls live; tests substitute fakes). Counters are recreated on `InvalidOperationException` and disposed in `StopAsync`.

## Key Files

| File | Purpose |
|---|---|
| `Collections/WindowsSensorsCollection.cs`, `UnixSensorsCollection.cs`, `DefaultSensorsCollection.cs` | Registration surface implementations |
| `PublicAPI/IWindowsCollection.cs`, `IUnixCollection.cs` | Public registration interfaces |
| `DefaultSensors/Windows/**`, `DefaultSensors/Unix/**` | Sensor implementations |
| `DefaultSensors/Other/*.cs`, `DefaultSensors/Diagnostic/*.cs` | Module info + queue diagnostics |
| `Prototypes/Collections/*.cs` | Per-sensor defaults (paths, periods, alerts) |
| `DefaultSensors/BaseTemplates/*.cs` | Disk space / prediction shared logic |

## Native port (#1099, C++ collector — epic #1093)

The native collector (`src/native/collector`) reproduces the **registration payloads** of the
default-sensor catalog as a declarative table (`kDefaultSensorCatalog` in `hsm_collector.cpp`); each
row maps 1:1 to a `Prototypes/Collections/**` prototype. `hsm_collector_add_default_sensor(id, params)`
plus the `AddAll*`/per-category bulk helpers (C ABI 0.4.0) register the sensor; `params` pins the
volatile path segments (process name → "Process &lt;name&gt;", disk letter → the `{letter}` slot).

What is byte-pinned (cross-language): `Path` (category + name under `.computer`/`.module`), `SensorType`,
`OriginalUnit`, `Statistics`, `KeepHistory`, `TTLs`, `AggregateData`/`EnableGrafana`/`IsSingletonSensor`,
`EnumOptions`, and the default **alert(s)** (EMA scheduled-hourly, free-disk ArrowDown+SensorError,
windows-info `IfValue` notification, service-status confirmation, service-alive TTL). Parity is locked
**every catalog row** by the committed normalized golden `tests/conformance/collector/golden/default_sensors_wire.golden`:
`WireFormatGoldenLockTests.All_default_sensor_registrations_match_the_golden` reproduces it from the REAL
managed prototypes (`Get(null).ApiRequest` → `HttpRequest`) and native `native_default_sensors_wire_golden`
reproduces it from the catalog — so a future managed-prototype change to any field diverges loudly. Seven
representatives additionally carry full (Path+Description-inclusive) byte-locks
(`Default_sensor_registrations_match_the_native_golden_bytes` ↔ `NativeDefaultSensorWireMatchesNet`), and
the cross-driver `default_sensors_contract.hsmtest` corpus runs both drivers over the same substrings.

`add_all_default_sensors` registers a **reduced set** vs the managed `AddAllDefaultSensors`: a single `C`
disk (not the live `DriveInfo.GetDrives()` fan-out) and no GC sensors — so the native bulk is smaller by
design today (group composition pinned by `native_default_sensor_group_composition`). The full per-drive
enumeration is the live-value follow-up.

Reproduced managed quirks: an alert-less default sensor emits `"Alerts":[]` (the prototype initializes
the list — a user `CreateXSensor` with no alerts emits `null`); the `SpecialAlertCondition` TTL alert
serializes `"Conditions":null`; `Free RAM memory` keeps `Statistics` `None` (it never sets EMA, and the
production `AddAll*` path passes `null` to `Get`); the scheduled-notification stamp is
`0001-01-01T12:00:00Z` (carried as a pre-formatted ISO string — it predates the unix epoch and cannot
round-trip through `gmtime_s`).

`Description` is **not** part of the byte contract — the managed originals interpolate machine-specific
data (process name, readable periods) and are non-deterministic; the catalog emits a short deterministic
line and the golden lane overrides the managed `Description` to match.

**Metric-source seam** (the `IPerformanceCounterFactory`/`IPerformanceCounter` equivalent):
`hsm_collector_set_metric_source_factory` installs a C-callback factory; the native `MetricSource` RAII
wrapper reads a sample per tick, recreates the source on a read error, and disposes it on stop. The
production factory is a no-op.

**Out of scope here (live-value follow-up under #1099):** the real platform readers (Windows
PDH/WMI/registry/EventLog; Linux `/proc/stat`, `/proc/meminfo`, `statvfs`), the per-sensor
scheduled-tick wiring that pumps those readers, the disk fan-out over real fixed drives, and the
free-space prediction EMA — all per-platform smoke-tested, not in the portable corpus. **Divergences:**
the `.NET`-specific time-in-GC sensors are dropped (a native host has no managed GC); the Unix surface
is the managed parity subset (no native systemd/journald/network extensions).

## Known Issues / Limitations

- Unix surface is a strict subset of Windows (see gaps above).
- Disk prediction speed is a simple EMA; bursty deletes/writes distort the estimate until the average converges.
