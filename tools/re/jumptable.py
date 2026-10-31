"""Decode a switch jump table: the target of every case.

    python tools/re/jumptable.py 0x1667938 0x505c 64     # OPD of the function, TOC offset, cases
    python tools/re/jumptable.py --table 0xCA8B90 64     # the table address itself

The compiler's pattern is "lwz rT, off(r2); rldic rI, rS, 2, 30; lwzx rX, rI, rT;
extsw; add rX, rX, rT; mtctr; bctr": the TOC entry at r2+off points to a table
of signed words, each relative to the table start. The function's TOC is the
second word of its OPD (rtti.py prints OPDs; for a function you only have the
code address of, search the OPD with elf.find_u32(code)).
"""
import struct
import sys

from elf import Elf, parse_int


def main():
    a = sys.argv[1:]
    elf = Elf()
    if len(a) == 3 and a[0] == "--table":
        table, count = parse_int(a[1]), int(a[2])
    elif len(a) == 3:
        opd, tocoff, count = parse_int(a[0]), parse_int(a[1]), int(a[2])
        tocoff = tocoff - 0x10000 if tocoff >= 0x8000 else tocoff   # signed 16-bit displacement
        toc = elf.opd_toc(opd)
        table = elf.u32(toc + tocoff)
        print(f"toc {toc:#x}, table {table:#x}")
    else:
        raise SystemExit(__doc__)
    for i in range(count):
        b = elf.read(table + 4 * i, 4)
        if not b:
            break
        rel = struct.unpack(">i", b)[0]
        print(f"  case {i:#04x} ({i:3}): {table + rel:#x}")


if __name__ == "__main__":
    main()
