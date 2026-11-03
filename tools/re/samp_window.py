"""What one thread ran inside a time window, from timestamped samples.

    python tools/re/samp_window.py work/ftrace/samples.txt <from ms> <to ms> [--map build/sonic06.map]

Record the samples with PS3_SAMPLE=1 (or 2: ms between samples),
PS3_SAMPLE_MAIN=1 or PS3_SAMPLE_DRAIN=1 (which thread), PS3_SAMPLE_RVAS=<file>
and PS3_SAMPLE_RVAS_TIME=1, and PS3_SAMPLE_DELAY=<s> so the sampling does not
slow the boot. With PS3_FTRACE in the same run the times are on the trace's
clock, so a window can be a frame or a drain pass from tools/re/ft_frames.py.
Prints the leaves (a function in the executable, or a DLL) and the leaf with
its first three callers in the executable.
"""
import argparse
import bisect
import collections
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]


def load_map(path):
    syms = []
    pat = re.compile(r'^\s*0001:[0-9a-f]+\s+(\S+)\s+([0-9a-f]{16})\s+(\S.*)$')
    for line in open(path, encoding='utf-8', errors='replace'):
        m = pat.match(line)
        if m:
            syms.append((int(m.group(2), 16), m.group(1)))
    syms.sort()
    return syms


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('samples')
    ap.add_argument('t0', type=float)
    ap.add_argument('t1', type=float)
    ap.add_argument('--map', default=str(ROOT / 'build' / 'sonic06.map'))
    args = ap.parse_args()

    syms = load_map(args.map)
    starts = [s[0] for s in syms]

    def sym(rva):
        if not rva:
            return '-'
        i = bisect.bisect_right(starts, 0x140000000 + rva) - 1
        name = syms[i][1]
        m = re.match(r'\?([A-Za-z_0-9]+)@', name)
        return m.group(1) if m else name[:40]

    mods, leaves, chains, n = {}, collections.Counter(), collections.Counter(), 0
    for line in open(args.samples):
        if line.startswith('# mod '):
            _, _, i, p = line.split(' ', 3)
            mods[int(i)] = p.strip().replace('\\', '/').split('/')[-1]
            continue
        if line.startswith('#'):
            continue
        p = line.split()
        if len(p) != 7 or not args.t0 <= int(p[2]) / 1000 <= args.t1:
            continue
        n += 1
        mod = int(p[3], 16)
        leaf = sym(int(p[0], 16)) if mod == 0xFFFFFFFF else mods.get(mod, f'module {mod}')
        leaves[leaf] += 1
        chains[' < '.join([leaf] + [sym(int(x, 16)) for x in p[4:7]])] += 1
    print(f'{n} samples')
    for k, v in leaves.most_common(10):
        print(f'  {v:5d} {k}')
    print('with callers:')
    for k, v in chains.most_common(12):
        print(f'  {v:5d} {k}')


if __name__ == '__main__':
    main()
