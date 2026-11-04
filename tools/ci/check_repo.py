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
5. No path from someone's own PC in a text file: a user profile (C:\\Users\\name) or a
   folder on another drive (D: to Z:). Machine paths belong in AGENTS.local.md and
   launcher.local.json, which stay out of git.
"""
import json, pathlib, re, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
FORBIDDEN = re.compile(r"^(game|work|build|deps|dist)/|\.(iso|elf|self|sprx|bin|pkg|pup|bmp|dxbc)$"
                       r"|_dump\.hlsl$|\.local\.(md|json)$", re.I)
MEDIA = re.compile(r"\.(png|jpe?g|gif|webp|bmp|mp4|webm|mov|avi|ico)$", re.I)
MEDIA_OK = ("docs/images/", "launcher/")
MAX_BYTES = 4 * 1024 * 1024
PLACEHOLDER = re.compile(r"\{\d+\}")
LOCAL_PATH = re.compile(r"\b[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9._-]+|\b[D-Zd-z]:[\\/]+[A-Za-z0-9_]")
TEXT = re.compile(r"\.(md|txt|py|ps1|cmd|bat|cs|xaml|json|ya?ml|c|cpp|h|hpp|cmake|patch|csproj|manifest)$|CMakeLists\.txt$", re.I)


def text_problems(files):
    """files: {path: text}. A path to someone's own folders."""
    bad = []
    for p, text in files.items():
        for n, line in enumerate(text.splitlines(), 1):
            m = LOCAL_PATH.search(line)
            if m:
                bad.append(f"{p}:{n}: '{m[0]}' is a path on someone's own PC")
    return bad


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
    local_bad = text_problems({"a.md": r"see C:\Users\someone\Desktop", "b.ps1": r"$x = 'E:\Games\iso'", "c.py": "D:/stuff/x"})
    local_ok = text_problems({"d.md": r"C:\Program Files\LLVM\bin and %USERPROFILE% and C:\Windows\Fonts"})
    ok = (fails == set(must_fail) and passes == set(must_pass) and len(strings) == 3
          and len(local_bad) == 3 and not local_ok)
    print(f"selftest: {len(fails)}/{len(must_fail)} bad paths caught, {len(passes)}/{len(must_pass)} good paths let through, "
          f"{len(strings)}/3 string problems caught, {len(local_bad)}/3 local paths caught, {len(local_ok)} false alarms")
    return 0 if ok else 1


def main():
    if "--selftest" in sys.argv:
        return selftest()
    paths = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout.split("\n")
    paths = [p for p in paths if p]
    bad = path_problems(paths, lambda p: (ROOT / p).stat().st_size if (ROOT / p).exists() else 0)
    bad += string_problems(load_tables())
    texts = {}
    for p in paths:
        # this file holds examples of what it looks for
        if TEXT.search(p) and (ROOT / p).exists() and p != "tools/ci/check_repo.py":
            texts[p] = (ROOT / p).read_text(encoding="utf-8", errors="replace")
    bad += text_problems(texts)
    for b in bad:
        print("::error::" + b if "GITHUB_ACTIONS" in __import__("os").environ else b)
    print(f"{len(paths)} tracked files, {len(bad)} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
