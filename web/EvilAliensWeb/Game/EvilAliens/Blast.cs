using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Blast : AlienDrawableGameComponent, IAlienKiller
{
	private Timer lifetime = new Timer(2500f, repeating: false);

	// Counter-rotation rate of the two blast layers (radians/sec); the opposite signs
	// churn the crackly plasma rim against itself so the blast reads as alive.
	private const float SpinSpeed = 1.3f;

	private float power;

	private bool mini;

	private int player;

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public bool IsMini => mini;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			c.Position = base.Position;
			c.Radius = (float)texture.Width * 0.5f * 0.8f * scale;
			return c;
		}
	}

	public Blast(Game game)
		: base(game)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Sprites/blast"));
		base.DrawOrder = 20;
		timers.Add(lifetime);
	}

	public static Blast NewBlast(ComponentBin collection, Game game)
	{
		Blast blast = collection.Recycle<Blast>();
		if (blast == null)
		{
			blast = new Blast(game);
		}
		return blast;
	}

	public void Setup(Vector2 position, int power, int player)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		this.player = player;
		mini = false;
		base.Position = position;
		this.power = power + 1;
		lifetime.Duration = 1000f * this.power;
	}

	public void SetupAsMini(Vector2 position, float lifetime, int player)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		this.player = player;
		mini = true;
		base.Position = position;
		this.lifetime.Duration = lifetime;
	}

	public override void Initialize()
	{
		base.Collides = true;
		lifetime.Start();
		lifetime.Reset();
		scale = 0f;
		// Update() overwrites scale + color from the lifetime curve before the first Draw,
		// so in-game this 0 baseline is never seen. The sprite harness freezes Update, though,
		// so a 0 scale would draw the blast invisibly there; park a visible size, gated on the
		// harness so gameplay (incl. the spawn-frame collision radius derived from scale) stays
		// byte-identical. See HarnessRegistry "blast" / the Braineroid pulsate=1 precedent.
		if (EvilAliensWeb.Compat.DebugFlags.Harness != null)
		{
			scale = 2.5f;
		}
		// A random starting angle so the counter-rotating layers (see Draw) start at a
		// different phase per blast — overlapping bombs never look stamped from one frame.
		rotation = RandomHelper.RandomNextAngle();
		base.Initialize();
	}

	// One static disc that only grew + faded looked lifeless. Draw TWO half-opacity copies
	// counter-rotating about the centre so the crackly plasma rim churns against itself and
	// the blast reads as alive (Trello card: "layer 2 together, each half the alpha, rotate
	// both in different directions"). Spin is time-based so it animates even in the frozen
	// sprite harness; the per-blast random base rotation desynchronises overlapping blasts.
	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = blendMode;
		float spin = (float)gameTime.TotalGameTime.TotalSeconds * SpinSpeed;
		Vector4 c = color.ToVector4();
		Color layer = new Color(new Vector4(c.X, c.Y, c.Z, c.W * 0.5f));
		spriteBatch.Draw(texture, Position, rotation + spin, DrawScale, center: true, layer, spriteEffects);
		spriteBatch.Draw(texture, Position, rotation - spin, DrawScale, center: true, layer, spriteEffects);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		float num = MyMath.PowerCurve(0f, 1f, 0.3f, 1f - lifetime.Normalized);
		if (!mini)
		{
			scale = num * MyMath.PowerCurve(1.3f, 3.5f, 1.5f, (power - 1f) / 4f);
		}
		else
		{
			scale = num * 0.45f;
		}
		color = new Color(new Vector4(1f, 1f, 1f, 1f - num));
		if (num >= 0.81f)
		{
			base.Collides = false;
		}
		if (lifetime.Finished)
		{
			Die();
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	public void SetPosition(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
	}

	public bool CausesCombo()
	{
		return IsMini;
	}

	public bool CanHitBosses()
	{
		return false;
	}

	public int Player()
	{
		return player;
	}
}
