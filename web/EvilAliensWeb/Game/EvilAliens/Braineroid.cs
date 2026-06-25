using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Braineroid : KillableAlien
{
	private bool hasbonus;

	private Powerup bonus;

	private bool wrapping;

	private float _time;

	private float rotationspeed;

	private BrainSize size;

	private bool stationary;

	private float pulsate;

	private float pulsatespeed;

	// Blue glow drawn additively behind the brain (BrainBoss-aura recipe, blue tinted).
	private Texture2D glowTexture;

	private const float GlowOmega = 2.6f;          // ~2.4s shimmer period

	private const float GlowScaleBase = 1.05f;     // glow drawn at brain DrawScale * this

	private const float GlowScaleShimmer = 0.04f;  // +/-4% breathe

	private const float GlowAlphaBase = 0.5f;

	private const float GlowAlphaShimmer = 0.12f;  // alpha rides 0.38..0.62

	private float glowPhase;   // per-instance offset so glows don't pulse in unison

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft = collisionBox.TopLeft * 0.9f + base.Position;
			collisionBox.BottomRight = collisionBox.BottomRight * 0.9f + base.Position;
			return collisionBox;
		}
	}

	public Braineroid(Game game)
		: base(game)
	{
		// Animated cyborg brain (5 cols x 4 rows, 20 frames). interpolationOptions =
		// always so the interpolate.fx shader cross-fades frame N->N+1 regardless of
		// the global Interpolate setting — that's what lets the low frame rate still
		// play smooth (0.4 fps => a very slow ~50s loop, the shader fills the gaps;
		// 20 frames is enough because the motion interpolates cleanly).
		LoadAnimation(new AnimationData("GFX/Sprites/brainanimated", 4, 5, 0, 0.4f, 0, 20));
		interpolationOptions = InterpolationOptions.always;
		glowTexture = content.Load<Texture2D>("GFX/Sprites/brainanimatedglow");
		base.DrawOrder = 20;
		base.MaxSpeed = 100f;
		base.Colorize = false;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && bonus != null)
		{
			collection.Remove((GameComponent)(object)bonus);
			bonus = null;
		}
	}

	public static Braineroid NewBraineroid(ComponentBin collection, Game game)
	{
		Braineroid braineroid = collection.Recycle<Braineroid>();
		if (braineroid == null)
		{
			braineroid = new Braineroid(game);
		}
		return braineroid;
	}

	public void Setup(Vector2 position, BrainSize size, float initialrotation, bool wrapping)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		hasbonus = false;
		this.wrapping = wrapping;
		base.Position = position;
		this.size = size;
		rotation = initialrotation;
		stationary = false;
		base.Direction = RandomHelper.RandomNextAngle();
		if (((base.Position.X < 0f) | (base.Position.X > 800f)) && (double)Math.Abs(base.DirectionalVector.X) < 0.5)
		{
			base.DirectionalVector = new Vector2(0.5f * (float)(-Math.Sign(base.Position.X)), base.DirectionalVector.Y);
		}
		if (((base.Position.Y < 0f) | (base.Position.Y > 600f)) && (double)Math.Abs(base.DirectionalVector.Y) < 0.5)
		{
			base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0.5f * (float)(-Math.Sign(base.Position.Y)));
		}
	}

	public void SetupStationary()
	{
		stationary = true;
	}

	public override void Initialize()
	{
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		// Baseline 1 (not 0): Update overwrites this before the first in-game Draw, but
		// the sprite harness never runs Update — pulsate 0 there would draw scale*0 = nothing.
		pulsate = 1f;
		// Desync per-instance so a cluster of brains isn't lock-step: random scale-pulse
		// phase (_time) and glow-pulse phase. The animation frame is randomised below.
		_time = RandomHelper.RandomNextFloat(0f, 10f);
		glowPhase = RandomHelper.RandomNextFloat(0f, MathHelper.TwoPi);
		switch (size)
		{
		case BrainSize.huge:
			scale = 2f;
			base.Speed = 0.06f * (1f + (Settings.GetInstance().DifficultyModifier - 1f) / 2f);
			rotationspeed = RandomHelper.RandomNextFloat(-5E-05f, 5E-05f);
			pulsatespeed = 3.32f;
			SetHitPoints(6, scaleWithDifficulty: false);
			base.DrawOrder = 20;
			PointValue = 10f;
			break;
		case BrainSize.medium:
			scale = 1f;
			base.Speed = 0.18f * (1f + (Settings.GetInstance().DifficultyModifier - 1f) / 2f);
			rotationspeed = RandomHelper.RandomNextFloat(-0.0002f, 0.0002f);
			pulsatespeed = 5f;
			SetHitPoints(3, scaleWithDifficulty: false);
			base.DrawOrder = 20;
			PointValue = 25f;
			break;
		case BrainSize.small:
			scale = 0.35f;
			base.Speed = 0.3f * (1f + (Settings.GetInstance().DifficultyModifier - 1f) / 2f);
			rotationspeed = RandomHelper.RandomNextFloat(-0.001f, 0.001f);
			pulsatespeed = 12f;
			SetHitPoints(1, scaleWithDifficulty: false);
			base.DrawOrder = 800;
			PointValue = 100f;
			break;
		}
		Vector2 speedVector = base.SpeedVector;
		(speedVector).Normalize();
		base.Position += speedVector * 10f;
		if (stationary)
		{
			base.Speed = 0.6f;
			base.Direction = (float)Math.PI;
			rotationspeed = 0f;
			pulsatespeed = 3f;
			SetHitPoints(3, scaleWithDifficulty: false);
		}
		base.Initialize();
		// Random starting animation frame so a cluster of brains isn't perfectly in
		// sync. (Set after base.Initialize so it isn't reset; the harness overrides
		// curframe afterwards for a deterministic frozen frame.)
		curframe = RandomHelper.RandomNextFloat(0f, Math.Max(1, rows * columns));
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		float num = scale;
		scale = num * pulsate;
		if (hasbonus)
		{
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, Powerup.PowerUpHue(bonus.type));
			if (bonus.type == Powerup.PowerupType.OneUp)
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * (float)gameTime.TotalGameTime.TotalSeconds % 360f);
			}
			spriteBatch.colorizeEffect.Enable();
		}
		DrawGlow(gameTime);
		base.Draw(gameTime);
		spriteBatch.colorizeEffect.Disable();
		spriteBatch.fadeEffect.Disable();
		scale = num;
	}

	// Soft blue glow behind the brain — additive, tracks the brain's (pulsated) size,
	// with its own subtle shimmer. The glow texture is pre-tinted blue, so it's drawn
	// white-with-alpha (like BrainAura over brainbossaura). Caller has already set
	// scale = num * pulsate (so DrawScale tracks the brain) and, for a bonus-carrying
	// Braineroid, enabled colorize — so the glow gets hue-shifted with the brain.
	private void DrawGlow(GameTime gameTime)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		if (glowTexture == null)
		{
			return;
		}
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		float s = (float)Math.Sin(t * GlowOmega + glowPhase);
		float glowScale = DrawScale * GlowScaleBase * (1f + GlowScaleShimmer * s);
		float alpha = GlowAlphaBase + GlowAlphaShimmer * s;
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		spriteBatch.Draw(glowTexture, Position, rotation, glowScale, center: true, new Color(new Vector4(1f, 1f, 1f, alpha)));
		spriteBatch.BlendMode = blendMode;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0220: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_020b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0245: Unknown result type (might be due to invalid IL or missing references)
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		_time += (float)gameTime.ElapsedGameTime.TotalSeconds;
		pulsate = 1f + (1f + (float)Math.Sin(_time * pulsatespeed)) * 0.07f;
		Move(gameTime);
		base.Update(gameTime);
		rotation += rotationspeed;
		// Off-screen margin = half the ON-SCREEN sprite width. texture.Width is now the
		// whole 8-frame sheet, so divide by columns for one frame and use DrawScale (not
		// raw scale) to account for the supersample factor — otherwise brains wrap/despawn
		// hundreds of px off-screen and the Braineroids minigame never clears a wave.
		float num = (float)(texture.Width / columns) * DrawScale / 2f;
		if (!wrapping)
		{
			if ((base.Position.X > 800f + num) & (base.DirectionalVector.X > 0f))
			{
				collection.Remove((GameComponent)(object)this);
			}
			if ((base.Position.Y > 600f + num) & (base.DirectionalVector.Y > 0f))
			{
				collection.Remove((GameComponent)(object)this);
			}
			if ((base.Position.X < 0f - num) & (base.DirectionalVector.X < 0f))
			{
				collection.Remove((GameComponent)(object)this);
			}
			if ((base.Position.Y < 0f - num) & (base.DirectionalVector.Y < 0f))
			{
				collection.Remove((GameComponent)(object)this);
			}
		}
		else
		{
			if ((base.Position.X > 800f + num) & (base.DirectionalVector.X > 0f))
			{
				base.Position = new Vector2(0f - num, base.Position.Y);
			}
			if ((base.Position.Y > 600f + num) & (base.DirectionalVector.Y > 0f))
			{
				base.Position = new Vector2(base.Position.X, 0f - num);
			}
			if ((base.Position.X < 0f - num) & (base.DirectionalVector.X < 0f))
			{
				base.Position = new Vector2(800f + num, base.Position.Y);
			}
			if ((base.Position.Y < 0f - num) & (base.DirectionalVector.Y < 0f))
			{
				base.Position = new Vector2(base.Position.X, 600f + num);
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		if (other is Lazer)
		{
			HitBy(other, isComboGenerator: false);
		}
		if (other is Floorbottom && MyMath.AngleToVector(base.Direction).Y > 0f)
		{
			base.DirectionalVector = new Vector2(MyMath.AngleToVector(base.Direction).X, 0f - MyMath.AngleToVector(base.Direction).Y);
			rotationspeed += 0.01f;
		}
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		if (hasbonus)
		{
			collection.Add((GameComponent)(object)bonus);
			bonus.Position = base.Position;
			bonus = null;
			hasbonus = false;
		}
		Die();
		if (!(other is Lazer))
		{
			AwardScore(isComboGenerator, other);
		}
		int num = 3;
		if (size == BrainSize.huge)
		{
			num = (int)((float)num * Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.LiveShips));
		}
		for (int i = 0; i < num; i++)
		{
			switch (size)
			{
			case BrainSize.huge:
			{
				Braineroid braineroid = NewBraineroid(collection, base.Game);
				braineroid.Setup(base.Position, BrainSize.medium, rotation, wrapping);
				collection.Add((GameComponent)(object)braineroid);
				break;
			}
			case BrainSize.medium:
			{
				Braineroid braineroid = NewBraineroid(collection, base.Game);
				braineroid.Setup(base.Position, BrainSize.small, rotation, wrapping);
				collection.Add((GameComponent)(object)braineroid);
				break;
			}
			}
		}
		switch (size)
		{
		case BrainSize.huge:
		{
			for (int j = 0; j < 10; j++)
			{
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				bloodExplosion.Setup(base.Position, 3f + (float)j / 10f, 1f + (float)j / 10f, base.Speed * 0.5f, base.Direction);
				collection.Add((GameComponent)(object)bloodExplosion);
			}
			sound.PlayCue("head asplode");
			break;
		}
		case BrainSize.medium:
		{
			BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			bloodExplosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.5f, base.Direction);
			collection.Add((GameComponent)(object)bloodExplosion);
			sound.PlayCue("small head asplode");
			break;
		}
		case BrainSize.small:
		{
			BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			bloodExplosion.Setup(base.Position, 1f, 0.8f, base.Speed * 0.5f, base.Direction);
			collection.Add((GameComponent)(object)bloodExplosion);
			sound.PlayCue("small head asplode");
			break;
		}
		}
	}

	internal void SetDirection(float a)
	{
		base.Direction = a;
	}

	internal void MakeBonus()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
	}
}
