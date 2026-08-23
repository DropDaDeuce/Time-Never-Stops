**Status: CLOSED** — designed 2026-08-23 (b15), **implemented and built on both arms in b23**. Kept because it is the record of *why* the capture works the way it does, and because sections 1-4 document game behaviour that will outlive this feature. **Where this plan and the code disagree, the code wins.**

**What changed on the way from design to code, all of it caught by the decompile:**

* **Section 2's "one `#if`" was wrong — the split is bigger and it is NOT symmetrical.** On MONO `itemsSoldByPlayer` / `moneyEarnedByPlayer` / `moneyEarnedByDealers` are genuinely private FIELDS and need `AccessTools.Field`; on IL2CPP they are generated public PROPERTIES and compile as direct member access. Neither form compiles on the other arm. `xpGained` needs no split.
* **`Registry` is in the ROOT `ScheduleOne` namespace, not `ScheduleOne.ItemFramework`**, and `ItemDefinition.Name` / `.Icon` are public fields inherited from `ScheduleOne.Core.Items.Framework.BaseItemDefinition` — which meant adding a `ScheduleOne.Core` / `Il2CppScheduleOne.Core` reference to the csproj that had never been there.
* **The icon is embedded, not loaded from `Mods\`.** `IconFileName` would have logged an Error every load for a file we do not ship.
* **A third file appeared: `S1APIBridge.cs`.** Section 5 said "isolate it"; that is what isolation actually took.

# Daily Summary as a phone app

Replace the 06:59 sleep trigger's *visible* half — the vanilla Daily Summary popup — with a
phone app that shows the previous day's numbers on demand. **The sleep flow itself stays.**
It drives `onSleepStart`/`onSleepEnd`, `SkipForwardToTime`, the `Sleep_Count` variable, the
daily `SaveManager.Save()` and the queued `IPostSleepEvent` list (`RankUpCanvas`,
`RegionUnlockedCanvas`). None of that is optional and none of it is what this plan touches.

The mod already has the "no popup" half: `EnableDailySummaryAwake=false` suppresses
`DailySummary.Open`, `RankUpCanvas.StartEvent` and `RegionUnlockedCanvas.StartEvent` while the
sleep machinery keeps running. **The missing piece is only a way to VIEW the numbers later.**

Decision: **capture the values ourselves (no dependency), render them with S1API's `PhoneApp`.**

---

## 1. The finding that changes the design — IL2CPP does not lose the fields

**This corrects the parity rule in `CLAUDE.md`, which is right about the symptom and wrong
about the cause.** `CLAUDE.md` says Il2CppInterop "does not expose instance fields of native
objects as .NET fields", and concludes the values are unreachable on IL2CPP. The first half is
true. The conclusion is not.

**Il2CppInterop does not drop those fields — it regenerates them as PROPERTIES** backed by a
native field offset held in a generated `NativeFieldInfoPtr_<name>` static. So
`AccessTools.Field()` / `Type.GetField()` return null on IL2CPP (which is exactly why
`TMAccess.SetTimeOnCurrentMinute` and `SetTimeSpeedMultiplierDirect` are no-ops there), **but
the data is reachable** — by direct member access under `#if IL2CPP`, or reflectively through
`AccessTools.PropertyGetter` / `PropertySetter`.

Measured 2026-08-23 by metadata-string presence in the two real assemblies on this disk:

* MONO — `Schedule I Mono\Schedule I_Data\Managed\Assembly-CSharp.dll` (4,222,464 bytes)
* IL2CPP — `Schedule I IL2CPP\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll` (13,342,208 bytes)

| Symbol | MONO | IL2CPP | Reading |
|---|---|---|---|
| `itemsSoldByPlayer` | present | present | |
| `get_itemsSoldByPlayer` | **absent** | **present** | field on MONO, property on IL2CPP |
| `NativeFieldInfoPtr_itemsSoldByPlayer` | absent | **present** | the generated offset holder |
| `get_` / `set_moneyEarnedByPlayer` | **absent** | **present** | same conversion |
| `_secondsOnCurrentMinute` | present | present | |
| `get__secondsOnCurrentMinute` | **absent** | **present** | same conversion — see section 7 |
| `<TimeSpeedMultiplier>k__BackingField` | present | see below | **renamed**, not removed — corrected 2026-08-23 |
| `set_TimeSpeedMultiplier` | present | present | property setter exists on BOTH arms |

