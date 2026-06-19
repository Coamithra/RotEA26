using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class BonusSpawner : GenericSpawner
{
	private bool mars;

	public BonusSpawner(Game game, float lifetime, float firesPerSecond, bool randomly)
		: base(game, lifetime, firesPerSecond, randomly, scaleSpawns: false)
	{
		SetScaleWithMultiplayer(value: true);
	}

	protected override void DoEvent(GameTime gameTime)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		UFO uFO = UFO.NewUFO(collectionHelper, game);
		if (!mars)
		{
			uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -24f), isBig: false, EnemyBehaviour.normal);
		}
		else
		{
			uFO.Setup(new Vector2(824f, RandomHelper.RandomNextFloat(0f, 500f)), isBig: false, EnemyBehaviour.normal);
		}
		uFO.SetAsBonus();
		collectionHelper.Add((GameComponent)(object)uFO);
	}

	public void SetMars()
	{
		mars = true;
	}
}
