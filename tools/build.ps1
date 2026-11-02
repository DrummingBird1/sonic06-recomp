# Configures (first time) and builds the port with clang-cl and Ninja inside
# the Visual Studio x64 environment. -Jobs is capped by RAM: a lifted unit
# peaks around 2.2 GB in clang, so 32 GB fits about 10 at once.
param(
  [int]$Jobs = 10,
  [string]$Config = 'Release',
  [switch]$Reconfigure,
  # Folder holding libavcodec/avcodec.h. Builds the runtime's FFmpeg decode
  # path (movies); the DLLs of the same major version are found at run time
  # (run.ps1 -FFmpeg). Implies a reconfigure.
  [string]$FFmpegInclude = '',
  # Extra CMake cache entries, e.g. -Define PORT_ENABLE_SPU=ON. Implies a
  # reconfigure.
  [string[]]$Define = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
# -products *: a Build Tools install (what the launcher installs) is not a
# "product" to vswhere by default, and the plain query finds nothing.
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$ninjaDir = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
$llvm = 'C:\Program Files\LLVM\bin'
$py = Join-Path $root '.venv\Scripts\python.exe'
$ps3recomp = Join-Path $root 'work\ps3recomp'
if (-not (Test-Path $ps3recomp)) {
  & (Join-Path $PSScriptRoot 'setup_ps3recomp.ps1')
  if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

$steps = @()
$cacheArgs = ''
if ($FFmpegInclude) {
  $cacheArgs = " -DPS3RECOMP_FFMPEG_RUNTIME=ON -DPS3RECOMP_FFMPEG_INCLUDE_DIR=`"$FFmpegInclude`""
}
foreach ($d in $Define) { $cacheArgs += " -D$d" }
if ($Reconfigure -or $FFmpegInclude -or $Define -or -not (Test-Path (Join-Path $build 'build.ninja'))) {
  $steps += "cmake -S `"$root`" -B `"$build`" -G Ninja -DCMAKE_BUILD_TYPE=$Config -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl -DPython3_EXECUTABLE=`"$py`" -DPS3RECOMP_DIR=`"$ps3recomp`"$cacheArgs"
}
$steps += "cmake --build `"$build`" -j $Jobs"
$cmd = "`"$vcvars`" >nul 2>&1 && set `"PATH=$llvm;$ninjaDir;%PATH%`" && " + ($steps -join ' && ')
cmd /c $cmd
exit $LASTEXITCODE
