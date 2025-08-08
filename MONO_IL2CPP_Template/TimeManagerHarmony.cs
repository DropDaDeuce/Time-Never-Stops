using Il2CppFishNet; // For InstanceFinder
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI; // For Player.Local

namespace Time_Never_Stops_TMH
{
    [HarmonyPatch(typeof(TimeManager), "get_IsEndOfDay")]
    public static class Patch_IsEndOfDay
    {
        public static bool Prefix(ref bool __result)
        {
            __result = false; // Never stop time at 4am
            return false;     // Skip original getter
        }
    }

    [HarmonyPatch(typeof(TimeManager), "Tick")]
    public static class Patch_Tick_XPMenu
    {
        private static int lastXPDay = -1;

        public static void Postfix(TimeManager __instance)
        {
            // Only run on host/server to avoid duplicate triggers
            if (!InstanceFinder.IsHost)
                return;

            // Only trigger once per day at 7am
            if (__instance.CurrentTime == 700 && __instance.ElapsedDays != lastXPDay)
            {
                lastXPDay = __instance.ElapsedDays;

                // Make sure DailySummary exists
                if (NetworkSingleton<DailySummary>.InstanceExists)
                {
                    NetworkSingleton<DailySummary>.Instance.Open();
                }
            }
        }
    }

    [HarmonyPatch(typeof(SleepCanvas), "SleepStart")]
    public static class Patch_SleepCanvas_SleepStart
    {
        // This transpiler removes the call to NetworkSingleton<DailySummary>.Instance.Open()
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var dailySummaryType = typeof(NetworkSingleton<DailySummary>);
            var openMethod = AccessTools.Method(dailySummaryType, "get_Instance");
            var callOpen = AccessTools.Method(typeof(DailySummary), "Open");

            for (int i = 0; i < codes.Count; i++)
            {
                // Look for: NetworkSingleton<DailySummary>.Instance.Open();
                if (i + 1 < codes.Count &&
                    codes[i].Calls(openMethod) &&
                    codes[i + 1].Calls(callOpen))
                {
                    // Remove both instructions (get_Instance and Open)
                    i++; // skip next instruction as well
                    continue;
                }
                yield return codes[i];
            }
        }
    }
}