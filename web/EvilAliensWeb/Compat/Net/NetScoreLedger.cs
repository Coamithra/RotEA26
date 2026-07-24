using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EvilAliensWeb.Compat.Net
{
    // Client-side score reconciliation policy (card b0ab09ec).
    //
    // THE PROBLEM. Every kill is credited on BOTH peers, and each applies its own combo
    // multiplier (ScoreVisualiser.comboModify = amount * (1 + combo/20)). The combo counter is
    // a purely local simulation -- the only thing that raises it is a local bullet's first hit
    // (Bullet.CollidesWith -> SustainCombo) -- and on a client those bullets hit frozen puppets
    // interpolated ~100ms behind the host's real entities. So the two counters drift, and the
    // same kill is worth a different number on each screen.
    //
    // WHY max(local, host) MADE IT WORSE. The original 1Hz EvScoreSync adopted the larger of
    // the two, so a score could never visibly roll backwards. But max() keeps every positive
    // excursion of an error and discards every negative one: even a perfectly UNBIASED per-kill
    // difference integrates into unbounded one-way drift. That is what the playtest saw -- a
    // slot the host had at 294 reading 304 on the joiner, and growing.
    //
    // THE POLICY HERE. The host's EvDeath now carries what the host ACTUALLY credited, per
    // slot. A local credit is booked as PROVISIONAL until that figure arrives, then replaced by
    // it. EvScoreSync adopts the host's score verbatim PLUS the still-provisional total, so:
    //   * the tally converges on the host's exact number instead of drifting off it;
    //   * a kill the host has not settled yet is carried, not erased -- which is what stops
    //     verbatim adoption from sawtoothing once a second;
    //   * the player still gets instant credit on their own kills.
    // Both carriers ride the ORDERED reliable lane, which is what makes the sum exact in either
    // arrival order: an EvDeath seen before a sync is inside that sync's number and off these
    // books, one seen after is outside it and still on them.
    //
    // Entries expire (AwardSettleWindowMs) because one path never echoes a figure back: if the
    // host's copy was already dead when our claim landed, it pays us from its recent-death
    // record without re-broadcasting an EvDeath. Expiring lets the next sync land on the host's
    // exact number rather than staying inflated forever.
    //
    // Time is passed IN rather than read from Environment.TickCount64 so SelfTest can drive the
    // real policy on a virtual clock (the eaNetSim.test idiom).
    internal sealed class NetScoreLedger
    {
        internal const float AwardSettleWindowMs = 3000f;
        private const int PendingCap = 256;

        private struct Pending
        {
            public ushort NetId;
            public byte Slot;
            public float Amount;
            public long AtMs;
        }

        private readonly List<Pending> pending = new List<Pending>();
        private readonly float[] bySlot = new float[NetProtocol.MaxSlots];

        internal void Reset()
        {
            pending.Clear();
            Array.Clear(bySlot, 0, bySlot.Length);
        }

        // A local kill just credited `amount` to `slot` using OUR combo multiplier.
        internal void NoteLocal(ushort netId, byte slot, float amount, long nowMs)
        {
            if (slot >= NetProtocol.MaxSlots || amount == 0f)
            {
                return;
            }
            pending.Add(new Pending { NetId = netId, Slot = slot, Amount = amount, AtMs = nowMs });
            bySlot[slot] += amount;
            // Backstop only -- entries normally clear within an RTT and the age sweep gets the
            // rest. Dropping the oldest keeps a pathological run (host silent mid-level)
            // bounded instead of growing the list without limit.
            while (pending.Count > PendingCap)
            {
                DropAt(0);
            }
        }

        // The host's figure for (netId, slot) arrived. Returns the score delta to apply: the
        // correction when we had booked a provisional credit, the full award when we had not
        // (the other peer's kill, which we never credited).
        internal float Settle(ushort netId, byte slot, float hostAward, out bool wasProvisional)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].NetId == netId && pending[i].Slot == slot)
                {
                    float provisional = pending[i].Amount;
                    DropAt(i);
                    wasProvisional = true;
                    return hostAward - provisional;
                }
            }
            wasProvisional = false;
            return hostAward;
        }

        // Provisional total still riding on top of the host's authoritative score for a slot.
        // Sweeps expired entries first -- see the class header.
        internal float Unsettled(int slot, long nowMs)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (nowMs - pending[i].AtMs >= AwardSettleWindowMs)
                {
                    DropAt(i);
                }
            }
            return (slot >= 0 && slot < NetProtocol.MaxSlots) ? bySlot[slot] : 0f;
        }

        internal int PendingCount => pending.Count;

        private void DropAt(int i)
        {
            bySlot[pending[i].Slot] -= pending[i].Amount;
            pending.RemoveAt(i);
        }

        // ---- self-test (eaNetScore.test) --------------------------------------------------
        //
        // Drives the REAL policy above on a virtual clock against a synthetic two-peer kill
        // stream, and -- crucially -- runs the OLD max() adoption over the identical stream
        // first. A green tick on the new policy means nothing unless the same input is shown
        // to break the old one, because the failure is a slow drift that no single frame, and
        // no screenshot, can show.
        //
        // A python mirror in tools/sim/ was rejected for the same reason eaNetSim.test was:
        // the policy is ~60 lines, so a mirror would just drift from the C# and prove nothing
        // about the code that actually ships.
        //
        // Model per kill: base points, a host combo and a client combo that disagree by
        // `comboSkew` steps. Both peers credit with their own multiplier; the host's EvDeath
        // lands rttMs later; EvScoreSync fires every ScoreSyncIntervalMs. Slot 0 only.
        internal static string SelfTest(int kills, int comboSkew, int rttMs, int seed)
        {
            kills = Math.Clamp(kills <= 0 ? 400 : kills, 1, 20000);
            comboSkew = Math.Clamp(comboSkew == 0 ? 6 : comboSkew, -200, 200);
            rttMs = Math.Clamp(rttMs <= 0 ? 80 : rttMs, 0, 2000);
            const long syncMs = 1000;
            const long killSpacingMs = 120;

            var rng = new Random(seed == 0 ? 12345 : seed);
            var sb = new StringBuilder();
            sb.Append("[netscore] self-test kills=").Append(kills)
              .Append(" comboSkew=").Append(comboSkew)
              .Append(" rtt=").Append(rttMs).Append("ms seed=").Append(seed).Append('\n');

            // The synthetic stream, shared by both policies so the comparison is honest.
            var killAt = new long[kills];
            var hostAward = new float[kills];
            var clientAward = new float[kills];
            int hostCombo = 0;
            double errSum = 0.0;
            double absErrSum = 0.0;
            float maxSingleErr = 0f;
            for (int i = 0; i < kills; i++)
            {
                killAt[i] = i * killSpacingMs;
                float basePoints = 10f * (1 + rng.Next(0, 5)); // 10..50, the common enemy band
                // Both counters climb through a fight and lapse at a lull (the 1s combotimer).
                // The client's sits `comboSkew` either side of the host's, so the per-kill error
                // is UNBIASED by construction -- the whole point being that max() drifts anyway.
                // Combos are floored at comboSkew rather than clamped at 0, because clamping
                // would quietly bias the client upward and manufacture the drift we are trying
                // to demonstrate; the unbiasedness is asserted below, not assumed.
                hostCombo = rng.Next(0, 25) == 0 ? comboSkew : hostCombo + 1;
                int clientCombo = hostCombo + (rng.Next(0, 2) == 0 ? comboSkew : -comboSkew);
                hostAward[i] = basePoints * (1f + hostCombo / 20f);
                clientAward[i] = basePoints * (1f + clientCombo / 20f);
                errSum += clientAward[i] - hostAward[i];
                absErrSum += Math.Abs(clientAward[i] - hostAward[i]);
                maxSingleErr = Math.Max(maxSingleErr, Math.Abs(clientAward[i] - hostAward[i]));
            }
            float meanErr = (float)(errSum / kills);
            float meanAbsErr = (float)(absErrSum / kills);

            float legacyGap = RunLegacyMax(kills, killAt, hostAward, clientAward, rttMs, syncMs, out float legacyFinalHost, out float legacyFinalClient);
            var ledger = new NetScoreLedger();
            float newGap = RunCurrent(ledger, kills, killAt, hostAward, clientAward, rttMs, syncMs,
                out float finalHost, out float finalClient, out float worstStep, out float maxSyncJump);

            sb.Append("  injected per-kill error: mean=").Append(F(meanErr))
              .Append(" meanAbs=").Append(F(meanAbsErr))
              .Append(" (mean must be ~0 -- an upward-biased stream would 'reproduce' the drift for the wrong reason)\n");
            sb.Append("  old max()  host=").Append(F(legacyFinalHost))
              .Append(" client=").Append(F(legacyFinalClient))
              .Append(" gap=").Append(F(legacyFinalClient - legacyFinalHost))
              .Append(" maxGap=").Append(F(legacyGap)).Append('\n');
            sb.Append("  new policy host=").Append(F(finalHost))
              .Append(" client=").Append(F(finalClient))
              .Append(" gap=").Append(F(finalClient - finalHost))
              .Append(" maxInvariantErr=").Append(F(newGap))
              .Append(" worstDownStep=").Append(F(worstStep))
              .Append(" (worst single kill error ").Append(F(maxSingleErr)).Append(")")
              .Append(" maxSyncJump=").Append(F(maxSyncJump))
              .Append(" pendingLeft=").Append(ledger.PendingCount).Append('\n');
            // maxGap and maxInvariantErr are NOT the same statistic and must not be read as a
            // before/after pair: the old policy's is max(client - host), this one's is
            // max|client - host - unsettled|, i.e. how far the ledger's own books drifted from
            // the score they are supposed to explain. worstDownStep is reported, not asserted
            // against maxSingleErr -- in this sim those two are the same quantity by
            // construction, so such a check would be green whatever the ledger did.

            // A float score accumulated over thousands of credits carries float32 rounding, so
            // "equal" is a tolerance, not ==. It is FAR below the 1-point display quantum.
            float tol = Math.Max(0.5f, finalHost * 1e-4f);
            bool converges = Math.Abs(finalClient - finalHost) <= tol;
            // The injected error must be near-symmetric, or the drift below is just bias
            // showing through and says nothing about max().
            bool unbiased = Math.Abs(meanErr) <= meanAbsErr * 0.15f;
            // The old policy must actually FAIL on this stream, or the test proves nothing.
            // The bar is one kill's worth of error, not a multiple of it: the legacy gap is a
            // random walk reflected at zero, so its size at a given seed is luck -- only its
            // SIGN is structural. A tighter bar would flake on a caller-supplied seed.
            bool reproducedBug = legacyFinalClient - legacyFinalHost > meanAbsErr;
            // Every entry must have been settled and removed -- an accounting leak here is how
            // the books would silently stop explaining the score.
            bool drained = ledger.PendingCount == 0;
            // The sawtooth guard, and the real test of the bookkeeping: `client` is only ever
            // moved by Settle's returns, so this only stays at zero if Settle matched the right
            // (netId, slot) with the right amount and Unsettled summed the remainder correctly.
            bool syncSilent = maxSyncJump <= tol && newGap <= tol;

            sb.Append(unbiased ? "  PASS" : "  FAIL")
              .Append(" injected per-kill error is unbiased (|mean| <= 15% of meanAbs)\n");
            sb.Append(reproducedBug ? "  PASS" : "  FAIL")
              .Append(" reproduces the old ratchet: max() drove the client above the host anyway\n");
            sb.Append(converges ? "  PASS" : "  FAIL")
              .Append(" new policy converges on the host tally (|gap| <= ").Append(F(tol)).Append(")\n");
            sb.Append(drained ? "  PASS" : "  FAIL")
              .Append(" every provisional entry was settled and removed (no accounting leak)\n");
            sb.Append(syncSilent ? "  PASS" : "  FAIL")
              .Append(" the books explain the score at all times, so the 1Hz sync never moves it\n");
            sb.Append(unbiased && reproducedBug && converges && drained && syncSilent
                ? "[netscore] PASS" : "[netscore] FAIL");
            return sb.ToString();
        }

        private static string F(float v)
        {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        // The policy this card replaced: credit locally with our own multiplier, then adopt
        // max(local, host) at each sync.
        private static float RunLegacyMax(int kills, long[] killAt, float[] hostAward, float[] clientAward,
            int rttMs, long syncMs, out float finalHost, out float finalClient)
        {
            float host = 0f;
            float client = 0f;
            float maxGap = 0f;
            int next = 0;
            int settled = 0;
            long end = killAt[kills - 1] + rttMs + 2 * syncMs;
            for (long t = 0; t <= end; t += 10)
            {
                while (next < kills && killAt[next] <= t)
                {
                    client += clientAward[next]; // instant local credit, our multiplier
                    next++;
                }
                while (settled < kills && killAt[settled] + rttMs <= t)
                {
                    host += hostAward[settled]; // host settles the claim at +rtt
                    settled++;
                }
                if (t % syncMs == 0 && host > client)
                {
                    client = host; // NetAdoptScore: raise only
                }
                maxGap = Math.Max(maxGap, client - host);
            }
            finalHost = host;
            finalClient = client;
            return maxGap;
        }

        // The shipped policy: provisional local credit, replaced by the host's figure when the
        // EvDeath lands, and verbatim + unsettled adoption at each sync.
        private static float RunCurrent(NetScoreLedger ledger, int kills, long[] killAt, float[] hostAward,
            float[] clientAward, int rttMs, long syncMs,
            out float finalHost, out float finalClient, out float worstStep, out float maxSyncJump)
        {
            float host = 0f;
            float client = 0f;
            float maxGap = 0f;
            worstStep = 0f;
            maxSyncJump = 0f;
            int next = 0;
            int settled = 0;
            long end = killAt[kills - 1] + rttMs + 2 * syncMs;
            for (long t = 0; t <= end; t += 10)
            {
                while (next < kills && killAt[next] <= t)
                {
                    client += clientAward[next];
                    ledger.NoteLocal((ushort)(next + 1), 0, clientAward[next], t);
                    next++;
                }
                while (settled < kills && killAt[settled] + rttMs <= t)
                {
                    host += hostAward[settled];
                    // The host's EvDeath reaches us in the same hop that settled it. The delta
                    // is negative whenever our combo ran hotter than the host's -- an inherent,
                    // per-kill-sized correction, which is exactly what the assertions bound.
                    float delta = ledger.Settle((ushort)(settled + 1), 0, hostAward[settled], out _);
                    client += delta;
                    worstStep = Math.Min(worstStep, delta);
                    settled++;
                }
                if (t % syncMs == 0)
                {
                    // The invariant that killed the sawtooth: with `unsettled` carried, the 1Hz
                    // adoption must not move the displayed score at all.
                    float adopted = host + ledger.Unsettled(0, t);
                    maxSyncJump = Math.Max(maxSyncJump, Math.Abs(adopted - client));
                    client = adopted;
                }
                maxGap = Math.Max(maxGap, Math.Abs(client - host - ledger.Unsettled(0, t)));
            }
            finalHost = host;
            finalClient = client;
            return maxGap;
        }
    }
}
