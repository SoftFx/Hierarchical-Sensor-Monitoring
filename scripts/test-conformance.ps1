param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$collectorTests = Join-Path $repoRoot "src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj"
$nativeSource = Join-Path $repoRoot "src\native\collector_spike"
$nativeBuild = Join-Path $nativeSource "build"

function Resolve-CMake {
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmake) {
        return $cmake.Source
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (!(Test-Path $vswhere)) {
        throw "cmake was not found in PATH and vswhere.exe was not found."
    }

    $found = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.CMake.Project -find Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe
    if (!$found) {
        throw "Visual Studio CMake was not found."
    }

    return @($found)[0]
}

function Resolve-CTest {
    param([string]$CMakePath)

    $ctest = Get-Command ctest -ErrorAction SilentlyContinue
    if ($ctest) {
        return $ctest.Source
    }

    $candidate = Join-Path (Split-Path $CMakePath -Parent) "ctest.exe"
    if (!(Test-Path $candidate)) {
        throw "ctest was not found next to cmake."
    }

    return $candidate
}

$cmakePath = Resolve-CMake
$ctestPath = Resolve-CTest $cmakePath

Write-Host "Running .NET collector conformance tests..."
dotnet test $collectorTests --filter FullyQualifiedName~CollectorConformanceTests
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Configuring C++ collector conformance tests..."
& $cmakePath -S $nativeSource -B $nativeBuild -G "Visual Studio 17 2022" -A x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Building C++ collector conformance tests..."
& $cmakePath --build $nativeBuild --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Running C++ collector conformance tests..."
& $ctestPath --test-dir $nativeBuild -C $Configuration --output-on-failure -R conformance
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
