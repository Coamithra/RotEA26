using Microsoft.Xna.Framework;

namespace EvilAliens;

// Card 11.4: the online-co-op lobby status panel -- a ConfirmationMenu whose message is
// re-texted per lobby phase (room code / waiting / connecting / failure notice) with a
// single Cancel/Back entry. ConfirmationMenu.Initialize presets selectedEntry = 1 (the
// Yes/No default-to-No), which would index past this menu's single entry on select.
internal class NetStatusMenu : ConfirmationMenu
{
	public NetStatusMenu(Game game, string text)
		: base(game, text)
	{
	}

	public override void Initialize()
	{
		base.Initialize();
		selectedEntry = 0;
	}
}
