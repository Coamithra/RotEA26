using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class ParatrooperAlien : KillableAlien
{
	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public ParatrooperAlien(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mediumship", 8, 4, 1, 25f));
		scale = 0.6f;
		SetHitPoints(1, scaleWithDifficulty: false);
		PointValue = 5f;
	}

	public static ParatrooperAlien NewAlien(ComponentBin collection, Game game)
	{
		ParatrooperAlien paratrooperAlien = collection.Recycle<ParatrooperAlien>();
		if (paratrooperAlien == null)
		{
			paratrooperAlien = new ParatrooperAlien(game);
		}
		return paratrooperAlien;
	}

	public void Setup()
	{
	}

	public override void Initialize()
	{
		base.Speed = 0.24f;
		base.Initialize();
		if (RandomHelper.Random.Next(2) == 1)
		{
			base.Position = new Vector2(900f, 70f);
			base.Direction = (float)Math.PI;
			base.DrawOrder = 20;
		}
		else
		{
			base.Position = new Vector2(-100f, 140f);
			base.Direction = 0f;
			base.DrawOrder = 21;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (OffScreen(100f))
		{
			Die();
		}
		if (RandomHelper.RandomFromAverage(0.2f, gameTime) && !(base.Position.X < 40f) && !(base.Position.X > 760f) && (!(base.Position.X > 360f) || !(base.Position.X < 440f)))
		{
			ParatrooperBrain paratrooperBrain = ParatrooperBrain.NewAlien(collection, base.Game);
			paratrooperBrain.Setup(base.Position);
			collection.Add((GameComponent)(object)paratrooperBrain);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		float direction = 0f;
		if (base.Direction == 0f)
		{
			direction = (float)Math.PI / 12f;
		}
		if (base.Direction == (float)Math.PI)
		{
			direction = (float)Math.PI * 11f / 12f;
		}
		explosion.Setup(base.Position, 2f, 1.5f, base.Speed / 2f, direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		AwardScore(combo: false, other);
		Die();
	}
}
