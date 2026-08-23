# Changelog

## [1.5.0] - Multiplayer Sync & Schedule I 0.4.6f13 Compatibility - 2026-08-23

> Supersedes 1.4.1, which was prepared but never published. Everything from it ships here, so
> the upgrade path for players is 1.4.0 → 1.5.0.

### Added
- **Local multiplayer on one PC (Mono build only).** Set `EnableLocalMultiplayer=true`, load a normal save in one copy of the game, and press **F6** in a second copy on the same machine to join it — no second Steam account, no lobby.
  - **Same machine only. This is not LAN**, and cannot be made LAN: the join address is hardcoded inside the game.
  - A local session has no Steam lobby, so the lobby-data time sync does not run. The clock still follows the host — the game pushes it every in-game minute — but **set `DaySpeedMultiplier` the same in both instances**, because the host cannot push that to you.
  - **Not available on IL2CPP.** Multiplayer there stays Steam-only; single-player is unaffected. The config key still appears on that build and warns in the log if it is switched on, rather than silently doing nothing.

- **Silent sleep.** The nightly changeover no longer takes your screen. No fade to black, no HUD drop, no cursor, no waiting — the clock just keeps going and you keep playing.
  - **Everything the game does overnight still happens**, in the same order as vanilla: employees become unpaid for the new day, you wake at full health, wanted levels clear, NPCs revive and heal, storage entities and vending machines and ATMs tick over, cartel deals go overdue or expire, quests advance, and **the host writes the daily save**.
  - Story text that the sleep screen used to display — the loan-shark countdown, the Deep End kidnap — is now a line at the top of the Summary app, so it is not lost with the screen.
  - New config entry: `EnableSilentSleep` (default `true`). **It only applies when the summary is not being shown as a popup** (i.e. the phone app is active, or `EnableDailySummaryAwake=false`). The vanilla popup parents itself to the sleep screen and cannot work without it, so with the popup in play you get the normal sleep sequence — the same fail-safe direction as the rest of the app gate.
  - **The game still saves once a night, and that save is a synchronous write**, so expect a brief hitch. It happens in an unmodded game too; vanilla just hides it behind the sleep screen.

- **Daily Summary phone app.** Yesterday's summary now lives in a Summary app on your in-game phone instead of a popup that lands on top of whatever you were doing — open it when it suits you. It also **scrolls**, so a busy day no longer has items silently cut off: the vanilla popup can only draw a fixed number of product rows and hides the rest.
  - **Nothing takes over your screen at 07:00 any more.** The rank-up and region-unlocked screens were full-screen post-sleep events that interrupted you even with the app installed; both are now reported as lines in the app instead — including which rank you reached and what it unlocked.
  - New config entry: `EnableDailySummaryApp` (default `true`). While it is on, the vanilla popup is suppressed and `EnableDailySummaryAwake` is ignored — the app replaces it.
  - Powered by **S1API** (originally by KaBooMa, forked and maintained by **ifBars**). One package covers both game builds.
  - **If S1API is missing, or the app fails to load or draw, the mod falls back to the vanilla popup automatically** and says so in the log. The summary is never lost, and nothing in the time, sleep or multiplayer-sync path depends on the app.

- **Multiplayer time synchronization** via Steam lobby data (huge thanks to **ifBars** for the initial implementation!):
  - Host periodically writes current time and elapsed days to lobby data; clients sync on change.
  - Host's day speed multiplier is broadcast to all clients via lobby data and applied automatically.
  - Client multiplier is restored to its original value when leaving the lobby.
  - Immediate multiplier sync triggered when the host changes their config at runtime.
