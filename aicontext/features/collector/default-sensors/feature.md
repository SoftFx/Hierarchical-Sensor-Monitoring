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
| `AddCollectorVersion` | Version | collector assembly version, posted on EVERY Start with comment `Start: dd/MM/yyyy HH:mm:ss` and on EVERY Stop with `Stop: dd/MM/yyyy HH:mm:ss` (UTC, `SensorBase.DefaultTimeFormat`, formatted with `CultureInfo.InvariantCulture` — `/` and `:` are culture-replaced separator placeholders in a .NET custom format string, so a bare `ToString` would emit `22.09.2026 18:33:37` on ru-RU and break parity with the native collector, #1433); the Start instant is stamped once at prototype creation and replayed unchanged by a restart, the Stop instant is the moment of the stop; KeepHistory ~5 y |
| `AddCollectorErrors` | string | fed by `MessageDeduplicator` callback (see `error-handling/`) |
| `AddProductVersion(VersionSensorOptions)` | Version | user-supplied product version; same `Start:`/`Stop:` marker posts as `AddCollectorVersion` (one `ProductVersionSensor` class serves both) |
| `CreateServiceCommandsSensor` | string commands | "Service commands" path; fixed strings "Service start/stop/restart", "Service update [from X] to Y", custom; registers an implicit `IfReceivedNewValue → notification` alert |

Queue self-diagnostics (`.module/Collector queue stats/...`, all `IsPrioritySensor=true`; suppression boundary in `data-pipeline/feature.md`):

| Method | Sensor | Feed point |
|---|---|---|
| `AddQueueOverflow` | int bar of dropped/evicted counts per queue | `HandleEnqueueResult` + `ReportRequeueEviction` (never suppressed) |
| `AddQueuePackageValuesCount` | int bar, values per package | `AddPackageInfo` after successful send |
| `AddQueuePackageProcessTime` | double bar, avg time-in-queue | `AddPackageInfo` |
| `AddQueuePackageContentSize` | double bar, package size (chars → **KB**, `Unit.KB`) | `AddPackageSendingInfo` |

## Group registration helpers

| Bulk | Expands to |
|---|---|
| `AddAllDefaultSensors(productVersion)` | `AddAllComputerSensors()` + `AddAllModuleSensors(productVersion)` |
| `AddAllComputerSensors()` | system + all-disks + windows-info (+network) — platform-dependent |
| `AddAllModuleSensors(version)` | process + collector monitoring + queue diagnostics + product version (if given) |
| `AddProcessMonitoringSensors` / `AddSystemMonitoringSensors` / `AddDiskMonitoringSensors` / `AddAllDisksMonitoringSensors` / `AddWindowsInfoMonitoringSensors` / `AddAllNetworkSensors` / `AddCollectorMonitoringSensors` / `AddAllQueueDiagnosticSensors` | per-category bulks |

## Free disk space prediction algorithm

`DefaultSensors/BaseTemplates/FreeDiskSpacePredictionBase.cs` — "сколько продержится свободное место
при наблюдаемой скорости расхода". Переписан в #1445 по двум причинам: старая формула на
простаивающем хосте навечно залипала на скорости одного всплеска записи, а окно усреднения было
минутным там, где ответ живёт на часах. На реальном хосте это выглядело так: соседние посты
давали 19 ч, 2 д 6 ч, 14 ч, 1 д 12 ч (четырёхкратный разброс скорости за полчаса), причём
свободное место за эти же полчаса РОСЛО, а истинная картина на часовом масштабе была ровная:
−30.4 GB за 23.5 ч, то есть ~1.3 GB/ч и полтора дня до заполнения.

**Сбор (sampling loop, каждые 10 минут).** `curSpeed = (lastFree - curFree) / elapsedSeconds` —
ЗНАКОВАЯ величина (>0 — место убывает, <0 — освобождается). В EMA складывается **каждый** интервал:
первый задаёт начальное значение, дальше `(1-a)*old + a*new` при `a = 1/37`.

**Окно — шесть часов.** Веса замеров — геометрическая прогрессия, поэтому средний возраст
замера за оценкой равен `period * (1-a) / a`. При 10 мин и `a = 1/37` это ровно 36 периодов =
**6.0 ч**; экспоненциальная постоянная времени `period / a` = **6.17 ч**, а вес любого одного замера
уменьшается вдвое каждые **4.22 ч**. Один интервал способен сдвинуть оценку не больше чем на
`a` = 2.7 % расстояния до только что измеренной скорости.

**Калибровка** считается по часам СБОРА, а не постинга: `(n/3)` — это n завершённых замеров
свободного места (default `DiskSensorOptions.CalibrationRequests = 3` → первая оценка через ~30 минут
после старта). Ровный расход репортится ТОЧНО сразу после калибровки: EMA одинаковых
замеров равна самому замеру — окно в 6 ч стоит задержки только тогда, когда скорость МЕНЯЕТСЯ.

**Постинг (каждые 5 мин).** Ровно одно из пяти состояний — вот что оператор видит в каждом:

| Состояние | Условие | Value | Status | Comment |
|---|---|---|---|---|
| Draining | speed > 0, оценка ниже потолка | `freeSpace / speed` | **Ok** | `Free space decreases by X Mbytes/hour.` |
| BeyondCeiling | speed > 0, но места хватит больше чем на год | `365.00:00:00` | OffTime | `Free space decreases by X Mbytes/hour. More than 365 days left.` |
| NoDrain | speed == 0 | `365.00:00:00` | OffTime | `Free space is not decreasing. Value cannot be calculated.` |
| Growing | speed < 0 | `365.00:00:00` | OffTime | `Free space increases by X Mbytes/hour. Value cannot be calculated.` |
| Calibration | замеров меньше `CalibrationRequests` | `365.00:00:00` | OffTime | `Calibration request (n/N). Value cannot be calculated yet.` |

Что это значит для оператора:
- **Только `Ok` — реальная оценка.** Любой `OffTime` означает "оценки нет", а не "осталось ровно год".
- **Потолок `365.00:00:00`** (`FreeDiskSpacePredictionBase.MaxPrediction`) читается как "при текущем
  расходе диск в ближайший год не заполнится". Он же защищает от переполнения `TimeSpan.FromSeconds`
  на очень маленькой скорости (раньше оно бросало исключение, и сенсор молча ничего не постил).
- **`00:00:00` больше никогда не заглушка.** Ноль теперь означает ровно одно — свободного места нет
  вообще. До #1445 ноль шлёл всю калибровку, и алерт читал его как "диск полон ПРЯМО СЕЙЧАС".
- **Всплеск записи почти не двигает оценку.** Один десятиминутный интервал со скоростью в 4×
  выше фона — ровно тот разброс, который наблюдался на живом хосте, — меняет горизонт на 9 %
  вместо прежних почти четырёхкратных качелей (старые константы умножали скорость на 3.6 за те же
  10 минут). Всплеск в 2× — около 3 %.
- **Задержка детекта "расход прекратился" — часы, и это осознанная цена окна.** Если запись
  остановилась, скорость падает вдвое каждые 4.22 ч, то есть горизонт удваивается каждые 4.22 ч.
  С оценки в 1.47 дня порог `value <= 2 дней` перестаёт пробиваться через ~1.9 ч простоя, а потолок
  достигается через ~34 ч. До #1445 это не происходило НИКОГДА — именно на это жаловался владелец,
  но "часы" — это не "сразу". Сенсор отвечает на вопрос масштаба часов-суток; для секундной
  реакции есть соседний сенсор "Free space on disk", который точен и мгновенен.
- **Реальный простаивающий диск оседает в `BeyondCeiling`, а не в `NoDrain`.** `NoDrain` требует СТРОГО
  `speed == 0.0`, то есть байт-в-байт одинакового свободного места на каждом замере — это случай скриптованного
  диска в конформансе, а не живого тома. На живом хосте EMA затухает к крошечной, но ненулевой
  скорости, и обе стороны постят `365.00:00:00` + OffTime с комментарием `... More than 365 days left.`
  Для алерта разницы нет: значение и статус у этих двух состояний одинаковые.

Read failures are sensor errors (Error value with message), not lifecycle failures — sampling
continues and recovers. Это единственный путь, где сенсор всё ещё может отправить `00:00:00`: оба
коллектора при провале чтения публикуют default-значение со статусом **Error** и текстом ошибки в
комментарии — общий контракт всех value-сенсоров, а не заглушка этого; статус Error делает его
громким. Провал первого чтения на Start НЕ становится baseline'ом ни в одном коллекторе (иначе знаковая
EMA засеялась бы огромным отрицательным значением) — baseline ставит следующий успешный замер.

