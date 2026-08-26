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
		base.DrawOrder = 20;
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
			EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
			float direction = MyMath.SnapAngle(oracle.GetRandomPlayerPosition() - base.Position, 32);
			evilBullet.Setup(base.Position, direction);
			collection.Add((GameComponent)(object)evilBullet);
		}
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)(num3 * 2f) * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier)
		{
			EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
			float direction = RandomHelper.RandomNextAngle();
			evilBullet.Setup(base.Position, direction);
			collection.Add((GameComponent)(object)evilBullet);
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
		Die();
		AwardScoreToAll(combo: true);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 6f, 5.3f, base.Speed * 0.1f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		collection.Purge<EvilBullet>();
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsBosses1) --------
	// The body animation runs off `animationProgress` (its own 20fps clock, NOT the component
	// curframe), advanced only in Update -- frozen on a puppet. (scale + color redden arrive in
	// the base state: Scale, and Hp -> NetApplyHp.)
	//
	// The frame is STILL SENT and no longer APPLIED (card 5f506d11): a puppet runs the loop
	// itself in NetDriveExtras below. Same seam, same audit, same reason as BattleSkull's --
	// Compat/Net/NetBodyAnim.
	// GET-ONLY since card 5f506d11: EncodeStateExtra is the last reader and nothing writes it
	// any more. A writable seam with no legitimate writer is a trap -- the next use of it would
	// fight the local loop below, which is the defect that card fixed.
	internal int NetAnimFrame => (int)animationProgress;

	// The loop's length. 0 before LoadContent has run, which is the "no sheet yet" answer
	// NetBodyAnim.Advance acts on -- and what a rig reasoning about the wrap reads (NetRulerTest).
	internal int NetAnimFrameCount => (sprite != null) ? sprite.Frames : 0;

	// This loop is the CLIENT'S to run: see NetBodyAnimLocal, and Compat/Net/NetBodyAnim for the
	// audit that says this type may.
	internal override bool NetBodyAnimLocal => true;

	// The client half: advance the body loop on the driver's REAL dt, exactly as Update does.
	internal override void NetDriveExtras(GameTime gameTime)
	{
		animationProgress = EvilAliensWeb.Compat.Net.NetBodyAnim.Advance(animationProgress,
			(float)gameTime.ElapsedGameTime.TotalSeconds, 20f, NetAnimFrameCount);
	}
}
