# HSM Linux probe (`hsm-linux-probe`)

A systemd-hosted Linux host probe that reports into an existing HSM server by **hosting the shared
native collector** (`src/native/collector`) through its stable C ABI.

Architecture and rationale: [`docs/initiatives/linux-docker-probe.md`](../../docs/initiatives/linux-docker-probe.md)
(epic #1413). This directory is workstream 2 (#1415) — the skeleton.

## The one rule that shapes everything here

**The probe implements no sensor the collector already has.** Metric acquisition lives in the
shared collector, conformance-locked against the managed one (root `CLAUDE.md` rules #9/#10); a
probe-local reimplementation would be a permanent semantic/wire divergence.

**Two sets, pinned separately.** The probe registers exactly the sensor set the managed
HSMDataCollector registers on Linux (`UnixSensorsCollection.AddAllDefaultSensors`: computer set +
module set — the [parity contract](#parity-contract)), **plus** a set of
[probe-only sensors](#probe-only-sensors) agreed one by one with the owner. Probe-only sensors
exist only here — never in the shared collector catalog, never on Windows — and go through the
collector's public sensor API, so wire format, queuing, batching, retry and TLS stay the library's;
only the acquisition (a sysfs read, a `statvfs`, a Docker Engine API call) and the schedule live in
the probe. The archive/backup part of #1417 is the next probe-only source.

The process node name is fixed as `.module/Process process`, the same as HsmAgent, so alert
templates apply across hosts — do not rename it. As in `src/agent`, the process sensors are
registered one by one and `Process ThreadPool thread count` is omitted (a native process has no
CLR pool); the probe posts `.module/Version` itself since the module group helper is not used.

## Parity contract

The registered set is pinned by `probe::tests::the_registered_set_is_exactly_the_managed_unix_default_set`
(the exact path list; the computer set is asserted when built with `linux-default-sensors`).

**How this table was produced.** Both collectors ran for 330 s in Linux containers against the same
fake Sensor API, and every `/commands` registration and `/list` value was captured and diffed:
the managed collector from `src/collector` via `collector.Unix.AddAllDefaultSensors(new Version(0,1,0))`
on .NET 8; the probe via `probe::tests::parity_capture` (an `#[ignore]`d audit test — rerun it with
`HSM_PARITY_ADDRESS`/`HSM_PARITY_PORT` set) linked against the #1414 collector (`4889f3f`,
collector 0.7.0) with `linux-default-sensors` on — the build the garage-server trial ran.

**Registration is byte-identical** for all 15 shared paths: `SensorType`, `OriginalUnit`,
`DisplayUnit`, `TTLs`, `KeepHistory`, `SelfDestroy`, `Statistics`, `AggregateData`, `EnableGrafana`,
`IsSingletonSensor`, `DefaultAlertsOptions`, `EnumOptions`, `Alerts` and `TtlAlerts` all match.
(`Description` is outside the byte contract by design — the managed text interpolates machine data.)
The registration carries no bar period, post period or tick; those columns are what the captured
values show. Every divergence is in value delivery.

Paths are relative to `<computer>/` (`.computer/…`) or `<computer>/<module>/` (`.module/…`).
TTL is "none" for every sensor except `Service alive`, which carries the inactivity alert.

| Sensor | Type · unit | Alerts / KeepHistory (both sides) | Bars / posting: managed → native | Live value: managed / native | Status |
|---|---|---|---|---|---|
| `.computer/Total CPU` | DoubleBar · % | EMA mean > 50 | 5-min bar, partial post every ~15 s (3 samples / 15 s) → 15-s bars (4 samples each), posted every ~20 s | yes / yes | **Known gap #1428** |
| `.computer/Free RAM memory` | DoubleBar · MB | EMA mean < 2 | as Total CPU | yes / yes | **Known gap #1428** |
| `.computer/Disks monitoring/Free space on disk` | Double · MB | EMA value ≤ 20480 → Error | every 5 min → every 5 min | yes / yes (values agree) | Parity |
| `.computer/Disks monitoring/Free space on disk prediction` | TimeSpan | — | every 5 min → — | yes (`00:00:00`) / **no** | **Known gap #1426** |
| `.module/Process process/Process CPU` | DoubleBar · % | — | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process process/Process memory` | DoubleBar · MB | EMA mean > 30720 | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process process/Process thread count` | DoubleBar | EMA mean > 2000 | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process <name>/ThreadPool thread count` | DoubleBar | EMA mean > 2000 | managed only | yes / not registered | **Intentional** — no CLR pool in a native process |
| `.module/Service alive` | Bool | TTL inactivity alert → Error · 180 d | every ~15 s → every ~15 s | first `False`, then `True` / `True` from the first post | **Finding F3** |
| `.module/Collector version` | Version | 5 y | on Start and Stop → on Start | `3.5.0.0`, `Start:`/`Stop: dd/MM/yyyy HH:mm:ss` / `0.7.0`, `Start: <ISO-8601>` only | Value: intentional (independent collector version); **F4** comment/Stop; **F1** |
| `.module/Collector errors` | String | — | on error → on error | none / none (clean run) | Parity |
| `.module/Collector queue stats/Items count in package` | IntBar · count | — | 5-min bar, partial post every ~15 s → one post per 5-min bar, at its close | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Package content size` | DoubleBar · KB (MB until #1459) | — | as Items count | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Package process time` | DoubleBar · s | — | as Items count | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Queue overflow` | IntBar · count | — | on overflow → on overflow | none / none (no overflow) | Parity |
| `.module/Version` | Version | 5 y | on Start and Stop → on Start and Stop (probe-posted) | `0.1.0`, `Start:`/`Stop: dd/MM/yyyy HH:mm:ss` / same format; the Stop post is lost to **F1** | Probe matches managed; **F1** |

**Findings that need collector work** (not fixable in the probe):

- **F1 — High.** The stop drain never delivers on a real transport. `StopWorker()` calls
  `http_transport_->Cancel()` to unblock a hung send; the flag stays set, so the `DrainQueueOnStop()`
  that follows sends through a cancelled transport ("Operation was aborted by an application
  callback") and drops everything: the stop-flushed partial bars, last-value snapshots and anything
  posted just before Stop (e.g. the `Version`/`Collector version` `Stop:` values). Observed on a
  healthy server: `Collector stop dropped 6 pending value(s)`. Every restart loses data (rule #8), and
  the #1107 "flush partial bar on Stop" contract does not hold over HTTP. The conformance corpus
  cannot see it: it runs on the in-memory sender. Present on master too.
- **F2 — Medium, #1428 family.** Non-metric bars (queue stats) keep the 5-min window but get no
  periodic partial post, so they surface only once per 5 min; the managed collector posts the
  running partial every ~15 s. Different symptom from the metric bars in #1428 (which shortened the
  window instead) — the #1428 fix should cover both.
- **F3 — Low.** `Service alive`: the managed `CollectorAlive` posts `False` on its first tick as a
  start marker, then `True`; the native heartbeat posts `True` from the first post.
- **F4 — Low.** `Collector version` comment: the native collector writes `Start: <ISO-8601>` and no
  `Stop:` value; managed writes `Start:`/`Stop: dd/MM/yyyy HH:mm:ss`. (The version *value* differs by
  design: the native collector reports its own independent version.)

## Probe-only sensors

Sensors that exist **only in this probe** (owner decisions of 2026-09-24, #1476, #1416). They are
pinned separately from the parity set by
`probe::tests::the_registered_set_is_the_parity_set_plus_the_probe_only_set` (`PROBE_ONLY_SET` +
`DOCKER_GARAGE_SET`, nothing more, nothing less), and their registration shape and alerts by
`probe::tests::probe_only_sensors_register_their_agreed_shape_and_alerts` and
`docker::tests::prime_registers_the_whole_tree_before_start_with_alerts`.

### Host and disk (#1476)

All four are computer-level (`is_computer_sensor`), so they sit under `<computer>/.computer/…`.

| Path | Type · unit | Period | TTL | Alerts (registered with the sensor) | Source | Records/day |
|---|---|---|---|---|---|---|
| `.computer/Logical cores` | Int | at start + every 24 h | 48 h | — | `sysconf(_SC_NPROCESSORS_ONLN)` (what `nproc` shows) | ≈ 2 |
| `.computer/CPU temperature` | DoubleBar · °C (no `Unit` code exists; said in the description) | a sample every 5 s into a 5-min bar (60 samples) | 15 min | Mean in (80, 90] → warning; Mean > 90 → **Error** | rule below | 288 |
| `.computer/Disks monitoring/Free space on disk %` | Double · Percents | every 5 min | 15 min | value in [5, 10) → warning; value < 5 → **Error** | `statvfs`: `f_bavail / f_blocks` of the mount holding `/srv/docker` | 288 |
| `.computer/Disks monitoring/Free inodes %` | Double · Percents | every 5 min | 15 min | value < 10 → warning | `statvfs`: `f_favail / f_files`, same mount | 288 |

*Legend:* **warning** = a notification with the ⚠ icon and **no status change**; **Error** = a
notification that also sets the sensor to Error. **TTL** = three periods for the 5-minute sensors (48 h, one
missed day, for the daily cores value): a source that stops producing — a read that keeps failing, a thread stuck on a hung
filesystem — turns the sensor to Timeout on the server instead of leaving its last value looking
fresh.

**Alert semantics.** Every alert notifies (the managed "instant hourly" schedule: the first
notification at once, repeats hourly while it holds — the same action as the managed default
alerts). HSM alerts can only raise a sensor to **Error**; the server offers no "set Warning" action
and neither does the managed alert DSL. So a *warning* is the notification with the ⚠ icon and no
status change — exactly like the managed Total CPU / Free RAM alerts — and an *error* also sets the
sensor to Error. The warning bands stop where the error band starts, so a single reading never
sends both.

**CPU temperature source rule** (deterministic, first match wins; `probe_only/host.rs`): hwmon
`coretemp` `temp<N>_input` whose label is exactly `Package id 0` → `/sys/class/thermal` zone of type
`x86_pkg_temp` → the first thermal zone whose type contains `cpu`. Directories are visited in
numeric order. Millidegrees → °C. Only the `name`/`type`/`label` attributes of other devices are read,
never their inputs (a `drivetemp` input would wake a sleeping disk). With no source the sensor is
**not registered** (one INFO line). A failed read skips the sample — never a 0 °C — and is logged
once until it recovers.

**Disk mount rule** (`probe_only/disk.rs`): the mount whose mount point is the longest
component-wise prefix of the canonical `/srv/docker` in `/proc/self/mountinfo` (the top one when
mounts are stacked; a missing target is walked up to its nearest existing ancestor). Resolved once
at registration and logged. The shared collector's `Free space on disk` always reports
`statvfs("/")` (it mirrors the managed `UnixDiskInfo`); where `/srv/docker` is on the root
filesystem — garage-server — both describe the same mount, and the log says so; where it is not,
the probe logs that the two differ. Only that one mount is ever `statvfs`'d; a filesystem without
an inode count (`f_files == 0`) gets no inode sensor.

**Isolation.** Each source runs on its own thread (a hung `statvfs` cannot stall the temperature
samples), every sample runs under `catch_unwind`, and nothing is posted before the collector starts.
The disk source's registration-time probing (`canonicalize` + one `statvfs`) runs on a helper
thread with a 5 s deadline, so a hung mount cannot hold up the start of the parity set (the disk
sensors are then not registered, with an ERROR line). On stop the sources get 2 s; stuck ones are
named, the collector drains anyway, and the process exits without joining a thread still blocked
in a read.

**Configuration** — `probe.hostSensors` (all default `true`, so a config written before these
sensors existed turns them on): `enabled` switches off all four; `cpuTemperature` and `disk` switch
off one source each. `Logical cores` has no switch of its own.

### Docker Compose services (#1416)

Everything lives under one node in the probe's module:
`<computer>/<module>/Docker/<project>/<service>/<sensor>`, e.g.
`garage-server/LinuxProbe/Docker/gitea/db/Service status`. Source: the Docker Engine API on
`probe.docker.socket` (default `/var/run/docker.sock`); every number below is in
`hsm-linux-probe/src/probe_only/docker/contract.rs`, every alert in `…/docker/alerts.rs`.

| Sensor | Type · unit | Cadence | Value | Alert (at registration) | Records/day |
|---|---|---|---|---|---|
| `CPU` | DoubleBar · % | sample every 5 s (`probe.docker.samplePeriodSec`), 5-min bar | % of the **whole host** (all cores = 100 %): Δ`cpu_usage.total_usage` / Δ`system_cpu_usage` × 100. Not × `online_cpus` — `docker stats` shows per-core % (up to 400 % on 4 cores) | mean > 90 for 30 min → warning notification | 288 |
| `Memory used %` | DoubleBar · % | as CPU | (`usage` − `inactive_file`) / limit × 100; no limit ⇒ of the host's `MemTotal` | mean > 90 → warning notification | 288 |
| `Memory limit` | Int · MB | at probe start and on change | the containers' limit; host `MemTotal` when unlimited (the value's comment says so) | none | ~2 |
| `Service status` | Enum (the Windows `ServiceControllerStatus` options) | poll every 60 s, AggregateData | running → Running; created, restarting → StartPending; paused → Paused; exited, dead, removing → Stopped; removed → Stopped for 7 days after last seen. A service first seen as a **completed one-shot job** — every container Exited (0) under restart policy `no` — is not monitored at all (see below) | `IfValue NotEqual Running`, confirmation 5 min, notification repeated hourly — the Windows `ServiceStatusPrototype` alert byte for byte (test-pinned) | ~0 |
| `Health` | Enum {starting, healthy, unhealthy} | poll every 60 s, AggregateData | `State.Health.Status`; registered **only** where a healthcheck exists | `unhealthy` for 5 min → notification, repeated hourly | ~0 |
| `Restart count` | Int · count | poll every 60 s, **posted only on change** | cumulative `RestartCount`, carried across recreates (never goes down) | value changed (`IsChanged`, so a new service's first baseline post does not notify) → notification | ~0 |
| `OOM killed` | Bool | poll every 60 s, AggregateData | `State.OOMKilled`, latched true for 24 h (`probe.docker.oomLatchHours`), across recreates | true → Error + notification | ~0 |

Cost: ≈ **580 records/day per service** (two bars + a handful of state changes), ≈ 4600/day for
eight services — within the owner's budget. The stats sensors register only for a service that has
run, `Health` only where a healthcheck is defined: no empty nodes.

**Registration.** Before the collector starts, the source lists the daemon once and registers
every service it finds (and every service remembered as recently removed), so they ride the Start
registration with their alerts. A service that appears later is registered at runtime: the
collector (0.9.1) posts a sensor created while it runs at its next dispatch cycle, and an alert
attached right after the create call rides that registration.

Behavior at the edges:

- **Identity** is the Compose `(project, service)` from the container labels — stable across
  recreate and upgrade. Segments keep `[A-Za-z0-9_-]`, anything else becomes `_`; two names that
  normalize alike are told apart by a six-hex-digit FNV hash suffix on the newcomer (a name that
  needed no normalization keeps the plain segment). Assigned nodes never move: each service's node
  is remembered in the state file and re-adopted after a restart.
- **Unlabelled containers** (`docker run`): skipped with one log line per container while
  `probe.docker.composeOnly` is `true` (default); with `false` they appear as `Docker/_standalone/<name>`.
  **One-off containers** (`docker compose run`, `com.docker.compose.oneoff=True`) are never counted
  as replicas of their service — a leftover exited one would otherwise pin it at `Stopped`.
- **Completed one-shot jobs** (owner decision): a Compose service whose containers have all exited
  with code 0 under restart policy `no` (garage's `lingua-ci/ci-image`, an image build) is a job
  that finished, not a service that stopped — no sensors, one INFO line
  (`<project>/<service>: completed job (exit 0, no restart policy), not monitored`), no config
  knob. Once such a service has a running container it is a service from then on (remembered in
  the state like any other), so its next exit is a real `Stopped`. A non-zero exit, or exit 0 under
  any other restart policy, is `Stopped` as before. An exited container whose inspect failed
  leaves the decision to the next poll.
- **Replicas:** CPU and memory usage are summed (the memory limit sum is capped at the host's
  memory); status and health are worst-of (Stopped < StartPending < Paused < Running; unhealthy <
  starting < healthy); restart counts are summed.
- **CPU is never posted as 0 for lack of data:** a container's first sample, a counter that went
  backwards, a new container id and an interval outside ½…3× the sample period are skipped. A
  service with any skipped replica skips that sample.
- **An unreachable daemon** is logged once (an info line when there is no socket at all, an error
  with a hint on `EACCES`), retried with a backoff doubling up to 60 s, and resumed silently;
  nothing is posted meanwhile. A failed or timed-out inspect or stats call skips that service's
  values (logged once per container) without touching the other services, and never posts a
  guess. A panic in a tick is caught and logged.
- **State** (restart baselines, last posted restart count, OOM latches, last-seen times, nodes) is one JSON
  file, `$STATE_DIRECTORY/docker-state.json` (`/var/lib/hsm-linux-probe`), written atomically only
  when something changed (at most hourly for last-seen). A missing or corrupt file means a fresh
  start, logged once.

**Engine API client.** A ~250-line HTTP/1.1 `GET` client over `std::os::unix::net::UnixStream`
(`docker/http.rs`) with `Content-Length`, chunked and close-delimited bodies, a 1.5 s deadline per
call (under the 2 s stop wait of the source threads) and a 4 MiB body cap — not the `curl` crate: the socket is local plaintext HTTP, and `curl-sys`
silently compiles its bundled libcurl when pkg-config misses the system one (two libcurls in one
process) and drags `libz-sys`/`openssl-sys` into the link line. The probe still links exactly one
libcurl, the collector's. The client can only build four requests, all `GET`: `/version`,
`/v1.45/containers/json?all=true`, `/v1.45/containers/{id}/json`,
`/v1.45/containers/{id}/stats?stream=false&one-shot=true` (container ids must be hex). The version
is pinned in the path — 1.45, the fixtures' — and negotiated down to a daemon's own (≥ 1.41, for
one-shot stats) or up to its `MinAPIVersion`.

**Socket access.** The socket (`srw-rw---- root:docker`) is root-equivalent; the probe restricts
itself to the four read-only GETs above by construction and review, not by the kernel. The unit
does **not** name the group (`SupplementaryGroups=docker` stops a unit from starting on a host
without that group); the package's postinst runs `/usr/lib/hsm-linux-probe/docker-access.sh
install`, which writes the drop-in `/etc/systemd/system/hsm-linux-probe.service.d/docker.conf` only
when `getent group docker` succeeds (and removes a stale one otherwise); postrm removes it on
remove/purge. After installing Docker on a host that
already runs the probe: `sudo /usr/lib/hsm-linux-probe/docker-access.sh install && sudo systemctl
daemon-reload && sudo systemctl restart hsm-linux-probe`.

**Tests** run on captures from garage-server (Docker 26.1.5, API 1.45, 12 containers in 6 Compose
projects) under `hsm-linux-probe/fixtures/docker/`: the listing, the inspects (trimmed to the
fields the probe reads — `Config.Env` and mounts dropped), two stats rounds 5.3 s apart, and three
raw HTTP responses (chunked, `Content-Length`, 404) byte for byte.

## Crate layout

```
src/probe-linux/
  hsm-collector-sys/   raw FFI declarations for the ABI subset the probe uses + the CMake build
  hsm-collector/       safe RAII wrapper: Collector, typed sensor handles, alerts, log sink
  hsm-linux-probe/     the binary: config, logging, signals, lifecycle wiring, sensor registration
    src/probe_only/    probe-only sources (host.rs, disk.rs, docker/) and their per-source threads
    fixtures/docker/   Engine API captures from garage-server
  packaging/           systemd unit, config skeleton, maintainer scripts, build-deb.sh,
                       docker-access.sh (Docker socket drop-in)
```

**Alerts in the wrapper.** `Collector::alert(AlertKind)` returns an `AlertBuilder` (conditions,
notification / scheduled notification, icon, `sensor_error`, confirmation / inactivity period,
`disabled`, `build`); every sensor handle has `attach_alert(&Alert)`. An alert is part of the
sensor's registration. Before Start it rides the Start batch; while the collector runs (collector
0.9.1) attaching re-records the registration and the live transport re-posts it, which is how a
sensor created at runtime gets its alerts. Attaching is refused only while the collector stops. `instant_hourly_schedule_anchor()` reproduces the managed
`ThenSendInstantHourlyScheduledNotification`. `Collector::enum_sensor_with_options` registers an
enum sensor with both its options and `SensorOptions` (the `Service status` shape; collector 0.9.0).

`hsm-collector-sys/build.rs` configures and builds `src/native/collector` with CMake
(`HSM_COLLECTOR_HTTP=ON`, the same switch `src/agent` uses), links `hsm_collector_core` statically
and libcurl dynamically. `HSM_COLLECTOR_LIB_DIR` short-circuits the build and links a prebuilt
collector instead.

> **TODO(#1413):** once workstream 1 (#1414) ships a `collector-v*` release, the in-tree CMake build
> is replaced by the pinned version from the repo's vcpkg registry — the probe is intended to be the
> first real `x64-linux` consumer of that registry.

## FFI safety rules

The wrapper crate exists to make these mechanical rather than remembered:

* **Nothing unwinds into C.** Every `extern "C"` callback body is wrapped in `catch_unwind`; a
  panicking Rust log sink drops its message instead of crossing the boundary.
* **Handles cannot outlive their collector.** Sensor handles borrow the `Collector`, so the
  use-after-free the raw ABI permits is a compile error here.
* **`Send`/`Sync` follow `hsm_collector.h`, not convenience.** They are asserted only for entry
  points the header documents as callable from any thread; the calls it asks the caller to
  serialize (start/stop/dispose/registration) are serialized by a lock inside `Collector`.
* **Struct layouts are asserted against the real header, at build time.** `build.rs` compiles
  `static_assert`s over `hsm_collector.h` for the size, alignment and every field offset of each
  mirrored struct, and pins the header's version against the version the linked library reports.
  A Rust-only `size_of` test could not do this: it measures the Rust mirror against a constant in
  the same crate, so a field appended on the C side — a MINOR bump under the collector's own
  versioning policy — would change neither and would surface only at runtime, as C writing past the
  end of a Rust stack slot. Verified by deliberately drifting the header: an appended field and a
  reordered field both fail the build.
* **The access key is a `Secret`.** Redacted from `Debug`/`Display`, wiped on drop, never in argv,
  never in an error message, never in the config file. Every copy this code makes — the probe's
  `Secret`, the `CollectorOptions` string, and the `CString` the wrapper hands to the ABI — is
  wiped once the collector has taken it. The collector's own C++ copy is not ours to clear, which
  is why the guarantee is best-effort.

## The #1414 feature gate

`hsm_collector_install_linux_metric_sources` — the entry point that makes the collector's default
host catalog (Total CPU, Free RAM, free disk, process sensors) produce live values on Linux — is
added by workstream 1 (#1414) and **does not exist in master's collector**. So:

* the declaration lives behind the cargo feature `linux-default-sensors`, **off by default**;
* with the feature off the crate builds and runs against today's collector, and the probe logs a
  loud warning at startup naming what will not be reported;
* with the feature off the probe also **skips registering** everything the metric-source factory
  feeds — the `.computer` catalog *and* the three `.module/Process process/…` sensors — rather than
  showing the operator nodes whose values can never arrive. The collector-driven module sensors
  (`Service alive`, `Collector version`, `Collector errors`, the queue stats, `Version`) are
  unaffected and still register;
* with the feature on against an older collector, the link fails — deliberately, so the gap is
  never silent.

Enable it once #1414 is merged: `cargo build --features linux-default-sensors` (and eventually make
it the default here).

## Platform support

**Linux is the only supported target.** OS-specific code is `cfg`-gated so the pure parts (config
parsing, log formatting, the key-permission logic) compile and test anywhere, but the probe is built, tested
and shipped for Linux only; the integration test is `#[cfg(target_os = "linux")]`.

## Building and testing

Requires a C++17 toolchain, CMake ≥ 3.21 and libcurl development headers:

```bash
sudo apt-get install -y build-essential cmake libcurl4-openssl-dev pkg-config
cd src/probe-linux
cargo fmt --all --check
cargo clippy --all-targets -- -D warnings
cargo build
cargo test
```

The CI lane `.github/workflows/probe-linux.yml` runs exactly that on `ubuntu-latest`.

### Building the `.deb`

`packaging/build-deb.sh <version>` builds the package reproducibly inside a plain `debian:13`
container — it installs the build dependencies and rustup itself, builds the release binary with
`--locked --features linux-default-sensors`, checks that the binary loads no shared library outside
the fixed `Depends`, and writes `dist/hsm-linux-probe_<version>_amd64.deb`. From the repository
root on Windows (Git Bash; Docker Desktop):

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/src" -w /src/src/probe-linux \
    -v hsm-probe-cargo:/root/.cargo debian:13 bash packaging/build-deb.sh 0.2.0~trial1
```

On Linux drop `MSYS_NO_PATHCONV=1` and use `$(pwd)`. The `hsm-probe-cargo` volume is optional; it
caches the toolchain and crates between runs. The version is a parameter: trials use `~trialN`,
which sorts **below** the plain release (`0.2.0~trial1 < 0.2.0`) and above every `0.1.0~…` —
check with `dpkg --compare-versions 0.2.0~trial1 gt 0.1.0~trial3`.

The package has the layout of the hand-built `0.1.0~trial*` packages: `/usr/bin/hsm-linux-probe`,
`/lib/systemd/system/hsm-linux-probe.service` (kept under `/lib`, where the trials put it — moving a
file between `/lib` and `/usr/lib` across versions is unsafe with dpkg on a merged `/usr`),
`/etc/hsm-linux-probe/config.json` (a conffile, from `config.example.json`) and the copyright
file; `Depends: libcurl4t64, ca-certificates, libc6, libstdc++6, libgcc-s1`. The maintainer scripts
are `packaging/deb/{postinst,prerm,postrm}`, reconstructed from the trial package: postinst creates
the `hsm-probe` system user/group and reloads systemd, and on a fresh install does not enable or
start the unit (`install.sh` does, once config and key are in place); prerm disables it on remove;
postrm purges `/var/lib` and `/var/log` state. On upgrade prerm stops the unit and, **from 0.2.0 on**,
leaves a marker in `/run` when it was running, so the new postinst starts it again — an upgrade
does not end monitoring. (Upgrading *from* a `0.1.0~trial*` package runs that package's old prerm,
which leaves no marker: start the unit by hand once.) The operator's config survives upgrades
(`apt-get install -o Dpkg::Options::=--force-confold ./hsm-linux-probe_….deb`).

## Configuration

`packaging/config.example.json` is the skeleton; it is parsed by a unit test, so it cannot rot.
Placeholders only — **no secrets**:

* `hsm.accessKeyFile` names the file holding the product access key (`CanSendSensorData` only, never
  a master key). A **relative** name (the skeleton's `"access-key"`) is a systemd credential and
  resolves against `$CREDENTIALS_DIRECTORY`, the directory `LoadCredential=` fills — so the config
  never hardcodes `/run/credentials/<unit>/`; without that variable a relative name is an error. An
  absolute path is used as is. The probe reads the key at startup and wipes every copy it
  makes once the collector has taken it (the collector's own C++ copy is not ours to clear).
* It warns if the key is readable beyond its owner. The check evaluates the POSIX ACL, not just the
  mode bits: systemd hands a credential to a non-root `User=` as a root-owned `0400` file plus a
  named-user ACL entry for the service, which makes `stat` report `0440` (the ACL mask shows in the
  group bits) although no group can read it. That case passes; a genuinely group- or
  world-readable key, or one readable by another named user or group, still warns.
* `hsm.address` must be `https://<host>` (case-insensitive) or a bare host name, which the
  collector defaults to HTTPS. Validation is an allow-list, so `http://`, `ftp://`, `ws://` and a
  typo'd scheme are all rejected rather than passed through to libcurl; the wrapper
  deliberately does not expose the ABI's `allow_untrusted_server_certificate` flag — it disables
  both peer and hostname verification, which §4.1/§4.3 ban. Trust a private CA by installing it
  with `update-ca-certificates`; libcurl/OpenSSL picks up the system store with verification on.
* `docker` (optional; every key has a default): `enabled` (`true`), `socket`
  (`/var/run/docker.sock`), `composeOnly` (`true`), `samplePeriodSec` (`5`, 1–300; the bars stay
  5 minutes whatever it is) and `oomLatchHours` (`24`). A host without Docker needs no change: the
  source logs one info line and waits for the socket; `enabled: false` turns it off entirely.

## Running

```bash
hsm-linux-probe --config /etc/hsm-linux-probe/config.json
hsm-linux-probe --version    # probe version + the linked collector version
```

`SIGTERM` (systemd stop/restart) and `SIGINT` request a graceful stop: the main thread notices
within 200 ms and the collector drains with its own bounded stop, so a host restart is never held
up. An overrun of `shutdownTimeoutSec` is logged; `TimeoutStopSec` in the unit is the backstop.
Values the bounded drain discards are reported at `WARN` (the collector emits that line at
`debug`; the probe raises a non-zero count, since it is data loss — root rule #8).

Log levels are `debug`, `info`, `warn`, `error`. Timestamps and the daily file roll are UTC, like
the collector's own file logger, so the two line up; the journal adds local time on its own.

`packaging/hsm-linux-probe.service` carries the §4.3 hardening (`NoNewPrivileges`,
`ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `StateDirectory`, empty capability bounding
set, `LoadCredential`) and the §5 budget (`MemoryMax=64M`, `CPUQuota=5%`). Docker socket access is
not in the unit but in a drop-in written only where a docker group exists — see
[Socket access](#docker-compose-services-1416).
