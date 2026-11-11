# HSM Agent

The **HSM Agent** is an always-on, CLR-free Windows service that monitors a computer (CPU, RAM, disks,
network) and streams the metrics to an HSM server. Drop it on a machine, it installs as an auto-start
service, connects with a per-product access key, and reports — **with zero configuration**.

It is a separate product built on top of the native collector (`src/native/collector`); the agent only
hosts it. The single signed `hsm-agent.exe` is identical across every download — all per-install data
(server address + key) lives in a separate `config.json`. Part of epic
[#1167](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/issues/1167).

## Install — one click from the server (recommended)

1. In the HSM web UI (admin), open a product's **access keys** view and click **Download agent**.
2. The download is a zip with `hsm-agent.exe`, a `config.json` already filled with this server's address
   and the product's access key, and `install.cmd` / `uninstall.cmd`.
3. On the target machine, unzip and run **`install.cmd`** (it self-elevates). Done — the `HSMAgent`
   service is installed (Automatic, Delayed), started, and connected. No questions asked.

> The server only generates and serves the zip. The actual install is the C++ exe's own `--install`;
> there is no .NET runtime requirement and no MSI/C# installer on the client.

For the download button to bake in the right URL, set the **Agent connection URL** under
*Configuration → Agent* (the externally-reachable Sensor-API base, e.g. `https://hsm.company.com`).
Behind Docker/NAT the server cannot infer this. When blank, it falls back to the request host and its
port — 443 when the host carries none; the retired 44330/44333 ports are never written into a bundle.
Use a scheme + host (+ optional port) only — a **path prefix is not
supported** (a reverse proxy must expose the Sensor API at the host root, not under a sub-path), and the
prefix is dropped rather than baked into a config that would not connect.

## Install — manual

From the build artifact (or any copy of the exe):

1. Edit `config.json` — set at least `server.address` and `server.accessKey` (see the reference below).
2. Run `install.cmd` (elevated), or directly:
   ```
   hsm-agent.exe --install      :: register the auto-start service (Administrator)
   sc start HSMAgent
   ```

`--install` copies nothing on its own — place `hsm-agent.exe` where you want it and put `config.json`
at `%ProgramData%\HSM Agent\config.json` (the default location). The bundled `install.cmd` does both.

## Run modes

```
hsm-agent --console            run in the foreground (Ctrl-C to stop) — debugging
hsm-agent --install            register the auto-start service (Administrator)
hsm-agent --uninstall          stop + remove the service (Administrator)
hsm-agent --config <path>      use a config file other than the default
(no arguments)                 service mode — used by the Windows SCM
```

## Uninstall

Run **`uninstall.cmd`** (self-elevating), or `hsm-agent.exe --uninstall`. This stops and deletes the
`HSMAgent` service and removes the Event Log source. The `%ProgramData%\HSM Agent` data/log folder is
left in place.

## Configuration reference

Default location `%ProgramData%\HSM Agent\config.json`. Only `server.address` and `server.accessKey`
are required; everything else has a default.

| Field | Default | Meaning |
|-------|---------|---------|
| `server.address` | — (required) | HSM Sensor API base URL, e.g. `https://hsm.company.com` |
| `server.port` | `443` | HSM server port (one port for everything since #1590; see "Retired ports" below for 44330/44333) |
| `server.accessKey` | — (required) | per-product access key (GUID) |
| `server.allowUntrustedCertificate` | `false` | accept a self-signed server certificate |
| `identity.computerName` | `"auto"` | `"auto"`/blank → the machine host name |
| `identity.module` | `"HSM Agent"` | module segment for module sensors |
| `sensors.computer` | `true` | umbrella host bundle (CPU/RAM/disks/network/OS info) |
| `sensors.system` / `disk` / `network` | `true` | finer subsets, used **only when `computer` is `false`** |
| `sensors.module` | `true` | collector self-sensors (alive / version / queue) |
| `sensors.process` | `false` | per-process sensors (opt-in) |
| `periods.collectMs` | `15000` | package collect period (ms) |
| `topCpu.enabled` | `false` | opt-in: periodic "top processes by CPU" sensors |
| `topCpu.periodMs` | `60000` | how often to sample + post the top list (ms) |
| `topCpu.minPercent` | `1.0` | skip processes below this %CPU (of the whole machine) |
| `topCpu.count` | `10` | how many of the busiest exe names to post per tick |
| `productVersion` | `"1.0.0.0"` | version reported by module sensors |

When `topCpu.enabled` is `true`, every `periodMs` the agent samples CPU usage of all processes,
aggregates by **executable name** (all `chrome.exe` instances summed), and posts the CPU% of the
busiest `count` names (that are also `>= minPercent`) to `Top CPU processes/<exe name>` Double sensors.
A name that isn't in the top list that tick gets no value — leaving a natural gap in its graph.

> Note: a sensor is created the first time an exe name reaches the top list and kept afterwards, so on a
> long-lived, busy host the number of `Top CPU processes/*` sensors grows with the number of distinct
> programs that have ever been busy. Raise `minPercent` to limit churn from short-lived spikes.

Blank/missing `address` or `accessKey`, or an out-of-range `port`, make the agent refuse to start (the
reason is logged to the Event Log and the file log).

## Retired ports 44330 / 44333

HSM servers used to answer on two ports: 44330 (Sensor API) and 44333 (Web UI). Since #1590 everything is
on **443**, and the server stack no longer publishes the old ports at all (**breaking change**, #1696).
An agent whose `config.json` still says `"port": 44330` cannot connect once the server's compose files
are upgraded, and the server cannot see a connection that never arrives.

- **Agent 0.5.50 and later** handles it. At every start it logs that `server.port` is a retired port.
  When that port stops answering and the server answers on 443 (confirmed by a second check a minute
  later), the service rewrites `config.json` to `"port": 443`, logs the move and restarts itself — no
  operator action. If neither port answers it
  logs an error every hour and changes nothing. A `--console` run only reports what to change.
- **Older agents** only log `Could not connect to server`. Set `"port": 443` under `"server"` in
  `%ProgramData%\HSM Agent\config.json` and restart the service (`sc stop HSMAgent`, then
  `sc start HSMAgent`), or reinstall from a freshly downloaded bundle.
- **To let older agents update themselves first:** upgrade the server image to one that ships agent
  0.5.50 or later while the old ports still answer, wait until the agents report the new version
  (`.module/Version`), and only then replace the server's compose files.
- A server that itself listens on 44330 (no proxy, `Kestrel.Port` set to it) keeps working: the agent
  leaves a retired port alone for as long as it answers.

## Where the data lands

Computer sensors appear in the server tree under `<computer>/.computer/...` for the product the access
key belongs to (note the literal `.computer` segment). Verify via the server UI or the history API
(`POST /api/sensors/history`).

## Troubleshooting

- **Logs.** Errors go to the Windows **Application Event Log** (source `HSMAgent`); everything goes to
  `%ProgramData%\HSM Agent\logs\hsm-agent.log` (rolled to `.log.1`).
- **Service state.** `sc query HSMAgent` should show `RUNNING` with `START_TYPE` Auto. On a crash the
  service auto-restarts (failure-actions) after a minute.
- **"Cannot connect to server".** Check `server.address`/`port` reachability from the machine, and the
  access key. If `port` is 44330 or 44333, see "Retired ports 44330 / 44333" above: the fix is `"port": 443`. For a self-signed server certificate set `server.allowUntrustedCertificate: true`.
- **Foreground debugging.** Run `hsm-agent --console` to see live log output on stderr.
- **Not started by the SCM.** Running `hsm-agent.exe` with no arguments from a console prints guidance —
  that mode is for the service manager; use `--console` for a foreground run.
