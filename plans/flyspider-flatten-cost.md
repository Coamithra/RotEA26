# Background FlyingSpider group-flatten: measure it properly, then decide

Card `9c92962e` · branch `feature/flyspider-flatten-cost` · worktree `wt12` (port 5304)

## Context

`SpriteBatchWrapper.BeginGroupFlatten`/`EndGroupFlatten` is a per-group render-target round
trip. Its **only** user is the background (fog) `FlyingSpider`: body + two wings are drawn at
alpha 0.2, and drawing them separately under straight alpha makes the overlaps composite to
~0.36, so the wings read more solid than the body. The flatten renders the three sprites
OPAQUE into a shared RT and composites the union once at the fog alpha.

The FPS-HUD card (`22e655b5`) produced first numbers via `?level=Level2&flyspiders[=fg]`:

| variant | ms/frame | scene ms | GL calls | headroom |
|---|---|---|---|---|
| background (flatten) | 7.3 | 6.6 | 98 | 137 fps |
| foreground (no flatten) | 2.8 | 2.1 | 42 | 356 fps |

**That 2.6× is not a flatten cost.** The two runs differ in at least six things, not one:

| | background | foreground |
|---|---|---|
| group flatten | **yes** | no |
| `Collides` | false (never killed → accumulates) | true (dies on the ship) |
| `Speed` | `bgSpeed × 1.11` | `bgSpeed × 1.35` (crosses ~22% faster → fewer on screen) |
| `scale` | `0.67 × 0.75` | `1.0 × 0.75` |
| alpha | 0.2 | 1.0 |
| `DrawOrder` | 1 | 20 |

Population alone (kills + the speed difference) explains most of the gap: normalised per GL
call the two are ~0.067 vs ~0.050 ms/call.

## Design

### 1. Make the A/B one-variable

Three small debug knobs. All default to null/off, so a shipped build is byte-identical
(the `DebugFlags` convention).

**`?flyspiderflatten=0`** — `DebugFlags.FlySpiderFlatten`. Background spiders skip the
`BeginGroupFlatten`/`EndGroupFlatten` bracket and draw directly (still at fog alpha, still at
fog scale/DrawOrder/`Collides=false`). This is the honest isolation: *the only* difference
between the two runs is the RT round trip. It doubles as the visual for option 3 in the card
(what dropping the flatten for the fog layer actually looks like).

**`?flyspidercount=<N>`** — `DebugFlags.FlySpiderCount`. Turns `?flyspiders` from an endless
stream into a **pinned bench**: `Level2.PopulateFlyingSpidersOnly` spawns exactly N spiders
once, laid out on a deterministic grid over the play area, with `Speed = 0` so nothing ever
crosses off-screen and dies. Population is exactly N for the whole run — no spawn/despawn
churn, no RNG, nothing to drift between the two runs. The swivel/flap timers still tick, so
wings still flap and bodies still bob: the per-frame draw work stays representative.

Placement/freeze ride a `SetupBench(index, count)` seam applied inside `Initialize` — the
`netForcedColorIndex` precedent — because `ComponentBin.Add` runs `Initialize` synchronously
and `tools/audit_add_order.py` requires full configuration *before* `Add`.

