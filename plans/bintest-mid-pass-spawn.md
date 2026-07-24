# Pin the mid-pass-spawn collision contract in `eaBinTest`

Card `bcdc7430` — follow-up to `9009a1c4` / PR #149, raised by that branch's review.

## Context

PR #149 (`8e3f4ef`) fixed `CollisionHandler.DetectCollisions` re-reading a live
`collidables.Count` mid-pass. Since bin births are instant (card `02d9ad67`), a collision
callback that spawns a collidable grows the list *while the pass runs*, so the old code
- indexed `boxes[m]` past what the pass had sized (`ArgumentOutOfRangeException`), or read
  entries still holding the previous frame's cells, and
- enumerated `collidables` with a `foreach` in the fill phase's all-pairs branch, one spawn
  away from `InvalidOperationException`.

The `?binlog` counter added there proves the path is *exercised*; nothing **asserts** the
fault is absent. A refactor that re-introduced a live `.Count` read would sail through
`eaBinTest` (10/10) and only resurface as an intermittent `[loop] TickDotNet threw`.

**The contract to pin:** *a collidable added DURING a pass must not participate in that
pass — and must join the next one.*

## Why "does it throw?" is not the test

A naive scenario would PASS on the buggy code, because whether `boxes[m]` throws depends on
`boxes`' high-water mark accumulated from prior play — i.e. on session history. So each
sub-scenario below is built so the **buggy** code is caught **deterministically**, by
planting the exact precondition the fault needs rather than hoping the session supplies it.

## Design

Two new scenarios in `Compat/BinTest.cs`, one per loop the fix touched. Both are
synchronous (no tick runs between steps, so `collidables` can only change through our own
actions and indices stay aligned), self-cleaning, and menu-scoped like the rest of the suite.

### New plumbing

- `Game1`: `internal CollisionHandler CollisionHandler => collisionHandler;` — the same
  one-line accessor pattern as `ComponentBin.Game` (added for BinTest by card `02d9ad67`).
- `BinTest`: a second nested scratch type `CollidingAlien : AlienDrawableGameComponent` —
  `Collides = true`, `Visible = false`, a hand-placed `ICollisionType`, a `Seen` list every
  `CollidesWith` appends to, and a one-shot `Spawn` hook. Because the hook runs from
  `CollidesWith`, the bin `Add` it makes is by construction *inside* `DetectCollisions`.
- All scratch boxes are 20px squares centred on design `(440, 300)` → grid cell `(5,3)`
  only, and mutually overlapping.

### Scenario 5a — the fill phase's all-pairs loop

`A` (box) and `M` (`CollisionMultibox`, the non-gridded branch that keeps the all-pairs
scan) overlap. `M.Spawn` adds a newborn `C`.

`M`'s all-pairs branch fires `A.CollidesWith(M)` then `M.CollidesWith(A)` → `C` is born
mid-enumeration.

- **Buggy code:** `foreach (ICollidable collidable2 in collidables)` sees the list version
  change → `InvalidOperationException`. Deterministic, zero dependence on session history
  (`List<T>.MoveNext` re-checks `_version` even on the final step).
- **Assertions:** the pass does not throw · `C` is instantly in `Game.Components` (the spawn
  was real) · `A.Seen` contains `M` (**positive control** — the branch ran and the geometry
  overlaps, so a green result can't be vacuous) · `C.Seen` is empty · neither `A.Seen` nor
  `M.Seen` contains `C`.

### Scenario 5b — the resolution phase's `boxes[m]` loop

This is the loop PR #149's `IndexOutOfRange` came from. Determinism comes from *planting*
the stale `boxes` entry the fault reads:

1. Add `A`, `B` (both `Collides = true`) and `W` (`Collides = false`, inert — the fill phase
   grids regardless of `Collides`), all overlapping at cell `(5,3)`.
2. **Warm-up pass.** Fills `boxes[iW]` with cell `(5,3)` and grows `boxes` to at least
   `iW + 1`.
3. `bin.Remove(W)` + `bin.Update()` → `W` leaves `collidables`. It was appended last, so
   nothing shifts and the next pass's frozen `count` is exactly `iW`.
   `boxes[iW]` keeps its cells: the pass's clear loop is `i < boxes.Count && i != count`, so
   it stops *at* `iW` — the very "entries between the old and new count still hold the
   previous frame's cells" case the fix's comment names.
4. Arm `A.Spawn`, reset the `Seen` lists, run the **test pass**. `A.CollidesWith(B)` adds
   `C`, which lands at index `iW`.

- **Buggy code:** the resolution loop reaches `m == iW`, reads the planted `boxes[iW]`, finds
  `A` and `B` in `fieldMatrix[5,3]`, and calls `C.CollidesWith(...)` — the newborn takes part
  in the pass that bore it. Deterministic.
- **Assertions:** no throw · `A.Seen` contains `B` (**positive control**) · `C.Seen` is empty.
5. **One more pass**, spawn disarmed: `C.Seen` is now non-empty — pinning the other half of
   the contract, that the newborn joins the NEXT pass. (Guards against an over-fix that
   excluded the newborn permanently.)

### Cleanup

`bin.Remove` + flush + `bin.PruneIdle` every scratch instance, exactly as the existing
scenarios do (each is an `IComponentWatcher`, so a pooled leftover would sit in the notify
multiset forever). 5a is fully torn down before 5b starts, so 5b's index arithmetic holds.

## Verification

Per the project rule, the verification tool *is* the change — `eaBinTest()` is the harness.

1. Clean `dotnet build -c Debug`.
2. Real Chrome (foreground), `?menu`, console `eaBinTest()` → the new scenarios PASS and the
   existing 10 still pass, with zero console exceptions.
3. **Negative control (the point of the card).** Temporarily revert `DetectCollisions` to the
   pre-#149 form (`8e3f4ef^`) in the worktree and re-run: 5a must report the
   `InvalidOperationException`, 5b must report the newborn participating. Then restore. A
   test that has never been seen to fail proves nothing.
4. Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run).

## Out of scope

- Making `DetectCollisions` itself defend against a mid-pass *removal* — the existing comment
  documents why it can't happen; asserting it would need a scratch removal path that game
  code deliberately doesn't have.
- Any change to `CollisionHandler` behaviour. This card only adds the accessor and the test.
