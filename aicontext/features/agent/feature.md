# Feature: HSM Agent (Windows service)

> Owner: agent | Last reviewed: 2026-10-13 | Canonical: yes
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

## Data directory access (issue #1279)

`config.json` holds the product access key in cleartext — a bearer credential (the `Key` header) that
is enough to post data as the product and to pull `GET /api/agent/exe`. `%ProgramData%` hands every new
subfolder an inheritable `BUILTIN\Users` read ACE, so before #1279 any local user could read the key.

- **Admin-only DACL, Administrators as owner.** `%ProgramData%\HSM Agent` carries a **protected**
  DACL (`SE_DACL_PROTECTED`, inheritance from `%ProgramData%` blocked) with exactly two ACEs:
  `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators`, full control, `(OI)(CI)` — `icacls` shows
  nothing else. No `Users`, `Authenticated Users`, `Everyone` or `CREATOR OWNER`. The same call
  makes `BUILTIN\Administrators` the **owner**: an owner keeps an implicit `WRITE_DAC` whatever the
  DACL says, and `Users` may create folders in `%ProgramData%`, so a dir pre-created by a non-admin
  could otherwise be re-opened by that user. Built from well-known SIDs (`CreateWellKnownSid`), never
  account names, so it works on localized Windows. Helper: `ApplyAgentDirectoryDacl`
  (`src/agent/src/dir_acl.cpp`, `SetNamedSecurityInfoW` with `OWNER_SECURITY_INFORMATION` +
  `PROTECTED_DACL_SECURITY_INFORMATION`). If the owner change is refused (e.g. an unelevated
  `--console` run: `ERROR_INVALID_OWNER`), the DACL is still applied and the owner failure is
  reported.
- **The directory is hardened, not the file.** Files get their rights by inheritance from the data dir.
  The directive-driven config rewrite (`WriteAgentConfig`: write `config.json.tmp` in the same dir,
  then `std::filesystem::rename` = `MoveFileExW(REPLACE_EXISTING)` over `config.json`) keeps the
  tightened rights: the temp file is created inside the hardened dir, so it inherits SYSTEM +
  Administrators, and a same-volume rename keeps the moved file's security descriptor. A per-file ACE
  would have been dropped by that rewrite; the inherited ones cannot be.
- **The temp file is always created fresh.** Before #1279 `Users` could add files to the data dir, so
  any local user could plant `config.json.tmp` and stay its owner. Hardening replaces only inherited
  ACEs, so such a file would keep its owner and explicit ACEs; opening it with `trunc` and renaming it
  would carry that descriptor onto `config.json` together with the key. `WriteAgentConfig` therefore
  deletes any existing `config.json.tmp` first, and **fails the rewrite** (logged, no restart) if it
  cannot, rather than write the key into a file it did not create.
