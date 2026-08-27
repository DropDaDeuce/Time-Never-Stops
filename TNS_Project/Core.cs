using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

#if IL2CPP
using TimeManager = Il2CppScheduleOne.GameTime.TimeManager;
using EDay = Il2CppScheduleOne.GameTime.EDay;
using Player = Il2CppScheduleOne.PlayerScripts.Player;
using PlayerScript = Il2CppScheduleOne.PlayerScripts;
using HUD = Il2CppScheduleOne.UI.HUD;
using UI = Il2CppScheduleOne.UI;
using GameManager = Il2CppScheduleOne.DevUtilities.GameManager;
using Il2CppScheduleOne.DevUtilities;
using PlayerCamera = Il2CppScheduleOne.PlayerScripts.PlayerCamera;
using SleepCanvas = Il2CppScheduleOne.UI.SleepCanvas;
using Employee = Il2CppScheduleOne.Employees.Employee;
using EMapRegion = Il2CppScheduleOne.Map.EMapRegion;
#else // MONO
using TimeManager = ScheduleOne.GameTime.TimeManager;
using EDay = ScheduleOne.GameTime.EDay;
using Player = ScheduleOne.PlayerScripts.Player;
using PlayerScript = ScheduleOne.PlayerScripts;
using HUD = ScheduleOne.UI.HUD;
using UI = ScheduleOne.UI;
using GameManager = ScheduleOne.DevUtilities.GameManager;
using ScheduleOne.DevUtilities;
using PlayerCamera = ScheduleOne.PlayerScripts.PlayerCamera;
using SleepCanvas = ScheduleOne.UI.SleepCanvas;
using Employee = ScheduleOne.Employees.Employee;
using EMapRegion = ScheduleOne.Map.EMapRegion;
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
        internal static bool IsHost
            => Player.Local != null && Player.Local.IsHost;

        private static MelonPreferences_Category cfgCategory;
        /// <summary>
        /// Whether the mod replaces the game's sleep sequence with its silent equivalent.
        ///
        /// READ THIS BEFORE CHANGING ANYTHING UNDER IT. "Silent" means the VISIBLE and BLOCKING
        /// half only. Every callback the sleep flow drives still runs, in vanilla's order, because
        /// the sleep flow is how the game does its day rollover: employees are unpaid again, NPCs
        /// revive and heal, storage and vending machines tick over, wanted levels clear, the
        /// player heals to full, cartel deals expire, quests advance — and the host writes the
        /// save. See Patch_SleepCanvas_SleepStart.SilentSleepRoutine for the full accounting.
        ///
        /// THE THIRD CLAUSE IS NOT OPTIONAL, AND IT IS THE ONE THAT LOOKS WRONG UNTIL YOU CHECK.
        /// The vanilla summary popup cannot exist without the sleep screen: DailySummary.Start()
        /// does State.InitializeDefaultParent(SleepCanvas.SleepingState), so Open() pushes onto a
        /// state machine that silent sleep never puts on the stack — the popup would come up
        /// unparented and the player could be left unable to dismiss it. So silent sleep engages
        /// ONLY when the popup was going to stay hidden anyway: the phone app has taken over, or
        /// the player set EnableDailySummaryAwake=false. Without S1API, or with a failed app, this
        /// falls back to the vanilla sleep screen WITH its popup — the same fail-safe direction as
        /// the rest of the app gate. The player is never left with no summary at all.
        /// </summary>
        internal static bool SilentSleepActive =>
            cfgMultiplierOnly?.Value != true
            && cfgSilentSleep?.Value == true
            && DailySummaryPatch.ShouldSuppressSummaryUI();

        public static MelonPreferences_Entry<string> cfgDaySpeedStr;
        public static MelonPreferences_Entry<bool> cfgEnableSummaryAwake;
        public static MelonPreferences_Entry<bool> cfgEnableSummaryApp;
        public static MelonPreferences_Entry<bool> cfgSilentSleep;
        public static MelonPreferences_Entry<bool> cfgLocalMultiplayer;
        public static MelonPreferences_Entry<float> cfgLegacyDaySpeed;
        public static MelonPreferences_Entry<bool> cfgMultiplierOnly; // NEW
        public static MelonPreferences_Entry<bool> cfgDebugLogging; // NEW
        public static MelonPreferences_Entry<int> cfgDailySummaryCutoffHHMM;

        private bool _updatingPref;
        private bool _lastEnableSummaryAwake;
        internal static bool SkipWakeFastForwardOnce;

        public const float DefaultSpeed = 1.0f;
        private const float MinSpeed = 0.1f;
        private const float MaxSpeed = 100.0f;

        // New: file watcher to avoid polling disk every second
        private FileSystemWatcher _cfgWatcher;
        private volatile bool _cfgFileChanged;

        // Time synchronization manager
        private TimeSyncManager? _timeSyncManager;
        private bool _timeSyncInitialized;
        private bool _timeSyncRetrying;
        // Handle for TimeSyncUpdateLoop. Kept so the loop can be stopped: without it every
        // save load started another never-ending coroutine that ran for the rest of the process.
        private object? _timeSyncUpdateHandle;

        public static float CurrentDaySpeed => Sanitize(ParseFloatOrDefault(cfgDaySpeedStr?.Value, DefaultSpeed));
        internal static int DailySummaryCutoffHHMM => SanitizeHHMM(cfgDailySummaryCutoffHHMM?.Value ?? 800);

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
                    "If enabled, the daily summary popup will be shown while awake (unless Multiplier Only Mode is ON).\n" +
                    "Ignored while the Daily Summary phone app is active — the app replaces the popup.",
                    false, false);

            cfgEnableSummaryApp = cfgCategory.GetEntry<bool>("EnableDailySummaryApp")
                ?? cfgCategory.CreateEntry(
                    "EnableDailySummaryApp",
                    true,
                    "Enable Daily Summary Phone App",
                    "If enabled, yesterday's summary is available on demand from a phone app instead of\n" +
                    "interrupting you with the popup. Requires S1API. If S1API is missing or the app fails\n" +
                    "to load, the popup is used instead so the summary is never lost.",
                    false, false);

            cfgSilentSleep = cfgCategory.GetEntry<bool>("EnableSilentSleep")
                ?? cfgCategory.CreateEntry(
                    "EnableSilentSleep",
                    true,
                    "Enable Silent Sleep",
                    "If enabled, the nightly sleep sequence runs with no screen takeover at all:\n" +
                    "no fade to black, no HUD drop, no cursor, no waiting.\n" +
                    "Everything the game does overnight still happens, including the daily save,\n" +
                    "so expect a brief hitch while it writes. Turn this off to get the vanilla\n" +
                    "sleep screen back (ignored while Multiplier Only Mode is ON).",
                    false, false);

            // MONO ONLY, AND THE ENTRY EXISTS ON BOTH ARMS ON PURPOSE. A key that is simply
            // absent on IL2CPP would be a silent no-op for anyone who copied a Mono config across —
            // exactly the failure this repo keeps hitting. Here it is visible, documented, and
            // warns once at startup if it is switched on where it cannot work.
            cfgLocalMultiplayer = cfgCategory.GetEntry<bool>("EnableLocalMultiplayer")
                ?? cfgCategory.CreateEntry(
                    "EnableLocalMultiplayer",
                    false,
                    "Enable Local Multiplayer (Mono only)",
                    "MONO BUILD ONLY. Lets a SECOND copy of the game on the SAME PC join your session\n" +
                    "over loopback, with no second Steam account. Host a normal single-player save,\n" +
                    "then press F6 in the other instance. Same machine only - this is not LAN.\n" +
                    "Time sync over Steam lobby data does not run in a local session; keep\n" +
                    "DaySpeedMultiplier identical in both instances. Off by default.",
                    false, false);

#if IL2CPP
            if (cfgLocalMultiplayer.Value)
            {
                TNSLog.Warning("EnableLocalMultiplayer is set, but it is a MONO-only feature and does nothing on the " +
                               "IL2CPP build. Multiplayer on IL2CPP is Steam-only; single-player is unaffected.");
            }
