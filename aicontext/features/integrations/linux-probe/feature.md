# Feature: Linux Probe (`hsm-linux-probe`)

> Owner: integrations | Last reviewed: 2026-09-23 | Canonical: yes
> Scope: The systemd-hosted Linux host probe in `src/probe-linux/` — a Rust process that hosts the native collector through its stable C ABI. Owns the host wiring (config, secrets, logging, lifecycle, packaging) and the sensor set it registers; owns no wire or sensor semantics.

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
| `hsm-collector` | Safe RAII wrapper: `Collector`, typed sensor handles, log sink |
| `hsm-linux-probe` | The binary: config, secrets, logging, signals, lifecycle, sensor registration |

**Phase 1 is parity.** The probe registers exactly the set the managed `HSMDataCollector` registers
on Linux (`UnixSensorsCollection.AddAllDefaultSensors`) and nothing of its own. The full parity
table — every path, type, unit, alert, posting cadence and whether a live value arrives, captured
from both collectors against one fake Sensor API — is the contract, and lives in
[`src/probe-linux/README.md`](../../../../src/probe-linux/README.md). Probe-only sources (Docker
#1416, disks and backups #1417) come in later workstreams and will go through the collector's
public sensor API, so wire format, queuing, batching, retry and TLS stay the library's.

Linux is the only supported target. The initiative is
[`docs/initiatives/linux-docker-probe.md`](../../../../docs/initiatives/linux-docker-probe.md).

---

## Invariants

- **No probe-local reimplementation of any sensor the collector has.** A second implementation next
  to the managed one is the divergence class rules #9/#10 forbid; sensors the catalog lacks are
  added to the shared collector, not here.
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
- `collector/` — all wire and sensor semantics; the probe adds none.
