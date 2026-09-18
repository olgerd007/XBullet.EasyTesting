param(
    [Parameter(Mandatory = $false)]
    [string] $ResultsDirectory = "TestResults",

    [Parameter(Mandatory = $false)]
    [double] $MinimumLineCoverage = 90,

    [Parameter(Mandatory = $false)]
    [double] $MinimumBranchCoverage = 80
)

$ErrorActionPreference = "Stop"

$reports = Get-ChildItem -LiteralPath $ResultsDirectory -Filter "*.cobertura.xml" -File -Recurse
if ($reports.Count -eq 0) {
    throw "No Cobertura coverage reports were found below '$ResultsDirectory'."
}

$lineHits = @{}
$branchHits = @{}

foreach ($report in $reports) {
    [xml] $coverage = Get-Content -LiteralPath $report.FullName
    $classes = $coverage.coverage.packages.package.classes.class | Where-Object {
        $source = [string] $_.filename
        if ([string]::IsNullOrWhiteSpace($source)) {
            return $false
        }

        $source = $source.Replace("\", "/")
        $source.Contains("/src/XBullet.EasyTesting.Snapshots", [StringComparison]::Ordinal) -and
            -not $source.EndsWith("/SnapshotDiffLauncher.cs", [StringComparison]::Ordinal)
    }

    foreach ($class in $classes) {
        $source = $class.filename.Replace("\", "/")
        foreach ($line in $class.lines.line) {
            $key = "$source|$($line.number)"
            $hits = [int] $line.hits
            if (-not $lineHits.ContainsKey($key) -or $hits -gt $lineHits[$key]) {
                $lineHits[$key] = $hits
            }

            if ($line.branch -eq "true" -and $line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                $covered = [int] $Matches[1]
                $total = [int] $Matches[2]
                if (-not $branchHits.ContainsKey($key) -or
                    $total -gt $branchHits[$key].Total -or
                    ($total -eq $branchHits[$key].Total -and $covered -gt $branchHits[$key].Covered)) {
                    $branchHits[$key] = [pscustomobject]@{
                        Covered = $covered
                        Total = $total
                    }
                }
            }
        }
    }
}

if ($lineHits.Count -eq 0) {
    throw "The reports did not contain snapshot source coverage."
}

$coveredLines = @($lineHits.Values | Where-Object { $_ -gt 0 }).Count
$totalLines = $lineHits.Count
$coveredBranches = 0
$totalBranches = 0
foreach ($branch in $branchHits.Values) {
    $coveredBranches += $branch.Covered
    $totalBranches += $branch.Total
}

$lineCoverage = 100 * $coveredLines / $totalLines
$branchCoverage = if ($totalBranches -eq 0) { 100 } else { 100 * $coveredBranches / $totalBranches }

Write-Host ("Snapshot coverage: lines {0:N1}% ({1}/{2}), branches {3:N1}% ({4}/{5})" -f
    $lineCoverage,
    $coveredLines,
    $totalLines,
    $branchCoverage,
    $coveredBranches,
    $totalBranches)

$failures = @()
if ($lineCoverage -lt $MinimumLineCoverage) {
    $failures += "line coverage is below $MinimumLineCoverage%"
}

if ($branchCoverage -lt $MinimumBranchCoverage) {
    $failures += "branch coverage is below $MinimumBranchCoverage%"
}

if ($failures.Count -gt 0) {
    throw "Snapshot coverage check failed: $($failures -join '; ')."
}
