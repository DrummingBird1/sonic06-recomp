"""Post-lift fixes applied to work/recompiled after ppu_lifter.py.

Import stubs spill the caller's TOC. A real .lib.stub trampoline stores r2 at
40(r1) before jumping to the firmware, and the caller reloads it with
`ld r2,40(r1)` afterwards. The lifter replaces each stub body with
ps3_hle_call(nid), and the runtime only writes that slot on its slow path:
fast handlers (sys_lwmutex_unlock and friends) return first. This title has
three TOCs, so the lifter keeps the stack reload instead of pinning a constant,
and the caller came back with r2 = whatever was on the stack (0 during the
static constructors). Doing the store in the stub, as the hardware does,
covers every dispatch path.

Port hooks: a call into the port's own C code at the start of a few lifted
functions (PORT_HOOKS below). The hooks are in src/ and do nothing unless a
setting asks for it.

Idempotent: a stub that already has the store, or a function that already has
its hook, is left alone. A hook whose function is missing fails the run, so a
lift that moved a function does not ship without it.
Usage: python patch_lift.py <recompiled dir>
"""
import pathlib
import re
import sys

STUB = re.compile(r"^(\s*)ps3_hle_call\((0x[0-9A-F]+u), ctx\); return;  /\* import stub \*/$", re.M)
SPILL = "vm_write64(ctx->gpr[1] + 0x28, ctx->gpr[2]); "

# Lifted function -> the port function called at its start (src/sonic06_timing.c).
PORT_HOOKS = {
    "func_00022FB0": "sonic06_doc_update",   # DocMarathonState::Update: the game step (issue #15)
    "func_00E3433C": "sonic06_app_frame",    # AppMarathon's per-frame update: the frame cap
}
INCLUDE = '#include "ppu_recomp.h"\n'


def add_hooks(text):
    """The text with every hook whose function it defines; and the names it added or found."""
    found = []
    for func, port in PORT_HOOKS.items():
        head = f"void {func}(ppu_context* ctx) {{\n"
        if head not in text:
            continue
        found.append(func)
        call = f"    {port}(ctx);  /* port hook (patch_lift.py) */\n"
        if head + call in text:
            continue
        decl = f'extern "C" void {port}(ppu_context*);\n'
        if decl not in text:
            if INCLUDE not in text:
                raise SystemExit(f"patch_lift: no {INCLUDE.strip()} to put the declaration of {port} after")
            text = text.replace(INCLUDE, INCLUDE + decl, 1)
        text = text.replace(head, head + call, 1)
    return text, found


def main() -> int:
    root = pathlib.Path(sys.argv[1])
    total = 0
    hooked = []
    for src in sorted(root.glob("ppu_recomp_*.cpp")):
        text = src.read_text(encoding="utf-8", errors="surrogateescape")
        new, n = STUB.subn(lambda m: f"{m.group(1)}{SPILL}ps3_hle_call({m.group(2)}, ctx); return;  /* import stub */", text)
        total += n
        new, found = add_hooks(new)
        hooked += found
        if new != text:
            src.write_text(new, encoding="utf-8", errors="surrogateescape", newline="\n")
    print(f"import stubs patched: {total}")
    missing = sorted(set(PORT_HOOKS) - set(hooked))
    if missing:
        print(f"patch_lift: lifted functions not found for the port hooks: {', '.join(missing)}")
        return 1
    print(f"port hooks: {len(hooked)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
