# Collapse ILSpy `|=` and duplicated-subexpression temporaries

Card `0c624f9d` · branch `refactor/collapse-ilspy-temporaries` · worktree `wt14` (port 5306)
Follow-up from `d26f0681` (rename decompiled locals, slice 1).

## Context

Slice 1 renamed ILSpy's `num`/`val`/`flag`/`text`/`array` locals. It deliberately left three
groups behind, because a rename is the wrong repair for them — the right repair is to **delete**
the local. This card does that.

The important discovery in research: **the card's stated verification gate does not work as
written.** `tools/verify_il_identical.py` builds `-c Debug` with no `Optimize` property, so
Roslyn keeps every local for the debugger. Deleting a local therefore changes the IL, and the
oracle would report DIFFERENT for a change that is genuinely behaviour-preserving. Measured on a
scratch assembly:

| Shape | Debug (unoptimized) | `-p:Optimize=true` |
|---|---|---|
| `bool num = held; held = num \| a;` | `3378ea0f…` | `0d3f9784…` |
| `held \|= a;` | `d5fb8cde…` | `0d3f9784…` |

So the oracle still applies — but only with optimization on, which is what makes the dead temp
disappear. That is a tool change this card owns.

## Design

### 1. `InputHandler.UpdateKeyPads` — 20 `|=` temporaries (`InputHandler.cs` 226–395)

ILSpy rendered `held |= X` as `bool numN = held; held = numN | X;`. All 20 (`num`, `num2`…`num20`)
mean "the previous value of `held`". Collapse each pair to `held |= X`, deleting 20 locals and
40 references — every remaining `numN` in the file.

Uniform `|=` even for the first write in each case (where `held` is provably `false`, since it is
initialised at the top of the `j` loop and cases 9/10/5/4 assign with `=`). Using `|=` throughout
matches the same-file precedent in `InputHandler.Update` (line 184–188, `held |= …`) and does not
require the reader to prove the `false` for themselves. `|` and `|=` are both non-short-circuiting,
so operand evaluation is unchanged.

### 2. Duplicated sub-expression temporaries (`PlayerShip.cs`)

- **`DoAIMove` 845–862** — `powerup.Position - base.Position` computed **five** times:
  `toPowerup` (845), `toPowerup2` (852), `toPowerup3` (855), `toPowerup4` (859), plus an inline
  fifth inside `MyMath.VectorToAngle(…)` at 862. Slice 1 could not share one name because 855/859
  are nested inside 845's scope (CS0136). Collapse to the single `toPowerup` at 845 and use it at
  all four later sites — including the inline one at 862, which is the same expression and would
  look arbitrary left behind.
- **`DoAIFire` 575–579** — `toBaddy` (575) and `toNearest` (578) are the same
  `baddy.Position - base.Position`; 578 is nested in 576's `if`. Collapse to `toBaddy`.

**Why this is safe** (this is the part no hash can prove — see Verification): `Position` is a
plain field-backed property (`AlienDrawableGameComponent.cs:232` — `get => _position;`), so it is
pure and repeated reads are redundant, not meaningful. Nothing between the first and last read
writes either operand: the `toPowerup` block only assigns the locals `position`, `pull` and the
`ref` steering parameter `direction`; the `toBaddy` block's intervening line is a read-only
condition.

The `toBaddy` locals at 788/793/799/804 are already shared (mutually exclusive sibling scopes) —
untouched.

### 3. Renames outside the slice-1 vocabulary

| File / method | Now | Becomes | Why |
|---|---|---|---|
| `PlayerShip.DoAIMove` | `position` (19 refs) | `steerTarget` | It is the steering TARGET and sits next to `base.Position`. The card suggested `target`; `steerTarget` is used instead because the same method already has `target_x`/`target_y` meaning something unrelated (wall-nav map coords). |
| `PlayerShip.DoAIFire` | `alienDrawableGameComponent` | `nearest` | It is the nearest target; pairs with the existing `nearestDist`. |
| `CollisionHandler.DetectCollisions` | `item` | `cell` | A `BoxInfo` grid cell. |
| `CollisionHandler.DetectCollisions` | `item2` | `occupant` | An `ICollidable` in that cell. Neighbours are `collidable`/`collidable2`/`collider`, so `occupant` stays distinct. |

Case-sensitive, word-boundary renames only — `position` must never touch `Position`/`base.Position`.

