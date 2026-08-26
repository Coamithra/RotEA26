# Backlog — open tickets

13 cards, in board order.

## 1. `bec47239` — in the host room menu, the cancel button is overlapped by "start when your crew is aboard!" — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** ([#353](https://github.com/Coamithra/RotEA26/pull/353)). `ConfirmationMenu.DrawMenu` placed the
prompt and the rows from two independent fixed anchors, so a prompt taller than the gap between them
drew through them (measured 98px of overlap on the 8-line lobby panel, 120px on the 9-line one). It now
lays the two out as one composite — but only when the default layout would collide, so every short
prompt is pixel-identical. Pinned by `tools/headless/probes/confirm_prompt_layout.txt`.

_Further testing would be nice:_ the final foreground-Chrome smoke check was not possible here (no
browser/dev server in this environment); verified headlessly via `eahl` instead.

## 2. `ed32efe1` — let's make the explosion that is created when you respawn have its size level be equal to the level you had the "2" powerup at — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** ([#355](https://github.com/Coamithra/RotEA26/pull/355)). The respawn pop's reward bomb was a fixed level 3; it is now
`Score.GetPowerupLevel(Powerup.PowerupType.Linker, slot)` — `Linker` is the powerup the game draws
as **"2"**, and it is already the respawn powerup (its level buys `respawntimebonus`, i.e. a shorter
version of this very countdown). Level 0 is a legal small blast for a player who never picked one up.

The level is **latched when the summon is raised**, not read at the pop: the pop spawns the new ship
two lines earlier and `PlayerShip.Initialize` calls `Score.ResetPowerup`, so a read at the pop
measured 0 for a maxed "2" every time.

**Protocol v26**: the level rides `EvRespawn`. The reward blast is not itself replicated, so while
its level was a constant the two peers' copies matched by construction; re-deriving the new one from
a peer's own ~10 Hz `MsgHudState` view could disagree, and the blast kills. One byte restores the
identity.

New rig seams `eaRespawn.raise(slot)` / `eaPowerupLevel(slot,type,level)`; pinned by the probe pair
`respawn_reward_level.txt` + `respawn_reward_level_zero.txt` (mutation-tested three ways) and by five
new `NetRespawnTest` legs.

_Further testing would be nice:_ the co-op path is covered by the in-process wire self-tests, not by
a live two-machine session — multiplayer is not testable in this environment.

## 3. `f6fc1d97` — in multiplayer games, on the client, I can see 1 hp ufo's blink white before they blow up (the hit effect for enemies with multiple hit points). Is this a result of how the networking works? That a joining peer just shows the "enemy hit" animation until the host acknowledges that the enemy died? For 1 hp enemies I'd like the joining clients to just immediately blow the monster up and send a message to the host. The host trusts clients and then also blows up that enemy (of if it already had it just ignores the message since it's already dealt with). — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** ([#356](https://github.com/Coamithra/RotEA26/pull/356)). Diagnosed: the culprit is the HOST's `EnemyHitFlash` beat.
`KillableAlien.HitBy` announced it for *every* hit, and the announcement sits above the
`hitpoints <= 0` branch — so a lethal hit told the peer "flash", and an `EvDeath` a beat later
told it "explode". On a 1 hp enemy that is every kill. The beat is now sent only when the enemy
SURVIVES the hit.

The ticket's proposed mechanism ("clients blow it up immediately and tell the host; the host
trusts them, or ignores a duplicate") turned out to be **already how it works** — a client's
bullets hit-test puppets for real and run the real `HitBy`, so it kills locally and files an
`EvClaim`, which the host applies or pays from its recent-death record. Nothing needed rebuilding
there; the blink was the whole visible defect.

Review turned up a **second live instance** of the same defect: `SpiderHelperMothership.KilledBy`
only flags `dying` and never clears `Collides`, so the host keeps hitting it for seconds with hp
already at 0 — showing nothing itself — while the joiner's copy (tracked rather than released, so
`dead` is false and hp still positive there) flashed on every one of those beats. The shipped
predicate `hitpoints > 0` covers it; the plausible `(hitpoints <= 0) & !dead` would not.

The decisive argument, also from review: `isBlinking()` is `hittimer.Active & (hitpoints > 0)` —
**the host never draws a blink on its own killing blow**, so the beat was asking the joiner to draw
something no screen in the session was drawing. Send side and draw side now agree by construction.

Pinned by a new `NetFxTest` section 5 (a real HOST session, a real `Bullet` through the real
`CollidesWith`, reading the frames the peer actually received). Three legs, mutation-tested three
ways, each failing the legs that describe it — the already-dead leg is what separates the shipped
predicate from the rejected one.

_Further testing would be nice:_ verified over the in-process wire, not a live two-machine
session — multiplayer is not testable in this environment.

