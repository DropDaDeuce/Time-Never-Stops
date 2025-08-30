using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

#if IL2CPP
using TimeManager = Il2CppScheduleOne.GameTime.TimeManager;
using EDay = Il2CppScheduleOne.GameTime.EDay;
using Player = Il2CppScheduleOne.PlayerScripts.Player;
using HUD = Il2CppScheduleOne.UI.HUD;
using DailySummary = Il2CppScheduleOne.UI.DailySummary;
using RankUpCanvas = Il2CppScheduleOne.UI.RankUpCanvas;
using GameManager = Il2CppScheduleOne.DevUtilities.GameManager;
using DevUtilities = Il2CppScheduleOne.DevUtilities;
using FishyNet = Il2CppFishNet;
#else // MONO
using TimeManager = ScheduleOne.GameTime.TimeManager;
using EDay = ScheduleOne.GameTime.EDay;
using Player = ScheduleOne.PlayerScripts.Player;
using HUD = ScheduleOne.UI.HUD;
using DailySummary = ScheduleOne.UI.DailySummary;
using RankUpCanvas = ScheduleOne.UI.RankUpCanvas;
using GameManager = ScheduleOne.DevUtilities.GameManager;
using DevUtilities = ScheduleOne.DevUtilities;
using FishyNet = FishNet;
#endif

