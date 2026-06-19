using Microsoft.Xna.Framework;

namespace EvilAliens;

public class ForegroundPlaceholder : DrawableGameComponent, IComponentWatcher
{
	private Background background;

	public ForegroundPlaceholder(Game game, Background background)
		: base(game)
	{
		this.background = background;
		base.DrawOrder = 900;
	}

	public override void Draw(GameTime gameTime)
	{
		background.DrawForeground(gameTime);
		base.Draw(gameTime);
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == background)
		{
			ServiceHelper.Get<IComponentBinService>().ComponentBin.Remove((GameComponent)(object)this);
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
