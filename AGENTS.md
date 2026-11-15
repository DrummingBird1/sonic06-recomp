# sonic06-recomp (Soleanna)

Single source of truth for any AI agent working in this repository (Claude, Codex, Copilot, Cursor, Gemini, ChatGPT, Grok, OpenCode). `CLAUDE.md` only imports this file, and the git-excluded `AGENTS.local.md` when it exists on the machine. Keep this file self-contained and public-safe: machine paths and personal notes go in `AGENTS.local.md`, never here.

<!-- BEGIN core-rules v1 (generated from the shared core-rules.md; do not edit by hand) -->
## Core rules (apply to every agent)

- Read this file first. If `HANDOFF.local.md` exists next to it, read it too (local only, not in git).
- Never push, force-push or create remotes unless the owner explicitly asks. Work on a branch named `agent/<tool>-<topic>`; merge to `main` only through a pull request.
- Never commit or print secrets (`.env`, keys, tokens). Keep personal paths, emails and private repo names out of tracked files.
- Package managers: JavaScript uses `pnpm`, Python uses `uv`. Do not switch without being asked.
- Dev servers listen on `127.0.0.1` only, unless the owner asks otherwise.
- Do not delete or overwrite user data. Move it to an archive folder or ask first, and do a dry run before bulk changes.
- Make the smallest change that solves the task. Run this project's own check and test commands (see Run and Test in this file) and say what you ran and what you could not verify.
- If you have no access to the repository (a chat-only agent), return a unified diff or complete files, and list what the owner should verify.
<!-- END core-rules -->

## Purpose

Soleanna (this repository is `sonic06-recomp`) is an unofficial PC port of Sonic the Hedgehog (2006), the PS3 version (BLES00028), built by static recompilation with the ps3recomp toolkit. The project ships source code only, plus a launcher that builds the game on the player's PC from the player's own disc. A feasibility study and working notes are kept by the maintainer outside git.

## Type

