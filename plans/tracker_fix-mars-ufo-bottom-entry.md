# Tracker: fix/mars-ufo-bottom-entry (card 47f0ead5)

Card: "Sometimes ufos on mars come in from the bottom which doesnt make sense as that's
supposed to be ground :) I couldve sworn I added code to deal with that but apparently not - fix it!"

Orchestrator overrides: slot wt5 (pre-assigned); NO live browser verification (build + diff
re-read + reasoning; list live-test items); skip interactive plan approval (conservative sound
fix); PAUSE before merging — report back, no `gh pr merge`, no card->Done, no worktree removal.

## Phase 1: Pick Up the Card
- [x] Move card 47f0ead5 to In Progress
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree wt5 + branch fix/mars-ufo-bottom-entry
- [x] Re-pull main after outage (674687e)

## Phase 2: Research
- [x] Read UfoSpawner.cs (port + src_decompiled — identical; no exclusion code was ever there)
- [x] Read UFO.cs (Initialize accelDir edge logic, Floor/Floorbottom collision)
- [x] Read Level2.cs / Demo2.cs (all Mars UfoSpawner call sites)
- [x] Read Floor.cs (ground line = 560; shadow/bounce zone y>=250)
- [x] Read StationarySpawner.cs / SweepUFO.cs / GenericSpawner.cs (no bottom path)
- [x] Simulate DoEvent spawn distribution (Python)
- [x] Enumerate ALL NewUFO call sites (BonusSpawner mars path OK: small-only, Y<=500,
      underside <=524, above ~540 terrain, uniform; InsaneBossI/Paratrooper checked)
- [x] Pin down root cause + write findings summary (below)

## Phase 3: Design
- [x] Draft approach (below — conservative, rides the existing `mars` level-type flag)
- [x] Check reusable patterns (mars flag already exists on UfoSpawner — fix lives behind it)

## ROOT CAUSE
`SetupMars`/`SetupMarsWest` set `num2 = 500f - num` — a sprite-size-aware cap meant to keep a
spawning saucer's underside above the ground — but the shared clamp applies `num2 + num`, so the
margin CANCELS: max spawn-center Y = 500 for every size. A big saucer (num=85 ~ half-size) can
enter centered at Y=500 with its underside ~585, BELOW the ground line (Floor.bottom=560,
terrain reads from ~540) — sliding in half-buried. And because Y is rand[0,600] CLAMPED to 500,
~1 in 5 mars entries pin at exactly that lowest point ("sometimes ufos come in from the bottom").
This is the code the user "could've sworn" they added: it exists, the `+num` defeats it.

## DESIGN
One change in `UfoSpawner.DoEvent`: after the existing clamp, when `mars`, re-roll
`val.Y = RandomNextFloat(0f, num2)` (uniform over the sky band; underside capped at y<=500).
- Mars entries are side-only (SetupMars/SetupMarsWest guarantee it), so Y is the free axis —
  X/edge selection, spawn COUNT and pacing are untouched (nothing dropped; one UFO per DoEvent).
- Level/data-driven: behind the existing `mars` flag, exactly how the code distinguishes
  ground levels. Space levels (default/ThreeDirectional/AsteroidChase) byte-identical.
- Also kills the 17-21% pile-up at the single lowest scanline (redistributed uniformly).
Out of scope (deliberate): the default TOP-entry spawners on Level2's SpiderBoss fight +
InsaneBossI + non-SetMars BonusSpawners — sky entry on a planet is natural and is not the
reported bug (faithful to the original Xbox build). BonusSpawner's mars path already spawns
small-only at underside <=524, above terrain — left as-is.

## Phase 4: Implement
- [ ] Edit UfoSpawner.DoEvent (mars Y re-roll + why-comment)
- [ ] CLAUDE.md: no documented contract changes — skip

## Phase 5: Verify (no live browser per override)
- [ ] dotnet build -c Debug clean
- [ ] Rigorous full-diff re-read + reasoning
- [ ] List what needs live/manual testing post-hoc

## Phase 6: Review & Ship (PAUSED at merge per override)
- [ ] Commit + push branch
- [ ] Peer review (Skill `review`; else meticulous self-review)
- [ ] Pull main into branch
- [ ] rtk gh pr create --fill
- [ ] STOP — report to orchestrator (no merge, no card move, no cleanup)

## Findings so far (Phase 2 notes)
- `UfoSpawner.DoEvent`: spawn = rand(0..800, 0..600) + AngleToVector(dir)*1000,
  clamped to [(-num,-num), (800+num, num2+num)]; num = 24 (small) / 85 (big);
  num2 = 500-num when `mars` else 600.
- Default entryDirections = [-pi/2] = TOP. SetupMars: mars=true, dir 0 (east/right edge).
  SetupMarsWest: mars=true, dir pi (west/left edge). SetupAsteroidChase (space only) adds
  pi/2 = bottom — not used on Mars.
- NO code path spawns a Mars UFO below the screen edge moving up. The "bottom" report maps to:
  (a) mars Y-cap: clamp max = (500-num)+num = 500 — the per-size margin CANCELS, so a big UFO
      (num=85, half-size ~85) can enter centered at Y=500 with its underside at ~585, i.e.
      below the ground line (Floor.bottom = 560) — it slides in half-buried in the ground.
      ~17% of mars spawns pin at exactly Y=500 (rand Y in [0,600] clamped to 500), so the
      low/buried entry is common, matching "sometimes". This is almost certainly the code the
      user "could've sworn" they added: `num2 = 500f - num` LOOKS like a size-aware ground
      margin but the `num2 + num` clamp cancels it.
  (b) Level2's SpiderBoss-fight reinforcements (3 spawners, lines 305/308/314) never call
      SetupMars/SetupMarsWest — they use the default TOP entry and skip the mars Y-cap
      entirely. Top = sky = visually fine on Mars, but they're the only Mars spawners
      without the mars flag (consistency question; scope TBD).
- Ground: Floor.bottom = 560 (landed ships sit at Y 545..560); Floor's collision (shadow +
  UFO bounce zone) spans y in [250, 1100]; Floorbottom at 560 kills UFOs moving down.
