# Makes work\ps3recomp: the shared ps3recomp clone at the pinned commit with
# this project's patches (patches\ps3recomp\*.patch) applied on top. The build
# and the lifter use this copy. The shared clone is only read: the copy
# borrows its objects (git clone --shared) and nothing is written back.
#
# Changing the toolkit: edit and commit inside work\ps3recomp, then run this
# script with -Export to rewrite patches\ps3recomp from those commits. -Reset
# rebuilds the copy from the pinned commit and the patch files.
param(
  [string]$Source = '<local>/shared\toolchains\ps3recomp',
  [string]$Commit = '4e8f3ca87a3e1d87bb83b8b7e9348fe2685cd514',
  [switch]$Reset,
  [switch]$Export
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = Join-Path $root 'work\ps3recomp'
$patches = Join-Path $root 'patches\ps3recomp'

# git inside the copy. Not named Git: PowerShell names are case-insensitive,
# and such a function would shadow git.exe and call itself.
function CopyGit { git.exe -C $copy @args; if ($LASTEXITCODE) { throw "git $args failed" } }

if ($Export) {
  if (-not (Test-Path $copy)) { throw "No copy at $copy to export from." }
  if (CopyGit status --porcelain) { throw "Uncommitted changes in $copy. Commit them first." }
  New-Item -ItemType Directory -Force $patches | Out-Null
  Get-ChildItem $patches -Filter *.patch | Remove-Item
  CopyGit format-patch --quiet --no-numbered --no-signature --zero-commit -o $patches $Commit
  "exported $((Get-ChildItem $patches -Filter *.patch).Count) patch(es) to $patches"
  exit 0
}

if ((Test-Path $copy) -and -not $Reset) {
  "copy exists: $copy at $(CopyGit rev-parse --short HEAD) ($(CopyGit rev-list --count "$Commit..HEAD") commit(s) on $($Commit.Substring(0, 7)))"
  exit 0
}

if (Test-Path $copy) {
  if (CopyGit status --porcelain) { throw "Uncommitted changes in $copy. Commit or discard them first." }
} else {
  git.exe clone --quiet --shared --no-checkout $Source $copy
  if ($LASTEXITCODE) { throw 'git clone failed' }
}
CopyGit checkout --quiet --detach $Commit
$list = @(Get-ChildItem $patches -Filter *.patch -ErrorAction SilentlyContinue | Sort-Object Name)
if ($list) { CopyGit am --quiet --keep-cr @($list.FullName) }
"copy ready: $copy at $(CopyGit rev-parse --short HEAD) with $($list.Count) patch(es)"
