using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Registry lives in the ROOT namespace, not ItemFramework — and Name/Icon are public fields on
// ScheduleOne.Core.Items.Framework.BaseItemDefinition, which ItemDefinition inherits. Both were
// checked in References\ rather than guessed; see CLAUDE.md, "Reference sources".
#if IL2CPP
using UI = Il2CppScheduleOne.UI;
using TimeManager = Il2CppScheduleOne.GameTime.TimeManager;
using Registry = Il2CppScheduleOne.Registry;
using MoneyManager = Il2CppScheduleOne.Money.MoneyManager;
using LevelManager = Il2CppScheduleOne.Levelling.LevelManager;
using FullRank = Il2CppScheduleOne.Levelling.FullRank;
using EMapRegion = Il2CppScheduleOne.Map.EMapRegion;
using Map = Il2CppScheduleOne.Map.Map;
using Il2CppScheduleOne.DevUtilities;
#else
using UI = ScheduleOne.UI;
using TimeManager = ScheduleOne.GameTime.TimeManager;
using Registry = ScheduleOne.Registry;
using MoneyManager = ScheduleOne.Money.MoneyManager;
using LevelManager = ScheduleOne.Levelling.LevelManager;
using FullRank = ScheduleOne.Levelling.FullRank;
using EMapRegion = ScheduleOne.Map.EMapRegion;
using Map = ScheduleOne.Map.Map;
using ScheduleOne.DevUtilities;
#endif

namespace Time_Never_Stops
{
    /// <summary>
    /// Holds the last day's summary numbers so the phone app can show them after the game has
    /// thrown them away.
    ///
    /// WHY THIS EXISTS: ScheduleOne.UI.DailySummary.SleepEnd() calls ClearStats(), which wipes
    /// itemsSoldByPlayer / moneyEarnedByPlayer / moneyEarnedByDealers / xpGained. Anything shown
    /// later has to be copied out BEFORE that runs. Capture happens in a prefix on ClearStats —
    /// see DailySummaryPatch in Core.cs.
    ///
    /// THIS FILE OWNS EVERY GAME-TYPE TOUCH FOR THE FEATURE. DailySummaryApp.cs deliberately
    /// references only S1API and UnityEngine, so it needs no #if and cannot break on one arm only.
    /// Keep it that way: if the app needs something from the game, add a helper here.
    ///
    /// NOTHING HERE MAY THROW INTO A CALLER. Capture runs inside a Harmony prefix on the game's
    /// sleep path; an exception escaping it would break sleep for the player. Every public entry
    /// point is wrapped, and a failure degrades to "no summary available" rather than propagating.
    /// </summary>
    internal static class DailySummaryStore
    {
        internal readonly struct SoldItem
        {
            internal SoldItem(string id, int count) { Id = id; Count = count; }
            internal string Id { get; }
            internal int Count { get; }
        }

        /// <summary>True once a day has actually been captured. False means "nothing to show yet".</summary>
        internal static bool HasSummary { get; private set; }

        /// <summary>Set when a capture was attempted and failed. The app shows this instead of zeroes.</summary>
        internal static string CaptureError { get; private set; }

        internal static string DayLabel { get; private set; } = string.Empty;
        internal static float MoneyEarnedByPlayer { get; private set; }
        internal static float MoneyEarnedByDealers { get; private set; }
        internal static int XpGained { get; private set; }

        /// <summary>Non-empty when the night produced a rank-up, e.g. "Hoodlum I". Null otherwise.</summary>
        internal static string RankBefore { get; private set; }
        internal static string RankAfter { get; private set; }
        private static readonly List<string> _unlocks = new List<string>();
        internal static IReadOnlyList<string> Unlocks => _unlocks;

        /// <summary>
        /// Regions unlocked this night. Filled by a prefix on RegionUnlockedCanvas.QueueUnlocked,
        /// which runs during the sleep flow — i.e. BEFORE the ClearStats capture — so the list is
        /// accumulated separately and folded in at capture time.
        /// </summary>
        private static readonly List<string> _pendingRegions = new List<string>();
        private static readonly List<string> _regions = new List<string>();
        internal static IReadOnlyList<string> RegionsUnlocked => _regions;

