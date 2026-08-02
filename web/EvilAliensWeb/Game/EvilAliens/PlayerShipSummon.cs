using System;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The "you are coming back" indicator that stands in for a dead player until their ship
// respawns. Card 37f3a663 replaced the original XBLIG look -- a LazerGenerator charge orb plus a
// DarkGoldenrod integer countdown -- with a clock ring that fills, pulses as it nears full and
// pops as the ship arrives, dropping a level-4 bomb as a reward for sticking with it.
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
// The ring is drawn as RingSegments thin rotated quads of GFX/Game/blank, a 10x10 OPAQUE WHITE
// texture this class already loaded (and never drew) as its own animation. The draws go through
// the SpriteBatchWrapper overloads, which clamp the source rect to LogicalBounds() -- a raw
// SpriteBatch.Draw would stretch the padding on a --padtest build, which is the SealAlpha trap
// (card b7e9b106).
internal class PlayerShipSummon : AlienDrawableGameComponent
{
	// Ring geometry, in 800x600 design px.
	private const int RingSegments = 48;
	private const float RingRadius = 26f;
	private const float RingThickness = 5f;

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

	// The reward for sticking with it. A fixed level 4 -- it is a gift, not the player's own bomb
	// progression, and no bomb is spent for it.
	private const int RewardBlastLevel = 4;

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

	public override void Initialize()
	{
		base.Initialize();
	}

	// Milliseconds left on the clock. The real mode derives it from the existing 1 Hz countdown
	// rather than running a second clock of its own, so the tick the ship actually arrives on is
	// unchanged and only the DRAWING moved.
	private float RemainingMs => cosmetic
		? cosmetictimer.TimeLeft
		: (float)(countdown - 1) * 1000f + countdowntimer.TimeLeft;

	// 0 = just died, 1 = about to pop. ?respawnphase=<0..1> parks it for a screenshot (the
	// ?ripplephase= convention) -- a 10 s ring that has to be caught mid-fill is exactly what a
	// timed live screenshot cannot verify.
	private float FillFraction
	{
		get
		{
			float? parked = DebugFlags.RespawnPhase;
			if (parked.HasValue && parked.Value >= 0f)
			{
				return MathHelper.Clamp(parked.Value, 0f, 1f);
			}
			return MathHelper.Clamp(1f - RemainingMs / totalMs, 0f, 1f);
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

		// Filled segments brighten with the pulse; the unfilled remainder stays a dim track, so
		// the ring reads as a clock FACE rather than an arc growing out of nowhere.
		float litAlpha = (0.75f + 0.25f * pulse) * popAlpha;
		float trackAlpha = 0.16f * popAlpha;
		Color lit = new Color(1f, 0.86f + 0.14f * pulse, 0.35f + 0.5f * pulse, litAlpha);
		Color track = new Color(1f, 1f, 1f, trackAlpha);

		float step = (float)Math.PI * 2f / (float)RingSegments;
		// Slightly longer than the exact arc step so neighbouring quads overlap and the ring has
		// no gaps at this radius.
		float segLength = radius * step * 1.9f;
		Vector2 segScale = new Vector2(segLength, RingThickness) / (float)texture.LogicalWidth();
		int litCount = (int)Math.Round(fill * (float)RingSegments);

		spriteBatch.BlendMode = (SpriteBlendMode)1;
		for (int i = litCount; i < RingSegments; i++)
		{
			DrawSegment(i, step, radius, segScale, track);
		}
		// Additive for the filled arc so it glows against whatever is scrolling underneath, the
		// way the rest of this game's energy FX read. Straight (non-premultiplied) alpha
		// throughout -- see the project-wide rule in the root CLAUDE.md.
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		for (int j = 0; j < litCount && j < RingSegments; j++)
		{
			DrawSegment(j, step, radius, segScale, lit);
		}
		// The leading edge, brighter and longer, so the clock has a visible hand.
		if (litCount > 0 && litCount < RingSegments)
		{
			DrawSegment(litCount - 1, step, radius, segScale * new Vector2(1f, 1.9f),
				new Color(1f, 1f, 1f, popAlpha));
		}
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
