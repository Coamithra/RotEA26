# Plan: flyspider-flatten-cost review follow-ups (card 6eb8dc9e)

## Context

Card 9c92962e shipped the swarm flatten as the fog layer's default plus the pinned
`?flyspidercount=` bench. A cold review of that branch raised 9 findings (4 should-fix, 5 nit)
that were deferred to ship fast. None is a blocker; none changes shipped behaviour except one
debug-bench correctness fix. The separate browser gate for 9c92962e is card `c781e17a` and stays
open.

**Session constraint:** the user is using the machine and asked for no live testing this session,
so the browser half of the Phase-5 gate is deferred (see Verification).

## Design — file by file

### 1. `FlyingSpider.Setup` doesn't reset recycled state *(should-fix)*

`ComponentBin.Recycle<FlyingSpider>()` hands back a pooled instance; `Setup(bool)` is the only
call every spawn path makes before `Add` (`FlyingSpiderEvent`, `Level2.spawnFlyingSpiderBench`,
`FlyingSpiderDescriptor.CreatePuppet`). It sets `isbackground` and nothing else, so a spider that
once served a `?flyspidercount=` bench keeps `benchIndex`/`benchCount` forever: `Initialize` →
`ApplyBenchPlacement` re-pins it at speed 0 on a stale grid slot. Repro: boot a bench, quit,
replay Level 2 — the recycled spiders are frozen, and (background) never die, so they are
permanently immortal scenery. `netForcedColorIndex` has the same shape: a recycled net puppet
would keep the host's forced tint in a later local game.

**Fix:** `Setup` clears `benchIndex = null; benchCount = 0; netForcedColorIndex = null;` and gains
a comment naming it as the per-spawn reset seam. Both setters that follow (`SetupBench`,
`NetForceColor`) are called *after* `Setup` on every path, verified above, so nothing is clobbered.

### 2. `Level2.cs` `?flyspiders` call-site comment repeats the refuted claim *(should-fix)*

Lines 179–181 still say `?flyspiders=fg` "is the A/B that isolates the render-target cost" — the
exact claim card 9c92962e refuted (fg vs bg differ in six things and the populations were never
equal). Rewrite to point at `?flyspidercount=<N>` + `?flyspiderflatten=per|0|swarm`, matching the
corrected wording already in `PopulateFlyingSpidersOnly`'s own comment and both CLAUDE.md files.

### 3. `DebugFlags` `IsOn` doc comment orphaned by `IsExplicitlyOff` *(should-fix)*

`IsExplicitlyOff` was inserted directly under the "A bare flag (?menu) or =1/=true/… means ON"
line, which is `IsOn`'s doc and is wrong as a description of `IsExplicitlyOff` (a bare flag is
*not* explicitly off — `IsExplicitlyOff(null)` returns false). **Fix:** put `IsOn` first with that
line, `IsExplicitlyOff` after it with its own "complement of IsOn" doc — which is also the natural
reading order, since the second is defined in terms of the first.

### 4. `FlyingSpiderSwarm` uses a raw `ComponentRemoved +=` *(should-fix)*

The ctor does `game.Components.ComponentRemoved += OnComponentRemoved` and never unsubscribes, so
the delegate roots the swarm in the collection's event for the process lifetime. The codebase seam
for exactly this is `IComponentWatcher` (`Floor`, the ownership model the class comment cites,
uses it), dispatched by `ComponentBin.NotifyWatchers` off a persistently-maintained list.

**Fix:** implement `IComponentWatcher` (`OnComponentRemoved(e)` + an empty `OnComponentAdded(e)`,
the `Floor` shape) and drop the `+=`. Correctness checked against `ComponentBin`: the removal
handler does `WatcherRemove(item)` → `idleList.Add(item)` → `WatcherAdd(item)` — a net-zero move —
*before* `NotifyWatchers`, so the swarm still receives its own removal and `Active` still clears.
Between construction and `Add` it is in none of the tracked lists, hence not a watcher — harmless,
it cannot be removed before it is added.

### 5. Bench grid collapses the bottom rows for background spiders *(nit)*

