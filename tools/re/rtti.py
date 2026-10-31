"""Find a C++ class by its RTTI name and dump its bases and vtables.

    python tools/re/rtti.py HUDMainMenu            # any RTTI name containing it
    python tools/re/rtti.py N9Sonicteam8MainModeE  # an exact mangled name
    python tools/re/rtti.py --list Title           # just list the matching names

Layout (Itanium ABI as the PS3 toolchain emits it, 32-bit pointers):
typeinfo = {vptr, name, ...}; a vtable is {offset_to_top, typeinfo, OPD...},
so every reference to the typeinfo address that follows an offset-to-top
word starts one of the class's vtables (the primary one has offset 0, each
secondary base its own negative offset). Each entry is an OPD; the code
address is its first word. Slot order on PS3: two destructors (complete and
deleting) where the Xbox 360 build (MarathonRecomp's headers) has one.
"""
import re
import sys

from elf import Elf


def typeinfo_kind(elf, ti):
    vptr = elf.u32(ti)
    if not vptr:
        return "?"
    meta = elf.u32(vptr - 4)
    name = elf.cstr(elf.u32(meta + 4) or 0) if meta else None
    return name or "?"


def class_name(elf, ti):
    p = elf.u32(ti + 4)
    return elf.cstr(p) if p else None


def is_code(elf, va):
    for sva, _off, size, flags in elf.segments:
        if sva <= va < sva + size:
            return bool(flags & 1)
    return False


def vtable_entries(elf, start, limit=200):
    out = []
    for i in range(limit):
        opd = elf.u32(start + 4 * i)
        if not opd or is_code(elf, opd):
            break
        code = elf.u32(opd)
        if not code or not is_code(elf, code):
            break
        out.append((i, opd, code))
    return out


def dump(elf, name_va, name):
    for ti_name_ref in elf.find_u32(name_va):
        ti = ti_name_ref - 4
        kind = typeinfo_kind(elf, ti)
        print(f"{name}\n  typeinfo {ti:#x} ({kind})")
        if "vmi" in kind:
            n = elf.u32(ti + 12) or 0
            for i in range(min(n, 16)):
                base, of = elf.u32(ti + 16 + 8 * i), elf.u32(ti + 20 + 8 * i)
                print(f"  base {class_name(elf, base)} at offset {of >> 8:#x} (flags {of & 0xFF:#x})")
        elif "si_class" in kind:
            print(f"  base {class_name(elf, elf.u32(ti + 8))} at offset 0")
        for ref in elf.find_u32(ti):
            ott = elf.u32(ref - 4)
            if ott is None:
                continue
            ott = ott - (1 << 32) if ott & 0x80000000 else ott
            if ott > 0 or ott < -0x10000:
                continue   # not an offset-to-top word: some other reference
            entries = vtable_entries(elf, ref + 4)
            if not entries:
                continue
            print(f"  vtable {ref + 4:#x} (offset to top {ott})")
            for i, opd, code in entries:
                print(f"    [{i:2}] +{4 * i:#05x}  opd {opd:#x}  code {code:#x}")


def main():
    args = sys.argv[1:]
    if not args:
        raise SystemExit(__doc__)
    list_only = args[0] == "--list"
    if list_only:
        args = args[1:]
    elf = Elf()
    want = args[0]
    pattern = re.compile(rb"N\d+Sonicteam[0-9A-Za-z_]*E\x00|N\d+[A-Za-z_][0-9A-Za-z_]*E\x00|\d+[A-Za-z_][0-9A-Za-z_]*\x00")
    names = {}
    for m in pattern.finditer(elf.data):
        s = m.group()[:-1].decode("latin1")
        if want in s and len(s) < 160:
            va = elf.off_to_va(m.start())
            if va is not None:
                names.setdefault(s, va)
    exact = {s: va for s, va in names.items() if s == want}
    hits = exact or names
    if not hits:
        raise SystemExit(f"no RTTI name containing {want!r}")
    for s, va in sorted(hits.items()):
        if list_only:
            print(f"{va:#x}  {s}")
        elif elf.find_u32(va):
            dump(elf, va, s)


if __name__ == "__main__":
    main()
