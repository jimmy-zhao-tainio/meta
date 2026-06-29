[CmdletBinding()]
param(
    [string] $DocsRoot = $PSScriptRoot,
    [string] $MetaRepoRoot,
    [string] $MetaBiRoot,
    [string] $MetaDocsExe
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

function New-CliSource {
    param(
        [Parameter(Mandatory = $true)][string] $Executable,
        [Parameter(Mandatory = $true)][string] $SourceWorkspace,
        [Parameter(Mandatory = $true)][string] $DocsWorkspace,
        [Parameter(Mandatory = $true)][string] $Group,
        [Parameter(Mandatory = $true)][int] $Ordinal
    )

    [pscustomobject]@{
        Executable = $Executable
        SourceWorkspace = $SourceWorkspace
        DocsWorkspace = $DocsWorkspace
        Group = $Group
        Ordinal = $Ordinal
        SourceId = "source:cli:$Executable"
    }
}

function Invoke-ImportCli {
    param([Parameter(Mandatory = $true)] $Source)

    $sourceWorkspace = Resolve-FullPath $Source.SourceWorkspace
    $docsWorkspace = Resolve-FullPath (Join-Path $script:WorkspacesRoot $Source.DocsWorkspace)
    $workspaceXml = Join-Path $docsWorkspace "workspace.xml"
    $targetSwitch = if (Test-Path -LiteralPath $workspaceXml -PathType Leaf) { "--workspace" } else { "--new-workspace" }

    $arguments = @(
        "import-cli",
        "--source-workspace", $sourceWorkspace,
        $targetSwitch, $docsWorkspace,
        "--group", $Source.Group,
        "--ordinal", $Source.Ordinal.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--source-id", $Source.SourceId
    )

    Write-Host "Importing $($Source.Executable) from $sourceWorkspace"
    & $script:MetaDocsExe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Importing $($Source.Executable) failed with exit code $LASTEXITCODE."
    }
}

$docsRootPath = Resolve-FullPath $DocsRoot
if ([string]::IsNullOrWhiteSpace($MetaRepoRoot)) {
    $MetaRepoRoot = Resolve-FullPath (Join-Path $docsRootPath "..\..")
}
else {
    $MetaRepoRoot = Resolve-FullPath $MetaRepoRoot
}

if ([string]::IsNullOrWhiteSpace($MetaBiRoot)) {
    $MetaBiRoot = Resolve-FullPath (Join-Path $MetaRepoRoot "..\meta-bi")
}
else {
    $MetaBiRoot = Resolve-FullPath $MetaBiRoot
}

if ([string]::IsNullOrWhiteSpace($MetaDocsExe)) {
    $MetaDocsExe = Join-Path $docsRootPath "..\Cli\bin\Debug\net8.0\meta-docs.exe"
}

$script:MetaDocsExe = Resolve-FullPath $MetaDocsExe
$script:WorkspacesRoot = Resolve-FullPath (Join-Path $docsRootPath "Workspaces")

if (-not (Test-Path -LiteralPath $script:MetaDocsExe -PathType Leaf)) {
    throw "meta-docs executable was not found: $script:MetaDocsExe"
}

if (-not (Test-Path -LiteralPath $script:WorkspacesRoot -PathType Container)) {
    throw "MetaDocs workspaces directory was not found: $script:WorkspacesRoot"
}

$sources = @(
    New-CliSource "meta" (Join-Path $MetaRepoRoot "Meta\Cli\meta.MetaCli") "meta-cli" "meta" 10
    New-CliSource "meta-cli" (Join-Path $MetaRepoRoot "MetaCli\Cli\meta-cli.MetaCli") "meta-cli-cli" "meta" 20
    New-CliSource "meta-docs" (Join-Path $MetaRepoRoot "MetaDocs\Cli\meta-docs.MetaCli") "meta-docs-cli" "meta" 30
    New-CliSource "meta-mesh" (Join-Path $MetaRepoRoot "MetaMesh\Cli\meta-mesh.MetaCli") "meta-mesh-cli" "meta" 40
    New-CliSource "meta-weave" (Join-Path $MetaRepoRoot "MetaWeave\Cli\meta-weave.MetaCli") "meta-weave-cli" "meta" 50
    New-CliSource "meta-schema" (Join-Path $MetaBiRoot "MetaSchema\Cli\meta-schema.MetaCli") "meta-schema-cli" "meta-bi" 110
    New-CliSource "meta-data-type" (Join-Path $MetaBiRoot "MetaDataType\Cli\meta-data-type.MetaCli") "meta-data-type-cli" "meta-bi" 120
    New-CliSource "meta-data-type-conversion" (Join-Path $MetaBiRoot "MetaDataTypeConversion\Cli\meta-data-type-conversion.MetaCli") "meta-data-type-conversion-cli" "meta-bi" 130
    New-CliSource "meta-sql" (Join-Path $MetaBiRoot "MetaSql\Cli\meta-sql.MetaCli") "meta-sql-cli" "meta-bi" 140
    New-CliSource "meta-transform-script" (Join-Path $MetaBiRoot "MetaTransform\Script\Cli\meta-transform-script.MetaCli") "meta-transform-script-cli" "meta-bi" 150
    New-CliSource "meta-transform-binding" (Join-Path $MetaBiRoot "MetaTransform\Binding\Cli\meta-transform-binding.MetaCli") "meta-transform-binding-cli" "meta-bi" 160
    New-CliSource "meta-data-quality" (Join-Path $MetaBiRoot "MetaDataQuality\Cli\meta-data-quality.MetaCli") "meta-data-quality-cli" "meta-bi" 170
    New-CliSource "meta-convert" (Join-Path $MetaBiRoot "MetaConvert\Cli\meta-convert.MetaCli") "meta-convert-cli" "meta-bi" 180
    New-CliSource "meta-analytics" (Join-Path $MetaBiRoot "MetaAnalytics\Cli\meta-analytics.MetaCli") "meta-analytics-cli" "meta-bi" 190
    New-CliSource "meta-data-warehouse" (Join-Path $MetaBiRoot "MetaDataWarehouse\Cli\meta-data-warehouse.MetaCli") "meta-data-warehouse-cli" "meta-bi" 200
    New-CliSource "meta-datavault-raw" (Join-Path $MetaBiRoot "MetaDataVault\Cli\Raw\meta-datavault-raw.MetaCli") "meta-datavault-raw-cli" "meta-bi" 210
    New-CliSource "meta-datavault-business" (Join-Path $MetaBiRoot "MetaDataVault\Cli\Business\meta-datavault-business.MetaCli") "meta-datavault-business-cli" "meta-bi" 220
    New-CliSource "meta-pipeline" (Join-Path $MetaBiRoot "MetaPipeline\Cli\meta-pipeline.MetaCli") "meta-pipeline-cli" "meta-bi" 230
    New-CliSource "meta-orchestration" (Join-Path $MetaBiRoot "MetaOrchestration\Cli\meta-orchestration.MetaCli") "meta-orchestration-cli" "meta-bi" 240
    New-CliSource "meta-tabular" (Join-Path $MetaBiRoot "MetaTabular\Cli\meta-tabular.MetaCli") "meta-tabular-cli" "meta-bi" 250
    New-CliSource "meta-multi-dimensional" (Join-Path $MetaBiRoot "MetaMultiDimensional\Cli\meta-multi-dimensional.MetaCli") "meta-multi-dimensional-cli" "meta-bi" 260
)

foreach ($source in $sources) {
    Invoke-ImportCli $source
}

Write-Host "Refreshed $($sources.Count) public CLI MetaDocs workspace(s)."