**CONFIRMED FROM DECOMPILED SOURCE 2026-08-23** — Mathew added both arms of `TimeManager` to
`D:\Projects\Schedule 1\References\`, which upgrades everything above from inference to proof
and **corrects one line of it.** The IL2CPP wrapper reads:

```csharp
public unsafe float _secondsOnCurrentMinute          // getter AND setter, native offset
public unsafe float _TimeSpeedMultiplier_k__BackingField
public unsafe int   _CurrentTime_k__BackingField
public unsafe int   _ElapsedDays_k__BackingField
public unsafe int   _DailyMinSum_k__BackingField
public unsafe bool  _IsSleepInProgress_k__BackingField
public unsafe bool  _HostSleepDone_k__BackingField
```

**The correction:** the backing field is **not** "sanitised away" on IL2CPP, as the table row
originally read. It is **renamed** — `<Name>k__BackingField` becomes `_Name_k__BackingField`.
The earlier metadata grep missed it because the literal `"<TimeSpeedMultiplier>k__BackingField"`
survives only as a UTF-16 *user string* (the argument to `GetIl2CppField`), while member names
live in the UTF-8 heap. The conclusion was right, the stated reason was half wrong.

**The rule, now provable:** Il2CppInterop regenerates **every** native instance field as a
`public unsafe` property with a getter and a setter. `AccessTools.Field()` finds nothing —
that, and only that, is why the two `TMAccess` writes no-op. Ordinary fields keep their name;
auto-property backing fields gain the `_Name_k__BackingField` form. **Anything that must work
on IL2CPP must not reach for a FIELD by reflection.**

Corroborating: S1API's `PhoneApp.CreateAppIcon` reads the private `HomeScreen.appIconPrefab`
via `AccessTools.Field` on Mono and plain `homeScreenInstance.appIconPrefab` on IL2CPP.

**Superseded 2026-08-23 (b20/b23):** this section once ended by warning that everything about
`DailySummary` in section 2 was still inferred from metadata strings. `References\` is now a full
decompile of both arms, section 2 was rewritten from it, and the feature was built against it —
which caught the postfix-tally design and the not-symmetrical `#if` before either shipped.

---

## 2. Capture design — one prefix, exactly once

The values die at sleep end: `DailySummary.SleepEnd()` calls `ClearStats()`, wired to
`TimeManager.onSleepEnd`. Anything shown later must be captured before that.

**Capture point: a Harmony PREFIX on `DailySummary.ClearStats`.** It fires exactly once,
immediately before the data is destroyed, no matter which path got there. That deliberately
sidesteps the three-independent-paths-to-sleep-end race that `TryRunSleepEndCleanup` and the
`_sleepEndCleanupDone` one-shot exist to tame — we do not add a fourth racer. Harmony patches
private methods by string name, so `ClearStats`'s visibility does not matter.

Read the three values plus XP, both arms:

```
MONO    ScheduleOne.UI.DailySummary        AccessTools.Field(...)       -> works today
IL2CPP  Il2CppScheduleOne.UI.DailySummary  __instance.itemsSoldByPlayer -> generated property
```

`TNS_Project\Core.cs` already aliases `UI` to `ScheduleOne.UI` / `Il2CppScheduleOne.UI`, so the
patch attribute is arm-neutral; only the value read needs the `#if`.

**CONFIRMED FROM SOURCE 2026-08-23 (b20)** — `References\` now holds `DailySummary` for both
arms. Everything below is read off the decompile, not inferred.

| Member | MONO | IL2CPP |
|---|---|---|
| `itemsSoldByPlayer` | `private Dictionary<string,int>` | `public unsafe Il2CppSystem.Collections.Generic.Dictionary<string,int>` |
| `moneyEarnedByPlayer` | `private float` | `public unsafe float` |
| `moneyEarnedByDealers` | `private float` | `public unsafe float` |
| `xpGained` | `public int { get; private set; }` | `public unsafe int` (+ `_xpGained_k__BackingField`) |
| `ClearStats()` | `private void` | `public unsafe void` |
| `SleepEnd()` | `private void` | `public unsafe void` |

Exactly the section 1 rule: the two ordinary private fields keep their names on IL2CPP, and the
auto-property backing field becomes `_xpGained_k__BackingField`.

**`ClearStats` is called from exactly one place** — `private void SleepEnd() => this.ClearStats();`,
wired once in `Start()` as `NetworkSingleton<TimeManager>.Instance.onSleepEnd += new Action(this.SleepEnd)`.
So the prefix really does fire once per night, and nothing else in the class clears state.

**THE ONE `#if` THE CAPTURE NEEDS:** the dictionary is a different *type* per arm —
`System.Collections.Generic.Dictionary<string,int>` on MONO,
**`Il2CppSystem.Collections.Generic.Dictionary<string,int>`** on IL2CPP. Copy it into our own
plain `Dictionary<string,int>` (or a `List<(string,int)>`) inside the `#if`; do not try to hold
a reference to the game's, which is cleared microseconds later. The two floats and the int need
no `#if` at all.

