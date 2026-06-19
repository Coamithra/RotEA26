using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class PausedScene : MenuSub1
{
	private ContentManager content;

	private ComponentBin collectionHelper;

	private Texture2D blankTexture;

	public PausedScene(Game game)
		: base(game)
	{
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		collectionHelper = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		base.DrawOrder = 2000;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blankTexture = content.Load<Texture2D>("GFX/Game/blank");
	}

	public static PausedScene newPausedScene(ComponentBin collection, Game game)
	{
		PausedScene pausedScene = collection.Recycle<PausedScene>();
		if (pausedScene == null)
		{
			pausedScene = new PausedScene(game);
		}
		return pausedScene;
	}

	public void Setup(ControlDevice starter)
	{
		controller = starter;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.DrawMenu(gameTime, yoffset + 75f);
		Vector2 val = font.MeasureString("Paused..") / 2f + new Vector2(0f, 60f);
		base.SpriteBatch.DrawString(font, "Paused..", new Vector2(400f, 300f), Color.AliceBlue, 0f, val, 1f, (SpriteEffects)0, 1f);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		_ = base.GraphicsDevice.Viewport;
		base.Draw(gameTime);
	}
}
