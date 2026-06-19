using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class JunkBossSpawner : GameEvent
{
	private bool bossspawned;

	private bool isbase;

	private JunkBoss boss;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	public JunkBossSpawner(Game game)
		: base(game, 0f)
	{
		deathEvent = boss_OnKilled;
	}

	public void SetBase()
	{
		isbase = true;
	}

	public override void Reset()
	{
		bossspawned = false;
		base.Reset();
		boss = null;
	}

	public override void Update(GameTime gameTime)
	{
		if (!bossspawned)
		{
			boss = JunkBoss.NewJunkBoss(collectionHelper, game);
			boss.Setup(isbase);
			collectionHelper.Add((GameComponent)(object)boss);
			boss.OnDeath += deathEvent;
			bossspawned = true;
		}
		base.Update(gameTime);
	}

	private void boss_OnKilled(object sender)
	{
		Terminate();
	}
}
