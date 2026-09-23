# HSM Linux probe (`hsm-linux-probe`)

A systemd-hosted Linux host probe that reports into an existing HSM server by **hosting the shared
native collector** (`src/native/collector`) through its stable C ABI.

Architecture and rationale: [`docs/initiatives/linux-docker-probe.md`](../../docs/initiatives/linux-docker-probe.md)
(epic #1413). This directory is workstream 2 (#1415) — the skeleton.

## The one rule that shapes everything here

**The probe implements no sensor the collector already has.** Metric acquisition lives in the
shared collector, conformance-locked against the managed one (root `CLAUDE.md` rules #9/#10); a
probe-local reimplementation would be a permanent semantic/wire divergence.

**Phase 1 — parity first.** The probe registers exactly the sensor set the managed HSMDataCollector
registers on Linux (`UnixSensorsCollection.AddAllDefaultSensors`: computer set + module set) and
nothing of its own. Probe-only signals — Docker (#1416), disks and backups (#1417) — come later, and
will go through the collector's public sensor API so wire format, queuing, batching, retry and TLS
stay the library's.

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
| `.module/Collector queue stats/Package content size` | DoubleBar · MB | — | as Items count | yes / only after 5 min | **Finding F2** |
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

## Crate layout

```
src/probe-linux/
  hsm-collector-sys/   raw FFI declarations for the ABI subset the probe uses + the CMake build
  hsm-collector/       safe RAII wrapper: Collector, typed sensor handles, log sink
  hsm-linux-probe/     the binary: config, logging, signals, lifecycle wiring, sensor registration
  packaging/           systemd unit sample + config skeleton
```

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
set, `LoadCredential`) and the §5 budget (`MemoryMax=64M`, `CPUQuota=5%`). `SupplementaryGroups=docker`
is commented out on purpose: the Docker socket is root-equivalent and is granted only when the
Docker source (#1416) exists to need it.
