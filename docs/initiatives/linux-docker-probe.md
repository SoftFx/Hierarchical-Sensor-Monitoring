# Linux host + Docker Compose probe (garage-server)

> Status: **phase 1 delivered and verified on the real host** (2026-09-24). Epic: #1413.
> The probe runs on garage-server reporting the managed Unix default set plus probe-only sensors
> agreed with the owner one by one (§4.2a): host (#1476), every mounted disk including the
> archives (#1481) and Docker Compose (#1416); the backup-contract sensors (#1417) are still to be
> agreed. Releases are on hold by owner decision
> (§4.5).
> See §9 for what shipped, §10 for what the work uncovered, §11 for what remains.
> Source task: garage_administration `hsm/TASK-linux-docker-monitoring.md`.
> Scope: a Linux probe that reports Debian host metrics, Docker Compose service metrics,
> SSD/archive-disk capacity and backup-contract signals into the existing HSM server.

## 1. Decision summary

Two-part decision:

1. **Grow the shared native collector (`hsm::collector`, C++) with Linux metric sources**
   for the existing default-sensor catalog (Total CPU bar, Free RAM, Free disk space
   (+prediction), process CPU/RSS/threads), **mirroring the managed Unix implementations
   algorithm-for-algorithm** under repo rules #9/#10 (same OS data source, mirrored
   normalization, conformance scenario locking equivalence — precedent: top-CPU). Publish as
   a new collector version through the existing vcpkg registry channel.
2. **Build a thin Rust probe (`src/probe-linux/`, systemd-hosted) that consumes that
   published collector as-is through its stable C ABI** (`hsm_collector.h` — the ABI exists
   precisely for foreign hosts; version-pinned build via the repo's vcpkg registry) and adds
   only the signal sources that exist in no collector today — Docker Compose metrics,
   loadavg, archive-disk snapshots, backup contract — through the collector's public sensor
   API (instant/enum/`DoubleBar`/`IntBar`), so wire format, transport, queuing, types and
   options are always the library's. No parallel HTTP client to HSM, no hand-rolled wire.

Guiding constraints (review feedback on earlier drafts):

- **Sensor identity is the hard part, host plumbing is not.** A sensor implemented "slightly
  differently" from the published collectors is a permanent semantic/wire divergence — the
  failure class epic #1093 exists to prevent. Therefore no probe-local reimplementation of
  any sensor the catalog already has: the Linux host sensors go **into** the shared
  collector, conformance-locked against the managed ones, and the probe just enables them.
- **No .NET on the Linux host.** The product's host-side stack is native (HsmAgent, the
  wrapper and the TT aggregator all run on `hsm::collector`); the probe stays native too.
  Language choice for the probe host: **Rust** (owner preference, and a substantive fit — a
  long-running daemon holding root-equivalent docker-socket access is where memory safety
  pays). The sensors themselves live in the C++ collector either way; the probe is plumbing.

The probe implements no averaging, smoothing, debounce, thresholds or notification delivery —
EMA/weighted statistics, TTL, alert templates and Telegram stay in HSM.

## 2. Why "just compile HsmAgent for Linux" does not work

Evidence from the current tree (`src/agent/`, `src/native/collector/`):

1. **The build refuses to produce a binary.** The entire `hsm-agent` executable target is
   wrapped in `if(WIN32)` (`src/agent/CMakeLists.txt:81`); a Linux configure+build yields
   only the config-parser library and its tests.
2. **It would not link even unguarded:** MSVC-only `wmain` (`src/agent/src/main.cpp:108`),
   unconditional `<windows.h>`, linked `advapi32 winhttp bcrypt`.
3. **The host model is Windows through and through:** SCM service lifecycle, Event Log +
   registry, `%ProgramData%` wide-string paths; self-update (~860 LOC) is
   WinHTTP + BCrypt + an SCM exe-swap dance, replaced on Debian by systemd + packaging.
4. **The decisive blocker is data.** Every live metric the agent ships comes from Windows
   PDH counters (`src/native/collector/src/platform/hsm_windows_metric_sources.cpp`, fully
   `#if _WIN32`); `agent_runtime.cpp:206` installs that factory unconditionally and it
   **throws off-Windows**. The native collector today has **zero** `/proc`-based sensors —
   a Linux agent build would have nothing to measure with even if it started.

What *is* Linux-ready is the native collector **core**: lifecycle, scheduler, bounded queue,
retry, dedup logging, wire format and the whole public sensor API are platform-free and are
built and tested on ubuntu (gcc + clang + ASan/TSan + the conformance corpus) on every PR
(`.github/workflows/native-collector-conformance.yml`). The missing piece is exactly one
seam: a Linux implementation behind `hsm_metric_source_factory_fn`
(`hsm_collector.h`, the seam `hsm_windows_metric_sources.cpp` already plugs into).

## 3. Options considered

| Option | Verdict | Rationale |
|---|---|---|
| **A. Native probe over the published native collector + Linux metric sources added to the shared collector** | **Chosen** | Stays on the product's native stack (agent/wrapper/aggregator precedent); no .NET runtime on the host; RSS ~5–20 MiB fits the 64 MiB budget with margin on the i5-2500. Sensor identity is guaranteed the same way the repo already guarantees it cross-collector: mirrored algorithm + same OS source + conformance lock (rules #9/#10, top-CPU precedent), not by trusting a probe author. The catalog gain (Unix default sensors in the vcpkg-published collector) benefits every native consumer, not just garage. Probe host language: **Rust over the C ABI** (see below). |
| B. .NET worker over the managed HSMDataCollector NuGet | Rejected (was draft v2) | Managed lib does ship Unix sensors today, but it drags a .NET runtime dependency onto the host, idles at 40–80 MiB against a 64 MiB target, and diverges from the C++ host-side direction. Its Unix sensors remain valuable **as the reference implementation** the native sources must mirror. |
| C. Probe-local `/proc` parsers over the native collector's custom-sensor API | Rejected (was draft v1) | Fast, but every host sensor would be a second, unconformed implementation next to the managed one — exactly the divergence class this repo forbids. |
| D. Port/refactor HsmAgent to Linux | Rejected | See §2; ~80% Windows plumbing, and the task forbids that refactor before an approved decision. |

Cost accepted with A: the collector workstream (§4.1) is real shared-library work under
compatibility + conformance rules, reviewed and versioned like any collector feature. It is
sequenced first and gates the probe.

Probe host language — Rust vs C++ (both native, both consume the same collector):

- **Rust (chosen):** owner preference; memory safety for the one root-equivalent-privileged
  daemon on the host; first-class fit for the probe-local work (serde_json for Docker/
  backup JSON, and — as built in #1416 — a dependency-free HTTP/1.1 reader over
  `UnixStream` for the local Engine API socket, so the process links one libcurl, the
  collector's; §4.3); small static binaries, trivial systemd hosting. The
  collector's C ABI is a designed-for-FFI surface (the aggregator wrapper already consumes
  it from another toolchain), so this is intended use, not a workaround.
- Accepted costs, stated openly: a new toolchain in repo + CI (cargo on the ubuntu lane,
  collector built by CMake/vcpkg first, linked from `build.rs`); a small `hsm-collector-sys`
  binding crate + safe wrapper for the subset of the ABI the probe uses (options, transport,
  lifecycle, instant/enum/bar sensors, logger callback); FFI discipline (no panic unwind
  across `extern "C"` — `catch_unwind` at every callback boundary). If review rejects the
  new toolchain, the fallback is the same architecture in C++ over the `hsm::collector` wrapper
  (`include/hsm_collector/collector.hpp`) —
  nothing else in this document changes.

## 4. Architecture

```
[systemd unit hsm-linux-probe.service]
   └── hsm-linux-probe (single Rust process; hsm-collector-sys FFI crate over the stable C ABI,
        static-linked against the published collector built via the repo vcpkg registry)
        ├── hsm::collector <pinned version> — as published, unmodified
        │     ├── Linux default sensors (§4.1): Total CPU bar, Free RAM, Free disk (+prediction),
        │     │     process CPU/RSS/threads   ← InstallLinuxMetricSources()
        │     ├── module self-sensors: Collector Alive / Version / Errors, queue diagnostics
        │     └── libcurl/OpenSSL → https://garage.lan:44330
        │           (VERIFYPEER=1, VERIFYHOST=2; Debian system trust store via update-ca-certificates)
        ├── probe sources feeding the collector's public sensor API:
        │     ├── loadavg (60 s): /proc/loadavg — exists in no collector today
        │     ├── docker (stats 5 s, state 60 s): Docker Engine API over the unix socket
        │     │     (HTTP/1.1 GET over UnixStream, one-shot stats; no stream, no external CLI)
        │     ├── disks (5 min / 5 s): statvfs of every real filesystem incl. the archives
        │     │     (#1481 — measured not to wake them) + /proc/diskstats write speed
        │     └── backup: timestamped JSON results on SSD (the §4.4 contract, still open)
        └── probe state on SSD (restart counters, OOM latches, last-seen backup result)
```

Key properties: one process; scheduling/queuing/batching/retry are the collector's; each
probe source is exception-isolated behind its own catch with a visible per-source status
sensor (no silent loss); Docker timeout/partial data ⇒ skipped values + diagnostic, never
zeros/`healthy`/re-stamped stale data; the archive HDDs are only ever `statvfs`'d (answered
from the superblock — measured on garage-server not to wake them, §4.2a), never opened, listed
or read.

