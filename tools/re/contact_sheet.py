"""Tile the frame dumps of a run (CELLMARK_DUMP_DIR) into one PNG to look at.

    python tools/re/contact_sheet.py work/frames/run work/frames/run.png [--cols 5] [--width 320]

Each tile is labelled with its frame number and mean RGB: a run of mean=0,0,0
is a black screen (or a present with no draws, unless the run set
CELLMARK_DUMP_MINDRAWS=1).
"""
import argparse
import pathlib

from PIL import Image, ImageDraw, ImageStat


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("frames")
    ap.add_argument("out")
    ap.add_argument("--cols", type=int, default=5)
    ap.add_argument("--width", type=int, default=320)
    a = ap.parse_args()
    files = sorted(pathlib.Path(a.frames).glob("frame_*.bmp"))
    if not files:
        raise SystemExit(f"no frame_*.bmp in {a.frames}")
    tw = a.width
    th = tw * 9 // 16
    rows = (len(files) + a.cols - 1) // a.cols
    sheet = Image.new("RGB", (a.cols * tw, rows * (th + 14)), (40, 40, 40))
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGB")
        mean = ImageStat.Stat(im).mean
        x, y = (i % a.cols) * tw, (i // a.cols) * (th + 14)
        sheet.paste(im.resize((tw, th)), (x, y + 14))
        draw.text((x + 2, y + 1), "%s mean=%d,%d,%d" % (f.stem[6:], *map(int, mean)), fill=(255, 255, 0))
    sheet.save(a.out)
    print(f"{len(files)} frames -> {a.out}")


if __name__ == "__main__":
    main()
