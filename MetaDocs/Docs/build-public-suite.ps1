[CmdletBinding()]
param(
    [string] $DocsRoot = $PSScriptRoot,
    [string] $MetaDocsExe,
    [string] $MetaBiRoot,
    [switch] $RefreshCliWorkspaces,
    [switch] $IncludeProseDiagnostics,
    [switch] $WarningsAsErrors,
    [switch] $IncludePrivateWorkspaces
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DocsRoot)) {
    $DocsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $Description,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )

    Write-Host $Description
    & $script:MetaDocsExe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$docsRootPath = Resolve-FullPath $DocsRoot
$workspacesRoot = Join-Path $docsRootPath "Workspaces"
$suiteWorkspace = Join-Path $docsRootPath "SuiteWorkspace"
$siteOutput = Join-Path $docsRootPath "Site"

if ([string]::IsNullOrWhiteSpace($MetaDocsExe)) {
    $MetaDocsExe = Join-Path $docsRootPath "..\Cli\bin\Debug\net8.0\meta-docs.exe"
}

$script:MetaDocsExe = Resolve-FullPath $MetaDocsExe

if (-not (Test-Path -LiteralPath $script:MetaDocsExe -PathType Leaf)) {
    throw "meta-docs executable was not found: $script:MetaDocsExe"
}

if (-not (Test-Path -LiteralPath $workspacesRoot -PathType Container)) {
    throw "MetaDocs workspaces directory was not found: $workspacesRoot"
}

if ($RefreshCliWorkspaces) {
    $refreshArgs = @{
        DocsRoot = $docsRootPath
        MetaDocsExe = $script:MetaDocsExe
    }
    if (-not [string]::IsNullOrWhiteSpace($MetaBiRoot)) {
        $refreshArgs.MetaBiRoot = $MetaBiRoot
    }

    & (Join-Path $docsRootPath "refresh-public-cli-workspaces.ps1") @refreshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Refreshing public CLI MetaDocs workspaces failed with exit code $LASTEXITCODE."
    }
}

$excludedWorkspaceNames = @(
    "metametabi-authored",
    "metametabi-safe-instances"
)

$workspaceDirectories = @(Get-ChildItem -LiteralPath $workspacesRoot -Directory |
    Where-Object {
        $IncludePrivateWorkspaces -or
            ($excludedWorkspaceNames -notcontains $_.Name)
    } |
    Sort-Object Name)

if ($workspaceDirectories.Count -eq 0) {
    throw "No MetaDocs source workspaces were found under '$workspacesRoot'."
}

$includeArgs = @()
foreach ($workspaceDirectory in $workspaceDirectories) {
    $workspaceXml = Join-Path $workspaceDirectory.FullName "workspace.xml"
    $modelXml = Join-Path $workspaceDirectory.FullName "model.xml"
    if (-not (Test-Path -LiteralPath $workspaceXml -PathType Leaf) -or
        -not (Test-Path -LiteralPath $modelXml -PathType Leaf)) {
        throw "MetaDocs workspace '$($workspaceDirectory.FullName)' is missing workspace.xml or model.xml."
    }

    $includeArgs += @("--include", $workspaceDirectory.FullName)
}

Invoke-Checked "Merging $($workspaceDirectories.Count) MetaDocs workspace(s)" (
    @("merge") + $includeArgs + @("--new-workspace", $suiteWorkspace))

$validateArgs = @("validate", "--workspace", $suiteWorkspace)
if ($IncludeProseDiagnostics) {
    $validateArgs += "--include-prose-diagnostics"
}

if ($WarningsAsErrors) {
    $validateArgs += "--warnings-as-errors"
}

Invoke-Checked "Validating public MetaDocs suite" $validateArgs
Invoke-Checked "Rendering public MetaDocs site" @("render-site", "--workspace", $suiteWorkspace, "--out", $siteOutput)

Write-Host "Built public MetaDocs suite:"
Write-Host "  Workspace: $suiteWorkspace"
Write-Host "  Site: $siteOutput"
