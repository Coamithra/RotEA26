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
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public PunchingBag(Game game)
		: base(game)
	{
		scale = 0.13f;
		LoadAnimation(new AnimationData("GFX/Sprites/eye"));
		((DrawableGameComponent)this).DrawOrder = 20;
		base.IsBoss = true;
		base.Colorize = true;
		PointValue = 2000f;
		SetHitPoints(100, scaleWithDifficulty: false);
	}

	public override void Initialize()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		base.Position += new Vector2(0f, ydrawingoffset);
		base.Draw(gameTime);
		base.Position -= new Vector2(0f, ydrawingoffset);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		collection.Remove((GameComponent)(object)this);
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}
}