- New config entry: `ForceDailySummaryWhileInUITime` (`cfgDailySummaryCutoffHHMM`) — configurable HHmm cutoff that forces post-sleep UIs to close so the day can progress.
- Helper methods `SanitizeHHMM` and `FormatHHMM` for clean HHmm time handling.
- `ClosePostSleepCanvases()` coroutine that closes DailySummary, RankUpCanvas, and RegionUnlockedCanvas at cutoff + 30 min.
- `Patch_TimeManager_SkipForwardToTime` — prevents the engine from rewinding time to 07:00 when it's already at or past wake time.
- `Patch_SleepCanvas_AddPostSleepEvent` — keeps the post-sleep canvases out of the queue while silent sleep is active, so a queue nothing drains cannot grow one entry per night.
- `SleepCanvas.SleepStart` deferral logic — sleep is now deferred until active UIs are cleared, with a configurable cutoff and 30-second safety timeout.
- `TMAccess.SetSleepInProgress` (both MONO and IL2CPP) for forced flag clearing when the engine gets stuck.
- `Debug_TimeManager` postfixes on the `StartSleep` and `SetHostSleepDone` RPCs, for tracing the multiplayer sleep flow in the log.
- `IsHost` property on `Core` to gate host-only TimeManager writes.
- `FindField` helper in `TMAccess` (IL2CPP): silently tries multiple candidate field names before giving up, avoiding AccessTools log spam on failed lookups. Logs the full `TimeManager` field list when `EnableDebugLogging` is on — useful for identifying renamed fields after game updates.
- `TryInitTimeSync()` helper: single self-contained TimeSyncManager init attempt with proper cleanup of partial state on failure.
- `_timeSyncRetrying` flag to prevent duplicate init coroutines when `OnSceneWasInitialized` fires multiple times (e.g. on scene reload or quit).
- Background slow-poll (every 15s) after fast init attempts exhaust — handles the common case of a friend joining a solo session after it has already started, which is when the game first activates its Steam networking stack.

