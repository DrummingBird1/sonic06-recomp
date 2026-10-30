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

Idempotent: a stub that already has the store is left alone.
Usage: python patch_lift.py <recompiled dir>
"""
import pathlib
import re
import sys

STUB = re.compile(r"^(\s*)ps3_hle_call\((0x[0-9A-F]+u), ctx\); return;  /\* import stub \*/$", re.M)
SPILL = "vm_write64(ctx->gpr[1] + 0x28, ctx->gpr[2]); "


def main() -> int:
    root = pathlib.Path(sys.argv[1])
    total = 0
    for src in sorted(root.glob("ppu_recomp_*.cpp")):
        text = src.read_text(encoding="utf-8", errors="surrogateescape")
        new, n = STUB.subn(lambda m: f"{m.group(1)}{SPILL}ps3_hle_call({m.group(2)}, ctx); return;  /* import stub */", text)
        if n:
            src.write_text(new, encoding="utf-8", errors="surrogateescape", newline="\n")
            total += n
    print(f"import stubs patched: {total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
