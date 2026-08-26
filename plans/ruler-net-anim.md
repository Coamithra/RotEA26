# The level-3 alien ruler on a joining peer (card 5f506d11)

## Context

The card reports three things about the "parade of alien rulers" (`BattleSkull`, the level-3 mini-boss whose Cast entry is literally "Alien Ruler") as seen by a JOIN peer:

1. the ruler animation is "quite jerky" -- and the card names the fix: use the same client-side smoothing the other UFOs already get;
2. its death animation (shrink + explode) "keeps playing repeatedly if I pause the game";
3. it spawns "far too many explosions", with a guess that the client both spawns them itself and receives them from the host.

The guess in (3) is wrong -- `Explosion` is not a replicated type (`NetTypeRegistry`'s table is the enemy set minus cosmetics), so no explosion ever crosses the wire. Every one of these is a puppet-layer defect, and there are three of them, one per symptom. All three were reproduced with numbers before any code changed; the rig is `NetRulerTest` (`eaNetRuler()` / `eval NetRuler`), committed as `tools/headless/probes/net_ruler.txt`.

## The three defects, as measured

**A. The body animation steps once per snapshot turn.** `BattleSkull` draws its `alienboss` sheet from `animationProgress`, its own 20 fps accumulator advanced in `Update` -- and a puppet's `Update` never runs. So the host replicates the integer frame in a state extra and the client assigns it. That is exactly what `AlienDrawableGameComponent.NetFrameLocal` exists to stop for `curframe`, one field over: a free-running loop must be owned by the client, because the snapshot lane is unordered and unsequenced and because a turn is 60 ms in a small world and up to ~1.2 s in a big one. Measured over 60 ticks with a 150 ms turn: the host advanced on 20 ticks in steps of 1, the puppet on 6 ticks in steps of 3.

**B. A release under a pause runs the death animation through the freeze.** `NetPuppets.ReleaseDyingPuppet` hands a dying puppet back its own `Update` by setting `Enabled = true`, and its own comment recorded the cost as a KNOWN, ACCEPTED DIVERGENCE: a release landing while a `ComponentBin.Push` pause is up enables the entity outside any pause layer, "so its dying animation runs on through the freeze". Measured: **+40 explosions** and a full shrink-and-flicker on a screen where nothing else is moving. It is not cosmetic-and-invisible the way that comment assumed -- it is the only thing moving, so it is the only thing you look at.

**C. A long pause resurrects the dead ruler, and it dies again.** Card 444eb614 gave released ids their own suppression ledger so the world-snapshot self-heal cannot rebuild an entity that is visibly dying here, with a 30 s backstop for "an id whose EvDeath never comes". A PAUSE is precisely the case that backstop cannot survive: both worlds freeze, so the host's copy never finishes ITS animation, never emits `EvDeath` and keeps streaming the id forever -- while the ledger's clock is real time and runs on regardless. Measured: past the window the entry reports `Rebuilt`, a fresh intact collidable ruler appears, its snapshot hp arrives as 0 and the client plays a SECOND death. Then that one is released, re-stamped, and the whole thing repeats one window later, for as long as the pause lasts. That is (2) verbatim, and it is most of (3): each repeat is another ~40 explosions.

C predates the pause, too: before card 444eb614 the window was 3 s, and `BattleSkull`'s dying state is 2.5 s -- so in ordinary play it just fit, and a pause was the only way to push it over. That is why the report ties the repeat to pausing.

## Design

**A -- own the loop locally.** `Compat/Net/NetBodyAnim.cs` states the rule once and carries the audit; `BattleSkull`, `ClassicBoss` and `FakeBoss` each override `NetDriveExtras` to advance `animationProgress` on the driver's real dt, and their descriptors stop applying the wire frame. The byte is still ENCODED, so the wire is unchanged and no protocol bump is needed -- the `NetFrameLocal` precedent exactly, where `NetBaseState.CurFrame` still ships and is simply ignored by types that own their frame.

THE AUDIT IS THE WHOLE RISK, and it is the same two questions `NetFrameLocal` asks. A type may own its loop only if (i) nothing but `Draw` reads the accumulator, and (ii) it really is a free-running loop at a CONSTANT fps that no state machine writes. All four types with a `NetAnimFrame` seam were checked:

| type | accumulator | verdict |
|---|---|---|
| `BattleSkull` | `+= dt * 20f`, unconditional in `Update`; read only by `Draw` | LOCAL |
| `ClassicBoss` | same | LOCAL |
| `FakeBoss` | same | LOCAL |
| `SpiderBoss` | written outright by the rear-up/launch/land choreography (`animationProgress = 0f` in four places), `animFps` varies, `currentAnimation` swaps between four sheets, and `Update`/`DoMove` READ it (`animationProgress > 30f` gates the walk) | STAYS REPLICATED |

**B -- adopt into the pause layer.** `ComponentBin.PauseAdopt(GameComponent)` puts a component into the innermost live pause layer (leaving it disabled) and answers false when no pause is up. `ReleaseDyingPuppet` asks for that before falling back to `Enabled = true`, so a release during a pause freezes with everything else and `Pop` starts it -- which is what `Pop` already computes correctly, since a released puppet is no longer a frozen puppet.

**C -- the deadline is the event, full stop.** The released-dying ledger loses its duration and becomes a membership set. It is still cleared by the two things that actually END it -- the host's `EvDeath` (its copy has left its world, so it has stopped streaming the id) and a successful `EvSpawn` for a re-used id -- plus the session/level `Reset`, and it is still bounded by its own 64-deep FIFO. The window was never the mechanism; its own header says so ("THE HONEST DEADLINE IS AN EVENT, NOT A DURATION"), and a duration cannot be right for a suppression whose end condition is another machine's world advancing.

`NetDeathFxTest.CheckFitsReleaseWindow` goes with it. It asserted each boss's measured animation fits inside the constant, which was a real guard while a longer death would ghost; with no duration there is no length that ghosts, so the assertion is vacuous rather than merely redundant.

## Out of scope

- `SpiderBoss`'s replicated frame (see the audit above -- correct as it stands).
- The pause's other divergences on a joining peer (the snapshot/event lanes keep draining while the world is frozen, by design -- that is what keeps a pause from stalling the peer).
- Anything about the ruler's own FX rate. The flicker series is authored (`Lerp(8, 24, ...)` per second over 2.5 s, ~40 pops) and both peers run the same one; the card's "far too many" is defect C multiplying it, not the design.

## Verification

`eaNetRuler()` / `eval NetRuler` -- the suite built for this card, menu-only and leave-no-trace, in the `eaNetDeathFx` shape. Three sections, each with the negative beside the positive:

1. the body animation: a real host `BattleSkull` ticked at 60 Hz as the CONTROL, against a puppet driven for the same span with a snapshot turn every 150 ms -- asserts the puppet advances as often as the host and never in a multi-frame step;
2. a release landing under a live `ComponentBin.Push` -- asserts the animation does NOT run and the ruler is still there to finish dying when the pause lifts;
3. a released ruler ticked to its own death, then the clock advanced past the old window with the host still streaming hp==0 -- asserts `LeftDead` rather than `Rebuilt`, no replacement, no second death; with an unknown live id beside it asserting the self-heal itself still works.

Plus: `eaNetDeathFx` (the whole release mechanism), `eaNetEntity`, `eaNetSnap` unchanged; the committed probe suite; and the Chrome smoke check.
