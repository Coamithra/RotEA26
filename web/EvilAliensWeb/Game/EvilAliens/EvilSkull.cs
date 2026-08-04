using System;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class EvilSkull : KillableAlien
{
	private EnemyBehaviour behaviour;

	private bool hasbonus;

	private Powerup bonus;

	private bool isInMaze;

	private bool justspawned;

	private bool hitwall;

	private float accelDir;

	private bool launched;

	private Timer launchedTimer = new Timer(1300f, repeating: false);

	private Timer fadeintimer;

	private Timer fadeouttimer;

	private Timer lifetime;

	private Timer shoottimer1;

	private Timer shoottimer2;

	// Shots taken so far in the CURRENT volley. Reset per life in Initialize -- see the note
	// there; it is the counter that decides when the volley ends.
	private int bulletsfired;

	// Stable per-instance tag for the ?skullvolley diagnostic, so one skull's volley can be
	// followed across the many that are usually on screen at once. Instance identity, not life
	// identity: the pool hands the same tag out again, which is exactly what made the
	// carried-counter bug legible.
	private static int volleyNextTag;

	private readonly int volleyTag = volleyNextTag++;

	public bool Fading => fadeintimer.Active | fadeouttimer.Active | fadeouttimer.Finished;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public EvilSkull(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/faceofdeathspritesheet", 4, 8, 1, 12f));
		base.DrawOrder = 20;
		PointValue = 25f;
		shoottimer1 = new Timer(5500f, repeating: true);
		shoottimer2 = new Timer(133f, repeating: true);
		fadeintimer = new Timer(1f, repeating: false);
		fadeouttimer = new Timer(800f, repeating: false);
		lifetime = new Timer(6000f, repeating: false);
		timers.Add(shoottimer1);
		timers.Add(shoottimer2);
		timers.Add(launchedTimer);
		timers.Add(fadeintimer);
		timers.Add(fadeouttimer);
		timers.Add(lifetime);
		SetHitPoints(1, scaleWithDifficulty: false);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && bonus != null)
		{
			collection.Remove((GameComponent)(object)bonus);
			bonus = null;
		}
	}

	public static EvilSkull NewEvilSkull(ComponentBin collection, Game game)
	{
		EvilSkull evilSkull = collection.Recycle<EvilSkull>();
		if (evilSkull == null)
		{
			evilSkull = new EvilSkull(game);
		}
		return evilSkull;
	}

	public void Setup(Vector2 startposition, EnemyBehaviour behaviour)
	{
		this.behaviour = behaviour;
		launched = false;
		hasbonus = false;
		isInMaze = false;
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
			justspawned = true;
			hitwall = false;
			base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
			fadeintimer.Duration = 1000f;
			fadeintimer.Start();
			lifetime.Start();
			fadeouttimer.Stop();
			break;
		case EnemyBehaviour.classic:
			base.Position = startposition;
			fadeouttimer.Stop();
			fadeintimer.Stop();
			lifetime.Stop();
			break;
		}
	}

	public void SetMaze()
	{
		isInMaze = true;
		base.Position = new Vector2(base.Position.X, -45f);
		fadeintimer.Duration = 10f;
		fadeintimer.Reset();
	}

	internal void SetMaze(bool p)
	{
		if (p)
		{
			SetMaze();
		}
		else
		{
			isInMaze = p;
		}
	}

	public void SetupLaunch(Vector2 startposition, float direction)
	{
		behaviour = EnemyBehaviour.normal;
		base.Position = startposition;
		base.Direction = direction;
		launched = true;
		launchedTimer.Start();
		isInMaze = true;
		hasbonus = false;
		fadeintimer.Stop();
		lifetime.Start();
		fadeouttimer.Stop();
	}

	public void MakeBonus()
	{
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
	}

	public override void Initialize()
	{
		base.Initialize();
		// PER-LIFE, like isdead/awarded/netTeleported and every Timer in base.Initialize (card
		// d8344c17). EvilSkull is pooled, and this counter was the one piece of its state nothing
		// reset: a skull killed part-way through a volley handed its count to whoever came out of
		// the pool next, whose own volley then ended that many shots early. Measured on Level 3:
		// 29% of spawns inherited a count of 1..7, so volley length was effectively random
		// between 1 and the cap instead of the cap -- which is what the bug report ("sometimes
		// shoots a massive series of bullets, rather than the expected number") was describing.
		// The cap itself, (int)(4 * DifficultyModifier), is a separate and DELIBERATE thing: it
		// climbs with the difficulty time-ramp (see web CLAUDE.md), so a late-level volley really
		// is 8-9 shots where an opening one is 4.
		bulletsfired = 0;
		color = Color.White;
		base.Acceleration = 6.0000002E-05f;
		base.Deceleration = 1.8E-05f;
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
			if (launched)
			{
				base.MaxSpeed = 0.15f;
				base.Speed = base.MaxSpeed;
				accelDir = base.Direction;
				shoottimer1.Duration = 8000f;
			}
			else
			{
				base.MaxSpeed = 0.03f;
				base.Speed = 0f;
				accelDir = RandomHelper.RandomNextAngle();
				base.Direction = accelDir;
				shoottimer1.Duration = 5500f;
			}
			shoottimer1.Start();
			shoottimer1.Randomize();
			shoottimer2.Stop();
			break;
		case EnemyBehaviour.classic:
			base.MaxSpeed = 0.107999995f;
			base.Direction = RandomHelper.RandomNextAngle();
			base.Speed = RandomHelper.RandomNextFloat(0.036000002f, 0.107999995f);
			shoottimer1.Duration = 4500f;
			shoottimer1.Start();
			shoottimer1.Randomize();
			shoottimer2.Stop();
			if (((base.Position.X < 0f) | (base.Position.X > 800f)) && (double)Math.Abs(base.DirectionalVector.X) < 0.5)
			{
				base.DirectionalVector = new Vector2(0.5f * (float)(-Math.Sign(base.Position.X)), base.DirectionalVector.Y);
			}
			if (((base.Position.Y < 0f) | (base.Position.Y > 600f)) && (double)Math.Abs(base.DirectionalVector.Y) < 0.5)
			{
				base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0.5f * (float)(-Math.Sign(base.Position.Y)));
			}
			break;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		if (fadeintimer.Active)
		{
			float num = 1f - fadeintimer.Normalized;
			color = new Color(new Vector4(1f, 1f, 1f, num));
		}
		else if (fadeouttimer.Active | fadeouttimer.Finished)
		{
			float normalized = fadeouttimer.Normalized;
			color = new Color(new Vector4(1f, 1f, 1f, normalized));
		}
		else
		{
			color = Color.White;
		}
		if (hasbonus)
		{
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(72f, 160f, Powerup.PowerUpHue(bonus.type));
			if (bonus.type == Powerup.PowerupType.OneUp)
			{
				// WorldTime, not gameTime: a Draw-time hue cycle on the raw clock kept the OneUp
				// rainbow rolling while the world sat frozen in a pause (card d79a2f48).
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(72f, 160f, 250f * WorldTime.Seconds % 360f);
			}
			spriteBatch.colorizeEffect.Enable();
		}
		base.Draw(gameTime);
		spriteBatch.colorizeEffect.Disable();
	}

	public override void Update(GameTime gameTime)
	{
		if (!hitwall)
		{
			justspawned = false;
		}
		else
		{
			hitwall = false;
		}
		if (justspawned)
		{
			return;
		}
		if (lifetime.Finished)
		{
			fadeouttimer.Start();
		}
		if (fadeouttimer.Finished)
		{
			collection.Remove((GameComponent)(object)this);
		}
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
		{
			if (launchedTimer.Active & launched)
			{
				base.MaxSpeed = MathHelper.Lerp(0.03f, 2.5f, launchedTimer.Normalized / 16.666666f);
				base.Speed = base.MaxSpeed;
			}
			if (launchedTimer.Finished & launched)
			{
				base.MaxSpeed = 0.03f;
			}
			Move((float?)accelDir, gameTime);
			base.Update(gameTime);
			Vector2 v = MyMath.AngleToVector(accelDir);
			if (!isInMaze)
			{
				int num = 10;
				if (base.Position.X > (float)(800 - num) && v.X > 0f)
				{
					v.X *= -1f;
				}
				if (base.Position.X < (float)num && v.X < 0f)
				{
					v.X *= -1f;
				}
				if (base.Position.Y > (float)(600 - num) && v.Y > 0f)
				{
					v.Y *= -1f;
				}
				if (base.Position.Y < (float)num && v.Y < 0f)
				{
					v.Y *= -1f;
				}
			}
			else if (!launched | !launchedTimer.Active)
			{
				base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			}
			else
			{
				base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * (1f - launchedTimer.Normalized);
			}
			accelDir = MyMath.VectorToAngle(v);
			if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0002 * gameTime.ElapsedGameTime.TotalMilliseconds)
			{
				accelDir = RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f);
			}
			float num2 = 75f;
			if ((base.Position.X < 0f - num2) | (base.Position.X > 800f + num2) | (base.Position.Y < 0f - num2) | (base.Position.Y > 600f + num2))
			{
				Die();
			}
			if (shoottimer2.Finished)
			{
				bool suppressed = true;
				if ((base.Position.X > 0f) & (base.Position.Y > 0f) & (base.Position.X < 800f) & (base.Position.Y < 600f))
				{
					bool flag = false;
					float num3 = 200f / Settings.GetInstance().DifficultyFactorized(0.4f);
					foreach (PlayerShip ship in oracle.GetShips())
					{
						bool num4 = flag;
						Vector2 val = ship.Position - base.Position;
						flag = num4 | ((val).Length() <= num3);
					}
					// `Fading`, not `fadeintimer.Active` (card d8344c17). The 2008 gate covered the
					// fade IN and not the fade OUT, so a skull kept shooting for the whole 800 ms
					// dissolve and on past it -- while `PlayerShip.CollidesWith` excludes a `Fading`
					// skull, so it could not be shot back at or even crashed into. Bullets streamed
					// out of something the player could neither see nor answer, which reads as the
					// nearest VISIBLE skull firing far more than it should. One predicate for both
					// halves now, and it is the same one collision uses: if the player cannot touch
					// it, it cannot touch the player.
					if (!(flag | Fading))
					{
						EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
						evilBullet.Setup(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 32));
						collection.Add((GameComponent)(object)evilBullet);
						suppressed = false;
					}
				}
				// The COUNTER still advances on a suppressed shot, exactly as in 2008 -- the volley
				// is a fixed number of beats, not a fixed number of bullets, so a skull that spends
				// its volley next to the player does not save the shots up for later.
				bulletsfired++;
				ReportVolleyShot("normal", suppressed);
				if (bulletsfired >= VolleyCap)
				{
					bulletsfired = 0;
					shoottimer2.Stop();
				}
			}
			if (shoottimer1.Finished)
			{
				ReportVolleyRearm(behaviour == EnemyBehaviour.classic ? "classic" : "normal");
				shoottimer2.Reset();
				shoottimer2.Start();
			}
			break;
		}
		case EnemyBehaviour.classic:
		{
			base.Update(gameTime);
			Vector2 directionalVector = base.DirectionalVector;
			if (base.Position.X > 800f && directionalVector.X > 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.X < 0f && directionalVector.X < 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.Y > 600f && directionalVector.Y > 0f)
			{
				directionalVector.Y *= -1f;
			}
			if (base.Position.Y < 0f && directionalVector.Y < 0f)
			{
				directionalVector.Y *= -1f;
			}
			base.DirectionalVector = directionalVector;
			if (shoottimer2.Finished)
			{
				// Same fade gate as the normal branch above. A classic skull stops all three fade
				// timers in Setup and so never fades, making this a no-op in shipped play -- it is
				// here so the invariant ("a skull the player cannot hit does not shoot") holds for
				// the type rather than for one of its two behaviours.
				bool suppressed = true;
				if (!Fading)
				{
					EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
					evilBullet.Setup(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 32));
					collection.Add((GameComponent)(object)evilBullet);
					suppressed = false;
				}
				bulletsfired++;
				ReportVolleyShot("classic", suppressed);
				if (bulletsfired >= VolleyCap)
				{
					bulletsfired = 0;
					shoottimer2.Stop();
				}
			}
			if (shoottimer1.Finished)
			{
				ReportVolleyRearm(behaviour == EnemyBehaviour.classic ? "classic" : "normal");
				shoottimer2.Reset();
				shoottimer2.Start();
			}
			break;
		}
		}
	}

	// The volley cap: how many shots this skull takes once shoottimer1 rearms it. Rises with the
	// difficulty time-ramp, so it is read fresh every shot rather than latched at volley start.
	private static int VolleyCap => (int)(4f * Settings.GetInstance().DifficultyModifier);

	// ?skullvolley / eaSkullVolley(): one line per shot, the only observable that shows a volley
	// running short. `shot=i/cap` is the assertion surface -- a run where every volley ends at
	// `cap/cap` is the fixed behaviour; carried counts show up as volleys whose first shot is not
	// `shot=1`.
	// Reported where shoottimer1 REARMS the volley, because that is the one moment the pooling bug
	// is expressible on a SINGLE line: a fresh volley must always open with the counter at zero, and
	// a carried count shows up here as `fired=` anything else. It is not visible in the shot stream
	// -- a carried counter makes a truncated volley look like the seamless continuation of the
	// previous skull's, which is exactly how it stayed hidden. `evilskull_volley.txt` asserts it.
	private void ReportVolleyRearm(string mode)
	{
		if (DebugFlags.SkullVolley)
		{
			Console.WriteLine($"[skull] tag={volleyTag} {mode} rearm fired={bulletsfired} cap={VolleyCap} t={WorldTime.Seconds * 1000f:F0}");
		}
	}

	private void ReportVolleyShot(string mode, bool suppressed)
	{
		if (DebugFlags.SkullVolley)
		{
			// fade= reuses NetFadePhase's own terms so the diagnostic can never drift from the
			// predicate PlayerShip gates collision on (`Fading`). `fade=out` means the skull is
			// mid-dissolve and already unshootable; a shot from there is the invisible-fire case.
			string fade = NetFadePhase switch { 2 => "out", 1 => "in", _ => "none" };
			Console.WriteLine($"[skull] tag={volleyTag} {mode} shot={bulletsfired}/{VolleyCap} fade={fade} shot_fired={!suppressed} mod={Settings.GetInstance().DifficultyModifier:F2} t={WorldTime.Seconds * 1000f:F0}");
		}
	}

	// The live volley state of every skull on screen, as DATA (eaSkullVolley() / eval
	// SkullVolley). Needs no shot to happen and no level to be reached, so it is how the
	// per-life reset is checked directly rather than inferred from a shot stream.
	internal string VolleyReport()
	{
		return $"tag={volleyTag} {behaviour} fired={bulletsfired} cap={VolleyCap} armed={shoottimer2.Active}";
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Wall || other is PlayerShip)
		{
			if (justspawned)
			{
				base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
				if (isInMaze)
				{
					base.Position = new Vector2(base.Position.X, -45f);
				}
				// Online co-op (card e79bb994): the grinning face reappears somewhere else
				// outright -- a reposition, not motion, so the host must not differentiate across
				// it. NOTE it is a RANDOM point, so plenty of these jumps are shorter than the
				// puppet layer's 100px snap threshold: unmarked, those were BLENDED, and the skull
				// slid across the screen on the joiner instead of reappearing.
				NetNoteTeleport();
				fadeintimer.Reset();
				justspawned = true;
				hitwall = true;
			}
			else if (other is Wall)
			{
				KilledBy(other, isComboGenerator: false);
			}
		}
		if (other is Explosion)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		Die();
		AwardScore(isComboGenerator, other);
		if (hasbonus)
		{
			if (isInMaze)
			{
				bonus.DoNotLimitSpeed();
			}
			else
			{
				bonus.SetSpeedLimit(0.051000003f);
			}
			bonus.Position = base.Position;
			collection.Add((GameComponent)(object)bonus);
			bonus = null;
			hasbonus = false;
		}
		Vector2 v = Vector2.Zero;
		if (other is Wall)
		{
			v = oracle.BackgroundSpeed;
		}
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, (v).Length(), MyMath.VectorToAngle(v));
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/EvilSkullDescriptor) -----
	// Client puppets run Enabled=false. behaviour picks the fade setup in Setup; the bonus
	// colorize hue is a spawn+state extra (like UFO). The fade phase MUST be replicated: Draw
	// derives its alpha from the fade timers and PlayerShip gates collision on !Fading, but the
	// puppet's Update never runs to START the fade-out, so the host drives it via NetSetFadePhase
	// (NetTickTimers then advances the timers, so alpha animates and Fading resolves).

	internal EnemyBehaviour NetBehaviour => behaviour;

	internal bool NetHasBonus => hasbonus;

	internal byte NetBonusType => (byte)(hasbonus ? bonus.type : Powerup.PowerupType.Blast);

	// 2 = fading out (near despawn), 1 = fading in, 0 = opaque. (== the Fading source terms.)
	internal byte NetFadePhase
	{
		get
		{
			if (fadeouttimer.Active | fadeouttimer.Finished)
			{
				return 2;
			}
			if (fadeintimer.Active)
			{
				return 1;
			}
			return 0;
		}
	}

	// Drive the frozen puppet's fade timers to the host's phase. Only the START edges need
	// replicating; NetTickTimers advances the running timer for the alpha ramp.
	internal void NetSetFadePhase(byte phase)
	{
		if (phase == 2)
		{
			fadeintimer.Stop();
			if (!(fadeouttimer.Active | fadeouttimer.Finished))
			{
				fadeouttimer.Reset();
				fadeouttimer.Start();
			}
		}
		else if (phase == 0)
		{
			fadeintimer.Stop();
			fadeouttimer.Stop();
		}
	}

	// Clear the one-frame spawn guard on a puppet (its Update never runs to clear it) so a stray
	// Wall/PlayerShip contact can't teleport it to a random respawn point.
	internal void NetSettle()
	{
		justspawned = false;
	}

	internal void NetMakeBonus(Powerup.PowerupType t)
	{
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
		bonus.MakeType(t);
	}

	internal void NetClearBonus()
	{
		hasbonus = false;
		bonus = null;
	}
}
