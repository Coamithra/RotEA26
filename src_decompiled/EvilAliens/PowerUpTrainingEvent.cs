using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class PowerUpTrainingEvent : GameEvent
{
	private Timer timer = new Timer(10f, repeating: true);

	private Timer messageShowTime = new Timer(4500f, repeating: false);

	private Powerup.PowerupType currentType;

	private TutorialMessage message;

	private PlayerShip.CollectPowerupEvent powerupCollectEvent;

	public PowerUpTrainingEvent(Game game)
		: base(game, 0f)
	{
		base.OnFinished += PowerUpTrainingEvent_OnFinished;
		powerupCollectEvent = powerupCollected;
	}

	private void PowerUpTrainingEvent_OnFinished(GameEvent sender)
	{
		if (message == null)
		{
			return;
		}
		collectionHelper.Remove((GameComponent)(object)message);
		message = null;
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			ship.OnCollectPowerup -= powerupCollectEvent;
		}
	}

	public override void Reset()
	{
		base.Reset();
		currentType = Powerup.PowerupType.Blast;
		timer.Duration = 10f;
		timer.Reset();
		timer.Start();
		messageShowTime.Reset();
		messageShowTime.Stop();
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			ship.OnCollectPowerup += powerupCollectEvent;
		}
	}

	private void powerupCollected(Powerup.PowerupType powerupType)
	{
		messageShowTime.Reset();
		messageShowTime.Stop();
		if (message != null)
		{
			collectionHelper.Remove((GameComponent)(object)message);
			message = null;
		}
		message = TutorialMessage.NewTutorialMessage(collectionHelper, game);
		switch (powerupType)
		{
		case Powerup.PowerupType.Blast:
			message.Setup("Power up B to create larger explosions");
			break;
		case Powerup.PowerupType.Option:
			message.Setup("Power up O to increase shield speed");
			break;
		case Powerup.PowerupType.FirePower:
			message.Setup("Power up F for exploding bullets");
			break;
		case Powerup.PowerupType.Range:
			message.Setup("Power up R for bouncing bullets");
			break;
		}
		collectionHelper.Add((GameComponent)(object)message);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		timer.Update(gameTime);
		messageShowTime.Update(gameTime);
		if (timer.Finished)
		{
			timer.Duration = 7000f;
			timer.Reset();
			Powerup powerup = Powerup.NewPowerup(collectionHelper, game);
			powerup.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -40f));
			powerup.MakeType(currentType);
			collectionHelper.Add((GameComponent)(object)powerup);
			switch (currentType)
			{
			case Powerup.PowerupType.Blast:
				currentType = Powerup.PowerupType.FirePower;
				break;
			case Powerup.PowerupType.FirePower:
				currentType = Powerup.PowerupType.Option;
				break;
			case Powerup.PowerupType.Option:
				currentType = Powerup.PowerupType.Range;
				break;
			case Powerup.PowerupType.Range:
				currentType = Powerup.PowerupType.Blast;
				break;
			}
		}
		bool flag = true;
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			flag &= ServiceHelper.Get<IScoreService>().Score.GetPowerupLevel(Powerup.PowerupType.FirePower, ship.Owner) >= 1;
			flag &= ServiceHelper.Get<IScoreService>().Score.GetPowerupLevel(Powerup.PowerupType.Range, ship.Owner) >= 1;
			flag &= ServiceHelper.Get<IScoreService>().Score.GetPowerupLevel(Powerup.PowerupType.Option, ship.Owner) >= 1;
			flag &= ServiceHelper.Get<IScoreService>().Score.GetPowerupLevel(Powerup.PowerupType.Blast, ship.Owner) >= 1;
		}
		if (flag)
		{
			Terminate();
		}
	}
}
