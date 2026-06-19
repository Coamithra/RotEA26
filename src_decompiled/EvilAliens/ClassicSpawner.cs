using Microsoft.Xna.Framework;

namespace EvilAliens;

public class ClassicSpawner : GameEvent
{
	private int waves;

	private int live;

	private Oracle oracle;

	private AlienDrawableGameComponent.DeathEvent deathEvent;

	private Timer perfecttimer = new Timer(1000f, repeating: false);

	private int perfectkilling;

	private Timer startup = new Timer(2000f, repeating: false);

	public ClassicSpawner(Game game, int waves)
		: base(game, 0f)
	{
		this.waves = waves;
		live = waves;
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
		deathEvent = ufo_OnDeath;
	}

	public override void Reset()
	{
		startup.Reset();
		startup.Start();
		base.Reset();
		live = waves;
		perfectkilling = 0;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		startup.Update(gameTime);
		perfecttimer.Update(gameTime);
		if (perfecttimer.Finished)
		{
			perfectkilling = 0;
		}
		if (startup.Finished)
		{
			SoundManager soundManager = ServiceHelper.Get<ISoundManagerService>().SoundManager;
			soundManager.PlayCue("newwave");
			Vector2 val = default(Vector2);
			for (int i = 0; i < waves; i++)
			{
				((Vector2)(ref val))._002Ector(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
				switch (RandomHelper.Random.Next(4))
				{
				case 0:
					val.X = -48f;
					break;
				case 1:
					val.X = 848f;
					break;
				case 2:
					val.Y = -48f;
					break;
				case 3:
					val.Y = 648f;
					break;
				}
				switch (RandomHelper.Random.Next(3))
				{
				case 0:
				{
					UFO uFO = UFO.NewUFO(collectionHelper, game);
					uFO.Setup(val, isBig: false, EnemyBehaviour.classic);
					collectionHelper.Add((GameComponent)(object)uFO);
					uFO.OnDeath += deathEvent;
					break;
				}
				case 1:
				{
					DeathStar deathStar = DeathStar.NewDeathStar(collectionHelper, game);
					deathStar.Setup(val, EnemyBehaviour.classic);
					collectionHelper.Add((GameComponent)(object)deathStar);
					deathStar.OnDeath += deathEvent;
					break;
				}
				case 2:
				{
					EvilSkull evilSkull = EvilSkull.NewEvilSkull(collectionHelper, game);
					evilSkull.Setup(val, EnemyBehaviour.classic);
					collectionHelper.Add((GameComponent)(object)evilSkull);
					evilSkull.OnDeath += deathEvent;
					break;
				}
				}
			}
			startup.Reset();
		}
		base.Update(gameTime);
	}

	private void ufo_OnDeath(object sender)
	{
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		perfectkilling++;
		perfecttimer.Start();
		perfecttimer.Reset();
		live--;
		if (live != 0)
		{
			return;
		}
		AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collectionHelper, game);
		ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
		if (perfectkilling >= waves)
		{
			animatedMessage.Setup("Wave Completed!\n     Perfect!", SoundManager.Texts.WaveCompleted, AnimatedMessage.MessageType.starwarsblue);
			score.AddScore(100f, isCombo: false, 0);
			Powerup powerup = Powerup.NewPowerup(collectionHelper, game);
			powerup.Setup(new Vector2(RandomHelper.RandomNextFloat(10f, 790f), 624f));
			switch (RandomHelper.Random.Next(2))
			{
			case 0:
				powerup.MakeType(Powerup.PowerupType.Option);
				break;
			case 1:
				powerup.MakeType(Powerup.PowerupType.FirePower);
				break;
			}
			collectionHelper.Add((GameComponent)(object)powerup);
		}
		else
		{
			animatedMessage.Setup("Wave Completed!", SoundManager.Texts.WaveCompleted, AnimatedMessage.MessageType.starwarsblue);
		}
		collectionHelper.Add((GameComponent)(object)animatedMessage);
		Terminate();
	}
}
