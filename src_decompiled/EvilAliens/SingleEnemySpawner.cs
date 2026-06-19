using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SingleEnemySpawner : GameEvent
{
	private bool spawned;

	public SingleEnemySpawner(Game game)
		: base(game, 0f)
	{
	}

	public override void Reset()
	{
		base.Reset();
		spawned = false;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		if (!spawned)
		{
			spawned = true;
			UFO uFO = UFO.NewUFO(collectionHelper, game);
			uFO.Setup(new Vector2(400f, -30f), isBig: false, EnemyBehaviour.classic);
			collectionHelper.Add((GameComponent)(object)uFO);
			uFO.OnDeath += ufo_OnDeath;
		}
	}

	private void ufo_OnDeath(object sender)
	{
		Terminate();
	}
}
