# Differential conformance fuzzer (#1094, epic #1093).
#
# Generates seeded random .hsmtest action sequences from the DETERMINISTIC verb
# subset, runs each fixture through BOTH collector drivers (the .NET driver via
# the Fuzz_fixture_executes theory + HSM_CONFORMANCE_FUZZ_DIR, the native driver
# via the conformance_fuzz entry point), and byte-compares the canonical payload
# dumps (dump_payloads_to). On divergence the failing fixture is minimized by
# whole-line removal and saved together with both dumps.
#
# Determinism constraints baked into the generator (do not widen casually):
# - inert dispatcher (collect period 60s) — everything materializes on stop-flush;
# - doubles limited to <= 7 significant digits (n/100), where net48 "R", .NET Core
#   and the native shortest-round-trip formatter all agree byte-for-byte;
# - NaN/Infinity only fed to bars (silently skipped) — instant double adds throw;
# - no rate/function sensors (wall-clock-dependent values);
# - bar periods 3600000 ms (inert) — bars publish only via flush-on-stop.

param(
    [int]$Seed = 0,
    [int]$Iterations = 25,
    [string]$Configuration = "Debug",
    [string]$NativeTestBinary = "",
    [switch]$SkipManagedBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$collectorTests = Join-Path $repoRoot "src/collector/HSMDataCollector.Tests/HSMDataCollector.Tests.csproj"
$reproRoot = Join-Path $repoRoot "tests/conformance/fuzz-repros"

function Resolve-NativeBinary {
    if ($NativeTestBinary -and (Test-Path $NativeTestBinary)) {
        return (Resolve-Path $NativeTestBinary).Path
    }

    $candidates = @(
        (Join-Path $repoRoot "src/native/collector_spike/build/$Configuration/hsm_collector_spike_tests.exe"),
        (Join-Path $repoRoot "src/native/collector_spike/build/hsm_collector_spike_tests")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Native test binary not found (looked at: $($candidates -join '; ')). Build the spike or pass -NativeTestBinary."
}

# One fixture = one case = one op sequence. Returns the fixture text with the
# literal token OUTFILE as the dump target (substituted per driver run).
#
# Each case uses ONE sensor population of ONE kind: per the DSL spec sensor
# indexes are kind-relative, but the native driver currently indexes the flat
# creation order — identical only when every sensor in a case has the same
# kind. (Aligning the native driver and pinning mixed-kind indexing is a
# separate corpus item.)
function New-FuzzFixture {
    param([int]$CaseSeed, [string]$CaseName)

    $rng = [System.Random]::new($CaseSeed)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# seed: $CaseSeed (regenerate: scripts/fuzz-conformance.ps1)")

    $maxQueue = 5 + $rng.Next(46)          # 5..50
    $package = 1 + $rng.Next(10)           # 1..10
    $lines.Add("$CaseName|create_collector_with_limits|$maxQueue|$package|60000")

    $mode = @("int", "bool", "double", "string", "int_bar", "double_bar")[$rng.Next(6)]
    $sensorCount = 1 + $rng.Next(4)        # 1..4 sensors of the chosen kind

    for ($s = 0; $s -lt $sensorCount; $s++) {
        switch ($mode) {
            "int_bar"    { $lines.Add("$CaseName|create_int_bar_sensor|fuzz/bar/$s|3600000|0") }
            "double_bar" { $lines.Add("$CaseName|create_double_bar_sensor|fuzz/bar/$s|3600000|0|$($rng.Next(4))") }
            default      { $lines.Add("$CaseName|create_${mode}_sensor|fuzz/$mode/$s") }
        }
    }

    $lines.Add("$CaseName|start")

    $running = $true
    $ops = 10 + $rng.Next(71)              # 10..80 ops
    for ($i = 0; $i -lt $ops; $i++) {
        if ($rng.Next(100) -lt 6) {
            # Lifecycle cycling: instant values added while stopped are dropped, bar values
            # keep accumulating — both deterministic.
            if ($running) { $lines.Add("$CaseName|stop") } else { $lines.Add("$CaseName|start") }
            $running = -not $running
            continue
        }

        $idx = $rng.Next($sensorCount)
        $status = @("Ok", "Warning", "Error")[$rng.Next(3)]
        switch ($mode) {
            "int"    { $lines.Add("$CaseName|add_int|$idx|$($rng.Next(-100000, 100000))|$status|fuzz-$i") }
            "bool"   { $lines.Add("$CaseName|add_bool|$idx|$(@('true','false')[$rng.Next(2)])|$status|fuzz-$i") }
            "double" { $lines.Add("$CaseName|add_double|$idx|$(($rng.Next(-10000000, 10000000)) / 100.0)|$status|fuzz-$i") }
            "string" { $lines.Add("$CaseName|add_string|$idx|value-$($rng.Next(1000))|$status|fuzz-$i") }
            "int_bar" { $lines.Add("$CaseName|add_bar_int|$idx|$($rng.Next(-100000, 100000))") }
            "double_bar" {
                # Bars silently skip non-finite values — exercise that path occasionally.
                if ($rng.Next(10) -eq 0) {
                    $value = @("NaN", "Infinity", "-Infinity")[$rng.Next(3)]
                } else {
                    $value = ($rng.Next(-10000000, 10000000)) / 100.0
                }
                $lines.Add("$CaseName|add_bar_double|$idx|$value")
            }
        }
    }

    if (-not $running) { $lines.Add("$CaseName|start") }
    $lines.Add("$CaseName|stop")
    $lines.Add("$CaseName|dump_payloads_to|OUTFILE")

    return ($lines -join "`n") + "`n"
}

function Write-DriverFixture {
    param([string]$FixtureText, [string]$TargetPath, [string]$DumpPath)

    $text = $FixtureText.Replace("OUTFILE", ($DumpPath -replace '\\', '/'))
    [System.IO.File]::WriteAllText($TargetPath, $text, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-ManagedRun {
    param([string]$FuzzDir)

    $env:HSM_CONFORMANCE_FUZZ_DIR = $FuzzDir
    try {
        & dotnet test $collectorTests --filter "FullyQualifiedName~Fuzz_fixture_executes" --logger "console;verbosity=minimal" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Managed fuzz run failed (driver-level failure, not a divergence) — inspect the dotnet test output."
        }
    }
    finally {
        Remove-Item Env:HSM_CONFORMANCE_FUZZ_DIR -ErrorAction SilentlyContinue
    }
}

function Invoke-NativeRun {
    param([string]$Binary, [string]$FixturePath)

    & $Binary conformance_fuzz $FixturePath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Native fuzz run failed (driver-level failure, not a divergence): $FixturePath"
    }
}

function Get-DumpLines {
    param([string]$Path)

    if (!(Test-Path $Path)) { throw "Dump file was not produced: $Path" }
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r", "").TrimEnd("`n")
    # Bar open/close are wall-clock-derived (aligned to the bar period) — the two driver
    # runs happen at different instants, so neutralize them; values, counts and ordering
    # are what the fuzzer compares.
    $text = $text -replace '"(OpenTimeMs|CloseTimeMs)":-?\d+', '"$1":T'
    return $text -split "`n"
}

# Order WITHIN one sensor path is contractual (FIFO); order ACROSS paths is
# explicitly unspecified (tests/conformance/README.md — "Inter-sensor flush
# order is unspecified"). The bar stop-flush iterates a hash map whose order
# differs between the C# Dictionary and the C++ std::unordered_map, so normalize
# by a STABLE sort on Path (preserves each path's own subsequence) before the
# positional compare — this still catches value/aggregation/formatting drift and
# per-sensor ordering bugs, just not the non-contractual cross-sensor order.
function Get-PathFromPayload {
    param([string]$Line)
    if ($Line -match '"Path":"([^"]*)"') { return $Matches[1] }
    return ""
}

function Compare-Dumps {
    param([string]$ManagedDump, [string]$NativeDump)

    $managed = @(Get-DumpLines $ManagedDump | Sort-Object -Stable { Get-PathFromPayload $_ })
    $native = @(Get-DumpLines $NativeDump | Sort-Object -Stable { Get-PathFromPayload $_ })

    if ($managed.Count -ne $native.Count) {
        return "payload count: managed=$($managed.Count) native=$($native.Count)"
    }

    for ($i = 0; $i -lt $managed.Count; $i++) {
        if ($managed[$i] -cne $native[$i]) {
            return "payload ${i} (after stable sort by path):`n  managed: $($managed[$i])`n  native:  $($native[$i])"
        }
    }

    return $null
}

# Runs one fixture text through both drivers; returns $null on agreement or a
# human-readable divergence description.
function Test-FixtureAgreement {
    param([string]$FixtureText, [string]$WorkDir, [string]$Binary, [string]$Name)

    $fuzzDir = Join-Path $WorkDir "managed-$Name"
    New-Item -ItemType Directory -Force -Path $fuzzDir | Out-Null

    $managedDump = Join-Path $WorkDir "$Name.managed.txt"
    $nativeDump = Join-Path $WorkDir "$Name.native.txt"
    $managedFixture = Join-Path $fuzzDir "$Name.hsmtest"
    $nativeFixture = Join-Path $WorkDir "$Name.native.hsmtest"

    Write-DriverFixture $FixtureText $managedFixture $managedDump
    Write-DriverFixture $FixtureText $nativeFixture $nativeDump

    Invoke-ManagedRun $fuzzDir
    Invoke-NativeRun $Binary $nativeFixture

    return Compare-Dumps $managedDump $nativeDump
}

# Whole-line minimization: repeatedly try dropping each op line (never the
# create/start/stop/dump skeleton); keep the drop when the divergence persists.
# Every probe costs a full managed run — cap the budget.
function Get-MinimizedFixture {
    param([string]$FixtureText, [string]$WorkDir, [string]$Binary)

    $lines = [System.Collections.Generic.List[string]]($FixtureText.TrimEnd("`n") -split "`n")
    $probes = 0
    $maxProbes = 12

    for ($i = $lines.Count - 1; $i -ge 0 -and $probes -lt $maxProbes; $i--) {
        $line = $lines[$i]
        if ($line -match '\|(create_|start$|stop$|dump_payloads_to\|)' -or $line.StartsWith("#")) { continue }

        $candidate = [System.Collections.Generic.List[string]]::new($lines)
        $candidate.RemoveAt($i)
        $candidateText = ($candidate -join "`n") + "`n"

        $probes++
        if (Test-FixtureAgreement $candidateText $WorkDir $Binary "min$probes") {
            $lines = $candidate
        }
    }

    return ($lines -join "`n") + "`n"
}

$binary = Resolve-NativeBinary
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("hsm-fuzz-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

if (-not $SkipManagedBuild) {
    & dotnet build $collectorTests --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Managed test project build failed." }
}

Write-Host "Differential fuzzer: seed=$Seed iterations=$Iterations work=$workRoot"
Write-Host "Native binary: $binary"

# Generate all fixtures up front; ONE managed run executes them all (dotnet test
# startup dominates), then the native binary replays each fixture individually.
$fixtures = @{}
$fuzzDir = Join-Path $workRoot "managed"
New-Item -ItemType Directory -Force -Path $fuzzDir | Out-Null

for ($it = 0; $it -lt $Iterations; $it++) {
    $caseSeed = $Seed * 100000 + $it
    $name = "fuzz_seed_$caseSeed"
    $text = New-FuzzFixture -CaseSeed $caseSeed -CaseName $name
    $fixtures[$name] = $text

    Write-DriverFixture $text (Join-Path $fuzzDir "$name.hsmtest") (Join-Path $workRoot "$name.managed.txt")
    Write-DriverFixture $text (Join-Path $workRoot "$name.native.hsmtest") (Join-Path $workRoot "$name.native.txt")
}

Invoke-ManagedRun $fuzzDir
foreach ($name in $fixtures.Keys) {
    Invoke-NativeRun $binary (Join-Path $workRoot "$name.native.hsmtest")
}

$divergences = @()
foreach ($name in ($fixtures.Keys | Sort-Object)) {
    $difference = Compare-Dumps (Join-Path $workRoot "$name.managed.txt") (Join-Path $workRoot "$name.native.txt")
    if ($null -eq $difference) { continue }

    Write-Host "DIVERGENCE in ${name}: $difference" -ForegroundColor Red
    Write-Host "Minimizing $name..."
    $minimized = Get-MinimizedFixture $fixtures[$name] $workRoot $binary

    New-Item -ItemType Directory -Force -Path $reproRoot | Out-Null
    $reproPath = Join-Path $reproRoot "$name.hsmtest"
    [System.IO.File]::WriteAllText($reproPath, $minimized, [System.Text.UTF8Encoding]::new($false))
    Copy-Item (Join-Path $workRoot "$name.managed.txt") (Join-Path $reproRoot "$name.managed.txt")
    Copy-Item (Join-Path $workRoot "$name.native.txt") (Join-Path $reproRoot "$name.native.txt")

    $divergences += "${name}: $difference (repro: $reproPath)"
}

if ($divergences.Count -gt 0) {
    Write-Host ""
    Write-Host "=== $($divergences.Count) divergence(s) found ===" -ForegroundColor Red
    $divergences | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Host "All $Iterations fuzz fixtures agree across drivers." -ForegroundColor Green
exit 0
