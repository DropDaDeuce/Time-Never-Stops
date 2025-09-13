using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.IO;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using static MelonLoader.MelonLogger;


#if IL2CPP
using TimeManager = Il2CppScheduleOne.GameTime.TimeManager;
using EDay = Il2CppScheduleOne.GameTime.EDay;
using Player = Il2CppScheduleOne.PlayerScripts.Player;
using PlayerScript = Il2CppScheduleOne.PlayerScripts;
using HUD = Il2CppScheduleOne.UI.HUD;
using UI = Il2CppScheduleOne.UI;
using GameManager = Il2CppScheduleOne.DevUtilities.GameManager;
using DevUtilities = Il2CppScheduleOne.DevUtilities;
using PlayerCamera = Il2CppScheduleOne.PlayerScripts.PlayerCamera;
using SleepCanvas = Il2CppScheduleOne.UI.SleepCanvas;
using Employee = Il2CppScheduleOne.Employees.Employee;
#else // MONO
using TimeManager = ScheduleOne.GameTime.TimeManager;
using EDay = ScheduleOne.GameTime.EDay;
using Player = ScheduleOne.PlayerScripts.Player;
using PlayerScript = ScheduleOne.PlayerScripts;
using HUD = ScheduleOne.UI.HUD;
using UI = ScheduleOne.UI;
using GameManager = ScheduleOne.DevUtilities.GameManager;
using DevUtilities = ScheduleOne.DevUtilities;
using PlayerCamera = ScheduleOne.PlayerScripts.PlayerCamera;
using SleepCanvas = ScheduleOne.UI.SleepCanvas;
using Employee = ScheduleOne.Employees.Employee;
#endif

