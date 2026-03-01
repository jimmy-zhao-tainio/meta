param(
    [string]$OutputPath = "COMMANDS-EXAMPLES.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

function Normalize-OutputForDocs {
    param([string]$Text)

    if ($null -eq $Text)
    {
        return ""
    }

    $normalized = $Text
    $normalized = $normalized -replace [regex]::Escape($repoRoot), "<repo>"
    $normalized = $normalized -replace [regex]::Escape($repoRoot.Replace('\\', '/')), "<repo>"
    if ($script:PathDisplayMap)
    {
        foreach ($entry in $script:PathDisplayMap.GetEnumerator() | Sort-Object { $_.Key.Length } -Descending)
        {
            $normalized = $normalized -replace [regex]::Escape($entry.Key), $entry.Value
            $normalized = $normalized -replace [regex]::Escape($entry.Key.Replace('\\', '/')), $entry.Value.Replace('\', '/')
        }
    }

    return $normalized
}

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

function Format-Arg {
    param([string]$Value)

    if ($null -eq $Value)
    {
        return '""'
    }

    if ($Value -match '^[A-Za-z0-9_./\\:=#,\-\[\]]+$')
    {
        return $Value
    }

    $escaped = $Value -replace '"', '\\"'
    return '"' + $escaped + '"'
}

function Invoke-MetaCapture {
    param([string[]]$MetaArgs)

    $display = "meta " + (($MetaArgs | ForEach-Object { Format-Arg $_ }) -join " ")
    $display = Normalize-OutputForDocs -Text $display

    $previousErrorActionPreference = $ErrorActionPreference
    try
    {
        $ErrorActionPreference = "Continue"
        $lines = & $cliExe @MetaArgs 2>&1
    }
    finally
    {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $exitCode = $LASTEXITCODE
    $text = if ($lines)
    {
        ($lines | ForEach-Object { $_.ToString().TrimEnd() }) -join "`n"
    }
    else
    {
        "(no output)"
    }

    [pscustomobject]@{
        Display = $display
        ExitCode = $exitCode
        Output = (Normalize-OutputForDocs -Text $text)
    }
}

function Invoke-MetaStrict {
    param([string[]]$MetaArgs)

    $result = Invoke-MetaCapture -MetaArgs $MetaArgs
    if ($result.ExitCode -ne 0)
    {
        throw "Setup command failed: $($result.Display)`nExit: $($result.ExitCode)`nOutput:`n$($result.Output)"
    }

    return $result
}

function Add-Case {
    param(
        [string]$Name,
        [string[]]$SuccessArgs,
        [string[]]$FailureArgs
    )

    $success = Invoke-MetaCapture -MetaArgs $SuccessArgs
    $failure = Invoke-MetaCapture -MetaArgs $FailureArgs

    $script:Cases.Add([pscustomobject]@{
            Name = $Name
            Success = $success
            Failure = $failure
        })
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("meta-command-examples-" + [Guid]::NewGuid().ToString("N"))
$baseWorkspace = Join-Path $workRoot "CommandExamples"
$initWorkspace = Join-Path $workRoot "CommandExamplesInit"
$importXmlWorkspace = Join-Path $workRoot "CommandExamplesImportedXml"
$brokenWorkspace = Join-Path $workRoot "CommandExamplesBroken"
$diffLeftWorkspace = Join-Path $workRoot "CommandExamplesDiffLeft"
$diffRightWorkspace = Join-Path $workRoot "CommandExamplesDiffRight"
$outputRoot = Join-Path $workRoot "CommandExamplesOut"
$inputRoot = Join-Path $baseWorkspace "input"
$bulkFile = Join-Path $inputRoot "cube-bulk-insert.tsv"
$bulkInvalidFile = Join-Path $inputRoot "cube-bulk-insert-invalid.tsv"
$bulkAutoIdFile = Join-Path $inputRoot "cube-bulk-insert-auto-id.tsv"

$script:PathDisplayMap = [ordered]@{
    $baseWorkspace = "Samples\\Fixtures\\CommandExamples"
    $initWorkspace = "Samples\\Fixtures\\CommandExamplesInit"
    $importXmlWorkspace = "Samples\\Fixtures\\CommandExamplesImportedXml"
    $brokenWorkspace = "Samples\\Fixtures\\CommandExamplesBroken"
    $diffLeftWorkspace = "Samples\\Fixtures\\CommandExamplesDiffLeft"
    $diffRightWorkspace = "Samples\\Fixtures\\CommandExamplesDiffRight"
    $outputRoot = "Samples\\Fixtures\\CommandExamplesOut"
}

$cleanupTargets = @($workRoot)

foreach ($path in $cleanupTargets)
{
    if (Test-Path $path)
    {
        Remove-Item $path -Recurse -Force
    }
}

Invoke-MetaStrict -MetaArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $baseWorkspace) | Out-Null
Invoke-MetaStrict -MetaArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $diffLeftWorkspace) | Out-Null
Invoke-MetaStrict -MetaArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $diffRightWorkspace) | Out-Null
Invoke-MetaStrict -MetaArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $brokenWorkspace) | Out-Null
Invoke-MetaStrict -MetaArgs @("insert", "Cube", "99", "--set", "CubeName=Diff Cube", "--set", "Purpose=Diff sample", "--set", "RefreshMode=Manual", "--workspace", $diffRightWorkspace) | Out-Null

