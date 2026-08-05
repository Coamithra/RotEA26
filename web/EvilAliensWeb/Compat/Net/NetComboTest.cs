using System;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetCombo.test() -- the verification for card 1a3ad45a (per-slot combo + powerup state
    // between peers).
    //
    // WHY A DATA TEST AND NOT TWO WINDOWS. The bug it pins has no frame that shows it: a peer
    // levels up a powerup for a slot it does not own, minutes into a fight, off a combo it
    // invented -- and the visible consequence (twelve seconds of slow motion on one screen, an
    // extra Option ship, a puppet whose bullets bounce when its owner's do not) looks like a
    // hiccup rather than a desync. A two-window run cannot show it either: a backgrounded tab
    // throttles to ~1 tick/sec, so the two combo streams never even reach realistic rates.
    //
    // Structure follows eaNetScore.test (card b0ab09ec): section 2 runs the OLD ungated
    // behaviour over the IDENTICAL stream first and asserts it breaks. A green tick on the fix
    // means nothing unless the same input is shown to break what it replaced.
    //
    // Leave-no-trace: the only live object touched is the ScoreVisualiser in section 1, and that
    // is done on an UNSEATED slot whose panel is not drawn, with its prior state restored.
    internal static class NetComboTest
    {
        internal static string Run()
        {
            var sb = new StringBuilder("[netcombo] self-test\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                if (ok)
                {
                    pass++;
                }
                else
                {
                    fail++;
                }
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
            }

            WireSection(sb, Check);
            DivergenceSection(sb, Check);
            OwnershipSection(sb, Check);

            sb.Append(fail == 0 ? "  -> ALL PASS (" : "  -> FAILURES (")
              .Append(pass).Append(" passed, ").Append(fail).Append(" failed)");
            return sb.ToString();
        }

        // ---- 1. wire round trip ------------------------------------------------------------
        //
        // MsgHudState is fixed-width per slot, so a layout slip decodes the wrong byte as a
        // powerup LEVEL -- which would hand a puppet the wrong weapon rather than crash. That is
        // exactly the class of failure no screenshot catches, hence the byte-level assertions.
        private static void WireSection(StringBuilder sb, Action<string, bool> check)
        {
            sb.Append(" [1] wire round trip (EncodeHudState / TryDecodeHudState)\n");

            byte[] slots = { 1, 3, 0, 0 };
            // 400 is the case a byte-wide field would have silently capped at 255 and underpaid
            // (the host spends this figure -- see EncodeHudState); 90000 proves the ushort clamp.
            int[] combos = { 37, 400, 90000, 0 };
            byte[] types = { (byte)Powerup.PowerupType.Range, NetProtocol.HudPowerupNone, 0, 0 };
            float[] progress = { 0.5f, 0.75f, 0f, 0f };
            int[][] levels =
            {
                new[] { 0, 2, 4, 1, 3 },
                new[] { 4, 4, 4, 4, 9 },                 // 9 must clamp to 4
                new int[NetProtocol.HudLevelCount],
                new int[NetProtocol.HudLevelCount]
            };

            // Per-layer Option counts (v16, card c5228350). Asymmetric per layer so a swap
            // between the two cannot pass; the hostile-byte case is a RAW packet below, because
            // the encoder clamps too and a value put through it can never reach the decoder's
            // guard (measured: an encoder-side 200 tests nothing).
            int[][] optionCounts =
            {
                new[] { 3, 1 },
                new[] { 2, 0 },
                new int[NetProtocol.HudOptionLayers],
                new int[NetProtocol.HudOptionLayers]
            };

            // v20: every entry carries the owner's declared score total; entry 0 gets a distinct
            // figure so the round-trip below cannot pass on a zeroed buffer.
            float[] scoreTotals = { 1500.25f, 42f, 7f, 0f };
            byte[] packet = NetProtocol.EncodeHudState(slots, combos, types, progress, levels, optionCounts, scoreTotals, 3);
            check("packet is [type][count] + 3 x HudSlotBytes",
                packet.Length == 2 + 3 * NetProtocol.HudSlotBytes && packet[0] == NetProtocol.MsgHudState && packet[1] == 3);
            check("declared count validates against the byte length",
                NetProtocol.TryDecodeHudCount(packet, out int count) && count == 3);

            int[] rx = new int[NetProtocol.HudLevelCount];
            int[] rxOpt = new int[NetProtocol.HudOptionLayers];
            bool got0 = NetProtocol.TryDecodeHudState(packet, 0, rx, rxOpt, out byte s0, out int c0, out Powerup.PowerupType? t0, out float p0, out float sc0);
            check("entry 0 slot/combo/type round-trip",
                got0 && s0 == 1 && c0 == 37 && t0 == Powerup.PowerupType.Range);
            check("entry 0 declared score total rides the entry bit-exact (v20)",
                got0 && sc0 == 1500.25f);
            // progress is quantised to a byte, so 0.5 comes back as 128/255 -- within half a step.
            check("entry 0 progress within one quantisation step", got0 && Math.Abs(p0 - 0.5f) <= 1f / 255f);
            bool levels0 = got0;
            for (int t = 0; t < NetProtocol.HudLevelCount; t++)
            {
                levels0 &= rx[t] == levels[0][t];
            }
            check("entry 0 levels round-trip in enum order", levels0);
            check("entry 0 option counts round-trip per LAYER (3 inner / 1 outer)",
                got0 && rxOpt[0] == 3 && rxOpt[1] == 1);

            // Entry 1 gets its OWN scratch array. Sharing `rx` made every assertion below depend
            // on decode ORDER -- the out-of-range-level check 20 lines down read `rx` after an
            // intervening decode of ENTRY 0 had refilled it with entry 0's levels, and had been
            // FAILING on main since it landed. A buffer per entry removes the dependency rather
            // than documenting it.
            int[] rx1 = new int[NetProtocol.HudLevelCount];
            int[] rxOpt1 = new int[NetProtocol.HudOptionLayers];
            bool got1 = NetProtocol.TryDecodeHudState(packet, 1, rx1, rxOpt1, out byte s1, out int c1, out Powerup.PowerupType? t1, out _, out _);
            check("entry 1 decodes independently (slot 3, no active powerup)",
                got1 && s1 == 3 && !t1.HasValue);
            // Card 88f87ba2: an activeType that is neither a real type nor the sentinel folds
            // into the SAME null, so a consumer has one case to handle rather than two.
            byte[] bogusType = (byte[])packet.Clone();
            const int entry0ActiveTypeOffset = 2 + 3;   // header, then [slot][combo:2] of entry 0
            bogusType[entry0ActiveTypeOffset] = 200;
            int[] rxBogus = new int[NetProtocol.HudLevelCount];
            check("an out-of-enum activeType decodes as 'no powerup', not as a cast",
                NetProtocol.TryDecodeHudState(bogusType, 0, rxBogus, new int[NetProtocol.HudOptionLayers],
                    out _, out _, out Powerup.PowerupType? tBad, out _, out _)
                    && !tBad.HasValue);
            // A byte-wide field would have returned 255 here and underpaid the slot's boss share.
            check("a combo past 255 survives the wire intact", got1 && c1 == 400);
            check("out-of-range level clamps to 4", got1 && rx1[NetProtocol.HudLevelCount - 1] == 4);
            check("entry 1 option counts round-trip independently (2 inner / 0 outer)",
                got1 && rxOpt1[0] == 2 && rxOpt1[1] == 0);
            // The hostile byte, hand-written past the encoder: it drives real component spawns on
            // a puppet, off a stranger's wire (the public game browser).
            byte[] hugeOptions = (byte[])packet.Clone();
            const int entry0Options = 2 + 5 + NetProtocol.HudLevelCount;
            hugeOptions[entry0Options] = 200;
            int[] rxOptHuge = new int[NetProtocol.HudOptionLayers];
            check("an absurd option count off the wire clamps to HudMaxOptionsPerLayer",
                NetProtocol.TryDecodeHudState(hugeOptions, 0, new int[NetProtocol.HudLevelCount],
                    rxOptHuge, out _, out _, out _, out _, out _)
                    && rxOptHuge[0] == NetProtocol.HudMaxOptionsPerLayer);

            bool got2 = NetProtocol.TryDecodeHudState(packet, 2, rx, rxOpt, out _, out int c2, out _, out _, out _);
            check("a combo past ushort saturates rather than wrapping", got2 && c2 == ushort.MaxValue);

            check("index past the declared count is rejected",
                !NetProtocol.TryDecodeHudState(packet, 3, rx, rxOpt, out _, out _, out _, out _, out _));
            byte[] truncated = new byte[packet.Length - 1];
            Array.Copy(packet, truncated, truncated.Length);
            check("a truncated packet is rejected whole", !NetProtocol.TryDecodeHudCount(truncated, out _));
            byte[] padded = new byte[packet.Length + 1];
            Array.Copy(packet, padded, packet.Length);
            check("a padded packet is rejected whole", !NetProtocol.TryDecodeHudCount(padded, out _));
            byte[] wrongType = (byte[])packet.Clone();
            wrongType[0] = NetProtocol.MsgShipState;
            check("another message type is not decoded as HUD state", !NetProtocol.TryDecodeHudCount(wrongType, out _));
            check("a short level buffer is refused rather than over-written",
                !NetProtocol.TryDecodeHudState(packet, 0, new int[NetProtocol.HudLevelCount - 1], rxOpt,
                    out _, out _, out _, out _, out _));
            check("a short option-count buffer is refused rather than over-written",
                !NetProtocol.TryDecodeHudState(packet, 0, rx, new int[NetProtocol.HudOptionLayers - 1],
                    out _, out _, out _, out _, out _));

            // Apply for real against the live ScoreVisualiser, on the LAST slot -- unseated in
            // every 2-peer session, so its panel is not drawn.
            //
            // If it IS seated (a 4-player couch game) this leg must not run: NetSetHudState drives
            // PlayerShip.PowerUp on that slot's real ship, whose effects are MathHelper.Max
            // accumulations the restore path explicitly cannot walk back -- it would permanently
            // power up player 4 mid-run. Skipping is the honest outcome; a skipped leg is not a
            // passed one.
            ScoreVisualiser sv = ServiceHelper.Get<IScoreService>()?.Score;
            if (sv == null)
            {
                sb.Append("  SKIP apply-to-live-ScoreVisualiser (no score service)\n");
                return;
            }
            const int scratchSlot = ScoreVisualiser.SlotCount - 1;
            Oracle oracle = ServiceHelper.Get<IOracleService>()?.Oracle;
            if (oracle != null && oracle.IsSeated(scratchSlot))
            {
                sb.Append("  SKIP apply-to-live-ScoreVisualiser (slot ").Append(scratchSlot)
                  .Append(" is seated -- powering up a live player is not reversible)\n");
                return;
            }
            int[] before = new int[NetProtocol.HudLevelCount];
            sv.NetReadHudState(scratchSlot, before, out int beforeCombo, out Powerup.PowerupType? beforeType, out float beforeProgress);

            int[] want = { 1, 0, 3, 2, 0 };
            sv.NetSetHudState(scratchSlot, 42, Powerup.PowerupType.FirePower, 0.25f, want);
            int[] after = new int[NetProtocol.HudLevelCount];
            sv.NetReadHudState(scratchSlot, after, out int afterCombo, out Powerup.PowerupType? afterType, out _);
            check("NetSetHudState lands the combo on the live ScoreVisualiser", afterCombo == 42);
            check("NetSetHudState lands the active powerup type", afterType == Powerup.PowerupType.FirePower);
            bool landed = true;
            for (int t = 0; t < NetProtocol.HudLevelCount; t++)
            {
                landed &= after[t] == want[t];
            }
            check("NetSetHudState lands every powerup level", landed);

            sv.NetSetHudState(ScoreVisualiser.SlotCount + 4, 99, Powerup.PowerupType.Blast, 0f, want);
            sv.NetSetHudState(-1, 99, Powerup.PowerupType.Blast, 0f, want);
            check("an out-of-range slot is ignored, not indexed",
                sv.Combo(scratchSlot) == 42);

            // Restore. Levels only ever climb in play, so the down-step path (NetSetPowerupLevel's
            // display-only branch) is what puts the scratch slot back -- and exercising it here is
            // deliberate: it is the branch a reset the peers reached at different moments takes.
            sv.NetSetHudState(scratchSlot, beforeCombo, beforeType, beforeProgress, before);
            int[] restored = new int[NetProtocol.HudLevelCount];
            sv.NetReadHudState(scratchSlot, restored, out int restoredCombo, out _, out _);
            bool clean = restoredCombo == beforeCombo;
            for (int t = 0; t < NetProtocol.HudLevelCount; t++)
            {
                clean &= restored[t] == before[t];
            }
            check("leave-no-trace: the scratch slot is restored (down-steps included)", clean);
        }

        // ---- 2. the divergence this card fixes ---------------------------------------------
        //
        // Drives the REAL PowerupData.AddExp curve over two independent combo streams -- the
        // owner's, and a peer's derived from the same fight but with the hit sequencing shifted
        // the way a ~100ms interpolation lag shifts it. Both legs see the IDENTICAL stream.
        //
        // What is real: PowerupData.AddExp, its level-up threshold and its onLevelUp event, AND
        // the gate's own decision -- the "after" leg asks NetSession.OwnsSlotCore for a slot held
        // by a Remote ship in a live session, exactly the question ScoreVisualiser.SustainCombo
        // asks. Invert or delete that predicate and this section goes red.
        // What is modelled: the surrounding loop (SustainCombo is driven by bullet collisions, and
        // calling it on the live visualiser would mutate a running game's HUD).
        private static void DivergenceSection(StringBuilder sb, Action<string, bool> check)
        {
            sb.Append(" [2] non-owned slot progression (old ungated vs the OwnsSlot gate)\n");

            Game game = ServiceHelper.Get<IComponentBinService>()?.ComponentBin?.Game;
            if (game == null)
            {
                sb.Append("  SKIP (no component bin -- run this from the main menu or in a level)\n");
                return;
            }

            const int steps = 3000;
            int[] ownerCombo = new int[steps];
            int[] peerCombo = new int[steps];
            var rng = new Random(20260724);
            int c = 0;
            for (int i = 0; i < steps; i++)
            {
                // Climbs through a fight, lapses at a lull (the 1s combotimer).
                c = rng.Next(0, 40) == 0 ? 0 : c + 1;
                ownerCombo[i] = c;
                // The peer's bullets hit puppets interpolated behind the host's real entities, so
                // it sees the same fight a few hits out of step -- same shape, different values.
                peerCombo[i] = i >= 6 ? ownerCombo[i - 6] : 0;
            }

            // The gate's real answers, for a slot held by the other peer's ship in a live session
            // (`false` = do not simulate) and for our own seated slot (`true` = do).
            bool gateForTheirSlot = NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.Remote);
            bool gateForOurSlot = NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.Keyboard);
            check("PRECONDITION: the gate answers false for a Remote-held slot", !gateForTheirSlot);
            check("PRECONDITION: the gate answers true for our own seated slot", gateForOurSlot);

            // Blast/Option/FirePower/Range: the level is what reaches the puppet's weapon. The
            // "after" leg runs iff the REAL predicate says to, so it is not a hard-coded skip.
            int oldLevel = RunExp(game, Powerup.PowerupType.Range, peerCombo, simulate: true, out _);
            int newLevel = RunExp(game, Powerup.PowerupType.Range, peerCombo, simulate: gateForTheirSlot, out _);
            int ownerLevel = RunExp(game, Powerup.PowerupType.Range, ownerCombo, simulate: gateForOurSlot, out _);
            sb.Append("  owner slot reaches Range level ").Append(ownerLevel)
              .Append("; the non-owning peer simulated level ").Append(oldLevel)
              .Append(" before this card, ").Append(newLevel).Append(" after\n");
            // The precondition: the old code must actually have levelled the slot, or the leg
            // below proves nothing. Asserted rather than assumed -- a stream too short to reach a
            // level-up would make the whole section vacuously green.
            check("PRECONDITION: the old ungated path levels a slot the peer does not own", oldLevel > 0);
            check("the gate stops a non-owned slot levelling locally", newLevel == 0);

            // OneUp is the one that is not merely cosmetic: PlayerShip.PowerUp's OneUp case is
            // Oracle.SetSlowmotion(12f), a whole-sim time scale. Every bar fill on a non-owned
            // slot was one unilateral 12-second slow motion on this peer alone.
            RunExp(game, Powerup.PowerupType.OneUp, peerCombo, simulate: true, out int oldTriggers);
            RunExp(game, Powerup.PowerupType.OneUp, peerCombo, simulate: gateForTheirSlot, out int newTriggers);
            sb.Append("  OneUp bar fills on the non-owning peer: ").Append(oldTriggers)
              .Append(" before (each one a unilateral 12s Oracle.SetSlowmotion), ")
              .Append(newTriggers).Append(" after\n");
            check("PRECONDITION: the old path reaches the OneUp slow-motion trigger", oldTriggers > 0);
            check("the gate fires no unilateral slow motion for a non-owned slot", newTriggers == 0);

            // The counters themselves still diverge -- that is expected and is precisely why the
            // owner's value is replicated for DISPLAY instead of being re-derived.
            int diverged = 0;
            for (int i = 0; i < steps; i++)
            {
                if (ownerCombo[i] != peerCombo[i])
                {
                    diverged++;
                }
            }
            sb.Append("  local combo counters disagree on ").Append(diverged).Append('/').Append(steps)
              .Append(" ticks -- replicated for display, never re-simulated\n");
            check("PRECONDITION: the two local combo simulations genuinely disagree", diverged > steps / 2);
        }

        // One powerup's exp curve over a combo stream, through the REAL PowerupData. `simulate` is
        // what SustainCombo's gate decides -- pass the predicate's real answer, never a literal,
        // for any leg meant to prove the gate works. Returns the level reached; `triggers` counts
        // onLevelUp firings, which for OneUp is the slow-motion count.
        private static int RunExp(Game game, Powerup.PowerupType type, int[] combos, bool simulate, out int triggers)
        {
            int fired = 0;
            var data = new PowerupData(game, Vector2.Zero, type);
            data.onLevelUp += (t, lvl, sender) => fired++;
            if (simulate)
            {
                foreach (int combo in combos)
                {
                    data.AddExp(combo);
                }
            }
            triggers = fired;
            return data.GetLevel();
        }

        // ---- 3. the ownership predicate ----------------------------------------------------
        //
        // OwnsSlot decides, for every slot, whether this peer simulates it at all. Table-driven
        // over OwnsSlotCore because the interesting cases (a slot held by the other peer, an
        // unseated one) cannot be reached through the live Oracle without seating and unseating
        // players in a running match -- and offline the predicate is unconditionally true, so a
        // live-roster-only test could never cover them at all.
        private static void OwnershipSection(StringBuilder sb, Action<string, bool> check)
        {
            sb.Append(" [3] NetSession.OwnsSlot\n");

            // Offline: everything is ours, whatever the seat says. This is what keeps
            // single-player and local co-op byte-identical -- get it wrong and powerups silently
            // stop levelling in the shipped game.
            check("offline an unseated slot is still ours",
                NetSession.OwnsSlotCore(sessionActive: false, seatedDevice: null));
            check("offline even a Remote-marked seat is ours (there is no peer)",
                NetSession.OwnsSlotCore(sessionActive: false, seatedDevice: ControlDevice.Remote));

            // In a session, ownership follows the seat's device.
            check("in-session our own keyboard seat is ours",
                NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.Keyboard));
            check("in-session a couch player's pad seat is ours",
                NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.PadTwo));
            check("in-session a local AI friend is ours",
                NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.AI));
            check("in-session the peer's primary (Remote) is NOT ours",
                !NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.Remote));
            check("in-session the peer's extra ship (RemoteFriend) is NOT ours",
                !NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: ControlDevice.RemoteFriend));
            check("in-session an unseated slot is NOT ours",
                !NetSession.OwnsSlotCore(sessionActive: true, seatedDevice: null));

            // And the live wrapper must not throw on a slot index off either end -- `slot` reaches
            // it from a raw wire byte.
            bool answered = true;
            try
            {
                NetSession.OwnsSlot(-1);
                NetSession.OwnsSlot(ScoreVisualiser.SlotCount + 10);
            }
            catch (Exception)
            {
                answered = false;
            }
            check("an out-of-range slot is answered without throwing", answered);

            if (NetSession.Active)
            {
                var seen = new StringBuilder("  live session roster: ");
                for (int i = 0; i < ScoreVisualiser.SlotCount; i++)
                {
                    seen.Append('s').Append(i).Append('=').Append(NetSession.OwnsSlot(i) ? "ours" : "theirs/empty").Append(' ');
                }
                sb.Append(seen).Append('\n');
            }
        }
    }
}
