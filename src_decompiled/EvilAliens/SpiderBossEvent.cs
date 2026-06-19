using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SpiderBossEvent : GameEvent
{
	private bool bossspawned;

	private SpiderBoss boss;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	public SpiderBossEvent(Game game)
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
			boss = SpiderBoss.NewSpiderBoss(collectionHelper, game);
			boss.Setup(intro: true);
			SpiderBoss spiderBoss = boss;
			spiderBoss.OnAlmostKilled = (AlienDrawableGameComponent.DeathEvent)Delegate.Combine(spiderBoss.OnAlmostKilled, deathEvent);
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