New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null

$brokenModelPath = Join-Path (Join-Path $brokenWorkspace "metadata") "model.xml"
Set-Content -Path $brokenModelPath -Value '<Model name="BrokenModel"><Entities><Entity name="Bad"></Entities></Model>' -Encoding utf8

@(
    "Id`tCubeName`tPurpose`tRefreshMode",
    "1`tSales Performance`tMonthly revenue and margin tracking.`tScheduled",
    "3`tOperations Cube`tOperations KPIs`tManual"
) | Set-Content -Path $bulkFile -Encoding utf8

@(
    "Id`tUnknownColumn",
    "1`tBadValue"
) | Set-Content -Path $bulkInvalidFile -Encoding utf8

@(
    "CubeName`tPurpose`tRefreshMode",
    "Auto Id Cube A`tGenerated by bulk insert auto-id sample`tManual",
    "Auto Id Cube B`tGenerated by bulk insert auto-id sample`tScheduled"
) | Set-Content -Path $bulkAutoIdFile -Encoding utf8

$script:Cases = New-Object 'System.Collections.Generic.List[object]'

Add-Case -Name "help" -SuccessArgs @("help") -FailureArgs @("help", "unknown-topic")
Add-Case -Name "command help" -SuccessArgs @("model", "--help") -FailureArgs @("model", "add-entity")
Add-Case -Name "init" -SuccessArgs @("init", $initWorkspace) -FailureArgs @("init", "Samples\Bad|Path")
Add-Case -Name "status" -SuccessArgs @("status", "--workspace", $baseWorkspace) -FailureArgs @("status", "--workspace", $brokenWorkspace)
Add-Case -Name "instance diff" -SuccessArgs @("instance", "diff", $diffLeftWorkspace, $diffRightWorkspace) -FailureArgs @("instance", "diff", $diffLeftWorkspace, "Samples\MissingWorkspace")
Add-Case -Name "list entities" -SuccessArgs @("list", "entities", "--workspace", $baseWorkspace) -FailureArgs @("list", "entities", "--workspace", $brokenWorkspace)
Add-Case -Name "list properties" -SuccessArgs @("list", "properties", "Cube", "--workspace", $baseWorkspace) -FailureArgs @("list", "properties", "MissingEntity", "--workspace", $baseWorkspace)
Add-Case -Name "list relationships" -SuccessArgs @("list", "relationships", "Measure", "--workspace", $baseWorkspace) -FailureArgs @("list", "relationships", "MissingEntity", "--workspace", $baseWorkspace)
Add-Case -Name "check" -SuccessArgs @("check", "--workspace", $baseWorkspace) -FailureArgs @("check", "--workspace", $brokenWorkspace)
Add-Case -Name "view entity" -SuccessArgs @("view", "entity", "Cube", "--workspace", $baseWorkspace) -FailureArgs @("view", "entity", "MissingEntity", "--workspace", $baseWorkspace)
Add-Case -Name "view instance" -SuccessArgs @("view", "instance", "Cube", "1", "--workspace", $baseWorkspace) -FailureArgs @("view", "instance", "Cube", "999", "--workspace", $baseWorkspace)
Add-Case -Name "query" -SuccessArgs @("query", "Cube", "--workspace", $baseWorkspace, "--contains", "CubeName", "Sales") -FailureArgs @("query", "Cube", "--workspace", $baseWorkspace, "--contains", "MissingField", "Value")
Add-Case -Name "graph stats" -SuccessArgs @("graph", "stats", "--workspace", $baseWorkspace, "--top", "3", "--cycles", "3") -FailureArgs @("graph", "stats", "--workspace", $brokenWorkspace, "--top", "3", "--cycles", "3")
Add-Case -Name "graph inbound" -SuccessArgs @("graph", "inbound", "Cube", "--workspace", $baseWorkspace, "--top", "10") -FailureArgs @("graph", "inbound", "MissingEntity", "--workspace", $baseWorkspace)

