<#
.SYNOPSIS
  Assemble the HSMCppWrapper drop-in bundle (both x64 configs) for the tt-aggregator2 handoff.

.DESCRIPTION
  Packs the pure-native HSMCppWrapper.dll (no CLR) plus its libcurl/zlib runtime, import libs, PDBs,
  and the public headers into a single versioned zip whose layout mirrors the aggregator's vendored
  tree (include/, dll/, lib/) so the team can unzip it straight over their checkout and relink.

  Called by .github/workflows/hsm-wrapper-release.yml on a `wrapper-v*` tag push (cuts a GitHub
  Release) or a manual dispatch (uploads the zip as a workflow artifact). Run it locally the same way:

    pwsh src/wrapper/packaging/pack.ps1 -BuildDir build/wrapper -VcpkgRoot $env:VCPKG_INSTALLATION_ROOT -Version 1.0.0

  Expects the wrapper to have been CMake-built for BOTH configs first:
    cmake -S src/wrapper -B build/wrapper -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake -DVCPKG_TARGET_TRIPLET=x64-windows
    cmake --build build/wrapper --config Release
    cmake --build build/wrapper --config Debug
#>
[CmdletBinding()]
param(
    # CMake build directory that contains the Release/ and Debug/ output subfolders.
    [Parameter(Mandatory)] [string] $BuildDir,
    # vcpkg root (its installed/x64-windows tree supplies the shared libcurl + zlib runtime DLLs).
    [Parameter(Mandatory)] [string] $VcpkgRoot,
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
$triplet = 'x64-windows'
$configs = @('Release', 'Debug')

if (-not $Commit)    { $Commit    = (& git -C $SourceRoot rev-parse --short HEAD 2>$null); if (-not $Commit) { $Commit = 'unknown' } }
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
    Copy-Item (Join-Path $binSrc 'HSMCppWrapper.pdb') $dllDst -ErrorAction SilentlyContinue
    Copy-Item $lib $libDst

    # Shared libcurl + zlib runtime (schannel TLS, no OpenSSL): Release from bin/, Debug from debug/bin/.
    # Copy an explicit libcurl/zlib allow-list rather than every *.dll, so a future transitive vcpkg
    # dependency can't silently enlarge the handoff bundle — and it stays matched to MANIFEST.md.
    $vcBin = if ($cfg -eq 'Debug') { Join-Path $VcpkgRoot "installed\$triplet\debug\bin" }
             else                  { Join-Path $VcpkgRoot "installed\$triplet\bin" }
    if (Test-Path $vcBin) {
        $runtimeDlls = @(Get-ChildItem -Path $vcBin -Filter '*.dll' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(libcurl|zlib)' })
        if ($runtimeDlls.Count -eq 0) {
            Write-Warning "No libcurl/zlib DLLs under '$vcBin' - runtime NOT bundled for $cfg."
        }
        foreach ($dllFile in $runtimeDlls) { Copy-Item $dllFile.FullName $dllDst }
    }
    else {
        Write-Warning "vcpkg bin not found for ${cfg}: '$vcBin' - libcurl/zlib runtime NOT bundled."
    }
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

Pure-native `HSMCppWrapper.dll` built over `hsm::collector` (no CLR). **Same public ABI** as the old
C++/CLI wrapper: relink only, zero source changes in the consumer.

| | |
|---|---|
| Version | __VER__ |
| Source commit | __COMMIT__ |
| Built | __DATE__ |
| Platform | x64, MSVC, Release + Debug |
| Transport | libcurl (schannel TLS) - shipped next to the DLL |

## Layout

```
include/
  HSMCppWrapper/      public wrapper ABI headers (DataCollector.h, HSMSensor.h, ...)
  hsm_collector/      native headers - needed ONLY if you call DataCollectorProxy::Native()
dll/HSMCppWrapper/x64/{Release,Debug}/
  HSMCppWrapper.dll   HSMCppWrapper.pdb   libcurl(-d).dll   zlib(d)1.dll
lib/HSMCppWrapper/x64/{Release,Debug}/
  HSMCppWrapper.lib   import library
```

## Drop-in recipe (tt-aggregator2)

1. Copy `include/`, `dll/`, `lib/` over your vendored tree (`aggregator/{include,dll,lib}/...`).
   The headers are **byte-identical** to what you already vendor - overwriting them is a no-op.
2. Copy the new runtime DLLs next to the wrapper (your PostBuild xcopy of `dll/.../*.dll` -> OutDir
   already picks them up): `libcurl.dll` + `zlib1.dll` (Release), `libcurl-d.dll` + `zlibd1.dll` (Debug).
3. **Delete the managed leftovers** from your build output / vendored dll dir - they are no longer
   loaded: `HSMDataCollector.dll`, `HSMSensorDataObjects.dll` (+ their `.pdb`).
4. Rebuild. No CLR is loaded anymore. The exported ABI matches (relink proven by `dumpbin
   /LINKERMEMBER`: 545 exports in both configs - 543 wrapper + 2 `Native()` C-ABI re-exports), so the
   link resolves with no source edits.

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
if ($env:GITHUB_OUTPUT) { "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
