using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// A quick electric ZAP burst — what a WebcamPlasma orb pops into when it reaches the player,
// INSTEAD of an explosion (electricity doesn't explode). A short-lived bloom flash plus a set
// of jagged lightning streaks radiating out, built from the laser's own crackle recipe (the
// lazermiddle strip + lazerglow bloom + a midpoint-displaced bolt, like Quad.DrawArcs). Pure
// additive cosmetic; no collision, no gameplay (WebcamLevel still docks the life via PlayerHit).
internal class WebcamZap : AlienDrawableGameComponent
{
	private const float LifeMs = 280f;

	private const int StreakCount = 8;

	private const int BoltSegments = 3;

	// electric palette (matches the plasma / laser)
	private static readonly Color CoreColor = new Color(210, 235, 255);
	private static readonly Color GlowColor = new Color(45, 120, 235);
	private static readonly Color FlareColor = new Color(150, 215, 255);

	private Texture2D strip;   // GFX/Sprites/lazermiddle
	private Texture2D bloom;   // GFX/Sprites/lazerglow

	private float elapsed;
	private float sizeScale = 1f;

	// per-streak base angle + reach, rolled at spawn
	private readonly float[] angle = new float[StreakCount];
	private readonly float[] reach = new float[StreakCount];

	private CollisionSimpleCircle noHit = new CollisionSimpleCircle(Vector2.Zero, 0f);

	public override ICollisionType CollisionType
	{
		get { noHit.Position = base.Position; return noHit; }
	}

	public WebcamZap(Game game)
		: base(game)
	{
		base.DrawOrder = 810;   // just above the plasma (800)
		base.Collides = false;
		PointValue = 0f;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		strip = content.Load<Texture2D>("GFX/Sprites/lazermiddle");
		bloom = content.Load<Texture2D>("GFX/Sprites/lazerglow");
	}

	public static WebcamZap NewWebcamZap(ComponentBin collection, Game game)
	{
		WebcamZap zap = collection.Recycle<WebcamZap>();
		if (zap == null)
		{
			zap = new WebcamZap(game);
		}
		return zap;
	}

	// position: where the orb popped. sizeScale scales the whole burst (1 ~ a saucer-plasma orb).
	public void Setup(Vector2 position, float sizeScale)
	{
		base.Position = position;
		this.sizeScale = sizeScale;
		elapsed = 0f;
		for (int i = 0; i < StreakCount; i++)
		{
			// evenly spread + jittered so they fan out all around, each a different length
			angle[i] = (float)(i * (Math.PI * 2.0 / StreakCount)) + RandomHelper.RandomNextFloat(-0.35f, 0.35f);
			reach[i] = RandomHelper.RandomNextFloat(45f, 90f) * sizeScale;
		}
	}

	public override void Initialize()
	{
		base.Initialize();
		elapsed = 0f;
	}

	public override void Update(GameTime gameTime)
	{
		elapsed += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (elapsed >= LifeMs)
		{
			Die();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		float p = MathHelper.Clamp(elapsed / LifeMs, 0f, 1f);
		// bright early, fades out (a pop)
		float fade = (float)Math.Pow(1f - p, 0.7);
		// streaks shoot out fast then hold
		float grow = MathHelper.Min(1f, p * 3.2f);

		SpriteBlendMode old = spriteBatch.BlendMode;
		spriteBatch.BlendMode = (SpriteBlendMode)2;   // Additive

		// bloom: a wide blue flash + a hot white core, growing as it fades
		float bloomD = (60f + 90f * grow) * sizeScale;
		DrawBloom(bloomD, GlowColor * (0.9f * fade));
		DrawBloom(bloomD * 0.55f, FlareColor * fade);

		// radiating lightning streaks
		for (int i = 0; i < StreakCount; i++)
		{
			Vector2 dir = new Vector2((float)Math.Cos(angle[i]), (float)Math.Sin(angle[i]));
			Vector2 end = base.Position + dir * (reach[i] * grow);
			DrawBolt(base.Position, end, fade);
		}

		spriteBatch.BlendMode = old;
	}

	private void DrawBloom(float diameter, Color color)
	{
		float s = diameter / (float)bloom.LogicalWidth();
		spriteBatch.Draw(bloom, base.Position, 0f, new Vector2(s, s), center: true, color);
	}

	// A jagged bolt from a to b (midpoint displacement, re-rolled each frame for crackle), drawn
	// as a wide dim glow pass + a thin hot core — the laser-tendril recipe, standalone.
	private void DrawBolt(Vector2 a, Vector2 b, float fade)
	{
		Vector2 d = b - a;
		float len = d.Length();
		if (len < 1f)
		{
			return;
		}
		Vector2 dir = d / len;
		Vector2 perp = new Vector2(0f - dir.Y, dir.X);
		Vector2 prev = a;
		float amp = len * 0.22f;
		for (int seg = 1; seg <= BoltSegments; seg++)
		{
			float f = (float)seg / BoltSegments;
			Vector2 pt = (seg == BoltSegments)
				? b
				: Vector2.Lerp(a, b, f) + perp * RandomHelper.RandomNextFloat(-amp, amp);
			DrawSeg(prev, pt, 7f, GlowColor * (0.7f * fade));
			DrawSeg(prev, pt, 2.5f, CoreColor * fade);
			prev = pt;
		}
	}

	private void DrawSeg(Vector2 p0, Vector2 p1, float thickness, Color color)
	{
		Vector2 d = p1 - p0;
		float len = d.Length();
		if (len < 0.5f)
		{
			return;
		}
		float rot = (float)Math.Atan2(0f - d.X, d.Y);
		Vector2 scale = new Vector2(thickness / (float)strip.LogicalWidth(), len / (float)strip.LogicalHeight());
		spriteBatch.Draw(strip, (p0 + p1) * 0.5f, rot, scale, center: true, color);
	}
}
