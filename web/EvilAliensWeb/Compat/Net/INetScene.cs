using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Step 2c of the de-static refactor (card 25ad0659; plan: plans/net-headless-sim.md), first
    // of its three slices -- the SCENE. INetEntity and entity creation follow separately, for the
    // reason 2b's split was justified: each is separately shippable and each fails differently.
    //
    // WHAT THIS IS. Everything the net cores reach on the live GameScene: the state transitions a
    // host broadcasts (reset / victory / checkpoint / pause / tether break / peer left), the
    // catch-up replay a join-in-progress peer needs, the two readbacks NetSession branches on
    // (Level, NetEndingNormally, JoinWouldSpawnNow), the host's kick menu, and SpawnPlayer.
    // Fifteen members, measured -- the design doc guessed fourteen.
    //
    // WHY IT IS WORTH A SEAM AT ALL, given the cores could just keep calling GameScene: the sim
    // has to assert on the ORDER of those transitions (the doc's scenario 6 -- interleave
    // EvReset / EvPause / EvCheckpoint and require RemotePaused to resolve only when both peers
    // are clear, and a reset mid-pause not to strand the world frozen). A real GameScene runs
    // those over ~3 s of game time with a background crossfade that needs Draw, so no headless
    // scenario can drive one; a recording INetScene turns the whole question into a list.
    //
    // AND IT PAYS OFF STEP 1b'S ONE OUTSTANDING DEBT, which is why the scene slice went first.
    // NetResetSpawnTest had to FAKE SpawnAllPlayers' respawn of the local seat, because
    // NetApplyReset purges PlayerShip and both retry legs need a non-null FindLocalShip(). That
    // fake is gone: SpawnPlayer is on this interface, so the suite drives the REAL one through a
    // decorator (see ScriptedNetScene there) instead of a hand-rolled copy that differed from it
    // in four ways.
    internal interface INetScene
    {
        // --- readbacks the session branches on -------------------------------------------------

        // The level a join-in-progress peer must be told to launch (EvLaunch).
        Levels Level { get; }

        // Victory or GameOver: the two states a peer leaving must NOT force-exit, because they
        // finish locally on both sides.
        bool NetEndingNormally { get; }

        // Whether a join arriving right now would be given a ship immediately -- the scene's own
        // answer, latched at the last CheckPlayerJoins.
        bool JoinWouldSpawnNow { get; }

        // --- host-broadcast state transitions --------------------------------------------------

        // mode is the EvReset branch the HOST took (respawn / reset / game over). The client
        // mirrors the exact transition rather than re-deriving it from its own LoseLife, which
        // no-ops on a client.
        void NetApplyReset(byte mode);

        void NetApplyVictory();

        void NetApplyCheckpoint();

        void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v);

        void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate);

        void NetApplyTetherBreak();

        // The single match-end path shared by EvLeave / drop timeout / pagehide bye / EvKick.
        void NetApplyPeerLeft();

        // --- pause, catch-up, kick -------------------------------------------------------------

        // The world unfreezes only when BOTH sides are clear; the scene owns that resolution.
        void NetSetRemotePaused(bool on);

        // Banner only -- it does NOT push the collection. The world staying live is the point.
        void NetSetPeerStalled(bool on);

        // Replays the deep mid-level scenery state (background ops, music, in-flight doodad) to a
        // peer that ran its own Initialize and so holds the level's OPENING backdrop.
        void NetReplayCatchUp();

        // The host's only agency under a remote pause. Returns false when we hold no freeze of
        // our own (our own pause menu was already up), which is what the caller latches on.
        bool NetShowKickMenu();

        // --- seating ----------------------------------------------------------------------------

        void SpawnPlayer(ControlDevice controlDevice, int slot);
    }

    // The seam a scenario swaps, and the same shape as NetHost.Current for the same reasons.
    //
    // THE PRODUCTION VALUE IS DERIVED, NEVER COPIED. GameScene.NetActiveScene stays exactly as it
    // was -- concrete, and still what AiBench / DebugInput / NetListing read -- and this reads
    // THROUGH it. A second static holding a copy would be two sources of truth for "is a scene
    // up", and the two going out of step is silent: every world message in NetSession is gated on
    // that answer, so a stale copy either drops the world on the floor or applies it into a scene
    // that has terminated.
    internal static class NetScene
    {
        private static INetScene overrideScene;

        // Assigning null hands the seam back to the live scene, so a scenario's finally is one
        // line -- the NetHost.Current rule. Note the asymmetry with NetHost: there is no
        // "production instance" to fall back to, because the honest production answer really is
        // "no scene is up", and null IS that answer here. Every core call site already null-checks
        // it (a menu session has no scene), so nothing gains a new branch.
        internal static INetScene Current
        {
            get { return overrideScene ?? GameScene.NetActiveScene; }
            set { overrideScene = value; }
        }

        // True while a scenario holds the seam. Only diagnostics should care -- it exists so a
        // suite can assert it handed the seam back, and so a stray override cannot masquerade as
        // a live scene in a log line.
        internal static bool IsOverridden => overrideScene != null;
    }
}
