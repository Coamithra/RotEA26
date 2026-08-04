using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class JunkBoss : KillableAlien
{
	private enum JunkBossState
	{
		enter,
		summonmeteors,
		attracting,
		normal,
		naked,
		asplode
	}

	private const float explosionduration = 125f;

	private const int thresholdinitially = 5;

	private const int hitpointsinitially = 150;

	private const float lazertimeduration = 2500f;

	private const float shoottime = 1100f;

	private bool isbase;

	private JunkBossState state;

	public float r;

	private float targetdir;

	private Timer lazertimer;

	private Timer shoottimer;

	private Timer generictimer;

	private Timer sucktimer;

	private int explosions;

	private int threshold;

	private bool retaliate;

	private bool dangermessage;

	private float ydrawingoffset;

	private int children;

	private LazerGenerator suckeffect;

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	// Animated "fleet commander drone" eye (replaces the old static eye sprite). Idle on/off
	// loop normally; the spin+lightning attract sheet during the `attracting` suck state.
	private static readonly AnimationData EyeIdle = new AnimationData("GFX/Sprites/eye_idle", 4, 2, 1, 12f);

	private static readonly AnimationData EyeAttract = new AnimationData("GFX/Sprites/eye_attract", 9, 8, 1, 12f);

	private bool eyeAttracting;

	private bool eyeFinishing;

	private float eyePrevFrame;

	// Sprite-harness only: when set (by the `eyeattract` HarnessRegistry factory), Initialize
	// loads the spin+lightning attract sheet instead of idle, so the frozen harness can show the
	// rotating/attracting animation. In real play the state machine (UpdateEyeAnim) swaps to it,
	// but the harness freezes Update so that never runs. Defaults false and Initialize clears it
	// after consuming it (JunkBoss is pooled/recycled), so gameplay can never inherit the flag.
	public bool HarnessForceAttract;

	public Vector2 GetPosition => base.Position;

	public override ICollisionType CollisionType
	{
		get
		{
			c.Position = base.Position;
			c.Radius = r;
			return c;
		}
	}

	public JunkBoss(Game game)
		: base(game)
	{
		LoadAnimation(EyeIdle);
		base.DrawOrder = 20;
		lazertimer = new Timer(2500f, repeating: true);
		shoottimer = new Timer(1100f, repeating: false);
		generictimer = new Timer(0f, repeating: false);
		sucktimer = new Timer(5000f, repeating: false);
		timers.Add(lazertimer);
		timers.Add(shoottimer);
		timers.Add(generictimer);
		timers.Add(sucktimer);
		base.IsBoss = true;
		base.Colorize = true;
		PointValue = 2000f;
		SetHitPoints(150, scaleWithDifficulty: false);
	}

	// Was a dead event-handler-shaped method that nothing ever subscribed (JunkBoss never wired
	// it to game.Components.ComponentRemoved) -- fixed into the real virtual override
	// (AlienDrawableGameComponent/IComponentWatcher, the same seam LazerGenerator.OnComponentRemoved
	// uses to stop its own SFX) so a dangling `suckeffect` reference can't survive this boss's own
	// removal by some future path that doesn't go through KilledBy's explicit cleanup above.
	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && suckeffect != null)
		{
			suckeffect = null;
		}
	}

	public static JunkBoss NewJunkBoss(ComponentBin collection, Game game)
	{
		JunkBoss junkBoss = collection.Recycle<JunkBoss>();
		if (junkBoss == null)
		{
			junkBoss = new JunkBoss(game);
		}
		return junkBoss;
	}

	public void AddChild()
	{
		children++;
	}

	public void RemoveChild()
	{
		children--;
	}

	public void Setup(bool isbase)
	{
		this.isbase = isbase;
	}

	public override void Initialize()
	{
		if (!isbase)
		{
			GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SignedInGamer current = enumerator.Current;
					current.Presence.PresenceMode = (GamerPresenceMode)34;
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
		}
		LoadAnimation(EyeIdle);
		eyeAttracting = false;
		eyeFinishing = false;
		eyePrevFrame = 0f;
		scale = 1f;
		// body collision radius from the idle CELL via DrawScale (texture.Width is the whole
		// sheet now); matches the old on-screen size. Not recomputed for the bigger attract cell
		// so the lightning halo never inflates the hitbox.
		r = DrawScale * (float)(texture.LogicalWidth() / columns) / 2f;
		base.Initialize();
		children = 0;
		lazertimer.Start();
		shoottimer.Start();
		state = JunkBossState.enter;
		generictimer.Duration = 7000f / Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players);
		generictimer.Reset();
		generictimer.Start();
		sucktimer.Stop();
		color = Color.White;
		base.Position = new Vector2(400f, (0f - r) * 2f);
		base.Direction = 0f;
		targetdir = base.Direction;
		base.MaxSpeed = 0.042f;
		base.Acceleration = 3.0000001E-05f;
		base.Deceleration = 1.19999995E-05f;
		base.Speed = 0f;
		threshold = 5;
		retaliate = false;
		suckeffect = null;
		// Harness: swap to the attract sheet AFTER `r` (the hitbox) is sized off the idle cell,
		// so the drawn/animated sprite is the attract state but the collision radius still matches
		// real play (idle-based, halo excluded). LoadAnimation resets curframe; the harness re-seeds it.
		// Consume-and-clear the flag so a recycled instance can never carry the attract sheet into a
		// real level (level entry ClearCache()s the bin, but this makes the field self-cleaning too).
		if (HarnessForceAttract)
		{
			LoadAnimation(EyeAttract);
			HarnessForceAttract = false;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Position += new Vector2(0f, ydrawingoffset);
		base.Draw(gameTime);
		base.Position -= new Vector2(0f, ydrawingoffset);
		if (suckeffect != null)
		{
			((DrawableGameComponent)suckeffect).Draw(gameTime);
		}
	}

	private void SwapDir()
	{
		float num = 150f;
		if (base.Position.X > 800f - num)
		{
			targetdir = (float)Math.PI;
		}
		if (base.Position.X < num)
		{
			targetdir = 0f;
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		ydrawingoffset = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6.0) * 3f;
		if (!shoottimer.Active & retaliate)
		{
			int num = (int)(10f * Settings.GetInstance().DifficultyModifier);
			float num2 = RandomHelper.RandomNextFloat(0f, 1f);
			for (int i = 0; i < num; i++)
			{
				float direction = (float)i * ((float)Math.PI * 2f) / (float)num + num2;
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
				evilBullet.Setup(base.Position, direction);
				collection.Add((GameComponent)(object)evilBullet);
			}
			retaliate = false;
			shoottimer.Duration = 1100f / Settings.GetInstance().DifficultyModifier;
			shoottimer.Reset();
			shoottimer.Start();
		}
		switch (state)
		{
		case JunkBossState.asplode:
			if (generictimer.Finished)
			{
				generictimer.Duration = 125f * RandomHelper.RandomNextFloat(0.8f, 1.2f);
				generictimer.Reset();
				generictimer.Start();
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(base.Position, 1.5f * RandomHelper.RandomNextFloat(0.8f, 1.2f), 2f, 0.13f, RandomHelper.RandomNextAngle());
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl1");
				explosions++;
				if (explosions == 25)
				{
					explosion = Explosion.NewExplosion(collection, base.Game);
					explosion.Setup(base.Position, 6f, 3.3f, 0f, 0f);
					collection.Add((GameComponent)(object)explosion);
					sound.PlayCue("expl2");
					AwardScoreToAll(combo: true);
					Die();
				}
			}
			break;
		case JunkBossState.enter:
			if (base.Position.Y < 100f)
			{
				base.Position = new Vector2(base.Position.X, base.Position.Y + (float)gameTime.ElapsedGameTime.TotalMilliseconds * 1.25f / 16.666666f);
			}
			else
			{
				base.Position = new Vector2(base.Position.X, 100f);
			}
			if (generictimer.Finished)
			{
				dangermessage = false;
				state = JunkBossState.summonmeteors;
				generictimer.Duration = 2000f;
				generictimer.Reset();
				generictimer.Start();
			}
			break;
		case JunkBossState.summonmeteors:
		{
			Move(gameTime);
			if (!dangermessage && !isbase)
			{
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				animatedMessage.SetWarningDirection(4.712389f);
				collection.Add((GameComponent)(object)animatedMessage);
				// Online co-op (card c146422f, "no warning when asteroids appear"): spawned from the
				// boss's host-only Update, so the join peer got the meteor shower unannounced. Same
				// EvMessage lane the script banners use. NOT MakeShort -- this one is the full-width
				// banner, unlike the SpiderBoss sweep arrows.
				EvilAliensWeb.Compat.Net.NetSession.OnGameMessage(
					"Danger!", (int)SoundManager.Texts.Danger,
					(int)AnimatedMessage.MessageType.redwarning, 4.712389f, isShort: false);
				dangermessage = true;
			}
			if (!generictimer.Finished)
			{
				break;
			}
			for (int j = 0; j < (int)(30f * Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players)); j++)
			{
				if (!isbase)
				{
					Ball ball = Ball.NewBall(collection, base.Game);
					ball.Setup(this);
					collection.Add((GameComponent)(object)ball);
				}
			}
			generictimer.Duration = 13000f;
			sucktimer.Reset();
			sucktimer.Start();
			generictimer.Reset();
			generictimer.Start();
			state = JunkBossState.attracting;
			break;
		}
		case JunkBossState.attracting:
			Move(gameTime);
			if (suckeffect != null)
			{
				suckeffect.SetPosition(base.Position);
				foreach (StarMine starMine in oracle.GetStarMines())
				{
					starMine.AttractByBoss(this);
				}
			}
			if (sucktimer.Finished)
			{
				suckeffect = LazerGenerator.NewLazerGenerator(collection, base.Game);
				suckeffect.Setup(base.Position, 4f, 0.5f, 0f, 0f);
				collection.Add((GameComponent)(object)suckeffect);
				sucktimer.Reset();
			}
			SwapDir();
			if (generictimer.Finished)
			{
				state = JunkBossState.normal;
				collection.Remove((GameComponent)(object)suckeffect);
				suckeffect = null;
				threshold = 5;
			}
			break;
		case JunkBossState.naked:
			FireLazer();
			Move((float?)targetdir, gameTime);
			SwapDir();
			if (generictimer.Finished)
			{
				dangermessage = false;
				state = JunkBossState.summonmeteors;
				generictimer.Duration = 2000f;
				generictimer.Reset();
				generictimer.Start();
			}
			break;
		case JunkBossState.normal:
			if ((children == 0) | ((children <= 5) & (threshold <= 0)))
			{
				state = JunkBossState.naked;
				generictimer.Duration = 10000f / Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players);
				generictimer.Reset();
				generictimer.Start();
			}
			FireLazer();
			Move((float?)targetdir, gameTime);
			SwapDir();
			break;
		}
		UpdateEyeAnim();
	}

	// Drive the two-sheet eye animation off the boss state: the spin+lightning attract sheet
	// during the `attracting` suck, finishing its current spin cycle (which ends on the rest
	// pose, same as idle frame 0) before swapping back to the idle on/off loop -- no jump.
	private void UpdateEyeAnim()
	{
		bool wantAttract = state == JunkBossState.attracting;
		if (wantAttract && !eyeAttracting)
		{
			LoadAnimation(EyeAttract);
			eyeAttracting = true;
			eyeFinishing = false;
			eyePrevFrame = 0f;
		}
		else if (!wantAttract && eyeAttracting && !eyeFinishing)
		{
			eyeFinishing = true;
		}
		if (eyeFinishing && curframe < eyePrevFrame)
		{
			LoadAnimation(EyeIdle);
			eyeAttracting = false;
			eyeFinishing = false;
			eyePrevFrame = 0f;
		}
		else
		{
			eyePrevFrame = curframe;
		}
	}

	private void FireLazer()
	{
		if (lazertimer.Finished)
		{
			Lazer lazer = Lazer.NewLazer(collection, base.Game);
			lazer.SetupSingleShot(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 16), 0f);
			collection.Add((GameComponent)(object)lazer);
			lazertimer.Duration = 2500f / Settings.GetInstance().DifficultyModifier;
			lazertimer.Reset();
		}
	}

	protected override void HitBy(ICollidable other, bool isComboGenerator)
	{
		if (state != JunkBossState.asplode)
		{
			base.HitBy(other, isComboGenerator);
			threshold--;
			if (!shoottimer.Active)
			{
				retaliate = true;
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		state = JunkBossState.asplode;
		base.Speed = 0f;
		explosions = 0;
		generictimer.Duration = 125f;
		generictimer.Start();
		generictimer.Reset();
		// Bug fix: a boss killed mid-`attracting` used to leave `suckeffect` (the swirling suck-in
		// particle swarm) orphaned in the collection. Its particles self-respawn every frame (see
		// LazerGenerator.Update), so it never naturally dies, and its looped "lazercharge" SFX
		// (CueConfig loop:true) never stops -- both the effect and its sound played forever. The
		// `attracting -> normal` transition already did this cleanup; asplode needs the same.
		if (suckeffect != null)
		{
			collection.Remove((GameComponent)(object)suckeffect);
			suckeffect = null;
		}
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsBosses1) --------
	// The eye's Draw-visible sheet: the idle on/off loop vs the spin+lightning attract sheet. A
	// frozen puppet never runs UpdateEyeAnim, so the host replicates which sheet is currently
	// loaded (`eyeAttracting`) and the client swaps to match; the base curframe (driver-advanced)
	// animates within it. r (the collision radius) is intentionally NOT recomputed on the swap,
	// exactly like real play.
	internal bool NetEyeAttracting => eyeAttracting;

	// The attraction glow (card c146422f, "no attraction vfx+sfx when the boss attracts
	// asteroids"). `suckeffect` is a child LazerGenerator this boss spawns mid-`attracting` and
	// draws BY HAND in Draw, so on a join peer -- where the boss is a frozen puppet -- the
	// swirling suck-in swarm and its looped "lazercharge" cue never existed. It was listed as an
	// accepted best-effort divergence in JunkBossDescriptor; it is replicated now, through exactly
	// the same seam the enemy laser windups use (Compat/Net/NetChargeGlow).
	//
	// It is NOT derivable from the eye's attract flag beside it: the eye swaps sheets the instant
	// the state begins, while the swarm appears one `sucktimer` later and outlives nothing.
	private bool netCharging;

	private Vector2 netChargeOffset;

	// 2.5f is LazerGenerator's own fallback: this boss never calls SetWindup on the suck swarm,
	// so that default IS the host's live value and the client's copy ramps identically.
	private float netChargeWindup = 2.5f;

	private float netChargeSize = 4f;

	// This emitter's own eased copy of the replicated aim (card eb057163). The wire value only
	// changes on this entity's snapshot turn, so the glow SWEEPS toward it instead of stepping;
	// it lives here rather than in NetChargeGlow because the child is pooled and the emitter is
	// what persists across a charge. Host-side this is never read (Drive is client-only).
	private Vector2 netEasedChargeOffset;

	internal bool NetCharging => suckeffect != null;

	internal Vector2 NetChargeOffset => suckeffect != null ? suckeffect.Position - base.Position : Vector2.Zero;

	internal float NetChargeWindup => suckeffect != null ? suckeffect.NetWindupSeconds : 2.5f;

	internal float NetChargeSize => suckeffect != null ? suckeffect.NetSize : 4f;

	// Client apply: record only -- the child spawn happens in NetDriveExtras (the descriptor
	// contract forbids spawning from ApplyStateExtra).
	internal void NetApplyCharge(bool charging, Vector2 offset, float windup, float size)
	{
		netCharging = charging;
		netChargeOffset = offset;
		netChargeWindup = windup;
		netChargeSize = size;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		EvilAliensWeb.Compat.Net.NetChargeGlow.Drive(ref suckeffect, ref netEasedChargeOffset, netCharging, netChargeOffset, netChargeWindup, netChargeSize, 0.5f, collection, base.Game, base.Position, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
	}

	internal void NetSetEyeAttract(bool attract)
	{
		if (attract == eyeAttracting)
		{
			return;
		}
		LoadAnimation(attract ? EyeAttract : EyeIdle);
		eyeAttracting = attract;
		eyeFinishing = false;
		eyePrevFrame = 0f;
	}
}
