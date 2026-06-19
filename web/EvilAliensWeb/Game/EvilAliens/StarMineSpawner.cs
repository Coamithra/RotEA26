using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class StarMineSpawner : GenericSpawner
{
	public StarMineSpawner(Game game, float lifetime, float hitspersecond)
		: base(game, lifetime, hitspersecond)
	{
		SetScaleWithMultiplayer(value: true);
		SetScaleSpawns(value: false);
	}

	protected override void DoEvent(GameTime gameTime)
	{
		if (!(RandomHelper.RandomNextFloat(0f, 1f) > Settings.GetInstance().DifficultyModifier))
		{
			StarMine starMine = StarMine.NewStarMine(collectionHelper, game);
			starMine.Setup();
			collectionHelper.Add((GameComponent)(object)starMine);
		}
	}
}
