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
    Write-Output "Documentation site generated at $sitePath"
}
finally {
    Pop-Location
}