### 4.1 Collector workstream: Linux metric sources (shared, conformance-governed)

New `src/native/collector/src/platform/hsm_linux_metric_sources.cpp` behind the existing
metric-source-factory seam, plus `InstallLinuxMetricSources()` (C ABI + RAII wrapper),
symmetric to the Windows factory. Mapping, per rule #10 (same OS source, mirrored algorithm;
the managed side may sit on a .NET wrapper over the same source):

| Default sensor | OS truth | Managed reference to mirror |
|---|---|---|
| Total CPU (bar) | `/proc/stat` aggregate line, busy% by delta | `ProcStatCpuUsage` (`DefaultSensors/Unix/SystemInfo/ProcStat.cs`) |
| Free RAM MB | `/proc/meminfo` `MemAvailable` | `ProcMeminfo.ParseAvailableKb` |
| Free disk space (+prediction) | `statvfs` on the target mount | `UnixDiskInfo` (`DriveInfo("/")` is statvfs underneath) |
| Process CPU / Memory / Thread count | `/proc/self/stat`, `statm`, `task/` | `UnixProcessCpu` etc. (.NET `Process` reads the same `/proc` files on Linux) |

Also in this workstream: platform-correct registration — on Linux,
`add_all_computer_sensors` must register the Unix set instead of the current unconditional
Windows nodes: `hsm_collector_add_all_computer_sensors` (`hsm_collector.cpp:7107`) calls
`hsm_collector_add_windows_info_monitoring_sensors` unconditionally at `:7116`, registering the
`kWindowsInfoGroup` sensors (`:6953`) that can never produce values off-Windows.

**Entry-point decision (settled while implementing, recorded here):** the existing ABI function
keeps its name and branches at **compile time**, rather than gaining a separate
`..._add_all_unix_computer_sensors`. The managed reference does split its surface
(`UnixSensorsCollection` vs `WindowsSensorsCollection` behind `IUnixCollection`/`IWindowsCollection`),
so this is a deliberate divergence: one binary targets one OS, so a per-platform entry point would
be dead code on the other, and every existing caller keeps working untouched. Two consequences the
implementing slice owns rather than discovers: the counting assertions in
`hsm_collector_tests.cpp:4057-4059` (18 computer / 30 default) become platform-dependent and must be
rewritten per platform, and the invariant recorded at `hsm_collector.cpp:6950-6952` — the event-log
sensors must stay in the group so the native default set is not smaller than the managed one — is
explicitly **not** in force off-Windows, where the managed Unix set has no event-log sensors either.

