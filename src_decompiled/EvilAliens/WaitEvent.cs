using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class WaitEvent : GameEvent
{
	public WaitEvent(Game game, float lifetime)
		: base(game, lifetime)
	{
	}
}
