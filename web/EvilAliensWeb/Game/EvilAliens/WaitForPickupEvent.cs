using Microsoft.Xna.Framework;

namespace EvilAliens;

// Tutorial pacing event: ends as soon as any player picks up the given powerup type, with
// the base lifetime as a timeout fallback so a passive player can't stall the tutorial.
// Detection subscribes to PlayerShip.OnCollectPowerup (powerup pickups don't move the
// Score powerup LEVEL — that's the power bar — so there's nothing reliable to poll).
// The -=/+= per Update keeps exactly one subscription per live ship even across death/
// respawn recycling (a recycled ship clears its event handlers).
internal class WaitForPickupEvent : GameEvent
{
	private Powerup.PowerupType type;

	private bool pickedUp;

	public WaitForPickupEvent(Game game, Powerup.PowerupType type, float timeoutSeconds)
		: base(game, timeoutSeconds)
	{
		this.type = type;
		base.OnFinished += WaitForPickupEvent_OnFinished;
	}

	public override void Reset()
	{
		base.Reset();
		pickedUp = false;
	}

	public override void Update(GameTime gameTime)
	{
		foreach (PlayerShip ship in Oracle.GetShips())
		{
			ship.OnCollectPowerup -= ship_OnCollectPowerup;
			ship.OnCollectPowerup += ship_OnCollectPowerup;
		}
		if (pickedUp)
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