Governance: conformance scenario(s) in `tests/conformance/collector/` locking the Unix
default set (paths, types, options, registration) with verb support in both drivers, plus
algorithm-contract fixtures in the native suite (top-CPU precedent:
`conformance_top_cpu_contract`); managed collector behavior unchanged (its Unix sensors
already exist — it is the reference side of the scenario). Collector version bump, tag
`collector-v<next>`, registry `versions/` update — the probe then pins that published
version. Windows-only sensors (event logs, service status, network speed, top-CPU, OS info)
are explicitly **not** ported here.

TLS: libcurl/OpenSSL on Debian uses the system trust store, so installing the garage cert
via `update-ca-certificates` keeps peer+hostname verification on with **no collector
change**. An explicit `CollectorOptions::ca_file` → `CURLOPT_CAINFO` knob (today the only
lever is `allow_untrusted_server_certificate`, which disables both checks — banned by the
task) is an optional small slice, not on the critical path.

### 4.2 Sensor tree — what the probe actually registers today

**Owner rule, decided 2026-09-22 and overriding the original plan below: "first do everything
the .NET collector already has; other sensors come later, agreed one by one."** The probe
therefore registers **exactly** the set the managed collector registers on Linux and nothing of
its own. The three probe-local sensors this document originally specified — load average,
logical cores and per-source status — were implemented, then removed again before merge.

The registered set is 15 paths, byte-identical in registration to managed (a 330 s capture of
both collectors against one fake server; the table lives in `src/probe-linux/README.md` and a
test pins the exact path list):

- `.computer/Total CPU`, `.computer/Free RAM memory`
- `.computer/Disks monitoring/Free space on disk` + `… prediction`
- `.module/Process process/{Process CPU, Process memory, Process thread count}`
- `.module/{Service alive, Collector version, Collector errors, Version}`
- `.module/Collector queue stats/{Items count in package, Package content size, Package process time, Queue overflow}`

