using System;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The ONE-WRITER-PER-SLOT score policy's self-test (eaNetScore.test, card af96bcc2).
    //
    // THE POLICY. Each slot has exactly one writer -- its owner. A kill is credited instantly
    // and FINALLY by whoever observed it, on their own slots, with their own combo multiplier
    // (AwardScore's OwnsSlot gate); the peer's copy of a slot is a plain replica, adopting the
    // owner's declared TOTAL off MsgHudState verbatim (ScoreVisualiser.NetSetScore). Nothing is
    // arbitrated, nothing settles, nothing is provisional. Double credit for one kill -- each
    // peer paying its own side -- is intended.
    //
    // WHY A REPLICA OF ONE WRITER CANNOT DRIFT, and what this suite pins: drift needs a second
    // writer to disagree with. The two superseded designs are run over the IDENTICAL kill
    // stream as negative controls, because a green tick on the new policy means nothing unless
    // the same input is shown to break what it replaced (the eaNetSim.test rule):
    //   * the pre-b0ab09ec max(local, host) adoption -- both peers credited every kill with
    //     their own combos and the replica kept the larger figure, turning an UNBIASED per-kill
    //     difference into unbounded one-way drift (the ratchet);
    //   * the two-writer model itself with no reconciliation at all -- what NetScoreLedger's
    //     provisional/settle machinery existed to patch, deleted with this card.
    //
    // Time is passed IN rather than read from a clock so the sim is deterministic (the
    // NetScoreLedger.SelfTest idiom, which this file replaces). A python mirror in tools/sim/
    // was rejected for the same reason as ever: the policy is a handful of lines, so a mirror
    // would drift from the C# and prove nothing about the code that ships.
    internal static class NetScoreTest
    {
        internal static string SelfTest(int kills, int comboSkew, int rttMs, int seed)
        {
            kills = Math.Clamp(kills <= 0 ? 400 : kills, 1, 20000);
            comboSkew = Math.Clamp(comboSkew == 0 ? 6 : comboSkew, -200, 200);
            rttMs = Math.Clamp(rttMs <= 0 ? 80 : rttMs, 0, 2000);
            const long syncMs = 100;      // the ~10 Hz MsgHudState cadence the totals ride now
            const long killSpacingMs = 120;

            var rng = new Random(seed == 0 ? 12345 : seed);
            var sb = new StringBuilder();
            sb.Append("[netscore] one-writer self-test kills=").Append(kills)
              .Append(" comboSkew=").Append(comboSkew)
              .Append(" rtt=").Append(rttMs).Append("ms seed=").Append(seed).Append('\n');

            // The synthetic stream, shared by every policy below so the comparison is honest.
            // ownerAward is what slot 0's OWNER credits per kill; otherAward is what the OTHER
            // peer would credit for the same kill with ITS combo -- the new policy never spends
            // it (that is the point), the two controls do.
            var killAt = new long[kills];
            var ownerAward = new float[kills];
            var otherAward = new float[kills];
            int ownerCombo = 0;
            double errSum = 0.0;
            double absErrSum = 0.0;
            for (int i = 0; i < kills; i++)
            {
                killAt[i] = i * killSpacingMs;
                float basePoints = 10f * (1 + rng.Next(0, 5)); // 10..50, the common enemy band
                // Both counters climb through a fight and lapse at a lull. The other peer's sits
                // comboSkew either side of the owner's, so the per-kill error is UNBIASED by
                // construction -- the ratchet control must drift anyway, or it proves nothing.
                ownerCombo = rng.Next(0, 25) == 0 ? comboSkew : ownerCombo + 1;
                int otherCombo = ownerCombo + (rng.Next(0, 2) == 0 ? comboSkew : -comboSkew);
                ownerAward[i] = basePoints * (1f + ownerCombo / 20f);
                otherAward[i] = basePoints * (1f + otherCombo / 20f);
                errSum += otherAward[i] - ownerAward[i];
                absErrSum += Math.Abs(otherAward[i] - ownerAward[i]);
            }
            float meanErr = (float)(errSum / kills);
            float meanAbsErr = (float)(absErrSum / kills);

            // ---- 1. the shipped policy: owner writes, replica adopts verbatim ----------------
            //
            // The replica hears the owner's total rttMs late and adopts it at the sync cadence.
            // What must hold: EXACT equality with the figure it adopted (no arithmetic of its
            // own anywhere), monotone display, staleness bounded by what happened SINCE that
            // figure -- never a drift term that grows with kill count.
            float owner = 0f;
            float replica = 0f;
            float lastDeclared = 0f;       // the freshest total the wire has delivered
            float maxAdoptError = 0f;      // replica vs the figure it should hold -- must be 0
            float maxStaleness = 0f;       // replica vs the owner's LIVE figure -- bounded, not 0
            float worstDownStep = 0f;      // the replica must never move backwards
            int next = 0;
            long end = killAt[kills - 1] + rttMs + 2 * syncMs;
            for (long t = 0; t <= end; t += 10)
            {
                while (next < kills && killAt[next] <= t)
                {
                    owner += ownerAward[next]; // the one writer, instant and final
                    next++;
                }
                if (t % syncMs == 0)
                {
                    // What the owner had declared one RTT ago -- rebuild it from the stream
                    // rather than buffering, since the total is monotone in t.
                    float declared = 0f;
                    for (int i = 0; i < kills && killAt[i] <= t - rttMs; i++)
                    {
                        declared += ownerAward[i];
                    }
                    lastDeclared = declared;
                    float prev = replica;
                    replica = declared;    // NetSetScore: verbatim, no local term
                    worstDownStep = Math.Min(worstDownStep, replica - prev);
                }
                maxAdoptError = Math.Max(maxAdoptError, Math.Abs(replica - lastDeclared));
                maxStaleness = Math.Max(maxStaleness, Math.Abs(owner - replica));
            }
            float finalGap = Math.Abs(owner - replica);

            // The staleness bound: everything the owner credited inside one sync interval plus
            // one RTT of lag. Derived from the same stream, so the assertion cannot be tuned to
            // pass -- it is "bounded by the window", the opposite shape to a drift.
            float windowBound = 0f;
            for (int i = 0; i < kills; i++)
            {
                float inWindow = 0f;
                for (int j = i; j < kills && killAt[j] <= killAt[i] + syncMs + rttMs; j++)
                {
                    inWindow += ownerAward[j];
                }
                windowBound = Math.Max(windowBound, inWindow);
            }

            // ---- 2. negative control: the pre-b0ab09ec max() ratchet -------------------------
            float ratchetOwner = 0f;
            float ratchetReplica = 0f;
            next = 0;
            int heard = 0;
            for (long t = 0; t <= end; t += 10)
            {
                while (next < kills && killAt[next] <= t)
                {
                    ratchetOwner += ownerAward[next];
                    ratchetReplica += otherAward[next]; // the second writer, its own combo
                    next++;
                }
                while (heard < kills && killAt[heard] + rttMs <= t)
                {
                    heard++;
                }
                if (t % syncMs == 0)
                {
                    float declared = 0f;
                    for (int i = 0; i < heard; i++)
                    {
                        declared += ownerAward[i];
                    }
                    ratchetReplica = Math.Max(ratchetReplica, declared); // raise only
                }
            }

            // ---- 3. negative control: two writers, no reconciliation at all ------------------
            float naiveOwner = 0f;
            float naiveReplica = 0f;
            for (int i = 0; i < kills; i++)
            {
                naiveOwner += ownerAward[i];
                naiveReplica += otherAward[i];
            }

            sb.Append("  injected per-kill error: mean=").Append(F(meanErr))
              .Append(" meanAbs=").Append(F(meanAbsErr))
              .Append(" (mean must be ~0 -- a biased stream would flatter every control)\n");
            sb.Append("  one-writer: owner=").Append(F(owner)).Append(" replica=").Append(F(replica))
              .Append(" adoptErr=").Append(F(maxAdoptError))
              .Append(" staleness=").Append(F(maxStaleness)).Append("/bound ").Append(F(windowBound))
              .Append(" downStep=").Append(F(worstDownStep)).Append('\n');
            sb.Append("  max() ratchet: owner=").Append(F(ratchetOwner))
              .Append(" replica=").Append(F(ratchetReplica))
              .Append(" gap=").Append(F(ratchetReplica - ratchetOwner)).Append('\n');
            sb.Append("  naive two-writer: gap=").Append(F(naiveReplica - naiveOwner)).Append('\n');

            int pass = 0;
            int fail = 0;
            void Check(bool ok, string what)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // The injected error must be near-symmetric, or the ratchet's drift below is just
            // bias showing through and demonstrates nothing about max().
            Check(Math.Abs(meanErr) <= meanAbsErr * 0.15f,
                "injected per-kill error is unbiased (|mean| <= 15% of meanAbs)");
            // The replica does no arithmetic of its own -- it IS the last declared figure.
            Check(maxAdoptError == 0f,
                "the replica holds the declared total EXACTLY at all times (no local term)");
            Check(worstDownStep >= 0f,
                "a replica of a monotone total never moves backwards (no ratchet needed)");
            Check(finalGap == 0f,
                "owner and replica agree exactly once the wire drains");
            Check(maxStaleness <= windowBound,
                "the transient gap is bounded by one sync window of awards -- lag, not drift");
            // The old designs must FAIL on this same stream. The ratchet's bar is one kill's
            // worth of error (its size at a given seed is luck; its SIGN is structural), the
            // naive two-writer's is any persistent nonzero gap.
            Check(ratchetReplica - ratchetOwner > meanAbsErr,
                "control: the max() ratchet drives the replica above the owner (card b0ab09ec)");
            Check(Math.Abs(naiveReplica - naiveOwner) > meanAbsErr,
                "control: two writers with no reconciliation do not agree -- the ledger's old job");

            AppendWireLegs(sb, Check);

            int total = pass + fail;
            sb.Append(fail == 0
                ? "[netscore] PASS (" + pass + "/" + total + ")"
                : "[netscore] FAIL (" + pass + "/" + total + ")");
            return sb.ToString();
        }

        // The v20 wire, against the LIVE ScoreVisualiser -- what the sim above cannot cover:
        // field offsets, the f32 surviving encode/decode bit-exact, and NetSetScore really being
        // verbatim (including DOWNWARD, the move max() refused -- a checkpoint revert on the
        // owner's side must reach the replica). Uses the unseated slot 3 and restores it (the
        // NetComboTest idiom), so it is menu-safe and leave-no-trace.
        private static void AppendWireLegs(StringBuilder sb, Action<bool, string> Check)
        {
            ScoreVisualiser sv = NetHost.Current.Score;
            if (sv == null)
            {
                Check(false, "wire legs: no live ScoreVisualiser to drive");
                return;
            }
            const int slot = 3;
            float before = sv.PointScore(slot);
            try
            {
                // EvDeath v20 carries no award payload -- the frame is header + 11 bytes.
                byte[] death = NetProtocol.EncodeDeathEvent(1, 60001, 0, new Vector2(1f, 2f));
                Check(death.Length == NetProtocol.DeathEventBytes && NetProtocol.DeathEventBytes == 15,
                    "EvDeath is award-free (frame " + death.Length + " bytes, payload 11)");

                // One HudState entry with a total that exercises the f32 exactly.
                var slots = new byte[NetProtocol.MaxSlots];
                var combos = new int[NetProtocol.MaxSlots];
                var types = new byte[NetProtocol.MaxSlots];
                var progress = new float[NetProtocol.MaxSlots];
                var levels = new int[NetProtocol.MaxSlots][];
                var options = new int[NetProtocol.MaxSlots][];
                var scores = new float[NetProtocol.MaxSlots];
                for (int i = 0; i < NetProtocol.MaxSlots; i++)
                {
                    levels[i] = new int[NetProtocol.HudLevelCount];
                    options[i] = new int[NetProtocol.HudOptionLayers];
                    types[i] = NetProtocol.HudPowerupNone;
                }
                slots[0] = slot;
                scores[0] = 123456.75f; // exactly representable; a .75 catches a quantised path
                byte[] hud = NetProtocol.EncodeHudState(slots, combos, types, progress, levels, options, scores, 1);
                var rxLevels = new int[NetProtocol.HudLevelCount];
                var rxOptions = new int[NetProtocol.HudOptionLayers];
                bool decoded = NetProtocol.TryDecodeHudState(hud, 0, rxLevels, rxOptions,
                    out byte rxSlot, out _, out _, out _, out float rxScore);
                Check(decoded && rxSlot == slot && rxScore == 123456.75f,
                    "the declared total rides MsgHudState bit-exact (" + F(rxScore) + ")");

                // Verbatim adoption on the live panel, both directions.
                sv.NetSetScore(slot, rxScore);
                Check(sv.PointScore(slot) == 123456.75f,
                    "NetSetScore adopts the owner's figure verbatim (live panel)");
                sv.NetSetScore(slot, 100.5f);
                Check(sv.PointScore(slot) == 100.5f,
                    "...including DOWNWARD -- the move the old ratchet refused");
            }
            finally
            {
                sv.NetSetScore(slot, before);
            }
        }

        private static string F(float v)
        {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
