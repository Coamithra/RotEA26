using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Spider : KillableAlien
{
	// Packed-sheet frame the spider snaps to ONCE on landing (source ~88 at half fps -> packed
	// 44): the crouched settled stance near the end of the rear-up. It resumes animating from
	// there on the following frames.
	private const float LandFrame = 44f;

	private float yspeed;

	private bool hasJumped;

	private bool hasLanded;

	private float rotationspeed;

	private float jumpXposition;

	private Texture2D spiderJump;

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
			collisionBox.TopLeft *= 0.9f;
			collisionBox.BottomRight *= 0.9f;
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		spiderJump = content.Load<Texture2D>("GFX/Sprites/spiderjump");
	}

	public Spider(Game game)
		: base(game)
	{
		// spider_sheet2 is the 7x7 (49-frame) "rear up" animation (AnimGen take, half-fps of the
		// 98 source frames), replacing the old 4-frame crawl. The supersample registry (design
		// width 160) draws it at the same on-screen size; its 384px cells render ~1:1 at the 1440
		// render cap (160 * 2.4). ~12 fps. The FlyingSpider reuses this same sheet, looping just
		// its reared sub-range via FirstFrame/LastFrame.
		LoadAnimation(new AnimationData("GFX/Sprites/spider_sheet2", 7, 7, 1, 12f));
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		scale = 1f;
		base.Direction = (float)Math.PI;
		PointValue = 100f;
		SetHitPoints(3, scaleWithDifficulty: true);
	}

	public static Spider NewSpider(ComponentBin collection, Game game)
	{
		Spider spider = collection.Recycle<Spider>();
		if (spider == null)
		{
			spider = new Spider(game);
		}
		return spider;
	}

	public void Setup()
	{
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		base.Position = new Vector2(1000f, 505f);
		base.Initialize();
		yspeed = 0f;
		hasJumped = false;
		hasLanded = false;
		rotation = 0f;
		rotationspeed = 0f;
		jumpXposition = RandomHelper.RandomNextFloat(300f, 900f);
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
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (!hasJumped)
		{
			base.Draw(gameTime);
			return;
		}
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Enable();
		}
		// spiderjump is now a 6x4 soar ANIMATION sheet (the AnimGen flying-spider take). Play it
		// looping while airborne: draw one source-rect cell, footprint-scaled by 1/factor so the
		// on-screen size matches the old static jump body, with the jump tumble (rotation). The
		// fake flapping wings are gone -- the animation carries the motion now.
		int cols = 6, rows = 4, sep = 1;
		int cellW = (spiderJump.Width - (cols - 1) * sep) / cols;
		int cellH = (spiderJump.Height - (rows - 1) * sep) / rows;
		float fJump = SuperSampleFactor("GFX/Sprites/spiderjump", cellW);
		int frame = (int)(gameTime.TotalGameTime.TotalMilliseconds / 55f) % (cols * rows);
		Rectangle src = new Rectangle(frame % cols * (cellW + sep), frame / cols * (cellH + sep), cellW, cellH);
		spriteBatch.Draw(spiderJump, src, base.Position, rotation, 1f / fJump, center: true, color);
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		if (base.Position.X < -500f)
		{
			Die();
		}
		base.Position += (oracle.BackgroundSpeed + new Vector2(0f, yspeed)) * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (hasJumped & !hasLanded)
		{
			yspeed += 0.02f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f;
			rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (yspeed < 0f)
			{
				rotationspeed = MathHelper.Max(rotationspeed - 3E-05f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f, 0f);
			}
			else
			{
				rotationspeed -= 6E-05f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f;
			}
		}
		if (!hasJumped & !hasLanded & (base.Position.X < jumpXposition))
		{
			hasJumped = true;
			rotation = -0.1f;
			rotationspeed = 0.0018f;
			yspeed = RandomHelper.RandomNextFloat(-8f, -19f) / 16.666666f;
		}
		if (hasJumped & (base.Position.Y > 505f))
		{
			hasJumped = false;
			hasLanded = true;
			rotation = 0f;
			rotationspeed = 0f;
			yspeed = 0f;
			base.Position = new Vector2(base.Position.X, 505f);
			// Snap to the settled "landed" frame ONCE on touchdown, then let it keep animating
			// from there (base.Update advances curframe normally on the following frames).
			curframe = LandFrame;
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Lazer)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		if (!(other is Lazer))
		{
			AwardScore(isComboGenerator, other);
		}
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		BloodExplosion bloodExplosion2 = bloodExplosion;
		Vector2 position = base.Position;
		Vector2 val = oracle.BackgroundSpeed + new Vector2(0f, yspeed);
		bloodExplosion2.Setup(position, 5f, 0.75f, MathHelper.Min((val).Length(), 0.24f), MyMath.VectorToAngle(oracle.BackgroundSpeed + new Vector2(0f, yspeed)));
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		BloodExplosion bloodExplosion3 = bloodExplosion;
		Vector2 position2 = base.Position;
		Vector2 val2 = oracle.BackgroundSpeed + new Vector2(0f, yspeed);
		bloodExplosion3.Setup(position2, 3f, 0.5f, MathHelper.Min((val2).Length(), 0.24f), MyMath.VectorToAngle(oracle.BackgroundSpeed + new Vector2(0f, yspeed)));
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		Die();
		if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
		{
			sound.PlayCue("bugdies");
		}
		sound.PlayCue("small head asplode");
	}
}
