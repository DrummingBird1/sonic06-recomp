"""The repository checks that CI runs on every push and pull request.

    python tools/ci/check_repo.py            # the tracked files
    python tools/ci/check_repo.py --selftest # proves each check can fail

1. Nothing from the game and nothing built from it: the project's rule (AGENTS.md).
   Paths under game/, work/, build/, deps/ or dist/; disc and executable images; frame
   and shader dumps; this machine's local files.
2. No file over 4 MB: everything the project tracks is text, small pictures or icons.
3. Pictures and video only in docs/images and launcher/: the drawn banner, the logo
   and the launcher's own screenshots. Game screenshots go to releases, not git.
4. The launcher's translations: every file parses, has no key that en.json lacks
   (a typo would silently fall back to English), and keeps en.json's {0} placeholders.
"""
import json, pathlib, re, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
FORBIDDEN = re.compile(r"^(game|work|build|deps|dist)/|\.(iso|elf|self|sprx|bin|pkg|pup|bmp|dxbc)$"
                       r"|_dump\.hlsl$|\.local\.(md|json)$", re.I)
MEDIA = re.compile(r"\.(png|jpe?g|gif|webp|bmp|mp4|webm|mov|avi|ico)$", re.I)
MEDIA_OK = ("docs/images/", "launcher/")
MAX_BYTES = 4 * 1024 * 1024
PLACEHOLDER = re.compile(r"\{\d+\}")


def path_problems(paths, size_of):
    bad = []
    for p in paths:
        if FORBIDDEN.search(p):
            bad.append(f"{p}: game files and anything built from them stay out of git")
        elif MEDIA.search(p) and not p.startswith(MEDIA_OK):
            bad.append(f"{p}: pictures and video belong in docs/images or launcher/ (game screenshots go to releases)")
        elif size_of(p) > MAX_BYTES:
            bad.append(f"{p}: {size_of(p) // 1024} KB, over the 4 MB limit")
    return bad


def string_problems(tables):
    """tables: {code: dict or exception}; en is the reference."""
    bad, en = [], tables.get("en")
    if not isinstance(en, dict):
        return ["launcher/Strings/en.json: missing or not valid JSON"]
    for code, t in sorted(tables.items()):
        if isinstance(t, Exception):
            bad.append(f"launcher/Strings/{code}.json: not valid JSON ({t})"); continue
        for k, v in t.items():
            if k not in en:
                bad.append(f"launcher/Strings/{code}.json: key '{k}' is not in en.json")
            elif sorted(PLACEHOLDER.findall(str(v))) != sorted(PLACEHOLDER.findall(str(en[k]))):
                bad.append(f"launcher/Strings/{code}.json: '{k}' has placeholders {PLACEHOLDER.findall(str(v))}, en.json has {PLACEHOLDER.findall(str(en[k]))}")
    return bad


def load_tables():
    out = {}
    for f in sorted((ROOT / "launcher" / "Strings").glob("*.json")):
        try:
            out[f.stem] = json.loads(f.read_text(encoding="utf-8"))
        except Exception as e:
            out[f.stem] = e
    return out


def selftest():
    sizes = {"docs/big.md": MAX_BYTES + 1}
    must_fail = ["game/disc/PS3_GAME/USRDIR/x.arc", "work/recompiled/a.c", "build/sonic06.exe", "EBOOT.BIN",
                 "foo/EBOOT.ELF", "disc.iso", "frame_000.bmp", "vs_12_dump.hlsl", "AGENTS.local.md",
                 "docs/shot.png", "README-clip.mp4", "docs/big.md"]
    must_pass = ["docs/images/banner.png", "launcher/app.ico", "launcher/logo.png", "AGENTS.local.example.md",
                 "src/sonic06_config.c", "docs/PHASE1.md"]
    fails = {p for p in must_fail if path_problems([p], lambda q: sizes.get(q, 10))}
    passes = {p for p in must_pass if not path_problems([p], lambda q: sizes.get(q, 10))}
    strings = string_problems({"en": {"a": "x {0}"}, "de": {"a": "y", "typo": "z"}, "fr": ValueError("bad")})
    ok = fails == set(must_fail) and passes == set(must_pass) and len(strings) == 3
    print(f"selftest: {len(fails)}/{len(must_fail)} bad paths caught, {len(passes)}/{len(must_pass)} good paths let through, "
          f"{len(strings)}/3 string problems caught")
    return 0 if ok else 1


def main():
    if "--selftest" in sys.argv:
        return selftest()
    paths = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout.split("\n")
    paths = [p for p in paths if p]
    bad = path_problems(paths, lambda p: (ROOT / p).stat().st_size if (ROOT / p).exists() else 0)
    bad += string_problems(load_tables())
    for b in bad:
        print("::error::" + b if "GITHUB_ACTIONS" in __import__("os").environ else b)
    print(f"{len(paths)} tracked files, {len(bad)} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
