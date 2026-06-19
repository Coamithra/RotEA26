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
		((DrawableGameComponent)this).DrawOrder = 910;
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
		((DrawableGameComponent)this).Initialize();
		timer.Reset();
		timer.Start();
		currentLetter = 0;
		ServiceHelper.Get<ISoundManagerService>().SoundManager.PlayCue("newwave");
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		font = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<SpriteFont>("GFX/Menu/Menufont");
	}

	public override void Update(GameTime gameTime)
	{
		((GameComponent)this).Update(gameTime);
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

	public override void Draw(GameTime gameTime)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = font.MeasureString(text) * 0.9f;
		val /= 2f;
		val.Y = 0f;
		spriteBatch.DrawString(font, displayingText, new Vector2(400f, 85f) - val, Color.White, 0f, Vector2.Zero, 0.9f, (SpriteEffects)0, 0f);
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
