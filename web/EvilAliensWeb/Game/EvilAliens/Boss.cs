using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Boss : KillableAlien
{
	private const int hitpointsinitially = 225;

	private float targetDirection;

	private float lazerangle;

	private Timer lazertimer;

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	private List<Lazer> lazors = new List<Lazer>();

	private Texture2D blank;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.Width *= 0.90999997f;
			collisionBox.Height *= 0.48999998f;
			collisionBox.CenterAround(base.Position - new Vector2(10f * scale, 0f));
			return collisionBox;
		}
	}

	public Boss(Game game)
		: base(game)
	{
		scale = 1f;
		LoadAnimation(new AnimationData("GFX/Sprites/mothershipB", 4, 4, 1, 16f));
		base.DrawOrder = 50;
		lazertimer = new Timer(10000f, repeating: true);
		AddTimer(lazertimer);
		base.IsBoss = true;
		PointValue = 2000f;
		SetHitPoints(225, scaleWithDifficulty: true);
		base.Colorize = true;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blank = content.Load<Texture2D>("GFX/Game/blank");
		firstHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipA");
		secondHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipB");
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent != this)
		{
			return;
		}
		foreach (Lazer lazor in lazors)
		{
			lazor.Free();
		}
		lazors.Clear();
	}

	public override void Initialize()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		interpolationOptions = InterpolationOptions.never;
		base.Acceleration = 1E-05f;
		base.Deceleration = 0f;
		base.MaxSpeed = 0.015f;
		lazertimer.Duration = 10000f;
		lazors.Clear();
		color = Color.White;
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		targetDirection = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position);
		Move((float?)targetDirection, gameTime);
		foreach (Lazer lazor in lazors)
		{
			lazor.MoveTo(base.Position);
			lazor.ChangeAim(-0.0007f * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
		}
		if (lazertimer.Finished)
		{
			lazertimer.Duration = 800f;
			Lazer lazer = Lazer.NewLazer(collection, base.Game);
			lazer.Setup(base.Position, lazerangle, this, 50f);
			lazors.Add(lazer);
			collection.Add((GameComponent)(object)lazer);
			lazerangle -= RandomHelper.RandomNextFloat(1.0995574f, (float)Math.PI * 9f / 20f);
			if (lazors.Count == 3)
			{
				lazors[0].Free();
				lazors.RemoveAt(0);
			}
		}
		float num = curframe;
		base.Update(gameTime);
		if (curframe < num)
		{
			if (texture == firstHalfOfSpritesheet)
			{
				texture = secondHalfOfSpritesheet;
			}
			else
			{
				texture = firstHalfOfSpritesheet;
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	public void Setup(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		Die();
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
		AwardScoreToAll(isComboGenerator);
	}
}