## 4. `085ebddc` — we should probably reduce the max magnitude of screenshake by 50% (so a global reduction by 50% across the board) — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** ([#357](https://github.com/Coamithra/RotEA26/pull/357)). All three peak constants halved: `Juice.MaxOffsetDesignPx` 7 → 3.5,
`MaxRollDegrees` 1 → 0.5, and the present blit's edge-covering zoom coefficient 0.06 → 0.03 — the
last of which moved into `Juice.MaxBlitZoom` from a literal at the blit, so a future halving cannot
take the offset and leave the swell behind. "Across the board" is what makes the zoom part of it:
what a player calls screen shake is the offset, the roll and the swell together.

New readback `eaShake.state()` / `eval ShakeState` reports the PEAK sampled since the last call.
That is the only honest observable — the offset and roll are re-rolled from a uniform random every
tick (so one frame is a sample, not a bound) and the effect is applied at the present blit, so it
moves no gameplay state and a screenshot of it is a frame of a moving thing.

Pinned by `tools/headless/probes/shake_peak.txt` (measured 3.15–3.34px / 0.44–0.48° / 0.0286 over
ten runs of a sustained burst, against a 3.5 / 0.5 / 0.03 ceiling). Mutation-tested four ways, each
reddening its own leg alone — including `Game1` dropping the zoom entirely, which an earlier
version of the probe was blind to because it recomputed the zoom peak from the constant instead of
reading what the blit drew.

Review derived the letterbox-coverage condition properly: `Z >= A/300 + (4/3)·radians(R)` = 0.0235
at the shipped values, so 0.03 is a **1.28× margin — the same factor the pre-card 7/1/0.06 had**.
The halving preserves the shipped safety factor exactly, which is the real argument for it; my
first comment claimed "~7× cover", which omitted the roll (half the budget) and would have licensed
a later editor to drop the zoom alone and put black at the frame edge.

_Further testing would be nice:_ the letterbox coverage is **derived and brute-forced, never
observed in a real window** — worth one look at a non-4:3 browser window during a big explosion.
The foreground-Chrome smoke check was also not possible here.

## 5. `c1cdd3e5` — On a joining client - while I was dead and respawning, the other players' ships (who respawned before me) did not appear on the playing field until mine did. — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** ([#358](https://github.com/Coamithra/RotEA26/pull/358)). Both puppet spawners gated on `FindLocalShip() != null`. Our own ship
being absent says nothing about whether *theirs* should be drawn — in co-op a death is not a world
wipe, the level keeps running, and the peer really is out there flying.

Both, deliberately: `ManagePuppet` draws the peer's primary ship and `TickFriends` draws its couch
players and the host's AI friends. All of those are "the other players' ships" to whoever is reading
the screen, and a room holds four machines plus couch seats, so a fix reaching only the first would
have left the report standing for most of a full room. (Review caught exactly that — the first round
of this change fixed `ManagePuppet` alone.)

The gate now also opens while **our own respawn summon** is up, and it must be ours and
non-cosmetic. The safety argument is not "every wipe purges the summon" — a joining client's own
`LoseLife` early-returns before its purges — it is that **every wipe arms a standing `Purge<T>`
filter, and the filter and that wipe's queued removals expire in the same `TopOfTickFlush`**. So a
summon still standing after a wipe means the filter that ate it is still armed, and `bin.TryAdd`
refuses the puppet regardless; that pairing is exact, and `TryAdd` was always the real guard.

One new visible behaviour, stated rather than hidden: `UpdateWin` does not purge for four seconds,
so if you die and a partner wins, their ship can now appear during the victory choreography.

Pinned by a new `NetResetSpawnTest` leg 1b — the pair of the existing leg 1, identical shiplessness
with one bit different — asserting both spawners and opening with one near miss per term of the
predicate (a real summon on another seat, then a cosmetic summon on our own). Mutation-tested five
ways, including the `ManagePuppet`-only shape; deleting the gate outright reddens legs 1 and 2
instead, so the suite bounds the fix from both sides.

_Further testing would be nice:_ verified over the in-process wire, not a live two-machine session.

## 6. `444eb614` — spider boss (lvl 2) - on a joining client, after the boss was defeated and its death animation played, the original sprite still appeared for a few frames — **DONE**

_No description or comments — the card title is the whole ticket._

**Done** (PR pending link). The client hands a dying boss its own death animation
(`NetPuppets.ReleaseDyingPuppet`), which drops the id from every map and marks it removed so the
world-snapshot **self-heal** will not rebuild it. That suppression ran on the flat 3 s
`RecentRemovalWindowMs`, justified in a comment with *"the host stops streaming the id within a
turn or two"* — which is false for exactly the deaths that reach it. A dying entity is still in
the host's world, so it stays in `NetIdRegistry.Live` and the host keeps streaming its id for the
**whole** animation. Measured, per type:

