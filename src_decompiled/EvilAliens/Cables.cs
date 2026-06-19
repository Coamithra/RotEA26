using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class Cables : AlienDrawableGameComponent
{
	private BrainBoss boss;

	private bool front = true;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	public Cables(Game game)
		: base(game)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		base.Collides = false;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == boss)
		{
			boss = null;
		}
	}

	public static Cables NewAlien(ComponentBin collection, Game game)
	{
		Cables cables = collection.Recycle<Cables>();
		if (cables == null)
		{
			cables = new Cables(game);
		}
		return cables;
	}

	public void Setup(BrainBoss owner, bool front)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		this.front = front;
		if (front)
		{
			((DrawableGameComponent)this).DrawOrder = 25;
		}
		else
		{
			((DrawableGameComponent)this).DrawOrder = 2;
		}
		boss = owner;
		if (front)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/cablesfront"));
		}
		else
		{
			LoadAnimation(new AnimationData("GFX/Sprites/cablesback"));
		}
		base.Position = new Vector2(0f, boss.Position.Y - 80f);
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		if (boss != null)
		{
			scale = boss.scale;
			spriteBatch.Draw(texture, boss.Position, 0f, scale, new Vector2(boss.Position.X, 80f));
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		if (boss != null)
		{
			base.Position = new Vector2(0f, boss.Position.Y - 80f);
			base.Update(gameTime);
		}
		else
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	internal void Free()
	{
		boss = null;
	}
}
