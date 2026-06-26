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

	// Default fraction of the visible radius that deals damage, and the fade-alpha floor below
	// which the blast goes inert. Both are overridable from the URL (?blasthit= / ?blastactive=)
	// so the look can be tuned live in the sprite harness; null override => these bake into a
	// shipped build unchanged. See ApplyLifecycle + Compat/DebugFlags.cs.
	private const float DefaultHitRadiusFactor = 0.8f;
	private const float DefaultActiveAlpha = 0.5f;

	private static float HitRadiusFactor => EvilAliensWeb.Compat.DebugFlags.BlastHitFactor ?? DefaultHitRadiusFactor;
	private static float ActiveAlpha => EvilAliensWeb.Compat.DebugFlags.BlastActiveAlpha ?? DefaultActiveAlpha;

	public bool IsMini => mini;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			c.Position = base.Position;
			// DrawScale (not raw scale): blast.png is a 1.5x supersampled sheet, so sizing the
			// hitbox off texture.Width * scale made it 1.5x too big — damage reached well outside
			// the visible disc (the "active bigger than the sprite suggests" half of the bug).
			// DrawScale divides the supersample back out, restoring the intended 0.8x-of-visible
			// radius regardless of the sheet's texel resolution.
			c.Radius = (float)texture.Width * 0.5f * HitRadiusFactor * DrawScale;
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

	// One static disc that only grew + faded looked lifeless. Draw TWO copies of the plasma
	// sprite, each at HALF the lifetime alpha, counter-rotating about the centre so the crackly
	// rim churns against itself and the blast reads as alive (Trello card: "layer 2 together,
	// each half the alpha, rotate both in different directions"). Two straight-alpha halves
	// composite a touch dimmer than the old single full-alpha disc — that softening is the
	// intended look, per the card. Spin is time-based so it animates even in the frozen sprite
	// harness; the per-blast random base rotation (Initialize) desyncs overlapping blasts in
	// gameplay (the harness overrides rotation via ?rot, so that desync isn't visible there).
	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = blendMode;
		float spin = ((float)gameTime.TotalGameTime.TotalSeconds * SpinSpeed) % MathHelper.TwoPi;
		Vector4 c = color.ToVector4();
		Color layer = new Color(new Vector4(c.X, c.Y, c.Z, c.W * 0.5f));
		spriteBatch.Draw(texture, Position, rotation + spin, DrawScale, center: true, layer, spriteEffects);
		spriteBatch.Draw(texture, Position, rotation - spin, DrawScale, center: true, layer, spriteEffects);
	}

	public override void Update(GameTime gameTime)
	{
		ApplyLifecycle(1f - lifetime.Normalized);
		if (lifetime.Finished)
		{
			Die();
		}
		base.Update(gameTime);
	}

	// Drive the blast's appearance + hit state from its elapsed fraction p (0 = just spawned,
	// 1 = end of life). Pulled out of Update so the sprite harness can scrub the SAME curve
	// without running gameplay (see HarnessApplyPhase).
	//
	// scale grows on the old punchy power-0.3 curve (unchanged). The fade, though, is now a
	// SMOOTHSTEP instead of `1 - p^0.3`: the old curve dimmed the disc to ~half opacity within
	// the first ~10% of life, so the blast "looked gone" long before it stopped dealing damage —
	// the "active longer than the sprite suggests" complaint. SmoothStep holds the blast clearly
	// visible through its active window, then eases cleanly to 0. Collision is tied to that fade
	// (active while alpha >= ActiveAlpha), so "dangerous" now coincides with "clearly visible".
	private void ApplyLifecycle(float p)
	{
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		float grow = MyMath.PowerCurve(0f, 1f, 0.3f, p);
		if (!mini)
		{
			scale = grow * MyMath.PowerCurve(1.3f, 3.5f, 1.5f, (power - 1f) / 4f);
		}
		else
		{
			scale = grow * 0.45f;
		}
		float fade = MathHelper.SmoothStep(1f, 0f, p);
		color = new Color(new Vector4(1f, 1f, 1f, fade));
		base.Collides = fade >= ActiveAlpha;
	}

	// Harness-only: park the blast at elapsed fraction p and apply the harness's scale multiplier,
	// so the sprite harness can LOOP + visualise the growth/fade/active window without running the
	// gameplay Update (which would Die at end of life). Production never calls this.
	internal void HarnessApplyPhase(float p, float scaleMul)
	{
		ApplyLifecycle(MathHelper.Clamp(p, 0f, 1f));
		scale *= scaleMul;
	}

	// Harness readout only: the blast's current fade alpha, read straight from the live color the
	// curve just set — so the viz can never drift from ApplyLifecycle if the fade is ever retuned.
	internal float CurrentFadeAlpha => color.ToVector4().W;

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
