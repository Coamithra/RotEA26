using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class FlyingSpider : KillableAlien
{
	// Per-frame wing-root anchor: DESIGN-space offset from the body centre (scaled by `scale`),
	// indexed by loop frame (0 = FirstFrame). Authored in tools/upscale/wing_editor.html so the
	// flapping wings stay glued to the reared body's back as the 9-frame loop (sheet 22..30)
	// plays. Re-run that tool to retune.
	private static readonly Vector2[] WingAnchors =
	{
		new Vector2(21.47f, 2.71f),  // loop 0 (sheet frame 22)
		new Vector2(20.49f, 3.44f),  // loop 1 (sheet frame 23)
		new Vector2(19.75f, 3.93f),  // loop 2 (sheet frame 24)
		new Vector2(19.51f, 4.18f),  // loop 3 (sheet frame 25)
		new Vector2(19.51f, 4.42f),  // loop 4 (sheet frame 26)
		new Vector2(19.51f, 4.42f),  // loop 5 (sheet frame 27)
		new Vector2(20.00f, 3.69f),  // loop 6 (sheet frame 28)
		new Vector2(20.74f, 3.44f),  // loop 7 (sheet frame 29)
		new Vector2(21.96f, 2.95f),  // loop 8 (sheet frame 30)
	};

	// Flying-spider size (Trello: "make the flying spiders slightly smaller since their graphic is
	// now larger with the stance"). The port reuses the reared-up HD sheet (frames 22..30) instead
	// of the OG 1x4 crawl sheet, which draws taller and a touch wider — measured on-screen silhouette
	// ~147x174 design px vs the OG's ~122x93 — so the enemy reads bigger than in the XBLIG. This
	// factor multiplies BOTH the foreground (1.0) and background (0.67) base scales to bring it back
	// toward the original; 0.75 was dialled in by eye in live Level 2 play (0.85 still read too big
	// in-game — the reared stance is inherently taller/wider than the OG crawl sheet). Both the sprite AND its box hitbox (sized off
	// the frame via DrawScale in retrieveBoundsFromTexture) shrink together, so collision keeps
	// tracking the visible size. (Update already scales the vertical swivel amplitude by `scale`,
	// so a smaller spider also bobs proportionally less — intended, and true of the OG coupling too.)
	// Live-tune by eye with ?flyspiderscale=<f> (null => this default); once the value feels right,
	// update this constant. See Compat/DebugFlags.cs.
	public const float DefaultSizeFactor = 0.75f;

	private static float SizeFactor =>
		EvilAliensWeb.Compat.DebugFlags.FlySpiderScale ?? DefaultSizeFactor;

	private bool isbackground;

	private Texture2D wing;

	private Timer swiveltimer = new Timer(2700f, repeating: true);

	private Timer flaptimer = new Timer(120f, repeating: true);

	private float startheight;

	// Net puppet only: forces Initialize's random grey-tint pick (foreground spiders only;
	// background ones are overridden to the fog colour) onto the host's choice. null in normal
	// play => the random pick. See NetForceColor.
	private byte? netForcedColorIndex;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft *= 0.95f;
			collisionBox.BottomRight *= 0.95f;
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public FlyingSpider(Game game)
		: base(game)
	{
		// Reuse the grounded spider's 7x7 rear-up sheet, looping only its "reared" sub-range
		// (packed frames 22..30 = source 44..60 at half fps) via FirstFrame/LastFrame. No
		// separate flying sheet -- the shared sheet carries the HD body; the wings (below) add
		// the flight motion. (Old code sliced this same name as a 1x4 crawl, which broke once the
		// sheet was repurposed to the 49-frame rear-up.)
		LoadAnimation(new AnimationData("GFX/Sprites/spider_sheet2", 7, 7, 1, 12f, 22, 31));
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		SetHitPoints(2, scaleWithDifficulty: false);
		PointValue = 100f;
		timers.Add(flaptimer);
		timers.Add(swiveltimer);
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		wing = content.Load<Texture2D>("GFX/Sprites/wing1");
	}

	public static FlyingSpider NewFlyingSpider(ComponentBin collection, Game game)
	{
		FlyingSpider flyingSpider = collection.Recycle<FlyingSpider>();
		if (flyingSpider == null)
		{
			flyingSpider = new FlyingSpider(game);
		}
		return flyingSpider;
	}

	public void Setup(bool isbackground)
	{
		this.isbackground = isbackground;
	}

	public override void Initialize()
	{
		base.Initialize();
		flaptimer.Randomize();
		base.Position = new Vector2(850f, RandomHelper.RandomNextFloat(0f, 475f));
		base.Direction = (float)Math.PI;
		base.MaxSpeed = base.Speed;
		rotation = RandomHelper.RandomNextFloat(-(float)Math.PI / 32f, (float)Math.PI / 32f);
		int colorPick = netForcedColorIndex ?? RandomHelper.Random.Next(3);
		switch (colorPick)
		{
		case 0:
			color = Color.DarkGray;
			break;
		case 1:
			color = Color.White;
			break;
		case 2:
			color = Color.DimGray;
			break;
		}
		startheight = base.Position.Y;
		if (isbackground)
		{
			base.Collides = false;
			color = new Color(new Vector4(1f, 1f, 1f, 0.2f));
			scale = 0.67f * SizeFactor;
			Vector2 backgroundSpeed = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed).Length() * 1.11f;
			base.DrawOrder = 1;
			startheight = MathHelper.Min(350f, startheight);
			swiveltimer.Duration = 4000f;
		}
		else
		{
			scale = 1f * SizeFactor;
			base.Collides = true;
			Vector2 backgroundSpeed2 = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed2).Length() * 1.35f;
			base.DrawOrder = 20;
			swiveltimer.Duration = 2700f;
		}
		swiveltimer.Randomize();
	}

	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (isbackground)
		{
			// Fog spiders are translucent (alpha 0.2). Drawing wing+body+wing separately at 0.2 with
			// straight-alpha blending makes the overlaps composite to ~0.36, so the wings read more
			// solid than the body — the reported "opacity is off". Flatten the three sprites OPAQUE
			// into a shared RT (the union has no internal double-up), then composite the whole
			// silhouette ONCE at the fog alpha, so body + wings fade as one. Foreground spiders are
			// opaque (alpha 1) — no double-up — so they skip this and draw directly.
			Color fog = color;
			// Design bbox centred on Position, generous enough to hold the reared body + both swung
			// wings at this scale without clipping (transparent padding costs nothing; the composite
			// only touches the used sub-rect).
			float half = 200f * scale;
			Rectangle box = new Rectangle(
				(int)Math.Floor(base.Position.X - half),
				(int)Math.Floor(base.Position.Y - half),
				(int)Math.Ceiling(2f * half),
				(int)Math.Ceiling(2f * half));
			color = new Color(fog.R, fog.G, fog.B, (byte)255);
			spriteBatch.BeginGroupFlatten(box);
			DrawSprites(gameTime);
			spriteBatch.BlendMode = (SpriteBlendMode)1;
			spriteBatch.EndGroupFlatten(new Color((byte)255, (byte)255, (byte)255, fog.A));
			color = fog;
		}
		else
		{
			DrawSprites(gameTime);
		}
	}

	private void DrawSprites(GameTime gameTime)
	{
		float num = flaptimer.Duration / 2f;
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Enable();
		}
		float timeElapsed = flaptimer.TimeElapsed;
		timeElapsed %= num * 2f;
		if (timeElapsed > num)
		{
			timeElapsed = num - (timeElapsed - num);
		}
		// wing1 is a 4x supersampled sheet; divide the draw scale by its factor (and scale the
		// design-space pivots up by it) so the wing renders at its true on-screen size. Anchor
		// both wings on the body in DESIGN space relative to its centre (Position) -- the old
		// texel-space offset assumed cell texels == screen px, which the supersampled rear-up
		// sheet blows far out of place.
		float wf = SuperSampleFactor("GFX/Sprites/wing1", wing.LogicalWidth());
		int wingIdx = (int)curframe - FirstFrame;
		if (wingIdx < 0) wingIdx = 0;
		else if (wingIdx >= WingAnchors.Length) wingIdx = WingAnchors.Length - 1;
		Vector2 wingAnchor = base.Position + WingAnchors[wingIdx] * scale;
		spriteBatch.Draw(wing, wingAnchor, MathHelper.Lerp(0f, (float)Math.PI / 2f, timeElapsed / num), scale / wf, new Vector2(82f, 11f) * wf, color, (SpriteEffects)1);
		base.Draw(gameTime);
		spriteBatch.Draw(wing, wingAnchor, MathHelper.Lerp(0f, -(float)Math.PI / 2f, timeElapsed / num), scale / wf, new Vector2(6f, 11f) * wf, color, (SpriteEffects)0);
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		float num = 50f * Settings.GetInstance().DifficultyModifier;
		base.Position = new Vector2(base.Position.X, startheight + num * scale * (float)Math.Sin(swiveltimer.Normalized * ((float)Math.PI * 2f)));
		base.Update(gameTime);
		if (base.Position.X < -100f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Floorbottom && base.DirectionalVector.Y > 0f)
		{
			base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0f - base.DirectionalVector.Y);
		}
		if (other is Lazer || other is SweepUFO)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		if (!base.IsDead)
		{
			Die();
			if (!(other is Lazer) && !(other is SweepUFO))
			{
				AwardScore(isComboGenerator, other);
			}
			BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			BloodExplosion bloodExplosion2 = bloodExplosion;
			Vector2 position = base.Position;
			Vector2 speedVector = base.SpeedVector;
			bloodExplosion2.Setup(position, 5f, 0.75f, MathHelper.Min((speedVector).Length(), 0.24f), MyMath.VectorToAngle(base.SpeedVector));
			bloodExplosion.MakeGreen();
			collection.Add((GameComponent)(object)bloodExplosion);
			bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			BloodExplosion bloodExplosion3 = bloodExplosion;
			Vector2 position2 = base.Position;
			Vector2 speedVector2 = base.SpeedVector;
			bloodExplosion3.Setup(position2, 3f, 0.5f, MathHelper.Min((speedVector2).Length(), 0.24f), MyMath.VectorToAngle(base.SpeedVector));
			bloodExplosion.MakeGreen();
			collection.Add((GameComponent)(object)bloodExplosion);
			if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
			{
				sound.PlayCue("bugdies");
			}
			sound.PlayCue("small head asplode");
		}
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/FlyingSpiderDescriptor) ---
	// Client puppets run Enabled=false. isbackground (the Setup bool) picks the WHOLE look:
	// fog alpha + smaller scale + DrawOrder + Collides=false + the group-flatten Draw path, so
	// it is pinned as a construction arg. Wing flap (flaptimer, ticked by NetTickTimers) + the
	// vertical bob (carried by base pos) + curframe self-animate, so there is no continuous
	// state extra. Foreground spiders take a random grey tint, forced via netForcedColorIndex.

	internal bool NetIsBackground => isbackground;

	internal byte NetColorIndex
	{
		get
		{
			if (color == Color.White)
			{
				return 1;
			}
			if (color == Color.DimGray)
			{
				return 2;
			}
			return 0; // DarkGray (Initialize's case 0)
		}
	}

	// Set BEFORE bin.Add triggers Initialize (which reads it); null => the random pick.
	internal void NetForceColor(byte idx)
	{
		netForcedColorIndex = idx;
	}
}