- Windows project. Layout: `launcher/` (C#, WPF, .NET Framework 4.8; translations in `launcher/Strings/<code>.json`, `en.json` is the reference), `src/` (the port's own C code, such as `sonic06_config.c`), `patches/ps3recomp/` (the project's changes to ps3recomp, applied on top of a pinned commit), `tools/` (`build.ps1`, `relift.ps1`, `run.ps1`, `setup_ps3recomp.ps1`; `re/` has the investigation tools; `ci/` has the repository checks), `docs/images/` (pictures on the README).
- ps3recomp is pinned at commit `4e8f3ca` (v0.18.0). `tools/setup_ps3recomp.ps1` makes the working copy `work/ps3recomp` from a clone (`-Source`, the `PS3RECOMP_SOURCE` variable, or `deps/ps3recomp`, which it downloads from GitHub), with the patches applied.
- Toolchain: clang-cl from LLVM (23.1.4 when this was written), CMake and Ninja; the CRT and Windows SDK come from Visual Studio with the C++ tools, or from xwin in `deps/winsysroot` (the launcher downloads it and `tools/build.ps1` uses it when there is no Visual Studio). The Python environment is `uv` in `.venv` (Python 3.12).

## Game files (hard rule)

- Never commit, attach, upload or link game files or anything built from them: the ISO or disc folder, `EBOOT.BIN` and `EBOOT.ELF`, the decrypted ELF, translated code, SPU programs, frame or shader dumps, and the built `build/sonic06.exe`. `game/`, `work/` and `build/` hold such files; `deps/` holds downloaded tools. All four stay out of git. A check on every pull request enforces part of this.
- Screenshots of the game running in the port are fine in issues and as GitHub release attachments, never in git. Never use official SEGA art, logos, cover art or assets taken from the disc.
- Decrypt `game/EBOOT.BIN` with RPCS3's binary decryption (`rpcs3 --decrypt`, as the launcher does, or the matching menu entry). The output is written next to the input with an `.elf` extension, so do not decrypt a file that is already named `.elf` in the same folder. `hksputhreadconstraint.elf` (the Havok SPU program) is still encrypted; a copy to decrypt is in `game/spu_self/`.

## Run

- Needs Windows 10 or 11, the .NET SDK (for the launcher), RPCS3 and the player's own BLES00028 disc.
- `Sonic06.cmd` builds the launcher and opens it; Setup, "Set up everything" installs the tools, copies and decrypts the disc, downloads FFmpeg and ps3recomp, translates the game code and builds it (about 17 minutes the first time). Without a window: `dotnet build launcher -c Release`, then `launcher\bin\Release\net48\Launcher.exe --setup <iso or folder> <path to rpcs3.exe> <log file>`.
- Python environment: `uv pip install --python .venv/Scripts/python.exe -r work/ps3recomp/requirements.txt -r tools/re/requirements.txt`.
- Build and run: `tools\relift.ps1`, `tools\build.ps1`, `tools\run.ps1`, for example `pwsh tools/run.ps1 -Seconds 60 -Env 'PS3_VERBOSE=0;PAD_SCRIPT=12:0x4000'`.
- Run settings: always `PS3_VERBOSE=0` (the log slows the game down); `PAD_SCRIPT` presses buttons at given seconds; `CELLMARK_DUMP*` saves frames to an existing folder; `SONIC06_BOOT=stage` starts in the developer menu.
- Changing the toolkit: do not edit the clone you copy from. Edit and commit inside `work/ps3recomp`, run `tools/setup_ps3recomp.ps1 -Export` (it rewrites `patches/ps3recomp/*.patch`) and commit the patch files. `-Reset` rebuilds the working copy from the pinned commit and the patches.
- Temporary hooks in the translated code go only through `tools/re/hook.py`, which keeps a backup. The trace file is `src/zz_trace.c` (`src/zz_*` is git-ignored). Run `hook.py restore` before every commit, and never edit `work/recompiled` permanently.

## Test

- CI (`.github/workflows/checks.yml`) runs `python tools/ci/check_repo.py --selftest`, `python tools/ci/check_repo.py` (no game files, no oversized files, pictures only in `docs/images/` and `launcher/`, valid translations, no machine paths) and `dotnet build launcher -c Release`. The game itself cannot be built in CI because it needs a disc.
- There are no unit tests for the port. Verify by running it and measuring: a log line, a frame rate before and after, a screenshot. For the launcher, `tools/launcher_ui.ps1` clicks through its pages and `Launcher.exe --layout <file>` looks for cut text in all ten languages.
- Not verified while writing this file: the commands above come from `CONTRIBUTING.md`, the CI workflow and the project's own notes, not from a run.

## Reporting

- Say that something works only after you measured it: a run, a log, a screenshot or a number. "Should work" is not a result.
- Say explicitly what you did not test, in the report and in the docs.
- A check that found nothing must first be shown able to find something (a planted fault) before you trust "no problems".
- Performance comparisons: alternate A and B with the same exe and no other load on the machine.

## Working in parallel

- One agent that runs the game per checkout: `game/`, `work/`, `build/` and `gamedata/` belong to the folder and git does not move them. Two agents in one folder step on each other (a hook in `work/recompiled`, a build that replaces `build/sonic06.exe` mid-run, a save that changes the route).
- A git worktree does not help: it starts without the translated code and without the game. A second agent that runs the game needs its own clone and its own setup (about 17 minutes and 10 GB).
- Work that neither runs nor builds (docs, analysis of existing logs) can share a folder if it touches different files.
- Commit only your own files, with `git commit -- <files>`, so you do not carry someone else's changes.

## Rules

- The owner works in Hebrew: reply in Hebrew unless asked otherwise. Commit messages, code and public docs are in English.
- One topic per pull request, with what you measured and what you did not test. Toolkit changes go in as regenerated patch files, not as edits to `work/`.
- `main` is protected (no force push, no deletion, the `checks` workflow must pass) and everything pushed is public at once. Do not change the repository's visibility without the owner's explicit request.
- Distribution is source code only: `build/sonic06.exe` contains translated game code and is never published.
- The repository checks reject maintainer notes: `docs/` (except `docs/images/`), `.claude/`, `tools/behind_the_scenes.py` and any `*.local.md` file stay out of git. `AGENTS.md` and `CLAUDE.md` are tracked on purpose and must contain no machine paths.

## Do not

- Do not commit `.env`, keys, tokens or other secrets.
- Do not add large binaries (archives, video, audio, executables) to git; decide on LFS or external storage first.
- Do not push or create a remote without the owner's explicit approval.

## State

- Public repository, default branch `main`. Issues and Discussions are open for outside users; check the open issues before you start, because they are the task list of people outside the project.

## Handoff

- Recent changes, unpushed work and open items are in `HANDOFF.local.md`; machine paths, the shared toolkit copy and the maintainer's working notes are in `AGENTS.local.md`. Both are git-excluded and exist only on the owner's machine: read them when they are there, and do not copy their content into tracked files.
