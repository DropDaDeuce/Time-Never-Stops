# Time Never Stops

**A mod for Schedule I that keeps time moving and lets you control the in‑game day speed.**  
Report issues on [GitHub](https://github.com/DropDaDeuce/Time-Never-Stops/issues).  
Also on [Nexus Mods](https://www.nexusmods.com/schedule1/mods/1141).

## Disclaimer
- **AI Content:** This mod was created with assistance from AI tools. The icon was generated with ChatGPT v5.

## Features
- **No 4:00 AM Freeze (default mode):** Time keeps advancing; the hard stop is removed.
- **Custom Day Speed:** 0.1x–100.0x multiplier; applied continuously and re‑applied if other code changes it.
- **Live Config Reload:** Edit the config file while the game is running; changes take effect within ~1 second.
- **Daily Summary Phone App (new, on by default):** yesterday's summary lives on your phone, so it never interrupts you. Open it when you want it — and unlike the vanilla popup it scrolls, so a busy day no longer has items quietly cut off the bottom. Needs **S1API**; if that is missing the mod falls back to the popup automatically.
- **Daily Summary While Awake:** `EnableDailySummaryAwake` shows the daily summary popup at 07:00 even if you didn’t sleep. Ignored while the phone app is running — the app replaces it.
- **Silent Sleep (new, on by default):** the nightly changeover no longer takes your screen — no fade to black, no HUD drop, no cursor, no waiting. Everything the game does overnight still happens: employees become unpaid for the new day, you wake at full health, wanted levels clear, NPCs revive, machines tick over, and the game saves. Applies while the summary is not being shown as a popup; see `EnableSilentSleep`.
- **Rank-ups and region unlocks are content, not interruptions:** the vanilla full-screen rank-up and region-unlocked screens used to seize the game at 07:00. They are now reported as lines in the Summary app instead — including which rank you reached and what it unlocked.
- **Sleep Prompt Suppression:** Hides the on‑screen sleep prompt (unless you disable full feature set).
- **Local Multiplayer (Mono build only, off by default):** a second copy of the game on the same PC can join your session with no second Steam account. See below.
- **Multiplier Only Mode:** `TimeMultiplierOnlyMode = true` limits the mod to *only* enforcing the day speed multiplier (disables 4AM bypass, awake summary injection, sleep prompt hiding) for a minimal footprint.

## Config File
Path: `UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg`

Keys:
- `DaySpeedMultiplier=1.0` — string parsed to float, range 0.1–100.0. Invalid or out-of-range values are clamped and rewritten.
- `EnableDailySummaryApp=true` — show yesterday's summary in a phone app instead of interrupting you with the popup. Requires S1API; falls back to the popup if it is missing or the app fails to load.
- `EnableDailySummaryAwake=true` — show the daily summary popup at 07:00 even if you did not sleep. **Ignored while the phone app is active.**
- `EnableSilentSleep=true` — run the nightly changeover with no screen takeover at all: no fade to black, no HUD drop, no cursor, no waiting. Everything the game does overnight still happens, the daily save included. Only applies when the summary is not being shown as a popup (i.e. the phone app is active, or you set `EnableDailySummaryAwake=false`); otherwise you get the normal sleep screen, because the popup needs it.
- `EnableLocalMultiplayer=false` — **Mono build only.** Lets a second copy of the game on the *same PC* join your session, with no second Steam account. See below.
- `TimeMultiplierOnlyMode=false` — true means only the speed multiplier is enforced; every other feature is disabled.
- `ForceDailySummaryWhileInUITime=800` — an HHmm time. Post-sleep menus still open at this point are closed so the day can move on.
- `EnableDebugLogging=false` — extra logging. Turn it on before reporting a bug.

## Installation

### 1. Requirements
- [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) **0.7.3 or newer**
- Schedule I (legit copy), game version **0.4.6f13**
- **SteamNetworkLib** by ifBars, **1.5.0 or newer** — required for multiplayer time sync. A mod manager installs it for you; if you install by hand, take the copy that matches your game build:
  - [SteamNetworkLib_IL2Cpp](https://thunderstore.io/c/schedule-i/p/ifBars/SteamNetworkLib_Il2Cpp/)
  - [SteamNetworkLib_Mono](https://thunderstore.io/c/schedule-i/p/ifBars/SteamNetworkLib_Mono/)
  - On Nexus instead: <https://www.nexusmods.com/schedule1/mods/1396>

  Without it the mod still loads and single-player is unaffected, but time will not sync between players and nothing on screen says why.

- **S1API_Forked** by ifBars (KaBooMa's original, forked), **3.2.0 or newer** — powers the Daily Summary phone app. A mod manager installs it for you.
  - [S1API_Forked on Thunderstore](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/)
  - On Nexus instead: <https://www.nexusmods.com/schedule1/mods/1194>

  **One package covers both game builds** — unlike SteamNetworkLib there is no Mono/IL2CPP choice to make. Without it the mod still works: it detects that S1API is absent and shows the vanilla daily-summary popup instead, so nothing is lost except the app. Set `EnableDailySummaryApp=false` if you would rather not install it.

### 2. Choose the Correct Build
You will see two Thunderstore packages / release files:
- `TimeNeverStops_IL2Cpp.dll` (IL2CPP build)
- `TimeNeverStops_Mono.dll` (Mono build)

Use the one matching your game build (Mono vs IL2CPP). If unsure, the IL2CPP build typically includes `GameAssembly.dll`; the pure Mono build has only a large set of managed assemblies under `..._Data/Managed/`.

### 3. Install
Place the chosen DLL in your `Schedule I/Mods/` folder (created by MelonLoader).  
Only install **one** variant that matches your game build.

### 4. Configure
Edit the config file (it is created after first launch). Save changes; they apply automatically.

## Usage Scenarios
- Want everything (no 4AM freeze + awake summary + prompt hiding): leave `TimeMultiplierOnlyMode=false`.
- Only want adjustable speed (compatibility with other time / sleep mods): set `TimeMultiplierOnlyMode=true`.
- Read the summary on your own schedule (default): leave `EnableDailySummaryApp=true` and open the Summary app on your phone.
- Want the old interrupting popup back: set `EnableDailySummaryApp=false` (and keep `EnableDailySummaryAwake=true`).
- Want no summary at all while awake: set both `EnableDailySummaryApp=false` and `EnableDailySummaryAwake=false` (it still appears after sleeping normally).
- Want the fade-to-black back at 07:00: set `EnableSilentSleep=false`.

### About the pause at 07:00

Whatever you set, the game **saves** once a night, and that save is a synchronous write — a short hitch, on a big save maybe a couple of seconds. It happens in an unmodded game too; you just never see it, because vanilla hides it behind the sleep screen. With this mod you are awake when it lands. It is the game saving, not the mod stalling, and it should not be skipped.

## Local Multiplayer (Mono only)

Mostly useful for testing. Set `EnableLocalMultiplayer=true`, then:

1. Launch two copies of the game on the same PC.
2. In the first, load a normal single-player save. The log will say `listening on 127.0.0.1:38465`.
3. In the second, press **F6**. It joins the first.

**Same machine only — this is not LAN**, and there is no way to make it LAN: the join address is hardcoded inside the game.

**There is no Steam lobby in a local session, so the time sync described below does not run.** The clock still follows the host, because the game itself pushes it every in-game minute. What it means in practice: **set `DaySpeedMultiplier` to the same value in both instances**, since the host cannot push it to you.

**Not available on the IL2CPP build.** Multiplayer there is Steam-only; single-player is unaffected.

## Multiplayer

Time is synchronised from the **host**. The host’s day speed is pushed to everyone connected, so while you are in
someone else’s game their setting wins; your own is restored when you leave.

### The supported setup: everyone has the mod

**Every player in the lobby needs this mod installed, and that is the only configuration that is tested or supported.**

A player without it still hits the vanilla 4:00 AM stop while everyone else carries on, because the game decides
whether a minute may pass using **that player's own clock**, not the host's. Their clock stops dead at 4:00 and there
is no reliable way for the host to push them past it.

**Host-only installs are untested and unsupported.** They may happen to work; nothing is done to make sure they do,
and problems arising from that setup are not bugs.

For the same reason `TimeMultiplierOnlyMode` and `DaySpeedMultiplier` should match across the lobby — a player who
has multiplier-only mode switched on keeps vanilla behaviour and will fall out of step with the rest.

This is the half that needs **SteamNetworkLib** (see Requirements). Without it the mod still loads and single-player
is unaffected, but clocks drift apart and nothing on screen says why.

## Compatibility
- Harmony patches, for diagnosing conflicts:
  - `TimeManager.RpcLogic___PassMinute_Client_3316948804` (prefix & postfix) — the main one
  - `TimeManager.SkipForwardToTime` (prefix), `TimeManager.StartSleep` and `TimeManager.SetHostSleepDone` (postfix, logging only)
  - `SleepCanvas.SleepStart` (prefix) and `SleepCanvas.AddPostSleepEvent` (prefix)
  - `DailySummary.Open`, `DailySummary.Close` and `DailySummary.ClearStats` (all prefix)
  - `RankUpCanvas.StartEvent` (prefix)
  - `RegionUnlockedCanvas.StartEvent` (prefix) and `RegionUnlockedCanvas.QueueUnlocked` (postfix)
  - `HUD.Update` (postfix)
- May conflict with other mods that:
  - Patch the same methods
  - Force or freeze `TimeManager.TimeSpeedMultiplier`
  - Inject alternate daily summary or sleep flows
- **If you use another time or sleep mod**, set `TimeMultiplierOnlyMode=true`. Every patch above then early-returns to
  vanilla behaviour and only the speed multiplier is applied.

## Troubleshooting

### Multiplayer time sync does nothing, and the log says "Steam is not initialized"

You are launching the game outside Steam. Some mod managers start `Schedule I.exe` directly instead of going
through Steam, and when they do the game’s Steam features never initialise — that includes multiplayer itself,
not just this mod’s time sync.

Either fix works:

- **Launch through Steam.** This is the supported path and needs nothing else.
- **Or tell the game its Steam app id.** Create a file named `steam_appid.txt` next to `Schedule I.exe`,
  containing exactly:

  ```
  3164500
  ```

  Plain text, nothing else in the file. Steam still has to be running.

**Single-player is unaffected either way.** Without Steam there is no lobby, so there is nothing to sync — the
error looks alarming but it cannot cause your clock to drift on its own.

### Where the log is

`MelonLoader\Latest.log`, in your Schedule I folder. Set `EnableDebugLogging = true`, reproduce the problem,
and attach that file when reporting anything.

## Credits
- **Source:** <https://github.com/DropDaDeuce/Time-Never-Stops>  
- **Template Inspiration:** Deeej’s S1 Mono / IL2CPP template  
- **Harmony / MelonLoader Teams:** For tooling enabling mod development.