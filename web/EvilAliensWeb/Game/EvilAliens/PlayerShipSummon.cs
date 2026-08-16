using System;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The "you are coming back" indicator that stands in for a dead player until their ship
// respawns. Card 37f3a663 replaced the original XBLIG look -- a LazerGenerator charge orb plus a
// DarkGoldenrod integer countdown -- with a clock ring that fills, pulses as it nears full and
// pops as the ship arrives, dropping a free bomb as a reward for sticking with it.
//
// Card 045c5a92 then restyled that ring to the owner's mock (new_assets_raw/respawndesign.png): a
// near-black disc, a magenta rim with a thick glowing pink arc sweeping it, radiating spikes, a
// whole-second countdown numeral in the middle and an italic "RESPAWNING!" underneath. The numeral
// is the 2008 integer countdown coming BACK in a new form, with the owner's explicit approval.
//
// THE ARC STILL FILLS RATHER THAN DRAINS. The mock is a hand-composited still whose arc extent is
// illustrative; card 37f3a663's fill-and-pop is approved, shipped BEHAVIOUR, this card asked for a
// LOOK, and the ring reaching full is what makes the pop read as the arrival.
//
// TWO MODES, and the difference is authority:
//   Setup       the real one, for a ship THIS peer owns. It runs the countdown, rumbles the
//               owner's pad, and at zero spawns the PlayerShip.
//   SetupRemote a COSMETIC copy of the peer's, driven by NetProtocol.EvRespawn. Same ring, same
//               pop, same reward blast -- but it never spawns a ship. The peer's own ship
//               arrives through the ordinary remoteAlive edge (NetSession.SpawnPuppet), which
//               stays the only way a puppet is born.
// Being the same TYPE in both modes is deliberate: every existing Purge<PlayerShipSummon>
// (GameScene.LoseLife / NetApplyReset / Terminate) then cleans the cosmetic one up for free.
//
// The ring, the disc and the spikes are all rotated quads of GFX/Game/blank, a 10x10 OPAQUE WHITE
// texture this class already loaded (and never drew) as its own animation, plus GFX/Sprites/
// lazerglow for the soft halos. The draws go through the SpriteBatchWrapper overloads, which clamp
// the source rect to LogicalBounds() -- a raw SpriteBatch.Draw would stretch the padding on a
// --padtest build, which is the SealAlpha trap (card b7e9b106). NO NEW ART: both textures and the
// menufont ship already, and all three are in GameScene.PreloadGraphicalContent (which every level
// override calls first), so LoadContent below is a cache hit rather than a decode at the worst
// possible moment -- the first respawn after a death.
//
// Alpha is STRAIGHT project-wide: the solid layers are BlendMode 1 (NonPremultiplied) and the
// glows are BlendMode 2 (additive). BlendState.AlphaBlend, KNI's PREmultiplied variant, is never
// used here -- a glowing magenta ring is exactly the thing that goes additive-bright by accident.
internal class PlayerShipSummon : AlienDrawableGameComponent
{
	// Ring geometry, in 800x600 design px. The radius is the mock's, measured off it: the rim's
	// outer edge lands at ~37 design px and the spike tips at ~48.
	// 96, not the 48 the gold ring used. SpriteBatch does no antialiasing, so the arc's outer
	// silhouette is a hard polygon: its flat sides sag inside the true circle by (r*step/2)^2/(2r),
	// which at 48 segments and this much thicker stroke is 0.083 design px -- enough to stair-step
	// visibly along the outer edge at real render scale. Doubling the tessellation quarters that to
	// 0.021 px, and costs only sprites within a batch that was already one texture and one blend.
	private const int RingSegments = 96;
	private const float RingRadius = 34f;
	private const float RimThickness = 3f;
	private const float ArcThickness = 10f;
	private const float DiscRadiusFactor = 0.95f;
	private const float SegOverlap = 1.02f;

	// The spikes radiating outward from the rim. Each is a short stack of `blank` quads whose width
	// shrinks toward the tip -- see the taper loop in Draw for why lazerglow is the wrong primitive.
	private const int SpikeCount = 12;
	private const float SpikeLength = 17f;
	private const float SpikeWidth = 4.5f;
	private const int SpikeTaperSteps = 4;

