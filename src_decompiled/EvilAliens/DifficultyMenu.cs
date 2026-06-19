using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class DifficultyMenu : MenuSub1
{
	public enum LevelType
	{
		Regular,
		Challenge
	}

	public delegate void DifficultySelected(DifficultyMenu sender);

	private LevelType _levelType;

	private Settings.DifficultyLevel difficultyChosen;

	private Levels level;

	private List<Settings.DifficultyLevel> difficultyLevelValues = Game1.GetEnumValues<Settings.DifficultyLevel>();

	private int dir;

	public LevelType levelType
	{
		get
		{
			return _levelType;
		}
		set
		{
			_levelType = value;
		}
	}

	public Settings.DifficultyLevel DifficultyChosen => difficultyChosen;

	public Levels Level
	{
		get
		{
			return level;
		}
		set
		{
			level = value;
		}
	}

	public event DifficultySelected OnDifficultySelected;

	public DifficultyMenu(Game game)
		: base(game)
	{
		for (int i = 0; i < Game1.GetEnumValues<Settings.DifficultyLevel>().Count; i++)
		{
			AddEntry(((Settings.DifficultyLevel)i).ToString().Replace('_', ' '));
			AddEntryEvent(difficultyMenu_difficultySelected);
		}
	}

	public override void Reset()
	{
		base.Reset();
		selectedEntry = (int)Settings.GetInstance().CurrentDifficulty;
		while (!IsValid((Settings.DifficultyLevel)selectedEntry))
		{
			selectedEntry--;
		}
	}

	private void difficultyMenu_difficultySelected(MenuSub1 sender)
	{
		difficultyChosen = (Settings.DifficultyLevel)selectedEntry;
		if (this.OnDifficultySelected != null)
		{
			this.OnDifficultySelected(this);
		}
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0292: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		string text = "Select Difficulty..";
		Vector2 val = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(text, new Vector2(400f, 100f), Color.Azure, 0f, val, 1.2f, (SpriteEffects)0, 1f);
		yoffset = 40f;
		Vector2 val2 = default(Vector2);
		((Vector2)(ref val2))._002Ector(400f, 300f);
		int num = 0;
		for (int i = 0; i < difficultyLevelValues.Count; i++)
		{
			if (IsValid((Settings.DifficultyLevel)i))
			{
				num++;
			}
		}
		Vector2 position = default(Vector2);
		((Vector2)(ref position))._002Ector(val2.X - 75f, yoffset + val2.Y - (float)(font.LineSpacing * num) / 3f);
		Vector2 val3 = default(Vector2);
		for (int j = 0; j < num; j++)
		{
			float num5;
			Color aliceBlue;
			if (j == selectedEntry)
			{
				float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
				float num3 = 15f / font.MeasureString(menuEntries[j]).X;
				float num4 = MyMath.Mod(num2 / 2f, 1f);
				aliceBlue = Color.AliceBlue;
				num5 = 1f + num3 * brainPulsate.Evaluate(num4);
				aliceBlue = ((Achievements.GetInstance().Data[level].isFinished && selectedEntry <= (int)Achievements.GetInstance().Data[level].difficulty) ? Color.PaleGreen : Color.AliceBlue);
			}
			else if (!Achievements.GetInstance().Data[level].isFinished || j > (int)Achievements.GetInstance().Data[level].difficulty)
			{
				aliceBlue = Color.Gray;
				num5 = 1f;
			}
			else
			{
				aliceBlue = Color.LimeGreen;
				num5 = 1f;
			}
			if (!unLockableDataEntries[j].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[j].item))
			{
				float x = font.MeasureString(menuEntries[j]).X;
				float num6 = (x * num5 - x) / 2f;
				((Vector2)(ref val3))._002Ector(num6, (float)(font.LineSpacing / 2));
				base.SpriteBatch.DrawString(font, menuEntries[j], position, aliceBlue, 0f, val3, num5, (SpriteEffects)0, 0f);
				position.Y += (float)font.LineSpacing;
			}
		}
	}

	private bool IsValid(Settings.DifficultyLevel difficulty)
	{
		bool result = true;
		if (difficulty >= Settings.DifficultyLevel.Very_Hard && !Unlockables.GetInstance().IsUnlocked(Unlockables.Items.HarderDifficulties))
		{
			result = false;
		}
		if (difficulty >= Settings.DifficultyLevel.Inzane && !Unlockables.GetInstance().IsUnlocked(Unlockables.Items.InsaneDifficulty))
		{
			result = false;
		}
		if (level == Levels.Level3 && difficulty >= Settings.DifficultyLevel.Hard && !Unlockables.GetInstance().IsUnlocked(Unlockables.Items.InsaneDifficulty))
		{
			if (Achievements.GetInstance().Data[Levels.Level1].difficulty < Settings.DifficultyLevel.Hard)
			{
				result = false;
			}
			if (Achievements.GetInstance().Data[Levels.Level2].difficulty < Settings.DifficultyLevel.Hard)
			{
				result = false;
			}
		}
		if (levelType == LevelType.Challenge)
		{
			if (difficulty > Achievements.GetInstance().Data[level].difficulty + 1)
			{
				result = false;
			}
			if (difficulty > Settings.DifficultyLevel.Easy && !Achievements.GetInstance().Data[level].isFinished)
			{
				result = false;
			}
		}
		return result;
	}

	protected override void selectNext()
	{
		base.selectNext();
		dir = 1;
	}

	protected override void selectPrevious()
	{
		base.selectPrevious();
		dir = -1;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		while (!IsValid((Settings.DifficultyLevel)selectedEntry))
		{
			selectedEntry = MyMath.Mod(selectedEntry + dir, menuEntries.Count);
		}
	}
}
