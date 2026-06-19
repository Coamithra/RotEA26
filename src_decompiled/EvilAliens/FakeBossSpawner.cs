using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class FakeBossSpawner : GameEvent
{
	private bool bossspawned;

	private bool forceDifficulty;

	private Settings.DifficultyLevel forcedDifficultyLevel;

	private FakeBoss boss;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	public FakeBossSpawner(Game game)
		: base(game, 0f)
	{
		deathEvent = boss_OnKilled;
	}

	public override void Reset()
	{
		bossspawned = false;
		base.Reset();
	}

	public override void Update(GameTime gameTime)
	{
		if (!bossspawned)
		{
			boss = FakeBoss.NewFakeBoss(collectionHelper, game);
			boss.Setup();
			if (forceDifficulty)
			{
				boss.ForceDifficulty(forcedDifficultyLevel);
			}
			boss.OnDeath += deathEvent;
			collectionHelper.Add((GameComponent)(object)boss);
			bossspawned = true;
		}
		base.Update(gameTime);
	}

	private void boss_OnKilled(object sender)
	{
		Terminate();
	}

	internal void ForceDifficulty(Settings.DifficultyLevel difficultyLevel)
	{
		forceDifficulty = true;
		forcedDifficultyLevel = difficultyLevel;
	}
}