**`?flyspiderbox=<half>`** — `DebugFlags.FlySpiderBox`, overriding the flatten bbox
half-extent (`FlyingSpider.Draw`'s hard-coded `200f * scale`). This is what separates the two
candidate mechanisms:

- **per-CALL / per-FBO-bind bound** → cost is flat in the box size;
- **fill bound** → cost scales with box area (the RT `Clear` is full-RT and the composite quad
  is the whole box).

That matters because the current box is *generous*: `half = 200 * scale`, i.e. a 400×400
design-px square, while the drawn union needs about `±105 * scale` (body half-extent ~80×89
design px; wing design size 92×26 with origin (82,11) swinging ±90°, so a ~83 radius about an
anchor ~21 off the body centre). At render scale that is a ~966² RT cleared *and* composited
per spider per frame where ~507² would do — a free ~3.6× if the cost turns out to be fill.

**`eaFlySpiders()`** — console readout (`DebugInput` → `Console`), reporting the live
FlyingSpider count split background/foreground plus the active flatten/box settings, so every
number pasted onto the card is self-documenting. The card asks for exactly this; it is also
how the *realistic* population gets established (see below).

### 2. Measure

Focused Chrome, `?fpsuncapped`, read `eaFps.stats()` once the 120-frame window settles.

| run | flags |
|---|---|
| baseline (N=0) | `?level=Level2&flyspiders&flyspidercount=0&invuln` |
| N ∈ {20, 40, 80}, flatten on | `…&flyspidercount=<N>` |
| N ∈ {20, 40, 80}, flatten off | `…&flyspidercount=<N>&flyspiderflatten=0` |
| N=40, tight box | `…&flyspidercount=40&flyspiderbox=110` |

Gives: the marginal per-spider flatten cost, a linearity check (a per-call cost is linear in
N; a fill cost is too, so linearity is a sanity check, not the discriminator), and the
box-size sweep that *is* the discriminator.

Separately: a plain `?level=Level2&invuln` run with `eaFlySpiders()` to establish the
**realistic** steady-state background population, so the decision is made against the real
level's number rather than a stress N.

### 3. Decide (driven by the numbers, in this order)

1. Marginal cost at the realistic population is small → **keep it**, record the measured cost
   in `web/EvilAliensWeb/CLAUDE.md` next to the flatten bullet, and replace the current
   indicative-numbers paragraph with the controlled ones.
2. Cost is fill-bound → **tighten the bbox** to the computed extent (a constant change, the
   visual is bit-identical because the composite only ever touched the used sub-rect anyway),
   re-measure, then (1).
3. Still expensive → weigh **dropping the flatten for the fog layer** (screenshot A/B of the
   double-brighten at alpha 0.2 via `?flyspiderflatten=0` — at 0.2 the artefact may simply not
   be visible) against keeping it.

**Out of scope:** the card's "flatten the whole swarm into ONE RT pass" option. It needs
scene-level bracketing of every background-spider draw (a DrawOrder-sandwich pair of
components, i.e. a change to the draw pipeline) and it would also silently change the look
(spider-vs-spider overlaps stop double-brightening too). If the measurement says the
per-spider flatten is genuinely expensive *and* tightening the box doesn't fix it, that
becomes a follow-up card with its own design — not a rider on a measurement card.

## Results

**GL draw calls per frame, pinned bench, Level 2.** Focus-independent (a per-frame *count* is
valid however slowly frames arrive), counted by patching `drawElements`/`drawArrays` and rolling
per tick.

| N | no flatten | swarm | per-spider (shipped) |
|---:|---:|---:|---:|
| 0 | 20.2 | 20.2 | 20.2 |
| 40 | 102.8 | 102.1 | 180.4 |
| 80 | — | 183.0 | 340.1 |
| **slope (calls/spider)** | **2.07** | **2.02** | **3.99** |

- The per-spider flatten costs **+1.97 GL calls per background spider** — it *doubles* the calls a
  fog spider would otherwise cost. Dead linear (N=80 per-spider predicted 340 from the N=40 slope,
  measured 340.1).
- The swarm flatten's slope is indistinguishable from no flatten at all: its overhead is **~1 call
  total**, independent of N.
- The card's mechanism hypothesis is confirmed exactly, and `web/…/CLAUDE.md` already states
  BlazorGL's cost is per-CALL.

**Population, the variable that broke the original comparison.** The bench holds at exactly N
(verified over ~45s at N=40 and N=80 via `eaFlySpiders()`), so the three modes above differ in
nothing but the flatten.

**Visual (frozen `?harness=flyingspiderbg`, pose pinned).** Flatten off → the wings read visibly
more solid than the body (~0.36 vs 0.2). Flatten on → the silhouette fades as one. The artefact is
real, so "just drop it" is the worst of the three options. The swarm variant preserves this
exactly (identical per-spider math); it differs only where two *spiders* overlap, which also stops
double-brightening — at alpha 0.2 over Mars dust that is not perceptible.

**Still outstanding: the ms numbers.** They need a foregrounded window (`document.hidden` was true
throughout — Chrome throttles a hidden tab's rAF and the project's own docs measure a 2.5ms frame
reading 22.8ms unfocused). Everything above is deliberately chosen to be immune to that; the
frame-time confirmation and the `?flyspiderbox=` fill-vs-call sweep are not.

**Recommendation (pending the ms confirmation): switch the default to `swarm`.** It keeps the
visual the flatten exists for, at ~1/40th of its draw-call cost, and stops the cost scaling with
the population. Flipping the default is a one-line change to `DebugFlags.FlySpiderFlatten`'s
initialiser plus making `Level2` always construct the driver.

## Verification

Per the project gate (`CONTRIBUTING.md`): clean Debug build, tool-driven verification, zero
console exceptions in real Chrome.

- The bench itself is the tool. Its correctness is checked as DATA, not by eye:
  `eaFlySpiders()` must report exactly N, unchanged over ≥30s (proves the pin — a drifting
  count is the exact defect this card exists to remove).
- Flatten on/off must be visually compared on a **frozen** bench (nothing translates in bench
  mode, so a screenshot is valid here — the spiders bob/flap, so the pair is compared for the
  wing-vs-body brightness relationship, not pixel equality).
- The measurement matrix above, focused window, `?fpsuncapped`.
- Final smoke: plain `?level=Level2&invuln` boots, spiders look unchanged, console clean.
