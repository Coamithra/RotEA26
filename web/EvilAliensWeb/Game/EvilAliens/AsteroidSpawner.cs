using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class AsteroidSpawner : GenericSpawner
{
	private const float bigOneWaitTime = 4000f;

	private bool startBig;

	private bool startedWithAReallyBigOne = true;

	private Timer waitForReallyBigOne = new Timer(4000f, repeating: false);

	private float asteroidAngle;

	private float targetangle;

	private bool background;

	private Timer directionChanger = new Timer(5000f, repeating: true);

	// Online co-op (card 9a3175d0): the two SetBackground() asteroids every DoEvent emits are
	// pure belt decoration, so they are not replicated per entity -- this spawner is, as one
	// "effect on" beat, and the joiner runs its own background-only copy. Announced only by the
	// host's real (non-background-only) spawner; the joiner's copy is the effect, not its source.
	// One announce per activation; the eventList Resets an event as it activates it, which is
	// what re-arms this after a checkpoint revert.
	private bool netAnnounced;

	public AsteroidSpawner(Game game, float lifetime, float firesPerSecond, bool startWithBig)
		: base(game, lifetime, firesPerSecond, randomly: false, scaleSpawns: true)
	{
		startBig = startWithBig;
		waitForReallyBigOne.Start();
		waitForReallyBigOne.Reset();
		background = false;
		base.OnFinished += NetSwarmFinished;
	}

	public override void Reset()
	{
		base.Reset();
		netAnnounced = false;
		startedWithAReallyBigOne = startBig;
		asteroidAngle = MyMath.VectorToAngle(new Vector2(800f, 600f));
		targetangle = asteroidAngle;
		directionChanger.Duration = 5000f;
		directionChanger.Reset();
		directionChanger.Start();
	}

	public override void Update(GameTime gameTime)
	{
		// Announced from Update, not Reset: Reset is also called from the GameEvent constructor,
		// before `background` is even assigned, and being ticked is what actually means "this
		// effect is running now".
		if (!background && !netAnnounced)
		{
			netAnnounced = true;
			GameScene.NetNoteCosmeticSwarm(
				EvilAliensWeb.Compat.Net.NetCosmeticKind.BackgroundAsteroids, on: true, HitsPerSecond);
		}
		if (startedWithAReallyBigOne)
		{
			startedWithAReallyBigOne = false;
			Vector2 position = CalculateAsteroidStartPos(0.5f, 600f);
			Asteroid asteroid = Asteroid.NewAsteroid(collectionHelper, game);
			asteroid.Setup(position, asteroidAngle, 0.3f, reallyBig: true);
			collectionHelper.Add((GameComponent)(object)asteroid);
			waitForReallyBigOne.Duration = 4000f / (1f + (Settings.GetInstance().DifficultyModifier - 1f) / 2f);
			waitForReallyBigOne.Reset();
			waitForReallyBigOne.Start();
		}
		waitForReallyBigOne.Update(gameTime);
		directionChanger.Update(gameTime);
		base.Update(gameTime);
		float num = 0.0001f;
		if (asteroidAngle > targetangle)
		{
			num *= -1f;
		}
		asteroidAngle += num * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (directionChanger.Finished)
		{
			if ((targetangle == 0f) | (targetangle == (float)Math.PI / 2f))
			{
				directionChanger.Duration = 10000f;
				directionChanger.Reset();
			}
			if (targetangle == 0f)
			{
				targetangle = (float)Math.PI / 2f;
			}
			else
			{
				targetangle = 0f;
			}
		}
	}

	protected override void DoEvent(GameTime gameTime)
	{
		Vector2 position;
		Asteroid asteroid;
		if (!waitForReallyBigOne.Active & !background)
		{
			position = CalculateAsteroidStartPos(RandomHelper.RandomNextFloat(0f, 1f), 100f);
			asteroid = Asteroid.NewAsteroid(collectionHelper, game);
			asteroid.Setup(position, asteroidAngle, 0.38f, reallyBig: false);
			collectionHelper.Add((GameComponent)(object)asteroid);
		}
		position = CalculateAsteroidStartPos(RandomHelper.RandomNextFloat(0f, 1f), 100f);
		asteroid = Asteroid.NewAsteroid(collectionHelper, game);
		asteroid.Setup(position, asteroidAngle, 0.38f, reallyBig: false);
		asteroid.SetBackground();
		collectionHelper.Add((GameComponent)(object)asteroid);
		position = CalculateAsteroidStartPos(RandomHelper.RandomNextFloat(0f, 1f), 100f);
		asteroid = Asteroid.NewAsteroid(collectionHelper, game);
		asteroid.Setup(position, asteroidAngle, 0.38f, reallyBig: false);
		asteroid.SetBackground();
		collectionHelper.Add((GameComponent)(object)asteroid);
	}

	private Vector2 CalculateAsteroidStartPos(float n, float offset)
	{
		float num = asteroidAngle;
		float num2 = 600f * (float)Math.Sin(num);
		float num3 = 800f * (float)Math.Cos(num);
		num2 += offset;
		num3 += offset;
		Vector2 val = new Vector2(0f, 600f) + num2 * -MyMath.AngleToVector(num);
		Vector2 val2 = new Vector2(800f, 0f) + num3 * -MyMath.AngleToVector(num);
		return val + n * (val2 - val);
	}

	// Drop the collidable half of the pair: only the two SetBackground() decorations are spawned,
	// and Update's opening big asteroid is skipped by constructing with startWithBig:false. This
	// is exactly what a join peer's own copy of the belt needs (card 9a3175d0) -- the real
	// asteroids on its screen arrive as host-authoritative puppets.
	internal void SetBackGroundOnly()
	{
		background = true;
	}

	private void NetSwarmFinished(GameEvent sender)
	{
		if (!background && netAnnounced)
		{
			netAnnounced = false;
			GameScene.NetNoteCosmeticSwarm(
				EvilAliensWeb.Compat.Net.NetCosmeticKind.BackgroundAsteroids, on: false, 0f);
		}
	}
}
