# Packs a release into dist\: one file, Soleanna-<version>.exe. It is the launcher
# with the source the build needs inside it (payload.zip), which it unpacks into a
# folder the player picks (launcher\Installer.cs). The payload is an allow-list
# of files at HEAD (git archive, so nothing untracked or ignored can slip in): the
# build scripts, src\, patches\, CMakeLists.txt, the license and the changelog. No
# documents, no agent notes, no CI files, and nothing from the game. Then checks
# the payload against the list and the project's rules, and prints the SHA-256.
#
#   pwsh tools/package_release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (git -C $root status --porcelain --untracked-files=no) { throw 'Uncommitted changes: the payload is made from HEAD, so commit them first.' }

# What a player's install needs: the launcher runs build.ps1, relift.ps1,
# setup_ps3recomp.ps1 and run.ps1, relift runs patch_lift.py, and Setup installs
# tools\re\requirements.txt into the Python environment.
$files = @('CMakeLists.txt', 'LICENSE', 'CHANGELOG.md', 'src', 'patches',
           'tools/build.ps1', 'tools/relift.ps1', 'tools/setup_ps3recomp.ps1', 'tools/run.ps1',
           'tools/patch_lift.py', 'tools/re/requirements.txt')
$allowed = '^(CMakeLists\.txt|LICENSE|CHANGELOG\.md|src/.+|patches/ps3recomp/.+\.patch|tools/(build|relift|setup_ps3recomp|run)\.ps1|tools/patch_lift\.py|tools/re/requirements\.txt)$'
# Nothing from the game, and nothing built from it: the project's own rules.
$forbidden = '^(game|work|build|deps|dist)/|\.(iso|elf|self|sprx|bin|pkg|pup|bmp|dxbc)$|_dump\.hlsl$|\.local\.(md|json)$'

$version = (Select-Xml -Path (Join-Path $root 'launcher\Launcher.csproj') -XPath '//Version').Node.InnerText
$hash = git -C $root rev-parse --short HEAD
$payload = Join-Path $root 'launcher\payload'
if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
New-Item -ItemType Directory -Force $payload | Out-Null
try {
  $zip = Join-Path $payload 'payload.zip'
  git -C $root archive --format=zip -o $zip HEAD -- @files
  if ($LASTEXITCODE) { throw 'git archive failed.' }
  Set-Content -NoNewline -Encoding ascii (Join-Path $payload 'payload.version') "$version ($hash)"

  Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
  $z = [System.IO.Compression.ZipFile]::OpenRead($zip)
  try {
    $entries = @($z.Entries | Where-Object { $_.Name } | ForEach-Object FullName)
  } finally { $z.Dispose() }
  $bad = @($entries | Where-Object { $_ -match $forbidden -or $_ -notmatch $allowed })
  if ($bad) { throw "Refusing to package, these must not be in the release:`n$($bad -join "`n")" }

  # The launcher again, with the payload inside (Launcher.csproj embeds payload\ when it exists).
  dotnet build (Join-Path $root 'launcher') -c Release -nologo -v q --no-incremental
  if ($LASTEXITCODE) { throw 'The launcher did not build.' }
  $dist = Join-Path $root 'dist'
  New-Item -ItemType Directory -Force $dist | Out-Null
  $exe = Join-Path $dist "Soleanna-$version.exe"
  Copy-Item -Force (Join-Path $root 'launcher\bin\Release\net48\Launcher.exe') $exe
} finally {
  if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
}
# And once more without it, so the checkout's own Launcher.exe stays the developer build.
dotnet build (Join-Path $root 'launcher') -c Release -nologo -v q --no-incremental
if ($LASTEXITCODE) { throw 'The launcher did not build without the payload.' }

"$exe"
"$($entries.Count) files inside, version $version ($hash), $([math]::Round((Get-Item $exe).Length / 1MB, 2)) MB"
"sha256 $((Get-FileHash $exe -Algorithm SHA256).Hash.ToLower())"
