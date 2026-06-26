# Conformance unsupported-marker triage (#1101, epic #1093).
#
# The conformance DSL lets a driver register a verb it cannot yet implement as
# "unsupported" (so the run fails with a TODO count instead of a generic
# unknown-verb error — the failure list is the port backlog; see
# tests/conformance/README.md). Policy (#1101): every such marker MUST reference
# a cpp-port issue so the backlog is tracked, never silently accumulated.
#
# Convention: mark an unsupported verb with the token
#   CONFORMANCE-UNSUPPORTED: <verb> (#<issue>)
# in the driver source. This script scans BOTH drivers, and FAILS if any marker
# is missing a #<issue> reference. With no markers present (the current state —
# both drivers implement the full vocabulary) it is a green no-op guard that
# keeps the policy enforced going forward.
#
# Exit codes: 0 = every marker references an issue (or none exist); 1 = a marker
# without a #<issue> reference.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$drivers = @(
    (Join-Path $repoRoot "src/native/collector/tests/hsm_collector_tests.cpp"),
    (Join-Path $repoRoot "src/collector/HSMDataCollector.Tests/CollectorConformanceTests.cs")
)

$marker = 'CONFORMANCE-UNSUPPORTED:'
$issue = [regex]'#\d+'

$violations = @()
$found = 0

foreach ($driver in $drivers) {
    if (-not (Test-Path $driver)) { continue }

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($driver)) {
        $lineNumber++
        if ($line -notmatch [regex]::Escape($marker)) { continue }

        $found++
        if (-not $issue.IsMatch($line)) {
            $violations += "${driver}:${lineNumber}: unsupported marker without a #<issue> reference: $($line.Trim())"
        }
    }
}

Write-Host "Unsupported-marker triage: scanned $($drivers.Count) driver(s), found $found marker(s)."

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "UNSUPPORTED MARKERS WITHOUT AN ISSUE REFERENCE ($($violations.Count)):" -ForegroundColor Red
    Write-Host "  Each '$marker' must reference a cpp-port issue, e.g. '$marker advance_clock (#1101)'."
    $violations | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "OK — every unsupported marker references a cpp-port issue."
exit 0
