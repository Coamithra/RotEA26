# Tracker: perf/throttle-checkscreenshot

Card a03637f5 — Perf follow-up: throttle GameScene.checkScreenShot per-frame scan

## Phase 1: Pick Up
- [x] Grab top card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read card + code (GameScene.cs:792-876)
- [x] Worktree wt3 + branch perf/throttle-checkscreenshot

## Phase 2/3: Research + Design
- [x] Traced: checkScreenShot() runs every frame from UpdateNormal.
      After 5s snapshottimer expires, scans ALL Game.Components each
      frame computing weighted action count; >30 starts 800ms delay.
- [x] Design: throttle the scan to every N frames via a frame counter.
      Preserves >30 threshold semantics exactly; shifts trigger by <=N-1
      frames (immaterial vs 5s cadence + 800ms delay + randomness).
      N=6 (~100ms @60fps). No user tooling needed -> no "For me" card.

## Phase 4: Implement
- [ ] Add SnapshotScanInterval const + snapshotScanCounter field
- [ ] Throttle the scan block in checkScreenShot()

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run + console clean; screenshot capture still works (level thumbnail)

## Phase 6: Ship
- [ ] Commit, /review, PR, self-merge, cleanup, card -> Done
