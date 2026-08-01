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
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.DrawMenu(gameTime, yoffset + 75f);
		Vector2 val = font.MeasureString("Paused..") / 2f + new Vector2(0f, 60f);
		base.SpriteBatch.DrawMetalString(font, "Paused..", new Vector2(400f, 300f), Color.AliceBlue, 0f, val, 1f);
		// Card 2001fbd8 privacy + easy-reference: while the game is publicly listed, show that
		// (and the room code) here -- the host can always find their code, and a player can
		// always see their game is joinable. Nothing shown when the game isn't listed.
		if (EvilAliensWeb.Compat.Net.NetListing.Listed)
		{
			string line = "Listed online  -  room " + EvilAliensWeb.Compat.Net.NetListing.RoomCode;
			Vector2 o = font.MeasureString(line) / 2f;
			// Card d1a0559b: this used to sit at a hard-coded y=400, which is INSIDE the row
			// list -- the four pause entries run from ~322 to ~442, so it landed across
			// "Instructions"/"Exit to Main Menu" every single time it was shown. Park it below
			// the last row instead, DERIVED from the same layout DrawMenu just used, so it
			// tracks a font or entry-count change instead of needing a second magic number.
			// GetListCentre() reports the base layout at yoffset 0; this menu draws at +75
			// (above), and every entry here is unlocked, so visible == menuEntries.Count.
			float lastRowY = GetListCentre().Y + 75f + (float)(menuEntries.Count - 1) / 2f * (float)font.LineSpacing;
			float lineY = lastRowY + (float)font.LineSpacing * 0.9f;
			base.SpriteBatch.DrawString(font, line, new Vector2(400f, lineY), Color.Gold, 0f, o, 0.6f, (SpriteEffects)0, 0f);
		}
	}

	public override void Draw(GameTime gameTime)
	{
		_ = base.GraphicsDevice.Viewport;
		base.Draw(gameTime);
	}
}
