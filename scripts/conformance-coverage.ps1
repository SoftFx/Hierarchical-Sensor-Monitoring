# Conformance coverage / disposition report (#1094 + #1101, epic #1093).
#
# The functional checklist
# (docs/initiatives/cpp-collector-port-functional-inventory.md) was RETIRED in
# #1101: it is a frozen index in which every line carries exactly one
# disposition. This script is the gate that keeps it frozen.
#
# A line is "resolved" when it is one of:
#   - [x] + "— conformance: fixture:case[, ...]"  (owned by the portable corpus)
#   - [~] (+ conformance)                          (partial — registration corpus-pinned)
#   - [ ] + "— platform: ..."                      (platform-bound; live read = smoke/#1099)
#   - [ ] + "— unit: <test>"                       (language-local unit test only)
#   - [ ] + "— [decide]: <rationale>"              (explicitly out of port scope)
#
# The script:
#   - per-section resolved/total and corpus-covered/total counts;
#   - validates every "— conformance:" reference against the actual corpus
#     (unknown fixture or case fails the run — annotations must not rot);
#   - -Strict (CI gate): additionally FAILS if any line is unresolved (a bare
#     "- [ ]" with no disposition) — this is what keeps the checklist frozen;
#   - -ShowUnticked prints every line not owned by the in-proc corpus
#     (i.e. resolved by platform/unit/decide instead);
#   - -JsonOut <path> writes a shields.io endpoint badge JSON.
#
# Exit codes: 0 = OK; 1 = a stale annotation, or (with -Strict) an unresolved line.

param(
    [switch]$ShowUnticked,
    [switch]$Strict,
    [string]$JsonOut
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
$untickedCorpus = @()   # resolved by platform/unit/decide (not owned by the corpus)
$unresolved = @()       # bare "- [ ]" with no disposition — only possible if someone breaks the freeze
$staleRefs = @()

function Test-ConformanceRefs([string]$text) {
    if ($text -match '—\s*conformance:\s*(.+)$') {
        foreach ($reference in ($Matches[1] -split ',')) {
            $reference = $reference.Trim()
            # stop at the first trailing parenthetical/extra clause if present
            $parts = $reference -split ':', 2
            $fixture = $parts[0].Trim()
            $case = if ($parts.Count -gt 1) { ($parts[1].Trim() -split '\s')[0] } else { '*' }
            if (-not $corpus.ContainsKey($fixture)) {
                $script:staleRefs += "unknown fixture '$fixture' in: $text"
            }
            elseif ($case -ne '*' -and -not $corpus[$fixture].Contains($case)) {
                $script:staleRefs += "unknown case '${fixture}:${case}' in: $text"
            }
        }
    }
}

foreach ($line in [System.IO.File]::ReadAllLines($inventory)) {
    if ($line -match '^##\s+(.*)$') {
        $section = $Matches[1]
        continue
    }

    # [x] ticked-corpus, [~] partial, [ ] disposed-but-not-corpus
    if ($line -notmatch '^- \[(x|~| )\]\s*(.*)$') { continue }

    if (-not $stats.Contains($section)) {
        $stats[$section] = @{ Resolved = 0; Corpus = 0; Total = 0 }
    }
    $stats[$section].Total++

    $mark = $Matches[1]
    $text = $Matches[2]

    $hasConformance = $text -match '—\s*conformance:'
    $hasDisposition = ($text -match '—\s*(platform|unit)\s*:') -or ($text -match '\[decide\]\s*:')

    $isCorpus = ($mark -eq 'x') -or ($mark -eq '~' -and $hasConformance) -or $hasConformance
    $isResolved = ($mark -eq 'x') -or ($mark -eq '~') -or $hasConformance -or $hasDisposition

    if ($hasConformance -or $mark -eq 'x' -or $mark -eq '~') { Test-ConformanceRefs $text }

    if ($isCorpus) { $stats[$section].Corpus++ }
    if ($isResolved) {
        $stats[$section].Resolved++
        if (-not $isCorpus) { $untickedCorpus += "[$section] $text" }
    }
    else {
        $unresolved += "[$section] $text"
    }
}

$totalResolved = 0; $totalCorpus = 0; $totalLines = 0
Write-Host "Conformance disposition by checklist section (resolved / corpus-owned / total):"
foreach ($entry in $stats.GetEnumerator()) {
    $totalResolved += $entry.Value.Resolved
    $totalCorpus   += $entry.Value.Corpus
    $totalLines    += $entry.Value.Total
    Write-Host ("  {0,3}/{1,-3} corpus {2,3}  {3}" -f $entry.Value.Resolved, $entry.Value.Total, $entry.Value.Corpus, $entry.Key)
}
Write-Host ("TOTAL resolved: {0}/{1} ({2:P0}); corpus-owned: {3}/{1} ({4:P0})" -f `
    $totalResolved, $totalLines, ($totalResolved / [Math]::Max(1, $totalLines)), `
    $totalCorpus, ($totalCorpus / [Math]::Max(1, $totalLines)))

if ($ShowUnticked) {
    Write-Host ""
    Write-Host "Resolved but NOT corpus-owned (platform / unit / decide):"
    $untickedCorpus | ForEach-Object { Write-Host "  $_" }
}

if ($JsonOut) {
    $pct = [int][Math]::Round(100.0 * $totalResolved / [Math]::Max(1, $totalLines))
    $color = if ($totalResolved -eq $totalLines) { 'brightgreen' } elseif ($pct -ge 90) { 'green' } else { 'yellow' }
    $badge = [ordered]@{
        schemaVersion = 1
        label         = 'checklist resolved'
        message       = "$totalResolved/$totalLines"
        color         = $color
    }
    $dir = Split-Path -Parent $JsonOut
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    ($badge | ConvertTo-Json -Compress) | Set-Content -Path $JsonOut -Encoding utf8
    Write-Host ""
    Write-Host "Wrote badge endpoint JSON -> $JsonOut"
}

$failed = $false

if ($staleRefs.Count -gt 0) {
    Write-Host ""
    Write-Host "STALE ANNOTATIONS ($($staleRefs.Count)):" -ForegroundColor Red
    $staleRefs | ForEach-Object { Write-Host "  $_" }
    $failed = $true
}

if ($Strict -and $unresolved.Count -gt 0) {
    Write-Host ""
    Write-Host "UNRESOLVED LINES — checklist is frozen; every line needs a disposition ($($unresolved.Count)):" -ForegroundColor Red
    Write-Host "  Resolve each with '— conformance: fixture:case' (tick), '— platform: ...', '— unit: <test>', or '— [decide]: <rationale>'."
    $unresolved | ForEach-Object { Write-Host "  $_" }
    $failed = $true
}

if ($failed) { exit 1 }
exit 0
