# sonic06-recomp

An unofficial PC port of the PlayStation 3 version of Sonic the Hedgehog (2006), BLES00028, made by static recompilation with [ps3recomp](https://github.com/sp00nznet/ps3recomp).

**This repository contains no game code or assets, and you need your own copy of the game.** The game is translated and built on your machine from your disc. Nothing here is affiliated with or endorsed by SEGA.

## Status

Work in progress, not ready for general use.

- Runs from the title screen through menus, movies, the town and the stages.
- Every boss and stage entry starts without a crash, in tests of about a minute each.
- No stage has been played to the end by a person yet.
- The first time a scene appears there can be a short pause while its shaders compile; after that they are read from `build/shadercache`. On the Graphics tab, "Compile shaders in the background" removes the pause; some objects can then appear a few frames late.
- Tested on one Windows PC only.

## What you need

- Windows 10 or 11, 64-bit, with 8 GB of RAM or more. The launcher builds as many files at a time as the memory allows: one with 8 GB, four with 16 GB, ten with 32 GB. On the 20-thread PC it was made on, ten at a time took 14 minutes, and two at a time took 23 minutes with a peak of 5 GB. A PC with less memory has not been tried yet.
- About 10 GB of free disk space for the copied disc and the build, plus the build tools if you do not have them yet.
- [RPCS3](https://rpcs3.net), to decrypt the game's `EBOOT.BIN` on your machine.
- Your own BLES00028 disc, as an ISO or a folder.

The launcher installs the rest after asking: LLVM, CMake, Ninja, Git, uv and PowerShell 7 with winget, and Microsoft's C runtime and Windows SDK (about 630 MB, downloaded from Microsoft with [xwin](https://github.com/Jake-Shadle/xwin)) once you accept Microsoft's license. Visual Studio is not needed; if you have it with the C++ tools, the build uses it instead. It also downloads FFmpeg 8 (an LGPL build) for the movies, SDL2 2.32.10 (zlib license) for controllers, and ps3recomp.

## Getting started

The launcher is called **Soleanna**, after the game's city. It ships no artwork of the game: once your disc is copied, it shows the pictures from the disc itself (`PS3_GAME/PIC0.PNG` and `ICON0.PNG`).

Run `Launcher.exe` (or `Sonic06.cmd` in a checkout), open **Setup** and press **Set up everything**. It lists what it will do, then:

1. installs the missing build tools,
2. copies your disc into `game/`,
3. decrypts `EBOOT.BIN` with your RPCS3,
4. downloads FFmpeg and ps3recomp,
5. translates the game code and builds it. This is the long part: about 17 minutes on a fast PC with 32 GB, and longer on a slower one or with less memory.

Then press **Play**. The other pages:

- **Graphics:** your own preset next to the three built in, a graphics card picker when the PC has more than one, window or borderless fullscreen, window size, aspect ratio (keep 16:9 or stretch), VSync, the internal resolution, the upscaling filter (smooth, sharp pixels, bicubic, nearest), sharpening, FXAA, texture filtering and brightness, with performance, balanced and quality presets. The internal resolution is the size the game draws at: 1280×720 as on the PS3, up to 3840×2160, or automatic, which matches your window. On the RTX 4070 SUPER it was tested on, 3840×2160 ran as fast as 1280×720; a weaker graphics card has not been tried.
- **Settings:** the stick dead zone, F12 for a screenshot (saved in Pictures\Sonic06), the launcher's language and the game's, sound, the keyboard as a controller, and whether Esc closes the game (off by default, so pausing never quits).
- **Keyboard and mouse:** every pad control can take up to two keys or mouse buttons, and the mouse can move the camera (the right stick), with a sensitivity slider.
- **Support:** update and rebuild, a bug report, the logs.

The launcher speaks English, Hebrew, Spanish, French, German, Italian, Portuguese (Brazil), Japanese, Russian and Arabic, and starts in your Windows language. The translations have not been checked by native speakers yet: corrections to `launcher/Strings/*.json` are welcome.

Controllers work through SDL2, which the launcher downloads: Xbox, DualShock 4, DualSense and Switch Pro. Without SDL2 the game falls back to XInput, which sees Xbox-style controllers only. Without a controller, the keyboard is the pad: by default arrows move, Z jumps, Enter is START, and the Settings page changes any of it.

From a console, `tools/launcher.ps1` has the same steps without the window.

## Reporting a problem

On the **Support** tab, **Create a bug report** saves a zip on your desktop: your PC's specs, the setup status and the last game logs. It has no game files, and your Windows user folder is replaced by `%USERPROFILE%`. Attach it to an issue with what you did and what you saw.

## Contributing

Start with [docs/HANDOFF.md](docs/HANDOFF.md), in Hebrew: the state of the project, the test recipes and the next steps. [docs/PHASE1.md](docs/PHASE1.md) is the history of every fix. The launcher's source is in `launcher/` (C#, .NET Framework 4.8: `dotnet build launcher -c Release`).

Keep game files, and anything derived from them, out of git: the ISO, `game/`, `work/` and the built executable.

## License

The code in this repository is under the [MIT license](LICENSE). It does not cover the game, which belongs to SEGA, or the tools the launcher downloads, which have their own licenses.