[assembly: MelonInfo(typeof(Time_Never_Stops.Core), "Time Never Stops", Time_Never_Stops.BuildVersion.Value, "DropDaDeuce", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<string> cfgDaySpeedStr;
        public  static MelonPreferences_Entry<bool> cfgEnableSummaryAwake;
        private MelonPreferences_Entry<float> cfgLegacyDaySpeed;
        public  static MelonPreferences_Entry<bool> cfgMultiplierOnly; // NEW

        private bool _updatingPref;
        private bool _lastEnableSummaryAwake;

        private const float DefaultSpeed = 1.0f;
        private const float MinSpeed = 0.1f;
        private const float MaxSpeed = 100.0f;

        public override void OnInitializeMelon()
        {
            var legacyCat = MelonPreferences.CreateCategory("TimeNeverStops");
            cfgLegacyDaySpeed = legacyCat.GetEntry<float>("DaySpeedMultiplier")
                ?? legacyCat.CreateEntry(
                    "DaySpeedMultiplier",
                    DefaultSpeed,
                    "Day Speed Multiplier",
                    "Sets the in-game time speed multiplier. 1.0 = normal speed, 0.5 = half speed, 2.0 = double speed.Minimum 0.1, Maximum 3.0. Default = 1.0 (Vanilla Game Speed)",
                    false, false);
            legacyCat.DeleteEntry("DaySpeedMultiplier");

            if (cfgLegacyDaySpeed.Value != DefaultSpeed)
                MelonLogger.Warning($"Legacy DaySpeedMultiplier found with value {cfgLegacyDaySpeed.Value}. Migrating to new config system.");

            string migratedDefaultStr = Math.Clamp(cfgLegacyDaySpeed.Value, MinSpeed, MaxSpeed)
                                            .ToString("R", CultureInfo.InvariantCulture);

            var cfgDir = Path.Combine(MelonEnvironment.UserDataDirectory, "DropDaDeuce-TimeNeverStops");
            var cfgFile = Path.Combine(cfgDir, "TimeNeverStops.cfg");
            Directory.CreateDirectory(cfgDir);

            cfgCategory = legacyCat;
            legacyCat = null;
            cfgCategory.SetFilePath(cfgFile, autoload: File.Exists(cfgFile), printmsg: true);

            // New master toggle
            cfgMultiplierOnly = cfgCategory.GetEntry<bool>("TimeMultiplierOnlyMode")
                ?? cfgCategory.CreateEntry(
                    "TimeMultiplierOnlyMode",
                    false,
                    "Multiplier Only Mode",
                    "If true the mod ONLY changes the day speed multiplier.\nDisables:\n- 4AM freeze bypass\n- Daily summary while awake\n- Sleep prompt hiding.\nChange at runtime is applied but a restart is safest.",
                    false, false);

            cfgDaySpeedStr = cfgCategory.GetEntry<string>("DaySpeedMultiplier")
                         ?? cfgCategory.CreateEntry(
                                "DaySpeedMultiplier",
                                migratedDefaultStr,
                                "Day Speed Multiplier",
                                "String parsed to float by the mod.\n1.0 = normal, 0.5 = half, 2.0 = double.\nMin 0.1, Max 100.0, Default 1.0.",
                                false, false);

            cfgEnableSummaryAwake = cfgCategory.GetEntry<bool>("EnableDailySummaryAwake")
                ?? cfgCategory.CreateEntry(
                    "EnableDailySummaryAwake",
                    true,
                    "Enable Daily Summary While Awake",
                    "If enabled, the daily summary will be shown while awake (unless Multiplier Only Mode is ON).",
                    false, false);

            if (!cfgMultiplierOnly.Value)
            {
                _lastEnableSummaryAwake = cfgEnableSummaryAwake.Value;
                MelonCoroutines.Start(WatchSummaryToggle());
                MelonCoroutines.Start(SleepStateWatcher());
            }
            else
            {
                MelonLogger.Msg("TimeMultiplierOnlyMode enabled: all non-multiplier features disabled.");
            }

            var initial = Sanitize(ParseFloatOrDefault(cfgDaySpeedStr.Value, DefaultSpeed));
            if (!IsStringValidAndInRange(cfgDaySpeedStr.Value))
                cfgDaySpeedStr.Value = initial.ToString(CultureInfo.InvariantCulture);

            _updatingPref = true;
            cfgCategory.SaveToFile();
            _updatingPref = false;

            cfgDaySpeedStr.OnEntryValueChanged.Subscribe((_, newV) =>
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

            // Always run multiplier loop
            MelonCoroutines.Start(SetDaySpeedLoop());
        }

        private IEnumerator SleepStateWatcher()
        {
            bool last = false;
            TimeManager lastTm = null;
            while (true)
            {
                while (TimeManager.Instance == null) { lastTm = null; last = false; yield return null; }
                var tm = TimeManager.Instance;
                if (tm != lastTm) { lastTm = tm; last = tm.SleepInProgress; }
                bool cur = tm.SleepInProgress;
                if (cur && !last) OnSleepStart();
                else if (!cur && last) OnSleepEnd();
                last = cur;
                yield return null;
            }
        }

        private void OnSleepStart() => Patch_Tick_XPMenu.SuppressDuringSleep(true);
        private void OnSleepEnd()
        {
            Patch_Tick_XPMenu.SuppressDuringSleep(false);
            Patch_Tick_XPMenu.MarkHandledForToday();
        }

        private static bool TryParseFloatInvariant(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static float ParseFloatOrDefault(string s, float fallback) =>
            (TryParseFloatInvariant(s, out var v) && float.IsFinite(v)) ? v : fallback;

        private static float Sanitize(float v) =>
            Math.Clamp(float.IsFinite(v) ? v : DefaultSpeed, MinSpeed, MaxSpeed);

        private static bool IsStringValidAndInRange(string s) =>
            TryParseFloatInvariant(s, out var v) && float.IsFinite(v) && v >= MinSpeed && v <= MaxSpeed;

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
                    if ((i++ & 1) == 0) cfgCategory.LoadFromFile(false);
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
                var s = val.ToString(CultureInfo.InvariantCulture);
                if (cfgDaySpeedStr.Value != s)
                {
                    cfgDaySpeedStr.Value = s;
                    cfgCategory.SaveToFile(false);
                }
                return val;
            }

            static bool Differs(float a, float b, float eps = 1e-4f) => Mathf.Abs(a - b) > eps;
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

        private IEnumerator WatchSummaryToggle()
        {
            var tick = new WaitForSeconds(1f);
            while (true)
            {
                bool cur = cfgEnableSummaryAwake.Value;
                if (cur != _lastEnableSummaryAwake)
                {
                    _lastEnableSummaryAwake = cur;
                    cfgCategory.SaveToFile(false);
                    MelonLogger.Msg($"EnableDailySummaryAwake: {(cur ? "ON" : "OFF")}");
                }
                yield return tick;
            }
        }
    }

    public static class PatchLogger
    {
        public static void LogPatchLoad(string patchName) =>
            MelonLogger.Msg($"[Harmony] {patchName} loaded.");
    }

    [HarmonyPatch(typeof(TimeManager), "Tick")]
    [HarmonyPriority(Priority.High)]
    public static class Patch_Tick_XPMenu
    {
        private static bool firedToday, busy;
        private static int lastHHmm;
        private static bool suppressForSleep;
        public static bool startupSkip = true;

        static Patch_Tick_XPMenu()
        {
            PatchLogger.LogPatchLoad(nameof(Patch_Tick_XPMenu));
            startupSkip = true;
        }

        public static void SuppressDuringSleep(bool on) => suppressForSleep = on;
        public static void MarkHandledForToday()
        {
            firedToday = true;
            lastHHmm = 700;
        }

        [HarmonyPrefix]
        public static bool Prefix(TimeManager __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true)
                return true; // vanilla behavior when multiplier-only mode

            bool wouldFreeze = (__instance.CurrentTime == 400) ||
                               (__instance.IsCurrentTimeWithinRange(400, 600) && !GameManager.IS_TUTORIAL);
            if (!wouldFreeze) return true;

            if ((UnityEngine.Object)Player.Local == null)
            {
                MelonLogger.Warning("Local player does not exist. Waiting for player spawn.");
                return false;
            }

            TMAccess.SetTimeOnCurrentMinute(__instance, 0f);
            TMAccess.TryStartStaggeredMinPass(__instance,
                1.0f / (__instance.TimeProgressionMultiplier * Time.timeScale));

            if (__instance.CurrentTime == 2359)
            {
                TMAccess.SetElapsedDays(__instance, __instance.ElapsedDays + 1);
                TMAccess.SetCurrentTime(__instance, 0);
                TMAccess.SetDailyMinTotal(__instance, 0);
                __instance.onDayPass?.Invoke();
                __instance.onHourPass?.Invoke();
                if (__instance.CurrentDay == EDay.Monday)
                    __instance.onWeekPass?.Invoke();
            }
            else if (__instance.CurrentTime % 100 >= 59)
            {
                TMAccess.SetCurrentTime(__instance, __instance.CurrentTime + 41);
                __instance.onHourPass?.Invoke();
            }
            else
            {
                TMAccess.SetCurrentTime(__instance, __instance.CurrentTime + 1);
            }

            TMAccess.SetDailyMinTotal(__instance, TimeManager.GetMinSumFrom24HourTime(__instance.CurrentTime));
            __instance.HasChanged = true;
            if (__instance.ElapsedDays == 0 && __instance.CurrentTime == 2000 && __instance.onFirstNight != null)
                __instance.onFirstNight.Invoke();

            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(TimeManager __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true)
                return;

            bool enableSummaryAwake = Core.cfgEnableSummaryAwake?.Value ?? true;
            if (!FishyNet.InstanceFinder.IsHost || GameManager.IS_TUTORIAL || !enableSummaryAwake) return;
            if (__instance.SleepInProgress || suppressForSleep) return;

            var ds = DevUtilities.NetworkSingleton<DailySummary>.Instance;
            if (ds != null && ds.IsOpen) return;

            if (startupSkip)
            {
                startupSkip = false;
                lastHHmm = __instance.CurrentTime;
                firedToday = true;
                return;
            }

            int hhmm = __instance.CurrentTime;
            if (lastHHmm > hhmm) firedToday = false;

            if (!firedToday && !busy && hhmm >= 700 && __instance.ElapsedDays >= 1)
            {
                MelonCoroutines.Start(ShowDailySummaryRoutine(__instance));
                firedToday = true;
            }

            lastHHmm = hhmm;
        }

        private static IEnumerator ShowDailySummaryRoutine(TimeManager tm)
        {
            busy = true;
            tm.ResetHostSleepDone();

            var ds = DevUtilities.NetworkSingleton<DailySummary>.Instance;
            var hud = DevUtilities.Singleton<HUD>.Instance;
            if (ds == null || ds.IsOpen) { busy = false; yield break; }

            if (hud != null) hud.canvas.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<UnityEngine.EventSystems.EventSystem>();
                go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            ds.Open();
            while (ds.IsOpen) yield return null;

            var rankCanvas = UnityEngine.Object.FindObjectOfType<RankUpCanvas>(true);
            if (rankCanvas != null)
            {
                rankCanvas.StartEvent();
                if (hud != null) hud.canvas.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Time.timeScale = 1f;
                while (rankCanvas.IsRunning) yield return null;
            }

            tm.MarkHostSleepDone();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (hud != null) hud.canvas.enabled = true;
            else
            {
                hud = DevUtilities.Singleton<HUD>.Instance;
                if (hud != null) hud.canvas.enabled = true;
                else MelonLogger.Warning("HUD not found. Cannot restore HUD canvas.");
            }
            busy = false;
        }
    }

    [HarmonyPatch(typeof(HUD), "Update")]
    [HarmonyPriority(Priority.Low)]
    public static class Patch_HUD_HideSleepPrompt
    {
        static void Postfix(HUD __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return; // disabled in multiplier-only mode
            if (__instance?.SleepPrompt != null)
                __instance.SleepPrompt.gameObject.SetActive(false);
        }
    }

#if MONO
    internal static class TMAccess
    {
        private static readonly Action<TimeManager, int> _setCurrentTime;
        private static readonly Action<TimeManager, int> _setElapsedDays;
        private static readonly Action<TimeManager, int> _setDailyMinTotal;
        private static readonly Action<TimeManager, float> _setTimeOnCurrentMinute;
        private static readonly MethodInfo _staggeredMinPass;

        static TMAccess()
        {
            _setCurrentTime       = BuildSetter<int>("CurrentTime");
            _setElapsedDays       = BuildSetter<int>("ElapsedDays");
            _setDailyMinTotal     = BuildSetter<int>("DailyMinTotal");
            _setTimeOnCurrentMinute = BuildSetter<float>("TimeOnCurrentMinute");
            _staggeredMinPass     = AccessTools.Method(typeof(TimeManager), "StaggeredMinPass");
        }

        private static Action<TimeManager, T> BuildSetter<T>(string propName)
        {
            var setter = AccessTools.PropertySetter(typeof(TimeManager), propName);
            if (setter == null) return (_, __) => { };
            return (Action<TimeManager, T>)Delegate.CreateDelegate(typeof(Action<TimeManager, T>), null, setter);
        }

        public static void SetCurrentTime(TimeManager tm, int v)         => _setCurrentTime(tm, v);
        public static void SetElapsedDays(TimeManager tm, int v)         => _setElapsedDays(tm, v);
        public static void SetDailyMinTotal(TimeManager tm, int v)       => _setDailyMinTotal(tm, v);
        public static void SetTimeOnCurrentMinute(TimeManager tm, float v)=> _setTimeOnCurrentMinute(tm, v);

        public static void TryStartStaggeredMinPass(TimeManager tm, float arg)
        {
            if (_staggeredMinPass == null) return;
            try
            {
                var enumerator = _staggeredMinPass.Invoke(tm, new object[] { arg }) as IEnumerator;
                if (enumerator != null)
                    tm.StartCoroutine(enumerator);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not invoke StaggeredMinPass (Mono): {ex.Message}");
            }
        }
    }
#else
    internal static class TMAccess
    {
        public static void SetCurrentTime(TimeManager tm, int v)          => tm.CurrentTime = v;
        public static void SetElapsedDays(TimeManager tm, int v)          => tm.ElapsedDays = v;
        public static void SetDailyMinTotal(TimeManager tm, int v)        => tm.DailyMinTotal = v;
        public static void SetTimeOnCurrentMinute(TimeManager tm, float v)=> tm.TimeOnCurrentMinute = v;
        public static void TryStartStaggeredMinPass(TimeManager tm, float arg) =>
            tm.StartCoroutine(tm.StaggeredMinPass(arg));
    }
#endif
}