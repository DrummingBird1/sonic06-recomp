# Packs a release into dist\: the tracked source at HEAD (git archive, so
# nothing untracked or ignored can slip in: no game files, no work\, no build\)
# with Launcher.exe added at its root, where the launcher expects to run. Then
# checks the zip for anything that must never be published, and prints its
# SHA-256.
#
#   pwsh tools/package_release.ps1 [-Name sonic06-recomp]
param(
  [string]$Name = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (git -C $root status --porcelain --untracked-files=no) { throw 'Uncommitted changes: the archive is made from HEAD, so commit them first.' }
if (-not $Name) { $Name = Split-Path -Leaf $root }

$version = (Select-Xml -Path (Join-Path $root 'launcher\Launcher.csproj') -XPath '//Version').Node.InnerText
dotnet build (Join-Path $root 'launcher') -c Release -nologo -v q
if ($LASTEXITCODE) { throw 'The launcher did not build.' }
$exe = Join-Path $root 'launcher\bin\Release\net48\Launcher.exe'

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$base = "$Name-$version-$(git -C $root rev-parse --short HEAD)"
$zip = Join-Path $dist "$base.zip"
if (Test-Path $zip) { Remove-Item $zip }
git -C $root archive --format=zip --prefix="$base/" -o $zip HEAD
if ($LASTEXITCODE) { throw 'git archive failed.' }

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
try {
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $exe, "$base/Launcher.exe") | Out-Null
  # Nothing from the game, and nothing built from it. The patterns are the
  # project's own rules: game\, work\, build\, the ISO, ELF and SELF images,
  # frame dumps and shader dumps, and this machine's local files.
  $forbidden = '^[^/]+/(game|work|build|deps)/|\.(iso|elf|self|sprx|bin|bmp|dxbc)$|_dump\.hlsl$|\.local\.(md|json)$'
  $bad = @($z.Entries | Where-Object { $_.FullName -match $forbidden } | ForEach-Object FullName)
  $count = $z.Entries.Count
} finally { $z.Dispose() }
if ($bad) { Remove-Item $zip; throw "Refusing to package, these must not be published:`n$($bad -join "`n")" }

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$zip"
"$count files, $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MB"
"sha256 $hash"
