# Makes work\ps3recomp: a ps3recomp clone at the pinned commit with this
# project's patches (patches\ps3recomp\*.patch) applied on top. The build and
# the lifter use this copy. The source clone is only read: the copy borrows its
# objects (git clone --shared) and nothing is written back.
#
# The source: -Source, else $env:PS3RECOMP_SOURCE (a clone of your own), else
# deps\ps3recomp, cloned from GitHub the first time, as the launcher does.
#
# Changing the toolkit: edit and commit inside work\ps3recomp, then run this
# script with -Export to rewrite patches\ps3recomp from those commits. -Reset
# rebuilds the copy from the pinned commit and the patch files.
param(
  [string]$Source = '',
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

$list = @(Get-ChildItem $patches -Filter *.patch -ErrorAction SilentlyContinue | Sort-Object Name)

# A setup that stopped half way leaves a copy that exists but is not usable:
# cloned but never checked out (no tools\, so the lift's python "can't open
# file"), or checked out with git am stopped (the plain pinned commit, so a
# build without the patches: upside down, silent, freezing). The next run took
# it as done (issues #9 and #11). Ready means: checked out, no am in progress,
# and at least one commit per patch file on top of the pinned commit.
$checkedOut = Test-Path (Join-Path $copy 'tools\ppu_lifter.py')
$amStopped = Test-Path (Join-Path $copy '.git\rebase-apply')
function CopyReady {
  if (-not $checkedOut -or $amStopped) { return $false }
  git.exe -C $copy merge-base --is-ancestor $Commit HEAD 2>$null
  if ($LASTEXITCODE) { return $false }
  return [int](CopyGit rev-list --count "$Commit..HEAD") -ge $list.Count
}

if ((Test-Path $copy) -and -not $Reset) {
  if (CopyReady) {
    "copy exists: $copy at $(CopyGit rev-parse --short HEAD) ($(CopyGit rev-list --count "$Commit..HEAD") commit(s) on $($Commit.Substring(0, 7)))"
    exit 0
  }
  "copy at $copy is incomplete (an earlier setup stopped): making it again from $($Commit.Substring(0, 7)) and the patches"
}

if (Test-Path $copy) {
  if ($amStopped) { CopyGit am --quit }
  # A copy that was never checked out shows every file as deleted: nothing to lose.
  if ($checkedOut -and (CopyGit status --porcelain)) { throw "Uncommitted changes in $copy. Commit or discard them first." }
} else {
  if (-not $Source) { $Source = $env:PS3RECOMP_SOURCE }
  if (-not $Source) {
    $Source = Join-Path $root 'deps\ps3recomp'
    if (-not (Test-Path (Join-Path $Source '.git'))) {
      "cloning ps3recomp into $Source"
      git.exe clone --quiet https://github.com/sp00nznet/ps3recomp.git $Source
      if ($LASTEXITCODE) { throw 'git clone of ps3recomp failed' }
    }
  }
  git.exe clone --quiet --shared --no-checkout $Source $copy
  if ($LASTEXITCODE) { throw 'git clone failed' }
}
CopyGit checkout --quiet --force --detach $Commit
# git am makes commits, and a fresh git (winget) has no user.name/user.email:
# "Committer identity unknown", and the patches were never applied. Commits in
# this copy stay on this PC; -Export keeps each patch's own author.
$identity = @()
if (-not (git.exe -C $copy config user.email)) {
  $identity = '-c', 'user.name=Soleanna setup', '-c', 'user.email=setup@soleanna.invalid'
}
if ($list) { CopyGit @identity am --quiet --keep-cr @($list.FullName) }
"copy ready: $copy at $(CopyGit rev-parse --short HEAD) with $($list.Count) patch(es)"
