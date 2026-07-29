using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class MarsBossSpawner : GameEvent
{
	private bool first;

	private MarsBoss boss1;

	private MarsBoss boss2;

	public MarsBossSpawner(Game game)
		: base(game, 0f)
	{
		first = true;
	}

	private void AlienDrawableComponent_AlmostKilled(object sender)
	{
		if (sender == boss1)
		{
			boss1 = null;
		}
		if (sender == boss2)
		{
			boss2 = null;
		}
	}

	public override void Reset()
	{
		first = true;
		base.Reset();
		boss1 = null;
		boss2 = null;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (first)
		{
			first = false;
			boss1 = new MarsBoss(game);
			boss1.Setup(MarsBoss.BossPosition.left);
			MarsBoss boss1Ref = boss1;
			boss1Ref.OnAlmostKilled = (AlienDrawableGameComponent.DeathEvent)Delegate.Combine(boss1Ref.OnAlmostKilled, new AlienDrawableGameComponent.DeathEvent(AlienDrawableComponent_AlmostKilled));
			collectionHelper.Add((GameComponent)(object)boss1);
			boss2 = new MarsBoss(game);
			boss2.Setup(MarsBoss.BossPosition.right);
			MarsBoss boss2Ref = boss2;
			boss2Ref.OnAlmostKilled = (AlienDrawableGameComponent.DeathEvent)Delegate.Combine(boss2Ref.OnAlmostKilled, new AlienDrawableGameComponent.DeathEvent(AlienDrawableComponent_AlmostKilled));
			collectionHelper.Add((GameComponent)(object)boss2);
		}
		if (boss1 == null && boss2 == null)
		{
			Terminate();
		}
	}
}