### Changed
- TimeSyncManager init trigger moved from `Menu` scene to `Main` scene (i.e. when a save is actually loaded), ensuring Steamworks is active before the first attempt.
- Initial init now uses a fast burst of 3 attempts (2s apart), followed by an indefinite 15-second background poll rather than giving up entirely.
- `Menu` scene load now resets all sync state (`_timeSyncInitialized`, `_timeSyncRetrying`, disposes manager), so each new save load starts completely clean.
- `OnLobbyLeft` no longer logs "Lobby left, stopping time sync" when called from `Dispose()` cleanup — was generating misleading noise during retry attempts.
- `SetDaySpeedLoop` now only applies config to TimeManager on the host; clients receive the value via Steam lobby sync.
- `EnsureHUDReset` now checks active UI count before deciding to lock or free the camera.
- Renamed `Debug_TimeManager_SleepFlow` to `Debug_TimeManager`; removed noisy `Debug_Employee_Pay` patch class.
- Core.cs refactor: cleaned up `using` directives, dropped `DevUtilities` alias in favor of direct namespace import.
- Broadened access on several fields to `public static` where needed for cross-class use.
- Promoted `ParseFloatOrDefault`, `Sanitize`, `TryParseFloatInvariant`, and `ApplyDaySpeed` to `public` for reuse.
- The build now compiles against the game install at `$(S1Dir)` instead of the vendored copies in `TNS_Project\Libs\`. The SDK searches `{CandidateAssemblyFiles}` (fed by the default `None` glob over `Libs\`) *before* `{HintPathFromItem}`, so every reference silently resolved to a stale local snapshot and `$(S1Dir)` was never consulted — which is why four months of game API changes produced no build error. Set `EnableDefaultNoneItems=false` to close that off.
- `Nullable` set to `annotations` rather than `disable`, clearing 32 CS8632 warnings per build without changing behaviour.

### Fixed
- **The day-speed multiplier and per-minute timing now actually apply on the IL2CPP build.** `TMAccess.SetTimeSpeedMultiplierDirect` and `SetTimeOnCurrentMinute` were silent no-ops there: they looked the values up as reflected *fields*, and Il2CppInterop does not expose native instance fields as fields — it regenerates each one as a property. They are now written directly. **Client-side multiplier sync was the visible casualty**, since it has to bypass `SetTimeSpeedMultiplier`'s server-only guard, so IL2CPP clients previously ignored the host's multiplier entirely. (MONO was never affected.)
- **No more spurious "sleep did not finish" warnings in the log on nights with a story message.** The wait for the game's sleep sequence used a flat 6-second budget, but vanilla's tail runs ~1.1s normally and ~6.1s or more when the game has queued a sleep message to display — so a perfectly healthy night could log what looked like a failure. The budget now extends automatically when a message is queued.
- **The mod now compiles and runs against Schedule I 0.4.6f13.** Four game API changes had broken it, all in the sleep/HUD reset path:
  - `Player.CurrentBed` was removed from the game — the assignment is gone (the game's own `SleepCanvas` dropped the identical line; there is no replacement).
  - `PlayerCamera.SetCanLook`'s parameter was renamed `c` → `canLook`.
  - `ScheduleOne.UI.InputPromptsCanvas` was replaced by `ScheduleOne.UI.Input.InputPromptsManager`, which has no parameterless `UnloadModule` — now calls `UnloadModule("Back")`, matching what the game does around its own sleep flow.
  - `PlayerCamera.activeUIElements` went private — now reads the public `ActiveUIElementCount`.
- **Daily-summary sleep no longer soft-locks clients.** `StartSleep()` was called on every machine. It is an `ObserversRpc` with `RunLocally`, so on a client the network send failed and only the local half ran — setting `IsSleepInProgress` and then waiting forever on `HostSleepDone`, a flag only the host ever sets. A client could be left on a black "Waiting for host" screen with no way out. The call is now host-only; the host's RPC still drives the sleep flow on every client, so the awake daily summary is unchanged for everyone.
- **The 4AM bypass fired `onDayPass` before `onHourPass` at the midnight rollover; the game fires them the other way round.** Order corrected to match `RpcLogic___PassMinute_Client`.
- **Moving the mouse over the daily summary also turned the player’s head.** The sleep-start routine set `SetCanLook(true)` and `FreeMouse()` together, so one mouse movement drove both the cursor and the camera. `PlayerCamera.Update` gates look purely on `CanLook && !transformOverriden` and checks no UI state, and the awake summary has no bed overriding the transform — so nothing stopped it. The cursor and head-turn are now separated: when the summary is shown the camera is held still, and when it is suppressed control is handed straight back.
- **The HUD snapped at the end of sleep.** Cleanup ran on `SetHostSleepDone`, about 1.6 seconds before the game’s own sleep sequence finishes, so the mod restored camera, mouse, movement and inventory while the screen was still fading — and because the "Sleeping" UI element was still registered it picked the wrong branch, which vanilla then undid a moment later. Cleanup now waits for the game’s sleep sequence to finish before restoring anything, and only forces the reset if it has not finished within six seconds — so the normal path is vanilla’s and the stuck-state recovery is unchanged. The wait watches for the game’s own sleep UI to close rather than for the screen to be empty, so a menu you already had open at 07:00 no longer holds it up.
- **`TimeSyncUpdateLoop` leaked a coroutine per save load.** It never exited and its handle was discarded, so returning to the menu and loading again left the old loop running for the rest of the session. It is now tracked and stopped on menu load, on re-init, and on unload.
- Fixed mod behavior for the 0.4.5f2 game update.
- `suppressForSleep` stuck-guard: automatically clears if the flag is true but the engine is no longer sleeping.
- Forced `SleepInProgress = false` when `HostDailySummaryDone` is set but the engine flag hasn't cleared.
- Fixed bug where "Time synchronization enabled" was logged even when `TimeSyncManager.Initialize()` had internally swallowed an error and `IsInitialized` was false.

### Notes
- Massive thanks to **ifBars** for the Steam lobby networking implementation!
- **IL2CPP only:** `_secondsOnCurrentMinute` and the `TimeSpeedMultiplier` backing field are not accessible via standard .NET reflection in IL2CPP mode. In Il2CppInterop, native instance fields exist only as `NativeFieldInfoPtr_*` metadata pointers on the wrapper type — `AccessTools.GetDeclaredFields` returns only those pointers, not the actual fields. `SetTimeOnCurrentMinute` and `SetTimeSpeedMultiplierDirect` are currently no-ops in IL2CPP and would require native IL2CPP field API access to implement properly.

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