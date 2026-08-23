# packaging\nexus

The Nexus Mods archive. `build-pack.ps1` runs `pack-nexus.ps1` on every pack, so a normal
`.\run-build.ps1` produces three zips in `Packages\`: the two Thunderstore packages and
`TimeNeverStops-<version>.zip` for Nexus.

**Mod page:** https://www.nexusmods.com/schedule1/mods/1141

## Why this is shaped differently from the Thunderstore packages

Thunderstore gets **two packages**, one per build, because r2modman and Gale install a package
wholesale and the player picks the right one on the website. Nexus is **one mod page with one
download**, so both builds go in the archive and the choice moves into the install:

```
TimeNeverStops-<ver>.zip
  IL2CPP\Mods\TimeNeverStops_IL2Cpp.dll
  Mono\Mods\TimeNeverStops_Mono.dll
  fomod\{info.xml, ModuleConfig.xml, icon.png}
  READ ME FIRST - Manual Install.txt
  README.md
  CHANGELOG.md
```

Nexus has no r2modman-style convention: Vortex and a player with a mouse both expect an archive
that mirrors the **game folder**. So `Mono\Mods\` and `IL2CPP\Mods\` are one level above the
layout the game wants, and a manual installer drags the `Mods` folder out of whichever matches
their build. Managers that read `fomod\` turn the same choice into a radio button.

**There is deliberately no unconditional file in the archive.** Installing both DLLs is the one
outcome nobody wants, so nothing installs without an answer to the build question.

## Files here

| File | What it is |
|---|---|
| `pack-nexus.ps1` | Builds the archive. Reads `Version.props` and `TNS_Project\bin\` — it does **not** compile, so a hand run packages whatever the last build left there. |
| `fomod\ModuleConfig.xml` | The guided installer. **The schema URL in it is a magic string** — read the comment at the top before touching it. |
| `fomod\info.xml` | FOMOD metadata. `@VERSION@` is a token the pack script substitutes; do not replace it with a literal. |
| `schema\XmlScript5.0.xsd` | Validated against at pack time. Not shipped in the zip. |
| `MANUAL-INSTALL.txt` | Goes in the zip root as *READ ME FIRST - Manual Install.txt*. |
| `Nexus_Description.bbcode` | The mod page body. **Paste as BBCode — Nexus does not render Markdown.** Not shipped in the zip. |

## What the pack script refuses to do

* **Package a stale build.** Both DLLs carry `<ModVersion>` as their `FileVersion`, so a
  mismatch against `Version.props` means `bin\` predates the version bump. It throws.
* **Package a FOMOD Vortex would misread.** It re-runs Vortex's own version-detection regex
  (from `Nexus-Mods/fomod-installer`, `XmlScriptType.cs`) *and* validates against the 5.0
  schema. Schema validation alone is not enough — pointing a validator at the 5.0 schema
  bypasses the version detection, which is the half most likely to be broken. A wrong schema
  string silently falls back to XmlScript 1.0, a schema older than `installSteps`, and Vortex
  then rejects the installer with a line number that explains nothing.
* **Ship a silently incomplete zip.** It compares the staged file count against the archive's.

## Uploading

There is **no CLI path for Nexus** — `run-build.ps1 -Publish` covers Thunderstore only. The
Nexus archive is uploaded by hand, and two fields on the upload form matter more than they look:
the file's **Name** and **Version** are what fill the Name and Version columns in every user's
Vortex. `info.xml` does not do it for you. Tick the AI-generated-content box to match the
disclosure in the description.
