# Runs the port against the extracted disc in game\disc and tees the output
# to work\logs\run-<time>.log. Extra environment knobs (see the toolkit's
# docs/DIAGNOSTICS.md) can be passed as -Env 'RSX_LIVE_DRAW=1;PS3_HLE_TRACE=1'.
param(
  [string]$Env = '',
  [int]$Seconds = 0,   # stop the run after this many seconds; 0 = run until it exits
  # Folder with the FFmpeg DLLs (avcodec-NN.dll, avutil-NN.dll and what they
  # load) matching the headers of build.ps1 -FFmpegInclude. Without it the
  # movies decode to black pictures.
  [string]$FFmpeg = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'build\sonic06.exe'
$elf = Join-Path $root 'game\EBOOT.elf'
$logs = Join-Path $root 'work\logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs ("run-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))

$env:PS3_VFS_ROOT = Join-Path $root 'game\disc'
$env:PS3_TITLE = 'Sonic the Hedgehog'
if ($FFmpeg) { $env:PATH = "$FFmpeg;$env:PATH" }
foreach ($kv in ($Env -split ';' | Where-Object { $_ })) {
  $k, $v = $kv -split '=', 2
  Set-Item "env:$k" $v
}

$p = Start-Process -FilePath $exe -ArgumentList "`"$elf`"" -WorkingDirectory $root `
      -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err" -PassThru -NoNewWindow
if ($Seconds -gt 0) {
  if (-not $p.WaitForExit($Seconds * 1000)) {
    Stop-Process -Id $p.Id -Force
    $p.WaitForExit(10000) | Out-Null
    "stopped after $Seconds s"
  }
} else { $p.WaitForExit() }
"exit code: $($p.ExitCode)"
Get-Content "$log.out", "$log.err" | Set-Content $log
Remove-Item "$log.out", "$log.err"
"log: $log"
