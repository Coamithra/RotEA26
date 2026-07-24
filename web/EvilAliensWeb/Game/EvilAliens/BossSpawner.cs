using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class BossSpawner : GameEvent
{
	private bool bossspawned;

	private Boss boss;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	public BossSpawner(Game game)
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
			boss = collectionHelper.Recycle<Boss>();
			if (boss == null)
			{
				boss = new Boss(game);
			}
			boss.Setup(new Vector2(400f, -150f));
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
}
