# Rename decompiled local variables — slice 1 + the verification oracle

Card `d26f0681`. Follow-up from `432a31e9` (comment-noise strip).

## Context

The recovered binary is a Release build with no PDB, so local variable names were never in
metadata and ILSpy invented them. Everything else survived (namespaces, types, methods,
**parameters**, fields, properties, enum members, literals). Census in
`web/EvilAliensWeb/Game/` today — restricted to the decompiler's invented vocabulary
(`num`/`val`/`flag`/`text`/`array` + numbered variants):

```
124 files · 3032 references · ~727 declaration sites
num 1562 · val 649 · flag 383 · text 332 · array 106
```

The card's worry was verification: "a rename is only safe if the file still builds AND the
feature still behaves, so prefer files that already have a harness/sim behind a debug flag."

**That constraint turns out to be unnecessary, and this card's main deliverable is proving
why.** A local-variable rename is invisible to the compiler's output: local names live only
in the PDB, and with `DebugType=none` no PDB is emitted. So a *correct* rename must produce a
**byte-identical assembly**. That is a total oracle — far stronger than any screenshot.

Measured in this worktree, before writing the plan:

| Change | `EvilAliensWeb.dll` SHA-256 |
|---|---|
| baseline | `459091d2…e16fd` |
| 19 locals renamed across `FillCollisionMatrixLine` (160 lines) | `459091d2…e16fd` — **identical** |
| negative control: `maxLineSteps` `128` → `129` (one constant) | `2f6dc778…10b8` — **differs** |

So the oracle is both sound (a rename is a provable no-op) and sensitive (a one-token
semantic slip is caught). It also covers the whole assembly, not just the edited file, so an
accidental edit anywhere else is caught too.

What the oracle does **not** cover: whether a name is *good*. A misleading-but-compiling name
hashes identically. Name quality stays a human judgement — which is the real work here.

## Design

### 1. Ship the oracle as a tool

New `tools/verify_il_identical.py` (a *verification* script — **not** codegen, unlike its
`fix_*.py` / `strip_il_comments.py` neighbours; safe and meant to be re-run):

```
python tools/verify_il_identical.py --baseline    # stash edits, build, record hash
python tools/verify_il_identical.py --check       # rebuild current tree, compare
```

- Builds `dotnet build web/EvilAliensWeb -c Debug -p:DebugType=none -p:Deterministic=true`.
- Records the SHA-256 of `bin/Debug/net8.0/EvilAliensWeb.dll` into a gitignored sidecar.
- `--baseline` takes its reading from a clean `git stash` of the working tree so the baseline
  is genuinely "before my edits", then restores.
- Exits non-zero on mismatch and prints what to do (the diff is semantic, not cosmetic).
- Documents the one caveat: baseline and check must run **in the same directory** (the
  deterministic hash embeds normalized paths), which the per-card worktree flow guarantees.

### 2. Rename slice 1 — five core files, 625 of 3032 refs (21%)

Chosen for "what a future agent actually has to read", not for menu-screen bulk:

| File | refs | renamed | What the names are |
|---|---:|---:|---|
| `EvilAliens/PlayerShip.cs` | 260 | 260 | 236 of them the **attract-mode AI**: `DoAIMove` (175), `findNextTileOnMap`, `doAIBomb`, `getDistanceToLine`, `DoAIFire` |
| `EvilAliens/CollisionHandler.cs` | 186 | 186 | broad-phase grid fill + the `FillCollisionMatrixLine` DDA rasteriser (`num`…`num18`) |
| `EvilAliens/BackgroundImage.cs` | 107 | 107 | parallax tile cursor + the mirrored-pass loop counters |
| `EvilAliens/InputHandler.cs` | 96 | 56 | `flag` → `held`; the other 40 are deferred, see below |
| `EvilAliens/MyMath.cs` | 16 | 16 | pure math helpers |

