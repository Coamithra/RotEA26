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

	// Card 8d6883f3: the entry's bundled image is NOT passed in -- it is looked up from the
	// level (LevelArt.ScreenshotPath) at load time. The caller used to spell the path out, a
	// third copy of the same twelve strings that could drift from ScreenshotSaver's preload
	// set. This also removes one of the class's positional parallel lists.
	public void AddEntryData(string briefing, Levels level)
	{
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
		for (int i = 0; i < levels.Count; i++)
		{
			// The bundled fallback art, from the one table ScreenshotSaver.StockShots is
			// also derived from -- so anything reached here was preloaded and splash-warmed.
			//
			// Card 0d166364: null here is an AUTHORING BUG -- MenuScene added a carousel entry
			// for a level missing from LevelArt.ScreenshotPath -- so it is reported rather than
			// absorbed. THE NOISE IS THE WHOLE SIGNAL and must not be quietened: level1empty is
			// already warm, so a silent fallback leaves nothing for
			// tools/headless/probes/stockshots_warm.txt to see. Why each caller differs:
			// LevelArt.ScreenshotPath.
			string imageName = LevelArt.ScreenshotPath(levels[i]);
			if (imageName == null)
			{
				System.Console.WriteLine("[levelart] carousel entry " + levels[i]
					+ " has no bundled art -- add it to LevelArt.ScreenshotPath; drawing "
					+ LevelArt.DefaultScreenshotPath);
				imageName = LevelArt.DefaultScreenshotPath;
			}
			Texture2D image;
			if (General.ScreenshotEnabled(levels[i]))
			{
				image = ScreenshotSaver.GetScreenshot(levels[i]);
				if (image == null)
				{
					image = Content.Load<Texture2D>(imageName);
				}
			}
			else
			{
				image = Content.Load<Texture2D>(imageName);
			}
			entryImages.Add(image);
		}
	}

	// The selected level's name (top) and briefing (bottom) -- drawn after the carousel entries.
	protected override void DrawCarouselOverlay(GameTime gameTime)
	{
		Vector2 textOrigin = font.MeasureString(menuEntries[selectedEntry]) / 2f;
		base.SpriteBatch.DrawMetalString(font, menuEntries[selectedEntry], new Vector2(400f, 50f), Color.AliceBlue, 0f, textOrigin, 1f);
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
		textOrigin = font.MeasureString(briefing) / 2f;
		textOrigin.Y = 0f;
		base.SpriteBatch.DrawString(font, briefing, new Vector2(400f, 350f), Color.AliceBlue, 0f, textOrigin, 0.7f, (SpriteEffects)0, 0f);
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
			float toCentre = MathHelper.Lerp(1f, 0f, step);
			Vector2 position = new Vector2(MathHelper.Lerp(800f, 400f, toCentre), 200f);
			Color color = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, toCentre)));
			float entryScale = MathHelper.Lerp(0.25f, 0.4f, toCentre);
			float fitX = 800f / (float)entryImages[entry].Width;
			float fitY = 600f / (float)entryImages[entry].Height;
			Vector2 scale = new Vector2(fitX * entryScale, fitY * entryScale);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(entryImages[entry], position, 0f, scale, center: true, color);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			DrawAchievementText(entry, position, entryScale, color);
			// Mouse hit box: the screenshot is drawn centred at `position`, sized
			// imgW*scale.X x imgH*scale.Y -- fitX/fitY cancel the image dimensions,
			// so that is exactly 800*entryScale x 600*entryScale.
			RecordEntryHit(entry, position, 800f * entryScale, 600f * entryScale);
		}
		else
		{
			float toCentre = MathHelper.Lerp(0f, 1f, step);
			Vector2 position2 = new Vector2(MathHelper.Lerp(0f, 400f, toCentre), 200f);
			Color color2 = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, toCentre)));
			float entryScale = MathHelper.Lerp(0.25f, 0.4f, toCentre);
			float fitX = 800f / (float)entryImages[entry].Width;
			float fitY = 600f / (float)entryImages[entry].Height;
			Vector2 scale2 = new Vector2(fitX * entryScale, fitY * entryScale);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(entryImages[entry], position2, 0f, scale2, center: true, color2);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			DrawAchievementText(entry, position2, entryScale, color2);
			// Mouse hit box: screenshot centred at `position2`, sized 800*entryScale x 600*entryScale.
			RecordEntryHit(entry, position2, 800f * entryScale, 600f * entryScale);
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
			string label = Achievements.GetInstance().Data[levels[entry]].difficulty.ToString().Replace('_', ' ');
			float stampScale = MathHelper.Lerp(2.5f, 8.75f, (float)Achievements.GetInstance().Data[levels[entry]].difficulty / (float)difficultyLevelValues.Count);
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
			Vector2 labelOrigin = font.MeasureString(label) / 2f;
			base.SpriteBatch.DrawString(label, position, color, -(float)Math.PI / 12f, labelOrigin, scale * stampScale, (SpriteEffects)0, 1f);
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
