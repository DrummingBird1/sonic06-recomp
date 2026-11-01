@echo off
rem Starts the launcher window. See tools/launcher.ps1.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\launcher_gui.ps1"