| type | dying duration | vs the 3 s window |
|---|---|---|
| Parachute | 100 ms | under |
| BattleSkull | 2500 ms | under |
| JunkBoss | 25 × 125 ms × U(0.8,1.2) | **straddles** (2500–3750) |
| FakeBoss | 4000 ms | over |
| MarsBoss (the survivor) | 5000 ms | over |
| SpiderBoss | `5000 / DifficultyFactorized(0.5)` | over (2941–7407) |
| **BrainBoss** | 20000 ms + a 300 ms fade | **over, and the longest** |

So at t=3 s the suppression lapsed, the next snapshot turn read as an unknown id, and the
self-heal built a fresh, **intact, collidable** boss standing where one was visibly dying. The
report names the spider boss because once `dead` its `Draw` shows only debris, so the rebuilt
puppet is the only thing on screen still wearing the boss's sprite — and its ghost lasts
`fall − 3 s`, i.e. "a few frames" on a save whose difficulty has ramped. On a `KillableAlien` it
is worse than cosmetic: hp arrives as 0, so the client plays a **second** death.

Released ids now get their own ledger and a longer window, and the real deadline is an **event**:
the host's `EvDeath` says its copy has left its world, so it has stopped streaming the id, and the
suppression is cleared there (and by a reliable `EvSpawn`, for an id the host re-used). The 30 s
window is only the backstop for an `EvDeath` that never comes. The long window is for released
deaths **only** — widening the flat one would leave a genuinely-missed spawn missing for thirty
seconds, which is its own leg in the suite.

**Review caught the first cut shipping 15 s**, which still ghosted the BrainBoss, and caught that
piggybacking the flag on `recentlyRemoved`'s 512-deep ledger would let the boss's *own* opening
purge evict its suppression. Both fixed. The constant is no longer defended by a census in a
comment: each boss leg ticks its real choreography to completion and requires the **measured**
duration to fit the window, so a future longer death fails there (the BrainBoss measures 20316 ms).

Pinned by `NetDeathFxTest` section 9's GHOST legs — the same call at three clock readings (inside
the flat window, past it, after the host's `EvDeath`), the `EvSpawn` clear on its own leg, and an
ordinarily-removed id as the control. Mutation-tested five ways; reverting the fix reproduces the
report in numbers (4 SpiderBosses in the world where there should be 3), and restoring the 15 s
constant reddens the BrainBoss leg alone.

_Further testing would be nice:_ verified over the in-process wire and the real component
`Update`s, not a live two-machine session — multiplayer is not testable in this environment. The
ledger's own 64-deep eviction cap has no leg (reaching it needs 64 concurrent deferred deaths) and
degrades to the pre-card behaviour, which shipped.

## 7. `8732568e` — multiplayer games (on a joining peer side) seem to have a lot of loud explosion effect sounds. I suspect we get a big packet with a bunch of dead enemies and play the sound a couple of times in the same frame perhaps? I propose either adding a function to our audio engine to limit the nr of sounds played at exactly the same time (to one) or create special case code just for the explosion sfx.

_No description or comments — the card title is the whole ticket._

## 8. `d44a49a4` — the respawning timer gfx need a bit of tweaking.

**Description / notes:**

1) the middle circle is pure black, obstructing the game - should be transparent (can be slightly darkened but very subtle)

2) the growing ring is a bit too thick, make it about 60% of what it is now.

3) Needs to have the color of the player who will respawn there (rather than pink)

4) the text is not nicely vertically centered rn. Needs to move down a bit.

## 9. `745728f9` — space mines (lvl 3, aka death stars) seem to also explore when they reach a dead player's location

**Description / notes:**

also the homing sound doesnt play for joining clients

## 10. `c600c55a` — mission failed in multiplayer dumps everyone back to the menu :)

**Description / notes:**

should be the same as when you beat a level, the host can select a new level (or the same) to try.

## 11. `5f506d11` — the "parade of alien rulers" in level 3 - the alien ruler animation is quite jerky on a joining client. We can use the same client-side logic to smooth that out that we do for other ufos etc

**Description / notes:**

Also the alein ruler's death animation (shrink + explode) keeps playing repeatedly if I pause the game. And it's spawning far too many explosions (this is on joining client, not sure if that's a network thing, perhaps the client spawns them itself and then receives them as well from the host through networking?)

## 12. `430494a7` — just before the overmind final boss, when flying through the blocks (walls) that lead up to the boss, in the middle of the wall section there is a brief visual stutter where a different set of walls is shown, looks like a section from previously in the game. Lasts maybe 1-2 frames.

_No description or comments — the card title is the whole ticket._

## 13. `51566427` — in a multiplayer match, as long as the host doesnt disconnect we don't need to end the game

_No description or comments — the card title is the whole ticket._
