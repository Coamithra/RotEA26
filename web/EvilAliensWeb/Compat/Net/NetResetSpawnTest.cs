using System;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The reset / TryAdd ship-puppet spawn scenario (card 25ad0659 step 1b; the defect is card
    // 74403f83's, PR #160). Run `eaNetResetSpawn()` inside a level, or `eval NetResetSpawn` under
    // eahl. Committed as tools/headless/probes/net_reset_spawn.txt.
    //
    // WHAT IT COVERS. NetSession.SpawnPuppet and NetSession.Friends.SpawnFriend ADOPT the
    // PlayerShip they add and gate their retry on the reference being null -- so adopting a ship
    // the ComponentBin DIVERTED (a standing Purge<PlayerShip> is live) points that reference at a
    // ship the world does not have, silently: nothing throws and no counter moves. Card 74403f83
    // fixed both sites with ComponentBin.TryAdd, but the fix was only ever proven at the
    // PRIMITIVE (eaBinTest scenario 5: a bare TryAdd landed/diverted pair). Reaching the two real
    // call sites needs a live session with a host-granted peer slot and buffered ship samples --
    // which is what this does: ONE real CLIENT session on one endpoint of an in-process NetWire,
    // and a scripted host driving the other end by hand. Its whole production cost is four
    // internal seams (StartForTest, LocalBuildHash, HasRemotePuppet, HasFriendPuppet) plus one
    // corrected log string -- the session-start line used to print "WebRTC" for an
    // InMemoryTransport, which this is the first rig to have noticed.
    //
    // WHAT IT MEASURED, and it corrects card 74403f83's own severity claim: the faithful pre-card
    // mutation (bin.Add + unconditional adopt) fails exactly ONE of the assertions below, because
    // ManagePuppet and TickFriends BOTH open by releasing a puppet the oracle does not hold
    // (`!oracle.GetShips().Contains(...)`) and that block predates the fix (Stage 11.1, 6f36aae).
    // So the bug's window is one tick, not the session, and the "stranded for the rest of the
    // session" wording that used to sit here, in ComponentBin.TryAdd's comment, in SpawnPuppet's
    // own and in two CLAUDE.md files was overstated (all corrected). The guard stays -- the
    // release is a safety net, not the intended path -- and leg 2's single assertion is the
    // load-bearing one here. Do not add legs that expect the broken code to stay broken past one
    // tick; leg 3 passes under that mutation and always will.
    //
    // THE LEGS, and why they are in this order.
    //   0b. THE SERVICE SEAM, added by step 2b. The four ServiceHelper.Get<>() lookups the net
    //      cores made now resolve through INetHost, and a call site left on the process-global
    //      registry would change NOTHING observable today -- it only bites at step 3, as two
    //      peers quietly sharing one Oracle. So this leg counts reads THROUGH the seam
    //      (RecordingNetHost) during StartForTest and requires the exact number each core makes.
    //      Mutation-tested six ways, one per call site, each isolated: reverting any one of them
    //      fails exactly one assertion here and names the service.
    //   1. NEGATIVE. The GameScene.LoseLife / UpdateWin / UpdateResetting purges run in
    //      base.Update, and collectionHelper.Update() flushes them BEFORE the rx drain -- so by
    //      the time SpawnPuppet could run, FindLocalShip() is null and both callers' gates are
    //      shut. Asserted by the seats: neither Remote nor RemoteFriend is allocated at all,
    //      which distinguishes "the caller was never entered" from "TryAdd refused". Without this
    //      leg the whole scenario could pass on a path that never reaches the code.
    //   2. POSITIVE, filter live. Only NetApplyReset can reach the branch, because it purges from
    //      INSIDE the drain: the local ship's purge death is still merely QUEUED there, so
    //      FindLocalShip() is non-null and the gate is OPEN. Both callers take their seat and both
    //      TryAdds are refused; nothing is adopted and nothing enters the world.
    //   3. POSITIVE, filter expired. One bin.TopOfTickFlush() -- the real tick boundary -- and the
    //      identical send/pump/Update sequence adopts both puppets, in the seats leg 2 took.
    //   3b. THE CLOCK, added by step 2a. Legs 1-3 would pass on a wall clock too, so they show
    //      the pinned host does no harm rather than that the session reads it. This one moves
    //      ONLY the virtual clock -- no packets, no ticks -- and requires the session to act,
    //      straddling exactly one threshold so the assertion pins WHICH deadline fired.
    // Leg 3 is also leg 1's and leg 2's POSITIVE CONTROL: it proves the wire, the handshake, the
    // stream decode and both spawn paths all work, so their "nothing happened" assertions cannot
    // be passing because the scripted peer's frames never arrived or were never processed. Each
    // leg additionally asserts the endpoint's own RxDelivered advanced by the frames it sent.
    //
    // THE TICK ORDERING IT MODELS, from Game1.UpdateInner:
    //   TopOfTickFlush -> base.Update -> collectionHelper.Update -> DetectCollisions ->
    //   NetSession.Update -> DrainRx (NetApplyReset arms the purge here) -> ManagePuppet ->
    //   SpawnPuppet, then TickFriends -> SpawnFriend.
    // Everything from DrainRx onward is inside ONE NetSession.Update() call, so the scenario calls
    // that directly and supplies the two flush points itself. It does NOT tick the game: driving
    // the real Resetting -> Startup choreography would take ~3 s of game time plus a background
    // crossfade that needs Draw, and none of it is under test. The one thing it therefore has to
    // stand in for is SpawnAllPlayers' respawn of the local seat -- see RespawnLocalShip, which
    // lists the four ways it is NOT a faithful copy.
    //
    // *** THIS SUITE IS DESTRUCTIVE. It is the only one in this directory that is. ***
    // It really pairs a session onto the live level, really moves the local player's seat, and
    // really applies an EvReset -- so the scene ends up in its reset branch with the checkpoint
    // revert ahead of it. Run it in a throwaway ?level= boot, never in a game you care about.
    // What it DOES restore, and asserts it restored (leg 4): the session is stopped, no Remote or
    // RemoteFriend seat is left squatting the roster, and the local player is back in its original
    // slot on its original device with a live ship. The scene's own reset choreography then runs
    // from there. It refuses to run at all with no GameScene up or with a real session Active.
    //
    // FLAKINESS -- CLOSED by step 2a, and worth knowing what it used to be. This suite drives a
    // real NetSession, so it used to run on Environment.TickCount64 and two real-clock windows
    // could in principle bite: the 500 ms FriendTimeoutMs and the 8 s peer-drop verdict. It
    // out-ran them by re-sending both streams immediately before every Update, leaving an
    // exposure of the handful of microseconds between the send and the Update -- measured
    // non-flaky over 10 consecutive runs, which is what let it commit as a probe.
    // Since step 2a it installs a PinnedNetHost for the whole run, so the session's clock does
    // not advance at all unless the scenario advances it and neither window can elapse. The
    // re-sends STAY: they are also what keeps each leg's ship/friend buffers fresh, which is a
    // separate job from beating the clock. This is the step 2a debt the header of
    // plans/net-headless-sim.md predicted would fall out of the injected clock, and it did.
    internal static class NetResetSpawnTest
    {
        private const string Room = "resetspawn";

        // The seat the scripted host grants us. Must be >= 1: the client sets peerPrimarySlot =
        // HostPrimarySlot (0) on adoption, so slot 0 has to be free for the peer's own puppet --
        // which is exactly what the dev ?net=join flow does (DecideSlotAdopt -> MoveSeat).
        private const byte GrantedSlot = 1;

        // The extra ship the scripted host streams as MsgFriendState, i.e. one of ITS couch
        // players or AI friends arriving as a ControlDevice.RemoteFriend puppet here.
        private const byte FriendSlot = 2;

        // Arbitrary non-zero peer-identity token (0 means "no identity" and is never blockable).
        private const ulong PeerToken = 0x5EED5EEDUL;

        private static readonly Vector2 RemoteShipPos = new Vector2(360f, 240f);
        private static readonly Vector2 FriendShipPos = new Vector2(300f, 300f);
        private static readonly Vector2 LocalShipPos = new Vector2(400f, 500f);

        private const float FacingUp = 4.712389f; // 3*pi/2, the spawn heading every ship uses

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

            sb.Append("[netreset] EvReset -> SpawnPuppet/SpawnFriend TryAdd (cards 74403f83, 25ad0659)\n");

            // ---- gate ----------------------------------------------------------------------
            // The inverse of eaBinTest's: that suite refuses to run NEAR a live world, this one
            // needs one (NetApplyReset goes through GameScene.NetActiveScene, and FindLocalShip
            // needs a real seated ship). A real session must not be torn down under a player.
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("SKIP (needs a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append("[netreset] 0 passed, 0 failed\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("SKIP (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append("[netreset] 0 passed, 0 failed\n");
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            // Step 2c's scene seam, read through NetScene.Current rather than
            // GameScene.NetActiveScene so this suite exercises the seam the cores now use. It is
            // also what lets the two respawns below drive the REAL SpawnPlayer instead of the
            // hand-rolled stand-in step 1b had to carry -- see the header.
            // ... wrapped in a call counter, which is leg 3c's instrument. A DECORATOR over the
            // live scene, not a replacement: leg 2's EvReset must still perform the real
            // Purge<PlayerShip> from inside the drain, or every leg under it goes vacuous.
            RecordingNetScene rec = new RecordingNetScene(NetScene.Current);
            INetScene scene = rec;

            sb.Append(" 0. rig\n");
            // A single local player at slot 0 with a live ship, and slots 1/2 free. Everything
            // below is written against that shape, so a boot that does not have it (a couch
            // player, an AI friend from the Mechanical Friends cheat, a level that seats a
            // partner) must report rather than assert about the wrong roster.
            bool rosterOk = oracle.Players == 1 && oracle.IsSeated(0) && oracle.IsAlive(0)
                && !oracle.IsSeated(GrantedSlot) && !oracle.IsSeated(FriendSlot);
            Check("PRECONDITION one local player at slot 0 with a live ship, slots 1+2 free"
                + " (players=" + oracle.Players + ")", rosterOk);
            if (!rosterOk)
            {
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }
            ControlDevice localDevice = oracle.Controller(0);

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;
            // Per-STREAM seq + sender clock, mirroring production's own split (`friendTxSeq` is
            // kept apart from `txSeq` "so the primary stream's seq stays contiguous"). Sharing one
            // counter would make the ship stream arrive as 1, 3, 5 and score a phantom
            // metrics.StreamSeqGaps on every frame -- harmless while nothing here reads the
            // metrics, and exactly what would defeat a later seqGap/drop assertion in this suite.
            ushort shipSeq = 1;
            ushort friendSeq = 1;
            uint shipMs = 100;
            uint friendMs = 100;

            // The session's clock for this whole run (card 25ad0659 step 2a). Installed just
            // before the try below -- see the note there for why the ORDER matters.
            PinnedNetHost clock = new PinnedNetHost();

            // ... with a read counter over it, which is leg 0b's whole instrument. It is a
            // decorator over the decorator: the clock still pins time, and this only watches.
            RecordingNetHost services = new RecordingNetHost(clock);

            // Legs 1-3 sit in a local function purely so the `!paired` bail-out can be a plain
            // `return`: `return sb.ToString()` from inside the `try` renders the report BEFORE the
            // finally's teardown has appended its own lines, so on the one path a destructive
            // suite most needs to say whether it put the roster back, it would say nothing.
            void RunLegs()
            {
                // ---- 0b: step 2b's seam, and this is the leg that makes it load-bearing -----
                // The four ServiceHelper.Get<>() lookups the net cores used to make now resolve
                // through INetHost, and the ONLY way that can silently fail is a call site left
                // reading the process-global registry -- which changes nothing today and makes
                // two peers share one Oracle at step 3. `services` counts reads, so a missed
                // site shows up as a ZERO here rather than as a mystery years later.
                //
                // The counts are exact on purpose (>= 1 would let NetPuppets.Enable regress
                // behind StartWith's own read of the same service). Raise them if a read is
                // genuinely added; do not relax them to a floor.
                //
                // They assume the puppet layer is NOT already enabled -- NetPuppets.Enable
                // early-returns when it is, which would report bin/score reads=1 and blame a
                // missed call site for what is really leaked state. Unreachable as things stand:
                // the only other caller, NetSnapshotTest, skips itself while a GameScene is up
                // (which this suite requires) and disables in a finally. If that ever changes,
                // this leg needs a NetPuppets.Enabled precondition, not looser counts.
                sb.Append(" 0b. the session was built through the INetHost seam (step 2b)\n");
                NetSession.StartForTest(game, host: false, ours, Room);
                int gotOracle = services.OracleReads;
                int gotBin = services.BinReads;
                int gotSound = services.SoundReads;
                int gotScore = services.ScoreReads;
                peer.Open(Room);
                Check("session started as a CLIENT", NetSession.IsClient);

                // A CLIENT session, so StartWith takes the NetPuppets.Enable branch -- which is
                // why bin and score are read twice and oracle/sound once. A host session would
                // read each once (NetIdRegistry.Enable makes no service lookup).
                Check("oracle came from the host, once (reads=" + gotOracle + ") and IS the live one",
                    gotOracle == 1 && ReferenceEquals(NetHost.Current.Oracle, oracle));
                Check("bin came from the host twice -- StartWith AND NetPuppets.Enable (reads="
                    + gotBin + ") and IS the live one",
                    gotBin == 2 && ReferenceEquals(NetHost.Current.ComponentBin, bin));
                Check("sound came from the host, once (reads=" + gotSound + ")", gotSound == 1);
                Check("score came from the host twice -- StartWith AND NetPuppets.Enable (reads="
                    + gotScore + ")", gotScore == 2);

                // ---- handshake: the scripted peer is the HOST and grants us GrantedSlot ------
                sb.Append(" 0c. handshake -- the scripted host grants us slot " + GrantedSlot + "\n");
                // (protocolVersion, isHost, buildHash, flags, primarySlot, peerId, blockedSlots)
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, GrantedSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();

                bool paired = NetSession.PeerUp && NetSession.LocalPrimarySlot == GrantedSlot;
                Check("the scripted host's hello was accepted and its grant adopted"
                    + " (peer=" + (NetSession.PeerUp ? "up" : "down")
                    + " pri=" + NetSession.LocalPrimarySlot + ")", paired);
                Check("our own seat MOVED to the granted slot, freeing slot 0 for the peer",
                    !oracle.IsSeated(0) && oracle.IsSeated(GrantedSlot) && oracle.IsAlive(GrantedSlot));
                if (!paired)
                {
                    // Nothing below can mean anything without a settled pairing, and a rejected
                    // hello (build hash, role, protocol) prints its own [net] line above. The
                    // teardown in the finally still runs AND still reports.
                    return;
                }

                // ---- 1. NEGATIVE: a purge armed in base.Update is flushed BEFORE the drain ----
                sb.Append(" 1. NEGATIVE -- LoseLife / UpdateWin / UpdateResetting: purge flushed before the drain\n");
                long rx = ours.RxDelivered;
                peer.SendStream(ShipFrame(ref shipSeq, ref shipMs));
                peer.SendStream(FriendFrame(ref friendSeq, ref friendMs));
                wire.Pump();
                // What GameScene.UpdateWin / UpdateResetting / LoseLife do from base.Update, and
                // then what collectionHelper.Update() does to it -- note Update() flushes the
                // deaths but does NOT expire the filter (only TopOfTickFlush does), so this is
                // the genuine "filter still live, local ship already gone" state.
                bin.Purge<PlayerShip>();
                bin.Update();
                Check("PRECONDITION the purge is carried out before the drain (no local ship)",
                    !oracle.IsAlive(GrantedSlot));
                NetSession.Update();
                Check("the peer's 2 stream frames were delivered into the session",
                    ours.RxDelivered - rx == 2);
                Check("no remote ship puppet is adopted", !NetSession.HasRemotePuppet);
                Check("no friend ship puppet is adopted", !NetSession.HasFriendPuppet(FriendSlot));
                Check("SpawnPuppet was never entered -- no Remote seat allocated",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote));
                Check("SpawnFriend was never entered -- no RemoteFriend seat allocated",
                    !oracle.IsSeated(FriendSlot));

                // ---- 2. POSITIVE: EvReset purges from INSIDE the drain ----------------------
                sb.Append(" 2. POSITIVE -- EvReset purges inside the drain: both TryAdds refused\n");
                bin.TopOfTickFlush();
                scene.SpawnPlayer(localDevice, GrantedSlot);
                Check("PRECONDITION the local ship is back for the reset leg",
                    oracle.IsAlive(GrantedSlot));

                rx = ours.RxDelivered;
                // ResetModeRespawn rather than ResetModeReset: NetApplyReset purges BEFORE its
                // mode switch, so all three modes arm the same standing filter, and this is the
                // one that does not also spend a life.
                peer.SendReliable(NetProtocol.EncodeByteEvent(eventSeq++, NetProtocol.EvReset,
                    NetSession.ResetModeRespawn));
                peer.SendStream(ShipFrame(ref shipSeq, ref shipMs));
                peer.SendStream(FriendFrame(ref friendSeq, ref friendMs));
                wire.Pump();
                NetSession.Update();

                Check("the peer's 3 frames were delivered into the session",
                    ours.RxDelivered - rx == 3);
                // This is what keeps the caller's gate OPEN and is why only this purge site can
                // reach the branch at all. Asserted BEFORE the flush below carries it out.
                Check("EvReset left the local ship's purge death merely QUEUED",
                    oracle.IsAlive(GrantedSlot));
                Check("SpawnPuppet DID take the peer's primary seat",
                    oracle.DeviceIsPlaying(ControlDevice.Remote)
                    && oracle.GetPlayerIndex(ControlDevice.Remote) == NetSession.HostPrimarySlot);
                Check("... but TryAdd was refused, so no remote puppet was adopted",
                    !NetSession.HasRemotePuppet);
                Check("... and no ship entered the world in that seat",
                    oracle.GetPlayerShip(NetSession.HostPrimarySlot) == null);
                Check("SpawnFriend DID take the friend seat",
                    oracle.IsSeated(FriendSlot) && oracle.Controller(FriendSlot) == ControlDevice.RemoteFriend);
                Check("... but TryAdd was refused, so no friend puppet was adopted",
                    !NetSession.HasFriendPuppet(FriendSlot));
                Check("... and no ship entered the world in the friend seat",
                    oracle.GetPlayerShip(FriendSlot) == null);

                int seatedBefore = oracle.Players;
                int remoteSeatBefore = oracle.GetPlayerIndex(ControlDevice.Remote);

                // ---- 3. POSITIVE: the next tick, once the filter has expired ----------------
                sb.Append(" 3. POSITIVE -- next tick, filter expired: both adopted, both seats REUSED\n");
                bin.TopOfTickFlush();
                Check("the flush carried the purge out", !oracle.IsAlive(GrantedSlot));
                scene.SpawnPlayer(localDevice, GrantedSlot);
                Check("PRECONDITION the local ship is back for the retry leg",
                    oracle.IsAlive(GrantedSlot));

                rx = ours.RxDelivered;
                peer.SendStream(ShipFrame(ref shipSeq, ref shipMs));
                peer.SendStream(FriendFrame(ref friendSeq, ref friendMs));
                wire.Pump();
                NetSession.Update();

                Check("the peer's 2 stream frames were delivered into the session",
                    ours.RxDelivered - rx == 2);
                Check("the remote ship puppet IS adopted once the filter has expired",
                    NetSession.HasRemotePuppet);
                PlayerShip remotePuppet = oracle.GetPlayerShip(NetSession.HostPrimarySlot);
                Check("it landed in the seat leg 2 took, as a Remote ship",
                    remotePuppet != null && remotePuppet.Controller == ControlDevice.Remote);
                Check("the friend puppet IS adopted once the filter has expired",
                    NetSession.HasFriendPuppet(FriendSlot));
                Check("it landed in the friend seat leg 2 took",
                    oracle.GetPlayerShip(FriendSlot) != null);
                // The seat was REUSED via DeviceIsPlaying / the IsSeated fallback, not re-taken:
                // the Remote registration is the same slot it was before the refusal and the
                // roster did not grow a second seat for either puppet.
                Check("the seats were REUSED, not re-allocated"
                    + " (players " + seatedBefore + " -> " + oracle.Players
                    + ", remote slot " + remoteSeatBefore + " -> " + oracle.GetPlayerIndex(ControlDevice.Remote) + ")",
                    oracle.Players == seatedBefore
                    && oracle.GetPlayerIndex(ControlDevice.Remote) == remoteSeatBefore
                    && oracle.IsSeated(FriendSlot)
                    && oracle.Controller(FriendSlot) == ControlDevice.RemoteFriend);
                Check("one ship per seat -- no duplicate was added"
                    + " (ships=" + oracle.GetShips().Count + ")", oracle.GetShips().Count == 3);

                // ---- 3b. the session's OWN cadence runs on the injected clock ----------------
                // Lettered rather than numbered because it rides on leg 3's adopted puppets and
                // teardown is already leg 4 -- and legs print in the order they run.
                // Legs 1-3 would pass on a wall clock too, so on their own they show the pinned
                // host does no HARM, not that NetSession reads it. This leg discriminates: it
                // moves ONLY the virtual clock -- no packets, no ticks -- and requires the
                // session to act on it, which a wall-clock read cannot do. The interval is
                // chosen to straddle exactly one threshold (FriendTimeoutMs 500 < 600 <
                // PeerStallMs 1200 < PeerTimeoutMs 3000), so the assertion pins WHICH deadline
                // fired rather than merely that something did.
                sb.Append(" 3b. the session's cadence runs on the INJECTED clock\n");
                Check("PRECONDITION both puppets are up before the clock moves",
                    NetSession.HasRemotePuppet && NetSession.HasFriendPuppet(FriendSlot));
                clock.Advance(600);
                NetSession.Update();
                Check("advancing the virtual clock past FriendTimeoutMs explodes the friend puppet",
                    !NetSession.HasFriendPuppet(FriendSlot));
                Check("... and only that one -- the primary remote is on the 3 s peer timeout",
                    NetSession.HasRemotePuppet);

                // ---- 3c: step 2c's scene seam -----------------------------------------------
                // Exactly one EvReset was sent (leg 2's), and the ONLY way it can reach a scene
                // is through NetScene.Current. A handler left on GameScene.NetActiveScene would
                // do the identical work and leave this at zero -- which is the whole point: that
                // divergence is invisible until step 4 supplies a scene of its own.
                sb.Append(" 3c. the world reaches the scene through the INetScene seam\n");
                Check("leg 2's EvReset arrived through NetScene.Current (resets=" + rec.ResetCalls
                    + ")", rec.ResetCalls == 1);
                // The two respawns are the rig's own, and they are here rather than in a comment
                // because they are what replaced step 1b's hand-rolled stand-in: the real
                // GameScene.SpawnPlayer, reached through the seam.
                Check("both retry legs respawned via the REAL SpawnPlayer (spawns="
                    + rec.SpawnPlayerCalls + ")", rec.SpawnPlayerCalls == 2);
            }

            // THE CLOCK IS PINNED FOR THE WHOLE RUN (card 25ad0659 step 2a). Installed BEFORE
            // StartForTest, because StartWith stamps sessionStartAt from it, and handed back only
            // after Teardown, because Stop() reads it too. Nothing advances it, so neither of the
            // two real-clock windows this suite used to have to out-run -- FriendTimeoutMs (500
            // ms) and the 8 s peer-drop verdict -- can elapse mid-run at all. See the FLAKINESS
            // note in the header for what that replaced.
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = services;
            NetScene.Current = rec;
            try
            {
                RunLegs();
            }
            catch (Exception ex)
            {
                // Name, message AND the top frames: for an unexpected failure this is the whole
                // diagnostic, and name+message alone was NOT enough -- a WASM-only
                // `IOException: I/O error` here said nothing about which of the dozen real engine
                // calls in the scenario raised it (the BinTest precedent, one step further).
                Check("the scenario ran (" + Describe(ex) + ")", ok: false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" 4. teardown -- the roster this suite must hand back\n");
                Teardown(oracle, bin, game, localDevice, Check);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the virtual clock is handed back (NetHost.Current restored)",
                    ReferenceEquals(NetHost.Current, hostBefore));
                // Null hands the seam back to the live scene, so this asserts the scenario left
                // nothing behind -- a stray override would silently outlive the run, and every
                // world message after it would be applied into a decorator over a dead scene.
                Check("the scene seam is handed back (no override left standing)",
                    !NetScene.IsOverridden
                        && ReferenceEquals(NetScene.Current, GameScene.NetActiveScene));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // Leg 3c's instrument (card 25ad0659 step 2c). The scene twin of RecordingNetHost below,
        // and it exists for the identical reason: a core call site left on
        // GameScene.NetActiveScene instead of NetScene.Current behaves IDENTICALLY today -- the
        // seam reads through that very field -- and only diverges once a scenario supplies a
        // scene of its own, which is step 4. So the assertion has to be "the call arrived HERE",
        // not "the call had the right effect".
        //
        // It wraps the live scene rather than replacing it: leg 2's EvReset must still perform
        // the REAL Purge<PlayerShip> from inside the drain, which is the whole subject of this
        // suite. A blank fake would make every leg below it vacuous.
        private sealed class RecordingNetScene : INetScene
        {
            private readonly INetScene inner;

            internal int ResetCalls;
            internal int SpawnPlayerCalls;

            internal RecordingNetScene(INetScene forwardTo)
            {
                inner = forwardTo;
            }

            public Levels Level => inner.Level;

            public bool NetEndingNormally => inner.NetEndingNormally;

            public bool JoinWouldSpawnNow => inner.JoinWouldSpawnNow;

            public void NetApplyReset(byte mode)
            {
                ResetCalls++;
                inner.NetApplyReset(mode);
            }

            public void NetApplyVictory() => inner.NetApplyVictory();

            public void NetApplyCheckpoint() => inner.NetApplyCheckpoint();

            public void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v)
                => inner.NetApplyBackgroundOp(op, v);

            public void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate)
                => inner.NetApplyCosmeticSwarm(kind, on, rate);

            public void NetApplyTetherBreak() => inner.NetApplyTetherBreak();

            public void NetApplyPeerLeft() => inner.NetApplyPeerLeft();

            public void NetSetRemotePaused(bool on) => inner.NetSetRemotePaused(on);

            public void NetSetPeerStalled(bool on) => inner.NetSetPeerStalled(on);

            public void NetReplayCatchUp() => inner.NetReplayCatchUp();

            public bool NetShowKickMenu() => inner.NetShowKickMenu();

            // Counted separately from ResetCalls because BOTH this suite and (in production) the
            // scene's own reset choreography call it -- conflating them would make leg 3c's
            // number depend on how many times the rig respawned.
            public void SpawnPlayer(ControlDevice controlDevice, int slot)
            {
                SpawnPlayerCalls++;
                inner.SpawnPlayer(controlDevice, slot);
            }
        }

        // Leg 0b's instrument (card 25ad0659 step 2b). A pass-through INetHost that counts how
        // often each of the four services is read THROUGH the seam. Everything else forwards
        // untouched -- it wraps the PinnedNetHost rather than replacing it, so the clock stays
        // pinned and the flags/fingerprints stay production.
        //
        // Deliberately NOT a stub that returns its own services: the assertion is "the cores
        // stopped reading ServiceHelper", and handing the session fabricated services would test
        // the counter and nothing else -- besides needing a second Game, which no ctor here can
        // do without.
        //
        // The COUNT is the whole discriminator; the two `ReferenceEquals` beside it are close to
        // free rather than load-bearing. All four getters return DISTINCT types with exactly one
        // source apiece in ServiceHelper, so a mis-wired mapping does not compile -- which is why
        // the sound and score legs do without one and lose nothing. (2a's flag surface was the
        // opposite case: eleven same-typed members, so a swap among them was the live hazard and
        // NetHostTest drives its impairment triple to three distinct values for exactly that.)
        private sealed class RecordingNetHost : INetHost
        {
            private readonly INetHost inner;

            internal int OracleReads;
            internal int BinReads;
            internal int ScoreReads;
            internal int SoundReads;

            internal RecordingNetHost(INetHost forwardTo)
            {
                inner = forwardTo ?? NetHost.Production;
            }

            public long NowMs => inner.NowMs;

            public string BuildHash => inner.BuildHash;

            public string PeerToken => inner.PeerToken;

            public bool DebugActive => inner.DebugActive;

            public bool NetJip => inner.NetJip;

            public bool NetLog => inner.NetLog;

            public bool NetDropGrant => inner.NetDropGrant;

            public int NetLocal => inner.NetLocal;

            public float NetLagMs => inner.NetLagMs;

            public float NetLossPct => inner.NetLossPct;

            public float NetJitterMs => inner.NetJitterMs;

            public Oracle Oracle
            {
                get { OracleReads++; return inner.Oracle; }
            }

            public ComponentBin ComponentBin
            {
                get { BinReads++; return inner.ComponentBin; }
            }

            public ScoreVisualiser Score
            {
                get { ScoreReads++; return inner.Score; }
            }

            public SoundManager SoundManager
            {
                get { SoundReads++; return inner.SoundManager; }
            }
        }

        // Type + message, with the inner chain flattened in (the FlattenedContentLoadException
        // idiom): a WASM exception's real cause is routinely one level down.
        private static string Describe(Exception ex)
        {
            string s = ((object)ex).GetType().Name + ": " + ex.Message;
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                s += " <- " + ((object)inner).GetType().Name + ": " + inner.Message;
            }
            return s;
        }

        // The top few stack frames, one per line, so an unexpected failure names the call site.
        // Truncated: the whole trace through a game tick is unreadable in a console line.
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
            if (lines.Length > MaxFrames)
            {
                // Say so: a report that just stops mid-trace reads like a short stack.
                frames.Append("  (trace truncated after ").Append(MaxFrames).Append(" frames)\n");
            }
            return frames.ToString();
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netreset] {0} passed, {1} failed\n", pass, fail);
        }

        // Always alive=true: the alive-flag edge belongs to the puppet DEATH path (ExplodePuppet),
        // which is a different subject, so this suite never varies it rather than carrying a seam
        // that reads as coverage it does not have.
        private static byte[] ShipFrame(ref ushort seq, ref uint senderMs)
        {
            senderMs += 33; // advance, or ShipStateBuffer refuses the sample as stale
            return NetProtocol.EncodeShipState(seq++, senderMs, RemoteShipPos, Vector2.Zero,
                FacingUp, alive: true, firing: false, shotsPerSec: 8, bulletLife: 450f);
        }

        private static byte[] FriendFrame(ref ushort seq, ref uint senderMs)
        {
            senderMs += 33; // as above -- the channel has its own buffer and its own clock
            return NetProtocol.EncodeFriendState(FriendSlot, seq++, senderMs, FriendShipPos,
                Vector2.Zero, FacingUp, firing: false, shotsPerSec: 8, bulletLife: 450f);
        }

        // Hand the level back a roster it can play on. It is NOT the roster the suite found --
        // the scene is in its reset branch and will replay from the checkpoint -- but nothing
        // net-owned may be left behind: a squatting Remote / RemoteFriend seat would have
        // SpawnAllPlayers spawn ships for players who do not exist, for the rest of the run.
        private static void Teardown(Oracle oracle, ComponentBin bin, Game game,
            ControlDevice localDevice, Action<string, bool> check)
        {
            try
            {
                // First: while a client session is Active, ComponentBin.Add diverts replicable
                // spawns and NetPuppets owns a driver component. Stop() undoes both.
                NetSession.Stop("reset/tryadd scenario finished");
                check("the session is stopped", !NetSession.Active);

                foreach (PlayerShip s in oracle.GetShips().ToArray())
                {
                    bin.Remove((GameComponent)(object)s);
                }
                bin.TopOfTickFlush(); // carry the removals out AND clear any filter still armed

                oracle.RemovePlayerAt(NetSession.HostPrimarySlot, ControlDevice.Remote);
                oracle.RemovePlayerAt(FriendSlot, ControlDevice.RemoteFriend);
                check("no Remote or RemoteFriend seat is left squatting the roster",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote)
                    && !oracle.DeviceIsPlaying(ControlDevice.RemoteFriend));

                if (oracle.IsSeated(GrantedSlot) && !oracle.IsSeated(0))
                {
                    oracle.MovePlayerSlot(GrantedSlot, 0);
                }
                bool restored = oracle.Players == 1 && oracle.IsSeated(0)
                    && oracle.Controller(0) == localDevice;
                if (restored)
                {
                    PlayerShip ship = new PlayerShip(game);
                    ship.Setup(0, LocalShipPos, startup: false, invulnerable: true, FacingUp);
                    bin.Add((GameComponent)(object)ship);
                }
                check("the local player is back at slot 0 on " + localDevice + " with a live ship",
                    restored && oracle.IsAlive(0) && oracle.GetShips().Count == 1);
            }
            catch (Exception ex)
            {
                check("teardown ran (" + Describe(ex) + ")", false);
            }
        }
    }
}
