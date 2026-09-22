# HSM Linux probe (`hsm-linux-probe`)

A systemd-hosted Linux host probe that reports into an existing HSM server by **hosting the shared
native collector** (`src/native/collector`) through its stable C ABI.

Architecture and rationale: [`docs/initiatives/linux-docker-probe.md`](../../docs/initiatives/linux-docker-probe.md)
(epic #1413). This directory is workstream 2 (#1415) — the skeleton.

## The one rule that shapes everything here

**The probe implements no sensor the collector already has.** Metric acquisition lives in the
shared collector, conformance-locked against the managed one (root `CLAUDE.md` rules #9/#10); a
probe-local reimplementation would be a permanent semantic/wire divergence. The probe only adds
signals that exist in *no* collector today, and even those go through the collector's public sensor
API, so wire format, queuing, batching, retry and TLS are always the library's.

In this slice that means exactly two probe-added sources:

| Sensor | Type | Source | Period | TTL |
|---|---|---|---|---|
| `CPU/Load average {1m,5m,15m}` | Double ×3 | `/proc/loadavg` | 60 s | 3 × period |
| `CPU/Logical cores` | Int | `/proc/cpuinfo` | daily (also on start) | 2 × period |
| `Probe/Sources/{loadavg,logical-cores} status` | Enum {ok, degraded, failed} | probe-internal | with its source | as its source |

Docker, disk and backup sources are workstreams #1416/#1417 and are deliberately absent.

## Crate layout

```
src/probe-linux/
  hsm-collector-sys/   raw FFI declarations for the ABI subset the probe uses + the CMake build
  hsm-collector/       safe RAII wrapper: Collector, typed sensor handles, log sink
  hsm-linux-probe/     the binary: config, logging, signals, lifecycle wiring, the two sources
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
* **Struct layouts are asserted.** `hsm-collector-sys` carries size/alignment tests for the three
  structs that cross the ABI — the only way a silent field insertion gets caught, since the Rust
  compiler never sees the C header.
* **The access key is a `Secret`.** Redacted from `Debug`/`Display`, wiped on drop, never in argv,
  never in an error message, never in the config file.

## The #1414 feature gate

`hsm_collector_install_linux_metric_sources` — the entry point that makes the collector's default
host catalog (Total CPU, Free RAM, free disk, process counters) produce live values on Linux — is
added by workstream 1 (#1414) and **does not exist in master's collector**. So:

* the declaration lives behind the cargo feature `linux-default-sensors`, **off by default**;
* with the feature off the crate builds and runs against today's collector, and the probe logs a
  loud warning at startup saying the default host catalog will not be reported;
* with the feature off the probe also **skips registering** the `.computer` catalog, rather than
  showing the operator a tree of nodes whose values can never arrive;
* with the feature on against an older collector, the link fails — deliberately, so the gap is
  never silent.

Enable it once #1414 is merged: `cargo build --features linux-default-sensors` (and eventually make
it the default here).

## Platform support

**Linux is the only supported target.** OS-specific code is `cfg`-gated so the pure parts (config
parsing, `/proc` parsers, log formatting) compile and test anywhere, but the probe is built, tested
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
  a master key). Under systemd that is the `LoadCredential=` drop at
  `/run/credentials/hsm-linux-probe.service/access-key`. The probe reads it at startup, warns if it
  is readable beyond its owner, and wipes its in-process copies once the collector has taken it.
* `hsm.address` must be `https://…`. Plaintext is rejected by config validation, and the wrapper
  deliberately does not expose the ABI's `allow_untrusted_server_certificate` flag — it disables
  both peer and hostname verification, which §4.1/§4.3 ban. Trust a private CA by installing it
  with `update-ca-certificates`; libcurl/OpenSSL picks up the system store with verification on.

## Running

```bash
hsm-linux-probe --config /etc/hsm-linux-probe/config.json
hsm-linux-probe --version    # probe version + the linked collector version
```

`SIGTERM` (systemd stop/restart) and `SIGINT` request a graceful stop: the sampling loop exits
within 200 ms and the collector drains with its own bounded stop, so a host restart is never held
up. An overrun of `shutdownTimeoutSec` is logged; `TimeoutStopSec` in the unit is the backstop.

`packaging/hsm-linux-probe.service` carries the §4.3 hardening (`NoNewPrivileges`,
`ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `StateDirectory`, empty capability bounding
set, `LoadCredential`) and the §5 budget (`MemoryMax=64M`, `CPUQuota=5%`). `SupplementaryGroups=docker`
is commented out on purpose: the Docker socket is root-equivalent and is granted only when the
Docker source (#1416) exists to need it.
