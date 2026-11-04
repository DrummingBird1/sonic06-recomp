# Changelog

## 0.1.0 (first public version)

A work in progress, published so others can try it and help. It contains no game code or assets: the launcher builds the game on your PC from your own BLES00028 disc.

- **What runs:** the title screen, menus and movies, the story to Soleanna's town, and every boss and stage entry, each started without a crash in tests of about a minute. No stage has been played to the end by a person yet.
- **Speed:** on the RTX 4070 SUPER it was tested on, the first story scene holds the game's own 30 fps cap, and Wave Ocean runs at 57 to 59 fps alone and 59.5 to 59.8 in split-screen, with internal resolutions up to 3840×2160 as fast as 1280×720. Shaders are cached on disk after their first use.
- **The launcher (Soleanna):** copies your disc, decrypts it with your RPCS3, installs the build tools, downloads FFmpeg, SDL2 and ps3recomp, and builds the game. Ten languages, updates, and a bug report.
- **Graphics:** window or borderless fullscreen, internal resolution with real supersampling above the window, four upscaling filters, sharpening, FXAA, texture filtering, brightness, saturation and contrast, and presets.
- **Controls:** controllers through SDL2 (Xbox, DualShock 4, DualSense, Switch Pro), not yet tried with a real controller. Keyboard and mouse with a PC layout (WASD, Space to jump, Esc to pause, the mouse for the camera), remappable in the launcher.
- **Known gaps:** tested on one Windows PC only; weaker graphics cards, AMD and Intel GPUs and a clean PC have not been tried.

The launcher was numbered 0.3 to 0.5 while it was private; this is its first public version, so it starts again at 0.1.0.
