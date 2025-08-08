# Time Never Stops

**Author:** DropDaDeuce  
**Version:** 1.0.0  
**Game:** Schedule I (TVGS)  
**Requires:** MelonLoader

---

## Description

Removes the automatic **4 AM time stop** in Schedule I, allowing the in-game time to continue past the usual end-of-day limit.

This mod makes a **minimal change** — it simply overrides the game's `IsEndOfDay` check so the day never automatically ends.  
This preserves normal game functionality, including:
- XP calculation screen when sleeping manually
- Daily events and time progression
- Other time-dependent systems

---

## Installation

1. **Install MelonLoader** (if not already installed) for Schedule I.
2. Download the latest `Time_Never_Stops.dll` from the releases section.
3. Place `Time_Never_Stops.dll` into your game’s `Mods` folder.
4. Launch the game — you should see `"Time Never Stops initialized"` in the MelonLoader console.

---

## Compatibility

- **Compatible:** Other mods that adjust time speed or UI, as long as they don't patch `TimeManager.get_IsEndOfDay`.
- **Incompatible:** Mods that replace the entire `TimeManager.Update` method or hardcode a 4 AM day-end.

---

## Technical Details

This mod uses a Harmony patch:

```csharp
[HarmonyPatch(typeof(TimeManager), "get_IsEndOfDay")]
public static class Patch_IsEndOfDay
{
    public static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}