Add-Case -Name "model add-entity" -SuccessArgs @("model", "add-entity", "CmdEntity", "--workspace", $baseWorkspace) -FailureArgs @("model", "add-entity", "Cube", "--workspace", $baseWorkspace)
Add-Case -Name "model rename-entity" -SuccessArgs @("model", "rename-entity", "CmdEntity", "CmdEntityRenamed", "--workspace", $baseWorkspace) -FailureArgs @("model", "rename-entity", "MissingEntity", "Anything", "--workspace", $baseWorkspace)
Add-Case -Name "model add-property" -SuccessArgs @("model", "add-property", "CmdEntityRenamed", "Label", "--required", "true", "--workspace", $baseWorkspace) -FailureArgs @("model", "add-property", "MissingEntity", "Label", "--workspace", $baseWorkspace)
Add-Case -Name "model rename-property" -SuccessArgs @("model", "rename-property", "CmdEntityRenamed", "Label", "LabelText", "--workspace", $baseWorkspace) -FailureArgs @("model", "rename-property", "CmdEntityRenamed", "MissingProp", "Anything", "--workspace", $baseWorkspace)
Add-Case -Name "model add-relationship" -SuccessArgs @("model", "add-relationship", "CmdEntityRenamed", "Cube", "--default-id", "1", "--workspace", $baseWorkspace) -FailureArgs @("model", "add-relationship", "CmdEntityRenamed", "MissingTarget", "--default-id", "1", "--workspace", $baseWorkspace)
Add-Case -Name "model drop-relationship" -SuccessArgs @("model", "drop-relationship", "CmdEntityRenamed", "Cube", "--workspace", $baseWorkspace) -FailureArgs @("model", "drop-relationship", "Measure", "Cube", "--workspace", $baseWorkspace)
Add-Case -Name "model drop-property" -SuccessArgs @("model", "drop-property", "CmdEntityRenamed", "LabelText", "--workspace", $baseWorkspace) -FailureArgs @("model", "drop-property", "CmdEntityRenamed", "MissingProp", "--workspace", $baseWorkspace)
Add-Case -Name "model drop-entity" -SuccessArgs @("model", "drop-entity", "CmdEntityRenamed", "--workspace", $baseWorkspace) -FailureArgs @("model", "drop-entity", "Cube", "--workspace", $baseWorkspace)

Add-Case -Name "insert" -SuccessArgs @("insert", "Cube", "10", "--set", "CubeName=Ops Cube", "--set", "Purpose=Operational reporting", "--set", "RefreshMode=Scheduled", "--workspace", $baseWorkspace) -FailureArgs @("insert", "Cube", "10", "--set", "CubeName=Duplicate", "--workspace", $baseWorkspace)
Add-Case -Name "insert auto-id" -SuccessArgs @("insert", "Cube", "--auto-id", "--set", "CubeName=Auto Id Cube", "--set", "Purpose=Autogenerated id sample", "--set", "RefreshMode=Manual", "--workspace", $baseWorkspace) -FailureArgs @("insert", "Cube", "11", "--auto-id", "--set", "CubeName=Conflict", "--workspace", $baseWorkspace)
Add-Case -Name "instance update" -SuccessArgs @("instance", "update", "Cube", "10", "--set", "RefreshMode=Manual", "--workspace", $baseWorkspace) -FailureArgs @("instance", "update", "Cube", "1", "--set", "MissingField=BadValue", "--workspace", $baseWorkspace)
Add-Case -Name "instance relationship set" -SuccessArgs @("instance", "relationship", "set", "Measure", "1", "--to", "Cube", "2", "--workspace", $baseWorkspace) -FailureArgs @("instance", "relationship", "set", "Measure", "1", "--to", "Cube", "999", "--workspace", $baseWorkspace)
Add-Case -Name "instance relationship list" -SuccessArgs @("instance", "relationship", "list", "Measure", "1", "--workspace", $baseWorkspace) -FailureArgs @("instance", "relationship", "list", "Measure", "999", "--workspace", $baseWorkspace)

Add-Case -Name "bulk-insert" -SuccessArgs @("bulk-insert", "Cube", "--from", "tsv", "--file", $bulkFile, "--key", "Id", "--workspace", $baseWorkspace) -FailureArgs @("bulk-insert", "Cube", "--from", "tsv", "--file", $bulkInvalidFile, "--key", "Id", "--workspace", $baseWorkspace)
Add-Case -Name "bulk-insert auto-id" -SuccessArgs @("bulk-insert", "Cube", "--from", "tsv", "--file", $bulkAutoIdFile, "--auto-id", "--workspace", $baseWorkspace) -FailureArgs @("bulk-insert", "Cube", "--from", "tsv", "--file", $bulkAutoIdFile, "--auto-id", "--key", "Id", "--workspace", $baseWorkspace)
Add-Case -Name "delete" -SuccessArgs @("delete", "Cube", "10", "--workspace", $baseWorkspace) -FailureArgs @("delete", "Cube", "2", "--workspace", $baseWorkspace)