Two deliberate differences from managed, both recorded in code: the process node keeps the
fixed name `Process process` so one alert template matches every host (#1429), and
`ThreadPool thread count` is absent because a native process has no CLR thread pool — the
Windows agent behaves the same way.

**Second set — probe-only sensors (#1476).** On top of the parity set the probe registers sensors
the owner agreed one by one, which by decision (2026-09-24) exist **only in the Linux probe** —
never in the shared collector catalog, never on Windows; moving any of them into the catalog is a
separate, later step. They are pinned by their own test (`PROBE_ONLY_SET`) next to the unchanged
parity test, so the parity contract stays exactly the 15 paths above. On garage-server the probe
therefore registers 19 sensors. The agreed host/disk rows are in §4.2a.

### 4.2a Probe-only sensors — agreed one by one

Per the owner rule above, each sensor is agreed individually before implementation, with its data
source, what it costs in history, and whether it belongs in the shared collector catalog rather
than in the probe.

**Host — agreed 2026-09-24, built in #1476** (computer-level: `<computer>/.computer/…`;
source rules, alert semantics and config in `src/probe-linux/README.md` "Probe-only sensors"):

| Path | Type · period | Alert | Source | Records/day |
|---|---|---|---|---|
| `.computer/Logical cores` | Int · at start + daily, TTL 48 h | — | `sysconf(_SC_NPROCESSORS_ONLN)` | ≈ 2 |
| `.computer/CPU temperature` | DoubleBar °C · 5 s samples, 5-min bar, TTL 15 min | Mean > 80 warning, > 90 Error | hwmon coretemp `Package id 0` → thermal zone `x86_pkg_temp` → first `cpu` zone; not registered without one | 288 |

"Warning" is a notification with the ⚠ icon and no status change: HSM alerts can only raise
Error (the server's only status action), exactly like the managed Total CPU / Free RAM alerts.
Rejected: load average (Total CPU is already a 5-minute bar with an EMA).

**Disks — every mounted real filesystem, agreed 2026-09-29, built in #1481.** #1476 first
reported only the filesystem holding `/srv/docker` and deferred the archive HDDs to a
backup-snapshot file, on the assumption that polling would wake them. The owner measured it on
garage-server (2026-09-29): `statvfs` on the sleeping FUSE-NTFS archives `/mnt/wd4tb` (sda2) and
`/mnt/mediacentr` (sdb1) and on the ext4 `/mnt/oldlinux` (sdb5) left both disks in STANDBY
(`smartctl -n standby`, before, 5 s and 35 s after). So the archives are polled directly, and the
snapshot-file design is dropped; the probe only ever calls `statvfs` on a mount — it never opens,
lists or reads anything under one — and reads `/proc/diskstats` (kernel memory).

Per filesystem, under `.computer/Disks monitoring/`, named like the Windows per-drive sensors
(`Free space on C disk`) with `root` for `/` and the last mount-path segment otherwise:

| Sensor | Type · period | Alert | Source |
|---|---|---|---|
| `Free space on <name> disk` | Double MB, EMA · 5 min, TTL 15 min | — (the % sensor carries the alerts; a fixed 20 GB one would hold a small `/boot/efi` in Error) | `statvfs` `f_bavail × f_frsize` |
| `Free space on <name> disk %` | Double % · 5 min, TTL 15 min | < 10 warning, < 5 Error | `f_bavail / f_blocks` |
| `Free inodes on <name> disk %` | Double % · 5 min, TTL 15 min | < 10 warning | `f_favail / f_files` |
| `Average disk write speed on <name> disk` | DoubleBar MBytes_sec, EMA · 5 s samples, 5-min bar, TTL 15 min | — (as on Windows) | `/proc/diskstats` of the whole disk under the partition |

Real filesystems are the block-backed types in `/proc/self/mountinfo`, deduplicated by source
device (garage-server's 11 real-type mounts are 4 filesystems: `root`, `wd4tb`, `mediacentr`,
`oldlinux`), re-scanned every 10 min so new mounts register at runtime. ≈ 1 150 records/day per
filesystem, ≈ 4 600 on garage-server. The two #1476 root-only percent sensors moved to
`Free space on root disk %` / `Free inodes on root disk %`; the managed-parity
`Free space on disk` (+ prediction) is unchanged.

**Docker Compose — agreed 2026-09-24, built in #1416** (garage_administration
`hsm/SENSORS-DECISIONS.md` §2). All under one node in the probe's module:
`<computer>/<module>/Docker/<project>/<service>/<sensor>`. Every number lives in
`hsm-linux-probe/src/probe_only/docker/contract.rs`, every alert in `…/docker/alerts.rs`; the
operator-facing table and the edge behavior are in the probe README ("Probe-only sensors").

| Sensor | Type / cadence | Value | Alert (attached at registration) |
|---|---|---|---|
| `CPU` | DoubleBar, 5-min bar, sample every 5 s (`probe.docker.samplePeriodSec`) | % of the **whole host**, max 100 = Δ`total_usage` / Δ`system_cpu_usage` × 100 (not × `online_cpus`; the description says `docker stats` shows per-core %). First sample, counter reset, container-id change, interval outside ½…3× the period ⇒ skipped, never 0 | mean > 90 for 30 min → warning notification |
| `Memory used %` | DoubleBar, same sampling | (`usage` − `inactive_file`) / limit × 100; unlimited ⇒ of host `MemTotal` | mean > 90 → warning notification |
| `Memory limit` | Int, MB; at start and on change | the limit (host `MemTotal` when unlimited, with a comment) | none |
| `Service status` | Enum, 60 s, AggregateData | the Windows `ServiceStatusPrototype` options byte-for-byte; running → Running; created, restarting → StartPending; paused → Paused; exited, dead, removing → Stopped; removed → Stopped for 7 days after last sighting. A service first seen with every container Exited (0) under restart policy `no` is a completed one-shot job: not registered, one INFO line; once it runs it is a service from then on (owner decision) | `IfValue NotEqual Running`, confirmation 5 min, instant-hourly notification — the Windows prototype's alert byte for byte |
| `Health` | Enum {starting, healthy, unhealthy}, 60 s, AggregateData, only where a healthcheck exists | `State.Health.Status` | `unhealthy` for 5 min → notification |
| `Restart count` | Int, 60 s, posted only on change | cumulative `RestartCount`; a new container id starts a new baseline (state on disk), never goes down | value changed → notification |
| `OOM killed` | Bool, 60 s, AggregateData | `State.OOMKilled` latched 24 h (`probe.docker.oomLatchHours`), survives recreate | true → Error + notification |

Replicas: CPU/memory summed (the limit sum capped at host memory), status and health worst-of,
restarts summed. Budget ≈ 580 records/day per service, ≈ 4600 for eight. No empty nodes: stats
sensors register once a service has run, `Health` only where a healthcheck is defined. Services
present at start register before Start (alerts in the Start batch); a service that appears later
registers at runtime — which needed a collector fix (0.9.1): public-API sensors created while the
collector runs were recorded locally but never POSTed to `/commands`, and an alert attached after
their creation never reached the registration.

**Backups (#1417)** — the original Stage-0 proposal below, still subject to the same per-sensor
agreement (the Stage-0 Docker rows are superseded by the table above).

| Path (under garage-server/LinuxProbe/) | Type | Period | TTL | Notes |
|---|---|---|---|---|
| `Backup/<job>/{Last result, Duration min, Missed deadline}` | Enum/Double/Bool | on new result | job-specific | From the backup task's snapshot contract (§4.4). |
| `Backup/<job>/Last success heartbeat` | Bool/TTL | only on *new* confirmed success | deadline-derived | Re-reading the same result never refreshes it; "never succeeded yet" is a distinct initial state. |
| `Probe/Sources/<name> status` | Enum {ok, degraded, failed} | 60 s | 3 min | Per-source failure isolation made visible. |

Docker path identity: from `com.docker.compose.project`/`.service` labels — stable across
recreate/upgrade. Normalization: `[A-Za-z0-9_-]` kept, others → `_`, collisions detected via
a reverse map and disambiguated with a short stable hash (FNV-1a, six hex digits, on the
newcomer; a name needing no normalization keeps the plain segment; nodes are remembered in the
state file and re-adopted after a restart, so they never move); rule fixed by unit tests.
Containers without compose labels: config `probe.docker.composeOnly: true` (default) skips them
with one deduplicated diagnostic per container; `false` puts them under
`Docker/_standalone/<name>`. `docker compose run` one-offs (`com.docker.compose.oneoff=True`) are
never counted as replicas.

HSM-side templates (documented for the operator, thresholds configurable, nothing hardcoded
in the probe or collector catalog): probe TTL 3 min; sustained CPU via HSM EMA; low SSD
space; stopped/unhealthy/OOM/restarts; stale HDD snapshot; backup failure/runtime/missed
deadline/stale success.

### 4.3 Docker access & threat model

Chosen: **task option 1 — the probe itself is the single minimal host service with socket
access.** `SupplementaryGroups=docker` on the unit (no login user in the `docker` group),
plus hardening: `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome`, private tmp,
`ReadWritePaths` limited to its state dir, empty capability bounding set, `MemoryMax` /
`CPUQuota` from §5, no listening ports. Documented honestly: socket access is
root-equivalent; the probe is the one privileged component and restricts itself to read-only
endpoints (`/containers/json`, `/containers/{id}/stats?stream=false&one-shot=true`,
`/containers/{id}/json`, `/version`) **by construction and review, not by the kernel**.

Client (decided in #1416, replacing the `curl`-crate plan): a minimal hand-rolled HTTP/1.1
`GET` client over `std::os::unix::net::UnixStream` (`hsm-linux-probe/src/probe_only/docker/http.rs`,
~250 lines incl. `Content-Length`, chunked and close-delimited bodies, one 1.5 s deadline per call including the connect,
a 4 MiB body cap), JSON via serde_json. The Engine API socket is local plaintext HTTP, so libcurl
would contribute nothing, while the `curl` crate would bring `curl-sys` — whose build silently
compiles its bundled libcurl when pkg-config does not find the system one, i.e. exactly the "two
libcurls in one process" outcome the one-HTTP-stack argument was meant to prevent — plus
`libz-sys`/`openssl-sys` on the link line, widening the package's `Depends`. The hand-rolled
client adds no dependency, the process still links exactly one libcurl (the collector's, for
HTTPS to HSM), and every framing path is unit-tested against raw bytes captured from the
garage daemon. Read-only by construction: the client has only `GET`, and the request paths come
from a closed enum of the four endpoints with container ids validated as hex. The API version is
pinned in the path (1.45, the fixtures'), negotiated down to an older daemon's own version
(≥ 1.41 for one-shot stats) or up to a newer daemon's `MinAPIVersion`.

Socket access (decided in #1416): the unit does **not** carry `SupplementaryGroups=docker` —
systemd refuses to start a unit whose supplementary group does not exist, so a hardcoded group
would break the probe on every host without Docker. The package ships
`/usr/lib/hsm-linux-probe/docker-access.sh`; postinst runs `install`, which writes the drop-in
`/etc/systemd/system/hsm-linux-probe.service.d/docker.conf` (`SupplementaryGroups=docker`) only
when `getent group docker` succeeds (and removes a stale one otherwise), on a fresh install and on
the upgrade from before 0.3.0 only (an operator's removal sticks); postrm removes it on
remove/purge. Connecting to the socket needs no write access to its filesystem, so
`ProtectSystem=strict` stays; `RestrictAddressFamilies` already has `AF_UNIX`.

Rejected: a docker-socket-proxy allowlist container — it moves the same root-equivalent
trust into another standing privileged container on a 4-core host, and its lifecycle would
sit outside the HSM deployment we are told not to touch. The HSM container itself never gets
the socket or the archive mounts (unchanged).

Secrets: access key via systemd `LoadCredential=` → `/run/credentials/...` (root-owned
source, 0400); config stores only the path; the key never appears in argv, journal,
exception text or any sensor. Repo/docs carry only `${HSM_ACCESS_KEY}`-style placeholders.
Product key with `CanSendSensorData` only — no master key. `allowUntrustedCertificate`
stays `false` everywhere including examples; tests use a local test CA, never production keys.

### 4.4 Backup & archive integration contract (owned jointly with the backup task)

Interface: timestamped JSON files on SSD (e.g. `/var/lib/hsm-linux-probe/inbox/`), written
atomically (`tmp` + `rename`) by the backup implementation, read-only for the probe:

- `backup-<job>.json`: `{ job, started_at, finished_at, result: ok|failed, duration_s, detail }`

Archive **capacity** is no longer part of this contract: since #1481 the probe `statvfs`es the
archive mounts directly every 5 minutes, which was measured not to wake the sleeping disks
(§4.2a). The backup-result contract above stays separate and is still to be agreed (#1417).

Probe semantics (fixture-tested): distinguishes explicit failure / runtime-exceeded (running
marker or `started_at` without `finished_at`) / missed deadline (schedule known from config)
/ last confirmed success / never-succeeded. Result identity (`started_at` + job) is
remembered in probe state; the success heartbeat fires once per new result. No parsing of
backup logs, no touching backup scripts or Scheduled Tasks — only this contract is shared.

HDD standby acceptance: before/after a test cycle, verify state with a standby-safe check
(`hdparm -C /dev/sdX` — CHECK POWER MODE does not spin the disk up; command recorded in the
runbook) over > 20 min. SMART/temperature of archive disks is optional and must be
standby-gated (`smartctl -n standby`).

### 4.5 Distribution & install channel

> **Reality check (2026-09-24).** The `probe-v*` channel below is **not built yet** (#1418):
> `src/server/HSMServer/probe-release.txt` is empty, so on an ordinary server the download
> endpoint answers 503 by design. garage-server runs a hand-built trial package staged into a
> locally built server image. The owner has put releases on hold, so no `probe-v*`, no
> `agent-v*` and no NuGet push are made, even though master carries collector 0.8.1, HsmAgent
> 0.5.35 and managed collector 3.5.3. One thing did publish: the `collector-v0.8.1` tag and its
> vcpkg-registry entry. **Packaging lesson from the trial:** a version that sorts *below* the
> installed one (`0.1.0~rc1` after `0.1.0~trial2`) makes `apt-get install` refuse the upgrade,
> so the channel must guarantee forward-sorting versions.

Ship as a **`.deb` package published through a GitHub Release**, mirroring the repo's
existing release channels (`agent-v*`, `wrapper-v*`): tag `probe-v<version>` → CI workflow
(build in a container for the **oldest supported target release** — currently `debian:13`
(trixie), which is what garage-server runs; building on a newer release than the target would
produce both a too-new glibc requirement and `Depends:` on packages that do not exist there —
`cargo build --release`,
package, tests) → Release with the `.deb` + its sha256, `--latest=false` as usual.

The package carries the binary, the hardened systemd unit, a config skeleton in
`/etc/hsm-linux-probe/` (dpkg conffile), and a postinst
that creates the system user and `StateDirectory`. The access key is **not** packaged — it is
placed once, manually, as the root-owned `LoadCredential=` source per the runbook.
`libcurl`/`libssl` are linked dynamically against the distro packages (declared as `Depends:`)
so OpenSSL security fixes arrive via ordinary `apt upgrade`, not a probe rebuild.

Install: download from the Release, `sha256sum -c`, `apt install ./hsm-linux-probe_*.deb`,
place the key, `systemctl enable --now hsm-linux-probe`. Upgrade: install the newer `.deb` +
restart. Rollback: install the previous `.deb` from Releases (dpkg downgrades cleanly) —
matching the task's reversibility requirement.

Rejected: probe-in-Docker (needs docker.sock, host `/proc` and host mounts — reintroduces
the cgroup/host-metric trap the task warns about; the probe is a host service by nature);
self-update à la HsmAgent (single host, systemd + package is simpler and safer — already out
of scope); a hosted apt repository (overkill for one server; the Release channel converts
into one later if the fleet grows).

### 4.6 Per-product download bundle: one-command, preconfigured install (#1424)

Owner requirement: parity with the Windows agent, where the admin clicks **Download agent** on a
product and the client runs one command and is connected
(`aicontext/features/server/agent-download/feature.md`). The Linux probe gets the same flow.

- **Server side** (HSMServer web app + model layer, no HSMServer.Core change): an admin-only
  **Download Linux probe** button next to the Windows one streams
  `hsm-linux-probe-<product>.tar.gz`, built by a pure, unit-tested bundle builder that sits
  alongside `AgentInstallerBundle`:
  - the `.deb`, **byte-identical** to the pinned `probe-v*` release. The server names it in
    `src/server/HSMServer/probe-release.txt`, and every server build path downloads it and
    checks its SHA-256, the same way `agent-release.txt` works;
  - a generated `config.json` in the probe schema (§4.1). The address and port come from the
    existing `AgentConnectionResolver` (so the admin's "Agent connection URL" setting applies
    unchanged), `computerName: "auto"`, and `accessKeyFile` points at the LoadCredential path;
  - `access-key`: the product key from the existing `AgentKeySelector`, in its own file. It is
    never written into `config.json`, so the config can be shown and diffed safely;
  - `server-ca.pem`: the server's **public** certificate chain, with no private key. `install.sh`
    installs it as `hsm-server.crt`, because `update-ca-certificates` only reads files with a
    `.crt` extension and silently ignores any other name — a `.pem` left as-is would leave the
    trust store unchanged while the install log still reported success. This keeps
    TLS verification on for self-signed installs, which Windows gets only by baking
    `allowUntrustedCertificate`. The Linux probe never exposes that switch. If the server's
    certificate is already publicly trusted (the Caddy/Let's Encrypt setup from #1411), the file
    is omitted;
  - `install.sh` / `uninstall.sh`.
- **Client side:** `tar xzf hsm-linux-probe-<product>.tar.gz && sudo ./install.sh`. The script
  refuses to run as non-root. It runs `apt install ./hsm-linux-probe_*.deb`, puts
  `config.json` in `/etc/hsm-linux-probe/` **before** the package, and installs the package with
  `--force-confdef --force-confold`, so the generated config wins and the package skeleton lands
  beside it as `config.json.dpkg-dist` (observed on the live install). `install.sh` will not
  overwrite an existing config unless `--force-config` is passed. Ownership is therefore split by
  design — the bundle owns the live file, the package owns the skeleton — and the consequence is
  that a later package upgrade treats the config as a locally modified conffile: dpkg keeps the
  operator's file and leaves the new skeleton as `.dpkg-dist` rather than upgrading it silently.
  A future release may move the generated file out of the conffile path (or use `ucf`) so the two
  mechanisms stop overlapping. It writes `access-key` as root:root
  0400. It installs the certificate as `/usr/local/share/ca-certificates/hsm-server.crt` and runs
  `update-ca-certificates`, then verifies that the anchor actually took effect instead of trusting
  the exit code. Note the accepted cost: this trusts the HSM server certificate for **every** TLS
  client on the host, which is wider than the probe needs. The narrower alternative is the §4.1
  `ca_file` knob pointing at a probe-owned bundle; it stays off the critical path until the
  collector grows that option, then runs `systemctl enable --now hsm-linux-probe` and prints the
  unit status. It then deletes the extracted key file. `uninstall.sh` disables and purges the
  unit and package, removes the key, the CA file and the config, and never touches HSM history.
- **Deliberately not a `curl … | sudo bash` one-liner:** the download endpoint needs an admin
  session, and a token in the URL would put the product key's bearer into shell history, proxy
  logs and process args, which §4.3 forbids. The flow is the Windows one: download in the
  browser, copy to the host, run one command.
- **Key scope (this supersedes the "send-only key" wording in §4.3 and §8):** the bundle uses the
  same key selection as the Windows download (product DefaultKey, otherwise a key that can send
  data and add nodes and sensors) — registration needs add-node and add-sensor rights, so a
  strictly send-only key cannot register the sensor tree. It is
  product-scoped, not a master key. A dedicated, separately revocable per-download key is the
  same follow-up already listed for Windows. For garage-server, the operator can still issue a
  send-only key and swap the file; the runbook documents this.
- **Tests:** bundle contents and layout, a key that never appears in `config.json`,
  byte-identical `.deb`, a 503 with a clear message while no probe release is staged, the
  admin-only guard, and `install.sh` checked by shellcheck plus a smoke run in a `debian:13`
  container against a locally built `.deb`. The server pipeline guards (the Windows zip and the
  Docker image both contain the staged `.deb`) mirror the #1266 agent guards.

## 5. Resource budget & retention — measured on garage-server

Measured over 24 h on the real host (Debian 13, i5-2500), not estimated:

| | Target in the plan | Measured |
|---|---|---|
| Steady-state RSS | ≤ 64 MB | 3.9 MB reported by systemd right after start, 6.7 MB peak, 17–19 MB RSS including shared pages |
| CPU | ≤ 0.5 % of one core | ~0.2 % (180 s of CPU time in 24 h) |
| Restarts / errors | none | 0 restarts, 0 errors in the journal and on `.module/Collector errors` |
| Sensors | ~100 planned with Docker | **15** in the parity-only phase |
| History per bar sensor | — | 288 records/day (one 5-minute bar), against 5760/day before #1428 |

The original projection below assumed the full Docker/disk set; it stays as the estimate to
re-check when those sensors are agreed.

- Process: 1 (native Rust binary, no managed runtime); threads: collector scheduler/sender +
  probe timers (plain threads, no async runtime needed at this scale); no busy loops; all
  Docker/HSM requests time-boxed; queue bounded (collector policy). Expected steady-state
  RSS 5–20 MiB — comfortably inside the 64 MiB target; proposed `MemoryMax=64M` (raise to
  128M only if soak says otherwise), `CPUQuota=5%`.
- Cardinality at ~8 compose services: collector defaults ~12 + loadavg/cores 4 + docker
  ~8×8 + disks ~8 + backup ~5 + source-status ~5 ≈ **100 sensors** → ~130 k values/day at
  60 s. LevelDB growth and recommended `KeepHistory` computed on the eval server and attached
  to the PR (measured bytes/value × rate).

## 6. Test strategy

Collector workstream (§4.1): conformance scenario(s) for the Unix default set (both
drivers), native algorithm-contract fixtures (busy% delta incl. guest fields, CPU-count
change, counter reset, zero interval; MemAvailable parse incl. missing-field fallback and
malformed input), sanitizer lanes already cover threading. Managed side unchanged — its
existing `ProcParsersTests`/`LinuxProcSensorTests` remain the reference.
Probe unit tests (pure functions over saved anonymized fixtures, run on both CI OSes):
`/proc/loadavg` parse; Docker DTO parsing, label normalization + collision rule, CPU delta
math (1 core ⇒ 100%, 2 ⇒ 200%, reset ⇒ skip, recreate ⇒ new baseline, timeout ⇒ no value),
memory-limit semantics (finite/unlimited/cache convention), state machine
(running/health/OOM/restart-counter reset), backup-contract state machine.
Integration (ubuntu CI + local Docker): fake-server capture (types/paths/units; bar payloads
produced by the collector, no probe-side 5-min aggregation), test-CA chain accepted / wrong
cert rejected, `Key` header present without value leakage, send failure visible in
diagnostics not replaced by success; failure isolation; bounded timeouts.
Manual acceptance on garage: HDD-standby proof, CPU formula under controlled 1- and 2-core
load, compose-service recreate keeps path/baseline, soak with churn + HSM outage within
budget.

## 7. Delivery plan — PR slices (each an isolated agent worktree)

| # | Slice | Depends on | Contents |
|---|---|---|---|
| 0 | This initiative | — | Architecture review gate — especially the §4.1 collector workstream. |
| 1 | **Collector: Linux metric sources** | 0 | `hsm_linux_metric_sources.cpp` + `InstallLinuxMetricSources()` (C ABI + wrapper), platform-correct `add_all_computer_sensors`, conformance scenario + contract fixtures, docs, version bump, `collector-v<next>` tag + registry update. |
| 2 | Probe skeleton | 1 (published version) | `src/probe-linux/`: cargo workspace — `hsm-collector-sys` binding crate + safe wrapper (collector built from the pinned registry version via CMake/vcpkg in `build.rs`; first real `x64-linux` registry consumer — closes that untested gap), config, logging, lifecycle, defaults enabled, loadavg/cores sensors, systemd unit sample, ubuntu CI workflow (cargo fmt/clippy/test), first fake-server test. |
| 3 | Docker source | 2 | Engine-API client over unix socket, DTO/normalizer/identity/delta, SSD state persistence, fixtures. |
| 4 | Disks + backup contract | 2 (3 for tree shape) | SSD statvfs detail, archive snapshots, backup contract reader, standby runbook section. |
| 5 | Packaging + rollout kit | 2–4 | `.deb` build in `debian:13` CI container, `probe-v*` release workflow (§4.5), install/upgrade/rollback runbook, hardening finalized, soak + RSS/CPU measurements, retention table, `aicontext/` feature docs. |
| 6 | Per-product download bundle (#1424) | 5 for a real .deb (builder + endpoint can land first, 503 until a release is pinned) | §4.6: server bundle builder, admin-only endpoint + button, `probe-release.txt` staging in both server build legs, install.sh/uninstall.sh, tests. |
| — | Optional: `ca_file` transport knob | — | `CollectorOptions.ca_file` → `CURLOPT_CAINFO` + `native_http` test; not on the critical path (system trust store suffices). |

Left to the operator after review: create the product + send-only key, approve CA trust
install, approve alert thresholds and Telegram destinations, run the controlled rollout
(deployment plan in the task doc; rollback = disable unit + revoke key only).

## 8. Explicitly out of scope

Managed DataCollector changes; HsmAgent refactor; probe self-update (systemd + package
upgrade instead); porting Windows-only sensors (event logs, service status, network speed,
top-CPU, OS info) to Linux; block I/O sensors (optional follow-up); external independent
availability checker; changes to HSM server core, its container limits, compose file, backup
scripts, Windows Scheduled Tasks, or `docs/initiatives/ai-manageable-control-plane.md`.


## 9. What shipped (delivery log)

Phase 1 is in `master`. The collector went 0.7.0 → 0.8.1 across six PRs, each with conformance
coverage in both drivers and an agent version bump:

| PR | What | Versions |
|---|---|---|
| #1419 | this document | — |
| #1421 | Linux metric sources behind the existing factory seam; Unix registration on Linux | collector 0.7.0, agent 0.5.29 |
| #1420 | the Rust probe (`src/probe-linux`): sys crate, safe wrapper, systemd unit, CI lane | — |
| #1430 | built-in bars post partials of the catalog bar period instead of closing every 15 s | 0.7.2 / 0.5.31 |
| #1435 | the stop drain actually delivers on HTTP; restart re-registration fixed | 0.7.3 / 0.5.32 |
| #1436 | start/stop markers mirror managed; culture-invariant timestamps | 0.7.4 / 0.5.33, managed 3.5.1 |
| #1425 | per-product download bundle + `install.sh` generated by the server | — |
| #1438 | typed metric-source seam with error reporting; live disk prediction; `DiskLetter` fix | 0.8.0 / 0.5.34, managed 3.5.2 |
| #1446 | the prediction tells the truth: signed EMA, 6 h window, explicit states | 0.8.1 / 0.5.35, managed 3.5.3 |
| #1476 PR | alerts in the Rust wrapper; enum-with-options ABI; option-anchored bars/rates; probe-only host/disk sensors; `build-deb.sh` | 0.9.0 / 0.5.37, probe 0.2.0 |
| #1416 PR | Docker Compose source (7 sensors per service, Engine API over the socket via a dependency-free HTTP/1.1 client, restart/OOM/vanished state on SSD, conditional socket drop-in); collector: sensors created while running are registered on the server, alerts attachable while running | 0.9.1 / 0.5.38, probe 0.3.0 |
| #1481 PR | every mounted real filesystem: free space (MB, %), free inodes and write speed per filesystem, Windows per-drive naming, archives polled directly (standby measured safe), 10-min re-scan with runtime registration; `probe.disks` config | probe 0.4.0 |

**Verified live on garage-server**, not only in CI: installed through the server-generated
bundle exactly as an operator would, 15 sensors registered, every value cross-checked against
the host (CPU, `MemAvailable`, `df`, process RSS and thread count all matched), one 5-minute bar
per window instead of twenty, `Stop:` markers delivered across a restart, both archive HDDs
still in standby before and after (checked with `smartctl -n standby -i`, since garage has no
`hdparm`), 3.9–6.7 MB by systemd's cgroup accounting and 17–19 MB RSS
including shared pages, both against a 64 MB cap (§5), and no errors in the journal or on
`.module/Collector errors` over 24 h.

**Phase 2 — probe-only host/disk sensors (#1476, collector 0.9.0 / agent 0.5.37, probe 0.2.0).**
The Rust wrapper gained alert support (`AlertBuilder`, `attach_alert` on every sensor handle) and
`enum_sensor_with_options`, the foundation the Docker source (#1416) builds on. That needed one
additive C entry point, `hsm_collector_create_enum_sensor_with_sensor_options` (the managed
`Service status` shape: EnumOptions + AggregateData + alert), and exposed a native/managed
divergence fixed in the same release: bar and rate sensors created with options ignored
`is_computer_sensor` / `sensor_location` for the path. The probe registers the four sensors of
§4.2a on its own threads, and the `.deb` is now built by `src/probe-linux/packaging/build-deb.sh`
in a `debian:13` container instead of by hand; an upgrade restarts a running probe.

Trial on garage-server, 2026-09-28, `0.1.0~trial3` → `0.2.0~trial1` → `0.2.0~trial2` with the
existing config and credential untouched: `Registered 19 sensor(s) on connect` (15 + 4); `Logical
cores` = 4 (`nproc` 4); `CPU temperature` read from hwmon coretemp `Package id 0`, full 5-minute
bars of **60 samples** (12:00–12:05 UTC: mean 39.7 °C, min 32, max 50; the sensor read 43 °C at
the same time); `Free space on disk %` = 43.31 and `Free inodes %` = 83.28, matching `df -P /` /
`df -Pi /` (47159604 / 108897780 blocks, 5791935 / 6955008 inodes) on the same `/` mount the
collector's `Free space on disk` reports (46 054 MB). Cost after 15 min: cgroup memory 5.4 MiB
(peak 6.4 MiB) of 64 MiB, process RSS 18.5 MB, CPU 2.2 s in 883 s ≈ 0.25 % of one core against the
5 % quota, 7 tasks of 32; no warnings or errors in the journal; both archive HDDs still in standby
before and after. The server's Sensor API history shows a closed bar only once the next one
arrives (it keeps the newest bar in memory as the partial "last value") — a server behavior, the
UI shows the latest bar at once.

## 10. What the parity work uncovered

Building the probe was the cheap part. Comparing the two collectors byte-for-byte, and then
watching the result on a real host, found defects that had been shipping for months — **most of
them in the Windows agent as well**:

| Defect | Who was affected | Where |
|---|---|---|
| Built-in bars closed every 15 s instead of posting partials of a 5-minute bar → 20× the history records, EMA and alerts on the wrong grid | every native host, incl. the Windows agent | #1428 |
| The stop drain never delivered on the real HTTP transport (a cancel flag was never cleared) → data lost on every restart, **and** sensor re-registration broke after Stop→Start | same | #1432 |
| `dd/MM/yyyy` rendered as `22.09.2026` on a localised host, because `/` and `:` are separator placeholders in a .NET custom format string | managed, on any non-invariant machine | #1433 |
| The Windows factory bound letter-less disk rows to drive `N:` (the `n` of "on") | Windows hosts registering the Unix rows | #1426 |
| The disk-space prediction only folded shrinking samples, never decayed, and sent `00:00:00` during calibration — "the disk is full now" to an alert | both collectors | #1445 |
| Heartbeat thread / `last_error` data races, heartbeat driven by the wrong period, package size structurally always 0, scientific notation in an operator comment | native hosts | #1453, #1444, #1437, #1459, #1460 |

The lesson worth keeping: a second implementation held to byte-identical parity is a very
effective test of the first one, and a live host is a very effective test of both — three of the
defects above (the `N:` binding, the locale format, the prediction) are invisible to CI, which
runs on an invariant locale with no `N:` drive and a quiet disk.

## 11. What remains

**Needs an owner decision before any code:**
- the backup-contract sensors (#1417, §4.4) — the archive-disk capacity part is done by direct
  polling (#1481);
- whether the probe-only host sensors (logical cores, CPU temperature) ever move into the shared
  catalog and onto Windows — for now they stay Linux-probe-only (load average was rejected);
- when releases resume: `agent-v0.5.35` plus the `agent-release.txt` pin (without it none of the
  Windows-affecting fixes above reach deployed agents), the managed NuGet push, and the
  `probe-v*` channel (#1418).

**Known and tracked, no decision needed:** the fixes batched in PR #1462, merged (#1453, #1444,
#1437, #1459, #1460); the Windows `DiskRead` fractional-MB divergence; the managed
`InitAsync`/`StartAsync` calibration race.

**Operational note from the trial, unrelated to the probe:** garage-server's SSD was draining
about 1.3 GB/h, driven by the `lingua-ci` docker-in-docker volume; at that rate the disk would
have filled in under two days. It was found by reading the probe's own free-space history —
which is, after all, the point of the exercise.
