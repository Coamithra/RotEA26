using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // WHICH PEER SEES A PRESENTATION EFFECT (cards 7a8ec0d3 + a66e190a). Run `eaNetLocalFx()`
    // from the MAIN MENU, or `eval NetLocalFx` under eahl. Committed as
    // tools/headless/probes/net_local_fx.txt.
    //
    // Two cards, one question, so one suite: an effect that belongs to ONE player must not show
    // up on the other player's screen (the floating score), and an effect that scales the WORLD
    // must show up on both (the 1up slow motion).
    //
    // SECTION 1 -- the floating score is the KILLER's alone. Every net award path ends at
    // ScoreVisualiser.AddScore's positional overload, which now spawns the "+10" only for a slot
    // NetSession.OwnsSlot answers for. The leg that matters is not "no floater appeared": it is
    // "no floater appeared AND the score moved", because a gate that accidentally suppressed the
    // whole payout would pass the first half. The owned-slot claim beside it is the positive
    // control, without which a rig that never reaches AddScore at all reads as a pass.
    //
    // SECTION 2 -- the 1up slow motion crosses the wire. The tx leg asserts that a slow motion
    // starting HERE puts exactly one EvSlowmo on the peer's wire carrying the real duration; the
    // rx leg asserts that a peer's EvSlowmo scales OUR world AND sends nothing back. That
    // no-echo assertion is the load-bearing one: two peers that each re-announced what they
    // received would slow-motion each other permanently, and nothing else in the repo would say
    // so -- Oracle.SetSlowmotion EXTENDS a running window, so the bug is a world that never
    // speeds up again rather than a crash.
    //
    // THE SLOW MOTION IS A TIME SCALE AND THAT IS DELIBERATE (the analysis is in
    // Compat/Net/CLAUDE.md). Juice.AddHitStop is refused inside a session because a freeze is
    // scale ZERO on ONE peer; this is 0.4 on BOTH, and the whole net layer runs on real time, so
    // the wire carries the slowed truth and no puppet is corrected backward.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE, the eaNetScenarios shape rather than eaNetResetSpawn's:
    // HandleClaim reads no scene, the rx path gates on the INetScene SEAM (a stand-in satisfies
    // it), and the roster, score panels and slow-motion state are restored AND asserted restored.
    // The one thing it cannot take back is the single floating text its positive control spawns
    // -- it is a local list entry drawn at Nowhere, off screen, and it retires itself over the
    // next second of ordinary Updates.
    internal static class NetLocalFxTest
    {
        private const string Room = "netlocalfx";

        // The seats, and THE PEER HOLDS THE LOWER ONE ON PURPOSE. Both are seated explicitly --
        // an UNSEATED slot also answers false to OwnsSlot, so seating only ours would let the
        // negative leg pass for the wrong reason. Putting the peer at slot 0 is what makes leg
        // 1c discriminate: AwardScoreToAll offers its single positional floater to the FIRST
        // seated slot, so the pre-card rule would hand it to a slot whose popup is now
        // suppressed and the boss kill would show this screen no figure at all.
        private const byte OurSlot = 1;
        private const byte TheirSlot = 0;

        private const ulong PeerToken = 0x10CA1F0CUL;

        // Off-screen, so neither the enemies this suite plants nor the one floater its control
        // spawns is ever drawn.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // What the game itself asks for (PlayerShip.PowerUp's OneUp case) and what a scripted
        // peer sends. Distinct values so a leg cannot pass by reading the wrong one.
        private const float LocalSlowmoSeconds = 12f;
        private const ushort PeerSlowmoMs = 3000;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netlocalfx] floating scores + the 1up slow motion (cards 7a8ec0d3 / a66e190a)\n");

            // The eaNetScenarios gate: this starts a REAL session, seats the roster and plants
            // real entities in the LIVE bin, so a session, level or attract demo is a reason to
            // SKIP rather than let an unrun suite read as a pass.
            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
            Game game = bin.Game;

            float[] scoreBefore = new float[ScoreVisualiser.SlotCount];
            for (int i = 0; i < scoreBefore.Length; i++)
            {
                scoreBefore[i] = score.PointScore(i);
            }
            int livesBefore = score.Lives;
            int playersBefore = oracle.Players;
            List<GameComponent> planted = new List<GameComponent>();

            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunLegs(sb, Check, oracle, bin, score, game, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + Describe(ex) + ")", ok: false);
            }
            finally
            {
                sb.Append(" 9. teardown -- what this suite must hand back\n");
                Teardown(sb, Check, oracle, bin, score, game, planted, scoreBefore,
                    livesBefore, playersBefore);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, ScoreVisualiser score, Game game, List<GameComponent> planted)
        {
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            // Everything the peer RECEIVED that is an EvSlowmo. Section 2's tx leg reads it, and
            // its rx leg reads it again as the no-echo assertion -- the wire is the only place
            // either is observable, since an applied slow motion looks the same however it began.
            List<byte[]> slowmoFrames = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= 2 && payload[0] == NetProtocol.MsgEvent
                    && payload[1] == NetProtocol.EvSlowmo)
                {
                    slowmoFrames.Add(payload);
                }
            }

            sb.Append(" 0. rig -- a real HOST session, a scripted peer, and two seats\n");

            // A scene stand-in: the rx path gates on "is a scene up", and nothing in this suite
            // is about what a scene DOES (the NetScenarioTest scenario-5 argument).
            NetScene.Current = new LocalFxScene();
            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            peer.OnData += Sniff;
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("the scripted peer paired with a real host session",
                NetSession.IsHost && NetSession.PeerUp);
            if (!NetSession.PeerUp)
            {
                peer.OnData -= Sniff;
                return; // OnLocalSlowmotion early-returns with no peer; every leg would be vacuous
            }

            // The pairing has already reserved a seat for the joiner's primary, and WHERE is the
            // allocator's choice -- so both seats are taken over rather than filled in, or the
            // suite's two slots would depend on that choice.
            Seat(oracle, OurSlot, ControlDevice.Keyboard);
            Seat(oracle, TheirSlot, ControlDevice.Remote);
            Check("PRECONDITION OwnsSlot separates the two seats (ours="
                + NetSession.OwnsSlot(OurSlot) + " theirs=" + NetSession.OwnsSlot(TheirSlot) + ")",
                NetSession.OwnsSlot(OurSlot) && !NetSession.OwnsSlot(TheirSlot));

            sb.Append(" 1. a floating score belongs to the killer's own screen (card 7a8ec0d3)\n");

            // 1a. THE NEGATIVE, and it is a PAIR: the peer's kill must credit the peer's slot and
            // still put no popup on our screen. Asserting the absence alone would pass on a gate
            // that suppressed the whole payout, and on a rig whose claim never arrived.
            UFO theirs = Plant(bin, game, planted);
            Check("PRECONDITION the planted UFO got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)theirs, out NetIdRegistry.Entry theirEntry));
            bin.TopOfTickFlush();
            int floatersBefore = score.FloatingTextCount;
            float theirScoreBefore = score.PointScore(TheirSlot);
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, theirEntry.Id, TheirSlot));
            wire.Pump();
            NetSession.Update();
            float theirGain = score.PointScore(TheirSlot) - theirScoreBefore;
            Check("the peer's claim really paid its slot (+" + Fmt(theirGain)
                + ") -- without this the leg below is vacuous", theirGain > 0f);
            Check("...and spawned NO floating score on our screen (floaters "
                + floatersBefore + " -> " + score.FloatingTextCount + ")",
                score.FloatingTextCount == floatersBefore);

            // 1b. THE POSITIVE CONTROL, through the identical path with only the SLOT changed.
            // Local co-op leans on this: a couch partner shares this screen and must keep its
            // popups, and offline OwnsSlot is true for every seated slot.
            UFO mine = Plant(bin, game, planted);
            Check("PRECONDITION the second UFO got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)mine, out NetIdRegistry.Entry myEntry));
            bin.TopOfTickFlush();
            floatersBefore = score.FloatingTextCount;
            float ourScoreBefore = score.PointScore(OurSlot);
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, myEntry.Id, OurSlot));
            wire.Pump();
            NetSession.Update();
            Check("a kill credited to a slot WE own pays it (+"
                + Fmt(score.PointScore(OurSlot) - ourScoreBefore) + ")",
                score.PointScore(OurSlot) - ourScoreBefore > 0f);
            Check("...and DOES spawn its floating score (floaters " + floatersBefore + " -> "
                + score.FloatingTextCount + ")", score.FloatingTextCount == floatersBefore + 1);

            // 1c. AwardScoreToAll -- the boss shape, which pays EVERY seated slot but shows only
            // ONE positional figure. Driven directly rather than through a claim: only a boss
            // reaches it, and what is under test is which slot the one floater is offered to, not
            // how the kill arrived. The peer holds the lower seat here (see the constants), so
            // the pre-card "first seated" rule would spend the floater on a suppressed slot and
            // this screen would show nothing for a kill it was paid for.
            UFO boss = Plant(bin, game, planted);
            bin.TopOfTickFlush();
            floatersBefore = score.FloatingTextCount;
            theirScoreBefore = score.PointScore(TheirSlot);
            ourScoreBefore = score.PointScore(OurSlot);
            boss.AwardScoreToAll(combo: false);
            Check("AwardScoreToAll still pays BOTH seated slots (ours +"
                + Fmt(score.PointScore(OurSlot) - ourScoreBefore) + ", theirs +"
                + Fmt(score.PointScore(TheirSlot) - theirScoreBefore) + ")",
                score.PointScore(OurSlot) - ourScoreBefore > 0f
                    && score.PointScore(TheirSlot) - theirScoreBefore > 0f);
            Check("...and shows exactly ONE figure, on the slot we own rather than the first"
                + " seated one (floaters " + floatersBefore + " -> " + score.FloatingTextCount + ")",
                score.FloatingTextCount == floatersBefore + 1);

            sb.Append(" 2. the 1up slow motion crosses the wire (card a66e190a)\n");

            ClearSlowmotion(oracle);
            Check("PRECONDITION the world is at full speed (slowmotion="
                + Fmt(oracle.Slowmotion) + ")", oracle.Slowmotion == 1f);

            // 2a. TX. This is the call PlayerShip.PowerUp's OneUp case makes -- the real entry
            // point, not a private one, which is what makes the announcement's PLACEMENT part of
            // the assertion rather than a detail this suite restates.
            slowmoFrames.Clear();
            oracle.SetSlowmotion(LocalSlowmoSeconds);
            wire.Pump();
            Check("a local 1up slow motion still scales OUR world (slowmotion="
                + Fmt(oracle.Slowmotion) + ")", oracle.Slowmotion == 0.4f);
            ushort sentMs = 0;
            bool sentOk = slowmoFrames.Count == 1
                && NetProtocol.TryDecodeSlowmoEvent(slowmoFrames[0], out sentMs);
            Check("...and puts exactly one EvSlowmo on the peer's wire (" + slowmoFrames.Count
                + " frames, " + sentMs + "ms)",
                sentOk && sentMs == (ushort)(LocalSlowmoSeconds * 1000f));
            Check("...for the window it really opened locally ("
                + Fmt(oracle.NetSlowmotionMsLeft) + "ms left of " + sentMs + ")",
                Math.Abs(oracle.NetSlowmotionMsLeft - LocalSlowmoSeconds * 1000f) < 1f);

            // 2b. RX, plus the no-echo assertion. Cleared first, so "the peer's frame did it" is
            // distinguishable from "2a's slow motion is still running".
            ClearSlowmotion(oracle);
            Check("PRECONDITION the world is back at full speed before the peer's beat",
                oracle.Slowmotion == 1f);
            slowmoFrames.Clear();
            peer.SendReliable(NetProtocol.EncodeSlowmoEvent(eventSeq++, PeerSlowmoMs));
            wire.Pump();
            NetSession.Update();
            Check("the peer's EvSlowmo scales our world too (slowmotion="
                + Fmt(oracle.Slowmotion) + ")", oracle.Slowmotion == 0.4f);
            // THE DURATION, and it needs its own leg: `Slowmotion` is a flat 0.4 whatever the
            // window, so a receiver that dropped the ms->seconds conversion would open a window
            // a THOUSAND times too long and every other assertion here would still be green.
            // PeerSlowmoMs is deliberately not the value 2a sent, so this cannot read that one.
            Check("...for the duration the peer asked for (" + Fmt(oracle.NetSlowmotionMsLeft)
                + "ms left of " + PeerSlowmoMs + ")",
                Math.Abs(oracle.NetSlowmotionMsLeft - PeerSlowmoMs) < 1f);
            // THE LOAD-BEARING ONE. An rx path calling SetSlowmotion instead of NetSetSlowmotion
            // would pass every assertion above and leave the two peers announcing each other's
            // announcements for as long as the session lasted.
            wire.Pump();
            Check("...and sends NOTHING back (" + slowmoFrames.Count
                + " EvSlowmo frames echoed)", slowmoFrames.Count == 0);

            peer.OnData -= Sniff;
        }

        // ---- rig helpers -------------------------------------------------------------------

        // A real replicable enemy through its own factory, parked off-screen, added to the LIVE
        // bin so NetIdRegistry allocates it a real id through the real ComponentAdded seam --
        // which is what makes HandleClaim run the real per-type KilledBy (and therefore the real
        // AwardScore) rather than a stand-in. The NetScenarioTest.Plant shape.
        private static UFO Plant(ComponentBin bin, Game game, List<GameComponent> planted)
        {
            UFO ufo = UFO.NewUFO(bin, game);
            ufo.Setup(Nowhere, isBig: false, EnemyBehaviour.normal);
            bin.Add((GameComponent)(object)ufo);
            planted.Add((GameComponent)(object)ufo);
            return ufo;
        }

        private static void Seat(Oracle oracle, int slot, ControlDevice device)
        {
            if (oracle.IsSeated(slot))
            {
                oracle.RemovePlayerAt(slot, oracle.Controller(slot));
            }
            oracle.AddPlayerAt(slot, device);
        }

        // Back to full speed through the PRODUCTION clear -- Oracle.Update drops slow motion
        // whenever no player ship is alive, which at the main menu is always. Using it rather
        // than a test-only setter is what keeps this suite from asserting against a back door.
        private static void ClearSlowmotion(Oracle oracle)
        {
            ((Microsoft.Xna.Framework.GameComponent)(object)oracle).Update(new GameTime());
        }

        // Hand the menu back what it lent us. The claims above pay REAL points into the live
        // panels and seat REAL roster slots; a run that left either raised would inflate the next
        // play's HUD and make a second back-to-back run read different numbers from the first.
        private static void Teardown(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, ScoreVisualiser score, Game game, List<GameComponent> planted,
            float[] scoreBefore, int livesBefore, int playersBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("netlocalfx teardown");
                }
                ClearSlowmotion(oracle);
                Check("the world is handed back at full speed (slowmotion="
                    + Fmt(oracle.Slowmotion) + ")", oracle.Slowmotion == 1f);

                // The kills spawn real explosions and can drop a real bonus powerup, so the sweep
                // is by TYPE rather than by the planted list alone -- a UFO's KilledBy is what
                // makes this suite non-vacuous, and its debris is the price of that.
                foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
                {
                    if ((item is UFO || item is Powerup || item is Explosion)
                        && !planted.Contains(item))
                    {
                        planted.Add(item);
                    }
                }
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                bin.TopOfTickFlush();
                int left = 0;
                foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
                {
                    if (planted.Contains(item))
                    {
                        left++;
                    }
                }
                Check("every entity this suite built or spawned is out of the world ("
                    + planted.Count + " tracked, " + left + " left)", left == 0);
                planted.Clear();

                for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
                {
                    if (oracle.IsSeated(slot))
                    {
                        oracle.RemovePlayerAt(slot, oracle.Controller(slot));
                    }
                }
                Check("the roster is empty again (players " + playersBefore + " -> "
                    + oracle.Players + ")", oracle.Players == playersBefore);

                for (int slot = 0; slot < ScoreVisualiser.SlotCount; slot++)
                {
                    score.NetSetScore(slot, scoreBefore[slot], 0f);
                }
                score.Lives = livesBefore;
                bool restored = true;
                for (int slot = 0; slot < ScoreVisualiser.SlotCount; slot++)
                {
                    restored &= Math.Abs(score.PointScore(slot) - scoreBefore[slot]) < 0.01f;
                }
                Check("the score panels and lives are back where the suite found them",
                    restored && score.Lives == livesBefore);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + Describe(ex) + ")", false);
            }
        }

        private static string Fmt(float f)
        {
            return f.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Describe(Exception ex)
        {
            string s = ((object)ex).GetType().Name + ": " + ex.Message;
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                s += " <- " + ((object)inner).GetType().Name + ": " + inner.Message;
            }
            return s;
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlocalfx] {0} passed, {1} failed\n", pass, fail);
        }

        // The minimum INetScene, so the rx path's "is a scene up" gate opens. Nothing here is
        // about what a scene does -- the slow motion is applied to the ORACLE, and the claim path
        // reads no scene at all.
        private sealed class LocalFxScene : INetScene
        {
            public Levels Level => Levels.Level1;

            public bool NetEndingNormally => false;

            public bool JoinWouldSpawnNow => false;

            public bool NetScriptHoldsShipSpawn => false;

            public void NetApplyIntroVolley(int seed) { }

            public void NetApplyReset(byte mode) { }

            public void NetApplyVictory() { }

            public void NetApplyCheckpoint() { }

            public void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v) { }

            public void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate) { }

            public void NetApplyTetherBreak() { }

            public void NetApplyPeerLeft() { }

            public void NetSetRemotePaused(bool on) { }

            public void NetSetPeerStalled(bool on) { }

            public void NetReplayCatchUp() { }

            public bool NetShowKickMenu() => false;

            public void SpawnPlayer(ControlDevice controlDevice, int slot) { }
        }
    }
}
