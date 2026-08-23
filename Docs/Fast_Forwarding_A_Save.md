# Fast-forwarding a save for testing

**Why this file exists:** several checks in `ToDo.md` need a save that is *further along* than a fresh
start — a busy day with real item rows in the summary app, an actual rank-up, a region unlock, a
story beat that queues a sleep message. Burning in-game days does not produce any of that on its
own. This is the shortest route to each, written down once so it is not re-derived.

Everything here is read out of `References\MONO\Assembly-CSharp\ScheduleOne\Console.cs` and
`UI\ConsoleUI.cs` at 0.4.6f13. **None of it is a mod feature** — it is the game's own console.

---

## Turning the console on

`ConsoleUI.IsConsoleEnabled` is:

```csharp
GameManager.Instance.Settings.ConsoleEnabled && InstanceFinder.IsServer || Application.isEditor
```

So in a retail build you need **both**:

1. **`ConsoleEnabled` switched on in the game's own settings menu**, and
2. **to be the host.** A client cannot use the console — `Console.SubmitCommand` re-checks
   `InstanceFinder.IsHost` before dispatching, so a client typing a command gets nothing.

The toggle key is an `InputActionReference` (`ToggleConsoleReference`), i.e. a **rebindable binding,
not a hardcoded key** — look it up in the game's controls settings rather than trusting a
remembered default. The console also refuses to open while the pause menu is up.

Command history is Up/Down arrow. `bind t 'settime 1200'` binds a command to a key, `unbind t` /
`clearbinds` undo it.

---

## The commands that actually move a save forward

The full list is 62 words long; these are the ones that matter for the checks we have open.

| Goal | Command | Notes |
|---|---|---|
| Money | `changecash 5000` / `changebalance 5000` | cash on hand vs. online balance |
| Rank | `addxp 100` | **this is what produces a rank-up line in the summary app** |
| Property / business | `setowned barn`, `setowned laundromat` | |
| Employees | `addemployee botanist barn` | needed for the "employees want paying again" check |
| Region unlock | `setregionunlocked downtown` | **produces the `New region:` line** |
| Items | `give ogkush 5`, `setquality heavenly`, `setquantity 5` | |
| Plants | `growplants` | sets **every** plant in the world fully grown |
| NPCs | `setunlocked <npc_id>`, `setrelationship <npc_id> 5`, `setdiscovered ogkush` | |
| Story | `setqueststate <quest> <state>`, `setquestentrystate <quest> <entry> <state>`, `endtutorial` | |
| Story variables | `setvar <variable> <value>` | e.g. `Days_Since_Tutorial_Completed`, `Sleep_Count` |
| Time | `settime 1530` | |
| Save | `save` | writes immediately rather than waiting for the nightly save |

`forcesleep` exists too — useful for triggering a night without waiting for 07:00.

---

## Burning days: use the mod, not `setdayduration`

**Use `DaySpeedMultiplier` in `TimeNeverStops.cfg`.** It is the mod's own knob, it hot-reloads
within a second, and it is what the host pushes to clients in a real lobby.

Do **not** reach for the console's `setdayduration` or `settimescale` for this. The mod enforces its
multiplier onto `TimeManager` every tick, so the two will fight and the log will not tell you which
one won.

**Pick a moderate multiplier, and here is the arithmetic for why.** `TimeManager.CycleDuration`
defaults to **24 real minutes per in-game day**, so:

| `DaySpeedMultiplier` | Real time per in-game day |
|---|---|
| 1 | 24 min |
| 5 | ~4.8 min |
| 10 | ~2.4 min |
| 100 | **~14 seconds** |

**At 100 the game writes a full save every ~14 seconds.** That save is
`SaveManager.SaveRoutine`'s unyielded first loop — a synchronous main-thread stall of a second or
more on a large save — so a large fraction of wall-clock is spent frozen, and the summary app
rebuilds constantly. **5–10 is the useful range.** Days are also the *slowest* way to make a save
look "further along"; the table above is faster for everything except elapsed-day count itself.

---

## What a fast-forwarded save is still no good for

* **Anything needing a modless or mixed-config second player.** See `ToDo.md` and *Pending manual
  steps* 6.
* **The lobby-data sync (`__tns_*`).** That needs a real Steam lobby, so a local same-machine
  session (`EnableLocalMultiplayer`, Mono only) cannot exercise it no matter how far along the save is.
