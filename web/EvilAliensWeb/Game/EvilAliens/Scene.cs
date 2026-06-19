using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace EvilAliens;

public abstract class Scene : DrawableGameComponent, IComponentWatcher
{
	private SpriteBatchWrapper batch;

	private IInputHandlerService inputHandlerService;

	private ISoundManagerService soundManagerService;

	protected ComponentBin Collection;

	protected ContentManager Content;

	protected SoundManager SoundManager => soundManagerService.SoundManager;

	protected InputHandler InputHandler => inputHandlerService.InputHandler;

	protected SpriteBatchWrapper SpriteBatch => batch;

	public Scene(Game aGame)
		: base(aGame)
	{
		inputHandlerService = ServiceHelper.Get<IInputHandlerService>();
		soundManagerService = ServiceHelper.Get<ISoundManagerService>();
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		Collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		Content = ServiceHelper.Get<IContentManagerService>().ContentManager;
	}

	public virtual void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
	}

	public virtual void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
