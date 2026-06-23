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
| Server settings "Agent connection URL" + "Allow untrusted server certificate" | `HSMServer/ServerConfiguration/Sections/AgentConfig.cs` (`ExternalConnectionUrl`, `AllowUntrustedCertificate`) (+ `IServerConfig`/`ServerConfig`) |
| Settings UI (Agent tab) | `Views/Configuration/_Agent.cshtml`, `Views/Configuration/Index.cshtml`, `AgentSettingsViewModel`, `ConfigurationController.SaveAgentSettings` |
| Download button | `Views/AccessKeys/_ProductAccessKeys.cshtml` (admin-only) |
| Exe drop-point | `HSMServer/wwwroot/agent/hsm-agent.exe` (staged by `server-build.yml` before publish, W9) |
| Key selection / URL resolution (pure, testable) | `Model/Agent/AgentKeySelector.cs`, `Model/Agent/AgentConnectionResolver.cs` |
| Tests | `tests/HSMServer.Core.Tests/AgentInstallerBundleTests.cs` (5) + `AgentDownloadLogicTests.cs` (11: key selection + URL resolution) |

## Connection URL resolution

`AgentConnectionResolver.Resolve(externalUrl, sensorPort, fallbackScheme, fallbackHost)`: if the admin
set `AgentConfig.ExternalConnectionUrl`, parse it into `address` (scheme://host + any path base) +
`port`. An **explicit** port is kept even when it equals the scheme default (`:443` survives — detected
from the raw authority, not `Uri.IsDefaultPort`); only an absent port falls back to the configured
`Kestrel.SensorPort`. Behind Docker/NAT the server cannot infer its external address, so this setting
exists; when blank it falls back to the request host + the Sensor port. `AgentConfig.AllowUntrustedCertificate`
is baked into the bundle's `server.allowUntrustedCertificate` (for self-hosted / self-signed servers).
The result is written into the bundle's `config.json`, which the agent maps onto `CollectorOptions`.

## Behavior notes

- The signed exe is **staged into `wwwroot/agent/` by `server-build.yml`** (it builds the agent on the
  Windows release runner and copies it in before `dotnet publish`, so the published/container server
  serves a real bundle). If the binary is absent the endpoint returns **503** with a clear message —
  config/script generation + key/URL selection are unit-tested independently of the binary.
- The generated `config.json` matches the agent's config schema; only `server.address` + `server.accessKey`
  are required, the rest defaults (`identity.computerName: "auto"`, all sensor groups on).

## Out of scope (follow-up)

- A dedicated, separately-revocable per-download "agent key" (currently reuses the product DefaultKey).
- A fully-live download→install→data **CI** lane: the agent installs on Windows while the server runs as
  a Linux container, so co-locating them in one runner is fragile. The server-half (key/URL/bundle) is
  unit-tested; the agent service lifecycle (`--install`/`sc query`/`--console`/`--uninstall`) is
  CI-smoked on the windows runner; the final cross-OS "data appears after a real download+install" run
  is a documented **manual** smoke (mirrors the #1166 collector recipe).