        /// <summary>
        /// The story text vanilla would have shown on the sleep screen — "The loan sharks are
        /// arriving tonight.", "In the middle of the night, the door is kicked in...".
        ///
        /// SILENT SLEEP IS WHY THIS EXISTS. The message is written by quests into
        /// SleepCanvas.QueuedSleepMessage and displayed by SleepCanvas's own coroutine. With that
        /// coroutine bypassed the text has no other route to the player, and losing it means the
        /// Deep End kidnap and the loan-shark countdown happen with no explanation at all.
        /// Pending/live pair, same shape as the regions above, because the silent-sleep routine
        /// reads it BEFORE the ClearStats capture that folds it in.
        /// </summary>
        private static string _pendingOvernightMessage;
        internal static string OvernightMessage { get; private set; }

        /// <summary>
        /// Raised after a successful capture so the phone app can rebuild itself.
        ///
        /// WITHOUT THIS THE APP IS ALWAYS EMPTY. S1API calls OnCreatedUI exactly once, from its
        /// HomeScreen.Start postfix — that is scene load, long before any night has happened — and
        /// never again when the player opens the app. b23 shipped without this and the panel showed
        /// "No summary yet." forever, which is precisely what Mathew saw in game.
        /// </summary>
        internal static event Action OnSummaryChanged;

        private static readonly List<SoldItem> _items = new List<SoldItem>();
        internal static IReadOnlyList<SoldItem> Items => _items;

#if !IL2CPP
        // MONO only. These are private instance fields on the real type, so they need reflection —
        // the IL2CPP arm reaches the same data through generated properties instead and compiles
        // the access directly. Resolved once; a null here degrades to zeroes rather than throwing.
        private static readonly FieldInfo _fiItemsSold =
            AccessTools.Field(typeof(UI.DailySummary), "itemsSoldByPlayer");
        private static readonly FieldInfo _fiMoneyPlayer =
            AccessTools.Field(typeof(UI.DailySummary), "moneyEarnedByPlayer");
        private static readonly FieldInfo _fiMoneyDealers =
            AccessTools.Field(typeof(UI.DailySummary), "moneyEarnedByDealers");

        static DailySummaryStore()
        {
            if (_fiItemsSold == null || _fiMoneyPlayer == null || _fiMoneyDealers == null)
                TNSLog.Warning("DailySummaryStore: one or more DailySummary fields were not found — " +
                               "the phone app will show an incomplete summary. A game update likely renamed them; " +
                               "check References\\MONO\\Assembly-CSharp\\ScheduleOne\\UI\\DailySummary.cs.");
        }
#endif

