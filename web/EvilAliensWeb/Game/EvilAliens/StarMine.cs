using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

public class StarMine : KillableAlien
{
	private enum MineState
	{
		free,
		attracted_to_player,
		attracted_to_boss
	}

	private JunkBoss boss;

	private Vector2 prevposition;

	private float r;

	private int hitpointsattached;

	private MineState state;

	private PlayerShip target;

	private float backgroundfactor;

	private bool connectedwithbg;

	private Timer timer = new Timer(1800f, repeating: false);

	private Timer soundtimer = new Timer(300f, repeating: false);

	private SoundEffectInstance sfx;

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			c.Position = base.Position;
			c.Radius = r;
			return c;
		}
	}

	public StarMine(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/deathstarsheet2", 4, 8, 1, 25f));
		r = 24f;
		base.DrawOrder = 20;
		base.MaxSpeed = 0.18f;
		base.Deceleration = 6.0000002E-05f;
		SetHitPoints(10, scaleWithDifficulty: true);
		timers.Add(timer);
		timers.Add(soundtimer);
		PointValue = 20f;
	}

	private void connectToBackground()
	{
		connectedwithbg = true;
	}

	private void disconnectFromBackground()
	{
		connectedwithbg = false;
	}

	private void moveWithBackground(GameTime gameTime)
	{
		if (connectedwithbg)
		{
			backgroundfactor = MathHelper.Clamp(backgroundfactor + (float)gameTime.ElapsedGameTime.TotalMilliseconds / 1000f, 0f, 1f);
		}
		else
		{
			backgroundfactor = MathHelper.Clamp(backgroundfactor - (float)gameTime.ElapsedGameTime.TotalMilliseconds / 1000f, 0f, 1f);
		}
		base.Position += MyMath.PowerCurve(0f, 1f, 2f, backgroundfactor) * oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == target)
		{
			target = null;
		}
		if (e.GameComponent == boss)
		{
			boss = null;
			state = MineState.free;
			connectToBackground();
		}
		if (e.GameComponent == this && boss != null)
		{
			boss.RemoveChild();
			boss = null;
		}
	}

	public static StarMine NewStarMine(ComponentBin collection, Game game)
	{
		StarMine starMine = collection.Recycle<StarMine>();
		if (starMine == null)
		{
			starMine = new StarMine(game);
		}
		return starMine;
	}

	public void Setup()
	{
		base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -24f);
		base.Speed = 0f;
		backgroundfactor = 1f;
	}

	internal void SetupLaunch(Vector2 spawnposition, float a)
	{
		base.Position = spawnposition;
		base.Direction = a;
		base.Speed = base.MaxSpeed;
		backgroundfactor = 0f;
	}

	public override void Initialize()
	{
		base.Initialize();
		sfx = null;
		timer.Stop();
		soundtimer.Stop();
		connectedwithbg = true;
		state = MineState.free;
		// PER LIFE, and it was NOT reset before (card 745728f9). `ComponentBin` recycles mines, so
		// a mine out of the pool inherited the PREVIOUS mine's target -- a stale `PlayerShip`
		// reference, possibly a corpse, possibly an instance the pool has since handed to another
		// slot. Latent today (the `free` state overwrites it before anything reads it) and exactly
		// the shape that is not latent for long: it is `EvilSkull.bulletsfired` (card d8344c17)
		// again, a pooled field nothing cleared. Found by MineTargetTest's second run in one
		// process, which is what that leave-no-trace convention is for.
		target = null;
		hitpointsattached = 3;
		prevposition = base.Position;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		float acquireRange = 250f * Settings.GetInstance().DifficultyFactorized(0.5f);
		prevposition = base.Position + oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		switch (state)
		{
		case MineState.free:
		{
			bool acquired = false;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				// A DEAD ship is not a target (card 745728f9). `GetShips()` is updated at the
				// ComponentBin's removal FLUSH, not at Die(), so a ship that died this tick is
				// still in it with IsDead already true -- and locking onto one is how a mine ended
				// up flying to a corpse's last position and detonating there.
				if (ship.IsDead)
				{
					continue;
				}
				Vector2 toShip = ship.Position - base.Position;
				if ((toShip).LengthSquared() <= acquireRange * acquireRange)
				{
					target = ship;
					acquired = true;
				}
			}
			if (acquired)
			{
				if (!soundtimer.Active)
				{
					sound.Stop(sfx);
					sfx = sound.Play("targetacquired");
					soundtimer.Start();
					// Online co-op (card 745728f9, "the homing sound doesnt play for joining
					// clients"): a puppet mine is FROZEN, so this Update -- and this cue -- never
					// runs over there. The lock-on is a warning the other player is entitled to
					// hear, exactly like an enemy Lazer's telegraph (card c146422f), so it rides
					// its own beat. Emitted HERE, at the host's real acquire, and gated on the
					// same soundtimer that gates the local cue, so the wire carries one beat per
					// sound rather than one per tick of an ongoing lock.
					EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
						EvilAliensWeb.Compat.Net.NetFxKind.MineTargetAcquired, this);
				}
				state = MineState.attracted_to_player;
				disconnectFromBackground();
				timer.Duration = 1800f;
				timer.Reset();
				timer.Start();
			}
			break;
		}
		case MineState.attracted_to_player:
		{
			// THE LOCK IS ON A LIVE SHIP, and losing it is the whole of card 745728f9's first
			// half: *"space mines (lvl 3, aka death stars) seem to also explode when they reach a
			// dead player's location"*. `target` was only ever cleared by the release-RANGE test,
			// and nothing set it to null when the ship died -- so the mine kept pulling toward a
			// corpse's frozen Position and, 1800 ms after the acquire, Asplode()d there.
			//
			// Worse, `PlayerShip` is POOLED: a dead target's instance can be handed back out by
			// `Recycle<PlayerShip>` for a respawn, at which point the mine is silently homing on a
			// live ship it never acquired, on somebody else's timer. Testing `IsDead` closes that
			// too, because the death is visible for at least one tick before the recycle.
			//
			// Going back to `free` rather than just dropping the pull is deliberate: `free` does
			// not consult `timer`, so a mine whose target dies cannot detonate on the old clock,
			// and a re-acquire Resets it. It also has to be within `acquireRange` of someone to
			// re-acquire at all, which is the behaviour the report asks for -- the mine loses
			// its lock.
			if (target == null || target.IsDead || !oracle.GetShips().Contains(target))
			{
				target = null;
				state = MineState.free;
				connectToBackground();
				break;
			}
			float releaseRange = acquireRange + acquireRange * 0.08f;
			Vector2 pull = target.Position - base.Position;
			if ((pull).LengthSquared() > 0.25f)
			{
				(pull).Normalize();
			}
			pull *= 0.00029999999f;
			base.SpeedVector += pull * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (timer.Finished)
			{
				Asplode();
			}
			Vector2 toTarget = target.Position - base.Position;
			if ((toTarget).LengthSquared() >= releaseRange * releaseRange)
			{
				state = MineState.free;
				connectToBackground();
			}
			break;
		}
		case MineState.attracted_to_boss:
		{
			if (boss == null)
			{
				state = MineState.free;
				connectToBackground();
				break;
			}
			Vector2 pull = boss.Position - base.Position;
			if ((pull).LengthSquared() > 0.25f)
			{
				(pull).Normalize();
			}
			pull *= 0.00029999999f;
			base.SpeedVector += pull * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			break;
		}
		}
		Move(gameTime);
		moveWithBackground(gameTime);
		base.Update(gameTime);
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.5 * gameTime.ElapsedGameTime.TotalSeconds * (double)Settings.GetInstance().DifficultyModifier)
		{
			Fire();
		}
		if (OffScreen(100f))
		{
			Die();
		}
	}

	private void Asplode()
	{
		if (!base.IsDead)
		{
			// Online co-op (card 4e406eba): a mine detonating on its own timer, or set off by a
			// neighbour's blast, is a real death with NO killing blow -- so KillableAlien.HitBy
			// never runs and the removal seam had nothing to attribute. Without this note the
			// host broadcast KillerNone and the mine simply vanished on the other screen.
			EvilAliensWeb.Compat.Net.NetSession.NoteSelfDestruct(this);
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 3.5f, 2.5f, 0.03f, base.Direction);
			explosion.MakeBlue();
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2f, 1.3f, 0.06f, base.Direction);
			explosion.MakeBlue();
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
			Die();
		}
	}

	// The client half of the lock-on beat above (card 745728f9). A puppet mine is frozen, so its
	// own Update never reaches the cue -- this is the only way the joiner hears it. Draw-free and
	// state-free: it plays the sound and nothing else, because the mine's MOTION is replicated by
	// the snapshot stream and its detonation by the host's own death event.
	//
	// It does NOT touch `soundtimer`: that timer gates the HOST's emission, so the wire already
	// carries one beat per sound. Consuming it here would only make a puppet that was itself
	// briefly a local mine (it never is) behave differently.
	//
	// **IT MUST FALL THROUGH TO `base`**, and that is not a formality: `StarMine` is a
	// `KillableAlien`, whose own override is what plays the 35 ms HIT BLINK. An override that
	// simply returned would delete the blink for every mine on the joiner's screen -- a silent
	// regression in the very feature (card 43e85936) this beat is modelled on.
	internal override void NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
	{
		if (kind != EvilAliensWeb.Compat.Net.NetFxKind.MineTargetAcquired)
		{
			base.NetPlayFx(kind);
			return;
		}
		if (!base.IsDead)
		{
			sound.Stop(sfx);
			sfx = sound.Play("targetacquired");
		}
	}

	// ---- readbacks for Compat/Net/MineTargetTest (card 745728f9) --------------------------------
	//
	// The lock is INVISIBLE: a locked mine and a free one draw the same sprite, and the difference
	// -- which ship it is pulling toward, and whether the 1800 ms detonation clock is running --
	// is private state that no frame and no metric can show. That is why the card's first half is
	// verified as data.
	internal bool NetLockedOn => state == MineState.attracted_to_player;

	internal PlayerShip NetTarget => target;

	internal bool NetDetonationClockRunning => state == MineState.attracted_to_player && timer.Active;

	// Drive one acquire/steer tick with no scene attached -- the isolation-sim pattern. The suite
	// ticks the MINE and nothing else, so the level's own state machine cannot advance underneath
	// it (a real player death would otherwise wipe the world a tick later and take the mine with
	// it, destroying the very observation).
	internal void NetTickForTest(GameTime gameTime)
	{
		Update(gameTime);
	}

	// Park a mine at an exact point, at rest and off the background scroll. The production entries
	// cannot: `Setup()` drops it at a RANDOM x above the screen (the spawner's own entry) and
	// `SetupLaunch` gives it `MaxSpeed`, which over the 1800 ms detonation clock carries it ~324 px
	// -- past the mine's own release range, so the suite's positive control could never reach a
	// detonation at all. `Speed` and `backgroundfactor` are protected, hence a seam rather than a
	// caller-side poke.
	internal void NetParkForTest(Vector2 at)
	{
		base.Position = at;
		base.Speed = 0f;
		base.SpeedVector = Vector2.Zero;
		backgroundfactor = 0f;
		prevposition = at;
	}

	private void Fire()
	{
		float holdFireRange = 200f / Settings.GetInstance().DifficultyFactorized(0.4f);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			Vector2 toShip = ship.Position - base.Position;
			if ((toShip).Length() <= holdFireRange)
			{
				return;
			}
		}
		EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
		evilBullet.Setup(base.Position, RandomHelper.RandomNextAngle());
		collection.Add((GameComponent)(object)evilBullet);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Wall)
		{
			base.Position = prevposition;
			if (DetectCollision(other))
			{
				Die();
			}
		}
		if (other is Bullet)
		{
			if (state == MineState.attracted_to_boss)
			{
				hitpointsattached--;
				if (hitpointsattached == 0)
				{
					float angle = MyMath.VectorToAngle(base.Position - boss.GetPosition) + (float)Math.PI / 4f * RandomHelper.RandomNextFloat(-1f, 1f);
					base.SpeedVector = MyMath.AngleToVector(angle) * 10f;
					state = MineState.free;
					boss.RemoveChild();
					boss = null;
					connectToBackground();
					Explosion explosion = Explosion.NewExplosion(collection, base.Game);
					explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
					collection.Add((GameComponent)(object)explosion);
					sound.PlayCue("expl1");
				}
			}
			else
			{
				Vector2 knockback = base.Position - ((Bullet)other).Position;
				(knockback).Normalize();
				knockback *= 0.036000002f;
				base.SpeedVector += knockback;
			}
		}
		if (state != MineState.attracted_to_boss && other is Explosion)
		{
			Asplode();
		}
		if (state != MineState.attracted_to_boss || (!(other is StarMine) && !(other is JunkBoss)))
		{
			return;
		}
		if (other is StarMine && ((StarMine)other).state == MineState.attracted_to_boss)
		{
			StarMine starMine = (StarMine)other;
			Vector2 toMine = starMine.Position - base.Position;
			float distance = (toMine).Length();
			if (distance < r + starMine.r)
			{
				float overlap = r + starMine.r - distance;
				Vector2 pushDir = toMine;
				(pushDir).Normalize();
				float ownScaleShare = scale / (starMine.scale + scale);
				base.Position -= pushDir * overlap * (1f - ownScaleShare);
				starMine.Position += pushDir * overlap * ownScaleShare;
			}
		}
		if (other is JunkBoss)
		{
			JunkBoss junkBoss = (JunkBoss)other;
			Vector2 toBoss = junkBoss.GetPosition - base.Position;
			float distance = (toBoss).Length();
			if (distance < r + junkBoss.r)
			{
				_ = junkBoss.r;
				Vector2 pushDir = toBoss;
				(pushDir).Normalize();
				// Fidelity (review M4): the spatial grid fires each circle pair once per direction
				// per frame; the shipped 2008 build's all-pairs scan fired this ungated 1px push-out
				// twice per frame — the x2 preserves the original net separation rate so
				// attached StarMines don't sink deeper into the JunkBoss.
				base.Position -= pushDir * 2f;
			}
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		AwardScore(isComboGenerator, other);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		Die();
	}

	// Card 4e406eba: the mine's self-destruct looks nothing like being shot -- Asplode() is two
	// big BLUE bursts and "expl2", KilledBy is one small white burst and "expl1" -- so the peer
	// replays the real thing. Asplode() guards on IsDead and ends in Die(), so it is safe to
	// call on a puppet and removes it itself.
	internal override void NetReplayUnattributedDeath(ICollidable agent)
	{
		Asplode();
	}

	internal void AttractByBoss(JunkBoss junkBoss)
	{
		if (hitpointsattached > 0)
		{
			if (boss == null)
			{
				junkBoss.AddChild();
			}
			state = MineState.attracted_to_boss;
			boss = junkBoss;
			disconnectFromBackground();
		}
	}
}