Две детали часть cross-collector контракта и воспроизведены дословно в нативном
`src/disk_prediction.hpp` (#1426, #1445):
- value, status и comment одного поста считаются из ОДНОГО снимка состояния, так что тройка
  всегда согласована (до #1445 счётчик калибровки двигался внутри `GetValue`, и пост СРАЗУ после
  калибровки нёс `TimeSpan.Zero` с уже рабочими status и comment);
- the comment divides the speed by 1 MiB whatever unit the platform's `IDiskInfo` reports in
  (bytes on Windows, kB on Unix) — mirrored rather than corrected, because the two collectors
  must produce the same comment for the same host;
- **the rate is printed in MB/HOUR with up to SIX decimals (#1460, collector 3.5.4 / native
  0.8.2).** It used to be MB/sec in the payload's shortest-round-trip form, so a realistic idle
  drain reached the operator as `Free space decreases by 1.6574101944286661E-06 Mbytes/sec.` —
  a 17-digit scientific literal in a sentence a human reads. Per hour is the scale a 365-day
  sensor answers on. Six decimals rather than three **because the `Mbytes` label above is only
  accurate on Windows**: the comment divides by 1 MiB whatever unit the platform reports free
  space in, and the Unix reader reports kB, so a Unix number is 1024x smaller than its label
  says — at three decimals an ordinary Unix drain rounded back to `0.000`, the same
  structurally-zero reading the issue was about. Trailing zeros are trimmed with one decimal
  always kept, so a fast drain reads `1800.0` rather than `1800.000000`. The sensor VALUE is
  unaffected: only the comment text changed. The digits are produced by INTEGER arithmetic
  (scale the same double by 1 000 000, round half away from zero) rather than by
  `ToString("F6")` / `printf("%.6f")`, because those two disagree at a decimal midpoint
  (half-away-from-zero vs half-to-even) and the corpus pins this text byte-for-byte. As a side effect the mantissa is
  now identical on net472 and net6.0, which the 15-digit/round-trip split used to make differ;
  an absurd magnitude that would overflow the scaled integer falls back to the round-trip form
  on both sides. Pinned by `metric_source_contract:disk_prediction_comment_reads_a_slow_drain_in_mb_per_hour`
  plus the mirrored unit tests (`FreeDiskSpacePredictionTests` / `native_disk_prediction_*`);
- the NUMBER is rendered with the invariant culture on both sides — managed used plain
  interpolation, which on a comma-decimal host (`ru-RU`, `de-DE`, …) emitted `1,5` where native
  emits `1.5`; fixed in #1426 so the contract holds on every host.

Known managed wrinkle: the send loop starts in `InitAsync` with a zero due time while the sampler's
first tick is aligned to the next period boundary, so how many posts precede the N-th measurement
depends on scheduling. The conformance fixture therefore does not pin the NUMBER of calibration
posts — только их форму.

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

**`Free space on {letter} disk` posts WHOLE megabytes on Windows too (collector 0.8.2).** The
Windows source divided the byte count as a double and posted the fraction
(`51234.87109375`) where managed `WindowsDiskInfo.FreeSpaceMb` does an integer division
(`BytesToMegabytes` → `(int)(value / (1 << 20))`) and posts `51234` — one sensor, two values on
the same host, which is what rule #10 forbids; the Linux source already truncated the same way.
The emitted value therefore changes on every Windows host on upgrade (it loses the fractional
part). Found during #1426 and deferred then. Pinned by
`native_windows_disk_binds_only_lettered_rows`, which now also requires a live read to be a
whole number; the corpus cannot carry it, because the truncation lives in the platform reader
the corpus deliberately replaces with a scripted source.

### Typed sources and read-failure reporting (#1426)

The original seam carried a `double` and three outcomes, which left two gaps: a broken source could
only say "no value", so it degraded silently (against rule #8), and a non-double sensor could not be
driven at all. Both are closed **additively** in collector 0.8.0 — `hsm_metric_read_fn`,
`hsm_metric_source_factory_fn` and `hsm_collector_set_metric_source_factory` keep their exact
signatures and semantics, so an existing host plugin needs no change:

| Addition | Purpose |
|---|---|
| `HSM_METRIC_READ_SAMPLE_ERROR` | THIS read failed and the source is still usable — report, do not recreate |
| `hsm_metric_sample_t` | a typed sample: `kind` (double / TimeSpan-ms), the value, a `status`, a `comment` and an `error` string |
| `hsm_metric_read_sample_fn` | a reader that fills that sample |
| `hsm_metric_source_t` + `hsm_collector_set_metric_source_factory_ex` | the factory fills one struct instead of three out-params, so the seam can grow again without a new factory type |
| `hsm_metric_source_t.refresh` / `.refresh_period_ms` | an OPTIONAL second cadence for a source whose posted value is derived from samples taken more often than it posts — the native shape of a managed sensor that runs its own sampling loop beside the post loop. Served before the due gate on BOTH the value and the bar path, so a refresh that comes due between bar ticks cannot leave the scheduler hint in the past |

A sample is not taken on trust. `kind` must match the bound sensor's type (`TIMESPAN_MS` for a
TimeSpan sensor, `DOUBLE` for every other value type and for the sample a bar accumulates) or the
read is treated as a failure and reported like one — otherwise a source that filled `double_value`
on a TimeSpan sensor would publish `00:00:00` from an untouched `timespan_ms`, a wrong value with an
OK status. An out-of-range `status` falls back to OK and a `comment` over 1024 characters is
trimmed, the same guards `AddValueJson` / `AddRate` / the file path already applied.

Both setters share one factory slot: installing either replaces whichever was there, so a collector
never holds two competing sources for one path. The collector zero-initializes both structs and sets
their `struct_size` before every call; a source must not write past the size it is handed.

**What a failure does now.** `HSM_METRIC_READ_ERROR` keeps its old meaning (the SOURCE is faulted:
dispose + recreate; a declined recreate parks the sensor). `HSM_METRIC_READ_SAMPLE_ERROR` keeps the
source. Either way the failure becomes visible, and exactly as the managed side already makes it
visible:

- the deduplicated error channel gets `Sensor: <path>, <reason>`. Managed `AddException` formats
  `Sensor: {SensorPath}, {ex}`, i.e. the exception's type + message + stack trace, so only the
  `Sensor: <path>, ` prefix and the reason text are shared — the corpus asserts loosely for that
  reason, and the two texts are NOT byte-identical;
- native `LogError` now also posts every emitted line on the `.module/Collector errors` sensor when
  the host registered it, which is the second half of the managed `MessageDeduplicator` action
  (`logger.Error` + `CollectorErrors.SendCollectorError`). Only messages that survive deduplication
  get there, so a storm collapses on the wire exactly as it does in the log;
- a VALUE sensor also posts one value per post period: the default value, status `Error`, and the
  failure message as the comment — managed `BuildSensorValue`'s catch arm. A BAR sensor posts
  nothing and just skips the sample, which is what `CollectableBarMonitoringSensorBase` does.

`HSM_METRIC_READ_NO_VALUE` stays SILENT by design: it means a legitimately empty tick (a delta source
seeding its baseline, an interval too short to measure), which managed skips silently too.

**Managed side of the same change.** `UnixTotalCpu` and `UnixFreeRamMemory` used to swallow their
`IOException`/`UnauthorizedAccessException`/`SecurityException` and return `null`, and treated
unparseable content the same way — the exact divergence the native change would have created. They
now route both cases to `HandleException`, so the two collectors report the same failures. Every
other Unix sensor already let its exception reach the collect loop.

Two cases stay deliberately SILENT, because reporting them would recreate the divergence in the
opposite direction:
- **the host has no `/proc` at all** (`FileNotFoundException` / `DirectoryNotFoundException`).
  `UnixSensorsCollection` is chosen for EVERY non-Windows OS, so this is macOS/FreeBSD, or a
  container with `/proc` masked. The sensor can never produce a value there — a platform fact, not
  data loss — and the native collector is silent for the same host because its Linux factory is
  compiled out and the sensor stays registration-only. Reporting would mean a permanent recurring
  error on `.module/Collector errors` for a sensor that was never going to work.
- **the `UnixTotalCpu` constructor's baseline read**, which runs before the sensor is started; every
  later tick reports the same failure anyway.

A file that EXISTS but cannot be read (permissions, I/O error) is the real failure case and is
reported — that is the host where the sensor is supposed to work.

Conformance: `metric_source_contract.hsmtest` (`metric_source_error_is_reported`,
`disk_prediction_calibrates_then_predicts`).

### Native Linux metric sources (#1414)

`InstallLinuxMetricSources()` binds the Unix catalog's value-typed sensors to the SAME OS truth the
managed Unix sensor reads, running the mirrored algorithm (rule #10 — one sensor, one acquisition
mechanism). Dispatch is on the sensor NAME (last path segment), as on Windows. The free-disk reader
binds ONLY the exact letter-less names `Free space on disk` / `Free space on disk prediction`: a
letter-bearing Windows row (still registerable on Linux via `add_default_sensor` + `disk_letter`)
stays registration-only instead of reporting the root mount under a label naming another volume.
The Windows factory enforces the MIRROR of that rule (#1426): it binds by drive letter, and the
letter must be a standalone token (`" <L> disk"`, a space on both sides), so the letter-less Unix
rows — registerable on a Windows host, and ending in `on disk` — are declined rather than parsed as
drive `N:` off the `n` of `on`. Pinned by `native_linux_free_disk_binds_only_the_unix_row` and
`native_windows_disk_binds_only_lettered_rows`. Total CPU seeds its baseline on the
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
core count (two saturated cores read 200%).

A read that FAILS — an unopenable/unreadable `/proc` file, unexpected content in one, a failing
`statvfs`, an unlistable `/proc/self/task` — returns `HSM_METRIC_READ_SAMPLE_ERROR` with the reason
(errno text or the offending path) since #1426, so it reaches the error channel instead of vanishing;
the source is KEPT, so a delta reader does not lose its baseline to a transient failure. Only a tick
that is legitimately empty (a baseline seed, no elapsed jiffies, a sub-tick interval) still returns
`NO_VALUE` and stays silent.

`Free space on disk prediction` is backed by a live source since #1426: `statvfs("/")` sampled every
10 min to feed the drain-speed EMA (#1445), and a TimeSpan posted on the sensor's own post period — the
`refresh` cadence on the typed seam is exactly this. It samples the free space in whole KILOBYTES,
mirroring `UnixDiskInfo.FreeSpace` (`AvailableFreeSpace / 1024`); the unit cancels in the prediction's
division, so Windows can sample bytes and both still produce the same TimeSpan. It binds by the same
EXACT letter-less name rule as the free-space row, so a letter-bearing prediction row stays
registration-only. Still registration-only on Linux: `ThreadPool thread count` (a .NET runtime metric
with no native equivalent). The Windows-only sensors (event logs, service status, network speed,
top-CPU, OS info) are explicitly not ported.

The parsing and delta math live in `src/proc_metrics.{hpp,cpp}` — portable, OS-read-free, and
unit-tested on every CI lane (`proc_stat_*`, `proc_meminfo_*`, `proc_self_stat_*`,
`process_cpu_usage_*`) using the SAME sample text and expected numbers as the managed
`ProcParsersTests`; the `/proc` reads themselves live in `src/platform/hsm_linux_metric_sources.cpp`
behind `#if defined(__linux__)` (the `tcp_connection_stats.hpp` precedent). Linux-only ctest smoke
(`native_linux_metric_sources_produce_live_value`, `native_linux_process_metrics_produce_live_value`,
`native_linux_disk_prediction_produces_live_value`) asserts the sensors actually emit, guarding the
registered-but-empty class #1189 exposed on Windows. The prediction math itself lives in
`src/disk_prediction.hpp` — portable, OS-read-free, shared by BOTH platform factories and unit-tested
(`native_disk_prediction_calibrates_then_predicts`).

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
| Publish ordering | `_sendValueInProgress` + roll-only-on-confirmed-send serialize the two publishers of one bar | a per-sensor `publish_mutex_` held across snapshot **and** enqueue by roll-on-add, the tick roll and the partial post: the closed bar of a window is always published before any partial of the next one (the server persists on a NEW OpenTime, so a reordering here would reorder stored bars) |
| Rounding | `Complete()` on a copy: double → `Math.Round(v, Precision=2, AwayFromZero)`; int → mean only | same serializer as the public bars |

Every post of one window therefore carries the SAME `OpenTime`/`CloseTime` while `Count`, `Min`,
`Max`, `Mean` and `Last` grow. Before 0.7.1 the native bar window equalled the post period, so each 15 s
post was a separate, closed bar with its own `OpenTime`.


**`Package content size` reports KILOBYTES (#1459, collector 3.5.4 / native 0.8.2).** It registered
`Unit.MB` while a bar renders at 2-decimal precision, so a realistic package — a couple of kilobytes,
0.002 MB — rounded to `0.00`: the sensor was structurally incapable of reporting anything but zero.
Both collectors now divide by 1024 instead of 1024² and register `Unit.KB` (2). They also had to be made to measure the SAME quantity (rule #10): managed multiplied the serialized body's char count by `sizeof(char)`, measuring the in-memory UTF-16 string, while the body goes on the wire as UTF-8 and native sums the bytes it sends — a 2x divergence that was invisible while both were stuck at `0.00`. Managed now reports the bytes sent, like native, and native counts the array brackets and separating commas the send path adds around its elements — without them the two stayed a systematic ~1% apart. The unit is part of
the registration, so an existing node keeps showing MB until the sensor re-registers (which happens
on the next collector start), while the VALUES switch immediately — a node that has not re-registered
shows kilobyte numbers under an MB label until then. The native side additionally measures the
package BEFORE handing it to the sender, which is free to consume the batch. Pinned by
`default_sensors_contract:queue_content_size_registers_in_kilobytes` +
`native_package_content_size_reports_kilobytes`.

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
WMI/registry/EventLog) and the disk fan-out over real fixed drives — per-platform smoke-tested, not
in the portable corpus. The non-double scheduled-tick wiring and the free-space prediction EMA left
this list in #1426: the seam now carries a typed sample, the prediction runs on both platforms out of
the shared `src/disk_prediction.hpp`, and both are corpus-pinned by
`metric_source_contract.hsmtest`. **Divergences:** the `.NET`-specific time-in-GC sensors are dropped (a native host has
no managed GC); the Unix surface is the managed parity subset (no native systemd/journald/network
extensions).

## Known Issues / Limitations

- Unix surface is a strict subset of Windows (see gaps above).
- ~~**Native (Linux and Windows): a failing disk read is invisible on the server.**~~ Closed by
  #1426: a failing read reports `SAMPLE_ERROR` with the reason, which reaches the deduplicated log
  and `.module/Collector errors`, and a value sensor posts the default value with `SensorStatus.Error`
  and the message as the comment — what managed `FreeDiskSpaceBase` already did.
- ~~**Native: `Free space on disk prediction` has no live value**~~ Closed by #1426 on Linux AND
  Windows: the seam carries a typed sample (double or TimeSpan-ms) plus an auxiliary sampling
  cadence, and both platform factories bind the row to the shared `src/disk_prediction.hpp` math.
- Disk prediction speed is a simple EMA; bursty deletes/writes distort the estimate until the average converges.
- Managed `FreeDiskSpacePredictionBase` starts its send loop in `InitAsync` (first post due
  immediately) and only resets the request counter in `StartAsync`, so the opening calibration post
  races that reset. Cosmetic (one extra calibration post, a possibly wrong `(n/N)` in its comment)
  and deliberately NOT pinned by conformance.
