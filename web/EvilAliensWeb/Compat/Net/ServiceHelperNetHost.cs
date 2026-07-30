using System;

namespace EvilAliensWeb.Compat.Net
{
    // The PRODUCTION INetHost (card 25ad0659, step 2a). Every member below is literally the
    // expression that used to sit at the call site -- this class exists to be the one place
    // those expressions live, not to change any of them. The production path therefore never
    // stops going through DebugFlags / WebRtcInterop / Environment.TickCount64: this IS those
    // calls, one interface hop away.
    //
    // The name is forward-looking: step 2b moves the four ServiceHelper.Get<> lookups here too,
    // at which point it earns it. In 2a it holds none, and that is not an oversight.
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
    }
}
