<#
Runs the external MetaCli authoring demo from the repository root or any
subdirectory below it.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File Samples\Demos\MetaCliAuthoringDemo\demo-meta-cli.ps1

The script recreates Samples\Demos\MetaCliAuthoringDemo\out and writes a
deterministic command transcript to out\demo-meta-cli-output.txt.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-RepoRoot {
    param([string]$StartDirectory)

    $directory = [System.IO.Path]::GetFullPath($StartDirectory)
    while (-not [string]::IsNullOrWhiteSpace($directory)) {
        $solutionPath = Join-Path $directory "Metadata.Framework.sln"
        $projectPath = Join-Path $directory "MetaCli\Cli\MetaCli.Cli.csproj"
        if ((Test-Path -LiteralPath $solutionPath -PathType Leaf) -and
            (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            return $directory
        }

        $parent = Split-Path -Parent $directory
        if ($parent -eq $directory) {
            break
        }

        $directory = $parent
    }

    throw "Could not resolve repository root from '$StartDirectory'."
}

function Format-Command {
    param([string[]]$Parts)

    ($Parts | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + $_.Replace('"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join " "
}

function Write-TranscriptLine {
    param([string]$Line = "")

    $Line | Tee-Object -FilePath $script:TranscriptPath -Append
}

function Invoke-MetaCli {
    param(
        [string[]]$Arguments,
        [int[]]$ExpectedExitCodes
    )

    $dotnetArgs = @("run", "--project", "MetaCli/Cli/MetaCli.Cli.csproj", "--") + $Arguments
    Write-TranscriptLine ("> " + (Format-Command (@("dotnet") + $dotnetArgs)))

    Push-Location $script:RepoRoot
    try {
        $output = & dotnet @dotnetArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    foreach ($line in $output) {
        Write-TranscriptLine $line.ToString()
    }

    Write-TranscriptLine "ExitCode: $exitCode"
    Write-TranscriptLine

    if ($ExpectedExitCodes -notcontains $exitCode) {
        throw "Command exited with $exitCode; expected one of: $($ExpectedExitCodes -join ', ')."
    }
}

$script:RepoRoot = Find-RepoRoot $PSScriptRoot
$demoRelativeRoot = "Samples\Demos\MetaCliAuthoringDemo"
$outRelativeRoot = "$demoRelativeRoot\out"
$workspaceRelativePath = "$outRelativeRoot\MetaCliDemo.Workspace"

$demoRoot = Join-Path $script:RepoRoot $demoRelativeRoot
$outRoot = Join-Path $script:RepoRoot $outRelativeRoot
$resolvedDemoRoot = [System.IO.Path]::GetFullPath($demoRoot)
$resolvedOutRoot = [System.IO.Path]::GetFullPath($outRoot)
if (-not $resolvedOutRoot.StartsWith($resolvedDemoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to recreate output directory outside the demo root: '$resolvedOutRoot'."
}

if (Test-Path -LiteralPath $outRoot) {
    Remove-Item -LiteralPath $outRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outRoot | Out-Null
$script:TranscriptPath = Join-Path $outRoot "demo-meta-cli-output.txt"
New-Item -ItemType File -Path $script:TranscriptPath | Out-Null

Invoke-MetaCli @("--new-workspace", $workspaceRelativePath) @(0)
Invoke-MetaCli @("add-application", "--workspace", $workspaceRelativePath, "--id", "app-demo", "--name", "demo", "--executable-name", "demo") @(0)
Invoke-MetaCli @("add-value-arity", "--workspace", $workspaceRelativePath, "--id", "arity-none", "--name", "None", "--min-value-count", "0", "--max-value-count", "0") @(0)
Invoke-MetaCli @("add-value-arity", "--workspace", $workspaceRelativePath, "--id", "arity-one", "--name", "One", "--min-value-count", "1", "--max-value-count", "1") @(0)
Invoke-MetaCli @("add-value-shape", "--workspace", $workspaceRelativePath, "--id", "shape-flag", "--name", "Flag", "--value-arity", "arity-none") @(0)
Invoke-MetaCli @("add-value-shape", "--workspace", $workspaceRelativePath, "--id", "shape-path", "--name", "Path", "--value-arity", "arity-one", "--value-label", "<path>", "--allows-option-like-value", "true") @(0)
Invoke-MetaCli @("add-value-shape", "--workspace", $workspaceRelativePath, "--id", "shape-text", "--name", "Text", "--value-arity", "arity-one", "--value-label", "<value>") @(0)
Invoke-MetaCli @("add-value-shape", "--workspace", $workspaceRelativePath, "--id", "shape-visibility", "--name", "Visibility", "--value-arity", "arity-one", "--value-label", "<visibility>") @(0)
Invoke-MetaCli @("add-allowed-value", "--workspace", $workspaceRelativePath, "--id", "visibility-public", "--value-shape", "shape-visibility", "--value", "public") @(0)
Invoke-MetaCli @("add-allowed-value", "--workspace", $workspaceRelativePath, "--id", "visibility-internal", "--value-shape", "shape-visibility", "--value", "internal", "--previous-value", "visibility-public") @(0)

Invoke-MetaCli @("set-default-command", "--workspace", $workspaceRelativePath, "--application", "app-demo", "--command-id", "cmd-root", "--executable-command-id", "exec-root", "--name", "root") @(0)
Invoke-MetaCli @("add-command", "--workspace", $workspaceRelativePath, "--id", "cmd-model", "--application", "app-demo", "--name", "model", "--token", "model") @(0)
Invoke-MetaCli @("add-command", "--workspace", $workspaceRelativePath, "--id", "cmd-add-property", "--application", "app-demo", "--name", "add-property", "--token", "add-property", "--parent-command", "cmd-model") @(0)
Invoke-MetaCli @("add-executable-command", "--workspace", $workspaceRelativePath, "--id", "exec-add-property", "--command", "cmd-add-property") @(0)
Invoke-MetaCli @("add-option", "--workspace", $workspaceRelativePath, "--parameter-id", "param-workspace", "--option-id", "option-workspace", "--executable-command", "exec-add-property", "--name", "workspace", "--value-shape", "shape-path", "--token-id", "token-workspace", "--token", "--workspace", "--required", "true") @(0)
Invoke-MetaCli @("add-option-token", "--workspace", $workspaceRelativePath, "--id", "token-workspace-short", "--option", "option-workspace", "--token", "-w", "--previous-token", "token-workspace") @(0)
Invoke-MetaCli @("add-positional", "--workspace", $workspaceRelativePath, "--parameter-id", "param-entity", "--positional-id", "pos-entity", "--executable-command", "exec-add-property", "--name", "Entity", "--value-shape", "shape-text", "--required", "true") @(0)
Invoke-MetaCli @("add-positional", "--workspace", $workspaceRelativePath, "--parameter-id", "param-property", "--positional-id", "pos-property", "--executable-command", "exec-add-property", "--name", "Property", "--value-shape", "shape-text", "--previous-argument", "pos-entity", "--required", "true") @(0)

Invoke-MetaCli @("add-command", "--workspace", $workspaceRelativePath, "--id", "cmd-add-entity", "--application", "app-demo", "--name", "add-entity", "--token", "add-entity", "--parent-command", "cmd-model") @(0)
Invoke-MetaCli @("add-executable-command", "--workspace", $workspaceRelativePath, "--id", "exec-add-entity", "--command", "cmd-add-entity") @(0)
Invoke-MetaCli @("add-positional", "--workspace", $workspaceRelativePath, "--parameter-id", "param-id", "--positional-id", "pos-id", "--executable-command", "exec-add-entity", "--name", "Id", "--value-shape", "shape-text") @(0)
Invoke-MetaCli @("add-option", "--workspace", $workspaceRelativePath, "--parameter-id", "param-auto-id", "--option-id", "option-auto-id", "--executable-command", "exec-add-entity", "--name", "auto-id", "--value-shape", "shape-flag", "--token-id", "token-auto-id", "--token", "--auto-id") @(0)
Invoke-MetaCli @("add-option-token", "--workspace", $workspaceRelativePath, "--id", "token-auto-id-short", "--option", "option-auto-id", "--token", "-a", "--previous-token", "token-auto-id") @(0)
Invoke-MetaCli @("add-parameter-group", "--workspace", $workspaceRelativePath, "--id", "group-id-choice", "--executable-command", "exec-add-entity", "--name", "IdChoice", "--member-id", "group-id-choice-id", "--parameter", "param-id", "--required", "true") @(0)
Invoke-MetaCli @("add-parameter-group-member", "--workspace", $workspaceRelativePath, "--id", "group-id-choice-auto", "--parameter-group", "group-id-choice", "--parameter", "param-auto-id", "--previous-member", "group-id-choice-id") @(0)

Invoke-MetaCli @("add-duplicate-option-behavior", "--workspace", $workspaceRelativePath, "--id", "duplicate-error", "--name", "Error") @(0)
Invoke-MetaCli @("add-unknown-token-behavior", "--workspace", $workspaceRelativePath, "--id", "unknown-error", "--name", "Error") @(0)
Invoke-MetaCli @("add-parser-policy", "--workspace", $workspaceRelativePath, "--id", "parser-default", "--application", "app-demo", "--name", "Default", "--stop-parsing-token", "--", "--allows-equals-value-syntax", "true", "--allows-options-after-positionals", "false", "--allows-short-option-clusters", "false", "--duplicate-option-behavior", "duplicate-error", "--unknown-token-behavior", "unknown-error") @(0)
Invoke-MetaCli @("add-output-format", "--workspace", $workspaceRelativePath, "--id", "output-format-text", "--name", "Text", "--content-type", "text/plain") @(0)
Invoke-MetaCli @("add-output-stream", "--workspace", $workspaceRelativePath, "--id", "output-stream-stdout", "--name", "Stdout") @(0)
Invoke-MetaCli @("add-output", "--workspace", $workspaceRelativePath, "--id", "output-add-property-summary", "--executable-command", "exec-add-property", "--name", "Summary", "--output-format", "output-format-text", "--output-stream", "output-stream-stdout") @(0)
Invoke-MetaCli @("add-exit-code", "--workspace", $workspaceRelativePath, "--id", "exit-usage", "--application", "app-demo", "--code", "2", "--name", "UsageError") @(0)
Invoke-MetaCli @("add-exit-code", "--workspace", $workspaceRelativePath, "--id", "exit-add-property-ok", "--application", "app-demo", "--executable-command", "exec-add-property", "--code", "0", "--name", "Ok") @(0)

Invoke-MetaCli @("show", "--workspace", $workspaceRelativePath) @(0)

Invoke-MetaCli @("add-application", "--workspace", $workspaceRelativePath, "--id", "app-demo", "--name", "Duplicate") @(4)
Invoke-MetaCli @("add-option-token", "--workspace", $workspaceRelativePath, "--id", "token-missing-option", "--option", "missing-option", "--token", "--missing", "--previous-token", "token-workspace") @(4)

Write-Host "Transcript: $script:TranscriptPath"
