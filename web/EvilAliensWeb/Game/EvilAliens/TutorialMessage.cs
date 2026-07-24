using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class TutorialMessage : DrawableGameComponent, IComponentWatcher
{
	private string text;

	private string displayingText;

	private Timer timer = new Timer(35f, repeating: true);

	private SpriteBatchWrapper spriteBatch;

	private int currentLetter;

	private SpriteFont font;

	public TutorialMessage(Game game)
		: base(game)
	{
		base.DrawOrder = 910;
	}

	public static TutorialMessage NewTutorialMessage(ComponentBin collection, Game game)
	{
		TutorialMessage tutorialMessage = collection.Recycle<TutorialMessage>();
		if (tutorialMessage == null)
		{
			tutorialMessage = new TutorialMessage(game);
		}
		return tutorialMessage;
	}

	public void Setup(string text)
	{
		this.text = text;
		displayingText = "";
	}

	public override void Initialize()
	{
		base.Initialize();
		timer.Reset();
		timer.Start();
		currentLetter = 0;
		ServiceHelper.Get<ISoundManagerService>().SoundManager.PlayCue("newwave");
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		font = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<SpriteFont>("GFX/Menu/Menufont");
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		timer.Update(gameTime);
		if (timer.Finished)
		{
			currentLetter++;
			if (currentLetter <= text.Length)
			{
				displayingText += text[currentLetter - 1];
			}
		}
	}

	// Cache slot for the flattened banner sprite (DrawShadowStringCached); the score HUD
	// owns keys 0..15 (player*4+role), so the banner lives well clear of them.
	private const int BannerCacheKey = 100;

	public override void Draw(GameTime gameTime)
	{
		Vector2 val = font.MeasureString(text) * 0.9f;
		val /= 2f;
		val.Y = 0f;
		// Flattened shadow+text (same treatment the score/pops got — no shadow bleed-through),
		// in the holodeck's cyan so the banner reads as part of the simulation's UI. Cached by
		// slot: the rasterise re-runs only when the typewriter adds a letter.
		spriteBatch.DrawShadowStringCached(BannerCacheKey, displayingText, new Vector2(400f, 85f) - val, 0.9f,
			new Color(0f, 0.16f, 0.24f), new Color(0.78f, 0.96f, 1f), new Vector2(2f, 2f), 0.95f, metal: false, 0f);
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
