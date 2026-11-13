# The one entry point for a player: shows what is missing, sets up from the
# player's own copy, builds, and starts the game. It ships no game data, and it
# does not decrypt anything: EBOOT.BIN goes through the player's own RPCS3.
#
#   pwsh tools/launcher.ps1                  status, then play if all is ready
#   pwsh tools/launcher.ps1 -Check           status only
#   pwsh tools/launcher.ps1 -Iso <file.iso>  extract the disc into game/
#   pwsh tools/launcher.ps1 -Build           ps3recomp, lift and build
#
# Paths the machine needs are kept in launcher.local.json, which stays out of git:
# -FFmpeg (folder with the DLLs), -FFmpegInclude, -Ps3Recomp (a clone at 4e8f3ca).
param(
  [switch]$Check,
  [switch]$Build,
  [string]$Iso = '',
  [string]$FFmpeg = '',
  [string]$FFmpegInclude = '',
  [string]$Ps3Recomp = '',
  [int]$Seconds = 0    # stop after this many seconds, for a test run
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfgPath = Join-Path $root 'launcher.local.json'
$cfg = @{ ffmpeg = ''; ffmpegInclude = ''; ps3recomp = '' }
if (Test-Path $cfgPath) {
  (Get-Content $cfgPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $cfg[$_.Name] = $_.Value }
}
if ($FFmpeg) { $cfg.ffmpeg = $FFmpeg }
if ($FFmpegInclude) { $cfg.ffmpegInclude = $FFmpegInclude }
if ($Ps3Recomp) { $cfg.ps3recomp = $Ps3Recomp }
if ($FFmpeg -or $FFmpegInclude -or $Ps3Recomp) { $cfg | ConvertTo-Json | Set-Content $cfgPath }

function Find-FFmpeg {
  $dirs = @($cfg.ffmpeg, (Join-Path $root 'deps\ffmpeg\bin')) + ($env:PATH -split ';')
  foreach ($d in $dirs) {
    if ($d -and (Test-Path $d) -and (Get-ChildItem $d -Filter 'avcodec-*.dll' -ErrorAction SilentlyContinue)) { return $d }
  }
  return ''
}

function Find-Vcvars {
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (-not (Test-Path $vswhere)) { return '' }
  $vs = & $vswhere -latest -products * -property installationPath
  # Nothing found prints nothing, and Join-Path would throw (issue #10).
  if (-not $vs) { return '' }
  $bat = Join-Path "$vs" 'VC\Auxiliary\Build\vcvars64.bat'
  if (Test-Path $bat) { return $bat }
  return ''
}

function Get-Status {
  $ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
  $ff = Find-FFmpeg
  @(
    [pscustomobject]@{ Step = 'Game disc'; Ok = Test-Path (Join-Path $root 'game\disc\PS3_GAME\USRDIR'); Fix = 'launcher.ps1 -Iso <your BLES00028 iso>' }
    [pscustomobject]@{ Step = 'Decrypted EBOOT'; Ok = Test-Path (Join-Path $root 'game\EBOOT.elf'); Fix = 'RPCS3: Utilities > Decrypt PS3 Binaries > game\EBOOT.BIN' }
    [pscustomobject]@{ Step = 'FFmpeg DLLs'; Ok = [bool]$ff; Fix = 'launcher.ps1 -FFmpeg <folder with avcodec-*.dll>' }
    [pscustomobject]@{ Step = 'Game built'; Ok = Test-Path (Join-Path $root 'build\sonic06.exe'); Fix = 'launcher.ps1 -Build' }
    [pscustomobject]@{ Step = "Memory ${ramGb} GB (build needs 32)"; Ok = $ramGb -ge 32; Fix = 'only the build needs it' }
    [pscustomobject]@{ Step = 'LLVM clang-cl'; Ok = Test-Path 'C:\Program Files\LLVM\bin\clang-cl.exe'; Fix = 'install LLVM, needed for the build' }
    [pscustomobject]@{ Step = 'Visual Studio C++'; Ok = [bool](Find-Vcvars); Fix = 'install the C++ workload, needed for the build' }
    [pscustomobject]@{ Step = 'Python environment'; Ok = Test-Path (Join-Path $root '.venv\Scripts\python.exe'); Fix = 'launcher.ps1 -Build creates it' }
  )
}

function Extract-Disc([string]$image) {
  $image = (Resolve-Path -LiteralPath $image).Path   # the usual name has [BLES00028] in it
  $mount = Mount-DiskImage -ImagePath $image -PassThru
  try {
    $drive = ($mount | Get-Volume).DriveLetter
    $sfo = "${drive}:\PS3_GAME\PARAM.SFO"
    if (-not (Test-Path $sfo)) { throw 'not a PS3 disc: no PS3_GAME\PARAM.SFO' }
    if ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($sfo)) -notmatch 'BLES00028') {
      throw 'this is not BLES00028, the only version the port was made for'
    }
    $disc = Join-Path $root 'game\disc'
    New-Item -ItemType Directory -Force $disc | Out-Null
    robocopy "${drive}:\" $disc /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }
    Copy-Item (Join-Path $disc 'PS3_GAME\USRDIR\EBOOT.BIN') (Join-Path $root 'game\EBOOT.BIN') -Force
    "Disc extracted. Decrypt game\EBOOT.BIN in RPCS3 next; the output is game\EBOOT.elf."
  } finally {
    Dismount-DiskImage -ImagePath $image | Out-Null
  }
}

function Build-Game {
  $tools = Join-Path $root 'tools'
  if (-not (Test-Path (Join-Path $root 'work\ps3recomp'))) {
    $src = if ($cfg.ps3recomp) { @{ Source = $cfg.ps3recomp } } else { @{} }
    & (Join-Path $tools 'setup_ps3recomp.ps1') @src
  }
  $py = Join-Path $root '.venv\Scripts\python.exe'
  if (-not (Test-Path $py)) {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'Install uv: https://github.com/astral-sh/uv' }
    Push-Location $root
    try {
      uv venv --python 3.12 .venv
      uv pip install --python $py -r work\ps3recomp\requirements.txt -r tools\re\requirements.txt
    } finally { Pop-Location }
  }
  if (-not (Test-Path (Join-Path $root 'work\recompiled'))) { & (Join-Path $tools 'relift.ps1') }
  $inc = if ($cfg.ffmpegInclude) { @{ FFmpegInclude = $cfg.ffmpegInclude } } else { @{} }
  & (Join-Path $tools 'build.ps1') @inc
}

if ($Iso) { Extract-Disc $Iso; return }
if ($Build) { Build-Game; return }

$status = Get-Status
$status | Format-Table Step, Ok, @{ n = 'To fix'; e = { if ($_.Ok) { '' } else { $_.Fix } } } -AutoSize | Out-String | Write-Output
if ($Check) { return }
$missing = $status | Select-Object -First 4 | Where-Object { -not $_.Ok }
if ($missing) { throw "Not ready: $(($missing.Step) -join ', ')" }
& (Join-Path $PSScriptRoot 'run.ps1') -FFmpeg (Find-FFmpeg) -Seconds $Seconds -Env 'PS3_VERBOSE=0;GCM_FLIPCOUNT=1'
