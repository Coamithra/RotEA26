# Recover the unmerged bg-tile-seams review fixes (card 51bdd4a9)

## Context

Local commit `f029d30` "Review fixes: per-texel pad-bleed calibration, bgfreeze off-switch" was
created 2.5 minutes *after* PR #142 (`fix/bg-tile-seams`) merged, and the branch was never pushed
again — so the `/review` findings on that card never shipped. ~145 commits have landed since.

Confirmed by inspection against today's `main` (31b8cc7): **none of the eight files' review fixes
are present upstream**. But one of them, `check_pad_bleed.py`, was substantially rewritten
upstream by card `110153c7` (509612a, "walls: mip chain for the 756-v1 wall sheet") — which built
its mip-level checking **on top of the very calibration the review commit replaced**. So this is
not a mechanical cherry-pick.

A trial `git cherry-pick -n f029d30` produces:

| File | Result | Verdict |
|---|---|---|
| `web/EvilAliensWeb/Compat/DebugFlags.cs` | auto-merges | apply verbatim |
| `web/EvilAliensWeb/Game/EvilAliens/Background.cs` | auto-merges | apply verbatim |
| `tools/textures/build_textures.py` | auto-merges | apply verbatim |
| `tools/textures/build_texviewer.py` | auto-merges | apply verbatim |
| `tools/CLAUDE.md` | conflict | hand-merge (both sides additive) |
| `web/EvilAliensWeb/CLAUDE.md` | conflict | hand-merge (both sides additive) |
| `tools/textures/check_pad_bleed.py` | 5 conflicts, structural | **re-apply by hand onto main's mip version** |
| `plans/bg-tile-seams.md` | deleted on main | drop — the plan shipped, its edits are moot |

### The one real semantic collision

- **9aa1ba6** (what PR #142 merged): `deltas()` / `exceeds()`, `FLOOR = 16`. Calibration compared
  the **whole edge's max** gutter step against the **whole edge's max** intrinsic step.
- **f029d30** (the lost review fix): replaced them with `weighted()` + `edge_margin()` — judged
  **per texel**, against the image's own across-edge step within `WINDOW = 1` texel; `FLOOR 16 → 32`.
  The finding: one high-contrast spot on a long edge licensed a real gap everywhere else along it
  (it passed a 116/255 alpha discontinuity on `eye_idle` pre-fix).
- **main today** (509612a on top of 9aa1ba6): added `read_dds()` / `mip_level_image()` /
  `check_edges()` so **every mip level** is checked — but still through the old whole-edge-max
  `deltas`/`exceeds`, `FLOOR = 16`. The reviewed weakness is still live, and now applies at every
  level.

Both improvements are wanted. Neither subsumes the other.

## Design

### 1. `tools/textures/check_pad_bleed.py` — port the calibration into the mip structure

Keep main's `read_dds` / `mip_level_image` / per-level loop **unchanged**. Replace the metric:

- Delete `deltas()` and `exceeds()`; add `weighted()` and `edge_margin()` from `f029d30` verbatim,
  plus `WINDOW = 1` and a re-swept `FLOOR` (see below).
- Rewrite `check_edges()` to return a list of `(name, (slack, label))` per edge instead of a list
  of failure strings — right column / bottom row / corner, each via `edge_margin(edge, gutter,
  inner)`. The corner takes f029d30's **diagonal inner neighbour** as its reference (main's
  `max(col_ref, row_ref)` existed only because the old metric had no per-texel reference; a
  1-texel edge now has its own).
- Keep main's per-level guards (`(pw,ph) == (lw,lh)` → unpadded; `lw < 2 or lh < 2` → no inner
  texel to reference).
- `check()` prints the tightest margin per edge, per level; `--verbose` shows it for passing
  assets too (a review finding) alongside main's mip-count note.
- Add f029d30's `fail()` + DDS-magic/truncation validation to `read_dds` (main trusts the header).
- Add f029d30's `run(verbose=False)` wrapper so `build_textures.py` can import it; `main()` becomes
  `sys.exit(0 if run(...) else 1)`.
- Module docstring: keep main's mip paragraph, drop its "**proves** … EQUALS … all zero deltas"
  over-claim (the exact wording the review flagged), and add f029d30's tolerance-check +
  measured-sensitivity paragraphs.

### 1b. `FLOOR` had to be re-swept — the two changes genuinely interact

Applying the reviewed calibration at `FLOOR = 32` made `756-v1` (the one mipped asset) fail at
levels 1–3, steps of 25–41 against the floor. Investigated before touching the constant:

- **The build is not at fault.** `build_mip_chain` re-runs `edge_gutter()` on each level's own
  canvas, so pre-compression the gutter is an exact replica. Verified directly by rebuilding each
  level's canvas from the source PNG: `gutter == edge` byte-identical at levels 0–3. Every
  observed step is therefore pure BC3 quantisation error.
- **Why mips need more headroom.** The intrinsic reference systematically under-reads cross-block
  error — adjacent content texels usually share BC3 endpoints, while the gutter lands in a
  different 4×4 block. Downsampling flattens the content, so at higher levels the reference
  collapses toward 0 and the allowance falls to `FLOOR`, while the compressor's absolute error
  does not shrink with it.
- **Re-swept over all 124 assets × every level** (396 edge measurements). `FLOOR = 32` reproduces
  the original sweep exactly — 0 false positives *at level 0* — confirming the review commit's
  value was right for the code it was written against; the 4 stragglers are all mip levels that
  did not exist then. Worst legitimate step anywhere: **41**. Nothing came within 3× of
  `HARD = 128`.

