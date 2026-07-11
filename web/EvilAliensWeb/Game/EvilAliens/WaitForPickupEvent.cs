using Microsoft.Xna.Framework;

namespace EvilAliens;

// Tutorial pacing event: ends as soon as any player picks up the given powerup type, with
// the base lifetime as a timeout fallback so a passive player can't stall the tutorial.
// Detection subscribes to PlayerShip.OnCollectPowerup (powerup pickups don't move the
// Score powerup LEVEL — that's the power bar — so there's nothing reliable to poll).
// The -=/+= per Update keeps exactly one subscription per live ship even across death/
// respawn recycling (a recycled ship clears its event handlers).
//
// A grab does NOT end the beat instantly. The gate is LinkWith'd to the lesson's banner
// (which self-clears so the next lesson's text can't stack on it), so terminating the
// instant the powerup is touched used to kill the still-typewriting banner after only a
// few letters — by the later lessons the player knows to grab immediately, so the text
// never finished. minShowSeconds holds the beat (banner + wave) up for at least that long
// after it starts, so the short lesson text types out and stays readable before the pickup
// clears it. The timeout fallback is unaffected (a passive player still advances at it).
internal class WaitForPickupEvent : GameEvent
{
	private Powerup.PowerupType type;

	private bool pickedUp;

	private float minShowSeconds;

	private float elapsedSeconds;

	public WaitForPickupEvent(Game game, Powerup.PowerupType type, float timeoutSeconds, float minShowSeconds = 0f)
		: base(game, timeoutSeconds)
	{
		this.type = type;
		this.minShowSeconds = minShowSeconds;
		base.OnFinished += WaitForPickupEvent_OnFinished;
	}

	public override void Reset()
	{
		base.Reset();
		pickedUp = false;
		elapsedSeconds = 0f;
	}

	public override void Update(GameTime gameTime)
	{
		elapsedSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			ship.OnCollectPowerup -= ship_OnCollectPowerup;
			ship.OnCollectPowerup += ship_OnCollectPowerup;
		}
		if (pickedUp && elapsedSeconds >= minShowSeconds)
		{
			Terminate();
			return;
		}
		base.Update(gameTime);
	}

	private void ship_OnCollectPowerup(Powerup.PowerupType picked)
	{
		if (picked == type)
		{
			pickedUp = true;
		}
	}

	private void WaitForPickupEvent_OnFinished(GameEvent sender)
	{
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			ship.OnCollectPowerup -= ship_OnCollectPowerup;
		}
	}
}
