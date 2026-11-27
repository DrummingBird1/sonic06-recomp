# Soleanna roadmap

The current state and the open questions, built from the changelog, the README and the open issues as of 2026-11-27. Nothing here is a promise or a date. The issues are the live task list. This file is updated with each release or when the plan changes.

## Where it stands

- **Latest version:** 0.4.0: setup no longer stops on a leftover link to a removed tool (#26), exclusive fullscreen goes back to borderless when the driver cannot show frames (#23), and the launcher speaks Simplified Chinese (#22). 0.3.0 brought the options for PCs that cannot hold 60 frames per second: real speed when frames drop, 30 and 120 fps, a "Weak PC" preset, exclusive fullscreen, a shadows switch and a frame rate counter. See [CHANGELOG.md](CHANGELOG.md).
- **What runs:** the title screen, menus and movies, the story to Soleanna's town, and every boss and stage entry, each started without a crash in tests of about a minute.
- **Outside reports:** a player with a GT730 and an i5-4590 cleared Sonic's episode with S ranks (#16) and Silver's at 100% (#25). They see about 17 fps in Soleanna town (#24). On the maintainer's PC the town holds 57-60 fps even on one CPU core, so the limit there looks like the graphics card or its DirectX 12 driver; a log from that PC was asked for in #24.

## Now: confirm 0.4.0 on other PCs

- **#26, #23:** fixed in 0.4.0, each reproduced or tested on the maintainer's PC only. Not yet confirmed by the reporters.
- **#24:** the GT730 log decides how much a DirectX 11 backend would help.
- **#9, #10, #11:** fixed in 0.2.0. They stay open until the reporters confirm.
- **#15, #16:** the full-speed option and the sound fix were measured on the maintainer's PC only, the sound one with 4 cores to stand in for an i5-4590. Reports from weak PCs decide whether full speed can become the default.
- **#14, black screen:** the issue has only the empty template. It needs details or a bug report from the reporter.
- **#5, a clean PC:** the full setup has not been run on a fresh Windows install yet.

## Next: a DirectX 11 backend (0.5.0, experimental)

- Asked for in #11, #16, #17 and #24 for old cards like the GT730. The DirectX 12 backend already compiles its shaders to Shader Model 5.0 and uses nothing DirectX 11 lacks, so the plan is a second backend next to it, chosen in the launcher, built and checked stage by stage against the DirectX 12 picture. It is the biggest piece of work so far and has no date.

## Open questions

- **Broken UI in some stages (#16):** which stages, and whether it is the same on a fast PC.
- **More than 60 frames per second:** with the game moving by real time, the remaining limit is a fixed wait of 15 ms between frames inside the game's own code. Untried.
- **Fewer particles, a draw distance setting (#11, #17):** each needs work inside the game's renderer. Turning shadows off did not raise the frame rate on the weak card it was measured on (Intel UHD 770), so the gain of such options is not known in advance.
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
