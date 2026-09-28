# Feature: Linux Probe (`hsm-linux-probe`)

> Owner: integrations | Last reviewed: 2026-09-28 | Canonical: yes
> Scope: The systemd-hosted Linux host probe in `src/probe-linux/` — a Rust process that hosts the native collector through its stable C ABI. Owns the host wiring (config, secrets, logging, lifecycle, packaging), the sensor set it registers, and the acquisition of its probe-only sensors; owns no wire semantics.

---

## Description

The probe (epic #1413, workstream #1415) reports a Debian host into an existing HSM server. It is
**plumbing, not a sensor library**: metric acquisition lives in the shared native collector
(`src/native/collector`), which the probe hosts through `hsm_collector.h`. This is the third native
host of that collector, after `src/agent` (Windows service) and `src/wrapper` (aggregator).

Three crates under `src/probe-linux/`:

| Crate | Role |
|---|---|
| `hsm-collector-sys` | Hand-written FFI declarations for the ABI subset the probe uses; builds the collector with CMake (`HSM_COLLECTOR_HTTP=ON`) and links it |
| `hsm-collector` | Safe RAII wrapper: `Collector`, typed sensor handles, alert builders, log sink |
| `hsm-linux-probe` | The binary: config, secrets, logging, signals, lifecycle, sensor registration, probe-only sources (`src/probe_only/`) |

The probe registers **two separately pinned sets**:

1. **The parity set** — exactly what the managed `HSMDataCollector` registers on Linux
   (`UnixSensorsCollection.AddAllDefaultSensors`), 15 paths. The full parity table — every path,
   type, unit, alert, posting cadence and whether a live value arrives, captured from both
   collectors against one fake Sensor API — is the contract, and lives in
   [`src/probe-linux/README.md`](../../../../src/probe-linux/README.md); pinned by
   `probe::tests::the_registered_set_is_exactly_the_managed_unix_default_set`.
2. **The probe-only set** (#1476) — sensors the owner agreed one by one that exist **only in the
   Linux probe**, never in the shared collector catalog and never on Windows (moving one into the
   catalog is a separate, later decision). Today: `.computer/Logical cores`,
   `.computer/CPU temperature`, `.computer/Disks monitoring/Free space on disk %` and
   `… /Free inodes %` (computer-level). Pinned by `PROBE_ONLY_SET` in
   `probe::tests::the_registered_set_is_the_parity_set_plus_the_probe_only_set` ("nothing more,
   nothing less"), their registration and alerts by
   `probe::tests::probe_only_sensors_register_their_agreed_shape_and_alerts`. Sources, periods,
   alerts and costs: README "Probe-only sensors". Next: the Docker source (#1416), registered from
   its own `probe_only::docker` module under `Docker/<project>/<service>/…`.

Probe-only sensors go through the collector's public sensor API, so wire format, queuing,
batching, retry and TLS stay the library's; only the acquisition (a sysfs read, a `statvfs`) and
the schedule live in the probe.

**Alerts.** The wrapper binds the collector's alert DSL: `Collector::alert(kind)` →
`AlertBuilder` (conditions, notification / scheduled notification, icon, `sensor_error`,
confirmation / inactivity period, `disabled`) → `attach_alert` on any sensor handle. An alert is
part of the registration the collector emits at Start, so `attach_alert` is **refused unless the
collector is stopped** (a late attach would register the sensor without it, silently). HSM alerts
can only raise a sensor to Error; a "warning" is a notification with the ⚠ icon and no status
change, as in the managed Total CPU / Free RAM defaults. Enum state sensors use
`Collector::enum_sensor_with_options` (collector 0.9.0) — EnumOptions + SensorOptions, the managed
`Service status` shape; `aggregate_data` must be set explicitly (it is not defaulted to true).

**Configuration.** `probe.hostSensors.{enabled, cpuTemperature, disk}`, all default `true`, so a
config written before the probe-only sensors turns them on.

**Packaging.** `src/probe-linux/packaging/build-deb.sh <version>` builds the `.deb` in a plain
`debian:13` container (layout `/usr/bin`, `/lib/systemd/system`, the `/etc` conffile; `Depends:
libcurl4t64, ca-certificates, libc6, libstdc++6, libgcc-s1`, checked against the binary's shared
libraries). A fresh install creates the `hsm-probe` user and does not start the unit; an upgrade
restarts it if it was running (prerm leaves a `/run` marker, postinst starts it).

Linux is the only supported target. The initiative is
[`docs/initiatives/linux-docker-probe.md`](../../../../docs/initiatives/linux-docker-probe.md).

---

## Invariants

- **No probe-local reimplementation of any sensor the collector has.** A second implementation next
  to the managed one is the divergence class rules #9/#10 forbid. A probe-only sensor must not
  shadow a parity path (the pinned test checks it) and exists only by explicit owner agreement.
- **Probe-only sources are isolated.** Each registers its sensors (with their alerts) before Start
  and then samples on a thread of its own, so a read blocked on a hung filesystem cannot stall
  another source; every sample runs under `catch_unwind`; a failed read is skipped and logged
  once until it recovers — never posted as a value (no 0 for "unknown"). Registration-time
  filesystem probing that could block (the disk source's `canonicalize` + `statvfs`) runs on a
  helper thread with a deadline, so a hung mount cannot hold up Start of the parity set.
- **Stop is bounded around the sources.** On SIGTERM the sources are signalled and waited for at
  most 2 s; stuck ones are named in the log, the collector drains anyway, and the process then
  exits without joining a thread that is still blocked in a read.
- **The process node is the fixed `.module/Process process`**, as in HsmAgent — one HSM alert
  template applies to every native host (#1429, closed by-design). It is never named per process.
  `Process ThreadPool thread count` is omitted: a native process has no CLR thread pool.
- **Metric-fed sensors are registered only when the build can feed them.** The host catalog and the
  process sensors are driven by the collector's metric-source factory; without the Linux metric
  sources (#1414, cargo feature `linux-default-sensors`) they would be permanently empty nodes, so
  the probe skips them and says so at startup.
- **The ABI mirrors are checked against the real header at build time.** `hsm-collector-sys/build.rs`
  compiles `static_assert`s over `hsm_collector.h` for the size, alignment and every field offset of
  each mirrored struct, and pins the header version against the linked library. A collector that
  appends a struct field — a MINOR bump under `src/native/collector/CLAUDE.md` — fails the probe
  build instead of silently corrupting the stack.
- **No panic crosses the C ABI.** Every `extern "C"` callback body is wrapped in `catch_unwind`.
- **Sensor handles cannot outlive their collector** — they borrow it, so the use-after-free the raw
  ABI allows is a compile error.
- **The access key is never in the config, in argv, in a log or in an error message.** It is read at
  startup from `hsm.accessKeyFile` (relative = a systemd `LoadCredential=` credential, resolved
  against `$CREDENTIALS_DIRECTORY`), and every in-process copy is wiped after the collector takes it.
- **Transport is HTTPS-only.** Config validation accepts `https://<host>` or a bare host; the wrapper
  deliberately does not expose the ABI's `allow_untrusted_server_certificate`.
- **Values dropped by the collector's bounded stop drain are logged at WARN** (the collector reports
  them at debug), per rule #8.

---

## Related

- `integrations/native-collector/feature.md` — the C++/C ABI surface the probe consumes.
- `agent/feature.md` — the Windows host of the same collector; the probe mirrors its wiring order.
- `collector/` — all wire semantics, and the semantics of every parity sensor; the probe adds none
  (its probe-only sensors are plain instant/bar sensors on the collector's public API).
