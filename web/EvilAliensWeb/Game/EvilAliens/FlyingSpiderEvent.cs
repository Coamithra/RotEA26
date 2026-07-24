using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class FlyingSpiderEvent : GenericSpawner
{
	private bool isbackground;

	// Online co-op (card 9a3175d0): the BACKGROUND form of this swarm is pure fog, so its spiders
	// are not replicated per entity -- this event is, as one "effect on" beat, and the joiner runs
	// its own copy of it. One announce per activation; the eventList Resets an event as it
	// activates it, which is what re-arms this after a checkpoint revert.
	private bool netAnnounced;

	public FlyingSpiderEvent(Game game, float duration, float hitspersec, bool isbackground)
		: base(game, duration, hitspersec)
	{
		this.isbackground = isbackground;
		SetScaleWithMultiplayer(value: true);
		// Level 2 ends this swarm by LinkWith rather than by lifetime, so the "off" has to come
		// off the finish event -- Terminate is the one path every ending shares.
		base.OnFinished += NetSwarmFinished;
	}

	protected override void DoEvent(GameTime gameTime)
	{
		FlyingSpider flyingSpider = FlyingSpider.NewFlyingSpider(collectionHelper, game);
		flyingSpider.Setup(isbackground);
		collectionHelper.Add((GameComponent)(object)flyingSpider);
	}

	public override void Reset()
	{
		base.Reset();
		netAnnounced = false;
	}

	public override void Update(GameTime gameTime)
	{
		// Announced from Update, not Reset: Reset is also called from the GameEvent constructor
		// (before `isbackground` is even assigned), and being ticked is what actually means "this
		// effect is running now".
		if (isbackground && !netAnnounced)
		{
			netAnnounced = true;
			GameScene.NetNoteCosmeticSwarm(
				EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, HitsPerSecond);
		}
		base.Update(gameTime);
	}

	private void NetSwarmFinished(GameEvent sender)
	{
		if (isbackground && netAnnounced)
		{
			netAnnounced = false;
			GameScene.NetNoteCosmeticSwarm(
				EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f);
		}
	}
}
