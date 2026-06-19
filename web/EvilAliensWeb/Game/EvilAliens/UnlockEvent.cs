using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class UnlockEvent : GameEvent
{
	private AnimatedMessage message;

	private string text;

	private bool first;

	private Levels level;

	private Unlockables.Items item;

	private AnimatedMessage.UnlockType unlockType;

	private SoundManager.Texts speechText;

	public UnlockEvent(Game game, string text, Unlockables.Items item, AnimatedMessage.UnlockType unlockType, Levels level)
		: base(game, 0f)
	{
		this.text = text;
		first = true;
		this.item = item;
		this.level = level;
		this.unlockType = unlockType;
		switch (unlockType)
		{
		case AnimatedMessage.UnlockType.challenge:
			speechText = SoundManager.Texts.ChallengeUnlocked;
			break;
		case AnimatedMessage.UnlockType.cheat:
			speechText = SoundManager.Texts.CheatUnlocked;
			break;
		case AnimatedMessage.UnlockType.level:
			speechText = SoundManager.Texts.LevelUnlocked;
			break;
		case AnimatedMessage.UnlockType.difficulty:
			speechText = SoundManager.Texts.DifficultyUnlocked;
			break;
		}
	}

	public override void Reset()
	{
		base.Reset();
		first = true;
		message = null;
	}

	public override void Update(GameTime gameTime)
	{
		if (message == null && Unlockables.GetInstance().IsUnlocked(item))
		{
			Terminate();
		}
		else
		{
			if (!first)
			{
				return;
			}
			if (!CheckForRequirements())
			{
				Terminate();
				return;
			}
			Unlockables.GetInstance().Unlock(item);
			if (item == Unlockables.Items.HarderDifficulties)
			{
				Unlockables.GetInstance().Unlock(Unlockables.Items.InsaneDifficulty);
			}
			if (unlockType == AnimatedMessage.UnlockType.cheat)
			{
				Unlockables.GetInstance().Unlock(Unlockables.Items.Cheats);
			}
			if (unlockType == AnimatedMessage.UnlockType.challenge)
			{
				Unlockables.GetInstance().Unlock(Unlockables.Items.Challenges);
			}
			Unlockables.GetInstance().SaveThreaded();
			message = AnimatedMessage.NewAnimatedMessage(collectionHelper, game);
			message.Setup(text, speechText, AnimatedMessage.MessageType.unlocked);
			message.SetUnlockType(unlockType);
			collectionHelper.Add((GameComponent)(object)message);
			first = false;
			game.Components.ComponentRemoved += Components_ComponentRemoved;
		}
	}

	private bool CheckForRequirements()
	{
		if (Settings.GetInstance().CheckForCheats())
		{
			return false;
		}
		bool flag = true;
		switch (item)
		{
		case Unlockables.Items.HarderDifficulties:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard;
			break;
		case Unlockables.Items.InsaneDifficulty:
			flag &= Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Very_Hard;
			flag &= Levels.Level1 == level || Achievements.GetInstance().Data[Levels.Level1].difficulty == Settings.DifficultyLevel.Very_Hard;
			flag &= Levels.Level2 == level || Achievements.GetInstance().Data[Levels.Level2].difficulty == Settings.DifficultyLevel.Very_Hard;
			flag &= Levels.Level3 == level || Achievements.GetInstance().Data[Levels.Level3].difficulty == Settings.DifficultyLevel.Very_Hard;
			flag = false;
			break;
		case Unlockables.Items.Braineroids:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard;
			break;
		case Unlockables.Items.TeamChallenge:
			flag &= Oracle.Players > 1;
			break;
		case Unlockables.Items.BossTrain:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard;
			break;
		case Unlockables.Items.OwnLevel:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard;
			break;
		case Unlockables.Items.InfiniteLives:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Very_Hard;
			break;
		case Unlockables.Items.Turbo:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Very_Hard;
			break;
		case Unlockables.Items.Friends:
			flag = false;
			break;
		case Unlockables.Items.PowerUp:
			flag &= Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Very_Hard;
			break;
		}
		return flag;
	}

	public void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == message)
		{
			message = null;
		}
	}
}
