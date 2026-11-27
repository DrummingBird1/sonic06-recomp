# Soleanna roadmap

The current state and the open questions, built from the changelog, the README and the open issues as of 2026-11-22. Nothing here is a promise or a date. The issues are the live task list. This file is updated with each release or when the plan changes.

## Where it stands

- **Latest version:** 0.3.0, for PCs that cannot hold 60 frames per second: the game can keep its real speed when frames drop, a 30 fps limit, 120 fps for fast screens (experimental), a "Weak PC" preset, exclusive fullscreen, a shadows switch, a frame rate counter, and fewer sound dropouts on PCs with few cores. It also fixes the build stopping with "'cmake' is not recognized" (#13). See [CHANGELOG.md](CHANGELOG.md).
- **What runs:** the title screen, menus and movies, the story to Soleanna's town, and every boss and stage entry, each started without a crash in tests of about a minute.
- **Outside reports:** a player reported clearing Sonic's whole episode with an S rank on a GT730 and an i5-4590 (#16). The same player reported hit sounds cutting out, which 0.3.0 addresses, and broken UI in some stages, which is not investigated yet.

## Now: confirm 0.3.0 on other PCs

- **#13:** fixed in 0.3.0 by finding CMake by its full path. Not yet confirmed by the reporter.
- **#9, #10, #11:** fixed in 0.2.0. They stay open until the reporters confirm.
- **#15, #16:** the full-speed option and the sound fix were measured on the maintainer's PC only, the sound one with 4 cores to stand in for an i5-4590. Reports from weak PCs decide whether full speed can become the default.
- **#14, black screen:** the issue has only the empty template. It needs details or a bug report from the reporter.
- **#5, a clean PC:** the full setup has not been run on a fresh Windows install yet.

## Next: open questions

- **Broken UI in some stages (#16):** which stages, and whether it is the same on a fast PC.
- **More than 60 frames per second:** with the game moving by real time, the remaining limit is a fixed wait of 15 ms between frames inside the game's own code. Untried.
- **Fewer particles, a draw distance setting, a DirectX 11 backend (#11, #17):** each needs work inside the game's renderer. Turning shadows off did not raise the frame rate on the weak card it was measured on (Intel UHD 770), so the gain of such options is not known in advance.
- **#7:** testing on AMD, Intel and weaker NVIDIA cards.
- **#2:** testing with a real controller. SDL2 support is in, but no real controller has been tried by the maintainer.

## Known bugs and gaps

- **#3:** Kingdom Valley's water looks very bright from steep angles. It may be correct; it needs a comparison with the original game.
- **#4:** Silver's attract demo shows without the world after a game over.
- **#1:** no stage has been played to the end on the maintainer's PC. #16 is the first outside report of a finished episode.
- **Havok's SPU program** (`hksputhreadconstraint.elf`) is still encrypted and is not translated.
- **Not tested:** a full game from start to finish on more than one PC, sound effect balance in stages, and sync between sound and picture.

## Help wanted

- **#6:** review the launcher's translations (eleven languages, Simplified Chinese added in 0.4.0).
- **#1, #2, #5, #7:** play, test controllers, try a clean PC, and try other hardware.
- **Weak PCs:** try the "Weak PC" preset and say how it plays.

## Tracking

The issues on GitHub are the task list. There is no project board: none was created, because the maintainer's board workspace is full.
