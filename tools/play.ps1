# Plays the game: no time limit, no test input. Close the window to stop.
# A controller works through SDL2 (XInput without build\SDL2.dll). With none, the
# keyboard is the pad; the keys are in docs/HANDOFF.md. The launcher finds FFmpeg; -FFmpeg names its folder.
param([string]$FFmpeg = '')
& (Join-Path $PSScriptRoot 'launcher.ps1') -FFmpeg $FFmpeg
