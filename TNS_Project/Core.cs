using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using MelonLoader;
using MelonLoader.Utils;
using System.Collections;
using System.Globalization;
using UnityEngine;

// Change this to your mod's info
[assembly: MelonInfo(typeof(Time_Never_Stops.Core), "Time Never Stops", "1.1.1", "DropDaDeuce", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<string> cfgDaySpeedStr;
        private MelonPreferences_Entry<float> cfgLegacyDaySpeed;

        private bool _updatingPref;

        private const float DefaultSpeed = 1.0f;
        private const float MinSpeed = 0.1f;
        private const float MaxSpeed = 100.0f;

        public override void OnInitializeMelon()
        {
            // 1) Legacy read from DEFAULT MelonPreferences.cfg
            var legacyCat = MelonPreferences.CreateCategory("TimeNeverStops");
            cfgLegacyDaySpeed = legacyCat.GetEntry<float>("DaySpeedMultiplier")
                ?? legacyCat.CreateEntry(
                    "DaySpeedMultiplier",
                    DefaultSpeed,
                    "Day Speed Multiplier",
                    "Sets the in-game time speed multiplier. 1.0 = normal speed, 0.5 = half speed, 2.0 = double speed.Minimum 0.1, Maximum 3.0. Default = 1.0 (Vanilla Game Speed)",
                    false, false);
            legacyCat.DeleteEntry("DaySpeedMultiplier"); // remove old entry if exists

            if (cfgLegacyDaySpeed.Value != DefaultSpeed)
            {
                // If the legacy value is not the default, warn user and migrate
                MelonLogger.Warning($"Legacy DaySpeedMultiplier found with value {cfgLegacyDaySpeed.Value}. Migrating to new config system.");
            }
            string migratedDefaultStr = Math.Clamp(cfgLegacyDaySpeed.Value,MinSpeed,MaxSpeed).ToString("R", CultureInfo.InvariantCulture);

            // 2) Rebind to your own file
            var cfgDir = Path.Combine(MelonEnvironment.UserDataDirectory, "DropDaDeuce-TimeNeverStops");
            var cfgFile = Path.Combine(cfgDir, "TimeNeverStops.cfg");
            Directory.CreateDirectory(cfgDir);

            cfgCategory = legacyCat; // reuse same object
            legacyCat = null; // no longer needed

            cfgCategory.SetFilePath(cfgFile, autoload: File.Exists(cfgFile), printmsg: true);

            // 3) New (string) entry — only use migrated default if missing
            cfgDaySpeedStr = cfgCategory.GetEntry<string>("DaySpeedMultiplier")
                         ?? cfgCategory.CreateEntry(
                                "DaySpeedMultiplier",
                                migratedDefaultStr,
                                "Day Speed Multiplier",
                                "String parsed to float by the mod.\n1.0 = normal, 0.5 = half, 2.0 = double.\nMin 0.1, Max 100.0 (Why? Because.), Default 1.0 (Vanilla Game Speed).",
                                false, false);

            // 4) Validate once and write file (no forced rounding)
            var initial = Sanitize(ParseFloatOrDefault(cfgDaySpeedStr.Value, DefaultSpeed));
            if (!IsStringValidAndInRange(cfgDaySpeedStr.Value))
                cfgDaySpeedStr.Value = initial.ToString(CultureInfo.InvariantCulture);

            _updatingPref = true;
            cfgCategory.SaveToFile();
            _updatingPref = false;

            // 5) Changes: accept any precision; only write back if invalid/out of range
            cfgDaySpeedStr.OnEntryValueChanged.Subscribe((oldV, newV) =>
            {
                if (_updatingPref) return;

                var parsed = ParseFloatOrDefault(newV, DefaultSpeed);
                var clamped = Sanitize(parsed);

                if (!IsStringValidAndInRange(newV))
                {
                    _updatingPref = true;
                    cfgDaySpeedStr.Value = clamped.ToString(CultureInfo.InvariantCulture);
                    cfgCategory.SaveToFile(false);
                    _updatingPref = false;
                }

                ApplyDaySpeed(clamped);
            });

            MelonCoroutines.Start(SetDaySpeedLoop());
        }

        // helpers
        private static bool TryParseFloatInvariant(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static float ParseFloatOrDefault(string s, float fallback) =>
            (TryParseFloatInvariant(s, out var v) && float.IsFinite(v)) ? v : fallback;

        private static float Sanitize(float v) =>
            Math.Clamp(float.IsFinite(v) ? v : DefaultSpeed, MinSpeed, MaxSpeed);

        // accept any string that parses and is within range
        private static bool IsStringValidAndInRange(string s) =>
            TryParseFloatInvariant(s, out var v) && float.IsFinite(v) && v >= MinSpeed && v <= MaxSpeed;

        // runtime application
        private IEnumerator SetDaySpeedLoop()
        {
            var tick = new WaitForSeconds(1f);

            while (true)
            {
                while (TimeManager.Instance == null) yield return null;

                var tm = TimeManager.Instance;
                float last = ReadPrefAndNormalize();
                ApplyDaySpeed(last);

                int i = 0;
                while (TimeManager.Instance == tm)
                {
                    // pull manual edits, but not every frame
                    if ((i++ % 2) == 0) cfgCategory.LoadFromFile(false);

                    float desired = ReadPrefAndNormalize();

                    if (Differs(desired, last) || Differs(desired, tm.TimeProgressionMultiplier))
                    {
                        last = desired;
                        ApplyDaySpeed(last);
                    }

                    yield return tick;
                }
            }

            float ReadPrefAndNormalize()
            {
                var raw = ParseFloatOrDefault(cfgDaySpeedStr.Value, DefaultSpeed);
                var val = Sanitize(float.IsFinite(raw) ? raw : DefaultSpeed);

                // Optional: reflect sanitized value back to the file so it stays clean.
                var s = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (cfgDaySpeedStr.Value != s)
                {
                    cfgDaySpeedStr.Value = s;
                    cfgCategory.SaveToFile(false);
                }
                return val;
            }

            static bool Differs(float a, float b, float eps = 1e-4f)
                => Mathf.Abs(a - b) > eps;
        }

        private static void ApplyDaySpeed(float newSpeed)
        {
            var tm = TimeManager.Instance;
            if (tm == null) return;

            var safe = Sanitize(newSpeed);
            if (Mathf.Abs(tm.TimeProgressionMultiplier - safe) > 0.0001f)
            {
                tm.TimeProgressionMultiplier = safe;
                MelonLogger.Msg($"Day speed set to {safe:0.########}x");
            }
        }
    }

    // --- Your Harmony patches are now children of the namespace ---

    public static class PatchLogger
    {
        public static void LogPatchLoad(string patchName)
        {
            MelonLogger.Msg($"[Harmony] {patchName} loaded.");
        }
    }

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

            // Only show summary if it's day 2 or later
            if (!firedToday && !busy && hhmm >= 700 && __instance.ElapsedDays >= 1)
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
