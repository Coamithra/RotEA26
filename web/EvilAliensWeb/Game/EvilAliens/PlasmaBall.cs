using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			c.Position = base.Position;
			// texture.Width is the DOWNSCALED redraw (plasmaball2: 523px vs design 697); DrawScale
			// removes the supersample factor so the hitbox tracks the visible disc, not the raw PNG
			// (matches Draw's DrawScale — the Blast/Braineroid supersample bug class, inverted).
			c.Radius = (float)texture.Width * 0.32f * DrawScale;
			return c;
		}
	}

	public PlasmaBall(Game game)
		: base(game)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
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
}