        /// <summary>
        /// Copies everything out of the live DailySummary. Call from a PREFIX on ClearStats — by
        /// the time the postfix would run the values are already gone.
        ///
        /// Deliberately captures the day label too: vanilla's Open() titles the popup with the
        /// CURRENT day, so reading it later would label yesterday's numbers with today's date.
        /// </summary>
        internal static void Capture(UI.DailySummary ds)
        {
            try
            {
                if (ds == null)
                {
                    Fail("DailySummary instance was null at capture time.");
                    return;
                }

                _items.Clear();

                // THE PARITY SPLIT, AND IT IS NOT SYMMETRICAL — see CLAUDE.md, "TMAccess / SCAccess".
                //
                // On MONO these three are genuinely PRIVATE FIELDS, so they need reflection.
                // On IL2CPP Il2CppInterop regenerated them as PUBLIC PROPERTIES under the same
                // names, so direct member access works and is compiler-checked. Writing the MONO
                // form on both arms does not compile; writing the IL2CPP form on both arms does not
                // compile either. Both halves are required.
                //
                // xpGained needs no split: it has a public getter on MONO and is a property on
                // IL2CPP, so plain member access is correct everywhere.
#if IL2CPP
                var sold = ds.itemsSoldByPlayer;
                if (sold != null)
                {
                    // Il2CppSystem dictionary — enumerate and copy out. The game clears the
                    // original microseconds from now, so holding its reference gives an empty list.
                    foreach (var kvp in sold)
                        _items.Add(new SoldItem(kvp.Key, kvp.Value));
                }

                MoneyEarnedByPlayer  = ds.moneyEarnedByPlayer;
                MoneyEarnedByDealers = ds.moneyEarnedByDealers;
#else
                var sold = _fiItemsSold?.GetValue(ds) as Dictionary<string, int>;
                if (sold != null)
                {
                    foreach (var kvp in sold)
                        _items.Add(new SoldItem(kvp.Key, kvp.Value));
                }

                MoneyEarnedByPlayer  = _fiMoneyPlayer?.GetValue(ds) is float mp ? mp : 0f;
                MoneyEarnedByDealers = _fiMoneyDealers?.GetValue(ds) is float md ? md : 0f;
#endif
                XpGained             = ds.xpGained;
                DayLabel             = BuildDayLabel();

                CaptureRankUp(XpGained);

                _regions.Clear();
                _regions.AddRange(_pendingRegions);
                _pendingRegions.Clear();

                // Deliberately assigned even when null: a message belongs to ONE night, and
                // leaving last night's text on today's summary would be worse than losing it.
                OvernightMessage        = _pendingOvernightMessage;
                _pendingOvernightMessage = null;

                HasSummary   = true;
                CaptureError = null;

                TNSLog.Debug($"Daily summary captured: {_items.Count} item type(s), " +
                             $"player={MoneyEarnedByPlayer}, dealers={MoneyEarnedByDealers}, xp={XpGained}, " +
                             $"rankUp={(RankAfter != null ? RankBefore + " -> " + RankAfter : "none")}, " +
                             $"regions={_regions.Count}.");

                RaiseChanged();
            }
            catch (Exception ex)
            {
                // Do NOT rethrow. This runs on the sleep path; breaking it would cost the player
                // their night, and a missing summary is a cosmetic loss by comparison.
                Fail($"{ex.GetType().Name}: {ex.Message}");
                TNSLog.Error($"Daily summary capture failed, sleep is unaffected: {ex}");
            }
        }

        private static void RaiseChanged()
        {
            try { OnSummaryChanged?.Invoke(); }
            catch (Exception ex)
            {
                // A subscriber blowing up must not take the sleep path with it — this runs inside
                // the ClearStats prefix.
                TNSLog.Error($"A summary-changed subscriber threw; sleep is unaffected: {ex}");
            }
        }

