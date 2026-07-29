# Rename decompiled locals — slice 3 (card c62f159e)

## Context

ILSpy invented every local name in the recovered binary (Release, no PDB). Slices 1 (`d26f0681`,
625 refs) and 2 (`ace0b261`, 532 refs) named them in the engine-core and gameplay files. This
slice takes the **UI/menu group**, which the card deliberately left whole so one agent gives it
uniform naming.

Scope is the five fully-invented patterns only: `num` / `val` / `flag` / `text` / `array`
(with or without a numeric suffix). Type-derived names — `color2`, `position2`, `scale2`,
`direction2`, `value2` — belong to card `bdbcf2b9`, which is sequenced AFTER this one, and are
left untouched even where they sit on the adjacent line.

## Census (measured on this branch, not the card's raw word counts)

| file | refs | decl sites |
|---|---|---|
| GammaMenu | 96 | 26 |
| MenuScene | 79 | 19 |
| MenuSub1 | 60 | 10 |
| SubMenuCarousel | 58 | 9 |
| SubMenuLevelChoice | 56 | 13 |
| InstructionsMenu | 49 | 11 |
| PlayerSettingsMenu | 48 | 11 |
| DifficultyMenu | 29 | 9 |
| **total** | **475** | **108** |

Two things the raw census hides, both found before writing any code:

- **Four `text` PARAMETERS** are in these files and must never be renamed (metadata originals):
  `MenuScene.ShowNetStatus(string text)`, `MenuSub1.RemoveEntry(string text)`, and both
  `MenuSub1.AddEntry(string text, ...)` overloads. Every `text` inside those bodies is the
  parameter.
- **Bare declarations exist** (`float num4;` in `MenuSub1.DrawMenu`, assigned in both branches of
  an `if`). A decl-with-initializer regex misses them, so the sweep works over whole method
  bodies, not over declaration lines.

## Design

### Sweep method

Per file, per METHOD BODY (brace-matched spans), never file-wide:

1. Collect the invented tokens present in that body.
2. Subtract any token that appears in that method's SIGNATURE — the parameter guard.
3. Apply renames **longest name first** (`num18` before `num1` before `num`), on `\b` boundaries.
4. Substitute only in the CODE part of each line: whole-comment lines are skipped, and a line
   with a trailing `//` is split so only the left side is touched.
5. Assert an exact expected occurrence count per rename; abort the file on any mismatch.

Identical names may be reused across sibling `if`/`else` blocks (legal C#, and names do not
exist in IL). `SubMenuLevelChoice.DrawEntryAt` is two symmetric branches computing the same
five values under different numbers — giving both branches the same names makes the duplication
legible instead of hiding it behind `num2` vs `num6`.

### Naming conventions

Precedent over invention, per the two shipped slices:

- **The selected-entry pulse triad repeats verbatim in three files** under three different
  numberings — `MenuSub1.DrawMenu` (528-530), `PlayerSettingsMenu.drawSetting` (190-192),
  `DifficultyMenu.DrawMenu` (110-112). All three compute `15f/textWidth`, `TotalSeconds`, and
  `MyMath.Mod(t/2, 1)` feeding `brainPulsate.Evaluate(...)`. They get ONE set of names across the
  group (`pulseAmount` / `pulseTime` / `pulsePhase`, with the `num4` it scales becoming
  `entryScale`). Uniformity here is the main reason the card kept the group together.
- **Never shadow a field of the class or its base** — `AlienDrawableGameComponent` has
  `protected Color color`, so a `Color val` local becomes `tint` (slice 2's precedent, e.g.
  `InstructionsMenu.ExplainPowerup`).
- Layout math takes the names already in the tree for those quantities (`SafeZone` edges → `left`
  / `right` / `bottom`, `font.LineSpacing` → `lineH`, following slice 2's `rowH`).
- `bool flagN` accumulators over the four pads become intent names (`GammaMenu.Update`'s three
  are gamma-up / gamma-down / confirm held).
- A `string text` reassigned through four literals in one method becomes `line`.

### The one thing needing a ruling: two comments name the locals

`SubMenuLevelChoice.cs:122` and `:138` are hand-written prose that CITE the invented names:

```
// imgW*scaleX x imgH*scaleY = 800*num2 x 600*num2 (scaleX = (800/imgW)*num2).
// Mouse hit box: screenshot centred at `position2`, sized 800*num6 x 600*num6.
```

Renaming `num2`/`num6` and leaving these makes both comments wrong. The card's hygiene rule says
no changed line may start with `//`. Proposed resolution: **two commits**.

1. The mechanical sweep — comment-clean, so the numstat and no-`//`-changed checks apply to it
   exactly as in slices 1 and 2.
2. A separate, tiny, hand-written commit re-syncing those two comment lines to the new names.

That keeps the mechanical guarantee intact and does not ship stale prose. The alternative —
skipping `num2`/`num6` in that file — leaves the worst names in it untouched.

## Verification

The hash oracle IS the gate for this card class; no harness, no browser, no `eahl`.

```
python tools/verify_il_identical.py --ref origin/main     # must report IDENTICAL
dotnet build web/EvilAliensWeb -c Debug                   # clean
```

Plus the three checks the oracle cannot make, run on the SWEEP commit:

- `git diff --numstat` and `git diff --ignore-all-space --numstat` identical (no reformatting).
- `git diff -U0` contains no changed line starting with `//`.
- insertions == deletions (a pure rename changes no line count).

Any unexpected oracle verdict stops the card and gets reported, rather than debugged here.

## Out of scope

- Type-derived names with dead numeric suffixes (`color2`, `position2`, `scale2`, `explosionData2`)
  — card `bdbcf2b9`, sequenced after this one.
- Collapsing dead `= default(T)` initializers (`MenuSub1.DrawMenu:519`, `DifficultyMenu:103`) —
  card `cbdf0a6f` classified and deliberately left these; this slice renames them, never collapses.
- ILSpy's redundant parenthesisation.
- The remaining ~1370 refs in the non-menu leftovers (a slice 4).
