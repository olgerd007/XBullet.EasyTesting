[CmdletBinding()]
param(
    [switch] $NoBuild,
    [switch] $WarningsAsErrors,
    [switch] $CheckExternalLinks,
    [ValidateSet("Warn", "Error")]
    [string] $TransientFailurePolicy = "Warn"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryDirectory = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Invoke-CheckedScript(
    [string] $path,
    [hashtable] $parameters,
    [string] $description) {
    & $path @parameters
    if (-not $?) {
        throw "$description failed."
    }
}

Push-Location $repositoryDirectory
try {
    Invoke-CheckedScript (Join-Path $PSScriptRoot "verify-markdown.ps1") @{} `
        "Markdown verification"
    Invoke-CheckedScript (Join-Path $PSScriptRoot "sync-documentation-snippets.ps1") @{ Check = $true } `
        "Documentation snippet verification"

    $buildArguments = @{}
    if ($NoBuild) {
        $buildArguments.NoBuild = $true
    }
    if ($WarningsAsErrors) {
        $buildArguments.WarningsAsErrors = $true
    }
    Invoke-CheckedScript (Join-Path $PSScriptRoot "build-documentation.ps1") $buildArguments `
        "Generated documentation verification"

    $linkArguments = @{}
    if ($CheckExternalLinks) {
        $linkArguments.CheckExternalLinks = $true
        $linkArguments.TransientFailurePolicy = $TransientFailurePolicy
    }
    Invoke-CheckedScript (Join-Path $PSScriptRoot "verify-documentation-links.ps1") $linkArguments `
        "Documentation link verification"

    Write-Output "Documentation verification completed successfully."
}
finally {
    Pop-Location
}
