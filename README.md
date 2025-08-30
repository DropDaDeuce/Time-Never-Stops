# Time Never Stops

**A mod for Schedule I that keeps time moving and lets you control the in‑game day speed.**  
Report issues on the GitHub repository or the modding Discord.

## Disclaimer
- **AI Content:** This mod was created with assistance from AI tools. The icon was generated with ChatGPT v5.

## Features
- **No 4:00 AM Freeze (default mode):** Time keeps advancing; the hard stop is removed.
- **Custom Day Speed:** 0.1x–100.0x multiplier; applied continuously and re‑applied if other code changes it.
- **Live Config Reload:** Edit the config file while the game is running; changes take effect within ~1 second.
- **Daily Summary While Awake (optional):** `EnableDailySummaryAwake` shows the daily summary at 07:00 even if you didn’t sleep.
- **Rank Up Flow Integration:** Automatically runs the rank‑up UI after the daily summary (awake or post‑sleep) and restores HUD state.
- **Sleep Prompt Suppression:** Hides the on‑screen sleep prompt (unless you disable full feature set).
- **Multiplier Only Mode:** `TimeMultiplierOnlyMode = true` limits the mod to *only* enforcing the day speed multiplier (disables 4AM bypass, awake summary injection, sleep prompt hiding) for a minimal footprint.

## Config File
Path: `UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg`

Keys:
- DaySpeedMultiplier=1.0; String parsed to float, range 0.1 - 100.0 (Invalid or out‑of‑range values are clamped and rewritten.)
- EnableDailySummaryAwake=true; Show summary at 07:00 while awake
- TimeMultiplierOnlyMode=false; True = only enforce speed multiplier, disable other features

## Installation

### 1. Requirements
- [MelonLoader](https://github.com/LavaGang/MelonLoader/releases)
- Schedule I (legit copy)

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
- Disable awake summaries: set `EnableDailySummaryAwake=false` (summary still appears after sleeping).

## Compatibility
- Provides Harmony patches on:
  - `TimeManager.Tick` (prefix & postfix)
  - `HUD.Update`
- May conflict with other mods that:
  - Patch the same methods
  - Force or freeze `TimeProgressionMultiplier`
  - Inject alternate daily summary flows

## Credits
- **Source:** <https://github.com/DropDaDeuce/Time-Never-Stops>  
- **Template Inspiration:** Deeej’s S1 Mono / IL2CPP template  
- **Harmony / MelonLoader Teams:** For tooling enabling mod development.