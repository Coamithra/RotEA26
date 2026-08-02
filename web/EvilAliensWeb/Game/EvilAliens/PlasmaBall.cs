using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class PlasmaBall : AlienDrawableGameComponent
{
	private enum PlasmaBallState
	{
		entry,
		fly
	}

	private const float scalemodifier = 0.25f;

	// Drawn additively at each rotation to fake a flickering-lightning ball. The
	// original art was a soft dim plasma so it took 3 layers; the upscaled sprite is a
	// fully-rendered bright electric orb, so 2 additive layers already read as lightning
	// without blowing out to white (was 3 -- small-sprite upscale effort).
	private float[] rotations = new float[2];

	private PlasmaBallState state;

	private Timer stateTimer = new Timer(1000f, repeating: false);

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			c.Position = base.Position;
			// texture.Width is the DOWNSCALED redraw (plasmaball2: 523px vs design 697); DrawScale
			// removes the supersample factor so the hitbox tracks the visible disc, not the raw PNG
			// (matches Draw's DrawScale — the Blast/Braineroid supersample bug class, inverted).
			c.Radius = (float)texture.LogicalWidth() * 0.32f * DrawScale;
			return c;
		}
	}

	public PlasmaBall(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/plasmaball2"));
		base.DrawOrder = 800;
		color = Color.LightBlue;
		blendMode = (SpriteBlendMode)2;
		timers.Add(stateTimer);
	}

	public static PlasmaBall NewAlien(ComponentBin collection, Game game)
	{
		PlasmaBall plasmaBall = collection.Recycle<PlasmaBall>();
		if (plasmaBall == null)
		{
			plasmaBall = new PlasmaBall(game);
		}
		return plasmaBall;
	}

	public void Setup(Vector2 position, float direction)
	{
		base.Position = position;
		base.Direction = direction;
		base.Speed = 0.06f;
		base.MaxSpeed = 0.6f;
		base.Acceleration = 0.00029999999f;
	}

	public override void Initialize()
	{
		base.Initialize();
		state = PlasmaBallState.entry;
		stateTimer.Start();
		scale = 0.025f;
		for (int i = 0; i < rotations.Length; i++)
		{
			rotations[i] = RandomHelper.RandomNextAngle();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		for (int i = 0; i < rotations.Length; i++)
		{
			rotation = rotations[i];
			base.Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (RandomHelper.RandomFromAverage(10f, gameTime))
		{
			int num = RandomHelper.Random.Next(rotations.Length);
			rotations[num] = RandomHelper.RandomNextAngle();
		}
		for (int i = 0; i < rotations.Length; i++)
		{
			float num2 = 1f;
			if (i % 2 == 0)
			{
				num2 = -1f;
			}
			rotations[i] += (float)Math.PI / 2f * num2 * (float)gameTime.ElapsedGameTime.TotalSeconds;
		}
		switch (state)
		{
		case PlasmaBallState.entry:
			scale = MathHelper.SmoothStep(0.25f, 0.025f, stateTimer.Normalized);
			if (stateTimer.Finished)
			{
				state = PlasmaBallState.fly;
				scale = 0.25f;
			}
			break;
		case PlasmaBallState.fly:
			Move((float?)base.Direction, gameTime);
			break;
		}
		base.Update(gameTime);
		if (OffScreen(200f))
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/PlasmaBallDescriptor) ------
	// The final boss's "electricity balls" (BrainBoss.Update spawns these). A frozen client puppet
	// never runs Update, so both crackle angles held at their spawn values and the orb was a STILL
	// IMAGE -- the reported "they don't animate" (card 435db27f). The descriptor header noted it as
	// an accepted cosmetic loss; it is not accepted any more, and it costs no wire bytes because
	// the whole thing is locally simulable: a fixed +-PI/2 rad/s counter-spin plus a re-roll on an
	// average-10/s coin flip. Nothing reads `rotations` but Draw, and the angles were ALREADY
	// per-instance random and so never matched across peers -- so running them locally is exactly
	// as correct as the host's copy and strictly better than a freeze.
	//
	// PRIVATE RNG, never RandomHelper.Random -- the Quad / ShipConnector rule. A per-frame draw off
	// the shared generator from a client-only path desynchronises every other consumer of it on
	// that peer, and this runs once per puppet per tick.
	//
	// Real dt, like everything else NetPuppets.Drive hands out.
	private static readonly System.Random netSpinRng = new System.Random();

	internal override void NetDriveExtras(GameTime gameTime)
	{
		base.NetDriveExtras(gameTime);
		float dtSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
		// The live Update rolls this through RandomFromAverage(10f); reproduce the RATE directly
		// rather than the call, since that helper reads the shared generator.
		if (netSpinRng.NextDouble() < 10f * dtSeconds)
		{
			rotations[netSpinRng.Next(rotations.Length)] =
				(float)(netSpinRng.NextDouble() * Math.PI * 2.0);
		}
		for (int i = 0; i < rotations.Length; i++)
		{
			float dir = ((i % 2 == 0) ? (-1f) : 1f);
			rotations[i] += (float)Math.PI / 2f * dir * dtSeconds;
		}
	}
}
