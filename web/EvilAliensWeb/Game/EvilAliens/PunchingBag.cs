using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class PunchingBag : KillableAlien
{
	private float ydrawingoffset;

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

	public PunchingBag(Game game)
		: base(game)
	{
		scale = 1f;
		LoadAnimation(new AnimationData("GFX/Sprites/eye_idle", 4, 2, 1, 12f));
		base.DrawOrder = 20;
		base.IsBoss = true;
		base.Colorize = true;
		PointValue = 2000f;
		SetHitPoints(100, scaleWithDifficulty: false);
	}

	public override void Initialize()
	{
		base.Initialize();
		base.Position = new Vector2(400f, -20f);
	}

	public static PunchingBag NewPunchingBag(ComponentBin collection, Game game)
	{
		PunchingBag punchingBag = collection.Recycle<PunchingBag>();
		if (punchingBag == null)
		{
			punchingBag = new PunchingBag(game);
		}
		return punchingBag;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Position += new Vector2(0f, ydrawingoffset);
		base.Draw(gameTime);
		base.Position -= new Vector2(0f, ydrawingoffset);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		ydrawingoffset = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6.0) * 3f;
		if (base.Position.Y < 170f)
		{
			base.Position = new Vector2(base.Position.X, base.Position.Y + (float)gameTime.ElapsedGameTime.TotalMilliseconds * 1.25f / 16.666666f);
		}
		else
		{
			base.Position = new Vector2(base.Position.X, 170f);
		}
		base.HitPoints = 100;
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		throw new NotImplementedException();
	}

	internal void Terminate()
	{
		collection.Remove((GameComponent)(object)this);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}
}
