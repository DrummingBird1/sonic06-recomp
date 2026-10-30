"""Resolve host RVAs from runtime crash/watchdog reports to symbols using the
linker map (build/sonic06.map). Usage: python addr2func.py <rva> [<rva> ...]"""
import bisect
import pathlib
import re
import sys

MAP = pathlib.Path(__file__).resolve().parent.parent / "build" / "sonic06.map"


def load():
    base, syms = 0x140000000, []
    for line in MAP.read_text(errors="ignore").splitlines():
        m = re.search(r"Preferred load address is ([0-9a-fA-F]+)", line)
        if m:
            base = int(m.group(1), 16)
        m = re.match(r"\s*[0-9a-fA-F]{4}:[0-9a-fA-F]{8}\s+(\S+)\s+([0-9a-fA-F]{8,16})\s", line)
        if m:
            syms.append((int(m.group(2), 16) - base, m.group(1)))
    syms.sort()
    return syms


def main() -> None:
    syms = load()
    keys = [a for a, _ in syms]
    for arg in sys.argv[1:]:
        rva = int(arg, 16)
        i = bisect.bisect_right(keys, rva) - 1
        name = syms[i][1] if i >= 0 else "?"
        print(f"{rva:#x}  {name}+{rva - syms[i][0]:#x}")


if __name__ == "__main__":
    main()
