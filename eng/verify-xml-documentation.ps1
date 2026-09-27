[CmdletBinding()]
param(
    [string] $SourceDirectory = (Join-Path $PSScriptRoot ".." "src"),
    [switch] $ReportOnly,
    [switch] $SummaryOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Import-Roslyn {
    if ("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree" -as [type]) {
        return
    }

    $sdkVersion = (& dotnet --version).Trim()
    $sdkLine = & dotnet --list-sdks |
        Where-Object { $_ -match "^$([regex]::Escape($sdkVersion))\s" } |
        Select-Object -First 1
    if ($sdkLine -notmatch "^\S+\s+\[(.+)\]$") {
        throw "Could not locate the .NET SDK directory for version '$sdkVersion'."
    }

    $roslynDirectory = Join-Path $Matches[1] $sdkVersion "Roslyn" "bincore"
    Add-Type -Path (Join-Path $roslynDirectory "Microsoft.CodeAnalysis.dll")
    Add-Type -Path (Join-Path $roslynDirectory "Microsoft.CodeAnalysis.CSharp.dll")
}

function Get-ModifierTexts($node) {
    @($node.Modifiers | ForEach-Object { $_.Text })
}

function Get-PropertyValue($instance, [string] $name) {
    $property = $instance.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-VisibleModifiers($node) {
    $modifiers = @(Get-ModifierTexts $node)
    if ($modifiers -contains "public") {
        return $true
    }

    if (($modifiers -contains "protected") -and -not ($modifiers -contains "private")) {
        return $true
    }

    $parent = $node.Parent
    return $null -ne $parent -and $parent.GetType().Name -eq "InterfaceDeclarationSyntax"
}

function Test-PublicApiNode($node) {
    if (-not (Test-VisibleModifiers $node)) {
        return $false
    }

    foreach ($ancestor in $node.Ancestors()) {
        if ($ancestor.GetType().Name -notin @(
                "ClassDeclarationSyntax",
                "StructDeclarationSyntax",
                "InterfaceDeclarationSyntax",
                "RecordDeclarationSyntax",
                "EnumDeclarationSyntax")) {
            continue
        }

        if (-not (Test-VisibleModifiers $ancestor)) {
            return $false
        }
    }

    return $true
}

function Get-Documentation($node) {
    $documentationTrivia = $node.GetLeadingTrivia() |
        ForEach-Object { $_.GetStructure() } |
        Where-Object { $null -ne $_ -and $_.GetType().Name -eq "DocumentationCommentTriviaSyntax" } |
        Select-Object -Last 1
    if ($null -eq $documentationTrivia) {
        return $null
    }

    $text = $documentationTrivia.ToFullString()
    $text = [regex]::Replace($text, "(?m)^\s*///\s?", "")
    $text = [regex]::Replace($text, "(?s)^\s*/\*\*", "")
    $text = [regex]::Replace($text, "(?s)\*/\s*$", "")
    $text = [regex]::Replace($text, "(?m)^\s*\*\s?", "")

    try {
        return [xml] "<doc>$text</doc>"
    }
    catch {
        return [pscustomobject]@{ ParseError = $_.Exception.Message }
    }
}

function Get-ParameterNodes($node) {
    $parameterList = Get-PropertyValue $node "ParameterList"
    if ($null -eq $parameterList) {
        return @()
    }

    return @($parameterList.Parameters)
}

function Get-TypeParameterNodes($node) {
    $typeParameterList = Get-PropertyValue $node "TypeParameterList"
    if ($null -eq $typeParameterList) {
        return @()
    }

    return @($typeParameterList.Parameters)
}

function Test-ReadableProperty($node) {
    if ($null -ne (Get-PropertyValue $node "ExpressionBody")) {
        return $true
    }

    $accessorList = Get-PropertyValue $node "AccessorList"
    if ($null -eq $accessorList) {
        return $false
    }

    return @($accessorList.Accessors | Where-Object { $_.Keyword.Text -eq "get" }).Count -gt 0
}

function Test-NonVoidResult($node) {
    switch ($node.GetType().Name) {
        "MethodDeclarationSyntax" { return $node.ReturnType.ToString() -ne "void" }
        "DelegateDeclarationSyntax" { return $node.ReturnType.ToString() -ne "void" }
        "OperatorDeclarationSyntax" { return $true }
        "ConversionOperatorDeclarationSyntax" { return $true }
        default { return $false }
    }
}

function Get-NodeName($node) {
    $identifier = Get-PropertyValue $node "Identifier"
    if ($null -ne $identifier) {
        return $identifier.Text
    }

    $operatorToken = Get-PropertyValue $node "OperatorToken"
    if ($null -ne $operatorToken) {
        return "operator $($operatorToken.Text)"
    }

    if ($null -ne (Get-PropertyValue $node "ThisKeyword")) {
        return "this[]"
    }

    return $node.GetType().Name
}

function Get-LineNumber($node) {
    $node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
}

function Add-Diagnostic(
    [System.Collections.Generic.List[object]] $diagnostics,
    [string] $path,
    $node,
    [string] $id,
    [string] $message) {
    $diagnostics.Add([pscustomobject]@{
            Path = $path
            Line = Get-LineNumber $node
            Id = $id
            Message = $message
        })
}

Import-Roslyn

$resolvedSourceDirectory = (Resolve-Path $SourceDirectory).Path
$repositoryDirectory = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$diagnostics = [System.Collections.Generic.List[object]]::new()
$declarationKinds = @(
    "ClassDeclarationSyntax",
    "StructDeclarationSyntax",
    "InterfaceDeclarationSyntax",
    "RecordDeclarationSyntax",
    "EnumDeclarationSyntax",
    "DelegateDeclarationSyntax",
    "MethodDeclarationSyntax",
    "ConstructorDeclarationSyntax",
    "OperatorDeclarationSyntax",
    "ConversionOperatorDeclarationSyntax",
    "PropertyDeclarationSyntax",
    "IndexerDeclarationSyntax"
)

$files = Get-ChildItem $resolvedSourceDirectory -Recurse -Filter "*.cs" -File |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" }

foreach ($file in $files) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($file.FullName),
        $null,
        $file.FullName)
    $root = $tree.GetRoot()
    $relativePath = [System.IO.Path]::GetRelativePath($repositoryDirectory, $file.FullName)

    foreach ($node in $root.DescendantNodesAndSelf()) {
        if ($node.GetType().Name -notin $declarationKinds -or -not (Test-PublicApiNode $node)) {
            continue
        }

        $documentation = Get-Documentation $node
        $name = Get-NodeName $node
        if ($null -eq $documentation) {
            Add-Diagnostic $diagnostics $relativePath $node "XMLDOC001" `
                "Public API '$name' has no XML documentation."
            continue
        }

        $parseError = Get-PropertyValue $documentation "ParseError"
        if ($null -ne $parseError) {
            Add-Diagnostic $diagnostics $relativePath $node "XMLDOC002" `
                "XML documentation for '$name' is invalid: $parseError"
            continue
        }

        if ($null -ne $documentation.SelectSingleNode("/doc/inheritdoc")) {
            continue
        }

        $summary = $documentation.SelectSingleNode("/doc/summary")
        if ($null -eq $summary -or [string]::IsNullOrWhiteSpace($summary.InnerText)) {
            Add-Diagnostic $diagnostics $relativePath $node "XMLDOC003" `
                "Public API '$name' has no useful <summary>."
        }

        $parameterNodes = @(Get-ParameterNodes $node)
        $documentedParameters = @{}
        foreach ($parameter in @($documentation.SelectNodes("/doc/param"))) {
            $documentedParameters[$parameter.name] = $parameter
        }

        foreach ($parameter in $parameterNodes) {
            $parameterName = $parameter.Identifier.Text
            if (-not $documentedParameters.ContainsKey($parameterName) -or
                [string]::IsNullOrWhiteSpace($documentedParameters[$parameterName].InnerText)) {
                Add-Diagnostic $diagnostics $relativePath $node "XMLDOC004" `
                    "Parameter '$parameterName' on '$name' has no useful <param> documentation."
            }
        }

        $typeParameterNodes = @(Get-TypeParameterNodes $node)
        $documentedTypeParameters = @{}
        foreach ($typeParameter in @($documentation.SelectNodes("/doc/typeparam"))) {
            $documentedTypeParameters[$typeParameter.name] = $typeParameter
        }

        foreach ($typeParameter in $typeParameterNodes) {
            $typeParameterName = $typeParameter.Identifier.Text
            if (-not $documentedTypeParameters.ContainsKey($typeParameterName) -or
                [string]::IsNullOrWhiteSpace($documentedTypeParameters[$typeParameterName].InnerText)) {
                Add-Diagnostic $diagnostics $relativePath $node "XMLDOC005" `
                    "Generic parameter '$typeParameterName' on '$name' has no useful <typeparam> documentation."
            }
        }

        if (Test-NonVoidResult $node) {
            $returns = $documentation.SelectSingleNode("/doc/returns")
            if ($null -eq $returns -or [string]::IsNullOrWhiteSpace($returns.InnerText)) {
                Add-Diagnostic $diagnostics $relativePath $node "XMLDOC006" `
                    "Non-void member '$name' has no useful <returns> documentation."
            }
        }

        if ($node.GetType().Name -in @("PropertyDeclarationSyntax", "IndexerDeclarationSyntax") -and
            (Test-ReadableProperty $node)) {
            $value = $documentation.SelectSingleNode("/doc/value")
            if ($null -eq $value -or [string]::IsNullOrWhiteSpace($value.InnerText)) {
                Add-Diagnostic $diagnostics $relativePath $node "XMLDOC007" `
                    "Readable property '$name' has no useful <value> documentation."
            }
        }
    }
}

$diagnostics = @($diagnostics | Sort-Object Path, Line, Id, Message)
if (-not $SummaryOnly) {
    foreach ($diagnostic in $diagnostics) {
        Write-Output "$($diagnostic.Path):$($diagnostic.Line): error $($diagnostic.Id): $($diagnostic.Message)"
    }
}

Write-Output "XML documentation verification found $($diagnostics.Count) issue(s) in $($files.Count) source file(s)."
$diagnostics |
    Group-Object Id |
    Sort-Object Name |
    ForEach-Object { Write-Output "  $($_.Name): $($_.Count)" }

if ($diagnostics.Count -gt 0 -and -not $ReportOnly) {
    exit 1
}