`FLOOR 32 → 64`: clears the worst legitimate step by 1.6×, and still sits 1.8× under the smallest
real bleed on record (the 116/255 alpha discontinuity on pre-fix `eye_idle`); a transparent pad
reads the full 255. `SLACK`, `HARD` and `WINDOW` are unchanged. This is a small extension of the
recovered commit, forced by the mip card that landed in between — not a re-litigation of it.

### 2. `--selftest` (new, beyond the recovered commit)

The review finding is a *sensitivity* claim, and nothing in the repo pins it. Add a
`--selftest` (the repo's existing convention — `verify_decompiled_diff.py`, `build_models.py`,
`datfmt.py` all have one) that runs `check_edges` against synthetic in-memory RGBA images, no DDS
and no texconv involved:

1. gutter replicates the edge → **pass** (no false positive);
2. gutter transparent → **fail** (catches the original bug);
3. **the regression case**: a quiet edge with one high-contrast spot, and a real gap in the quiet
   stretch → the *old* whole-edge-max rule passes it, the new per-texel rule fails it.

Case 3 is the review finding, executable and permanent. Without it the calibration change is a
claim in a commit message that nobody can re-check.

### 3. `tools/textures/build_textures.py` — apply verbatim

- Reworded `GUTTER` rationale (one texel is what correctness needs; 4 rounds to a BC3 block).
  Still accurate under main's mip path — each level re-runs `edge_gutter()` on its own pad.
- New final gate: a real (non-`--dry-run`) build imports `check_pad_bleed` and fails on a
  regression, instead of relying on a sentence in the docs. Costs ~2.3 s over 124 assets.

### 4. `tools/textures/build_texviewer.py` — apply verbatim

`bt.edge_gutter(...)` on the preview canvas; the "matches exactly" comment now names the two ways
a preview still differs from a shipped `.dds`.

### 5. `web/EvilAliensWeb/Compat/DebugFlags.cs` — apply verbatim

`?bgfreeze=false` currently *enables* the freeze at x=400 (`TryParse` fails → falls through to the
default), contradicting the file's own on/off convention. Parse numerics first (so `=0` still means
column 0), then honour `IsOn`.

### 6. `web/EvilAliensWeb/Game/EvilAliens/Background.cs` — apply verbatim

Deduplicate the two identical layer loops behind a local `Advance()`; hoist `layerDelta`.
Pure refactor — same arithmetic, same order.

### 7. Docs — hand-merge both conflicts

- `tools/CLAUDE.md`: main's bullet gained the mip-level sentences; f029d30 rewrites the *first*
  half (tolerance-check not proof, auto-run by `build_textures.py`, measured sensitivity). Keep
  both halves.
- `web/EvilAliensWeb/CLAUDE.md`: main's `?bgfreeze` bullet gained the layers-coincide GOTCHA;
  f029d30 adds caveat (2) — what it does *not* freeze — and the `=false` / `=0` semantics. Keep
  both. Also correct the "asserts the gutter matches the edge" claim in the edge-gutter bullet.

## Verification — results

**No live browser testing this session (user is overwatching), so the final in-game smoke check
is deferred — see "Not verified here".** Everything below is offline and reproducible.

1. **`check_pad_bleed.py --verbose`** — all 124 shipped `.dds` pass under the new per-texel
   calibration at every mip level, 1.6 s. ✅
2. **`check_pad_bleed.py --selftest`** — all three cases behave as specified, and the superseded
   whole-edge rule is confirmed to miss the licensed-gap case the per-texel rule catches. ✅
3. **Pre-compression exactness** — each mip level's canvas rebuilt from the source PNG;
   `gutter == edge` byte-identical at levels 0–3, proving the `FLOOR = 32` flags were BC3 noise
   and not a build defect. ✅
4. **`FLOOR` sweep** — 396 edge measurements over 124 assets × every level; see §1b. ✅
5. **Real build gate, positive** — `build_textures.py --only gfx/base/756-v1 --padtest 100` ran
   the gate, passed, and reproduced the committed `.dds` **byte for byte** (no asset churn). ✅
6. **Real build gate, negative** — with `edge_gutter()` neutered, the same build **failed**
   (`rc=1`), flagging alpha steps of 255 at every level: the original bug, caught 4× above the
   floor. Tree restored afterwards. ✅
7. **`dotnet build web/EvilAliensWeb -c Debug`** — 0 errors; no new warnings in either touched
   C# file. ✅
8. **`verify_decompiled_diff.py --ref main`** — difference confined to `Background.Update` (the
   intended dedup, same arithmetic and order) and `DebugFlags.Parse`'s `bgfreeze` case. The
   report's second member name is ILSpy's known mis-attribution of the giant switch; its other
   hunks are pure `resultNN` temp renumbering caused by the `out var` moving position. ✅
9. **Diff spot-check** — no lowercase `content/`, no `BlendState.AlphaBlend`, no regenerated
   `Game/` beyond `Background.cs`, no `.dds`/asset churn. ✅

### Not verified here

- **In-game smoke check** of `?bgfreeze=0` (pins) vs `?bgfreeze=false` (scrolls) and zero console
  errors. The original commit verified this before it was orphaned; the C# is being applied
  verbatim, so this is a re-confirmation, not an unknown. Flagged for the user.

## Out of scope

- Re-running `build_textures.py` for real (no `.dds` content change is intended; the canary /
  `--padtest` trap makes a stray rebuild a real hazard).
- The `plans/bg-tile-seams.md` edits — that plan shipped and was deleted upstream.
- Anything about the mip chain itself (card `110153c7`, already merged).
