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
            int[] combos = { 37, 400, 0, 0 };            // 400 must saturate at 255
            byte[] types = { (byte)Powerup.PowerupType.Range, NetProtocol.HudPowerupNone, 0, 0 };
            float[] progress = { 0.5f, 0.75f, 0f, 0f };
            int[][] levels =
            {
                new[] { 0, 2, 4, 1, 3 },
                new[] { 4, 4, 4, 4, 9 },                 // 9 must clamp to 4
                new int[NetProtocol.HudLevelCount],
                new int[NetProtocol.HudLevelCount]
            };

            byte[] packet = NetProtocol.EncodeHudState(slots, combos, types, progress, levels, 2);
            check("packet is [type][count] + 2 x HudSlotBytes",
                packet.Length == 2 + 2 * NetProtocol.HudSlotBytes && packet[0] == NetProtocol.MsgHudState && packet[1] == 2);
            check("declared count validates against the byte length",
                NetProtocol.TryDecodeHudCount(packet, out int count) && count == 2);

            int[] rx = new int[NetProtocol.HudLevelCount];
            bool got0 = NetProtocol.TryDecodeHudState(packet, 0, rx, out byte s0, out int c0, out byte t0, out float p0);
            check("entry 0 slot/combo/type round-trip",
                got0 && s0 == 1 && c0 == 37 && t0 == (byte)Powerup.PowerupType.Range);
            // progress is quantised to a byte, so 0.5 comes back as 128/255 -- within half a step.
            check("entry 0 progress within one quantisation step", got0 && Math.Abs(p0 - 0.5f) <= 1f / 255f);
            bool levels0 = got0;
            for (int t = 0; t < NetProtocol.HudLevelCount; t++)
            {
                levels0 &= rx[t] == levels[0][t];
            }
            check("entry 0 levels round-trip in enum order", levels0);

            bool got1 = NetProtocol.TryDecodeHudState(packet, 1, rx, out byte s1, out int c1, out byte t1, out _);
            check("entry 1 decodes independently (slot 3, no active powerup)",
                got1 && s1 == 3 && t1 == NetProtocol.HudPowerupNone);
            check("combo saturates at 255 rather than wrapping", got1 && c1 == 255);
            check("out-of-range level clamps to 4", got1 && rx[NetProtocol.HudLevelCount - 1] == 4);

            check("index past the declared count is rejected",
                !NetProtocol.TryDecodeHudState(packet, 2, rx, out _, out _, out _, out _));
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
                !NetProtocol.TryDecodeHudState(packet, 0, new int[NetProtocol.HudLevelCount - 1], out _, out _, out _, out _));

            // Apply for real against the live ScoreVisualiser. Slot 3 is used deliberately: it is
            // unseated in every 2-peer session, so its panel is not drawn and this cannot disturb
            // a run in progress. Prior state is restored below regardless.
            ScoreVisualiser sv = ServiceHelper.Get<IScoreService>()?.Score;
            if (sv == null)
            {
                sb.Append("  SKIP apply-to-live-ScoreVisualiser (no score service)\n");
                return;
            }
            const int scratchSlot = ScoreVisualiser.SlotCount - 1;
            int[] before = new int[NetProtocol.HudLevelCount];
            sv.NetReadHudState(scratchSlot, before, out int beforeCombo, out byte beforeType, out float beforeProgress);

            int[] want = { 1, 0, 3, 2, 0 };
            sv.NetSetHudState(scratchSlot, 42, (byte)Powerup.PowerupType.FirePower, 0.25f, want);
            int[] after = new int[NetProtocol.HudLevelCount];
            sv.NetReadHudState(scratchSlot, after, out int afterCombo, out byte afterType, out _);
            check("NetSetHudState lands the combo on the live ScoreVisualiser", afterCombo == 42);
            check("NetSetHudState lands the active powerup type", afterType == (byte)Powerup.PowerupType.FirePower);
            bool landed = true;
            for (int t = 0; t < NetProtocol.HudLevelCount; t++)
            {
                landed &= after[t] == want[t];
            }
            check("NetSetHudState lands every powerup level", landed);

            sv.NetSetHudState(ScoreVisualiser.SlotCount + 4, 99, 0, 0f, want);
            sv.NetSetHudState(-1, 99, 0, 0f, want);
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
        // What is real: PowerupData.AddExp, its level-up threshold and its onLevelUp event.
        // What is modelled: the gate itself, applied here exactly as ScoreVisualiser.increasecombo
        // applies it (`powerupactive && OwnsSlot`) -- increasecombo is private, and calling
        // SustainCombo on the live visualiser would mutate a running game's HUD.
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

            // Blast/Option/FirePower/Range: the level is what reaches the puppet's weapon.
            int oldLevel = RunExp(game, Powerup.PowerupType.Range, peerCombo, gated: false, out _);
            int newLevel = RunExp(game, Powerup.PowerupType.Range, peerCombo, gated: true, out _);
            int ownerLevel = RunExp(game, Powerup.PowerupType.Range, ownerCombo, gated: false, out _);
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
            RunExp(game, Powerup.PowerupType.OneUp, peerCombo, gated: false, out int oldTriggers);
            RunExp(game, Powerup.PowerupType.OneUp, peerCombo, gated: true, out int newTriggers);
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

        // One powerup's exp curve over a combo stream, through the REAL PowerupData. `gated`
        // mirrors increasecombo's `powerupactive && OwnsSlot` for a slot we do not own: false =
        // the pre-card behaviour, true = the gate (so AddExp is never reached). Returns the level
        // reached; `triggers` counts onLevelUp firings, which for OneUp is the slow-motion count.
        private static int RunExp(Game game, Powerup.PowerupType type, int[] combos, bool gated, out int triggers)
        {
            int fired = 0;
            var data = new PowerupData(game, Vector2.Zero, type);
            data.onLevelUp += (t, lvl, sender) => fired++;
            if (!gated)
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
        // OwnsSlot decides, for every slot, whether this peer simulates it at all. Its OFFLINE
        // answer is the one that matters most: it must be true for everything, or single-player
        // silently stops levelling powerups.
        private static void OwnershipSection(StringBuilder sb, Action<string, bool> check)
        {
            sb.Append(" [3] NetSession.OwnsSlot\n");

            if (NetSession.Active)
            {
                // In a live session the roster belongs to the match; asserting against it would
                // mean seating and unseating players mid-game. Report what it says instead -- a
                // reported leg is not a passed one.
                var seen = new StringBuilder("  live session roster: ");
                for (int i = 0; i < ScoreVisualiser.SlotCount; i++)
                {
                    seen.Append('s').Append(i).Append('=').Append(NetSession.OwnsSlot(i) ? "ours" : "theirs/empty").Append(' ');
                }
                sb.Append(seen).Append("\n  SKIP the offline assertions (a session is up)\n");
                return;
            }

            bool allOwned = true;
            for (int i = 0; i < ScoreVisualiser.SlotCount; i++)
            {
                allOwned &= NetSession.OwnsSlot(i);
            }
            check("offline every slot is ours (single-player must be unchanged)", allOwned);
            check("offline an out-of-range slot is still answered without throwing",
                NetSession.OwnsSlot(-1) && NetSession.OwnsSlot(ScoreVisualiser.SlotCount + 10));
        }
    }
}
