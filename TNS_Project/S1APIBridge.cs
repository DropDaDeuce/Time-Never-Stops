using System;
using System.Reflection;

namespace Time_Never_Stops
{
    /// <summary>
    /// The firewall between the mod and S1API.
    ///
    /// THE RULE THIS TYPE EXISTS TO ENFORCE: no always-loaded code may mention DailySummaryApp.
    /// That class derives from S1API.PhoneApp.PhoneApp, so if S1API is absent the CLR cannot load
    /// it. As long as nothing references it, it is simply never loaded and the rest of the mod runs
    /// untouched. The moment Core.cs (or anything else on the startup path) names that type, a
    /// missing S1API becomes a TypeLoadException in OUR startup instead of a missing phone icon.
    /// So Core talks to this class, and only DailySummaryApp itself calls back into it.
    ///
    /// Harmony is safe either way — AccessTools.GetTypesFromAssembly catches
    /// ReflectionTypeLoadException and patches the types it could load — but relying on that alone
    /// would leave the behaviour to a third party's error handling. This is explicit instead.
    ///
    /// FAIL-SAFE DIRECTION: when anything is uncertain, we do NOT suppress the vanilla popup. The
    /// player always keeps a way to see their summary; the worst case is that they see it twice.
    /// </summary>
    internal static class S1APIBridge
    {
        private const string S1ApiAssemblyName = "S1API";
        private const string PhoneAppTypeName  = "S1API.PhoneApp.PhoneApp";

        private static bool _probed;
        private static bool _present;
        private static bool _registered;
        private static string _failure;
        private static bool _loggedAbsence;

        /// <summary>
        /// True if S1API is loaded in this process.
        ///
        /// Probed BY NAME through the assembly list — deliberately never by referencing the type,
        /// which is the whole point of this class. Cached after the first call.
        /// </summary>
        internal static bool IsS1APIPresent
        {
            get
            {
                if (_probed) return _present;
                _probed = true;

                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!string.Equals(asm.GetName().Name, S1ApiAssemblyName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Present as an assembly is not enough — confirm the type we depend on is
                        // actually there, so a renamed or gutted S1API reads as absent rather than
                        // exploding later.
                        _present = asm.GetType(PhoneAppTypeName, throwOnError: false) != null;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _present = false;
                    TNSLog.Debug($"S1API probe failed, treating as absent: {ex.Message}");
                }

                return _present;
            }
        }

        /// <summary>True once the phone app has constructed and registered itself successfully.</summary>
        internal static bool AppRegistered => _registered && _failure == null;

        /// <summary>Set if the app registered but then failed — its UI threw, most likely.</summary>
        internal static string AppFailure => _failure;

        internal static void MarkAppRegistered()
        {
            _registered = true;
            _failure = null;
        }

        internal static void MarkAppFailed(string reason)
        {
            _failure = reason ?? "unknown";
            TNSLog.Error($"Daily Summary phone app is unavailable ({_failure}). " +
                         "Falling back to the vanilla popup so the summary is not lost.");
        }

        /// <summary>
        /// Whether the vanilla Daily Summary popup should be suppressed in favour of the app.
        ///
        /// Note the direction: this returns true ONLY when the app is known-good. Absent S1API,
        /// a failed app, an app that never registered, or the feature switched off all mean the
        /// popup keeps working exactly as it did before this feature existed.
        /// </summary>
        internal static bool ShouldGateSummaryBehindApp()
        {
            if (Core.cfgEnableSummaryApp?.Value != true) return false;

            if (!IsS1APIPresent)
            {
                if (!_loggedAbsence)
                {
                    _loggedAbsence = true;
                    TNSLog.Error(
                        "EnableDailySummaryApp is on but S1API is not installed, so there is no phone app to " +
                        "show the summary in. Install ifBars-S1API_Forked, or set EnableDailySummaryApp=false " +
                        "to silence this. The vanilla popup stays enabled in the meantime.");
                }
                return false;
            }

            return AppRegistered;
        }

        /// <summary>Called on Menu scene load so a new session re-probes and re-registers cleanly.</summary>
        internal static void Reset()
        {
            _registered = false;
            _failure = null;
            // _probed/_present are process-wide facts about the loaded assembly list; leave them.
        }
    }
}
