# Changelog

## [1.4.0] - Stop Breaking Things Update - 2025-09-12
### Added
- Config option `EnableDebugLogging` (`cfgDebugLogging`) to selectively emit detailed diagnostic messages without cluttering normal logs.

### Changed
- Run ForceSleep at 6:59 to ensure all game mechanics that rely on sleep events can run properly.
    - Added many patches to ensure mod functionality works correctly with this change and doesn't interfere with normal game behavior.
- Refactored internal logging to use `TNSLog` static class for consistent formatting and easier future enhancements.
- All debug output (detailed internal state changes, config reloads, etc.) is now conditional on the new `EnableDebugLogging` setting.

### Notes
- Thank you **bigjme** for the bug report!

## 1.3.0 - Multiplier Only Mode, Mono Build & Quality Pass - 2025-08-30
### Added
- Separate published **Mono build** (alongside IL2CPP) with its own Thunderstore package; functionally identical feature set.
- **Multiplier Only Mode** (`TimeMultiplierOnlyMode`): restricts the mod to just enforcing the day speed multiplier (disables 4AM freeze bypass, awake daily summary injection, sleep prompt hiding).
- Continuous multiplier normalization: re‑applies sanitized value if external code or the game changes `TimeProgressionMultiplier`.

### Changed
- Packaging pipeline now syncs version from `Version.props` into both IL2CPP and Mono TOMLs (single source of truth).
- Config writes reduced (only when value actually changes) to minimize disk churn.
- Summary / sleep handling cleanly no‑ops when Multiplier Only Mode is enabled.

### Notes
- Players wanting only adjustable time speed can enable Multiplier Only Mode; default behavior remains unchanged otherwise.
- Mono and IL2CPP zips are now built and hashed in the same run for easier release verification.

## 1.2.1 - Compatibility & Stability Fix - 2025-08-29
### Fixed
- Crash / NullReference during game load caused by attempting to subscribe to `TimeManager` sleep callbacks before the instance existed (or after the game update changed their accessibility/signature).
- Removed invalid delegate `+= / -=` usage on `TimeManager` members that are no longer proper events after the game update.
- Daily Summary + Rank Up flow now reliably suppresses during sleep without relying on those callbacks.

### Changed
- Replaced direct sleep event subscriptions with a lightweight coroutine that polls `TimeManager.SleepInProgress` and triggers start/end logic on state transitions (safer across updates).
- Switched HUD patch target from `FixedUpdate` to `Update` to keep the sleep prompt suppressed after upstream changes.
- Defensive null checks around config + `TimeManager` usage to avoid early initialization edge cases.
- Internal refactor: consolidated sleep suppression + daily summary rearm logic into clearer helper methods.

### Notes
- Functionality is unchanged from the player perspective; this release restores full operation after the latest game update altered internals.
- If further game updates expose stable events again, logic can be swapped back from polling with no user-facing impact.

## 1.2.0 - Awake Summary & Sleep Fix - 2025-08-10

### Added
- **EnableDailySummaryAwake** config toggle (default: 'true') to allow the Daily Summary to display at 07:00 while awake, and if false won't display unless the player sleeps.
- Awake Daily Summary flow improvements:
  - Skips first tick after load to avoid immediate trigger.
  - Suppresses summary during sleep; marks the day handled after sleeping.
  - Automatically runs **RankUpCanvas** after the summary closes.
  - Ensures an EventSystem exists so the UI is interactable.

### Changed
- Removed custom SleepCanvas patch and restored vanilla sleep behavior.
  - Sleeping now uses the base game's time skip, so plants grow and cooking advances normally overnight.
- Integrated awake summary with sleep events via 'TimeManager.onSleepStart' and 'TimeManager.onSleepEnd'.

## 1.1.1 - Minor Fix - 2025-08-09
### Fixed
- SetDaySpeedLoop now checks both the config value and the current 'TimeProgressionMultiplier' each tick.
- If the in-game value differs from the config (due to other mods or game logic), it is automatically corrected.
- Prevents cases where the day speed could silently change without user intent.

## 1.1.0 - Major Bug Fixes & Features - 2025-08-09
### Added
- Dedicated config file at 'UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg' with auto-migration from old settings.
- Support for day speed multipliers up to **100.0x** (previously capped at 3.0x).
- Ability to change DaySpeedMultiplier in-game via config file (no need to restart the game).

### Changed
- Config entry type changed from float to string for more precise parsing and sanitization.
- Day speed loop now:
  - Waits for 'TimeManager' before starting.
  - Reloads config file each tick to pick up manual edits.
  - Applies changes only when value differs (epsilon compare).
  - Reattaches if 'TimeManager.Instance' changes.
- Centralized parsing + sanitization into helper methods.

### Fixed
- Issue where day speed could get "stuck" on the old value if 'OnEntryValueChanged' was missed.
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