Add-Case -Name "generate sql" -SuccessArgs @("generate", "sql", "--out", (Join-Path $outputRoot "sql"), "--workspace", $baseWorkspace) -FailureArgs @("generate", "sql", "--out", (Join-Path $outputRoot "sql-broken"), "--workspace", $brokenWorkspace)
Add-Case -Name "generate csharp" -SuccessArgs @("generate", "csharp", "--out", (Join-Path $outputRoot "csharp"), "--workspace", $baseWorkspace) -FailureArgs @("generate", "csharp", "--out", (Join-Path $outputRoot "csharp-broken"), "--workspace", $brokenWorkspace)
Add-Case -Name "generate ssdt" -SuccessArgs @("generate", "ssdt", "--out", (Join-Path $outputRoot "ssdt"), "--workspace", $baseWorkspace) -FailureArgs @("generate", "ssdt", "--out", (Join-Path $outputRoot "ssdt-broken"), "--workspace", $brokenWorkspace)

Add-Case -Name "import xml" -SuccessArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $importXmlWorkspace) -FailureArgs @("import", "xml", "Samples\\Contracts\\SampleModel.xml", "Samples\\Contracts\\SampleInstance.xml", "--new-workspace", $baseWorkspace)

$workspaceDiffForMerge = Invoke-MetaCapture -MetaArgs @("instance", "diff", $diffLeftWorkspace, $diffRightWorkspace)
$diffWorkspacePath = $null
if ($workspaceDiffForMerge.Output -match '(?m)^DiffWorkspace:\s*(.+)$')
{
    $diffWorkspacePath = $Matches[1].Trim()
    $diffWorkspacePath = $diffWorkspacePath -replace [regex]::Escape("<repo>"), $repoRoot
}

if ([string]::IsNullOrWhiteSpace($diffWorkspacePath))
{
    throw "Unable to resolve diff workspace path from output:`n$($workspaceDiffForMerge.Output)"
}

$workspaceMergeSuccess = Invoke-MetaCapture -MetaArgs @("instance", "merge", $diffLeftWorkspace, $diffWorkspacePath)
Invoke-MetaStrict -MetaArgs @("insert", "Cube", "100", "--set", "CubeName=Conflict Cube", "--set", "Purpose=Merge conflict sample", "--set", "RefreshMode=Manual", "--workspace", $diffLeftWorkspace) | Out-Null
$workspaceMergeFailure = Invoke-MetaCapture -MetaArgs @("instance", "merge", $diffLeftWorkspace, $diffWorkspacePath)
$script:Cases.Add([pscustomobject]@{
        Name = "instance merge"
        Success = $workspaceMergeSuccess
        Failure = $workspaceMergeFailure
    })

$content = New-Object System.Text.StringBuilder
[void]$content.AppendLine("# Meta CLI Real Command Examples")
[void]$content.AppendLine()
[void]$content.AppendLine("All examples below were executed against local workspaces in this repository. Each section includes one successful run and one failing run with captured output and exit code.")
[void]$content.AppendLine()

foreach ($case in $Cases)
{
    [void]$content.AppendLine("## $($case.Name)")
    [void]$content.AppendLine()
    [void]$content.AppendLine("Success:")
    [void]$content.AppendLine('```powershell')
    [void]$content.AppendLine("> $($case.Success.Display)")
    [void]$content.AppendLine("[exit $($case.Success.ExitCode)]")
    [void]$content.AppendLine($case.Success.Output)
    [void]$content.AppendLine('```')
    [void]$content.AppendLine()
    [void]$content.AppendLine("Failure:")
    [void]$content.AppendLine('```powershell')
    [void]$content.AppendLine("> $($case.Failure.Display)")
    [void]$content.AppendLine("[exit $($case.Failure.ExitCode)]")
    [void]$content.AppendLine($case.Failure.Output)
    [void]$content.AppendLine('```')
    [void]$content.AppendLine()
}

$resolvedOutput = Resolve-Path -LiteralPath (Split-Path -Path (Join-Path $repoRoot $OutputPath) -Parent) -ErrorAction SilentlyContinue
if (-not $resolvedOutput)
{
    New-Item -ItemType Directory -Path (Split-Path -Path (Join-Path $repoRoot $OutputPath) -Parent) -Force | Out-Null
}

Set-Content -Path (Join-Path $repoRoot $OutputPath) -Value $content.ToString() -Encoding utf8
if (Test-Path $workRoot)
{
    Remove-Item $workRoot -Recurse -Force
}
Write-Host "Wrote $OutputPath"

