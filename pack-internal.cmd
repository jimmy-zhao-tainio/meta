@echo off
setlocal
set "PACKAGE_OUTPUT=%~1"
if "%PACKAGE_OUTPUT%"=="" set "PACKAGE_OUTPUT=%~dp0.nupkg"
if exist "%PACKAGE_OUTPUT%" rmdir /s /q "%PACKAGE_OUTPUT%"
mkdir "%PACKAGE_OUTPUT%"

call :pack Meta\Operations\Meta.Operations.csproj || exit /b %errorlevel%
call :pack Meta\TypedModels\Meta.TypedModels.csproj || exit /b %errorlevel%
call :pack Meta\Core\Meta.Core.csproj || exit /b %errorlevel%
call :pack Meta\Surfaces\Meta.Surfaces.csproj || exit /b %errorlevel%
call :pack Meta\Surfaces.Xml\Meta.Surfaces.Xml.csproj || exit /b %errorlevel%
call :pack Meta\Surfaces.CSharp\Meta.Surfaces.CSharp.csproj || exit /b %errorlevel%
call :pack Meta\Surfaces.Sql\Meta.Surfaces.Sql.csproj || exit /b %errorlevel%
call :pack Meta\Integration\Meta.Integration.csproj || exit /b %errorlevel%
call :pack MetaCli\MetaCli.Model.csproj || exit /b %errorlevel%
call :pack MetaCli\Core\MetaCli.Core.csproj || exit /b %errorlevel%
call :pack MetaWeave\MetaWeave.Model.csproj || exit /b %errorlevel%
call :pack MetaWeave\Script\Execution\MetaWeaveScript.Execution.csproj || exit /b %errorlevel%
call :pack MetaWeave\Script\Sql\MetaWeaveScript.Sql.csproj || exit /b %errorlevel%
call :pack MetaWeave\Core\MetaWeave.Core.csproj || exit /b %errorlevel%
exit /b 0

:pack
echo Packing %~1
dotnet pack "%~1" --configuration Release --output "%PACKAGE_OUTPUT%" --nologo
exit /b %errorlevel%
