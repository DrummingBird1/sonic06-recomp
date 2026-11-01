# sonic06-recomp

An unofficial PC port of the PlayStation 3 version of Sonic the Hedgehog (2006), BLES00028, made by static recompilation with [ps3recomp](https://github.com/sp00nznet/ps3recomp).

**This repository contains no game code or assets, and you need your own copy of the game.** The game is translated and built on your machine from your disc image. Nothing here is affiliated with or endorsed by SEGA.

## Status

Work in progress, not ready for general use.

- Runs from the title screen through menus, movies, the town and the stages.
- Every boss and stage entry starts without a crash, in tests of about a minute each.
- No stage has been played to the end by a person yet, and the sound has only been measured, not heard.
- Tested on one Windows PC only.

## What you need

- Windows 11 x64 with 32 GB of RAM, for the build
- [LLVM](https://llvm.org) with clang-cl, and Visual Studio with the C++ workload
- Python 3.12 and [uv](https://github.com/astral-sh/uv)
- A checkout of ps3recomp at commit `4e8f3ca`
- An FFmpeg 8 shared build, DLLs and headers, for the movies
- [RPCS3](https://rpcs3.net), to decrypt `EBOOT.BIN` from your own firmware install
- Your own BLES00028 disc image

## Getting started

Run `Sonic06.cmd`. The window shows what is missing and offers the steps:

1. **Choose game ISO** copies the disc into `game/`.
2. Decrypt `game/EBOOT.BIN` in RPCS3, with Utilities > Decrypt PS3 Binaries. The output is `game/EBOOT.elf`.
3. **Choose FFmpeg folder** points to the folder with `avcodec-*.dll`.
4. **Build** translates and compiles the game. It took 18 minutes from a clean clone.
5. **Play**.

From a console, the same steps are in `tools/launcher.ps1`. Pass your checkout with `-Ps3Recomp` and the FFmpeg headers with `-FFmpegInclude` once, and they are remembered. Python setup is in [docs/HANDOFF.md](docs/HANDOFF.md).

A controller works through XInput. Without one, the keyboard is the pad: arrows move, Z jumps, Enter is START. The full table is in the handoff document.

## Contributing

Start with [docs/HANDOFF.md](docs/HANDOFF.md), in Hebrew: the state of the project, the test recipes and the next steps. [docs/PHASE1.md](docs/PHASE1.md) is the history of every fix.

Keep game files, and anything derived from them, out of git: the ISO, `game/`, `work/` and the built executable.

## License

No license has been chosen for this repository yet.
