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
  separate release); alerts must be attached before the registration is emitted
  (pre-Start or pre-create-while-running) since attaching rebuilds the payload.
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
