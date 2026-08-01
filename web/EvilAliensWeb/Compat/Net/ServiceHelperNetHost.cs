using System;
using EvilAliens;

namespace EvilAliensWeb.Compat.Net
{
    // The PRODUCTION INetHost (card 25ad0659, steps 2a + 2b). Every member below is literally
    // the expression that used to sit at the call site -- this class exists to be the one place
    // those expressions live, not to change any of them. The production path therefore never
    // stops going through DebugFlags / WebRtcInterop / Environment.TickCount64 / ServiceHelper:
    // this IS those calls, one interface hop away.
    //
    // Step 2b is what makes the name honest: the four ServiceHelper.Get<> lookups the net cores
    // used to make -- four in NetSession.StartWith, two in NetPuppets.Enable, one in
    // NetPuppets.WireRoundTripTest -- now resolve here.
    //
    // NOT null-tolerant, deliberately. `ServiceHelper.Get<T>()` dereferences a static `Game`
    // that Game1 sets before anything constructs a session, so a null here means the process has
    // no game at all (tools/sim/logic_probe is the one such loader) -- and swallowing that would
    // hand the net layer a null service to fail on much later, somewhere unrelated. The call
    // sites threw before this card too; keep it that way.
    internal sealed class ServiceHelperNetHost : INetHost
    {
        public long NowMs
        {
            get { return Environment.TickCount64; }
        }

        // ?netfakehash=<s> makes this tab disagree with its peer on the build hash, driving the
        // real peerHash-mismatch -> SendRejectOnce path on the dev rig (both dev tabs otherwise
        // read 'dev'). Null/empty = the genuine published fingerprint. Dev-only.
        public string BuildHash
        {
            get
            {
                return string.IsNullOrEmpty(DebugFlags.NetFakeBuildHash)
                    ? WebRtcInterop.BuildHash()
                    : DebugFlags.NetFakeBuildHash;
            }
        }

        // ?netfakepeer=<s> plays the same trick on the identity token, and the loopback rig
        // NEEDS it: two dev tabs share one localStorage, so they mint the SAME eaRtc.peerId and
        // a host blocking the joiner would block itself.
        public string PeerToken
        {
            get
            {
                return string.IsNullOrEmpty(DebugFlags.NetFakePeerId)
                    ? WebRtcInterop.PeerId()
                    : DebugFlags.NetFakePeerId;
            }
        }

        public bool DebugActive
        {
            get { return DebugFlags.Active; }
        }

        public bool NetJip
        {
            get { return DebugFlags.NetJip; }
        }

        public bool NetAllowDebug
        {
            get { return DebugFlags.NetAllowDebug; }
        }

        public bool NetLog
        {
            get { return DebugFlags.NetLog; }
        }

        public bool NetDropGrant
        {
            get { return DebugFlags.NetDropGrant; }
        }

        public int NetLocal
        {
            get { return DebugFlags.NetLocal; }
        }

        public float NetLagMs
        {
            get { return DebugFlags.NetLagMs; }
        }

        public float NetLossPct
        {
            get { return DebugFlags.NetLossPct; }
        }

        public float NetJitterMs
        {
            get { return DebugFlags.NetJitterMs; }
        }

        // ---- step 2b -----------------------------------------------------------------------

        public Oracle Oracle
        {
            get { return ServiceHelper.Get<IOracleService>().Oracle; }
        }

        public ComponentBin ComponentBin
        {
            get { return ServiceHelper.Get<IComponentBinService>().ComponentBin; }
        }

        public ScoreVisualiser Score
        {
            get { return ServiceHelper.Get<IScoreService>().Score; }
        }

        public SoundManager SoundManager
        {
            get { return ServiceHelper.Get<ISoundManagerService>().SoundManager; }
        }
    }
}
