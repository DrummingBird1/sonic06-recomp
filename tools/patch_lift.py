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
functions (PORT_HOOKS below), and a call in place of a constant inside a few
others (BODY_PATCHES). The code they call is in src/ and changes nothing
unless a setting asks for it.

Idempotent: a stub that already has the store, or a function that already has
its hook or its patch, is left alone. A hook whose function is missing fails the run, so a
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

# Text replaced inside one lifted function's body: AppMarathon's two frame
# limiters keep at least 15 ms (0x3A98 us) between frames, which stops the game
# near 66 frames a second. sonic06_min_frame_us() is 15000 unless a frame cap
# above 60 is set (src/sonic06_timing.c).
MIN_FRAME = [
    ("(uint64_t)0x3A97;", "(uint64_t)(sonic06_min_frame_us() - 1);"),
    ("(uint64_t)(int64_t)(0x3A98);", "(uint64_t)(int64_t)(sonic06_min_frame_us());"),
]
BODY_PATCHES = {
    "func_00F00C34": ("sonic06_min_frame_us", 'extern "C" int sonic06_min_frame_us(void);\n', MIN_FRAME),
    "func_00EFFF28": ("sonic06_min_frame_us", 'extern "C" int sonic06_min_frame_us(void);\n', MIN_FRAME),
}


def patch_bodies(text):
    """The text with each BODY_PATCHES function patched; and the names it patched or found patched."""
    found = []
    for func, (port, decl, pairs) in BODY_PATCHES.items():
        head = f"void {func}(ppu_context* ctx) {{\n"
        start = text.find(head)
        if start < 0:
            continue
        end = text.find("\n}\n", start)
        body = text[start:end]
        if port in body:
            found.append(func)
            continue
        for old, new in pairs:
            if body.count(old) != 1:
                raise SystemExit(f"patch_lift: {func}: expected one '{old}', found {body.count(old)}")
            body = body.replace(old, new)
        text = text[:start] + body + text[end:]
        if decl not in text:
            text = text.replace(INCLUDE, INCLUDE + decl, 1)
        found.append(func)
    return text, found


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
    hooked, bodies = [], []
    for src in sorted(root.glob("ppu_recomp_*.cpp")):
        text = src.read_text(encoding="utf-8", errors="surrogateescape")
        new, n = STUB.subn(lambda m: f"{m.group(1)}{SPILL}ps3_hle_call({m.group(2)}, ctx); return;  /* import stub */", text)
        total += n
        new, found = add_hooks(new)
        hooked += found
        new, found = patch_bodies(new)
        bodies += found
        if new != text:
            src.write_text(new, encoding="utf-8", errors="surrogateescape", newline="\n")
    print(f"import stubs patched: {total}")
    missing = sorted((set(PORT_HOOKS) - set(hooked)) | (set(BODY_PATCHES) - set(bodies)))
    if missing:
        print(f"patch_lift: lifted functions not found for the port's changes: {', '.join(missing)}")
        return 1
    print(f"port hooks: {len(hooked)}, patched functions: {len(bodies)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
