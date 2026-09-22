# Linux host + Docker Compose probe (garage-server)

> Status: **accepted** (owner review, 2026-09-22). Epic: #1413; workstreams #1414–#1418, #1424.
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
  backup JSON, the `curl` crate over the *same* libcurl for the unix-socket Engine API —
  one HTTP stack in the process); small static binaries, trivial systemd hosting. The
  collector's C ABI is a designed-for-FFI surface (the aggregator wrapper already consumes
  it from another toolchain), so this is intended use, not a workaround.
- Accepted costs, stated openly: a new toolchain in repo + CI (cargo on the ubuntu lane,
  collector built by CMake/vcpkg first, linked from `build.rs`); a small `hsm-collector-sys`
  binding crate + safe wrapper for the subset of the ABI the probe uses (options, transport,
  lifecycle, instant/enum/bar sensors, logger callback); FFI discipline (no panic unwind
  across `extern "C"` — `catch_unwind` at every callback boundary). If review rejects the
  new toolchain, the fallback is the same architecture in C++ over `hsm_collector_cpp` —
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
        │     ├── docker (60 s): Docker Engine API over unix socket (curl UNIX_SOCKET_PATH,
        │     │     one-shot stats; no `docker stats` stream, no external CLI)
        │     ├── ssd (5 min): statvfs on the filesystem containing /srv/docker
        │     └── backup/archive: timestamped JSON snapshots on SSD (never touches /mnt/*)
        └── probe state on SSD (restart counters, OOM latches, last-seen backup result)
```

Key properties: one process; scheduling/queuing/batching/retry are the collector's; each
probe source is exception-isolated behind its own catch with a visible per-source status
sensor (no silent loss); Docker timeout/partial data ⇒ skipped values + diagnostic, never
zeros/`healthy`/re-stamped stale data; archive HDDs are never touched by any polling path.

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
Windows nodes (`hsm_collector.cpp:7089` registers `Windows Version`/`Windows Last Restart`
etc. that can never produce values off-Windows).

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

### 4.2 Sensor tree (computer=`garage-server`, module=`LinuxProbe`, dedicated product)

Default-sensor paths/types/options are whatever the published collector registers — not
redefined here. Probe-added sensors (all via the public API):

| Path (under garage-server/LinuxProbe/) | Type | Period | TTL | Notes |
|---|---|---|---|---|
| `CPU/Load average {1m,5m,15m}` | Double ×3 | 60 s | 3 min | `/proc/loadavg`. Exists in no collector; graduation into the shared catalog is a separate rule-#9/#10 decision. |
| `CPU/Logical cores` | Int | start + daily | 48 h | Host capacity, so operators read container % correctly. |
| `Disk/srv-docker/{Free %, Free inodes}` | Double | 5 min | 15 min | statvfs on the mount actually containing `/srv/docker` (resolved, not assumed); free MB comes from the collector's disk sensor. |
| `Disk/archive/{wd4tb,mediacentr}/{Free GB, Snapshot age}` | Double | on snapshot change | ≥ backup window + slack (~26 h) | From SSD snapshot JSON only. Stale = TTL expiry, a distinct state. |
| `Docker/<project>/<service>/CPU % one core` | DoubleBar | 60 s | 3 min | Δ cumulative cgroup counter between two *valid* samples; 100% = one core (host may show up to 400%). First sample / counter reset / container-id change / abnormal interval ⇒ skip, never 0. |
| `Docker/<project>/<service>/Memory {usage,limit} bytes` | Double ×2 | 60 s | 3 min | usage = cgroup usage − `inactive_file` (docker-stats convention, fixture-pinned). Unlimited limit ⇒ host MemTotal as effective limit, flagged in comment — never 0. |
| `Docker/<project>/<service>/Running` | Bool | 60 s | 3 min | Missing expected service ≠ running. |
| `Docker/<project>/<service>/Replicas running` | Int | 60 s | 3 min | Service-level aggregate (CPU/mem summed, health worst-of); instance sensors deliberately absent in v1. |
| `Docker/<project>/<service>/Health` | Enum {starting, healthy, unhealthy, none} | 60 s | 3 min | No healthcheck ⇒ `none`, never `healthy`. Stable enum options registered once. |
| `Docker/<project>/<service>/OOM killed` | Bool | 60 s | 24 h | Latched on SSD for configurable retention (default 24 h); survives recreate. |
| `Docker/<project>/<service>/Restart count` | Int | 60 s | 3 min | Service-level cumulative counter on SSD; only positive observed deltas; container-id change starts a new baseline (no negative deltas). |
| `Backup/<job>/{Last result, Duration min, Missed deadline}` | Enum/Double/Bool | on new result | job-specific | From the backup task's snapshot contract (§4.4). |
| `Backup/<job>/Last success heartbeat` | Bool/TTL | only on *new* confirmed success | deadline-derived | Re-reading the same result never refreshes it; "never succeeded yet" is a distinct initial state. |
| `Probe/Sources/<name> status` | Enum {ok, degraded, failed} | 60 s | 3 min | Per-source failure isolation made visible. |

Path identity: from `com.docker.compose.project`/`.service` labels — stable across
recreate/upgrade. Normalization: `[A-Za-z0-9_-]` kept, others → `_`, collisions detected via
a reverse map and disambiguated with a short stable hash; rule fixed by unit tests.
Containers without compose labels: config `composeOnly: true` (default) skips them with a
diagnostic; `false` puts them under `Docker/_standalone/<name>`.

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
Client: the Rust `curl` crate over `CURLOPT_UNIX_SOCKET_PATH` — the same libcurl the
collector's HTTP transport links, one HTTP stack per process; JSON via serde_json. Both are
probe-only dependencies, never added to the collector.

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
- `archive-capacity/<disk>.json`: `{ disk, sampled_at, free_bytes, total_bytes }` — produced
  during the backup window while the disk is already awake.

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

Ship as a **`.deb` package published through a GitHub Release**, mirroring the repo's
existing release channels (`agent-v*`, `wrapper-v*`): tag `probe-v<version>` → CI workflow
(build in a `debian:13` container for the glibc/dependency baseline, `cargo build --release`,
package, tests) → Release with the `.deb` + its sha256, `--latest=false` as usual.

The package carries the binary, the hardened systemd unit, a config skeleton in
`/etc/hsm-linux-probe/` (dpkg conffile — operator edits survive upgrades), and a postinst
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
  - `server-ca.pem`: the server's **public** certificate chain, with no private key. This keeps
    TLS verification on for self-signed installs, which Windows gets only by baking
    `allowUntrustedCertificate`. The Linux probe never exposes that switch. If the server's
    certificate is already publicly trusted (the Caddy/Let's Encrypt setup from #1411), the file
    is omitted;
  - `install.sh` / `uninstall.sh`.
- **Client side:** `tar xzf hsm-linux-probe-<product>.tar.gz && sudo ./install.sh`. The script
  refuses to run as non-root. It runs `apt install ./hsm-linux-probe_*.deb`, puts
  `config.json` in `/etc/hsm-linux-probe/` (as a dpkg conffile, so a later reinstall keeps the
  operator's edits unless `--force-config` is passed), and writes `access-key` as root:root
  0400. It adds `server-ca.pem` to `/usr/local/share/ca-certificates/` and runs
  `update-ca-certificates`, then runs `systemctl enable --now hsm-linux-probe` and prints the
  unit status. It then deletes the extracted key file. `uninstall.sh` disables and purges the
  unit and package, removes the key, the CA file and the config, and never touches HSM history.
- **Deliberately not a `curl … | sudo bash` one-liner:** the download endpoint needs an admin
  session, and a token in the URL would put the product key's bearer into shell history, proxy
  logs and process args, which §4.3 forbids. The flow is the Windows one: download in the
  browser, copy to the host, run one command.
- **Key scope:** the bundle uses the same key selection as the Windows download (product
  DefaultKey, otherwise a key that can send data and add nodes and sensors). It is
  product-scoped, not a master key. A dedicated, separately revocable per-download key is the
  same follow-up already listed for Windows. For garage-server, the operator can still issue a
  send-only key and swap the file; the runbook documents this.
- **Tests:** bundle contents and layout, a key that never appears in `config.json`,
  byte-identical `.deb`, a 503 with a clear message while no probe release is staged, the
  admin-only guard, and `install.sh` checked by shellcheck plus a smoke run in a `debian:13`
  container against a locally built `.deb`. The server pipeline guards (the Windows zip and the
  Docker image both contain the staged `.deb`) mirror the #1266 agent guards.

## 5. Resource budget & retention (to validate in the PR)

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
