using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class BrainBossSpawner : GameEvent
{
	private bool bossspawned;

	private bool challenge;

	private BrainBoss boss;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	public BrainBossSpawner(Game game, bool challenge)
		: base(game, 0f)
	{
		this.challenge = challenge;
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
			boss = BrainBoss.NewBrainBoss(collectionHelper, game);
			boss.Setup(challenge);
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
