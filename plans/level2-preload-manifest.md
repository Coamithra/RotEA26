# Card 74b30beb — Level2 preload manifest is missing the marsloop ground tiles

## Context

Booting `?level=Level2&loadlog` logs eleven `COLD decode` lines for `gfx/marsbg/marsloop1..11`
(~110ms each in the browser, ~1.2s total) right after the level starts, plus repeated
`[hitch] 12x-134ms frame in Level2`. `Content/preload/manifest.txt` lists only two Level2
entries (`brainanimated`, `brainanimatedglow`) and no `gfx/marsbg/*`, so nothing warms the Mars
ground before the scene goes up. Per web CLAUDE.md this is a manifest DATA gap, fixed by
`?loadlog` + `eaPreloadExport()`, not by code.

## Research findings (all measured, not inferred)

### 1. The whole loop runs under `eahl` — with two named gaps

Verified by building `tools/headless` in this worktree and running

```
eahl.exe --flags "?level=Level2&loadlog&aiplayer&invuln&noattract" --frames 40000 --nodraw
```

- `?loadlog` works: `HeadlessHost.cs:81` calls `LoadProfiler.Init(_js)`, and
  `HeadlessJsRuntime` answers `eaLoadProfile.load/save/download`. The card's eleven marsloop
  COLD lines reproduce exactly (as marsloop**1..12** — the card's run missed 12).
- The export **writes a real file**: `eaLoadProfile.download` →
  `HeadlessJsRuntime.WriteDownload` → `<dir of --out>/preload_manifest.txt`, and
  `ExportPreloadManifest` also returns the text, which `eval` prints to stdout.
- **GAP A — `eval PreloadExport` does not exist yet.** `eval` binds by reflection to public
  statics on `Compat/DebugInput.cs` only (`tools/headless/Program.cs:384`), and
  `eaPreloadExport()` bypasses DebugInput: `index.html:1784` calls
  `DotNet.invokeMethod("EvilAliensWeb","ExportPreloadManifest")` directly on `LoadProfiler`.
  Fix = a one-line `public static string PreloadExport() => LoadProfiler.ExportPreloadManifest();`
  on `DebugInput`. Tooling addition, not a fix-by-code.
- **GAP B — `[hitch]` does not exist headlessly.** `LoadProfiler.NoteFrame` is called from
  exactly one place, `Pages/Index.razor.cs:113`. `eahl` never calls it. So the card's second
  VERIFY clause ("the `[hitch]` lines stop") is **browser-only evidence** and needs a Chrome leg.
- Decode COST differs (desktop CLR + local FS: ~6ms where the browser measured ~110ms), but
  COLD-vs-warm is a **structural** fact — was the decode inside a `BeginPreload`/`EndPreload`
  bracket — not a timing one, and the ms/size fields are bookkeeping the loader ignores. So
  eahl is valid evidence for *which* assets are cold; it is not evidence about the hitch.

### 2. Why the manifest CAN fix this (the thing worth checking before writing anything)

`Background.SetMars()` (`Background.cs:1122`) loads all 12 marsloop tiles **synchronously in
`Level2.Initialize`** (`Level2.cs:65`). `QueueIdleWarm`'s comment claims the equivalent
`SetSpace()` case is one "neither PreloadGraphicalContent nor the manifest can ever warm first" —
that comment is **stale**; it predates `Game1.WarmThenLaunch`'s pre-launch warm.

The headless log proves the ordering:

```
[loadprofile] Level2 preload: 2 textures, 27ms      <- Game1.WarmThenLaunch, BEFORE the scene is Added
[loadprofile] COLD decode in Level2: gfx/marsbg/marsloop1 ... marsloop12   <- Level2.Initialize -> SetMars
[loadprofile] Level2 preload: 35 textures, 250ms    <- GameScene.LoadContent (PreloadGraphicalContent + ApplyManifest)
```

The marsloops fall **between** the two brackets, i.e. after the pre-launch manifest warm and
before the scene's own preload. So a `Level2|gfx/marsbg/marsloop<n>` entry is decoded by
`PumpLevelWarm` one-per-tick before `Initialize` runs, and `SetMars`'s `Content.Load` becomes a
cache hit. **Manifest data is the right and sufficient fix.**

The full between-bracket Level2 set (all genuine gaps): `gfx/sprites/shadow`,
`gfx/marsbg/clouds-background`, `marshills1..3`, `marsloop1..12`, `clouds-foreground2`,
`gfx/game/blank`, `gfx/menu/powerbar`, `gfx/sprites/playersheet`, `photocamera`, `bombicon`,
`smoke`, `explosion`, `gfx/hud/barlit`, `barunlit2`, `barlitedge`.

### 3. Two traps in "just paste the export"

- **Replacing `manifest.txt` wholesale destroys ~150 curated entries.** `Serialize()` emits only
  `_byLevel` (this run's recordings + the localStorage-learned set); `Shipped()` is parsed
  separately and never merged in. So a fresh-process export contains *only the levels this run
  played* and **none of the file's hand-written rationale comments** (TeamChallenge, asteroids,
  brain sheets, the Level3 credits-cast block). Plan: use the export as the authoritative entry
  list and **merge** its new Level2 block into the existing file, keeping every current line.
- **A `?level=` boot poisons the Level2 attribution.** `QueueIdleWarm`'s 21-asset space/star set
  normally drains during the splash; with `?level=&autostart` there is no splash, so it drains
  one-per-tick *into live Level2 gameplay* and the export records
  `Level2|gfx/game/space/space00..11` + `star00..07` — 20 entries that are pure rig artifact.
  Confirmed in the 40k-frame log: they are the *only* post-second-bracket COLD lines.
  Plan: capture the export from a **menu-path** boot (`?menu&noattract` + `eval Press` through
  Start Game → difficulty → carousel → Level 2), so the idle queue drains as designed and the
  Level2 set is clean and pasteable verbatim. The `?level=` run stays as the cross-check.

### 4. Other levels — scope

- **Same `SetMars` gap, in scope:** `Demo2.cs:66` and `Paratrooper.cs:84` call
  `Background.SetMars()` from `Initialize`; `InsaneBossI.cs:266` calls it **mid-level**
  (worse — a live-gameplay decode, not a load-tick one). Each gets the same treatment: run it
  headlessly under `?loadlog`, export, merge its block.
- **Boot-phase decodes are OUT of scope, and mostly false positives.** `(boot)` is
  `LoadProfiler.SentinelBoot`, not a `Levels` value — `ManifestAssets` is only ever called with a
  level name, so **a `(boot)|...` manifest line is inert**. Boot assets can only be warmed by
  `Game1.QueueMenuWarm`/`QueueIdleWarm`, i.e. **C# code**. Of the four the card names,
  `title-revenged` is *already* in `QueueMenuWarm` and logs COLD only because the warm itself
  decodes outside a bracket (`RecordTexture` has no boot exemption, unlike `NoteFrame` which
  does). Genuinely unwarmed: `gfx/splash/easplashredone`, `uglysplash22`, `gfx/cursor2`,
  `gfx/sprites/awardmentblade`, `gfx/screenshots/*`. → **follow-up card**, not this one.

## Design

1. `Compat/DebugInput.cs`: add `PreloadExport()`, a one-line passthrough to
   `LoadProfiler.ExportPreloadManifest()`, so the export is reachable from `eahl --repl`'s
   `eval` (and unchanged from the browser console, which keeps using `eaPreloadExport`).
2. Capture, per level (`Level2`, `Demo2`, `Paratrooper`, `InsaneBossI`), via a menu-path
   `eahl --repl`/`--script` run with `?loadlog&aiplayer&invuln`, then `eval PreloadExport`.
3. `wwwroot/Content/preload/manifest.txt`: **merge** the exported per-level blocks in, each under
   a comment block in the file's existing style stating what the gap was (the `SetMars`
   Initialize-before-the-bracket story) and why the pre-launch warm covers it.

   **Merge semantics (ruled by the overseer):** for a level whose export was freshly captured,
   that level's SECTION is replaced *wholesale* by the new export — so a stale entry for that
   level cannot linger. Every other byte of the file is preserved exactly. Concretely: `Level2`,
   `Demo2`, `Paratrooper` and `InsaneBossI` lines are re-derived from their captures; the
   `TeamChallenge` / `SpaceDodge` / `Level1` / `Level3` / `Braineroids` / `Demo1` / `Tutorial`
   blocks and all existing comments are untouched.

   **Card correction worth recording:** the card says "replace `Content/preload/manifest.txt`
   with the download". Taken literally that deletes every curated entry for levels not in the
   run — the card was written before anyone had established that `Serialize()` emits only the
   run's own recordings and never merges `Shipped()`. Recorded on the card.