### The postfix-tally fallback was WRONG, and would have shipped broken in multiplayer

The original brief proposed tallying by postfixing the public `AddSoldItem` / `AddPlayerMoney` /
`AddDealerMoney` / `AddXP`. **Do not do this.** Those methods are `[ObserversRpc]` **writers**,
not accumulators — the public method body is nothing but the send:

```csharp
[ObserversRpc]
public void AddSoldItem(string id, int amount) => this.RpcWriter___Observers_AddSoldItem_3643459082(id, amount);
```

`RpcWriter___` bails with a warning unless `IsServerInitialized`. **The accumulation happens in
`RpcLogic___AddSoldItem_3643459082`**, which the RpcReader calls on every client. So postfixing
the public method would have tallied **on the host only** and produced an empty summary for
every client — a bug that a solo test could never surface (board rule 11). If a tally is ever
needed, the correct targets are the `RpcLogic___Add*_<hash>` methods, which are `public` on both
arms — but they carry the same FishNet hash fragility as the main patch, so **direct read
remains the better design.**

### Multiplayer — open question 6.3 is ANSWERED

The values are broadcast to all observers and accumulated identically on every client, so
**each player's `DailySummary` holds the same totals**, and a local read gives exactly what that
player's popup would have shown. **`itemsSoldByPlayer` is not per-player despite the name** —
`moneyEarnedByPlayer` vs `moneyEarnedByDealers` is the *sales channel* (sold by you vs sold by
your dealers), not player identity. Nothing here needs host/client branching.

**Failure behaviour, as Mathew asked for:** if the lookup returns null or the read throws, set
a flag and have the app render "summary unavailable" rather than zeroes. Zeroes are
indistinguishable from a genuinely quiet day and would be read as a data bug.

**Persistence is an open question — see section 6.**

---

## 3. Does S1API conflict with us? No — zero overlapping patch targets

Checked 2026-08-23 against all 34 files in `S1API/Internal/Patches/` at `ifBars/S1API@stable`
(~90 patched methods across Building, NPCs, Economy, Quests, Products, Persistence, Casino,
Vehicles, Phone/TV apps).

**S1API patches none of TNS's targets:** not `TimeManager.RpcLogic___PassMinute_Client_3316948804`,
not `SkipForwardToTime`, not `StartSleep`/`SetHostSleepDone`, not `DailySummary.Open`/`.Close`,
not `RankUpCanvas.StartEvent`, not `RegionUnlockedCanvas.StartEvent`, not `SleepCanvas.SleepStart`,
not `HUD.Update`. It never touches `DailySummary` at all.

S1API is `[MelonPriority(Int32.MinValue)]` so it loads first, but with no shared targets that is
moot.

*Scope:* this covers `S1API/Internal/Patches/`, S1API's convention and the only directory
holding patch files. A stray `[HarmonyPatch]` elsewhere in its other ~813 source files cannot be
ruled out without code search.

---

## 4. Two real interactions — one-way, and they exist ALREADY

Neither crashes anything. In both, **TNS degrades data S1API hands to other mods.** S1API has
~96k downloads, so any player running both already has these — adopting the dependency does not
create them, it just means we own them knowingly.

**(a) Suppressing `SkipForwardToTime` breaks S1API's sleep-duration number.**
`Patch_TimeManager_SkipForwardToTime.Prefix` returns `false` when `skipFlag || pastWake`.
Vanilla `SkipForwardToTime` is what fires `onTimeSkip`. S1API caches that into
`_lastSleepSkippedMinutes` and replays it as `OnSleepEnd(int minutes)`. Suppress the original
and S1API reports the **previous** night's figure — silently wrong, not zero.
*Fix:* fire `onTimeSkip` ourselves when we suppress. Cheap, and correct regardless of adoption.

**(b) `onTick` — ANSWERED 2026-08-23, and it is a NON-ISSUE.** The worry was that our
`PassMinute` replacement re-invokes `onHourPass`, `onDayPass`, `onWeekPass`, `onMinutePass` and
`onUncappedMinutePass` but not `onTick`, which S1API subscribes to — so S1API-driven NPCs,
quests and schedules might stop ticking inside the bypass window.

