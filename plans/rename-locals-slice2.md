# Rename decompiled locals — slice 2

Card `ace0b261`, continuation of `d26f0681` (slice 1: 625 refs across 5 files + the oracle
`tools/verify_il_identical.py`).

## Context

The recovered binary is a Release build with no PDB, so ILSpy invented every local variable
name (`num`, `num2`, `val`, `flag`, `text`, `array`). Everything else — namespaces, types,
methods, **parameters**, fields, properties, enum members, constants, literals — came from
assembly metadata and is original. Slice 1 established that this card class is *provable*:
local names live only in the PDB, so a correct rename compiles to a byte-identical
`EvilAliensWeb.dll`. The only real cost left is reading the surrounding math carefully enough
to pick an honest name.

## Research findings (these change the card's own plan)

**1. The card's top render-group suggestion is a false positive.** `SpriteBatchWrapper.cs`
shows 101 raw hits, but 95 of them are the **parameter** `string text`, which appears in 15
method signatures (`DrawString`, `DrawMetalString`, `DrawShadowString`, `RasteriseMetalText`,
…). Parameters are metadata originals and must never be renamed. The file has ~2 genuine
invented locals. **Excluded from this slice, and it should not be picked up by a future one.**

**2. The remaining-work number is inflated the same way.** A raw word-boundary census over
`Game/` counts 2405 hits in 121 files, but that includes every legitimate `text`/`val`
parameter. Counting only *declarations* of invented locals gives **439 distinct locals across
116 files**. The honest unit of remaining work is "439 locals", not "2367 refs" — the ref count
is the edit volume, the local count is the thinking volume.

**3. Density ≠ read-frequency.** `CastDisplayer` (84) and `BragScene` (83) are near the top by
volume but are end-credits / brag-screen code that a future card almost never opens. This slice
optimises for what an agent actually reads.

## Design — the slice

**13 files, ~541 refs.** Two coherent groups:

*Engine core — the files nearly every card opens:*

| File | refs | What the locals are |
|---|---|---|
| `Oracle.cs` | 47 | roster/entity queries: counters, `any`-flags, `foreach` items |
| `AlienDrawableGameComponent.cs` | 51 | base class of every enemy — frame/scale/shadow math |
| `GameScene.cs` | 48 | the level state machine |
| `ScoreVisualiser.cs` | 43 | score/combo HUD layout |
| `Wall.cs` | 51 | L3 tower projection (heavily documented in web CLAUDE.md) |

*Gameplay objects:*

| File | refs | What the locals are |
|---|---|---|
| `PowerupData.cs` | 73 | bar-fill chase, exp curve, tutorial zoom easing |
| `StarMine.cs` | 57 | mine behaviour |
| `SpiderBoss.cs` | 52 | boss phases |
| `Ball.cs` | 42 | bounce physics |
| `Explosion.cs` | 36 | explosion lifecycle |
| `BloodExplosion.cs` | 25 | (same family — kept together for consistent naming) |
| `MiniExplosion.cs` | 16 | (same family) |

**Explicitly out of scope**, left as clean future slices:

- The UI/menu group (`GammaMenu` 98, `MenuScene` 79, `MenuSub1` 59, `SubMenuCarousel` 58,
  `SubMenuLevelChoice` 54, `InstructionsMenu` 49, `PlayerSettingsMenu` 48, `DifficultyMenu` 30)
  — highly repetitive layout math, best done in one pass by one agent so the naming is uniform.
- `CastDisplayer` / `BragScene` — see finding 3.
- `SpriteBatchWrapper` — see finding 1.

## Rules (carried forward from slice 1, all held up)

- Scope every substitution to **ONE method body**, longest name first (`num18` before `num1`
  before `num`), on word boundaries.
- **NEVER rename a parameter.** Only locals are invented. `out int numN` inside a call *is* a
  local declaration and is fair game; a name in a method signature is not.
- Comments naming a renamed identifier get updated with it — **but the oracle cannot see
  comments**, and slice 1's substitution corrupted the English word "flag" in a prose sentence.
  Every touched comment line gets re-read by eye.
- Prefer naming precedent already in the tree over invention.
- Zero behaviour, formatting or dead-code change — the oracle enforces this literally.

## Verification

1. `python tools/verify_il_identical.py --ref main` → **IDENTICAL**. This is the gate; it is a
   total oracle over the whole assembly, so a stray edit in any file is caught.
2. Clean `dotnet build -c Debug` — 0 errors, warning count unchanged.
3. Manual by-eye pass over the full diff for the one thing the oracle is blind to: comment
   prose, and whether each new name is actually *honest* about what the value is.

No harness, sim, dev server or browser — per root `CLAUDE.md`, this change class is proven, not
spot-checked, and building a visual rig for it would be strictly weaker than the hash.

## Out of scope

- Renaming parameters, fields, or anything else that survived in metadata.
- Any behaviour fix noticed in passing → follow-up card, not this diff.
- The remaining ~103 files → follow-up card (slice 3), with the corrected census.
