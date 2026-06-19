using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class FlyingSpiderEvent : GenericSpawner
{
	private bool isbackground;

	public FlyingSpiderEvent(Game game, float duration, float hitspersec, bool isbackground)
		: base(game, duration, hitspersec)
	{
		this.isbackground = isbackground;
		SetScaleWithMultiplayer(value: true);
	}

	protected override void DoEvent(GameTime gameTime)
	{
		FlyingSpider flyingSpider = FlyingSpider.NewFlyingSpider(collectionHelper, game);
		flyingSpider.Setup(isbackground);
		collectionHelper.Add((GameComponent)(object)flyingSpider);
	}
}
