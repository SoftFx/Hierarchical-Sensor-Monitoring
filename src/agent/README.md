# HSM Agent

An always-on, CLR-free Windows service that hosts the native collector
([`src/native/collector`](../native/collector)) and streams this computer's metrics (CPU, RAM,
disks, network, and the collector's own self-sensors) to an HSM server.

It is a *probe*: drop it on a machine, it installs as an auto-start service, connects with a
per-product access key, and reports — **with zero client-side configuration**. The single signed
`hsm-agent.exe` is byte-identical across every download; only the bundled `config.json` differs
(server address + key). Part of epic
[#1167](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/issues/1167).

> Status: foundation (W1–W3 + logging) + the W8 build/packaging lane (CI artifact, install scripts,
> [docs](../../docs/hsm-agent.md)). The server-side per-product config download (W6/W7) and full E2E CI
> (W9) land in follow-up PRs. The first version ships the standard host sensors only — no plugin system.

## What it monitors

With a minimal config it registers and streams the standard host catalog via
`AddAllComputerSensors()` + `AddAllModuleSensors()`, with live values read through the Windows
PDH/Win32 metric sources (`InstallWindowsMetricSources()`). Computer sensors appear in the server
tree under `<computer>/.computer/...` for the product the access key belongs to.

## Build

The agent links the in-tree collector with the libcurl HTTP transport, so a vcpkg toolchain (for
curl) is required for the `hsm-agent` executable.

```sh
cmake -S src/agent -B src/agent/build/debug \
      -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake \
      -DCMAKE_BUILD_TYPE=Debug -DHSM_AGENT_WERROR=ON -DHSM_COLLECTOR_WERROR=ON
cmake --build src/agent/build/debug
ctest --test-dir src/agent/build/debug --output-on-failure   # config parser unit tests
```

The portable config parser (`hsm_agent_config`) and its unit tests build on every platform without
curl; only the `hsm-agent` executable is Windows + HTTP gated.

## Run

```
hsm-agent --console               run in the foreground (Ctrl-C to stop) — debugging
hsm-agent --install               register the auto-start service (Administrator)
hsm-agent --uninstall             stop + remove the service (Administrator)
hsm-agent --config <path>         override the config location
(no arguments)                    service mode — used by the Windows SCM
```

Install drops the service `HSMAgent` as **Automatic (Delayed)** with crash auto-restart, and
registers the `HSMAgent` Event Log source. Logs go to the Windows **Application** Event Log
(errors) and to `%ProgramData%\HSM Agent\logs\hsm-agent.log` (everything).

## Configuration

Default location: `%ProgramData%\HSM Agent\config.json` (see
[`config.template.json`](config.template.json)).

| Field | Default | Meaning |
|-------|---------|---------|
| `server.address` | — (required) | HSM Sensor API base URL |
| `server.port` | `44330` | HSM Sensor API port |
| `server.accessKey` | — (required) | per-product access key |
| `server.allowUntrustedCertificate` | `false` | accept a self-signed server cert |
| `identity.computerName` | `"auto"` | `"auto"`/blank → the machine host name |
| `identity.module` | `"HSM Agent"` | module segment for module sensors |
| `sensors.computer` | `true` | umbrella host bundle (CPU/RAM/disks/network/OS) |
| `sensors.system` / `disk` / `network` | `true` | finer subsets, used **only when `computer` is `false`** |
| `sensors.module` | `true` | collector self-sensors (alive / version / queue) |
| `sensors.process` | `false` | per-process sensors (opt-in) |
| `periods.collectMs` | collector default (15000) | package collect period |

Blank/missing `address` or `accessKey`, or an out-of-range `port`, make the agent refuse to start
(the error is logged to the Event Log and the file log).

> Note: in HTTP mode the collector's `SentCount()` stays 0 (it counts the in-memory recorder only) —
> verify delivery via the server history API (`POST :44330/api/sensors/history`), not `SentCount`.
