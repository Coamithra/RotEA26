using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class BattleSkullEvent : GenericSpawner
{
	private bool spawnleft = true;

	public BattleSkullEvent(Game game, float lifetime, float firesPerSecond)
		: base(game, lifetime, firesPerSecond, randomly: false, scaleSpawns: false)
	{
	}

	public override void Reset()
	{
		base.Reset();
		spawnleft = true;
	}

	protected override void DoEvent(GameTime gameTime)
	{
		if (Oracle.LiveShips > 1)
		{
			SpawnBattleSkull();
			SpawnBattleSkull();
		}
		else
		{
			SpawnBattleSkull();
		}
	}

	private void SpawnBattleSkull()
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		float num = ((!spawnleft) ? 680f : 120f);
		BattleSkull battleSkull = BattleSkull.NewBattleSkull(collectionHelper, game);
		battleSkull.Setup(new Vector2(num, 700f));
		collectionHelper.Add((GameComponent)(object)battleSkull);
		spawnleft = !spawnleft;
	}
}