`ApplyBenchPlacement` lays the grid over `y ∈ [0,475]`, then background spiders clamp
`startheight = min(350, y)`, and `Update` drives the on-screen Y from `startheight`. At N=40
(7 cols × 6 rows) the two bottom rows (y ≈ 356 and 435) both flatten onto 350, stacking 12 spiders
on one band — the opposite of the "spread over the field" the grid comment promises, and it
concentrates the overlap the flatten is measuring.

**Fix:** scale the row range to the band the variant actually occupies —
`ySpan = isbackground ? 350f : 475f` — so the clamp becomes a no-op instead of a collapse, and
comment why 350 is the background ceiling.

### 6. `?flyspidercount=` promise doesn't hold for the foreground variant *(nit)*

Foreground bench spiders keep `Collides = true`, so the player can shoot them (N decays — the
exact drift the bench exists to remove) and they can kill an un-invulned ship mid-measurement.

**Fix:** force `Collides = false` in bench mode (in `ApplyBenchPlacement`, which already runs last
in `Initialize`). Chosen over merely scoping the comment because "exactly N for the whole run" is
the bench's entire contract. Trade-off, stated in the comment: the foreground bench then also
skips the collision pass, so it measures DRAW cost only — which is what the bench is for (GL draw
calls / frame ms), and the background variant it is compared against was never collidable anyway.
`DebugFlags`' `?flyspidercount=` doc gains the same note.

### 7. `FlyingSpiderSwarm.members` cleared at the END of `Draw` *(nit)*

`Draw` early-returns on `members.Count == 0` before reaching the clear, and any throw between
`CollectMembers` and the clear leaves entries behind that the next frame appends to — double-drawn
inside the flatten, compounding. **Fix:** clear at the top of `CollectMembers` (the collector owns
its own scratch), drop the trailing clear.

### 8. Malformed `?flyspidercount=` / `?flyspiderbox=` swallowed silently *(nit)*

Four lines under the comment explaining that a mislabelled bench run is unacceptable, a typo'd
value falls through the `TryParse` and the run silently uses the *stream* (or the baked box) while
being labelled as the bench. **Fix:** the same `else { Console.WriteLine("[debug] unknown …") }`
warning `?flyspiderflatten=` already carries, naming the accepted range for each.

### 9. Doc + field-placement tidy *(nit)*

- `web/EvilAliensWeb/CLAUDE.md` "Console QA helpers" index gains `eaFlySpiders()` (it is documented
  in the feature bullet but missing from the maintained list).
- `Level2.cs` — `benchBackground` / `benchCount` move from between two methods up to the class
  header field block, next to `flyingSpiderSwarm`.

## Verification

- Clean `dotnet build web/EvilAliensWeb -c Debug` (zero warnings introduced).
- **Item 5 as DATA, not a screenshot:** a throwaway python mirror of `ApplyBenchPlacement`'s grid
  math prints the per-row `startheight` census before and after for N = 4/40/80, foreground and
  background — the collapse (and its removal) is an arithmetic property, so a frame could only
  show it worse. Kept in the scratchpad, not committed.
- Items 1, 4, 6, 7 are argued against the code they interact with (recycle order, `ComponentBin`
  notify path, `Initialize` ordering) — each traced in the design above.
- Diff spot-check per CONTRIBUTING (no lowercase `content/`, no `BlendState.AlphaBlend`, no
  codegen re-run, no hand-edited pipeline output).
- **DEFERRED — the browser half of the gate.** The user asked for no live testing this session.
  Nothing here changes shipped rendering (item 6 is bench-only, item 5 is bench-only, item 1 fixes
  a debug-pool leak), but "a clean build does NOT mean it runs" still applies. The needed pass —
  `?level=Level2&flyspiders&flyspidercount=40`, `eaFlySpiders()`, plain `?level=Level2` smoke,
  zero console exceptions — is already the standing card `c781e17a`; I will note this branch on
  that card rather than open a duplicate.

## Out of scope

- Anything from card `c781e17a`'s four browser checks.
- Re-measuring the flatten cost numbers (unchanged by this card).
- The `?flyspiders=fg` fast-boot itself — the comment is corrected, the flag stays.
