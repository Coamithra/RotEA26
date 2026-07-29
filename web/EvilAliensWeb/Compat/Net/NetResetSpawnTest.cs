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
    // which is what this does, with no production change beyond four internal seams: ONE real
    // CLIENT session on one endpoint of an in-process NetWire, and a scripted host driving the
    // other end by hand.
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
    // THE THREE LEGS, and why they are in this order.
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
    // stand in for is SpawnAllPlayers' respawn of the local seat, which it does with the game's
    // own three lines (new PlayerShip / Setup / bin.Add) -- flagged at each site.
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
    // FLAKINESS. Unlike its Game-free siblings this DOES run on Environment.TickCount64 (that is
    // what NetSession.Update reads), so two real-clock windows could in principle bite: the 500 ms
    // FriendTimeoutMs and the 8 s peer-drop verdict. Every leg re-sends both streams immediately
    // before its Update, which resets both clocks, so the exposure per leg is the handful of
    // microseconds between the send and the Update. Measured non-flaky over 10 consecutive runs
    // before it was committed as a probe; if it ever does flake, the fix is step 2a's injected
    // clock, not a looser assertion.
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
            ushort streamSeq = 1;
            uint senderMs = 100;

            try
            {
                NetSession.StartForTest(game, host: false, ours, Room);
                peer.Open(Room);
                Check("session started as a CLIENT", NetSession.IsClient);

                // ---- handshake: the scripted peer is the HOST and grants us GrantedSlot ------
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
                    // hello (build hash, role, protocol) prints its own [net] line above.
                    sb.Append(Tally(pass, fail));
                    return sb.ToString();
                }

                // ---- 1. NEGATIVE: a purge armed in base.Update is flushed BEFORE the drain ----
                sb.Append(" 1. NEGATIVE -- LoseLife / UpdateWin / UpdateResetting: purge flushed before the drain\n");
                long rx = ours.RxDelivered;
                peer.SendStream(ShipFrame(ref streamSeq, ref senderMs, alive: true));
                peer.SendStream(FriendFrame(ref streamSeq, ref senderMs));
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
                RespawnLocalShip(bin, game);
                Check("PRECONDITION the local ship is back for the reset leg",
                    oracle.IsAlive(GrantedSlot));

                rx = ours.RxDelivered;
                // ResetModeRespawn rather than ResetModeReset: NetApplyReset purges BEFORE its
                // mode switch, so all three modes arm the same standing filter, and this is the
                // one that does not also spend a life.
                peer.SendReliable(NetProtocol.EncodeByteEvent(eventSeq++, NetProtocol.EvReset,
                    NetSession.ResetModeRespawn));
                peer.SendStream(ShipFrame(ref streamSeq, ref senderMs, alive: true));
                peer.SendStream(FriendFrame(ref streamSeq, ref senderMs));
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
                RespawnLocalShip(bin, game);
                Check("PRECONDITION the local ship is back for the retry leg",
                    oracle.IsAlive(GrantedSlot));

                rx = ours.RxDelivered;
                peer.SendStream(ShipFrame(ref streamSeq, ref senderMs, alive: true));
                peer.SendStream(FriendFrame(ref streamSeq, ref senderMs));
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
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
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
            string[] lines = trace.Split('\n');
            StringBuilder frames = new StringBuilder();
            for (int i = 0; i < lines.Length && i < 8; i++)
            {
                frames.Append("  ").Append(lines[i].Trim()).Append('\n');
            }
            return frames.ToString();
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netreset] {0} passed, {1} failed\n", pass, fail);
        }

        // Stand-in for GameScene.SpawnAllPlayers' respawn of the local seat: the same three lines
        // it uses, minus the spawn-spread arithmetic and the Recycle (a fresh instance keeps the
        // scenario's object identities readable -- the recycle pool at this point holds the very
        // ships the purge and the diverted TryAdds put there). The real choreography reaches this
        // ~3 s of game time later via Resetting -> Startup plus a background crossfade that needs
        // Draw; none of that is under test, and the ONLY thing the retry legs need from it is a
        // non-null FindLocalShip().
        private static void RespawnLocalShip(ComponentBin bin, Game game)
        {
            PlayerShip ship = new PlayerShip(game);
            ship.Setup(GrantedSlot, LocalShipPos, startup: false, invulnerable: true, FacingUp);
            bin.Add((GameComponent)(object)ship);
        }

        private static byte[] ShipFrame(ref ushort seq, ref uint senderMs, bool alive)
        {
            senderMs += 33;
            return NetProtocol.EncodeShipState(seq++, senderMs, RemoteShipPos, Vector2.Zero,
                FacingUp, alive, firing: false, shotsPerSec: 8, bulletLife: 450f);
        }

        private static byte[] FriendFrame(ref ushort seq, ref uint senderMs)
        {
            senderMs += 1; // distinct sender times: ShipStateBuffer refuses a non-advancing sample
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
