# Soleanna roadmap

The current state and the open questions, built from the changelog, the README and the open issues as of 2026-11-21. Nothing here is a promise or a date. The issues are the live task list. This file is updated with each release or when the plan changes.

## Where it stands

- **Latest release:** 0.2.0 (2026-11-13). It fixes setup on other PCs. Before it, the first outside users could not reach the game (#9, #10, #11). The game itself is the same as in 0.1.0. See [CHANGELOG.md](CHANGELOG.md).
- **What runs:** the title screen, menus and movies, the story to Soleanna's town, and every boss and stage entry, each started without a crash in tests of about a minute.
- **Outside reports:** on 2026-11-15 a player reported clearing Sonic's whole episode with an S rank on a GT730 and an i5-4590, with occasional stutters and no crash (#16). The same player reported that hit and damage sounds sometimes cut out and that some stages have broken UI. These reports have not been reproduced on the maintainer's PC.
- **Open pull request:** #12, docs for working with an AI agent.

## Now: setup on other PCs

- **#9, #10, #11:** fixed in 0.2.0 and answered on 2026-11-13. They stay open until the reporters confirm 0.2.0 works for them. #11's reporter thanked us but did not say whether setup now works.
- **#13, `'cmake' is not recognized`:** an update from 0.1.0 to 0.2.0 reaches the build step, and the build stops at once. New since 0.2.0 and not investigated yet.
- **#14, black screen:** the issue has only the empty template, with no details or bug report. It needs more information from the reporter.
- **#5, a clean PC:** the full setup has not been run on a fresh Windows install yet.

## Next: weaker PCs and real hardware

Requests from players on low-end PCs. These are open questions, not decisions:

- **#11, #17:** more graphics options for weak GPUs: NPC shadows, particles, draw distance, light sharpening, exclusive fullscreen, and a DX11 backend.
- **#15:** a launch option to keep the physics at 30 ticks while rendering runs freely. When the PC cannot keep up, the game slows down instead of dropping frames (#16).
- **#7:** testing on AMD, Intel and weaker NVIDIA cards.
- **#2:** testing with a real controller. SDL2 support is in, but no real controller has been tried by the maintainer.

## Known bugs and gaps

- **#3:** Kingdom Valley's water looks very bright from steep angles. It may be correct; it needs a comparison with the original game.
- **#4:** Silver's attract demo shows without the world after a game over.
- **#1:** no stage has been played to the end on the maintainer's PC. #16 is the first outside report of a finished episode.
- **Havok's SPU program** (`hksputhreadconstraint.elf`) is still encrypted and is not translated.
- **Not tested:** a full game from start to finish on more than one PC, sound effect balance in stages, and sync between sound and picture.

## Help wanted

- **#6:** review the launcher's translations (ten languages).
- **#1, #2, #5, #7:** play, test controllers, try a clean PC, and try other hardware.

## Tracking

The issues on GitHub are the task list. There is no project board: none was created, because the maintainer's board workspace is full.
