# Feature: Default Sensors

> Owner: collector | Last reviewed: 2026-09-22 | Canonical: yes
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

Network (`.computer/Network/...`):

| Method | Sensor | Source | Notes |
|---|---|---|---|
| `AddNetworkConnectionsEstablished` | TCPv4+v6 established (gauge) | perf counter summed | 1 min period |
| `AddNetworkConnectionFailures` | TCPv4+v6 failures (delta) | perf counter summed | 1 min period |
| `AddNetworkConnectionsReset` | TCPv4+v6 resets (delta) | perf counter summed | 1 min period |
| `AddNetworkInterfacesSpeed` | Per-interface received/sent MB,sec (DoubleBar) | `NetworkInterface.GetIPStatistics()` delta / 10 s | Dynamic — one DoubleBar **per direction (received/sent) that actually carries traffic**, on each Up, non-loopback, **non-filter** NIC, discovered at runtime; paths `.computer/Network/<iface>/{Received,Sent} MB,sec` (via `RevealDefaultPath`, so they nest under the computer's `.computer` node like every host sensor — not a separate top-level `Network` node; comma, not `/`, so the unit isn't a separate tree node); `IsComputerSensor=true`; bar period 1 min; EMA; TTL 5 min; KeepHistory 90 d; per direction, counter reset/negative delta **or sub-displayable trickle (rounds to 0.00 at bar precision 2, i.e. < ~0.005 MB/s ≈ 5 KB/s — e.g. WSL/Hyper-V keepalives) → not posted, no sensor created** (avoids permanently-zero bars); idle/disappeared directions expire by TTL. NO PDH. |

Bulk `AddAllNetworkSensors` calls all four methods above.

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
| `AddCollectorAlive` | bool heartbeat | 15 s period; first value `false`, then `true`; the `false` start marker is armed once per SENSOR, so a collector restart beats `true` straight away; TTL 1 min; KeepHistory 180 d |
| `AddCollectorVersion` | Version | collector assembly version, posted on EVERY Start with comment `Start: dd/MM/yyyy HH:mm:ss` and on EVERY Stop with `Stop: dd/MM/yyyy HH:mm:ss` (UTC, `SensorBase.DefaultTimeFormat`); the Start instant is stamped once at prototype creation and replayed unchanged by a restart, the Stop instant is the moment of the stop; KeepHistory ~5 y |
| `AddCollectorErrors` | string | fed by `MessageDeduplicator` callback (see `error-handling/`) |
| `AddProductVersion(VersionSensorOptions)` | Version | user-supplied product version; same `Start:`/`Stop:` marker posts as `AddCollectorVersion` (one `ProductVersionSensor` class serves both) |
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

