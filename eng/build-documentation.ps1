[CmdletBinding()]
param(
    [switch] $NoBuild,
    [switch] $WarningsAsErrors
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryDirectory = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repositoryDirectory "XBullet.EasyTesting.sln"
$docfxPath = Join-Path $repositoryDirectory "docs" "docfx.json"
$generatedMetadataPath = Join-Path $repositoryDirectory "docs" "api" "generated"
$sitePath = Join-Path $repositoryDirectory "artifacts" "docs"

function Invoke-Checked([scriptblock] $command, [string] $description) {
    & $command
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

function Remove-GeneratedDirectory([string] $path) {
    $resolvedParent = [IO.Path]::GetFullPath((Split-Path $path -Parent))
    $resolvedPath = [IO.Path]::GetFullPath($path)
    if (-not $resolvedPath.StartsWith("$resolvedParent$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove generated path outside its expected parent: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Test-GeneratedRedirects([string] $sourceDirectory, [string] $outputDirectory) {
    $resolvedSourceDirectory = [IO.Path]::GetFullPath($sourceDirectory)
    $resolvedOutputDirectory = [IO.Path]::GetFullPath($outputDirectory)
    $redirectCount = 0

    foreach ($sourceFile in Get-ChildItem -LiteralPath $resolvedSourceDirectory -Recurse -File -Filter "*.md") {
        $lines = [IO.File]::ReadAllLines($sourceFile.FullName)
        if ($lines.Count -lt 3 -or $lines[0] -ne "---") {
            continue
        }

        $redirectUrl = $null
        for ($index = 1; $index -lt $lines.Count -and $lines[$index] -ne "---"; $index++) {
            if ($lines[$index] -match '^redirect_url:\s*["'']?(?<url>[^"'']+?)["'']?\s*$') {
                $redirectUrl = $Matches['url']
                break
            }
        }
        if (-not $redirectUrl) {
            continue
        }

        $relativeSourcePath = [IO.Path]::GetRelativePath($resolvedSourceDirectory, $sourceFile.FullName)
        $relativeOutputPath = [IO.Path]::ChangeExtension($relativeSourcePath, ".html")
        $redirectOutputPath = [IO.Path]::GetFullPath(
            (Join-Path $resolvedOutputDirectory $relativeOutputPath))
        if (-not (Test-Path -LiteralPath $redirectOutputPath)) {
            throw "Generated redirect page does not exist: $relativeOutputPath"
        }

        $redirectHtml = [IO.File]::ReadAllText($redirectOutputPath)
        if (-not $redirectHtml.Contains('http-equiv="refresh"') -or
            -not $redirectHtml.Contains($redirectUrl)) {
            throw "Generated page '$relativeOutputPath' does not redirect to '$redirectUrl'."
        }

        if ($redirectUrl -notmatch '^[a-z][a-z0-9+.-]*:' -and
            -not $redirectUrl.StartsWith("//", [StringComparison]::Ordinal)) {
            $targetWithoutFragment = $redirectUrl.Split('#', 2)[0].Split('?', 2)[0]
            $redirectDirectory = Split-Path $redirectOutputPath -Parent
            $targetPath = if ($targetWithoutFragment.StartsWith("/", [StringComparison]::Ordinal)) {
                Join-Path $resolvedOutputDirectory $targetWithoutFragment.TrimStart('/')
            }
            else {
                Join-Path $redirectDirectory $targetWithoutFragment
            }
            $resolvedTargetPath = [IO.Path]::GetFullPath($targetPath)
            if (-not $resolvedTargetPath.StartsWith(
                    "$resolvedOutputDirectory$([IO.Path]::DirectorySeparatorChar)",
                    [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $resolvedTargetPath)) {
                throw "Generated redirect target does not exist inside the site: $redirectUrl"
            }
        }

        $redirectCount++
    }

    Write-Output "Verified $redirectCount generated documentation redirect(s)."
}

Push-Location $repositoryDirectory
try {
    Invoke-Checked { dotnet tool restore } "Tool restore"

    if (-not $NoBuild) {
        Invoke-Checked {
            dotnet build $solutionPath --configuration Release -p:VerifyDocumentationOnBuild=false
        } "Release build"
    }

    Invoke-Checked { & (Join-Path $PSScriptRoot "verify-xml-documentation.ps1") } `
        "XML documentation verification"

    Remove-GeneratedDirectory $generatedMetadataPath
    Remove-GeneratedDirectory $sitePath

    $docfxArguments = @("tool", "run", "docfx", $docfxPath)
    if ($WarningsAsErrors) {
        $docfxArguments += "--warningsAsErrors"
    }

    Invoke-Checked { dotnet @docfxArguments } "Documentation build"
    Test-GeneratedRedirects (Join-Path $repositoryDirectory "docs") $sitePath
    Write-Output "Documentation site generated at $sitePath"
}
finally {
    Pop-Location
}
