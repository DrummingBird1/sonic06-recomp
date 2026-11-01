"""Lists or extracts files from the game's U8 archives (magic 55AA382D, 16-byte
nodes: type|name, data offset, stored size, unpacked size; zlib when stored)."""

    python tools/re/arc.py game/disc/PS3_GAME/USRDIR/win32/archives/player_sonic.arc 'dds$'
    python tools/re/arc.py ARC REGEX OUTDIR      # also writes the matches, unpacked

Extracted files are game files: keep them out of git, and delete them when done.
"""
import pathlib
import re
import struct
import sys
import zlib


def parse(p):
    d = pathlib.Path(p).read_bytes()
    magic, root = struct.unpack('>II', d[:8])
    assert magic == 0x55AA382D, hex(magic)
    n = struct.unpack('>I', d[root + 8:root + 12])[0]
    names = root + n * 16
    out = []
    ends = [(n, '')]
    for i in range(1, n):
        w, off, size, usize = struct.unpack('>IIII', d[root + i * 16:root + i * 16 + 16])
        t = w >> 24
        name = d[names + (w & 0xFFFFFF):].split(b'\0', 1)[0].decode('ascii', 'replace')
        while len(ends) > 1 and i >= ends[-1][0]:
            ends.pop()
        base = ends[-1][1]
        if t == 1:
            ends.append((size, base + name + '/'))
        else:
            out.append((base + name, off, size, usize))
    return d, out


if __name__ == '__main__':
    d, files = parse(sys.argv[1])
    pat = re.compile(sys.argv[2]) if len(sys.argv) > 2 else None
    dest = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else None
    for name, off, size, usize in files:
        if not pat or pat.search(name):
            print(f'{off:#10x} {size:9d} {usize:9d} {name}')
            if dest:
                raw = d[off:off + size]
                data = zlib.decompress(raw) if usize and size != usize else raw
                out = dest / name.replace('/', '_')
                out.write_bytes(data)
    print(len(files), 'files', len(d))
