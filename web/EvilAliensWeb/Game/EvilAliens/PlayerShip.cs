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

	// At or below this total demand the ship parks instead of thrusting. Just above SeekWeight.
	public const float DefaultSteerParkDemand = 0.95f;

	private static float SteerSmoothUrgentMs => EvilAliensWeb.Compat.DebugFlags.AiSteerSmoothUrgentMs ?? DefaultSteerSmoothUrgentMs;

	private static float SteerParkDemand => EvilAliensWeb.Compat.DebugFlags.AiParkDemand ?? DefaultSteerParkDemand;

	// How far ahead the wall logic looks, as MILLISECONDS of closing travel rather than a fixed
	// pixel count. The 2008 code probed `41.67 * MaxSpeed` = ~13.75px against wall tiles that are
	// 800/gridWidth = 67..267px wide -- roughly one ship-width of warning, which is why the bot
	// clipped so much. Closing speed is ship speed plus the wall's own scroll.
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
	// boss-approach standoff has to agree with it or the ship closes to somewhere it still
	// cannot shoot from.
	private const float BulletRangePerMs = 0.78f;

	// Where to sit relative to a halting boss: a fraction of gun range, clamped so a short-lived
	// bullet does not demand ramming distance and a long-lived one does not park off-screen.
	private const float BossStandoffFraction = 0.6f;

	private const float BossStandoffMinPx = 130f;

	private const float BossStandoffMaxPx = 300f;

	// How far down the screen the "UFOs spawn here" danger band reaches, and how hard it pushes.
	// Strong enough to stand up to a lane escape, so the ship settles below the spawn line
	// instead of being held against it.
	private const float TopEdgeDangerPx = 170f;

	private const float TopEdgeAvoidStrength = 20f;

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

	// A live beam kills along its whole length, so it earns a much wider berth than the 2008
	// flat 150px -- with the same steep falloff, so the outer field is still cheap to cross.
	private const float LazerAvoidRangePx = 260f;

	private const float LazerAvoidStrength = 14f;

	// Lateral push while a big UFO is winding up, to make its locked-at-fire aim stale.
	private const float LazerDodgeStrength = 7f;

	// Station-keeping "arrive" behaviour. Deadzone is generous because the exact station is
	// arbitrary -- an idle ship parked 20px off is indistinguishable from one parked on the spot,
	// and chasing the last few pixels is precisely what looked like fidgeting.
	private const float SeekArriveDeadzonePx = 30f;

	// Kept at the 2008 weight so the seek still loses to threat avoidance exactly as before.
	private const float SeekWeight = 0.8f;

	// ---- seek weights for a target the bot CHOSE (cards ada9e839 / 31ceb6ff) ----------------
	//
	// THE BUG THESE FIX. SeekWeight sits BELOW SteerParkDemand on purpose -- that is what makes an
	// idle ship coast to a stop instead of fidgeting around an arbitrary spot. But every
	// deliberate destination in DoAIMove also rides `steerTarget`: a powerup, a level-halting
	// boss's standoff point, a partner to dock with, a blastable cluster. They all inherited the
	// station's weight, so the park zeroed them too and the ship simply did not go, unless
	// something ELSE happened to be pushing that same tick. That is the whole of "the AI is
	// uninterested in powerups" -- and it is also why the boss-approach term added by card
	// f4d1721f could never be seen to do anything: it was correct code the park deleted.
	//
	// WHAT ACTUALLY SHIPPED, AND WHY IT IS ONE DESTINATION AND NOT ALL OF THEM. Raising the seek
	// for EVERY steerTarget was the first shape tried and it is measurably wrong: a level-halting
	// boss is a COMMITMENT (nothing advances until it dies, so paying safety for it is the
	// point), while a powerup, a blastable cluster and a Linker rendezvous are DETOURS whose
	// value has to beat their risk -- and it does not. So only the boss standoff below gets a
	// weight above the park. Everything else still rides SeekWeight and is still parked, exactly
	// as before this card. The full measurement, including the two rigs that disagree about what
	// a good pickup rate even is, is on card ada9e839.

	// POWERUPS -- the DECLINED half, kept as a seam rather than a fix (card ada9e839, which
	// returns to the backlog carrying the numbers). Baked AT SeekWeight, so it is inert and the
	// shipped bot is unchanged here; ?aiseekpowerup= is what reproduces the tables.
	// MEASURED (eahl, Very_Hard, N=16). Level 1 ?invuln, share of spawned powerups taken:
	// 0.8 -> 69%, 1.1 -> 89%, 1.6 -> 95%. Level 1 ?invuln OFF: deaths 3.88 -> 5.44 (at 1.1) /
	// 5.75 (at 1.6, with the standoff held down, so this is the DETOUR's own cost). And the
	// gate it fails, SpaceDodge (600 s cap, N=8, VICTORIES): 6/8 -> 2/8 at 1.1, 4/8 at 1.6,
	// deaths 14.9 -> 28.4 / 20.0. Its powerups sit in an asteroid field where a LOW pickup rate
	// is correct play, so no single scalar satisfies both levels. The researched next design is
	// a threat-aware seek (suppress the detour while any threat field is pushing), not a
	// different number here.
	public const float DefaultSeekPowerupWeight = SeekWeight;

	// THE LEVEL-HALTING BOSS STANDOFF (card 31ceb6ff). Judged on the challenge-level COMPLETION
	// MATRIX, not on deaths: eight of the nine challenge levels run with score.Lives = -1, so a
	// death there is free and the failure that matters is a level that never finishes. Closing on
	// a boss costs deaths by design.
	// Deliberately LOW but still clear of SteerParkDemand (0.95). That floor is not tunable: at
	// or below it the park zeroes this vote again and card 31ceb6ff regresses to inert code,
	// which is the exact defect being fixed. Pinned by logic_probe's ProbeAiSeekWeights.
	public const float DefaultSeekApproachWeight = 1.1f;

	private static float SeekPowerupWeight => EvilAliensWeb.Compat.DebugFlags.AiSeekPowerupWeight ?? DefaultSeekPowerupWeight;

	private static float SeekApproachWeight => EvilAliensWeb.Compat.DebugFlags.AiSeekApproachWeight ?? DefaultSeekApproachWeight;

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

	// Clearance the AI wants beyond ANY threat's hull, before the size term below. Much larger
	// than the 2008 flat 150 -- see ThreatFieldStrength for why a bigger field is not a bigger
	// no-go zone.
	// NOT the value every tier uses -- this is the VERY_HARD row of AiSkillByDifficulty, hence
	// the name rather than Default* like the tier-independent knobs.
	public const float VeryHardThreatFieldBasePx = 190f;

	// Extra clearance per pixel of the threat's own half-extent. The spider boss gets a field
	// several times a bullet's.
	public const float DefaultThreatFieldSizeScale = 1.8f;

	// Exponent of the (1-t)^p falloff. Higher = the field bites later and harder.
	public const float DefaultThreatFieldFalloff = 3f;

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
	// Inzane's FIELD is deliberately NOT pushed past the anchor's 190: every measurement bracketed
	// this knob between 30 and 190, so shrinking it is evidence-backed while GROWING it is pure
	// extrapolation -- and ThreatFieldStrength's own note warns a bigger field is a trade-off, not
	// a free win (the bot still has to close in to shoot). Inzane earns its edge on aim only.
	private static readonly AiSkill[] AiSkillByDifficulty = new AiSkill[5]
	{
		/* Easy      */ AiSkill.Deg(150f, 22.5f),
		/* Medium    */ AiSkill.Deg(163f, 19.5f),
		/* Hard      */ AiSkill.Deg(176f, 17f),
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

	private int aiGapColumn = -1;

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
		base.MaxSpeed = 0.33f;
		base.Deceleration = 0.0047999998f;
		base.Acceleration = 0.003f;
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
			base.MaxSpeed = 0.33f;
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

	private void DoAIMove(ref Vector2 direction, GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		CollisionLevelMap collisionLevelMap = null;
		bool hasWall = false;
		bool altSteering = false;
		float steerRange = 150f;
		float minSteerStrength = 0f;
		float maxSteerStrength = 4f;
		Vector2 steerTarget = new Vector2(float.MaxValue, float.MaxValue);
		// How hard to pull toward whatever steerTarget ends up being. It carries the WEIGHT rather
		// than a flag because the answer is not two-valued: the idle station and every DETOUR park
		// (SeekWeight, as before this card), while a level-halting boss's standoff point has to
		// clear the park or it is dead code.
		// INVARIANT: a steerTarget write that can run AFTER another one must set this too, even
		// to SeekWeight -- otherwise it inherits the previous writer's weight rather than the
		// default, and a detour silently flies at the standoff's. Only the two writes inside the
		// baddy loop (a blastable cluster, a JunkBoss) are exempt, and only because nothing has
		// written a weight yet by then; the two station fallbacks are exempt because they run
		// solely while steerTarget is still MaxValue.
		float steerTargetWeight = SeekWeight;
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
				}
				continue;
			}
			if (baddy is JunkBoss)
			{
				steerTarget = baddy.Position;
			}
			// Sidestep a charging beam. A big UFO winds up for 2500ms and locks its aim at the
			// PLAYER only at the instant it fires, so the dodge is to be somewhere else by then --
			// moving ACROSS the UFO's line of sight during the windup makes the locked aim stale.
			// Standing still and reacting to the beam afterwards cannot work: it appears along its
			// whole length at once.
			if (baddy is UFO && ((UFO)baddy).IsBig && ((UFO)baddy).AiChargingLazer)
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
					direction += LazerDodgeStrength * across;
				}
			}
			// The vertical strips: the fixed X-600 landing column, and the climb that opens the
			// next cycle. Same treatment as the sweep lane, on the other axis -- flat across the
			// band, because every part of it is equally lethal.
			if (baddy is SpiderBoss && ((SpiderBoss)baddy).AiVerticalLaneActive)
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
					direction += new Vector2(away * urge, 0f);
				}
			}
			// Act on the boss's own telegraph. During the "Danger!" arrow the spider boss sits
			// off-screen in the lane it is about to cross, so it is STATIONARY -- the movement
			// prediction says nothing and the distance field is a screen away. Vacating the lane
			// now is the whole point of the warning, and it is far cheaper than trying to escape
			// a screen-wide sweep once it has started.
			if (baddy is SpiderBoss && ((SpiderBoss)baddy).AiSweepIncoming)
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
					direction += new Vector2(0f, away * urge);
				}
			}
			// Card f4d1721f: track the nearest level-HALTING boss so the ship can close on it if
			// it is out of gun range (below). The 2008 code only ever did this for JunkBoss, so
			// against any other boss the AI hovered at its default station and fired only when the
			// boss happened to drift within range -- measured as 55% of ticks with a shootable
			// target and no shot fired, against a BrainBoss parked at the top of the screen.
			// Same on-screen predicate DoAIFire uses. BrainBoss eases in from a negative Y, and
			// without this the ship is dragged toward a standoff point off the top of the screen
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
				SteerThroughWall(ref direction, (Wall)baddy, collisionLevelMap);
			}
			else if (baddy is Lazer)
			{
				getDistanceToLine(baddy, out var d, out var shortestpoint);
				// A live beam is instant death along its whole length, so it gets a far wider
				// berth than the 2008 flat 150px -- with the same steep falloff the threat field
				// uses, so the outer part of the field stays cheap enough to fly in and shoot.
				if (d <= LazerAvoidRangePx)
				{
					float strength = ThreatFieldStrength(d / LazerAvoidRangePx, LazerAvoidStrength);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, d / LazerAvoidRangePx);
					}
					direction += strength * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - shortestpoint) + dodgeAngle);
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
				if (EvadeMovingThreat(ref direction, baddy, dodgeAngle, minSteerStrength, maxSteerStrength))
				{
					continue;
				}
				float dist;
				if (baddy.GetCollisionType() is CollisionBox)
				{
					Vector2 toBaddy = base.Position - baddy.Position;
					dist = (toBaddy).Length() - ((CollisionBox)baddy.GetCollisionType()).Width / 2f * (float)Math.Sqrt(2.0);
				}
				else if (baddy.GetCollisionType() is CollisionMultibox)
				{
					Vector2 toBaddy = base.Position - baddy.Position;
					dist = (toBaddy).Length() - ((CollisionMultibox)baddy.GetCollisionType()).Items[0].Width / 2f * (float)Math.Sqrt(2.0);
				}
				else if (baddy.GetCollisionType() is CollisionSimpleCircle)
				{
					float radius = ((CollisionSimpleCircle)baddy.GetCollisionType()).Radius;
					Vector2 toBaddy = base.Position - baddy.Position;
					dist = MathHelper.Clamp((toBaddy).Length() - radius, 0f, 1000f);
				}
				else
				{
					Vector2 toBaddy = base.Position - baddy.Position;
					dist = (toBaddy).Length();
				}
				// Personal-space field, sized to the THREAT (card f4d1721f). The 2008 code gave
				// everything the same flat 150px, which is nothing to something the size of the
				// spider boss -- by the time it pushed at all the ship was already inside the
				// hitbox. `dist` is edge distance, so this is clearance the AI wants BEYOND the
				// thing's own hull, and it scales with how big the hull is.
				float field = ThreatFieldRange(baddy);
				if (dist <= field)
				{
					float strength = ThreatFieldStrength(dist / field, maxSteerStrength);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, dist / field);
					}
					direction += strength * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - baddy.Position) + dodgeAngle);
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
			}
			// PowerupReachPx, not the 150px `steerRange` the 2008 code shared with the screen-edge
			// margin -- see the const. Beyond this the powerup is still the steerTarget above, so
			// the ship heads for it; this term only shapes the approach.
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
		// Close on a level-halting boss that is out of gun range (card f4d1721f). Nothing else in
		// the level advances until it dies, so hovering at the default station waiting for it to
		// drift into range is not a strategy -- it is the stall. The standoff point keeps the
		// ship's current bearing on the boss and only closes the distance, so this asks to get in
		// RANGE, never to ram it; the threat repulsion above still owns how close is too close.
		// Placed after the powerup pass so a boss fight outranks a pickup detour.
		if (haltingBoss != null)
		{
			float gunRange = bulletlifetime * BulletRangePerMs;
			Vector2 fromBoss = base.Position - haltingBoss.Position;
			float bossDist = (fromBoss).Length();
			float standoff = MathHelper.Clamp(gunRange * BossStandoffFraction, BossStandoffMinPx, BossStandoffMaxPx);
			EvilAliensWeb.Compat.AiBench.NoteBossApproach(this, bossDist, standoff);
			if (bossDist > standoff && bossDist > 0.001f)
			{
				steerTarget = haltingBoss.Position + (fromBoss / bossDist) * standoff;
				steerTargetWeight = SeekApproachWeight;
			}
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.readyToConnect && ship != this && readyToConnect && !isConnectedWith(ship))
			{
				steerTarget = ship.Position;
				// EVERY steerTarget write sets its own weight, including the ones that keep the
				// station's. This one overwrites the boss standoff above, so inheriting silently
				// would fly the DETOUR at the standoff's weight -- the one case where "leave it
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
			delta = base.Position - steerTarget;
			float distToTarget = (delta).Length();
			if (distToTarget > SeekArriveDeadzonePx)
			{
				// Plain positional pull, as in 2008 -- but with a wider deadzone. The 10px original
				// meant an idle ship chased the last few pixels of an arbitrary station forever,
				// sailing past and turning round: the visible "why is it fidgeting when nothing is
				// happening". A velocity-damped ARRIVE was tried here and reverted -- it contains
				// -SpeedVector, so it brakes the ship whenever it is moving relative to its station,
				// which is most of a boss fight. That measured coast 28% -> 59% and 24 -> 70 deaths:
				// the bot was being held at a standstill and could not accelerate out of trouble.
				// Widening the deadzone kills the fidget without ever opposing a real manoeuvre.
				// The weight was chosen where the target was (card ada9e839): a chosen objective
				// has to clear SteerParkDemand or the park zeroes the only vote asking for it,
				// while the idle station must stay below it or the fidget comes back.
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
		if (!altSteering)
		{
			if (base.Position.X < edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, base.Position.X / edgeMargin);
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, base.Position.X / edgeMargin);
				}
				direction += push * new Vector2(1f, 0f);
			}
			if (base.Position.X > 800f - edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, Math.Abs((800f - base.Position.X) / edgeMargin));
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, Math.Abs((800f - base.Position.X) / edgeMargin));
				}
				direction += push * new Vector2(-1f, 0f);
			}
			if (base.Position.Y < edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, base.Position.Y / edgeMargin);
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, base.Position.Y / edgeMargin);
				}
				direction += push * new Vector2(0f, 1f);
			}
			if (base.Position.Y > bottomEdge - edgeMargin)
			{
				float push = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, Math.Abs((bottomEdge - base.Position.Y) / edgeMargin));
				if (altSteering)
				{
					push = MathHelper.Lerp(maxSteerStrength, minSteerStrength, Math.Abs((bottomEdge - base.Position.Y) / edgeMargin));
				}
				direction += push * new Vector2(0f, -1f);
			}
		}
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
		if (base.Position.Y < TopEdgeDangerPx)
		{
			direction += new Vector2(0f, TopEdgeAvoidStrength * (1f - base.Position.Y / TopEdgeDangerPx));
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
		// PARK when the only thing pulling is the station itself. Move() throws the magnitude away
		// and thrusts at full acceleration along the angle, so a weak-but-nonzero steer is not a
		// gentle nudge -- it is full throttle at an arbitrary point the ship is already next to,
		// which it then sails past and comes back to, forever. That is the visible up-down bounce.
		// The threshold sits just above the station pull (SeekWeight 0.8), so a lone seek coasts to
		// a stop while the seek plus ANYTHING else -- an edge push, a threat, a wall -- still flies.
		if ((direction).Length() <= SteerParkDemand)
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
	private bool EvadeMovingThreat(ref Vector2 direction, AlienDrawableGameComponent baddy, float dodgeAngle, float minSteerStrength, float maxSteerStrength)
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
		direction += strength * MyMath.AngleToVector(MyMath.VectorToAngle(side) + dodgeAngle);
		return true;
	}

	// How far from a threat's HULL the AI wants to stay, scaled by how big the hull is. The 2008
	// code used one flat 150px for everything, which is nothing next to the spider boss -- by the
	// time the field pushed at all the ship was inside the hitbox, and the fight read as the bot
	// having no idea what it was doing.
	private static float ThreatFieldRange(AlienDrawableGameComponent baddy)
	{
		return ThreatFieldBasePx + ThreatRadius(baddy) * ThreatFieldSizeScale;
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
		float u = 1f - MathHelper.Clamp(t, 0f, 1f);
		return maxSteerStrength * (float)Math.Pow(u, ThreatFieldFalloff);
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
	// What the 2008 code did, and why it jittered (all three measured with ?aibench):
	//   * it probed a fixed `41.67 * MaxSpeed` = ~13.75px ahead, against tiles 67..267px wide --
	//     about one ship-width of warning at full closing speed;
	//   * on a hit it SLAMMED the steer (`direction.X = -max(|direction.Y|, 1)`), a full reversal
	//     rather than a push, so the next tick's clear probe threw it straight back;
	//   * it re-picked left-vs-right every single tick, and a wall scrolling on by one row can
	//     swap which side is cheaper, reversing the ship mid-approach.
	// Together those spun the commanded heading at ~1050 deg/s. This version looks ahead by
	// TIME, pushes proportionally, and commits to a gap.

	// Steer toward the committed gap in this wall, and away from tiles that are close in the
	// direction of travel. Called once per Wall in the steering loop; only ever adds to
	// `direction`, so it composes with every other steering term like they compose with each
	// other. The hard "do not fly into that" clamp is ClampIntoWallSpace, applied last.
	private void SteerThroughWall(ref Vector2 direction, Wall wall, CollisionLevelMap map)
	{
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
	// Unlike the 2008 override this fires only when a tile is within roughly ONE TICK of travel,
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