**Dynamic samplers** (sensors not backed by static catalog rows): both network-speed (#1189) and top-CPU
(#1103) run background threads started by `Start()` that discover items at runtime and lazily create
`DoubleBar` sensors per item. `EnableNetworkInterfaceSpeedSensors(period_ms)` / `EnableTopCpuSensors(…)`
configure and enable them before `Start`; calling them after `Start` returns `HSM_RESULT_INVALID_STATE`.
The native sampler uses `GetIfTable2` (Windows only, Win10+ API set, Iphlpapi), same counter-reset-skip
rule as the managed driver — the `network_speed_contract.hsmtest` fixture covers lifecycle + catalog-prototype
registration for both drivers.

Two live-sampler filters keep the interface set meaningful (both collectors, parity):
- **Filter modules excluded** — `GetIfTable2` returns NDIS lightweight-filter pseudo-interfaces
  (`<adapter>-QoS Packet Scheduler-NNNN`, `-WFP …`, `-Hyper-V Virtual Switch Extension Filter-NNNN`)
  as Up, non-loopback rows; the native sampler skips rows with
  `InterfaceAndOperStatusFlags.FilterInterface` set. The managed driver never saw them
  (`NetworkInterface` wraps `GetAdaptersAddresses`, which omits filter modules), so this restores parity.
- **Idle interfaces excluded** — an interface is surfaced only when its octet delta for the interval is
  non-zero. Perpetually-quiet interfaces (Hyper-V vSwitch bindings, Wi-Fi Direct virtuals with no peer)
  never create a sensor; an interface appears as soon as it transfers and expires by TTL once it goes quiet.

Both are live-acquisition rules (not part of the action-protocol corpus), so the conformance fixture, which
registers prototypes directly, is unaffected.

**Fixed process node — intentional managed/native divergence (#1429).** Native hosts register the
process sensors under the FIXED node `.module/Process process` (the `{proc}` fallback in
`ResolveDefaultCategory`; hosts leave `process_name` NULL on purpose). The path is identical on every
host, so one HSM alert template on `.module/Process process/...` applies to all agents. The managed
collector's `Process {ProcessName}` naming is a known divergence; do not "fix" either side (substitute
the real process name natively, or fix the managed name) without an explicit product decision.

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
production default factory is a no-op; two ready-made factories ship with the library —
`hsm_collector_install_windows_metric_sources` (PDH/Win32, #1164) and
`hsm_collector_install_linux_metric_sources` (`/proc` + `statvfs`, #1414). Each returns
`HSM_RESULT_INVALID_STATE` off its platform and must be installed before `Start`.

### Native Linux metric sources (#1414)

`InstallLinuxMetricSources()` binds the Unix catalog's value-typed sensors to the SAME OS truth the
managed Unix sensor reads, running the mirrored algorithm (rule #10 — one sensor, one acquisition
mechanism). Dispatch is on the sensor NAME (last path segment), as on Windows. The free-disk reader
binds ONLY the exact letter-less name `Free space on disk`: a letter-bearing Windows row (still
registerable on Linux via `add_default_sensor` + `disk_letter`) stays registration-only instead of
reporting the root mount under a label naming another volume. Total CPU seeds its baseline on the
FIRST scheduled read (posting nothing), not at source construction: the factory binds during Start and
the first read follows within milliseconds, where a single jiffy would read as 0% or 100% and could
trip the built-in EmaMean > 50 warning — seeding on the first read reproduces the managed timing (first
value one full sample period after start). Process CPU likewise seeds on its first read, and
additionally skips any sample whose
interval is shorter than one clock tick (`utime`/`stime` are tick-quantized, so a sub-tick window
would read as hundreds of percent — a spike the managed sensor, sampling a full bar tick after its
constructor, never produces).

| Native source | OS truth | Managed reference mirrored |
|---|---|---|
| `Total CPU` | `/proc/stat` aggregate line, busy% by delta | `UnixTotalCpu` + `ProcStatCpuUsage` |
| `Free RAM memory` | `/proc/meminfo` `MemAvailable` (kB / 1024.0 → MB, double division) | `UnixFreeRamMemory` + `ProcMeminfo.ParseAvailableKb` |
| `Free space on disk` | `statvfs("/")`, `f_bavail * f_bsize`, then two integer divides → whole MB | `UnixFreeDiskSpace` + `UnixDiskInfo` (`DriveInfo.AvailableFreeSpace` = `f_bsize * f_bavail` in .NET's `pal_mount.c`; `f_bsize`, not `f_frsize`) |
| `Process CPU` | `/proc/self/stat` `utime+stime` delta / wall delta × 100 | `UnixProcessCpu` (.NET reads the same fields) |
| `Process memory` | `/proc/self/stat` `rss` pages × page size, integer divide → MB | `UnixProcessMemory` (`WorkingSet64`) |
| `Process thread count` | `/proc/self/task` entry count | `UnixProcessThreadCount` (`Process.Threads`) |

Pinned semantics carried over from the managed side: `idle` is the `idle` field ONLY (iowait counts as
busy); `guest`/`guest_nice` are excluded from the total (Linux already folds them into user/nice); the
busy fraction clamps to 0..100 and posts nothing when the interval is zero or the counters moved
backwards (CPU-count change / reset); the free-RAM fallback is
`MemFree + Buffers + Cached + SReclaimable - Shmem` clamped at 0; the disk and process-memory values
truncate to whole MB exactly as the managed integer conversions do; process CPU is NOT normalized by
core count (two saturated cores read 200%). A read failure is reported as "no value this tick" — a
skipped bar, not a fault — mirroring the managed sensors' swallowed `IOException`; only a failed
`statvfs` returns `ERROR` (recreate), as the Windows disk reader does.

Not backed by a live source (registration-only, same as Windows): `Free space on disk prediction`
(the seam is double-valued, the sensor is a TimeSpan) and `ThreadPool thread count` (a .NET runtime
metric with no native equivalent). The Windows-only sensors (event logs, service status, network
speed, top-CPU, OS info) are explicitly not ported.

The parsing and delta math live in `src/proc_metrics.{hpp,cpp}` — portable, OS-read-free, and
unit-tested on every CI lane (`proc_stat_*`, `proc_meminfo_*`, `proc_self_stat_*`,
`process_cpu_usage_*`) using the SAME sample text and expected numbers as the managed
`ProcParsersTests`; the `/proc` reads themselves live in `src/platform/hsm_linux_metric_sources.cpp`
behind `#if defined(__linux__)` (the `tcp_connection_stats.hpp` precedent). Linux-only ctest smoke
(`native_linux_metric_sources_produce_live_value`, `native_linux_process_metrics_produce_live_value`)
asserts the sensors actually emit, guarding the registered-but-empty class #1189 exposed on Windows.

### Native metric-driven bars: partial posts (#1428)

Applies to every default DoubleBar/IntBar the native collector binds to a metric source (Total CPU,
Free RAM memory, Process CPU / memory / thread count, the Windows disk bars), on Windows PDH and Linux
`/proc` alike — the scheduling lives in the shared core, not in the platform factories. It is a
transcription of managed `BarMonitoringSensorBase` + `CollectableBarMonitoringSensorBase`, using the
managed `BarSensorOptions` defaults every default-bar prototype inherits:

| Step | Managed | Native (collector ≥ 0.7.1) |
|---|---|---|
| Bar window | `BarPeriod` 5 min; `OpenTime = floor(UtcNow / BarPeriod) · BarPeriod`, `CloseTime = OpenTime + BarPeriod` (wall clock) | `kDefaultBarPeriodMs` (the sensor's registered bar period), same alignment in unix ms (5 min divides the 0001→1970 offset, so both land on the same instants) |
| Sample tick | `BarTickPeriod` 5 s from Start (collect loop); each tick first rolls the bar if `CloseTime < now` (strict), then reads the source and adds the sample | `kMetricBarSampleMs` 5 s; same roll-then-sample order. The tick at Start only primes the source (its value is discarded), so the first sample lands one tick after Start as in managed: gauges (Free RAM, process memory, disk bars) get no extra sample, and Total CPU seeds its delta baseline when managed does at construction |
| Partial post | every `PostDataPeriod` (catalog 15 s), first at the next wall-clock multiple of it (a full period when Start is exactly on one); posts a copy of the in-progress bar, nothing when it is empty | same cadence and alignment (`post_period_ms` of the catalog row) |
| Window boundary | the post falling on the boundary instant still carries the old bar (`CloseTime == now` is not past); the next sample tick publishes the closed bar again and opens the next window | identical |
| Stop | flushes a non-empty partial bar, then opens a fresh one (no resend on stop → start → stop) | identical (`TryFlushBarJson`) |
| Snapshot vs roll | `_sendValueInProgress`: a roll attempted while a partial is being sent is deferred (no newer closed bar can overtake the older partial) | `partial_send_in_progress_`: roll-on-add defers while a partial is between snapshot and enqueue; the next tick rolls |
| Rounding | `Complete()` on a copy: double → `Math.Round(v, Precision=2, AwayFromZero)`; int → mean only | same serializer as the public bars |

Every post of one window therefore carries the SAME `OpenTime`/`CloseTime` while `Count`, `Min`,
`Max`, `Mean` and `Last` grow. Before 0.7.1 the native bar window equalled the post period, so each 15 s
post was a separate, closed bar with its own `OpenTime`.

**Push-fed built-in bars** follow the same schedule without the sampling step (managed
`PublicBarMonitoringSensor`, whose collect tick only runs `CheckCurrentBar`): the queue diagnostics
(`.module/Collector queue stats/Queue overflow`, `Items count in package`, `Package process time`,
`Package content size` — 5 min / 5 s / 15 s) and the per-interface network speed bars
(`.computer/Network/<iface>/{Received,Sent} MB,sec` — 1 min / 15 s / 15 s, managed
`WindowsNetworkInterfaceSpeedMonitor`). Values still arrive by push (roll-on-add stays); on top of
that they post a partial every post period with a stable `OpenTime`, and the tick rolls a closed bar
and publishes it without waiting for a next value. Before 0.7.1 they published only on roll-on-add or
at Stop — at most once per window. Any catalog DoubleBar/IntBar that no metric source binds takes this
path at Start. Still different from managed: the queue-diagnostic `Comment` (managed lists per-queue
totals, e.g. `Data: 12`; native has one queue and sends no comment).

**Storage effect.** The server keeps a same-`OpenTime` post as the in-memory partial of the current bar
and persists a bar only when a NEW `OpenTime` arrives (`BarValuesStorage`). A native sender now writes
**one bar per 5 minutes per sensor** — as a managed sender does — instead of one per 15 s post (20× fewer
records), and EMA / alert evaluation runs on the same 5-min grid for both collectors.

Pinned by: the conformance fixture `bar_sampled_partial_contract.hsmtest` (both drivers; a sampled bar
with fixture-sized periods — OpenTime stable across partials, Count accumulating, a new OpenTime after
the boundary, the stop flush); the native manual-clock tests `native_metric_bar_partial_posts_keep_open_time`,
`native_metric_bar_rolls_over_at_window_boundary`, `native_metric_bar_flushes_partial_on_stop` and
`native_built_in_push_bar_posts_partials` (queue diagnostics), whose
expected values are derived step by step from the managed algorithm; and the live smoke tests on both
platforms, which assert that real Total CPU posts are partials of one aligned 5-min bar.

### Platform-correct registration (#1414)

The managed collector picks `WindowsSensorsCollection` / `UnixSensorsCollection` at runtime; the native
collector is compiled for one OS, so the choice is made at compile time. Built for Linux:
`add_disk_monitoring_sensors` registers the Unix pair (`Free space on disk` +
`Free space on disk prediction`, ids `HSM_DEFAULT_UNIX_FREE_DISK_SPACE{,_PREDICTION}` — separate
catalog rows because the managed Unix prototypes carry NO drive letter, which also changes the `{name}`
the free-space alert template interpolates), and `add_all_computer_sensors` registers system + disk
ONLY, mirroring `UnixSensorsCollection.AddAllComputerSensors`. The Windows OS-info, event-log and
network-connection nodes are not in that set: they could never produce a value on Linux and would only
plant permanently empty sensors in the server tree. Windows composition is unchanged.
`native_default_sensor_group_composition` pins both compositions; the two Unix rows are pinned in the
shared golden and by `unix_default_sensors_contract.hsmtest` in BOTH drivers.

**Out of scope here (live-value follow-up under #1099):** the remaining platform readers (Windows
WMI/registry/EventLog), the per-sensor scheduled-tick wiring for non-double sensors, the disk fan-out
over real fixed drives, and the free-space prediction EMA — all per-platform smoke-tested, not in the
portable corpus. **Divergences:** the `.NET`-specific time-in-GC sensors are dropped (a native host has
no managed GC); the Unix surface is the managed parity subset (no native systemd/journald/network
extensions).

## Known Issues / Limitations

- Unix surface is a strict subset of Windows (see gaps above).
- **Native (Linux and Windows): a failing disk read is invisible on the server.** A failed
  `statvfs` / `GetDiskFreeSpaceExW` returns `READ_ERROR`; the collector recreates the source (logged,
  deduplicated) but posts nothing, and the row's TTL is infinite. The managed `FreeDiskSpaceBase`
  instead posts `0` with `SensorStatus.Error` and the exception message. This is a limitation of the
  metric seam (a read outcome cannot carry a status/comment); a status-carrying read outcome is the
  follow-up.
- **Native: `Free space on disk prediction` has no live value** (the seam is double-valued, the sensor a
  TimeSpan), whereas the managed `UnixFreeDiskSpacePrediction` does emit values on Linux. Registration
  is at parity; the value path is the #1099 prediction-EMA follow-up.
- Disk prediction speed is a simple EMA; bursty deletes/writes distort the estimate until the average converges.
