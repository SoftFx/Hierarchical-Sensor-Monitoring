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
*Configuration → Agent* (the externally-reachable Sensor-API base, e.g. `https://hsm.company.com:44330`).
Behind Docker/NAT the server cannot infer this. When blank, it falls back to the request host + the
configured Sensors API port.

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
| `server.port` | `44330` | HSM Sensor API port |
| `server.accessKey` | — (required) | per-product access key (GUID) |
| `server.allowUntrustedCertificate` | `false` | accept a self-signed server certificate |
| `identity.computerName` | `"auto"` | `"auto"`/blank → the machine host name |
| `identity.module` | `"HSM Agent"` | module segment for module sensors |
| `sensors.computer` | `true` | umbrella host bundle (CPU/RAM/disks/network/OS info) |
| `sensors.system` / `disk` / `network` | `true` | finer subsets, used **only when `computer` is `false`** |
| `sensors.module` | `true` | collector self-sensors (alive / version / queue) |
| `sensors.process` | `false` | per-process sensors (opt-in) |
| `periods.collectMs` | `15000` | package collect period (ms) |
| `productVersion` | `"1.0.0.0"` | version reported by module sensors |

Blank/missing `address` or `accessKey`, or an out-of-range `port`, make the agent refuse to start (the
reason is logged to the Event Log and the file log).

## Where the data lands

Computer sensors appear in the server tree under `<computer>/.computer/...` for the product the access
key belongs to (note the literal `.computer` segment). Verify via the server UI or the history API
(`POST :44330/api/sensors/history`).

## Troubleshooting

- **Logs.** Errors go to the Windows **Application Event Log** (source `HSMAgent`); everything goes to
  `%ProgramData%\HSM Agent\logs\hsm-agent.log` (rolled to `.log.1`).
- **Service state.** `sc query HSMAgent` should show `RUNNING` with `START_TYPE` Auto. On a crash the
  service auto-restarts (failure-actions) after a minute.
- **"Cannot connect to server".** Check `server.address`/`port` reachability from the machine, and the
  access key. For a self-signed server certificate set `server.allowUntrustedCertificate: true`.
- **Foreground debugging.** Run `hsm-agent --console` to see live log output on stderr.
- **Not started by the SCM.** Running `hsm-agent.exe` with no arguments from a console prints guidance —
  that mode is for the service manager; use `--console` for a foreground run.
