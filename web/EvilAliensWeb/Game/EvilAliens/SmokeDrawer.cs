using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SmokeDrawer : DrawableGameComponent, IComponentWatcher
{
	private Explosion owner;

	private ComponentBin collection;

	public SmokeDrawer(Game game)
		: base(game)
	{
		base.DrawOrder = 39;
	}

	public static SmokeDrawer NewSmokeDrawer(ComponentBin collection, Game game)
	{
		SmokeDrawer smokeDrawer = collection.Recycle<SmokeDrawer>();
		if (smokeDrawer == null)
		{
			smokeDrawer = new SmokeDrawer(game);
		}
		return smokeDrawer;
	}

	public void Setup(Explosion owner)
	{
		this.owner = owner;
	}

	public override void Initialize()
	{
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		owner.DrawSmoke(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == owner)
		{
			collection.Remove((GameComponent)(object)this);
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