**Not touched:** the wider vocabulary census (`value` 184, `item` 143, `list` 100, `result` 39).
`value` is the implicit setter parameter in real property setters and must NEVER be renamed —
follow-up card.

## Verification — what was actually measured

The plan above assumed `--optimize` would make part 1 hash-identical too. **It does not**, and
finding out why is most of this card's verification value. All results below are measured, with
the reference being the branch point `ae3bac5` (note: `origin/main` moved during the card, so
`--ref origin/main` correctly resolves to the *merge-base*, not the tip).

1. **`--optimize` added to `tools/verify_il_identical.py`** — appends `-p:Optimize=true`, folded
   into the cache key (which already hashes `BUILD_FLAGS`, so an optimized baseline can never be
   served to a default run).
   - Positive control: **renames only → IDENTICAL** (`4a6206a0…` both sides).
   - Negative control: clean `ae3bac5` + one constant flipped (`-0.58f` → `-0.59f`) → **DIFFERENT**.
     Run in a throwaway worktree, because the discriminating test needs a tree that is otherwise
     IDENTICAL — flipping a constant on top of an already-DIFFERENT tree proves nothing.
2. **Part 3 (renames) is byte-identical — proven.** This is the only part the hash oracle covers.
3. **Part 1 (`|=` collapse) is NOT hash-identical**, and cannot be made so:
   `bool num = held; held = num | X;` reads `held` *before* the neighbouring
   `GamePadButtons b = state.Buttons;` line, while `held |= X` reads it *after*. The optimizer
   will not reorder a local read across a property call it cannot prove pure, so the `ldloc`
   moves. Measured: the delta is **40 normalised IL lines, pure reordering, zero instructions
   added or removed** (`UpdateKeyPads` 456 → 456 IL lines). Benign because `held` is a method-local
   `bool` that `GamePadState.get_Buttons()` cannot observe or touch.
   - An inlining variant that preserves the read order was tried and **rejected**: it removes the
     `buttonsN` locals too, which renumbers the slots and produces yet another non-identical hash,
     for a bigger diff and no proof. It is also outside this card's scope (see Out of scope).
4. **Part 2 (duplicated sub-expressions) is NOT hash-identical either**, by construction:
   collapsing five `powerup.Position - base.Position` reads to one removes four `get_Position`
   calls, and Roslyn never CSEs a property call away.
5. **Both are verified instead by BOUNDING the difference** with the new
   `tools/verify_decompiled_diff.py`. Two independent bounds, because each has a blind spot the
   other covers:
   - *Per-method IL bound*: of **4012 methods in the assembly, exactly 3 differ** —
     `InputHandler::UpdateKeyPads`, `PlayerShip::DoAIFire`, `PlayerShip::DoAIMove`. None added,
     none removed. `CollisionHandler::DetectCollisions` is absent, re-confirming renames are free.
     (Raw IL diffs must have `// Method begins at RVA 0x…` normalised out first — removing code
     shifts every later method's RVA, which otherwise reports 2208 false positives.)
   - *Decompiled-C# diff*: confined to `DoAIFire` (−2/+3) and `DoAIMove` (−35/+37), showing exactly
     the removed recomputations. `UpdateKeyPads` does not appear because ILSpy normalises both
     `|=` shapes to the same C# — i.e. the decompiler considers them the same construct.
   - **A raw IL diff of `DoAIMove` is actively misleading and must not be read directly**: deleting
     locals renumbers every later slot, so `diff` mispairs `ldloc.s 53` with `ldloc.s 51` and
     reports 317 changed lines including wall-navigation code that was never touched. Decompiling
     first is what makes that noise disappear.
6. Clean `dotnet build web/EvilAliensWeb -c Debug` — 0 errors, and no new warnings in any touched
   file.
7. Final smoke: boot in real Chrome, zero console exceptions. No harness or sim is warranted — per
   root `CLAUDE.md` this class is the IL oracle's job, and `UpdateKeyPads` reads physical gamepads,
   which the rig has none of.

## Out of scope

- The wider ILSpy-vocabulary census (`value`/`item`/`list`/`result`) — follow-up card.
- ILSpy's redundant parenthesisation (`(position) = …`, `(delta).LengthSquared()`,
  `(state).ThumbSticks`) — a separate artifact class, pervasive across `Game/`, follow-up card.
- Any behaviour change. If a reading of the AI or input code looks like a bug, it is left alone
  and recorded, not fixed here.
