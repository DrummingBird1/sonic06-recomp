"""Search guest code for instructions matching a regex.

    python tools/re/scan.py 0xE00000 0x1000000 "stw r\\d+, 0x140\\(r(?!1\\))"
    python tools/re/scan.py 0x10000 0x1600000 "bl 0xefff28$"     # direct callers

The text matched is "mnemonic operands" as capstone prints it (lower-case hex,
e.g. "lwz r3, 0x74(r31)"). Undecodable words are skipped, so a range may span
data. Direct callers only: virtual calls go through vtables (see rtti.py) and
cross-TOC veneers ("std r2,0x28(r1); addis r2,...; b target").
"""
import re
import sys

import capstone

from elf import Elf, parse_int

_md = capstone.Cs(capstone.CS_ARCH_PPC, capstone.CS_MODE_64 | capstone.CS_MODE_BIG_ENDIAN)


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    lo, hi, pat = parse_int(sys.argv[1]), parse_int(sys.argv[2]), re.compile(sys.argv[3])
    elf = Elf()
    code = elf.read(lo, hi - lo) or b""
    for i in range(0, len(code) - 3, 4):
        ins = next(_md.disasm_lite(code[i:i + 4], lo + i), None)
        if ins:
            text = f"{ins[2]} {ins[3]}".strip()
            if pat.search(text):
                print(f"{lo + i:08x}  {text}")


if __name__ == "__main__":
    main()
