namespace EvilAliensWeb.Compat
{
    // Parsed from ?net=host / ?net=join / ?net=jiphost / ?net=jipjoin (DebugFlags). None = the
    // co-op net layer is never constructed -- a plain boot stays byte-identical single-player.
    // In the Compat namespace (not Compat.Net) because DebugFlags carries it.
    //
    // Host/Join are the original dev loopback rig: BOTH peers boot with ?level=, neither is a
    // menu or a listed session, and the pairing is immediate. JipHost/JipJoin (card 054947f3)
    // are the JOIN-IN-PROGRESS shapes of the same rig -- the host attaches a real
    // StartListedSession to a running level only once a peer actually appears, and the joiner is
    // a real menu-session client that mirrors the host's EvLaunch and warms the level itself. So
    // they reach `MenuScene.NetLaunchMirror` -> scene Initialize -> `EvReady` -> `ReplayLive`,
    // which Host/Join structurally cannot: both of those already hold a scene at pairing time.
    public enum NetRole
    {
        None,
        Host,
        Join,
        JipHost,
        JipJoin
    }
}
