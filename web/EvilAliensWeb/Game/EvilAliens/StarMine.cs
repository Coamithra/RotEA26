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

	// ?minelog identity. A `[mine]` line is only useful if the acquire, the release and the death
	// can be tied to ONE mine -- Level 3 runs nine spawners and a lock is a per-mine decision.
	// Assigned per LIFE (in Initialize), so a pooled instance coming back out reads as a new mine
	// rather than silently continuing the previous one's story -- the same trap `target` and
	// `EvilSkull.bulletsfired` fell into.
	private static int nextLogId;

	private int logId;

	// Game-time ms since this mine acquired its current target, for the `[mine] boom` line's
	// `lockMs=`. The detonation clock is 1800 ms, so this is what separates 'went off ON its own
	// clock' from 'was set off early by something else' -- two events that are otherwise the same
	// pair of blue explosions.
	private float lockedMs;

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
		logId = ++nextLogId;
		lockedMs = 0f;
	}

	// Distance to the nearest LIVE player ship, or 'none' for a shipless world. `IsDead` rather
	// than list membership, for the same reason the acquire loop uses it: `GetShips()` only drops
	// a corpse at the removal FLUSH, so on the tick a ship dies it is still in the list.
	private string NearestLiveShip()
	{
		float best = float.MaxValue;
		foreach (PlayerShip s in oracle.GetShips())
		{
			if (s.IsDead)
			{
				continue;
			}
			float d = (s.Position - base.Position).Length();
			if (d < best)
			{
				best = d;
			}
		}
		return (best == float.MaxValue) ? "none" : best.ToString("0.0");
	}

	// ---- ?minelog (card 745728f9) --------------------------------------------------------------
	//
	// A mine's whole decision surface is invisible: a locked mine and a free one draw the same
	// sprite, and a mine that reached the end of its 1800 ms clock and one set off by a
	// neighbour's blast produce the identical pair of blue explosions. So the acquire, every
	// release and every death report themselves here, with the REASON.
	//
	// Every line also carries the nearest recorded player DEATH SPOT and its age, because that is
	// the correlation the card is about -- *'space mines seem to also explode when they reach a
	// dead player's location'*. The registry lives in `Compat/MineLog` rather than being read off
	// `Oracle`'s per-slot cached position: that one is overwritten the moment the slot respawns,
	// and `PlayerShipSummon` puts the new ship back exactly where the old one died, so an
	// instrument reading it is blind to the very window the report describes.
	//
	// **EVERYTHING IS BUILT INSIDE THE GUARD**, which is the `EvilSkull.ReportVolleyShot`
	// convention (card d8344c17) and not a style preference: these run from `Update` for every
	// live mine, and `NearestLiveShip` scans every ship. A shipped build must not concatenate a
	// string, or walk that list, for a flag that is off. So each reporter takes only cheap
	// arguments and does its own formatting.
	//
	// EVERY KEY IS UNIQUE ON A LINE, so a grep can anchor on one: the distance to the TARGET is
	// `targetD=` and only the death-spot's is `d=`. They were both `d=` first, which reads fine
	// and greps terribly.
	private string MineLogTail()
	{
		return $" t={EvilAliensWeb.Compat.WorldTime.Seconds * 1000f:F0}"
			+ EvilAliensWeb.Compat.MineLog.NearestDeathSpot(base.Position);
	}

	private void ReportAcquire()
	{
		if (EvilAliensWeb.Compat.MineLog.On)
		{
			Console.WriteLine($"[mine] acquire id={logId}"
				+ $" at={EvilAliensWeb.Compat.MineLog.Fmt(base.Position)}"
				+ $" target=slot{target.Owner}"
				+ $" targetAt={EvilAliensWeb.Compat.MineLog.Fmt(target.Position)}"
				+ $" targetD={(target.Position - base.Position).Length():F1}"
				+ MineLogTail());
		}
	}

	// Called BEFORE `target` is cleared, so it can still name what was let go. `IsDead` bails
	// because `Asplode` does not `break` out of the `attracted_to_player` case: after a
	// detonation this tick's remaining range test still runs, and a corpse reporting that it
	// released its target reads as a second event that never happened.
	private void ReportRelease(string reason)
	{
		if (EvilAliensWeb.Compat.MineLog.On && !base.IsDead)
		{
			Console.WriteLine($"[mine] release id={logId}"
				+ $" at={EvilAliensWeb.Compat.MineLog.Fmt(base.Position)}"
				+ $" reason={reason} lockMs={lockedMs:F0}"
				+ $" targetD={((target == null) ? "-" : ((target.Position - base.Position).Length()).ToString("F1"))}"
				+ MineLogTail());
		}
	}

	// A quiet death -- shot, wall, off the bottom of the screen. Same `IsDead` bail as
	// `ReportRelease`, and for the same tick: `OffScreen` is tested after `Asplode` could
	// already have run.
	private void ReportDie(string reason)
	{
		if (EvilAliensWeb.Compat.MineLog.On && !base.IsDead)
		{
			Console.WriteLine($"[mine] die id={logId}"
				+ $" at={EvilAliensWeb.Compat.MineLog.Fmt(base.Position)}"
				+ $" reason={reason} state={state}"
				+ MineLogTail());
		}
	}

	// `target=` and `lockMs=` are reported as `none`/`-` unless the mine is ACTUALLY locked,
	// and that is the finding this instrument exists to avoid making itself. Neither field is
	// cleared when a lock ends by RANGE (`target` is deliberately kept so the release test can
	// re-read it, and `lockedMs` is only rezeroed at the next acquire), so a mine set off later
	// by a neighbour's blast would otherwise print `state=free target=slot1 lockMs=1817` --
	// naming a ship it stopped homing on and a lock that had already ended. Those are the two
	// fields the card's whole analysis leans on, so a stale reading is exactly the false
	// positive `live=` was added to prevent.
	private void ReportBoom(string reason)
	{
		if (EvilAliensWeb.Compat.MineLog.On)
		{
			bool locked = state == MineState.attracted_to_player && target != null;
			// `live=` is the field that makes `deathspot=` READABLE, and it was added because
			// without it the first measurement of this card was a false positive. A mine can
			// only ever detonate on its own clock while it is pulling toward a LIVE ship, so a
			// detonation 'at a dead player's location' decomposes into two very different
			// events: the survivor was standing on their partner's corpse (live= small --
			// ordinary homing, and the normal case in co-op, where players fly together), or the
			// mine went off with no live ship anywhere near it (live= large -- the report).
			// Reporting only the distance to the corpse cannot tell them apart.
			Console.WriteLine($"[mine] boom id={logId}"
				+ $" at={EvilAliensWeb.Compat.MineLog.Fmt(base.Position)}"
				+ $" reason={reason} state={state}"
				+ $" target={(locked ? ("slot" + target.Owner) : "none")}"
				+ $" lockMs={(locked ? lockedMs.ToString("F0") : "-")}"
				+ $" clock={(timer.Active ? timer.TimeElapsed.ToString("F0") : "-")}"
				+ $" live={NearestLiveShip()}"
				+ MineLogTail());
		}
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
				// ComponentBin's removal FLUSH, not at Die(), so for the rest of the tick in which
				// a ship died it is still in this list with `IsDead` already true -- "in the list"
				// is not "alive". THAT ONE TICK is the whole window this guard closes: from the
				// flush onward `OnComponentRemoved` (below) has already dropped the corpse out of
				// `GetShips()` by itself. See the `attracted_to_player` branch for the measurement,
				// and for what the card's report is still NOT explained by.
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
				lockedMs = 0f;
				ReportAcquire();
			}
			break;
		}
		case MineState.attracted_to_player:
		{
			// THE LOCK IS ON A LIVE SHIP (card 745728f9).
			//
			// **THIS IS HARDENING, NOT THE CARD'S REPORTED BUG -- do not read it as the fix for
			// "space mines seem to also explode when they reach a dead player's location".** An
			// earlier cut of this change claimed `target` was only ever cleared by the release-
			// RANGE test, so a mine flew to a corpse and detonated there 1800 ms later. That is
			// FALSE, and `OnComponentRemoved` (above) is why: `PlayerShip.Asplode` -> `Die()`
			// queues `collection.Remove(this)`, and the flush fires `ComponentRemoved`, which
			// this class already watched -- it nulls `target`, and drops the ship out of
			// `Oracle.GetShips()` off the same event. MEASURED in a real flushed world: the
			// target is null before the mine's next Update runs at all, with every guard on this
			// line removed. So the pre-card window was ONE TICK (~17 ms), never 1800 ms.
			//
			// What this line is still worth: that one tick is real (a mine CAN acquire or hold a
			// corpse between `Die()` and the flush), and `PlayerShip` is POOLED -- a dead target's
			// instance can be handed back out by `Recycle<PlayerShip>` for a respawn, at which
			// point a mine that kept the reference would be homing on a live ship it never
			// acquired, on somebody else's timer. `IsDead` closes both.
			//
			// The third clause is belt-and-braces, NOT the load-bearing test: a ship leaves
			// `GetShips()` and gets nulled here off the same event, so `target` is already null by
			// the time the scan could miss it. It is kept because the two lists are maintained by
			// two independent handlers and a future reordering of them is exactly the kind of
			// change nobody would think to re-verify here.
			//
			// Going back to `free` rather than just dropping the pull is deliberate: `free` does
			// not consult `timer`, so a mine whose target dies cannot detonate on the old clock,
			// and a re-acquire Resets it.
			//
			// STILL UNEXPLAINED, and the card's first half is NOT closed by any of this: what
			// actually detonates a mine at a dead player's spot. Refuted so far, with evidence --
			// (a) the 1800 ms flight to a corpse, above; (b) chain-detonation on the player's own
			// death explosions: `CollidesWith` does `Asplode()` on any `Explosion`, but an
			// `Explosion` only sets `Collides` while its `collisiontimer` runs and ONLY
			// `MakeBlue()` starts it -- `PlayerShip.Asplode`'s two explosions are never made blue,
			// so they are inert. Measured alongside: a freed mine keeps its inward SpeedVector and
			// coasts through the death spot (200px out -> 5px at t=58 ticks) without detonating.
			if (target == null || target.IsDead || !oracle.GetShips().Contains(target))
			{
				ReportRelease((target == null) ? "targetnull"
					: (target.IsDead ? "targetdead" : "targetgone"));
				target = null;
				state = MineState.free;
				connectToBackground();
				break;
			}
			lockedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
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
				Asplode("timer");
			}
			Vector2 toTarget = target.Position - base.Position;
			if ((toTarget).LengthSquared() >= releaseRange * releaseRange)
			{
				ReportRelease("range");
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
			ReportDie("offscreen");
			Die();
		}
	}

	// `why` is for the `?minelog` line only -- the three call sites are the 1800 ms clock, a
	// neighbour's blue blast, and a peer replaying the host's self-destruct, and no frame,
	// counter or explosion tells them apart.
	private void Asplode(string why)
	{
		if (!base.IsDead)
		{
			ReportBoom(why);
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
		// DELIBERATELY NOT `IsDead`-filtered, unlike the acquire loop in Update (card 745728f9).
		// This is a hold-fire test, so counting the one tick's corpse errs toward NOT shooting --
		// the safe direction, and 2008 behaviour. Skipping corpses here would make a mine start
		// firing next to a body, which is a new behaviour nobody asked for.
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
				ReportDie("wall");
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
			Asplode("explosion");
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
		ReportDie("shot");
		Die();
	}

	// Card 4e406eba: the mine's self-destruct looks nothing like being shot -- Asplode() is two
	// big BLUE bursts and "expl2", KilledBy is one small white burst and "expl1" -- so the peer
	// replays the real thing. Asplode() guards on IsDead and ends in Die(), so it is safe to
	// call on a puppet and removes it itself.
	internal override void NetReplayUnattributedDeath(ICollidable agent)
	{
		Asplode("netreplay");
	}

	internal void AttractByBoss(JunkBoss junkBoss)
	{
		if (hitpointsattached > 0)
		{
			if (boss == null)
			{
				junkBoss.AddChild();
			}
			// ?minelog: the boss capture ENDS a player lock without going through the release
			// test, so without this the trace shows an acquire with no matching release. It
			// reports only -- `target` and `lockedMs` are deliberately left alone, exactly as
			// before, and `ReportBoom` reads them as `none`/`-` from this state anyway.
			if (state == MineState.attracted_to_player)
			{
				ReportRelease("boss");
			}
			state = MineState.attracted_to_boss;
			boss = junkBoss;
			disconnectFromBackground();
		}
	}
}