**Naming precedent is already in the tree** — prefer it over invention. `CollisionHandler`
has a hand-written `FillCollisionMatrixCircle` using `r`/`left`/`right`/`top`/`bottom`; its
decompiled twin `FillCollisionMatrixBox` computes exactly the same four values as
`num`/`num2`/`num3`/`num4`. That is the target style: short, concrete, matching the
neighbouring hand-written code, not `topRowIndexInclusive`.

Also per the card: hand-written port comments already name these values in prose
(`FillCollisionMatrixLine`'s comment talks about `val.X`, columns and rows) — that prose is
the intent record, and any comment naming an old identifier gets updated with it.

### 3. Rules for the pass

- **Scope every substitution to one method body**, longest name first (`num18` before `num1`
  before `num`), on word boundaries. A `num` in one method must never leak into another.
- **Never rename a parameter** — parameter names came from metadata and are original. Only
  locals are artifacts. (`BackgroundImage.DrawBackground(Vector2 position, …)` stays.)
- **Comments referencing a renamed identifier get renamed too**, or they go stale.
- **No behaviour edits, no reformatting, no dead-code removal** in this card — anything
  spotted becomes a follow-up card. The oracle enforces this literally.
- Preserve CRLF + UTF-8-without-BOM (as `strip_il_comments.py` does).
- `src_decompiled/` stays verbatim — it is the read-only reference copy.

The mapping-application script is a scratchpad throwaway (per-file mappings aren't reusable);
only the verifier ships.

## Verification

1. `tools/verify_il_identical.py --baseline` before touching anything.
2. Per file, after each mapping pass: `--check` must report identical.
3. Final: full `dotnet build -c Debug` clean (normal flags, PDB on) — 0 errors, and no *new*
   warnings vs. the 38 pre-existing ones.
4. Final smoke boot in real Chrome via the wt7 DevServer (port 5287) — game boots, plays,
   **zero console exceptions**. This is the "clean build ≠ it runs" backstop; it is not what
   proves the rename, the oracle is.
5. Diff spot-check for the repo's known traps (stray lowercase `content/`, `BlendState.AlphaBlend`,
   re-run codegen, hand-edited pipeline output) — all inapplicable here but checked.

Note the oracle makes a per-feature harness/sim **unnecessary** for this class of change; a
screenshot could not have proved what the hash proves.

## Found while doing it — deferred to follow-up cards

Two decompiler artefacts turned up that a *rename* cannot fix, because the right repair is to
delete code rather than name it. Renaming them would have meant inventing distinctions that
do not exist, which is exactly the "wrong rename is worse than no rename" trap:

- **`InputHandler.UpdateKeyPads` — 20 `|=` temporaries (40 of the file's 96 refs).** ILSpy
  rendered `held |= X` as `bool numN = held; held = numN | X;`. Every one of the 20 means
  "the previous value of `held`", and four of them share a single `case` block, so they
  cannot even all be called `prev`. The repair is to collapse them back to `held |= X`.
  The neighbouring keyboard path in `Update` already reads that way.
- **Duplicated sub-expression temporaries.** `DoAIMove` recomputes
  `powerup.Position - base.Position` four times into `val9`/`val11`/`val12`/`val13`, in nested
  scopes, so C# (CS0136) forbids giving them one shared name. They are named `toPowerup`…
  `toPowerup4` here — honest about the duplication rather than pretending the four differ.
  Collapsing them to one local is the real fix. Same shape in `DoAIFire` (`toBaddy`/`toNearest`).

Both are provable with the same oracle, which is why they are worth doing.

## Out of scope

- The other 119 files / ~2367 refs — follow-up cards, one per coherent group.
- ILSpy's other invented names that are outside this card's five-name vocabulary and so were
  left untouched even inside the five files: `position` for `DoAIMove`'s steering *target*
  (actively confusing next to `base.Position`), `alienDrawableGameComponent` for the nearest
  target in `DoAIFire`, and `item`/`item2` in `CollisionHandler.DetectCollisions`.
- `value` (184), `item` (143), `list` (100), `result` (39): NOT in this card's vocabulary and
  partly legitimate (`value` is the implicit property-setter parameter and must never be
  renamed). If it's worth doing, it needs its own census and card.
- Any behaviour, formatting, or dead-code change.
