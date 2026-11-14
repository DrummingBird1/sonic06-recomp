# Contributing to Soleanna

Thanks for looking. Soleanna (the repository is `sonic06-recomp`) is a static recompilation of the PS3 version of Sonic the Hedgehog (2006). Help is welcome in four ways, and you don't need to write code for the first two.

1. **Try it on your PC** and fill in a [hardware report](https://github.com/DrummingBird1/sonic06-recomp/issues/new?template=hardware.md). It has been tested on one PC so far, so every report from other hardware counts.
2. **Play a stage to the end**, or as far as you can, and [report](https://github.com/DrummingBird1/sonic06-recomp/issues/new?template=bug.md) what breaks. Attach the zip from the launcher's **Support → Create a bug report**.
3. **Fix or improve a translation** in `launcher/Strings/<code>.json`. English (`en.json`) is the reference; nobody has reviewed the other nine yet.
4. **Change the code**: the launcher, the port's own code, or the toolkit patches.

## The rule that matters most

**Never commit, attach or link game files, or anything built from them.** That means the ISO or disc folder, `EBOOT.BIN` and `EBOOT.ELF`, the translated code, SPU programs, frame or shader dumps, and the built `sonic06.exe`. It also covers `game/`, `work/` and `build/`, which git ignores. The project only works as source code that each player builds from their own disc, so a PR or issue with game files in it is closed. A check on every PR enforces part of this.

Screenshots of the game running in the port are fine in issues. The project's own page shows a few as release attachments, never in git, and never official SEGA art, logos or assets taken from the disc.

## Building from source

You need Windows 10 or 11, the [.NET SDK](https://dotnet.microsoft.com/download) to build the launcher, [RPCS3](https://rpcs3.net) and your own BLES00028 disc.

```bat
Sonic06.cmd
```

`Sonic06.cmd` builds the launcher and opens it. **Setup → Set up everything** installs the rest (LLVM, CMake, Ninja, Git, uv, PowerShell 7, and Microsoft's CRT and SDK through xwin), copies and decrypts your disc, downloads FFmpeg, SDL2 and ps3recomp, translates the game code and builds it. The first time takes about 17 minutes on a fast PC.

Without the window:

```bat
dotnet build launcher -c Release
launcher\bin\Release\net48\Launcher.exe --setup <iso or folder> <path to rpcs3.exe> <log file>
```

## Where things are

| Path | What |
|---|---|
| `launcher/` | The Soleanna launcher: C#, WPF, .NET Framework 4.8 |
| `src/` | The port's own code: settings for this game (`sonic06_config.c`) and handlers the toolkit lacks |
| `patches/ps3recomp/` | The project's changes to ps3recomp, `0001` to `0047`, applied on top of the pinned commit |
| `tools/` | `build.ps1`, `relift.ps1` and `run.ps1`; `re/` has the investigation tools; `sweep.py` runs every stage |
| `docs/images/` | The pictures on the README |
| `game/`, `work/`, `build/` | Local only: your disc, the decrypted and translated code, the build |

Each fix is explained in its commit message and in its patch file. Issues and PRs in English are welcome.

## The toolkit and its patches

ps3recomp is pinned at commit `4e8f3ca` (v0.18.0). `tools/setup_ps3recomp.ps1` makes a working copy in `work/ps3recomp`, at that commit with the patches from `patches/ps3recomp/` applied. To change the toolkit:

1. Edit and commit inside `work/ps3recomp`.
2. Run `tools/setup_ps3recomp.ps1 -Export`, which rewrites `patches/ps3recomp/*.patch`.
3. Commit the patch files in this repository.

`-Reset` rebuilds the working copy from the pinned commit and the patches.

## Running and testing

```powershell
pwsh tools/run.ps1 -Seconds 60 -Env 'PS3_VERBOSE=0;PAD_SCRIPT=12:0x4000'
```

- **`PS3_VERBOSE=0`**: always, or the log slows the game down.
- **`PAD_SCRIPT`**: presses buttons at given seconds.
- **`CELLMARK_DUMP*`**: saves frames.
- **`SONIC06_BOOT=stage`**: starts in the developer menu, the quickest way into any stage.

The settings the port starts with, and why, are in `src/sonic06_config.c`. For the launcher, `tools/launcher_ui.ps1` clicks through its pages, and `Launcher.exe --layout <file>` looks for cut text in all ten languages.

Temporary hooks in the translated code go through `tools/re/hook.py`, which keeps a backup. Run `hook.py restore` before you commit.

## Working with an AI coding agent

Claude Code, Codex and similar agents are welcome, and much of the project was written with one. There is no `AGENTS.md` or `CLAUDE.md` in the repository on purpose: the maintainer's working notes stay local, and the checks reject them. This file is the instructions; point your agent at it. Notes of your own for an agent go in a file git ignores, for example one listed in `.git/info/exclude`.

What an agent has to know besides the rule above:

- **Measured, not assumed.** Something works when a run, a log line, a frame or a number shows it. Say what wasn't tested. When a check finds nothing, first show that it can find something, for example with a planted fault.
- **One agent that runs the game per checkout.** `game/`, `work/`, `build/` and `gamedata/` belong to the folder, and git doesn't carry them. Two agents in one folder collide: a hook in `work/recompiled`, a build that replaces `build/sonic06.exe` while it runs, a save that changes the story route. A git worktree starts without the translated code or the game, so a second agent that runs the game needs its own clone and its own setup, as in [Building from source](#building-from-source), with about 10 GB of disk. Agents that only read logs or edit text can share a folder if they don't touch the same files.
- **Commit only your own files**, with `git commit -- <files>`, so another agent's changes don't ride along. Run `python tools/ci/check_repo.py` first: it is the same check CI runs.
- **The toolkit changes only through `work/ps3recomp` and `-Export`**, never in the ps3recomp clone it was copied from.
- **Timing comparisons:** alternate A and B with the same exe, with nothing else loading the PC.
- **Two traps.** A save in `gamedata/` sends EPISODE SELECT straight to the town, so move it aside when you measure the story. In Git Bash, an argument that starts with `/`, like a `sweep.py` pattern, turns into a Windows path: run it with `MSYS_NO_PATHCONV=1`.
- **Where the state is:** the Status table in the README, [CHANGELOG.md](CHANGELOG.md), and the open [issues](https://github.com/DrummingBird1/sonic06-recomp/issues). The issues are the to-do list.

## Pull requests

- **One topic per PR.** Write what you measured: a log line, a frame rate before and after, a screenshot. "Should work" isn't a result.
- **Say what you didn't test.**
- **Commit messages in English.**
- **The checks:** they build the launcher, look for game files and oversized files, and validate the translations. They have to pass.
- **Toolkit changes** go in as regenerated patch files, not as edits to `work/`.
