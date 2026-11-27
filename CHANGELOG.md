# Soleanna changelog

Soleanna is the project's name, and its launcher's; `sonic06-recomp` is the repository's.

## 0.4.0 (fixes from player reports)

Fixes for what players reported after 0.3.0, and a new launcher language. The DirectX 11 backend that players asked for is planned as the next version.

- **Setup stopped at "ps3recomp with the project's patches" with "The system cannot find the file specified"** (#26): a tool on PATH can be a link whose target is gone, left by an uninstalled or moved program. The launcher counted it as installed and then could not start it. It now skips such links and goes on to the next copy on PATH, and when a program does not start, the message names the file.
- **Exclusive fullscreen showed a black screen with sound on a GT730** (#23): the game now resizes its buffers after entering exclusive fullscreen, as DirectX asks, and if the driver keeps failing to show frames there, it goes back to the borderless window by itself. Tested with planted failures on one PC, not yet on a GT730.
- **Simplified Chinese** (#22): the launcher's eleventh language, from a player's translation, completed for every string. It is picked by itself on a Simplified Chinese Windows.
- **Choppy motion at low frame rates with full speed on** (#24): measured, not changed. At a steady 30 the game moves by even steps; the jumps come from long frames on a PC that falls well below 30, so the fix is in speed, not in timing.
- **Updating from 0.3.0** builds the game again (about 15 minutes), because the fix for #23 is in the project's changes to ps3recomp. What came from your disc stays.

## 0.3.0 (weaker PCs)

Options for PCs that cannot hold 60 frames a second, 120 frames a second for fast PCs, and a setup fix.

- **Build stopped with "'cmake' is not recognized"** (#13): with Visual Studio installed, the build called plain `cmake` and counted on Visual Studio to put its own copy on PATH. It now finds CMake on PATH, in Visual Studio or in `Program Files\CMake`, as the launcher does, and calls it by its full path.
- **Game speed** (#15): the game moves a fixed 1/60 of a second each frame, so below 60 frames per second everything slows down. Two new options in Graphics:
  - **Keep the game at full speed (experimental):** the game moves by the real time between frames, using a mode the game already has but leaves off. At 30 frames per second a stage's timer then follows the clock. With it on, all 83 stage entries and 18 bosses of the developer menu started and ran a minute each at 30 frames per second without a crash; the timer itself was checked in Wave Ocean.
  - **Frame rate: 30, 60 or 120.** 30 is a steady 30 instead of a frame rate that jumps between 30 and 60, best with the first option. 120 (experimental) is for fast screens and always keeps the game at full speed: in Wave Ocean a stage's timer followed the clock, and cutscenes keep their 30. Tested with the vsync off on a 60 Hz screen, not yet on a 120 Hz one.
- **"Weak PC" preset:** one click for the lightest picture, shaders compiled in the background, 30 frames per second and full speed.
- **Sound cutting out** (#16): on a PC with few cores the game's audio thread is sometimes a moment late with the next block of sound, and it played as a short silence. The mixer now waits a few milliseconds for a block that is still empty right after sound. Measured on 4 cores in a minute of Wave Ocean: 7 dropouts on average instead of 38 (four runs each, alternating), and no change on a PC with many cores.
- **Exclusive fullscreen** (#17): a third display mode next to window and borderless, using DirectX's own fullscreen. It falls back to borderless when the driver refuses it.
- **Shadows** (#11, #17): can be turned off. Characters and objects then cast no shadows. On the Intel UHD 770 it did not raise the frame rate; on other cards it is untested.
- **Frame rate counter:** frames per second in the corner of the picture, also in fullscreen.
- **Updating from 0.2.0** translates and builds the game code again, as a first setup does: the new options need two small calls in it. The launcher now also redoes the translation when the project's fixes to it change.
- **Not yet:** fewer particles, a draw distance setting and a DirectX 11 backend (#11, #17) need work inside the game's renderer.

## 0.2.0 (setup on other PCs)

The first people to try 0.1.0 on their own PCs did not get to the game. This version fixes what stopped them; the game itself is the same.

- **Setup on a PC whose git has no name and email** (most fresh installs): applying the project's patches to ps3recomp failed, and running Setup again then went on with an unpatched copy. The game built from it is upside down, silent and freezes in the first scene (#9), and a copy that was never checked out stops the lift with "can't open file" (#11). Git now gets a name for those local commits, and Setup checks that the copy has every patch and remakes one that an earlier run left half done.
- **Building next to a Visual Studio without the C++ tools** (#10): the build stopped with "Cannot bind argument to parameter 'Path' because it is null" before it got to the Windows SDK that Setup installed. It now uses that SDK, the same rule the launcher uses to decide whether to install it.
- **Japanese and Korean Windows:** the lift writes a character those code pages do not have, and stopped there. Python now runs in UTF-8 mode in every build script.
- **A lift that stopped half way** is redone on the next Setup, instead of being built as it was.
- **When a step fails,** the message now shows the lines from its log that say why, instead of pointing at the log, and the log no longer shows PowerShell's colour codes.
- **If 0.1.0 already failed for you:** run `Soleanna-0.2.0.exe`, let it update your install, and accept the rebuild it offers. Everything from your disc stays.

## 0.1.0 (first public version)

A work in progress, published so others can try it and help. It contains no game code or assets: the launcher builds the game on your PC from your own BLES00028 disc.

- **What runs:** the title screen, menus and movies, the story to Soleanna's town, and every boss and stage entry, each started without a crash in tests of about a minute. No stage has been played to the end by a person yet.
- **Speed:** on the RTX 4070 SUPER it was tested on, the first story scene holds the game's own 30 fps cap, and Wave Ocean runs at 57 to 59 fps alone and 59.5 to 59.8 in split-screen, with internal resolutions up to 3840×2160 as fast as 1280×720. Shaders are cached on disk after their first use.
- **The launcher (Soleanna):** copies your disc, decrypts it with your RPCS3, installs the build tools, downloads FFmpeg, SDL2 and ps3recomp, and builds the game. Ten languages, updates, and a bug report.
- **Graphics:** window or borderless fullscreen, internal resolution with real supersampling above the window, four upscaling filters, sharpening, FXAA, texture filtering, brightness, saturation and contrast, and presets.
- **Controls:** controllers through SDL2 (Xbox, DualShock 4, DualSense, Switch Pro), not yet tried with a real controller. Keyboard and mouse with a PC layout (WASD, Space to jump, Esc to pause, the mouse for the camera), remappable in the launcher.
- **One file:** the release is `Soleanna-0.1.0.exe`. It installs itself into a folder you pick, with only what the build needs (the build scripts, `src`, the ps3recomp patches and the license), adds a desktop shortcut, and later updates that folder from a newer release file. It replaced the first upload, a zip of the whole repository.
- **Known gaps:** tested on one Windows PC only; weaker graphics cards, AMD and Intel GPUs and a clean PC have not been tried.

The launcher was numbered 0.3 to 0.5 while it was private; this is its first public version, so it starts again at 0.1.0.
