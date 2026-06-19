using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class ClassicBoss : KillableAlien
{
	private const int hitpointsstart = 350;

	private float targetdir;

	private AnimatedSprite sprite;

	private float animationProgress;

	private CollisionMultibox boxes;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_004e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0122: Unknown result type (might be due to invalid IL or missing references)
			//IL_0138: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Unknown result type (might be due to invalid IL or missing references)
			if (boxes == null)
			{
				boxes = new CollisionMultibox();
				boxes.Items.Add(new CollisionBox());
				boxes.Items.Add(new CollisionBox());
			}
			boxes.Items[0].Width = MainGame.AlienBossSizeOne.X * scale;
			boxes.Items[0].Height = MainGame.AlienBossSizeOne.Y * scale;
			boxes.Items[0].CenterAround(base.Position - new Vector2(0f, 15f * scale));
			boxes.Items[1].Width = MainGame.AlienBossSizeTwo.X * scale;
			boxes.Items[1].Height = MainGame.AlienBossSizeTwo.Y * scale;
			boxes.Items[1].CenterAround(base.Position - new Vector2(0f, -30f * scale));
			return boxes;
		}
	}

	public ClassicBoss(Game game)
		: base(game)
	{
		scale = 1.1f;
		((DrawableGameComponent)this).DrawOrder = 20;
		PointValue = 10000f;
		base.Colorize = true;
		base.IsBoss = true;
		SetHitPoints(350, scaleWithDifficulty: false);
	}

	public static ClassicBoss NewClassicBoss(ComponentBin collection, Game game)
	{
		ClassicBoss classicBoss = collection.Recycle<ClassicBoss>();
		if (classicBoss == null)
		{
			classicBoss = new ClassicBoss(game);
		}
		return classicBoss;
	}

	protected override void LoadContent()
	{
		sprite = new AnimatedSprite("GFX/alienboss/alienboss");
		base.LoadContent();
	}

	public void Setup()
	{
	}

	public override void Initialize()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		base.Position = new Vector2(400f, -120f);
		base.Direction = (float)Math.PI / 2f;
		targetdir = base.Direction;
		base.MaxSpeed = 0.05f;
		base.Acceleration = 0.0002f;
		base.Deceleration = 0.0001f;
		base.Speed = 0f;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		sprite.Draw((int)animationProgress, base.Position, color, scale, center: true);
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_029f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0342: Unknown result type (might be due to invalid IL or missing references)
		//IL_0321: Unknown result type (might be due to invalid IL or missing references)
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * 20f, sprite.Frames);
		float num = 1f - (float)base.HitPoints / 350f;
		float num2 = MathHelper.Lerp(0.0002f, 0.0008f, num);
		float num3 = MathHelper.Lerp(0.00015f, 0.002f, num);
		base.MaxSpeed = MathHelper.Lerp(0.05f, 0.25f, num);
		scale = MathHelper.Lerp(0.9f, 1.35f, num);
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)num2 * gameTime.ElapsedGameTime.TotalMilliseconds)
		{
			targetdir = RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f);
		}
		Move((float?)targetdir, gameTime);
		Vector2 directionalVector = base.DirectionalVector;
		Vector2 v = MyMath.AngleToVector(targetdir);
		float num4 = 70f;
		if (base.Position.X > 800f - num4)
		{
			if (directionalVector.X > 0f)
			{
				directionalVector.X *= -1f;
			}
			if (v.X > 0f)
			{
				v.X *= -1f;
			}
		}
		if (base.Position.X < num4)
		{
			if (directionalVector.X < 0f)
			{
				directionalVector.X *= -1f;
			}
			if (v.X < 0f)
			{
				v.X *= -1f;
			}
		}
		if (base.Position.Y > 600f - num4)
		{
			if (directionalVector.Y > 0f)
			{
				directionalVector.Y *= -1f;
			}
			if (v.Y > 0f)
			{
				v.Y *= -1f;
			}
		}
		if (base.Position.Y < num4)
		{
			if (directionalVector.Y < 0f)
			{
				directionalVector.Y *= -1f;
			}
			if (v.Y < 0f)
			{
				v.Y *= -1f;
			}
		}
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)num3 * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier)
		{
			EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, ((GameComponent)this).Game);
			float direction = MyMath.SnapAngle(oracle.GetRandomPlayerPosition() - base.Position, 32);
			evilBullet.Setup(base.Position, direction);
			collection.Add((GameComponent)(object)evilBullet);
		}
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)(num3 * 2f) * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier)
		{
			EvilBullet evilBullet2 = EvilBullet.NewEvilBullet(collection, ((GameComponent)this).Game);
			float direction2 = RandomHelper.RandomNextAngle();
			evilBullet2.Setup(base.Position, direction2);
			collection.Add((GameComponent)(object)evilBullet2);
		}
		base.DirectionalVector = directionalVector;
		targetdir = MyMath.VectorToAngle(v);
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		Die();
		AwardScoreToAll(combo: true);
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 6f, 5.3f, base.Speed * 0.1f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		collection.Purge<EvilBullet>();
	}
}