#endif

            cfgDebugLogging = cfgCategory.GetEntry<bool>("EnableDebugLogging")
                ?? cfgCategory.CreateEntry(
                    "EnableDebugLogging",
                    false,
                    "Enable Debug Logging",
                    "If true, prints additional debug messages from the mod to help diagnose issues.",
                    false, false);

            cfgDailySummaryCutoffHHMM = cfgCategory.GetEntry<int>("ForceDailySummaryWhileInUITime")
                ?? cfgCategory.CreateEntry(
                    "ForceDailySummaryWhileInUITime",
                    800,
                    "Force Daily Summary While In UI Time",
                    "The time the mod will force the daily summary to show if the user is still in a UI.",
                    false, false);

            cfgDailySummaryCutoffHHMM.OnEntryValueChanged.Subscribe((_, newVal) =>
            {
                if (_updatingPref) return;
                int sanitized = SanitizeHHMM(newVal);
                if (sanitized != newVal)
                {
                    _updatingPref = true;
                    cfgDailySummaryCutoffHHMM.Value = sanitized;
                    cfgCategory.SaveToFile(false);
                    _updatingPref = false;
                }
                TNSLog.Msg($"Daily summary cutoff set to {FormatHHMM(sanitized)} ({sanitized:D4}).");
            });

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

                // If host, sync multiplier to lobby immediately when changed
                if (_timeSyncManager != null)
                {
                    try
                    {
                        _timeSyncManager.SyncHostMultiplierNow();
                    }
                    catch (Exception ex)
                    {
                        TNSLog.Warning($"Error syncing multiplier change to lobby: {ex.Message}");
                    }
                }
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
                void markDirty(object _, FileSystemEventArgs __) => _cfgFileChanged = true;
                void markDirtyRename(object _, RenamedEventArgs __) => _cfgFileChanged = true;
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

            TNSLog.Debug($"Init complete. DaySpeed={cfgDaySpeedStr.Value}, MultiplierOnly={cfgMultiplierOnly.Value}, " +
                         $"SummaryAwake={cfgEnableSummaryAwake.Value}, DebugLogging={cfgDebugLogging.Value}, " +
                         $"DailySummaryCutoff={FormatHHMM(cfgDailySummaryCutoffHHMM.Value)} ({cfgDailySummaryCutoffHHMM.Value:D4}).");
        }

        /// <summary>
        /// One bool test per frame when local multiplayer is off, which it is by default. The
        /// config hot-reloads, so the check has to be live rather than latched at startup.
        /// </summary>
        public override void OnUpdate() => LocalMultiplayer.Tick();

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            TNSLog.Debug($"Scene initialized: '{sceneName}' (buildIndex={buildIndex}).");

            if (sceneName == "Menu")
            {
                // Reset time sync state between sessions so every new save load starts clean.
                _timeSyncRetrying = false;
                _timeSyncInitialized = false;
                StopTimeSyncUpdateLoop();
                if (_timeSyncManager != null)
                {
                    try { _timeSyncManager.Dispose(); } catch { }
                    _timeSyncManager = null;
                }

                // Yesterday's summary belongs to the save that was just left. Clearing it also
                // clears any capture error, so a bad night does not haunt the next save.
                DailySummaryStore.Reset();
                // S1API rebuilds the phone app on the next HomeScreen.Start; until it does, the
                // vanilla popup is the fallback again.
                S1APIBridge.Reset();

                // Drop any loopback listener state with the session that owned it.
                LocalMultiplayer.Reset();
            }
            else if (sceneName == "Main" && !_timeSyncInitialized && !_timeSyncRetrying)
            {
                _timeSyncRetrying = true;
                MelonCoroutines.Start(InitTimeSyncWithRetry());
                LocalMultiplayer.OnGameSceneLoaded();
            }
        }

        private IEnumerator InitTimeSyncWithRetry()
        {
            const int maxFastAttempts = 3;
            const float fastRetryDelay = 2f;
            const float pollInterval = 15f;

            // Fast initial burst — covers the normal case where Steamworks is up at load time.
            for (int attempt = 1; attempt <= maxFastAttempts; attempt++)
            {
                if (TryInitTimeSync())
                {
                    TNSLog.Msg("Time synchronization enabled.");
                    yield break;
                }

                if (attempt < maxFastAttempts)
                {
                    TNSLog.Debug($"Steamworks not ready — retrying in {fastRetryDelay}s...");
                    yield return new WaitForSecondsRealtime(fastRetryDelay);
                }
            }

            // Switch to a slow background poll.
            // Covers the common case: solo session that a friend joins later,
            // which is when the game actually spins up its Steam networking.
            TNSLog.Debug($"Fast attempts exhausted. Polling every {pollInterval}s " +
                         "in case a multiplayer session starts later...");
            var pollWait = new WaitForSecondsRealtime(pollInterval);
            int polls = 0;
            while (TimeManager.Instance != null && !_timeSyncInitialized)
            {
                yield return pollWait;
                polls++;
                TNSLog.Debug($"TimeSyncManager poll {polls}: attempting init...");
                if (TryInitTimeSync())
                {
                    TNSLog.Msg($"Time synchronization enabled (connected after {polls} " +
                               $"poll{(polls == 1 ? "" : "s")}).");
                    yield break;
                }
            }

            if (!_timeSyncInitialized)
                TNSLog.Debug("TimeSyncManager polling stopped (session ended or scene changed).");
            _timeSyncRetrying = false;
        }

        /// <summary>
        /// Single init attempt. Disposes any partial state first.
        /// Sets _timeSyncInitialized and _timeSyncRetrying on success.
        /// </summary>
        private void StopTimeSyncUpdateLoop()
        {
            if (_timeSyncUpdateHandle == null) return;
            try { MelonCoroutines.Stop(_timeSyncUpdateHandle); } catch { /* ignore */ }
            _timeSyncUpdateHandle = null;
        }

        private bool TryInitTimeSync()
        {
            // Every retry attempt lands here, so the old loop has to go before a new one starts.
            StopTimeSyncUpdateLoop();
            if (_timeSyncManager != null)
            {
                try { _timeSyncManager.Dispose(); } catch { }
                _timeSyncManager = null;
            }
            try
            {
                _timeSyncManager = new TimeSyncManager();
                _timeSyncManager.SetHostMultiplierCallback(() =>
                    Sanitize(ParseFloatOrDefault(cfgDaySpeedStr.Value, DefaultSpeed)));
                _timeSyncManager.Initialize();

                if (_timeSyncManager.IsInitialized)
                {
                    _timeSyncUpdateHandle = MelonCoroutines.Start(TimeSyncUpdateLoop());
                    _timeSyncInitialized = true;
                    _timeSyncRetrying = false;
                    return true;
                }
                TNSLog.Debug("TryInitTimeSync: Initialize() returned but IsInitialized is false.");
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"TryInitTimeSync failed: {ex.Message}");
            }
            return false;
        }

        public override void OnDeinitializeMelon()
        {
            try { _cfgWatcher?.Dispose(); } catch { /* ignore */ }
            _cfgWatcher = null;

            StopTimeSyncUpdateLoop();
            try { _timeSyncManager?.Dispose(); } catch { /* ignore */ }
            _timeSyncManager = null;
        }

        private IEnumerator SleepStateWatcher()
        {
            bool last = false;
            TimeManager lastTm = null;
            while (true)
            {
                while (TimeManager.Instance == null) { lastTm = null; last = false; yield return null; }
                var tm = TimeManager.Instance;
                if (tm != lastTm) { lastTm = tm; last = tm.IsSleepInProgress; }
                bool cur = tm.IsSleepInProgress;
                if (cur && !last) OnSleepStart();
                else if (!cur && last) OnSleepEnd();
                last = cur;
                yield return null;
            }
        }

        private void OnSleepStart()
        {
            var tm = TimeManager.Instance;
            TNSLog.Debug($"Sleep start detected (watcher). IsHost={IsHost}, t={tm?.CurrentTime:D4}, day={tm?.CurrentDay}, elapsed={tm?.ElapsedDays}.");
            Patch_Tick_XPMenu.SuppressDuringSleep(true);
            Patch_Tick_XPMenu.MarkHandledForToday(); // Mark BEFORE sleep sequence completes
        }

        private void OnSleepEnd()
        {
            Patch_SleepCanvas_SleepStart.StopRoutine();
            Patch_Tick_XPMenu.SuppressDuringSleep(false);
            Patch_Tick_XPMenu.TryRunSleepEndCleanup("SleepStateWatcher");
            SkipWakeFastForwardOnce = false;
            TNSLog.Debug("Sleep end (SleepStateWatcher) -> cleanup delegated.");
        }

        public static bool TryParseFloatInvariant(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        public static float ParseFloatOrDefault(string s, float fallback) =>
            (TryParseFloatInvariant(s, out var v) && float.IsFinite(v)) ? v : fallback;

        public static float Sanitize(float v) =>
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
                TNSLog.Debug($"TimeManager acquired. IsHost={IsHost}, speed={last:0.########}x.");

                // Only the host enforces the config value onto TimeManager
                if (IsHost)
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

                    // Host: keep TM in sync with config (and external tampering)
                    if (IsHost)
                    {
                        if (Differs(desired, last) || Differs(desired, tm.TimeSpeedMultiplier))
                        {
                            last = desired;
                            ApplyDaySpeed(last);
                        }
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

        private IEnumerator TimeSyncUpdateLoop()
        {
            // Use realtime so timeScale changes do not stall update processing
            var tick = new WaitForSecondsRealtime(0.1f); // Update every 100ms for responsive sync
            while (true)
            {
                try
                {
                    _timeSyncManager?.Update();
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"Error in time sync update loop: {ex.Message}");
                }

                yield return tick;
            }
        }

        public static void ApplyDaySpeed(float newSpeed)
        {
            var tm = TimeManager.Instance;
            if (tm == null) return;
            var safe = Sanitize(newSpeed);
            if (Mathf.Abs(tm.TimeSpeedMultiplier - safe) > 0.0001f)
            {
                tm.SetTimeSpeedMultiplier(safe);
                TNSLog.Msg($"Day speed set to {safe:0.########}x");
            }
        }

        internal static int SanitizeHHMM(int v)
        {
            if (v < 0) v = 0;
            int hh = v / 100;
            int mm = v % 100;
            if (hh < 0) hh = 0; else if (hh > 23) hh = 23;
            if (mm < 0) mm = 0; else if (mm > 59) mm = 59;
            return hh * 100 + mm;
        }

        internal static string FormatHHMM(int hhmm)
        {
            try { return TimeManager.Get12HourTime(hhmm); }
            catch
            { // Fallback HH:mm
                int hh = Mathf.Clamp(hhmm / 100, 0, 23);
                int mm = Mathf.Clamp(hhmm % 100, 0, 59);
                return $"{hh:00}:{mm:00}";
            }
        }
    }

    public static class PatchLogger
    {
        public static void LogPatchLoad(string patchName) =>
            TNSLog.Msg($"[Harmony] {patchName} loaded.");
    }

    [HarmonyPatch(typeof(TimeManager), "RpcLogic___PassMinute_Client_3316948804")]
    [HarmonyPriority(Priority.High)]
    public static class Patch_Tick_XPMenu
    {
        private static bool firedToday;
        private static int lastHHmm;
        private static int lastHH = -1;
        private static bool suppressForSleep;
        public static bool startupSkip = true;
        private static int lastElapsedDays = -1;
        private static bool wouldFreeze = false;
        private static bool _sleepEndCleanupDone = false;

        static Patch_Tick_XPMenu()
        {
            PatchLogger.LogPatchLoad(nameof(Patch_Tick_XPMenu));
            startupSkip = true;
        }

        private static bool IsDailySummaryOpen()
        {
            try
            {
                var ds = NetworkSingleton<UI.DailySummary>.Instance;
                return ds != null && ds.IsOpen;
            }
            catch { return false; }
        }

        private static void LogState(string tag, TimeManager tm)
        {
            TNSLog.Debug($"{tag}: t={tm.CurrentTime:D4}, day={tm.CurrentDay}, elapsed={tm.ElapsedDays}, " +
                         $"firedToday={firedToday}, startupSkip={startupSkip}, suppress={suppressForSleep}, " +
                         $"sleep={tm.IsSleepInProgress}, dsOpen={IsDailySummaryOpen()}, lastHHmm={lastHHmm}, " +
                         $"lastElapsedDays={lastElapsedDays}, wouldFreeze={wouldFreeze}");
        }

        public static void SuppressDuringSleep(bool on)
        {
            suppressForSleep = on;
            if (on) _sleepEndCleanupDone = false; // new sleep cycle — reset so cleanup can fire again
        }

        public static void MarkHandledForToday()
        {
            firedToday = true;
            lastHHmm = 700;
            TNSLog.Debug("MarkHandledForToday: firedToday=true, lastHHmm=0700");
        }

        /// <summary>
        /// Guards against the sleep-end cleanup firing multiple times per cycle (watcher +
        /// PassMinute_Client RPC + SetHostSleepDone RPC can all fire in quick succession).
        /// Returns true if cleanup ran, false if it was already done this cycle.
        /// </summary>
        public static bool TryRunSleepEndCleanup(string source)
        {
            if (_sleepEndCleanupDone)
            {
                TNSLog.Debug($"Sleep end cleanup already done this cycle — skipping ({source}).");
                return false;
            }
            _sleepEndCleanupDone = true;

            // Under silent sleep this whole path is not just unnecessary, it is HARMFUL.
            // EnsureHUDReset re-locks the mouse, re-enables the inventory, forces CanMove and
            // unloads the "Back" input module — all correct after vanilla's sleep screen has held
            // the player captive, and all of it a random yank on a player who has been walking
            // around uninterrupted the entire time. Silent sleep never touched the HUD, the
            // camera, the cursor or the input stack, so there is nothing to put back.
            if (Patch_SleepCanvas_SleepStart.SilentSleepEngaged)
            {
                TNSLog.Debug($"Sleep end cleanup skipped ({source}): silent sleep left the HUD alone.");
                MarkHandledForToday();
                return false;
            }

            TNSLog.Debug($"Sleep end cleanup running ({source}).");
            MarkHandledForToday();
            MelonCoroutines.Start(EnsureHUDResetWhenSettled(source));
            return true;
        }

        /// <summary>
        /// Waits for the game's own sleep coroutine to finish before forcing the HUD back.
        ///
        /// Every path into cleanup fires on SetHostSleepDone, and vanilla still has roughly 1.6s of
        /// scripted tail to run after that: TimeLabel for 1s, onSleepEndFade, StopTransformOverride,
        /// 0.1s, then SetReadyToSleep / StopTransformOverride(reenableCameraLook: true) /
        /// RemoveScreen / RemoveActiveUIElement("Sleeping") / LerpBlackOverlay back in.
        /// Running EnsureHUDReset at t=0 of that meant fighting it — and because "Sleeping" was still
        /// registered, GetLocalActiveUICount() returned 1 and the reset took the wrong branch
        /// (SetCanLook(false) + FreeMouse()), which vanilla then undid a moment later. That snap is
        /// what the sleep-end jank actually was.
        ///
        /// So: let the game finish, and only step in if it does not. The timeout is what keeps the
        /// original anti-hang behaviour — those guards are load-bearing, not defensive decoration.
        /// </summary>
        /// <summary>
        /// The UI element SleepCanvas.SleepStart registers for the duration of its coroutine.
        ///
        /// It is removed THIRD-FROM-LAST, not last: SleepingState.PopFromDefaultParent() and a
        /// 0.5s LerpBlackOverlay(0f, 0.5f) still follow it, so the screen stays black for about
        /// another half second after this signal. That is fine for our purposes — EnsureHUDReset
        /// touches the camera, mouse and inventory, none of which the fade owns — but the signal
        /// means "the sleep coroutine reached its cleanup", not "the screen is clear".
        /// </summary>
        private const string SleepingUIElement = "Sleeping";

        /// <summary>
        /// Vanilla's tail after SetHostSleepDone is NOT a constant, which is what the old flat 6s
        /// budget got wrong.
        ///
        ///   baseline                1f (TimeLabel) + 0.1f                        = 1.1s
        ///   with a queued message   + 0.5 + 0.5 fade + displayTime + 0.5 fade + 0.5
        ///
        /// QueueSleepMessage's displayTime defaults to 3f, so the queued-message path needs ~6.1s
        /// — just past the old 6s — and a longer message pushes it further. The result was
        /// "Sleep sequence did NOT finish within 6s" on a perfectly healthy night, which is the
        /// exact false alarm this routine exists to avoid.
        ///
        /// So: keep the short budget for the common case, and extend it when the game actually has
        /// a message queued. SleepCanvas.QueuedSleepMessage is a public getter on both arms.
        /// </summary>
        private const float SleepSettleBudget = 6f;
        private const float SleepSettleBudgetWithMessage = 20f;

        private static float GetSleepSettleBudget()
        {
            try
            {
                var sc = Singleton<SleepCanvas>.Instance;
                if (sc != null && !string.IsNullOrEmpty(sc.QueuedSleepMessage))
                {
                    TNSLog.Debug($"Sleep message queued; extending settle budget to {SleepSettleBudgetWithMessage:0}s.");
                    return SleepSettleBudgetWithMessage;
                }
            }
            catch (Exception ex)
            {
                // Reading it failed — take the LONGER budget. A late safety pass costs nothing;
                // a spurious "did NOT finish" line in a log players attach to bug reports does.
                TNSLog.Debug($"Could not read QueuedSleepMessage ({ex.Message}); using the extended settle budget.");
                return SleepSettleBudgetWithMessage;
            }
            return SleepSettleBudget;
        }

        private static IEnumerator EnsureHUDResetWhenSettled(string source)
        {
            float maxWait = GetSleepSettleBudget();
            float start = Time.realtimeSinceStartup;

            // Wait for the GAME'S sleep sequence to finish, not for the screen to be empty.
            // Those are different things: if the player had a menu open at 06:59 the sleep is
            // deferred, proceeds on the ForceDailySummaryWhileInUITime cutoff, and that menu is
            // STILL OPEN when sleep ends — so a plain "no active UI" test can never come true and
            // every such day burns the full timeout and logs like a failure when nothing is wrong.
            while (SleepUiStillRunning() && Time.realtimeSinceStartup - start < maxWait)
                yield return null;

            float waited = Time.realtimeSinceStartup - start;
            TNSLog.Debug(SleepUiStillRunning()
                ? $"Sleep sequence did NOT finish within {maxWait:0}s ({source}); forcing EnsureHUDReset (activeUI={GetLocalActiveUICountSafe()})."
                : $"Sleep sequence finished after {waited:0.00}s ({source}); running EnsureHUDReset as a safety pass (activeUI={GetLocalActiveUICountSafe()}).");

            EnsureHUDReset();
        }

        /// <summary>
        /// True while the game still holds its "Sleeping" UI element.
        ///
        /// Falls back to the old "any UI at all" test if the list cannot be read — IL2CPP exposes
        /// ActiveUIElements through an Il2CppSystem list, and a generic-collection access failing
        /// on one arm only is exactly the parity trap this codebase keeps hitting. Degrading to
        /// b12's behaviour is safe; throwing out of a coroutine is not.
        /// </summary>
        private static bool SleepUiStillRunning()
        {
            try
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) return false;

                var elements = cam.ActiveUIElements;
                if (elements == null) return false;

                for (int i = 0; i < elements.Count; i++)
                {
                    if (string.Equals(elements[i], SleepingUIElement, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"SleepUiStillRunning: could not read ActiveUIElements ({ex.Message}); " +
                             "falling back to active-UI count.");
                return GetLocalActiveUICountSafe() > 0;
            }
        }

        private static int GetLocalActiveUICountSafe()
        {
            try { return Patch_SleepCanvas_SleepStart.GetLocalActiveUICount(); }
            catch { return 0; }
        }

        [HarmonyPrefix]
        public static bool Prefix(TimeManager __instance, int oldTime)
        {
            if (__instance == null || __instance.enabled == false) return true;

            // Only the host enforces the local config value
            if (Core.IsHost)
            {
                var desired = Core.CurrentDaySpeed;
                if (Mathf.Abs(__instance.TimeSpeedMultiplier - desired) > 0.0001f)
                    Core.ApplyDaySpeed(desired);
            }

            if (Core.cfgMultiplierOnly?.Value == true)
                return true; // vanilla behavior when multiplier-only mode

            wouldFreeze = (__instance.CurrentTime == 400) ||
                          (__instance.IsCurrentTimeWithinRange(400, 600) && !GameManager.IS_TUTORIAL);

            if (!wouldFreeze) return true;

            if ((UnityEngine.Object)Player.Local == null)
            {
                TNSLog.Warning("Local player does not exist. Waiting for player spawn.");
                return false;
            }

            // Mirror what RpcLogic___PassMinute_Client does in its happy path:
            // set CurrentTime from the RPC param first, then advance.
            TMAccess.SetCurrentTime(__instance, oldTime);
            float staggerTime = TimeManager.MinuteDuration / (Time.timeScale * 0.9f);
            TMAccess.SetTimeOnCurrentMinute(__instance, 0f);

            if (__instance.CurrentTime == 2359)
            {
                TMAccess.SetElapsedDays(__instance, __instance.ElapsedDays + 1);
                TMAccess.SetCurrentTime(__instance, 0);
                TMAccess.SetDailyMinSum(__instance, 0);
                // Order matters and matches the game: RpcLogic___PassMinute_Client fires
                // onHourPass BEFORE onDayPass at the rollover. Do not swap these.
                __instance.onHourPass?.Invoke();
                __instance.onDayPass?.Invoke();
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

            TMAccess.SetDailyMinSum(__instance, TimeManager.GetMinSumFrom24HourTime(__instance.CurrentTime));
            __instance.HasChanged = true;
            __instance.onMinutePass?.InvokeAllStaggered(staggerTime);
            __instance.onUncappedMinutePass?.InvokeAllStaggered(staggerTime);
            __instance.onTimeChanged?.Invoke();

            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(TimeManager __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true)
                return;

            lastElapsedDays = __instance.ElapsedDays;

            if (lastHH != (int)(__instance.CurrentTime / 100))
                LogState("PassMinute/Postfix/start", __instance);
            lastHH = (int)(__instance.CurrentTime / 100);

            // If guard stuck, clear it
            if (suppressForSleep && !__instance.IsSleepInProgress)
            {
                TNSLog.Warning("Guard clear: suppressForSleep was true while not sleeping.");
                suppressForSleep = false;
            }

            // If HostDailySummaryDone but SleepInProgress still true (engine didn't clear), force it off.
            try
            {
                if (__instance.IsSleepInProgress && __instance.HostSleepDone && !IsDailySummaryOpen())
                {
                    TMAccess.SetSleepInProgress(__instance, false);
                    TNSLog.Warning("SleepInProgress stuck after host done; forced SleepInProgress=false.");
                }
            }
            catch { /* ignore */ }

            if (__instance.CurrentTime == Core.DailySummaryCutoffHHMM + 30)
            {
                MelonCoroutines.Start(ClosePostSleepCanvases());
            }

            if (__instance.IsSleepInProgress || suppressForSleep)
                return;

            var ds = NetworkSingleton<UI.DailySummary>.Instance;
            if (ds != null && ds.IsOpen)
            {
                TNSLog.Debug("Skip: DailySummary is open.");
                return;
            }

            if (startupSkip)
            {
                startupSkip = false;
                lastHHmm = __instance.CurrentTime;
                firedToday = (__instance.CurrentTime > 658);
                TNSLog.Debug($"Startup init: lastHHmm={lastHHmm}, firedToday={firedToday}");
            }

            int hhmm = __instance.CurrentTime;

            if (lastHHmm > hhmm)
            {
                firedToday = false;
                TNSLog.Debug("Rearm: midnight wrap detected (lastHHmm > hhmm).");
            }

            if (!firedToday && __instance.CurrentTime > 658 && __instance.ElapsedDays > 0)
            {
                // HOST ONLY, and this guard is load-bearing. StartSleep is
                // [ObserversRpc(RunLocally = true)]: when the host calls it every client runs
                // RpcLogic___StartSleep anyway, so clients still get the awake daily summary.
                // When a CLIENT calls it the RPC writer fails ("server is not active") and only
                // the local logic runs — which sets IsSleepInProgress = true and then waits on
                // HostSleepDone, a flag only the host ever sets (SleepCanvas.SleepStart, under
                // if (InstanceFinder.IsServer)). With no host sleeping, that wait never ends and
                // the client is stuck on a black "Waiting for host" overlay. The stuck-guard in
                // this same postfix cannot rescue it either, because it requires HostSleepDone.
                if (Core.IsHost)
                {
                    TNSLog.Debug($"Triggering StartSleep: t={__instance.CurrentTime:D4}, elapsed={__instance.ElapsedDays}, " +
                                 $"dsOpen={IsDailySummaryOpen()}, suppress={suppressForSleep}");
                    __instance.StartSleep();
                }
                else
                {
                    // Mark it handled locally so the client does not re-evaluate this every
                    // minute for the rest of the day while it waits for the host's RPC.
                    TNSLog.Debug($"Skipping StartSleep: not host (t={__instance.CurrentTime:D4}). " +
                                 "Waiting for the host's ObserversRpc to drive the sleep flow.");
                    firedToday = true;
                }
            }

            lastHHmm = hhmm;
        }

        private static IEnumerator ClosePostSleepCanvases()
        {
            var ds = NetworkSingleton<UI.DailySummary>.Instance;
            if (ds != null && ds.IsOpen)
            {
                ds.Close();
                TNSLog.Debug($"{Core.DailySummaryCutoffHHMM + 30}: Closed DailySummary.");
            }

            yield return new WaitForSecondsRealtime(2f);

            try
            {
                var ranks = Resources.FindObjectsOfTypeAll<UI.RankUpCanvas>();
                if (ranks != null)
                {
                    foreach (var r in ranks)
                    {
                        if (r == null) continue;
                        if (r.IsRunning || (r.Canvas != null && r.Canvas.enabled))
                        {
                            r.EndEvent();
                            if (r.Canvas != null) r.Canvas.enabled = false;
                            TNSLog.Debug($"{Core.DailySummaryCutoffHHMM + 30}: Closed RankUpCanvas.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"CloseAllRankUpCanvases failed: {ex.Message}");
            }

            yield return new WaitForSecondsRealtime(2f);

            var region = Singleton<UI.RegionUnlockedCanvas>.Instance;
            if (region != null && region.IsRunning)
            {
                region.EndEvent();
                TNSLog.Debug($"{Core.DailySummaryCutoffHHMM + 30}: Closed RegionUnlockedCanvas.");
            }
        }

        public static void EnsureHUDReset()
        {
            // This is now called from a deferred coroutine (EnsureHUDResetWhenSettled), so the
            // session can have ended while it was waiting — quit to menu, save reload. None of
            // these singletons are guaranteed any more, and an NRE here would surface as a
            // Harmony-postfix error with no obvious cause. Guard every one.
            if ((UnityEngine.Object)Player.Local == null)
            {
                TNSLog.Debug("EnsureHUDReset: no local player (session ended while waiting) — skipping.");
                return;
            }

            // Player.CurrentBed was removed from the game in 0.4.6f13 — the game's own
            // SleepCanvas.SleepStart dropped the same assignment. There is no replacement.
            Player.Local.SetReadyToSleep(ready: false);

            int activeUiCount = GetLocalActiveUICountSafe();
            bool uiCleared = activeUiCount == 0;
            TNSLog.Debug($"EnsureHUDReset: uiCleared={uiCleared} (activeUI={activeUiCount}).");

            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.StopTransformOverride(0f, reenableCameraLook: true, returnToOriginalRotation: false);
                if (uiCleared)
                {
                    cam.SetCanLook(canLook: true);
                    cam.LockMouse();
                }
                else
                {
                    // Something is still holding a UI element. Leave the cursor usable rather than
                    // locking the player into a screen they cannot interact with.
                    cam.SetCanLook(canLook: false);
                    cam.FreeMouse();
                }
            }

            var inv = PlayerSingleton<PlayerScript.PlayerInventory>.Instance;
            if (inv != null) inv.SetInventoryEnabled(enabled: true);

            // 0.4.6f13 replaced UI.InputPromptsCanvas with UI.Input.InputPromptsManager, which
            // has no parameterless UnloadModule — "Back" is the id the game's own SleepCanvas
            // loads and unloads around the sleep flow.
            var prompts = Singleton<UI.Input.InputPromptsManager>.Instance;
            if (prompts != null) prompts.UnloadModule("Back");

            var move = PlayerSingleton<PlayerScript.PlayerMovement>.Instance;
            if (move != null) move.CanMove = true;

            var sleepCanvas = SleepCanvas.Instance;
            if (sleepCanvas != null && sleepCanvas.MenuContainer != null)
                sleepCanvas.MenuContainer.gameObject.SetActive(false);
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
            // Gated on the SAME test as the daily-summary popup, which it did not used to be.
            // The point of the phone app is that nothing seizes the screen at 07:00, and the
            // rank-up canvas is a full-screen post-sleep event that does exactly that — Mathew hit
            // it on the b23 run with the app installed and working. The rank-up now shows up as a
            // line in the app instead; see DailySummaryStore.CaptureRankUp.
            //
            // Suppressing this is also SAFER than letting it run: SleepCanvas blocks on
            // WaitUntil(() => !pse.IsRunning) and ONLY EndEvent() ever clears IsRunning, and
            // EndEvent has no code callers at all (it is wired to a prefab animation event). Never
            // starting the event means IsRunning stays false and the wait passes immediately.
            if (DailySummaryPatch.ShouldSuppressSummaryUI())
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
            // Same reasoning as the rank-up canvas above. The unlocked region is reported in the
            // app instead — captured by Patch_RegionUnlockedCanvas_QueueUnlocked below, which runs
            // whether or not this canvas is allowed to draw.
            if (DailySummaryPatch.ShouldSuppressSummaryUI())
            {
                TNSLog.Debug("Region Unlocked Canvas Skipping");
                __instance.EndEvent();
                return false; // Skip original method
            }

            TNSLog.Debug("Region Unlocked Canvas Running");
            return true; // Run original method
        }
    }

    /// <summary>
    /// Records which region was unlocked so the app can report it.
    ///
    /// Hooked at QUEUE time rather than at StartEvent, because StartEvent is the thing being
    /// suppressed — and because the region is held in a private field that QueueUnlocked is the
    /// only writer of. Runs during sleep, so the store holds it pending until the ClearStats
    /// capture folds it into the night's summary.
    /// </summary>
    [HarmonyPatch(typeof(UI.RegionUnlockedCanvas), "QueueUnlocked")]
    public static class Patch_RegionUnlockedCanvas_QueueUnlocked
    {
        static Patch_RegionUnlockedCanvas_QueueUnlocked() => PatchLogger.LogPatchLoad(nameof(Patch_RegionUnlockedCanvas_QueueUnlocked));

        [HarmonyPostfix]
        public static void Postfix(EMapRegion _region)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return;
            DailySummaryStore.NoteRegionUnlocked(_region);
        }
    }

    [HarmonyPatch(typeof(UI.DailySummary))]
    public static class DailySummaryPatch
    {
        // Logged like the other patch classes so a log can prove the ClearStats hook attached.
        // Worth having: when the app showed nothing after a night, "did the capture patch even
        // load?" was the first question and nothing in the log answered it.
        static DailySummaryPatch() => PatchLogger.LogPatchLoad(nameof(DailySummaryPatch));

        /// <summary>
        /// Whether the vanilla summary UI — the popup AND the post-sleep canvases behind it —
        /// should stay hidden this night.
        ///
        /// Two independent reasons, and they are NOT the same thing:
        ///   - the player turned the popup off (EnableDailySummaryAwake=false), the pre-existing
        ///     behaviour; or
        ///   - the phone app is up and has taken over as the place to read it.
        /// Multiplier-only mode outranks both and always yields vanilla behaviour.
        ///
        /// ONE TEST FOR ALL THREE CANVASES ON PURPOSE. b23 gated only DailySummary on the app and
        /// left RankUpCanvas and RegionUnlockedCanvas on the old config-only test, so with the app
        /// working the player still got a full-screen interruption at 07:00 — which defeats the
        /// entire point of the feature. If a fourth post-sleep canvas ever shows up, it gates here.
        /// </summary>
        internal static bool ShouldSuppressSummaryUI()
        {
            if (Core.cfgMultiplierOnly?.Value == true) return false;
            if (Core.cfgEnableSummaryAwake?.Value == false) return true;
            return S1APIBridge.ShouldGateSummaryBehindApp();
        }

        /// <summary>
        /// Snapshot the day's numbers before the game destroys them.
        ///
        /// ClearStats is the right hook and the only one: it is called from exactly one place —
        /// SleepEnd(), wired in DailySummary.Start() to TimeManager.onSleepEnd — so it fires once
        /// per night no matter which of the mod's three sleep-end paths got there first. Hooking
        /// the sleep events instead would have made this a fourth racer against
        /// TryRunSleepEndCleanup.
        ///
        /// PREFIX, not postfix: a postfix would run after the values were wiped.
        /// This never returns false — the game must always get its ClearStats.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("ClearStats")]
        public static void ClearStatsPrefix(UI.DailySummary __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return;
            TNSLog.Debug("DailySummary.ClearStats reached — capturing the night's summary.");
            DailySummaryStore.Capture(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(UI.DailySummary.Open))]
        public static bool OpenPrefix(UI.DailySummary __instance)
        {
            if (ShouldSuppressSummaryUI())
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
            if (ShouldSuppressSummaryUI())
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
        // Reflection handle to private SleepStart
        private static readonly MethodInfo _miSleepStart =
            AccessTools.Method(typeof(SleepCanvas), "SleepStart", Type.EmptyTypes);

        // Allow original exactly once after deferral (avoid recursion)
        private static readonly HashSet<SleepCanvas> _allowOnce = new HashSet<SleepCanvas>();
        // Track deferral coroutines to avoid duplicates
        private static readonly Dictionary<SleepCanvas, object> _gateRoutines = new Dictionary<SleepCanvas, object>();

        // Short helper coroutine handle to stabilize look/mouse during sleep start
        private static object _lookRoutine;

        private static int AbsoluteCutoffHHMM => Core.DailySummaryCutoffHHMM;

        [HarmonyPrefix]
        public static bool Prefix(SleepCanvas __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return true;

            // Latched for the night. TryRunSleepEndCleanup must answer the same question this
            // prefix did, and Core.SilentSleepActive can legitimately change underneath it — the
            // app can report a failure mid-night, and the config is hot-reloaded within a second.
            SilentSleepEngaged = Core.SilentSleepActive;

            if (SilentSleepEngaged)
            {
                // Replace the whole sequence rather than neutralise it piece by piece. Vanilla's
                // visual half lives inside one long coroutine — black overlay, SleepingState push,
                // AddActiveUIElement("Sleeping"), the timed labels — and a coroutine cannot be
                // patched partway through. The deferral gate below is not needed either: it exists
                // to stop the sleep screen stealing an open menu, and there is no longer a screen.
                if (_silentRoutine != null)
                {
                    TNSLog.Debug("SleepStart: a silent sleep is already running; ignoring the repeat.");
                    return false;
                }
                _silentRoutine = MelonCoroutines.Start(SilentSleepRoutine(__instance));
                return false;
            }

            if (_allowOnce.Contains(__instance))
            {
                _allowOnce.Remove(__instance);
                TNSLog.Debug("SleepStart: allowOnce pass-through.");
                StartLookRoutine(__instance);
                return true;
            }

            int activeUi = GetLocalActiveUICount();
            if (activeUi > 0)
            {
                if (!_gateRoutines.ContainsKey(__instance))
                {
                    TNSLog.Debug($"SleepStart deferred: activeUI={activeUi}; starting gate routine.");
                    var handle = MelonCoroutines.Start(WaitUntilMenusClosedThenRun(__instance));
                    _gateRoutines[__instance] = handle;
                }
                return false;
            }

            TNSLog.Debug("SleepStart: proceeding (no active UI).");
            StartLookRoutine(__instance);
            return true;
        }

        private static void StartLookRoutine(SleepCanvas __instance)
        {
            if (_lookRoutine != null)
                MelonCoroutines.Stop(_lookRoutine);
            _lookRoutine = MelonCoroutines.Start(MakeThingsWorkRoutine(__instance));
        }

        public static void StopRoutine()
        {
            if (Core.cfgMultiplierOnly?.Value == true) return;
            if (_lookRoutine != null)
            {
                MelonCoroutines.Stop(_lookRoutine);
                _lookRoutine = null;
            }
        }

        private static IEnumerator MakeThingsWorkRoutine(SleepCanvas __instance)
        {
            const int frames = 3;
            for (int i = 0; i < frames; i++)
            {
                try
                {
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam != null)
                    {
                        // CanLook and the mouse are independent knobs, and they must NOT both be on.
                        // PlayerCamera.Update gates mouse-look on `CanLook && !transformOverriden` and
                        // checks no UI state at all. The awake summary has no bed, so the transform is
                        // not overridden — setting CanLook(true) alongside FreeMouse() meant one mouse
                        // movement both drove the cursor and turned the player's head.
                        bool summaryWillShow = Core.cfgEnableSummaryAwake?.Value != false;
                        if (summaryWillShow)
                        {
                            // Summary is about to take the screen: cursor yes, head-turn no.
                            cam.SetCanLook(canLook: false);
                            cam.FreeMouse();
                        }
                        else
                        {
                            // Summary suppressed — hand control straight back and clear the fade,
                            // so the player keeps playing as if nothing happened.
                            cam.SetCanLook(canLook: true);
                            cam.LockMouse();
                            SCAccess.LerpBlackOverlay(__instance, 0f, 0.1f);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"SleepStart look restore failed: {ex.Message}");
                }
                yield return null;
            }
            _lookRoutine = null;
        }

        private static IEnumerator WaitUntilMenusClosedThenRun(SleepCanvas sc)
        {
            const float pollInterval = 0.25f;
            const float maxWait = 30f;
            float startRT = Time.realtimeSinceStartup;

            while (true)
            {
                if (sc == null) break; // Canvas destroyed

                var tm = TimeManager.Instance;

                bool cutoffReached = tm != null && tm.CurrentTime >= AbsoluteCutoffHHMM;
                bool timeoutReached = Time.realtimeSinceStartup - startRT >= maxWait;
                bool uiCleared = GetLocalActiveUICount() == 0;

                if (uiCleared || cutoffReached || timeoutReached)
                {
                    if (uiCleared) TNSLog.Debug("SleepStart defer: UI cleared; proceeding.");
                    if (cutoffReached) TNSLog.Debug($"SleepStart defer: cutoff {Core.FormatHHMM(AbsoluteCutoffHHMM)} reached; proceeding.");
                    if (timeoutReached) TNSLog.Debug("SleepStart defer: timeout reached; proceeding.");

                    // If already past 07:00 when finally entering SleepStart,
                    // skip the game's SkipForwardToTime() to avoid rewinding time.
                    if (tm != null && tm.CurrentTime > 700)
                    {
                        Core.SkipWakeFastForwardOnce = true;
                        TNSLog.Debug("Guard set: will skip SkipForwardToTime (already past wake).");
                    }

                    _allowOnce.Add(sc);
                    try
                    {
                        TNSLog.Debug("Deferred SleepStart: invoking private SleepStart via reflection...");
                        _miSleepStart?.Invoke(sc, null);

                        var tm2 = TimeManager.Instance;
                        string ct = (tm2 != null) ? tm2.CurrentTime.ToString("D4") : "null";
                        bool sip = tm2 != null && tm2.IsSleepInProgress;
                        TNSLog.Debug($"Deferred SleepStart invoked. SleepInProgress={sip}, t={ct}");
                    }
                    catch (Exception ex)
                    {
                        TNSLog.Error($"Deferred SleepStart invoke failed: {ex.Message}");
                    }
                    break;
                }

                yield return new WaitForSecondsRealtime(pollInterval);
            }
            _gateRoutines.Remove(sc);
        }

        // Handle for the silent-sleep coroutine. Non-null means one is in flight; a second
        // SleepStart in the same night is ignored rather than racing the first.
        private static object _silentRoutine;

        /// <summary>
        /// Whether THIS night went down the silent path. Set by the prefix, read by
        /// Patch_Tick_XPMenu.TryRunSleepEndCleanup, which runs later and must not re-decide.
        /// </summary>
        internal static bool SilentSleepEngaged { get; private set; }

        /// <summary>
        /// How long to wait for TimeManager's own WaitForSleepEnd to finish. Generous on purpose:
        /// the last thing that coroutine does is SaveManager.Save(), whose first loop walks every
        /// saveable with no yield, and on a large save that stall is seconds. Overrunning this is a
        /// log line, never a corrective action.
        /// </summary>
        private const float SilentSleepEndBudgetHost = 30f;

        /// <summary>
        /// The client budget, and it is four times the host's for a reason that only shows up in a
        /// MIXED LOBBY. A client waits on the host's SetHostSleepDone broadcast, so its wait is
        /// really "how long does the HOST take" — and if the host is not running silent sleep (no
        /// S1API, or EnableSilentSleep=false) the host is sitting in the full vanilla sleep screen,
        /// which has its own 30s realtime deferral gate on top of the sequence itself. At 30s the
        /// client would log a scary warning about a night that is merely slow, and then invoke
        /// onSleepEndFade OUT OF ORDER — teleporting a Deep End player who has not "woken" yet.
        ///
        /// Still bounded, deliberately: an unbounded WaitUntil is the exact deadlock class the
        /// vanilla sleep flow is riddled with (see the sleep note in CLAUDE.md).
        /// </summary>
        private const float SilentSleepEndBudgetClient = 120f;

        /// <summary>
        /// The silent replacement for SleepCanvas's Sleep() coroutine.
        ///
        /// WHAT IS DROPPED — all of it presentation or blocking, none of it state:
        ///   the black overlay and its two 0.5s fades; SleepingState.PushToDefaultParent() (the
        ///   HUD drop and the cursor); AddActiveUIElement("Sleeping"); the "Back" input-prompt
        ///   unload; TimeLabel/WakeLabel and their 1s hold; StopTransformOverride; the queued
        ///   post-sleep canvases; DailySummary.Open(); every WaitForSecondsRealtime.
        ///
        /// WHAT IS KEPT, AND WHY EACH ONE HAD TO BE — every item here was traced through the
        /// 0.4.6f13 decompile, because "bypass sleep" quietly means "bypass the day rollover":
        ///
        ///   * onSleepStart (fired by TimeManager before we are even called, so untouched) —
        ///     wanted level and crimes cleared, NPCs revived and healed, bodies sent to the
        ///     medical centre, storage entities emptied, vending machines and ATMs ticked,
        ///     customer addiction decayed, trash regenerated, sewer mushrooms respawned,
        ///     tutorial keyframe advanced, Quest_TheDeepEnd's kidnap message queued.
        ///   * onSleepFullyFaded — NOT cosmetic despite the name. PlayerHealth subscribes to it
        ///     with SetHealth(100f). Skip it and sleeping stops healing you.
        ///   * SetHostSleepDone(true) — the ONLY thing in the entire game that releases
        ///     TimeManager's WaitForSleepEnd, and therefore the only route to onSleepEnd AND to
        ///     SaveManager.Save(). onSleepEnd is where EMPLOYEES ARE MARKED UNPAID FOR THE NEW DAY
        ///     (Employee.OnSleepEnd -> PaidForToday = false), where DailySummary.ClearStats runs
        ///     (our capture hook), where cartel deals go overdue or expire, where the mix
        ///     operation is re-driven, and where three quests check their start conditions.
        ///     Bypassing SleepCanvas without calling this would have silently frozen all of it.
        ///   * onSleepEndFade — story logic, not a fade. Quest_SinkOrSwim.CheckArrival spawns the
        ///     loan-shark vehicle and completes its entry; Quest_TheDeepEnd.SleepFadeOut teleports
        ///     the player to the Thomas meeting. Skip it and the main story dead-ends.
        ///   * Both UnityEvents are also prefab-wired, so there may be listeners no decompile can
        ///     show. Invoking them rather than reimplementing their known subscribers is the only
        ///     way to keep those too.
        ///
        /// The post-sleep canvases are the one thing dropped on purpose after checking: both
        /// RankUpCanvas.StartEvent and RegionUnlockedCanvas.StartEvent were read in full and are
        /// pure presentation — sliders, labels, sounds, animations. Their content is captured into
        /// the phone app instead (DailySummaryStore.CaptureRankUp / NoteRegionUnlocked), and they
        /// are kept out of the queue entirely by Patch_SleepCanvas_AddPostSleepEvent.
        /// </summary>
        private static IEnumerator SilentSleepRoutine(SleepCanvas sc)
        {
            // Resume on the NEXT frame. MelonCoroutines.Start runs the body up to the first yield
            // synchronously, and we are standing inside SleepCanvas.SleepStart — which is the
            // FIRST onSleepStart subscriber, because SleepCanvas subscribes in Awake and everything
            // else subscribes in Start. Nothing else that hangs off onSleepStart has run yet:
            // not Player.SleepStart, and not Quest_TheDeepEnd.BeforeSleep, which is what queues
            // the kidnap message we are about to read.
            yield return null;

            TNSLog.Debug("Silent sleep: vanilla sleep sequence bypassed; running the callbacks by hand.");

            var tm = TimeManager.Instance;
            if (tm == null)
            {
                TNSLog.Warning("Silent sleep: no TimeManager — aborting. The night will not complete.");
                _silentRoutine = null;
                yield break;
            }

            // Vanilla's SleepStart body minus its visual half. SetReadyToSleep(false) matters in
            // multiplayer (it is what stops the host re-entering sleep immediately), and the menu
            // removal closes the sleep screen if the player happened to have it open.
            try
            {
                if ((UnityEngine.Object)Player.Local != null)
                    Player.Local.SetReadyToSleep(ready: false);

                if (sc != null && sc.MenuState != null)
                    sc.MenuState.RemoveFromDefaultParent();
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Silent sleep: menu teardown failed (continuing): {ex.Message}");
            }

            // Lift tonight's story text off the canvas before anything can overwrite it, and clear
            // it so it is not shown twice. Read AFTER the yield above, so Quest_TheDeepEnd has had
            // its chance to queue one; read BEFORE onSleepEndFade below, which is where
            // Quest_SinkOrSwim queues TOMORROW's — that one must survive untouched.
            string overnightMessage = ReadAndClearSleepMessage(sc);

            // Not cosmetic: PlayerHealth.SetHealth(100f) is a listener on this.
            try { sc?.onSleepFullyFaded?.Invoke(); }
            catch (Exception ex) { TNSLog.Error($"Silent sleep: onSleepFullyFaded threw: {ex}"); }

            // Hand the message to the store BEFORE releasing sleep end: the capture that folds it
            // into the summary runs inside onSleepEnd, which the next few lines trigger.
            DailySummaryStore.NoteOvernightMessage(overnightMessage);

            // The release valve. Host only, mirroring vanilla's own InstanceFinder.IsServer guard —
            // a client calling this fails in the RPC writer. A client does not need to: the host's
            // ObserversRpc sets HostSleepDone on every machine, which frees each client's own copy
            // of WaitForSleepEnd, so onSleepEnd fires everywhere. Only the host saves.
            if (Core.IsHost)
            {
                TNSLog.Debug("Silent sleep: host — SetHostSleepDone(true); onSleepEnd and the daily save follow.");
                try { tm.SetHostSleepDone(true); }
                catch (Exception ex) { TNSLog.Error($"Silent sleep: SetHostSleepDone failed: {ex}"); }
            }
            else
            {
                TNSLog.Debug("Silent sleep: client — waiting for the host's SetHostSleepDone RPC.");
            }

            // IsSleepInProgress going false is the observable proof that WaitForSleepEnd ran to
            // completion, i.e. that onSleepEnd fired and (on the host) the save was written. There
            // is no other signal, and no session can run this game, so this is the log line to look
            // for. The "Daily summary captured" line from the ClearStats hook is the corroborator.
            float budget = Core.IsHost ? SilentSleepEndBudgetHost : SilentSleepEndBudgetClient;
            float start = Time.realtimeSinceStartup;
            while (tm != null && tm.IsSleepInProgress && Time.realtimeSinceStartup - start < budget)
                yield return null;

            float waited = Time.realtimeSinceStartup - start;
            if (tm != null && tm.IsSleepInProgress)
            {
                TNSLog.Warning($"Silent sleep: sleep end did NOT complete within {budget:0}s. " +
                               "onSleepEnd and the daily save may not have run this night. " +
                               (Core.IsHost
                                    ? "This machine is the host, so SetHostSleepDone(true) was sent — suspect the patch or a throwing subscriber."
                                    : "This machine is a client; the host never flagged sleep done."));
            }
            else
            {
                TNSLog.Debug($"Silent sleep: sleep end completed after {waited:0.00}s (save included on the host).");
            }

            // Story logic, and it runs AFTER sleep end in vanilla too. Quest_SinkOrSwim queues
            // tomorrow's message from in here, which is why the read above had to come first.
            try { sc?.onSleepEndFade?.Invoke(); }
            catch (Exception ex) { TNSLog.Error($"Silent sleep: onSleepEndFade threw: {ex}"); }

            if (!string.IsNullOrEmpty(overnightMessage))
                TNSLog.Msg($"Overnight: {overnightMessage}");

            _silentRoutine = null;
        }

        /// <summary>
        /// Takes the pending sleep message off the canvas and blanks it.
        ///
        /// The setter is protected, but QueueSleepMessage(string, float) is public on both arms and
        /// writes the same field, so an empty string through it is the supported way to clear.
        /// </summary>
        private static string ReadAndClearSleepMessage(SleepCanvas sc)
        {
            try
            {
                if (sc == null) return null;
                string msg = sc.QueuedSleepMessage;
                if (string.IsNullOrEmpty(msg)) return null;
                sc.QueueSleepMessage(string.Empty, 0f);
                return msg;
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Silent sleep: could not read the queued sleep message: {ex.Message}");
                return null;
            }
        }

        public static int GetLocalActiveUICount()
        {
            try
            {
                // activeUIElements went private in 0.4.6f13; ActiveUIElementCount is the public
                // accessor and exists on both the Mono assembly and the Il2Cpp interop wrapper.
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                return cam != null ? cam.ActiveUIElementCount : 0;
            }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Stops the post-sleep canvases queueing themselves while silent sleep is active.
    ///
    /// Vanilla drains this queue inside SleepCanvas's coroutine — the coroutine silent sleep does
    /// not run — so without this the list grows by one RankUpCanvas entry EVERY NIGHT and is never
    /// cleared. Turning silent sleep off later would then fire a whole backlog of identical events
    /// at the first real sleep. Blocking the add is cleaner than reaching for the private list, and
    /// it needs no reflection and no #if.
    ///
    /// Nothing is lost: both StartEvent bodies were read in full and are pure presentation, and
    /// the mod already suppresses them. RegionUnlockedCanvas.QueueUnlocked still completes, so
    /// Patch_RegionUnlockedCanvas_QueueUnlocked still captures the region for the phone app.
    /// </summary>
    [HarmonyPatch(typeof(SleepCanvas), "AddPostSleepEvent")]
    public static class Patch_SleepCanvas_AddPostSleepEvent
    {
        static Patch_SleepCanvas_AddPostSleepEvent() => PatchLogger.LogPatchLoad(nameof(Patch_SleepCanvas_AddPostSleepEvent));

        [HarmonyPrefix]
        public static bool Prefix() => !Core.SilentSleepActive;
    }

    [HarmonyPatch(typeof(TimeManager), "SkipForwardToTime")]
    public static class Patch_TimeManager_SkipForwardToTime
    {
        [HarmonyPrefix]
        public static bool Prefix(TimeManager __instance)
        {
            if (__instance == null) return true;

            // Two reasons to skip the fast-forward:
            // 1. The one-shot flag set by deferred SleepStart — we explicitly know we're entering
            //    sleep past 07:00 and don't want to rewind.
            // 2. Belt-and-suspenders: time is already at/past 07:00 regardless of how we got here.
            bool skipFlag = Core.SkipWakeFastForwardOnce;
            bool pastWake = __instance.CurrentTime >= 700;

            if (skipFlag || pastWake)
            {
                Core.SkipWakeFastForwardOnce = false; // consume the one-shot guard
                TNSLog.Debug($"Skipping SkipForwardToTime (skipFlag={skipFlag}, pastWake={pastWake}, t={__instance.CurrentTime:D4}).");
                return false;
            }

            TNSLog.Debug($"SkipForwardToTime: running original (t={__instance.CurrentTime:D4}).");
            return true;
        }
    }

#if !IL2CPP
    internal static class TMAccess
    {
        private static readonly Action<TimeManager, int>   _setCurrentTime;
        private static readonly Action<TimeManager, int>   _setElapsedDays;
        private static readonly Action<TimeManager, int>   _setDailyMinSum;
        private static readonly FieldInfo                  _secondsOnCurrentMinute;
        private static readonly Action<TimeManager, bool>  _setSleepInProgress;
        // TimeSpeedMultiplier has private set + server-only guard in SetTimeSpeedMultiplier(),
        // so we write the backing field directly for client-side multiplier sync.
        private static readonly FieldInfo                  _timeSpeedMultiplierField;

        static TMAccess()
        {
            _setCurrentTime          = BuildSetter<int>("CurrentTime");
            _setElapsedDays          = BuildSetter<int>("ElapsedDays");
            _setDailyMinSum          = BuildSetter<int>("DailyMinSum");
            _secondsOnCurrentMinute  = AccessTools.Field(typeof(TimeManager), "_secondsOnCurrentMinute");
            _setSleepInProgress      = BuildSetter<bool>("IsSleepInProgress");
            _timeSpeedMultiplierField = AccessTools.Field(typeof(TimeManager), "<TimeSpeedMultiplier>k__BackingField");
        }

        private static Action<TimeManager, T> BuildSetter<T>(string propName)
        {
            var setter = AccessTools.PropertySetter(typeof(TimeManager), propName);
            if (setter == null) return (_, __) => { };
            return (Action<TimeManager, T>)Delegate.CreateDelegate(typeof(Action<TimeManager, T>), null, setter);
        }

        public static void SetCurrentTime(TimeManager tm, int v)                => _setCurrentTime(tm, v);
        public static void SetElapsedDays(TimeManager tm, int v)                => _setElapsedDays(tm, v);
        public static void SetDailyMinSum(TimeManager tm, int v)                => _setDailyMinSum(tm, v);
        public static void SetTimeOnCurrentMinute(TimeManager tm, float v)      => _secondsOnCurrentMinute?.SetValue(tm, v);
        public static void SetSleepInProgress(TimeManager tm, bool v)           => _setSleepInProgress?.Invoke(tm, v);
        public static void SetTimeSpeedMultiplierDirect(TimeManager tm, float v) => _timeSpeedMultiplierField?.SetValue(tm, Mathf.Max(v, 0f));
    }
#else
    internal static class TMAccess
    {
        private static readonly MethodInfo _setCurrentTime          = AccessTools.PropertySetter(typeof(TimeManager), "CurrentTime");
        private static readonly MethodInfo _setElapsedDays          = AccessTools.PropertySetter(typeof(TimeManager), "ElapsedDays");
        private static readonly MethodInfo _setDailyMinSum          = AccessTools.PropertySetter(typeof(TimeManager), "DailyMinSum");
        private static readonly MethodInfo _setSleepInProgress      = AccessTools.PropertySetter(typeof(TimeManager), "IsSleepInProgress");

        // _secondsOnCurrentMinute and the TimeSpeedMultiplier backing field are NOT reached by
        // reflection here, and that was the bug rather than a limitation.
        //
        // Il2CppInterop does not drop native instance fields — it REGENERATES each one as a
        // 'public unsafe' PROPERTY on the wrapper, with a getter and a setter, reading and writing
        // the native offset via NativeFieldInfoPtr_*. So AccessTools.Field / GetDeclaredFields find
        // nothing (they look for FIELDS), which is the entire reason both setters used to be silent
        // no-ops on this arm. Direct member access works and is checked by the compiler.
        //
        // Two naming rules, confirmed 2026-08-23 against the decompile in References\:
        //   ordinary fields keep their name        -> _secondsOnCurrentMinute
        //   auto-property backing fields renamed   -> <Name>k__BackingField becomes _Name_k__BackingField
        // See CLAUDE.md, "TMAccess / SCAccess". Do NOT reintroduce a FindField lookup for these.

        static TMAccess()
        {
            // Dump the member list when debug logging is on — use this to find renamed members
            // after a game update.
            if (Core.cfgDebugLogging?.Value == true)
                LogTimeManagerMembers();
        }

        /// <summary>
        /// Dumps TimeManager's declared members to the debug log.
        /// Run with EnableDebugLogging=true to identify renamed members after a game update.
        ///
        /// PROPERTIES ARE THE IMPORTANT HALF ON THIS ARM and the field list will look almost empty,
        /// which is not a bug: Il2CppInterop regenerates native instance fields as properties. An
        /// earlier version of this dump listed fields only, which is precisely why the two setters
        /// above were believed unreachable for so long. If a member moved, look in the PROPERTIES
        /// list first.
        /// </summary>
        private static void LogTimeManagerMembers()
        {
            try
            {
                var fields = AccessTools.GetDeclaredFields(typeof(TimeManager));
                TNSLog.Debug($"[TMAccess] TimeManager declared fields ({fields.Count}) — expected to be sparse on IL2CPP:");
                foreach (var f in fields)
                    TNSLog.Debug($"  field [{f.FieldType.Name}] {f.Name}");

                var props = AccessTools.GetDeclaredProperties(typeof(TimeManager));
                TNSLog.Debug($"[TMAccess] TimeManager declared properties ({props.Count}) — native fields land here:");
                foreach (var p in props)
                    TNSLog.Debug($"  prop  [{p.PropertyType.Name}] {p.Name}{(p.CanWrite ? " (writable)" : "")}");
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"[TMAccess] Member probe failed: {ex.Message}");
            }
        }

        public static void SetCurrentTime(TimeManager tm, int v)                 => _setCurrentTime?.Invoke(tm, new object[] { v });
        public static void SetElapsedDays(TimeManager tm, int v)                 => _setElapsedDays?.Invoke(tm, new object[] { v });
        public static void SetDailyMinSum(TimeManager tm, int v)                 => _setDailyMinSum?.Invoke(tm, new object[] { v });
        public static void SetSleepInProgress(TimeManager tm, bool v)            => _setSleepInProgress?.Invoke(tm, new object[] { v });

        // Generated field-properties — see the note above. These are real writes on IL2CPP now,
        // not the no-ops they were before 2026-08-23.
        public static void SetTimeOnCurrentMinute(TimeManager tm, float v)       => tm._secondsOnCurrentMinute = v;
        public static void SetTimeSpeedMultiplierDirect(TimeManager tm, float v) => tm._TimeSpeedMultiplier_k__BackingField = Mathf.Max(v, 0f);
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

    // Debug helpers to verify sleep flow.
    [HarmonyPatch(typeof(TimeManager))]
    public static class Debug_TimeManager
    {
        [HarmonyPostfix, HarmonyPatch("StartSleep")]
        static void StartSleep_Postfix(TimeManager __instance)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return;
            TNSLog.Debug("StartSleep (RPC) observed -> suppressing tick actions.");
            Patch_Tick_XPMenu.SuppressDuringSleep(true);
        }

        // When host marks done, everyone gets this ObserversRpc; mirror cleanup when done == true.
        [HarmonyPostfix, HarmonyPatch("SetHostSleepDone")]
        static void SetHostSleepDone_Postfix(TimeManager __instance, bool done)
        {
            if (Core.cfgMultiplierOnly?.Value == true) return;
            TNSLog.Debug($"SetHostSleepDone (RPC) observed: done={done}");
            if (!done) return;

            Patch_SleepCanvas_SleepStart.StopRoutine();
            Patch_Tick_XPMenu.SuppressDuringSleep(false);
            Patch_Tick_XPMenu.TryRunSleepEndCleanup("SetHostSleepDone_RPC");
            Core.SkipWakeFastForwardOnce = false;

            if (__instance.IsSleepInProgress)
            {
                TMAccess.SetSleepInProgress(__instance, false);
                TNSLog.Warning("Host done -> forced SleepInProgress=false (engine hadn't cleared yet).");
            }
        }
    }
}