# Regenerates everything in work/ from the decrypted game/EBOOT.elf:
# the loader tables, the import list and the lifted PPU code.
# Run from anywhere; paths are relative to the repo root.
param(
  [string]$Ps3Recomp = ''   # default: the project's patched copy (setup_ps3recomp.ps1)
)
$ErrorActionPreference = 'Stop'
# Python's text files and pipes default to the Windows ANSI code page. The
# lifter writes an em dash into every chunk, which cp932 (Japanese) and cp949
# (Korean) cannot encode, so the lift stopped there. UTF-8 mode makes every
# locale write and print the same bytes.
$env:PYTHONUTF8 = '1'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Ps3Recomp) {
  $Ps3Recomp = Join-Path $root 'work\ps3recomp'
  if (-not (Test-Path $Ps3Recomp)) { & (Join-Path $PSScriptRoot 'setup_ps3recomp.ps1') }
}
$py = Join-Path $root '.venv\Scripts\python.exe'
$tools = Join-Path $Ps3Recomp 'tools'
$elf = Join-Path $root 'game\EBOOT.elf'
if (-not (Test-Path $elf)) { throw "Missing $elf. Decrypt game\EBOOT.BIN with RPCS3 first (rpcs3 --decrypt, or the launcher)." }
# Trace hooks keep backups of the files they touched; a relift would replace
# those files and a later restore would copy the old lift back over the new.
if (Test-Path (Join-Path $root 'work\hook_backup')) {
  throw 'Trace hooks are active (work\hook_backup). Run: .venv\Scripts\python.exe tools\re\hook.py restore'
}

$loader = Join-Path $root 'work\loader'
$recomp = Join-Path $root 'work\recompiled'
New-Item -ItemType Directory -Force $loader | Out-Null

& $py "$tools\ppu_loader.py" $elf -o "$loader\"
if ($LASTEXITCODE) { throw 'ppu_loader failed' }
& $py "$tools\gen_imports.py" $elf -o "$loader\imports_named.json"
if ($LASTEXITCODE) { throw 'gen_imports failed' }

# .init starts with a linker TOC stub at 0x10200 (std r2,40(r1); r2 += 0x1FF90;
# b 0x13BC754) that _init (0x10210) calls to run the static constructors. It has
# no OPD entry and sits below the first OPD function, so the lifter dropped the
# call as "non-code": the constructors never ran and _init's `ld r2,40(r1)`
# restored the TOC from an unwritten stack slot.
& $py -I -c @"
import json, sys
p = sys.argv[1]
f = json.load(open(p))
if not any(int(x['start'], 16) == 0x10200 for x in f):
    f.insert(0, {'start': '0x10200', 'end': '0x10210', 'toc': '0x1699580', 'opd': None})
    json.dump(f, open(p, 'w'), indent=1)
"@ "$loader\EBOOT.functions.json"
if ($LASTEXITCODE) { throw 'adding the .init stub failed' }

if (Test-Path $recomp) { Remove-Item -Recurse -Force $recomp }
New-Item -ItemType Directory -Force $recomp | Out-Null
& $py "$tools\ppu_lifter.py" $elf --functions "$loader\EBOOT.functions.json" `
      --hle-stubs "$loader\EBOOT.imports.json" -o "$recomp\"
if ($LASTEXITCODE) { throw 'ppu_lifter failed' }

& $py -I (Join-Path $PSScriptRoot 'patch_lift.py') $recomp
if ($LASTEXITCODE) { throw 'patch_lift failed' }

# Written last: the launcher redoes the lift when this is missing (a lift that
# stopped half way) or names another toolkit tree (a lift made by an
# incomplete or older ps3recomp copy).
Set-Content (Join-Path $recomp 'lift-complete.txt') (git.exe -C $Ps3Recomp rev-parse 'HEAD^{tree}')
