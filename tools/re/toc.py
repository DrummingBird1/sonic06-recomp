"""Read TOC entries: the pointer stored at r2+offset, and the string or words it points to.

    python tools/re/toc.py 0x16a9528 0x4020 0x4234 0x3ee8 -0x5e34

The first argument is the function's TOC (second word of its OPD; jumptable.py
prints it). This title has three TOCs, so take the one of the function you
are reading. Offsets are as they appear in "lwz rX, off(r2)". A pointer to a
pointer (a global variable) needs one more dereference: this prints the first
three words at the target, so follow the first one by hand.
"""
import sys

from elf import Elf, parse_int


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    elf = Elf()
    toc = parse_int(sys.argv[1])
    for a in sys.argv[2:]:
        off = int(a, 0) if a.lstrip("-").lower().startswith("0x") else int(a, 16)
        p = elf.u32(toc + off)
        if p is None:
            print(f"{a}: outside the image")
            continue
        s = elf.cstr(p)
        words = [elf.u32(p + 4 * i) for i in range(3)]
        shown = " ".join(f"{w:#x}" if w is not None else "-" for w in words)
        text = f" {s!r}" if s and s.isprintable() and len(s) > 1 else ""
        print(f"{a}: {p:#x}{text}  [{shown}]")


if __name__ == "__main__":
    main()
