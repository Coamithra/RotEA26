namespace EvilAliensWeb.Compat.Net
{
    // Step 2a of the de-static refactor (card 25ad0659; plan: plans/net-headless-sim.md).
    //
    // WHAT THIS IS. The net cores (NetSession, NetPuppets, NetImpairment) reach outside their
    // own state for four kinds of thing: the four ServiceHelper services, the entity/scene
    // object graph, the WALL CLOCK, and a handful of dev flags baked at boot. INetHost is the
    // ONE injected seam that carries all of it. It arrives in three slices because the measured
    // surface is 45 + 18 + 14 members, not the ~30 the design doc first sketched:
    //   * 2a (this file) -- the clock, the build/identity fingerprints and the debug flags.
    //   * 2b -- the four ServiceHelper services (oracle / bin / score / sound).
    //   * 2c -- INetEntity + INetScene + entity creation.
    // Everything stays STATIC and single-instance through all three; the instance cores and
    // NetContext are step 3.
    //
    // WHY THE CLOCK FIRST. Every scenario worth writing against this layer is about ORDERING
    // over time (a claim inside the RTT window, a grant expiring, a snapshot racing a reset),
    // and today those cadences are read straight off Environment.TickCount64. A test can only
    // reach them by sending real packets and hoping the wall clock cooperates -- which is
    // exactly what NetResetSpawnTest has to do, re-arming both of its windows by re-sending
    // immediately before each Update. A virtual clock turns every one of those into an
    // assertion instead of a hope, so it buys determinism for all of 2b, 2c, 3 and 4.
    //
    // WHAT IS DELIBERATELY *NOT* HERE:
    //   * NetSession.Start()'s own ?net= / ?rtc / ?room= reads. That method is the COMPOSITION
    //     ROOT -- it decides whether a session is constructed at all and which transport it
    //     gets. A host cannot answer that, because it is chosen after the answer is known.
    //   * NetListing / NetLobby / NetGameBrowser / WebRtcTransport. The sim never constructs
    //     any of them, so they are outside the seam; NetListing keeps its own NowMs.
    //   * NetWaitOverlay's clock read. It is a Draw-time pulse alpha, not session cadence.
    internal interface INetHost
    {
        // Real-time milliseconds. The whole net layer runs on this rather than on the GameTime
        // Game1 hands components, because that one is scaled by turbo / slow-mo / hit-stop and
        // scaling a network cadence by a local visual effect is how the pupPops burst happened.
        long NowMs { get; }

        // The published binary's fingerprint, ALREADY resolved against ?netfakehash. Hashed by
        // the caller into the handshake's compatibility key -- peers must run the identical
        // binary or they are rejected with "update required".
        string BuildHash { get; }

        // This peer's self-reported identity token, ALREADY resolved against ?netfakepeer.
        // May be empty, which the caller maps to id 0 ("no identity") rather than hashing.
        string PeerToken { get; }

        // Flag members keep their DebugFlags names on purpose: step 2a's review question is
        // "is every mapped call 1:1", and name identity is what makes that answerable by
        // reading rather than by tracing. DebugActive is the one rename (DebugFlags.Active
        // would read as NetSession.Active at these call sites, which is a different thing).
        bool DebugActive { get; }   // DebugFlags.Active   -- refuses menu-session pairing
        bool NetJip { get; }        // ?netjip             -- present as clean anyway
        bool NetLog { get; }        // ?netlog             -- verbose per-event logging
        bool NetDropGrant { get; }  // ?netdropgrant       -- one-shot dropped couch grant
        int NetLocal { get; }       // ?netlocal=<1-3>     -- synthetic couch joins
        float NetLagMs { get; }     // ?netlag
        float NetLossPct { get; }   // ?netloss
        float NetJitterMs { get; }  // ?netjitter
    }

    // The single production instance, and the seam a scenario swaps.
    internal static class NetHost
    {
        internal static readonly INetHost Production = new ServiceHelperNetHost();

        private static INetHost current = Production;

        // NEVER NULL. Assigning null restores the production host, so a scenario's finally is
        // one line and cannot leave the layer holding a dead override -- which would be a
        // silent, session-wide fault (a frozen clock reads as "the peer stopped sending").
        internal static INetHost Current
        {
            get { return current; }
            set { current = value ?? Production; }
        }
    }
}
