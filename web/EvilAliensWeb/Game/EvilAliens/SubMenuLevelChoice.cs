using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class SubMenuLevelChoice : MenuSub1
{
	private int preferredDirection;

	private List<Texture2D> entryImages = new List<Texture2D>();

	private List<string> entryImageNames = new List<string>();

	private List<string> briefings = new List<string>();

	private List<Levels> levels = new List<Levels>();

	private float scroller;

	private Timer swaptimer = new Timer(400f, repeating: false);

	private float prevSelected;

	private int targetSelected;

	private List<Settings.DifficultyLevel> difficultyLevelValues = Game1.GetEnumValues<Settings.DifficultyLevel>();

	public SubMenuLevelChoice(Game game)
		: base(game)
	{
		prevSelected = selectedEntry;
		targetSelected = selectedEntry;
		swaptimer.Stop();
		swaptimer.Reset();
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

	public override void Update(GameTime gameTime)
	{
		swaptimer.Update(gameTime);
		if (swaptimer.Active)
		{
			int num = 0;
			int num2 = 0;
			int num3 = (int)Math.Round(prevSelected);
			while (num3 != targetSelected)
			{
				num3 = MyMath.Mod(num3 + 1, menuEntries.Count);
				if (!unLockableDataEntries[num3].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[num3].item))
				{
					num++;
				}
			}
			num3 = (int)prevSelected;
			while (num3 != targetSelected)
			{
				num3 = MyMath.Mod(num3 - 1, menuEntries.Count);
				if (!unLockableDataEntries[num3].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[num3].item))
				{
					num2++;
				}
			}
			int num4 = targetSelected;
			if (num2 < num)
			{
				if ((float)num4 > prevSelected)
				{
					num4 -= menuEntries.Count;
				}
			}
			else if (num2 > num)
			{
				if ((float)num4 < prevSelected)
				{
					num4 += menuEntries.Count;
				}
			}
			else if (preferredDirection == 1)
			{
				if ((float)num4 < prevSelected)
				{
					num4 += menuEntries.Count;
				}
			}
			else if ((float)num4 > prevSelected)
			{
				num4 -= menuEntries.Count;
			}
			scroller = MyMath.Mod(prevSelected + MathHelper.SmoothStep(0f, (float)num4 - prevSelected, 1f - swaptimer.Normalized), menuEntries.Count);
		}
		else if (swaptimer.Finished)
		{
			swaptimer.Reset();
			swaptimer.Stop();
			prevSelected = selectedEntry;
			scroller = selectedEntry;
		}
		base.Update(gameTime);
		if (selectedEntry != targetSelected)
		{
			swaptimer.Reset();
			swaptimer.Start();
			targetSelected = selectedEntry;
			prevSelected = scroller;
		}
	}

	protected override void selectNext()
	{
		base.selectNext();
		preferredDirection = 1;
	}

	protected override void selectPrevious()
	{
		base.selectPrevious();
		preferredDirection = -1;
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_0260: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b3: Unknown result type (might be due to invalid IL or missing references)
		int num = 0;
		int num2 = int.MaxValue;
		int num3 = int.MaxValue;
		float a = 0f;
		for (int i = 0; i < entryImages.Count; i++)
		{
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				if ((float)num2 <= scroller && scroller < (float)i)
				{
					a = (float)(num - 1) + (scroller - (float)num2) / (float)(i - num2);
				}
				if (num3 > entryImages.Count)
				{
					num3 = i;
				}
				num2 = i;
				num++;
			}
		}
		if ((float)num2 <= scroller && scroller < (float)entryImages.Count)
		{
			a = (float)(num - 1) + (scroller - (float)num2) / (float)(entryImages.Count + num3 - num2);
		}
		if (0f <= scroller && scroller < (float)num3)
		{
			a = (float)(num - 1) + (scroller + (float)entryImages.Count - (float)num2) / (float)(entryImages.Count + num3 - num2);
		}
		int num4 = 0;
		int num5 = 0;
		for (int j = 0; j < entryImages.Count; j++)
		{
			if (!unLockableDataEntries[j].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[j].item))
			{
				float step = 0.5f + 0.333f * MyMath.DifferenceMod(a, num4, num);
				if (j != targetSelected)
				{
					DrawEntryAt(j, step);
				}
				else
				{
					num5 = num4;
				}
				num4++;
			}
		}
		float step2 = 0.5f + 0.33f * MyMath.DifferenceMod(a, num5, num);
		DrawEntryAt(targetSelected, step2);
		Vector2 val = font.MeasureString(menuEntries[selectedEntry]) / 2f;
		base.SpriteBatch.DrawString(font, menuEntries[selectedEntry], new Vector2(400f, 50f), Color.AliceBlue, 0f, val, 1f, (SpriteEffects)0, 0f);
		val = font.MeasureString(briefings[selectedEntry]) / 2f;
		val.Y = 0f;
		base.SpriteBatch.DrawString(font, briefings[selectedEntry], new Vector2(400f, 350f), Color.AliceBlue, 0f, val, 0.7f, (SpriteEffects)0, 0f);
	}

	private void DrawEntryAt(int entry, float step)
	{
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		if (!(step > 1f || step < 0f))
		{
			step *= 2f;
			if (step > 1f)
			{
				step -= 1f;
				float num = MathHelper.Lerp(1f, 0f, step);
				Vector2 position = default(Vector2);
				(position) = new Vector2(MathHelper.Lerp(800f, 400f, num), 200f);
				Color color = default(Color);
				(color) = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num)));
				float num2 = MathHelper.Lerp(0.25f, 0.4f, num);
				float num3 = 800f / (float)entryImages[entry].Width;
				float num4 = 600f / (float)entryImages[entry].Height;
				Vector2 scale = default(Vector2);
				(scale) = new Vector2(num3 * num2, num4 * num2);
				base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
				base.SpriteBatch.Draw(entryImages[entry], position, 0f, scale, center: true, color);
				base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
				DrawAchievementText(entry, position, num2, color);
			}
			else
			{
				float num5 = MathHelper.Lerp(0f, 1f, step);
				Vector2 position2 = default(Vector2);
				(position2) = new Vector2(MathHelper.Lerp(0f, 400f, num5), 200f);
				Color color2 = default(Color);
				(color2) = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num5)));
				float num6 = MathHelper.Lerp(0.25f, 0.4f, num5);
				float num7 = 800f / (float)entryImages[entry].Width;
				float num8 = 600f / (float)entryImages[entry].Height;
				Vector2 scale2 = default(Vector2);
				(scale2) = new Vector2(num7 * num6, num8 * num6);
				base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
				base.SpriteBatch.Draw(entryImages[entry], position2, 0f, scale2, center: true, color2);
				base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
				DrawAchievementText(entry, position2, num6, color2);
			}
		}
	}

	private void DrawAchievementText(int entry, Vector2 position, float scale, Color color)
	{
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
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
