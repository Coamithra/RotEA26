using Microsoft.Xna.Framework;

namespace EvilAliens;

// Card 0257f8ba: the host's lobby panel -- room code + live roster + Start Game/Cancel. The
// machinery is exactly NetStatusMenu's (a re-texted ConfirmationMenu); it is its own TYPE so
// eaMenuCensus can tell the roster panel from the plain status panel. The census reports type
// names, and "which panel is taking input" is precisely what the lobby probe asserts -- with
// one shared type, the panel swap on the first peer's arrival would be invisible to it.
internal class NetLobbyMenu : NetStatusMenu
{
	public NetLobbyMenu(Game game, string text)
		: base(game, text)
	{
	}
}
