# Native collector C ABI

The permanent interop boundary of the native collector (`src/native/collector`,
epic #1093 / workstream #1095). Every non-C++ consumer — and the C++ wrapper
itself — goes through the C header `include/hsm_collector/hsm_collector.h`. This
document is the contract that header implements; it is the "C ABI doc" the #1095
*Done-when* requires.

## Design rules

- **C linkage only.** Every entry point is `extern "C"`; no C++ types cross the
  boundary. The implementation is C++ but compiles into one library that exports
  only the `hsm_*` symbols.
- **No exceptions cross the boundary.** Internally the core may use exceptions,
  but every entry point catches and converts them to a result code. A C consumer
  never sees a C++ exception. Host-supplied callbacks that throw are caught and
  swallowed (see *Callback isolation*).
- **Opaque handles.** `hsm_collector_t` and `hsm_sensor_t` are forward-declared
  structs; their layout is private. Consumers hold pointers only.

## Handles & ownership

| Handle | Created by | Freed by | Notes |
|---|---|---|---|
| `hsm_collector_t*` | `hsm_collector_create` | `hsm_collector_destroy` | `hsm_collector_dispose` is the *graceful terminal transition*; `destroy` frees the handle. Call dispose then destroy, or just destroy. |
| `hsm_sensor_t*` | `hsm_collector_create_*_sensor` | `hsm_sensor_release` | Releasing a sensor handle frees only the handle — the collector keeps the sensor registered and the scheduler keeps driving it until the collector is destroyed. |

Out-parameters (`hsm_*_t** out_*`) are set to `NULL` on any failure, so a
consumer can check the return code or the null handle interchangeably.

## String ownership

- **Inbound** strings (`const char*` arguments: paths, comments, options) are
  borrowed for the duration of the call and copied internally as needed. The
  caller keeps ownership and may free them after the call returns.
- **Outbound** strings (`const char**` out-params from
  `hsm_collector_get_sent_json` / `get_registration_json`, and
  `hsm_collector_last_error`) point to storage **owned by the collector**. They
  are valid until the next call that mutates the same collection (or until
  destroy). The consumer must copy if it needs to retain them; it must not free
  them.
- `user_data` pointers passed to callbacks are stored verbatim and must
  **outlive the collector** (not merely the sensor handle).

## Error model

`hsm_result_t`: `OK(0)`, `INVALID_ARGUMENT(1)`, `INVALID_STATE(2)`,
`NOT_FOUND(3)`, `LIMIT_EXCEEDED(4)`, `INTERNAL_ERROR(255)`. On a non-OK result,
`hsm_collector_last_error(collector)` returns a human-readable message for the
most recent failed call on that collector (empty after a success).

## Options & validation

`hsm_collector_options_t` mirrors the managed `CollectorOptions`
(`aicontext/features/collector/public-api/feature.md`). Each numeric field uses
`0` to select the managed default; the dedup window's `0` is meaningful (log
immediately). `hsm_collector_create` validates:

- `access_key` / `server_address` required, non-blank → else `INVALID_ARGUMENT`.
- `port` in `1..65535` → else `INVALID_ARGUMENT`.
- every numeric field `>= 0` (negative → `INVALID_ARGUMENT`).