        /// <summary>
        /// Works out whether the night crossed a rank boundary, from XP alone.
        ///
        /// Vanilla's RankUpCanvas does the same arithmetic: the rank you had is the rank for
        /// (TotalXP - xpGained), and the rank you have now is the rank for TotalXP. Deriving it
        /// here means we do not have to let RankUpCanvas run to find out — which is the point,
        /// since the whole reason for this is that its UI blocks the player.
        /// </summary>
        private static void CaptureRankUp(int xpGained)
        {
            RankBefore = null;
            RankAfter = null;
            OvernightMessage = null;
            _unlocks.Clear();

            try
            {
                var lm = NetworkSingleton<LevelManager>.Instance;
                if (lm == null || xpGained <= 0) return;

                int totalXp = lm.TotalXP;
                FullRank before = lm.GetFullRank(totalXp - xpGained);
                FullRank after = lm.GetFullRank(totalXp);

                // Compare the fields rather than the struct: == is defined on FullRank, but on
                // IL2CPP this is an interop struct and value equality is not worth trusting.
                if (before.Rank == after.Rank && before.Tier == after.Tier) return;

                RankBefore = FullRank.GetString(before);
                RankAfter = FullRank.GetString(after);

                // Unlockables are a separate try: a dictionary keyed by an interop struct is the
                // shakiest thing in this method, and losing the unlock list must not cost us the
                // rank-up line itself.
                try
                {
                    if (lm.Unlockables != null && lm.Unlockables.ContainsKey(after))
                    {
                        var list = lm.Unlockables[after];
                        if (list != null)
                        {
                            foreach (var u in list)
                                if (u != null && !string.IsNullOrEmpty(u.Title)) _unlocks.Add(u.Title);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TNSLog.Debug($"Could not read unlockables for the new rank: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"Rank-up detection failed: {ex.Message}");
                RankBefore = null;
                RankAfter = null;
                _unlocks.Clear();
            }
        }

        /// <summary>
        /// Records a region unlock as the game queues it. Called from a prefix on
        /// RegionUnlockedCanvas.QueueUnlocked, which happens during sleep, before ClearStats.
        /// </summary>
        internal static void NoteRegionUnlocked(EMapRegion region)
        {
            try
            {
                string name = region.ToString();
                try
                {
                    var data = Singleton<Map>.Instance?.GetRegionData(region);
                    if (data != null && !string.IsNullOrEmpty(data.Name)) name = data.Name;
                }
                catch (Exception ex)
                {
                    TNSLog.Debug($"Could not resolve region name for {region}: {ex.Message}");
                }

                if (!_pendingRegions.Contains(name)) _pendingRegions.Add(name);
                TNSLog.Debug($"Region unlock recorded for the summary: {name}");
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"NoteRegionUnlocked failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Records the sleep message the silent-sleep routine lifted off SleepCanvas, so the
        /// following ClearStats capture can fold it into the summary. Called before the capture,
        /// exactly like NoteRegionUnlocked.
        /// </summary>
        internal static void NoteOvernightMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _pendingOvernightMessage = message;
            TNSLog.Debug($"Overnight message recorded for the summary: {message}");
        }

        private static void Fail(string reason)
        {
            HasSummary   = false;
            CaptureError = reason;
            ClearData();
            RaiseChanged();
        }

        /// <summary>Clears everything. Called on Menu scene load so a new save starts empty.</summary>
        internal static void Reset()
        {
            HasSummary   = false;
            CaptureError = null;
            ClearData();
            _pendingRegions.Clear();
            _pendingOvernightMessage = null;
            OnSummaryChanged = null; // the old app instance is gone with the scene
        }

        private static void ClearData()
        {
            DayLabel = string.Empty;
            _items.Clear();
            _regions.Clear();
            _unlocks.Clear();
            RankBefore = null;
            RankAfter = null;
            MoneyEarnedByPlayer = MoneyEarnedByDealers = 0f;
            XpGained = 0;
        }

        /// <summary>
        /// "Monday, Day 4" — the same shape vanilla's DailySummary.Open() builds, including the
        /// +1 on ElapsedDays. Falls back to an empty string rather than throwing.
        /// </summary>
        private static string BuildDayLabel()
        {
            try
            {
                var tm = NetworkSingleton<TimeManager>.Instance;
                if (tm == null) return string.Empty;
                return $"{tm.CurrentDay}, Day {tm.ElapsedDays + 1}";
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"Could not build day label: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Resolves a sold-item id to its display name and icon.
        ///
        /// Resolution happens HERE, at display time, rather than being baked in at capture: an item
        /// id is stable, a resolved name or a Sprite reference is not (mods change the registry,
        /// and a Sprite from a previous scene can be destroyed under us).
        /// </summary>
        internal static bool TryGetItemDisplay(string id, out string displayName, out Sprite icon)
        {
            displayName = id;
            icon = null;

            try
            {
                var def = Registry.GetItem(id);
                if (def == null) return false;
                if (!string.IsNullOrEmpty(def.Name)) displayName = def.Name;
                icon = def.Icon;
                return true;
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"Could not resolve item '{id}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Formats a money value the way the game does, so the app matches the popup it replaces.
        /// Falls back to a plain currency format if MoneyManager is unavailable.
        /// </summary>
        internal static string FormatMoney(float amount)
        {
            try
            {
                return MoneyManager.FormatAmount(amount);
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"MoneyManager.FormatAmount failed: {ex.Message}");
                return "$" + amount.ToString("N2");
            }
        }
    }
}
