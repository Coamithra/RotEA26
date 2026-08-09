using System;
using System.Collections.Generic;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class PlayerShip : AlienDrawableGameComponent
{
	public delegate void CollectPowerupEvent(Powerup.PowerupType powerup);

	private const int shotspersecdefault = 8;

	private const int shotspersecmax = 18;

	private const float bulletlifetimedefault = 450f;

	private const float bulletlifetimemax = 1500f;

	private const float bulletlifetimeperpowerup = 70f;

	private bool asplodeOnNextFrame;

	private ICollidable asplosionCauser;

	private Timer pacifistTimer = new Timer(90000f, repeating: false);

	private bool isTutorial;

	private int player;

	private float hue;

	private int shotspersec;

	private float startdir;

	private Texture2D gloweffect;

	private float bulletlifetime;

	private int respawntimebonus;

	private float asplodingbulletspercentage;

	private float asplodingbulletssize;

	private float bouncebulletspercentage;

	private int bounceamount;

	private int bulletsSplit;

	private Powerup.PowerupType currentPower;

	private bool haspower;

	private PowerupEffect powerupEffect;

	private Blast blast;

	private bool readyToConnect;

	private List<ShipConnector> connectors = new List<ShipConnector>();

	private bool hasWon;

	private List<Option>[] options;

	private Timer invulnerabilityTimer = new Timer(2500f, repeating: false);

	public Vector2 TopLeft;

	public Vector2 BottomRight;

	private CollisionBox boundBox;

	private Timer shoottimer;

	private Timer starttimer;

	private ControlDevice controller;

	private DeathEvent deathEvent;

	private int optionLevel;

	// ---- ship motion model -------------------------------------------------------------------
	// The 2008 values, promoted from bare literals in ResetShip/Update to named consts (card
	// ada9e839). Nothing about the flight model changed -- these exist because the AI's arrival
	// deadzone is SIZED off them: `Move(null, ...)` applies deceleration alone, so a ship at full
	// speed coasts 0.5 * MaxSpeed^2 / Deceleration = 11.3px before it halts, and the deadzone has
	// to cover that or the ship crosses it and oscillates. logic_probe's ProbeAiFieldComposition
	// asserts exactly that relation, which it can only do if the two numbers are reachable.
	public const float ShipMaxSpeed = 0.33f;

	public const float ShipDeceleration = 0.0047999998f;

	public const float ShipAcceleration = 0.003f;

	// ---- AI tuning (card f4d1721f) ---------------------------------------------------------
	// Repo convention: baked Default* consts + nullable ?ai* overrides in DebugFlags, so a
	// shipped build with no query string is byte-identical to one with these consts inlined.

	// Low-pass time constant for the AI's steering vector. BAKED OFF (0 = fly the raw sum every
	// tick, exactly as 2008 did -- the original DoAIMove had no persistent steer state at all).
	// The 90ms blend was a port addition against heading jitter when big terms nearly cancel
	// (~1050 deg/s inside a Level-3 wall), but its memory works both ways: when a force
	// DISAPPEARS -- the station pull cutting off inside its arrival deadzone -- the blended
	// vector keeps thrusting along the stale heading for ~125ms, ~40px at full speed, which is
	// the owner-reported veer-past-and-come-back on every level entry (iterative rep 1). Killed
	// on the owner's ruling; `?aismooth=<ms>` (with `?aismoothurgent=`) restores the blend as
	// the A/B arm, and the adaptive-blend machinery below is kept for exactly that.
	public const float DefaultSteerSmoothMs = 0f;

	// The player's own death is the biggest impact in the game, so it gets a real freeze frame
	// (Compat/Juice.cs) on top of the two explosions' shake. Named rather than literal because
	// NetResetSpawnTest's hit-stop control has to request the SAME duration to be a control at
	// all -- a third unlinked copy of 0.18f would drift silently. Refused outright inside an
	// online co-op session; see Juice.AddHitStop for why.
	public const float DeathHitStopSeconds = 0.18f;

	// Smoothing floor, used when the push is strong (see the adaptive blend in DoAIMove).
	// 0 with the blend baked off above -- the adaptive lerp runs from SteerSmoothMs down to
	// this, so a nonzero floor under a zero ceiling would turn smoothing back ON under
	// pressure, inverted. Set both or neither; `?aismoothurgent=` is the restore arm.
	public const float DefaultSteerSmoothUrgentMs = 0f;

	// The demand either side of which smoothing is at full / at the floor.
	private const float SteerCalmDemand = 2f;

	private const float SteerUrgentDemand = 9f;

	// THE BLANKET EQUILIBRIUM FLOOR -- 2008's own line, at 2008's own place (the very end of
	// DoAIMove): `if (direction.Length() <= 0.2f) direction = Vector2.Zero;`. A steer that has
	// cancelled to noise is full throttle in an arbitrary direction, because Move() keeps only
	// the angle; at or below the floor the ship holds still instead.
	//
	// HISTORY, so nobody re-derives either dead end: the port first raised this to 0.95 -- above
	// the 0.8 seek, a veto that deleted every deliberate destination (card ada9e839 restored
	// 0.2) -- and then split it into a repellents-only cancellation floor plus this one, so
	// opposing pushes could be zeroed before an attractor joined the sum. The split was retired
	// by owner ruling (iterative rep 1, "too smart for its own good"): one floor, whole sum,
	// exactly as shipped in 2008. Known residual: two repellents whose >0.2 resultant flips
	// direction over a few px can still rattle the ship -- accepted; a probe-ahead scheme is
	// logged as a someday, not built.
	//
	// The boss approach deliberately fades DOWN through this floor near firing range -- that is
	// what widens its crossing into a parked band (card b56633fb; ProbeAiBossApproach pins it).
	public const float DefaultSteerNoiseFloor = 0.2f;

	private static float SteerSmoothUrgentMs => EvilAliensWeb.Compat.DebugFlags.AiSteerSmoothUrgentMs ?? DefaultSteerSmoothUrgentMs;

	private static float SteerNoiseFloor => EvilAliensWeb.Compat.DebugFlags.AiSteerNoiseFloor ?? DefaultSteerNoiseFloor;

	// How far ahead the wall logic looks, as MILLISECONDS of closing travel rather than a fixed
	// pixel count. The 2008 code probed `1.2 * dtMs * MaxSpeed` = ~6.6px at 60Hz against wall
	// tiles that are 800/gridWidth = 67..267px wide -- a FIFTH of a ship-width of warning, which
	// is why the bot clipped so much. Closing speed is ship speed plus the wall's own scroll.
	//
	// The ~13.75px figure this comment used to quote (card d79b7ea7) is a different probe: it is
	// `41.67 * MaxSpeed`, the 2008 hard clamp's, which the port KEPT as `WallClampMs`. Attributing
	// it here credited the replacement with replacing something that was never replaced.
	public const float DefaultWallReactionMs = 420f;

	// A gap must beat the COMMITTED one by this many tiles of cost before the AI switches. The
	// old code re-decided left-vs-right every tick, so a wall scrolling by one row could swap the
	// cheaper side and reverse the ship mid-approach, forever. Hysteresis is what turns a gap
	// choice into a plan.
	public const float DefaultGapSwitchMargin = 1.5f;

	// Rows of grid looked at when judging a column. Four rows is 267..1067px of wall depending
	// on grid width -- past that the wall has usually scrolled into a different shape anyway.
	public const int DefaultWallScanRows = 4;

	// Cost added per blocked column the ship would have to cross to reach a gap.
	public const float DefaultWallCrossPenalty = 4f;

	// How far ahead a moving threat is projected when judging it. Radial "how far is it right
	// now" repulsion pushes the ship ALONG the path of anything crossing the screen -- which is
	// exactly the spider boss's screen-wide sweep. Steering by closest approach instead moves the
	// ship off the line before it arrives.
	public const float DefaultThreatLeadMs = 700f;

	// A level-halting boss competes at this fraction of its true distance when the AI picks a
	// target, so it outranks the trash the boss itself keeps spawning.
	public const float DefaultPriorityTargetBias = 0.45f;

	// Random error added to every shot's aim angle (JunkBoss excepted -- it gets exact aim). Was a
	// bare local in DoAIFire; promoted to a named const + ?aiaim (card c10e3e7f).
	// NOT the value every tier uses -- this is the VERY_HARD row of AiSkillByDifficulty, which is
	// why it is not named Default* like the tier-independent knobs above. Kept as `Math.PI / 12f`
	// rather than ToRadians(15f) so the anchor row is bit-for-bit what card f4d1721f measured.
	public const float VeryHardAimSpreadRad = (float)Math.PI / 12f;

	private static float SteerSmoothMs => EvilAliensWeb.Compat.DebugFlags.AiSteerSmoothMs ?? DefaultSteerSmoothMs;

	private static float WallReactionMs => EvilAliensWeb.Compat.DebugFlags.AiWallReactionMs ?? DefaultWallReactionMs;

	private static float GapSwitchMargin => EvilAliensWeb.Compat.DebugFlags.AiGapSwitchMargin ?? DefaultGapSwitchMargin;

	private static int WallScanRows => EvilAliensWeb.Compat.DebugFlags.AiWallScanRows ?? DefaultWallScanRows;

	private static float WallCrossPenalty => EvilAliensWeb.Compat.DebugFlags.AiWallCrossPenalty ?? DefaultWallCrossPenalty;

	private static float ThreatLeadMs => EvilAliensWeb.Compat.DebugFlags.AiThreatLeadMs ?? DefaultThreatLeadMs;

	private static float PriorityTargetBias => EvilAliensWeb.Compat.DebugFlags.AiPriorityBias ?? DefaultPriorityTargetBias;

	// DIFFICULTY-SCALED (with ThreatFieldBasePx below); see AiSkillByDifficulty.
	private static float AimSpread => EvilAliensWeb.Compat.DebugFlags.AiAimSpreadRad ?? Skill.AimRad;

	// Bullet travel per ms of its lifetime -- i.e. `bulletlifetime * this` is how far a shot
	// reaches. The 0.78 factor is the 2008 range test in DoAIFire, named here because the
	// boss-approach ANCHOR r* is derived from it, so "close until you can shoot" and "a shot
	// reaches this far" cannot drift apart.
	private const float BulletRangePerMs = 0.78f;

	// ---- THE BOSS-APPROACH ATTRACTOR (card b56633fb) ----------------------------------------
	//
	// WHAT WAS WRONG. The approach used to fly at a geometric standoff point
	// (clamp(gunRange * 0.6, 130, 300) = 211px centre = 41px EDGE at the base weapon) carrying a
	// CONSTANT weight, DefaultSeekApproachWeight 1.1. That 1.1 was never calibrated against
	// anything: it was picked to sit above the 0.95 whole-sum park, which card ada9e839 has since
	// deleted. At the very point the ship was asked to reach, the boss's own repellent is
	// 4*(1-41/406)^3 = 2.9 -- so the net force at the destination pointed AWAY, by a factor of
	// 2.6, and `bossfar` read ~99% forever. A destination inside your own repellent is not a
	// destination.
	//
	// THE SHAPE, and why it is INVERTED. A(d) grows with edge distance and quiets to ~0 inside
	// firing range -- the opposite of every other falloff in this file, and deliberately so:
	//   * outside r* the attractor always OUTWEIGHS the repellent, because A is climbing while
	//     repel is decaying. The outweigh invariant holds by SHAPE, not by a solved constant that
	//     a Range powerup could invalidate;
	//   * at r* the two are EQUAL by construction (that is what the weight is solved for), so the
	//     net crosses zero exactly at the distance the ship can shoot from -- in the case where the
	//     repellent is still audible there at all; where a Range powerup has put r* beyond the
	//     field's own radius, repel is 0 and the weight floors instead, so the term keeps closing
	//     until there is something to balance against;
	//   * inside r* the attractor has quieted and the boss repellent pushes back out, which makes
	//     the equilibrium self-limiting rather than a point the ship has to hit.
	// There is no deadzone and no standoff radius. The whole-sum floor (DefaultSteerNoiseFloor
	// 0.2) then turns the crossing into a BAND -- |A - repel| <= 0.2 reads as "hold still" -- and
	// that band has to be wider than the ship's 11.3px stopping distance or it coasts through and
	// pingpongs. Width is 0.4 / (|A'| + |repel'|); at the shipped numbers (Very_Hard, base weapon:
	// r* = 181.3px edge, w = 0.678, |repel'| = 0.00905/px, |A'| = w/r* = 0.00374/px) that is
	// **31px**, i.e. 2.7x the stopping distance. Swept over every tier and the whole
	// bulletlifetime range by logic_probe's ProbeAiBossApproach, which is where the bound lives.
	//
	// KNOWN LIMIT -- THE BAND ASSUMES THE TWO FORCES ARE COLLINEAR, AND FOR SEATS 3/4 THEY ARE NOT.
	// The radial threat push is emitted at `VectorToAngle(...) + dodgeAngle`, a per-slot rotation
	// that fans co-op ships apart: +-PI/16 for players 0/1, +-PI/6 for players 2/3. The attractor
	// points straight at the boss, so at the crossing the two equal magnitudes cancel to
	// 2*w*sin(dodge/2) rather than to zero -- 0.13 at PI/16 (under the 0.2 floor, so the band is
	// real) but 0.35 at PI/6, where there is no parked band at all and such a ship orbits instead.
	// Stated rather than solved: every measurement here and every AI rig is slot 0, four AI ships
	// is a case nothing exercises today, and rotating the attractor to match would change the
	// meaning of "aim at the boss" for the human-facing seats too.
	//
	// EXPONENT 1 -- A is LINEAR in edge distance, and this is a CEILING rather than the exponent
	// itself (see the damping below). The band width is what constrains it: |A'(r*)| = k*w/r*, and
	// the card's arithmetic puts the usable ceiling near 0.02/px, which k=1 clears by 5x at the
	// shipped configuration. Linear also keeps "quiet inside r*" honest without a second mechanism
	// -- at half firing range A is w/2 against a repellent of 1.9. Note both of those are the k=1
	// reading: where the damping below bites, A falls off far more gently inside r* (at k=0.24,
	// A(r*/2) is 0.85w) and "quiet" means only that the repellent still wins there, which the probe
	// asserts directly rather than taking on the shape's word.
	private const float BossApproachExponent = 1f;

	// Anchor floor. r* is derived (gun range minus the boss's own body term), so a boss whose hull
	// is bigger than the weapon's reach would drive it to zero or below -- and a tiny r* is what
	// makes |A'| = w/r* explode and collapses the band. Floored at 3x the 11.3px stopping distance:
	// below that the ship cannot hold a standoff there anyway, and the band bound is verified AT
	// the floor rather than assumed away.
	// IT IS THE FIRST RESORT, NOT THE WHOLE ANSWER: it can only rescue an anchor that has gone to
	// zero, and the measured failure is a boss with a LIVE anchor of ~100px, which the floor never
	// touches. That case is what the exponent damping below exists for. Raising this floor instead
	// was measured and rejected: covering it needs ~115px, and asking the ship to stand 115px
	// clear of a hull that wide parks it OUTSIDE gun range -- reinstating the never-shoots failure
	// this whole term exists to remove.
	private const float BossApproachMinAnchorPx = 34f;

	// SAFETY FACTOR on the band bound: the parked band must be at least this many stopping
	// distances wide. 1x would be the bare "does not coast straight through"; 2x is what the
	// exponent is solved against, so a band sits comfortably clear of the bound rather than on it.
	private const float BossApproachBandMargin = 2f;

	// Ceiling on the attractor's GROWTH away from the boss (see the return of BossApproachWeight
	// for why it does not bind the anchor), so it can never out-vote a full-strength threat
	// field (the structural bound ProbeAiFieldComposition asserts about every seek: Move() keeps
	// only the ANGLE, so a seek that can beat the field is a bot that flies into things to reach
	// them). Only reachable far off-screen at the shipped numbers.
	private const float BossApproachMaxWeight = 3.5f;

	// How far down the screen the "UFOs spawn here" danger band reaches, and how hard it pushes.
	// Strong enough to stand up to a lane escape, so the ship settles below the spawn line
	// instead of being held against it.
	//
	// A PORT ADDITION with no DEDICATED counterpart in 2008: the original's only top-edge term is
	// the generic 150px/strength-4 screen-bound push, which this port still ships verbatim a few
	// lines above (`edgeMargin`/`maxSteerStrength` in DoAIMove). So `?aitopedgestrength=0` does
	// not disable an edge push, it restores the 2008 treatment exactly -- which is what makes it
	// the null arm rather than a mutilation. Card 2248e5eb; verdict recorded in
	// web/EvilAliensWeb/CLAUDE.md.
	public const float DefaultTopEdgeDangerPx = 170f;

	private static float TopEdgeDangerPx => EvilAliensWeb.Compat.DebugFlags.AiTopEdgeDangerPx ?? DefaultTopEdgeDangerPx;

	// BAKED OFF (owner ruling, iterative rep 1 lap 3): the band's 20 was calibrated to out-vote
	// upward shoves of up to 18 from the cone/wedge era, which is retired -- the strongest
	// upward force left is a plateau field's 4, and 20 buried the powerup near-field's ~4.8
	// pull five times over (it re-broke every top-edge pickup the moment the yield bandaid was
	// reverted). The generic top-edge plateau (max 4) is the whole protection now, as in 2008.
	// `?aitopedgestrength=<n>` restores a band for the A/B; the 170px ramp shape is unchanged.
	public const float DefaultTopEdgeAvoidStrength = 0f;

	private static float TopEdgeAvoidStrength => EvilAliensWeb.Compat.DebugFlags.AiTopEdgeAvoidStrength ?? DefaultTopEdgeAvoidStrength;

	// Same, for the descent/climb column (the boss's standing box is 240px wide).
	private const float VerticalLaneClearancePx = 240f;

	// How far from the centre of the boss's telegraphed lane the AI wants to be. The lethal band
	// is a ~187px third of the screen, so this clears it with room to spare.
	private const float SweepLaneClearancePx = 210f;

	// Must beat the station pull, a powerup detour and the edge pushes combined: the whole third
	// of the screen is off limits while the boss is in play, and being in it is simply a death.
	private const float SweepLaneAvoidStrength = 18f;

	// How many big UFOs to leave alive during the SpiderBoss fight -- see DoAIFire.
	private const int SpiderBossLaserPlatforms = 2;

	// THE 2008 MAGNITUDES, RESTORED ON MEASUREMENT (card 2248e5eb). The port had widened the beam
	// field to 260px / strength 14 and added a 7-strength lateral sidestep, all three unmeasured.
	// Against the 2008 arm (150 / 4 / no sidestep) at N=60 paired the port values LOSE, and lose
	// on the rig built around the beam: on `?level=Level2&spiderboss` the 2008 arm is **-4.55
	// +- 0.69** deaths (8.67 -> 4.12) with victories 3/60 -> 24/60, and the mechanism is legible
	// in the killer histogram -- `SpiderBoss(standing)` 290 -> 6. An oversized beam field was
	// shoving the ship off the beam and into the stationary boss, which is the very failure card
	// b56633fb was filed for. Level 1 prefers the port values (+0.98 +- 0.65) and that is a real
	// but far smaller loss, stated rather than hidden. The port values stay reachable as
	// `?ailazerpx=260&ailazerstrength=14&ailazerdodge=7`, which is the negative control.
	//
	// WHAT IS *NOT* RESTORED, and it is a stated confound of that A/B: the CURVE FAMILY. 2008 ran
	// MyMath.PowerCurve, the classic max*(1-t^2) plateau; this term keeps the port's (1-t)^p
	// spike, so at 150px it pushes less than the original did at the same range. Card 05a2b818
	// ruled on the family GLOBALLY and decisively (?aifieldcurve=classic costs +11.37 SpaceDodge /
	// +10.20 CrazyGame deaths), so re-opening it per-type would re-litigate a settled ruling.
	// These are the 2008 MAGNITUDES inside the validated port shape, and the arm that was measured
	// is exactly the configuration that ships.
	public const float DefaultLazerAvoidRangePx = 150f;

	private static float LazerAvoidRangePx => EvilAliensWeb.Compat.DebugFlags.AiLazerAvoidRangePx ?? DefaultLazerAvoidRangePx;

	public const float DefaultLazerAvoidStrength = 4f;

	private static float LazerAvoidStrength => EvilAliensWeb.Compat.DebugFlags.AiLazerAvoidStrength ?? DefaultLazerAvoidStrength;

	// Lateral push while a big UFO is winding up, to make its locked-at-fire aim stale. A port
	// invention with no 2008 counterpart, OFF at the baked default per the verdict above.
	// `?ailazerdodge=7` brings it back for anyone re-opening the question.
	public const float DefaultLazerDodgeStrength = 0f;

	private static float LazerDodgeStrength => EvilAliensWeb.Compat.DebugFlags.AiLazerDodgeStrength ?? DefaultLazerDodgeStrength;

	// Station-keeping "arrive" behaviour, and THE anti-pingpong mechanism for the seek attractor
	// (card ada9e839). Inside this radius the pull is switched off entirely, so the ship coasts
	// the last stretch instead of thrusting at a point it is already on top of, sailing past it
	// and turning round -- the visible idle fidget.
	//
	// IT IS SIZED BY STOPPING DISTANCE, not by taste. `Move(null, ...)` applies deceleration
	// alone, so a ship entering the deadzone at full speed travels
	// 0.5 * ShipMaxSpeed^2 / ShipDeceleration = 11.3px before it halts. Any radius comfortably
	// above that is stable (the ship cannot coast out the far side); the radius is what decides
	// how close to the target it comes to rest. 2008 used 10px -- i.e. the author sized it to
	// exactly this figure -- and the port widened it to 30 while the 0.95 park was in force,
	// which is a confounded measurement, since a lone 0.8 seek was being zeroed outright and
	// could not fidget.
	//
	// HYSTERESIS SINCE ITERATIVE REP 1 (owner ruling): the single radius above is replaced by the
	// standard two-threshold arrival -- the pull PARKS once the ship is within SeekParkPx and
	// stays parked until the distance opens past SeekResumePx (target moved, ship was shoved
	// away, or the seek switched to a different destination, which resets the latch via its
	// kind). Two radii, one bit of state, no per-target ids: a target swap or a moving target
	// shows up as the distance opening past the resume radius all by itself.
	//
	// THE BOUND MOVES TO THE RESUME RADIUS. A ship crossing the park edge at full speed coasts
	// the 11.3px stopping distance; with one radius that had to fit INSIDE the zone (hence the
	// old 15). Under hysteresis the park radius may sit below it -- the ship comes to rest
	// somewhere inside the RESUME radius, and only re-engages if pushed clear out of it. So the
	// invariant ProbeAiFieldComposition pins is SeekResumePx > SeekParkPx + 11.3: a full-speed
	// arrival halts at ~park+11.3, which must still be inside the resume zone or the latch
	// re-triggers on its own momentum. 8/20 gives 0.7px of margin; watch it if either moves.
	public const float DefaultSeekParkPx = 8f;

	public const float DefaultSeekResumePx = 20f;

	// ?aiseekdeadzone= keeps its name and now drives the PARK radius; ?aiseekresume= is its pair.
	private static float SeekParkPx => EvilAliensWeb.Compat.DebugFlags.AiSeekDeadzonePx ?? DefaultSeekParkPx;

	private static float SeekResumePx => EvilAliensWeb.Compat.DebugFlags.AiSeekResumePx ?? DefaultSeekResumePx;

	// Kept at the 2008 weight so the seek still loses to threat avoidance exactly as before.
	private const float SeekWeight = 0.8f;

	// ---- seek weights for a target the bot CHOSE (cards ada9e839 / 31ceb6ff) ----------------
	//
	// THE BUG THAT WAS HERE, AND HOW IT IS GONE. Every deliberate destination in DoAIMove rides
	// ONE `steerTarget` carrying ONE weight: the idle station, a powerup, a level-halting boss,
	// a partner to dock with, a blastable cluster. The port ended DoAIMove with a
	// 0.95 "park" that zeroed the whole steer whenever it came out at or below that -- so a lone
	// 0.8 seek produced NO MOTION AT ALL and the bot simply did not go anywhere unless something
	// else happened to be pushing that tick. That is the whole of "the AI is uninterested in
	// powerups", and it is why the boss-approach term card f4d1721f added was correct code the
	// park deleted.
	//
	// Card ada9e839 removed the park rather than tuning around it: attraction and repulsion now
	// compose properly (repellents are summed and floored on their own, attractors are never
	// floored, and each attractor's anti-pingpong mechanism is its own DEADZONE). So these
	// weights are RELATIVE authority within the sum now, and nothing here has to clear a
	// threshold to be heard at all.
	public const float DefaultSeekPowerupWeight = SeekWeight;

	// THE LEVEL-HALTING BOSS APPROACH (cards 31ceb6ff -> b56633fb). There is no constant weight
	// here any more -- see BossApproachExponent above for the shape that replaced
	// DefaultSeekApproachWeight, and BossApproachWeight for the solve. The two claims that const
	// carried are now properties of the curve: it is above SeekWeight wherever the bot is
	// deliberately closing (a halting boss is a COMMITMENT -- nothing in the level advances until
	// it dies -- while a powerup is a DETOUR), and it is bounded below the threat field's 4 by
	// BossApproachMaxWeight.
	// The multiplier below is what `?aiseekapproach=` now sets: 1 = the solved weight.
	public const float DefaultBossApproachScale = 1f;

	private static float SeekPowerupWeight => EvilAliensWeb.Compat.DebugFlags.AiSeekPowerupWeight ?? DefaultSeekPowerupWeight;

	// A SCALE on the solved anchor weight since card b56633fb, NOT the weight itself -- the flag
	// key is unchanged so the rejection sweep and the flag list are, but a value from before that
	// card means something else entirely and the two are not commensurable.
	private static float BossApproachScale => EvilAliensWeb.Compat.DebugFlags.AiSeekApproachWeight ?? DefaultBossApproachScale;

	// How far out a powerup exerts its own direct pull. The 2008 code reused `steerRange`, which
	// is really the screen-EDGE margin -- two unrelated quantities that happened to be equal, so
	// retuning either silently moved the other. Named separately for that reason, and BAKED AT
	// THE 2008 VALUE: widening it to 300 was tried as the other half of card ada9e839's fix and
	// MEASURED INERT (Level 1, N=16: 95% collected at 150 vs 94% at 300 -- the seek above already
	// takes the ship there, and this term only shapes the last stretch of the approach). Left as a
	// knob rather than folded back into steerRange so the next person can sweep it honestly.
	public const float DefaultPowerupReachPx = 150f;

	private static float PowerupReachPx => EvilAliensWeb.Compat.DebugFlags.AiPowerupReachPx ?? DefaultPowerupReachPx;

	// Fraction of a wall tile across which co-op AI ships fan out inside their shared gap.
	private const float GapSeatSpreadFraction = 0.5f;

	// Clearance the AI wants beyond ANY threat's hull, before the size term below.
	// NOT the value every tier uses -- this is the VERY_HARD row of AiSkillByDifficulty, hence
	// the name rather than Default* like the tier-independent knobs.
	//
	// 150 IS THE 2008 BASE, restored (card 05a2b818). The port shipped 190, and the audit split
	// this formula's two parameters and measured them separately: the BASE is refuted (190 costs
	// CrazyGame -2.67 deaths paired against 150, N=60) while the SIZE SCALE below is validated.
	// So what ships is neither era's formula -- 2008's base with the port's size term -- and it
	// survives on MEASUREMENT, not on either doctrine: zero significant losses on any of the five
	// rigs, and the best spider-rig victory count of every arm tried. The full table is in
	// web/EvilAliensWeb/CLAUDE.md; the arms are reachable as ?aifieldpx= x ?aifieldsize=.
	public const float VeryHardThreatFieldBasePx = 150f;

	// Extra clearance per pixel of the threat's own half-extent. The spider boss gets a field
	// several times a bullet's.
	//
	// VALIDATED SEPARATELY FROM THE BASE (card 05a2b818), and it is the half of the formula that
	// earns its keep: dropping it to 0 -- i.e. the 2008 flat field -- costs the spider rig +1.28
	// deaths and 3 of its 4 victories (big-UFO kills 117 -> 187), because a flat field is nothing
	// next to a 90px-half UFO. It is also what makes BrainBoss expensive (a ~250px hull draws a
	// ~600px field), which is the trade the base reduction above partly pays for. Do not "simplify"
	// it away to a flat number: that experiment is this card's og150 arm and it loses.
	public const float DefaultThreatFieldSizeScale = 1.8f;

	// Exponent of the (1-t)^p falloff. Higher = the field bites later and harder.
	public const float DefaultThreatFieldFalloff = 3f;

	// PER-TYPE repellent scale for ASTEROIDS (card ada9e839). Multiplies the asteroid's own
	// repulsion -- radial field and closest-approach evade alike -- and nothing else's.
	//
	// WHY A TYPE-SPECIFIC KNOB AND NOT A GLOBAL ONE. Measured on SpaceDodge (the asteroid-belt
	// challenge), an asteroid's radial term contributes a MEAN of 0.42 against the 0.8 seek it
	// has to out-vote, so the bot correctly computes that a powerup across the belt is worth the
	// trip and is then correct all the way into the rock. The belt is the one place in the game
	// where a dense field of lethal-on-contact obstacles must out-argue an ordinary detour, and
	// the global alternative was already measured and DECLINED: `?aifieldfall=` 3 -> 2 made
	// CrazyGame deaths 6.19 -> 7.75 and SpiderBoss(standing) deaths 41 -> 53. Steeper mountains
	// HERE, rather than a special case that stops the bot wanting the powerup at all -- threat
	// awareness belongs in the repellent's shape, never in a gate on another force.
	// The other two axes of the SAME per-type field (card ada9e839). Magnitude alone only makes
	// the same short mountain taller, which is what ejected the ship out of the belt into the
	// UFO traffic around it; RANGE and FALLOFF change its SHAPE. A wider, shallower asteroid
	// field raises the mean pressure the bot feels across the belt -- which is what has to beat
	// the 0.8 seek -- without the steep close-range shove.
	// Range is a MULTIPLIER on ThreatFieldRange; falloff REPLACES the exponent in (1-t)^p, where
	// lower = bites earlier and more gently.
	// LIMIT: the shape axes reach the MAIN path only. The `altSteering` branch overwrites the
	// falloff-shaped strength with a linear Lerp, so under it ?aiasteroidfall= does nothing while
	// ?aiasteroidscale= still bites. Harmless today -- `altSteering` is a dead 2008 local that is
	// never set true -- but a sweep run under a revived alt path would be measuring half a config.
	// ---- DIRECTIONAL REPELLENT SHAPES: the velocity cone and the lane wedge (card e425781b) ----
	//
	// WHAT PROBLEM THIS SOLVES, because three attempts at the obvious answer failed first. A
	// circular field can only say "I am here"; it cannot say "I am about to be THERE". Measured on
	// SpaceDodge, the bot's mean edge distance from an asteroid is 252px while the radial field
	// falls under the 0.8 seek at 199px -- so the ship spends its life OUTSIDE every warning
	// perimeter, and no amount of magnitude, range, falloff or curve-family tuning moves that
	// (cards ada9e839 and e88e21ca between them swept all four and reached 3/8 victories against a
	// 4/8 base). The deaths are mid-field lane collisions, which is geometry, not strength.
	//
	// THE SHAPE. Every mover projects a MESA along its own velocity: full strength across the body
	// it is sweeping, tapering with distance ahead, cheap to either side. So a repellent's
	// meaningful domain starts at the collision EDGE (inside is death, and curve values there are
	// wasted dynamic range) and extends along the TRAJECTORY, which is where the ship actually is.
	// It is universal on motion -- asteroids, bullets, UFOs and the spider boss's sweep all fall
	// out of the same evaluation with no per-type code, because a fast mover automatically
	// projects a long cone. See AddSweptRepellent.

	// Cone LENGTH: `speed * lead * (bodyRadius / ref)` -- a time horizon SCALED BY THE MOVER'S
	// SIZE (owner ruling, iterative rep 1 lap 5). The reference is the regular UFO's ~20px body
	// term (bench-derived: its field range read r176 = 150 + 1.8 * 14.4 half-extent, x sqrt2),
	// and the lead is cut from the inherited 700ms to a third so the regular UFO's cone lands at
	// ~33% of its pre-ruling length. So: a mover the UFO's size projects `speed * 233ms`, one
	// N times its size projects N times that, and a bullet's needle nearly vanishes (its body
	// barely exists -- the circle and the standard field carry it). ?aiconelead= still sweeps
	// the time constant live.
	public const float DefaultConeLeadMs = 233f;

	// The size reference the length ratio is taken against (the regular UFO's body term).
	public const float ConeLeadRefRadiusPx = 20f;

	// Ceiling on that length. 800 is the design field's own width, past which the shape is off
	// screen and cannot describe anything; it exists so a very fast mover (a bullet) does not
	// project a cone longer than the world.
	public const float DefaultConeMaxLenPx = 800f;

	// THE MESA IS GONE (owner redesign, iterative rep 1 lap 5). The cone used to be a bespoke
	// field -- separate along/across falloff exponents, a taper, a magnitude scale, a flat
	// 300px skirt with an optional size-scaled variant -- seven knobs, each individually swept.
	// It is now a SHAPE, not a field: the mover's own repulsion circle capped by a triangle to
	// `position + velocity * lead`, and the push is the ordinary threat field evaluated on the
	// NEAREST-FEATURE distance to that shape (the getDistanceToLine treatment the Lazer has
	// always had, extended to one more shape). Behind the base: radial from the circle --
	// the same push the radial branch computes, so a stationary mover degenerates to exactly
	// the 2008 circle and there is no more circle-plus-hat double counting. Beside the
	// triangle: normal push off the near edge. Past the apex: radial from the tip. Inside:
	// full strength, out the near side. One curve, one reach, both the threat's own
	// (ThreatFieldRange + the per-type family), and the only shape parameters left are the
	// two above (lead time and length cap).

	// ---- the LANE WEDGE ----
	// A symmetric cone is WRONG for a mover whose path hugs a screen edge: it offers the gap
	// between path and edge as an escape, and that gap is a trap -- the ship dodges into it and is
	// crushed against the wall as the mover arrives. So when the swept band leaves too little room
	// on one side, the shape becomes asymmetric: everything between the path and the hugged edge
	// is closed at full strength, and the only downhill direction is out of the lane.
	// WHICH EDGE IS SCREEN GEOMETRY, not a type test -- it is whichever side the swept band leaves
	// less than a survivable gap on, so the spider boss's three fixed lanes and its
	// sweep-to-the-right-edge landing all resolve themselves.

	// Peak wedge magnitude. Held at the value the hand-rolled spider lane escapes used, so that
	// replacing them is a change of SHAPE and not of strength -- it still has to beat the station
	// pull, a powerup detour and the edge pushes combined, because the whole band is simply death.
	public const float DefaultLaneWedgeStrength = 18f;

	// The wedge's own along-axis exponent. Same plateau family as the cone's; separate because the
	// wedge runs the full length of the play field rather than a speed-scaled cone length, so the
	// two are shaping very different spans.
	public const float DefaultLaneWedgeFallAlong = 2f;

	// Baked off in the iterative rep-1 sweep to establish the 2008 baseline, then REINTRODUCED
	// by owner ruling the same session once the baseline was seen playing ("they will fix a
	// lot") -- the cones ride on top of the classic plateau fields now, which is a configuration
	// no earlier measurement covered. `?aicone=0` / `?aiwedge=0` are the off arms.
	// EvadeMovingThreat stays off (superseded by the cones per the same ruling).
	private static bool ConeEnabled => EvilAliensWeb.Compat.DebugFlags.AiConeShapes ?? true;

	private static bool LaneWedgeEnabled => EvilAliensWeb.Compat.DebugFlags.AiLaneWedge ?? true;

	private static float ConeLeadMs => EvilAliensWeb.Compat.DebugFlags.AiConeLeadMs ?? DefaultConeLeadMs;

	private static float ConeMaxLenPx => EvilAliensWeb.Compat.DebugFlags.AiConeMaxLenPx ?? DefaultConeMaxLenPx;

	private static float LaneWedgeStrength => EvilAliensWeb.Compat.DebugFlags.AiLaneWedgeStrength ?? DefaultLaneWedgeStrength;

	private static float LaneWedgeFallAlong => EvilAliensWeb.Compat.DebugFlags.AiLaneWedgeFallAlong ?? DefaultLaneWedgeFallAlong;

	// ---- per-difficulty AI skill (card c10e3e7f) -------------------------------------------
	// One bot drives the attract demos, the Mechanical Friends cheat and ?aiplayer, and until
	// this card it had ONE set of constants -- it played identically on Easy and Inzane.
	//
	// ABSOLUTE final values per tier (the WebcamLevel.Tunings[] idiom): no DifficultyModifier
	// divisor and no within-run ramp, so a bench run is reproducible. The Very_Hard row holds the
	// VeryHard* consts above, which keeps the configuration card f4d1721f actually measured
	// exactly where it was measured.
	//
	// The spread is deliberately SUBTLE, and that is a design constraint rather than caution:
	// a Mechanical Friend that visibly cannot play defeats the point of having one. "Worse"
	// here means aiming a little looser and giving threats a little less room -- never a bot that
	// reads as broken. Expect the gradient to show on ?aibench and not to the eye.
	// Consequence to be honest about: only the ENDS of the ladder differ by enough to matter
	// (22.5deg vs 11.25deg of aim). Adjacent middle rows are ~2deg apart, far below the ~4x change
	// it took to move a metric at all, so Medium-vs-Hard is a smooth interpolation rather than a
	// difference anyone could measure. That is intended, not an oversight -- but do not claim the
	// middle tiers play differently.
	//
	// ONLY these two scale, and that is a MEASURED result, not a judgement call. Four knobs
	// were tried; each was isolated by holding the tier (and so the level's own difficulty
	// scaling) fixed and moving one ?ai* override, since comparing tiers end-to-end cannot
	// separate the pilot from the enemies:
	//   AimRad         Level1, 15deg -> 57.3deg  : progress 50/64 -> 45/64.  KEPT
	//   FieldPx        spiderboss, 190 -> 30px   : deaths 11 -> 14.          KEPT (weak)
	//                  (that pair is PARK-ERA and its 190 is no longer the anchor -- it survives
	//                   only as evidence of the knob's DIRECTION, which card 05a2b818 confirmed
	//                   is non-monotone between 150 and 190. Do not quote the numbers.)
	//   WallReactionMs OwnLevel, 420 -> 600ms     : victories 25 -> 0 of 30.
	//                                               NOT TIERABLE (card 21bb6849)
	//   ThreatLeadMs   CrazyGame, 700 -> 200ms    : victories 23 -> 14 of 30.
	//                                               NOT TIERABLE (card 21bb6849)
	//
	// **The last two are NOT excluded for being inert.** They were, originally, on one run each
	// (n=1) that happened to pick the one rig where each is inert -- card b174b00f retired that
	// verdict and showed both have large authority. Card 21bb6849 then ran the tuning campaign
	// that retraction called for (eahl, Very_Hard, ?invuln OFF, N=30, 300 sim-s) and still leaves
	// them out, for a DIFFERENT reason: neither has a band that is at once WORSE, SUBTLE and
	// MONOTONE, which is what a difficulty ladder needs.
	//   WallReactionMs -- below the anchor is not a degradation at all (80ms matches 420ms on
	//     survival and churns LESS), so the only degrading direction is a LONGER look-ahead,
	//     which models nothing recognisable as a novice. That direction has a ~130ms usable band
	//     and then a cliff: on OwnLevel 550ms already fails the level in 14 of 30 runs and 600ms
	//     in 30 of 30.
	//   ThreatLeadMs -- the response around the anchor is a broad shallow plateau; nothing within
	//     +-200ms of 700 is distinguishable at N=30. The nearest measurably-worse value is 200ms,
	//     a 3.5x change one step above total collapse (80ms wins 0 of 30) -- and at 200ms the knob
	//     is inert on SpaceDodge, Level3 and Level1, so the row would change exactly one level.
	// Full tables and the rigs: web/EvilAliensWeb/CLAUDE.md, the per-tier skill bullet. **Don't
	// re-add either without re-running that campaign**, and mind the instrument caution that came
	// with it: `contacts` is floored by ClampIntoWallSpace -- the hard override runs regardless of
	// how far ahead the bot looked -- so wall look-ahead only shows up where `turn` can carry it.
	// Their consts and ?aireact/?aithreatlead overrides are untouched from card f4d1721f.
	//
	// The steering smoothing / park demand are excluded for a different reason -- jitter and
	// idle fidget are the BUGS f4d1721f fixed, so degrading them reproduces a defect instead of
	// modelling a novice. Nor does PriorityTargetBias scale: degrading it stops the bot
	// prioritising the boss that HALTS the level, and a demo that never progresses is worse
	// than one that plays badly.
	// Field names stay SHORT and distinct from the resolver properties above (FieldPx, not
	// ThreatFieldBasePx): `ThreatFieldBasePx => ... ?? Skill.ThreatFieldBasePx` reads as infinite
	// recursion at a glance even though the two names live in different scopes.
	private readonly struct AiSkill
	{
		internal readonly float FieldPx;

		internal readonly float AimRad;

		internal AiSkill(float fieldPx, float aimRad)
		{
			FieldPx = fieldPx;
			AimRad = aimRad;
		}

		// Aim given in DEGREES, which is the readable form for the ladder. The anchor row does not
		// use this: ToRadians(15f) is not guaranteed to be the same float as Math.PI/12f, and
		// Very_Hard has to stay bit-identical to what card f4d1721f measured.
		internal static AiSkill Deg(float fieldPx, float aimDegrees)
		{
			return new AiSkill(fieldPx, MathHelper.ToRadians(aimDegrees));
		}
	}

	// Indexed by Settings.DifficultyLevel (Easy, Medium, Hard, Very_Hard, Inzane). Aim is in
	// DEGREES so the ladder is inspectable at a glance; the anchor row alone is passed in radians.
	// Inzane's FIELD is deliberately NOT pushed past the anchor: every measurement has bracketed
	// this knob at or below it, so shrinking it is evidence-backed while GROWING it is pure
	// extrapolation -- and ThreatFieldStrength's own note warns a bigger field is a trade-off, not
	// a free win (the bot still has to close in to shoot). Card 05a2b818 sharpened that from a
	// caution into a result: 190 was measured against 150 and LOST. Inzane earns its edge on aim
	// only.
	//
	// THE FIELD COLUMN IS RESCALED, NOT RE-MEASURED (card 05a2b818). That card moved the anchor
	// 190 -> 150, which would otherwise have collapsed the ladder outright -- the old Easy row was
	// itself 150, so Easy would have equalled Very_Hard and the two middle rows would have come
	// out BETTER than the anchor. Every field row is therefore multiplied by 150/190 (~0.789),
	// which preserves each tier's spacing relative to the new anchor and nothing more. The AIM
	// column is untouched.
	// It is a rescale rather than a fresh sweep on purpose: the doc's own argument stands that
	// tier-vs-tier cannot be measured end-to-end (the ENEMIES scale with the same tier, so an
	// outcome delta between tiers is unattributable), and only the ANCHOR row has evidence behind
	// it. So do not read the lower rows as measured values -- they are the old proportions.
	// PER-TIER SKILL RETIRED (owner ruling, iterative rep 1). 2008 flew one fixed skill at every
	// difficulty -- aim spread PI/12, field range 150 -- and the ladder below never had evidence
	// beyond its anchor row (the file's own rescale note admitted the lower rows were
	// proportions, not measurements). Every tier now flies the Very_Hard values; the table is
	// kept commented for the day a measured ladder is wanted again.
	//   Easy AiSkill.Deg(118f, 22.5f) / Medium .Deg(129f, 19.5f) / Hard .Deg(139f, 17f)
	//   / Very_Hard (anchor) / Inzane .Deg(anchor, 11.25f)
	private static readonly AiSkill FixedSkill = new AiSkill(VeryHardThreatFieldBasePx, VeryHardAimSpreadRad);

	// One skill at every tier since the ladder retired (above); the ?aiaim/?aifieldpx overrides
	// still win downstream, which is how any future ladder would be measured.
	private static AiSkill Skill => FixedSkill;

	// For the ?aibench readout. The RESOLVED values (overrides applied), so the bench line answers
	// "which skill row am I actually flying?" directly instead of leaving it to be inferred from
	// noisy outcome counters -- the tier lookup is the whole mechanism of card c10e3e7f, and every
	// end-to-end metric that could confirm it is confounded by the ENEMIES scaling with the same
	// tier. This is the only non-confounded observation of it.
	internal static void GetAiSkillReadout(out float fieldPx, out float aimRad)
	{
		// The field is the flat standard 150 for everything now (owner ruling, lap 7).
		fieldPx = SweptFieldRangePx;
		aimRad = AimSpread;
	}

	// An impact this close, this centred, gets a steer strong enough to beat every other term.
	private const float ThreatPanicMs = 260f;

	private const float ThreatPanicMissFraction = 0.55f;

	private const float ThreatPanicStrength = 16f;

	// Below this (px/ms) a threat is not a "mover" and the plain radial repulsion models it
	// better. The player ship's own MaxSpeed is 0.33 px/ms, so this is about a third of that.
	private const float ThreatMinSpeed = 0.1f;

	// Clearance the AI wants past the threat's own half-extent when judging a predicted miss.
	private const float ThreatMissMargin = 90f;

	// Even a far-off but dead-on collision course deserves some steer, or the AI would ignore
	// everything until it was nearly too late.
	private const float ThreatUrgencyFloor = 0.35f;

	// Wall steering weights. These sit well above the generic steer terms (maxSteerStrength 4)
	// on purpose: inside a wall the gap is the only survivable place to be, and a stray powerup
	// pull must not drift the ship out of the slot it is threading.
	private const float WallLateralIdle = 3f;

	private const float WallLateralUrgent = 14f;

	// Downward hold-off while a blocked row closes and the ship is still off its gap -- buying
	// the time the lateral move needs. Positive Y is down (screen coords).
	private const float WallBackOff = 6f;

	// Weight of one row of clearance in ColumnScore, relative to one tile of sideways travel.
	// Deliberately large: crossing the whole screen is worth it to be somewhere survivable.
	private const float WallRowWeight = 8f;

	// The emergency clamp's horizontal reach, in ms of travel: about one tick at 60Hz, which is
	// the range where a hard reversal is genuinely right and cannot alternate.
	private const float WallClampMs = 42f;

	// The clamp reaches further UP, because the wall closes on the ship whether or not the ship
	// is moving toward it (the 2008 code used the same 3x factor).
	private const float WallClampUpFactor = 3f;

	// Smoothed steering vector (see DefaultSteerSmoothMs) and the committed wall gap.
	private Vector2 aiSteer = Vector2.Zero;

	private int aiGapColumn = -1;

	// Process-wide one-shot for the `[aiwallnav] steering:` line. Static rather than per-ship so
	// four co-op ships announce once between them; it is a diagnostic, not state, and nothing
	// reads it back.
	private static bool aiWallNavAnnounced;

	// Names the wall-steering algorithm that ACTUALLY RAN, once per process (card d79b7ea7).
	//
	// It is called from INSIDE `SteerThroughWall` and `SteerThroughWall2008` rather than from the
	// dispatch that chooses between them, and that placement is the whole point -- an earlier
	// revision read `DebugFlags.AiWallNav2008` at the call site, so it reported the FLAG and not
	// the branch. Inverting the dispatch left it printing "2008" while the port code ran, and the
	// probe pair that exists to catch exactly that passed the mutation. An audit arm whose
	// dispatch is broken measures the shipped build twice and prints a plausible table with a
	// small difference in it; this line is the only thing that can say otherwise.
	private static void AnnounceWallNav(string which)
	{
		if (!aiWallNavAnnounced)
		{
			aiWallNavAnnounced = true;
			Console.WriteLine("[aiwallnav] steering: " + which);
		}
	}

	public int Owner => player;

	public ControlDevice Controller => controller;

	// A live ship changes driver without respawning (card e6927ef8: a real pad taking over
	// TeamChallenge's auto-pilot partner seat). `controller` is a copy taken from the oracle in
	// Setup, so the oracle's own seat must be re-pointed too -- Oracle.SetController is the
	// caller's other half. The AI's accumulated steering state is dropped: leaving a stale
	// smoothed steer behind would have the human's first frames fighting the bot's last vote.
	internal void AdoptController(ControlDevice device)
	{
		controller = device;
		ResetAiState();
	}

	public int OptionLevel => optionLevel;

	public override ICollisionType CollisionType
	{
		get
		{
			boundBox.TopLeft = base.Position + TopLeft;
			boundBox.BottomRight = base.Position + BottomRight;
			return boundBox;
		}
	}

	public event CollectPowerupEvent OnCollectPowerup;

	public PlayerShip(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/playersheet", 4, 8, 1, 6f));
		interpolationOptions = InterpolationOptions.always;
		base.DrawOrder = 20;
		boundBox = new CollisionBox(Vector2.Zero, Vector2.Zero);
		starttimer = new Timer(520f, repeating: false);
		shoottimer = new Timer(125f, repeating: true);
		shoottimer.Stop();
		AddTimer(shoottimer);
		AddTimer(starttimer);
		AddTimer(invulnerabilityTimer);
		options = new List<Option>[2];
		options[0] = new List<Option>();
		options[1] = new List<Option>();
		deathEvent = PlayerShip_OnDeath;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent is Option)
		{
			List<Option>[] optionLayers = options;
			foreach (List<Option> list in optionLayers)
			{
				if (list.Contains((Option)(object)e.GameComponent))
				{
					list.Remove((Option)(object)e.GameComponent);
					RedressOptions();
				}
			}
		}
		if (e.GameComponent == powerupEffect)
		{
			powerupEffect = null;
		}
		if (e.GameComponent == blast)
		{
			blast = null;
		}
		if (e.GameComponent is ShipConnector && connectors.Contains((ShipConnector)(object)e.GameComponent))
		{
			connectors.Remove((ShipConnector)(object)e.GameComponent);
			if (connectors.Count == 0)
			{
				readyToConnect = false;
			}
		}
		if (e.GameComponent == this)
		{
			this.OnCollectPowerup = null;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		gloweffect = content.Load<Texture2D>("GFX/Sprites/singleconnectorglow");
	}

	private void RedressOptions()
	{
		List<Option>[] optionLayers = options;
		foreach (List<Option> list in optionLayers)
		{
			for (int j = 0; j < list.Count; j++)
			{
				float angle = (float)j * ((float)Math.PI * 2f) / (float)list.Count;
				list[j].SetAngle(angle);
			}
		}
	}

	private void PlayerShip_OnDeath(object sender)
	{
		// A PUPPET's respawn is its owner's business, and we already draw a cosmetic summon off
		// its EvRespawn announcement -- raising a local one here would double it up, and would
		// run a countdown for a ship this peer does not decide the respawn of. Normally
		// unreachable (NetSession.ExplodePuppet takes a puppet out WITHOUT Die() for exactly
		// this reason), so this is the guard for every other way one could be killed.
		if (IsNetPuppet)
		{
			Console.WriteLine("[respawn] summon suppressed slot=" + player + " (net puppet)");
			return;
		}
		// Card 37f3a663: raise the respawn indicator only when somebody else is still flying.
		// Otherwise this death is a WIPE and GameScene.LoseLife purges the summon on the next
		// tick -- one frame of countdown, which is the "looks a bit broken" the card reports.
		// See PlayerShipSummon.ShouldSummon for why this counts IsDead rather than membership.
		int otherLiveShips = CountOtherLiveShips();
		if (!PlayerShipSummon.ShouldSummon(otherLiveShips))
		{
			Console.WriteLine("[respawn] summon suppressed slot=" + player + " (no other live ship)");
			return;
		}
		PlayerShipSummon playerShipSummon = PlayerShipSummon.NewPlayerShipSummon(collection, base.Game);
		playerShipSummon.Setup(player, startdir, base.Position, respawntimebonus);
		collection.Add((GameComponent)(object)playerShipSummon);
		Console.WriteLine("[respawn] summon slot=" + player + " ms=" + playerShipSummon.DurationMs
			+ " others=" + otherLiveShips);
		// Online co-op: tell the peer, so it draws the same indicator and knows its buddy is
		// coming back and where. No-op unless a session is up and this ship is ours.
		EvilAliensWeb.Compat.Net.NetSession.OnLocalRespawnSummon(this, base.Position, playerShipSummon.DurationMs);
	}

	// Player ships other than THIS one that have not themselves died. `Die()` only queues the
	// removal, so at OnDeath time the oracle's list still holds this ship -- and, when two ships
	// go in the same tick, the other one too. `IsDead` is what tells them apart.
	private int CountOtherLiveShips()
	{
		int live = 0;
		foreach (PlayerShip s in oracle.GetShips())
		{
			if (s != this && !s.IsDead)
			{
				live++;
			}
		}
		return live;
	}

	public Vector2 GetPosition()
	{
		return base.Position;
	}

	public void SetPosition(Vector2 newposition)
	{
		base.Position = newposition;
	}

	public override void Draw(GameTime gameTime)
	{
		if (hue != -1f)
		{
			spriteBatch.colorizeEffect.Enable();
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(180f, 250f, hue);
		}
		if (oracle.Players == 1 && haspower)
		{
			spriteBatch.colorizeEffect.Enable();
			if (currentPower == Powerup.PowerupType.OneUp)
			{
				// WorldTime, not gameTime: a Draw-time hue cycle on the raw clock kept the OneUp
				// rainbow rolling while the world sat frozen in a pause (card d79a2f48).
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * WorldTime.Seconds % 360f);
			}
			else
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(10f, 360f, Powerup.PowerUpHue(currentPower));
			}
		}
		if (invulnerabilityTimer.Active & (MyMath.Mod(invulnerabilityTimer.TimeElapsed, 100f) <= 50f))
		{
			spriteBatch.lightenEffect.Enable();
		}
		if (readyToConnect)
		{
			spriteBatch.BlendMode = (SpriteBlendMode)2;
			spriteBatch.Draw(gloweffect, base.Position, 0f, 1f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/singleconnectorglow", gloweffect.LogicalWidth()), center: true, Color.White);
			spriteBatch.BlendMode = (SpriteBlendMode)1;
		}
		base.Draw(gameTime);
		spriteBatch.lightenEffect.Disable();
		spriteBatch.colorizeEffect.Disable();
	}

	public void Setup(int player, Vector2 position, bool startup, bool invulnerable, float startdirection)
	{
		pacifistTimer.Reset();
		pacifistTimer.Start();
		startdir = startdirection;
		base.Position = position;
		if (startup)
		{
			starttimer.Start();
		}
		else
		{
			starttimer.Stop();
		}
		this.player = player;
		controller = oracle.Controller(player);
		hue = oracle.Hue(player);
		if (invulnerable)
		{
			TemporaryInvulnerability();
		}
		else
		{
			invulnerabilityTimer.Stop();
		}
		bounceamount = 1;
		bulletsSplit = 0;
		bouncebulletspercentage = 0f;
		asplodingbulletspercentage = 0f;
		shotspersec = 8;
		bulletlifetime = 450f;
		List<Option>[] optionLayers = options;
		foreach (List<Option> list in optionLayers)
		{
			list.Clear();
		}
	}

	public void SetTutorial()
	{
		isTutorial = true;
	}

	public override void Initialize()
	{
		optionLevel = 0;
		asplodeOnNextFrame = false;
		// Cleared with its flag, or it survives the respawn: Killer_OnDeath only fires while the
		// killer itself dies, so a ship killed by something that is still alive carried that
		// reference for the rest of the session and AiBench would attribute a LATER, unrelated
		// death to it (card b56633fb -- the killer histogram is only worth reading if it cannot
		// name a stale one).
		asplosionCauser = null;
		isTutorial = false;
		respawntimebonus = 0;
		readyToConnect = false;
		haspower = false;
		Score.ResetPowerup(player);
		invulnerabilityTimer.Reset();
		shoottimer.Duration = 1000f / (float)shotspersec;
		ResetAiState();
		base.MaxSpeed = ShipMaxSpeed;
		base.Deceleration = ShipDeceleration;
		base.Acceleration = ShipAcceleration;
		CollisionBox collisionBox = retrieveBoundsFromTexture();
		TopLeft = collisionBox.TopLeft;
		BottomRight = collisionBox.BottomRight;
		starttimer.Reset();
		shoottimer.Reset();
		shoottimer.Stop();
		// Net fire state (card a45b78f6). PlayerShip is POOLED, so a field initializer would not
		// re-run -- and a recycled ship inheriting the previous one's counter would make its
		// puppet's first delta an invented burst. The aim seeds facing UP, the value SendShipState
		// used to substitute for a ship that had never fired.
		NetShotCount = 0;
		NetLastFireAim = 4.712389f;
		netAppliedShotCount = 0;
		netShotBaselineSet = false;
		netShotsPending = 0;
		base.Initialize();
		hasWon = false;
		base.OnDeath += deathEvent;
		color = Color.White;
		if (Settings.GetInstance().PowerUp)
		{
			PowerUp();
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (asplodeOnNextFrame)
		{
			if (asplosionCauser != null)
			{
				Asplode();
				return;
			}
			asplodeOnNextFrame = false;
		}
		if (!isTutorial && controller != ControlDevice.AI && !IsNetPuppet && Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard)
		{
			pacifistTimer.Update(gameTime);
		}
		if (pacifistTimer.Finished)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Pacifist);
			pacifistTimer.Reset();
		}
		if (powerupEffect != null)
		{
			powerupEffect.SetPosition(base.Position);
		}
		if (blast != null)
		{
			blast.SetPosition(base.Position);
		}
		if (!hasWon)
		{
			if (starttimer.Active)
			{
				Move((float?)startdir, gameTime);
			}
			else
			{
				Vector2 direction = Vector2.Zero;
				switch (EffectiveController())
				{
				case ControlDevice.PadOne:
				case ControlDevice.PadTwo:
				case ControlDevice.PadThree:
				case ControlDevice.PadFour:
				{
					int i = controller switch
					{
						ControlDevice.PadOne => 0, 
						ControlDevice.PadTwo => 1, 
						ControlDevice.PadThree => 2, 
						ControlDevice.PadFour => 3, 
						_ => throw new Exception(), 
					};
					Vector2 leftStick = input.LeftStick(i);
					if ((leftStick).LengthSquared() > 0.09f)
					{
						direction = input.LeftStick(i);
					}
					Vector2 rightStick = input.RightStick(i);
					if ((rightStick).LengthSquared() > 0.0025000002f)
					{
						FireAt(MyMath.VectorToAngle(input.RightStick(i)));
					}
					else if (shoottimer.Finished)
					{
						shoottimer.Stop();
						shoottimer.Reset();
					}
					if (input.PadPressed(PadKeys.LTRT, i))
					{
						doBlast();
					}
					break;
				}
				case ControlDevice.Keyboard:
					if (input.Down(MyKeys.Down))
					{
						direction.Y += 1f;
					}
					if (input.Down(MyKeys.Up))
					{
						direction.Y -= 1f;
					}
					if (input.Down(MyKeys.Right))
					{
						direction.X += 1f;
					}
					if (input.Down(MyKeys.Left))
					{
						direction.X -= 1f;
					}
					if (input.Pressed(MyKeys.Mouse2))
					{
						doBlast();
					}
					if (input.Down(MyKeys.Mouse1))
					{
						float aimDirection = MyMath.VectorToAngle(input.MousePosition - base.Position);
						FireAt(aimDirection);
					}
					else if (shoottimer.Finished)
					{
						shoottimer.Stop();
						shoottimer.Reset();
					}
					break;
				case ControlDevice.AI:
				{
					// Perf batch 2: GetBaddies() rebuilds its list by scanning every game
					// component; it was called three times per AI ship per frame (DoAIMove,
					// DoAIFire, doAIBomb). Build it once and thread it through — the component
					// set can't change mid-frame (adds/removes are deferred to ComponentBin.Update).
					List<AlienDrawableGameComponent> baddies = oracle.GetBaddies();
					DoAIMove(ref direction, gameTime, baddies);
					DoAIFire(gameTime, baddies);
					break;
				}
				case ControlDevice.Remote:
					// Online co-op (Stage 11): the OTHER peer's ship. Position comes from the
					// interpolation buffer (~100ms behind), shots are respawned locally from the
					// replicated shot COUNT; direction stays Zero so the Move below is a no-op.
					EvilAliensWeb.Compat.Net.NetSession.DriveRemoteShip(this, gameTime);
					break;
				case ControlDevice.RemoteFriend:
					// Coverage-gaps follow-up: a client-side puppet for one of the HOST's AI friend
					// ships -- same network-driven scheme as Remote, but keyed by its slot channel.
					EvilAliensWeb.Compat.Net.NetSession.DriveFriendShip(this, gameTime);
					break;
				}
				Move(direction, gameTime);
			}
			base.Update(gameTime);
			if (!starttimer.Active)
			{
				Vector2 position = base.Position;
				if (base.Position.X > 800f - BottomRight.X)
				{
					position.X = 800f - BottomRight.X;
				}
				if (base.Position.X < 0f - TopLeft.X)
				{
					position.X = 0f - TopLeft.X;
				}
				if (base.Position.Y > 600f - BottomRight.Y)
				{
					position.Y = 600f - BottomRight.Y;
				}
				if (base.Position.Y < 0f - TopLeft.Y)
				{
					position.Y = 0f - TopLeft.Y;
				}
				base.Position = position;
			}
		}
		else
		{
			base.MaxSpeed = ShipMaxSpeed;
			Move((float?)startdir, gameTime);
			base.Update(gameTime);
		}
		oracle.SetPlayerPosition(player, base.Position);
	}

	private void doBlast()
	{
		if (Score.NrBombs(player) > 0)
		{
			Score.RemoveBomb(player);
			blast = Blast.NewBlast(collection, base.Game);
			blast.Setup(base.Position, Score.GetPowerupLevel(Powerup.PowerupType.Blast, player), player);
			collection.Add((GameComponent)(object)blast);
			sound.PlayCue("blast");
			// Online co-op: bombs are discrete, so they ride the reliable event lane (the
			// ship stream carries continuous state only). No-op unless a net session is up.
			EvilAliensWeb.Compat.Net.NetSession.OnLocalBlast(this, base.Position, Score.GetPowerupLevel(Powerup.PowerupType.Blast, player));
		}
	}

	// ---- Online co-op (Stage 11) seams -- see Compat/Net/NetSession ----------------------

	// ?aiplayer forces the LOCAL ship onto the AI branch at level start (unattended two-tab
	// soak tests). The controller field itself stays what it was (Keyboard/pad), so joins,
	// pause and "which ship do we stream" logic are untouched; Remote puppets are exempt.
	private ControlDevice EffectiveController()
	{
		if (EvilAliensWeb.Compat.DebugFlags.AIPlayer && !IsNetPuppet)
		{
			return ControlDevice.AI;
		}
		return controller;
	}

	// A network-driven puppet ship (the other peer's ship, or one of the host's AI friends):
	// its OWNER decides its motion/hits/pickups, so the local sim never damages it, lets it grab
	// a powerup, or forces it onto the ?aiplayer AI branch.
	private bool IsNetPuppet => controller == ControlDevice.Remote || controller == ControlDevice.RemoteFriend;

	// The fire state the co-op ship stream carries (card a45b78f6): how many shots this ship has
	// actually SPAWNED, cumulative and wrapping, plus the aim of the newest one. Both are stamped
	// inside FireAt's cadence gate, so an increment is a bullet -- never an intent. It is the
	// DELTA that means anything: the absolute value is meaningless across a ship, and it wraps
	// every 256 shots by design.
	internal byte NetShotCount { get; private set; }

	internal float NetLastFireAim { get; private set; }

	// Receive side, for a Remote / RemoteFriend puppet: the last count we have acted on, whether
	// we have one at all yet, and the shots still owed (a lossy or bursty link can deliver a
	// delta > 1, which drains one per tick rather than stacking bullets on one point).
	private byte netAppliedShotCount;

	private bool netShotBaselineSet;

	private int netShotsPending;

	// A delta larger than this is not catch-up, it is a DESYNC -- a peer whose ship respawned
	// (its counter restarts at 0) or that went silent long enough for the count to run away.
	// Six shots is ~330 ms of continuous loss even at the maxed 18/s fire rate, well past the
	// point where "exact" means anything; beyond it the receiver reseeds and fires nothing,
	// rather than dumping a magazine into the world at once.
	private const int NetMaxCatchUpShots = 6;

	internal Vector2 NetVelocity => SpeedVector;

	internal int NetShotsPerSec => shotspersec;

	// Read seams for eaNetPickup() (cards 83271f3d / 10f9dba4). Both front private state a
	// screenshot cannot show: whether the "2" powerup armed this ship, and how many Option ships
	// it is flying -- the two things the remote-pickup mirror exists to keep equal between peers.
	internal bool NetReadyToConnect => readyToConnect;

	internal int NetOptionCount => options[0].Count + options[1].Count;

	// Per ORBIT LAYER, which is what MsgHudState carries (card c5228350): the layer decides the
	// orbit radius, so a total would let the observer rebuild the owner's outer ring inside.
	// Out of range reads 0 rather than throwing -- the caller's index comes off the wire's
	// HudOptionLayers, which is allowed to outlive this ship's layer count.
	internal int NetOptionLayerCount(int layer)
	{
		if (layer < 0 || layer >= options.Length)
		{
			return 0;
		}
		return options[layer].Count;
	}

	// Online co-op (card c5228350): drive this puppet's Option population to what its OWNER
	// reports, per layer. The owner is authoritative, so this both ADDS the options a
	// join-in-progress peer never saw claimed and DROPS any this peer has over -- an observer
	// shoots at its own local copies, and only the owner's count settles it.
	//
	// NetSession.HandleHudState is the only caller and gates on !OwnsSlot, so it never touches a
	// ship whose own CollidesWith/PowerUp maintain the real population. Counts arrive already
	// clamped at the decode boundary (NetProtocol.HudMaxOptionsPerLayer), indexed by LAYER --
	// the array rather than one parameter per layer, so a third orbit could never be silently
	// folded into the second's count. A layer the wire does not mention reads 0.
	internal void NetSetOptionCounts(int[] counts)
	{
		if (counts == null)
		{
			return;
		}
		bool changed = false;
		for (int layer = 0; layer < options.Length; layer++)
		{
			int want = layer < counts.Length ? Math.Max(0, counts[layer]) : 0;
			List<Option> list = options[layer];
			while (list.Count > want)
			{
				Option surplus = list[list.Count - 1];
				list.RemoveAt(list.Count - 1);
				// A silent despawn, not a kill: nothing shot this one down here, so it must not
				// explode, score or make a noise. Dropping it from `list` leads because
				// OnComponentRemoved's own list maintenance is then a no-op -- but the WORLD
				// removal is queued like every other Die(), so for up to one flush the dropped
				// option still draws (at its stale angle, beside the redressed ring) and still
				// collides. Cosmetic, and the same deal every Die() call site takes.
				surplus.NetDespawn();
				changed = true;
			}
			while (list.Count < want)
			{
				Option option = Option.NewOption(collection, base.Game);
				option.Setup(this, 0f, layer + 1, player);
				// TryAdd, not Add: this caller ADOPTS what it adds, and Add diverts SILENTLY
				// into the recycle pool under a standing Purge<AlienDrawableGameComponent>
				// (GameScene's reset/win paths arm one, and the net rx drains inside the same
				// tick). Adopting a diverted option would satisfy list.Count with a component
				// the world does not have, and the reconcile would never notice. On a refusal
				// leave the list alone and let the next packet retry -- the NetSession
				// SpawnPuppet rule, root CLAUDE.md.
				if (!collection.TryAdd((GameComponent)(object)option))
				{
					break;
				}
				list.Add(option);
				changed = true;
			}
		}
		if (changed)
		{
			RedressOptions();
		}
	}

	internal float NetBulletLife => bulletlifetime;

	// Read seam for eaNetLevelEnd (card b4a9fe60): the angle this ship flew IN on and will fly
	// OUT on at victory (the hasWon arm of Update thrusts at it forever). Private, set once in
	// Setup, and it drives nothing else observable -- so the only way to tell a puppet spawned
	// on the scene's direction from one spawned on the hard-coded South is to read it.
	internal float NetStartDirection => startdir;

	// How many shots a puppet owes, given the count that just arrived and the last one it acted
	// on. Pure and static so the whole wrap domain can be swept as a decision (eaNetFire leg 1)
	// rather than sampled at whatever counts a scripted burst happens to produce.
	// `resync` = the delta is too big to be catch-up (see NetMaxCatchUpShots): adopt the count,
	// fire nothing.
	internal static int NetShotDelta(byte received, byte lastApplied, out bool resync)
	{
		int delta = (byte)(received - lastApplied);
		resync = delta > NetMaxCatchUpShots;
		return resync ? 0 : delta;
	}

	// Applied every tick to a ControlDevice.Remote puppet: interpolated position (speed
	// zeroed -- the buffer is the sole motion source), replicated fire loadout, and the owner's
	// shots respawned through the real bullet construction so remote bullets are built like
	// local ones. The count is what paces them (card a45b78f6) -- NOT a local cadence gate,
	// which would re-derive a rate the owner has already measured for us and get it wrong
	// whenever a packet was lost, late or early.
	internal void NetApplyRemoteState(Vector2 pos, float aim, byte shotCount, int shotsPerSec, float bulletLife)
	{
		base.Position = pos;
		Speed = 0f;
		int shots = Math.Clamp(shotsPerSec, 1, 18);
		if (shots != shotspersec)
		{
			shotspersec = shots;
			shoottimer.Duration = 1000f / (float)shotspersec;
		}
		bulletlifetime = MathHelper.Clamp(bulletLife, 450f, 1500f);
		// The first sample only establishes where the owner's counter stands: everything before
		// it happened before this puppet existed, so none of it is owed.
		if (!netShotBaselineSet)
		{
			netShotBaselineSet = true;
			netAppliedShotCount = shotCount;
		}
		else if (shotCount != netAppliedShotCount)
		{
			int owed = NetShotDelta(shotCount, netAppliedShotCount, out bool resync);
			if (resync)
			{
				// The counter is no longer continuous with what we were tracking, so neither is
				// anything still queued from before it: firing that backlog now would put the
				// previous sequence's bullets in front of this one.
				netShotsPending = 0;
			}
			netShotsPending += owed;
			netAppliedShotCount = shotCount;
		}
		// One per tick: a burst that arrives together still leaves the barrel one bullet at a
		// time instead of spawning a stack of them on a single point.
		if (netShotsPending > 0)
		{
			netShotsPending--;
			SpawnShot(aim);
		}
	}

	// Remote peer used a bomb (reliable EvBlast event): spawn it here at the puppet, WITHOUT
	// the local Score bomb-count gate -- the owner already spent the bomb on its side.
	internal void NetDoBlast(int level)
	{
		blast = Blast.NewBlast(collection, base.Game);
		blast.Setup(base.Position, level, player);
		collection.Add((GameComponent)(object)blast);
		sound.PlayCue("blast");
	}

	// Move a live ship to another roster slot (card 4d904410). Only the JOIN peer's primary
	// ever moves, and only in the dev ?net=join flow: it boots into a level at slot 0 and
	// learns its host-granted slot when it pairs. The oracle registration moves first
	// (Oracle.MovePlayerSlot); this re-stamps the ship's own slot identity and colour.
	internal void NetSetOwner(int slot, float newHue)
	{
		player = slot;
		hue = newHue;
	}

	// What a bullet can actually DAMAGE -- the AI's target set (card f4d1721f). This MIRRORS the
	// type list in Bullet.CollidesWith; the two must be changed together, and a type present
	// there but missing here is a target the AI is blind to. That drift is what stalled the bot:
	// BrainBoss and FakeBoss gate the end of Level 3, StationaryBoss sits mid-Level-2, and none
	// of them were listed -- so the AI parked next to a halting boss and shot at nothing.
	// Three deliberate exclusions from the bullet list:
	//   SpiderBoss              bullets DEFLECT off it by design (only a Lazer hurts it), so
	//                           aiming at it is pure wasted uptime -- see SpiderBoss.CollidesWith.
	//   SpiderHelperMothership  the thing that kills the spider boss for you. It is fake-killable
	//                           with an enormous HP pool, so targeting it would swallow the AI's
	//                           aim for the whole fight.
	//   Asteroid                killable, but it does not sustain combo, shooting one splits it,
	//                           and the belt is meant to be flown through, not cleared.
	private static bool IsAiShootable(AlienDrawableGameComponent baddy)
	{
		return baddy is UFO || baddy is Boss || baddy is Braineroid || (baddy is Ball && ((Ball)baddy).IsConnected())
			|| baddy is JunkBoss || (baddy is EvilSkull && !((EvilSkull)baddy).Fading) || baddy is DeathStar
			|| baddy is ClassicBoss || baddy is BattleSkull || baddy is Spider || baddy is StationaryBoss
			|| baddy is MarsBoss || baddy is StarMine || baddy is BrainBoss || (baddy is FlyingSpider && baddy.Collides)
			|| baddy is FakeBoss || baddy is SweepUFO || baddy is ParatrooperAlien || baddy is Parachute
			|| baddy is ParatrooperBrain || baddy is PunchingBag;
	}

	// What can actually KILL the ship -- the AI's avoidance set. Mirrors the type list in
	// PlayerShip.CollidesWith (the branch that reaches Asplode/AsplodeWall). Wall and Lazer are
	// excluded here only because DoAIMove handles them with dedicated, better-shaped logic
	// (a tile-map gap search and a distance-to-line steer) before this predicate is reached.
	// Gating avoidance on this rather than on `Collides` alone stops the bot dodging things that
	// cannot hurt it -- a Parachute is shootable but harmless, and swerving around one costs
	// exactly the positioning that gets a ship killed by something that is not.
	private static bool IsAiThreat(AlienDrawableGameComponent baddy)
	{
		return baddy is UFO || baddy is Boss || baddy is Braineroid || baddy is EvilBullet || baddy is Asteroid
			|| baddy is Ball || baddy is JunkBoss || baddy is DeathStar || baddy is ClassicBoss
			|| baddy is StationaryBoss || baddy is Spider || baddy is MarsBoss || baddy is BattleSkull
			|| baddy is FlyingSpider || baddy is Explosion || baddy is StarMine || baddy is PlasmaBall
			|| baddy is BrainBoss || baddy is FakeBoss || baddy is SweepUFO || baddy is SpiderBoss
			|| baddy is PunchingBag || (baddy is EvilSkull && !((EvilSkull)baddy).Fading);
	}

	// Bosses that HALT the level script: until one dies nothing else advances, so at comparable
	// range it outranks trash that respawns forever. Without this the AI happily spends a boss
	// fight plinking at the skulls the boss keeps spawning.
	private static bool IsAiPriorityTarget(AlienDrawableGameComponent baddy)
	{
		// SPIDERBOSS IS EXCLUDED, EXPLICITLY (card b56633fb). It was excluded by OMISSION from the
		// list below, which is the same behaviour and no protection at all: the obvious "the AI
		// ignores the spider boss" edit is to add it to that list, and doing so would make card
		// b56633fb's symptom -- the bot walking into the PARKED boss, its single largest killer --
		// dramatically worse, with nothing failing to say so. It must be DODGED, not sought:
		// bullets deflect off it (only a Lazer hurts it, which is why IsAiShootable excludes it
		// too), so approaching it buys nothing and costs the fight. Pinned by logic_probe's
		// ProbeAiBossApproach with BrainBoss as the positive control.
		// IF YOU ARE HERE BECAUSE ADDING SpiderBoss TO THE LIST BELOW CHANGED NOTHING: this guard
		// is why, and it wins deliberately. Removing it needs card b56633fb read first -- the
		// standing boss is the bot's single largest killer, and seeking it makes that worse.
		if (baddy is SpiderBoss)
		{
			return false;
		}
		return baddy is BrainBoss || baddy is FakeBoss || baddy is MarsBoss || baddy is JunkBoss
			|| baddy is ClassicBoss || baddy is Boss || baddy is StationaryBoss || baddy is BattleSkull;
	}

	// Per-life AI state, cleared with everything else in Setup.
	private void ResetAiState()
	{
		aiSteer = Vector2.Zero;
		aiGapColumn = -1;
	}

	private void DoAIFire(GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		float aimSpread = AimSpread;
		// Compared in SQUARED space while the loop scans (and carrying the priority discount, so
		// it is a score rather than a distance); the winner's true distance is recovered after the
		// loop for the range test.
		// The SpiderBoss fight is won with the ENEMY's guns: only a Lazer can hurt the boss, and a
		// big UFO fires one at the player, so the boss walks into any beam that crosses the
		// screen. Killing every big UFO leaves nothing but the helper mothership's slow cycle, so
		// a couple are deliberately spared -- the surplus is still cleared.
		// This only pays off together with the laser dodging below: the beams the AI is inviting
		// are aimed AT IT. Sparing them without that measured 24 -> ~70 deaths.
		bool spiderBossAlive = false;
		bool bossSweeping = false;
		UFO sparedUfo = null;
		float sparedRoom = -1f;
		foreach (AlienDrawableGameComponent scan in baddies)
		{
			if (scan is SpiderBoss && !scan.IsDead)
			{
				spiderBossAlive = true;
				bossSweeping |= ((SpiderBoss)scan).AiSweepIncoming;
			}
			else if (scan is UFO && ((UFO)scan).IsBig && !scan.IsDead)
			{
				// Spare exactly ONE, and make it the one with the most room around it -- scored by
				// its distance to the NEAREST ship, so in co-op it is far from everybody. Keeping
				// the beam platform at arm's length is what makes this survivable: its beam still
				// crosses the screen for the boss to walk into, but the AI is not standing next to
				// the thing that is aiming at it.
				float room = float.MaxValue;
				foreach (PlayerShip ship in oracle.GetShips())
				{
					Vector2 toShip = scan.Position - ship.Position;
					room = MathHelper.Min(room, (toShip).Length());
				}
				if (room > sparedRoom)
				{
					sparedRoom = room;
					sparedUfo = (UFO)scan;
				}
			}
		}
		// ...but NOT during a fly-by. Dodging a screen-wide sweep and a big UFO's beam at the same
		// time is how the bot dies, and it is worst in the upper lane where the UFOs live. The
		// boss spends most of the fight grounded, which is plenty of time to feed it beams.
		if (!spiderBossAlive || bossSweeping)
		{
			sparedUfo = null;
		}
		float nearestDist = float.MaxValue;
		AlienDrawableGameComponent nearest = null;
		// The priority bias decides WHICH target wins, but a discounted boss can win from well
		// outside gun range (at bias 0.45 a boss 780px away outranks a UFO at 350px). Without a
		// fallback the AI then fires at nothing at all while a killable target sits in range --
		// inflating the very idle% the bias exists to reduce. So track the nearest genuinely
		// reachable target alongside it.
		float nearestInRangeSq = float.MaxValue;
		AlienDrawableGameComponent inRangeTarget = null;
		float gunRangeSq = (bulletlifetime * BulletRangePerMs) * (bulletlifetime * BulletRangePerMs);
		// A level-halting boss is worth reaching past a lot of trash, so it competes on a
		// DISCOUNTED distance rather than by raw proximity. Scored in the same squared space the
		// loop compares in, hence the squared factor.
		float priorityBiasSq = PriorityTargetBias * PriorityTargetBias;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (IsAiShootable(baddy) && !ReferenceEquals(baddy, sparedUfo))
			{
				if (isBlastable(baddy) && blast != null && blast.Collides)
				{
					break;
				}
				Vector2 toBaddy = baddy.Position - base.Position;
				float scoreSq = (toBaddy).LengthSquared();
				if (IsAiPriorityTarget(baddy))
				{
					scoreSq *= priorityBiasSq;
				}
				bool onScreen = baddy.Position.X > 0f && baddy.Position.X < 800f && baddy.Position.Y > 0f && baddy.Position.Y < 600f;
				if (scoreSq < nearestDist && onScreen)
				{
					nearestDist = scoreSq;
					nearest = baddy;
				}
				float trueDistSq = (toBaddy).LengthSquared();
				if (onScreen && trueDistSq <= gunRangeSq && trueDistSq < nearestInRangeSq)
				{
					nearestInRangeSq = trueDistSq;
					inRangeTarget = baddy;
				}
			}
		}
		// Undo the bias before the range test: the discount decides WHICH target wins, never
		// whether a bullet can actually reach it.
		if (nearest != null)
		{
			Vector2 toChosen = nearest.Position - base.Position;
			nearestDist = (toChosen).Length();
			if (nearestDist > bulletlifetime * BulletRangePerMs && inRangeTarget != null)
			{
				nearest = inRangeTarget;
				nearestDist = (float)Math.Sqrt(nearestInRangeSq);
			}
		}
		else
		{
			nearestDist = float.MaxValue;
		}
		bool fired = false;
		if (nearestDist <= bulletlifetime * BulletRangePerMs)
		{
			fired = true;
			if (nearest is JunkBoss)
			{
				FireAt(MyMath.VectorToAngle(nearest.Position - base.Position));
			}
			else
			{
				FireAt(MyMath.VectorToAngle(nearest.Position - base.Position) + RandomHelper.RandomNextFloat(0f - aimSpread, aimSpread));
			}
		}
		// AI bench (card f4d1721f): "there was something on screen I could have killed and I did
		// not shoot" is the signature of a target the AI cannot see -- the shape of the Level 3
		// stall, where the boss that gates the level was never in the list above.
		EvilAliensWeb.Compat.AiBench.NoteFireDecision(this, nearest != null, fired);
		doAIBomb(baddies);
	}

	private void doAIBomb(List<AlienDrawableGameComponent> baddies)
	{
		if (blast != null)
		{
			return;
		}
		int minTargets;
		switch (Score.NrBombs(player))
		{
		case 0:
			return;
		case 1:
			minTargets = 10;
			break;
		case 2:
			minTargets = 7;
			break;
		case 3:
			minTargets = 4;
			break;
		default:
			minTargets = 4;
			break;
		}
		int targetsInRange = 0;
		float blastRadius = 200 * (1 + Score.GetPowerupLevel(Powerup.PowerupType.Blast, player));
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (isBlastable(baddy))
			{
				Vector2 toBaddy = baddy.Position - base.Position;
				if ((toBaddy).LengthSquared() <= blastRadius * blastRadius)
				{
					targetsInRange++;
				}
			}
		}
		if (targetsInRange >= minTargets)
		{
			doBlast();
		}
	}

	private bool isBlastable(AlienDrawableGameComponent alien)
	{
		if (!(alien is EvilBullet) && (!(alien is UFO) || ((UFO)alien).IsBig) && (!(alien is Braineroid) || !(alien.scale < 0.1f)))
		{
			return alien is EvilSkull;
		}
		return true;
	}

	// ?aiseeklog state: last kind printed + a tick counter for the heartbeat. Per ship, so a
	// co-op pair logs independently.
	private string aiSeekLogKind = "";
	private int aiSeekLogTick;

	// The seek-arrival hysteresis latch (see DefaultSeekParkPx): parked-in-the-zone, plus which
	// seek kind parked it -- a kind change resets the latch so a fresh destination pulls
	// immediately. A stale value on a pool-recycled ship self-heals: a spawn point outside the
	// resume radius unparks on the first tick.
	private bool seekParked;
	private string seekParkedKind = "";

	// One [aiseek] line on every seek-kind change and a heartbeat every 30 ticks while the kind
	// holds. The line carries where the ship IS, where the seek points, its weight and whether
	// the deadzone has silenced it -- the attribution a position trace cannot give.
	private void LogAiSeek(string kind, Vector2 target, float weight, float dist, bool inDeadzone)
	{
		aiSeekLogTick++;
		if (kind == aiSeekLogKind && aiSeekLogTick % 30 != 0)
		{
			return;
		}
		aiSeekLogKind = kind;
		Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
			"[aiseek] p{0} kind={1} target={2:0},{3:0} pos={4:0},{5:0} dist={6:0} w={7:0.00} dz={8}",
			player, kind, target.X, target.Y, base.Position.X, base.Position.Y, dist, weight,
			inDeadzone ? 1 : 0));
	}

	private void DoAIMove(ref Vector2 direction, GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		CollisionLevelMap collisionLevelMap = null;
		bool hasWall = false;
		bool altSteering = false;
		float steerRange = 150f;
		float minSteerStrength = 0f;
		float maxSteerStrength = 4f;
		// REPELLENTS ARE SUMMED SEPARATELY FROM ATTRACTORS (card ada9e839). Everything that pushes
		// the ship AWAY from something -- every threat field, the lazer terms, the spider boss's
		// lane escapes, the screen edges -- accumulates here and is folded into `direction` once,
		// below, after the cancellation floor has had its say. Attractors (the seek and the
		// powerup's own pull) go straight into `direction` and are never floored; each of them
		// stops attracting inside its own deadzone instead.
		//
		// WHY THE SPLIT IS THE FIX. The two families fail differently. A repellent pair that
		// shoves from opposite sides resolves to a near-zero vector whose direction is noise, and
		// Move() throws magnitude away and thrusts full-tilt along the angle -- so the ship
		// rattles between two walls it should just sit between. An attractor cannot fail that way
		// (it is one pull toward one point), so a floor big enough to fix the first family can
		// only ever silently delete the second, which is exactly what the 0.95 park did.
		//
		// NOT in here, deliberately: SteerThroughWall (a committed gap PLAN with its own
		// hysteresis, not a field), and the top-edge band and ClampIntoWallSpace, which both run
		// AFTER the low-pass on purpose -- moving either across that boundary would change wall
		// and ceiling behaviour this card has no business touching.
		Vector2 repel = Vector2.Zero;
		Vector2 steerTarget = new Vector2(float.MaxValue, float.MaxValue);
		// How hard to pull toward whatever steerTarget ends up being. It carries the WEIGHT rather
		// than a flag because the answer is not two-valued: the idle station and every DETOUR park
		// at SeekWeight, while a level-halting boss carries a weight SOLVED per tick against that
		// boss's own repellent (card b56633fb, BossApproachWeight), which is the one write here
		// that is not a constant.
		// INVARIANT: a steerTarget write that can run AFTER another one must set this too, even
		// to SeekWeight -- otherwise it inherits the previous writer's weight rather than the
		// default, and a detour silently flies at the boss approach's. Only the two writes inside the
		// baddy loop (a blastable cluster, a JunkBoss) are exempt, and only because nothing has
		// written a weight yet by then; the two station fallbacks are exempt because they run
		// solely while steerTarget is still MaxValue.
		float steerTargetWeight = SeekWeight;
		// ?aiseeklog attribution: which write site owns steerTarget this tick. Set beside every
		// assignment below; read only by LogAiSeek, so a shipped build carries a dead local.
		string seekKind = "none";
		float dodgeAngle = 0f;
		if (player == 0)
		{
			dodgeAngle = (float)Math.PI / 16f;
		}
		if (player == 1)
		{
			dodgeAngle = -(float)Math.PI / 16f;
		}
		if (player == 2)
		{
			dodgeAngle = (float)Math.PI / 6f;
		}
		if (player == 3)
		{
			dodgeAngle = -(float)Math.PI / 6f;
		}
		AlienDrawableGameComponent haltingBoss = null;
		float haltingBossDistSq = float.MaxValue;
		Vector2 delta;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (isBlastable(baddy) && blast != null && blast.Collides)
			{
				delta = baddy.Position - base.Position;
				float distSq = (delta).LengthSquared();
				Vector2 toTarget = steerTarget - base.Position;
				if (distSq < (toTarget).LengthSquared())
				{
					steerTarget = baddy.Position;
					seekKind = "blast";
				}
				continue;
			}
			if (baddy is JunkBoss)
			{
				steerTarget = baddy.Position;
				seekKind = "junkboss";
			}
			// Sidestep a charging beam. A big UFO winds up for 2500ms and locks its aim at the
			// PLAYER only at the instant it fires, so the dodge is to be somewhere else by then --
			// moving ACROSS the UFO's line of sight during the windup makes the locked aim stale.
			// Standing still and reacting to the beam afterwards cannot work: it appears along its
			// whole length at once.
			// OFF at the baked default (card 2248e5eb). `dodgeStrength > 0` is an early-out and
			// nothing more -- adding the zero vector would steer identically -- but it is what
			// makes "this term is switched off" visible at the branch instead of only at the
			// constant three hundred lines up.
			float dodgeStrength = LazerDodgeStrength;
			if (dodgeStrength > 0f && baddy is UFO && ((UFO)baddy).IsBig && ((UFO)baddy).AiChargingLazer)
			{
				Vector2 fromUfo = base.Position - baddy.Position;
				float range = (fromUfo).Length();
				if (range > 1f)
				{
					// Perpendicular to the line of sight, on the side the ship is already drifting
					// toward so the sidestep never fights its current momentum.
					Vector2 across = new Vector2(0f - fromUfo.Y, fromUfo.X) / range;
					if (Vector2.Dot(across, SpeedVector) < 0f)
					{
						across = -across;
					}
					repel += dodgeStrength * across;
				}
			}
			// The vertical strips: the fixed X-600 landing column, and the climb that opens the
			// next cycle. Same treatment as the sweep lane, on the other axis -- flat across the
			// band, because every part of it is equally lethal.
			// ?ailaneescape=0 drops both hand-rolled spider escapes, so the lane wedge added by
			// card e425781b can be measured against them instead of on top of them. Built as a
			// temporary seam for that supersession A/B, and PERMANENT because the A/B kept them:
			// dropping the escapes doubles SpiderBoss(standing) deaths (12 -> 24 over 8 paired
			// runs) and costs 26 points of powerup pickup. The wedge is an ADDITION, not a
			// replacement.
			if (EvilAliensWeb.Compat.DebugFlags.AiLaneEscape != false
				&& baddy is SpiderBoss && ((SpiderBoss)baddy).AiVerticalLaneActive)
			{
				float laneX = ((SpiderBoss)baddy).AiVerticalLaneX;
				float offLane = base.Position.X - laneX;
				if (Math.Abs(offLane) < VerticalLaneClearancePx)
				{
					// ALWAYS break left out of a landing. The landing now sweeps everything from the
					// boss to the right screen edge (see SpiderBoss's land case), so right is not
					// an escape at all -- it is a dead end that merely looks like one. For the
					// jump/climb, which has no such sweep, either side is fine so the ship takes
					// whichever it is already nearer.
					float away = ((SpiderBoss)baddy).AiLandingSweep
						? -1f
						: ((Math.Abs(offLane) < 1f) ? ((laneX > 400f) ? -1f : 1f) : Math.Sign(offLane));
					// Same steep falloff as every other field here: hardest at the centre line,
					// fading out toward the clearance edge. A flat push across the band was tried
					// and it fights the screen bounds all the way out instead of easing off once
					// the ship is clearly out of the way.
					float urge = MyMath.PowerCurve(SweepLaneAvoidStrength, 0f, 2f, Math.Abs(offLane) / VerticalLaneClearancePx);
					repel += new Vector2(away * urge, 0f);
				}
			}
			// Act on the boss's own telegraph. During the "Danger!" arrow the spider boss sits
			// off-screen in the lane it is about to cross, so it is STATIONARY -- the movement
			// prediction says nothing and the distance field is a screen away. Vacating the lane
			// now is the whole point of the warning, and it is far cheaper than trying to escape
			// a screen-wide sweep once it has started.
			if (EvilAliensWeb.Compat.DebugFlags.AiLaneEscape != false
				&& baddy is SpiderBoss && ((SpiderBoss)baddy).AiSweepIncoming)
			{
				float laneY = ((SpiderBoss)baddy).AiSweepLaneCentreY;
				float offLane = base.Position.Y - laneY;
				if (Math.Abs(offLane) < SweepLaneClearancePx)
				{
					// Flee DOWNWARD out of the lane unless the lane IS the bottom one. Which way
					// to run is not symmetric: UFOs enter from the top, so the upper third is the
					// busy half of the screen and running up out of the middle lane trades one
					// hazard for another. Only the bottom lane forces the ship upward.
					float away = (laneY > 400f) ? -1f : 1f;
					// Steep falloff, like every other field here: hardest on the centre line and
					// easing off as the ship clears the band, so it hands over cleanly to the
					// screen-edge terms instead of shoving all the way into them.
					float urge = MyMath.PowerCurve(SweepLaneAvoidStrength, 0f, 2f, Math.Abs(offLane) / SweepLaneClearancePx);
					repel += new Vector2(0f, away * urge);
				}
			}
			// Card f4d1721f: track the nearest level-HALTING boss so the ship can close on it if
			// it is out of gun range (below). The 2008 code only ever did this for JunkBoss, so
			// against any other boss the AI hovered at its default station and fired only when the
			// boss happened to drift within range -- measured as 55% of ticks with a shootable
			// target and no shot fired, against a BrainBoss parked at the top of the screen.
			// Same on-screen predicate DoAIFire uses. BrainBoss eases in from a negative Y, and
			// without this the ship is dragged toward a point off the top of the screen
			// during the entry -- while DoAIFire is still refusing to shoot at it.
			if (IsAiPriorityTarget(baddy) && baddy.Position.X > 0f && baddy.Position.X < 800f
				&& baddy.Position.Y > 0f && baddy.Position.Y < 600f)
			{
				Vector2 toBoss = baddy.Position - base.Position;
				float bossDistSq = (toBoss).LengthSquared();
				if (bossDistSq < haltingBossDistSq)
				{
					haltingBossDistSq = bossDistSq;
					haltingBoss = baddy;
				}
			}
			if (baddy is Wall)
			{
				hasWall = true;
				collisionLevelMap = (CollisionLevelMap)((Wall)baddy).GetCollisionType();
				if (EvilAliensWeb.Compat.DebugFlags.AiWallNav2008)
				{
					SteerThroughWall2008(ref direction, collisionLevelMap, gameTime);
				}
				else
				{
					SteerThroughWall(ref direction, (Wall)baddy, collisionLevelMap);
				}
			}
			else if (baddy is Lazer)
			{
				getDistanceToLine(baddy, out var d, out var shortestpoint);
				// A live beam is instant death along its whole length. The port widened this berth
				// past the 2008 flat 150px and card 2248e5eb measured that back off again: the
				// wider field pushed the ship off the beam and into whatever was behind it. See
				// DefaultLazerAvoidRangePx for the numbers and the curve-family confound.
				// `lazerRange > 0` is the guarded divisor, not a redundant test: ?ailazerpx=0 is a
				// legitimate "no beam field at all" arm, and d can be exactly 0 (the ship standing
				// on the beam), which would otherwise reach 0/0.
				float lazerRange = LazerAvoidRangePx;
				if (lazerRange > 0f && d <= lazerRange)
				{
					float strength = MyMath.PowerCurve(LazerAvoidStrength, 0f, 2f, d / lazerRange);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, d / lazerRange);
					}
					repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - shortestpoint) + dodgeAngle);
				}
				// THE TIP'S SWEPT SHAPE (owner ruling, lap 5): the beam GROWS -- its tip advances
				// at growthspeed * DifficultyModifier, faster than the ship at higher tiers --
				// and the distance-to-line above only covers the segment that already exists, so
				// the swath about to be claimed had zero warning. The tip gets the standard
				// swept treatment at the UFO reference radius (factor 1.0), deliberately NOT its
				// literal ~8px half-thickness: a guaranteed-death front deserves no less warning
				// than a UFO at the same speed just for being thin. No wedge (the LANE concept
				// does not apply to a lengthwise front).
				if (ConeEnabled && ((Lazer)baddy).TryGetAiTipMotion(out Vector2 lazerTip, out Vector2 tipVel))
				{
					SweptShape tipShape = EvaluateSweptShape(base.Position, lazerTip, tipVel,
						ConeLeadRefRadiusPx, ConeLeadRefRadiusPx, AiHalfExtent(), maxSteerStrength, wedgeEnabled: false);
					if (tipShape.ConeStrength > 0f)
					{
						float tipStrength = tipShape.ConeStrength;
						EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy,
							EvilAliensWeb.Compat.AiBench.ThreatPath.Cone, tipStrength, tipShape.ConeLength, tipShape.ConeEdgeDist);
						repel += tipStrength * MyMath.AngleToVector(MyMath.VectorToAngle(tipShape.ConeDir) + dodgeAngle);
					}
				}
			}
			else
			{
				// Card f4d1721f: dodge only what can actually KILL the ship. Steering around a
				// harmless-but-collidable object (a Parachute) costs exactly the positioning that
				// gets a ship killed by something that is not harmless.
				if (!baddy.Collides || !IsAiThreat(baddy))
				{
					continue;
				}
				// A fast mover is judged by where it is GOING, not where it is. Radial repulsion
				// from something crossing the screen pushes the ship ALONG its path -- which is
				// precisely the spider boss's screen-wide sweep, and why that fight read as "no
				// idea what it's doing". See EvadeMovingThreat.
				// When it engages it REPLACES the radial push below rather than adding to it.
				// Adding both was tried and measured much worse (4 -> 27 deaths on the spider boss):
				// for something crossing the screen the radial term points ALONG its path, so
				// keeping it around actively fights the evade it is supposed to back up. Anything
				// slow, static, or not actually on a collision course falls through to the field.
				// THE UNIFIED SWEPT SHAPE (owner redesign, lap 5): for a mover it IS the whole
				// repellent -- the shape includes the body circle -- so a handled threat skips
				// the radial branch below entirely (adding both would count the circle twice).
				// Stationary objects, refused teleport paths and the ?aicone=0 arm return false
				// and fall through to the plain radial field.
				if (AddSweptRepellent(ref repel, baddy, dodgeAngle, maxSteerStrength))
				{
					continue;
				}
				// EvadeMovingThreat stays BAKED OFF (owner ruling: superseded by the shape);
				// `?aievade=1` re-arms it for A/B.
				if (EvilAliensWeb.Compat.DebugFlags.AiEvadeMovers == true
					&& EvadeMovingThreat(ref repel, baddy, dodgeAngle, minSteerStrength, maxSteerStrength))
				{
					continue;
				}
				float dist = ThreatEdgeDistance(base.Position, baddy);
				// THE STANDARD FIELD, FLAT (owner ruling, lap 7 -- the full return to 2008):
				// 150px beyond the body's edge, 4 -> 0 on the quadratic plateau, for EVERY
				// stationary threat regardless of size -- a big thing's field starts further out
				// because its edge is further out, never because it is wider. This is the swept
				// capsule at speed zero, so the whole threat system is one rule; the size-scaled
				// range (150 + 1.8*half-extent) and the per-type curve/falloff switches are gone
				// with their flags.
				if (dist <= SweptFieldRangePx)
				{
					float strength = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, dist / SweptFieldRangePx);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, dist / SweptFieldRangePx);
					}
					EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy, EvilAliensWeb.Compat.AiBench.ThreatPath.Field, strength, SweptFieldRangePx, dist);
					repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - baddy.Position) + dodgeAngle);
				}
			}
		}
		foreach (Powerup powerup in oracle.GetPowerups())
		{
			if ((powerup.type == Powerup.PowerupType.Linker && readyToConnect) || !(powerup.Position.X > 0f) || !(powerup.Position.X < 800f) || !(powerup.Position.Y > 0f) || !(powerup.Position.Y < 600f))
			{
				continue;
			}
			bool goForPowerup = wantsToTakePowerup(powerup);
			if (goForPowerup)
			{
				foreach (PlayerShip ship in oracle.GetShips())
				{
					if (ship.wantsToTakePowerup(powerup))
					{
						Vector2 otherToPowerup = ship.Position - powerup.Position;
						float otherDistSq = (otherToPowerup).LengthSquared();
						Vector2 myToPowerup = base.Position - powerup.Position;
						if (otherDistSq < (myToPowerup).LengthSquared() && !isConnectedWith(ship))
						{
							goForPowerup = false;
						}
					}
				}
			}
			if (!goForPowerup)
			{
				continue;
			}
			Vector2 toPowerup = powerup.Position - base.Position;
			float distToPowerup = (toPowerup).Length();
			Vector2 toTarget = steerTarget - base.Position;
			if (distToPowerup < (toTarget).Length())
			{
				steerTarget = powerup.Position;
				steerTargetWeight = SeekPowerupWeight;
				seekKind = "powerup";
			}
			// PowerupReachPx, not the 150px `steerRange` the 2008 code shared with the screen-edge
			// margin -- see the const. Beyond this the powerup is still the steerTarget above, so
			// the ship heads for it; this term only shapes the approach.
			// An ATTRACTOR, so it goes into `direction` and is never floored (card ada9e839). It
			// needs no explicit deadzone: its target CEASES TO EXIST when the ship reaches it --
			// contact collects the powerup and `oracle.GetPowerups()` stops returning it -- so
			// there is no point it can oscillate about. That is the one attractor here whose
			// deadzone is implicit rather than written down; note it before copying the shape.
			float powerupReach = PowerupReachPx;
			if (distToPowerup <= powerupReach)
			{
				float pull = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, distToPowerup / powerupReach);
				if (altSteering)
				{
					pull = MathHelper.Lerp(maxSteerStrength, minSteerStrength, distToPowerup / powerupReach);
				}
				direction += pull * MyMath.AngleToVector(MyMath.VectorToAngle(toPowerup));
			}
		}
		// NOTE: an earlier revision also parked the ship on the far side of the boss to line the
		// beam up through it. That was removed -- it is not needed (a beam crossing the screen
		// gets hit by the boss on its own jump/fly cycle) and it was actively lethal: standing
		// still on a chosen spot waiting to be shot at measured 24 -> 75 deaths, because the
		// boss simply landed on the stationary ship. Sparing the big UFOs in DoAIFire is the
		// whole mechanism; where the ship stands is the evasion code's business.
		// Close on a level-halting boss (cards f4d1721f -> 31ceb6ff -> b56633fb). Nothing else in
		// the level advances until it dies, so hovering at the default station waiting for it to
		// drift into range is not a strategy -- it is the stall. The target is the BOSS, not a
		// geometric standoff point: where the ship comes to rest is decided by this attractor
		// meeting the boss's own repellent, which is the whole design (see BossApproachExponent).
		// So this asks to get in RANGE, never to ram it, and the threat repulsion above still owns
		// how close is too close.
		// Placed after the powerup pass so a boss fight outranks a pickup detour.
		if (haltingBoss != null)
		{
			float bossEdgeDist = ThreatEdgeDistance(base.Position, haltingBoss);
			// Gun range is a CENTRE distance (it is what DoAIFire range-tests), so the body term
			// converts it into the edge space everything here is measured in.
			float anchorPx = bulletlifetime * BulletRangePerMs - ThreatBodyTerm(haltingBoss);
			float pull = BossApproachWeight(bossEdgeDist, anchorPx, SweptFieldRangePx,
				DefaultThreatFieldFalloff, classic: true, 1f, maxSteerStrength, SteerNoiseFloor) * BossApproachScale;
			// The bench call is UNCONDITIONAL -- the term's calibration is what it measures, and a
			// tick where the boss lost the vote is exactly the tick worth counting.
			EvilAliensWeb.Compat.AiBench.NoteBossApproach(this, bossEdgeDist,
				MathHelper.Max(anchorPx, BossApproachMinAnchorPx), pull);
			// It has to OUT-VOTE a destination something else CHOSE, not merely exist. One
			// `steerTarget` carries one destination, so writing it unconditionally would let a boss
			// term that has quieted to a fraction of SeekPowerupWeight delete a live powerup detour
			// -- the opposite of "a boss fight outranks a pickup detour", and arriving precisely
			// inside firing range, where this term is DESIGNED to fall silent.
			// The X > 2000 sentinel means NOBODY has chosen yet (the idle station is assigned below,
			// after this), and there the boss takes the wheel at any weight: the alternative is
			// hovering at a station the boss may not be in range of, which is the stall this whole
			// term exists to end. So the comparison is against a real competing vote only.
			if (steerTarget.X > 2000f || pull > steerTargetWeight)
			{
				steerTarget = haltingBoss.Position;
				steerTargetWeight = pull;
				seekKind = "boss";
			}
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.readyToConnect && ship != this && readyToConnect && !isConnectedWith(ship))
			{
				steerTarget = ship.Position;
				seekKind = "connect";
				// EVERY steerTarget write sets its own weight, including the ones that keep the
				// station's. This one overwrites the boss approach above, so inheriting silently
				// would fly the DETOUR at the approach's weight -- the one case where "leave it
				// at the default" and "leave it at whatever the last writer set" differ.
				steerTargetWeight = SeekWeight;
			}
		}
		if (steerTarget.X > 2000f && !collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				if (collection.ContainsType<Wall>())
				{
					(steerTarget) = new Vector2(400f, 300f);
				}
				else
				{
					(steerTarget) = new Vector2(400f, 400f);
				}
			}
			// Spread by the player's ORDINAL among seated slots, not `player + 1`: online co-op's
			// roster is sparse (card 4d904410), and a high slot would otherwise respawn off-screen.
			else if (collection.ContainsType<Wall>())
			{
				float spacing = 800 / (oracle.Players + 1);
				(steerTarget) = new Vector2((float)oracle.SeatOrdinal(player) * spacing, 300f);
			}
			else
			{
				float spacing = 800 / (oracle.Players + 1);
				(steerTarget) = new Vector2((float)oracle.SeatOrdinal(player) * spacing, 400f);
			}
		}
		if (steerTarget.X > 2000f && collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				(steerTarget) = new Vector2(266f, 300f);
			}
			else
			{
				(steerTarget) = new Vector2(266f, 600f / (float)(oracle.Players + 1) * (float)oracle.SeatOrdinal(player));
			}
		}
		if (steerTarget.X < 2000f)
		{
			// Only the station fallbacks above assign steerTarget without stamping a kind.
			if (seekKind == "none")
			{
				seekKind = "station";
			}
			delta = base.Position - steerTarget;
			float distToTarget = (delta).Length();
			// HYSTERESIS ARRIVAL (owner ruling, iterative rep 1; see DefaultSeekParkPx). Park the
			// pull inside SeekParkPx, resume it only past SeekResumePx; a seek-kind change resets
			// the latch, and a target swap or a moving target opens the distance past the resume
			// radius on its own, so no per-target identity is needed. The single hard-edged
			// deadzone this replaces relied on its radius covering the 11.3px stopping distance;
			// here that bound belongs to the RESUME radius (park + 11.3 < resume), pinned by
			// ProbeAiFieldComposition.
			// A velocity-damped ARRIVE was tried in an earlier era and reverted -- it contains
			// -SpeedVector, so it brakes the ship whenever it is moving relative to its station,
			// which is most of a boss fight (coast 28% -> 59%, 24 -> 70 deaths). Don't re-derive.
			if (seekKind != seekParkedKind)
			{
				seekParked = false;
				seekParkedKind = seekKind;
			}
			if (seekParked)
			{
				if (distToTarget >= SeekResumePx)
				{
					seekParked = false;
				}
			}
			else if (distToTarget <= SeekParkPx)
			{
				seekParked = true;
			}
			if (EvilAliensWeb.Compat.DebugFlags.AiSeekLog)
			{
				LogAiSeek(seekKind, steerTarget, steerTargetWeight, distToTarget, seekParked);
			}
			if (!seekParked)
			{
				// Plain positional pull, as in 2008: into `direction`, never floored below the
				// blanket 0.2 (the pull is 0.8, so the floor cannot censor it alone).
				direction += steerTargetWeight
					* MyMath.AngleToVector(MyMath.VectorToAngle(steerTarget - base.Position));
			}
		}
		float edgeMargin = steerRange;
		float bottomEdge = 600f;
		if (collection.ContainsType<Floor>())
		{
			bottomEdge = 560f;
		}
		// (An edge-band "powerup yield" briefly lived here in iterative rep 1 and was killed the
		// same session by owner ruling -- a bandaid on a bandaid; the classic field curve is the
		// structural fix for edge powerups. The pushes below are 2008 verbatim.)
		if (!altSteering)
		{
			if (base.Position.X < edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, base.Position.X / edgeMargin);
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, base.Position.X / edgeMargin);
				}
				repel += push * new Vector2(1f, 0f);
			}
			if (base.Position.X > 800f - edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, Math.Abs((800f - base.Position.X) / edgeMargin));
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, Math.Abs((800f - base.Position.X) / edgeMargin));
				}
				repel += push * new Vector2(-1f, 0f);
			}
			if (base.Position.Y < edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, base.Position.Y / edgeMargin);
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, base.Position.Y / edgeMargin);
				}
				repel += push * new Vector2(0f, 1f);
			}
			if (base.Position.Y > bottomEdge - edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, Math.Abs((bottomEdge - base.Position.Y) / edgeMargin));
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, Math.Abs((bottomEdge - base.Position.Y) / edgeMargin));
				}
				repel += push * new Vector2(0f, -1f);
			}
		}
		// THE COMBINE. The per-family cancellation floor that used to sit here (card ada9e839:
		// zero `repel` alone when its resultant was <= 0.2, before the low-pass) is GONE -- owner
		// ruling, iterative rep 1: back to 2008's single blanket floor on the final sum at the
		// end of the method, on the "too smart for its own good" grounds. The known cost is the
		// case that floor was built for: two repellents shoving from opposite sides can still
		// resolve to a >0.2 residual whose direction flips over a few px, which no end-of-sum
		// floor can catch -- accepted for now (the probe-ahead idea is logged, not built).
		// `repel` still accumulates separately so the bench can report it as its own vector.
		EvilAliensWeb.Compat.AiBench.NoteRepel(this, repel, zeroed: false);
		direction += repel;
		// Low-pass the summed steer (card f4d1721f). Everything above votes with a vector, Move()
		// consumes only the resulting ANGLE, and nothing damped how fast that angle could move --
		// so near-cancelling votes used to spin the heading at ~1050 deg/s inside a wall. Blending
		// the VECTOR makes opposing votes cancel toward zero (the ship coasts, which is the right
		// answer) while a sustained vote still converges in a few frames. Exponential in dt so the
		// smoothing is framerate-independent.
		// How hard everything is pushing, before smoothing. This is the AI's own measure of "how
		// much trouble am I in", and both rules below key off it.
		float demand = (direction).Length();
		// Smoothing is ADAPTIVE: heavy damping is what stops idle fidget, but it is exactly wrong
		// when something is bearing down -- a 90ms low-pass on an evade is 30px of travel spent
		// not turning. So the time constant collapses toward zero as the push gets strong: park
		// carefully when things are calm, fly immediately when they are not.
		float smoothMs = MathHelper.Lerp(SteerSmoothMs, SteerSmoothUrgentMs,
			MathHelper.Clamp((demand - SteerCalmDemand) / MathHelper.Max(SteerUrgentDemand - SteerCalmDemand, 0.001f), 0f, 1f));
		if (smoothMs > 0f)
		{
			float blend = 1f - (float)Math.Exp(0f - gameTime.ElapsedGameTime.TotalMilliseconds / smoothMs);
			aiSteer = Vector2.Lerp(aiSteer, direction, MathHelper.Clamp(blend, 0f, 1f));
			direction = aiSteer;
		}
		// The emergency wall clamp is applied AFTER the smoothing, deliberately: it is a hard
		// "do not fly into that" override, and low-passing it (as an earlier revision did) turns a
		// full reversal into a gentle suggestion -- which measured as 46 wall contacts against the
		// old code's 8.
		// The top edge is not just a boundary, it is where UFOs enter -- a ship pinned against it
		// gets exploded by something spawning on top of it. The stock edge repulsion tops out at
		// maxSteerStrength (4), which is no contest against a lane escape (18) or the spider
		// boss's own field, so fleeing upward parked the ship on the ceiling. This term is scaled
		// to actually compete, and it ramps linearly rather than with the steep field falloff so
		// it is already pushing well before the ship gets there.
		// `topEdgePx > 0` is the guarded divisor, mirroring the beam field above: ?aitopedgepx=0
		// passes the flag's `>= 0` range check, and relying on the position clamp to keep Y
		// positive is an invariant three hundred lines away.
		float topEdgePx = TopEdgeDangerPx;
		if (topEdgePx > 0f && base.Position.Y < topEdgePx)
		{
			direction += new Vector2(0f, TopEdgeAvoidStrength * (1f - base.Position.Y / topEdgePx));
		}
		if (hasWall)
		{
			ClampIntoWallSpace(ref direction, collisionLevelMap);
			// ...and the override is REMEMBERED. Leaving aiSteer untouched here means the very
			// next tick blends back toward the pre-clamp heading, so a probe that flickers clear
			// snaps the ship straight back at the wall -- the clamp becomes its own oscillator.
			// Committing it makes the escape the new baseline to smooth from.
			aiSteer = direction;
		}
		// THE EQUILIBRIUM GUARD -- the 2008 line, restored verbatim in value and position (card
		// ada9e839). Move() discards magnitude and thrusts at full acceleration along the ANGLE,
		// so a steer that has cancelled down to a whisker is not a gentle nudge, it is a sprint in
		// a direction that is numerical noise. At or below the floor the ship holds still, which
		// for a potential field is the CORRECT answer at an equilibrium point.
		//
		// IT CANNOT CENSOR A REAL FORCE, and that is by construction rather than by luck: every
		// FIXED-weight attractor weighs at least SeekWeight 0.8, and any repellent that survived the
		// repulsion floor above already exceeds 0.2. So the only way to land here is genuine
		// cancellation between an attractor and a repellent that both really are pushing.
		//
		// THE BOSS APPROACH IS THE ONE DELIBERATE EXCEPTION (card b56633fb), and it is the case
		// DefaultSteerNoiseFloor's own "FUTURE HAZARD" note predicted: it is an attractor whose
		// weight VARIES with distance and passes down through this floor near firing range. Being
		// zeroed there is the DESIGN, not a censored force -- the floor is what turns the crossing
		// into a parked band wide enough for the ship to stop in, which BossApproachWeight solves
		// its exponent against. It is out of ProbeAiFieldComposition's weakest-attractor bound for
		// that reason and pinned by ProbeAiBossApproach instead.
		// Read that as CONVERGED magnitude, because this runs downstream of the low-pass: a lone
		// 0.8 seek starting from rest blends to only ~0.135 on its first tick and IS zeroed for
		// that one frame. `aiSteer` itself is not zeroed, so the next tick continues converging
		// and the ship is moving within a few frames -- a start-up delay, not a censored force.
		// ProbeAiFieldComposition asserts the bound on the unsmoothed weights, which is the
		// property that actually has to hold.
		// logic_probe's ProbeAiFieldComposition asserts both bounds; the port's 0.95 -- which was
		// ABOVE the 0.8 seek and therefore deleted every deliberate destination the bot had -- is
		// exactly what that assertion exists to stop coming back.
		//
		// AFTER the low-pass, deliberately. The blend's output decays exponentially toward zero
		// and never reaches it, so a floor placed before it would hand the smoother a clean zero
		// and still leave the ship thrusting full-tilt down a decaying residual for many frames
		// afterwards. Applied here, the ship actually stops.
		if ((direction).Length() <= SteerNoiseFloor)
		{
			direction = Vector2.Zero;
		}
		// AI bench (card f4d1721f): this is the AI's decision for the tick, and Move() consumes
		// only its ANGLE -- so the heading measured here is exactly what the ship will fly.
		EvilAliensWeb.Compat.AiBench.NoteSteer(this, direction, gameTime);
	}

	// Steer off the PATH of a threat that is closing fast, rather than radially away from where
	// it happens to be (card f4d1721f). Returns false for anything slow or already receding, so
	// the original distance-based repulsion below still handles the static/drifting majority.
	//
	// Why this exists: the SpiderBoss's flyleft/flyright states cross the entire screen width at
	// a fixed Y. Radial repulsion from a boss directly to the ship's left pushes the ship RIGHT
	// -- straight down the boss's own track -- and only starts pushing at all inside 150px, by
	// which time a mover that size cannot be avoided. Steering perpendicular to its travel moves
	// the ship off the line while there is still time, which is what a player does.
	// `repel` is DoAIMove's repulsion accumulator, not its final steer -- this is a push-away
	// term and is subject to the cancellation floor like every other one (card ada9e839).
	private bool EvadeMovingThreat(ref Vector2 repel, AlienDrawableGameComponent baddy, float dodgeAngle, float minSteerStrength, float maxSteerStrength)
	{
		// Engage on the THREAT's own speed, never the relative speed. This matters: relative
		// velocity is non-zero for a STATIONARY threat whenever the ship is moving, so gating on
		// it made this method take over for parked objects too -- and since it returns true the
		// caller skips the radial push-away field, leaving a perpendicular slide as the only
		// avoidance. A sideways slide does not stop you flying INTO something, which is exactly
		// what the ship did to the grounded spider boss. Anything not really moving belongs to
		// the field.
		// ObservedVelocity, not SpeedVector: the latter is derived from _speed/_direction and
		// reads zero for everything that writes Position directly -- including the spider boss's
		// fly states, i.e. the case this method exists for.
		Vector2 threatVelocity = baddy.ObservedVelocity;
		// THE SAME TELEPORT GUARD THE SWEPT-PATH SEAM APPLIES (card c1d783ad), and this path needs
		// it MORE, not less: a reposition passes the speed gate below trivially, collapses `t` to
		// almost nothing, and so lands inside ThreatPanicMs -- a ThreatPanicStrength (16) shove,
		// four times maxSteerStrength, aimed along a course the thing never took. The screen
		// wrappers reach here for real. Refusing is the same answer a stationary threat gets, and
		// the radial field still describes the body.
		if (!AlienDrawableGameComponent.IsAiSweptPathPlausible(threatVelocity))
		{
			return false;
		}
		// The player ship's own MaxSpeed is 0.33 px/ms, so this is ~a third of that.
		if ((threatVelocity).Length() < ThreatMinSpeed)
		{
			return false;
		}
		// Closest approach is then computed on the RELATIVE course, because the ship is moving too
		// and ignoring that mispredicts every near-miss it is closing on.
		Vector2 rel = threatVelocity - SpeedVector;
		float speed = (rel).Length();
		if (speed < 0.001f)
		{
			return false;
		}
		Vector2 toShip = base.Position - baddy.Position;
		// Time of closest approach on the two present courses.
		float t = Vector2.Dot(toShip, rel) / (speed * speed);
		if (t <= 0f)
		{
			// Closest approach is behind it: already past, nothing to dodge.
			return false;
		}
		float lead = ThreatLeadMs;
		if (t > lead)
		{
			// Too far out in time to be worth bending the flight path for -- and acting on it
			// now would just be noise added to whatever the ship is actually doing.
			return false;
		}
		Vector2 miss = toShip - rel * t;
		float missDist = (miss).Length();
		float margin = ThreatMissMargin + ThreatRadius(baddy);
		if (missDist > margin)
		{
			return false;
		}
		// Push perpendicular to the threat's travel, on the side the ship is already closer to.
		Vector2 side = (missDist > 0.001f) ? (miss / missDist) : new Vector2(0f - rel.Y, rel.X) / speed;
		// Full strength at a dead-on collision course, tapering to nothing at the margin, and
		// again by how soon it lands -- an impact 100ms away deserves more than one 700ms away.
		float byMiss = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, missDist / margin);
		float byTime = MathHelper.Clamp(1f - t / lead, 0f, 1f);
		float strength = byMiss * (ThreatUrgencyFloor + (1f - ThreatUrgencyFloor) * byTime);
		// A dead-on hit about to land RIGHT NOW has to outrank every other steering term, not
		// merely tie with them -- otherwise the evade is one vote of at most maxSteerStrength (4)
		// against a boss-approach pull, a powerup pull and the edge pushes, and the ship takes the
		// hit while politely averaging its options. Removing this measured 18 -> 27 deaths.
		if (t < ThreatPanicMs && missDist < margin * ThreatPanicMissFraction)
		{
			strength = MathHelper.Max(strength, ThreatPanicStrength);
		}

		EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy, EvilAliensWeb.Compat.AiBench.ThreatPath.Evade, strength);
		repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(side) + dodgeAngle);
		return true;
	}

	// THE DIRECTIONAL REPELLENT (card e425781b) -- one mesa along the mover's own swept path, plus
	// the asymmetric wedge when that path hugs a screen edge. Adds to `repel`, so it composes with
	// the radial field and everything else exactly as those compose with each other, and is
	// subject to the same cancellation floor. Called for every threat; a thing that is not moving
	// contributes nothing and falls through to the radial field alone, as before.
	//
	// COORDINATES. With `axis` the unit travel direction and `d` the ship relative to the band's
	// anchor: `u = dot(d, axis)` is how far AHEAD the ship is and `w = |d - u*axis|` how far to the
	// SIDE. The corridor's half-width tapers from the body's own half-extent at u=0 to a point at
	// the cone's length, and the falloff across is measured from the corridor's edge outward -- so
	// the field is at FULL strength anywhere inside the swept body and none of its dynamic range
	// is spent on the interior, which is the card's design principle.
	//
	// THE PUSH IS PURELY TRANSVERSE, and that is a decision rather than an approximation. The
	// mesa's own gradient also has an along-axis component, and it points FORWARD -- further down
	// the mover's track, away from the mover. Following it would ask the ship to outrun the thing
	// chasing it, which it cannot do (an asteroid moves 0.38px/ms against ShipMaxSpeed 0.33), and
	// it is the identical failure the radial field already has against a screen-crosser: a push
	// ALONG the path rather than off it. So only the sideways component is taken, which is also
	// what a player does and what EvadeMovingThreat did for the same reason.
	// The one field parameter of the swept shape: the 2008 steerRange, verbatim.
	private const float SweptFieldRangePx = 150f;

	// The triangle's length, in ONE place so the steering and the ?cones overlay can never
	// disagree: speed x lead, scaled by the mover's size against the UFO reference, capped at
	// the world-sized ceiling. See DefaultConeLeadMs for the ruling and the numbers.
	private static float SweptConeLength(float speed, float bodyRadius)
	{
		return MathHelper.Min(speed * ConeLeadMs * (bodyRadius / ConeLeadRefRadiusPx), ConeMaxLenPx);
	}

	// The swept shape's GEOMETRY for one mover, exactly as the steering will evaluate it this
	// tick -- the ?cones overlay draws precisely this, so the picture and the force can never
	// drift apart. False for everything AddSweptRepellent would skip (cones off, no path,
	// refused teleport, stationary).
	internal static bool TryDescribeSweptShape(AlienDrawableGameComponent baddy,
		out Vector2 anchor, out float radius, out Vector2 apex)
	{
		anchor = default(Vector2);
		radius = 0f;
		apex = default(Vector2);
		if (!ConeEnabled)
		{
			return false;
		}
		// THE STEERING LOOP'S OWN GATES, mirrored, so the overlay draws exactly what the AI
		// avoids and nothing else. ORDER MATTERS AND BIT ONCE (owner catch, iterative rep 1):
		// the beam must be tested BEFORE the IsAiThreat gate, because the steering handles
		// Lazer in its own dedicated branch and the predicate deliberately does not list it --
		// gating first silently deleted every tip shape from the overlay while the steering
		// pushed away regardless. Wall stays excluded (grid nav, no swept shape); everything
		// generic must be collide-active AND a genuine threat, which is what keeps the player's
		// own bullets and spent explosions dark.
		if (baddy is Lazer beam)
		{
			if (!beam.Collides || !beam.TryGetAiTipMotion(out anchor, out var tipVel))
			{
				return false;
			}
			radius = ConeLeadRefRadiusPx;
			float tipSpeed = (tipVel).Length();
			apex = (tipSpeed < 0.001f)
				? anchor
				: anchor + tipVel / tipSpeed * SweptConeLength(tipSpeed, radius);
			return true;
		}
		if (baddy is Wall || !baddy.Collides || !IsAiThreat(baddy))
		{
			return false;
		}
		radius = MathHelper.Max(ThreatBodyTerm(baddy), 1f);
		// A threat with NO swept path -- truly motionless (the scroll pauses during Level 2's
		// set pieces, so parked UFOs really do read zero), or a refused teleport frame -- still
		// describes its CIRCLE: the radial field's body is a real force and the owner wants it
		// visible. Only the triangle needs a path. This is why the path test must not gate the
		// whole description (iterative rep 1 sighting: landed UFOs vanishing from the overlay
		// whenever the ground stood still).
		if (!baddy.TryGetAiSweptPath(out anchor, out var velocity, out _))
		{
			anchor = baddy.Position;
			apex = anchor;
			return true;
		}
		float speed = (velocity).Length();
		apex = (speed < 0.001f)
			? anchor
			: anchor + velocity / speed * SweptConeLength(speed, radius);
		return true;
	}

	// The unified swept repellent (owner redesign, iterative rep 1 lap 5): for a MOVER this is
	// the WHOLE repellent -- the shape includes the body circle -- so the caller skips the
	// radial branch when this returns true, or the circle would be counted twice. Returns false
	// (pushing nothing) for a stationary object, a refused teleport path, or the ?aicone=0 arm,
	// all of which fall through to the plain radial field.
	private bool AddSweptRepellent(ref Vector2 repel, AlienDrawableGameComponent baddy, float dodgeAngle, float maxSteerStrength)
	{
		if (!ConeEnabled)
		{
			return false;
		}
		if (!baddy.TryGetAiSweptPath(out var anchor, out var velocity, out var halfWidth))
		{
			return false;
		}
		if ((velocity).Length() < 0.001f)
		{
			return false;
		}
		SweptShape shape = EvaluateSweptShape(base.Position, anchor, velocity,
			ThreatBodyTerm(baddy), halfWidth, AiHalfExtent(), maxSteerStrength, LaneWedgeEnabled);
		if (shape.ConeStrength > 0f)
		{
			float strength = shape.ConeStrength;
			EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy,
				EvilAliensWeb.Compat.AiBench.ThreatPath.Cone, strength, shape.ConeLength, shape.ConeEdgeDist);
			repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(shape.ConeDir) + dodgeAngle);
		}
		if (shape.WedgeStrength > 0f)
		{
			float strength = shape.WedgeStrength;
			EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy,
				EvilAliensWeb.Compat.AiBench.ThreatPath.Wedge, strength, shape.WedgeLength, shape.WedgeEdgeDist);
			repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(shape.WedgeDir) + dodgeAngle);
		}
		return true;
	}

	// What the shape evaluates to at one point: the two terms with their directions, plus the two
	// quantities the bench reports beside every other repellent (the term's own reach, and how far
	// outside the shape the ship is).
	internal struct SweptShape
	{
		internal float ConeStrength;

		internal Vector2 ConeDir;

		internal float ConeLength;

		internal float ConeEdgeDist;

		internal float WedgeStrength;

		internal Vector2 WedgeDir;

		internal float WedgeLength;

		internal float WedgeEdgeDist;
	}

	// THE SHAPE (owner redesign; see the const block above). Two candidates, evaluated
	// independently, SHORTER DISTANCE WINS -- which, because the field curve is monotone in
	// distance, is exactly the distance field of the UNION of the two shapes:
	//   the CIRCLE: the mover's own repulsion circle (radius = its body term, the same one the
	//     radial branch subtracts). dist = |p - anchor| - r, push radial. This alone makes a
	//     stationary treatment unnecessary -- wherever "behind", "inside the body" or the
	//     flat-back sliver would need a special case, the circle's distance is simply smaller
	//     and it wins the competition. All the bits that poke out win naturally.
	//   the TRIANGLE: base corners on the circle's perpendicular diameter, apex at
	//     `anchor + axis * min(speed * lead, cap)`. Inside -> dist 0, out the near flank;
	//     outside -> nearest point on the two side edges (segment clamps), push away from it.
	//     The base edge is skipped on proof, not oversight: it is a chord INSIDE the disk, so
	//     the circle's distance is always <= the distance to it.
	// Strength = THE standard field treatment, owner-ruled: flat 150px reach, 4 -> 0 on the
	// quadratic plateau -- `PowerCurve(4, 0, 2, d/150)` verbatim, the same expression the screen
	// edges, the beam and the powerup near-field have always used (deliberately NOT the radial
	// branch's size-scaled range, and deliberately outside the ?aifieldcurve= family switch --
	// the edges do not switch either). The caller adds the dodge twist. Direction is the winner's, so it flips
	// at the union's internal watershed -- inherent to any nearest-feature field, a few degrees,
	// invisible under the twist. Pure -- primitives in, shape out -- so logic_probe drives it
	// directly.
	internal static SweptShape EvaluateSweptShape(Vector2 shipPos, Vector2 anchor,
		Vector2 velocity, float bodyRadius, float bandHalfWidth, float shipHalfExtent,
		float maxSteerStrength, bool wedgeEnabled)
	{
		SweptShape result = default(SweptShape);
		float speed = (velocity).Length();
		if (speed < 0.001f)
		{
			return result;
		}
		Vector2 axis = velocity / speed;
		float r = MathHelper.Max(bodyRadius, 1f);
		float coneLen = SweptConeLength(speed, r);
		Vector2 d = shipPos - anchor;
		float dlen = (d).Length();
		// Candidate 1: the circle.
		float circleDist = MathHelper.Max(dlen - r, 0f);
		Vector2 circleDir = (dlen > 0.001f) ? (d / dlen) : (-axis);
		// Candidate 2: the triangle.
		Vector2 perp = new Vector2(0f - axis.Y, axis.X);
		float w = Vector2.Dot(d, perp);
		// The near flank; w == 0 resolves to +perp deterministically, not by float noise.
		Vector2 side = (w >= 0f) ? perp : (-perp);
		Vector2 corner = anchor + side * r;
		Vector2 apex = anchor + axis * coneLen;
		Vector2 edge = apex - corner;
		float u = Vector2.Dot(d, axis);
		// Inside test: ahead of the base, and on the inner side of the NEAR edge. (A point past
		// the apex reads as outward of the near edge, so this needs no far bound.)
		float cross = edge.X * (shipPos.Y - corner.Y) - edge.Y * (shipPos.X - corner.X);
		bool outwardOfEdge = ((w >= 0f) ? cross : (0f - cross)) > 0f;
		float triDist;
		Vector2 triDir;
		if (u > 0f && !outwardOfEdge)
		{
			triDist = 0f;
			triDir = side;
		}
		else
		{
			// Nearest point on the near side edge. The far edge can never be nearer than the
			// near one (the ship is on this side of the axis), and behind the base the clamps
			// land on the corners, where the circle wins anyway.
			float edgeLenSq = MathHelper.Max((edge).LengthSquared(), 0.0001f);
			float t = MathHelper.Clamp(Vector2.Dot(shipPos - corner, edge) / edgeLenSq, 0f, 1f);
			Vector2 q = corner + t * edge;
			Vector2 away = shipPos - q;
			triDist = (away).Length();
			triDir = (triDist > 0.001f) ? (away / triDist) : side;
		}
		// The competition: shorter distance wins = the union's distance field.
		float dist;
		Vector2 dir;
		if (circleDist <= triDist)
		{
			dist = circleDist;
			dir = circleDir;
		}
		else
		{
			dist = triDist;
			dir = triDir;
		}
		if (dist <= SweptFieldRangePx)
		{
			result.ConeStrength = MyMath.PowerCurve(maxSteerStrength, 0f, 2f, dist / SweptFieldRangePx);
			result.ConeDir = dir;
			result.ConeLength = coneLen;
			result.ConeEdgeDist = dist;
		}
		// ---- the lane wedge (unchanged in spirit; its outside falloff now rides the same
		// threat-field curve as everything else instead of a private exponent) ----
		if (!wedgeEnabled)
		{
			return result;
		}
		if (u <= 0f)
		{
			return result;
		}
		float stoppingDistance = 0.5f * ShipMaxSpeed * ShipMaxSpeed / ShipDeceleration;
		float survivableGap = 2f * (shipHalfExtent + stoppingDistance);
		// A wedge is for a LANE, and a lane is a band too wide to go around -- an 18-strength
		// shove on every bullet drifting near the ceiling would out-vote the whole field. The
		// gate keeps small movers from wedging at all.
		if (bandHalfWidth < survivableGap)
		{
			return result;
		}
		// Which way is "out of the lane", if either -- measured at the cross-section the SHIP is
		// at, not at the anchor (a mover typically enters from off-screen).
		Vector2 bandPoint = anchor + u * axis;
		float room1 = PlayfieldExitDistance(bandPoint, perp) - bandHalfWidth;
		float room2 = PlayfieldExitDistance(bandPoint, -perp) - bandHalfWidth;
		Vector2 outDir;
		if (room1 < survivableGap && room1 <= room2)
		{
			outDir = -perp;
		}
		else if (room2 < survivableGap)
		{
			outDir = perp;
		}
		else
		{
			return result;
		}
		// The lane is lethal for its whole extent, so the wedge runs the remaining play field.
		float wedgeLen = MathHelper.Max(PlayfieldExitDistance(anchor, axis), coneLen);
		float wedgeAlong = 1f - (float)Math.Pow(MathHelper.Clamp(u / wedgeLen, 0f, 1f), LaneWedgeFallAlong);
		if (wedgeAlong <= 0f)
		{
			return result;
		}
		// Full strength across the whole band, the ordinary field falloff beyond its far edge.
		float outward = Vector2.Dot(d, outDir);
		float wedgeAcross;
		if (outward <= bandHalfWidth)
		{
			wedgeAcross = 1f;
		}
		else if (outward - bandHalfWidth >= SweptFieldRangePx)
		{
			wedgeAcross = 0f;
		}
		else
		{
			wedgeAcross = MyMath.PowerCurve(1f, 0f, 2f, (outward - bandHalfWidth) / SweptFieldRangePx);
		}
		if (wedgeAcross > 0f)
		{
			result.WedgeStrength = LaneWedgeStrength * wedgeAlong * wedgeAcross;
			result.WedgeDir = outDir;
			result.WedgeLength = wedgeLen;
			result.WedgeEdgeDist = MathHelper.Max(0f, outward - bandHalfWidth);
		}
		return result;
	}

	// How far from `from` along `dir` the 800x600 design field extends. A slab test, so it is
	// correct for a diagonal path as well as the axis-aligned ones the game actually produces;
	// clamped at zero for an anchor that is already outside on that axis (the spider boss starts
	// its sweeps off-screen).
	private static float PlayfieldExitDistance(Vector2 from, Vector2 dir)
	{
		float best = float.MaxValue;
		if (Math.Abs(dir.X) > 0.0001f)
		{
			best = MathHelper.Min(best, ((dir.X > 0f) ? (800f - from.X) : (0f - from.X)) / dir.X);
		}
		if (Math.Abs(dir.Y) > 0.0001f)
		{
			best = MathHelper.Min(best, ((dir.Y > 0f) ? (600f - from.Y) : (0f - from.Y)) / dir.Y);
		}
		return (best == float.MaxValue) ? 0f : MathHelper.Max(0f, best);
	}

	// How far from a threat's HULL the AI wants to stay, scaled by how big the hull is. The 2008
	// code used one flat 150px for everything, which is nothing next to the spider boss -- by the
	// time the field pushed at all the ship was inside the hitbox, and the fight read as the bot
	// having no idea what it was doing.
	// Per-type repellent multiplier. One switch, applied to BOTH repulsion paths (the radial
	// field and EvadeMovingThreat), so a type cannot end up scaled on one and not the other --
	// which on an asteroid would be a silent half-fix, since the belt uses both.
	// Per-type RANGE multiplier, folded into ThreatFieldRange so every caller agrees on how big
	// the field is -- `dist <= field` and `dist / field` must be the same field or the falloff is
	// evaluated against a range the gate never used.
	// Per-type FALLOFF exponent. Falls back to the global one for every type that has no override.
	// Strength across that field: FULL up close, dropping away fast so the outer half is
	// effectively free. That combination is the point -- a big field with a gentle falloff would
	// be a no-go zone the ship could never enter, and it still has to fly in close to shoot and
	// to weave through bullets.
	//
	// Deliberately NOT MyMath.PowerCurve: that is `max * (1 - t^p)`, whose falloff gets SHALLOWER
	// as p rises (p=4 still pushes at 34% strength at 90% of the range). This is `max * (1-t)^p`,
	// which is the shape the name "falloff" implies -- p=3 is down to 12% at half range.
	// THE TWO CURVE FAMILIES ARE DIFFERENT SHAPES, NOT DIFFERENT EXPONENTS (card e88e21ca).
	//   classic (2008): max * (1 - t^2)  -- MyMath.PowerCurve. A fat PLATEAU: 75% strength at half
	//                   range, still 36% at 80%. The whole field pushes.
	//   port:           max * (1 - t)^p  -- a SPIKE: 12% at half range with p=3. Only the inner
	//                   fifth pushes meaningfully.
	// `?aifieldfall=` only ever swept p WITHIN the port's family, so the 2008 SHAPE had never been
	// tested at all -- and the port's own comment argues against PowerCurve on the grounds that
	// its falloff gets shallower as p rises, which is a reason to not raise p, not a reason to
	// change family. The user's testimony is that the 2008 bot beat SpaceDodge with circles only.
	private static float ThreatFieldStrength(float t, float maxSteerStrength, float falloff, bool classic)
	{
		if (classic)
		{
			return MyMath.PowerCurve(maxSteerStrength, 0f, 2f, t);
		}
		float u = 1f - MathHelper.Clamp(t, 0f, 1f);
		return maxSteerStrength * (float)Math.Pow(u, falloff);
	}

	// CLASSIC IS THE DEFAULT AGAIN (owner ruling, iterative rep 1): every threat field and the
	// beam term run 2008's plateau. The spike family stays reachable via ?aifieldcurve=port
	// (?aifieldfall= only shapes that arm).
	// Per-type curve family, falling back to the global switch.
	// The boss-approach attractor, solved (card b56633fb). PURE -- primitives in, weight out, no
	// ship and no component -- so logic_probe can sweep it over every difficulty tier and the whole
	// bulletlifetime range with no game running. See BossApproachExponent for the shape argument.
	//   edgeDist   how far the ship's hull is from the boss's, now
	//   anchorPx   r*: firing range expressed in that same EDGE space (gun range - body term)
	//   the rest   the boss's OWN repellent parameters, passed through so the weight is solved
	//              against the very field it has to cross rather than a restatement of it
	//   minWeight  the whole-sum floor; without it a weapon out-ranging the field solves to w=0
	//              (repel is genuinely zero out there) and the attractor would go inert exactly
	//              when it is most needed
	public static float BossApproachWeight(float edgeDist, float anchorPx, float fieldRange,
		float falloff, bool classic, float typeScale, float maxSteerStrength, float minWeight)
	{
		float anchor = MathHelper.Max(anchorPx, BossApproachMinAnchorPx);
		// Clamped before the curve, not inside it: the classic family is max*(1-t^2), which goes
		// NEGATIVE past t=1, and an anchor beyond the field's radius is the ordinary Range-powerup
		// case rather than an error.
		// ONE guarded divisor for all three ratios below: ?aifieldpx=0 really can make the field
		// radius zero, and the slope probe divides by it too.
		float safeRange = (fieldRange > 0f) ? fieldRange : 1f;
		float t = MathHelper.Clamp(anchor / safeRange, 0f, 1f);
		float w = ThreatFieldStrength(t, maxSteerStrength, falloff, classic) * typeScale;
		w = MathHelper.Max(w, minWeight);

		// THE EXPONENT IS DAMPED SO THE PARKED BAND SURVIVES A BIG HULL UP CLOSE, and this is
		// derived, not tuned. The band the whole-sum floor manufactures is
		// 2*floor / (|A'| + |repel'|) wide, and |A'(r*)| = k*w/r* -- so a boss whose hull eats most
		// of the weapon's reach has a small r* with a LARGE w sitting on it, and the linear k=1
		// curve turns over too fast for the ship to stop inside the band.
		//
		// THE ONE CONFIGURATION THAT MAKES THIS BITE -- do not delete it as dead code. BrainBoss at
		// its pulse peak on the base weapon: its hitbox is hw = 165 * scale and `scale` pulses
		// 1.00 -> 1.10 (deeper as its HP drops), so the body term runs 233 -> 257px against a
		// 351px gun range, leaving r* at 118 -> 94px. Undamped that bands 13.5px at scale 1.0 and
		// 10.0px at the peak -- through the 11.3px stopping distance, i.e. the ship coasts across
		// its own equilibrium and pingpongs while shooting the brain. Damped, k solves to 0.24 at
		// rest and 0.09 at the peak, and the band is 22.2px at both. Every OTHER halting boss, tier and weapon in the game
		// solves to k = 1 and is untouched (the next-tightest band is 53px), and any Range powerup
		// removes the case entirely by growing r*.
		//
		// The repellent's slope is measured on the REAL curve rather than differentiated by hand,
		// so this stays correct across both curve families and any per-type falloff.
		float h = 1f;
		float repelSlope = Math.Abs(ThreatFieldStrength(MathHelper.Clamp((anchor - h) / safeRange, 0f, 1f), maxSteerStrength, falloff, classic)
			- ThreatFieldStrength(MathHelper.Clamp((anchor + h) / safeRange, 0f, 1f), maxSteerStrength, falloff, classic)) * typeScale / (2f * h);
		float stoppingPx = ShipMaxSpeed * ShipMaxSpeed / (2f * ShipDeceleration);
		float slopeBudget = 2f * minWeight / (BossApproachBandMargin * stoppingPx) - repelSlope;
		// A budget of zero means the repellent alone is steeper than the bound allows: nothing the
		// attractor does can widen the band, so it goes FLAT (k=0, a constant w) -- the best
		// available shape there, and the constant-weight design the card started from.
		float k = (w > 0f)
			? MathHelper.Clamp(MathHelper.Max(slopeBudget, 0f) * anchor / w, 0f, BossApproachExponent)
			: BossApproachExponent;

		float reach = MathHelper.Max(edgeDist, 0f) / anchor;
		float pull = w * (float)Math.Pow(reach, k);
		// THE CEILING BINDS GROWTH, NOT THE ANCHOR -- hence max(cap, w) rather than the cap alone.
		// It exists so the pull far from the boss cannot out-vote a full-strength threat field; at
		// r* itself the pull IS the repellent by construction, so it can never overpower anything
		// there, and clamping it below w would move the crossing outward and leave the net pointing
		// AWAY at firing range. A deep field with a shallow curve reaches that: at
		// ?aifieldcurve=classic the repellent at r* can be 3.75 against the 3.5 cap (found by
		// ProbeAiBossApproach sweeping both curve families, not by inspection).
		return MathHelper.Clamp(pull, 0f, MathHelper.Max(BossApproachMaxWeight, w));
	}

	// Centre-to-EDGE offset of a threat's hull -- what `dist` subtracts from a centre distance to
	// get the EDGE distance every field here is measured in. Extracted from the four-branch switch
	// that used to sit inline in DoAIMove (card b56633fb) because the boss-approach anchor has to
	// convert gun RANGE (a centre distance, from DoAIFire) into that same edge space: two copies of
	// this switch would let the attractor and the repellent it is solved against drift apart, which
	// is the one way the crossing could silently stop being at firing range.
	// Not `ThreatRadius`: that is the half-extent the FIELD SIZE scales with, this is the sqrt(2)
	// corner-inclusive offset the DISTANCE is measured from. They differ by that factor on a box.
	private static float ThreatBodyTerm(AlienDrawableGameComponent baddy)
	{
		ICollisionType type = baddy.GetCollisionType();
		if (type is CollisionBox)
		{
			return ((CollisionBox)type).Width / 2f * (float)Math.Sqrt(2.0);
		}
		if (type is CollisionMultibox)
		{
			return ((CollisionMultibox)type).Items[0].Width / 2f * (float)Math.Sqrt(2.0);
		}
		if (type is CollisionSimpleCircle)
		{
			return ((CollisionSimpleCircle)type).Radius;
		}
		return 0f;
	}

	// Edge distance from a point to a threat. The circle branch CLAMPS and the others do not --
	// that asymmetry is inherited verbatim from the 2008 code and is preserved on purpose; a box's
	// edge distance goes negative once the ship is inside the corner radius, which the (1-t)^p
	// falloff already saturates at full strength.
	private static float ThreatEdgeDistance(Vector2 from, AlienDrawableGameComponent baddy)
	{
		Vector2 toBaddy = from - baddy.Position;
		float dist = (toBaddy).Length() - ThreatBodyTerm(baddy);
		return (baddy.GetCollisionType() is CollisionSimpleCircle) ? MathHelper.Clamp(dist, 0f, 1000f) : dist;
	}

	// Rough half-extent of a threat, so a boss the size of a quarter of the screen is given more
	// room than a bullet. Mirrors the collision-type switch the radial branch uses.
	private static float ThreatRadius(AlienDrawableGameComponent baddy)
	{
		ICollisionType type = baddy.GetCollisionType();
		if (type is CollisionBox)
		{
			return ((CollisionBox)type).Width / 2f;
		}
		if (type is CollisionMultibox)
		{
			return ((CollisionMultibox)type).Items[0].Width / 2f;
		}
		if (type is CollisionSimpleCircle)
		{
			return ((CollisionSimpleCircle)type).Radius;
		}
		return 0f;
	}

	// ---- Level-3 wall navigation (card f4d1721f, rewritten) --------------------------------
	//
	// The wall is a scrolling bool grid (CollisionLevelMap). Its rows come DOWN at the ship, so
	// in wall-local coords the ship is climbing: row y-1 is what arrives next. Touching any
	// occupied tile is AsplodeWall() -- instant death -- so this is the one place the AI cannot
	// afford to be approximate.
	//
	// What the 2008 APPROACH steer did, and what this replaced. Reachable for real since card
	// d79b7ea7 -- `?aiwallnav2008=1`, transcribed below as SteerThroughWall2008:
	//   * it probed a fixed `1.2 * dtMs * MaxSpeed` = ~6.6px ahead at 60Hz, against tiles
	//     67..267px wide -- a fifth of a ship-width of warning at full closing speed;
	//   * it re-picked left-vs-right every single tick, and a wall scrolling on by one row can
	//     swap which side is cheaper, reversing the ship mid-approach.
	// This version looks ahead by TIME, pushes proportionally, and commits to a gap.
	//
	// TWO CLAIMS THAT USED TO LIVE HERE WERE WRONG, both corrected by card d79b7ea7's audit:
	//   * the SLAM (`direction.X = -max(|direction.Y|, 1)`) was listed as a third thing this
	//     replaced. It is not: the slam is `ClampIntoWallSpace`, which the port KEPT verbatim --
	//     see its own comment. `?aiwallnav2008=1` does not switch it either, because there is
	//     nothing to switch it to.
	//   * "together those spun the commanded heading at ~1050 deg/s" is not this term's figure.
	//     That churn is the missing steering LOW-PASS (card 05a2b818 reproduces it cleanly at
	//     `?aismooth=0`, and validates the low-pass on it); with the low-pass in place BOTH wall
	//     algorithms are smooth, and the 2008 one is if anything smoother while dying more.
	//     So do not defend these constants on churn -- they earn their keep on SURVIVAL, and the
	//     audit's paired numbers are in web/EvilAliensWeb/CLAUDE.md.

	// Steer toward the committed gap in this wall, and away from tiles that are close in the
	// direction of travel. Called once per Wall in the steering loop; only ever adds to
	// `direction`, so it composes with every other steering term like they compose with each
	// other. The hard "do not fly into that" clamp is ClampIntoWallSpace, applied last.
	private void SteerThroughWall(ref Vector2 direction, Wall wall, CollisionLevelMap map)
	{
		AnnounceWallNav("port");
		CollisionBox box = (CollisionBox)GetCollisionType();
		int x = 0;
		int y = 0;
		map.GetMapCoords(ref x, ref y, base.Position);
		float tile = map.TileSize;
		int column = ChooseGapColumn(x, y, map, box.Width);
		// Per-seat lateral offset inside the chosen column. The 2008 wall code gave each slot its
		// own nudge (8/4/6/10) precisely so co-op ships did not stack; without an equivalent every
		// AI ship computes the same column and drives at the same point, which in a Level-3 wall
		// -- where a touch is instant death -- turns four Mechanical Friends into one collision.
		// Spread by seat ORDINAL (the roster can be sparse), clamped well inside the tile so the
		// offset can never push a ship into the column's own wall.
		float seatSpread = 0f;
		int seated = oracle.Players;
		if (seated > 1)
		{
			float slot = (float)oracle.SeatOrdinal(player) / (float)(seated + 1) - 0.5f;
			seatSpread = slot * tile * GapSeatSpreadFraction;
		}
		float dx = map.ColumnCentreX(column) + seatSpread - base.Position.X;
		// How much room the ship has in ITS OWN column. Measured in PIXELS to the face of the
		// first blocked row, not in rows: a row count cannot distinguish "a slab is 60px above
		// me" from "a slab is 1000px above me", and treating those alike makes the avoidance push
		// either permanent (it was -- the ship pinned itself against the bottom of the screen and
		// stopped steering entirely) or far too late. Closing speed is the ship's own top speed
		// plus the wall's scroll, so `reach` is the distance it can actually still react within.
		float closing = base.MaxSpeed + (wall.ObservedVelocity).Length();
		float reach = MathHelper.Max(closing * WallReactionMs, box.Height);
		float gapPx = DistanceToBlockedRow(x, y, map);
		float urgency = 1f - MathHelper.Clamp(gapPx / MathHelper.Max(reach, 1f), 0f, 1f);
		if (Math.Abs(dx) > tile * 0.15f)
		{
			// A committed lateral move is worth pressing: the gap is the only survivable place to
			// be, and the wall puts a deadline on getting there. Scaled well above the generic steer
			// terms (maxSteerStrength 4) so a stray powerup pull cannot drift the ship out of the
			// slot it is threading.
			float lateral = MathHelper.Lerp(WallLateralIdle, WallLateralUrgent, urgency);
			direction += new Vector2((float)Math.Sign(dx) * lateral, 0f);
		}
		// Back off downward while a blocked row is genuinely closing -- that buys the time the
		// lateral move needs, and it is the only thing that helps when the ship is directly under
		// a slab. NOT gated on dx: under a block with nowhere better to be, retreating is still
		// the right answer. Positive Y is down (screen coords).
		if (urgency > 0f)
		{
			direction += new Vector2(0f, WallBackOff * urgency);
		}
	}

	// Pixels from the ship to the bottom face of the first blocked row above it in its own
	// column, or float.MaxValue when nothing is blocked within the scan. This is the number the
	// urgency ramp needs -- see the SteerThroughWall comment for what using a row COUNT here did.
	private float DistanceToBlockedRow(int x, int y, CollisionLevelMap map)
	{
		int clear = RowsClearAhead(x, y, map);
		if (clear >= WallScanRows)
		{
			return float.MaxValue;
		}
		// Rows above the ship are y-1, y-2, ...; the first blocked one is y-clear-1, and the face
		// that reaches the ship is its bottom edge.
		return MathHelper.Max(base.Position.Y - map.RowBottomY(y - clear - 1), 0f);
	}

	// How many clear rows sit above the ship in `column`-agnostic terms: the distance, in rows,
	// to the first occupied tile straight ahead. Caps out -- past the look-ahead the exact number
	// stops mattering and scanning further is wasted work.
	private static int RowsClearAhead(int x, int y, CollisionLevelMap map)
	{
		for (int i = 1; i <= WallScanRows; i++)
		{
			if (map.TileIsOccupied(x, y - i))
			{
				return i - 1;
			}
		}
		return WallScanRows;
	}

	// Pick the column to thread, and STICK to it. Replaces findNextTileOnMap, whose per-tick
	// left-vs-right re-decision was one of the three jitter sources. Two rules make it a plan
	// rather than a twitch:
	//   * a candidate must be wide enough for the ship (`shipWidth`), not merely a free tile;
	//   * the committed column is only abandoned when a rival beats it by GapSwitchMargin tiles,
	//     or when it stops being passable at all.
	private int ChooseGapColumn(int x, int y, CollisionLevelMap map, float shipWidth)
	{
		// Tiles the ship's box actually overlaps. NOT ceil(width/tile): a 29px box in a 114px tile
		// straddles TWO tiles whenever it sits near a boundary, and ceil() reports one -- which
		// made the advertised "does the ship fit" check a no-op on every shipped grid (all are
		// width=7). floor()+1 is the honest worst case.
		int span = (int)(shipWidth / map.TileSize) + 1;
		// Seed with the ship's OWN column so "stay where I am" is a real candidate. Starting from
		// float.MinValue made it dead code -- the blocked-column sentinel is greater, so the first
		// iteration always won and a fully-blocked row would have steered hard at column 0.
		int best = x;
		float bestScore = ColumnScore(x, x, y, map, span);
		for (int c = 0; c < map.Width; c++)
		{
			float score = ColumnScore(c, x, y, map, span);
			if (score > bestScore)
			{
				bestScore = score;
				best = c;
			}
		}
		// Hysteresis: only abandon the committed column when a rival beats it by a margin. The
		// 2008 search re-decided left-vs-right EVERY tick, and a wall scrolling on by one row can
		// swap which side is cheaper -- so the ship reversed mid-approach, forever. This is what
		// turns a gap choice into a plan.
		if (aiGapColumn >= 0 && aiGapColumn < map.Width)
		{
			float heldScore = ColumnScore(aiGapColumn, x, y, map, span);
			if (heldScore >= bestScore - GapSwitchMargin)
			{
				return aiGapColumn;
			}
		}
		aiGapColumn = best;
		return best;
	}

	// How good a column is to be in, as a single comparable number. GRADED rather than a
	// pass/fail test on purpose: inside a dense maze section there is often no column that is
	// clear for the full look-ahead, and a boolean "passable" test then reports nothing passable
	// -- which in an earlier revision of this code made the AI hold station and let the wall
	// scroll into it. There is always a least-bad column, and the AI must always be heading for
	// one.
	//   + rows of clearance ahead (dominant: being alive next second beats being efficient)
	//   - how far the ship must travel sideways
	//   - a penalty per blocked column it would have to cross to get there
	// A column whose own row is blocked scores far below everything else: the ship cannot be
	// there at all.
	private static float ColumnScore(int c, int x, int y, CollisionLevelMap map, int span)
	{
		int half = span / 2;
		for (int col = c - half; col <= c + half; col++)
		{
			if (map.TileIsOccupied(col, y))
			{
				return float.MinValue / 2f;
			}
		}
		// Clearance of the narrowest point across the ship's full width -- checking the ship's
		// real footprint is what stops the AI committing to a slot it physically cannot fit
		// through, which the old single-tile test could not see.
		int clearance = WallScanRows;
		for (int col = c - half; col <= c + half; col++)
		{
			clearance = Math.Min(clearance, RowsClearAhead(col, y, map));
		}
		return (float)clearance * WallRowWeight
			- (float)Math.Abs(c - x)
			- (float)BlockedBetween(x, c, y, map) * WallCrossPenalty;
	}

	// Blocked columns strictly between `from` and `to` on the ship's own row and the one above --
	// the cells it would have to pass through to get there.
	private static int BlockedBetween(int from, int to, int y, CollisionLevelMap map)
	{
		int lo = Math.Min(from, to);
		int hi = Math.Max(from, to);
		int blocked = 0;
		for (int c = lo + 1; c < hi; c++)
		{
			if (map.TileIsOccupied(c, y) || map.TileIsOccupied(c, y - 1))
			{
				blocked++;
			}
		}
		return blocked;
	}

	// The last-resort "do not fly into that" clamp, applied after every other steering term.
	//
	// **THIS IS THE 2008 BLOCK, not a port-era replacement** (card d79b7ea7, struck from the audit
	// on source inspection rather than measured). `DoAIMove`'s `if (flag)` tail in
	// `src_decompiled/EvilAliens/PlayerShip.cs` lines 1114-1158 has the same two side probes at
	// `41.666668 * MaxSpeed`, the same ungated upward probe at 3x that, and the same
	// `direction.X = -max(|direction.Y|, 1)` slam. The port differs in three ways, none
	// behavioural: `WallClampMs` is 42 rather than 41.666668 (13.86px against 13.75px at
	// MaxSpeed 0.33, 0.8%), the two corner probes per side are OR-ed into one bool instead of
	// applying the identical assignment twice, and the 3x lives in a named
	// `WallClampUpFactor`. So there was no port value here to audit, and `?aiwallnav2008=1`
	// deliberately does not switch this half.
	//
	// Unlike the 2008 gap-approach steer this fires only when a tile is within roughly ONE TICK of travel,
	// where the reversal is genuinely correct and cannot alternate -- at that range the probe
	// stays hit until the ship is actually clear. Everything further out is handled by the
	// proportional steer in SteerThroughWall.
	private void ClampIntoWallSpace(ref Vector2 direction, CollisionLevelMap map)
	{
		CollisionBox box = (CollisionBox)GetCollisionType();
		float reach = base.MaxSpeed * WallClampMs;
		int cx = 0;
		int cy = 0;
		if (direction.X > 0f)
		{
			map.GetMapCoords(ref cx, ref cy, box.BottomRight + new Vector2(reach, 0f));
			bool hit = map.TileIsOccupied(cx, cy);
			map.GetMapCoords(ref cx, ref cy, box.TopRight + new Vector2(reach, 0f));
			hit |= map.TileIsOccupied(cx, cy);
			if (hit)
			{
				direction.X = 0f - MathHelper.Max(Math.Abs(direction.Y), 1f);
			}
		}
		else if (direction.X < 0f)
		{
			map.GetMapCoords(ref cx, ref cy, box.BottomLeft + new Vector2(0f - reach, 0f));
			bool hit = map.TileIsOccupied(cx, cy);
			map.GetMapCoords(ref cx, ref cy, box.TopLeft + new Vector2(0f - reach, 0f));
			hit |= map.TileIsOccupied(cx, cy);
			if (hit)
			{
				direction.X = MathHelper.Max(Math.Abs(direction.Y), 1f);
			}
		}
		// Upward is the dangerous axis: the wall closes on the ship whether or not it is moving,
		// so this probe is not gated on direction.Y and reaches further.
		float up = reach * WallClampUpFactor;
		map.GetMapCoords(ref cx, ref cy, box.TopLeft + new Vector2(0f, 0f - up));
		bool above = map.TileIsOccupied(cx, cy);
		map.GetMapCoords(ref cx, ref cy, box.TopRight + new Vector2(0f, 0f - up));
		above |= map.TileIsOccupied(cx, cy);
		if (above)
		{
			direction.Y = MathHelper.Max(Math.Abs(direction.X), 1f);
		}
	}

	// ---- the 2008 wall navigation, kept reachable as the audit's null hypothesis ----------------
	//
	// `?aiwallnav2008=1` (card d79b7ea7) swaps `SteerThroughWall` for this. It exists because the
	// port's wall-nav constants could not be audited against the original AT ALL by flag: the
	// 2008 code is a different ALGORITHM, so no setting of `?aireact`/`?aiscanrows`/
	// `?aicrosspenalty`/`?aigapmargin` reconstitutes it, and card 05a2b818's bar for a port value
	// ("beat the original in a paired A/B, ties revert") needs an original to run against.
	//
	// TRANSCRIBED VERBATIM from `src_decompiled/EvilAliens/PlayerShip.cs` -- the block at lines
	// 849-914 of `DoAIMove` and `findNextTileOnMap` at 1167-1228 -- so a reviewer can diff it by
	// eye. Nothing here is improved, tidied or corrected: a reference arm that has drifted
	// measures nothing. Only the decompiler's slot names (`num6`, `num7`) are given readable
	// names, the empty `else if (target_x != x) { }` tail is dropped as unreachable dead code,
	// and the two `Vector2`/`CollisionBox` casts follow this file's normal style.
	//
	// NOT included, deliberately: the `if (flag)` hard clamp that follows it in the original.
	// `ClampIntoWallSpace` IS that block -- same probes (`41.666668 * MaxSpeed`, 3x upward), same
	// slam -- so the port never replaced it and there is nothing to switch. See its own comment.
	private void SteerThroughWall2008(ref Vector2 direction, CollisionLevelMap map, GameTime gameTime)
	{
		AnnounceWallNav("2008");
		// `num6`: the approach probe, ~6.6px at 60Hz and MaxSpeed 0.33. (NOT the ~13.75px figure
		// this file quotes elsewhere -- that is the CLAMP's `41.67 * MaxSpeed` probe, which the
		// port kept.) Against tiles 67..267px wide it is a fifth of a tile of warning.
		float probe = 1.2f * (float)gameTime.ElapsedGameTime.TotalMilliseconds * base.MaxSpeed;
		// Per-seat push strength, so four co-op ships did not steer identically.
		float push = 0f;
		if (player == 0)
		{
			push = 8f;
		}
		if (player == 1)
		{
			push = 4f;
		}
		if (player == 2)
		{
			push = 6f;
		}
		if (player == 3)
		{
			push = 10f;
		}
		CollisionBox box = (CollisionBox)GetCollisionType();
		int x = 0;
		int y = 0;
		map.GetMapCoords(ref x, ref y, base.Position);
		int target_x = 0;
		int target_y = 0;
		FindNextTileOnMap2008(x, y, ref target_x, ref target_y, map);
		// The original re-runs GetMapCoords inside each branch, clobbering x and y after they have
		// been used for the branch decision. Kept as-is.
		if (target_y < y)
		{
			map.GetMapCoords(ref x, ref y, new Vector2(box.Left - probe, base.Position.Y));
			if (map.TileIsOccupied(x, y - 1))
			{
				direction += new Vector2(push, 0f);
			}
			map.GetMapCoords(ref x, ref y, new Vector2(box.Right + probe, base.Position.Y));
			if (map.TileIsOccupied(x, y - 1))
			{
				direction += new Vector2(0f - push, 0f);
			}
		}
		else if (target_x > x)
		{
			map.GetMapCoords(ref x, ref y, new Vector2(box.Left - probe, base.Position.Y));
			if (map.TileIsOccupied(x, y - 1))
			{
				direction += new Vector2(push, 0f);
			}
			if (map.TileIsOccupied(target_x, y - 1))
			{
				direction += new Vector2(0f, push);
			}
		}
		else if (target_x < x)
		{
			map.GetMapCoords(ref x, ref y, new Vector2(box.Right + probe, base.Position.Y));
			if (map.TileIsOccupied(x, y - 1))
			{
				direction += new Vector2(0f - push, 0f);
			}
			if (map.TileIsOccupied(target_x, y - 1))
			{
				direction += new Vector2(0f, push);
			}
		}
	}

	// `findNextTileOnMap`, verbatim from src_decompiled/EvilAliens/PlayerShip.cs lines 1167-1228.
	// Straight up if the tile above is clear; otherwise walk left and right along the ship's own
	// row and the one above it, take whichever side reaches a clear column first, and break a tie
	// by seat index. No look-ahead depth, no cost for the columns crossed, and NO memory -- it is
	// re-decided every tick, which is the flip-flop `GapSwitchMargin` was added to stop.
	private void FindNextTileOnMap2008(int x, int y, ref int target_x, ref int target_y, CollisionLevelMap map)
	{
		if (!map.TileIsOccupied(x, y - 1))
		{
			target_x = x;
			target_y = y - 1;
			return;
		}
		int scan = x - 1;
		int leftCost = 0;
		while (map.TileIsOccupied(scan, y) || map.TileIsOccupied(scan, y - 1))
		{
			leftCost++;
			scan--;
			if (scan < 0)
			{
				leftCost = 1000;
				break;
			}
		}
		scan = x + 1;
		int rightCost = 0;
		while (map.TileIsOccupied(scan, y) || map.TileIsOccupied(scan, y - 1))
		{
			rightCost++;
			scan++;
			if (scan >= map.Width)
			{
				rightCost = 1000;
				break;
			}
		}
		if (leftCost < rightCost)
		{
			target_x = x - 1;
			target_y = y;
			return;
		}
		if (leftCost > rightCost)
		{
			target_x = x + 1;
			target_y = y;
			return;
		}
		if (player == 0)
		{
			target_x = x - 1;
		}
		if (player == 1)
		{
			target_x = x + 1;
		}
		if (player == 2)
		{
			target_x = x - 1;
		}
		if (player == 3)
		{
			target_x = x + 1;
		}
		target_y = y;
	}

	private void getDistanceToLine(AlienDrawableGameComponent alien, out float d, out Vector2 shortestpoint)
	{
		Vector2 start = ((CollisionLine)((Lazer)alien).GetCollisionType()).Start;
		Vector2 end = ((CollisionLine)((Lazer)alien).GetCollisionType()).End;
		Vector2 position = base.Position;
		if (start == end)
		{
			shortestpoint = start;
			Vector2 toStart = position - start;
			d = (toStart).Length();
			return;
		}
		// One decompiled slot serving two roles: `t` holds the raw dot product on this line and
		// only becomes the normalised 0..1 position along the segment after the divide below.
		float t = (position.X - start.X) * (end.X - start.X) + (position.Y - start.Y) * (end.Y - start.Y);
		float dot = t;
		Vector2 segment = end - start;
		t = dot / (segment).LengthSquared();
		if (t < 0f)
		{
			shortestpoint = start;
		}
		else if (t > 1f)
		{
			shortestpoint = end;
		}
		else
		{
			shortestpoint = start + t * (end - start);
		}
		Vector2 toClosest = position - shortestpoint;
		d = (toClosest).Length();
	}

	private void FireAt(float direction)
	{
		// The pacifist awardment watches the INTENT, so it is reset outside the gate -- holding
		// the trigger down counts as shooting even on the ticks the cadence swallows.
		pacifistTimer.Reset();
		pacifistTimer.Start();
		if (shoottimer.Finished | !shoottimer.Active)
		{
			shoottimer.Start();
			// Net seam (card a45b78f6): the co-op ship stream carries a CUMULATIVE count of the
			// shots this ship really spawned, and the aim of the newest one. Both are stamped
			// HERE, inside the cadence gate and beside the Bullet, so "a shot the owner fired"
			// and "an increment on the wire" are the same event by construction -- which is what
			// makes two taps inside one cadence period one bullet on BOTH screens. It wraps; the
			// receiver only ever reads the delta.
			NetShotCount++;
			NetLastFireAim = direction;
			SpawnShot(direction);
		}
	}

	// The shot itself, factored out of FireAt so the co-op puppet path can spawn a replicated
	// shot through the REAL construction (bounce/asplode rolls, cue and all) without also
	// inheriting the local cadence gate -- the receiver is paced by the owner's counter, and
	// gating it a second time here is the arithmetic card a45b78f6 deleted.
	private void SpawnShot(float direction)
	{
		Bullet bullet = Bullet.NewBullet(collection, base.Game);
		bullet.Setup(base.Position, direction, bulletlifetime, player);
		if ((float)RandomHelper.Random.Next(100) < bouncebulletspercentage)
		{
			bullet.SetBouncing(bounceamount);
			bullet.SetSplit(bulletsSplit);
		}
		if ((float)RandomHelper.Random.Next(100) < asplodingbulletspercentage)
		{
			bullet.SetAsploding(asplodingbulletssize);
		}
		collection.Add((GameComponent)(object)bullet);
		sound.PlayCue("fire");
	}

	private void DoSpecial(bool pickup)
	{
		if (!pickup)
		{
			return;
		}
		switch (currentPower)
		{
		case Powerup.PowerupType.Linker:
			readyToConnect = true;
			break;
		case Powerup.PowerupType.Blast:
			Score.AddBomb(player);
			break;
		case Powerup.PowerupType.Option:
			SpawnPickupOptions();
			break;
		case Powerup.PowerupType.FirePower:
			shotspersec++;
			shotspersec = Math.Min(shotspersec, 18);
			shoottimer.Duration = 1000f / (float)shotspersec;
			break;
		case Powerup.PowerupType.Range:
			bulletlifetime = MathHelper.Min(70f + bulletlifetime, 1500f);
			break;
		case Powerup.PowerupType.OneUp:
			Score.AddLife();
			break;
		}
	}

	// The Option pickup's spawn. LOCAL ONLY since card c5228350 -- a puppet's population is
	// reconciled to its owner's reported per-layer count (NetSetOptionCounts) rather than derived
	// from this arithmetic a second time. Still its own method because the count depends on
	// optionLevel, which is what made deriving it remotely wrong (card 10f9dba4: an observer's
	// optionLevel lags the owner's by up to one HUD packet).
	private void SpawnPickupOptions()
	{
		int perLayer = 1;
		int layers = 1;
		if (optionLevel == 3)
		{
			perLayer = 2;
		}
		if (optionLevel == 4)
		{
			layers = 2;
		}
		for (int i = 0; i < layers; i++)
		{
			for (int j = 0; j < perLayer; j++)
			{
				Option option = Option.NewOption(collection, base.Game);
				option.Setup(this, 0f, i + 1, player);
				collection.Add((GameComponent)(object)option);
				options[i].Add(option);
			}
		}
		RedressOptions();
	}

	// Online co-op (cards 83271f3d / 10f9dba4 / c5228350): the OTHER peer's player collected a powerup, and
	// this ship is their puppet here. Mirror the SHIP-side half of the local pickup path
	// (DoSpecial(pickup: true)) so the puppet is not a player with a HUD icon and none of the
	// effect. NetSession.ApplyRemotePowerup is the only caller and gates on !OwnsSlot, so this
	// never runs for a ship whose pickup already went through CollidesWith.
	//
	// Only ONE type is not already replicated by some other means:
	//   Linker    -- readyToConnect is set NOWHERE else, so without this the "2" powerup's glow
	//                never appears on the puppet AND PlayerShip.CollidesWith's
	//                (readyToConnect & other.readyToConnect) is false on BOTH peers, i.e. the
	//                connector is unreachable in an online session (card 83271f3d).
	// The other five are deliberately inert here:
	//   Option    -- the whole population rides MsgHudState as a per-layer COUNT since card
	//                c5228350, and the owner is authoritative over it (NetSetOptionCounts). This
	//                path used to spawn the pickup's own 1-4 alongside the level-driven ones
	//                arriving over the HUD packet, which added up correctly in steady state and
	//                could not work at all for a JOIN-IN-PROGRESS peer -- it replays no claims,
	//                so it reconstructed the level half alone and always saw fewer. Two derived
	//                sources for one population is the defect; do not restore this case as a
	//                "low latency estimate" beside the count.
	//   FirePower -- shotspersec rides MsgShipState (NetApplyRemoteState) already.
	//   Range     -- bulletlifetime likewise.
	//   Blast     -- AddBomb is deliberately not mirrored: the SPEND side (NetDoBlast) does not
	//                decrement the remote's bombs either, so mirroring the increment alone would
	//                pile the other player's bomb icons up forever.
	//   OneUp     -- lives are host-authoritative (EvScoreSync sends them verbatim); the host
	//                credits a client's extra life in NetSession.HandleClaim instead.
	internal void NetApplyRemotePickup(Powerup.PowerupType type)
	{
		switch (type)
		{
		case Powerup.PowerupType.Linker:
			readyToConnect = true;
			break;
		}
	}

	private void doPowerupEffect()
	{
		powerupEffect = PowerupEffect.NewPowerupEffect(collection, base.Game);
		powerupEffect.Setup(base.Position, 1f, 0.6f, 0f, base.Direction);
		collection.Add((GameComponent)(object)powerupEffect);
	}

	public override void CollidesWith(ICollidable other)
	{
		if (other is PlayerShip && (readyToConnect & ((PlayerShip)other).readyToConnect) && !isConnectedWith(other))
		{
			ShipConnector shipConnector = ShipConnector.NewAlien(collection, base.Game);
			shipConnector.Setup(this, (PlayerShip)other);
			((PlayerShip)other).connectors.Add(shipConnector);
			connectors.Add(shipConnector);
			collection.Add((GameComponent)(object)shipConnector);
			bool hasHumanPlayer = false;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				if (ship.controller != ControlDevice.AI)
				{
					hasHumanPlayer = true;
				}
			}
			if (oracle.NrOfShipConnectors() == 3 && hasHumanPlayer)
			{
				ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Coop);
			}
		}
		// AI bench (card f4d1721f): score the wall touch BEFORE the invulnerability gate below.
		// A wall touch is AsplodeWall(), i.e. instant death, so an honest run ends at the first
		// mistake and measures one wall section; ?invuln lets the soak cover all six -- but only
		// if the clip is still counted here, or the run that survives everything is exactly the
		// run that reports zero mistakes.
		if (other is Wall && !hasWon && EffectiveController() == ControlDevice.AI)
		{
			EvilAliensWeb.Compat.AiBench.NoteWallContact(this);
		}
		if ((other is UFO || other is Lazer || other is Boss || other is Braineroid || other is EvilBullet || other is Asteroid || other is Ball || other is JunkBoss || other is DeathStar || other is ClassicBoss || other is StationaryBoss || other is Spider || other is MarsBoss || other is BattleSkull || other is Wall || other is FlyingSpider || other is Explosion || other is StarMine || other is PlasmaBall || other is BrainBoss || other is FakeBoss || other is SweepUFO || other is SpiderBoss || other is PunchingBag || (other is EvilSkull && !((EvilSkull)other).Fading)) && (!invulnerabilityTimer.Active & !hasWon))
		{
			if (connectors.Count > 0)
			{
				foreach (ShipConnector connector in connectors)
				{
					connector.TakeHit();
				}
			}
			// DebugFlags.Invuln (?invuln) is a session-only runtime override -- it must NEVER
			// write into Settings.Invulnerability (that would persist into the save; see Game1's
			// startScreen_OnFinished comment for the history of that bug).
			// A Remote puppet never takes damage locally: under distributed authority its OWNER
			// decides when it was hit (you never die to something you dodged on your screen) --
			// its death arrives via the ship stream's alive flag instead (Compat/Net/NetSession).
			else if (!Settings.GetInstance().Invulnerability && !DebugFlags.Invuln && !IsNetPuppet)
			{
				if (other is Wall)
				{
					AsplodeWall();
				}
				else if (other is AlienDrawableGameComponent)
				{
					if (!((AlienDrawableGameComponent)other).IsDead)
					{
						queueAsplosion(other);
						((AlienDrawableGameComponent)other).OnDeath += Killer_OnDeath;
					}
				}
				else
				{
					Asplode();
				}
			}
		}
		if (other is Floorbottom)
		{
			base.Position = new Vector2(base.Position.X, ((Floorbottom)other).Bottom - ((CollisionBox)GetCollisionType()).Height / 2f);
		}
		// A Remote puppet can't grab powerups: pickups are CLAIMS under distributed authority
		// (replicated as events in card 11.3) -- letting the puppet take one here would steal
		// it from the local player's world with no way to reconcile.
		if (other is Powerup && !((Powerup)other).taken && !IsNetPuppet)
		{
			// AI ships only, like the wall-contact hook above: a human sharing the couch under
			// ?aibench would otherwise open a ShipRec for their slot and add a phantom column --
			// and on slot 0 it is a HUMAN's counters that reach eaAiBench.matrix's table.
			if (EffectiveController() == ControlDevice.AI)
			{
				EvilAliensWeb.Compat.AiBench.NotePickup(this);
			}
			currentPower = ((Powerup)other).type;
			Score.SetPowerup(currentPower, player);
			haspower = true;
			DoSpecial(pickup: true);
			sound.PlayCue("powerup");
			((Powerup)other).taken = true;
			// Online co-op: pickups are generous claims -- note WHO took it so the removal
			// seam can claim it (client) / attribute it (host). No-op without a session.
			EvilAliensWeb.Compat.Net.NetSession.NotePowerupTaken((Powerup)other, player);
			if (this.OnCollectPowerup != null)
			{
				this.OnCollectPowerup(currentPower);
			}
		}
		base.CollidesWith(other);
	}

	// Online co-op (card 83271f3d): the peer broke a tether on its screen, so break the ones this
	// ship holds. Only Linker connectors are ever in this list -- TeamChallenge's scripted tether
	// is built by ShipConnector.Setup, which does not register with either endpoint, and the scene
	// breaks that one itself.
	//
	// A Linker connector is formed INDEPENDENTLY on each peer (both run CollidesWith against their
	// own copy of the pair), and ShipConnector.TakeHit fires EvTetherBreak unconditionally -- so
	// without a break here the peer that did not see the hit stays tethered, and its
	// NetPullOwnShip keeps dragging its ship toward an anchor the other player has already let go
	// of.
	//
	// EVERY connector, NOT just the ones with a puppet endpoint. With couch players a pair of
	// LOCALLY-owned ships can be connected here while the same pair is two puppets on the peer, so
	// a puppet-endpoint filter would break that link when we saw the hit and not when they did --
	// a one-directional break, which is worse than the known over-break below.
	// KNOWN LIMIT: EvTetherBreak carries no connector identity (it is the or-of-either-peer
	// idempotent event TeamChallenge's single tether was designed around), so a peer breaking one
	// of two live connectors breaks both here. Fixing that means putting endpoint slots on the
	// wire.
	internal void NetBreakConnectors()
	{
		// Backwards because NetBreakSilently ends in Die(); removal is queued and this list is
		// only mutated at the ComponentRemoved flush, so nothing can shrink under us today -- the
		// reverse walk is what keeps that true if it ever becomes synchronous.
		for (int i = connectors.Count - 1; i >= 0; i--)
		{
			connectors[i].NetBreakSilently();
		}
	}

	private void Killer_OnDeath(object sender)
	{
		asplosionCauser = null;
	}

	private void queueAsplosion(ICollidable other)
	{
		asplodeOnNextFrame = true;
		asplosionCauser = other;
	}

	private bool isConnectedWith(ICollidable other)
	{
		bool connected = false;
		foreach (ShipConnector connector in connectors)
		{
			connected |= connector.A == other;
			connected |= connector.B == other;
		}
		return connected;
	}

	public void Win()
	{
		hasWon = true;
	}

	public void TemporaryInvulnerability()
	{
		invulnerabilityTimer.Duration = 2500f;
		invulnerabilityTimer.Reset();
		invulnerabilityTimer.Start();
	}

	public void TemporaryInvulnerability(int seconds)
	{
		invulnerabilityTimer.Duration = seconds * 1000;
		invulnerabilityTimer.Reset();
		invulnerabilityTimer.Start();
	}

	private void AsplodeWall()
	{
		// VERBATIM, never `asplosionCauser ?? "Wall"`. CollidesWith calls this directly for a Wall
		// without queueing, so the causer field belongs to some EARLIER death here and reading it
		// would file a wall clip under whatever killed the ship last.
		EvilAliensWeb.Compat.AiBench.NoteDeath(this, "Wall");
		// Game juice: the player's own death is the biggest impact in the game — a real
		// freeze-frame + extra trauma on top of what the two explosions below add.
		EvilAliensWeb.Compat.Juice.AddHitStop(DeathHitStopSeconds);
		EvilAliensWeb.Compat.Juice.AddTrauma(0.35f);
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		float impulse = oracle.BackgroundSpeed.Length();
		float direction = MyMath.VectorToAngle(oracle.BackgroundSpeed);
		explosion.Setup(base.Position, 2f, 2f, impulse, direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 3.5f, 3.5f, impulse, direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	public void Asplode()
	{
		if (!base.IsDead)
		{
			// Only trust the causer when this death was actually QUEUED by a collision. Asplode is
			// also called directly (eaKillShips, a scripted kill), where the field holds whatever
			// last queued one -- reporting that would invent a collision. Deliberately NOT cleared
			// here: the field gates the early-out in Update, so spending it would let a dead ship
			// run the rest of its Update a tick sooner. Initialize clears it per life.
			EvilAliensWeb.Compat.AiBench.NoteDeath(this, asplodeOnNextFrame ? asplosionCauser : null);
			// Game juice: same death punch as AsplodeWall — freeze-frame + extra trauma on
			// top of the two explosions' own shake.
			EvilAliensWeb.Compat.Juice.AddHitStop(DeathHitStopSeconds);
			EvilAliensWeb.Compat.Juice.AddTrauma(0.35f);
			Die();
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2f, 2f, 0f, 0f);
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 3.5f, 3.5f, 0f, 0f);
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
		}
	}

	internal void PowerUp()
	{
		shotspersec = 18;
		bulletlifetime = 1500f;
		shoottimer.Duration = 1000f / (float)shotspersec;
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < 6; j++)
			{
				Option option = Option.NewOption(collection, base.Game);
				option.Setup(this, 0f, i + 1, player);
				collection.Add((GameComponent)(object)option);
				options[i].Add(option);
			}
		}
		RedressOptions();
		Score.MaxExp(Owner);
		PowerUp(Powerup.PowerupType.Blast, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.FirePower, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Linker, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Range, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Option, 4, doEffect: false);
	}

	internal void AddRangePowerups(int p)
	{
		bulletlifetime = MathHelper.Min(70f * (float)p + bulletlifetime, 1500f);
	}

	internal void RemovePowerup()
	{
		haspower = false;
		Score.ResetPowerup(player);
	}

	internal void PowerUp(Powerup.PowerupType type, int newLevel, bool doEffect)
	{
		if (doEffect)
		{
			doPowerupEffect();
		}
		switch (type)
		{
		case Powerup.PowerupType.Option:
		{
			optionLevel = newLevel;
			Option option = Option.NewOption(collection, base.Game);
			option.Setup(this, 0f, 1, player);
			collection.Add((GameComponent)(object)option);
			options[0].Add(option);
			RedressOptions();
			break;
		}
		case Powerup.PowerupType.FirePower:
			switch (newLevel)
			{
			case 1:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 15f);
				asplodingbulletssize = 400f;
				break;
			case 2:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 30f);
				asplodingbulletssize = 400f;
				break;
			case 3:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 60f);
				asplodingbulletssize = 400f;
				break;
			case 4:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 75f);
				asplodingbulletssize = 1400f;
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.Range:
			switch (newLevel)
			{
			case 1:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 50f);
				break;
			case 2:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 2);
				break;
			case 3:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 2);
				bulletsSplit = Math.Max(bulletsSplit, 1);
				break;
			case 4:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 5);
				bulletsSplit = Math.Max(bulletsSplit, 2);
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.Linker:
			switch (newLevel)
			{
			case 1:
				respawntimebonus = Math.Max(2, respawntimebonus);
				break;
			case 2:
				respawntimebonus = Math.Max(4, respawntimebonus);
				break;
			case 3:
				respawntimebonus = Math.Max(7, respawntimebonus);
				break;
			case 4:
				respawntimebonus = Math.Max(14, respawntimebonus);
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.OneUp:
			ServiceHelper.Get<IOracleService>().Oracle.SetSlowmotion(12f);
			Score.RemovePowerup(player);
			break;
		case Powerup.PowerupType.Blast:
			break;
		}
	}

	private bool wantsToTakePowerup(Powerup p)
	{
		if (Score.GetPowerupProgress(player) > 0.6f && p.type != currentPower)
		{
			return false;
		}
		if (readyToConnect && p.type == Powerup.PowerupType.Linker)
		{
			return false;
		}
		if (Score.NrBombs(player) == 3 && p.type == Powerup.PowerupType.Blast)
		{
			return false;
		}
		if (shotspersec == 18 && p.type == Powerup.PowerupType.FirePower)
		{
			return false;
		}
		if (bulletlifetime == 1500f && p.type == Powerup.PowerupType.Range)
		{
			return false;
		}
		return true;
	}
}
