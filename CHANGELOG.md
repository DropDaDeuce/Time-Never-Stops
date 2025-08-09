# Change# Changelog

## 1.1.1 - Minor Fix - 2025-08-09
### Fixed
- SetDaySpeedLoop now checks both the config value and the current `TimeProgressionMultiplier` each tick.
- If the in-game value differs from the config (due to other mods or game logic), it is automatically corrected.
- Prevents cases where the day speed could silently change without user intent.

## 1.1.0 - Major Bug Fixes & Features - 2025-08-09
### Added
- Dedicated config file at `UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg` with auto-migration from old settings.
- Support for day speed multipliers up to **100.0x** (previously capped at 3.0x).
- Ability to change DaySpeedMultiplier in-game via config file (no need to restart the game).

### Changed
- Config entry type changed from float to string for more precise parsing and sanitization.
- Day speed loop now:
  - Waits for `TimeManager` before starting.
  - Reloads config file each tick to pick up manual edits.
  - Applies changes only when value differs (epsilon compare).
  - Reattaches if `TimeManager.Instance` changes.
- Centralized parsing + sanitization into helper methods.

### Fixed
- Issue where day speed could get "stuck" on the old value if `OnEntryValueChanged` was missed.
- Issue where day speed multiplier was not applied when quitting a save and loading a new/old save.

## 1.0.2 - Bug Fixes - 2025-08-08
### Fixed
- **Daily Summary Fix:** Fixed an issue where the daily summary UI would appear when loading a new game, and at the start of character creation. Fixed by making sure daily summary only appears after the first day has passed.

## 1.0.1 - Minor Fixes - 2025-08-07
### Fixed
- **Website Update:** Updated the website link in the mod settings to point to the correct GitHub repository.

## 1.0.0 - Initial Release - 2025-08-07
### Added
- **Time Never Stops:** Removed the default time freeze at 4:00 AM. The game clock continues to advance, allowing for uninterrupted gameplay.
- **Custom Day Speed:** Added the ability to adjust the in-game time speed multiplier (0.1x to 3.0x) via mod settings.