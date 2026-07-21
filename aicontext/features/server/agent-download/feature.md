# Feature: Per-product HSM Agent download (server side)

> Owner: server | Last reviewed: 2026-07-21 | Canonical: yes
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

## Packaging: the agent is a released artifact the server references (#1266, #1298)

The agent ships through its **own release channel**, mirroring the collector's `collector-v*` model:
pushing an `agent-v<version>` tag runs `agent-release.yml`, which fails unless the tag equals
`project(HsmAgent VERSION …)`, builds `src/agent` (WERROR), runs ctest + the SCM service smoke, and
publishes a GitHub Release (`--latest=false`) with `hsm-agent.exe` + `hsm-agent.exe.sha256`.

The server names the agent it ships in one tracked line — `src/server/HSMServer/agent-release.txt`.
`hsm-agent.exe` and `version.txt` in `wwwroot/agent/` stay **gitignored staged artifacts**; every
shippable-server path downloads the pinned release, verifies the SHA-256, and stages the pair before
`dotnet publish`:

| Path | How it stages the pair |
|---|---|
| Windows zip artifact / GitHub release (`build`, windows-latest) | `gh release download agent-v<pin>` + sha check, before `dotnet publish` |
| Docker image (`publish-docker-image`, ubuntu-latest) | Same step in its own checkout (a native Windows service cannot be built there) |
| Local image (`scripts/local-docker-build.ps1`) | Same download by default; `-IncludeAgent` builds from source instead (dev override, needs vcpkg) |

**Nothing in the server pipeline compiles C++ anymore** — the server lane needs no vcpkg, and both CI
legs are byte-identical by construction (same release asset, checksum-verified). This is also the
signing seam (#1167): sign the exe in the release lane, and every consumer ships the signed bytes
untouched (byte-identical invariant).

Both legs are guarded, because a missing binary is otherwise invisible until a user clicks Download:
the Windows job fails if the publish output lacks either file, and the image job fails if
`/app/wwwroot/agent/` inside the built image lacks them (#1266 — before those guards, every container
release shipped a UI button that could only answer 503, which is what QA hit on 3.40).

**`version.txt` is derived from the pin, never hand-maintained.** `GET /api/agent/version` and the
`update-available` directive on the hot data path (`SensorsController`) both read it, so a value that
does not match the staged binary silently disables the self-update channel (#1174) rather than failing
loudly. The release lane's tag==CMake guardrail makes pin == exe version. With nothing staged, both
readers fall back to `0.0.0`.

**Shipping a newer agent:** bump `project(HsmAgent VERSION …)` in the change PR (rule in
`src/agent/AGENTS.md`), push the matching `agent-v*` tag after merge, then bump `agent-release.txt`
in a one-line PR. Server releases in between keep shipping the previous pin.

## Out of scope (follow-up)

- A dedicated, separately-revocable per-download "agent key" (currently reuses the product DefaultKey).
- A fully-live download→install→data **CI** lane: the agent installs on Windows while the server runs as
  a Linux container, so co-locating them in one runner is fragile. The server-half (key/URL/bundle) is
  unit-tested; the agent service lifecycle (`--install`/`sc query`/`--console`/`--uninstall`) is
  CI-smoked on the windows runner; the final cross-OS "data appears after a real download+install" run
  is a documented **manual** smoke (mirrors the #1166 collector recipe).
