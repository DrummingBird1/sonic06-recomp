"""Smoke test entries of the developer stage list, one run each.

    python tools/sweep.py boss-run '/ boss /'       # entries matching a regex
    python tools/sweep.py csc 'csc' --seconds 70

Each run boots with SONIC06_BOOT=stage, walks the menu blind, then holds the
stick forward and presses CROSS. The report lands in work/sweep/<name>/report.txt,
with a frame folder per entry and a sheet.png of all of them.
"""
import argparse
import pathlib
import re
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, 'tools/re')
from stagelist import load, path, presses
sys.path.insert(0, 'tools')
import audio_gaps

ROOT = pathlib.Path(__file__).resolve().parent.parent


def find_ffmpeg():
    """The FFmpeg 8 DLLs: the launcher's setting or its download."""
    dirs = []
    try:
        import json
        dirs.append(json.loads((ROOT / 'launcher.local.json').read_text(encoding='utf-8')).get('ffmpeg', ''))
    except (OSError, ValueError):
        pass
    dirs.append(str(ROOT / 'deps' / 'ffmpeg' / 'bin'))
    return next((d for d in dirs if d and list(pathlib.Path(d).glob('avcodec-*.dll'))), '')


FFMPEG = find_ffmpeg()
LOAD_SECONDS = 14
STILLS = 12


def latest_log():
    return max((ROOT / 'work/logs').glob('run-*.log'), key=lambda p: p.stat().st_mtime)


def run_entry(entries, entry, out, seconds, extra=()):
    menu = presses(entries, entry)
    play = menu[-1][0] + LOAD_SECONDS
    pad = menu + [(t, 0x4000) for t in range(play, seconds, 4)]
    wav = ROOT / 'work/audio/sweep.raw'
    env = {
        'PS3_VERBOSE': '0', 'SONIC06_BOOT': 'stage', 'GCM_FLIPCOUNT': '1', 'FRAME_BUDGET': '1',
        'PAD_SCRIPT_HOLD': '8', 'PAD_SCRIPT': ','.join(f'{t}:{m:#06x}' for t, m in pad),
        'PAD_STICK': f'128,0,128,128,{play},{seconds}',
        'AUDIO_WAV': wav.as_posix(), 'CELLMARK_DUMP': str(STILLS), 'CELLMARK_DUMP_SKIP': '60',
        'CELLMARK_DUMP_EVERY': str(seconds * 50 // STILLS), 'CELLMARK_DUMP_DIR': out.as_posix(),
        **dict(kv.split('=', 1) for kv in extra),
    }
    subprocess.run(['pwsh', 'tools/run.ps1', '-Seconds', str(seconds), '-FFmpeg', FFMPEG,
                    '-Env', ';'.join(f'{k}={v}' for k, v in env.items())],
                   cwd=ROOT, capture_output=True, text=True)
    log = latest_log()
    text = log.read_text(encoding='utf-8', errors='replace')
    flips = re.findall(r'\[flip\] (\d+) at (\d+)', text)
    budget = [ln for ln in text.splitlines() if ln.startswith('[BUDGET]') and ln.split()[-2] != '0']
    sound = sorted(set(re.findall(r"/sound/([\w./-]+\.at3)'", text)))
    return dict(log=log.name, crash=text.count('[CRASH]'), flips=flips[-1][0] if flips else '0',
                stills=len(list(out.glob('*.bmp'))), trunc=len(budget), sound=sound, loud=loudness(wav),
                gaps=audio_gaps.count(wav) if wav.exists() else -1)


def loudness(wav):
    """One character for each 5 seconds of output: '.' is silence."""
    if not wav.exists():
        return ''
    a = np.fromfile(wav, dtype='<f4')
    step = 5 * 48000 * 2
    blocks = [a[i:i + step] for i in range(0, len(a) - step + 1, step)]
    return ''.join('.' if np.sqrt(np.mean(b * b)) < 0.001 else 'x' for b in blocks)


def sheet(rows, dest, width=200):
    h = width * 9 // 16
    img = Image.new('RGB', (width * 6, (h + 14) * len(rows)), (40, 40, 40))
    draw = ImageDraw.Draw(img)
    for y, (name, folder) in enumerate(rows):
        top = y * (h + 14)
        draw.text((2, top + 1), name, fill=(255, 255, 0))
        for x, f in enumerate(sorted(folder.glob('*.bmp'))[-6:]):
            img.paste(Image.open(f).convert('RGB').resize((width, h)), (x * width, top + 14))
    img.save(dest)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name')
    ap.add_argument('pattern')
    ap.add_argument('--seconds', type=int, default=60)
    ap.add_argument('--env', action='append', default=[], help='KEY=VALUE for the run')
    ap.add_argument('--step', type=int, default=1, help='every Nth match, for a sample')
    a = ap.parse_args()
    entries = load()
    pat = re.compile(a.pattern, re.I)
    chosen = [e for e in entries if pat.search(' / '.join(path(e)))][::a.step]
    base = ROOT / 'work/sweep' / a.name
    base.mkdir(parents=True, exist_ok=True)
    rows = []
    with open(base / 'report.txt', 'w', encoding='utf-8') as rep:
        for i, e in enumerate(chosen):
            label = ' / '.join(path(e)).encode('ascii', 'replace').decode()
            out = base / f'{i:03d}'
            out.mkdir(exist_ok=True)
            for old in out.glob('*.bmp'):
                old.unlink()
            r = run_entry(entries, e, out, a.seconds + presses(entries, e)[-1][0], a.env)
            line = (f'{i:3d} crash={r["crash"]} flips={r["flips"]} stills={r["stills"]} '
                    f'trunc={r["trunc"]} gaps={r["gaps"]} sound={r["loud"]} {label} {r["log"]} {" ".join(r["sound"])}')
            print(line, flush=True)
            rep.write(line + '\n')
            rep.flush()
            rows.append((f'{i} {label}', out))
            sheet(rows, base / 'sheet.png')


if __name__ == '__main__':
    main()
