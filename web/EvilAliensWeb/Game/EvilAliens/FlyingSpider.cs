using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

	private bool isbackground;

	private Texture2D wing;

	private Timer swiveltimer = new Timer(2700f, repeating: true);

	private Timer flaptimer = new Timer(120f, repeating: true);

	private float startheight;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		flaptimer.Randomize();
		base.Position = new Vector2(850f, RandomHelper.RandomNextFloat(0f, 475f));
		base.Direction = (float)Math.PI;
		base.MaxSpeed = base.Speed;
		rotation = RandomHelper.RandomNextFloat(-(float)Math.PI / 32f, (float)Math.PI / 32f);
		switch (RandomHelper.Random.Next(3))
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
			scale = 0.67f;
			Vector2 backgroundSpeed = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed).Length() * 1.11f;
			base.DrawOrder = 1;
			startheight = MathHelper.Min(350f, startheight);
			swiveltimer.Duration = 4000f;
		}
		else
		{
			scale = 1f;
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
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
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
		float wf = SuperSampleFactor("GFX/Sprites/wing1", wing.Width);
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
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
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
}
