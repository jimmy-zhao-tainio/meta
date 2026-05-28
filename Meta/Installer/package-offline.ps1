param(
    [switch] $ReadyToRun,
    [switch] $SelfContained,
    [switch] $SingleFile
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$publishRoot = Join-Path $repoRoot 'Meta\Installer\bin\publish'
$packageFlavor = if ($SelfContained) {
    if ($SingleFile) { 'self-contained-singlefile' } else { 'self-contained-shared' }
}
else {
    if ($SingleFile) { 'framework-dependent-singlefile' } else { 'framework-dependent-shared' }
}
$outDir = Join-Path $publishRoot "win-x64-$packageFlavor"
$payloadDir = Join-Path $outDir 'payload\meta\bin'
$packageDate = Get-Date -Format 'yyyy-MM-dd'
$zipPath = Join-Path $publishRoot "meta-offline-win-x64-$packageDate-$packageFlavor.zip"
$publishReadyToRun = if ($ReadyToRun) { 'true' } else { 'false' }
$publishSelfContained = if ($SelfContained) { 'true' } else { 'false' }
$payloadPublishSingleFile = if ($SingleFile) { 'true' } else { 'false' }
$installerPublishSingleFile = 'true'
$includeNativeLibrariesForSelfExtract = if ($SingleFile -or $SelfContained) { 'true' } else { 'false' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Add-ZipFile {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)][string] $SourcePath,
        [Parameter(Mandatory = $true)][string] $EntryName
    )

    $normalizedEntryName = $EntryName.Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $Archive,
        $SourcePath,
        $normalizedEntryName,
        [System.IO.Compression.CompressionLevel]::Fastest) | Out-Null
}

function Add-ZipDirectory {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)][string] $SourceDirectory,
        [Parameter(Mandatory = $true)][string] $EntryPrefix
    )

    $root = (Resolve-Path $SourceDirectory).Path.TrimEnd('\', '/')
    foreach ($file in [System.IO.Directory]::EnumerateFiles($root, '*', [System.IO.SearchOption]::AllDirectories) | Sort-Object) {
        $relativePath = $file.Substring($root.Length).TrimStart('\', '/')
        Add-ZipFile -Archive $Archive -SourcePath $file -EntryName (Join-Path $EntryPrefix $relativePath)
    }
}

function Remove-DirectoryUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $rootPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside expected root. Path: $fullPath Root: $fullRoot"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function New-OfflineZip {
    param(
        [Parameter(Mandatory = $true)][string] $DestinationPath,
        [Parameter(Mandatory = $true)][string] $InstallerPath,
        [Parameter(Mandatory = $true)][string] $PayloadPath
    )

    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Add-ZipFile -Archive $archive -SourcePath $InstallerPath -EntryName (Split-Path $InstallerPath -Leaf)
        Add-ZipDirectory -Archive $archive -SourceDirectory $PayloadPath -EntryPrefix 'payload'
    }
    finally {
        $archive.Dispose()
    }
}

Remove-DirectoryUnderRoot -Path $outDir -Root $publishRoot
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

Write-Host "PackageFlavor: $packageFlavor"
Write-Host "PublishSelfContained: $publishSelfContained"
Write-Host "PublishSingleFile payload: $payloadPublishSingleFile"
Write-Host "PublishReadyToRun: $publishReadyToRun"

Write-Host 'Publishing install-meta.exe...'
dotnet publish (Join-Path $repoRoot 'Meta\Installer\Meta.Installer.csproj') -c Release -r win-x64 --self-contained $publishSelfContained -p:UseAppHost=true -p:PublishSingleFile=$installerPublishSingleFile -p:IncludeNativeLibrariesForSelfExtract=$includeNativeLibrariesForSelfExtract -p:PublishReadyToRun=$publishReadyToRun -p:UpdateInstallMetaPublishDir=false -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Publishing meta.exe payload...'
dotnet publish (Join-Path $repoRoot 'Meta\Cli\Meta.Cli.csproj') -c Release -r win-x64 --self-contained $publishSelfContained -p:UseAppHost=true -p:PublishSingleFile=$payloadPublishSingleFile -p:IncludeNativeLibrariesForSelfExtract=$includeNativeLibrariesForSelfExtract -p:PublishReadyToRun=$publishReadyToRun -p:UpdateMetaPublishDir=false -o $payloadDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Publishing meta-weave.exe payload...'
dotnet publish (Join-Path $repoRoot 'MetaWeave\Cli\MetaWeave.Cli.csproj') -c Release -r win-x64 --self-contained $publishSelfContained -p:UseAppHost=true -p:PublishSingleFile=$payloadPublishSingleFile -p:IncludeNativeLibrariesForSelfExtract=$includeNativeLibrariesForSelfExtract -p:PublishReadyToRun=$publishReadyToRun -p:UpdateMetaWeavePublishDir=false -o $payloadDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Removing debug symbol files (*.pdb) from release payload...'
Get-ChildItem -LiteralPath $outDir -Recurse -Filter '*.pdb' -File | Remove-Item -Force

Write-Host 'Removing old local zip packages...'
Get-ChildItem -LiteralPath $publishRoot -Filter 'meta-offline-win-x64-*.zip' -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host 'Creating zipped offline package...'
$items = @(
    (Join-Path $outDir 'install-meta.exe'),
    (Join-Path $outDir 'payload')
)
foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath $item)) {
        throw "Missing release item: $item"
    }
}
New-OfflineZip `
    -DestinationPath $zipPath `
    -InstallerPath (Join-Path $outDir 'install-meta.exe') `
    -PayloadPath (Join-Path $outDir 'payload')

Write-Host ''
Write-Host 'Offline package ready:'
Write-Host "  $outDir"
Write-Host 'Zipped release:'
Write-Host "  $zipPath"
Write-Host ''
Write-Host 'Required layout:'
Write-Host '  install-meta.exe'
Write-Host '  payload\meta\bin\...'
if (-not $SelfContained) {
    Write-Host ''
    Write-Host 'Requires .NET 8 runtime on the target machine.'
}
