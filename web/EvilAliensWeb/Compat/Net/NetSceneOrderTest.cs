using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // SCENARIO 6 of the step-4 harness (card 25ad0659): reset / pause / checkpoint ORDERING.
    // Run `eaNetSceneOrder()` inside a level, or `eval NetSceneOrder` under eahl. Committed as
    // tools/headless/probes/net_scene_order.txt.
    //
    // WHY IT IS NOT IN NetScenarioTest. Scenarios 1-5 are menu-runnable; this one is about what
    // a real GameScene DOES with the transitions, so a stand-in scene would make every assertion
    // vacuous -- the whole subject is the state machine on the other side of the seam. That makes
    // it DESTRUCTIVE, exactly like eaNetResetSpawn: it applies a real EvReset to the live scene
    // and leaves it in its reset branch. Run it in a throwaway ?level=Level2&invuln boot.
    //
    // WHAT IT ASSERTS, and each is a property no frame can show:
    //   1. ORDER. The reliable lane is ordered, so a batch of beats must reach the scene in the
    //      order it was sent -- checkpoint, pause on, reset, pause off. A recorder DECORATES the
    //      live scene (it does not replace it), so the real handlers still run underneath.
    //   2. RemotePaused resolves only when BOTH sides are clear, and a pause the peer never
    //      releases must not be cleared by anything else -- it is the freeze the kick offer is
    //      the escape hatch for.
    //   3. A RESET ARRIVING MID-PAUSE MUST NOT STRAND THE WORLD FROZEN. That is the failure the
    //      doc names, and it is invisible from outside the bin -- hence ComponentBin.FreezeDepth.
    //
    // The clock is pinned for the whole run (PinnedNetHost), so none of the session's real-time
    // deadlines -- the 1.2 s stall banner, the 3 s peer timeout, the 4 s kick offer -- can fire
    // mid-scenario and change the state under an assertion.
    internal static class NetSceneOrderTest
    {
        private const string Room = "sceneorder";
        private const byte GrantedSlot = 1;
        private const ulong PeerToken = 0x0DDE7A11UL;

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

            sb.Append("[netorder] scenario 6 -- reset / pause / checkpoint ordering (card 25ad0659)\n");

            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            // A DECORATOR over the live scene, never a replacement: leg 3's reset must perform the
            // real purge-and-replay, and legs 1-2 must move the real pause machinery. A blank fake
            // would turn every assertion below into a statement about the fake.
            OrderRecorder scene = new OrderRecorder(NetScene.Current);
            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            NetScene.Current = scene;

            int freezeAtStart = bin.FreezeDepth;
            try
            {
                NetWire wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                InMemoryTransport peer = wire[1];
                ushort seq = 1;

                sb.Append(" 0. rig -- a real CLIENT session with a scripted host on the wire\n");
                NetSession.StartForTest(game, host: false, ours, Room);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, GrantedSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                bool paired = NetSession.IsClient && NetSession.PeerUp;
                Check("the scripted host paired (peer=" + (NetSession.PeerUp ? "up" : "down") + ")",
                    paired);
                Check("PRECONDITION the world is not already frozen (depth=" + freezeAtStart + ")",
                    freezeAtStart == 0);

                if (paired)
                {
                    // ---- 1. ORDER ------------------------------------------------------------
                    // One batch, four beats. The reliable lane is ORDERED, so what the scene sees
                    // is the contract -- and the sequence is chosen so a handler that fired on the
                    // wrong edge would reorder it rather than merely miscount: a checkpoint that
                    // ran inside the reset, or a pause-off applied before the reset, both show.
                    sb.Append(" 1. the beats reach the scene in the order they were sent\n");
                    scene.Log.Clear();
                    peer.SendReliable(NetProtocol.EncodeEmptyEvent(seq++, NetProtocol.EvCheckpoint));
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 1));
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvReset,
                        NetSession.ResetModeRespawn));
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 0));
                    wire.Pump();
                    NetSession.Update();

                    string got = string.Join(",", scene.Log);
                    const string Want = "checkpoint,pause:on,reset:0,pause:off";
                    Check("the scene saw exactly [" + Want + "] (got [" + got + "])", got == Want);
                    // The positive control for the whole suite: an empty log would satisfy no
                    // ordering claim at all, and is what a run whose frames never arrived looks
                    // like. Stated separately so a FAIL names the cause.
                    Check("... which is four arrivals, not silence", scene.Log.Count == 4);

                    // ---- 2. RemotePaused resolves only when BOTH are clear --------------------
                    sb.Append(" 2. RemotePaused is the PEER's half of the freeze\n");
                    Check("PRECONDITION the batch above left the peer's pause released",
                        !NetSession.RemotePaused);
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 1));
                    wire.Pump();
                    NetSession.Update();
                    Check("the peer's pause raises RemotePaused", NetSession.RemotePaused);
                    // A SECOND on-edge must not double-latch it into something a single off-edge
                    // cannot clear -- that would be a world frozen for the rest of the session
                    // with the peer believing it had resumed.
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 1));
                    wire.Pump();
                    NetSession.Update();
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 0));
                    wire.Pump();
                    NetSession.Update();
                    Check("one off-edge clears it even after a repeated on-edge",
                        !NetSession.RemotePaused);

                    // ---- 3. A RESET MID-PAUSE MUST NOT STRAND THE WORLD FROZEN ---------------
                    // The doc's own wording. The peer pauses, a reset lands while the freeze is
                    // held, then the peer resumes -- and the bin must be back to zero layers.
                    // FreezeDepth is the assertion because nothing else can distinguish "resumed"
                    // from "still frozen but nothing is trying to move".
                    sb.Append(" 3. a reset arriving mid-pause does not strand the world frozen\n");
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 1));
                    wire.Pump();
                    NetSession.Update();
                    int frozenDepth = bin.FreezeDepth;
                    Check("the peer's pause really froze the world (depth=" + frozenDepth + ")",
                        frozenDepth > freezeAtStart);
                    scene.Log.Clear();
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvReset,
                        NetSession.ResetModeRespawn));
                    wire.Pump();
                    NetSession.Update();
                    Check("the reset was applied while the freeze was held (scene saw ["
                        + string.Join(",", scene.Log) + "])", scene.Log.Contains("reset:0"));
                    Check("... and did NOT unfreeze the world by itself -- the peer is still paused"
                        + " (depth=" + bin.FreezeDepth + ")", bin.FreezeDepth == frozenDepth);
                    peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvPause, 0));
                    wire.Pump();
                    NetSession.Update();
                    Check("the peer's resume unfroze it, back to where the suite found it"
                        + " (depth=" + bin.FreezeDepth + ")", bin.FreezeDepth == freezeAtStart);
                    Check("... and RemotePaused is clear", !NetSession.RemotePaused);
                }
            }
            catch (Exception ex)
            {
                Check("the scenario ran (" + Describe(ex) + ")", false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" 4. teardown\n");
                try
                {
                    NetSession.Stop("scene-order scenario finished");
                    Check("the session is stopped", !NetSession.Active);
                    // Whatever went wrong above, the world must not be left frozen -- this suite
                    // is run inside somebody's level.
                    while (bin.FreezeDepth > freezeAtStart)
                    {
                        bin.Pop();
                    }
                    Check("the world is not left frozen (depth=" + bin.FreezeDepth + ")",
                        bin.FreezeDepth == freezeAtStart);
                    // The scripted host granted us slot 1, so AdoptGrantedPrimarySlot MOVED the
                    // local player off slot 0 against a live scene. Put the seat back, as the
                    // sibling destructive suite does: the scene is left in its reset branch
                    // either way, but SpawnAllPlayers respawns by SEAT, so a player left in the
                    // wrong slot outlives the reset this suite is allowed to cause.
                    Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
                    if (oracle.IsSeated(GrantedSlot) && !oracle.IsSeated(0))
                    {
                        oracle.MovePlayerSlot(GrantedSlot, 0);
                    }
                    Check("the local player is back in slot 0 (players=" + oracle.Players + ")",
                        oracle.IsSeated(0) && !oracle.IsSeated(GrantedSlot));
                }
                catch (Exception ex)
                {
                    Check("teardown ran (" + Describe(ex) + ")", false);
                }
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the clock and the scene seam are handed back",
                    ReferenceEquals(NetHost.Current, hostBefore) && !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // Records the ORDER, forwards the work. `inner` is a snapshot taken at install, which is
        // safe for the same two reasons NetResetSpawnTest's is: the suite refuses to run without
        // a GameScene, and the only reset it drives is ResetModeRespawn, which does not terminate
        // one. Do not copy it into a scenario that tears a scene down.
        private sealed class OrderRecorder : INetScene
        {
            private readonly INetScene inner;

            internal readonly List<string> Log = new List<string>();

            internal OrderRecorder(INetScene forwardTo)
            {
                inner = forwardTo;
            }

            public Levels Level => inner.Level;

            public bool NetEndingNormally => inner.NetEndingNormally;

            public bool JoinWouldSpawnNow => inner.JoinWouldSpawnNow;
            public bool NetScriptHoldsShipSpawn => inner.NetScriptHoldsShipSpawn;
            public void NetApplyIntroVolley(int seed) => inner.NetApplyIntroVolley(seed);

            public void NetApplyReset(byte mode)
            {
                Log.Add("reset:" + mode.ToString(CultureInfo.InvariantCulture));
                inner.NetApplyReset(mode);
            }

            public void NetApplyVictory()
            {
                Log.Add("victory");
                inner.NetApplyVictory();
            }

            public void NetApplyCheckpoint()
            {
                Log.Add("checkpoint");
                inner.NetApplyCheckpoint();
            }

            public void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v)
            {
                Log.Add("bg:" + op);
                inner.NetApplyBackgroundOp(op, v);
            }

            public void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate)
                => inner.NetApplyCosmeticSwarm(kind, on, rate);

            public void NetApplyTetherBreak() => inner.NetApplyTetherBreak();

            public void NetApplyPeerLeft() => inner.NetApplyPeerLeft();

            public void NetSetRemotePaused(bool on)
            {
                Log.Add(on ? "pause:on" : "pause:off");
                inner.NetSetRemotePaused(on);
            }

            public void NetSetPeerStalled(bool on) => inner.NetSetPeerStalled(on);

            public void NetReplayCatchUp() => inner.NetReplayCatchUp();

            public bool NetShowKickMenu() => inner.NetShowKickMenu();

            public void SpawnPlayer(ControlDevice controlDevice, int slot)
                => inner.SpawnPlayer(controlDevice, slot);
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

        private static string Frames(Exception ex)
        {
            string trace = ex.StackTrace;
            if (string.IsNullOrEmpty(trace))
            {
                return "  (no stack trace)\n";
            }
            const int MaxFrames = 8;
            string[] lines = trace.Split('\n');
            StringBuilder frames = new StringBuilder();
            for (int i = 0; i < lines.Length && i < MaxFrames; i++)
            {
                frames.Append("  ").Append(lines[i].Trim()).Append('\n');
            }
            return frames.ToString();
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netorder] {0} passed, {1} failed\n", pass, fail);
        }
    }
}