**They do not.** The decompile shows `onTick` is invoked from **`TickLoop()`**, a separate
coroutine on a fixed 0.5s realtime cadence gated only on `Time.timeScale`:

```csharp
private IEnumerator TickLoop() {
  ...
  timeManager.onTick.InvokeAllStaggered(timeManager._tickStaggerTime);   // _tickStaggerTime => 0.45f
}
```

`PassMinute` is driven by `TimeLoop()`, a different coroutine, and **never touches `onTick`.**
Our prefix cannot starve it. **No fix needed, and nothing to add to the transcription.**

While the decompile was open, the transcription itself was diffed against vanilla
`RpcLogic___PassMinute_Client_3316948804` and **it is correct**: the branch order, the
`onHourPass`-before-`onDayPass` rollover ordering, the `+41` hour carry and the Monday
`onWeekPass` test all match, and `staggerTime` is exactly vanilla's
`_minuteStaggerTime => MinuteDuration / (Time.timeScale * 0.9f)`. Two deliberate divergences,
both benign: the mod zeroes `_secondsOnCurrentMinute` *before* the branch where vanilla does it
after (nothing reads it in between), and it sets `HasChanged = true`, which vanilla's
`PassMinute` does not. **The RPC hash `3316948804` is still live on BOTH arms at 0.4.6f13.**

---

## 4b. What to render — vanilla `Open()` is the spec, and it truncates

Mathew's ask was that the app "mimic how the UI is displayed in the first place." Vanilla
`DailySummary.Open()` is that spec, verbatim:

* **Title** — `$"{TimeManager.Instance.CurrentDay}, Day {TimeManager.Instance.ElapsedDays + 1}"`.
  Note the `+ 1`. Capture the day/date at `ClearStats` time too, or the app will label
  yesterday's numbers with today's date.
* **Each sold item** — `Registry.GetItem(id)` returns an `ItemDefinition` carrying `.Icon`
  (a `Sprite`, feeds straight into a `UIFactory` image) and `.Name`. Quantity renders as
  `$"{count}x"`. **Store the raw `id` string, not the resolved name** — resolve at display time,
  so a captured summary survives a mod/registry change.
* **Both money figures** — `MoneyManager.FormatAmount(float)`, `public static`, available on
  both arms. Use it rather than rolling a currency format, or the app will not match the game.
* **XP** — `$"{xpGained} XP"`.

**Vanilla TRUNCATES the item list.** `ProductEntries` is a fixed-length serialized array; `Open()`
fills `ProductEntries.Length` entries and `SetActive(false)`s the rest, so a busy day silently
drops items off the popup. **The phone app has no such limit** — a scrollable list
(`UIFactory.ScrollableVerticalList`) can show everything. That is a genuine improvement over the
thing being replaced, and worth doing deliberately rather than reproducing the cap.

Also worth knowing for the sleep-end UI work: `Open()` calls
`PlayerCamera.AddActiveUIElement(this.name)` and `State.PushToDefaultParent()`, and `Close()`
reverses both. When `EnableDailySummaryAwake=false` suppresses `Open` via the existing prefix,
neither ever runs, so nothing leaks into `ActiveUIElements` — consistent with what b16 measured.

## 5. The phone app

`S1API.PhoneApp.PhoneApp`, verified by reading the 752-line source, not the docs.

Registration is **automatic**: a `HomeScreen.Start` postfix calls
`ReflectionUtils.GetDerivedClasses<PhoneApp>()`, instantiates every public subclass having a
parameterless constructor, then calls `SpawnUI` / `SpawnIcon`. **No registration code.**

Contract — 4 abstract properties + one method:

```csharp
protected abstract string AppName      { get; }   // unique key, also the GameObject name
protected abstract string AppTitle     { get; }   // display title
protected abstract string IconLabel    { get; }   // text under the home-screen icon
protected abstract string IconFileName { get; }   // png under MelonEnvironment.ModsDirectory
protected abstract void OnCreatedUI(GameObject container);
```

Optional: `IconSprite`, `Orientation` (`Horizontal`/`Vertical`), `OnPhoneClosed()`,
`Exit(S1API.PhoneApp.ExitAction)`, plus `SetIconSprite` / `SetIconTexture`.

