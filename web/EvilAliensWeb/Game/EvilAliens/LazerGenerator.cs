using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class LazerGenerator : AlienDrawableGameComponent
{
	private bool silent;

	private bool freed;

	private Vector2 impulse = Vector2.Zero;

	private LazerGeneratorData[] particles;

	private SoundEffectInstance sfx;

	private float size = 1f;

	private float lifetime = 1f;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	// --- Chargeup ramp + "energy well" (Trello "improve laser animation") -----------------
	// The chargeup no longer draws at a flat scale. Its per-particle scale RAMPS 1 -> peak over the
	// windup (ease-out, near-linear), and a pulsing "energy well" forms at the convergence centre --
	// a white-hot orb (layered like the laser TIP) that grows to ~1.6x the tip while fluctuating
	// erratically in size, reading as energy gathering before it bursts out as the beam. `progress` drives
	// both; it's `elapsed / windupSeconds` so the animation stretches to WHATEVER windup the caller
	// passes (varies per UFO/laser + difficulty -- see SetWindup).
	private float elapsed;
	private float windupSeconds = 2.5f; // fallback; callers pass the real (difficulty-scaled) duration
	private bool loopWindup;            // showcase loops the ramp to watch it; in-game plays once + holds
	private Texture2D wellTex;          // GFX/Sprites/lazerglow -- the SAME clean radial glow as the beam tip, lazy
	private const float DefaultPeakChargeScale = 4f; // ramp target (was a flat 5x); ?lazerchargescale overrides
	private const float ChargeEase = 1.4f;           // ease-out exponent (near-linear) for the 1->peak ramp
	private const float LaserTipDiameter = 48f;      // Quad beam width(16) x TipFlareScale(3) = the real tip bloom
	private const float WellTipFactor = 1.6f;        // energy-well final size vs the laser tip (a touch bigger = stored energy)
	private const float WellSeedFrac = 0.15f;        // well grows from 15% -> 100% of its final size
	// The well is drawn as a STACK of additive glows so its centre saturates white-hot with a blue
	// halo -- exactly how Quad's tip reads (blue glow cap + near-white core cap + cyan-white flare
	// stacked). A single flat-blue draw read too dim vs the tip. Colours == Quad's beam layers.
	private static readonly Color WellHaloColor = new Color(35, 110, 235);  // == Quad.GlowColor (blue outer halo)
	private static readonly Color WellMidColor = new Color(150, 215, 255);  // == Quad.FlareColor (cyan-white body)
	private static readonly Color WellCoreColor = new Color(210, 235, 255); // == Quad.CoreColor (white-hot centre)
	private static float PeakChargeScale => EvilAliensWeb.Compat.DebugFlags.LazerChargeScale ?? DefaultPeakChargeScale;
	// Render-only RNG for the well's fluctuation phase (kept off the gameplay RandomHelper so it can't
	// desync a future lockstep co-op session, like Quad's fxr).
	private static readonly System.Random fxr = new System.Random();
	private readonly float fxPhase = (float)(fxr.NextDouble() * 1000.0);

	public override ICollisionType CollisionType => b;

	public LazerGenerator(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		base.Collides = false;
		LoadAnimation(new AnimationData("GFX/Menu/star"));
		base.DrawOrder = 40;
		particles = new LazerGeneratorData[10];
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i] = new LazerGeneratorData();
		}
		base.Visible = false;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			sound.Stop(sfx);
		}
	}

	public static LazerGenerator NewLazerGenerator(ComponentBin collection, Game game)
	{
		LazerGenerator lazerGenerator = collection.Recycle<LazerGenerator>();
		if (lazerGenerator == null)
		{
			lazerGenerator = new LazerGenerator(game);
		}
		return lazerGenerator;
	}

	public void Setup(Vector2 position, float size, float lifetime, float impulse, float direction)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		silent = false;
		base.Position = position;
		this.size = size;
		this.lifetime = lifetime;
		base.Direction = direction;
		this.impulse = MyMath.AngleToVector(direction) * impulse;
	}

	public override void Initialize()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			lazerGeneratorData.Initialize(size, lifetime, impulse);
		}
		if (!silent)
		{
			sfx = sound.Play("lazercharge");
		}
		elapsed = 0f;
		freed = false;
		base.Initialize();
	}

	// Windup progress, 0..1. `elapsed / windupSeconds` so the whole animation stretches to fit the
	// caller's charge duration; the showcase loops it, in-game plays once and holds at full.
	private float Progress()
	{
		float p = (windupSeconds > 0f) ? (elapsed / windupSeconds) : 1f;
		if (loopWindup) return p - (float)System.Math.Floor(p);
		return (p < 0f) ? 0f : ((p > 1f) ? 1f : p);
	}

	// Flexible windup: the caller passes the REAL charge duration (varies per UFO/laser + difficulty),
	// so the ramp + energy well always fill exactly the time before the beam fires. loop=true (the
	// showcase) repeats the ramp forever to watch it.
	internal void SetWindup(float seconds, bool loop)
	{
		windupSeconds = (seconds > 0.05f) ? seconds : 2.5f;
		loopWindup = loop;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		float progress = Progress();
		// Per-particle scale ramp: 1 -> peak over the windup, ease-out (near-linear).
		float ramp = 1f + (PeakChargeScale - 1f) * (1f - (float)System.Math.Pow(1f - progress, ChargeEase));
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		DrawWell(progress); // the gathering "energy well" glow, behind the sparks
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			if (!(lazerGeneratorData.lifetime <= 0f))
			{
				float num = 4f * lazerGeneratorData.normalizedLifetime * (1f - lazerGeneratorData.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(texture, base.Position + lazerGeneratorData.position, 0f, lazerGeneratorData.scale * ramp, center: true, val);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	// The "energy well": a glowing orb at the convergence centre that grows from a small seed to
	// ~1.6x the laser tip over the windup, fluctuating erratically (~90-110%) as energy gathers, then
	// bursts out as the beam. Drawn as a STACK of additive glows (same lazerglow texture Quad's tip
	// uses) so the centre saturates WHITE-HOT with a blue halo, matching the beam tip -- a single
	// mid-blue draw read too dim/flat. The hot core also brightens as the charge nears eruption.
	// Fades in over the first quarter so the showcase's loop restart isn't a hard pop.
	private void DrawWell(float progress)
	{
		if (wellTex == null)
		{
			wellTex = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/Sprites/lazerglow");
		}
		float g = progress;
		float baseD = LaserTipDiameter * WellTipFactor;
		float a = g * 4f;
		if (a > 1f) a = 1f;
		// NON-UNIFORM growth: each layer of the composite grows on its own STAGGERED schedule (the
		// diffuse outer glow swells first; the hot core condenses in later), so it reads as energy
		// gathering + focusing rather than one blob scaling. Grow() = delayed smoothstep 0..1.
		float pOuter = Grow(g, 0.00f);
		float pMid = Grow(g, 0.10f);
		float pCore = Grow(g, 0.24f);
		float pInner = Grow(g, 0.36f);
		// Per-layer erraticness gradient: the inner core wobbles SMOOTHEST (small, slow), the body a
		// bit more, the outer halo MOST (larger, faster) -- decorrelated phases so they don't pulse in
		// lockstep. Frequencies are low so it shimmers/breathes rather than jitters.
		float wOuter = Wobble(0.10f, 2.3f, fxPhase);
		float wMid = Wobble(0.065f, 1.7f, fxPhase * 1.7f);
		float wCore = Wobble(0.035f, 1.2f, fxPhase * 2.3f);
		float wInner = Wobble(0.022f, 1.0f, fxPhase * 3.1f);
		// Blue halo (wide) -> cyan-white body -> white-hot core: additively these saturate the centre
		// to white while leaving a blue rim, like the tip. The core layers fade in with their own
		// (delayed) growth, and the innermost hots up with progress so the well glares before eruption.
		DrawGlow(baseD * 1.30f * Size(pOuter) * wOuter, WellHaloColor * a);
		DrawGlow(baseD * 0.90f * Size(pMid) * wMid, WellMidColor * a);
		DrawGlow(baseD * 0.55f * Size(pCore) * wCore, WellCoreColor * (a * pCore));
		DrawGlow(baseD * 0.30f * Size(pInner) * wInner, WellCoreColor * (a * pInner * (0.4f + 0.6f * g)));
	}

	// Delayed smoothstep: 0 until `delay`, then eases 0..1 over the rest of the windup. Staggers the
	// well layers so they don't all grow in lockstep.
	private static float Grow(float g, float delay)
	{
		if (g <= delay) return 0f;
		float u = (g - delay) / (1f - delay);
		if (u > 1f) u = 1f;
		return u * u * (3f - 2f * u);
	}

	// A layer's growth 0..1 -> its diameter fraction: never below the small seed, up to full size.
	private static float Size(float p)
	{
		return WellSeedFrac + (1f - WellSeedFrac) * p;
	}

	// A gentle +/-amp size shimmer (two low, incommensurate sines -> a smooth wander, not a jitter).
	// Bigger amp + fscale => more erratic; each layer passes a different phase so they decorrelate.
	private float Wobble(float amp, float fscale, float phase)
	{
		float t = elapsed;
		return 1f + amp * (0.6f * (float)System.Math.Sin(t * 7f * fscale + phase)
			+ 0.4f * (float)System.Math.Sin(t * 11f * fscale + phase * 1.9f));
	}

	// One additive radial glow of the well texture, centred at the convergence point.
	private void DrawGlow(float diameter, Color color)
	{
		float s = diameter / (float)wellTex.LogicalWidth();
		spriteBatch.Draw(wellTex, base.Position, 0f, s, center: true, color);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (freed)
		{
			collection.Remove((GameComponent)(object)this);
		}
		bool flag = false;
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			lazerGeneratorData.Update(gameTime);
			if (lazerGeneratorData.lifetime > 0f)
			{
				flag = true;
			}
			if (lazerGeneratorData.lifetime <= 0f)
			{
				lazerGeneratorData.Initialize(size, lifetime, impulse);
			}
		}
		base.Update(gameTime);
		if (!flag)
		{
			collection.Remove((GameComponent)(object)this);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	public void SetPosition(Vector2 vector2)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = vector2;
	}

	internal void Free()
	{
		freed = true;
	}

	internal void SetupSilent()
	{
		silent = true;
	}
}
