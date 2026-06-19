using Microsoft.Xna.Framework;

namespace EvilAliens;

public class BrainSpawner : GenericSpawner
{
	private bool wrapping;

	public BrainSpawner(Game game, float lifetime, float firesPerSecond, bool wrapping)
		: base(game, lifetime, firesPerSecond, randomly: false, scaleSpawns: true)
	{
		this.wrapping = wrapping;
	}

	protected override void DoEvent(GameTime gameTime)
	{
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		Braineroid braineroid = Braineroid.NewBraineroid(collectionHelper, game);
		float num = 200f;
		Vector2 position = default(Vector2);
		switch (RandomHelper.Random.Next(1, 4))
		{
		case 1:
			(position) = new Vector2(0f - num, RandomHelper.RandomNextFloat(0f, 600f));
			break;
		case 2:
			(position) = new Vector2(800f + num, RandomHelper.RandomNextFloat(0f, 600f));
			break;
		case 3:
			(position) = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 0f - num);
			break;
		case 4:
			(position) = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 600f + num);
			break;
		default:
			(position) = new Vector2(400f, 300f);
			break;
		}
		braineroid.Setup(position, BrainSize.huge, 0f, wrapping);
		collectionHelper.Add((GameComponent)(object)braineroid);
	}
}
