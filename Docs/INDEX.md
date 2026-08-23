# Docs Index

**Purpose: tell a session, in one read, which docs are LIVE work and which are closed reference — before it spends context opening the wrong one.** Created 2026-08-23 (board b1).

**Maintenance rule:** when a doc's status changes, update its row here in the same batch. A row that lies is worse than no row.

**When a plan doc is done, does it MOVE?** Usually **no — reclassify it here instead of relocating it.** Done entries and board-history rows point at plans by path, and moving one breaks those pointers — the exact failure class this index exists to kill. Move a plan to `Docs\Plans\Archive\` only when it is genuinely DEAD: it tells you nothing you cannot get from the code or its Done entry. A closed design that is still consulted is not dead — mark it CLOSED and leave it where it is.

---

## Start here

| File | What it is |
|---|---|
| `CLAUDE.md` (repo root) | **Law.** Read first, every session. Architecture, hard rules, do-not-relearn traps. Written 2026-08-23 (board b2). |
| `Docs\Session_Board.md` | **The multi-session rulebook AND the live lock table.** Read before your first file read/edit. Also carries *Open defects* and *Pending manual steps*. |
| `ToDo.md` (repo root) | The live backlog — **OPEN ITEMS ONLY**. Seeded 2026-08-23. |
| `CHANGELOG.md` (repo root) | Per-release player-facing record. Newest release at the top. Keep-a-Changelog headers (`### Added` / `### Changed` / `### Fixed`). |
| `README.md` (repo root) | **Player-facing.** Features, config keys, install instructions, the Mono-vs-IL2CPP explainer. Marketing + user manual, not architecture, and it is allowed to lag the code. |
| `Docs\Fast_Forwarding_A_Save.md` | **Test-setup reference.** How to get a save *further along* fast: enabling the game's own console (host-only, plus a settings toggle), the commands that actually move a save forward, and why `DaySpeedMultiplier=100` backfires (a full save every ~14 seconds). |

## History (nothing here is open work)

| File | Covers |
|---|---|
| `Docs\Archive\Done\Done_YYYY-MM.md` | The dated Done log, one file per month, 2026-08 onward. **New entries append to the current month's file, at the top.** This is the engineering record — what was measured and why a call was made. `CHANGELOG.md` is the player-facing version of the same events. |
| `Docs\Archive\Session_Board_History.md` | Released claim rows moved off the board verbatim, so the board stays under one read. |

## Plans

| File | Status |
|---|---|
| `Docs\Plans\README.md` | What belongs in this folder. |
| `Docs\Plans\Daily_Summary_Phone_App.md` | **CLOSED** — the Daily Summary phone app, **implemented in b23**. Kept, not deleted: sections 1-4 document game behaviour that outlives the feature (the IL2CPP field→property rule, why `ClearStats` is the capture point, and that `DailySummary`'s `Add*` methods are ObserversRpc writers that tally host-only if patched). Its header lists what changed between design and code. |

## Not docs, but a session will need them

| Path | What it is |
|---|---|
| `D:\Projects\Schedule 1\References\` | **Decompiled game source. OUTSIDE this repo, shared by every Schedule I project on the disk — never copy it in.** As of 2026-08-23 a **full decompile of `Assembly-CSharp` + `ScheduleOne.Core` for both arms** (~2,200 files each). Path mirrors the namespace: `References\{MONO\Assembly-CSharp\ScheduleOne, IL2CPP\Assembly-CSharp\Il2CppScheduleOne}\<Namespace>\<Type>.cs`. **Read the MONO side for logic; IL2CPP files are interop stubs that prove names/signatures/visibility only.** Check it before guessing at any private game member — see `CLAUDE.md`, *Reference sources*. |
| `Version.props` | **Single source of truth for the version** (`<ModVersion>`). `build-pack.ps1` rewrites `versionNumber` in both `packaging\*.toml` to match it — so a build EDITS TRACKED FILES. `CHANGELOG.md`'s heading is the one copy nothing syncs. |
| `packaging\thunderstore_il2cpp.toml`, `packaging\thunderstore_mono.toml` | The two Thunderstore packages (`TimeNeverStops_IL2Cpp`, `TimeNeverStops_Mono`), same namespace `DropDaDeuce`. |
| `packaging\nexus\README.md` | **The Nexus archive: why its layout differs, what the pack script refuses to do, and what the upload form needs.** Read before changing anything under `packaging\nexus\`. Added 2026-08-23 (b3). The mod page is `https://www.nexusmods.com/schedule1/mods/1141`. |
| `packaging\nexus\Nexus_Description.bbcode` | The Nexus page body. **Paste as BBCode; Nexus does not render Markdown.** The Thunderstore equivalent is `README.md`. |
| `run-build.ps1` / `build-pack.ps1` / `run-build.bat` | The build + pack chain. **`run-build.ps1 -Publish` runs `tcli publish` — that is a real, irreversible upload.** See the board's rule 5. |
| `TNS_Project\Time Never Stops.csproj` | Two configurations, `MONO` and `IL2CPP`, each pointed at a **different game install** through `$(S1Dir)`. **`$(S1Dir)` was fixed in b8** — a gitignored `Directory.Build.props` at the repo root sets it per configuration (copy `Directory.Build.props.example`); `-p:S1Dir=` overrides both. The board's *Open defects* is empty. |
