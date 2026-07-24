using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The level/challenge picker. Now derives the flying/scaling carousel geometry from
// SubMenuCarousel (card 2001fbd8); this class keeps only the level-keyed data (screenshots,
// briefings, the achievement-difficulty overlay) and draws a single entry + the header/footer.
internal class SubMenuLevelChoice : SubMenuCarousel
{
	private List<Texture2D> entryImages = new List<Texture2D>();

	private List<string> entryImageNames = new List<string>();

	private List<string> briefings = new List<string>();

	private List<Levels> levels = new List<Levels>();

	private List<Settings.DifficultyLevel> difficultyLevelValues = Game1.GetEnumValues<Settings.DifficultyLevel>();

	// Mirrors MenuScene.netMode, so the WebcamAliens refusal and the message explaining it
	// can never disagree. Set by the MenuScene alongside its own flag.
	internal bool NetMode { get; set; }

	public SubMenuLevelChoice(Game game)
		: base(game)
	{
	}

	public void AddEntryData(string imageFilename, string briefing, Levels level)
	{
		entryImageNames.Add(imageFilename);
		levels.Add(level);
		briefings.Add(briefing);
	}

	public override void Initialize()
	{
		base.Initialize();
		loadScreenshots();
	}

	public Levels GetSelectedLevel()
	{
		return levels[selectedEntry];
	}

	protected override void LoadContent()
	{
		base.LoadContent();
	}

	private void loadScreenshots()
	{
		entryImages.Clear();
		for (int i = 0; i < entryImageNames.Count; i++)
		{
			string text = entryImageNames[i];
			Texture2D val;
			if (General.ScreenshotEnabled(levels[i]))
			{
				val = ScreenshotSaver.GetScreenshot(levels[i]);
				if (val == null)
				{
					val = Content.Load<Texture2D>(text);
				}
			}
			else
			{
				val = Content.Load<Texture2D>(text);
			}
			entryImages.Add(val);
		}
	}

	// The selected level's name (top) and briefing (bottom) -- drawn after the carousel entries.
	protected override void DrawCarouselOverlay(GameTime gameTime)
	{
		Vector2 val = font.MeasureString(menuEntries[selectedEntry]) / 2f;
		base.SpriteBatch.DrawMetalString(font, menuEntries[selectedEntry], new Vector2(400f, 50f), Color.AliceBlue, 0f, val, 1f);
		// Online co-op excludes the webcam challenge (the camera IS the controller and the
		// mask is wall-clock local -- plans/stage11-online-coop.md). Card 11.5: say so in
		// place of the briefing, so the blocked entry explains itself instead of silently
		// refusing to respond. Blocking (not hiding) keeps the carousel's parallel index
		// lists intact. NetMode is set by the MenuScene from the SAME flag that gates the
		// refusal itself -- deriving it independently (e.g. from NetSession.Active) would let
		// the two disagree on a URL dev-rig boot, where the message would claim the level is
		// unavailable and selecting it would launch anyway.
		string briefing = (NetMode && levels[selectedEntry] == Levels.WebcamAliens)
			? "NOT AVAILABLE IN ONLINE CO-OP\nYour camera is the controller, so this one can't\nbe shared over the network. Pick another challenge."
			: briefings[selectedEntry];
		val = font.MeasureString(briefing) / 2f;
		val.Y = 0f;
		base.SpriteBatch.DrawString(font, briefing, new Vector2(400f, 350f), Color.AliceBlue, 0f, val, 0.7f, (SpriteEffects)0, 0f);
	}

	protected override void DrawEntryAt(int entry, float step)
	{
		if (step > 1f || step < 0f)
		{
			return;
		}
		step *= 2f;
		if (step > 1f)
		{
			step -= 1f;
			float num = MathHelper.Lerp(1f, 0f, step);
			Vector2 position = new Vector2(MathHelper.Lerp(800f, 400f, num), 200f);
			Color color = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num)));
			float num2 = MathHelper.Lerp(0.25f, 0.4f, num);
			float num3 = 800f / (float)entryImages[entry].Width;
			float num4 = 600f / (float)entryImages[entry].Height;
			Vector2 scale = new Vector2(num3 * num2, num4 * num2);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(entryImages[entry], position, 0f, scale, center: true, color);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			DrawAchievementText(entry, position, num2, color);
			// Mouse hit box: the screenshot is drawn centred at `position`, sized
			// imgW*scaleX x imgH*scaleY = 800*num2 x 600*num2 (scaleX = (800/imgW)*num2).
			RecordEntryHit(entry, position, 800f * num2, 600f * num2);
		}
		else
		{
			float num5 = MathHelper.Lerp(0f, 1f, step);
			Vector2 position2 = new Vector2(MathHelper.Lerp(0f, 400f, num5), 200f);
			Color color2 = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num5)));
			float num6 = MathHelper.Lerp(0.25f, 0.4f, num5);
			float num7 = 800f / (float)entryImages[entry].Width;
			float num8 = 600f / (float)entryImages[entry].Height;
			Vector2 scale2 = new Vector2(num7 * num6, num8 * num6);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(entryImages[entry], position2, 0f, scale2, center: true, color2);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			DrawAchievementText(entry, position2, num6, color2);
			// Mouse hit box: screenshot centred at `position2`, sized 800*num6 x 600*num6.
			RecordEntryHit(entry, position2, 800f * num6, 600f * num6);
		}
	}

	private void DrawAchievementText(int entry, Vector2 position, float scale, Color color)
	{
		if (Achievements.GetInstance().Data[levels[entry]].isFinished)
		{
			Settings.DifficultyLevel difficultyLevel = Settings.DifficultyLevel.Easy;
			if (Unlockables.GetInstance().IsUnlocked(Unlockables.Items.HarderDifficulties))
			{
				difficultyLevel = Settings.DifficultyLevel.Hard;
			}
			if (Unlockables.GetInstance().IsUnlocked(Unlockables.Items.InsaneDifficulty))
			{
				difficultyLevel = Settings.DifficultyLevel.Hard;
			}
			string text = Achievements.GetInstance().Data[levels[entry]].difficulty.ToString().Replace('_', ' ');
			float num = MathHelper.Lerp(2.5f, 8.75f, (float)Achievements.GetInstance().Data[levels[entry]].difficulty / (float)difficultyLevelValues.Count);
			if (difficultyLevel > Achievements.GetInstance().Data[levels[entry]].difficulty)
			{
				Color gray = Color.Gray;
				(color) = new Color(new Vector4((gray).ToVector3(), (float)(int)(color).A / 255f));
			}
			else
			{
				Color limeGreen = Color.LimeGreen;
				(color) = new Color(new Vector4((limeGreen).ToVector3(), (float)(int)(color).A / 255f));
			}
			Vector2 val = font.MeasureString(text) / 2f;
			base.SpriteBatch.DrawString(text, position, color, -(float)Math.PI / 12f, val, scale * num, (SpriteEffects)0, 1f);
		}
	}

	internal void SelectLevel(Levels level)
	{
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (levels[i] == level)
			{
				selectedEntry = i;
			}
		}
	}
}
