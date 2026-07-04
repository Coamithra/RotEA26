# Tracker: fix/collisionhandler-dda-hang (card 7a3e70ad)

## Phase 1: Pick Up
- [x] Grab top Backlog card -> In Progress (atomic grab)
- [x] Pull latest main
- [x] Read the card + referenced code (CollisionHandler, Lazer, CollisionLine, SpiderHelperMothership)
- [x] Create worktree wt3 + branch fix/collisionhandler-dda-hang

## Phase 2/3: Research + Design
- [x] Confirm root cause: near-vertical Lazer (num tiny-nonzero) enters the val.X-exit diagonal
      branch; sub-ULP val.X step never advances -> infinite loop (100% CPU hang).
- [x] Lazer AND Bullet feed FillCollisionMatrixLine; only near-axis Lazers hit the degenerate case
      (bullets are short lines nowhere near the cap). Pure-axis (num==0 / num2==0) branches are safe.
- [x] Fix = guaranteed-termination iteration cap (card option b) on the DDA while-loops. Zero
      correctness regression (cap >> max real cells; degenerate line still marks correct cells first).
- [x] Remove the per-lazer FireTilt workaround in SpiderHelperMothership (card asks to).

## Phase 4: Implement
- [x] Add maxLineSteps cap + guard to FillCollisionMatrixLine loops
- [x] Remove FireTilt from SpiderHelperMothership; fire exactly PiOver2
- [x] Update CLAUDE.md (CollisionHandler cap; SpiderHelper bullet no longer references workaround)

## Phase 5: Verify
- [x] dotnet build -c Debug clean
- [x] Boot ?level=Level2&spiderboss&invuln&spiderhelperidle=3 -> helper fires straight down, hits boss, NO hang
- [x] Regression: existing UFO/boss lazers still collide (Level1/Level3)

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] pull main, rebuild
- [ ] PR + self-merge, confirm Pages green
- [ ] worktree cleanup, delete tracker
- [ ] card -> Done + comment + follow-ups
