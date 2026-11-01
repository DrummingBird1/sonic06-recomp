"""The developer stage list: scripts/stage/stageselect.lub read straight from
scripts.arc, and the pad presses that reach an entry of it.

The list is the menu of SONIC06_BOOT=stage. Its text does not show in this
build, so the menu is driven blind. An entry's keys Story, Stage, Area and
Player are the menu levels, when present, and its ID is the last one. Each
level lists its names in the order they first appear in the file.

    python tools/re/stagelist.py                  # every entry and its path
    python tools/re/stagelist.py boss             # entries matching a regex
    python tools/re/stagelist.py --pad eWyvern_sonic   # PAD_SCRIPT for an entry
"""
import re
import struct
import sys
import zlib

from arc import parse

ARC = 'game/disc/PS3_GAME/USRDIR/ps3/archives/scripts.arc'
NAME = 'ps3/scripts/stage/stageselect.lub'
LEVELS = ('Story', 'Stage', 'Area', 'Player')
DOWN, CROSS = 0x0040, 0x4000
RK_BASE = 250        # Lua 5.0: a register index from here on is a constant
FIELDS_PER_FLUSH = 32


class Chunk:
    """Lua 5.0 bytecode, little-endian, for the one function a data file has."""

    def __init__(self, d):
        assert d[:5] == b'\x1bLuaP' and d[5] == 1
        self.d, self.p = d, 22
        self.function()

    def take(self, n, fmt):
        v = struct.unpack_from('<' + fmt, self.d, self.p)[0]
        self.p += n
        return v

    def int(self):
        return self.take(4, 'i')

    def string(self):
        n = self.take(4, 'I')
        v = self.d[self.p:self.p + n - 1].decode('latin1')
        self.p += n
        return v

    def constant(self):
        kind = self.take(1, 'B')
        if kind == 3:
            return self.take(8, 'd')
        if kind == 4:
            return self.string()
        assert kind == 0, kind

    def function(self):
        self.string()                 # source
        self.int()                    # line
        self.p += 4                   # upvalues, parameters, vararg, stack
        lines = self.int()
        self.p += lines * 4
        for _ in range(self.int()):   # locals
            self.string()
            self.p += 8
        for _ in range(self.int()):   # upvalue names
            self.string()
        self.k = [self.constant() for _ in range(self.int())]
        assert self.int() == 0        # nested functions
        self.code = [self.take(4, 'I') for _ in range(self.int())]


def run(chunk):
    """The file only builds tables: LOADK, NEWTABLE, SETTABLE, SETLIST."""
    regs, env = {}, {}

    def rk(x):
        return chunk.k[x - RK_BASE] if x >= RK_BASE else regs[x]

    for ins in chunk.code:
        op, a, b, c = ins & 63, ins >> 24, ins >> 15 & 511, ins >> 6 & 511
        bx = ins >> 6 & 0x3FFFF
        if op == 1:                   # LOADK
            regs[a] = chunk.k[bx]
        elif op == 7:                 # SETGLOBAL
            env[chunk.k[bx]] = regs[a]
        elif op == 9:                 # SETTABLE
            regs[a][rk(b)] = rk(c)
        elif op == 10:                # NEWTABLE
            regs[a] = {}
        elif op == 31:                # SETLIST
            for i in range(1, bx % FIELDS_PER_FLUSH + 2):
                regs[a][bx - bx % FIELDS_PER_FLUSH + i] = regs[a + i]
        elif op == 27:                # RETURN
            break
        else:
            raise SystemExit(f'opcode {op} in a data file')
    return env


def load():
    d, files = parse(ARC)
    off, size, usize = next(f[1:] for f in files if f[0] == NAME)
    raw = d[off:off + size]
    env = run(Chunk(zlib.decompress(raw) if usize and size != usize else raw))
    table = env['StageList']
    return [table[i] for i in sorted(table)]


def path(entry):
    return [entry[k] for k in LEVELS if k in entry] + [entry['ID']]


def indices(entries, entry):
    """The menu position at each level on the way to entry."""
    out, node = [], {}
    for e in entries:
        n = node
        for name in path(e):
            n = n.setdefault(name, {})
    n = node
    for name in path(entry):
        out.append(list(n).index(name))
        n = n[name]
    return out


def presses(entries, entry, start=9):
    """(second, mask) pairs, one press a second from start."""
    t, out = start, []
    for i in indices(entries, entry):
        for _ in range(i):
            out.append((t, DOWN))
            t += 1
        out.append((t, CROSS))
        t += 1
    return out


def find(entries, key):
    return next(e for e in entries if e['ID'] == key or key in path(e))


if __name__ == '__main__':
    entries = load()
    if len(sys.argv) > 2 and sys.argv[1] == '--pad':
        pad = presses(entries, find(entries, sys.argv[2]))
        print(','.join(f'{t}:{m:#06x}' for t, m in pad))
    else:
        pat = re.compile(sys.argv[1], re.I) if len(sys.argv) > 1 else None
        for e in entries:
            line = ' / '.join(path(e))
            if not pat or pat.search(line):
                print(indices(entries, e), line.encode('ascii', 'replace').decode())