**This class needs no `#if`.** It touches only S1API and UnityEngine types, and Unity types are
*not* `Il2Cpp`-prefixed — only game assemblies are. It would be the first file in this repo
that is arm-neutral. `Core.cs` still keeps its alias block, because the `ClearStats` patch in
section 2 targets a game type.

**Two traps:**

* **`S1API/PhoneApp/readme.md` is STALE.** It tells you to call `PhoneAppManager.Register(...)`
  and override `OnCreated(GameObject)`. `PhoneAppManager` is not a type and the real hook is
  `OnCreatedUI`. `MyAwesomeApp.cs` is entirely commented out. **Use `S1API/docs/phone-app.md`
  or ifbars.github.io**, both of which match the source.
* **The instance is re-created on every `HomeScreen.Start`.** Keep the captured summary in a
  static on our side; the `PhoneApp` subclass must be a view over it, holding no state.

**Isolate it.** A missing `SteamNetworkLib` fails soft — `TryInitTimeSync` catches the type-load
failure and single-player is unaffected. A missing S1API is a `TypeLoadException` on the
subclass *at type load*, which is not catchable the same way. The app class must sit in its own
file reachable only behind a guarded entry point.

---

## 6. Open questions

1. ~~**Does vanilla `PassMinute` invoke `onTick`?**~~ **ANSWERED 2026-08-23 — no. Non-issue,
   see section 4(b).** `onTick` comes from `TickLoop()`, a separate 0.5s coroutine.
2. **Where does yesterday's summary persist?** The summary is **save-specific, not global**, so
   the mod's `UserData` config file is the wrong home. `S1API.Saveables.SaveableField` ties data
   to the game save and is the right shape — but it deepens the dependency. Decide before
   implementing. A first cut can hold it in memory only and simply show nothing after a reload.
3. ~~**Multiplayer — does a client's instance carry its own numbers or the host's?**~~
   **ANSWERED 2026-08-23 — every client accumulates the same broadcast totals, so a local read
   is correct everywhere and needs no host/client branching.** See section 2. A two-machine
   round is still owed to confirm the *app* behaves, but the data question is closed.
4. ~~**Do the IL2CPP generated properties compile as `public`?**~~ **ANSWERED 2026-08-23 — yes,
   `public unsafe`, getter and setter, confirmed in the decompile.** See section 1.
5. ~~**Is `DailySummary` shaped the way section 2 assumes?**~~ **ANSWERED 2026-08-23 — Mathew
   added both arms to `References\`. Section 2 is now read off source, and it caught a real
   error in the fallback design.** Nothing in this plan rests on metadata strings any more.

**Only 6.2 (persistence) is genuinely still open.** Everything else here is either answered or
an in-game confirmation that cannot be done from a session.

---

## 7. Adjacent — BOTH `TMAccess` no-ops are fixable, confirmed from source

Not this plan's job, but it falls out of section 1 and must not be re-derived. **Tracked as
`ToDo.md` item 15.**

Both entries under *Known-broken* in `CLAUDE.md` were right that the writes no-op and wrong
about why. The data was never unreachable — the code just asks for a **field** and IL2CPP has a
**property**. From the decompile:

| Currently no-ops on IL2CPP | Because it looks for | IL2CPP actually exposes |
|---|---|---|
| `SetTimeOnCurrentMinute` | field `_secondsOnCurrentMinute` | `public unsafe float _secondsOnCurrentMinute { get; set; }` |
| `SetTimeSpeedMultiplierDirect` | field `<TimeSpeedMultiplier>k__BackingField` | `public unsafe float _TimeSpeedMultiplier_k__BackingField { get; set; }` |

Three viable fixes, cheapest first:

1. **`#if IL2CPP` direct member access** — `__instance._secondsOnCurrentMinute = 0f;`. Compiles
   to a native-offset write, no reflection, cannot silently resolve to null.
2. **`AccessTools.PropertySetter`** on the generated property name — keeps `TMAccess`'s existing
   delegate-building shape.
3. **The `TimeSpeedMultiplier` property setter itself.** The decompile shows
   `set_TimeSpeedMultiplier` is `private set` on MONO and surfaced by the interop wrapper on
   IL2CPP, and it is **distinct from the `SetTimeSpeedMultiplier(float)` method that carries the
   server-only guard** — so it sidesteps the guard without touching a backing field at all.
   Likely the cleanest of the three; verify the private setter has no guard of its own.

**Still needs a compile and an in-game log**, and per board rule 11 the multiplier-sync half is
a two-machine claim. Also noted: `SetTimeAndSync` is a public method on both arms and may be a
better route for `TimeSyncManager`'s clock writes than the property setter it uses now.

