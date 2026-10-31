"""Temporary trace hooks in the lifted code (work/recompiled).

    python tools/re/hook.py add 0x9AF9A8=1 0xC83BB8=2    # port_trace(id, ctx) at function entry
    python tools/re/hook.py wrap 0xE148E4=3              # port_trace_exit(id, ctx) after it returns
    python tools/re/hook.py list
    python tools/re/hook.py restore                      # put every touched file back

How it works: the lifted function func_XXXXXXXX(ppu_context*) gets a call to
port_trace / port_trace_exit, which you write in src/zz_trace.c (created from
tools/re/trace_template.c the first time; src/zz_* is git-ignored). ctx->gpr[3]
is `this` or the first argument at entry, the return value at exit; ctx->lr is
the caller. Each touched file is backed up once to work/hook_backup and
`restore` copies it back.

Costs and rules: every touched ppu_recomp_NNN.cpp is a huge translation unit,
so the next tools/build.ps1 recompiles it (several minutes each). When done:
restore, delete src/zz_trace.c, and build again (the restored files get a new
timestamp so Ninja rebuilds them). Restore before tools/relift.ps1 (it refuses
while hooks are active) and before committing; never commit src/zz_*.
Needs work/recompiled (tools/relift.ps1). A function that is only
a cross-TOC veneer ("std r2,0x28(r1); addis r2,...; b target") returns before
its target runs: hook the target instead (ppcdis.py shows it).
"""
import json
import pathlib
import re
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RECOMP = ROOT / "work" / "recompiled"
BACKUP = ROOT / "work" / "hook_backup"
STATE = BACKUP / "hooks.json"
TRACE = ROOT / "src" / "zz_trace.c"
TEMPLATE = pathlib.Path(__file__).with_name("trace_template.c")
DECL = ('extern "C" void port_trace(int, ppu_context*);\n'
        'extern "C" void port_trace_exit(int, ppu_context*);\n')


def load_state():
    return json.loads(STATE.read_text()) if STATE.exists() else {"files": [], "hooks": []}


def save_state(s):
    BACKUP.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(s, indent=1))


def find_file(name):
    pat = re.compile(rf"^void {name}\(ppu_context\* ctx\) \{{$", re.M)
    for f in sorted(RECOMP.glob("ppu_recomp_*.cpp")):
        if pat.search(f.read_text(encoding="utf-8", errors="surrogateescape")):
            return f
    return None


def hook(mode, specs):
    s = load_state()
    for spec in specs:
        addr, hid = spec.split("=", 1)
        name = f"func_{int(addr, 16):08X}"
        f = find_file(name)
        if not f:
            print(f"{name}: not found in {RECOMP}")
            continue
        if f.name not in s["files"]:
            BACKUP.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f, BACKUP / f.name)
            s["files"].append(f.name)
        t = f.read_text(encoding="utf-8", errors="surrogateescape")
        if DECL not in t:
            t = t.replace('#include "ppu_recomp.h"\n', '#include "ppu_recomp.h"\n' + DECL, 1)
        head = f"void {name}(ppu_context* ctx) {{\n"
        if mode == "add":
            t = t.replace(head, head + f"    port_trace({int(hid)}, ctx);\n", 1)
        else:
            t = t.replace(head,
                          f"static void {name}_real(ppu_context* ctx);\n{head}"
                          f"    {name}_real(ctx);\n    port_trace_exit({int(hid)}, ctx);\n}}\n"
                          f"static void {name}_real(ppu_context* ctx) {{\n", 1)
        f.write_text(t, encoding="utf-8", errors="surrogateescape", newline="\n")
        s["hooks"].append({"mode": mode, "func": name, "id": int(hid), "file": f.name})
        print(f"{mode} {name} id {hid} in {f.name}")
    save_state(s)
    if not TRACE.exists():
        shutil.copy2(TEMPLATE, TRACE)
        print(f"created {TRACE.relative_to(ROOT)} from the template: edit it, then tools/build.ps1")


def restore():
    s = load_state()
    for name in s["files"]:
        # copyfile, not copy2: the restored file must look newer than the
        # object built from the hooked one, or Ninja keeps that object and the
        # link fails on port_trace.
        shutil.copyfile(BACKUP / name, RECOMP / name)
        print(f"restored {name}")
    if BACKUP.exists():
        shutil.rmtree(BACKUP)
    if TRACE.exists():
        print(f"{TRACE.relative_to(ROOT)} is still there: delete it before building, or the link keeps it (harmless) and git ignores it")


def main():
    a = sys.argv[1:]
    if not a:
        raise SystemExit(__doc__)
    if a[0] in ("add", "wrap") and len(a) > 1:
        hook(a[0], a[1:])
    elif a[0] == "list":
        for h in load_state()["hooks"]:
            print(f"{h['mode']:4} {h['func']} id {h['id']} ({h['file']})")
    elif a[0] == "restore":
        restore()
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
