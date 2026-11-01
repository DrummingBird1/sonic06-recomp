"""Find dropouts in an AUDIO_WAV capture (f32le stereo 48 kHz, 256-frame blocks).

    python tools/audio_gaps.py work/audio/a.raw

A dropout is an all-zero block with sound on both sides: the mixer read a block
the game had not written yet (cellAudio zeroes each block after reading it).
Loudness over seconds cannot see these: 7 silent blocks in every 15 still
average out loud (PHASE1.md, section 39). Prints the total, then per 5 s
window the dropped and the loud blocks.
"""
import sys

import numpy as np

BLK = 256
RATE = 48000


def blocks(path):
    a = np.fromfile(path, dtype='<f4')
    n = len(a) // (BLK * 2)
    return a[:n * BLK * 2].reshape(n, BLK * 2)


def find_gaps(b, near=4):
    """Per block: is it a dropout? Zero, with a loud block within `near` on each side."""
    zero = ~np.any(b != 0.0, axis=1)
    loud = np.abs(b).max(axis=1) > 0.01
    gap = np.zeros(len(b), bool)
    for i in np.nonzero(zero)[0]:
        if loud[max(0, i - near):i].any() and loud[i + 1:i + near + 1].any():
            gap[i] = True
    return gap, zero, loud


def count(path):
    return int(find_gaps(blocks(path))[0].sum())


def main():
    b = blocks(sys.argv[1])
    gap, zero, loud = find_gaps(b)
    n = len(b)
    print(f'{sys.argv[1]}: {n} blocks ({n * BLK / RATE:.1f}s), zero {zero.sum()}, gaps {gap.sum()}')
    per = int(5 * RATE / BLK)
    cells = [f'{s * BLK // RATE:4d}s:{gap[s:s + per].sum():3d}/{loud[s:s + per].sum():3d}' for s in range(0, n, per)]
    for k in range(0, len(cells), 8):
        print('  ' + '  '.join(cells[k:k + 8]))


if __name__ == '__main__':
    main()