[assembly: MelonInfo(typeof(Time_Never_Stops.Core), "Time Never Stops", Time_Never_Stops.BuildVersion.Value, "DropDaDeuce", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    internal static class TNSLog
    {
        // Toggle comes from config.
        private static bool DebugEnabled => Core.cfgDebugLogging?.Value == true;

        // Info
        public static void Msg(string message) => MelonLogger.Msg(message);
        public static void Msg(string format, params object[] args) => MelonLogger.Msg(string.Format(format, args));

        // Debug (hidden unless enabled)
        public static void Debug(string message)
        {
            if (DebugEnabled)
                MelonLogger.Msg($"[Debug] {message}");
        }
        public static void Debug(string format, params object[] args)
        {
            if (DebugEnabled)
                MelonLogger.Msg("[Debug] " + string.Format(format, args));
        }

        // Warning
        public static void Warning(string message) => MelonLogger.Warning(message);
        public static void Warning(string format, params object[] args) => MelonLogger.Warning(string.Format(format, args));

        // Error
        public static void Error(string message) => MelonLogger.Error(message);
        public static void Error(string format, params object[] args) => MelonLogger.Error(string.Format(format, args));
    }

    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<string> cfgDaySpeedStr;
        public static MelonPreferences_Entry<bool> cfgEnableSummaryAwake;
        private MelonPreferences_Entry<float> cfgLegacyDaySpeed;
        public static MelonPreferences_Entry<bool> cfgMultiplierOnly; // NEW
        public static MelonPreferences_Entry<bool> cfgDebugLogging; // NEW

        private bool _updatingPref;
        private bool _lastEnableSummaryAwake;

        private const float DefaultSpeed = 1.0f;
        private const float MinSpeed = 0.1f;
        private const float MaxSpeed = 100.0f;

        // New: file watcher to avoid polling disk every second
        private FileSystemWatcher _cfgWatcher;
        private volatile bool _cfgFileChanged;

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
                TNSLog.Warning($"Legacy DaySpeedMultiplier found with value {cfgLegacyDaySpeed.Value}. Migrating to new config system.");

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

            cfgDebugLogging = cfgCategory.GetEntry<bool>("EnableDebugLogging")
                ?? cfgCategory.CreateEntry(
                    "EnableDebugLogging",
                    false,
                    "Enable Debug Logging",
                    "If true, prints additional debug messages from the mod to help diagnose issues.",
                    false, false);

            if (!cfgMultiplierOnly.Value)
            {
                _lastEnableSummaryAwake = cfgEnableSummaryAwake.Value;
                MelonCoroutines.Start(SleepStateWatcher());
            }
            else
            {
                TNSLog.Msg("TimeMultiplierOnlyMode enabled: all non-multiplier features disabled.");
            }

            var initial = Sanitize(ParseFloatOrDefault(cfgDaySpeedStr.Value, DefaultSpeed));
            if (!IsStringValidAndInRange(cfgDaySpeedStr.Value))
                cfgDaySpeedStr.Value = initial.ToString(CultureInfo.InvariantCulture);

            // Debounced initial save
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

            cfgEnableSummaryAwake.OnEntryValueChanged.Subscribe((_, newVal) =>
            {
                if (_updatingPref) return;
                _lastEnableSummaryAwake = newVal;
                TNSLog.Msg($"EnableDailySummaryAwake: {(newVal ? "ON" : "OFF")}");
                _updatingPref = true;
                cfgCategory.SaveToFile(false);
                _updatingPref = false;
            });

            cfgDebugLogging.OnEntryValueChanged.Subscribe((_, newVal) =>
            {
                if (_updatingPref) return;
                cfgDebugLogging.Value = newVal;
                TNSLog.Msg($"DebugLogging: {(newVal ? "Enabled" : "Disabled")}");
                _updatingPref = true;
                cfgCategory.SaveToFile(false);
                _updatingPref = false;
            });

            // Setup file watcher to react to external config edits without tight polling
            try
            {
                _cfgWatcher = new FileSystemWatcher(cfgDir, Path.GetFileName(cfgFile))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler markDirty = (_, __) => _cfgFileChanged = true;
                RenamedEventHandler markDirtyRename = (_, __) => _cfgFileChanged = true;
                _cfgWatcher.Changed += markDirty;
                _cfgWatcher.Created += markDirty;
                _cfgWatcher.Renamed += markDirtyRename;
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Config watcher init failed: {ex.Message}. Falling back to in-process updates only.");
            }

            // Always run multiplier loop
            MelonCoroutines.Start(SetDaySpeedLoop());
        }

        public override void OnDeinitializeMelon()
        {
            try { _cfgWatcher?.Dispose(); } catch { /* ignore */ }
            _cfgWatcher = null;
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

        private void OnSleepStart()
        {
            Patch_Tick_XPMenu.SuppressDuringSleep(true);
            TNSLog.Debug("Sleep start");
        }
        private void OnSleepEnd()
        {
            Patch_SleepCanvas_SleepStart.StopRoutine();
            Patch_Tick_XPMenu.SuppressDuringSleep(false);
            Patch_Tick_XPMenu.MarkHandledForToday();
            Patch_Tick_XPMenu.EnsureHUDReset();
            TNSLog.Debug("Sleep end -> PaidForToday reset path should run on employees");
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
            // Use realtime so timeScale changes do not stall preference enforcement
            var tick = new WaitForSecondsRealtime(1f);
            while (true)
            {
                while (TimeManager.Instance == null) yield return null;
                var tm = TimeManager.Instance;
                float last = ReadPrefAndNormalize();
                ApplyDaySpeed(last);

                while (TimeManager.Instance == tm)
                {
                    // Reload if external edits occurred
                    if (_cfgFileChanged)
                    {
                        _cfgFileChanged = false;
                        cfgCategory.LoadFromFile(false);
                    }

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
                    _updatingPref = true;
                    cfgDaySpeedStr.Value = s;
                    cfgCategory.SaveToFile(false);
                    _updatingPref = false;
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
                TNSLog.Msg($"Day speed set to {safe:0.########}x");
            }
        }
    }

    public static class PatchLogger
    {
        public static void LogPatchLoad(string patchName) =>
            TNSLog.Msg($"[Harmony] {patchName} loaded.");
    }

    [HarmonyPatch(typeof(TimeManager), "Tick")]
    [HarmonyPriority(Priority.High)]
    public static class Patch_Tick_XPMenu
    {
        private static bool firedToday;
        private static int lastHHmm;
        private static int lastHH = -1;
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
                TNSLog.Warning("Local player does not exist. Waiting for player spawn.");
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

            if (lastHH != (int)(__instance.CurrentTime / 100)) { TNSLog.Debug($"Current Time: {__instance.CurrentTime} Current Day: {__instance.CurrentDay} Days Elapsed: {__instance.ElapsedDays}"); }
            lastHH = (int)(__instance.CurrentTime / 100);

            if (__instance.SleepInProgress || suppressForSleep) return;

            var ds = DevUtilities.NetworkSingleton<UI.DailySummary>.Instance;
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

            if (!firedToday && __instance.CurrentTime > 658 && __instance.ElapsedDays > 0)
            {
                firedToday = true;
                TNSLog.Debug("Forcing Sleep in code.");
                TimeManager.Instance.ForceSleep();
            }

            lastHHmm = hhmm;
        }

        public static void EnsureHUDReset()
        {
            Player.Local.CurrentBed = null;
            Player.Local.SetReadyToSleep(ready: false);
            DevUtilities.PlayerSingleton<PlayerCamera>.Instance.SetCanLook(c: true);
            DevUtilities.PlayerSingleton<PlayerCamera>.Instance.StopTransformOverride(0f, reenableCameraLook: true, returnToOriginalRotation: false);
            DevUtilities.PlayerSingleton<PlayerCamera>.Instance.LockMouse();
            DevUtilities.PlayerSingleton<PlayerScript.PlayerInventory>.Instance.SetInventoryEnabled(enabled: true);
            DevUtilities.Singleton<UI.InputPromptsCanvas>.Instance.UnloadModule();
            DevUtilities.PlayerSingleton<PlayerScript.PlayerMovement>.Instance.canMove = true;
            SleepCanvas.Instance.MenuContainer.gameObject.SetActive(false);
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

    [HarmonyPatch(typeof(UI.RankUpCanvas), "StartEvent")]
    public static class Patch_RankUpCanvas_StartEvent
    {
        static Patch_RankUpCanvas_StartEvent() => PatchLogger.LogPatchLoad(nameof(Patch_RankUpCanvas_StartEvent));

        [HarmonyPrefix]
        public static bool PostFix(UI.RankUpCanvas __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == false && Core.cfgEnableSummaryAwake?.Value == false)
            {
                TNSLog.Debug("Rank Up Canvas Skipping");
                __instance.Canvas.enabled = false;
                __instance.EndEvent();
                return false; // Skip original method
            }

            TNSLog.Debug("Rank Up Canvas Running");
            return true; // Run original method
        }
    }

    [HarmonyPatch(typeof(UI.RegionUnlockedCanvas), "StartEvent")]
    public static class Patch_RegionUnlockedCanvas_StartEvent
    {
        static Patch_RegionUnlockedCanvas_StartEvent() => PatchLogger.LogPatchLoad(nameof(Patch_RegionUnlockedCanvas_StartEvent));

        [HarmonyPrefix]
        public static bool PostFix(UI.RegionUnlockedCanvas __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == false && Core.cfgEnableSummaryAwake?.Value == false)
            {
                TNSLog.Debug("Region Unlocked Canvas Skipping");
                __instance.EndEvent();
                return false; // Skip original method
            }

            TNSLog.Debug("Region Unlocked Canvas Running");
            return true; // Run original method
        }
    }

    [HarmonyPatch(typeof(UI.DailySummary))]
    public static class DailySummaryPatch
    {

        [HarmonyPrefix]
        [HarmonyPatch(nameof(UI.DailySummary.Open))]
        public static bool OpenPrefix(UI.DailySummary __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == false && Core.cfgEnableSummaryAwake?.Value == false)
            {
                TNSLog.Debug("Daily Summary Open Skipping");
                return false; // This skips the original Open method
            }
            TNSLog.Debug("Daily Summary Open Running");
            return true; // Run original method
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(UI.DailySummary.Close))]
        public static bool ClosePrefix(UI.DailySummary __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == false && Core.cfgEnableSummaryAwake?.Value == false)
            {
                TNSLog.Debug("Daily Summary Close Skipping");
                return false; // This skips the original Close method
            }
            TNSLog.Debug("Daily Summary Close Running");
            return true; // Run original method
        }
    }

    [HarmonyPatch(typeof(SleepCanvas), "SleepStart")]
    public static class Patch_SleepCanvas_SleepStart
    {
        // Small guard coroutine handle
        private static object _routine;

        [HarmonyPrefix]
        public static void Prefix(SleepCanvas __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return; // disabled in multiplier-only mode

            // Start a very short-lived coroutine that will survive through
            // the original SleepStart body (which disables look).
            if (_routine != null)
                MelonCoroutines.Stop(_routine);

            TNSLog.Debug("Running SleepStart Prefix.");

            _routine = MelonCoroutines.Start(MakeThingsWorkRoutine(__instance));
        }

        public static void StopRoutine()
        {
            if (Core.cfgMultiplierOnly?.Value == true) return; // disabled in multiplier-only mode

            // Stop the helper routine early (it will also end itself, this is just defensive).
            if (_routine != null)
            {
                MelonCoroutines.Stop(_routine);
                _routine = null;
            }
        }

        private static IEnumerator MakeThingsWorkRoutine(SleepCanvas __instance)
        {
            const int frames = 3;
            for (int i = 0; i < frames; i++)
            {
                MakeThingsWork(__instance);
                yield return null;
            }
            _routine = null;
        }

        private static void MakeThingsWork(SleepCanvas __instance)
        {
            try
            {
                var cam = DevUtilities.PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) return;

                // Re-enable looking unconditionally.
                cam.SetCanLook(true);

                if (Core.cfgEnableSummaryAwake?.Value == false) { cam.LockMouse(); } else { cam.FreeMouse(); }
                if (Core.cfgEnableSummaryAwake?.Value == false)
                {
                    SCAccess.LerpBlackOverlay(__instance, 0f, 0.1f);
                }
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"SleepStart look restore failed: {ex.Message}");
            }
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
                TNSLog.Warning($"Could not invoke StaggeredMinPass (Mono): {ex.Message}");
            }
        }
    }
