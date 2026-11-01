# Plays the game: no time limit, no test input. Close the window to stop.
# A controller works through XInput. With none, the keyboard is the pad; the keys
# are in docs/HANDOFF.md. -FFmpeg is the folder with the FFmpeg DLLs, as in run.ps1.
param([string]$FFmpeg = '<FFmpeg folder>')
& (Join-Path $PSScriptRoot 'run.ps1') -FFmpeg $FFmpeg -Env 'PS3_VERBOSE=0'
