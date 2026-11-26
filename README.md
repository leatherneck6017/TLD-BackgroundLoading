# BackgroundLoading

Do you alt-tab while The Long Dark loads, only to come back and find it stopped the moment you looked away? BackgroundLoading is here to help!

BackgroundLoading is a lightweight mod for The Long Dark that keeps the game loading while its window is unfocused. Unity normally pauses a windowed game as soon as it loses focus, so long loads stall until you click back in. BackgroundLoading turns on Unity's "run in background" only while the game is loading, and skips the game's own pause-on-focus-loss during that time. Once the load is done it puts both back, so the game still pauses when unfocused during normal play, just like vanilla.

## Features

- Keeps the game running in the background from startup until the main menu is shown and idle.
- Keeps running during later loads too: loading a save, region transitions, and quitting to the main menu.
- Skips the game's own pause-on-focus-loss while loading, and switches "run in background" back on if anything else turns it off mid-load.
- Restores the game's original setting about 4 seconds after loading finishes, so the game pauses when unfocused during gameplay and on the idle main menu. If the window is still unfocused at that point, the game's own focus-loss pause is applied then, just as if you had alt-tabbed during normal play.
- Optional "Keep running in background always" setting.
- Logs each switch to the MelonLoader log (for example, `Run in background ON (loading: loading screen)`), plus a line whenever a focus-loss pause is skipped.
- If another mod or the game switches "run in background" off, logs a `[runInBackground culprit]` warning naming the mod and method (or showing the game's stack) so you can report it to that mod's author.

## Requirements

- The Long Dark (tested on v2.60)
- MelonLoader v0.7.2
- ModSettings

## Installation

1. Install MelonLoader v0.7.2 for The Long Dark if you have not already.
2. Install ModSettings if you have not already.
3. Place `BackgroundLoading.dll` into your game's `Mods` folder (for example, `E:\SteamLibrary\steamapps\common\TheLongDark\Mods`).
4. Launch the game.

## Settings

Open Options > Mod Settings > Background Loading.

- **Enabled** (default on): keep the game running in the background while it loads. Turn off to use the game's normal behavior.
- **Keep running in background always** (default off): keep the game running whenever it is unfocused, not just while loading. Warning: during gameplay, time keeps passing (your survivor keeps getting colder and hungrier) and audio keeps playing while you are alt-tabbed.

## Issues
If you encounter any, please report them on the [GitHub Issues](https://github.com/leatherneck6017/TLD-BackgroundLoading/issues) page or contact me directly on the [TLD Modding Discord](https://discord.gg/tldmodding) (@leatherneck6017).

## AI Disclaimer

AI tools were consulted during the development of this mod as part of my learning process for exploring The Long Dark's IL2CPP APIs and troubleshooting. All code was tested and verified in-game.

## License

This project is open source and available under the [MIT License](LICENSE.txt).