---

## 8. What adopting S1API costs, and what else is in it

**Package.** `ifBars-S1API_Forked`, latest **3.2.0**, Thunderstore updated 2026-08-16, **95,964
downloads**. Its only declared dependency is `LavaGang-MelonLoader-0.7.2` — we already require
0.7.3, so nothing new is pulled in.

**One package covers both arms.** `S1APILoader` is a `MelonPlugin` that checks
`MelonUtils.IsGameIl2Cpp()` at `OnApplicationEarlyStart` and enables
`S1API.{Mono|Il2Cpp}.MelonLoader.dll` while `.disabled`-ing the other. So **both tomls declare
the identical string** — unlike `SteamNetworkLib`, which is split per arm. Nexus needs the line
added by hand to `Nexus_Description.bbcode` and `MANUAL-INSTALL.txt`.

**Build reference: NuGet `S1API.Forked`,** netstandard2.1, transitive `Newtonsoft.Json 13.0.2`
(MelonLoader already ships it — use `ExcludeAssets="runtime"` so it does not land in
`Packages\`). netstandard2.1 matches the MONO arm exactly and net6.0 consumes it, so one
`PackageReference` serves both. **No vendored DLL** — which avoids repeating the
`Libs\SteamNetworkLib-*.dll` drift problem.

**Do NOT use `KaBooMa-S1API`.** The original is at 1.6.2, last updated 2025-04-29, and predates
0.4.6 entirely. The ifBars fork is the live one.

**Churn is the real cost.** Releases land near-daily, and 3.0.6 to 3.1.0 broke a public
signature: `Exit()` took `ScheduleOne.DevUtilities.ExitAction` until **Schedule I 0.4.6f11 moved
that native type**, forcing S1API to swap in its own wrapper. Pin the version and read the
release notes before bumping.

The flip side, and the reason to take it anyway: S1API shipped phone-app hotfixes for **0.4.6f11
and 0.4.6f12**, and 0.4.6 changed the app hierarchy from one child named `Container` to two. The
phone UI is the highest-churn surface in the game. Hand-rolling a prefab clone means ~750 lines
of private-UI reflection *and* inheriting that breakage stream permanently.

**What else is worth taking:**

| Module | Verdict |
|---|---|
| `PhoneApp` | Adopt — the point of this plan. |
| `UI.UIFactory`, `Utils.ButtonUtils`, `Utils.ImageUtils` | Adopt with it. `ButtonUtils.AddListener` handles the IL2CPP delegate conversion we would otherwise write. |
| `Saveables.SaveableField` | Candidate for section 6.2. |
| `GameTime.TimeManager` | **Skip.** Read-only plus `SetTime`. No `TimeSpeedMultiplier`, no `DailyMinSum`, no `IsSleepInProgress` write. Strictly weaker than `TMAccess`. |
| `Leveling.LevelManager` | Marginal. `OnXPChanged`/`OnRankUp` are cross-runtime, but we suppress the rank canvas rather than read it. |
| `Logging.Log`, `Input.Controls`, `Money` | Skip. We have `TNSLog`; the app icon needs no keybind; `Money` is cash balance and marked *in development*. |

---

## 9. Owed elsewhere

* ~~**`CLAUDE.md`'s IL2CPP parity note needs the section 1 correction.**~~ **DONE 2026-08-23
  (b19)** — rewritten from the decompile, with both naming rules and a pointer to `References\`.
* ~~**Board *Pending manual steps* 1 is stale.**~~ **WRONG — it was already struck through and
  marked DONE by b9.** Recorded here because b15 asserted otherwise: the claim was made from a
  truncated read of the board and should not have been made. The install state is fine.
* **The section 1 measurement belongs in `Docs\Archive\Done\Done_2026-08.md`** when this work
  ships — it is the "must not be re-derived" class and this folder is disposable.
* **`References\` is registered in `CLAUDE.md` and `Docs\INDEX.md`.** It holds `TimeManager`
  **and `DailySummary`** for both arms as of 2026-08-23 (b20). Still absent, and each one is a
  place this repo could guess wrong again: `SleepCanvas`, `RankUpCanvas`,
  `RegionUnlockedCanvas`, `PlayerCamera`, `HUD`.
* **The ObserversRpc finding in section 2 belongs in `CLAUDE.md`'s architecture notes** if the
  summary work grows beyond this plan. It is already in the *Reference sources* section as a
  warning, which is enough for now.
