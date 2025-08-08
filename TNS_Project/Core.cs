using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using System.Collections;
using UnityEngine;

[assembly: MelonInfo(typeof(Time_Never_Stops.Core), "Time Never Stops", "1.0.0", "DropDaDeuce", null)] // Change this
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<float> cfgDaySpeed;

        public override void OnInitializeMelon()
        {
            cfgCategory = MelonPreferences.CreateCategory("TimeNeverStops", "Time Never Stops Settings");
            cfgDaySpeed = cfgCategory.CreateEntry<float>(
                "DaySpeedMultiplier",
                1.0f,
                "Day Speed Multiplier",
                "Sets the in-game time speed multiplier. 1.0 = normal speed, 0.5 = half speed, 2.0 = double speed.\nMinimum 0.1, Maximum 3.0. Default = 1.0 (Vanilla Game Speed)",
                false, false
            );
            MelonCoroutines.Start(SetDaySpeedLoop());
            LoggerInstance.Msg("Time Never Stops Initialized.");
            cfgDaySpeed.OnEntryValueChanged.Subscribe((oldV, newV) =>
            {
                var tm = TimeManager.Instance;
                if (tm != null)
                {
                    float m = Mathf.Clamp(newV, 0.1f, 3.0f);
                    tm.TimeProgressionMultiplier = m;
                    LoggerInstance.Msg($"Day speed set to {m:0.##}x");
                }
            });
        }

        private IEnumerator SetDaySpeedLoop()
        {
            // Wait for TimeManager to exist
            while (TimeManager.Instance == null)
                yield return null;
            var timeManager = TimeManager.Instance;

            // Clamp and set the multiplier
            float multiplier = Mathf.Clamp(cfgDaySpeed.Value, 0.1f, 3.0f);
            timeManager.TimeProgressionMultiplier = multiplier;
            LoggerInstance.Msg($"Day speed set to {multiplier:0.##}x ({multiplier * 100f:0.#}% of normal speed)");
        }

        public override void OnUpdate()
        {
            // Press F12 to fast forward to 11PM (23:00)
            if (Input.GetKeyDown(KeyCode.F12))
            {
                var timeManager = Il2CppScheduleOne.GameTime.TimeManager.Instance;
                if (timeManager != null)
                {
                    timeManager.SetTime(2300, true); // 2300 = 11:00 PM
                    MelonLogger.Msg("Fast forwarded to 11PM for testing.");
                }
            }
        }
    }

   public static class PatchLogger
    {
        public static void LogPatchLoad(string patchName)
        {
            MelonLogger.Msg($"[Harmony] {patchName} loaded.");
        }
    }

    //Patches for TimeManager and SleepCanvas to modify time behavior

    /*[HarmonyPatch(typeof(TimeManager), "get_IsEndOfDay")]
    public static class Patch_IsEndOfDay
    {
        static Patch_IsEndOfDay()
        {
            PatchLogger.LogPatchLoad(nameof(Patch_IsEndOfDay));
        }

        public static bool Prefix(ref bool __result)
        {
            __result = false; // Never stop time at 4am
            return false;     // Skip original getter
        }
    }*/

    [HarmonyPatch(typeof(TimeManager), "Tick")]
    [HarmonyPriority(Priority.High)]
    public static class Patch_Tick_XPMenu
    {
        private static bool firedToday, busy;
        private static int lastHHmm;

        // NEW: skip the very first Tick after a fresh load/scene init
        private static bool startupSkip = true;

        static Patch_Tick_XPMenu()
        {
            PatchLogger.LogPatchLoad(nameof(Patch_Tick_XPMenu));
            startupSkip = true; // ensure true on domain reload
        }

        // --- your existing freeze-window bypass Prefix stays the same ---
        [HarmonyPrefix]
        public static bool Prefix(TimeManager __instance)
        {
            bool wouldFreeze = (__instance.CurrentTime == 400) ||
                               (__instance.IsCurrentTimeWithinRange(400, 600) && !GameManager.IS_TUTORIAL);

            if (!wouldFreeze)
                return true;

            if ((UnityEngine.Object)Player.Local == (UnityEngine.Object)null)
            {
                MelonLogger.Warning("Local player does not exist. Waiting for player to spawn.");
                return false;
            }

            __instance.TimeOnCurrentMinute = 0.0f;
            try
            {
                __instance.StartCoroutine(__instance.StaggeredMinPass(
                    1.0f / (__instance.TimeProgressionMultiplier * Time.timeScale)));
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error invoking onMinutePass: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }

            if (__instance.CurrentTime == 2359)
            {
                ++__instance.ElapsedDays;
                __instance.CurrentTime = 0;
                __instance.DailyMinTotal = 0;
                __instance.onDayPass?.Invoke();
                __instance.onHourPass?.Invoke();
                if (__instance.CurrentDay == EDay.Monday)
                    __instance.onWeekPass?.Invoke();
            }
            else if (__instance.CurrentTime % 100 >= 59)
            {
                __instance.CurrentTime += 41;
                __instance.onHourPass?.Invoke();
            }
            else
            {
                ++__instance.CurrentTime;
            }

            __instance.DailyMinTotal = TimeManager.GetMinSumFrom24HourTime(__instance.CurrentTime);
            __instance.HasChanged = true;
            if (__instance.ElapsedDays == 0 && __instance.CurrentTime == 2000 && __instance.onFirstNight != null)
                __instance.onFirstNight.Invoke();

            return false;
        }

        // --- Postfix with startup guard ---
        [HarmonyPostfix]
        public static void Postfix(TimeManager __instance)
        {
            if (!InstanceFinder.IsHost || GameManager.IS_TUTORIAL) return;

            // Skip the first tick right after load/init so we don’t fire immediately on startup
            if (startupSkip)
            {
                startupSkip = false;
                lastHHmm = __instance.CurrentTime; // initialize baseline

                // Prevent summary on load if time is already past 7:00
                firedToday = __instance.CurrentTime >= 700;
                return;
            }

            int hhmm = __instance.CurrentTime;

            // re-arm daily trigger on midnight rollover
            if (lastHHmm > hhmm) firedToday = false;

            if (!firedToday && !busy && hhmm >= 700)
            {
                MelonCoroutines.Start(ShowDailySummaryRoutine(__instance));
                firedToday = true;
            }

            lastHHmm = hhmm;
        }

        private static System.Collections.IEnumerator ShowDailySummaryRoutine(TimeManager tm)
        {
            busy = true;
            tm.ResetHostSleepDone();

            var ds = NetworkSingleton<DailySummary>.Instance;
            var hud = Singleton<HUD>.Instance;
            if (ds == null || ds.IsOpen) { busy = false; yield break; }

            // HUD off + cursor ON so you can click the UI
            if (hud != null) hud.canvas.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f; // just in case

            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<UnityEngine.EventSystems.EventSystem>();
                go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Open & wait until user clicks Continue
            ds.Open();
            while (ds.IsOpen)
                yield return null;

            // Handshake for clients
            tm.MarkHostSleepDone();

            // Restore HUD + cursor lock for gameplay
            if (hud != null) hud.canvas.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            busy = false;
        }
    }

    [HarmonyPatch(typeof(SleepCanvas), "SleepStart")]
    public static class Patch_SleepCanvas_SleepStart
    {
        static Patch_SleepCanvas_SleepStart()
        {
            PatchLogger.LogPatchLoad(nameof(Patch_SleepCanvas_SleepStart));
        }

        public static bool Prefix(SleepCanvas __instance)
        {
            Player.Local.SetReadyToSleep(false);
            __instance.MenuContainer.gameObject.SetActive(false);
            __instance.IsMenuOpen = false;
            __instance.WakeLabel.text = "Waking up at " + TimeManager.Get12HourTime(700f);
            MelonCoroutines.Start(CustomSleepCoroutine(__instance));
            return false; // Skip original
        }

        private static IEnumerator CustomSleepCoroutine(SleepCanvas __instance)
        {
            __instance.BlackOverlay.enabled = true;
            __instance.SleepMessageLabel.text = string.Empty;

            if (InstanceFinder.IsServer)
            {
                MelonLogger.Msg("Resetting host sleep done");
                NetworkSingleton<TimeManager>.Instance.ResetHostSleepDone();
            }

            var hud = Singleton<HUD>.Instance;
            if (hud != null) hud.canvas.enabled = false;

            __instance.LerpBlackOverlay(1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.5f).Cast<Il2CppSystem.Object>();

            __instance.onSleepFullyFaded?.Invoke();

            yield return new WaitForSecondsRealtime(0.35f).Cast<Il2CppSystem.Object>();

            var tm = TimeManager.Instance;
            tm?.SetTime(659, true);

            __instance.LerpBlackOverlay(0f, 0.35f);
            yield return new WaitForSecondsRealtime(0.35f).Cast<Il2CppSystem.Object>();
            __instance.BlackOverlay.enabled = false;

            if (hud != null) hud.canvas.enabled = true;

            Time.timeScale = 1f;

            __instance.SetIsOpen(false);
        }
    }

    [HarmonyPatch(typeof(HUD), "FixedUpdate")]
    [HarmonyPriority(Priority.Low)] // run after vanilla & other patches
    public static class Patch_HUD_HideSleepPrompt
    {
        static void Postfix(HUD __instance)
        {
            // Only shows at 04:00; just force it off
            if (__instance != null && __instance.SleepPrompt != null)
                __instance.SleepPrompt.gameObject.SetActive(false);
        }
    }
}