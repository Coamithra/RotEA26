using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class Lvl1StartDemoEvent : GameEvent
{
	// The "hail of bullets" half of the Level 1 intro, factored out of the state machine below
	// so the HOST and a co-op JOIN PEER emit it from ONE place (card 8a7772d6).
	//
	// WHY THE JOINER NEEDS ITS OWN COPY AT ALL: `Bullet` is not in NetTypeRegistry -- the
	// player's bullets are never replicated, a remote ship's are re-fired locally off the fire
	// stream -- and this volley is fired by the level script, which is host-only. So the joiner
	// used to watch the twenty intro UFOs pop with nothing visibly killing them.
	//
	// THE CLIENT COPY IS COSMETIC AND THAT IS A CONTRACT, not a shortcut: it must not be able to
	// kill a puppet, file an EvClaim or drop a damaging mini-Blast, because the host's own volley
	// is already doing all of that authoritatively. See the `cosmetic` branch for the two
	// concrete differences and what each one costs visually.
	public sealed class Volley
	{
		public const int BulletCount = 70;

		private const float IntervalMs = 33f;

		private const float LifetimeMs = 3000f;

		private const int BounceCount = 100;

		private const float AsplodingSize = 5000f;

		// Fired from just below the bottom edge, up the middle -- the ship arrives from the
		// same place moments later, which is the shot the intro is setting up.
		private static readonly Vector2 Origin = new Vector2(400f, 799f);

		private const float MinAngle = (float)Math.PI * -3f / 4f;

		private const float MaxAngle = -(float)Math.PI / 4f;

		// PRIVATE and SEEDED, never RandomHelper.Random -- the Quad/ShipConnector rule. Both
		// peers seed it from the same wire value so the volley leaves the tube at the same
		// angles on both screens. It cannot stay matched past the first RICOCHET (a bounce
		// re-rolls off the shared RNG, and the client's copy does not bounce at all), so read
		// this as "the same volley", not "the same trajectories".
		private readonly Random rng;

		private readonly bool cosmetic;

		private readonly Timer timer = new Timer(IntervalMs, repeating: false);

		private int created;

		public bool Finished => created >= BulletCount;

		// Readbacks for NetIntroGateTest. A Volley's only other output is bullets in the bin,
		// and a Bullet's heading is `protected` -- so without these the emitter's count and its
		// seed-determinism could only be checked by reaching into game internals.
		public int Fired => created;

		public float LastAngle { get; private set; }

		public Volley(int seed, bool cosmetic)
		{
			rng = new Random(seed);
			this.cosmetic = cosmetic;
			timer.Reset();
			timer.Start();
		}

		public void Update(GameTime gameTime, ComponentBin collection, Game game)
		{
			timer.Update(gameTime);
			if (Finished || !timer.Finished)
			{
				return;
			}
			Bullet bullet = Bullet.NewBullet(collection, game);
			LastAngle = NextAngle();
			bullet.Setup(Origin, LastAngle, LifetimeMs, 0);
			if (!cosmetic)
			{
				// The host's volley is the real one: it ricochets off the formation (which is
				// what actually clears it) and each bullet pops into a mini blast.
				bullet.SetBouncing(BounceCount);
				bullet.SetAsploding(AsplodingSize);
			}
			collection.Add((GameComponent)(object)bullet);
			if (cosmetic)
			{
				// AFTER the Add, because Bullet.Initialize sets Collides itself and KNI runs
				// Initialize synchronously inside the Add. A colliding copy would hit the
				// joiner's frozen puppets and file claims for kills the host's own volley is
				// already being credited with. What switching it off costs: no ricochets, so
				// these fly straight out of the top instead of scattering. SetAsploding is
				// skipped for the same reason -- the mini Blast it drops on death damages.
				bullet.Collides = false;
			}
			created++;
			timer.Reset();
			timer.Start();
		}

		private float NextAngle()
		{
			return (float)(rng.NextDouble() * (MaxAngle - MinAngle)) + MinAngle;
		}
	}

	private enum demostate
	{
		wait,
		createufos,
		wait2,
		createbullets,
		wait3,
		done
	}

	private Timer timer = new Timer(0f, repeating: false);

	private demostate state;

	private int ufoscreated;

	private Volley volley;

	public Lvl1StartDemoEvent(Game game)
		: base(game, 0f)
	{
	}

	public override void Reset()
	{
		timer.Duration = 10f;
		timer.Reset();
		timer.Start();
		base.Reset();
		state = demostate.wait;
		ufoscreated = 0;
		volley = null;
	}

	public override void Update(GameTime gameTime)
	{
		timer.Update(gameTime);
		switch (state)
		{
		case demostate.wait:
			if (timer.Finished)
			{
				timer.Duration = 300f;
				timer.Reset();
				timer.Start();
				state = demostate.createufos;
				UFO uFO2 = UFO.NewUFO(collectionHelper, game);
				uFO2.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 648f), isBig: false, EnemyBehaviour.normal);
				uFO2.SetAsBonus();
				collectionHelper.Add((GameComponent)(object)uFO2);
			}
			break;
		case demostate.createufos:
			if (timer.Finished)
			{
				UFO uFO = UFO.NewUFO(collectionHelper, game);
				uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 648f), isBig: false, EnemyBehaviour.normal);
				collectionHelper.Add((GameComponent)(object)uFO);
				ufoscreated++;
				timer.Reset();
				timer.Start();
			}
			if (ufoscreated == 20)
			{
				timer.Duration = 100f;
				timer.Reset();
				timer.Start();
				state = demostate.wait2;
			}
			break;
		case demostate.wait2:
			if (timer.Finished)
			{
				// Card 8a7772d6: announce the volley (and its seed) to a co-op join peer as we
				// start it, so its own cosmetic copy runs alongside ours. No-op with no peer.
				int seed = RandomHelper.Random.Next();
				volley = new Volley(seed, cosmetic: false);
				EvilAliensWeb.Compat.Net.NetSession.OnIntroVolley(seed);
				state = demostate.createbullets;
			}
			break;
		case demostate.createbullets:
			volley.Update(gameTime, collectionHelper, game);
			if (volley.Finished)
			{
				timer.Duration = 2000f;
				timer.Reset();
				timer.Start();
				state = demostate.wait3;
			}
			break;
		case demostate.wait3:
			if (timer.Finished)
			{
				timer.Duration = 80f;
				timer.Reset();
				timer.Start();
				state = demostate.done;
			}
			break;
		case demostate.done:
			Terminate();
			break;
		}
		base.Update(gameTime);
	}
}
