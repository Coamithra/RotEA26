# AI boss standoff — stand at real bullet range, not centre range (card bb949dd9)

## Context

"AI: player seems to go way nearer bosses than he has to (given bullet range)."

Measured on `?level=Level2&marsboss` (eahl, Very_Hard, seed 1): the bot parks at `boss=171px` edge distance — exactly the card-b56633fb anchor `r* = bulletlifetime*0.78 - ThreatBodyTerm(boss) ≈ 175px`. The mechanism works as designed; the DESIGN is what undershoots. Both DoAIFire's range test (`nearestDist <= bulletlifetime * BulletRangePerMs`, a centre-to-centre test) and the anchor derived from it treat gun range as "bullet reaches the boss's CENTRE". A bullet dies after travelling `lifetime * 0.78px/ms` but connects when it ENTERS the boss's collision box — i.e. a hull half-extent earlier. For MarsBoss (box half-width ~124px) a shot from ~475px centre still lands, so the honest standoff is ~300px edge, not 175px. The bot stands ~124px (one boss half-width) closer than its bullets require, inside beam/bullet soup — deaths on that rig are dominated by `Lazer`.

## Design

One shared credit, used by the fire gate and the anchor so they cannot drift apart (the ThreatBodyTerm precedent):

- `PlayerShip.ShotReachCredit(baddy)` — the guaranteed hull-entry distance along any aim line through the centre: `min(Width, Height)/2` for a box (also multibox item 0), `Radius` for a circle, 0 otherwise. The MIN extent so the credit never claims a hit the geometry cannot deliver at any approach angle.
- `DoAIFire`: the in-range scan and the final fire gate become `dist <= gunRange + ShotReachCredit(target)` (per target).
- `DoAIMove`: `anchorPx = bulletlifetime * BulletRangePerMs + ShotReachCredit(haltingBoss) - ThreatBodyTerm(haltingBoss)`. `bossfar=` keeps meaning "cannot shoot from here" because both sides move together.
- Seam: `?aishotreach=<0..1>` scales the credit (`DefaultShotReachHullScale = 1`); `0` restores the pre-card centre-range test — the A/B arm. Standard value-carrying-flag rejection.

## Verification

- `logic_probe` `ProbeAiBossApproach`: anchor derivation updated (`+ entry` term); hull tuples gain an entry extent, swept at both 0 and radius so the band/crossing invariants hold with and without the credit. `ProbeAiFlagRejection` gains the `aishotreach` row.
- `tools/headless/probes/ai_boss_approach.txt` re-run (bounds are on idle%/bossfar which move WITH the anchor).
- `ai_sweep` (seeds 1-8 x2, honest about N in the PR): marsboss + spider rigs, `shipped=` vs `centre=aishotreach=0`. Gate direction: boss= up, deaths/time-to-victory not worse beyond SEM, `SpiderBoss(standing)` not up.
- `run_probes.py` green; clean build.

## Out of scope

- SpiderBoss avoidance/repellent tuning (not sought by design; standing-kill history in web CLAUDE.md).
- Aim-spread retuning (longer shots miss more at the extreme edge of the new band; measured via the sweep, not tuned here).
