@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%\..\.."
set "REPO_ROOT=%CD%"

set "OUT_DIR=%REPO_ROOT%\Meta\Installer\bin\publish\win-x64"
set "PAYLOAD_DIR=%OUT_DIR%\payload\meta\bin"

if exist "%OUT_DIR%\payload" rmdir /s /q "%OUT_DIR%\payload"
mkdir "%PAYLOAD_DIR%" >nul 2>&1

echo Publishing install-meta.exe...
dotnet publish "%REPO_ROOT%\Meta\Installer\Meta.Installer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateInstallMetaPublishDir=false -o "%OUT_DIR%"
if errorlevel 1 goto :fail

echo Publishing meta.exe payload...
dotnet publish "%REPO_ROOT%\Meta\Cli\Meta.Cli.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateMetaPublishDir=false -o "%PAYLOAD_DIR%"
if errorlevel 1 goto :fail

echo Publishing meta-weave.exe payload...
dotnet publish "%REPO_ROOT%\MetaWeave\Cli\MetaWeave.Cli.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateMetaWeavePublishDir=false -o "%PAYLOAD_DIR%"
if errorlevel 1 goto :fail

echo Removing debug symbol files (*.pdb) from release payload...
for /r "%OUT_DIR%" %%F in (*.pdb) do del /q "%%F"

echo.
echo Offline package ready:
echo   %OUT_DIR%
echo.
echo Required layout:
echo   install-meta.exe
echo   payload\meta\bin\...
popd
exit /b 0

:fail
echo Packaging failed.
popd
exit /b 1
