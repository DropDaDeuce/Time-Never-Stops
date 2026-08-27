# Docs\Plans

**Ephemeral design docs.** A plan lives here while its work is open and is thrown away — or marked DEAD in `Docs\INDEX.md` — once what it describes is in the code and its findings are in a `Done_YYYY-MM.md` entry.

Write one when a change is big enough that the design needs to survive a session boundary: a new patch surface (a `TimeManager` method nobody has hooked yet), a subsystem rework (the sleep and daily-summary flow), a game-update recovery pass, a release checklist. Do not write one for a change you are about to make in the same session.

**Rules:**

* Give the doc a status header on line 1 — `LIVE`, `CLOSED`, or `DEAD` — and add its row to `Docs\INDEX.md` in the same batch. A plan nobody can find is worse than no plan.
* **When the plan and the code disagree, the code wins and the plan is the bug.** Say so in the plan rather than quietly leaving both versions readable — a superseded section that still reads as current is how a later session builds the wrong thing.
* **Say which build arm a finding applies to.** MONO and IL2CPP are one codebase behind `#if` and two reference sets; "the field is called X" is only half a fact here. Where a name differs between arms, write both.
* Durable findings (why a design was chosen, what was measured, what nearly went wrong) belong in the month's Done entry, **not** only here. This folder is disposable; the archive is not.
* A closed design that people still consult — the answer to "why does it work this way" — is not DEAD. Mark it CLOSED and leave it in place; other docs point at it by path.

Anything that stays relevant after its work ships is not a plan. Put it in `Docs\` proper, or in `CLAUDE.md` if it is law.
