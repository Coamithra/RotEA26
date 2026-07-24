using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The HOST's interactive replacement for NetPauseOverlay while the remote peer holds a
    // pause (card 0b8a300b -- anti-griefing). Without it the host has no agency at all: a
    // remote pause freezes the world via ComponentBin.Push, which disables every collection
    // component INCLUDING GameScene, so the host's own pause trigger never runs -- and the
    // peer-drop failsafe can't rescue them either, because a held pause widens the timeout to
    // the 120s PausedPeerTimeoutMs backstop. A stranger off the public game browser could
    // freeze someone's run indefinitely.
    //
    // This works for the same reason the local pause menu does: it is added AFTER the Push, so
    // it stays Enabled and updates/draws normally over the frozen world.
    //
    // Only ever shown to the HOST -- a client keeps the plain overlay, since there is nobody
    // for it to kick (kicking the host is just leaving, which the pause menu already offers).
    // It is deliberately delayed by NetSession.KickOfferDelayMs rather than shown on the pause
    // edge: a short, innocent pause should look exactly as it always has.
    internal sealed class NetKickMenu : ConfirmationMenu
    {
        internal NetKickMenu(Game game)
            : base(game, "The other player has paused the game.")
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            // ConfirmationMenu presets selectedEntry = 1 (the Yes/No default-to-No). Here entry
            // 0 is "Keep Waiting", and it must be the default: this menu appears unprompted over
            // a frozen screen, so a reflexive Enter has to be the harmless option, not a kick.
            selectedEntry = 0;
        }
    }
}
