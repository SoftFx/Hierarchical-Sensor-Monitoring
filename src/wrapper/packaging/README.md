# HSMCppWrapper handoff bundle

How the native `HSMCppWrapper.dll` is delivered to downstream consumers (tt-aggregator2) that vendor
prebuilt binaries and relink.

## What ships

`pack.ps1` assembles one versioned zip whose layout mirrors the aggregator's vendored tree, so the
team unzips it straight over their checkout:

```
HSMCppWrapper-<ver>/
  include/HSMCppWrapper/   public wrapper ABI headers (Option A: relink)
  include/hsm_collector/   native collector headers (Native() + Option B: your own native adapter)
  dll/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.dll + .pdb + libcurl(-d).dll + z(d).dll + zstd.dll
  lib/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.lib          (Option A: relink)
  lib/hsm_collector/x64/{Release,Debug}/   hsm_collector_core.lib     (Option B: native adapter)
  MANIFEST.md              version, source commit, and both consume recipes (A relink / B adapter)
```

The full swap recipe (what to overwrite, which managed DLLs to delete) is written into `MANIFEST.md`
inside every bundle; behavioral residue vs the managed wrapper lives in
[`docs/native-collector-migration.md`](../../../docs/native-collector-migration.md).

The run-time DLLs are whatever vcpkg's app-local deployment puts next to `HSMCppWrapper.dll`; since
2.0.0 that is libcurl (8.14.0 or newer, with WebSocket, Schannel TLS), zlib and zstd (table sensors).

## Transport and table sensors (2.0.0)

- **Default transport: the collector session** (one WebSocket to `/api/sensors/session`; ADR-0019,
  point 5 as amended on 2026-11-25). Until 2.0.0 the wrapper forced HTTP. There is no automatic
  fallback to HTTP.
- **`DataCollectorProxy::UseHttpTransport()`** (new, the only ABI addition of 2.0.0) is for a server
  too old to have sessions: called before `Start` (or after `Stop`), the collector sends over the HTTP
  Sensor API instead and never asks for a session. From `Start` or `StartAsync` until `Stop` it throws
  `hsm::collector::Error` and changes nothing (also right after a `StartAsync` whose start has not begun
  yet); after `StopAsync` it waits for that stop, then applies.
