@echo off
rem Starts the launcher: Launcher.exe next to this file in a release, or the one
rem built from launcher\ in a checkout (built here if the .NET SDK is installed).
set "L=%~dp0Launcher.exe"
if not exist "%L%" set "L=%~dp0launcher\bin\Release\net48\Launcher.exe"
if not exist "%L%" (
  where dotnet >nul 2>&1 || (echo The launcher is not built: install the .NET SDK, or use a release. & pause & exit /b 1)
  dotnet build "%~dp0launcher" -c Release || (pause & exit /b 1)
)
start "" "%L%"