- **`logs\` gets the same treatment.** The log never contains the key, but it does carry host,
  process and service names. `logs\` receives its own protected copy of the same DACL and owner, so it
  stays closed even if someone later loosens the data dir by hand.
- **Junctions and symlinks are refused.** If the data dir or `logs\` is a reparse point (a non-admin
  could pre-create either as a link to a directory of their choosing), `ApplyAgentDirectoryDacl`
  changes nothing and reports "refusing to change the security of … it is a reparse point", and
  `EnsureDirectories` does not create `logs\` through a linked data dir. The install script aborts
  before `icacls` when `%DATA_DIR%` is one. The fix is to remove the link and reinstall.
- **Self-heal on every start.** `EnsureDirectories()` creates both dirs and re-applies the owner + DACL
  on every service start (and on `--console` and `--install`). It is idempotent. The agent owns these
  ACLs: an operator-added ACE on either dir is removed at the next start. **Upgrade path:** an agent
  installed before #1279 (0.5.x, data dir and `config.json` inheriting `Users` read) is repaired on the
  first start of the new binary, whether it arrives by self-update (`--apply-update` restarts the
  service) or by `update-hsmagent-service.cmd`. `SetNamedSecurityInfoW` re-propagates the new
  inheritable ACEs to existing children, so `config.json` and the log files are tightened in place and
  their contents are untouched. Only inherited ACEs are replaced; the owner of existing child files is
  not reset. An explicit ACE someone put on `config.json` itself survives (out of scope).
- **Rotate the key after upgrading a pre-#1279 host.** The repair closes the hole from that start on,
  but on every host that ran a pre-#1279 agent the key was already readable by every local user,
  possibly for months. Operators should **rotate the product access key** (issue a new key,
  re-download the bundle, update `config.json` on each machine) once the fleet runs the fixed agent.
- **Failure is loud, never fatal (rule #6).** If the owner or DACL cannot be applied, each failure is
  logged at Error level to the file log and the Event Log (service) or to stderr + the file log
  (`--console`, `--install`). The service still starts, because it must still read its config. A
  non-elevated `--console` run cannot hand ownership to Administrators and, against an installed data
  dir, also hits `ACCESS_DENIED` on the DACL (and can no longer read the default config — by design).
  Run console mode elevated.
- **Install scripts.** `install-hsmagent-service.cmd` (the static copy in `src/agent/packaging/` and
  the server-generated copy in the download bundle, which must stay identical) runs
  `icacls "%DATA_DIR%" /setowner *S-1-5-32-544` and then
  `icacls "%DATA_DIR%" /inheritance:r /grant *S-1-5-18:(OI)(CI)F /grant *S-1-5-32-544:(OI)(CI)F`
  (icacls cannot combine `/setowner` with `/grant`) **before** copying `config.json` in. That removes
  the inherited `Users` read, so a fresh config is not readable by users. It is not airtight on its
  own: `/grant` adds to the DACL rather than replacing it, so an explicit ACE someone added to the dir
  by hand survives until `--install` (later in the same script) and every service start re-apply the
  exact DACL; and on a reinstall the existing `config.json` keeps any explicit ACE of its own.
- **Out of scope:** DPAPI/CNG encryption of the key at rest (the service must read it unattended, so
  machine-scoped encryption only moves the secret away from non-admins, which the ACL already does);
  per-download revocable agent keys; `%ProgramFiles%\HSM Agent` rights (admin-write / user-read by
  default, the binary is not a secret); resetting the owner of files already inside the data dir (the
  only one the agent writes into, `config.json.tmp`, is recreated instead); a dir **and** `config.json`
  both planted by a non-admin before the very first install (the script keeps an existing config).

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
  `wwwroot/agent/version.txt` — gitignored, derived from the pinned agent release
  (`agent-release.txt`) when CI stages the exe, so it always matches the staged binary (see
  `server/agent-download/feature.md` → *Packaging*; a stale value here disables this whole channel
  silently). SHA-256 is computed from the staged exe at request time.

  The agent itself ships as a GitHub Release: push an `agent-v<version>` tag matching
  `project(HsmAgent VERSION …)` and `agent-release.yml` builds, smokes, and publishes
  `hsm-agent.exe` + its SHA-256 (#1298). Servers consume that release by pin — see the release
  procedure in `src/agent/CLAUDE.md`.
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

## Server directive channel (issue #1198)

The server piggybacks `X-Hsm-Directive` response headers on data-POST responses to push config to the
agent without extra polling. The agent advertises its build via an `X-Agent-Version` request header;
directives are emitted on **both** the typed value endpoints and the `/list` batch endpoint — the
agent's real data path, so emitting only on the typed endpoints means it never sees them.
- `update-available:<version>` — sent when `AutoUpdateEnabled` and a newer binary is staged
  (`wwwroot/agent/version.txt`, read through a short-TTL cache so the hot `/list` path avoids
  per-POST disk I/O). The agent wakes its `UpdateChecker` immediately instead of waiting for the next
  poll. Because the server re-sends this on every POST while a newer build is staged, the checker
  **debounces** directive-driven checks (a trigger within ~60 s of the last attempt is ignored), so a
  persistently-failing update (download error / hash mismatch) doesn't re-download the exe every batch;
  the first trigger after a quiet period still applies promptly. The scheduled poll is unaffected.
- `sensor-disable:<group>` / `sensor-enable:<group>` (groups: computer/system/disk/network/module/
  process, mapped by `SensorGroupFlag` — the single source of truth shared with its unit test) —
  re-sent on **every** response while the group stays toggled, so the agent acts **only when the flag
  actually changes**: it rewrites `config.json`, then **restarts the service to apply it** (the
  collector's sensor set is fixed at `Start`). A graceful (exit-0) stop is *not* auto-restarted by the
  SCM, so the agent spawns a detached **`--restart-service`** helper — the no-swap twin of
  `--apply-update` (wait for `STOPPED` → `StartService`) — *before* `RequestStop()`. Unchanged groups
  are ignored so it never restart-loops; if several groups change in one response only the first
  spawns the helper (which re-reads the fully-updated config), the rest just record their flag. If the
  helper can't be spawned the agent does **not** stop (so it never strands itself down) and retries on
  the next re-send.
- Toggling a product's sensor groups (server `ProductController.UpdateSensorGroups`) is gated by the
  **ProductManager** role on that product, like every other product mutation.

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

The sensor description carries the process's full exe path; for a protected/system process whose path
the OS denies (e.g. `vmmemWSL`, `System`), it shows `**Path:** _(system process — path unavailable)_`
instead of an empty line, in both collectors. The managed collector caches that denial (an empty path
entry) so `MainModule` is probed once per name rather than throwing every tick.

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

- Portable unit tests (`tests/agent_tests.cpp`, name-dispatched, 15 cases — config parser + topCpu/update
  config + the sensor-group directive name↔field map) — run on every platform, registered in `ctest`.
- Windows-only data-dir ACL tests (#1279, same exe, 4 cases).
  - `agent_dir_acl_is_protected_system_and_admins_only` drives the real `EnsureDirectories()` against
    a redirected `%ProgramData%` seeded with the pre-#1279 state (data dir handing `Users` read down to
    `config.json` and `logs\`). Across two consecutive "starts" it checks: protected DACL, exactly
    SYSTEM + Administrators `(OI)(CI)(F)` explicit on both dirs, and no wide ACE left on the existing
    files. Elevated, it also checks that both dirs are owned by Administrators (or SYSTEM) with no
    error; unelevated, it checks that the owner change is refused with exactly one owner error per dir
    while the DACL is still applied (the fallback path).
  - `agent_dir_acl_admin_reads_and_rewrites_config` needs an elevated process and exits 77
    (CTest *skipped*) otherwise. Same seeded dir plus a planted `config.json.tmp` carrying an explicit
    marker ACE. It checks that the existing config survives byte-identical, the agent can still read
    it, and a `WriteAgentConfig` rewrite succeeds and lands with only the inherited SYSTEM +
    Administrators ACEs, owned by Administrators. It runs on the elevated GitHub runner.
  - `agent_config_rewrite_replaces_planted_temp_file` (unelevated, plain scratch dir): a planted
    `config.json.tmp` with an explicit marker ACE is replaced, not reused (the marker never reaches
    `config.json`); a stale temp file that cannot be deleted (held open) fails the rewrite and leaves
    `config.json` untouched.
  - `agent_dir_acl_refuses_reparse_points` (unelevated: junctions need no privilege). It runs on a
    `mklink /J` junction and checks that `ApplyAgentDirectoryDacl` refuses it with the reparse-point
    error. `EnsureDirectories` with the data dir planted as a junction reports that one error and
    creates no `logs\` behind it. With `logs\` planted as a junction, the data dir is still hardened
    and `logs\` is refused. Each time the target's owner + DACL (SDDL) stay unchanged.
- **CI build lane (W8):** `.github/workflows/agent-windows-build.yml` — windows-latest, vcpkg curl,
  configures + builds `src/agent` Release with warnings-as-errors, runs the config `ctest`, and uploads
  the bundle payload (`hsm-agent.exe` + `config.json` template + `install-hsmagent-service.cmd`/`uninstall-hsmagent-service.cmd`) as an
  artifact.
- **Service smoke (W9):** the same lane then exercises the real SCM lifecycle on the admin runner —
  `--install` → `sc qc HSMAgent` shows `AUTO_START` → a `--console` run survives an unreachable server
  (no crash) → `--uninstall`. The lane first seeds a pre-#1279 data dir (plain `mkdir` + a
  `config.json`, inheriting `Users` read, owner set to NETWORK SERVICE as a stand-in for a non-admin
  owner). After `--install` and again after the console run it runs `icacls` on the data dir, `logs\`,
  `config.json` and the log file. It fails on any `BUILTIN\Users` / `Everyone` / `Authenticated Users`
  ACE (by name or SID `S-1-5-32-545` / `S-1-1-0` / `S-1-5-11`), on an inherited `(I)` ACE or a
  non-Administrators/SYSTEM owner on either dir, or if `config.json`'s contents changed. The release
  lane (`agent-release.yml`) runs the identical smoke step. The exe is also staged into the server
  image by `server-build.yml` (copied into `wwwroot/agent/` before publish) so the download serves a
  real bundle.
- Live E2E mirrors the already-proven collector recipe (#1166): against a Dockerized
  `hsmonitoring/hierarchical_sensor_monitoring:latest`, the host sensors land in the tree under
  `<computer>/.computer/...` for the key's product. In HTTP mode `SentCount()` stays 0 (in-memory
  recorder only) — verify via `POST :44330/api/sensors/history`, not `SentCount`.

## Pointers

- Host pipeline it consumes: `integrations/native-collector` (RAII API), `collector/*` (wire/parity).
- Epic: #1167. Builds on epic #1093 + PR #1166 (#1164/#1165).
