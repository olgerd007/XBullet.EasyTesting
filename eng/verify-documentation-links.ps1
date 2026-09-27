[CmdletBinding()]
param(
    [string] $RepositoryDirectory = (Join-Path $PSScriptRoot ".."),
    [switch] $CheckExternalLinks,
    [ValidateSet("Warn", "Error")]
    [string] $TransientFailurePolicy = "Warn",
    [ValidateRange(0, 5)]
    [int] $RetryCount = 2,
    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedRepositoryDirectory = (Resolve-Path $RepositoryDirectory).Path
$diagnostics = [System.Collections.Generic.List[object]]::new()
$externalSources = @{}

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

function Get-MarkdownAnchors([string] $path) {
    $counts = @{}
    $anchors = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $insideFence = $false
    $fenceMarker = $null
    $fenceLength = 0

    foreach ($line in [IO.File]::ReadLines($path)) {
        if ($line -match '^\s*([`]{3,}|[~]{3,})(.*)$') {
            $marker = $Matches[1]
            $suffix = $Matches[2].Trim()
            if (-not $insideFence) {
                $insideFence = $true
                $fenceMarker = $marker.Substring(0, 1)
                $fenceLength = $marker.Length
            }
            elseif ($marker.StartsWith($fenceMarker, [StringComparison]::Ordinal) -and
                $marker.Length -ge $fenceLength -and [string]::IsNullOrWhiteSpace($suffix)) {
                $insideFence = $false
                $fenceMarker = $null
                $fenceLength = 0
            }
            continue
        }

        if ($insideFence -or $line -notmatch '^(#{1,6})\s+(.+?)\s*#*\s*$') {
            continue
        }

        $heading = $Matches[2]
        $heading = [regex]::Replace($heading, '!?' + '\[([^\]]+)\]\([^)]+\)', '$1')
        $heading = [regex]::Replace($heading, '<[^>]+>', '')
        $heading = [regex]::Replace($heading, '[*_~`]', '')
        $slug = $heading.ToLowerInvariant()
        $slug = [regex]::Replace($slug, '[^\p{L}\p{Nd}\s_-]', '')
        $slug = [regex]::Replace($slug, '\s', '-')
        $slug = [regex]::Replace($slug, '-+', '-').Trim('-')

        if ($counts.ContainsKey($slug)) {
            $counts[$slug]++
            $slug = "$slug-$($counts[$slug])"
        }
        else {
            $counts[$slug] = 0
        }
        [void] $anchors.Add($slug)
    }

    return $anchors
}

function Get-LinkTargets([string] $line) {
    $targets = [System.Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches(
            $line,
            '!?' + '\[[^\]]*\]\((?<target><[^>]+>|[^)\s]+)(?:\s+["''][^"'']*["''])?\)')) {
        $targets.Add($match.Groups['target'].Value.Trim('<', '>'))
    }

    if ($line -match '^\s*\[[^\]]+\]:\s*(?<target><[^>]+>|\S+)') {
        $targets.Add($Matches['target'].Trim('<', '>'))
    }

    foreach ($match in [regex]::Matches($line, '<(?<target>https?://[^>]+)>')) {
        $targets.Add($match.Groups['target'].Value)
    }

    return $targets
}

$markdownFiles = Get-ChildItem -LiteralPath $resolvedRepositoryDirectory -Recurse -File -Filter "*.md" |
    Where-Object {
        $_.FullName -notmatch "[\\/](?:\.git|\.vs|artifacts|bin|obj|TestResults[^\\/]*)[\\/]"
    } |
    Sort-Object FullName

$anchorCache = @{}
$generatedTocPath = [IO.Path]::GetFullPath(
    (Join-Path $resolvedRepositoryDirectory "docs" "api" "generated" "toc.yml"))

foreach ($file in $markdownFiles) {
    $relativeSourcePath = [IO.Path]::GetRelativePath($resolvedRepositoryDirectory, $file.FullName)
    $insideFence = $false
    $fenceMarker = $null
    $fenceLength = 0
    $lineNumber = 0

    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        if ($line -match '^\s*([`]{3,}|[~]{3,})(.*)$') {
            $marker = $Matches[1]
            $suffix = $Matches[2].Trim()
            if (-not $insideFence) {
                $insideFence = $true
                $fenceMarker = $marker.Substring(0, 1)
                $fenceLength = $marker.Length
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

        foreach ($target in Get-LinkTargets $line) {
            $target = [Net.WebUtility]::HtmlDecode($target)
            $repositoryRelative = $false
            if ($target -match '^(?:mailto|tel|data|xref):') {
                continue
            }

            if ($target -match '^https://github\.com/olgerd007/XBullet\.EasyTesting/(?:blob|tree)/main/(?<path>[^#?]+)(?<fragment>#[^?]*)?$') {
                $target = $Matches['path'] + $Matches['fragment']
                $repositoryRelative = $true
            }
            if ($target -match '^https?://') {
                if (-not $externalSources.ContainsKey($target)) {
                    $externalSources[$target] = [pscustomobject]@{
                        Path = $relativeSourcePath
                        Line = $lineNumber
                    }
                }
                continue
            }

            $parts = $target.Split('#', 2)
            $relativeTarget = [Uri]::UnescapeDataString($parts[0])
            $fragment = if ($parts.Count -eq 2) {
                [Uri]::UnescapeDataString($parts[1]).ToLowerInvariant()
            }
            else {
                ""
            }

            if ($repositoryRelative) {
                $targetPath = [IO.Path]::GetFullPath(
                    (Join-Path $resolvedRepositoryDirectory (
                            $relativeTarget -replace '/', [IO.Path]::DirectorySeparatorChar)))
            }
            elseif ($relativeTarget.StartsWith("/", [StringComparison]::Ordinal)) {
                $targetPath = [IO.Path]::GetFullPath(
                    (Join-Path $resolvedRepositoryDirectory "docs" $relativeTarget.TrimStart('/')))
            }
            elseif ([string]::IsNullOrEmpty($relativeTarget)) {
                $targetPath = $file.FullName
            }
            else {
                $targetPath = [IO.Path]::GetFullPath(
                    (Join-Path $file.DirectoryName (
                            $relativeTarget -replace '/', [IO.Path]::DirectorySeparatorChar)))
            }

            if (-not (Test-Path -LiteralPath $targetPath) -and
                -not $targetPath.Equals($generatedTocPath, [StringComparison]::OrdinalIgnoreCase)) {
                Add-Diagnostic $relativeSourcePath $lineNumber "LINK001" `
                    "Local link target '$target' does not exist."
                continue
            }

            if ($fragment -and [IO.Path]::GetExtension($targetPath) -ieq ".md") {
                if (-not $anchorCache.ContainsKey($targetPath)) {
                    $anchorCache[$targetPath] = Get-MarkdownAnchors $targetPath
                }
                if (-not $anchorCache[$targetPath].Contains($fragment)) {
                    Add-Diagnostic $relativeSourcePath $lineNumber "LINK002" `
                        "Anchor '#$fragment' does not exist in '$relativeTarget'."
                }
            }
        }
    }
}

if ($CheckExternalLinks) {
    $configurationPath = Join-Path $PSScriptRoot "documentation-links.json"
    $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
    $ignoredPatterns = @($configuration.ignoredExternalPatterns)
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $true
    $handler.AutomaticDecompression = [Net.DecompressionMethods]::All
    $client = [Net.Http.HttpClient]::new($handler)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("XBullet-EasyTesting-Documentation-Link-Checker/1.0")

    try {
        foreach ($url in @($externalSources.Keys | Sort-Object)) {
            if (@($ignoredPatterns | Where-Object { $url -match $_ }).Count -gt 0) {
                continue
            }

            $source = $externalSources[$url]
            $lastFailure = $null
            $transient = $false
            $attemptsUsed = 0
            for ($attempt = 0; $attempt -le $RetryCount; $attempt++) {
                $attemptsUsed++
                $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $url)
                $cancellation = [Threading.CancellationTokenSource]::new(
                    [TimeSpan]::FromSeconds($TimeoutSeconds))
                try {
                    $response = $client.Send(
                        $request,
                        [Net.Http.HttpCompletionOption]::ResponseHeadersRead,
                        $cancellation.Token)
                    try {
                        $statusCode = [int] $response.StatusCode
                        if (($statusCode -ge 200 -and $statusCode -lt 400) -or
                            $statusCode -in @(401, 403)) {
                            $lastFailure = $null
                            break
                        }

                        $lastFailure = "HTTP $statusCode"
                        $transient = $statusCode -in @(408, 425, 429) -or $statusCode -ge 500
                    }
                    finally {
                        $response.Dispose()
                    }
                }
                catch {
                    $lastFailure = $_.Exception.GetBaseException().Message
                    $transient = $true
                }
                finally {
                    $cancellation.Dispose()
                    $request.Dispose()
                }

                if (-not $transient -or $attempt -eq $RetryCount) {
                    break
                }
                Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
            }

            if ($null -eq $lastFailure) {
                continue
            }

            $message = "External link '$url' failed after $attemptsUsed attempt(s): $lastFailure."
            if ($transient -and $TransientFailurePolicy -eq "Warn") {
                Write-Warning "$($source.Path):$($source.Line): $message"
            }
            else {
                Add-Diagnostic $source.Path $source.Line "LINK003" $message
            }
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

$diagnostics = @($diagnostics | Sort-Object Path, Line, Id)
foreach ($diagnostic in $diagnostics) {
    Write-Output "$($diagnostic.Path):$($diagnostic.Line): error $($diagnostic.Id): $($diagnostic.Message)"
}

$externalMessage = if ($CheckExternalLinks) { " and $($externalSources.Count) external link(s)" } else { "" }
Write-Output "Link verification checked $($markdownFiles.Count) Markdown file(s)$externalMessage and found $($diagnostics.Count) error(s)."
if ($diagnostics.Count -gt 0) {
    exit 1
}
