using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class BattleSkull : KillableAlien
{
	private enum BattleSkullState
	{
		normal,
		dying
	}

	private bool fired;

	private AnimatedSprite sprite;

	private Texture2D blank;

	private Timer DeathTimer = new Timer(2500f, repeating: false);

	private float animationProgress;

	private BattleSkullState state;

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

	public BattleSkull(Game game)
		: base(game)
	{
		((DrawableGameComponent)this).DrawOrder = 17;
		SetHitPoints(25, scaleWithDifficulty: false);
		PointValue = 1000f;
		timers.Add(DeathTimer);
	}

	public static BattleSkull NewBattleSkull(ComponentBin collection, Game game)
	{
		BattleSkull battleSkull = collection.Recycle<BattleSkull>();
		if (battleSkull == null)
		{
			battleSkull = new BattleSkull(game);
		}
		return battleSkull;
	}

	public void Setup(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
	}

	protected override void LoadContent()
	{
		blank = content.Load<Texture2D>("GFX/Game/blank");
		sprite = new AnimatedSprite("GFX/alienboss/alienboss");
		base.LoadContent();
	}

	public override void Initialize()
	{
		scale = 1f;
		base.Collides = true;
		state = BattleSkullState.normal;
		base.Speed = 0.06f;
		base.Colorize = true;
		base.Direction = -(float)Math.PI / 2f;
		base.Initialize();
		fired = false;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Enable();
		}
		if (state != BattleSkullState.dying)
		{
			int num = (int)(base.HitPointsNormalized * 100f);
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(-10f, 10f, (float)num);
			spriteBatch.colorizeEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		sprite.Draw((int)animationProgress, base.Position, color, scale, center: true);
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Disable();
		}
		if (state != BattleSkullState.dying)
		{
			spriteBatch.colorizeEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		//IL_0217: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_028c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0297: Unknown result type (might be due to invalid IL or missing references)
		//IL_029c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * 20f, sprite.Frames);
		base.Update(gameTime);
		switch (state)
		{
		case BattleSkullState.normal:
			if ((base.Position.Y < 100f) & !fired)
			{
				fired = true;
				for (int i = 0; i < (int)(Settings.GetInstance().DifficultyModifier * 5f); i++)
				{
					float direction = (float)i * ((float)Math.PI * 2f) / (float)(int)(Settings.GetInstance().DifficultyModifier * 5f);
					EvilSkull evilSkull = EvilSkull.NewEvilSkull(collection, ((GameComponent)this).Game);
					evilSkull.SetupLaunch(base.Position + new Vector2(0f, 50f), direction);
					collection.Add((GameComponent)(object)evilSkull);
				}
			}
			if (base.Position.Y < -100f)
			{
				Die();
			}
			break;
		case BattleSkullState.dying:
		{
			if (DeathTimer.Finished)
			{
				Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
				collection.Add((GameComponent)(object)explosion);
				explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl2");
				Die();
			}
			float num = MyMath.PowerCurve(0f, 1f, 2f, 1f - DeathTimer.Normalized);
			scale = MathHelper.Lerp(1f, 0.66f, num);
			color = new Color(new Vector3(MathHelper.Lerp(1f, 0.5f, num)));
			if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)MathHelper.Lerp(8f, 24f, num) * gameTime.ElapsedGameTime.TotalSeconds)
			{
				Explosion explosion2 = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion2.Setup(base.Position + new Vector2(RandomHelper.RandomNextFloat(-60f, 60f), RandomHelper.RandomNextFloat(-90f, 90f)) * scale, 0.8f * scale, 0.8f * scale, 0f, 0f);
				collection.Add((GameComponent)(object)explosion2);
				sound.PlayCue("expl1");
			}
			break;
		}
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		AwardScore(isComboGenerator, other);
		state = BattleSkullState.dying;
		base.Collides = false;
		DeathTimer.Start();
		DeathTimer.Reset();
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 2.3f, 1.3f, base.Speed * 0.95f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}
}
