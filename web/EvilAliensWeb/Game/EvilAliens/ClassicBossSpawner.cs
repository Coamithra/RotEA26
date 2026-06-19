using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class ClassicBossSpawner : GameEvent
{
	private bool bossspawned;

	private ClassicBoss boss;

	public ClassicBossSpawner(Game game)
		: base(game, 0f)
	{
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
			boss = ClassicBoss.NewClassicBoss(collectionHelper, game);
			boss.OnDeath += boss_OnKilled;
			collectionHelper.Add((GameComponent)(object)boss);
			bossspawned = true;
		}
		base.Update(gameTime);
	}

	private void boss_OnKilled(object sender)
	{
		Terminate();
	}
}
