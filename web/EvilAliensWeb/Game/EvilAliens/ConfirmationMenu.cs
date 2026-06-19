using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class ConfirmationMenu : MenuSub1
{
	private ContentManager content;

	private ComponentBin collectionHelper;

	private Texture2D blankTexture;

	private string text;

	public ConfirmationMenu(Game game, string text)
		: base(game)
	{
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		collectionHelper = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		base.DrawOrder = 2000;
		this.text = text;
	}

	public override void Reset()
	{
		base.Reset();
	}

	public override void Initialize()
	{
		base.Initialize();
		selectedEntry = 1;
	}

	protected override void LoadContent()
	{
		blankTexture = content.Load<Texture2D>("GFX/Game/blank");
		base.LoadContent();
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

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.DrawMenu(gameTime, yoffset + 75f);
		Vector2 val = font.MeasureString(text) / 2f + new Vector2(0f, 60f);
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f), Color.AliceBlue, 0f, val, 1f, (SpriteEffects)0, 1f);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = base.GraphicsDevice.Viewport;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, (viewport).Width, (viewport).Height), new Color((byte)0, (byte)0, (byte)0, (byte)128));
		base.Draw(gameTime);
	}

	internal void Setup(ControlDevice pauser)
	{
		controller = pauser;
	}
}
