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

	private void DoAIFire(GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		float aimSpread = (float)Math.PI / 12f;
		// Squared while the loop scans (it is compared against LengthSquared); the Math.Sqrt
		// after the loop turns it into a real distance for the range test.
		float nearestDist = float.MaxValue;
		AlienDrawableGameComponent nearest = null;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (baddy is UFO || baddy is Braineroid || (baddy is Ball && ((Ball)baddy).IsConnected()) || baddy is JunkBoss || baddy is Boss || baddy is Spider || baddy is MarsBoss || baddy is DeathStar || baddy is ClassicBoss || baddy is BattleSkull || (baddy is FlyingSpider && baddy.Collides) || baddy is StarMine || (baddy is EvilSkull && !((EvilSkull)baddy).Fading) || baddy is SweepUFO)
			{
				if (isBlastable(baddy) && blast != null && blast.Collides)
				{
					break;
				}
				Vector2 toBaddy = baddy.Position - base.Position;
				if ((toBaddy).LengthSquared() < nearestDist && baddy.Position.X > 0f && baddy.Position.X < 800f && baddy.Position.Y > 0f && baddy.Position.Y < 600f)
				{
					nearestDist = (toBaddy).LengthSquared();
					nearest = baddy;
				}
			}
		}
		nearestDist = (float)Math.Sqrt(nearestDist);
		if (nearestDist <= bulletlifetime * 0.78f)
		{
			if (nearest is JunkBoss)
			{
				FireAt(MyMath.VectorToAngle(nearest.Position - base.Position));
			}
			else
			{
				FireAt(MyMath.VectorToAngle(nearest.Position - base.Position) + RandomHelper.RandomNextFloat(0f - aimSpread, aimSpread));
			}
		}
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
		Vector2 steerTarget = default(Vector2);
		(steerTarget) = new Vector2(float.MaxValue, float.MaxValue);
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
			if (baddy is Wall)
			{
				hasWall = true;
				float wallProbeStep = 1.2f * (float)gameTime.ElapsedGameTime.TotalMilliseconds * base.MaxSpeed;
				float wallNudge = 0f;
				if (player == 0)
				{
					wallNudge = 8f;
				}
				if (player == 1)
				{
					wallNudge = 4f;
				}
				if (player == 2)
				{
					wallNudge = 6f;
				}
				if (player == 3)
				{
					wallNudge = 10f;
				}
				collisionLevelMap = (CollisionLevelMap)((Wall)baddy).GetCollisionType();
				CollisionBox collisionBox = (CollisionBox)GetCollisionType();
				int x = 0;
				int y = 0;
				collisionLevelMap.GetMapCoords(ref x, ref y, base.Position);
				int target_x = 0;
				int target_y = 0;
				findNextTileOnMap(x, y, ref target_x, ref target_y, collisionLevelMap);
				if (target_y < y)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Left - wallProbeStep, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(wallNudge, 0f);
					}
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Right + wallProbeStep, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(0f - wallNudge, 0f);
					}
				}
				else if (target_x > x)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Left - wallProbeStep, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(wallNudge, 0f);
					}
					if (collisionLevelMap.TileIsOccupied(target_x, y - 1))
					{
						direction += new Vector2(0f, wallNudge);
					}
				}
				else if (target_x < x)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Right + wallProbeStep, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(0f - wallNudge, 0f);
					}
					if (collisionLevelMap.TileIsOccupied(target_x, y - 1))
					{
						direction += new Vector2(0f, wallNudge);
					}
				}
				else if (target_x != x)
				{
				}
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
				if (!baddy.Collides)
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
			Vector2 toTarget = steerTarget - base.Position;
			if (distToPowerup < (toTarget).Length())
			{
				steerTarget = powerup.Position;
			}
			if (distToPowerup <= steerRange)
			{
				float pull = MyMath.PowerCurve(maxSteerStrength, minSteerStrength, 2f, distToPowerup / steerRange);
				if (altSteering)
				{
					pull = MathHelper.Lerp(maxSteerStrength, minSteerStrength, distToPowerup / steerRange);
				}
				direction += pull * MyMath.AngleToVector(MyMath.VectorToAngle(toPowerup));
			}
		}
		foreach (PlayerShip ship2 in oracle.GetShips())
		{
			if (ship2.readyToConnect && ship2 != this && readyToConnect && !isConnectedWith(ship2))
			{
				steerTarget = ship2.Position;
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
			if (distToTarget > 10f)
			{
				direction += 0.8f * MyMath.AngleToVector(MyMath.VectorToAngle(steerTarget - base.Position));
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
		if (hasWall)
		{
			CollisionBox collisionBox2 = (CollisionBox)GetCollisionType();
			float wallProbeReach = 41.666668f * base.MaxSpeed;
			if (direction.X > 0f)
			{
				int x2 = 0;
				int y2 = 0;
				collisionLevelMap.GetMapCoords(ref x2, ref y2, collisionBox2.BottomRight + new Vector2(wallProbeReach, 0f));
				if (collisionLevelMap.TileIsOccupied(x2, y2))
				{
					direction.X = 0f - MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
				collisionLevelMap.GetMapCoords(ref x2, ref y2, collisionBox2.TopRight + new Vector2(wallProbeReach, 0f));
				if (collisionLevelMap.TileIsOccupied(x2, y2))
				{
					direction.X = 0f - MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
			}
			else if (direction.X < 0f)
			{
				int x3 = 0;
				int y3 = 0;
				collisionLevelMap.GetMapCoords(ref x3, ref y3, collisionBox2.BottomLeft + new Vector2(0f - wallProbeReach, 0f));
				if (collisionLevelMap.TileIsOccupied(x3, y3))
				{
					direction.X = 0f + MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
				collisionLevelMap.GetMapCoords(ref x3, ref y3, collisionBox2.TopLeft + new Vector2(0f - wallProbeReach, 0f));
				if (collisionLevelMap.TileIsOccupied(x3, y3))
				{
					direction.X = 0f + MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
			}
			int x4 = 0;
			int y4 = 0;
			collisionLevelMap.GetMapCoords(ref x4, ref y4, collisionBox2.TopLeft + new Vector2(0f, -3f * wallProbeReach));
			if (collisionLevelMap.TileIsOccupied(x4, y4))
			{
				direction.Y = MathHelper.Max(Math.Abs(direction.X), 1f);
			}
			collisionLevelMap.GetMapCoords(ref x4, ref y4, collisionBox2.TopRight + new Vector2(0f, -3f * wallProbeReach));
			if (collisionLevelMap.TileIsOccupied(x4, y4))
			{
				direction.Y = MathHelper.Max(Math.Abs(direction.X), 1f);
			}
		}
		if ((direction).Length() <= 0.2f)
		{
			direction = Vector2.Zero;
		}
	}

	private void findNextTileOnMap(int x, int y, ref int target_x, ref int target_y, CollisionLevelMap map)
	{
		if (!map.TileIsOccupied(x, y - 1))
		{
			target_x = x;
			target_y = y - 1;
			return;
		}
		int scanX = x - 1;
		int leftCost = 0;
		while (map.TileIsOccupied(scanX, y) || map.TileIsOccupied(scanX, y - 1))
		{
			leftCost++;
			scanX--;
			if (scanX < 0)
			{
				leftCost = 1000;
				break;
			}
		}
		scanX = x + 1;
		int rightCost = 0;
		while (map.TileIsOccupied(scanX, y) || map.TileIsOccupied(scanX, y - 1))
		{
			rightCost++;
			scanX++;
			if (scanX >= map.Width)
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
