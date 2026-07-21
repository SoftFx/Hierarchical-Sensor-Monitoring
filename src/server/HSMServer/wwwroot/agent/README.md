# Agent binary drop-point

The per-product download endpoint (`GET /api/agent/installer?productId=…`, epic #1167 W6/W7) serves a
zip containing the prebuilt **`hsm-agent.exe`** from this directory, plus a generated `config.json` and
install scripts.

Publish the signed release `hsm-agent.exe` here as `wwwroot/agent/hsm-agent.exe`. Until the binary is
present the endpoint returns HTTP 503 with a clear message — the rest of the download flow (config +
scripts) is exercised by unit tests.

The exe is served **byte-identical** to the published, signed binary; only `config.json` is generated
per product (Authenticode invariant).

## What is generated vs. checked in

`hsm-agent.exe` and `version.txt` are **staged release artifacts, not sources** — both are gitignored.
The agent ships as its own GitHub Release (`agent-v<version>` tag → `agent-release.yml`, #1298), and
both `server-build.yml` legs download the release named by `src/server/HSMServer/agent-release.txt`,
verify its SHA-256, and stage the pair here before publish. Neither leg compiles the agent. Both fail
if the publish output or the image layer is missing either file (#1266).

`version.txt` is derived from the pin. It must always describe the exe next to it: `GET
/api/agent/version` and the update directive on the hot data path (`SensorsController`) both read it,
so a stale value silently disables the self-update channel (#1174). The release lane guarantees
tag == compiled-in `HSM_AGENT_VERSION`, so pin == exe version. When nothing is staged, both readers
fall back to `0.0.0`.

To ship a newer agent: bump `project(HsmAgent VERSION …)`, merge, push the matching `agent-v*` tag,
then bump `agent-release.txt` in a one-line PR.
