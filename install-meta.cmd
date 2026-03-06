@echo off
setlocal

set "TARGET_DIR=%LOCALAPPDATA%\meta\bin"
set "META_EXE=%~dp0Meta.Cli\bin\publish\win-x64\meta.exe"
set "META_WEAVE_EXE=%~dp0MetaWeave.Cli\bin\publish\win-x64\meta-weave.exe"

if not exist "%META_EXE%" (
  echo Error: missing "%META_EXE%".
  echo Build Metadata.Framework.sln first so meta.exe is published.
  exit /b 1
)

if not exist "%META_WEAVE_EXE%" (
  echo Error: missing "%META_WEAVE_EXE%".
  echo Build Metadata.Framework.sln first so meta-weave.exe is published.
  exit /b 1
)

if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

copy /Y "%META_EXE%" "%TARGET_DIR%\meta.exe" >nul
copy /Y "%META_WEAVE_EXE%" "%TARGET_DIR%\meta-weave.exe" >nul

for /f "tokens=2,*" %%A in ('reg query HKCU\Environment /v Path 2^>nul ^| findstr /R /C:"Path"') do set "USER_PATH=%%B"

echo;%USER_PATH%; | find /I ";%TARGET_DIR%;" >nul
if errorlevel 1 (
  if defined USER_PATH (
    reg add HKCU\Environment /v Path /t REG_EXPAND_SZ /d "%USER_PATH%;%TARGET_DIR%" /f >nul
  ) else (
    reg add HKCU\Environment /v Path /t REG_EXPAND_SZ /d "%TARGET_DIR%" /f >nul
  )
)

echo Installed:
echo   %TARGET_DIR%\meta.exe
echo   %TARGET_DIR%\meta-weave.exe
echo.
echo Restart cmd to pick up PATH changes.
