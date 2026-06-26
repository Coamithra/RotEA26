# Tracker: fix/bomb-blast-fadeout

Card `10599b5419ae600d24561e3a` — "bomb seems to be active longer than sprite suggests
(smoothstep the blast fadeout?). Will need a separate visualization tool to tweak params
until it looks good to the human."

## Phase 1: Pick Up the Card
- [x] Atomic grab (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree (wt3) + branch fix/bomb-blast-fadeout

## Phase 2: Research
- [ ] Find the bomb / blast object code
- [ ] Trace the draw chain (sprite/alpha fadeout) vs the active/hit lifetime
- [ ] Identify the mismatch: how long blast damages vs how long it visually shows
- [ ] Decide on smoothstep fadeout + harness visualization

## Phase 3: Design
- [x] Draft approach (file-by-file)
- [x] Blast already in harness; needs a LIFETIME viz (loop + collision ring + readout)
- [x] Align with user (proceeding per "implement the top ticket"; final look shown via the tool)

### Root cause
1. SPATIAL: blast.png upscaled 384->576 (textureScale 1.5) but Blast.CollisionType uses raw
   `texture.Width * scale` not `DrawScale` -> hitbox grew 0.8x -> 1.2x the visible disc.
2. TEMPORAL: alpha = 1 - p^0.3 dims to ~half within first 10% of life, but collision stays
   active to ~50% -> "active longer than the sprite suggests."

### Design
- Blast.cs: refactor curve into ApplyLifecycle(p); smoothstep the fade (MathHelper.SmoothStep);
  collide while fade >= ActiveAlpha (default 0.5, ~ same active duration); CollisionType radius
  uses DrawScale * HitRadiusFactor (0.8). DebugFlags overrides BlastActiveAlpha / BlastHitFactor.
  Add internal HarnessApplyPhase(p, scaleMul) so the harness can drive the lifecycle frozen.
- HarnessScene.cs: when obj is Blast, loop blastPhase 0..1 over ~3s, draw the real collision
  ring (procedural texture) + a live readout (phase/alpha/scale/radius/ACTIVE + param values).
- DebugFlags.cs: ?blastactive= ?blasthit= ?blastloop= (+ doc).
- CLAUDE.md + harness.html: document the blast viz + flags + re-authored curve.

## Phase 4: Implement
- [x] Make the changes (Blast.cs, HarnessScene.cs, DebugFlags.cs, HarnessRegistry.cs)
- [x] Update CLAUDE.md (blast lifecycle + harness viz) + harness.html

## Phase 5: Verify
- [x] dotnet build -c Debug clean (0 errors, no new warnings)
- [x] Run + look via claude-in-chrome on port 5283 (?harness=blast loop: green/red ring, readout)
- [x] Level1 boot regression check clean
- [x] Zero console exceptions
- NOTE: live in-game bomb not fired (Mouse2-triggered, needs held bombs; not eaPress-reachable).
  Harness drives the REAL Blast object through the same ApplyLifecycle + CollisionType.

## Phase 6: Review & Ship
- [ ] Commit
- [ ] /review and fix findings
- [ ] Pull main into branch
- [ ] Re-build
- [ ] PR + self-merge
- [ ] Clean up worktree/branch
- [ ] Delete tracker
- [ ] Move card to Done + comment
- [ ] Follow-up cards if needed
- [ ] Overview for user

## Phase 7: Clean up
- [ ] Stop dev server, close Chrome tabs
