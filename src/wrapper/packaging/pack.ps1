<#
.SYNOPSIS
  Assemble the HSMCppWrapper drop-in bundle (both x64 configs) for the tt-aggregator2 handoff.

.DESCRIPTION
  Packs the pure-native HSMCppWrapper.dll (no CLR) plus its libcurl/zlib/zstd runtime, import libs, PDBs,
  and the public headers into a single versioned zip whose layout mirrors the aggregator's vendored
  tree (include/, dll/, lib/) so the team can unzip it straight over their checkout and relink.

  Called by .github/workflows/hsm-wrapper-release.yml on a `wrapper-v*` tag push (cuts a GitHub
  Release) or a manual dispatch (uploads the zip as a workflow artifact). Run it locally the same way:

    pwsh src/wrapper/packaging/pack.ps1 -BuildDir build/wrapper -Version 2.1.0

  Expects the wrapper to have been CMake-built for BOTH configs first, with a vcpkg checkout at the
  builtin-baseline of src/wrapper/vcpkg.json (see packaging/README.md, "Building the bundle locally"):
    cmake -S src/wrapper -B build/wrapper -DCMAKE_TOOLCHAIN_FILE=<pinned vcpkg>/scripts/buildsystems/vcpkg.cmake -DVCPKG_TARGET_TRIPLET=x64-windows
    cmake --build build/wrapper --config Release
    cmake --build build/wrapper --config Debug
