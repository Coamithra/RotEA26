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

	// Low-pass time constant for the AI's steering vector. THE anti-jitter lever: DoAIMove sums
	// a dozen competing terms and Move() consumes only the resulting ANGLE, so when the big
	// terms nearly cancel a tiny residual used to swing the heading right round -- measured at
	// ~1050 deg/s (about three revolutions per second) inside a Level-3 wall. Smoothing the
	// VECTOR (not the angle) is what damps that: two opposing commands blend toward zero and the
	// ship coasts, while a sustained command still converges within a few frames. Rate-limiting
	// the angle instead would force a genuine 180 reversal to sweep the long way round.
	public const float DefaultSteerSmoothMs = 90f;

	// The player's own death is the biggest impact in the game, so it gets a real freeze frame
	// (Compat/Juice.cs) on top of the two explosions' shake. Named rather than literal because
	// NetResetSpawnTest's hit-stop control has to request the SAME duration to be a control at
	// all -- a third unlinked copy of 0.18f would drift silently. Refused outright inside an
	// online co-op session; see Juice.AddHitStop for why.
	public const float DeathHitStopSeconds = 0.18f;

	// Smoothing floor, used when the push is strong (see the adaptive blend in DoAIMove).
	public const float DefaultSteerSmoothUrgentMs = 15f;

	// The demand either side of which smoothing is at full / at the floor.
	private const float SteerCalmDemand = 2f;

	private const float SteerUrgentDemand = 9f;

	// REPULSION CANCELLATION FLOOR (card ada9e839). Repellents are summed on their own, and if
	// that resultant comes out at or below this the ship is not pushed at all. It exists for the
	// case the steering field cannot otherwise express: two threats shoving from opposite sides
	// resolve to a near-zero vector whose DIRECTION is noise, and Move() discards magnitude and
	// thrusts at full acceleration along the angle -- so "barely pushed" reads as "sprint that
	// way" and the ship jitters between two walls instead of holding still between them.
	//
	// 0.2 IS THE 2008 VALUE, restored. The original DoAIMove ended with
	// `if (direction.Length() <= 0.2f) direction = Vector2.Zero;`. This port raised that to 0.95
	// -- above the 0.8 seek -- which turned a noise floor into a VETO that deleted every
	// deliberate destination the bot had (see the seek weights below, and the card). The number
	// is back where it started; what changed is that it now applies to the repulsion sum ALONE
	// rather than to the whole steer, so it can never censor an attractor again.
	public const float DefaultRepulseCancelDelta = 0.2f;

	// WHOLE-SUM equilibrium guard, applied last (see the end of DoAIMove for the placement
	// argument). Same 0.2, same job at a different level: a steer that has cancelled to noise is
	// full throttle in an arbitrary direction, because Move() keeps only the angle. It is BELOW
	// every FIXED-weight attractor by construction -- the weakest is SeekWeight 0.8 and a
	// surviving repellent already beats DefaultRepulseCancelDelta -- so it can only ever fire on
	// real cancellation, never censor a lone vote. That bound is the whole difference from the
	// 0.95 this port shipped, and it is asserted by logic_probe's ProbeAiFieldComposition.
	//
	// THAT HAZARD IS NOW REALISED, DELIBERATELY (card b56633fb). This note used to read "an
	// attractor that FADES with distance (there is none today) would drop under this floor and be
	// parked exactly as the 0.95 park parked everything -- such a term must either keep its
	// magnitude above the floor or accept being inert as a deliberate choice". The boss approach
	// is that term and takes the second option ON PURPOSE: its weight is solved to CROSS the
	// repellent at firing range, so this floor is what widens that crossing into a band the ship
	// can come to rest in. It is therefore out of ProbeAiFieldComposition's weakest-attractor
	// bound; ProbeAiBossApproach asserts the band instead. The warning still stands for any
	// FIXED-weight attractor added later.
	public const float DefaultSteerNoiseFloor = 0.2f;

	private static float SteerSmoothUrgentMs => EvilAliensWeb.Compat.DebugFlags.AiSteerSmoothUrgentMs ?? DefaultSteerSmoothUrgentMs;

	private static float RepulseCancelDelta => EvilAliensWeb.Compat.DebugFlags.AiRepelCancelDelta ?? DefaultRepulseCancelDelta;

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
	// reaches this far" cannot drift apart. It is exactly `Bullet.Initialize`'s Speed, and a
	// bullet dies on its lifetime, so the travel is exact rather than approximate.
	private const float BulletRangePerMs = 0.78f;

	// ---- GUN REACH: A BULLET ONLY HAS TO REACH THE HULL (card bb949dd9) --------------------
	//
	// WHAT WAS WRONG. Both the fire gate and the boss-approach anchor measured range to the
	// target's CENTRE -- the 2008 test, verbatim (src_decompiled DoAIFire, `num2 <= bulletlifetime
	// * 0.78f`), and harmless in 2008 because nothing POSITIONED the ship off it. The port's
	// approach term is anchored on that same test, so the ship parked where its centre distance
	// equalled the bullet's travel and threw away the whole hull: measured on `?level=Level2&
	// marsboss`, `boss=159px` of EDGE distance against a hull whose corner term is 176px, i.e.
	// the bot flew ~124px closer than it could have shot from. On BrainBoss (hull 233->257px
	// against a 351px reach) it is worse still -- r* was 118px.
	//
	// THE RULE, and it is one rule with no per-type code: a shot aimed at the centre strikes the
	// hull after `centreDist - hitRadius`, so the reach is the bullet's travel PLUS the target's
	// own hull radius. Small targets get a small credit and nothing about them changes; a boss
	// gets a big one, which is the whole point.
	//
	// THE CREDIT IS THE INSCRIBED HALF-EXTENT (ThreatRadius), NOT the sqrt(2) corner term
	// (ThreatBodyTerm), and that is the conservative choice ON PURPOSE: the corner term is the
	// hull's radius along the diagonal only, so crediting it would claim reach the bullet does
	// not have when the ship approaches along an axis. It is also what keeps the reach
	// self-limiting against the aim spread -- at Very_Hard's PI/12, a shot at the edge of the
	// cone stops striking a 124px-half MarsBoss hull beyond ~480px of centre distance, and the
	// inscribed credit puts the gate at 475.
	//
	// BOTH CALLERS GO THROUGH THIS, which is the same argument ThreatBodyTerm's own comment
	// makes: two copies would let the gate the bot fires on and the anchor it parks on drift
	// apart, and then the ship stands where it cannot shoot.
	public const float DefaultGunHullCredit = 1f;

	// ?aigunhull= -- the A/B seam on that credit. `0` restores the pre-card centre-distance gate
	// (and with it the pre-card anchor) exactly, which is the negative control. A tuning seam,
	// not a bug reproduction, so it stays OUT of DebugFlags.Active -- the ?aisweptmax= precedent.
	private static float GunHullCredit => EvilAliensWeb.Compat.DebugFlags.AiGunHullCredit ?? DefaultGunHullCredit;

	// Max CENTRE distance a shot aimed at the target's centre can be fired from and still strike
	// its hull. PURE -- primitives in, distance out -- so logic_probe sweeps it over the whole
	// bulletlifetime range and every hull with no game running, rather than restating it.
	public static float AiGunReachPx(float bulletLifetime, float hitRadius)
	{
		return bulletLifetime * BulletRangePerMs + MathHelper.Max(hitRadius, 0f) * GunHullCredit;
	}

	// ---- SPARING THE SPIDER BOSS'S EXECUTIONERS (card 2c74d5b7) ------------------------------
	//
	// Only a Lazer hurts the SpiderBoss (SpiderBoss.CollidesWith), and a BIG UFO fires one six
	// times as often as a small one (UFO.Update: 0.0009/ms against 0.00015). The boss's whole HP
	// pool is `5 * DifficultyFactorized(0.75)`, so the fight's LENGTH is very nearly "how many big
	// UFOs got to fire" -- every one the bot clears is a gun it took away from itself, and killing
	// one mid-windup (the 2500ms UFOState.lazor charge) deletes the beam outright.
	//
	// DoAIFire already spares exactly ONE, the one furthest from every ship. The card asked for
	// "some" -- i.e. more than one -- and proposed a reduced engage radius to get there. That
	// mechanism is BUILT and reachable (`?aibigufopx=<px>`), and it **BAKES TO 0, i.e. OFF**,
	// because across its whole usable band it measured as a net loss. Read the next two bullets
	// before re-enabling it.
	//
	// **THE BAND IS BOUNDED ABOVE BY THE BOT'S REACH, which is the durable answer to the card's
	// question.** It never fires past AiGunReachPx -- `bulletlifetime * BulletRangePerMs` = 351px
	// at the base weapon PLUS the target's hull credit (card bb949dd9) -- so a radius at or above
	// that is behaviourally inert. Measured ~400px for a big UFO: runs at 400 / 420 / 450 produce
	// an identical world. "Reduce the radius" therefore has ~100..400px to work in, not an open
	// range.
	//
	// **AND EVERY VALUE IN THAT BAND LOSES.** eahl, Very_Hard, `?invuln` off, seeds 1-8 x2 (N=16,
	// small -- these are directional, and the sign was consistent across both variants):
	//
	//   arm                       deaths (paired)   victories   win@    Lazer kills   mean alive
	//   off (=0)                  3.81              6/16        158s    49            1.32
	//   capped at 250             +0.94 +- 1.09     7/16        219s    68            1.36
	//   capped at 300             +1.19 +- 1.23     4/16        112s    68            1.40
	//   UNCAPPED at 250           +1.50 +- 0.89     6/16        241s    72            1.46
	//
	// The counter moves exactly as designed and the fight does not follow. The mechanism is that
	// the beams the bot is inviting are aimed AT IT and it was already dying to them (`Lazer` is
	// ~90% of deaths on this rig), so each extra platform costs more dodging than it buys boss
	// damage. Neither deaths diff clears 2 SEM, so this is "not better", not "significantly
	// worse" -- but nothing here is an improvement to ship.
	//
	// WHY A RADIUS AND NOT "SPARE N". Distance is what makes sparing survivable at all: the beam
	// is aimed AT THE PLAYER at fire time and stays put for >=3250ms, so a far platform's beam
	// crosses most of the screen for the boss to walk into while a near one's is a point-blank
	// shot the bot has no room to leave. A count would spare the nearest as readily as the far.
	//
	// IF YOU RE-OPEN THIS, the untried directions are the ones that change WHERE the bot stands
	// rather than how many guns it leaves alive -- the beam-avoidance magnitudes (card 2248e5eb
	// re-measured those and they are not obviously final on this rig) or a positional term that
	// keeps the ship off the invited beam's line. Raising the radius is not one of them.
	public const float DefaultBigUfoEngagePx = 0f;

	private static float BigUfoEngagePx => EvilAliensWeb.Compat.DebugFlags.AiBigUfoEngagePx ?? DefaultBigUfoEngagePx;

	// How many big UFOs the radius gate may leave alive at once, the spare-one rule INCLUDED --
	// so with `?aibigufopx=` on, at most this many. Deliberately NOT a flag: `?aibigufopx=` is
	// the one seam here, and a second knob would let a sweep wander back to the uncapped
	// configuration the table above records as the worst arm of the four. It is a name for the
	// two slots (`sparedUfo` + `sparedFarUfo`) in DoAIFire rather than a loop bound -- at 2 an
	// explicit pair is cheaper and clearer than a top-N scan, so raising it means writing one.
	public const int BigUfoSpareCap = 2;

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
	// pingpongs. Width is 0.4 / (|A'| + |repel'|). The worked example this used to carry (r* =
	// 181.3px edge, band 31px) is CARD bb949dd9-SUPERSEDED: r* now credits the boss's hull, so
	// the same MarsBoss-sized hull solves to r* ~ 301px edge with a much shallower repellent
	// there, and the band is correspondingly wider. Read the bound off
	// logic_probe's ProbeAiBossApproach, which sweeps every tier x the whole bulletlifetime range
	// x every hull and is where the bound actually lives -- not off a number in this comment.
	// SIDE EFFECT WORTH KNOWING: at the new r* the boss's own repellent has often decayed BELOW
	// the whole-sum floor, so the equilibrium is the floored-attractor case this shape was
	// already built for (the Range-powerup branch below), not a solved crossing. The ship then
	// parks somewhat inside r* rather than on it -- still far outside where it used to stand,
	// and `bossfar` remains the honest readout of whether it can shoot from there.
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

	// Anchor floor. r* is derived (gun reach minus the boss's own body term), so a boss whose hull
	// is bigger than the weapon's reach would drive it to zero or below -- and a tiny r* is what
	// makes |A'| = w/r* explode and collapses the band. Floored at 3x the 11.3px stopping distance:
	// below that the ship cannot hold a standoff there anyway, and the band bound is verified AT
	// the floor rather than assumed away.
	// CARD bb949dd9 PUSHED EVERY REAL HULL WELL CLEAR OF IT and the floor is now unreached in
	// practice -- the reach credits the hull radius, so r* is `travel - (sqrt(2)-1)*halfExtent`
	// and the widest boss in the game (BrainBoss at its pulse peak, halfExtent 182) still solves
	// to ~276px. Keep it: `?aifieldpx=`/`?aigunhull=` and a hypothetical wider boss can still
	// drive r* down, and the probe verifies the band bound AT the floor.
	// THE PARAGRAPH THAT USED TO SIT HERE IS OBSOLETE, and it is worth knowing why rather than
	// just deleting it: raising this floor to ~115px was rejected because "asking the ship to
	// stand 115px clear of a hull that wide parks it OUTSIDE gun range". That was true only
	// while gun range was measured to the boss's CENTRE; it is not the trade any more.
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

	// 20 -> 12 (card 13960838). `maxSteerStrength` is 4, and 4 is ALSO the ceiling of the powerup
	// approach pull, so at 20 the band out-voted the strongest pull possible for the top
	// `170 * (1 - 4.8/20)` = 129px of the screen -- a strip where a pickup was not unlikely but
	// arithmetically unreachable, which is the card's complaint. At 12 that strip is 102px, i.e.
	// NARROWED, not removed: a band that could never out-vote a powerup would not deter a ship
	// from the spawn line either, which is what card 2248e5eb measured this term as being for.
	//
	// MEASURED, and read the direction of the evidence rather than the number: N=16 paired by seed
	// (seeds 1-8 x2), pickups 45.4% -> 55.7% spider and 52.4% -> 57.4% brainboss, deaths
	// -1.00 +- 0.84 and -0.19 +- 0.66. **12 rather than something lower is deliberate.**
	// 2248e5eb's cost for dropping the band outright was +0.67..+0.88 deaths, which is INVISIBLE
	// at this sweep's ~0.8 SEM -- so a value that merely looks flat on deaths here is not evidence
	// of being safe, and the conservative pick is the highest strength that recovers most of the
	// pickup gain. The N=60 gate is the arbiter. `?aitopedgestrength=0` remains the 2008 arm.
	public const float DefaultTopEdgeAvoidStrength = 12f;

	private static float TopEdgeAvoidStrength => EvilAliensWeb.Compat.DebugFlags.AiTopEdgeAvoidStrength ?? DefaultTopEdgeAvoidStrength;

	// WHERE the band's push is applied (card 13960838). True = into `repel`, with every other
	// repellent: upstream of the cancellation floor and of the steering low-pass. False =
	// `?aitopedgecompose=0`, the pre-card placement, straight into `direction` AFTER the low-pass.
	//
	// The old placement is why the card was filed. `maxSteerStrength` is ALSO the ceiling of the
	// powerup pull a few hundred lines up, so at the pre-card strength of 20 the two were not
	// competing forces at all, they were a force and a rounding error, and no attractor in the
	// method could survive the top of the screen. Bypassing the low-pass made it worse than the
	// ratio suggests: every other vote is smoothed toward its sustained value while this one lands
	// whole on the frame it is computed.
	//
	// PLACEMENT AND MAGNITUDE ARE SEPARATE CHANGES AND ONLY THE SECOND MOVED THE PICKUP RATE.
	// This one buys the ability to argue with the band at all -- under the old placement no
	// strength above 4 could be out-voted, so retuning it would have been meaningless. What the
	// measurement then showed is that the rate tracks the strength (see DefaultTopEdgeAvoidStrength
	// for the table and the 20 -> 12 that came with it), while composing it is what carries the
	// death improvement. Do not collapse the two into one claim.
	private static bool TopEdgeComposes => EvilAliensWeb.Compat.DebugFlags.AiTopEdgeCompose;

	// One latch PER SITE for the `[aitopedge]` line, keyed on the placement name. Static, so each
	// is per PROCESS rather than per ship -- four co-op ships would otherwise print four times and
	// a pool recycle more still.
	//
	// TWO LATCHES, NOT ONE, and the difference is a real defect class: a single shared latch means
	// whichever site fires first silences the other, so a build applying the band at BOTH sites --
	// double strength on an ordinary boot -- prints exactly the line a healthy build prints. That
	// mutation passed the probe pair while the latch was shared (measured). Per-site, such a build
	// prints both lines and the pair's `expect-not` catches it.
	private static readonly HashSet<string> topEdgeReported = new HashSet<string>();

	// Which placement actually RAN. The `[debug] flags active` dump reports only the PARSE, so an
	// A/B arm whose flag parsed but whose branch did not fire measures the shipped code twice and
	// prints an entirely plausible table -- the `?aiwallnav2008` lesson, and the reason that flag
	// prints its own line too.
	//
	// **CALLED FROM INSIDE EACH BRANCH, and passed the branch's OWN name -- never handed
	// `TopEdgeComposes` to re-derive.** A version reading the predicate here was written first and
	// is exactly as useless as the flags dump: inverting the dispatch left it printing "composed"
	// while the push went the other way, and the probe pair passed on the mutation it exists to
	// catch (measured). The report has to be evidence of the branch, not a second reading of its
	// condition.
	private static void ReportTopEdgePlacement(string placement)
	{
		if (topEdgeReported.Add(placement))
		{
			Console.WriteLine("[aitopedge] placement: " + placement + " band=" + TopEdgeDangerPx
				+ "px strength=" + TopEdgeAvoidStrength);
		}
	}

	// The band's magnitude at a design-space Y, as a pure function of its two parameters, so
	// logic_probe can sweep the profile and DERIVE the crossover against the powerup pull instead
	// of having a number restated beside it (card 13960838; the 05a2b818 style). DoAIMove calls
	// this rather than repeating the arithmetic -- a second copy is how the probe comes to be
	// pinning a formula the game does not use.
	//
	// LINEAR, not the `(1-t)^p` spike every threat field uses: this band is a PRIOR about where
	// UFOs are going to appear rather than a field around something that already exists, so it has
	// to be pushing well before the ship gets there. That shape is card 2248e5eb's and is untouched.
	//
	// `dangerPx > 0` is the guarded divisor -- `?aitopedgepx=0` passes that flag's own `>= 0` range
	// check, so a zero depth is reachable, and relying on the position clamp to keep Y positive
	// would be leaning on an invariant three hundred lines away.
	public static float TopEdgeAvoidMagnitude(float y, float dangerPx, float strength)
	{
		if (dangerPx <= 0f || y >= dangerPx)
		{
			return 0f;
		}
		return strength * (1f - y / dangerPx);
	}

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
	// 15 IS AN AUDIT RESULT AND THE 30 IT REPLACES IS REFUTED (card 05a2b818). Re-measured clean
	// at N=60, the response is MONOTONE in this radius on CrazyGame and flat on the other four
	// rigs -- paired against 30: 10px -3.60 deaths, 15px -2.87, 20px -1.97 (victories 36 -> 50 /
	// 48 / 44 of 60). **10 measured BEST and was rejected anyway**, on the bound rather than on
	// the number: it sits BELOW the 11.3px stopping distance, so the ship cannot come to rest
	// inside it and the radius stops being a deadzone at all -- which is the idle fidget the port
	// widened it for, and the invariant ProbeAiFieldComposition pins. 15 is the smallest value
	// that keeps the bound intact, and it takes ~80% of the measured win.
	// Pinned against the motion constants by logic_probe's ProbeAiFieldComposition.
	public const float DefaultSeekArriveDeadzonePx = 15f;

	private static float SeekArriveDeadzonePx => EvilAliensWeb.Compat.DebugFlags.AiSeekDeadzonePx ?? DefaultSeekArriveDeadzonePx;

	// WHICH deliberate destination won a tick (card fd126847). Diagnostics only -- the steer the AI
	// bench reports is POST-low-pass, so from outside the ship the raw destination is unreadable and
	// an oscillation cannot be attributed to a term. `?aiseeklog` / `eaAiSeek()` print it.
	// It is ALSO what scopes the predictive arrive gate below to the STATION: every other writer of
	// `steerTarget` has its own arrival semantics (a powerup ceases to exist on contact, the boss
	// approach parks in a solved band, a dock partner and a blastable cluster are both moving), so
	// they keep the plain position gate.
	internal enum AiSeekKind
	{
		None,
		Station,
		Powerup,
		Boss,
		Dock,
		Blast,
		JunkBoss
	}

	// THE PREDICTIVE ARRIVE GATE (card fd126847). Answers "should the seek still be pulling", where
	// the pre-card answer was the bare `distance > deadzone`.
	//
	// WHY THAT WAS NOT ENOUGH, measured. The deadzone is sized against the ship's 11.3px COASTING
	// stopping distance, and the stability argument at DefaultSeekArriveDeadzonePx assumes the ship
	// is coasting the moment it crosses the boundary. It is not: the steer is low-passed
	// (DefaultSteerSmoothMs 90), so when the pull switches off `aiSteer` decays exponentially and
	// Move() keeps thrusting at FULL acceleration down that decaying vector until it falls under
	// SteerNoiseFloor -- ~6 more ticks, during which the ship is still speeding up. The real stopping
	// distance from the boundary is therefore well over the deadzone radius, the ship leaves the far
	// side, the gate re-arms at full strength, and it slams back. Measured on `?level=Level3&
	// brainboss`: a 38px, 20-tick limit cycle, 227px of path per second for 3px of net travel.
	//
	// THE FIX IS A SWITCH, NEVER A NEW FORCE. It predicts where the ship would come to rest if the
	// pull stopped NOW (the low-pass tail, then the coast) and stops pulling once that rest point is
	// inside the deadzone. Two properties make it safe here:
	//   * it is a STRICT SUPERSET of the pre-card off-cases -- `d <= deadzone` still returns false,
	//     and at rest the prediction collapses to the ship's own position, so a stationary ship gets
	//     exactly the old answer. It can only ever switch the seek OFF, never on, and it adds no
	//     vector to the sum;
	//   * it therefore cannot brake a real manoeuvre, which is what got the velocity-damped ARRIVE
	//     (a `-SpeedVector` term in the steer) reverted -- see the seek block in DoAIMove.
	// `?aiseekarrive=0` restores the pre-card gate; the caller owns that flag, so this stays pure.
	//
	// `smoothedMagnitude` is |aiSteer| as it stands BEFORE this tick's blend, i.e. what the tail
	// would decay from. Using the live value rather than the SeekWeight worst case matters: the worst
	// case over-predicts by ~30px for a ship that has barely started moving, which would cut the pull
	// far out and leave the bot creeping to its station in hops.
	internal static bool SeekArriveEngaged(Vector2 toTarget, Vector2 velocity, float deadzonePx,
		float smoothedMagnitude, float smoothMs, float noiseFloor)
	{
		float dist = (toTarget).Length();
		if (dist <= deadzonePx)
		{
			return false;
		}
		float speed = (velocity).Length();
		if (speed <= 0f)
		{
			return true;
		}
		// How long the low-pass keeps a command alive after the pull stops: |aiSteer| decays as
		// exp(-t/smoothMs) and the whole steer is zeroed once it reaches the noise floor. Thrust is
		// all-or-nothing (Move() keeps only the angle), so every one of those ms is full throttle.
		float tailMs = 0f;
		if (smoothMs > 0f && noiseFloor > 0f && smoothedMagnitude > noiseFloor)
		{
			tailMs = smoothMs * (float)Math.Log(smoothedMagnitude / noiseFloor);
		}
		// Net acceleration while thrusting is ShipAcceleration: Move() adds (accel + decel) along the
		// command and subtracts decel along the current heading, and the tail points the way the ship
		// is already going.
		float tailPx = 0f;
		float endSpeed = speed;
		if (tailMs > 0f)
		{
			float toMaxMs = (ShipAcceleration > 0f) ? ((ShipMaxSpeed - speed) / ShipAcceleration) : 0f;
			float accelMs = MathHelper.Clamp(toMaxMs, 0f, tailMs);
			tailPx = speed * accelMs + 0.5f * ShipAcceleration * accelMs * accelMs;
			endSpeed = speed + ShipAcceleration * accelMs;
			tailPx += endSpeed * (tailMs - accelMs);
		}
		float coastPx = endSpeed * endSpeed / (2f * ShipDeceleration);
		Vector2 restPoint = velocity / speed * (tailPx + coastPx);
		return (restPoint - toTarget).Length() > deadzonePx;
	}

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
	public const float DefaultAsteroidThreatScale = 1f;

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
	public const float DefaultAsteroidRangeScale = 1f;

	public const float DefaultAsteroidFalloff = DefaultThreatFieldFalloff;

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

	// Cone LENGTH per unit speed, as a time horizon: length = speed * this, so it scales with
	// speed by construction. Deliberately the SAME 700ms as DefaultThreatLeadMs rather than a new
	// number -- that is the already-swept "how far ahead is a moving threat worth reacting to"
	// horizon (card 21bb6849 measured a broad optimum around it on CrazyGame), and this is the
	// same question asked by a shape instead of by a special case. At an asteroid's 0.38px/ms it
	// is 266px of closed lane, against a ship that covers 231px in the same time -- i.e. the
	// warning arrives while the escape is still affordable.
	public const float DefaultConeLeadMs = DefaultThreatLeadMs;

	// Ceiling on that length. 800 is the design field's own width, past which the shape is off
	// screen and cannot describe anything; it exists so a very fast mover (a bullet) does not
	// project a cone longer than the world.
	public const float DefaultConeMaxLenPx = 800f;

	// How far OUTSIDE the swept corridor the cone still pushes -- the scale of the ACROSS-axis
	// falloff. THE ONE VALUE HERE THAT IS NOT DERIVED FROM ANYTHING, so it was swept rather than
	// picked, and then swept again on a second rig when the first answer proved rig-specific.
	//
	// SPACEDODGE, deaths / victories, paired seeds x2:
	//     75px  21.50 (2/4)  |  150px  8.56 (12/16)  |  300px  3.44 (16/16)  |  450px  6.00 (4/4)
	// An INTERIOR optimum -- 450 is worse than 300 -- so this is a width that fits the belt, not a
	// "wider is better" gradient left half-walked.
	//
	// THE MAGNITUDE AXIS WAS SWEPT ALONGSIDE IT AND DECLINED AT EQUAL OUTCOME: `?aiconescale=2.5`
	// also reaches 16/16, at 3.62 deaths against this shape's 3.44. Same call cards ada9e839 and
	// e88e21ca both made -- a taller mountain is whack-a-mole across levels, ejecting the ship out
	// of one hazard into the traffic around it, while transverse reach is a change of SHAPE.
	//
	// AND THE OBVIOUS GENERALISATION WAS TRIED AND IS WORSE, which is worth knowing before anyone
	// reaches for it again. CrazyGame wants the OPPOSITE width (deaths 1.00 at 60px, 2.25 at 150,
	// 8.50 at 300): it fields 30 simultaneous ~5px-half bullets, and a 300px skirt on each buries
	// the ship in transverse pushes that mostly cancel. The natural fix is the one the radial
	// field already made -- scale the reach with the mover's hull, as ThreatFieldRange does. So
	// that was built and measured: `halfExtent * 6.4` (which reproduces 300px at an asteroid's
	// ~47px half-extent) drops SpaceDodge to 12/16 at 10.12 deaths. Asteroid half-extents VARY,
	// and the small rocks -- the ones the belt is mostly made of -- lose the wide skirt that is
	// doing the work. A flat number is not elegant here; it is what measures better.
	// The rig disagreement is real and unresolved; `?aiconewidth=` is how the next attempt reaches
	// it, and the CrazyGame cost is stated in the card rather than hidden.
	public const float DefaultConeWidthPx = 300f;

	// Optional SIZE SCALING of that reach: 0 = off (the flat width above), otherwise the reach is
	// halfExtent * this, floored at DefaultConeWidthMinPx and capped at the flat width.
	//
	// BAKED INERT, AND THE SWEEP THAT SETTLED IT IS THE INTERESTING PART. The flat width is right
	// for SpaceDodge and wrong for CrazyGame (see DefaultConeWidthPx), so the obvious move is to
	// scale the reach with the mover -- as ThreatFieldRange already does -- with a FLOOR so a swarm
	// of small fast objects keeps a usable skirt. Swept k x floor, paired seeds 1-4 x2 (8 runs),
	// deaths, with victories noted only where not 8/8:
	//     cell          | SpaceDodge      | CrazyGame
	//     flat (shipped)|  4.25           |  8.50
	//     k4.5 / 60px   |  5.25 (6/8)     |  1.00
	//     k4.5 / 120px  |  8.50 (6/8)     |  3.75
	//     k6.4 / 60px   |  7.62           |  1.00
	//     k6.4 / 120px  | 10.00 (7/8)     |  3.75
	//     k8   / 60px   |  8.00           |  1.00
	//     k8   / 120px  | 14.62 (4/8)     |  3.75
	// So scaling really does fix CrazyGame (8.50 -> 1.00, better than no cone at all) and the floor
	// is the axis that matters. On the FULL SpaceDodge gate (seeds 1-8 x2) k6.4/60 reads 14/16,
	// coincidentally also at 7.62 deaths, against the flat width's 16/16 at 3.25. It then FAILED
	// the third gate outright: SpiderBoss
	// (standing) deaths over the same 8 runs read 12 on shipped main, 22 flat and **34** scaled.
	// That is the mechanism below, amplified -- a standing boss sweeps nothing and so projects no
	// cone at all, and the wider UFO skirt shoves the ship into it harder. No cell cleared, so the
	// flat width ships and this stays a seam.
	public const float DefaultConeSpread = 0f;

	// The floor that scaling clamps to, so a swarm of small fast movers keeps a usable skirt
	// instead of each projecting a corridor narrower than the ship.
	public const float DefaultConeWidthMinPx = 120f;

	// How the corridor narrows toward the far end: 1 is the true triangle of the design sketch
	// (a point at full length), 0 a parallel capsule.
	public const float DefaultConeTaper = 1f;

	// THE TWO FALLOFFS ARE DIFFERENT FAMILIES ON PURPOSE, and this is the crux of the shape.
	//   ALONG the axis: `1 - t^p`, a PLATEAU (p=2 keeps 75% at half the cone's length). The whole
	//     point is to have authority far out along the trajectory, which is the band the radial
	//     field abandons; a spike here would reproduce exactly the field this replaces.
	//   ACROSS the axis: `(1-t)^p`, a SPIKE (p=3 is down to 12% at half the width). Threading a
	//     gap between two rocks has to stay possible, so sideways clearance must get cheap fast.
	// Note the along-axis family is the 2008 `MyMath.PowerCurve` one, which card e88e21ca measured
	// and rejected -- but rejected as a RADIAL curve, where a plateau merely widens a circle. On a
	// trajectory axis it is the whole idea, so that result does not carry.
	public const float DefaultConeFallAlong = 2f;

	public const float DefaultConeFallAcross = DefaultThreatFieldFalloff;

	// Peak magnitude as a multiple of maxSteerStrength: 1.0 makes the corridor ahead exactly as
	// repellent as the hull itself, which is the honest statement -- being there when it arrives
	// and being inside it now are the same death.
	public const float DefaultConeScale = 1f;

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
	public const float DefaultLaneWedgeFallAlong = DefaultConeFallAlong;

	private static bool ConeEnabled => EvilAliensWeb.Compat.DebugFlags.AiConeShapes ?? true;

	private static bool LaneWedgeEnabled => EvilAliensWeb.Compat.DebugFlags.AiLaneWedge ?? true;

	private static float ConeLeadMs => EvilAliensWeb.Compat.DebugFlags.AiConeLeadMs ?? DefaultConeLeadMs;

	private static float ConeMaxLenPx => EvilAliensWeb.Compat.DebugFlags.AiConeMaxLenPx ?? DefaultConeMaxLenPx;

	private static float ConeWidthPx => EvilAliensWeb.Compat.DebugFlags.AiConeWidthPx ?? DefaultConeWidthPx;

	private static float ConeSpread => EvilAliensWeb.Compat.DebugFlags.AiConeSpread ?? DefaultConeSpread;

	private static float ConeWidthMinPx => EvilAliensWeb.Compat.DebugFlags.AiConeWidthMinPx ?? DefaultConeWidthMinPx;

	private static float ConeTaper => EvilAliensWeb.Compat.DebugFlags.AiConeTaper ?? DefaultConeTaper;

	private static float ConeFallAlong => EvilAliensWeb.Compat.DebugFlags.AiConeFallAlong ?? DefaultConeFallAlong;

	private static float ConeFallAcross => EvilAliensWeb.Compat.DebugFlags.AiConeFallAcross ?? DefaultConeFallAcross;

	private static float ConeScale => EvilAliensWeb.Compat.DebugFlags.AiConeScale ?? DefaultConeScale;

	private static float LaneWedgeStrength => EvilAliensWeb.Compat.DebugFlags.AiLaneWedgeStrength ?? DefaultLaneWedgeStrength;

	private static float LaneWedgeFallAlong => EvilAliensWeb.Compat.DebugFlags.AiLaneWedgeFallAlong ?? DefaultLaneWedgeFallAlong;

	private static float ThreatFieldBasePx => EvilAliensWeb.Compat.DebugFlags.AiThreatFieldPx ?? Skill.FieldPx;

	private static float ThreatFieldSizeScale => EvilAliensWeb.Compat.DebugFlags.AiThreatFieldSize ?? DefaultThreatFieldSizeScale;

	private static float ThreatFieldFalloff => EvilAliensWeb.Compat.DebugFlags.AiThreatFieldFalloff ?? DefaultThreatFieldFalloff;

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
	private static readonly AiSkill[] AiSkillByDifficulty = new AiSkill[5]
	{
		/* Easy      */ AiSkill.Deg(118f, 22.5f),
		/* Medium    */ AiSkill.Deg(129f, 19.5f),
		/* Hard      */ AiSkill.Deg(139f, 17f),
		/* Very_Hard */ new AiSkill(VeryHardThreatFieldBasePx, VeryHardAimSpreadRad),
		/* Inzane    */ AiSkill.Deg(VeryHardThreatFieldBasePx, 11.25f)
	};

	// EFFECTIVE difficulty, not CurrentDifficulty: the attract demos lock Hard and the tutorial
	// locks Very_Hard, and only the lock-aware value describes the fight the bot is actually
	// flying in. See Settings.EffectiveDifficulty.
	//
	// Memoised on the tier because this is read per THREAT per ship per frame (ThreatFieldRange is
	// called inside DoAIMove's baddy loop), which with a full field and four AI friends is
	// thousands of resolutions a frame -- the same hoist the "Perf batch 2" note above DoAIMove
	// applies to GetBaddies(). Single-threaded WASM, so a plain non-volatile pair is safe.
	//
	// The clamp is NOT for the save file: XmlSerializer writes enums by NAME and throws on an
	// unknown one (which lands in Settings.onLoadError and yields a fresh Settings), and
	// ?difficulty= is gated by Enum.IsDefined. It guards the real hazard -- a future
	// DifficultyLevel member added without a matching row here, which it maps to the last row.
	private static Settings.DifficultyLevel skillTier = (Settings.DifficultyLevel)(-1);

	private static AiSkill skillCached;

	private static AiSkill Skill
	{
		get
		{
			Settings.DifficultyLevel tier = Settings.GetInstance().EffectiveDifficulty;
			if (tier != skillTier)
			{
				skillTier = tier;
				skillCached = AiSkillByDifficulty[MathHelper.Clamp((int)tier, 0, AiSkillByDifficulty.Length - 1)];
			}
			return skillCached;
		}
	}

	// For the ?aibench readout. The RESOLVED values (overrides applied), so the bench line answers
	// "which skill row am I actually flying?" directly instead of leaving it to be inferred from
	// noisy outcome counters -- the tier lookup is the whole mechanism of card c10e3e7f, and every
	// end-to-end metric that could confirm it is confounded by the ENEMIES scaling with the same
	// tier. This is the only non-confounded observation of it.
	internal static void GetAiSkillReadout(out float fieldPx, out float aimRad)
	{
		fieldPx = ThreatFieldBasePx;
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

	// Last tick's seek decision, for `?aiseeklog` / `eaAiSeek()` (card fd126847). Recorded
	// unconditionally -- it is four field writes, and a console dump that only worked on a run booted
	// with the flag would be useless exactly when a live oscillation is in front of you. Only the
	// PRINTING is gated.
	private AiSeekKind aiSeekKind;

	private Vector2 aiSeekTarget;

	private float aiSeekDist;

	private bool aiSeekEngaged;

	private bool aiSeekPredictive;

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
	// `internal` rather than private since card fd126847: DebugInput.AiSeek needs it to pick the
	// bot-driven ships out of the component list, and under `?aiplayer` the seated device is still
	// Keyboard -- so the field alone would report the wrong set.
	internal ControlDevice EffectiveController()
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

	// WHICH big UFOs the AI deliberately leaves alive during a SpiderBoss fight, and the boss
	// facts the caller needs from the same scan (cards f4d1721f / 2c74d5b7).
	//
	// The SpiderBoss fight is won with the ENEMY's guns: only a Lazer can hurt the boss, and a big
	// UFO fires one AT THE PLAYER, so the boss walks into any beam that crosses the screen. Killing
	// every big UFO leaves nothing but the helper mothership's slow cycle. So:
	//   * ONE is always spared -- the one with the most ROOM, scored by its distance to the NEAREST
	//     ship so that in co-op it is far from everybody. Keeping the beam platform at arm's length
	//     is what makes this survivable: its beam still crosses the screen for the boss to walk
	//     into, but the AI is not standing next to the thing that is aiming at it. This only pays
	//     off together with the beam dodging in DoAIMove; sparing without it measured 24 -> ~70
	//     deaths.
	//   * ONE MORE is spared if it is further than `engagePx` from every ship -- the radius gate,
	//     capped at BigUfoSpareCap in total. `engagePx <= 0` is the gate OFF, i.e. the pre-card
	//     build exactly. See DefaultBigUfoEngagePx for why that is what ships.
	//   * NEITHER is spared during a fly-by. Dodging a screen-wide sweep and a big UFO's beam at
	//     the same time is how the bot dies, and it is worst in the upper lane where the UFOs live.
	//     The boss spends most of the fight grounded, which is plenty of time to feed it beams.
	//
	// SEPARATE FROM DoAIFire, AND STATIC, so the decision can be read as DATA at one instant over
	// one world (`eaBigUfoSpare()`), which is the only honest way to A/B the gate: the population
	// evolves, so running one arm and then the other inside a single fight compares two different
	// screens and can invert the true result (measured -- it did, during this card). It takes
	// `engagePx` as a PARAMETER rather than reading the flag for the same reason.
	// `oracle` is instance state, so this is static-in-spirit rather than static.
	private void SelectSparedBigUfos(List<AlienDrawableGameComponent> baddies, float engagePx,
		out UFO sparedUfo, out UFO sparedFarUfo, out int bigUfosAlive,
		out bool spiderBossAlive, out bool bossSweeping)
	{
		spiderBossAlive = false;
		bossSweeping = false;
		sparedUfo = null;
		sparedFarUfo = null;
		bigUfosAlive = 0;
		float sparedRoom = -1f;
		// The radius gate's slot is the SECOND-most-roomy big UFO, because the most-roomy one is
		// already `sparedUfo` -- "spare the furthest one beyond the radius" and the spare-one rule
		// would otherwise name the same UFO and the gate would do nothing at all.
		float sparedFarRoom = -1f;
		foreach (AlienDrawableGameComponent scan in baddies)
		{
			if (scan is SpiderBoss && !scan.IsDead)
			{
				spiderBossAlive = true;
				bossSweeping |= ((SpiderBoss)scan).AiSweepIncoming;
			}
			else if (scan is UFO && ((UFO)scan).IsBig && !scan.IsDead)
			{
				bigUfosAlive++;
				float room = float.MaxValue;
				foreach (PlayerShip ship in oracle.GetShips())
				{
					Vector2 toShip = scan.Position - ship.Position;
					room = MathHelper.Min(room, (toShip).Length());
				}
				// Top TWO by room, most first. The `>` (not `>=`) on the first slot preserves the
				// pre-card tie-break exactly -- first seen wins -- so gate-off is the OLD build
				// and not merely a similar one.
				if (room > sparedRoom)
				{
					sparedFarRoom = sparedRoom;
					sparedFarUfo = sparedUfo;
					sparedRoom = room;
					sparedUfo = (UFO)scan;
				}
				else if (room > sparedFarRoom)
				{
					sparedFarRoom = room;
					sparedFarUfo = (UFO)scan;
				}
			}
		}
		if (!spiderBossAlive || bossSweeping)
		{
			sparedUfo = null;
			sparedFarUfo = null;
		}
		else if (engagePx <= 0f || sparedFarRoom <= engagePx)
		{
			// The `engagePx <= 0f` half is tested EXPLICITLY rather than left to fall out of the
			// comparison: `room` is a length, so `room > 0` holds for every UFO and a bare
			// `sparedFarRoom > engagePx` would spare one at every radius INCLUDING zero -- the
			// exact opposite of the pre-card build a zero is supposed to restore.
			sparedFarUfo = null;
		}
	}

	// eaBigUfoSpare() -- the decision above, evaluated over the LIVE world at two radii at once.
	// Same-instant, same-screen, so it says what the rule does rather than what a 45-second slice
	// of a stochastic fight happened to contain.
	internal string AiBigUfoSpareReadout(float engagePx)
	{
		List<AlienDrawableGameComponent> baddies = oracle.GetBaddies();
		SelectSparedBigUfos(baddies, engagePx, out UFO on1, out UFO on2, out int alive,
			out bool bossAlive, out bool sweeping);
		SelectSparedBigUfos(baddies, 0f, out UFO off1, out UFO off2, out int _, out bool _, out bool _);
		int sparedOn = (on1 != null ? 1 : 0) + (on2 != null ? 1 : 0);
		int sparedOff = (off1 != null ? 1 : 0) + (off2 != null ? 1 : 0);
		return "[ai] bigufo alive=" + alive + " boss=" + (bossAlive ? "alive" : "none")
			+ " sweeping=" + (sweeping ? "yes" : "no")
			+ " engage=" + engagePx.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "px"
			+ " spared=" + sparedOn + " sparedAtZero=" + sparedOff + " cap=" + BigUfoSpareCap;
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
		SelectSparedBigUfos(baddies, BigUfoEngagePx, out UFO sparedUfo, out UFO sparedFarUfo,
			out int bigUfosAlive, out bool spiderBossAlive, out bool _);
		// The bench's own view of this (card 2c74d5b7). No-op unless ?aibench.
		EvilAliensWeb.Compat.AiBench.NoteBigUfos(spiderBossAlive, bigUfosAlive,
			(sparedUfo != null ? 1 : 0) + (sparedFarUfo != null ? 1 : 0));
		float nearestDist = float.MaxValue;
		AlienDrawableGameComponent nearest = null;
		// The priority bias decides WHICH target wins, but a discounted boss can win from well
		// outside gun range (at bias 0.45 a boss 780px away outranks a UFO at 350px). Without a
		// fallback the AI then fires at nothing at all while a killable target sits in range --
		// inflating the very idle% the bias exists to reduce. So track the nearest genuinely
		// reachable target alongside it.
		float nearestInRangeSq = float.MaxValue;
		AlienDrawableGameComponent inRangeTarget = null;
		// PER CANDIDATE since card bb949dd9, not one shared radius: the reach includes the
		// target's own hull credit (see AiGunReachPx), so a boss is engageable from further out
		// than a bullet is. Everything else about the scan is unchanged.
		// A level-halting boss is worth reaching past a lot of trash, so it competes on a
		// DISCOUNTED distance rather than by raw proximity. Scored in the same squared space the
		// loop compares in, hence the squared factor.
		float priorityBiasSq = PriorityTargetBias * PriorityTargetBias;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (IsAiShootable(baddy) && !ReferenceEquals(baddy, sparedUfo) && !ReferenceEquals(baddy, sparedFarUfo))
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
				float reachPx = AiGunReachPx(bulletlifetime, ThreatRadius(baddy));
				if (onScreen && trueDistSq <= reachPx * reachPx && trueDistSq < nearestInRangeSq)
				{
					nearestInRangeSq = trueDistSq;
					inRangeTarget = baddy;
				}
			}
		}
		// Undo the bias before the range test: the discount decides WHICH target wins, never
		// whether a bullet can actually reach it.
		// The reach is now the CHOSEN target's own (card bb949dd9), so it is re-read whenever
		// `nearest` changes -- a fallback to the nearest reachable target swaps the hull the
		// credit came from, and testing the fallback against the discounted winner's reach would
		// be the drift this whole helper exists to prevent. Zero when nothing was found, which
		// with `nearestDist = MaxValue` means "do not fire".
		float nearestReach = 0f;
		if (nearest != null)
		{
			Vector2 toChosen = nearest.Position - base.Position;
			nearestDist = (toChosen).Length();
			nearestReach = AiGunReachPx(bulletlifetime, ThreatRadius(nearest));
			if (nearestDist > nearestReach && inRangeTarget != null)
			{
				nearest = inRangeTarget;
				nearestDist = (float)Math.Sqrt(nearestInRangeSq);
				nearestReach = AiGunReachPx(bulletlifetime, ThreatRadius(nearest));
			}
		}
		else
		{
			nearestDist = float.MaxValue;
		}
		bool fired = false;
		if (nearestDist <= nearestReach)
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

	// Record (and under `?aiseeklog`, print) this tick's deliberate destination -- card fd126847.
	// The `[aiseek]` line is the term's ONLY observable: a seek that oscillates and a seek that
	// holds still produce the same still frame, and the AI bench's `steer=` is the post-low-pass
	// SUM, in which one attractor cannot be told from another.
	// `predictive` is the branch that ACTUALLY RAN, threaded in from the call site rather than
	// re-read off `DebugFlags.AiSeekArrive` here. That is card d79b7ea7's lesson applied: a report
	// derived from the flag says what was ASKED FOR, and an A/B arm whose dispatch is broken then
	// measures the shipped gate twice and prints a plausible table. It also lets the line say
	// `position` for the attractors that are deliberately out of the predictive gate's scope.
	private void NoteAiSeek(AiSeekKind kind, Vector2 target, float dist, bool engaged, bool predictive)
	{
		aiSeekKind = kind;
		aiSeekTarget = target;
		aiSeekDist = dist;
		aiSeekEngaged = engaged;
		aiSeekPredictive = predictive;
		if (EvilAliensWeb.Compat.DebugFlags.AiSeekLog)
		{
			Console.WriteLine("[aiseek] " + AiSeekReport());
		}
	}

	// One ship's seek state as data. Shared by the per-tick `?aiseeklog` trace and the on-demand
	// `eaAiSeek()` / `eval AiSeek` dump so the two can never drift into describing different things.
	internal string AiSeekReport()
	{
		return "p" + player
			+ " kind=" + aiSeekKind.ToString().ToLowerInvariant()
			+ " tgt=" + Fmt(aiSeekTarget.X) + "," + Fmt(aiSeekTarget.Y)
			+ " pos=" + Fmt(base.Position.X) + "," + Fmt(base.Position.Y)
			+ " dist=" + Fmt(aiSeekDist)
			+ " v=" + (SpeedVector).Length().ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
			+ " gate=" + (aiSeekEngaged ? "on" : "off")
			+ " dz=" + Fmt(SeekArriveDeadzonePx)
			+ " arrive=" + (aiSeekPredictive ? "predictive" : "position");
	}

	private static string Fmt(float v)
	{
		return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
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
		// Which writer owns `steerTarget` (card fd126847). Same INVARIANT as the weight above: a
		// write that can run after another one must set this too. It is what `?aiseeklog` reports and
		// what scopes the predictive arrive gate to the station -- see AiSeekKind.
		AiSeekKind seekKind = AiSeekKind.None;
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
					seekKind = AiSeekKind.Blast;
				}
				continue;
			}
			if (baddy is JunkBoss)
			{
				steerTarget = baddy.Position;
				seekKind = AiSeekKind.JunkBoss;
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
					float urge = ThreatFieldStrength(Math.Abs(offLane) / VerticalLaneClearancePx, SweepLaneAvoidStrength);
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
					float urge = ThreatFieldStrength(Math.Abs(offLane) / SweepLaneClearancePx, SweepLaneAvoidStrength);
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
					float strength = ThreatFieldStrength(d / lazerRange, LazerAvoidStrength);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, d / lazerRange);
					}
					repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - shortestpoint) + dodgeAngle);
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
				// ?aievade=0 disables the closest-approach path entirely, so everything falls
				// through to the radial field. A MEASUREMENT seam (card ada9e839): this special
				// case predates the field composition and has never been measured inside it.
				// THE DIRECTIONAL SHAPE (card e425781b), evaluated for every threat and ADDED to
				// the radial field below rather than replacing it -- the shipped shape is a circle
				// with a velocity-aligned hat on it, so both halves are real. Placed before the
				// evade so a mover contributes its cone even on the ticks the evade takes over.
				AddSweptRepellent(ref repel, baddy, dodgeAngle, maxSteerStrength);
				if (EvilAliensWeb.Compat.DebugFlags.AiEvadeMovers != false
					&& EvadeMovingThreat(ref repel, baddy, dodgeAngle, minSteerStrength, maxSteerStrength))
				{
					continue;
				}
				float dist = ThreatEdgeDistance(base.Position, baddy);
				// Personal-space field, sized to the THREAT (card f4d1721f). The 2008 code gave
				// everything the same flat 150px, which is nothing to something the size of the
				// spider boss -- by the time it pushed at all the ship was already inside the
				// hitbox. `dist` is edge distance, so this is clearance the AI wants BEYOND the
				// thing's own hull, and it scales with how big the hull is.
				float field = ThreatFieldRange(baddy);
				if (dist <= field)
				{
					float strength = ThreatFieldStrength(dist / field, maxSteerStrength, ThreatTypeFalloff(baddy), ThreatTypeClassicCurve(baddy));
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, dist / field);
					}
					strength *= ThreatTypeScale(baddy);
					EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy, EvilAliensWeb.Compat.AiBench.ThreatPath.Field, strength, field, dist);
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
				seekKind = AiSeekKind.Powerup;
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
			// Gun REACH is a CENTRE distance (it is what DoAIFire range-tests), so the body term
			// converts it into the edge space everything here is measured in. Since card
			// bb949dd9 the reach credits the boss's own hull radius -- a bullet only has to
			// reach the hull -- so r* is `travel - (sqrt(2)-1)*halfExtent` rather than
			// `travel - sqrt(2)*halfExtent`, and the ship stands where it can actually shoot
			// from instead of a whole hull's width closer. Same helper as the gate, on purpose.
			float anchorPx = AiGunReachPx(bulletlifetime, ThreatRadius(haltingBoss))
				- ThreatBodyTerm(haltingBoss);
			float pull = BossApproachWeight(bossEdgeDist, anchorPx, ThreatFieldRange(haltingBoss),
				ThreatTypeFalloff(haltingBoss), ThreatTypeClassicCurve(haltingBoss),
				ThreatTypeScale(haltingBoss), maxSteerStrength, SteerNoiseFloor) * BossApproachScale;
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
			if (ChooseBossSteerTarget(pull, steerTargetWeight, steerTarget.X > 2000f))
			{
				steerTarget = haltingBoss.Position;
				steerTargetWeight = pull;
				seekKind = AiSeekKind.Boss;
			}
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.readyToConnect && ship != this && readyToConnect && !isConnectedWith(ship))
			{
				steerTarget = ship.Position;
				// EVERY steerTarget write sets its own weight, including the ones that keep the
				// station's. This one overwrites the boss approach above, so inheriting silently
				// would fly the DETOUR at the approach's weight -- the one case where "leave it
				// at the default" and "leave it at whatever the last writer set" differ.
				steerTargetWeight = SeekWeight;
				seekKind = AiSeekKind.Dock;
			}
		}
		if (steerTarget.X > 2000f && !collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			seekKind = AiSeekKind.Station;
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
			seekKind = AiSeekKind.Station;
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
			delta = base.Position - steerTarget;
			float distToTarget = (delta).Length();
			// THE PREDICTIVE ARRIVE GATE (card fd126847), and note the SCOPE: the station only. Every
			// other writer of `steerTarget` has arrival semantics of its own -- a powerup stops
			// existing on contact, the boss approach parks in a band solved against the boss's own
			// repellent, a dock partner and a blastable cluster both move -- so predicting a rest
			// point against those would be predicting against a target that will not be there. They
			// keep the plain position test, which for them is the whole of their deadzone.
			// See SeekArriveEngaged for why the position test alone pingpongs at a FIXED target.
			bool predictiveArrive = seekKind == AiSeekKind.Station
				&& EvilAliensWeb.Compat.DebugFlags.AiSeekArrive;
			bool seekEngaged = predictiveArrive
				// The CALM time constant, not the adaptive one the blend below will pick: the tail is
				// what the smoother does once the pull is gone, and a station seek that has just
				// been switched off IS the calm case. Under a strong push the real constant
				// collapses toward SteerSmoothUrgentMs and this over-predicts the tail -- which only
				// releases the station pull sooner, in a tick where the repellents own the steer
				// anyway.
				// THE TAIL IS CAPPED AT THE SEEK'S OWN WEIGHT, and that cap is not a detail -- it is
				// what keeps this a deadzone rather than a gate on another force (the field
				// principle, card ada9e839). |aiSteer| is the whole smoothed SUM, so on a tick with
				// a threat pushing at 4 it predicts a 270ms tail and would release the station pull
				// ~100px out -- i.e. the seek would silently switch itself off whenever the bot was
				// dodging. Measured: that costs +4.00 +- 1.94 deaths on CrazyGame at N=16. The seek
				// can only ever have put its own weight into the smoother, so that is the most its
				// own decay can be worth, and everything above it belongs to forces that will still
				// be driving the ship after this pull is gone.
				? SeekArriveEngaged(steerTarget - base.Position, SpeedVector, SeekArriveDeadzonePx,
					MathHelper.Min((aiSteer).Length(), steerTargetWeight), SteerSmoothMs, SteerNoiseFloor)
				: (distToTarget > SeekArriveDeadzonePx);
			NoteAiSeek(seekKind, steerTarget, distToTarget, seekEngaged, predictiveArrive);
			if (seekEngaged)
			{
				// Plain positional pull, as in 2008. THE deliberate-destination attractor: it goes
				// into `direction` and is never floored (card ada9e839), because its anti-pingpong
				// mechanism is the deadzone above -- switched off inside it, full strength outside,
				// a hard edge.
				// **THAT HARD EDGE IS NOT SELF-STABILISING, AND THIS COMMENT USED TO CLAIM IT WAS**
				// (card fd126847). The claim was that a ship crossing the edge at full speed coasts
				// 11.3px and halts inside the zone, so it cannot cross back out under its own
				// momentum -- which is true of a COASTING ship and false of this one, because the
				// low-pass keeps thrusting for ~6 ticks after the pull is switched off. The gate
				// above predicts that tail instead; the deadzone radius is still what it is measured
				// against. **The margin is 3.7px at the shipped 15px radius**, not the
				// comfortable one it was at 30 -- which is exactly why card 05a2b818 stopped at
				// 15 rather than taking the 2008 value of 10 that measured better. Anything at or
				// below 11.3 breaks the bound; ProbeAiFieldComposition derives it from the motion
				// constants and fails on it. See DefaultSeekArriveDeadzonePx.
				// A velocity-damped ARRIVE was tried here instead and reverted -- it contains
				// -SpeedVector, so it brakes the ship whenever it is moving relative to its station,
				// which is most of a boss fight. That measured coast 28% -> 59% and 24 -> 70 deaths:
				// the bot was being held at a standstill and could not accelerate out of trouble.
				direction += steerTargetWeight
					* MyMath.AngleToVector(MyMath.VectorToAngle(steerTarget - base.Position));
			}
		}
		else
		{
			// Recorded too, so `?aiseeklog` can tell "no destination this tick" (docked, or a Floor
			// level with connectors up) from "the log stopped" -- a gap in the trace otherwise reads
			// as the ship having been removed.
			NoteAiSeek(AiSeekKind.None, steerTarget, 0f, engaged: false, predictive: false);
		}
		float edgeMargin = steerRange;
		float bottomEdge = 600f;
		if (collection.ContainsType<Floor>())
		{
			bottomEdge = 560f;
		}
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
		// THE TOP-EDGE DANGER BAND. The top edge is not just a boundary, it is where UFOs enter --
		// a ship pinned against it gets exploded by something spawning on top of it. The stock edge
		// repulsion above tops out at maxSteerStrength (4), which is no contest against a lane
		// escape (18) or the spider boss's own field, so fleeing upward parked the ship on the
		// ceiling. This term is scaled to actually compete. Card 2248e5eb measured it against the
		// 2008 arm (`?aitopedgestrength=0`) at N=60 and KEPT it; its magnitudes are untouched here.
		//
		// PLACEMENT (card 13960838): it is a REPELLENT, so it belongs HERE, in `repel`, with every
		// other one -- upstream of the cancellation floor and of the low-pass. It used to be added
		// to `direction` AFTER the smoothing, which made it the one steering vote in the whole
		// method that was neither damped nor allowed to cancel: a raw 0..20 vector welded onto a
		// smoothed sum whose usual magnitude is order 1-4. The consequence was not subtle -- the
		// powerup pull a few hundred lines up maxes out at that same `maxSteerStrength`, so
		// wherever the band beat 4 a pickup was arithmetically unreachable, which is what the card
		// was reported for. See TopEdgeAvoidMagnitude for the shape and the guarded divisor.
		Vector2 topEdgePush = new Vector2(0f,
			TopEdgeAvoidMagnitude(base.Position.Y, TopEdgeDangerPx, TopEdgeAvoidStrength));
		if (TopEdgeComposes)
		{
			repel += topEdgePush;
			ReportTopEdgePlacement("composed");
		}
		// THE REPULSION CANCELLATION FLOOR, and then the combine (card ada9e839). Everything that
		// pushes AWAY has now had its say; if the resultant of all of it is at or below the delta,
		// the repellents have argued each other to a standstill and the ship is not pushed at all.
		// Applied to `repel` alone and BEFORE the low-pass, which is the only placement that means
		// anything: the point is to stop a noise-directioned residual from ever entering the sum,
		// and a floor downstream of the blend would be judging a lagged mixture of this tick's
		// cancellation and the last few ticks' real pushes.
		bool repelZeroed = (repel).Length() <= RepulseCancelDelta;
		// Reported BEFORE the zeroing, because afterwards "two threats cancelled out" and "nothing
		// was pushing" are the same vector.
		EvilAliensWeb.Compat.AiBench.NoteRepel(this, repel, repelZeroed);
		if (repelZeroed)
		{
			repel = Vector2.Zero;
		}
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
		// ?aitopedgecompose=0 -- the top-edge band's PRE-CARD placement, restored verbatim: added
		// here, downstream of the low-pass, so it is neither smoothed nor subject to the
		// repulsion-cancel floor. It is the A/B arm for card 13960838 and the deliberate bug
		// reproduction, nothing else; the composed placement above is what ships.
		if (!TopEdgeComposes)
		{
			direction += topEdgePush;
			ReportTopEdgePlacement("post-smoothing");
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
		strength *= ThreatTypeScale(baddy);
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
	private void AddSweptRepellent(ref Vector2 repel, AlienDrawableGameComponent baddy, float dodgeAngle, float maxSteerStrength)
	{
		if (!ConeEnabled)
		{
			return;
		}
		if (!baddy.TryGetAiSweptPath(out var anchor, out var velocity, out var halfWidth))
		{
			return;
		}
		SweptShape shape = EvaluateSweptShape(base.Position, SpeedVector, anchor, velocity,
			halfWidth, AiHalfExtent(), maxSteerStrength, LaneWedgeEnabled);
		float typeScale = ThreatTypeScale(baddy);
		if (shape.ConeStrength > 0f)
		{
			float strength = shape.ConeStrength * typeScale;
			EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy,
				EvilAliensWeb.Compat.AiBench.ThreatPath.Cone, strength, shape.ConeLength, shape.ConeEdgeDist);
			repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(shape.ConeDir) + dodgeAngle);
		}
		if (shape.WedgeStrength > 0f)
		{
			float strength = shape.WedgeStrength * typeScale;
			EvilAliensWeb.Compat.AiBench.NoteThreatTerm(this, baddy,
				EvilAliensWeb.Compat.AiBench.ThreatPath.Wedge, strength, shape.WedgeLength, shape.WedgeEdgeDist);
			repel += strength * MyMath.AngleToVector(MyMath.VectorToAngle(shape.WedgeDir) + dodgeAngle);
		}
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

	// THE SHAPE ITSELF, as a pure function of geometry -- no ship, no component, no Game. That is
	// deliberate: it is the whole decision this card makes, and a decision is verified as DATA.
	// `logic_probe`'s ProbeAiConeShape calls exactly this and tabulates it at FIXED distances,
	// which is also the only honest way to compare two fields here -- see the card's warning that
	// a mean field strength is a selection effect, not a measurement.
	// `shipVel` is used solely to break the tie when the ship sits exactly on the centre line.
	internal static SweptShape EvaluateSweptShape(Vector2 shipPos, Vector2 shipVel, Vector2 anchor,
		Vector2 velocity, float halfWidth, float shipHalfExtent, float maxSteerStrength, bool wedgeEnabled)
	{
		SweptShape result = default(SweptShape);
		float speed = (velocity).Length();
		if (speed < 0.001f)
		{
			// Not moving: it has no path to project, and its radial field already describes it.
			return result;
		}
		Vector2 axis = velocity / speed;
		float coneLen = MathHelper.Min(speed * ConeLeadMs, ConeMaxLenPx);
		if (coneLen < 1f)
		{
			return result;
		}
		Vector2 d = shipPos - anchor;
		float u = Vector2.Dot(d, axis);
		if (u <= 0f)
		{
			// Behind the mover. Nothing is coming this way, and the body itself is the radial
			// field's business.
			return result;
		}
		Vector2 acrossVec = d - u * axis;
		float w = (acrossVec).Length();
		// THE ACROSS-AXIS REACH. Flat by default; ConeSpread > 0 scales it with the band this mover
		// actually sweeps, floored so a swarm of small fast objects keeps a usable skirt and capped at
		// the flat width so a big one never exceeds the value that was swept. See DefaultConeSpread.
		float acrossReach = ConeWidthPx;
		if (ConeSpread > 0f)
		{
			acrossReach = MathHelper.Clamp(halfWidth * ConeSpread, ConeWidthMinPx, ConeWidthPx);
		}
		// The unit direction OUT of the corridor, i.e. the way the cone pushes.
		Vector2 side;
		if (w > 0.001f)
		{
			side = acrossVec / w;
		}
		else
		{
			// Dead on the centre line, so the shape itself cannot pick a side -- take the one the
			// ship is already drifting toward, so the escape never fights its own momentum. When
			// it is not drifting either the sign is settled deterministically, rather than left
			// to the direction of a float rounding error.
			side = new Vector2(0f - axis.Y, axis.X);
			if (Vector2.Dot(side, shipVel) < 0f)
			{
				side = -side;
			}
		}
		// ---- the cone ----
		float taperedHalf = halfWidth * MathHelper.Max(0f, 1f - ConeTaper * (u / coneLen));
		float edgeAcross = MathHelper.Max(0f, w - taperedHalf);
		float along = 1f - (float)Math.Pow(MathHelper.Clamp(u / coneLen, 0f, 1f), ConeFallAlong);
		if (along > 0f)
		{
			float across = (edgeAcross >= acrossReach)
				? 0f
				: (float)Math.Pow(1f - edgeAcross / acrossReach, ConeFallAcross);
			if (across > 0f)
			{
				result.ConeStrength = maxSteerStrength * ConeScale * along * across;
				result.ConeDir = side;
				result.ConeLength = coneLen;
				result.ConeEdgeDist = edgeAcross;
			}
		}
		// ---- the lane wedge ----
		if (!wedgeEnabled)
		{
			return result;
		}
		// A gap only counts as an escape if the ship can survive in it: its own body, plus the
		// distance it needs to stop, on each side. Derived from the real motion constants rather
		// than chosen, exactly as DefaultSeekArriveDeadzonePx is.
		float stoppingDistance = 0.5f * ShipMaxSpeed * ShipMaxSpeed / ShipDeceleration;
		float survivableGap = 2f * (shipHalfExtent + stoppingDistance);
		// A WEDGE IS FOR A LANE, AND A LANE IS A BAND TOO WIDE TO GO AROUND. Anything narrower
		// than the room a ship needs is an obstacle, not a corridor: the ship can simply cross its
		// path, so offering only ONE escape direction would be a lie -- and an 18-strength shove
		// aimed at a bullet or a small rock drifting near the ceiling out-votes the entire rest of
		// the field. Measured before this gate existed, every UFO in SpaceDodge was wedging (3263
		// contributions at mean 4.25) purely for entering from the top.
		//
		// IT IS A SIZE THRESHOLD, NOT A "ONLY THE SPIDER BOSS" TEST, and the difference is worth
		// knowing before reading a threats= line. The bar is ~63px of half-extent, which bullets and
		// ordinary rocks miss and which a BIG UFO or a reallyBig asteroid clears -- so those still
		// raise a wedge when their path hugs an edge (measured on this build: UFO(wedge) 443
		// contributions at mean 1.81 on the spider rig, Asteroid(wedge) 296 at mean 0.98 on
		// SpaceDodge). That is the rule working rather than leaking: a 90px-wide UFO sweeping the
		// ceiling really does leave a gap the ship cannot cross in time. What the gate removes is
		// the population that made the term meaningless, not every non-boss.
		if (halfWidth < survivableGap)
		{
			return result;
		}
		// Which way is "out of the lane", if either. Measured at the cross-section the SHIP is at
		// (`anchor + u*axis`), not at the anchor -- a mover typically enters from off-screen, and
		// an anchor outside the play field reports zero room on its near side, which would wedge
		// everything that ever crossed a boundary.
		Vector2 bandPoint = anchor + u * axis;
		Vector2 across1 = new Vector2(0f - axis.Y, axis.X);
		float room1 = PlayfieldExitDistance(bandPoint, across1) - halfWidth;
		float room2 = PlayfieldExitDistance(bandPoint, -across1) - halfWidth;
		Vector2 outDir;
		if (room1 < survivableGap && room1 <= room2)
		{
			// Side 1 is the trap, so the escape is side 2.
			outDir = -across1;
		}
		else if (room2 < survivableGap)
		{
			outDir = across1;
		}
		else
		{
			// Both sides are survivable: an ordinary free mover, and the symmetric cone above is
			// the right shape. True of a mid-screen lane as much as of an asteroid.
			return result;
		}
		// The wedge runs the whole remaining length of the play field rather than the cone's
		// speed-scaled length: the lane is lethal for its entire extent, so closing only the near
		// stretch would invite the ship to sit in the far half of a corridor it cannot leave in
		// time -- and the boss's "Danger!" telegraph, which is the whole warning the player gets,
		// happens while it is still a screen away.
		float wedgeLen = MathHelper.Max(PlayfieldExitDistance(anchor, axis), coneLen);
		float wedgeAlong = 1f - (float)Math.Pow(MathHelper.Clamp(u / wedgeLen, 0f, 1f), LaneWedgeFallAlong);
		if (wedgeAlong <= 0f)
		{
			return result;
		}
		// FULL strength everywhere from the trapped edge across to the far side of the band, then
		// the cone's ordinary transverse falloff beyond it -- so the only downhill direction is
		// OUT, and a ship that has already left is nudged rather than shoved back.
		float outward = Vector2.Dot(d, outDir);
		float wedgeAcross;
		if (outward <= halfWidth)
		{
			wedgeAcross = 1f;
		}
		else if (outward - halfWidth >= acrossReach)
		{
			wedgeAcross = 0f;
		}
		else
		{
			wedgeAcross = (float)Math.Pow(1f - (outward - halfWidth) / acrossReach, ConeFallAcross);
		}
		if (wedgeAcross > 0f)
		{
			result.WedgeStrength = LaneWedgeStrength * wedgeAlong * wedgeAcross;
			result.WedgeDir = outDir;
			result.WedgeLength = wedgeLen;
			result.WedgeEdgeDist = MathHelper.Max(0f, outward - halfWidth);
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
	private static float ThreatTypeScale(AlienDrawableGameComponent baddy)
	{
		if (baddy is Asteroid)
		{
			return EvilAliensWeb.Compat.DebugFlags.AiAsteroidThreatScale ?? DefaultAsteroidThreatScale;
		}
		return 1f;
	}

	// Per-type RANGE multiplier, folded into ThreatFieldRange so every caller agrees on how big
	// the field is -- `dist <= field` and `dist / field` must be the same field or the falloff is
	// evaluated against a range the gate never used.
	private static float ThreatTypeRangeScale(AlienDrawableGameComponent baddy)
	{
		if (baddy is Asteroid)
		{
			return EvilAliensWeb.Compat.DebugFlags.AiAsteroidRangeScale ?? DefaultAsteroidRangeScale;
		}
		return 1f;
	}

	// Per-type FALLOFF exponent. Falls back to the global one for every type that has no override.
	private static float ThreatTypeFalloff(AlienDrawableGameComponent baddy)
	{
		if (baddy is Asteroid)
		{
			return EvilAliensWeb.Compat.DebugFlags.AiAsteroidFalloff ?? DefaultAsteroidFalloff;
		}
		return ThreatFieldFalloff;
	}

	private static float ThreatFieldRange(AlienDrawableGameComponent baddy)
	{
		// A per-type ABSOLUTE range replaces the size-scaled formula outright -- that is what the
		// 2008 field was: a flat 150px for everything, regardless of how big it is. Both are
		// measured the same way (EDGE distance; the original subtracted Width/2*sqrt2 exactly as
		// this port does), so the two are directly comparable.
		float flat = (baddy is Asteroid)
			? (EvilAliensWeb.Compat.DebugFlags.AiAsteroidFlatRangePx ?? 0f)
			: 0f;
		float baseRange = (flat > 0f) ? flat : (ThreatFieldBasePx + ThreatRadius(baddy) * ThreatFieldSizeScale);
		return baseRange * ThreatTypeRangeScale(baddy);
	}

	// Strength across that field: FULL up close, dropping away fast so the outer half is
	// effectively free. That combination is the point -- a big field with a gentle falloff would
	// be a no-go zone the ship could never enter, and it still has to fly in close to shoot and
	// to weave through bullets.
	//
	// Deliberately NOT MyMath.PowerCurve: that is `max * (1 - t^p)`, whose falloff gets SHALLOWER
	// as p rises (p=4 still pushes at 34% strength at 90% of the range). This is `max * (1-t)^p`,
	// which is the shape the name "falloff" implies -- p=3 is down to 12% at half range.
	private static float ThreatFieldStrength(float t, float maxSteerStrength)
	{
		return ThreatFieldStrength(t, maxSteerStrength, ThreatFieldFalloff, ClassicFieldCurve);
	}

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

	private static bool ClassicFieldCurve => EvilAliensWeb.Compat.DebugFlags.AiClassicFieldCurve ?? false;

	// Per-type curve family, falling back to the global switch.
	private static bool ThreatTypeClassicCurve(AlienDrawableGameComponent baddy)
	{
		if (baddy is Asteroid)
		{
			return EvilAliensWeb.Compat.DebugFlags.AiAsteroidClassicCurve
				?? EvilAliensWeb.Compat.DebugFlags.AiClassicFieldCurve ?? false;
		}
		return ClassicFieldCurve;
	}

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
		// DO NOT DELETE THIS AS DEAD CODE -- and since card bb949dd9 it reads even more like dead
		// code than it did, because NO shipped configuration reaches k < 1 any more.
		// THE CONFIGURATION THAT USED TO MAKE IT BITE: BrainBoss at its pulse peak on the base
		// weapon: its hitbox is hw = 165 * scale and `scale` pulses 1.00 -> 1.10 (deeper as its
		// HP drops), so the body term runs 233 -> 257px against a 351px bullet travel, which left
		// r* at 118 -> 94px. Undamped that bands 13.5px at scale 1.0 and 10.0px at the peak --
		// through the 11.3px stopping distance, i.e. the ship coasts across its own equilibrium
		// and pingpongs while shooting the brain. Damped, k solved to 0.24 at rest and 0.09 at
		// the peak, and the band was 22.2px at both.
		// WHAT CHANGED: the gun reach now credits the hull radius, so that same brain solves to
		// r* ~ 276px instead of 94 and the slope budget clears k = 1 by ~4x. The damping is
		// therefore INERT at every tier x weapon x hull that ships today -- which is exactly the
		// state in which someone deletes it. It is the bound, not a tuning value: `?aifieldpx=`,
		// `?aigunhull=` and any boss with a wider hull than today's can still drive r* down, and
		// ProbeAiBossApproach asserts the band over that whole domain rather than over the
		// shipped point.
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

	// Does the boss approach take the steering wheel this tick? PURE, and extracted (card
	// bb949dd9) because it is the FLOORED regime's only real assertion: since the anchor credits
	// the boss's hull, r* often sits where the boss's own repellent has decayed under the
	// whole-sum floor, so the pull floats at DefaultSteerNoiseFloor and loses every contest with
	// a live 0.8 powerup detour. What keeps a level-halting boss reachable there is the SENTINEL
	// -- with nothing else chosen the boss takes the wheel at any weight -- and a probe asserting
	// "the weight floors at 0.2" would pass on a build where that takeover had been broken.
	// logic_probe's ProbeAiBossApproach calls this, so the rule is verified rather than copied.
	public static bool ChooseBossSteerTarget(float pull, float currentTargetWeight, bool nobodyChose)
	{
		return nobodyChose || pull > currentTargetWeight;
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
