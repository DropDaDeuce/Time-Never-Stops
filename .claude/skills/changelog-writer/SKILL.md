---
name: changelog-writer
description: Use ANY time a line is going into CHANGELOG.md - after finishing a batch that changes player-visible behaviour, adding a new config setting, fixing a bug, or writing up a release. Also use when asked to rewrite, tidy, shorten or re-voice an existing changelog entry. Carries the house voice, which is TERSE - one past-tense sentence per bullet, around nine words, no explanation of the mechanism - plus the punctuation rules that keep it from reading like AI output.
---

# Writing the changelog

> **Adopted from the sibling `LethalGargoyles` repo (2026-08-23, board b34) at Mathew's request.**
> The voice rules, the compression table and the punctuation bans are **verbatim** and carry over
> unchanged, because they are about writing, not about that mod. **Three things differ here and are
> marked `THIS REPO` below: the group headers, where a new entry goes, and the config system.**
> The worked examples are still the Lethal Company ones. They are kept deliberately: they are the
> clearest demonstration of the compression, and swapping in Schedule I examples would mean
> inventing them rather than quoting a file that really reads that way.

`CHANGELOG.md` is a mod-manager release note. A player reads it mid-scroll, deciding whether to hit update. **They are skimming a list, not reading an article.**

Every release from v0.0.1 to v0.7.0 is written that way, and they are the house style. Measured against the file on 2026-08-17, **after b43 rewrote the v0.8.0 entry**:

|  | v0.0.1 to v0.7.0 (143 bullets) | v0.8.0 as it stands now (46) | v0.8.0 as first written (35) |
|---|---|---|---|
| Median bullet | **8-9 words** | 12 words | 53 words |
| Longest bullet | 32 words | 20 words | 138 words |
| Median sentences | **1** | 1 | 3 |

**The whole file now reads in one voice, which was not true before 2026-08-17.** v0.8.0 was originally written as a post-mortem (right-hand column) and b43 cut it from 2,187 words to 625. That column is kept here as the failure mode to recognise, **not** as a description of the file as it is. Write to the left.

## The shape of a bullet

**One past-tense sentence, verb first.** The verbs the file actually uses, most frequent first: `Added`, `Fixed`, `Updated`, `Optimized`, `Improved`, `Adjusted`, `Reduced`, `Implemented`, `Removed`.

```
- Fixed the gargoyle's push doing nothing for anyone except the host.
- Improved the gargoyle's navigation so it can reach players more reliably and gets stuck less often.
- Reduced the default idle range from 30 to 20 to improve target following.
- Added 21 new voice lines for activity-based taunts.
```

**Hard limits:** one sentence, around 9 words, 25 at the absolute ceiling. A second short sentence only when the player has to **do** something (delete a config line, add a file, install a dependency). Never a third.

**What gets cut, every time:**

* **The mechanism.** "Its search was restarting itself every frame" is a commit message. The player cannot see a frame. If a maintainer needs the cause it lives in `Docs\Archive\Done\` and in the commit.
* **The feel forecast.** "Expect him to feel considerably more present." Let them find out.
* **The reassurance.** "It is the intended behaviour finally working." "This will feel like a different mod."
* **The measurement.** Frame budgets, route-solve counts, metres of ground covered. Say "Significantly improved performance", or give one number inside the sentence if it is a config value.
* **Full-sentence bold.** Bold is for section headers and for the sub-topic labels below, nothing else. Bolding a whole lead sentence appears nowhere before v0.8.0.

## Grouping

**`THIS REPO`: use the Keep-a-Changelog `###` headers, NOT the bold ones.** `Time Never Stops`
has used these since v1.0.0 and every released entry in the file is built from them:

```
### Added
### Changed
### Fixed
### Notes
```

`### Added` covers what LethalGargoyles calls New Features, `### Changed` covers Changes, and
`### Notes` is the same honesty section described below. **Do not import the bold headers just
because this skill came from a repo that uses them** - a 1.5.0 entry in a different shape from
1.4.0 is exactly the one-voice problem the rest of this document exists to prevent.

For reference, the LethalGargoyles set these map from:

```
**New Features:**
**Changes:**
**Bug Fixes:**
**Documentation:**
**Notes:**
```

Add a one-off header only when a whole cluster genuinely does not fit one of those. The file has done it four times in thirteen releases: `**Dependency Changes:**`, `**Game Parity:**`, `**AI Improvements:**`, `**Icon**`. **v0.8.0 invented five in one release** (Performance, Tuning, New Settings, Config File Layout, Game Compatibility) and that is header sprawl, not organisation. New settings belong under `**New Features:**`, tuning belongs under `**Changes:**`.

**Nest when a feature has several parts.** Bold sub-topic label, then 4-space-indented bullets:

```
**New Features:**

- **Push Attack:**
    - Added a new "PushTarget" attack pattern where the gargoyle attempts to push players off edges.
    - Implemented a new cause of death in Coroner for deaths caused by gargoyle pushes.
- **Configuration:**
    - Added a config option to enable or disable the "PushTarget" attack.
    - Nerfed default value for Aggro Range from 6 to 4.
```

Flat bullets with no nesting are also correct (v0.7.0). Match whichever the neighbouring sections use.

## Voice

