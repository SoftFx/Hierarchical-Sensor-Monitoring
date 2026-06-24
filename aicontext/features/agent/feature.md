# Feature: HSM Agent (Windows service)

> Owner: agent | Last reviewed: 2026-06-25 | Canonical: yes
> Scope: The standalone **HSM Agent** product (`src/agent/`) — an always-on Windows service that hosts the native collector and streams this computer's metrics to an HSM server with zero client-side configuration. This feature owns the service host, config, and logging; it owns NO wire behavior (that all lives in `collector/` + `integrations/native-collector`).

---

## Description

The HSM Agent is a separate, CLR-free product **built on top of** the native collector
(`hsm_collector_cpp`, epic #1093). It is a probe: install it on a machine, it runs as an auto-start
Windows service (`HSMAgent`), snapshots the host (CPU/RAM/disks/network + the collector's own
self-sensors), and streams to an HSM server. Tracked by epic #1167.

The agent only *hosts* an already-proven pipeline: live HTTP transport (#1165, `UseHttpTransport`) and
Windows PDH/Win32 live readers (#1164, `InstallWindowsMetricSources`). `examples/windows-monitor` is
the console-shaped predecessor; `AgentRuntime` is its productionized, service-managed form.

**Pure-native, runs anywhere.** The agent and its installation are 100% C++ — no .NET runtime on the
target machine, no C#/MSI installer. Install is the exe's own `--install` (SCM registration). The
first version monitors the **standard host sensors only**; there is intentionally **no plugin system**
(extra sources are added later, separately, not via a config-declared plugin layer).

**Killer UX — zero client configuration.** An admin downloads a per-product config bundle from the HSM
web UI; the bundle carries the server address + access key in a `config.json`, and the client runs the
(byte-identical, pre-built) C++ exe which self-installs. The only server-side part is serving a
zip — never a .NET installer. (Server-side download = W6/W7, follow-up.)

**Foundation status (this feature's current extent):** W1–W3 + the logging half of W4 — a runnable,
self-installing, auto-start service that streams the standard host catalog, plus the W8 build/packaging
lane (CI artifact + install scripts + docs). Out of scope here (own follow-up PRs): the server-side
per-product config download + zip bundle (W6/W7), and full E2E CI (W9 — wiring the artifact into the
server image + the install→data→stop run). A plugin system is **not** planned for v1.

---

## Invariants

- **Signed-exe invariant.** The single `hsm-agent.exe` is byte-identical across every download; all
  per-install/per-product data (server address, key, identity, sensor groups) lives in a separate
  `config.json`. The PE is never patched — preserving Authenticode (no SmartScreen/AV breakage). The
  agent enforces this structurally by reading config from a file, never from embedded bytes.
- **Self-contained exe.** The Windows build is statically linked (`x64-windows-static` triplet +
  static CRT, forced in `src/agent/CMakeLists.txt`): the bundle ships one `hsm-agent.exe` with no
  libcurl/zlib DLLs or VC++ redistributable, so it runs on a clean machine. curl uses Schannel (no
  OpenSSL). This is what keeps the single-exe bundle (above) actually runnable after download.
- **No wire distortion.** The agent adds no payload behavior; it calls the collector's public API
  (`AddAllComputerSensors`/`AddAllModuleSensors`/`UseHttpTransport`/`InstallWindowsMetricSources`).
  All wire/parity contracts remain the collector's.
- **Single instance.** A named mutex (`Global\HSMAgent`) prevents a console run and the service from
  double-sending. (Best-effort: a non-elevated user who cannot create a Global object simply skips the
  guard rather than being blocked.)
- **Bounded, crash-resistant lifecycle.** The collector runs on a worker thread, never the SCM
  dispatcher thread. A server outage never bubbles an exception out of the worker (the collector's
  bounded retry queue + a `try/catch` around the runtime loop); the service stays up. A *fatal* error
  returns non-zero so the SCM failure-actions restart it.
- **Config validation refuses bad input loudly.** Blank/missing `server.address` or `server.accessKey`,
  or an out-of-range `port`, make the agent refuse to start, logging the reason to the Event Log + file
  log (not silently degrade).

## Service model

- SCM service `HSMAgent`, **Automatic (Delayed)** start, auto-restart on crash (restart after 60s,
  reset after a day). Installed via `--install` (needs elevation); removed via `--uninstall`.
- `main()` dispatches on argv: `(no args)` → SCM dispatcher (`StartServiceCtrlDispatcherW` →
  `ServiceMain` → `RegisterServiceCtrlHandlerExW`, status machine
  `START_PENDING → RUNNING → STOP_PENDING → STOPPED`); `--console` → foreground run with a Ctrl-C
  handler (debugging); `--install`/`--uninstall` → SCM management; `--config <path>` → override.
- On-disk layout: exe at `C:\Program Files\HSM Agent\` (placed by the installer, W7); config at
  `%ProgramData%\HSM Agent\config.json`; logs at `%ProgramData%\HSM Agent\logs\hsm-agent.log` + the
  Windows Application Event Log (source `HSMAgent`, registered at install).

## Config schema (`config.json`)

Parsed by a small dependency-free recursive-descent JSON reader (`src/config.cpp`) — the collector
exposes no general parser and the epic mandates minimal deps. Platform-agnostic so the parser is unit
tested on every CI lane. Only `server.address` + `server.accessKey` are required; everything else
defaults. Maps onto `CollectorOptions` + runtime group gates:

- `server.{address,port=44330,accessKey,allowUntrustedCertificate}` → connection options.
- `identity.computerName` (`"auto"`/blank → `GetComputerNameExW` physical DNS host name) → `computer_name`;
  `identity.module` → `module`.
- `sensors.{computer,system,disk,network,module,process}` — `computer` is the umbrella host bundle;
  `system`/`disk`/`network` are finer subsets used **only when `computer` is false**; `module` =
  collector self-sensors; `process` is opt-in.
- `periods.collectMs` → `package_collect_period_ms`. `productVersion` → module-sensor version.
- `topCpu.{enabled=false,periodMs=60000,minPercent=1.0,count=10}` — opt-in "top processes by CPU"
  (issue #1175). Validated only when enabled (period/count > 0, minPercent ≥ 0).
- `update.{enabled=true,checkPeriodHours=24}` — self-update channel (epic #1174). When enabled the
  agent periodically polls `GET <server>/api/agent/version` and self-updates when the server advertises
  a newer version AND its global `AutoUpdateEnabled` flag is true. `enabled=false` opts this machine out
  regardless of the server's flag. Validated: `checkPeriodHours ≥ 1`.

## Self-update (epic #1174)

Server-driven auto-upgrade of the running Windows service. The server exposes:
- `GET /api/agent/version` — public, returns `{ version, sha256, updateEnabled }`. Version is read from
  `wwwroot/agent/version.txt` (staged by CI). SHA-256 is computed from the staged exe at request time.
  `updateEnabled` mirrors `ServerConfig.Agent.AutoUpdateEnabled` (admin toggle, default false).
- `GET /api/agent/exe` — Key-header auth (agent's own access key), returns the binary stream with
  `X-Agent-Sha256` response header.

`UpdateChecker` (`src/agent/src/update_checker.cpp`, Windows-only) runs on a background thread after
`collector.Start()`. Flow per poll:
1. Fetch version manifest (WinHTTP, TLS enforced; `allow_untrusted_certificate` honoured for eval setups).
2. Parse version string into `(major, minor, patch)`; skip if `remote ≤ current` (downgrade protection).
3. Skip if `updateEnabled == false` (server kill-switch).
4. Download exe (`Key` header), verify SHA-256 against manifest value.
5. Write to `%ProgramFiles%\HSM Agent\hsm-agent.new.exe`.
6. Spawn `hsm-agent.exe --apply-update` as a detached process.
7. Call `RequestStop()` → graceful service shutdown begins.

`--apply-update` (`src/agent/src/apply_update.cpp`) is the detached binary-swap helper:
1. Wait up to 60 s for `HSMAgent` service to reach `STOPPED`.
2. Rename dance: `hsm-agent.exe → hsm-agent.old.exe`; `hsm-agent.new.exe → hsm-agent.exe`.
3. `StartService(HSMAgent)`.
4. Health gate: wait 30 s for `SERVICE_RUNNING`; on failure, roll back (`hsm-agent.old.exe →
   hsm-agent.exe`), restart old version, log error to Event Log.

On next startup, `AgentRuntime::Run()` deletes `hsm-agent.old.exe` to confirm the new version is live.

**Admin kill-switch:** `Agent.AutoUpdateEnabled: false` in `appsettings.json` (default). Flip to true
when ready to roll out; flip back to halt. Per-machine opt-out: set `update.enabled: false` in the
machine's `config.json`.

## Top processes by CPU (issue #1175)

Opt-in (`topCpu.enabled`). A dedicated thread in `AgentRuntime` (started after `Start()`, joined
before `Stop()`, waits on the same `cv_` so it exits the moment stop is requested) runs every
`periodMs`:
1. `WindowsCpuSampler` (`src/cpu_top.cpp`, `#ifdef _WIN32`) enumerates processes (`CreateToolhelp32Snapshot`
   + `GetProcessTimes`), diffs each PID's CPU time against the previous tick, and aggregates by exe name
   into CPU% **of the whole machine** (÷ logical-core count).
2. `SelectTopN` (portable, unit-tested) keeps names `>= minPercent` and returns the busiest `count`.
3. Each survivor's % is posted to a lazily-created `Top CPU processes/<exe>` Double sensor (cached by
   name). Names not in the top list that tick get **no value** → natural gaps in their series.
   The distinct-name set is **capped at `max(count * 8, 64)`** in both collectors (the server sensor
   registry is permanent, with no delete API): once the cap is reached, newly seen process names are
   skipped so a churn-heavy host can't grow the namespace without bound or exhaust the global MaxSensors.

Aggregating by exe name (not PID, not PDH `#n` suffix) keeps a stable sensor identity over time.
**Out of scope:** browser tab/site attribution (needs browser-level instrumentation; tracked separately).

## Logging

`AgentRuntime` installs a fan-out `SetLogger`: the **rolling file** sink takes every line
(`%ProgramData%\HSM Agent\logs\hsm-agent.log`, size-rolled to `.1`); the **Event Log** sink takes
Error-level lines (plus explicit service start/stop INFORMATION entries). Console mode also mirrors to
stderr. The collector's own dedup window keeps a flapping server from spamming the log.

> Note: the Event Log source points `EventMessageFile` at the exe, which has no embedded message table,
> so the Event Viewer shows the inserted text with a generic wrapper note. A dedicated message resource
> is a cosmetic follow-up.

## Verification

- Portable unit tests (`tests/agent_tests.cpp`, name-dispatched, 13 cases — config parser + topCpu config + update config) — run on every
  platform, registered in `ctest`.
- **CI build lane (W8):** `.github/workflows/agent-windows-build.yml` — windows-latest, vcpkg curl,
  configures + builds `src/agent` Release with warnings-as-errors, runs the config `ctest`, and uploads
  the bundle payload (`hsm-agent.exe` + `config.json` template + `install.cmd`/`uninstall.cmd`) as an
  artifact.
- **Service smoke (W9):** the same lane then exercises the real SCM lifecycle on the admin runner —
  `--install` → `sc qc HSMAgent` shows `AUTO_START` → a `--console` run survives an unreachable server
  (no crash) → `--uninstall`. The exe is also staged into the server image by `server-build.yml`
  (copied into `wwwroot/agent/` before publish) so the download serves a real bundle.
- Live E2E mirrors the already-proven collector recipe (#1166): against a Dockerized
  `hsmonitoring/hierarchical_sensor_monitoring:latest`, the host sensors land in the tree under
  `<computer>/.computer/...` for the key's product. In HTTP mode `SentCount()` stays 0 (in-memory
  recorder only) — verify via `POST :44330/api/sensors/history`, not `SentCount`.

## Pointers

- Host pipeline it consumes: `integrations/native-collector` (RAII API), `collector/*` (wire/parity).
- Epic: #1167. Builds on epic #1093 + PR #1166 (#1164/#1165).
