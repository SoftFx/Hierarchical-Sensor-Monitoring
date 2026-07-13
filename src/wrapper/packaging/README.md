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
  dll/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.dll + .pdb + libcurl(-d).dll + z(d).dll
  lib/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.lib          (Option A: relink)
  lib/hsm_collector/x64/{Release,Debug}/   hsm_collector_core.lib     (Option B: native adapter)
  MANIFEST.md              version, source commit, and both consume recipes (A relink / B adapter)
```

The full swap recipe (what to overwrite, which managed DLLs to delete) is written into `MANIFEST.md`
inside every bundle; behavioral residue vs the managed wrapper lives in
[`docs/native-collector-migration.md`](../../../docs/native-collector-migration.md).

## Cutting a release (the handoff)

The [`hsm-wrapper-release`](../../../.github/workflows/hsm-wrapper-release.yml) workflow builds both
x64 configs, runs the in-process ABI smoke, and packs the bundle.

- **Publish a versioned release** — push a `wrapper-v<semver>` tag. The workflow builds and attaches
  the zip to a GitHub Release, giving the aggregator team a stable download URL (they build on GitLab,
  so a Release asset is the clean cross-host channel):

  ```
  git tag wrapper-v1.0.0
  git push origin wrapper-v1.0.0
  ```

- **Dry-run bundle without a release** — run the workflow via **Actions -> hsm-wrapper-release ->
  Run workflow** (`workflow_dispatch`). It uploads the zip as a workflow artifact only (no Release,
  no tag).

## Building the bundle locally

```powershell
vcpkg install curl:x64-windows
cmake -S src/wrapper -B build/wrapper `
  -DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_INSTALLATION_ROOT/scripts/buildsystems/vcpkg.cmake `
  -DVCPKG_TARGET_TRIPLET=x64-windows
cmake --build build/wrapper --config Release
cmake --build build/wrapper --config Debug
pwsh src/wrapper/packaging/pack.ps1 -BuildDir build/wrapper -Version 1.0.0
# -> dist/HSMCppWrapper-1.0.0.zip
```

## Versioning

`wrapper-v*` tags version the **wrapper bundle**, independently of the server (`server-v*`) and the
native collector C ABI (`HSM_COLLECTOR_VERSION`). Bump when the wrapper is rebuilt for a handoff; the
manifest records the exact source commit each build came from.

The tag must be `wrapper-v<major>.<minor>.<patch>` with an optional pre-release suffix — e.g.
`wrapper-v1.0.0` or `wrapper-v1.0.0-rc1`. The workflow validates this and rejects anything else; note
that full-SemVer `+build` metadata (`wrapper-v1.0.0+build5`) is **not** accepted — keep build metadata
out of the tag.
