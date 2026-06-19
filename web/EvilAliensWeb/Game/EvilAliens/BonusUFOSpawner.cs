using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class BonusUFOSpawner : GenericSpawner
{
	private Powerup.PowerupType powerup;

	public BonusUFOSpawner(Game game, float lifetime, float firesPerSecond, Powerup.PowerupType powerup)
		: base(game, lifetime, firesPerSecond)
	{
		this.powerup = powerup;
	}

	protected override void DoEvent(GameTime gameTime)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		UFO uFO = UFO.NewUFO(collectionHelper, game);
		uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(20f, 780f), -30f), isBig: false, EnemyBehaviour.normal);
		uFO.SetAsBonus(powerup);
		uFO.SetDirection((float)Math.PI / 2f);
		collectionHelper.Add((GameComponent)(object)uFO);
	}
}
