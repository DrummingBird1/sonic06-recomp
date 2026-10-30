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
  [string]$FFmpegInclude = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$ninjaDir = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
$llvm = 'C:\Program Files\LLVM\bin'
$py = Join-Path $root '.venv\Scripts\python.exe'

$steps = @()
$ffmpeg = ''
if ($FFmpegInclude) {
  $ffmpeg = " -DPS3RECOMP_FFMPEG_RUNTIME=ON -DPS3RECOMP_FFMPEG_INCLUDE_DIR=`"$FFmpegInclude`""
}
if ($Reconfigure -or $FFmpegInclude -or -not (Test-Path (Join-Path $build 'build.ninja'))) {
  $steps += "cmake -S `"$root`" -B `"$build`" -G Ninja -DCMAKE_BUILD_TYPE=$Config -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl -DPython3_EXECUTABLE=`"$py`"$ffmpeg"
}
$steps += "cmake --build `"$build`" -j $Jobs"
$cmd = "`"$vcvars`" >nul 2>&1 && set `"PATH=$llvm;$ninjaDir;%PATH%`" && " + ($steps -join ' && ')
cmd /c $cmd
exit $LASTEXITCODE
