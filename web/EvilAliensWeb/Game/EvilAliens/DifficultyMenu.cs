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

	// Default +1 so the Update() validity-clamp loop (selectedEntry += dir) can never spin
	// with a zero step before the first arrow press sets a real direction.
	private int dir = 1;

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
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		string heading = "Select Difficulty..";
		Vector2 headingOrigin = font.MeasureString(heading) / 2f;
		base.SpriteBatch.DrawMetalString(heading, new Vector2(400f, 100f), Color.Azure, 0f, headingOrigin, 1.2f);
		yoffset = 40f;
		Vector2 menuCentre = new Vector2(400f, 300f);
		int rowCount = 0;
		for (int i = 0; i < difficultyLevelValues.Count; i++)
		{
			if (IsValid((Settings.DifficultyLevel)i))
			{
				rowCount++;
			}
		}
		Vector2 position = new Vector2(menuCentre.X - 75f, yoffset + menuCentre.Y - (float)(font.LineSpacing * rowCount) / 3f);
		Vector2 entryOrigin = default(Vector2);
		for (int j = 0; j < rowCount; j++)
		{
			float entryScale;
			Color aliceBlue;
			if (j == selectedEntry)
			{
				float pulseTime = (float)gameTime.TotalGameTime.TotalSeconds;
				float pulseAmount = 15f / font.MeasureString(menuEntries[j]).X;
				float pulsePhase = MyMath.Mod(pulseTime / 2f, 1f);
				aliceBlue = Color.AliceBlue;
				entryScale = 1f + pulseAmount * brainPulsate.Evaluate(pulsePhase);
				aliceBlue = ((Achievements.GetInstance().Data[level].isFinished && selectedEntry <= (int)Achievements.GetInstance().Data[level].difficulty) ? Color.PaleGreen : Color.AliceBlue);
			}
			else if (!Achievements.GetInstance().Data[level].isFinished || j > (int)Achievements.GetInstance().Data[level].difficulty)
			{
				aliceBlue = Color.Gray;
				entryScale = 1f;
			}
			else
			{
				aliceBlue = Color.LimeGreen;
				entryScale = 1f;
			}
			if (!unLockableDataEntries[j].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[j].item))
			{
				float x = font.MeasureString(menuEntries[j]).X;
				// Mouse hit box: non-selected rows are left-anchored at position (origin x = 0),
				// so the centre sits half a label-width to the right. The selected row's pulse
				// shifts/scales its draw slightly, but re-hovering the already-selected row is a
				// no-op, so the unscaled box is fine.
				RecordEntryHit(j, new Vector2(position.X + x / 2f, position.Y), x, font.LineSpacing);
				float pulseShiftX = (x * entryScale - x) / 2f;
				(entryOrigin) = new Vector2(pulseShiftX, (float)(font.LineSpacing / 2));
				base.SpriteBatch.DrawMetalString(font, menuEntries[j], position, aliceBlue, 0f, entryOrigin, entryScale);
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
