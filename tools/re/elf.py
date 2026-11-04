"""Read-only access to the decrypted EBOOT (game/EBOOT.elf) by guest address.

Shared by the other tools in this folder. The ELF is big-endian PPC64 with
32-bit pointers in data; OPDs (function descriptors) are {u32 code, u32 toc}.
"""
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
DEFAULT_ELF = ROOT / "game" / "EBOOT.elf"


class Elf:
    def __init__(self, path=DEFAULT_ELF):
        self.path = pathlib.Path(path)
        if not self.path.exists():
            raise SystemExit(f"{self.path} is missing: decrypt game/EBOOT.BIN with RPCS3 first (rpcs3 --decrypt, or the launcher).")
        self.data = self.path.read_bytes()
        d = self.data
        phoff = struct.unpack(">Q", d[0x20:0x28])[0]
        phnum = struct.unpack(">H", d[0x38:0x3A])[0]
        self.segments = []   # (vaddr, file offset, file size, flags) of PT_LOAD
        for i in range(phnum):
            ph = d[phoff + i * 0x38: phoff + (i + 1) * 0x38]
            ptype, flags = struct.unpack(">II", ph[0:8])
            off, va, _pa, filesz = struct.unpack(">QQQQ", ph[8:40])
            if ptype == 1 and filesz:
                self.segments.append((va, off, filesz, flags))

    def va_to_off(self, va):
        for sva, off, size, _ in self.segments:
            if sva <= va < sva + size:
                return off + va - sva
        return None

    def off_to_va(self, off):
        for sva, soff, size, _ in self.segments:
            if soff <= off < soff + size:
                return sva + off - soff
        return None

    def read(self, va, n):
        off = self.va_to_off(va)
        return None if off is None else self.data[off:off + n]

    def u32(self, va):
        b = self.read(va, 4)
        return struct.unpack(">I", b)[0] if b and len(b) == 4 else None

    def cstr(self, va, limit=256):
        off = self.va_to_off(va)
        if off is None:
            return None
        end = self.data.find(b"\0", off, off + limit)
        return self.data[off:end if end >= 0 else off + limit].decode("latin1")

    def find_bytes(self, pattern):
        """Guest addresses where pattern occurs."""
        out, i = [], self.data.find(pattern)
        while i >= 0:
            va = self.off_to_va(i)
            if va is not None:
                out.append(va)
            i = self.data.find(pattern, i + 1)
        return out

    def find_u32(self, value):
        """Guest addresses of aligned big-endian words equal to value."""
        return [va for va in self.find_bytes(struct.pack(">I", value)) if va % 4 == 0]

    def opd_code(self, opd):
        """Code address of a function descriptor."""
        return self.u32(opd)

    def opd_toc(self, opd):
        return self.u32(opd + 4)


def parse_int(s):
    return int(s, 0) if s.lower().startswith("0x") else int(s, 16)
