using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SkullSpawner : GenericSpawner
{
	private bool maze;

	private bool bonusonly;

	public SkullSpawner(Game game, float lifetime, float hitspersecond, bool maze, bool bonusonly)
		: base(game, lifetime, hitspersecond)
	{
		SetScaleWithMultiplayer(value: true);
		this.maze = maze;
		if (bonusonly)
		{
			this.bonusonly = bonusonly;
			SetRandomSpawn(value: false);
			SetScaleSpawns(value: false);
		}
	}

	protected override void DoEvent(GameTime gameTime)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		EvilSkull evilSkull = EvilSkull.NewEvilSkull(collectionHelper, game);
		evilSkull.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -48f), EnemyBehaviour.normal);
		if (maze)
		{
			evilSkull.SetMaze();
		}
		if (bonusonly || RandomHelper.RandomNextFloat(0f, 100f) <= 10f)
		{
			evilSkull.MakeBonus();
		}
		collectionHelper.Add((GameComponent)(object)evilSkull);
	}
}
