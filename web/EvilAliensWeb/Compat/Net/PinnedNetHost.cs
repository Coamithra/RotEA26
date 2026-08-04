using EvilAliens;

namespace EvilAliensWeb.Compat.Net
{
    // A VIRTUAL-CLOCK INetHost for scenarios (card 25ad0659, step 2a).
    //
    // It is a DECORATOR, not a blank fake, and that is the point: it pins the clock (and, if a
    // scenario asks, the impairment knobs) and forwards everything else to the production host.
    // A scenario that wants determinism in TIME therefore does not silently also change the
    // build hash, the peer token or the debug flags out from under the code it is testing --
    // which is how a "deterministic" rig quietly stops being the thing that ships. There is NO
    // blank fake to pair it with: step 4's FakeNetHost died with FakeEntity when 2c-iii was
    // measured and declined (the sim runs under eahl, which has a Game, so the four services are
    // real and stubbing them would make every assertion about the real world vacuous). A
    // scenario needing different services supplies them itself -- see below.
    //
    // The clock does not tick. Nothing advances it but Advance(), so a scenario's assertions
    // about "before" and "after" mean exactly what they say, and the two real-clock windows
    // NetResetSpawnTest documented (FriendTimeoutMs 500 ms, the 8 s peer-drop verdict) simply
    // cannot elapse mid-run.
    internal sealed class PinnedNetHost : INetHost
    {
        private readonly INetHost inner;

        // The virtual clock, in the same milliseconds Environment.TickCount64 would report.
        // Starts at 0 deliberately: a scenario asserting on a release deadline computes it in
        // small absolute numbers, and -- see NetHostTest section 3 -- starting BELOW any real
        // uptime is what makes "the packet came out on OUR clock" fail on a wall-clock read
        // rather than pass by coincidence on a freshly-booted machine.
        internal long Now;

        // null => forward to the production host. Only the impairment triple is overridable:
        // it is the one part of the flag surface with a Game-free consumer to point at.
        internal float? LagMs;
        internal float? LossPct;
        internal float? JitterMs;

        // Overridable for the same reason as the impairment triple: it has a consumer a scenario
        // can point at with no Game involved, and NetStaleTest's sag measurement is exactly the
        // "run the same frames with the fix off" shape ?netstaleguard=0 exists for -- from a
        // suite, without a reboot. null => forward to the production flag.
        internal bool? StaleGuard;

        // Overridable for the same reason as StaleGuard: NetChargeAimTest's mechanism section is
        // exactly the "run the same frames with the fix off" shape ?netaimease=0 exists for, and a
        // suite must be able to take that measurement without rebooting. null => the production flag.
        internal bool? AimEase;

        internal PinnedNetHost()
            : this(0L, null)
        {
        }

        internal PinnedNetHost(long now, INetHost forwardTo)
        {
            Now = now;
            inner = forwardTo ?? NetHost.Production;
        }

        internal void Advance(long ms)
        {
            Now += ms;
        }

        public long NowMs => Now;

        public string BuildHash => inner.BuildHash;

        public string PeerToken => inner.PeerToken;

        public bool DebugActive => inner.DebugActive;

        public bool NetJip => inner.NetJip;
        public bool NetAllowDebug => inner.NetAllowDebug;

        public bool NetLog => inner.NetLog;

        public bool NetDropGrant => inner.NetDropGrant;

        public int NetLocal => inner.NetLocal;

        public float NetLagMs => LagMs ?? inner.NetLagMs;

        public float NetLossPct => LossPct ?? inner.NetLossPct;

        public float NetJitterMs => JitterMs ?? inner.NetJitterMs;

        public bool SnapshotStaleGuard => StaleGuard ?? inner.SnapshotStaleGuard;

        public bool ChargeAimEase => AimEase ?? inner.ChargeAimEase;

        // Step 2b's four services forward UNCONDITIONALLY -- pinning the clock must not also
        // hand the layer a different world. A scenario wanting its own services overrides them
        // in its own host implementation, not by making this class a blank fake; the
        // whole reason it is a decorator is that a rig made deterministic in TIME should change
        // nothing else out from under the code it is testing.
        public Oracle Oracle => inner.Oracle;

        public ComponentBin ComponentBin => inner.ComponentBin;

        public ScoreVisualiser Score => inner.Score;

        public SoundManager SoundManager => inner.SoundManager;
    }
}
