# Configures (first time) and builds the port with clang-cl and Ninja, either
# inside the Visual Studio x64 environment or, without Visual Studio, against
# an xwin /winsysroot (-WinSysRoot). -Jobs is capped by RAM: a lifted unit
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
  [string[]]$Define = @(),
  # A /winsysroot folder made by xwin: Microsoft's CRT and Windows SDK without
  # Visual Studio. Default: deps\winsysroot, when it exists and no Visual
  # Studio with the C++ tools is installed.
  [string]$WinSysRoot = ''
)
$ErrorActionPreference = 'Stop'
# The build runs Python too (gen_hle_nids.py): the same locale-independent
# text files and pipes as relift.ps1.
$env:PYTHONUTF8 = '1'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
# -products *: a Build Tools install (what the launcher installs) is not a
# "product" to vswhere by default, and the plain query finds nothing.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = if (Test-Path $vswhere) {
  & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
} else { '' }
# vswhere that finds no C++ tools (a Visual Studio install without the C++
# workload) prints nothing, and $vs is $null: Join-Path would stop the build
# before the winsysroot branch below gets its chance (issue #10).
$vcvars = $null; $ninjaDir = $null; $vsComplete = $false
if ($vs) {
  $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
  $ninjaDir = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
  $vsComplete = (Test-Path $vcvars) -and (Test-Path (Join-Path $ninjaDir 'ninja.exe'))
}
$llvm = 'C:\Program Files\LLVM\bin'
$py = Join-Path $root '.venv\Scripts\python.exe'
$ps3recomp = Join-Path $root 'work\ps3recomp'
if (-not (Test-Path $ps3recomp)) {
  & (Join-Path $PSScriptRoot 'setup_ps3recomp.ps1')
  if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

$configure = $Reconfigure -or $FFmpegInclude -or $Define -or -not (Test-Path (Join-Path $build 'build.ninja'))
# SDL2 for controllers: deps\sdl2 (the include folder and lib\x64\SDL2.dll of
# SDL2-devel-<version>-VC.zip, which the launcher downloads). The build needs
# only the headers; SDL2.dll goes next to the exe, which loads it at run time
# and falls back to XInput without it.
$sdl2 = Join-Path $root 'deps\sdl2'
$sdlArgs = @()
if (Test-Path (Join-Path $sdl2 'include\SDL.h')) {
  $sdlArgs = @('-DPS3RECOMP_SDL2_RUNTIME=ON', "-DPS3RECOMP_SDL2_INCLUDE_DIR=$(Join-Path $sdl2 'include')")
  $cache = Join-Path $build 'CMakeCache.txt'
  if (-not (Test-Path $cache) -or -not (Select-String -Path $cache -Pattern 'PS3RECOMP_SDL2_RUNTIME:BOOL=ON' -Quiet)) { $configure = $true }
}
function Copy-Sdl2 {
  $dll = Join-Path $sdl2 'lib\x64\SDL2.dll'
  if (Test-Path $dll) { Copy-Item $dll (Join-Path $build 'SDL2.dll') -Force }
}
$sysroot = Join-Path $root 'deps\winsysroot'
# The launcher's rule (Tools.VisualStudio in launcher/Runner.cs): a Visual
# Studio without vcvars64.bat or its own Ninja does not count, and Setup
# installed deps\winsysroot instead, so use that.
if (-not $WinSysRoot -and -not $vsComplete -and (Test-Path $sysroot)) { $WinSysRoot = $sysroot }
if (-not $WinSysRoot -and -not ($vcvars -and (Test-Path $vcvars))) {
  throw 'No C++ build tools: neither Visual Studio with the C++ workload nor deps\winsysroot. Run Setup in the launcher, which installs the second.'
}

if ($WinSysRoot) {
  # clang-cl and lld-link find the CRT and the SDK under /winsysroot, so
  # nothing of Visual Studio is involved. CFLAGS, CXXFLAGS and LDFLAGS seed
  # CMake's flags on top of its defaults at the first configure. CMake's
  # compiler checks build as Release: xwin leaves out the debug CRT
  # (msvcrtd.lib), and a Debug check fails to link.
  $ninja = (Get-Command ninja.exe -ErrorAction SilentlyContinue).Source
  if (-not $ninja) { $ninja = Join-Path $root 'deps\ninja.exe' }
  $cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue).Source
  if (-not $cmake) { $cmake = 'C:\Program Files\CMake\bin\cmake.exe' }
  $env:PATH = "$llvm;$env:PATH"
  $env:CFLAGS = "/winsysroot `"$WinSysRoot`""
  $env:CXXFLAGS = $env:CFLAGS
  $env:LDFLAGS = "/winsysroot:`"$WinSysRoot`""
  if ($configure) {
    $a = @('-S', $root, '-B', $build, '-G', 'Ninja', "-DCMAKE_BUILD_TYPE=$Config",
           '-DCMAKE_C_COMPILER=clang-cl', '-DCMAKE_CXX_COMPILER=clang-cl', '-DCMAKE_LINKER=lld-link',
           '-DCMAKE_RC_COMPILER=llvm-rc', '-DCMAKE_MT=llvm-mt', '-DCMAKE_TRY_COMPILE_CONFIGURATION=Release',
           "-DCMAKE_MAKE_PROGRAM=$ninja", "-DPython3_EXECUTABLE=$py", "-DPS3RECOMP_DIR=$ps3recomp")
    if ($FFmpegInclude) { $a += '-DPS3RECOMP_FFMPEG_RUNTIME=ON', "-DPS3RECOMP_FFMPEG_INCLUDE_DIR=$FFmpegInclude" }
    foreach ($d in $Define) { $a += "-D$d" }
    $a += $sdlArgs
    & $cmake @a
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
  }
  & $cmake --build $build -j $Jobs
  $rc = $LASTEXITCODE
  if (-not $rc) { Copy-Sdl2 }
  exit $rc
}

$steps = @()
$cacheArgs = ''
if ($FFmpegInclude) {
  $cacheArgs = " -DPS3RECOMP_FFMPEG_RUNTIME=ON -DPS3RECOMP_FFMPEG_INCLUDE_DIR=`"$FFmpegInclude`""
}
foreach ($d in $Define) { $cacheArgs += " -D$d" }
foreach ($d in $sdlArgs) { $cacheArgs += " `"$d`"" }
if ($configure) {
  $steps += "cmake -S `"$root`" -B `"$build`" -G Ninja -DCMAKE_BUILD_TYPE=$Config -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl -DPython3_EXECUTABLE=`"$py`" -DPS3RECOMP_DIR=`"$ps3recomp`"$cacheArgs"
}
$steps += "cmake --build `"$build`" -j $Jobs"
$cmd = "`"$vcvars`" >nul 2>&1 && set `"PATH=$llvm;$ninjaDir;%PATH%`" && " + ($steps -join ' && ')
cmd /c $cmd
$rc = $LASTEXITCODE
if (-not $rc) { Copy-Sdl2 }
exit $rc
