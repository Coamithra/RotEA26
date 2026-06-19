using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class StationarySpawner : GenericSpawner
{
	private float bottom;

	private float ufochance;

	private float bigufochance;

	private float brainchance;

	private float spiderchance;

	public StationarySpawner(Game game, float bottom, float lifetime, float hitspersecond)
		: base(game, lifetime, hitspersecond, randomly: false, scaleSpawns: true)
	{
		this.bottom = bottom;
	}

	public void SetChances(float ufochance, float bigufochance, float brainchance, float spiderchance)
	{
		this.ufochance = ufochance;
		this.bigufochance = bigufochance;
		this.brainchance = brainchance;
		this.spiderchance = spiderchance;
	}

	protected override void DoEvent(GameTime gameTime)
	{
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		float num = RandomHelper.RandomNextFloat(0f, ufochance + bigufochance + brainchance + spiderchance);
		if (num >= ufochance + bigufochance + brainchance)
		{
			Spider spider = Spider.NewSpider(collectionHelper, game);
			spider.Setup();
			collectionHelper.Add((GameComponent)(object)spider);
		}
		else if (num >= ufochance + bigufochance)
		{
			Braineroid braineroid = Braineroid.NewBraineroid(collectionHelper, game);
			braineroid.Setup(new Vector2(950f, RandomHelper.RandomNextFloat(bottom - 90f, bottom - 70f)), BrainSize.huge, 0f, wrapping: false);
			braineroid.SetupStationary();
			collectionHelper.Add((GameComponent)(object)braineroid);
		}
		else if (num >= ufochance)
		{
			UFO uFO = UFO.NewUFO(collectionHelper, game);
			uFO.Setup(new Vector2(950f, RandomHelper.RandomNextFloat(bottom - 45f, bottom - 20f)), isBig: true, EnemyBehaviour.normal);
			uFO.SetStationary();
			collectionHelper.Add((GameComponent)(object)uFO);
		}
		else
		{
			UFO uFO2 = UFO.NewUFO(collectionHelper, game);
			uFO2.Setup(new Vector2(848f, RandomHelper.RandomNextFloat(bottom - 15f, bottom)), isBig: false, EnemyBehaviour.normal);
			uFO2.SetStationary();
			collectionHelper.Add((GameComponent)(object)uFO2);
		}
	}
}
