param(
    [string]$OutputPath = "COMMANDS.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

$cliProject = Join-Path $repoRoot "Meta.Cli\Meta.Cli.csproj"
$cliExe = Join-Path $repoRoot "meta.exe"

if (-not (Test-Path $cliProject))
{
    throw "Meta CLI project was not found at '$cliProject'."
}

if (-not (Test-Path $cliExe))
{
    & dotnet build $cliProject | Out-Null
}

function Invoke-MetaHelp {
    param([string[]]$MetaArgs)

    $previousErrorActionPreference = $ErrorActionPreference
    try
    {
        $ErrorActionPreference = "Continue"
        $lines = & $cliExe @MetaArgs "--help" 2>&1
    }
    finally
    {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($LASTEXITCODE -ne 0)
    {
        $commandText = "meta " + (($MetaArgs + "--help") -join " ")
        $outputText = if ($lines) { ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine } else { "(no output)" }
        throw "Help command failed: $commandText`n$outputText"
    }

    if (-not $lines)
    {
        return ""
    }

    return ($lines | ForEach-Object { $_.ToString().TrimEnd() }) -join "`n"
}

function Parse-HelpTopic {
    param([string[]]$MetaArgs)

    $text = Invoke-MetaHelp -MetaArgs $MetaArgs
    $normalized = $text -replace "`r", ""
    $lines = $normalized -split "`n"

    $summary = ""
    foreach ($line in $lines)
    {
        if ([string]::IsNullOrWhiteSpace($line))
        {
            continue
        }

        if ($line.StartsWith("Usage:", [System.StringComparison]::Ordinal))
        {
            break
        }

        $summary = $line.Trim()
        break
    }

    $usageMatch = [regex]::Match(
        $normalized,
        "Usage:\s*(?<usage>meta .*?)(?=\n(?:Options:|Examples:|Next:)|$)",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $usageMatch.Success)
    {
        throw "Could not parse Usage from help output for '$($MetaArgs -join " ")'."
    }

    $usage = (($usageMatch.Groups["usage"].Value -replace "\s+", " ").Trim())

    $examples = New-Object 'System.Collections.Generic.List[string]'
    $inExamples = $false
    foreach ($line in $lines)
    {
        if ($line.StartsWith("Examples:", [System.StringComparison]::Ordinal))
        {
            $inExamples = $true
            continue
        }

        if (-not $inExamples)
        {
            continue
        }

        if ($line.StartsWith("Next:", [System.StringComparison]::Ordinal))
        {
            break
        }

        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed))
        {
            continue
        }

        $examples.Add($trimmed)
    }

    [pscustomobject]@{
        Summary  = $summary
        Usage    = $usage
        Examples = @($examples)
    }
}

function Escape-MarkdownCell {
    param([string]$Text)

    if ($null -eq $Text)
    {
        return ""
    }

    return ($Text -replace '\|', '\|')
}

$surfaceSections = @(
    [pscustomobject]@{
        Title = "Workspace"
        Topics = @(
            @("init"),
            @("status")
        )
    },
    [pscustomobject]@{
        Title = "Inspect and validate"
        Topics = @(
            @("check"),
            @("list", "entities"),
            @("list", "properties"),
            @("list", "relationships"),
            @("view", "entity"),
            @("view", "instance"),
            @("query"),
            @("graph", "stats"),
            @("graph", "inbound")
        )
    },
    [pscustomobject]@{
        Title = "Model mutation and refactor"
        Topics = @(
            @("model", "suggest"),
            @("model", "refactor", "property-to-relationship"),
            @("model", "refactor", "relationship-to-property"),
            @("model", "add-entity"),
            @("model", "rename-entity"),
            @("model", "drop-entity"),
            @("model", "add-property"),
            @("model", "rename-property"),
            @("model", "drop-property"),
            @("model", "add-relationship"),
            @("model", "drop-relationship")
        )
    },
    [pscustomobject]@{
        Title = "Instance mutation"
        Topics = @(
            @("insert"),
            @("bulk-insert"),
            @("instance", "update"),
            @("instance", "relationship", "set"),
            @("instance", "relationship", "list"),
            @("delete")
        )
    },
    [pscustomobject]@{
        Title = "Diff and merge"
        Topics = @(
            @("instance", "diff"),
            @("instance", "merge"),
            @("instance", "diff-aligned"),
            @("instance", "merge-aligned")
        )
    },
    [pscustomobject]@{
        Title = "Import and generate"
        Topics = @(
            @("import", "xml"),
            @("import", "sql"),
            @("import", "csv"),
            @("generate", "sql"),
            @("generate", "csharp"),
            @("generate", "ssdt")
        )
    }
)

$generatedSurface = New-Object System.Text.StringBuilder
[void]$generatedSurface.AppendLine("<!-- GENERATED-COMMAND-SURFACE:START -->")
foreach ($section in $surfaceSections)
{
    [void]$generatedSurface.AppendLine("$($section.Title):")
    foreach ($topic in $section.Topics)
    {
        $topicData = Parse-HelpTopic -MetaArgs $topic
        [void]$generatedSurface.AppendLine("- ``$($topicData.Usage)``")
    }

    [void]$generatedSurface.AppendLine()
}
[void]$generatedSurface.Append("<!-- GENERATED-COMMAND-SURFACE:END -->")

$generatedQuickRef = New-Object System.Text.StringBuilder
[void]$generatedQuickRef.AppendLine("<!-- GENERATED-COMMAND-QUICKREF:START -->")
foreach ($section in $surfaceSections)
{
    [void]$generatedQuickRef.AppendLine("$($section.Title):")
    [void]$generatedQuickRef.AppendLine()
    [void]$generatedQuickRef.AppendLine("| Command | Summary | Example |")
    [void]$generatedQuickRef.AppendLine("|---|---|---|")
    foreach ($topic in $section.Topics)
    {
        $topicData = Parse-HelpTopic -MetaArgs $topic
        $example = if ($topicData.Examples.Count -gt 0) { $topicData.Examples[0] } else { $topicData.Usage }
        [void]$generatedQuickRef.AppendLine("| ``$(Escape-MarkdownCell $topicData.Usage)`` | $(Escape-MarkdownCell $topicData.Summary) | ``$(Escape-MarkdownCell $example)`` |")
    }

    [void]$generatedQuickRef.AppendLine()
}
[void]$generatedQuickRef.Append("<!-- GENERATED-COMMAND-QUICKREF:END -->")

$outputFile = Join-Path $repoRoot $OutputPath
$content = Get-Content $outputFile -Raw
$content = [regex]::Replace(
    $content,
    '(?s)<!-- GENERATED-COMMAND-SURFACE:START -->.*?<!-- GENERATED-COMMAND-SURFACE:END -->',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $generatedSurface.ToString() })
$content = [regex]::Replace(
    $content,
    '(?s)<!-- GENERATED-COMMAND-QUICKREF:START -->.*?<!-- GENERATED-COMMAND-QUICKREF:END -->',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $generatedQuickRef.ToString() })

Set-Content -Path $outputFile -Value $content -Encoding utf8
Write-Host "Wrote $OutputPath"
