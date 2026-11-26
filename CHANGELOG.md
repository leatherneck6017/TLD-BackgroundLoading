# Changelog

All notable changes to **BackgroundLoading** will be documented in this file.

## [1.0.2] - 25-Nov-26

### Added

- Names whatever switches `Application.runInBackground` off. A Harmony prefix on the setter logs a `[runInBackground culprit]` warning for every "set to false" that BackgroundLoading did not make itself:
  - **Case 1 (managed mod code):** the mod (name, version, author and DLL) and method that made the call, plus the managed stack.
  - **Case 2 (game/native code):** no mod on the managed stack; the game's IL2CPP stack is logged to show where it came from.
  - Each caller is reported once, up to 10 reports per session.
- The per-frame "switched off by something else" warning now says whether the caller was named above, or (**Case 3**) the change got past the setter patch from native game code.

- The new patch only reports and never blocks the call. The per-frame check still switches `Application.runInBackground` back on as before.

## [1.0.1] - 25-Nov-26

### Fixed

- Loading could still freeze while the window was unfocused (seen during profile loading at the main menu), then carry on the moment the window was clicked again. While the mod is ON it now:
  - skips the game's own pause-on-focus-loss (`GameManager.OnApplicationFocus(false)` and `GameManager.PauseWhenFocusLost(true)`);
  - checks every frame that `Application.runInBackground` is still on and switches it back on if the game or another mod turned it off, logging a warning when that happens.
- When loading finishes and the window is still unfocused, the game's own focus-loss pause is applied then, so normal play behaves like vanilla. Clicking back into the game undoes it as usual.
- If the game had already paused itself for focus loss when the mod switches on, that pause is undone. A pause the player opened is left alone.

### Changed

- Logs when a focus-loss pause is skipped and when the window is focused again.

## [1.0.0] - 25-Nov-26

### Added

- Turns on `Application.runInBackground` while the game is loading, so loads keep going when the window is unfocused: startup until the main menu is shown and idle, save loads, region transitions, and quitting to the main menu.
- Restores the original `Application.runInBackground` value once loading has been finished for about 4 seconds, and always on mod unload or game exit.
- Mod Settings page with "Enabled" and "Keep running in background always" options.
- Logs each ON/OFF switch, with the reason, to the MelonLoader log.
