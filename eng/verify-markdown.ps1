[CmdletBinding()]
param(
    [string] $RepositoryDirectory = (Join-Path $PSScriptRoot ".."),
    [int] $MaximumLineLength = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedRepositoryDirectory = (Resolve-Path $RepositoryDirectory).Path
$diagnostics = [System.Collections.Generic.List[object]]::new()

function Add-Diagnostic(
    [string] $path,
    [int] $line,
    [string] $id,
    [string] $message) {
    $diagnostics.Add([pscustomobject]@{
            Path = $path
            Line = $line
            Id = $id
            Message = $message
        })
}

$files = Get-ChildItem -LiteralPath $resolvedRepositoryDirectory -Recurse -File -Filter "*.md" |
    Where-Object {
        $_.FullName -notmatch "[\\/](?:\.git|\.vs|artifacts|bin|obj|TestResults[^\\/]*)[\\/]"
    } |
    Sort-Object FullName

foreach ($file in $files) {
    $relativePath = [IO.Path]::GetRelativePath($resolvedRepositoryDirectory, $file.FullName)
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n", [StringComparison]::Ordinal)) {
        Add-Diagnostic $relativePath 1 "MD001" "File must end with a newline."
    }

    $lines = [IO.File]::ReadAllLines($file.FullName)
    $insideFence = $false
    $fenceMarker = $null
    $fenceLength = 0
    $insideFrontMatter = $false
    $frontMatterClosed = $false
    $blankLineCount = 0
    $headingLevel = 0
    $h1Count = 0

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $lineNumber = $index + 1

        if ($index -eq 0 -and $line -eq "---") {
            $insideFrontMatter = $true
            continue
        }

        if ($insideFrontMatter) {
            if ($line -eq "---") {
                $insideFrontMatter = $false
                $frontMatterClosed = $true
            }
            continue
        }

        if ($line -match '^\s*([`]{3,}|[~]{3,})(.*)$') {
            $marker = $Matches[1]
            $suffix = $Matches[2].Trim()
            if (-not $insideFence) {
                $insideFence = $true
                $fenceMarker = $marker.Substring(0, 1)
                $fenceLength = $marker.Length
                if ([string]::IsNullOrWhiteSpace($suffix)) {
                    Add-Diagnostic $relativePath $lineNumber "MD002" `
                        "Opening code fences must declare a language such as 'text', 'csharp', or 'shell'."
                }
            }
            elseif ($marker.StartsWith($fenceMarker, [StringComparison]::Ordinal) -and
                $marker.Length -ge $fenceLength -and [string]::IsNullOrWhiteSpace($suffix)) {
                $insideFence = $false
                $fenceMarker = $null
                $fenceLength = 0
            }
            continue
        }

        if ($insideFence) {
            continue
        }

        if ($line -match "[ \t]+$") {
            Add-Diagnostic $relativePath $lineNumber "MD003" "Remove trailing whitespace."
        }

        if ($line.Contains("`t")) {
            Add-Diagnostic $relativePath $lineNumber "MD004" "Use spaces instead of tabs in Markdown prose."
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            $blankLineCount++
            if ($blankLineCount -gt 2) {
                Add-Diagnostic $relativePath $lineNumber "MD005" `
                    "Use no more than two consecutive blank lines."
            }
            continue
        }
        $blankLineCount = 0

        if ($line -match "^(#{1,6})([^#\s].*)$") {
            Add-Diagnostic $relativePath $lineNumber "MD006" "Add a space after the heading marker."
        }

        if ($line -match "^(#{1,6})\s+\S") {
            $currentHeadingLevel = $Matches[1].Length
            if ($currentHeadingLevel -eq 1) {
                $h1Count++
            }
            if ($headingLevel -gt 0 -and $currentHeadingLevel -gt ($headingLevel + 1)) {
                Add-Diagnostic $relativePath $lineNumber "MD007" `
                    "Heading level jumps from H$headingLevel to H$currentHeadingLevel."
            }
            $headingLevel = $currentHeadingLevel
        }

        $lineLengthExempt =
            $line.StartsWith("|", [StringComparison]::Ordinal) -or
            $line.StartsWith("<!--", [StringComparison]::Ordinal) -or
            $line -match "^\s*<https?://" -or
            $line -match "\]\(https?://" -or
            $line -match "^\s*https?://"
        if (-not $lineLengthExempt -and $line.Length -gt $MaximumLineLength) {
            Add-Diagnostic $relativePath $lineNumber "MD008" `
                "Line has $($line.Length) characters; wrap prose at $MaximumLineLength."
        }
    }

    if ($insideFrontMatter -and -not $frontMatterClosed) {
        Add-Diagnostic $relativePath 1 "MD009" "YAML front matter is not closed."
    }
    if ($insideFence) {
        Add-Diagnostic $relativePath $lines.Count "MD010" "Code fence is not closed."
    }
    if ($h1Count -gt 1) {
        Add-Diagnostic $relativePath 1 "MD011" "Use at most one H1 heading per Markdown file."
    }
}

$diagnostics = @($diagnostics | Sort-Object Path, Line, Id)
foreach ($diagnostic in $diagnostics) {
    Write-Output "$($diagnostic.Path):$($diagnostic.Line): error $($diagnostic.Id): $($diagnostic.Message)"
}

Write-Output "Markdown verification found $($diagnostics.Count) issue(s) in $($files.Count) file(s)."
if ($diagnostics.Count -gt 0) {
    exit 1
}