	// The centre numeral and the italic label under the disc. Both are sized off the mock's own
	// proportions: measured there, the label is 1.12x the disc's diameter and the numeral's cap
	// height is 0.21x it. The numeral is DELIBERATELY a little larger than that (~0.29x) -- the
	// mock's "2.1" is three glyphs where this is one or two, and 0.21x on a 600 px-tall screen
	// reads small in play. The label is faithful.
	private const string LabelText = "RESPAWNING!";
	private const float DigitScale = 0.95f;
	private const float LabelScale = 0.30f;
	private const float LabelOffsetY = 54f;
	private const float LabelItalic = 0.28f;

	// The "little snappy animation when the nr changes" the owner asked for: the numeral punches
	// out and settles inside PunchMs of every whole-second change. See DigitPunch.
	private const float PunchMs = 160f;
	private const float PunchScale = 0.45f;

	// Above this fill the ring pulses, and the pulse rate ramps from PulseHzStart to PulseHzEnd
	// as it closes on full -- "starts blinking/pulsating when it's near full".
	private const float PulseStartFill = 0.72f;
	private const float PulseHzStart = 2f;
	private const float PulseHzEnd = 9f;

	// The final stretch of the clock, during which the ring flares outward and fades: the POP.
	// It is part of the FILL rather than an afterlife because the component Die()s on the same
	// tick the ship arrives, so there is no frame left to draw one in.
	private const float PopMs = 220f;
	private const float PopRadiusGrowth = 0.9f;

	// The reward for sticking with it. A FIXED level, not the player's own bomb progression, and
	// no bomb is spent for it -- it is a gift.
	//
	// Card 258afd66 lowered it 4 -> 3: at 4 it cleared so much of the screen that a co-op partner's
	// death read as a free win rather than as a helping hand. A Blast's reach AND its lifetime both
	// scale with the level (Blast.Setup: lifetime = 1000ms * (level+1)), so the drop is visible in
	// data as well as on screen -- see the reward line in SpawnRewardBlast.
	//
	// IT ONLY EVER FIRES IN CO-OP, and that is deliberate rather than an oversight of the
	// suppression above. Every single-player death is a WIPE, so no summon is built and the
	// respawn goes through LoseLife -> the checkpoint reset instead -- where the world has just
	// been purged and a bomb would hit nothing. Ruled that way when the card was planned.
	private const int RewardBlastLevel = 3;

	// The mock's soft halos (spikes, the glow behind the disc, the glow behind the text) and the
	// numeral/label. Both are preloaded by GameScene, so this is a dictionary hit -- see the header.
	private Texture2D glowTex;

	private SpriteFont font;

	private int player;

	private int countdown;

	private Timer countdowntimer = new Timer(1000f, repeating: true);

	// The cosmetic mode's whole clock. The real mode's lives in countdown + countdowntimer and is
	// left exactly as it was, so the respawn MOMENT is unchanged by this card.
	private Timer cosmetictimer = new Timer(1000f, repeating: false);

	private bool cosmetic;

	// One-shot latch for the wiped-world Draw report below.
	private bool wipeReported;

	private float totalMs;

	private Vibrator vibrator;

	private float spawndirection;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	// How long this respawn takes, in ms. Read by PlayerShip so the announcement to the peer
	// carries the duration this clock will actually run for -- it falls out of the dying player's
	// own respawntimebonus as well as the difficulty, so the far peer cannot re-derive it.
	internal int DurationMs => (int)totalMs;

	// A cosmetic copy of the PEER's respawn: draws, pops and rewards, but spawns no ship.
	internal bool IsCosmetic => cosmetic;

	// The clock's remaining ms, for the monotonicity leg in NetRespawnTest. The value is otherwise
	// only reachable through DebugStateLine's text, and the defect it pins (the ring un-filling
	// once a second) is invisible to every screenshot rig -- ?respawnphase= parks the fill, and a
	// live capture cannot be timed to the one frame per second that was wrong.
	internal float DebugRemainingMs => RemainingMs;

	// The roster slot this indicator belongs to -- the dying player's. Read by NetSession to
	// re-point an announcement for a slot it is already showing rather than stacking a second one.
	internal int Owner => player;

	public PlayerShipSummon(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Game/blank"));
		base.DrawOrder = 20;
		timers.Add(countdowntimer);
		timers.Add(cosmetictimer);
		base.DrawOrder = 11;
		vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
	}

	public static PlayerShipSummon NewPlayerShipSummon(ComponentBin collection, Game game)
	{
		PlayerShipSummon playerShipSummon = collection.Recycle<PlayerShipSummon>();
		if (playerShipSummon == null)
		{
			playerShipSummon = new PlayerShipSummon(game);
		}
		return playerShipSummon;
	}

