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

`hsm-agent.exe` and `version.txt` are **build outputs, not sources** — both are gitignored. The Windows
`build` job of `server-build.yml` stages them (exe from the CMake build, version parsed from
`project(HsmAgent VERSION …)`), and the Linux image job receives the same pair as the `hsm-agent-staged`
artifact, because it cannot build a native Windows service on an ubuntu runner. Both jobs then fail if
the publish output or the image layer is missing either file (#1266).

`version.txt` must always describe the exe next to it: `GET /api/agent/version` and the update directive
on the hot data path (`SensorsController`) both read it, so a stale value silently disables the
self-update channel (#1174). When nothing is staged, both readers fall back to `0.0.0`.
