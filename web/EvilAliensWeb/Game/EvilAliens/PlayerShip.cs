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

	// Below this the smoothed steer is noise, not a decision (the pre-existing 0.2 deadzone).
	private const float SteerDeadzone = 0.2f;

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

	// How far ahead a moving threat is projected when judging it. Radial "how far is it right
	// now" repulsion pushes the ship ALONG the path of anything crossing the screen -- which is
	// exactly the spider boss's screen-wide sweep. Steering by closest approach instead moves the
	// ship off the line before it arrives.
	public const float DefaultThreatLeadMs = 700f;

	// A level-halting boss competes at this fraction of its true distance when the AI picks a
	// target, so it outranks the trash the boss itself keeps spawning.
	public const float DefaultPriorityTargetBias = 0.45f;

	private static float SteerSmoothMs => EvilAliensWeb.Compat.DebugFlags.AiSteerSmoothMs ?? DefaultSteerSmoothMs;

	private static float WallReactionMs => EvilAliensWeb.Compat.DebugFlags.AiWallReactionMs ?? DefaultWallReactionMs;

	private static float GapSwitchMargin => EvilAliensWeb.Compat.DebugFlags.AiGapSwitchMargin ?? DefaultGapSwitchMargin;

	private static float ThreatLeadMs => EvilAliensWeb.Compat.DebugFlags.AiThreatLeadMs ?? DefaultThreatLeadMs;

	private static float PriorityTargetBias => EvilAliensWeb.Compat.DebugFlags.AiPriorityBias ?? DefaultPriorityTargetBias;

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

	// Below this (px/ms) a threat is not a "mover" and the plain radial repulsion models it
	// better. The player ship's own MaxSpeed is 0.33 px/ms, so this is about a third of that.
	private const float ThreatMinSpeed = 0.1f;

	// Clearance the AI wants past the threat's own half-extent when judging a predicted miss.
	private const float ThreatMissMargin = 90f;

	// Even a far-off but dead-on collision course deserves some steer, or the AI would ignore
	// everything until it was nearly too late.
	private const float ThreatUrgencyFloor = 0.35f;

	// An impact this close, this centred, gets a steer strong enough to beat every other term.
	private const float ThreatPanicMs = 260f;

	private const float ThreatPanicMissFraction = 0.55f;

	private const float ThreatPanicStrength = 16f;

	// Wall steering weights. These sit well above the generic steer terms (maxSteerStrength 4)
	// on purpose: inside a wall the gap is the only survivable place to be, and a stray powerup
	// pull must not drift the ship out of the slot it is threading.
	private const float WallLateralIdle = 3f;

	private const float WallLateralUrgent = 14f;

	// Downward hold-off while a blocked row closes and the ship is still off its gap -- buying
	// the time the lateral move needs. Positive Y is down (screen coords).
	private const float WallBackOff = 6f;

	// Rows of grid looked at when judging a column. Four rows is 267..1067px of wall depending
	// on grid width -- past that the wall has usually scrolled into a different shape anyway.
	private const int WallScanRows = 4;

	// Cost added per blocked column the ship would have to cross to reach a gap.
	private const float WallCrossPenalty = 4f;

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

	private float aiGapCost;

	public int Owner => player;

	public ControlDevice Controller => controller;

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
		PlayerShipSummon playerShipSummon = PlayerShipSummon.NewPlayerShipSummon(collection, base.Game);
		playerShipSummon.Setup(player, startdir, base.Position, respawntimebonus);
		collection.Add((GameComponent)(object)playerShipSummon);
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
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * (float)gameTime.TotalGameTime.TotalSeconds % 360f);
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
		isTutorial = false;
		respawntimebonus = 0;
		readyToConnect = false;
		haspower = false;
		Score.ResetPowerup(player);
		invulnerabilityTimer.Reset();
		shoottimer.Duration = 1000f / (float)shotspersec;
		base.MaxSpeed = 0.33f;
		base.Deceleration = 0.0047999998f;
		base.Acceleration = 0.003f;
		CollisionBox collisionBox = retrieveBoundsFromTexture();
		TopLeft = collisionBox.TopLeft;
		BottomRight = collisionBox.BottomRight;
		starttimer.Reset();
		shoottimer.Reset();
		shoottimer.Stop();
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
						float direction2 = MyMath.VectorToAngle(input.MousePosition - base.Position);
						FireAt(direction2);
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
					// interpolation buffer (~100ms behind), shots are re-fired locally from the
					// replicated firing state; direction stays Zero so the Move below is a no-op.
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

	// Last tick this ship INTENDED to fire (FireAt is called every tick while the trigger is
	// held, its internal shoottimer does the cadence gating) and the aim it fired along --
	// exactly the "fire state" the ship stream carries.
	internal long NetLastFireMs { get; private set; }

	internal float NetLastFireAim { get; private set; }

	internal Vector2 NetVelocity => SpeedVector;

	internal int NetShotsPerSec => shotspersec;

	internal float NetBulletLife => bulletlifetime;

	// Applied every tick to a ControlDevice.Remote puppet: interpolated position (speed
	// zeroed -- the buffer is the sole motion source), replicated fire loadout, and shots
	// re-fired through the real FireAt path so remote bullets are built like local ones.
	internal void NetApplyRemoteState(Vector2 pos, float aim, bool firing, int shotsPerSec, float bulletLife)
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
		if (firing)
		{
			FireAt(aim);
		}
		else if (shoottimer.Finished)
		{
			shoottimer.Stop();
			shoottimer.Reset();
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

	private void DoAIFire(GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		float aimSpread = (float)Math.PI / 12f;
		// Squared while the loop scans (it is compared against LengthSquared); the Math.Sqrt
		// after the loop turns it into a real distance for the range test.
		float nearestDist = float.MaxValue;
		AlienDrawableGameComponent alienDrawableGameComponent = null;
		// A level-halting boss is worth reaching past a lot of trash, so it competes on a
		// DISCOUNTED distance rather than by raw proximity. Scored in the same squared space the
		// loop compares in, hence the squared factor.
		float priorityBiasSq = PriorityTargetBias * PriorityTargetBias;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (IsAiShootable(baddy))
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
				if (scoreSq < nearestDist && baddy.Position.X > 0f && baddy.Position.X < 800f && baddy.Position.Y > 0f && baddy.Position.Y < 600f)
				{
					nearestDist = scoreSq;
					alienDrawableGameComponent = baddy;
				}
			}
		}
		// Undo the bias before the range test: the discount decides WHICH target wins, never
		// whether a bullet can actually reach it.
		if (alienDrawableGameComponent != null)
		{
			Vector2 toChosen = alienDrawableGameComponent.Position - base.Position;
			nearestDist = (toChosen).Length();
		}
		else
		{
			nearestDist = float.MaxValue;
		}
		bool fired = false;
		if (nearestDist <= bulletlifetime * 0.78f)
		{
			fired = true;
			if (alienDrawableGameComponent is JunkBoss)
			{
				FireAt(MyMath.VectorToAngle(alienDrawableGameComponent.Position - base.Position));
			}
			else
			{
				FireAt(MyMath.VectorToAngle(alienDrawableGameComponent.Position - base.Position) + RandomHelper.RandomNextFloat(0f - aimSpread, aimSpread));
			}
		}
		// AI bench (card f4d1721f): "there was something on screen I could have killed and I did
		// not shoot" is the signature of a target the AI cannot see -- the shape of the Level 3
		// stall, where the boss that gates the level was never in the list above.
		EvilAliensWeb.Compat.AiBench.NoteFireDecision(this, alienDrawableGameComponent != null, fired);
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
		Vector2 position = default(Vector2);
		(position) = new Vector2(float.MaxValue, float.MaxValue);
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
				Vector2 toTarget = position - base.Position;
				if (distSq < (toTarget).LengthSquared())
				{
					position = baddy.Position;
				}
				continue;
			}
			if (baddy is JunkBoss)
			{
				position = baddy.Position;
			}
			// Card f4d1721f: track the nearest level-HALTING boss so the ship can close on it if
			// it is out of gun range (below). The 2008 code only ever did this for JunkBoss, so
			// against any other boss the AI hovered at its default station and fired only when the
			// boss happened to drift within range -- measured as 55% of ticks with a shootable
			// target and no shot fired, against a BrainBoss parked at the top of the screen.
			if (IsAiPriorityTarget(baddy) && IsAiShootable(baddy))
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
				if (d <= steerRange)
				{
					float strength = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, d / steerRange);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, d / steerRange);
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
				// This ADDS to the distance-based repulsion below rather than replacing it: the
				// prediction is only as good as the assumption that both keep their present
				// course, and letting it suppress the proximity term entirely means one wrong
				// prediction leaves the ship with no avoidance at all.
				EvadeMovingThreat(ref direction, baddy, dodgeAngle, minSteerStrength, maxSteerStrength);
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
				if (dist <= steerRange)
				{
					float strength = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, dist / steerRange);
					if (altSteering)
					{
						strength = MathHelper.Lerp(maxSteerStrength, minSteerStrength, dist / steerRange);
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
			Vector2 toTarget = position - base.Position;
			if (distToPowerup < (toTarget).Length())
			{
				position = powerup.Position;
			}
			Vector2 toPowerup2 = powerup.Position - base.Position;
			if ((toPowerup2).Length() <= steerRange)
			{
				Vector2 toPowerup3 = powerup.Position - base.Position;
				float pull = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, (toPowerup3).Length() / steerRange);
				if (altSteering)
				{
					Vector2 toPowerup4 = powerup.Position - base.Position;
					pull = MathHelper.Lerp(maxSteerStrength, minSteerStrength, (toPowerup4).Length() / steerRange);
				}
				direction += pull * MyMath.AngleToVector(MyMath.VectorToAngle(powerup.Position - base.Position));
			}
		}
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
			if (bossDist > standoff && bossDist > 0.001f)
			{
				position = haltingBoss.Position + (fromBoss / bossDist) * standoff;
			}
		}
		foreach (PlayerShip ship2 in oracle.GetShips())
		{
			if (ship2.readyToConnect && ship2 != this && readyToConnect && !isConnectedWith(ship2))
			{
				position = ship2.Position;
			}
		}
		if (position.X > 2000f && !collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				if (collection.ContainsType<Wall>())
				{
					(position) = new Vector2(400f, 300f);
				}
				else
				{
					(position) = new Vector2(400f, 400f);
				}
			}
			// Spread by the player's ORDINAL among seated slots, not `player + 1`: online co-op's
			// roster is sparse (card 4d904410), and a high slot would otherwise respawn off-screen.
			else if (collection.ContainsType<Wall>())
			{
				float spacing = 800 / (oracle.Players + 1);
				(position) = new Vector2((float)oracle.SeatOrdinal(player) * spacing, 300f);
			}
			else
			{
				float spacing = 800 / (oracle.Players + 1);
				(position) = new Vector2((float)oracle.SeatOrdinal(player) * spacing, 400f);
			}
		}
		if (position.X > 2000f && collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				(position) = new Vector2(266f, 300f);
			}
			else
			{
				(position) = new Vector2(266f, 600f / (float)(oracle.Players + 1) * (float)oracle.SeatOrdinal(player));
			}
		}
		if (position.X < 2000f)
		{
			delta = base.Position - position;
			float distToTarget = (delta).Length();
			if (distToTarget > 10f)
			{
				direction += 0.8f * MyMath.AngleToVector(MyMath.VectorToAngle(position - base.Position));
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
		float smoothMs = SteerSmoothMs;
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
		if (hasWall)
		{
			ClampIntoWallSpace(ref direction, collisionLevelMap);
			// ...and the override is REMEMBERED. Leaving aiSteer untouched here means the very
			// next tick blends back toward the pre-clamp heading, so a probe that flickers clear
			// snaps the ship straight back at the wall -- the clamp becomes its own oscillator.
			// Committing it makes the escape the new baseline to smooth from.
			aiSteer = direction;
		}
		if ((direction).Length() <= SteerDeadzone)
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
		// RELATIVE velocity: the question is "on our present courses, does this hit me", and the
		// ship is moving too. Using the threat's velocity alone mispredicts every case where the
		// ship is closing on the threat's path -- which is most near-misses turning into hits.
		// ObservedVelocity, not SpeedVector, for the threat: the latter is derived from
		// _speed/_direction and reads zero for everything that writes Position directly --
		// including the spider boss's fly states, i.e. exactly the case this method exists for.
		Vector2 rel = baddy.ObservedVelocity - SpeedVector;
		float speed = (rel).Length();
		// Below this it is not a "mover" in any meaningful sense and the radial term is a better
		// model. The player ship's own MaxSpeed is 0.33 px/ms, so this is ~a third of that.
		if (speed < ThreatMinSpeed)
		{
			return false;
		}
		Vector2 toShip = base.Position - baddy.Position;
		// Time of closest approach along the threat's own velocity (the ship's motion is left out
		// deliberately -- the ship is what we are choosing, so treating it as stationary asks the
		// right question: "if I stay here, does this hit me?").
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
		// Panic: a dead-on hit about to land RIGHT NOW has to outrank every other steering term,
		// not merely tie with them. Without this the evade is one vote of at most maxSteerStrength
		// (4) against a boss-approach pull, a powerup pull and the edge pushes -- and the ship
		// takes the hit while politely averaging its options.
		if (t < ThreatPanicMs && missDist < margin * ThreatPanicMissFraction)
		{
			strength = MathHelper.Max(strength, ThreatPanicStrength);
		}
		direction += strength * MyMath.AngleToVector(MyMath.VectorToAngle(side) + dodgeAngle);
		return true;
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
		float dx = map.ColumnCentreX(column) - base.Position.X;
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
		int span = MathHelper.Max((int)Math.Ceiling(shipWidth / map.TileSize), 1);
		int best = x;
		float bestScore = float.MinValue;
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
				aiGapCost = heldScore;
				return aiGapColumn;
			}
		}
		aiGapColumn = best;
		aiGapCost = bestScore;
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
		if (map == null)
		{
			return;
		}
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
		// Net seam: record the fire INTENT (called every tick while the trigger is held,
		// before the cadence gate below) -- this is what the co-op ship stream replicates.
		NetLastFireMs = Environment.TickCount64;
		NetLastFireAim = direction;
		pacifistTimer.Reset();
		pacifistTimer.Start();
		if (shoottimer.Finished | !shoottimer.Active)
		{
			shoottimer.Start();
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
			break;
		}
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
		EvilAliensWeb.Compat.AiBench.NoteDeath(this);
		// Game juice: the player's own death is the biggest impact in the game — a real
		// freeze-frame + extra trauma on top of what the two explosions below add.
		EvilAliensWeb.Compat.Juice.AddHitStop(0.18f);
		EvilAliensWeb.Compat.Juice.AddTrauma(0.35f);
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		float impulse = (backgroundSpeed).Length();
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
			EvilAliensWeb.Compat.AiBench.NoteDeath(this);
			// Game juice: same death punch as AsplodeWall — freeze-frame + extra trauma on
			// top of the two explosions' own shake.
			EvilAliensWeb.Compat.Juice.AddHitStop(0.18f);
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