- **Table sensors** come through `proxy.Native()`: include `<hsm_collector/tables.hpp>` (shipped under
  `include/hsm_collector/`) and call `proxy.Native().CreateTableSensor(path, options)`. They need the
  session **and a server with table sensors**: the first HSMServer release with the table connection
  (#1845), later than the first one with sessions (3.43.0; neither 3.43.x nor 3.44.0 has it). Against a
  server with sessions but without it, values and the other sensors flow and table sensors deliver
  nothing (`Refused` or `Pending` by the server's answer to their registration, or `Registered` with the
  table connection refused and their rows dropped and counted); after `UseHttpTransport()` they are
  `Unavailable`, with a reason that names the session. Details: `docs/native-collector-migration.md`.
- **Files on the session: about 1 MiB with a default server.** `SendFileAsync` sends a file as one
  element of a `list` frame, every file byte written as a decimal number and a comma (2 to 4 JSON bytes
  per byte). It must fit the server's `CollectorSessions.MaxFrameBytes` (default 4 MiB) less 64 bytes, so
  with the default the largest file is about 1 MiB (1.0 MiB worst case, about 1.1 MiB random binary),
  although the wrapper accepts up to 10 MiB. A larger one is dropped, logged ("Dropped a value of N bytes:
  it is larger than a collector session frame (M bytes).", in `.module/Collector errors`) and counted in
  `Queue overflow`. Over HTTP (1.x, or after `UseHttpTransport()`) files up to 10 MiB are posted (the
  server takes request bodies up to 50 MiB). Ways out: raise `CollectorSessions.MaxFrameBytes` on the
  server (up to 64 MiB; 42 MiB covers 10 MiB files), or call `UseHttpTransport()` on a host that sends
  large files. Large files as a feature: epic
  [#1882](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/issues/1882).
- **Rollout:** update a server without sessions (before HSMServer 3.43.0, or with
  `CollectorSessions.MaxSessions` at zero) **before** a host with wrapper 2.0.0 talks to it, or have that
  host call `UseHttpTransport()`. Otherwise the host sends nothing to that server.

## Cutting a release (the handoff)

The [`hsm-wrapper-release`](../../../.github/workflows/hsm-wrapper-release.yml) workflow builds both
x64 configs, runs the in-process ABI smoke, and packs the bundle.

- **Publish a versioned release** — push a `wrapper-v<semver>` tag whose version is the one in
  `project(HSMCppWrapper VERSION ...)` (a pre-release suffix is allowed; the workflow fails on a mismatch). The workflow builds and attaches
  the zip to a GitHub Release, giving the aggregator team a stable download URL (they build on GitLab,
  so a Release asset is the clean cross-host channel):

  ```
  git tag wrapper-v2.0.0
  git push origin wrapper-v2.0.0
  ```

- **Dry-run bundle without a release** — run the workflow via **Actions -> hsm-wrapper-release ->
  Run workflow** (`workflow_dispatch`). It uploads the zip as a workflow artifact only (no Release,
  no tag).

## Building the bundle locally

The dependencies come from vcpkg manifest mode: `src/wrapper/vcpkg.json` (curl with `websockets`,
zstd, zlib, nlohmann-json), pinned by its `builtin-baseline`. Use a vcpkg checkout at that commit, as
the workflows do (`lukka/run-vcpkg` with `vcpkgGitCommitId`); the configure step installs them. A global
vcpkg (for example the runner's `VCPKG_INSTALLATION_ROOT`) whose history lacks that commit cannot resolve
the baseline, so clone one for the build:

```powershell
git clone https://github.com/microsoft/vcpkg $env:TEMP\vcpkg-hsm-wrapper
git -C $env:TEMP\vcpkg-hsm-wrapper checkout 9e593bb18ea69cc5095e012465dcd675a822ed0d  # = builtin-baseline
& $env:TEMP\vcpkg-hsm-wrapper\bootstrap-vcpkg.bat -disableMetrics
$vcpkg = "$env:TEMP\vcpkg-hsm-wrapper"
cmake -S src/wrapper -B build/wrapper `
  -DCMAKE_TOOLCHAIN_FILE=$vcpkg/scripts/buildsystems/vcpkg.cmake `
  -DVCPKG_TARGET_TRIPLET=x64-windows
cmake --build build/wrapper --config Release
cmake --build build/wrapper --config Debug
pwsh src/wrapper/packaging/pack.ps1 -BuildDir build/wrapper -Version 2.0.0
# -> dist/HSMCppWrapper-2.0.0.zip
```

## Versioning

`wrapper-v*` tags version the **wrapper bundle**, independently of the server (`server-v*`) and the
native collector C ABI (`HSM_COLLECTOR_VERSION`). The wrapper's own version is
`project(HSMCppWrapper VERSION x.y.z)` in `src/wrapper/CMakeLists.txt`; tag the release with that
version. Semver: MAJOR for a change a host must act on (a removed or changed export, a changed default
behaviour), MINOR for an added export, PATCH for a fix. The manifest records the exact source commit
each build came from.

**2.0.0 (MAJOR):** the default transport became the collector session. The ABI only grew (one
method), but a host relinked against 2.0.0 sends nothing to a server without sessions unless it calls
`UseHttpTransport()`, which is a change a host has to act on. Bundles before it were 1.x.

The tag must be `wrapper-v<major>.<minor>.<patch>` with an optional pre-release suffix — e.g.
`wrapper-v1.0.0` or `wrapper-v1.0.0-rc1`. The workflow validates this and rejects anything else; note
that full-SemVer `+build` metadata (`wrapper-v1.0.0+build5`) is **not** accepted — keep build metadata
out of the tag.
