"""Disassemble guest PPC code from game/EBOOT.elf.

    python tools/re/ppcdis.py 0xC2C8C8            # to the function's blr (heuristic)
    python tools/re/ppcdis.py 0xC2C8C8:40 0xB76B34:72   # fixed instruction counts

Not named dis.py on purpose: that shadows the standard library module that
capstone imports, and the import fails with a circular-import error.
"""
import sys

import capstone

from elf import Elf, parse_int

_md = capstone.Cs(capstone.CS_ARCH_PPC, capstone.CS_MODE_64 | capstone.CS_MODE_BIG_ENDIAN)


def disasm(elf, va, count):
    """(address, mnemonic, operands) for count words; an undecodable word is '.word'."""
    out = []
    for i in range(count):
        a = va + 4 * i
        b = elf.read(a, 4)
        if not b or len(b) < 4:
            break
        ins = next(_md.disasm_lite(b, a), None)
        out.append((a, ins[2], ins[3]) if ins else (a, ".word", "0x" + b.hex()))
    return out


def function_length(elf, va, limit=4000):
    """Words up to the first blr that looks like a function end: the next word
    is padding or a stack-frame prologue. A heuristic, it can stop early or late."""
    for i, (a, mn, _op) in enumerate(disasm(elf, va, limit)):
        if mn == "blr":
            nxt = elf.u32(a + 4) or 0
            if nxt in (0, 0x60000000) or (nxt >> 16) in (0xF821, 0xF801) or nxt == 0x7C0802A6:
                return i + 1
    return limit


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    elf = Elf()
    for arg in sys.argv[1:]:
        if ":" in arg:
            a, n = arg.split(":", 1)
            va, count = parse_int(a), int(n)
        else:
            va = parse_int(arg)
            count = function_length(elf, va)
        print(f"--- {va:#x}")
        for a, mn, op in disasm(elf, va, count):
            print(f"  {a:08x}  {mn:8} {op}")


if __name__ == "__main__":
    main()
