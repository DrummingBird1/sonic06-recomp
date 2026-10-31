"""Name the import behind a stub address: "bl 0x13bd400" -> NID -> function.

    python tools/re/imports.py 0x13BD400 0x13BD860
    python tools/re/imports.py --all          # every import the lifted code calls

Reads the lifted stubs in work/recompiled (each is one ps3_hle_call(NID) line)
and the names in build/ppu_hle_nids.cpp, the table the build generates from
ps3recomp's libs. A NID that is not in that table is handled by a context
handler registered elsewhere (src/*.c, runtime/ppu/ppu_sysprx.cpp), so grep
for it there; the name is then in the ps3_hle_register_ctx call.
"""
import hashlib
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
# PS3 NID = first four bytes, little-endian, of SHA-1(name + this suffix)
# (ps3recomp tools/nid_database.py, RPCS3 ppu_generate_id).
NID_SUFFIX = bytes.fromhex("6759659904250490566427499489741a")


def compute_nid(name):
    return int.from_bytes(hashlib.sha1(name.encode() + NID_SUFFIX).digest()[:4], "little")


def nid_names():
    names = {}
    table = ROOT / "build" / "ppu_hle_nids.cpp"
    if table.exists():
        for m in re.finditer(r'ps3_hle_register\(0x([0-9A-Fa-f]{8})u, "([^"]+)"', table.read_text(errors="ignore")):
            names[int(m.group(1), 16)] = m.group(2)
    for src in list((ROOT / "src").glob("*.c*")) + [ROOT / "work/ps3recomp/runtime/ppu/ppu_sysprx.cpp"]:
        if src.exists():
            text = src.read_text(errors="ignore")
            for m in re.finditer(r'ps3_hle_register_ctx\(0x([0-9A-Fa-f]{8})u?, "([^"]+)"', text):
                names.setdefault(int(m.group(1), 16), m.group(2))
            for m in re.finditer(r'ps3_compute_nid\("([^"]+)"\)', text):
                names.setdefault(compute_nid(m.group(1)), m.group(1))
    return names


def stubs():
    out = {}
    pat = re.compile(r"^void func_([0-9A-F]{8})\(ppu_context\* ctx\) \{\n(?:.*\n){0,2}?.*ps3_hle_call\(0x([0-9A-F]{8})u", re.M)
    for f in sorted((ROOT / "work" / "recompiled").glob("ppu_recomp_*.cpp")):
        for m in pat.finditer(f.read_text(errors="ignore")):
            out[int(m.group(1), 16)] = int(m.group(2), 16)
    return out


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    names, table = nid_names(), stubs()
    want = sorted(table) if sys.argv[1] == "--all" else [int(a, 16) for a in sys.argv[1:]]
    for addr in want:
        nid = table.get(addr)
        if nid is None:
            print(f"{addr:#010x}: not an import stub")
        else:
            print(f"{addr:#010x}: NID {nid:#010x} {names.get(nid, '(not in the generated table)')}")


if __name__ == "__main__":
    main()
