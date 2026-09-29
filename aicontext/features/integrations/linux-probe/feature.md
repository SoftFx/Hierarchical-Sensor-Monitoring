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

**No computer node; the module node is `.probe`** (owner decisions 2026-09-29, #1493 and #1496:
one product = one host). The product root holds `.computer/…` (the host) and `.probe/` with
`.module/…` and `Docker/…` — the .NET layout with an empty `ComputerName`. `hsm.computerName` is
empty and `hsm.module` is `.probe` by default; the server bundle writes neither. Other values are
accepted, not recommended. Linux probe only — the Windows agent keeps
`<MACHINE>/HSM Agent/.module`.

The probe registers **two separately pinned sets**:

1. **The parity set** — exactly what the managed `HSMDataCollector` registers on Linux
   (`UnixSensorsCollection.AddAllDefaultSensors`), 15 paths. The full parity table — every path,
   type, unit, alert, posting cadence and whether a live value arrives, captured from both
   collectors against one fake Sensor API — is the contract, and lives in
   [`src/probe-linux/README.md`](../../../../src/probe-linux/README.md); pinned by
   `probe::tests::the_registered_set_is_exactly_the_managed_unix_default_set`.
2. **The probe-only set** (#1476) — sensors the owner agreed one by one that exist **only in the
   Linux probe**, never in the shared collector catalog and never on Windows (moving one into the
   catalog is a separate, later decision). Host: `.computer/Logical cores`,
   `.computer/CPU temperature` (computer-level). Pinned by `PROBE_ONLY_SET` in
   `probe::tests::the_registered_set_is_the_parity_set_plus_the_probe_only_set` ("nothing more,
   nothing less"), their registration and alerts by
   `probe::tests::probe_only_sensors_register_their_agreed_shape_and_alerts`. Sources, periods,
   alerts and costs: README "Probe-only sensors".
   **Disks** (#1481, part of the probe-only set) — five sensors per mounted real filesystem under
   `.computer/Disks monitoring/`, named like the Windows per-drive sensors with a name for the
   letter (`root` for `/`, else the last mount-path segment; collisions → the whole path with
   `/` → `_`, then a counter; the mount point → name map is persisted in
   `$STATE_DIRECTORY/disk-names.json`, so a mount point's name never changes, restarts included;
   the name follows the mount point, not the device):
   `Free space on <name> disk` (MB, EMA, no absolute-size alert), `… disk %`,
   `Free inodes on <name> disk %`, `Average disk write speed on <name> disk` (MBytes_sec bar from
   `/proc/diskstats` of the whole disk) and `Written per day on <name> disk` (#1485, once a day
   since #1498: decimal GB written to that whole disk during one local day, from the same counter,
   **posted once** in the day's last 30 s — TTL 26 h, a day that ended while the probe was down is
   not posted; from 0 at midnight; the day and the counters persist in `$STATE_DIRECTORY/disk-written.json`
   with the boot id and each disk's identity — WWID/serial, else its mount points — so a restart
   continues the day, a reboot keeps the day (only for the same physical disk) but not the
   counters, and a disk renamed by the kernel never inherits another disk's day). Block-backed types only, deduplicated by source device,
   re-scanned every 10 min (new mounts register at runtime; removed ones time out; every 5-min
   sample re-checks the mount table first; automounted and over-mounted filesystems are skipped —
   `ProtectHome=yes` unmounts a separate `/home` from the service's namespace, so it is not
   reported; logged once at WARN when `/etc/fstab` lists it). The archives
   are `statvfs`'d directly — measured not to wake sleeping disks; nothing under a mount is ever
   opened, listed or read. Pinned for garage-server (4 filesystems, 20 paths) by
   `DISKS_GARAGE_SET`, built from captured `mountinfo`/`diskstats`/sysfs
   (`probe_only/disks/fixtures/`). The managed-parity `Free space on disk` (+ prediction) is a
   different sensor and untouched.
3. **The Docker Compose tree** (#1416, part of the probe-only set) — seven sensors per Compose
   service under `.probe/Docker/<project>/<service>/`: `CPU` and `Memory used %` (5-minute bars
   of 5-second samples; CPU as % of the whole host; the memory limit is stated in the `Memory used %`
   description and follows a changed limit), `Disk written per hour` (decimal MB the service's
   containers wrote to block devices in one UTC clock hour, from the cgroup write counters in the
   same stats; sent just after the hour — the comment names the window; the running hour survives
   a probe restart through the state file; no alert, EMA statistics), `Service status` (the Windows
   `ServiceControllerStatus` enum and its alert), `Health` (only where a healthcheck exists),
   `Restart count` (posted on change) and `OOM killed` (latched 24 h). Source: the Docker Engine
   API over its Unix socket (`probe_only/docker/`). Services present at start register before
   Start; later ones at runtime. A service first seen as a completed one-shot job (every
   container Exited (0), restart policy `no`) is not monitored until it runs. Pinned for
   garage-server's captures (12 Compose containers, 11 monitored services, 70 paths) by
   `DOCKER_GARAGE_SET`.

Probe-only sensors go through the collector's public sensor API, so wire format, queuing,
batching, retry and TLS stay the library's; only the acquisition (a sysfs read, a `statvfs`, an
Engine API call) and the schedule live in the probe.

**Alerts.** The wrapper binds the collector's alert DSL: `Collector::alert(kind)` →
`AlertBuilder` (conditions, notification / scheduled notification, icon, `sensor_error`,
confirmation / inactivity period, `disabled`) → `attach_alert` on any sensor handle. An alert is
part of the sensor's registration: before Start it rides the Start batch; while the collector runs
(collector 0.9.1) attaching re-records the registration and the live transport re-posts it, which
is how a sensor created at runtime (a Compose service seen after start) gets its alerts. Attaching
is refused only while the collector stops. HSM alerts
can only raise a sensor to Error; a "warning" is a notification with the ⚠ icon and no status
change, as in the managed Total CPU / Free RAM defaults. Enum state sensors use
`Collector::enum_sensor_with_options` (collector 0.9.0) — EnumOptions + SensorOptions, the managed
`Service status` shape; `aggregate_data` must be set explicitly (it is not defaulted to true).

**Configuration.** `probe.hostSensors.{enabled, cpuTemperature}`,
`probe.disks.{enabled, exclude, writeSpeed}` (`exclude`: mount-point patterns with `*`) and
`probe.docker.{enabled, socket, composeOnly, samplePeriodSec, oomLatchHours, exclude}` (`exclude`:
`project/service` patterns with `*` wildcards, not monitored and dropped from the state), switches all
default `true` (and the `exclude` lists empty), so a config without those sections turns everything
on. The host switches covered the disk sensor before 0.4.0, so while `probe.disks.enabled` is unset a
`hostSensors.enabled: false` or the deprecated `hostSensors.disk: false` still disables the disks
(an upgrade never switches them back on); an explicit `probe.disks.enabled` wins.

**Packaging.** `src/probe-linux/packaging/build-deb.sh <version>` builds the `.deb` in a plain
`debian:13` container (layout `/usr/bin`, `/lib/systemd/system`, the skeleton at
`/usr/share/hsm-linux-probe/config.example.json` — **no conffile** since 0.3.1: postinst seeds
`/etc/hsm-linux-probe/config.json` only when it is absent, so no upgrade stops at a prompt or touches
the operator's file; purge removes it; `Depends:
libcurl4t64, ca-certificates, libc6, libstdc++6, libgcc-s1`, checked against the binary's shared
libraries). A fresh install creates the `hsm-probe` user and does not start the unit; an upgrade
restarts it if it was running (prerm leaves a `/run` marker, postinst starts it). Docker socket
access is a drop-in (`hsm-linux-probe.service.d/docker.conf`, `SupplementaryGroups=docker`) that
postinst writes (fresh install / the upgrade from before 0.3.0 only, so an operator's removal sticks) through
`/usr/lib/hsm-linux-probe/docker-access.sh` only where a `docker` group
exists — never in the unit, which would then not start on a host without one.

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
  once until it recovers — never posted as a value (no 0 for "unknown"). So that a source which
  stops producing is visible on the server, every probe-only sensor carries a TTL — three periods
  (15 min) for the 5-minute sensors, 48 h (one missed day) for the daily `Logical cores` — and
  turns to Timeout. Every `statvfs` — at registration and when sampling — runs on a helper
  thread with a 5 s deadline, and a filesystem whose last `statvfs` is still blocked is not asked
  again, so a hung mount can neither hold up Start nor stall the other disks.
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
- **The Docker source only reads.** Its HTTP client has no method but `GET` and builds requests from
  a closed set of four Engine API endpoints (container ids validated as hex); the socket is
  root-equivalent, so this is enforced by construction and review. Each call is bounded (1.5 s,
  under the 2 s source stop wait).
- **The Docker source never invents a value.** A skipped CPU delta (first sample, counter reset, new
  container id, interval outside ½…3× the period), a failed or timed-out inspect/stats call, or an
  unreachable daemon posts nothing — never 0, `false` or a stale state — and is logged once per
  condition; one wedged container never blanks the other services.
- **Docker identity is the Compose `(project, service)`,** not the container: nodes survive
  recreate (and restarts: each node is remembered and re-adopted), a restart count never goes
  down, an OOM latch survives recreate, and `docker compose run` one-offs are not replicas. State
  is `$STATE_DIRECTORY/docker-state.json`, written atomically and only on change (the running
  `Disk written per hour` accumulator at most every 5 minutes, at each posted hour and on stop).
- **An hourly total is posted once, for the hour that just ended.** It is written to the state
  file right after posting (no double post after a restart). The hour that has just ended is
  posted on the first tick after it, also when that tick is the first after a restart (its bytes
  were measured); an older hour is dropped with an INFO line; an hour without a single measured
  delta is skipped, never 0. A sample read after the boundary in a tick that rolled before it
  waits for the next tick, so no write of a new hour is credited to the old one.

---

## Related

- `integrations/native-collector/feature.md` — the C++/C ABI surface the probe consumes.
- `agent/feature.md` — the Windows host of the same collector; the probe mirrors its wiring order.
- `collector/` — all wire semantics, and the semantics of every parity sensor; the probe adds none
  (its probe-only sensors are plain instant/bar sensors on the collector's public API).
