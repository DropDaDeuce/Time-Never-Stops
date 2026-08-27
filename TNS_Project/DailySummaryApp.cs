using System;
using S1API.PhoneApp;
using S1API.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Time_Never_Stops
{
    /// <summary>
    /// The Daily Summary phone app.
    ///
    /// THIS FILE MUST NOT REFERENCE A GAME TYPE. It uses S1API and UnityEngine only, which is why
    /// it carries no #if and compiles identically for both arms — Unity types are NOT
    /// Il2Cpp-prefixed, only game assemblies are. Everything that touches the game lives in
    /// DailySummaryStore. Keep that split; it is the only reason this feature cannot break on one
    /// arm while passing on the other.
    ///
    /// REGISTRATION IS AUTOMATIC AND WE DO NOT CONTROL IT. S1API postfixes HomeScreen.Start, finds
    /// every public PhoneApp subclass with a parameterless constructor, constructs it and calls
    /// SpawnUI/SpawnIcon. Three consequences, and the second one cost a whole in-game test:
    ///   - This type is constructed fresh on every HomeScreen.Start, so it holds no summary state.
    ///     The data lives in the static DailySummaryStore and this class is a view over it.
    ///   - **OnCreatedUI IS CALLED ONCE, AT SCENE LOAD — NOT WHEN THE PLAYER OPENS THE APP.** So the
    ///     panel must rebuild itself when the summary changes, or it stays frozen at whatever was
    ///     true at scene load. b23 shipped without that and read "No summary yet." forever.
    ///   - Nothing in the mod may reference this type from an always-loaded path. If S1API is
    ///     missing, loading this type throws, and the only safe outcome is that it is never loaded
    ///     at all. Core.cs talks to S1APIBridge, never to this class.
    ///
    /// NOTHING HERE MAY ESCAPE AS AN EXCEPTION. S1API calls OnCreatedUI from inside a Harmony
    /// postfix on the game's own HomeScreen.Start, and Rebuild runs off the sleep path. Every entry
    /// point is wrapped, reports loudly through TNSLog, and degrades to a readable error panel.
    /// </summary>
    public class DailySummaryApp : PhoneApp
    {
        internal const string AppKey = "TNSDailySummary";
        private const string IconResourceName = "Time_Never_Stops.icon.png";

        protected override string AppName   => AppKey;
        protected override string AppTitle  => "Daily Summary";
        protected override string IconLabel => "Summary";

        // Never actually used: TrySetEmbeddedIcon supplies a sprite before SpawnIcon runs, and
        // S1API prefers that over this path. Abstract, so it still has to be answered.
        protected override string IconFileName => "TimeNeverStops_Summary.png";

        private static readonly Color PanelColor  = new Color(0.11f, 0.11f, 0.13f, 1f);
        private static readonly Color HeaderColor = new Color(0.16f, 0.16f, 0.19f, 1f);
        private static readonly Color MutedColor  = new Color(0.68f, 0.68f, 0.72f, 1f);
        private static readonly Color AccentColor = new Color(0.45f, 0.80f, 0.55f, 1f);
        private static readonly Color StoryColor  = new Color(0.92f, 0.80f, 0.45f, 1f);

        private GameObject _container;

        protected override void OnCreated()
        {
            try
            {
                // Set the icon BEFORE base.OnCreated(). S1API stores it as a pending sprite and
                // prefers it over IconFileName when SpawnIcon runs, which keeps us off the
                // load-a-PNG-from-Mods path and its "Icon file not found" error entirely.
                TrySetEmbeddedIcon();

                base.OnCreated();
                S1APIBridge.MarkAppRegistered();
                TNSLog.Msg("Daily Summary phone app registered.");
            }
            catch (Exception ex)
            {
                S1APIBridge.MarkAppFailed($"OnCreated failed: {ex.Message}");
                TNSLog.Error($"Daily Summary app failed during OnCreated: {ex}");
            }
        }

        protected override void OnCreatedUI(GameObject container)
        {
            _container = container;

            // Subscribe, then build. See the class note: this method runs once at scene load, so
            // without this subscription the panel never reflects a night that happens afterwards.
            DailySummaryStore.OnSummaryChanged -= Rebuild;
            DailySummaryStore.OnSummaryChanged += Rebuild;

            Rebuild();
        }

        protected override void OnDestroyed()
        {
            // A stale subscription would keep rebuilding into a destroyed GameObject.
            DailySummaryStore.OnSummaryChanged -= Rebuild;
            _container = null;
            base.OnDestroyed();
        }

        private void Rebuild()
        {
            var container = _container;
            if (container == null) return;

            try
            {
                UIFactory.ClearChildren(container.transform);
                BuildUI(container);
            }
            catch (Exception ex)
            {
                S1APIBridge.MarkAppFailed($"UI build failed: {ex.Message}");
                TNSLog.Error($"Daily Summary app UI failed to build; the mod's time features are unaffected: {ex}");
                TryShowFatalPanel(container, ex);
            }
        }

        private void BuildUI(GameObject container)
        {
            var root = UIFactory.Panel("TNSSummaryPanel", container.transform, PanelColor, fullAnchor: true);

            // Everything below sets anchors AND zeroes the offsets by hand.
            //
            // UIFactory.Panel only zeroes offsets when fullAnchor is true; given explicit anchors it
            // leaves the RectTransform's default 100x100 sizeDelta in place, so the panel overhangs
            // its parent by 50px on every side. UIFactory.Text is worse — it never touches the
            // RectTransform at all, so a label is a 100x100 box floating in the middle of its
            // parent, which is why b23's empty message rendered as a narrow wrapped column.
            // UIFactory.TopBar inherits the same overhang, which is what pushed the title off the
            // left edge of the phone. SetRect() is the fix; every element here goes through it.

            var header = UIFactory.Panel("TNSSummaryHeader", root.transform, HeaderColor);
            SetRect(header, 0f, 0.86f, 1f, 1f);

            var title = UIFactory.Text("TNSSummaryTitle", "Daily Summary", header.transform, 24,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(title.gameObject, 0.03f, 0f, 0.97f, 1f);

            var body = UIFactory.Panel("TNSSummaryBody", root.transform, PanelColor);
            SetRect(body, 0f, 0f, 1f, 0.86f);

            if (!DailySummaryStore.HasSummary)
            {
                var empty = UIFactory.Text("TNSSummaryEmpty", EmptyMessage(), body.transform, 18,
                    TextAnchor.MiddleCenter);
                SetRect(empty.gameObject, 0.06f, 0.1f, 0.94f, 0.9f);
                return;
            }

            // ScrollableVerticalList already anchors correctly and already puts a
            // VerticalLayoutGroup + ContentSizeFitter on its content. Do NOT add a second layout
            // group on top of it, which is what b23 did.
            var list = UIFactory.ScrollableVerticalList("TNSSummaryList", body.transform, out _);

            // THIS LINE IS THE BODY OVERFLOW, AND IT IS THE THIRD DISTINCT CAUSE IN THIS PANEL.
            //
            // ScrollableVerticalList anchors its Content to (0,1)-(1,1) but never touches
            // sizeDelta, so it keeps Unity's default 100x100. With a stretched horizontal anchor a
            // sizeDelta.x of 100 means "viewport width PLUS 100" — 50px of overhang on each side,
            // inherited by every row through the layout group. The viewport's Mask then slices the
            // ends off, which from the outside reads as text running out of the phone.
            //
            // Only the WIDTH may be zeroed. The height is driven by the ContentSizeFitter and
            // writing it here would fight the layout every frame.
            list.sizeDelta       = new Vector2(0f, list.sizeDelta.y);
            list.anchoredPosition = new Vector2(0f, list.anchoredPosition.y);

            // Take control of the width behaviour of that layout group. It is created with only
            // childControlHeight set, which leaves child WIDTH to Unity's default — and the default
            // left every row at UIFactory.Text's untouched 100-wide RectTransform, positioned so
            // the line ran off the left edge of the phone. Forcing width means each row is exactly
            // the content width minus padding, which is the only way to be sure nothing overhangs.
            var vlg = list.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childControlWidth = true;
                vlg.childForceExpandWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandHeight = false;
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.spacing = 6;
                vlg.padding = new RectOffset(16, 16, 12, 12);
            }

            if (!string.IsNullOrEmpty(DailySummaryStore.DayLabel))
                AddLine(list, "TNSSummaryDay", DailySummaryStore.DayLabel, 20, MutedColor);

            // Vanilla shows this on the sleep screen. Silent sleep never draws that screen, so
            // without this line the loan-shark countdown and the Deep End kidnap arrive with no
            // explanation whatsoever — see DailySummaryStore.OvernightMessage.
            if (!string.IsNullOrEmpty(DailySummaryStore.OvernightMessage))
                AddLine(list, "TNSSummaryOvernight", DailySummaryStore.OvernightMessage, 18, StoryColor);

            // Rank-ups and region unlocks are reported here instead of interrupting the player with
            // their own full-screen canvases — see Patch_RankUpCanvas_StartEvent in Core.cs.
            if (DailySummaryStore.RankAfter != null)
            {
                AddLine(list, "TNSSummaryRank",
                    $"Rank up:  {DailySummaryStore.RankBefore}  ->  {DailySummaryStore.RankAfter}", 20, AccentColor);

                var unlocks = DailySummaryStore.Unlocks;
                for (int i = 0; i < unlocks.Count; i++)
                    AddLine(list, $"TNSSummaryUnlock{i}", $"      Unlocked: {unlocks[i]}", 16, MutedColor);
            }

            var regions = DailySummaryStore.RegionsUnlocked;
            for (int i = 0; i < regions.Count; i++)
                AddLine(list, $"TNSSummaryRegion{i}", $"New region:  {regions[i]}", 20, AccentColor);

            // Vanilla's popup can only show ProductEntries.Length items and silently hides the
            // rest. This list scrolls, so show everything — that is the point of the app.
            var items = DailySummaryStore.Items;
            if (items.Count == 0)
            {
                AddLine(list, "TNSSummaryNoSales", "No sales recorded.", 18, MutedColor);
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                    AddItemRow(list, i, items[i]);
            }

            AddLine(list, "TNSSummaryPlayer",
                $"You earned  {DailySummaryStore.FormatMoney(DailySummaryStore.MoneyEarnedByPlayer)}", 20, AccentColor);
            AddLine(list, "TNSSummaryDealers",
                $"Dealers earned  {DailySummaryStore.FormatMoney(DailySummaryStore.MoneyEarnedByDealers)}", 20, AccentColor);
            AddLine(list, "TNSSummaryXP", $"{DailySummaryStore.XpGained} XP", 20, AccentColor);
        }

        /// <summary>
        /// Places a UI element by anchor fraction and zeroes its offsets — the part UIFactory
        /// leaves undone for anything that is not fullAnchor. Pure Unity, so no #if.
        ///
        /// ALL FOUR ARGUMENTS ARE REQUIRED, DELIBERATELY. b24 had this as optional Vector2?
        /// parameters defaulting to a full stretch, and calling it to "fix the offsets" on an
        /// element that already had anchors silently REPLACED those anchors with (0,0)-(1,1). The
        /// header and the body both became full-screen, the body was created second so it painted
        /// over the header, and the header disappeared entirely. Nothing about the call site looked
        /// wrong. No defaults here means that mistake cannot be made again.
        /// </summary>
        private static void SetRect(GameObject go, float xMin, float yMin, float xMax, float yMax)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static string EmptyMessage()
        {
            var err = DailySummaryStore.CaptureError;
            if (!string.IsNullOrEmpty(err))
                return "Yesterday's summary could not be read.\n\n" + err;

            return "No summary yet.\n\nIt will appear here after your first night.";
        }

        private static void AddLine(Transform parent, string name, string text, int size, Color color)
        {
            var label = UIFactory.Text(name, text, parent, size, TextAnchor.MiddleLeft);
            if (label == null) return;
            label.color = color;

            // The scroll content's VerticalLayoutGroup has childControlHeight = true, so each row
            // has to be able to state a height. Text reports its own preferred height, but a
            // minimum keeps short rows from collapsing to nothing.
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.minHeight = size + 8;
        }

        private static void AddItemRow(Transform parent, int index, DailySummaryStore.SoldItem item)
        {
            // Resolution can fail (an item from a mod the player has since removed). Fall back to
            // the raw id rather than dropping the row — a line the player cannot identify is more
            // useful than a silently shorter list.
            DailySummaryStore.TryGetItemDisplay(item.Id, out var displayName, out _);
            AddLine(parent, $"TNSSummaryItem{index}", $"      {item.Count}x   {displayName}", 18, Color.white);
        }

        /// <summary>
        /// Loads the icon embedded in this assembly. Cosmetic only — a failure here leaves the
        /// cloned prefab's default icon and is logged at debug, never escalated.
        /// </summary>
        private void TrySetEmbeddedIcon()
        {
            try
            {
                var asm = typeof(DailySummaryApp).Assembly;
                using (var stream = asm.GetManifestResourceStream(IconResourceName))
                {
                    if (stream == null)
                    {
                        TNSLog.Debug($"Embedded icon '{IconResourceName}' not found; using the default app icon.");
                        return;
                    }

                    var bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes)) SetIconTexture(tex);
                    else TNSLog.Debug("Embedded icon failed to decode; using the default app icon.");
                }
            }
            catch (Exception ex)
            {
                TNSLog.Debug($"Could not apply the embedded app icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Last resort when BuildUI threw. Shows the player why the app is blank instead of leaving
        /// an empty rectangle. Swallows its own failure — there is nothing sensible left to do.
        /// </summary>
        private static void TryShowFatalPanel(GameObject container, Exception cause)
        {
            try
            {
                var panel = UIFactory.Panel("TNSSummaryError", container.transform, PanelColor, fullAnchor: true);
                var text = UIFactory.Text("TNSSummaryErrorText",
                    "Daily Summary could not be displayed.\n\n" +
                    cause.GetType().Name + ": " + cause.Message +
                    "\n\nThe rest of Time Never Stops is unaffected.\nCheck MelonLoader\\Latest.log for detail.",
                    panel.transform, 16, TextAnchor.MiddleCenter);
                SetRect(text.gameObject, 0.06f, 0.1f, 0.94f, 0.9f);
            }
            catch
            {
                // Deliberately empty: we are already in the failure path.
            }
        }
    }
}
