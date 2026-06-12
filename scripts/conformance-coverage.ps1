# Conformance coverage report (#1094, epic #1093).
#
# Cross-references the functional checklist
# (docs/initiatives/cpp-collector-port-functional-inventory.md) with the
# conformance corpus (tests/conformance/collector/*.hsmtest):
#   - per-section ticked/total counts (a line is "covered" when it is [x] and
#     carries a "— conformance: fixture:case" annotation);
#   - validates every annotation against the actual corpus (unknown fixture or
#     case name fails the run — annotations must not rot);
#   - -ShowUnticked prints the unmapped lines (= the remaining port/coverage
#     backlog; the "fails on native" half of the picture is ctest output).
#
# Exit codes: 0 = all annotations valid; 1 = at least one stale annotation.

param(
    [switch]$ShowUnticked
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$inventory = Join-Path $repoRoot "docs/initiatives/cpp-collector-port-functional-inventory.md"
$corpusDir = Join-Path $repoRoot "tests/conformance/collector"

# fixture name (no extension) -> set of case names
$corpus = @{}
foreach ($file in Get-ChildItem $corpusDir -Filter *.hsmtest) {
    $cases = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        if ($line -match '^\s*(#|$)') { continue }
        $cases.Add(($line -split '\|')[0]) | Out-Null
    }
    $corpus[[System.IO.Path]::GetFileNameWithoutExtension($file.Name)] = $cases
}

$section = "(preamble)"
$stats = [ordered]@{}
$unticked = @()
$staleRefs = @()

foreach ($line in [System.IO.File]::ReadAllLines($inventory)) {
    if ($line -match '^##\s+(.*)$') {
        $section = $Matches[1]
        continue
    }

    if ($line -notmatch '^- \[(x| )\]\s*(.*)$') { continue }

    if (-not $stats.Contains($section)) {
        $stats[$section] = @{ Ticked = 0; Total = 0 }
    }
    $stats[$section].Total++

    $ticked = $Matches[1] -eq 'x'
    $text = $Matches[2]

    if (-not $ticked) {
        $unticked += "[$section] $text"
        continue
    }

    $stats[$section].Ticked++

    if ($text -match '—\s*conformance:\s*(.+)$') {
        foreach ($reference in ($Matches[1] -split ',')) {
            $parts = $reference.Trim() -split ':', 2
            $fixture = $parts[0].Trim()
            $case = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '*' }

            if (-not $corpus.ContainsKey($fixture)) {
                $staleRefs += "unknown fixture '$fixture' in: $text"
            }
            elseif ($case -ne '*' -and -not $corpus[$fixture].Contains($case)) {
                $staleRefs += "unknown case '${fixture}:${case}' in: $text"
            }
        }
    }
}

$totalTicked = 0
$totalLines = 0
Write-Host "Conformance coverage by checklist section:"
foreach ($entry in $stats.GetEnumerator()) {
    $totalTicked += $entry.Value.Ticked
    $totalLines += $entry.Value.Total
    Write-Host ("  {0,3}/{1,-3} {2}" -f $entry.Value.Ticked, $entry.Value.Total, $entry.Key)
}
Write-Host ("TOTAL: {0}/{1} ({2:P0})" -f $totalTicked, $totalLines, ($totalTicked / [Math]::Max(1, $totalLines)))

if ($ShowUnticked) {
    Write-Host ""
    Write-Host "Unmapped lines (remaining backlog):"
    $unticked | ForEach-Object { Write-Host "  $_" }
}

if ($staleRefs.Count -gt 0) {
    Write-Host ""
    Write-Host "STALE ANNOTATIONS ($($staleRefs.Count)):" -ForegroundColor Red
    $staleRefs | ForEach-Object { Write-Host "  $_" }
    exit 1
}

exit 0
