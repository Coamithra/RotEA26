# Tracker: feature/boss-colorize-harness

Card 15d16282 — tool to view/tune the level-3 alienboss (BattleSkull) hue-remap colorize.

## Phase 1: Pick up
- [x] Move card Backlog -> In Progress
- [x] Pull main
- [x] Read card
- [x] Worktree wt4 + branch feature/boss-colorize-harness

## Phase 2: Research
- [x] Colorize path: sprite.fx COLORIZE = feathered hue-range remap; ColorizeRange=(min,max,target)/360
- [x] BattleSkull.Draw enables colorizeEffect with RangeTarget=(-10,10, HitPointsNormalized*100) — the hue-remap "lightbulb" boss
- [x] FakeBoss/ClassicBoss only do KillableAlien red death-tint (no hue-remap) — out of scope for hue params
- [x] Harness precedent: Blast gates a harness-only tweak on DebugFlags.Harness!=null + carries override params; HarnessScene draws a readout

## Phase 3: Design
- [x] Plan (below)

### Plan
Tool = colorize visualiser in the sprite harness for BattleSkull (the alienboss hue-remap boss).
- DebugFlags: HueStart, HueEnd, HueTarget (nullable deg), HueCycle (auto-sweep target 0..360), HueLoopSeconds.
  All null/default => in-game byte-identical.
- BattleSkull.Draw: when DebugFlags.Harness != null, apply overrides to RangeTarget
  (min<-HueStart, max<-HueEnd, target<-HueTarget or the cycle/HP). Gameplay path untouched.
- HarnessScene: on-screen readout of the live colorize params for the alienboss boss (like Blast readout);
  drive the cycle clock.
- harness.html: note the colorize params on the battleskull option + optional fields.
- CLAUDE.md: document the new flags + tool.

## Phase 4: Implement
- [ ] DebugFlags: parse ?huestart ?hueend ?huetarget ?huecycle ?hueloop
- [ ] BattleSkull.Draw override (harness-gated)
- [ ] HarnessScene readout + cycle clock
- [ ] harness.html
- [ ] CLAUDE.md

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] re-read full diff
- [ ] list what needs live testing

## Phase 6: Ship
- [ ] commit + push
- [ ] foreground review (or note "review pending: orchestrator")
- [ ] PR create --fill (PAUSE before merge)
- [ ] follow-up Backlog card for the user to tune with the tool