4. Docs: correct the stale "the manifest can never warm these" claim in `Game1.QueueIdleWarm`'s
   comment, and add the `SetMars`/pre-launch-warm ordering fact to `web/EvilAliensWeb/CLAUDE.md`'s
   preload bullet. Note in `tools/headless/README.md` that `[hitch]` is browser-only.

## Verification

- **Headless (primary, proves the card's first clause):** re-run each level under `?loadlog` and
  assert **zero `COLD decode in <Level>` lines between the two preload brackets**. Valid because
  the COLD classification is purely the preload-bracket structure, which eahl runs identically
  (same `WebContentManager`, same `Game1`/`GameScene` brackets, content read live from `wwwroot`).
  Committed as a `--script` probe if it reads cleanly.
- **Chrome (required, proves the card's second clause):** `[hitch]` is emitted only from
  `Index.razor.cs`, so the "the `[hitch]` lines stop" criterion **cannot** be checked headlessly.
  Needs a foreground-Chrome `?level=Level2&loadlog` run reading the console for both COLD and
  `[hitch]`. **This is the Phase-5 gate anyway** (zero console exceptions) — flagging it because
  it means claude-in-chrome sanction is needed.
- No IL/decompile oracle applies (data change + one additive tooling method).

## Out of scope

- Any change to `QueueMenuWarm`/`QueueIdleWarm` or other C# warm plumbing (the boot-phase
  decodes) → follow-up card.
- Filing the boot-warm follow-up is Phase 6 paperwork, not work here.

## Open questions for the overseer

1. **Merge vs replace.** The card says "replace `Content/preload/manifest.txt` with the
   download". Doing that literally deletes ~150 curated entries for levels this run never
   touched. Proposing merge-in-place with the exported lines verbatim. Confirm?
2. **Chrome sanction.** The `[hitch]` half of VERIFY, and the standard Phase-5 gate, both need
   foreground Chrome. Requesting claude-in-chrome for that leg.
3. **Scope of "other levels".** Proposing Demo2 / Paratrooper / InsaneBossI (the other
   `SetMars` callers) in this card, and boot-phase warm as a follow-up card. Confirm, or narrow
   to Level2 only.
