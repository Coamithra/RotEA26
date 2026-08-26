# Pre-boss wall "different walls for 1-2 frames" — hitch dt clamp (card 430494a7)

## Context

Report: *"just before the overmind final boss, when flying through the blocks (walls) that lead up to the boss, in the middle of the wall section there is a brief visual stutter where a different set of walls is shown, looks like a section from previously in the game. Lasts maybe 1-2 frames."* The "overmind" is the BrainBoss (Hard+ only), and the walls that lead up to it are `BrainBossHard()`'s two `Walls(4)` events at the `speedupuber1` scroll speed (0.72 px/ms — the fastest wall scroll in the game).

## What was ruled out (evidence, not argument)

Every game-STATE mechanism that could draw a different wall grid was excluded:

- **Structurally**: `Walls.Update` runs `Wall.Setup(variation)` BEFORE `ComponentBin.Add`, so a pool-recycled `Wall` can never draw a stale grid; the two pre-boss `Walls(4)` events are the *same* grid anyway, so their seam cannot show "different" walls; `EntryLead`/`DeathY` are calibrated so nothing is visible at spawn or pops at death (verified live in the captures below).
- **Empirically**: two seeded per-frame eahl captures (`?level=Level3&aiplayer&invuln&difficulty=Hard&noattract&walltrace&seed=1`), analysed frame-by-frame for the flash signature (a frame that differs sharply from its predecessor while the successor matches the predecessor again):
  - 2200 frames covering Kylikova → `Walls(4)` #1 → seam → `Walls(4)` #2 → BrainBoss spawn: **zero** flash candidates, zero `[walltrace]` mid-screen POPs.
  - 4500 frames covering the whole `Walls(3)` maze (skulls + star mines live): same result.

  So under a fixed 60 Hz dt the game cannot produce the report. What eahl can never produce — and the browser can — is a **long tick**.

## The mechanism

The browser loop is `IsFixedTimeStep = false`: one rAF = one `Game.Tick`, and KNI's `GameStrategy.Tick` (read from the shipped `Xna.Framework.Game.dll`) hands the game its real elapsed time as ONE dt, clamped only by `MaxElapsedTime` — which defaults to **500 ms** and whose setter *throws* below 0.5 s, so it cannot be configured tighter.

So any main-thread stall ≥ 500 ms (GC pause, cold decode, first snapshot-RT allocation, compositor jank — the `[hitch]` watchdog exists because these are real here) delivers a single 500 ms step. At the pre-boss scroll speed that moves **every wall block 360 px in one frame** — more than a full variation-4 block (267 px), i.e. ~70% of the pattern's 533 px spatial period. The player sees: frozen frame (the stall — "a brief visual stutter"), then the wall layout instantly rearranged ("a different set of walls is shown"), which the eye re-locks onto within a beat ("lasts maybe 1-2 frames"). The 2008 game could never show this: it ran fixed-step 60 Hz on Xbox, where a slow frame produced several 16.7 ms catch-up updates, never one big step. The variable-step web port introduced dt values the game's physics was never designed for — this also lets bullets/enemies tunnel and skip during hitches.

## Design

1. **Clamp the world-visible dt in `Game1.UpdateCore`** to `DefaultMaxWorldDtMs = 100` (6 sim-frames):
   - Pure loss-of-time: the clamped remainder is dropped (KNI zeroes its accumulator per tick), so a ≥100 ms stall costs up to 400 ms of *game time* instead of a 360 px teleport (now bounded at 72 px on the fastest scroll). Ordinary 30-144 Hz play never reaches the clamp; only instantaneous sub-10 fps does.
   - **Skipped while `NetSession.Active`** — the co-op dead-reckoning assumes both worlds track real time, and a host that quietly loses time after its own hitch produces exactly the backward-correction class card 68f62e92 measured (`--hoststall`, 23 px rewinds). Same condition and same reason as the `?aiff` fast-forward exclusion.
   - Decision extracted as a pure static (`Game1.ClampedWorldDt`) so `logic_probe` covers it with no rig.
   - When it engages it says so: `[maxdt] clamped <raw>ms tick to <max>ms` — a clamp firing is rare and otherwise invisible, and the line is the probe surface (the `[hitch]` watchdog precedent).
2. **`?maxdt=<ms>` override** (`0` = off, restoring the shipped 500 ms behaviour — the `?netstaleguard=0` "deliberate bug reproduction" idiom). Value-carrying flag rules apply (reject + report bad values, `InForce`). In `DebugFlags.Active` only when overridden (it changes shared-run physics; the default path is inert in net sessions anyway).
3. **eahl rig: `stepdt <ms> [nodraw]`** — one frame at a caller-chosen dt. eahl's fixed-step loop can never produce a long tick, so this is the only headless way to look at one; it is what demonstrates the bug and verifies the fix.
4. **Probes**: `tools/headless/probes/maxdt_clamp.txt` (default boot: `stepdt 500` → the `[maxdt]` line + `eval WorldClock` advanced ~0.1 s) + `maxdt_clamp_off.txt` (`?maxdt=0` boot: no line, WorldClock advanced ~0.5 s). A boot pair because the flag parses at boot. `logic_probe`: `ProbeMaxDt` (default/pass-through/net-exempt/override/off cases) + a `?maxdt=` row in the flag-rejection sweep.

## Out of scope

- Fixed-step catch-up (multiple 16.7 ms sub-updates per long tick — the 2008 semantics). Strictly better physics but a much wider blast radius (every per-tick singleton pump); the clamp fixes the reported symptom.
- Hunting the specific stall on the reporter's machine (`[hitch]` + `?loadlog` in Chrome is the tool if it recurs; whatever stalls, the clamp bounds the damage).
- Draw-side raw-dt cosmetics (bomb ripple, slowmo-trail ease) — they keep the unclamped frame dt; a hitch advancing a ripple is invisible next to the world no longer teleporting.

## Verification

- `stepdt 500` frame pair mid-wall-run with `?maxdt=0` (360 px jump — the reproduced bug) vs default (72 px). 
- The probe pair above; `run_probes.py` green; `logic_probe` green.
- Chrome smoke: boots, plays, zero console exceptions.