	public void Setup(int player, float spawndirection, Vector2 position, int respawntimebonus)
	{
		this.spawndirection = spawndirection;
		this.player = player;
		base.Position = position;
		cosmetic = false;
		wipeReported = false;
		countdown = (int)Math.Round((float)(15 - respawntimebonus) * Settings.GetInstance().CurrentDifficulty switch
		{
			Settings.DifficultyLevel.Easy => 0.66f,
			Settings.DifficultyLevel.Medium => 0.66f,
			Settings.DifficultyLevel.Hard => 0.8f,
			Settings.DifficultyLevel.Very_Hard => 0.8f,
			Settings.DifficultyLevel.Inzane => 0.9f,
			_ => 0.66f,
		});
		totalMs = Math.Max(1f, (float)countdown * 1000f);
		// Instances are RECYCLED, so both clocks are restated rather than assumed fresh.
		countdowntimer.Reset();
		countdowntimer.Start();
		cosmetictimer.Stop();
	}

	// The peer's respawn, announced over NetProtocol.EvRespawn (card 37f3a663). `slot` is the
	// peer's roster slot; nothing here reads the local roster, because that seat is not ours.
	internal void SetupRemote(int slot, Vector2 position, int durationMs)
	{
		spawndirection = 0f;
		player = slot;
		base.Position = position;
		cosmetic = true;
		wipeReported = false;
		countdown = 0;
		totalMs = Math.Max(1f, durationMs);
		countdowntimer.Stop();
		cosmetictimer.Duration = totalMs;
		cosmetictimer.Reset();
		cosmetictimer.Start();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		glowTex = content.Load<Texture2D>("GFX/Sprites/lazerglow");
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	// Milliseconds left on the clock. The real mode derives it from the existing 1 Hz countdown
	// rather than running a second clock of its own, so the tick the ship actually arrives on is
	// unchanged and only the DRAWING moved.
	//
	// THE PENDING SECOND IS NOT A ROUNDING FUDGE -- without it the ring visibly un-fills once per
	// second. `base.Update` ticks the timers AFTER this class tests `Finished`, so between the
	// tick that rings the repeating timer and the tick that acts on it, `TimeLeft` has already
	// wrapped back to ~1000 while `countdown` is still the old value -- and Draw runs in that
	// window, reading a full second too much. It also ate the climax: the last drawn frame before
	// the ship arrived read fill 0.9 with no flare.
	private float RemainingMs
	{
		get
		{
			if (cosmetic)
			{
				return cosmetictimer.TimeLeft;
			}
			float pending = countdowntimer.Finished ? 1000f : 0f;
			return MathHelper.Max((float)(countdown - 1) * 1000f + countdowntimer.TimeLeft - pending, 0f);
		}
	}

	// THE ONE PARK SEAM, and everything the indicator looks like is derived from it: the fill, the
	// pulse, the pop, the numeral and its punch. ?respawnphase=<0..1> parks it for a screenshot
	// (the ?ripplephase= convention) -- a 10 s ring that has to be caught mid-fill is exactly what
	// a timed live screenshot cannot verify.
	//
	// Card 045c5a92 inverted the derivation -- the park used to sit on FillFraction, and the ms
	// clock ran on underneath it. That was invisible while the fill was the only thing drawn, but a
	// numeral read off the raw RemainingMs would have been UN-PARKABLE: ?harness=respawn freezes
	// Update, so the raw clock sits at its start value forever and every parked phase would have
	// shown the same digit. Parking the ms instead parks all five for free. The live path is
	// arithmetically unchanged -- FillFraction below is still exactly 1 - RemainingMs/totalMs --
	// and that is asserted rather than asserted-about: see respawn_digit.txt's fill column.
	private float ShownRemainingMs
	{
		get
		{
			float? parked = DebugFlags.RespawnPhase;
			if (parked.HasValue && parked.Value >= 0f)
			{
				return (1f - MathHelper.Clamp(parked.Value, 0f, 1f)) * totalMs;
			}
			return RemainingMs;
		}
	}

	// 0 = just died, 1 = about to pop.
	private float FillFraction => MathHelper.Clamp(1f - ShownRemainingMs / totalMs, 0f, 1f);

	// The numeral: WHOLE seconds left, never a fraction -- ruled by the owner ("we dont need
	// fractions of seconds there"), so the mock's decorative "2.1" is not what ships. ceil, so the
	// last second reads 1 rather than 0, and floored at 1 so a phase parked at exactly 1.0 (or the
	// final sub-millisecond of a real clock) does not flash a 0 nobody would ever see in play.
	private int ShownSeconds => Math.Max(1, (int)Math.Ceiling(ShownRemainingMs / 1000f));

	// The punch, 1 at the instant the numeral changes and 0 once PunchMs has passed. A PURE
	// FUNCTION of the same clock -- no per-instance accumulator, which matters twice over: these
	// objects are POOL-RECYCLED (so there is one less field that a missed Initialize could leak
	// across lives, the exact bug card d8344c17 found in EvilSkull), and being stateless it parks
	// with ?respawnphase= like everything else, so the animation has a screenshot rig for free.
	//
	// The coupling to the CHANGE is by construction, not by proximity: the numeral changes exactly
	// where ShownRemainingMs crosses a whole second, which is exactly where this reads 0 elapsed.
	// An edit that rounds the numeral differently would silently decouple the two.
	private float DigitPunch
	{
		get
		{
			float remain = ShownRemainingMs;
			float sinceChangeMs = 1000f - (remain - (float)Math.Floor(remain / 1000f) * 1000f);
			if (sinceChangeMs >= 1000f)
			{
				sinceChangeMs = 0f;     // exactly ON the boundary: the change is this instant
			}
			if (sinceChangeMs >= PunchMs)
			{
				return 0f;
			}
			// Snappy: full punch on the change, and most of it gone within the first third.
			float t = sinceChangeMs / PunchMs;
			return (1f - t) * (1f - t);
		}
	}

	// How far into the closing flare we are (0 = not popping yet, 1 = gone). Derived from the
	// fill, so parking the phase parks the pop too.
	private float PopFraction
	{
		get
		{
			float popStartFill = MathHelper.Clamp(1f - PopMs / totalMs, 0f, 1f);
			if (popStartFill >= 1f)
			{
				return 0f;
			}
			float fill = FillFraction;
			if (fill <= popStartFill)
			{
				return 0f;
			}
			return MathHelper.Clamp((fill - popStartFill) / (1f - popStartFill), 0f, 1f);
		}
	}

	// The blink/pulsate brightness near full, 0..1. On WorldTime (the shared Draw-time clock), so
	// it freezes with the world under a pause or a hit-stop instead of strobing on behind the
	// pause menu -- the rule in Compat/WorldTime.cs.
	private float PulseAmount
	{
		get
		{
			float fill = FillFraction;
			if (fill <= PulseStartFill)
			{
				return 0f;
			}
			float ramp = MathHelper.Clamp((fill - PulseStartFill) / (1f - PulseStartFill), 0f, 1f);
			float hz = MathHelper.Lerp(PulseHzStart, PulseHzEnd, ramp);
			return 0.5f + 0.5f * (float)Math.Sin(WorldTime.Seconds * hz * (float)Math.PI * 2f);
		}
	}

	// A one-line dump of everything the ring's look is derived from -- eaRespawn.state() /
	// `eval RespawnState`. The pulse is a moving value, so it is verified as DATA across steps
	// rather than by two screenshots of a frozen frame, which would pass on a build that had
	// stopped drawing the ring entirely.
	internal string DebugStateLine()
	{
		return "[respawn] fill=" + FillFraction.ToString("0.000")
			+ " pulse=" + PulseAmount.ToString("0.000")
			+ " pop=" + PopFraction.ToString("0.000")
			// The numeral and its punch are DATA for the same reason the pulse is: both are
			// time-varying, and a screenshot pair that matches also passes on a build that has
			// stopped drawing them. `secs` additionally proves the digit tracks the PARKED clock.
			+ " secs=" + ShownSeconds
			+ " punch=" + DigitPunch.ToString("0.000")
			+ " remainMs=" + (int)RemainingMs
			+ " totalMs=" + (int)totalMs
			+ " slot=" + player
			+ " wiped=" + WorldIsWiped
			+ (cosmetic ? " cosmetic" : " local");
	}

	// The SECOND wipe shape, and it needs a DRAW-time guard rather than a spawn-time one.
	// PlayerShip_OnDeath's ShouldSummon check catches the death that wipes the world by itself,
	// but not a co-op wipe where the deaths land in sequence: the first ship dies while its
	// partner is still flying, so the summon is correctly raised, and then the partner dies too
	// (TeamChallenge's tether does exactly this, in the SAME tick). GameScene.LoseLife purges it
	// -- a tick late, because both the death and the purge are queued -- so it draws once anyway,
	// which is the flash the card is about wearing a different hat.
	//
	// THE DISCRIMINANT IS "IS A LEVEL UP", NOT "IS THE ROSTER EMPTY", and the difference was
	// caught by the Chrome pass rather than by any headless run. A wipe is a GameScene concept --
	// it is LoseLife that purges -- and outside a level there is nothing to wipe. Gating on
	// `oracle.Players > 0` instead LOOKED equivalent and was not: the sprite harness leaves a
	// seated slot behind on the WASM boot path (and not on eahl's), so the ring vanished in the
	// browser and drew headlessly, with `[respawn] draw suppressed` as the only sign.
	// `GameScene.NetActiveScene` is the repo's single source of truth for "is a scene up".
	private bool WorldIsWiped
	{
		get
		{
			if (cosmetic || GameScene.NetActiveScene == null)
			{
				return false;
			}
			foreach (PlayerShip s in oracle.GetShips())
			{
				if (!s.IsDead)
				{
					return false;
				}
			}
			return true;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		if (WorldIsWiped)
		{
			// Reported ONCE per summon, and derived from what this Draw actually did rather than
			// restated beside it (the [xfade] seal idiom). It is the only observable: the summon
			// is purged within the same tick, so nothing afterwards can be asked whether it drew
			// -- and "it did not draw" is precisely what has to be proven.
			//
			// NOT reachable from eahl's `eval KillShip*` path, and that is a property of the RIG,
			// not of the guard: a scripted death lands BETWEEN frames, so the tick the ship dies
			// on and the tick LoseLife purges on coalesce and the summon never reaches a Draw. In
			// play the death lands mid-tick (the collision phase) and it does. The predicate
			// itself is observable either way -- DebugStateLine prints `wiped=`, and it reads
			// True at exactly that moment.
			if (!wipeReported)
			{
				wipeReported = true;
				Console.WriteLine("[respawn] draw suppressed slot=" + player + " (world wiped)");
			}
			return;
		}
		float fill = FillFraction;
		float pop = PopFraction;
		float pulse = PulseAmount;

		// The flare: the whole ring grows and fades out over the last PopMs.
		float radius = RingRadius * (1f + PopRadiusGrowth * pop);
		float popAlpha = 1f - pop;

		float step = (float)Math.PI * 2f / (float)RingSegments;
		int litCount = (int)Math.Round(fill * (float)RingSegments);

		// Magenta throughout, brightening with the pulse. Straight (non-premultiplied) alpha, per
		// the project-wide rule in the root CLAUDE.md.
		Color rim = new Color(0.55f, 0.13f, 0.52f, 0.62f * popAlpha);
		// THE CORE'S ALPHA CARRIES THE POP FADE AND NOTHING ELSE -- the pulse brightens it through
		// RGB instead. Two neighbouring quads must overlap or the ring seams, and blending two
		// translucent quads is NOT idempotent (0.72 over 0.72 reads 0.92), so any alpha below 1
		// draws a bright rib at every seam. At alpha 1 the overlap is exactly idempotent, which is
		// what makes the sweep read as one clean stroke for all but the 220 ms of the pop.
		Color arcCore = new Color(1f, 0.48f + 0.34f * pulse, 0.92f + 0.08f * pulse, popAlpha);

		// 1. The ambient halo the whole widget sits in, and the near-black disc over it. The disc
		//    is 48 wedge quads rather than a round texture -- a 48-gon at this radius is off a true
		//    circle by 0.08 px -- so it needs no new asset and no alpha-tested sprite.
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		DrawGlow(base.Position, new Vector2(radius * 3.2f, radius * 3.2f),
			new Color(0.85f, 0.15f, 0.75f, (0.20f + 0.10f * pulse) * popAlpha), 0f);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		DrawDisc(radius * DiscRadiusFactor, RingSegments, new Color(0.030f, 0.012f, 0.048f, 0.95f * popAlpha));

		// 2. The rim, all the way round: the clock FACE, so the arc reads as sweeping a dial rather
		//    than growing out of nowhere.
		//
		//    SegOverlap is only just over 1: neighbouring quads must MEET, and no more. The gold
		//    ring this replaced overlapped them by 1.9, which is invisible in one flat pass but
		//    scallops badly once the same arc is drawn as two ADDITIVE layers -- every overlap
		//    double-brightens, so the arc grew a row of bright ribs down it.
		Vector2 rimScale = SegScale(radius, RimThickness, step, SegOverlap);
		for (int i = 0; i < RingSegments; i++)
		{
			DrawSegment(i, step, radius, rimScale, rim);
		}

		// 3. The filled arc: a wide dim halo under a narrow bright core, which is what reads as a
		//    GLOW without a shader or a second texture.
		//
		//    THE CORE IS ALPHA-BLENDED AND ONLY THE HALO IS ADDITIVE, and that split is the whole
		//    reason the arc looks smooth. Two neighbouring quads must overlap or the ring has
		//    seams, and an ADDITIVE overlap double-brightens -- which drew a bright rib every 4.5
		//    px down an arc that is supposed to be one clean sweep. Blending the core instead makes
		//    the overlap idempotent (0.95 over 0.95 is 0.95), and the additive halo keeps the glow
		//    at an alpha low enough that its own ribs are invisible.
		// THERE IS NO PER-SEGMENT GLOW PASS, and that is a fix rather than an omission. A second,
		// wider ADDITIVE arc was the obvious way to bloom the stroke, and it cannot be made clean:
		// consecutive quads must overlap somewhere (a tangential quad spans its `step` of arc at
		// exactly one radius and too much or too little at every other), and an additive overlap
		// always double-brightens. Measured, that drew a 70-126/255 hatch across the whole 39-45 px
		// fringe while the stroke itself was flat to 1/255. The bloom instead comes from the
		// ambient lazerglow above -- a radial texture, so it has no seams at all -- plus the
		// engine's own bloom pass over an opaque, saturated stroke.
		Vector2 coreScale = SegScale(radius, ArcThickness, step, SegOverlap);
		for (int j = 0; j < litCount && j < RingSegments; j++)
		{
			DrawSegment(j, step, radius, coreScale, arcCore);
		}
		// Rounded caps at both ends of the arc, the mock's most distinctive detail. A small disc
		// of the arc's own half-thickness, which is exactly what a round line cap is.
		if (litCount > 0)
		{
			DrawCap(0f, radius, arcCore);
			DrawCap((float)litCount * step, radius, arcCore);
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;

		// 4. The spikes, breathing with the pulse. Each is a stack of `blank` quads whose width
		//    shrinks toward the tip -- a stepped needle. A single squashed lazerglow was tried
		//    first and is the wrong primitive here: that texture concentrates its energy in a small
		//    core, so at a 15 px length the visible part was a 3 px dot with no point on it.
		float spikeLen = SpikeLength * (0.85f + 0.15f * pulse) * (1f + PopRadiusGrowth * pop);
		Color spike = new Color(1f, 0.45f, 0.95f, (0.55f + 0.35f * pulse) * popAlpha);
		float spikeStep = (float)Math.PI * 2f / (float)SpikeCount;
		for (int s = 0; s < SpikeCount; s++)
		{
			float angle = -(float)Math.PI / 2f + (float)s * spikeStep;
			Vector2 dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
			float seg = spikeLen / (float)SpikeTaperSteps;
			for (int k = 0; k < SpikeTaperSteps; k++)
			{
				// Width tapers to a point; each piece sits one `seg` further out than the last.
				float w = SpikeWidth * (1f - (float)k / (float)SpikeTaperSteps);
				Vector2 at = base.Position + dir * (radius + seg * ((float)k + 0.5f));
				Vector2 sc = new Vector2(seg * 1.15f, w) / (float)texture.LogicalWidth();
				spriteBatch.Draw(texture, at, angle, sc, center: true, spike);
			}
		}

		// 5. The numeral and the label. Drawn last so they sit over the disc.
		DrawCountdownText(popAlpha, pulse);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	// One arc segment: a thin quad laid tangentially on the ring. Index 0 is 12 o'clock and the
	// fill runs clockwise.
	private void DrawSegment(int index, float step, float radius, Vector2 scale, Color color)
	{
		float angle = -(float)Math.PI / 2f + ((float)index + 0.5f) * step;
		Vector2 at = base.Position + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
		spriteBatch.Draw(texture, at, angle + (float)Math.PI / 2f, scale, center: true, color);
	}

	// The scale of one arc-segment quad. Its length is measured at the stroke's OUTER radius, not
	// at its centre-line, and that is the whole trick: a quad laid TANGENTIALLY spans `step` of arc
	// only where it touches the centre-line, so sizing it there leaves a wedge gap of
	// (thickness/2 * step) between consecutive quads at the outer edge -- 1.2 px for the arc's
	// halo, which is exactly the sawtooth that ran down the arc's outer edge. Sized from the outer
	// radius the outer corners meet and the surplus overlaps INWARD, under the opaque core.
	private Vector2 SegScale(float radius, float thickness, float step, float overlap)
	{
		return new Vector2((radius + thickness * 0.5f) * step * overlap, thickness)
			/ (float)texture.LogicalWidth();
	}

	// A filled circle as a fan of wedge quads sharing the centre -- the same `blank` quad and the
	// same wrapper overload the ring uses, so the LogicalBounds() clamp still applies.
	private void DrawDisc(float radius, int segments, Color color)
	{
		DrawDiscAt(base.Position, radius, segments, color);
	}

	private void DrawDiscAt(Vector2 centre, float radius, int segments, Color color)
	{
		float step = (float)Math.PI * 2f / (float)segments;
		// The chord across one wedge, widened so neighbours overlap and the fan has no seams.
		float chord = 2f * radius * (float)Math.Tan(step / 2f) * 1.15f;
		Vector2 scale = new Vector2(chord, radius) / (float)texture.LogicalWidth();
		for (int i = 0; i < segments; i++)
		{
			float angle = -(float)Math.PI / 2f + ((float)i + 0.5f) * step;
			Vector2 at = centre + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (radius * 0.5f);
			spriteBatch.Draw(texture, at, angle + (float)Math.PI / 2f, scale, center: true, color);
		}
	}

	// A round line cap: a small disc centred on the rim at `angle` from 12 o'clock.
	private void DrawCap(float angle, float radius, Color color)
	{
		float a = -(float)Math.PI / 2f + angle;
		Vector2 at = base.Position + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;
		DrawDiscAt(at, ArcThickness * 0.5f, 10, color);
	}

	// One lazerglow quad, optionally squashed to an ellipse and rotated -- the soft halo primitive
	// for the spikes, the ambient glow and the glow behind the text. `size` is the drawn extent in
	// design px, so callers think in px rather than in texture-scale factors.
	private void DrawGlow(Vector2 at, Vector2 size, Color color, float rotation)
	{
		if (glowTex == null)
		{
			return;
		}
		Vector2 scale = size / (float)glowTex.LogicalWidth();
		spriteBatch.Draw(glowTex, at, rotation, scale, center: true, color);
	}

	// The centre numeral + the italic "RESPAWNING!" under the disc.
	//
	// The label's ITALIC is a real shear, not a fake: SpriteBatchWrapper.BeginPerspective takes an
	// arbitrary design-space matrix (it exists for the credits crawl's projective one) and a shear
	// is just an affine member of that family, so the glyph quads skew on the GPU. There is no
	// italic face in the atlas and SpriteBatch cannot skew a single quad, so this is the only route.
	private void DrawCountdownText(float popAlpha, float pulse)
	{
		if (font == null)
		{
			return;
		}
		string digits = ShownSeconds.ToString();
		float punch = DigitPunch;

		// The numeral. Punch scales it about its centre and flashes it brighter for the same
		// PunchMs, so the change reads as a hit rather than as a crossfade.
		//
		// FITTED, not fixed. A respawn opens at whatever its duration is -- ten seconds on the
		// default difficulty -- so the FIRST frame carries two glyphs AND a full punch, which is
		// simultaneously the widest and the largest the numeral ever gets. DigitScale is the
		// one-glyph size; anything wider is scaled down to the box so a two-digit clock cannot
		// spill over the rim, and the box is pre-divided by the punch so the peak fits too.
		Vector2 measured = font.MeasureString(digits);
		float boxW = RingRadius * DiscRadiusFactor * 1.45f / (1f + PunchScale);
		float digitScale = DigitScale;
		if (measured.X > 0f && measured.X * digitScale > boxW)
		{
			digitScale = boxW / measured.X;
		}
		digitScale *= 1f + PunchScale * punch;
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		DrawGlow(base.Position, new Vector2(RingRadius * (1.5f + 0.4f * punch), RingRadius * (1.5f + 0.4f * punch)),
			new Color(1f, 0.30f, 0.90f, (0.30f + 0.35f * punch) * popAlpha), 0f);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		spriteBatch.DrawString(digits, base.Position, new Color(1f, 0.93f + 0.07f * punch, 1f, popAlpha),
			0f, centered: true, digitScale, (SpriteEffects)0, 0f);

		// The label, sheared. Lay the text out unscaled and centred on `anchor`, then let the
		// matrix scale + shear the whole block about that same anchor.
		Vector2 anchor = base.Position + new Vector2(0f, LabelOffsetY);
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		DrawGlow(anchor, new Vector2(150f, 34f), new Color(0.95f, 0.15f, 0.85f, 0.24f * popAlpha), 0f);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		Vector2 size = font.MeasureString(LabelText);
		// Row-vector convention: x' = x + y*M21, so a NEGATIVE M21 leans the top (smaller y) right.
		Matrix shear = new Matrix(1f, 0f, 0f, 0f, 0f - LabelItalic, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);
		Matrix design = Matrix.CreateTranslation(0f - anchor.X, 0f - anchor.Y, 0f)
			* shear
			* Matrix.CreateScale(LabelScale, LabelScale, 1f)
			* Matrix.CreateTranslation(anchor.X, anchor.Y, 0f);
		spriteBatch.BeginPerspective(design);
		spriteBatch.DrawStringPerspective(font, LabelText, anchor - size * 0.5f,
			new Color(1f, 0.42f + 0.2f * pulse, 0.95f, popAlpha));
		spriteBatch.EndPerspective();
	}

	// The pop itself: a free level-4 bomb at the respawn point. Deliberately NOT doBlast() -- no
	// bomb is spent, the level is fixed rather than the player's own progression, and no EvBlast
	// is sent. In a session the far peer's own cosmetic summon drops its copy off its own
	// EvRespawn announcement (the EvIntroVolley idiom), which keeps the two worlds symmetric
	// without racing the puppet's arrival: EvBlast's receiver needs a live ship in that slot, and
	// at a respawn the peer's puppet may not have been born yet.
	private void SpawnRewardBlast()
	{
		Blast rewardBlast = Blast.NewBlast(collection, base.Game);
		rewardBlast.Setup(base.Position, RewardBlastLevel, player);
		collection.Add((GameComponent)(object)rewardBlast);
		sound.PlayCue("blast");
		// Reported from the call site with the value actually passed, so this witnesses the WIRING
		// and not just the constant (card 258afd66). The blast's own lifetime is the independent
		// second witness -- Blast.Setup makes it 1000ms * (level+1), which BombRipple resolves the
		// ring's duration from, so `eval RippleState` reads 4.00 at level 3 and 5.00 at level 4.
		Console.WriteLine("[respawn] reward blast slot=" + player + " level=" + RewardBlastLevel);
	}

	public override void Update(GameTime gameTime)
	{
		if (cosmetic)
		{
			if (cosmetictimer.Finished)
			{
				SpawnRewardBlast();
				Die();
			}
			base.Update(gameTime);
			return;
		}
		if (countdowntimer.Finished)
		{
			bool flag = true;
			PlayerIndex playerIndex;
			switch (oracle.Controller(player))
			{
			case ControlDevice.PadOne:
				playerIndex = (PlayerIndex)0;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadTwo:
				playerIndex = (PlayerIndex)1;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadThree:
				playerIndex = (PlayerIndex)2;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadFour:
				playerIndex = (PlayerIndex)3;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			default:
				playerIndex = (PlayerIndex)0;
				flag = false;
				break;
			}
			countdown--;
			if (countdown <= 3 && countdown != 0 && flag)
			{
				float num = MathHelper.Lerp(0.35f, 0.35f, (float)countdown / 3f);
				vibrator.addVibration(new Vector2(0f, num), 500f, playerIndex);
			}
			if (countdown <= 0)
			{
				PlayerShip playerShip = collection.Recycle<PlayerShip>();
				if (playerShip == null)
				{
					playerShip = new PlayerShip(base.Game);
				}
				playerShip.Setup(player, base.Position, startup: false, invulnerable: true, spawndirection);
				collection.Add((GameComponent)(object)playerShip);
				SpawnRewardBlast();
				Die();
				if (flag)
				{
					vibrator.addVibration(new Vector2(0.35f, 0.5f), 1500f, playerIndex);
				}
			}
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	// ---- the SUPPRESSION half of card 37f3a663 -------------------------------------------
	//
	// Whether a dying ship should raise a respawn summon at all. The answer is "only if somebody
	// else is still flying", because otherwise the death is a WIPE: GameScene.UpdateNormal sees
	// oracle.AllShipsDead on the NEXT tick and LoseLife purges the summon again. That purge is a
	// tick late (the ship's removal is queued, so AllShipsDead is still false on the tick it
	// died), which is exactly the "animation appears for 1 frame when the player dies" the card
	// reports -- every single-player death, and every co-op death where the last two ships go in
	// the same tick.
	//
	// `otherLiveShips` counts player ships OTHER than the dying one that are not themselves dead
	// -- IsDead, not list membership, because a same-tick double death leaves both ships in the
	// oracle's list and a membership count would raise two doomed summons.
	internal static bool ShouldSummon(int otherLiveShips)
	{
		return otherLiveShips > 0;
	}
}
