# Feature: Per-product HSM Agent download (server side)

> Owner: server | Last reviewed: 2026-06-23 | Canonical: yes
> Scope: The admin-only server endpoint + UI that hand an operator a ready-to-run, per-product HSM Agent
> bundle (epic #1167, W6/W7). The agent product itself (the C++ Windows service) is `agent/feature.md`.

---

## Description

An admin opens a product's access-keys view and clicks **Download agent**; the server streams a zip
containing the byte-identical signed `hsm-agent.exe` + a generated `config.json` (this server's address
+ the product's access key) + silent `install.cmd`/`uninstall.cmd`. The client runs `install.cmd` → the
**C++ exe self-installs** as an auto-start Windows service and connects — zero configuration.

**The server only generates + serves the zip.** It is NOT an installer: the client-side install is the
pure-native exe's own `--install`. No .NET runtime, no C#/MSI installer is produced or required.

## Invariants

- **Signed-exe invariant.** The bundled `hsm-agent.exe` is served **byte-identical** to the published,
  signed binary. Only `config.json` is generated per product — the PE is never patched (Authenticode).
  Pinned by `AgentInstallerBundleTests.Zip_KeepsExeByteIdentical`.
- **Admin-only.** `[AuthorizeIsAdmin]` on the endpoint; the UI button renders only for `User.IsAdmin`.
- **The baked key is the product's access-key GUID.** The agent sends it in the `Key` header. Selection
  prefers the product **DefaultKey** (full permissions, never expires), else any valid key with
  send-data + add-node/add-sensor permissions.

## Key components

| Component | Location |
|---|---|
| Download endpoint `GET /api/agent/installer?productId=…` | `HSMServer/Controllers/AgentController.cs` |
| Bundle builder (config.json + scripts + zip; pure/testable) | `HSMServer/Model/Agent/AgentInstallerBundle.cs` |
| Server setting "Agent connection URL" | `HSMServer/ServerConfiguration/Sections/AgentConfig.cs` (+ `IServerConfig`/`ServerConfig`) |
| Settings UI (Agent tab) | `Views/Configuration/_Agent.cshtml`, `Views/Configuration/Index.cshtml`, `AgentSettingsViewModel`, `ConfigurationController.SaveAgentSettings` |
| Download button | `Views/AccessKeys/_ProductAccessKeys.cshtml` (admin-only) |
| Exe drop-point | `HSMServer/wwwroot/agent/hsm-agent.exe` (published by CI, W8) |
| Tests | `tests/HSMServer.Core.Tests/AgentInstallerBundleTests.cs` (5 cases) |

## Connection URL resolution

`AgentController.ResolveConnection()`: if the admin set `AgentConfig.ExternalConnectionUrl`, parse it
into `address` (scheme://host) + `port` (explicit port, else the configured `Kestrel.SensorPort`).
Behind Docker/NAT the server cannot infer its external address, so this setting exists; when blank it
falls back to the request host + the Sensor port. The result is written into the bundle's
`config.json` (`server.address` / `server.port`), which the agent maps onto `CollectorOptions`.

## Behavior notes

- If `wwwroot/agent/hsm-agent.exe` is absent (binary not yet published by CI), the endpoint returns
  **503** with a clear message — config/script generation is still unit-tested independently.
- The generated `config.json` matches the agent's config schema; only `server.address` + `server.accessKey`
  are required, the rest defaults (`identity.computerName: "auto"`, all sensor groups on).

## Out of scope (follow-up)

- Publishing the signed exe into `wwwroot/agent/` from CI (W8).
- A dedicated, separately-revocable per-download "agent key" (currently reuses the product DefaultKey).
- End-to-end download→install→data CI lane (W9).