The `DataSender` transport seam is **not** exposed here — it arrives with the
HTTP transport (#1096). Until then the collector records sent payloads in memory.

## Lifecycle

State machine (mirrors the managed `CollectorStatus`):

```
Stopped --start--> Starting --(init)--> Running --stop--> Stopping --> Stopped
Any-except-Disposed --dispose--> Disposed (terminal)
```

- `hsm_collector_start` / `_stop` are idempotent; both return `OK` when already
  in the target state. Start/stop on a `Disposed` collector → `INVALID_STATE`.
- `hsm_collector_dispose` is terminal, idempotent and never fails. From an active
  state it performs the stop (firing the `Stopping`/`Stopped` notifications), then
  moves to `Disposed`. A dispose racing an in-flight stop joins that stop rather
  than duplicating it — exactly one `Stopped` notification fires, and the terminal
  mode wins.
- `hsm_collector_status` reports the current state from any thread.
- `hsm_collector_test_connection` is callable in any state; OK when the sender is
  reachable (always, for the in-memory sender), `INVALID_STATE` once disposed.

**Gates** (same phase table as the managed collector): data is accepted while
`Starting`/`Running`/`Stopping` and dropped otherwise; new sensors start (and
register) immediately only while `Starting`/`Running`; registration is rejected
(without crashing, returning a null handle) while `Stopping`/`Disposed`.

**Registration** is idempotent by path (a duplicate returns the existing handle),
rejects a type conflict (`INVALID_ARGUMENT`), and is capped at `max_sensors`
(`LIMIT_EXCEEDED`).

## Lifecycle listeners

`hsm_collector_add_lifecycle_listener` registers a
`hsm_lifecycle_callback_t(status, user_data)` invoked after each transition
(`Starting`/`Running`/`Stopping`/`Stopped`; never `Disposed`). Only transitions
after registration are delivered. The portable equivalent of the managed
`ILifecycleListener`.

## Logging & deduplication

`hsm_collector_set_logger` installs a `hsm_log_callback_t(level, message,
user_data)` sink (`DEBUG`/`INFO`/`ERROR`). Error messages pass through the
`MessageDeduplicator` first: within `exception_deduplicator_window_ms`, repeats
of the same message are collapsed and re-emitted with an `(N suppressed)` suffix;
a window of `0` logs every message immediately. The cache is bounded by
`max_deduplicated_messages` (oldest-expiry eviction). Passing a `NULL` callback
clears the sink.

## Callback isolation (host-crash safety)

Every host-supplied callback — lifecycle listeners, the log sink, function-sensor
callbacks, the scheduler's per-task action — is invoked through a swallow-all
wrapper: a throwing or crashing callback can neither cross the C ABI boundary nor
break the collector (other listeners still fire, the scheduler loop keeps
running). This is the C-ABI face of the cross-cutting invariant tracked in
`docs/initiatives/cpp-collector-port-spike.md`.

## Threading

- Sensor value methods (`hsm_sensor_add_*`) are safe to call from any thread.
- Lifecycle calls (`start`/`stop`/`dispose`) should be driven from one thread or
  serialized; they serialize internally so a dispose racing a stop is safe.
- The collector owns one scheduler worker (a single `ScheduledTask`) that sleeps
  until the earliest periodic due-time, read through an injectable monotonic clock
  (the clock seam). Periodic posts have no overlapping runs and catch up by whole
  periods.

## Versioning & ABI stability

`hsm_collector_version()` returns the packed `HSM_COLLECTOR_VERSION`
(`MAJOR*10000 + MINOR*100 + PATCH`), letting a consumer built against one header
check the linked library at runtime.

Policy:

- **MINOR** — additive, backward-compatible growth: new functions, or new fields
  **appended** to the end of `hsm_collector_options_t` (zero-initialized struct
  stays valid because `0` means "managed default").
- **MAJOR** — any breaking change: reordering/removing a field, changing a
  result-code meaning, or changing a documented behavior.
- **PATCH** — implementation-only fixes with no surface change.

Test-only symbols prefixed `hsm_collector_test_*` (and `hsm_sensor_test_*` /
`hsm_alert_test_*`) are **not** part of the ABI; they are intentionally omitted
from the public header and are linked only by the native test binary.

The public C++ RAII API (#1100, `include/hsm_collector/*.hpp`, `namespace hsm::collector`) is a
header-only convenience layer over this ABI — it adds **no** ABI surface and does **not** bump the
version. It is documented in `aicontext/features/integrations/native-collector/feature.md`, with the
C++/CLI-wrapper migration audit in `docs/native-collector-migration.md`. The package version emitted
by `find_package(hsm_collector)` tracks this ABI semver.

Version history:

- **0.10.3** (#1515) — behavior fixes, ABI unchanged. A registration POST to `/commands` that gets
  no HTTP response or a 5xx is retried on the worker's send cycle (`package_collect_period_ms`)
  until the server accepts it — the Start batch, a built-in source's runtime sensor, and a queued
  runtime registration alike, each sensor queued once however long the outage. Before, the Start
  batch was posted once: a server restarting behind its proxy (`HTTP 502`) at the collector's
  start never learned the sensors' descriptions, units, TTLs or alerts until the next restart.
  The managed command queue re-sends a failed package on the same period. The failure line gains
  `; retrying on the next send cycle.`, and the landing retry logs `Registered N sensor(s) after
  an earlier failed attempt.` at Info. A 4xx stays final. The Stop line about values dropped
  from the full send queue reads whether the Queue overflow sensor is registered from its
  registration, not from the handle snapshot, so a snapshot that throws can no longer report the
  sensor as missing. Pinned by `native_http_connect_registration_is_retried_after_5xx` and
  `native_http_restart_registration_is_retried_after_5xx`.
- **0.10.2** (#1466, #1508) — behavior fixes, ABI unchanged. The Linux free-space prediction source
  samples free space in BYTES, like the Windows one and the managed `UnixDiskInfo` (both changed
  together): the comment's `Free space decreases by X Mbytes/hour` printed GiB on Linux, a number
  1024x too small; the posted TimeSpan and `Free space on disk` are unchanged. A Stop that finds
  values dropped from the full send queue while the queue diagnostics group is NOT registered
  logs `Collector stop: N value(s) dropped from the full send queue during this run (Queue
  overflow sensor not registered).` at Error — nothing folded the counter, so N is the whole run
  and no report was made; with the group registered the Info line about drops after the final
  report is unchanged. Pinned by `native_stop_overflow_log_without_the_queue_sensor_names_the_run`
  and the managed `Unix_disk_info_reports_bytes_and_the_same_whole_megabytes`.
- **0.10.1** (#1480) — behavior fix, ABI unchanged. `.module/Collector queue stats/Package process
  time` reports what the managed collector reports under that path: per sent package, the average
  time in seconds its values waited in the send queue before the package was collected (managed
  `PackageInfo.AvrTimeInQueue`). It used to post the package's HTTP send duration. Each queued value
  is stamped when it enters the queue and keeps the stamp across a failed-send retry; file payloads are
  not averaged. The four queue-stat rows register the managed descriptions, composed from the
  collector options. Pinned by `default_sensors_contract` (queue rows) and
  `native_package_process_time_is_the_average_queue_wait`. Also: a failed-send retry dropped because
  the queue is full is now counted in `Queue overflow` (one per value, as managed #1088), and Stop
  folds the drops of the last partial collect cycle into that bar before flushing it
  (`queue_overflow_contract:requeue_drop_at_capacity_counts_as_overflow` in both drivers,
  `native_requeue_drop_at_capacity_counts_as_overflow`).
- **0.10.0** (#1416 follow-up) — one additive entry point.
  `hsm_sensor_set_description(sensor, description)` replaces a sensor's registration description
  with the same re-emission rules as `hsm_sensor_attach_alert` (below, 0.9.1): before Start it is
  what Start registers; while running the run's recorded registration is replaced in place and the
  sensor re-posted on the HTTP transport. NULL emits `"Description":null`, which the server reads as
  "unchanged" — pass `""` to clear a description it already has. For a host whose description
  carries live facts (the Linux probe's Docker memory limit). Native-only when released; since
  #1482 the managed collector has the counterpart (`IDescribableSensor.SetDescription`, the
  `SetDescription` extension on every sensor handle) and both drivers run the
  `set_sensor_description` verb in `registration_contract:set_description_*`. Pinned by `native_set_description_rebuilds_the_registration` and
  `native_http_alert_after_runtime_registration_reregisters`. Pointer lifetime: the 0.9.1
  contract is unchanged — a `hsm_collector_get_registration_json` pointer stays valid until the
  collector is destroyed, also across an alert attach while running. The new function carries its
  own rule: a text it replaces is kept only until 256 later description changes (a host that
  re-describes a sensor on every replica change must not grow memory without limit).
- **0.9.1** (#1416) — behavior fix, ABI unchanged. A sensor created through the public create
  paths while the collector runs was recorded locally (`hsm_collector_get_registration_json`) but
  never POSTed to `/commands` on the HTTP transport — only the built-in lazy sources registered at
  runtime — and an alert attached after its create call never reached the recorded registration.
  Now a runtime-created sensor is registered on the server by the worker's next dispatch cycle
  (before that cycle's values; the managed command-queue cadence), and `hsm_sensor_attach_alert`
  works while the collector runs: the sensor's registration for the run is re-recorded in place and
  re-posted when it already went out. A posted, unchanged registration is never re-sent; a failed
  runtime post that got no HTTP response is retried at the next cycle; an HTTP error answer is final
  (like managed commands, which retry transport failures only). Pinned by
  `alert_registration_contract:alert_on_sensor_created_while_running_registers_with_it` (both
  drivers), `native_http_registers_sensors_created_while_running` and
  `native_http_alert_after_runtime_registration_reregisters`.
- **0.9.0** (#1476) — one additive entry point and one path fix.
  `hsm_collector_create_enum_sensor_with_sensor_options(collector, path, options, enum_options,
  count, out)` registers an enum sensor with its option set AND the full `hsm_sensor_options_t`
  surface (TTL, `aggregate_data`, `is_computer_sensor`, …) — the managed
  `CreateEnumSensor(path, new EnumSensorOptions { … })`, e.g. the `Service status` shape
  (EnumOptions + `AggregateData = true` + an alert). Until now an enum sensor could carry enum
  options (`hsm_collector_create_enum_sensor_with_options`, description only) or sensor options
  (`hsm_collector_create_sensor_with_options`, no enum options), never both. `options` must not be
  NULL; the registration is the enum kind's (DisplayUnit null unless set). Unlike the managed
  `EnumSensorOptions` constructor it does NOT default `AggregateData` to true — the default options
  emit null, so a state sensor sets `aggregate_data = 1` explicitly. Fix:
  `hsm_collector_create_{int,double}_bar_sensor_with_options` and
  `hsm_collector_create_rate_sensor_with_options` now anchor the path by the options, as every
  instant sensor already did (`is_computer_sensor` ⇒ `<computer>/<path>`, `sensor_location` Product
  ⇒ the bare path). Before, those three always used `<computer>/<module>/<path>` and
  `is_computer_sensor` only forced `IsSingletonSensor` — a divergence from the managed
  `CalculateSystemPath`. No in-repo host set either flag on a bar or rate, so no deployed path
  moves. **Compatibility note:** an out-of-tree consumer that did set `is_computer_sensor` or
  `sensor_location = Product` on a bar or rate created with options will see that sensor register
  at the new, managed-parity path after upgrading (a new server node; history stays on the old
  one). Shipped as a MINOR bump because the old path contradicted the documented meaning of those
  option fields — a bug fix, not a changed contract. Pinned by `options_surface_contract:enum_full_options_*`,
  `bar_options_contract:bar_{computer,product}_sensor_*` and
  `rate_options_contract:rate_{computer,product}_sensor_*` in both drivers.
- **0.8.2** (#1453, #1444, #1437, #1459, #1460) — no ABI change; four behavior fixes and one
  thread-safety contract. `hsm_collector_last_error` now returns a pointer into a THREAD-LOCAL
  copy of the message instead of into the collector's own storage, which its workers could
  reallocate under a reader: the signature is unchanged and every existing caller becomes safe
  without recompiling against anything new, but the returned pointer is now documented as valid
  only until the SAME thread calls the function again (a caller that held it across another
  thread's call was already reading freed memory). The buffer is one per THREAD, shared by
  every collector handle, so a call for one collector overwrites the text a previous call for
  another returned on that thread; a NULL handle still returns the static literal it always
  did. The self-monitoring sensor handles are
  published under a mutex, so adding the collector-monitoring or queue group after `Start` no
  longer races the self-monitor thread. `.module/Service alive` beats on the sensor's own post
  period instead of the package-collect period, and `.module/Collector queue stats/Package
  content size` registers `Unit.KB` and reports kilobytes. The disk-prediction COMMENT prints
  the rate in MB/hour with up to six decimals (the sensor value is unchanged).
- **0.8.1** (#1445) — no ABI change. The disk-space prediction math behind
  `HSM_DEFAULT_FREE_DISK_SPACE_PREDICTION` / `HSM_DEFAULT_UNIX_FREE_DISK_SPACE_PREDICTION` was
  rewritten (signed drain EMA over a six-hour window sampled every 10 min, five explicit posted
  states, a 365-day ceiling, calibration counted on the sampling clock) to mirror the managed fix. A host that only links the ABI sees the same
  entry points; a host that reads those sensors' values sees the new states.
- **0.8.0** (#1426) — additive typed metric sources. The double-only seam could not say WHY a read
  failed (so a broken source degraded silently, against root rule #8) and could not carry a
  non-double value (so the TimeSpan-typed disk-space prediction had no live value). Added:
  `HSM_METRIC_READ_SAMPLE_ERROR` (this read failed, the source stays), `hsm_metric_value_kind_t`,
  `hsm_metric_sample_t` (typed value + status + comment + error text, with `struct_size`),
  `hsm_metric_read_sample_fn`, `hsm_metric_source_t` (`read`/`read_sample`/`refresh` +
  `refresh_period_ms` + `dispose` + `user_data`, with `struct_size`),
  `hsm_metric_source_factory_ex_fn` and `hsm_collector_set_metric_source_factory_ex`. The original
  `hsm_metric_read_fn` / `hsm_metric_source_factory_fn` / `hsm_collector_set_metric_source_factory`
  are UNCHANGED in signature and semantics — a source that fills only `read` behaves exactly as
  before — so the Windows PDH factory, the Linux `/proc` factory and any host plugin keep linking.
  The two setters share one factory slot (installing either replaces the other). `kind` is ENFORCED
  against the bound sensor's type, and a source-supplied `status`/`comment` gets the same
  range/trim guards as every other value path. Behavior growth on top of the ABI: a reported read
  failure now reaches the deduplicated log AND the `.module/Collector errors` sensor when
  registered, and a value-typed sensor posts one Error-status value carrying the message (what
  managed already did).
- **0.4.0** (#1099) — additive default-sensor catalog: `hsm_default_sensor_t` (the
  built-in IWindowsCollection/IUnixCollection prototypes) + `hsm_default_sensor_params_t`
  + `hsm_collector_add_default_sensor` and the `add_all_*` / per-category bulk helpers;
  each id registers a byte-identical `AddOrUpdateSensorRequest` (path/type/unit/statistics/
  keep-history/TTLs/aggregate/grafana/singleton/EnumOptions + default alerts). Plus the
  metric-source seam (`hsm_collector_set_metric_source_factory` + `hsm_metric_read_fn`/
  `hsm_metric_dispose_fn`/`hsm_metric_source_factory_fn`): the IPerformanceCounter
  equivalent a default monitoring sensor reads each tick, with recreate-on-error +
  dispose-on-stop. The production factory is a no-op — the real PDH/WMI/registry/EventLog
  (Windows) and procfs (Linux) readers, and the per-sensor scheduled-tick wiring, are the
  #1099 live-value follow-up. GC-time sensors are intentionally dropped (no managed GC in a
  native host); the Unix surface is the managed parity subset.
- **0.3.0** (#1098) — additive sensor machinery: TimeSpan (type 7) / Version (type 8)
  instant sensors; the alert builder (`hsm_collector_create_alert` + `hsm_alert_*` +
  `hsm_sensor_attach_alert`); the full options surface
  (`hsm_collector_create_sensor_with_options` + `hsm_sensor_options_t`: KeepHistory/
  SelfDestroy/DisplayUnit/Statistics/IsSingletonSensor/AggregateData/EnableGrafana +
  IsComputerSensor/SensorLocation path model); and the service-commands sensor
  (`hsm_collector_create_service_commands_sensor` + `hsm_service_commands_send_*`).
  `hsm_alert_t` is an opaque handle owned by the collector (freed at destroy, no
  separate release); attaching rebuilds the payload (before 0.9.1 it had to happen before the
  registration was emitted; since 0.9.1 an attach while running re-registers the sensor).
- **0.2.0** (#1096) — HTTP transport options consumed; wire serialization.
- **0.1.0** (#1095) — initial lifecycle, scheduler, logging, registration core.

## Alerts (registration payload)

The alert builder ports the managed `HSMDataCollector.Alerts` model at the
**registration-payload** level. `hsm_alert_add_condition` takes the frozen numeric
`property/operation/combination/target` enums directly (the C# `IfValue`/`IfMax`/…
sugar that selects those values is not part of the ABI). `hsm_alert_set_icon` maps
`hsm_alert_icon_t` to the same UTF-8 emoji as `IconExtensions.ToUtf8`; the wire
serializer escapes it to `\uXXXX` exactly like System.Text.Json. A TTL alert
(`HSM_ALERT_KIND_TTL` + `hsm_alert_set_inactivity_period`) lands in `TtlAlerts` and
drives `TTLs` (ticks). Byte parity with .NET is pinned by the paired golden tests
(`WireFormatGoldenLockTests` / `NativeWireRegistrationWithAlertsMatchesNetByteLayout`).