#>
[CmdletBinding()]
param(
    # CMake build directory that contains the Release/ and Debug/ output subfolders. The runtime
    # dependency DLLs (libcurl/zlib/zstd) are taken from vcpkg's app-local deployment next to that output.
    [Parameter(Mandatory)] [string] $BuildDir,
    # Bundle version, e.g. "1.0.0" (a leading "wrapper-v"/"v" is stripped).
    [Parameter(Mandatory)] [string] $Version,
    # Repo root; defaults to three levels up from this script (src/wrapper/packaging -> repo root).
    [string] $SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    # Where the bundle folder + zip are written.
    [string] $OutDir = (Join-Path (Get-Location) 'dist'),
    # Source commit + build date for the manifest; auto-filled when omitted.
    [string] $Commit = '',
    [string] $BuildDate = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ver     = $Version -replace '^wrapper-v', '' -replace '^v', ''
$configs = @('Release', 'Debug')

if (-not $Commit) {
    # try/catch, not just 2>$null: under PowerShell 7.4+ ($PSNativeCommandUseErrorActionPreference) a
    # non-zero git exit raises a TERMINATING error with $ErrorActionPreference='Stop', so the
    # `-> 'unknown'` fallback would otherwise be unreachable when packing outside a git checkout.
    try { $Commit = (& git -C $SourceRoot rev-parse --short HEAD 2>$null) } catch { $Commit = $null }
    if (-not $Commit) { $Commit = 'unknown' }
}
if (-not $BuildDate) { $BuildDate = (Get-Date -Format 'yyyy-MM-dd') }

function New-Dir([string] $path) { New-Item -ItemType Directory -Force -Path $path | Out-Null; return $path }

$bundleName = "HSMCppWrapper-$ver"
$root = Join-Path $OutDir $bundleName
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
New-Dir $root | Out-Null

# --- binaries + runtime deps per config -------------------------------------
foreach ($cfg in $configs) {
    $binSrc = Join-Path $BuildDir $cfg
    $dll = Join-Path $binSrc 'HSMCppWrapper.dll'
    $lib = Join-Path $binSrc 'HSMCppWrapper.lib'
    foreach ($artifact in @($dll, $lib)) {
        if (-not (Test-Path $artifact)) {
            throw "Missing $cfg build: '$artifact' not found. Build both configs before packing (cmake --build build/wrapper --config $cfg)."
        }
    }

    $dllDst = New-Dir (Join-Path $root "dll\HSMCppWrapper\x64\$cfg")
    $libDst = New-Dir (Join-Path $root "lib\HSMCppWrapper\x64\$cfg")

    Copy-Item $dll $dllDst
    # PDB is advertised in MANIFEST.md; copy it when present, but make its absence visible rather than
    # silently shipping a bundle that doesn't match the manifest (the .dll/.lib are hard-required above).
    $pdb = Join-Path $binSrc 'HSMCppWrapper.pdb'
    if (Test-Path $pdb) { Copy-Item $pdb $dllDst }
    else { Write-Warning "No PDB for ${cfg} ('$pdb') - bundle ships without it, though MANIFEST.md lists it." }
    Copy-Item $lib $libDst

    # Runtime dependency DLLs: copy whatever vcpkg's app-local deployment placed next to the build
    # output — the exact runtime closure of HSMCppWrapper.dll (libcurl + zlib + zstd today; auto-adapting if
    # curl ever gains a dependency). This never guesses vcpkg's DLL names (its zlib is z.dll / zd.dll,
    # NOT zlib1.dll — an earlier name-based allow-list silently dropped it) so it can't miss a needed
    # DLL; the post-pack closure smoke in CI re-verifies the shipped set actually loads.
    $deps = @(Get-ChildItem -Path (Join-Path $binSrc '*.dll') |
        Where-Object { $_.Name -ne 'HSMCppWrapper.dll' })
    if ($deps.Count -eq 0) {
        # HSMCppWrapper.dll load-time imports libcurl, so an empty dep set means vcpkg's app-local
        # deploy did not run and the bundle would ship non-loadable. Fail loud: the CI post-pack
        # closure smoke only guards the workflow, but a local `pack.ps1` run has no such net.
        throw "No runtime dependency DLLs next to '$binSrc' for $cfg - vcpkg's app-local deploy did not run; the bundle would be missing libcurl/zlib/zstd."
    }
    foreach ($depFile in $deps) { Copy-Item $depFile.FullName $dllDst }

    # Native collector static library (hsm_collector_core.lib) so a consumer can link a PURE-NATIVE
    # adapter directly against hsm::collector and drop the wrapper (MANIFEST "Option B"). It is built as
    # a subproject of the wrapper (add_subdirectory -> build/wrapper/hsm_collector/<cfg>/); the
    # hsm_collector/ headers already ship under include/ below. The .lib carries both the C++ impl and
    # the C ABI, so no wrapper DLL is needed on that path.
    $collLib = Join-Path $BuildDir "hsm_collector\$cfg\hsm_collector_core.lib"
    if (-not (Test-Path $collLib)) {
        # Output layout can vary by generator; fall back to a scoped recursive search.
        $collLib = (Get-ChildItem -Path (Join-Path $BuildDir 'hsm_collector') -Recurse -Filter 'hsm_collector_core.lib' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\$cfg\\" } | Select-Object -First 1).FullName
    }
    if (-not $collLib -or -not (Test-Path $collLib)) {
        throw "Missing $cfg native collector lib: hsm_collector_core.lib not found under '$BuildDir\hsm_collector' - was the wrapper (which subbuilds the collector) built for $cfg?"
    }
    Copy-Item $collLib (New-Dir (Join-Path $root "lib\hsm_collector\x64\$cfg"))
}

# --- public headers (mirror the consumer include roots) ---------------------
$incRoot     = New-Dir (Join-Path $root 'include')
$wrapperInc  = New-Dir (Join-Path $incRoot 'HSMCppWrapper')
$nativeInc   = New-Dir (Join-Path $incRoot 'hsm_collector')
Copy-Item (Join-Path $SourceRoot 'src\wrapper\include\*') $wrapperInc -Recurse
Copy-Item (Join-Path $SourceRoot 'src\native\collector\include\hsm_collector\*') $nativeInc -Recurse

# --- manifest (single-quoted template so markdown backticks stay literal) ----
$manifestTemplate = @'
# HSMCppWrapper __VER__ - native drop-in bundle

Pure-native `HSMCppWrapper.dll` built over `hsm::collector` (no CLR). The public ABI of the old
C++/CLI wrapper, grown by one method in 2.0.0: replace the headers and relink, zero source changes in
the consumer.

| | |
|---|---|
| Version | __VER__ |
| Source commit | __COMMIT__ |
| Built | __DATE__ |
| Platform | x64, MSVC, Release + Debug |
| Transport | the collector session (WebSocket, libcurl 8.14+ with schannel TLS) by default; HTTP after `UseHttpTransport()` - libcurl shipped next to the DLL |
| Server | HSMServer 3.43.0 or later (collector sessions); for an older server call `DataCollectorProxy::UseHttpTransport()` before `Start`. Table sensors: the first HSMServer release with the table connection (#1845), later than 3.44.0 |
| Files | on the session a file must fit one server frame as JSON: about 1 MiB with the default 4 MiB `CollectorSessions.MaxFrameBytes` (see Transport below) |

## Layout

```
include/
  HSMCppWrapper/      public wrapper ABI headers (DataCollector.h, HSMSensor.h, ...)   -- Option A
  hsm_collector/      native collector headers (Native() and the pure-native adapter) -- Option B
dll/HSMCppWrapper/x64/{Release,Debug}/
  HSMCppWrapper.dll   HSMCppWrapper.pdb   + the vcpkg runtime: libcurl.dll + z.dll + zstd.dll (Debug: libcurl-d.dll + zd.dll + zstd.dll)
lib/HSMCppWrapper/x64/{Release,Debug}/
  HSMCppWrapper.lib        import library (Option A: relink)
lib/hsm_collector/x64/{Release,Debug}/
  hsm_collector_core.lib   native collector static lib (Option B: your own native adapter)
```

Two ways to consume this bundle:
- **Option A - drop-in relink:** keep the `hsm_wrapper::DataCollectorProxy` API, relink against the new
  `HSMCppWrapper.dll` (the ABI only grew, zero source changes). Recipe below.
- **Option B - your own native adapter:** write directly against `hsm::collector` and link
  `hsm_collector_core.lib`, dropping the wrapper entirely.

## Option A - drop-in relink (tt-aggregator2)

1. Copy `include/`, `dll/`, `lib/` over your vendored tree (`aggregator/{include,dll,lib}/...`).
   **Replace the headers**: 2.0.0 adds `DataCollectorProxy::UseHttpTransport()` (and comments) to
   `DataCollector.h` and removes or changes nothing, so your code compiles against them unchanged.
2. Copy the new runtime DLLs next to the wrapper (your PostBuild xcopy of `dll/.../*.dll` -> OutDir
   already picks them up): `libcurl.dll` + `z.dll` + `zstd.dll` (Release), `libcurl-d.dll` + `zd.dll` +
   `zstd.dll` (Debug). `zstd.dll` is new in 2.0.0 (table sensors).
3. **Delete the managed leftovers** from your build output / vendored dll dir - they are no longer
   loaded: `HSMDataCollector.dll`, `HSMSensorDataObjects.dll` (+ their `.pdb`).
4. Rebuild. No CLR is loaded anymore. The exported ABI only grows (`dumpbin /EXPORTS`: every earlier
   export is still there unchanged; 2.0.0 adds `DataCollectorProxy::UseHttpTransport` and the table
   functions of the re-exported C ABI), so the link resolves with no source edits.

**Transport (2.0.0).** The wrapper talks to the server over the collector session, one WebSocket on
`/api/sensors/session`, which needs **HSMServer 3.43.0 or later**. Update the server first; a host that
must keep talking to an older server calls `proxy.UseHttpTransport()` before `Start` (after `Start`
or `StartAsync` it throws `hsm::collector::Error` until a stop has finished). There is no automatic fallback:
against a server without sessions a host on the default sends nothing.

**Files on the session.** `SendFileAsync` sends a file as one element of a session frame, each file byte
written as a decimal number and a comma (2 to 4 JSON bytes per byte). With the server's default
`CollectorSessions.MaxFrameBytes` of 4 MiB the largest file is about **1 MiB** (1.0 MiB worst case),
although the wrapper accepts up to 10 MiB. A larger file is dropped: logged as "Dropped a value of N
bytes: it is larger than a collector session frame (M bytes)." (in `.module/Collector errors`) and
counted in `Queue overflow`. Over HTTP files up to 10 MiB are posted. If your host sends larger files,
raise `CollectorSessions.MaxFrameBytes` on the server (up to 64 MiB; 42 MiB covers 10 MiB files) or call
`proxy.UseHttpTransport()`. Large files as a feature: HSM epic #1882.

**Table sensors** come through `proxy.Native()`: `#include <hsm_collector/tables.hpp>` and call
`proxy.Native().CreateTableSensor(path, options)`. They need the session and a server with the table
connection (the first HSMServer release with it, later than 3.44.0); on HTTP they are Unavailable.
Against a server with sessions but without the table connection, values flow and table sensors deliver
nothing (Refused, Pending, or Registered with their rows dropped and counted).

## Option B - your own native adapter (no wrapper)

Build directly against the native collector and drop `HSMCppWrapper.dll` entirely:

1. Vendor `lib/hsm_collector/x64/{Release,Debug}/hsm_collector_core.lib` (e.g. under
   `aggregator/lib/hsm_collector/x64/...`) plus the `include/hsm_collector/` headers.
2. Link, per config: `hsm_collector_core.lib` + Windows SDK `iphlpapi.lib` + `pdh.lib` + `libcurl.lib`
   + `zstd.lib` + `z.lib` (the import libs for the shipped DLLs - from vcpkg `curl[ssl,websockets]`,
   `zstd`, `zlib` for `x64-windows`, or ask us to bundle them).
3. `#include <hsm_collector/hsm_collector.hpp>` and use the `hsm::collector::Collector` RAII API (or the
   C ABI in `hsm_collector.h`). Ship the same `libcurl.dll` + `z.dll` + `zstd.dll` runtime next to your
   binary, and choose the transport yourself (`UseSessionTransport()` or `UseHttpTransport()`).

`hsm_collector_core.lib` carries both the C++ implementation and the C ABI, so no wrapper DLL is needed
on this path. (If you keep the wrapper instead, its DLL already re-exports the C ABI, so Option A's
`HSMCppWrapper.lib` alone resolves `hsm::collector` calls - no separate collector lib required.)

## Behavioral residue

A few runtime behaviors follow the native backend rather than the old managed one (function sensors
are int-only and throw otherwise, `SendFileAsync` is synchronous, monitoring-init sub-flags are
ignored, ...). Full list: `docs/native-collector-migration.md` in the HSM repo.
'@

$manifest = $manifestTemplate.Replace('__VER__', $ver).Replace('__COMMIT__', $Commit).Replace('__DATE__', $BuildDate)
Set-Content -Path (Join-Path $root 'MANIFEST.md') -Value $manifest -Encoding UTF8

# --- zip (CreateFromDirectory with includeBaseDirectory=$true so the versioned top folder is kept —
#         Compress-Archive's root-folder behavior differs across PowerShell versions) --------------
$zip = Join-Path $OutDir "$bundleName.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $root, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

Write-Host "Bundle assembled: $zip"