#else
    internal static class TMAccess
    {
        public static void SetCurrentTime(TimeManager tm, int v) => tm.CurrentTime = v;
        public static void SetElapsedDays(TimeManager tm, int v) => tm.ElapsedDays = v;
        public static void SetDailyMinTotal(TimeManager tm, int v) => tm.DailyMinTotal = v;
        public static void SetTimeOnCurrentMinute(TimeManager tm, float v) => tm.TimeOnCurrentMinute = v;
        public static void TryStartStaggeredMinPass(TimeManager tm, float arg) =>
            tm.StartCoroutine(tm.StaggeredMinPass(arg));
    }
#endif

    internal static class SCAccess
    {
        private static readonly MethodInfo _lerpBlackOverlay =
            AccessTools.Method(typeof(SleepCanvas), "LerpBlackOverlay", new[] { typeof(float), typeof(float) });

        public static void LerpBlackOverlay(SleepCanvas inst, float transparency, float lerpTime)
        {
            if (_lerpBlackOverlay != null)
            {
                try
                {
                    _lerpBlackOverlay.Invoke(inst, new object[] { transparency, lerpTime });
                    return;
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"Invoke SleepCanvas.LerpBlackOverlay failed: {ex.Message}");
                }
            }
        }
    }

    // Debug helpers to verify sleep flow and employee payment.
    [HarmonyPatch(typeof(TimeManager))]
    public static class Debug_TimeManager_SleepFlow
    {
        [HarmonyPostfix, HarmonyPatch("ForceSleep")]
        static void ForceSleep_Postfix(TimeManager __instance)
            => TNSLog.Debug("ForceSleep invoked. SleepInProgress=" + __instance.SleepInProgress);

        [HarmonyPostfix, HarmonyPatch("MarkHostSleepDone")]
        static void MarkHostSleepDone_Postfix()
            => TNSLog.Debug("Host sleep done marked.");

        // If TimeManager exposes these, you can add postfixes as well:
        // [HarmonyPostfix, HarmonyPatch("OnSleepStart")] etc.
    }

    [HarmonyPatch(typeof(Employee))]
    public static class Debug_Employee_Pay
    {
        // Log when PaidForToday flips to true (when SetIsPaid is called).
        [HarmonyPrefix, HarmonyPatch("SetIsPaid")]
        static void SetIsPaid_Prefix(Employee __instance)
            => TNSLog.Debug($"Paying {__instance.FirstName} {__instance.LastName} DailyWage={__instance.DailyWage}");

        // Log the wage deduction source and amount.
        [HarmonyPrefix, HarmonyPatch("RemoveDailyWage")]
        static void RemoveDailyWage_Prefix(Employee __instance)
        {
            var home = __instance.GetHome();
            float before = (home != null) ? home.GetCashSum() : -1f;
            TNSLog.Debug($"Removing wage for {__instance.FirstName} {__instance.LastName}. HomeCashBefore={before}");
        }

        [HarmonyPostfix, HarmonyPatch("RemoveDailyWage")]
        static void RemoveDailyWage_Postfix(Employee __instance)
        {
            var home = __instance.GetHome();
            float after = (home != null) ? home.GetCashSum() : -1f;
            TNSLog.Debug($"Wage removed. HomeCashAfter={after}");
        }
    }
}