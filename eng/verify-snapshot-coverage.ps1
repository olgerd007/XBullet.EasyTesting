param(
    [Parameter(Mandatory = $false)]
    [string] $ResultsDirectory = "TestResults",

    [Parameter(Mandatory = $false)]
    [double] $MinimumLineCoverage = 90,

    [Parameter(Mandatory = $false)]
    [double] $MinimumBranchCoverage = 81
)

$ErrorActionPreference = "Stop"

$projectPackages = [ordered]@{
    "XBullet.EasyTesting" = "XBullet.EasyTesting"
    "XBullet.EasyTesting.Aspire" = "XBullet.EasyTesting.Aspire"
    "XBullet.EasyTesting.Azure" = "XBullet.EasyTesting.Azure"
    "XBullet.EasyTesting.AzureFunctions" = "XBullet.EasyTesting.AzureFunctions"
    "XBullet.EasyTesting.EntityFrameworkCore" = "XBullet.EasyTesting.EntityFrameworkCore"
    "XBullet.EasyTesting.Http" = "XBullet.EasyTesting.Http"
    "XBullet.EasyTesting.Messaging" = "XBullet.EasyTesting.Messaging"
    "XBullet.EasyTesting.Observability" = "XBullet.EasyTesting.Observability"
    "XBullet.EasyTesting.Snapshots" = "XBullet.EasyTesting.Snapshots.Core"
    "XBullet.EasyTesting.Snapshots.Http" = "XBullet.EasyTesting.Snapshots.Http"
    "XBullet.EasyTesting.Testcontainers" = "XBullet.EasyTesting.Testcontainers"
    "XBullet.EasyTesting.Verify.Xunit" = "XBullet.EasyTesting.Verify.Xunit"
}

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter "*.cobertura.xml" -File -Recurse)
if ($reports.Count -eq 0) {
    throw "No Cobertura coverage reports were found below '$ResultsDirectory'."
}

$coverageByProject = @{}
foreach ($project in $projectPackages.Keys) {
    $coverageByProject[$project] = [pscustomobject]@{
        Lines = @{}
        Branches = @{}
    }
}

foreach ($report in $reports) {
    [xml] $coverage = Get-Content -LiteralPath $report.FullName
    foreach ($class in $coverage.coverage.packages.package.classes.class) {
        $source = ([string] $class.filename).Replace("\", "/")
        if ([string]::IsNullOrWhiteSpace($source) -or
            $source.Contains("/obj/", [StringComparison]::Ordinal) -or
            $source.EndsWith("/SnapshotDiffLauncher.cs", [StringComparison]::Ordinal)) {
            continue
        }

        if ($source -notmatch "/src/(?<Project>XBullet[.]EasyTesting(?:[.][^/]+)?)/") {
            continue
        }

        $project = $Matches.Project
        if (-not $coverageByProject.ContainsKey($project)) {
            continue
        }

        $projectCoverage = $coverageByProject[$project]
        foreach ($line in $class.lines.line) {
            $key = "$source|$($line.number)"
            $hits = [int] $line.hits
            if (-not $projectCoverage.Lines.ContainsKey($key) -or
                $hits -gt $projectCoverage.Lines[$key]) {
                $projectCoverage.Lines[$key] = $hits
            }

            if ($line.branch -eq "true" -and
                $line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                $covered = [int] $Matches[1]
                $total = [int] $Matches[2]
                if (-not $projectCoverage.Branches.ContainsKey($key) -or
                    $total -gt $projectCoverage.Branches[$key].Total -or
                    ($total -eq $projectCoverage.Branches[$key].Total -and
                        $covered -gt $projectCoverage.Branches[$key].Covered)) {
                    $projectCoverage.Branches[$key] = [pscustomobject]@{
                        Covered = $covered
                        Total = $total
                    }
                }
            }
        }
    }
}

$failures = @()
foreach ($project in $projectPackages.Keys) {
    $package = $projectPackages[$project]
    $projectCoverage = $coverageByProject[$project]
    if ($projectCoverage.Lines.Count -eq 0) {
        $failures += "$package has no source coverage"
        Write-Host "$package coverage: no source coverage"
        continue
    }

    $coveredLines = @($projectCoverage.Lines.Values | Where-Object { $_ -gt 0 }).Count
    $totalLines = $projectCoverage.Lines.Count
    $coveredBranches = 0
    $totalBranches = 0
    foreach ($branch in $projectCoverage.Branches.Values) {
        $coveredBranches += $branch.Covered
        $totalBranches += $branch.Total
    }

    $lineCoverage = 100 * $coveredLines / $totalLines
    $branchCoverage = if ($totalBranches -eq 0) {
        100
    }
    else {
        100 * $coveredBranches / $totalBranches
    }

    Write-Host ("{0} coverage: lines {1:N1}% ({2}/{3}), branches {4:N1}% ({5}/{6})" -f
        $package,
        $lineCoverage,
        $coveredLines,
        $totalLines,
        $branchCoverage,
        $coveredBranches,
        $totalBranches)

    if ($branchCoverage -lt $MinimumBranchCoverage) {
        $failures += "$package branch coverage is below $MinimumBranchCoverage%"
    }

    if ($project -eq "XBullet.EasyTesting.Snapshots" -and
        $lineCoverage -lt $MinimumLineCoverage) {
        $failures += "$package line coverage is below $MinimumLineCoverage%"
    }
}

if ($failures.Count -gt 0) {
    throw "Package coverage check failed: $($failures -join '; ')."
}
