# sonic06-recomp

An unofficial PC port of the PlayStation 3 version of Sonic the Hedgehog (2006), BLES00028, made by static recompilation with [ps3recomp](https://github.com/sp00nznet/ps3recomp).

**This repository contains no game code or assets, and you need your own copy of the game.** The game is translated and built on your machine from your disc. Nothing here is affiliated with or endorsed by SEGA.

## Status

Work in progress, not ready for general use.

- Runs from the title screen through menus, movies, the town and the stages.
- Every boss and stage entry starts without a crash, in tests of about a minute each.
- No stage has been played to the end by a person yet.
- Tested on one Windows PC only.

## What you need

- Windows 10 or 11, 64-bit, with 8 GB of RAM or more. More memory builds faster: 8 GB builds two files at a time, 32 GB ten. So far the build has only been tried with 32 GB.
- About 10 GB of free disk space for the copied disc and the build, plus the build tools if you do not have them yet.
- [RPCS3](https://rpcs3.net), to decrypt the game's `EBOOT.BIN` on your machine.
- Your own BLES00028 disc, as an ISO or a folder.

The launcher installs the rest with winget, after asking: Visual Studio Build Tools (C++ and CMake), LLVM, Git, uv and PowerShell 7. It downloads FFmpeg 8 (an LGPL build) for the movies, and ps3recomp.

## Getting started

Run `Launcher.exe` (or `Sonic06.cmd` in a checkout) and press **Set up everything**. It lists what it will do, then:

1. installs the missing build tools,
2. copies your disc into `game/`,
3. decrypts `EBOOT.BIN` with your RPCS3,
4. downloads FFmpeg and ps3recomp,
5. translates the game code and builds it. This is the long part: 18 minutes on a fast PC, an hour or more on a typical one.

Then choose your settings on the **Play** tab (window size or fullscreen, VSync, language, sound, keyboard) and press **Play**.

A controller works through XInput. Without one, the keyboard is the pad: arrows move, Z jumps, Enter is START. The full table is in the handoff document.

From a console, `tools/launcher.ps1` has the same steps without the window.

## Reporting a problem

On the **Support** tab, **Create a bug report** saves a zip on your desktop: your PC's specs, the setup status and the last game logs. It has no game files, and your Windows user folder is replaced by `%USERPROFILE%`. Attach it to an issue with what you did and what you saw.

## Contributing

Start with [docs/HANDOFF.md](docs/HANDOFF.md), in Hebrew: the state of the project, the test recipes and the next steps. [docs/PHASE1.md](docs/PHASE1.md) is the history of every fix. The launcher's source is in `launcher/` (C#, .NET Framework 4.8: `dotnet build launcher -c Release`).

Keep game files, and anything derived from them, out of git: the ISO, `game/`, `work/` and the built executable.

## License

The code in this repository is under the [MIT license](LICENSE). It does not cover the game, which belongs to SEGA, or the tools the launcher downloads, which have their own licenses.
