using EvilAliens;

namespace EvilAliensWeb.Compat.Net
{
    // Steps 2a + 2b of the de-static refactor (card 25ad0659; plan: plans/net-headless-sim.md).
    //
    // WHAT THIS IS. The net cores (NetSession, NetPuppets, NetImpairment) reach outside their
    // own state for four kinds of thing: the four ServiceHelper services, the entity/scene
    // object graph, the WALL CLOCK, and a handful of dev flags baked at boot. INetHost is the
    // ONE injected seam that carries all of it. It arrives in three slices because the measured
    // surface is bigger than the ~30 the design doc first sketched. (The doc's own "45 + 18 + 14"
    // sizing is dead for THIS interface: 45 was a count of what the cores CALL, and 2b's finding
    // is that what has to move is the resolution, not the calls -- see the 2b block below. The
    // real total here is 11 + 4 = FIFTEEN, final: 2c added nothing to THIS interface.)
    //   * 2a -- the clock, the build/identity fingerprints and the debug flags.
    //   * 2b -- the four ServiceHelper services (oracle / bin / score / sound).
    //   * 2c -- INetEntity + INetScene, both their own interfaces rather than members here;
    //     entity creation (2c-iii) was measured and DECLINED, so nothing landed for it.
    // Everything stays STATIC and single-instance through all three; the instance cores and
    // NetContext are step 3, which the 2c-iii re-plan made optional and last.
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
    //   * (2b) NetListing's own `ServiceHelper.Get<IOracleService>()`, for the same reason its
    //     NowMs stayed out: listing plumbing a scenario never constructs.
    //   * (2b) NetPauseOverlay / NetWaitOverlay's IContentManagerService +
    //     ISpriteBatchWrapperService lookups. They are Draw-time and are not among the four
    //     services the cores reach through, so widening the seam for them would buy nothing.
    //   * (2b) The sibling test suites (NetComboTest, NetCosmeticTest, NetResetSpawnTest,
    //     NetSlotTest, NetSnapshotTest). They reach the LIVE world on purpose -- asserting
    //     against the real oracle/bin/score IS their job -- so they keep reading the registry
    //     directly. Step 4's scenarios are the ones that read through a host.
    //     ONE EXCEPTION, and it is what the rule is really about: NetPuppets.WireRoundTripTest
    //     IS on the seam. It does not assert about the live world -- it BORROWS a
    //     ScoreVisualiser to round-trip a wire frame through and puts the scores back -- so a
    //     step-4 scenario should be handed its own rather than the process's.
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
        bool NetAllowDebug { get; } // ?netallowdebug      -- ditto, for a MENU session
        bool NetLog { get; }        // ?netlog             -- verbose per-event logging
        bool NetDropGrant { get; }  // ?netdropgrant       -- one-shot dropped couch grant
        int NetLocal { get; }       // ?netlocal=<1-3>     -- synthetic couch joins
        float NetLagMs { get; }     // ?netlag
        float NetLossPct { get; }   // ?netloss
        float NetJitterMs { get; }  // ?netjitter
        // ?netstaleguard=0 -- one of the two flags here whose default answer is TRUE, because it
        // turns a shipped fix OFF rather than a diagnostic on. Card f5cf7a5c; see
        // NetPuppets.OnSnapshotEntry.
        bool SnapshotStaleGuard { get; }
        // ?netaimease=0 -- the SECOND flag here defaulting TRUE, and for the same reason: it turns
        // a fix off rather than a diagnostic on. Card eb057163; see Compat/Net/NetChargeGlow.
        bool ChargeAimEase { get; }

        // ---- step 2b: the four ServiceHelper services --------------------------------------
        //
        // WHY THESE ARE THE WHOLE OF 2b, and why it is four members rather than the doc's
        // "16 oracle.* + 5 bin.* + 8 score.* + 2 sound.*" (31). The blocker this step exists to
        // remove is named in the plan's Layer 1: `ServiceHelper` is a PROCESS-GLOBAL registry,
        // so two peers cannot hold distinct Oracles/Bins/Scores at once. What has to move is
        // therefore the RESOLUTION -- the six `ServiceHelper.Get<>()` lookups in the cores --
        // not the 79 call sites that follow it, which already read cached fields and are
        // unaffected by where those fields came from. Forwarding all 27 distinct members
        // (measured; the doc's 31 was an estimate) instead would rewrite every one of those call
        // sites, drag `PlayerShip` / `AlienDrawableGameComponent` into this interface (7 of the
        // 27 are entity-typed), and 2c would then have to redo them behind INetEntity. Same rule
        // as 2a: move the expression, verbatim, once.
        //
        // A scenario overrides these to give each peer its own services. It does NOT buy
        // Game-freedom -- all four ctors take a `Game`, and the harness lives in the game
        // assembly reached through eahl, which has one (plan banner correction 1). Layer 2,
        // the `Game` / concrete-entity entanglement, is 2c's problem and is untouched here.
        Oracle Oracle { get; }                  // ServiceHelper.Get<IOracleService>().Oracle
        ComponentBin ComponentBin { get; }      // ...IComponentBinService...ComponentBin
        ScoreVisualiser Score { get; }          // ...IScoreService....Score
        SoundManager SoundManager { get; }      // ...ISoundManagerService...SoundManager
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
