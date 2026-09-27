[CmdletBinding()]
param(
    [string] $RepositoryDirectory = (Join-Path $PSScriptRoot ".."),
    [string] $DocumentationDirectory,
    [switch] $Check
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryPath = (Resolve-Path $RepositoryDirectory).Path
if ([string]::IsNullOrWhiteSpace($DocumentationDirectory)) {
    $DocumentationDirectory = Join-Path $repositoryPath "docs"
}
$documentationPath = (Resolve-Path $DocumentationDirectory).Path
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$markerPattern = [regex]::new(
    '(?ms)^(?<start>[ \t]*<!--\s*snippet:\s*(?<source>[^#\r\n]+)#(?<region>[A-Za-z0-9_.-]+)\s*-->\r?\n[ \t]*```(?<language>[A-Za-z0-9_+.-]*)\r?\n)(?<body>.*?)(?<end>\r?\n[ \t]*```\r?\n[ \t]*<!--\s*end-snippet\s*-->)')

function Get-CanonicalSnippet([string] $sourceReference, [string] $regionName) {
    $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryPath $sourceReference.Trim()))
    $relativeSourcePath = [System.IO.Path]::GetRelativePath($repositoryPath, $sourcePath)
    if ([System.IO.Path]::IsPathRooted($relativeSourcePath) -or
        $relativeSourcePath -eq ".." -or
        $relativeSourcePath.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)")) {
        throw "Snippet source '$sourceReference' resolves outside the repository."
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Snippet source '$sourceReference' does not exist."
    }

    $escapedRegion = [regex]::Escape($regionName)
    $regionPattern = [regex]::new(
        "(?ms)^[ \t]*#region[ \t]+$escapedRegion[ \t]*\r?\n(?<body>.*?)^[ \t]*#endregion[ \t]*(?:\r?\n|$)")
    $sourceText = [System.IO.File]::ReadAllText($sourcePath)
    $regionMatches = $regionPattern.Matches($sourceText)
    if ($regionMatches.Count -ne 1) {
        throw "Expected exactly one region '$regionName' in '$sourceReference'; found $($regionMatches.Count)."
    }

    $lines = @($regionMatches[0].Groups["body"].Value -split "\r?\n")
    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[0])) {
        $lines = @($lines | Select-Object -Skip 1)
    }
    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[-1])) {
        $lines = @($lines | Select-Object -First ($lines.Count - 1))
    }

    $indents = @($lines |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [regex]::Match($_, '^\s*').Value.Length })
    $indent = if ($indents.Count -eq 0) { 0 } else { ($indents | Measure-Object -Minimum).Minimum }
    $normalizedLines = @($lines | ForEach-Object {
            if ($_.Length -ge $indent) { $_.Substring($indent).TrimEnd() } else { "" }
        })
    return $normalizedLines -join "`n"
}

$staleSnippets = [System.Collections.Generic.List[string]]::new()
$updatedFiles = [System.Collections.Generic.List[string]]::new()
$markerCount = 0
$markdownFiles = Get-ChildItem $documentationPath -Recurse -Filter "*.md" -File

foreach ($file in $markdownFiles) {
    $original = [System.IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n")
    $updated = $original
    $matches = @($markerPattern.Matches($original))
    $markerCount += $matches.Count

    for ($index = $matches.Count - 1; $index -ge 0; $index--) {
        $match = $matches[$index]
        $source = $match.Groups["source"].Value.Trim()
        $region = $match.Groups["region"].Value
        $snippet = Get-CanonicalSnippet $source $region
        $replacement = $match.Groups["start"].Value.Replace("`r`n", "`n") +
            $snippet +
            $match.Groups["end"].Value.Replace("`r`n", "`n")

        if ($match.Value.Replace("`r`n", "`n") -ne $replacement) {
            $relativeDocumentPath = [System.IO.Path]::GetRelativePath($repositoryPath, $file.FullName)
            $staleSnippets.Add("$relativeDocumentPath -> $source#$region")
            $updated = $updated.Remove($match.Index, $match.Length).Insert($match.Index, $replacement)
        }
    }

    if ($updated -ne $original -and -not $Check) {
        [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8WithoutBom)
        $updatedFiles.Add([System.IO.Path]::GetRelativePath($repositoryPath, $file.FullName))
    }
}

if ($Check -and $staleSnippets.Count -gt 0) {
    foreach ($staleSnippet in $staleSnippets) {
        Write-Error "DOCSNIP001: Generated documentation snippet is stale: $staleSnippet" -ErrorAction Continue
    }
    exit 1
}

if (-not $Check) {
    foreach ($updatedFile in $updatedFiles) {
        Write-Output "Updated documentation snippets in $updatedFile"
    }
}

Write-Output "Verified $markerCount documentation snippet(s) in $($markdownFiles.Count) Markdown file(s)."
