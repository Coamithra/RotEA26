# Review of 2026-07-01 merges to main (PRs #44–#52)

> **Update (2026-07-02):** H1–H3 are FIXED on this branch (`CreditsScene` guard reset
> per showing; `InstructionsMenu`/`HelpText` re-load their localContent textures each
> showing). H2's repro was verified end-to-end in a real browser: pre-fix, the second
> pause → Instructions open draws a black screen with `WebGL: INVALID_OPERATION:
> texParameter: no texture bound to target`; post-fix, three open/close cycles render
> identically with a clean console. H3 shares the exact mechanism and fix; H1 is
> verified by code reasoning (its repro needs two full level completions). M1–M4 and
> below are still open.

Follow-up review of the nine PRs merged to `main` on 2026-07-01 — the batch implementing
fixes from the 2026-06-30 code review. Eight are substantive; #49 only deletes two
per-card tracker docs (standard card-close paperwork, nothing to review).

Reviewed at their merge commits: #44 `3ae76ed` · #45 `9a20d94` · #46 `2c39ba7` ·
#47 `2ec3627` · #48 `12c8439` · #49 `f1eb699` · #50 `4ee1f9f` · #51 `b06f427` ·
#52 `55601a4`. File:line references are post-merge (at each PR's merge commit).

**Verdict at a glance:** the fixes generally do what they claim — but two PRs (#45,
#50) introduced new high-severity user-facing bugs while fixing the reviewed ones, and
three more mediums (#44 incomplete fix, #47 crossfade cut, #52 bloom cache) are worth
follow-up cards. All three HIGHs share one theme: **idempotency/disposal guards added
to components this codebase reuses across showings** — the guard or dispose is correct
for a one-shot object and wrong for a boot-time singleton that gets re-added.

| PR | Branch | Verdict |
|---|---|---|
| #44 | fix/controller-settings-crash | Fix verified; same bug class left open at 2 sites (MEDIUM) |
| #45 | fix/correctness-sweep | All fixes verified; 1 new HIGH (credits soft-lock) |
| #46 | fix/plasmaball-hitbox-drawscale | Correct; bug-class sweep complete; nits only |
| #47 | fix/eamusic-stop-race | Race closed; 1 new MEDIUM (crossfade cut) + 2 LOW |
| #48 | refactor/quality-cleanup-batch | Correct; builds clean; lows/nits only |
| #49 | chore/cleanup-trackers | Trivial (doc deletions) |
| #50 | fix/webcontentmanager-unload-leak | Mechanism correct; 2 new HIGH (over-disposal) |
| #51 | fix/perf-batch-1-texture-score | Correct; cache keys complete; lows/nits only |
| #52 | perf/batch2-hotpaths | Mostly correct; 1 MEDIUM (bloom kernel cache) + 1 deliberate fidelity change to playtest |

---

## HIGH — needs a follow-up card

### H1. PR #45 — credits screen soft-locks on the second level completion of a session — CONFIRMED
`Game/EvilAliens/CreditsScene.cs:47,406-410` + `Game/EvilAliens/Game1.cs:299,619-628`

The new `terminated` idempotency guard in `Terminate()` is never reset, but
`creditsScene` is a boot-time singleton re-added to the component collection on **every**
level completion. `Initialize()` (the per-showing reset — it resets `fadetimer`,
`fadeouttimer`, `paragraph`, `shutup`, `textpos`, `displayingcast`) does not reset
`terminated`.

**Failure:** beat Level 1 → credits roll and finish (`terminated = true`) → play and beat
any level again → credits play, but `Terminate()` early-returns forever: `OnFinished`
never fires, the scene is never removed — the game sits on the credits screen (skip
presses no-op) until page reload.

The guard does fix a real same-tick double-fire (skip press + `fadeouttimer.Finished` in
one `Update`); it just needs to be per-showing. **Fix: `terminated = false;` in
`CreditsScene.Initialize()`.** Verified by hand: `terminated` appears only at its
declaration, the guard, and the set — no reset anywhere.

### H2. PR #50 — real `Unload()` breaks re-opening pause → Instructions — CONFIRMED
`Compat/WebContentManager.cs:105-135` + `Game/EvilAliens/InstructionsMenu.cs:53-62,280-283` + `Game/EvilAliens/GameScene.cs:209-214,373`

`WebContentManager.Unload()` used to be a silent no-op (assets live in the private
`_cache`, unknown to the base `ContentManager`); the PR makes it actually dispose. But the
codebase's component-reuse pattern silently depended on the no-op: KNI's
`DrawableGameComponent.Initialize()` runs `LoadContent()` **once per instance, ever**
(verified by decompiling the shipped `nkast.Xna.Framework.Game` 4.1.9001:
`if (!_initialized) { _initialized = true; LoadContent(); }` — the flag is never reset),
and the explicit `base.LoadContent()` in `InstructionsMenu.Initialize()` is the empty
base method, not the override where `keyboardlayout`/`controllerlayout` are loaded.

**Failure:** in any level: pause → Instructions → Esc (`instructionsMenu_OnExit` calls
`instructionsMenu.Unload()`, now disposing both layout textures) → pause → Instructions
again → the singleton is re-added, `LoadContent` never re-runs, and `Draw` renders
disposed textures (ObjectDisposedException / WebGL error / black screen).

### H3. PR #50 — same over-disposal crashes the attract-demo controls overlay on repeat cycles — CONFIRMED (same mechanism as H2)
`Game/EvilAliens/HelpText.cs:77-80,332-338` + `Game/EvilAliens/Demo1.cs:16`

`HelpText.OnComponentRemoved` calls `Unload()` on itself whenever removed. Each
`Demo1/2/3` is a boot-time singleton owning one `HelpText`, re-added on every attract
run. First run loads the layouts; demo ends → dispose; the demo's **next** attract cycle
draws the disposed texture ~5s in (the Keyboard slide fade-in) — with zero user input
(idle on the main menu through ~4 attract cycles reaches Demo1's second showing).

**Fix direction for H2+H3:** re-`Load` the layout textures on each showing (works
post-PR since `_cache` is cleared), or drop the `Unload()` calls from these two reusable
owners and keep real unloading for genuinely one-shot owners (SplashScene is safe: it
sets `state = stopped`, `Draw` early-returns, never re-added). Manual verification pass
recommended: pause → Instructions twice, and menu idle through 4+ attract cycles.

---

## MEDIUM

### M1. PR #44 — the mouse-click device-picker hole is fixed at 2 of 4 sites; Play and Tutorial flows still break for mouse-only users — CONFIRMED (missing `else` + enum default verified by hand)
`Game/EvilAliens/MenuScene.cs:490-523` (`difficultyMenu_difficultySelected`) and `:900-928` (`mainMenu_TutorialSelected`)

The PR fixes the `NotSupportedException` in the two `_PlayerOptionsSelected` handlers by
defaulting to `ControlDevice.Keyboard` when neither Enter nor a pad button is down (the
state a left-click leaves). But the identical device-picker chain in the difficulty and
tutorial handlers has **no final `else`**: a mouse click leaves the `starter` field at
its previous value — on a fresh `MenuScene`, the enum default `(ControlDevice)0` =
**`PadOne`**.

**Failure (per reviewer trace, not re-verified downstream):** mouse-only user clicks
Play → mission → difficulty; the level starts with the sole player bound to PadOne; with
no gamepad connected, `GameScene`'s pad-disconnect check immediately pops the pause menu
and the keyboard never drives the ship — Continue re-pauses. Same for clicking Tutorial.
No crash (the PR's stated goal), but it's the same root cause at the two
highest-traffic call sites. **Fix: the same `else starter = ControlDevice.Keyboard;`.**

### M2. PR #47 — `fadeOut` cancels the ramp before reading the gain, so interrupting a mid-fade-in crossfade cuts to silence instead of fading
`wwwroot/index.html:551-552` (`eaMusic.fadeOut`)

Per the WebAudio spec, `cancelScheduledValues(t)` removes the pending
`linearRampToValueAtTime` and the `.value` getter then computes from the remaining
timeline — i.e. the initial `setValueAtTime(0.0001, t0)`. Reading `gain.gain.value`
**after** the cancel therefore yields ~0 whenever the outgoing track is still mid-rise.

**Failure:** `play(A)` then `play(B)` within 2.5s (rapid menu→level transition): when B's
decode lands, A is audibly mid-fade-in (~0.3 gain); the cancel snaps the computed value
to 0.0001 and A cuts instantly — an audible click instead of a crossfade. The
fully-faded-in case is unaffected (its ramp event is already in the past), which is why
casual testing wouldn't catch it. The cancel itself is the right fix for the "dying
track swells" bug it targets. **Fix: capture `var v = entry.gain.gain.value;` BEFORE the
cancel** (or use `cancelAndHoldAtTime` where available).

### M3. PR #52 — the bloom blur-kernel cache is never invalidated on a content reload, so a fresh effect instance can pair with a "already pushed" cache and render bloom black
`Game/BloomPostprocess/BloomComponent.cs:214-259` (with `UnloadContent` at `:162`)

The new `EnsureBlurKernel` caches the Gaussian weights/offsets keyed on
`BlurAmount` + half-res target size, but `UnloadContent`→`LoadContent` recreates
`gaussianBlurEffect` (default/zero `SampleWeights`) without resetting the cache keys —
`EnsureBlurKernel` early-returns and the weights are never `SetValue`'d on the new
instance until a resize or a preset change. Rare path on BlazorGL (no device loss, and
bloom is in `ComponentBin.dontTouchThisComponent` so it isn't removed in practice), but
the fix is one line: reset `cachedBlurAmount = float.NaN` in `LoadContent` (or
`UnloadContent`). Note this is the same "reload cycle breaks a once-ever assumption"
class as H2/H3.

### M4. PR #52 — deliberate but fidelity-changing: circle-pair collisions now fire once per direction per frame, halving ungated push-out side effects — worth a JunkBoss playtest
`Game/EvilAliens/CollisionHandler.cs:107-119,349-382` (effect at `Game/EvilAliens/Ball.cs:339-355`)

Moving `CollisionSimpleCircle` colliders into the spatial grid removes the old
double-fire of each `CollidesWith` direction. Damage paths are gated (verified
pair-by-pair: `hittimer`, `Asplode` on `IsDead`, self-limiting separation) so gameplay
damage is unchanged — but the ungated 1px-per-call push-outs now apply once per frame
instead of twice: connected linker Balls sink visibly deeper into the JunkBoss before
separating. The PR comment owns this as a "double nudge fix"; it is still a visible
departure from the shipped 2008 behavior. Recommend a JunkBoss-level playtest to accept
or compensate (e.g. 2px push).

---

## LOW

- **PR #47** `wwwroot/index.html:614,623-627` — a stale `pending` cue can supersede a
  newer `play()`: `play(A)` pre-gesture sets `pending="A"`; the unlocking gesture's async
  `ctx.resume()` races a `play(B)` issued in that window (B doesn't clear `pending`), and
  the resume callback's `startTrack("A")` claims a fresh token and supersedes B. Same
  race class the PR fixed, surviving on the unlock path. Fix: clear `pending` in `play()`
  on the started path.
- **PR #47** `wwwroot/index.html:566,643-653` (pre-existing) — the hoisted "already on
  it" early-return in `startTrack` skips re-applying the reset rate: `play(sameCue)`
  after a Level3/BrainBoss `setRate` sweep keeps the live node at the swept rate while
  `curRate` claims 1.
- **PR #44** `wwwroot/index.html:113-129` — the new "one bad frame survives" tick
  recovery only holds for Update-phase throws; a Draw-phase throw leaves the SpriteBatch
  open, so every later frame throws on `Begin` and escalates to the 30-failure terminal
  overlay. Clean death rather than a hang — acceptable, but the comment oversells it.
- **PR #48** `Game/EvilAliens/MenuScene.cs:1348` — `OnPreviewSelected?.Invoke` now fires
  into zero subscribers (the trial-mode `NewPreviewScene` was deleted) while the raising
  paths remain; unreachable today (`Guide.IsTrialMode` hardcoded false) but a soft-lock
  seam if ever flipped. Suggest a follow-up card to strip the whole trial preview path.
- **PR #48** `wwwroot/index.html:377-381` — the trailer overlay's `GAME_KEYS` swallow
  list matches WASD by `e.key` literal; on a non-Latin layout (keyCode 87 = 'ц') a WASD
  press passes through to the hidden menu. Match `e.keyCode`/`e.code` instead.
- **PR #50** — the leak the PR fixes is real but modest: the recovered memory is the
  one-shot splash art plus per-scene duplicate copies of the two controls-layout PNGs;
  nothing leaked unboundedly, and the warmed shared manager is (correctly) untouched.
  The in-code safety-audit comment checks ownership but misses the actual hazard class
  (reusable components with once-ever `LoadContent`) — see H2/H3.
- **PR #51** `SpriteBatchWrapper.cs:480` — cached score-text RTs use default
  `DiscardContents` usage with no contents-lost handling; garbage digits after a WebGL
  context loss/restore. Safe today (BlazorGL has no device-reset cycle — documented
  assumption elsewhere in the file).
- **PR #51** `tools/textures/build_textures.py` (`--manifest-only`) — the generated
  `PrecompiledTextures.cs` manifest can drift from the shipped `.dds`/`.rtex` set
  (graceful PNG fallback, no breakage; committed set verified 1:1 in this review).
- **PR #51** `Game/EvilAliens/ScoreVisualiser.cs:590-603` — the inactive-slot prompt
  lerps its colour per frame during its 500ms fades, so the new text cache re-rasterises
  every frame there (cost equals pre-PR; cache just buys nothing during prompt fades).
- **PR #52** `Game/EvilAliens/CollisionHandler.cs:349-382` — circles fully outside the
  10×8 grid no longer collide with gridded objects (the old all-pairs scan tested real
  geometry anywhere); matches the long-standing CollisionBox clamping semantics, and
  circle-vs-level-walls still works off-grid — marginal gameplay impact.
- **PR #52** `Game/EvilAliens/ComponentBin.cs:245-275` — `NotifyWatchers` iterates the
  live `watchers` list under a count snapshot (the pre-PR code iterated a copy); every
  current watcher defers its mutations, so this is latent fragility, not a live bug — a
  synchronous-removal watcher added later would skip entries or throw.

## NITS

- **PR #45** `Compat/DebugInput.cs:141-148` — `Enum.TryParse` accepts comma lists
  (`eaPress('Up,Down')` presses Down); debug-console-only.
- **PR #46** `Compat/HarnessScene.cs` — collision-ring overlay: ring is always green
  regardless of `Collides` (blast overlay distinguishes); `ringTex` built even for
  box-hitbox objects; ring centred on `objPos` not `circle.Position` (identical today).
- **PR #48** `Game/EvilAliens/MenuSub1.cs:342-343` — `HandleMouse` header comment stale
  (doesn't mention the new returns-true-on-movement behaviour).
- **PR #51** `SpriteBatchWrapper.cs:63` — `textSpriteCache` keys are caller-chosen bare
  ints; a future second caller could collide with ScoreVisualiser's range (rebuild
  thrash, correct output). Bounded today: 12 keys, one caller, RTs disposed in
  `UnloadContent`.
- **PR #51** `Game/EvilAliens/CastDisplayer.cs:147` — `EnsureAnimation` drops the old
  per-tick `color = Color.White` reassert; verified nothing else mutates it, but the
  code comment covers only the grid/fps assumption.
- **PR #44** `Pages/Index.razor.cs:56-61` — an `initRenderJS` failure only logs to
  console (silent black screen, no overlay); low likelihood.
- **PR #52** `Game/EvilAliens/ComponentBin.cs` — the `watchers` mirror deliberately
  preserves the original's double-notify for components in two lists at once; faithful,
  but easy to "fix" incorrectly later (the `RebuildWatchers` re-sync at
  `FullReset`/`ClearCache` is a good drift backstop).

---

## What was verified clean (highlights)

- **#45**: the `Background` star-slowdown fix (per-frame local instead of compounding
  into the field) is correct and drift-free; the localStorage prune is enumeration-safe;
  the `?level=`/`eaPress` enum guards are right; the `CollisionBox` `y+width→y+height`
  fix is latent (zero callers of that overload); the glint length-tracking correctly
  catches same-lead-char place gains and quietly fixes a latent re-glint on `Load()`.
- **#46**: new PlasmaBall radius = `697 × 0.32 × scale` — exactly the original-resolution
  tuning, now tracking the visible disc (~25% larger than the buggy state, intended).
  Sweep of every texture-derived collision size in `Game/` found no remaining member of
  the bug class (Blast, Ball, JunkBoss, and the shared box path all use `DrawScale`).
- **#47**: the token guard is checked at both async resumption points with no async gap
  before `src.start()`; no double-start; pause/resume (AudioContext suspend) is safe
  against ramps and the scheduled stop.
- **#48**: builds clean at the merge commit (0 errors); every deleted symbol
  (`ContentTestGame`, `SpikeGame`, `NewPreviewScene`, `playtestMenu`, …) grep-verified
  unreferenced; the `SetMetalParams` hoist is byte-identical; the `WindowDestRect`
  letterbox unification removes an old blit-vs-mouse rounding drift; the slowmo
  dt-correction is mathematically right and identity at 60Hz.
- **#51**: the score-text cache key covers every rasterisation input (text, scale, both
  colours, shadow offset, render scale); `alpha`/`glintTime` are deliberately
  composite-time so fades and the event-driven glint stay frame-exact; Pass-1 blend is
  the documented straight-alpha verbatim-copy trick, not the premultiplied same-name
  trap; RT binding save/restore correct; SoundManager singleton configs immutable.
- **#52**: holding `oracle.GetBaddies()` across DoAIMove→DoAIFire→doAIBomb is safe
  (nothing re-enters or mutates the shared list mid-Update; adds are deferred to
  `birthList`); the new box-tests-circle direction is symmetric (both delegate to the
  circle's own test — no lost callbacks); bloom's runtime invalidation is correct
  (resize caught via target dims, preset swaps via `BlurAmount`, `BloomSettings` fields
  readonly); the ComponentBin `watchers` mirror matches the old scan's multiset at every
  mutation site; the hoisted menu glow-ring/octagon arrays are pixel-identical with no
  cross-frame retention.

---

*Review method: one independent reviewer per PR over the full post-merge files (not
just the diffs), each sweeping for unfixed siblings of its PR's bug class; the four
highest-impact findings (H1–H3, M1) were then re-verified by hand in this session,
including decompiling KNI 4.1.9001's `DrawableGameComponent` to confirm the once-ever
`LoadContent` guard that H2/H3 rest on.*
