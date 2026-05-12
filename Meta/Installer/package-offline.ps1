$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$publishRoot = Join-Path $repoRoot 'Meta\Installer\bin\publish'
$outDir = Join-Path $publishRoot 'win-x64'
$payloadDir = Join-Path $outDir 'payload\meta\bin'
$packageDate = Get-Date -Format 'yyyy-MM-dd'
$zipPath = Join-Path $publishRoot "meta-offline-win-x64-$packageDate.zip"

if (Test-Path -LiteralPath (Join-Path $outDir 'payload')) {
    Remove-Item -LiteralPath (Join-Path $outDir 'payload') -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

Write-Host 'Publishing install-meta.exe...'
dotnet publish (Join-Path $repoRoot 'Meta\Installer\Meta.Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateInstallMetaPublishDir=false -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Publishing meta.exe payload...'
dotnet publish (Join-Path $repoRoot 'Meta\Cli\Meta.Cli.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateMetaPublishDir=false -o $payloadDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Publishing meta-weave.exe payload...'
dotnet publish (Join-Path $repoRoot 'MetaWeave\Cli\MetaWeave.Cli.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateMetaWeavePublishDir=false -o $payloadDir
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
Compress-Archive -LiteralPath $items -DestinationPath $zipPath -Force

Write-Host ''
Write-Host 'Offline package ready:'
Write-Host "  $outDir"
Write-Host 'Zipped release:'
Write-Host "  $zipPath"
Write-Host ''
Write-Host 'Required layout:'
Write-Host '  install-meta.exe'
Write-Host '  payload\meta\bin\...'
