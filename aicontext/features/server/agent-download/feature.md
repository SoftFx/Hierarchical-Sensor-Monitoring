# Feature: Per-product HSM Agent download (server side)

> Owner: server | Last reviewed: 2026-07-20 | Canonical: yes
> Scope: The admin-only server endpoint + UI that hand an operator a ready-to-run, per-product HSM Agent
> bundle (epic #1167, W6/W7). The agent product itself (the C++ Windows service) is `agent/feature.md`.

---

## Description

An admin opens a product's edit page ("HSM Agent" section) and clicks **Download agent**; the server streams a zip
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
| Server settings "Agent connection URL" + "Allow untrusted server certificate" + "Report top processes by CPU" | `HSMServer/ServerConfiguration/Sections/AgentConfig.cs` (`ExternalConnectionUrl`, `AllowUntrustedCertificate`, `EnableTopCpuProcesses`) (+ `IServerConfig`/`ServerConfig`) |
| Settings UI (Agent tab) | `Views/Configuration/_Agent.cshtml`, `Views/Configuration/Index.cshtml`, `AgentSettingsViewModel`, `ConfigurationController.SaveAgentSettings` |
| Download button | `Views/Product/EditProduct.cshtml` — "HSM Agent" section (admin-only) |
| Exe drop-point | `HSMServer/wwwroot/agent/hsm-agent.exe` + `version.txt` (gitignored; staged by `server-build.yml` — see *Packaging*) |
| Key selection / URL resolution (pure, testable) | `Model/Agent/AgentKeySelector.cs`, `Model/Agent/AgentConnectionResolver.cs` |
| Tests | `tests/HSMServer.Core.Tests/AgentInstallerBundleTests.cs` (7: incl. topCpu on/off) + `AgentDownloadLogicTests.cs` (13: key selection + URL resolution) |

`AgentConfig.EnableTopCpuProcesses` (admin toggle, Configuration → Agent) makes `BuildConfigJson` add a
`topCpu` block (`enabled:true, periodMs:60000, minPercent:1.0, count:10`) to the generated `config.json`,
so the downloaded agent also reports the top processes by CPU (issue #1175). Off by default; the client
agent has no UI and just runs the baked config.

## Connection URL resolution

`AgentConnectionResolver.Resolve(externalUrl, sensorPort, fallbackScheme, fallbackHost)`: if the admin
set `AgentConfig.ExternalConnectionUrl`, parse it into `address` (scheme://host only) + `port`. A
**path base is NOT supported** — the native collector's endpoint builder (`hsm_http_endpoints.hpp` /
`HostOnly`) strips everything after the first `/`, so a reverse-proxy prefix could never reach the wire;
the resolver drops it rather than bake a misleading address. IPv6 literals stay bracketed
(`https://[::1]`, since `Uri.Host` brackets them). An **explicit** port is kept even when it equals the
scheme default (`:443` survives —
detected from the raw authority, not `Uri.IsDefaultPort`); only an absent port falls back to the
configured `Kestrel.SensorPort`. Behind Docker/NAT the server cannot infer its external address, so this
setting exists; when blank it falls back to the request host + the Sensor port. `AgentConfig.AllowUntrustedCertificate`
is baked into the bundle's `server.allowUntrustedCertificate` (for self-hosted / self-signed servers).
The result is written into the bundle's `config.json`, which the agent maps onto `CollectorOptions`.

## Behavior notes

- If the binary is absent the endpoint returns **503** with a clear message — config/script generation +
  key/URL selection are unit-tested independently of the binary. See *Packaging* below for how the
  binary gets there.
- The generated `config.json` matches the agent's config schema; only `server.address` + `server.accessKey`
  are required, the rest defaults (`identity.computerName: "auto"`, all sensor groups on).

## Packaging: how the exe reaches a release (#1266)

`hsm-agent.exe` and `version.txt` are **generated, gitignored** build outputs, so every path that
produces a shippable server has to stage them explicitly. `server-build.yml` has two such paths, and
they are not the same job:

| Path | How it gets the pair |
|---|---|
| Windows zip artifact / GitHub release (`build`, windows-latest) | Builds `src/agent` with vcpkg and copies the exe in; parses `project(HsmAgent VERSION …)` from `src/agent/CMakeLists.txt` into `version.txt`, before `dotnet publish` |
| Docker image (`publish-docker-image`, ubuntu-latest) | Downloads the `hsm-agent-staged` artifact into `wwwroot/agent/` before its own `dotnet publish` |

The image job publishes from its **own checkout**, not from the Windows job's output, and a native
Windows service cannot be built on an ubuntu runner — so the artifact hand-off is the only way the
binary can reach the image. Before this was wired up, every container release shipped a UI button that
could only answer 503, which is exactly what QA hit on 3.40.

Both paths are guarded, because a missing binary is otherwise invisible until a user clicks Download:
the Windows job fails if the publish output lacks either file, and the image job fails if
`/app/wwwroot/agent/` inside the built image lacks them.

**`version.txt` must always describe the exe beside it.** `GET /api/agent/version` and the
`update-available` directive on the hot data path (`SensorsController`) both read it, so a value that
does not match the staged binary silently disables the self-update channel (#1174) rather than failing
loudly. It is derived from the agent's CMake version at stage time, never hand-maintained. With nothing
staged, both readers fall back to `0.0.0`.

For a local image that mirrors CI, `scripts/local-docker-build.ps1 -IncludeAgent` stages the same pair
(requires `VCPKG_ROOT` + CMake); without the flag the script warns and the endpoint 503s.

## Out of scope (follow-up)

- A dedicated, separately-revocable per-download "agent key" (currently reuses the product DefaultKey).
- A fully-live download→install→data **CI** lane: the agent installs on Windows while the server runs as
  a Linux container, so co-locating them in one runner is fragile. The server-half (key/URL/bundle) is
  unit-tested; the agent service lifecycle (`--install`/`sc query`/`--console`/`--uninstall`) is
  CI-smoked on the windows runner; the final cross-OS "data appears after a real download+install" run
  is a documented **manual** smoke (mirrors the #1166 collector recipe).
