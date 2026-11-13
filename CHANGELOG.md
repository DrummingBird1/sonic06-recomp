# Soleanna changelog

Soleanna is the project's name, and its launcher's; `sonic06-recomp` is the repository's.

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
