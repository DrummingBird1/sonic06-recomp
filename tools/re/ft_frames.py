"""Long frames in a PS3_FTRACE timeline, and what the main thread and the RSX did in them.

    python tools/re/ft_frames.py work/ftrace/scene.txt [--long 40] [--main 1]

Record the trace with PS3_FTRACE=<start s>:<seconds>:<path> (the toolkit's
include/ps3emu/ps3_ftrace.h). A frame is the time between two guest flip
requests. The report gives the frame times in vblanks (2 is 30 fps), each
frame over --long ms with the main thread's blocking calls and the RSX side
(drain, encode, present, GPU waits), the same for an average normal frame,
and the buffer-full callback's ring wraps (kind 16) with how long they waited.
"""
import argparse
import collections
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / 'work' / 'ps3recomp' / 'tools'))
import ftrace_report as fr  # noqa: E402

RECYCLE = 16
RSX_KINDS = (fr.DRAIN, fr.PRESENT, fr.GPU_ENCODE, fr.GPU_WAIT, fr.SEMA)
MAIN_KINDS = (fr.SYSCALL, fr.USLEEP, fr.HLE)


def what(spans, t0, t1, who, kinds):
    out = collections.Counter()
    for k, w, a, b, s0, s1 in spans:
        if s1 < t0 or s0 > t1 or k not in kinds or (who is not None and w != who):
            continue
        ov = fr.overlap(s0, s1, t0, t1)
        if ov <= 0:
            continue
        if k in (fr.SYSCALL, fr.USLEEP):
            name = 'usleep' if k == fr.USLEEP else fr.SYSCALL_NAMES.get(a, f'sc{a}')
            out[f'{name}@{b:08X}'] += ov
        elif k == fr.HLE:
            out[f'hle {a:08X}'] += ov
        else:
            out[{fr.DRAIN: 'drain', fr.PRESENT: 'present', fr.GPU_ENCODE: 'encode',
                 fr.GPU_WAIT: f'gpu wait {a}', fr.SEMA: 'acquire'}[k]] += ov
    return out


def fmt(c, n=1):
    return ', '.join(f'{k} {v / n:.1f}' for k, v in c.most_common(4))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('trace')
    ap.add_argument('--long', type=float, default=40.0, help='frames over this many ms are listed')
    ap.add_argument('--main', type=int, default=1, help='the main PPU thread id')
    ap.add_argument('--list', type=int, default=25, help='long frames listed at most')
    args = ap.parse_args()

    _, _, spans = fr.load(args.trace)
    flips = [s[4] for s in spans if s[0] == fr.FLIP_REQ]
    if len(flips) < 2:
        sys.exit('fewer than two flips in the trace')
    iv = [b - a for a, b in zip(flips, flips[1:])]
    slots = collections.Counter(round(x / 16.667) for x in iv)
    print(f'{len(flips)} guest flips over {(flips[-1] - flips[0]) / 1000:.1f} s'
          f' = {(len(flips) - 1) * 1000 / (flips[-1] - flips[0]):.2f}/s')
    print('frame times in vblanks:', dict(sorted(slots.items())))
    print(f'over 2 vblanks: {sum(n for k, n in slots.items() if k > 2)} frames,'
          f' {sum((k - 2) * n for k, n in slots.items() if k > 2)} vblanks lost;'
          f' 1 vblank: {slots.get(1, 0)}')

    rec = [s for s in spans if s[0] == RECYCLE]
    if rec:
        waits = [s[5] - s[4] for s in rec]
        print(f'ring wraps: {len(rec)}, waited {sum(waits):.0f} ms in all, longest {max(waits):.1f} ms')

    long = [(a, b) for a, b in zip(flips, flips[1:]) if b - a > args.long]
    print(f'\n{len(long)} frames over {args.long} ms')
    for a, b in long[:args.list]:
        m = what(spans, a, b, args.main, MAIN_KINDS)
        r = what(spans, a, b, None, RSX_KINDS)
        print(f'  {a / 1000:8.3f} s {b - a:6.1f} ms  main [{fmt(m)}]  rsx [{fmt(r)}]')
    norm = [(a, b) for a, b in zip(flips, flips[1:]) if 30 < b - a < 36]
    if norm:
        tm, tr = collections.Counter(), collections.Counter()
        for a, b in norm:
            tm.update(what(spans, a, b, args.main, MAIN_KINDS))
            tr.update(what(spans, a, b, None, RSX_KINDS))
        print(f'\na normal frame (mean of {len(norm)}): main [{fmt(tm, len(norm))}]  rsx [{fmt(tr, len(norm))}]')


if __name__ == '__main__':
    main()