* **Third person, past tense.** "the gargoyle", "the host", "players". Second person appears twice in the whole pre-0.8.0 file, both times for something the player physically does ("You can now add custom taunts by placing OGG files in..."). Use it for that and nothing else.
* **Lowercase "gargoyle"** when it is the creature, which is how most of the file reads.
* **Backticks are for code**, not for config paths in prose: `` `LethalGargoylesAI` ``, `` `PushTarget` ``, `` `Strings_en-us_gargoyle.xml` ``. Settings are named plainly: "Reduced the default idle range from 30 to 20."
* **Say the number when a default moved.** "from 6 to 4", "from 30 to 20".
* **`(default off)`** for a new setting that ships disabled. Say it in the bullet.
* **Thank people by name.** "Thank you Purple for the Gargoyle render used in the icon!"

## Where the entry goes

* **`THIS REPO`: there is no `## Unreleased` accumulator and no `ship-release` command.** The newest
  version heading at the top of the file IS the working entry, and it is written by hand in the shape
  `## [1.5.0] - Title - YYYY-MM-DD`. **`CLAUDE.md` calls this out specifically: the changelog heading is
  the one copy of the version that nothing syncs** - `Version.props` drives the tomls and
  `BuildVersion.Value`, but not this. Bump it yourself.
* **Not everything belongs here.** A refactor a player cannot observe, a doc fix, a board entry, a build-system change: those go in `Docs\Archive\Done\Done_YYYY-MM.md`. The test is literal: **could a player notice this without reading the source?**
* `CHANGELOG.md` is a single-writer shared doc under the board protocol. Claim it before editing it.
* **`THIS REPO`: the player-facing text exists in THREE more copies that drift** - `README.md`,
  `packaging\nexus\Nexus_Description.bbcode` and `packaging\nexus\MANUAL-INSTALL.txt`. A changelog
  bullet about a new setting or a changed default usually means all three want updating in the same
  batch. See the *Hard rules* section of `CLAUDE.md`.
* **`THIS REPO`: the packages EMBED `README.md` and the manual**, so touching them invalidates the
  built zips even when no code changed. Repack before publishing.

## The honesty items, and where they fit

These still have to be said. They do **not** get to bloat the bullets. Put them in `**Notes:**` at the bottom of the release, one line each, which is what v0.6.1 already does.

```
**Notes:**

- Not yet verified in a live round: the footstep volume fix and the freeze fixes.
- Existing config files keep their old taunt timers. Delete Min Taunt and Max Taunt to pick up the new defaults.
- Multiple gargoyles now spread across the crew instead of all chasing one player. This is intended.
```

* **Unverified in game.** Every session can build and none can play. If nobody has run it, say so.
  **`THIS REPO` has two arms and they are tested separately** - name which one, because "tested" that
  means Mono only is a different claim from "tested".
* **`THIS REPO`: MelonPreferences DOES add missing keys on next launch**, so a new setting appears in
  an existing `TimeNeverStops.cfg` by itself. What it does NOT do is change a value the player has
  already set. Say so only when a DEFAULT moved, not when a key was added.
* **A change that will look like a bug.** Say it, or the first report will be "the update broke targeting".

## Punctuation and phrasing bans

The default register of a language model is instantly recognisable, and a changelog that reads generated gets trusted less than one that reads written. The pre-0.8.0 file contains **zero** em dashes, en dashes, curly quotes and curly apostrophes. Keep it that way.

* **Em dashes and en dashes as sentence punctuation.** Split the sentence or use a comma. A hyphen inside a compound word or a range is fine.
* **Curly quotes and apostrophes.** Straight only.
* **Emoji, decorative bullets, arrows.**
* **The chiasmus.** "Less a fix, more a rewrite."
* **The rhetorical triad.** "Faster, quieter, and far less likely to get stuck."
* **LLM filler:** seamlessly, robust, leverage, utilize, comprehensive, streamlined, enhanced experience, "it's worth noting", "importantly", "significantly improved" with nothing behind it.
* **Announcing the writing.** "This update brings a number of improvements." Just list them.
* **Semicolons.** At most one per release, and probably zero. A one-sentence bullet does not need one.

## Compressing an over-written bullet

Strip it to the verb and the observable outcome. The left column below is real text from the **original** v0.8.0 entry; the right is what replaced it and is in the file today:

| v0.8.0 as first written | House style (what the file says now) |
|---|---|
| **The Gargoyle actually roams while he is searching for you.** His search was restarting itself every frame, so he never got far enough to pick anywhere to go and tended to loiter near where he spawned, only noticing you if you walked close to him. Expect him to feel considerably more present. | Fixed the gargoyle loitering near its spawn instead of roaming while searching. |
| Fixed stereo voice lines being cut off halfway through for everyone except the host. Clients decode the audio themselves and were reading stereo clips as if they were mono, so playback stopped at the halfway mark. Only one shipped line is stereo, but this also affects any custom line you add. | Fixed stereo voice lines being cut off halfway through for clients. Custom stereo lines are affected too. |
| Large frame-rate improvement, most noticeable with more than one Gargoyle alive. While sneaking or hiding, each of them was running a full route calculation plus line-of-sight checks against every navigation point in the interior, every single frame. | Significantly improved performance when multiple gargoyles are active. |
| `Pathfinding > Follow Players Through Exits` (**off by default**). Lets him use fire exits and the main entrance, so he can follow you between the facility and the surface. He has never been able to do this, so it stays off until you choose it. | Added a config option letting the gargoyle use fire exits and the main entrance (default off). |

## Before you call the entry done

* **Word-count the longest bullet.** Over 25 and it is wrong, no exceptions.
* **Count sentences.** More than one, and the second had better be telling the player to do something.
* Search the new text for the four banned characters. Zero hits.
* Check every bullet passes the player-notices test. The rest go to `Done_YYYY-MM.md`.
* Check the group headers against the file. A new header usually means a miscategorised bullet.
* Read it out loud. If it sounds like a report, cut it in half.
