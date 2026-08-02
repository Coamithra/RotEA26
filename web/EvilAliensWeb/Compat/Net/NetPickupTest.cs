using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetPickup() -- the verification for the remote-powerup-pickup family (cards 83271f3d, the
    // "2"/Linker powerup; 10f9dba4, the option count; d53431b4, the level-up sparkle). Run it
    // inside a level, or `eval NetPickup` under eahl. Committed as
    // tools/headless/probes/net_pickup.txt.
    //
    // WHAT IT COVERS. NetSession.ApplyRemotePowerup used to mirror ONLY the HUD icon, so the
    // other player's ship -- a puppet on this screen -- got the readout and none of the effect of
    // every powerup they collected. Two types have no other route onto the wire:
    //   Linker  -- readyToConnect is set nowhere but the local pickup path, so the "2" powerup's
    //              glow never appeared on the puppet AND PlayerShip.CollidesWith's
    //              (readyToConnect & other.readyToConnect) was false on BOTH peers: the connector
    //              was unreachable in an online session, which is precisely what card 83271f3d
    //              reported ("impossible to trigger the connector ... even if both players have
    //              the 2 powerup in their local view").
    //   Option  -- the pickup's 1-4 Option ships. The LEVEL-driven ones already arrive over
    //              MsgHudState, which is why the observer saw SOME and always FEWER.
    // FirePower and Range ride MsgShipState already; Blast and OneUp are deliberately unmirrored.
    //
    // WHY A DATA SUITE AND NOT TWO WINDOWS. Every claim here is about state a frame cannot show
    // (an armed flag, a count that is right on one screen), and the doc's standing gotcha applies:
    // a backgrounded tab throttles to ~1 tick/sec, so a two-window run cannot reach the rates
    // these paths live at. The shape follows eaNetScore.test / eaNetCombo.test: each positive
    // stands beside the PRE-CARD behaviour over the identical input, because a green tick means
    // nothing unless the same sequence is shown to break what it replaced.
    //
    // THE COMBINED-PATH ARITHMETIC (leg 4) is the leg to read first, and it is the one a
    // single-path test could not have caught. On a real observer BOTH mirrors run: the per-pickup
    // one added by this card and the per-level one from card 1a3ad45a. They add up rather than
    // double-count because a PICKUP NEVER CHANGES A LEVEL -- DoSpecial and PowerUp are disjoint,
    // and both peers derive the pickup's own count from the SAME ship-local optionLevel, which
    // only PowerUp writes. So leg 4 drives the full sequence in real-session order (owner: real
    // CollidesWith pickups + real combo-driven level-ups; observer: real EvClaim frames off the
    // wire + the real MsgHudState content) and requires the two counts to move by the same amount.
    //
    // *** DESTRUCTIVE, like eaNetResetSpawn / eaNetSceneOrder. *** It pairs a real session onto
    // the live level, seats a Remote puppet, spends real pickups into the live ScoreVisualiser and
    // really tethers the two ships together. Run it in a throwaway ?level=Level2&invuln boot,
    // never in a game you care about. Teardown stops the session, sweeps what it planted and frees
    // the Remote seat; it does NOT unwind the powerup levels or combo it spent.
    //
    // NOT COVERED, on purpose: card d53431b4's MUTE half (a remote pickup no longer plays the
    // "powerup" cue). SoundManager exposes no cue counter, and adding one for a test would be a
    // production field for no other reader -- see the card's closing comment.
    internal static class NetPickupTest
    {
        private const string Room = "netpickup";
        private const ulong PeerToken = 0x9A17C0DEUL;

        // Off-screen: a planted powerup must never be drawn, and must never be collected by the
        // real ship it shares a world with.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        public static string Run()
        {
            StringBuilder sb = new StringBuilder("[netpickup] remote powerup pickups"
                + " (cards 83271f3d / 10f9dba4 / d53431b4)\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // The eaNetResetSpawn gate: this needs a live world (a real local ship, and a scene
            // for SpawnPuppet to gate on), and must never tear down a session a player is in.
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
            Game game = bin.Game;

            List<GameComponent> planted = new List<GameComponent>();
            int playersBefore = oracle.Players;
            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunLegs(sb, Check, oracle, bin, score, game, planted);
            }
            catch (Exception ex)
            {
                Check("the legs ran (" + Describe(ex) + ")", false);
            }
            finally
            {
                sb.Append(" 9. teardown\n");
                Teardown(sb, Check, oracle, bin, planted, playersBefore);
                NetHost.Current = hostBefore;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, ScoreVisualiser score, Game game, List<GameComponent> planted)
        {
            // ---- 0. rig ------------------------------------------------------------------
            sb.Append(" 0. rig -- a real HOST session, a scripted client, and its ship puppet\n");
            bool rosterOk = oracle.Players == 1 && oracle.IsSeated(0) && oracle.IsAlive(0);
            Check("PRECONDITION one local player at slot 0 with a live ship (players="
                + oracle.Players + ")", rosterOk);
            if (!rosterOk)
            {
                return;
            }
            PlayerShip owner = oracle.GetShips()[0];

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;
            ushort shipSeq = 1;
            uint shipMs = 100;

            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("the scripted client paired (peer=" + (NetSession.PeerUp ? "up" : "down") + ")",
                NetSession.PeerUp);

            // The peer's ship stream is what makes SpawnPuppet seat a ControlDevice.Remote ship --
            // the real path, not a hand-placed one, because the whole suite is about what the
            // mirror does TO that puppet.
            peer.SendStream(ShipFrame(ref shipSeq, ref shipMs));
            wire.Pump();
            NetSession.Update();
            int peerSlot = oracle.GetPlayerIndex(ControlDevice.Remote);
            bool puppetUp = NetSession.HasRemotePuppet && peerSlot >= 0
                && peerSlot < ScoreVisualiser.SlotCount;
            Check("the peer's ship puppet was adopted into a Remote seat (slot=" + peerSlot + ")",
                puppetUp);
            if (!puppetUp)
            {
                return;
            }
            PlayerShip puppet = FindShip(oracle, peerSlot);
            Check("PRECONDITION the puppet ship is reachable and we do NOT own its slot",
                puppet != null && !NetSession.OwnsSlot(peerSlot));
            if (puppet == null)
            {
                return;
            }

            // ---- 1. the "2" powerup arms the puppet (card 83271f3d) ----------------------
            sb.Append(" 1. Linker -- the \"2\" powerup arms the collector's ship\n");
            // NEGATIVE FIRST, and it is the bug verbatim: the owner takes a Linker locally and
            // flies into the peer. Pre-card the puppet was never armed, so nothing happened.
            TakeLocalPickup(owner, score, Powerup.PowerupType.Linker);
            Check("PRECONDITION the local pickup armed OUR ship", owner.NetReadyToConnect);
            Check("NEGATIVE the puppet is still unarmed before its own pickup replicates",
                !puppet.NetReadyToConnect);
            int connectorsBefore = oracle.NrOfShipConnectors();
            owner.CollidesWith(puppet);
            bin.TopOfTickFlush();
            Check("... so flying into it forms NO connector -- the reported symptom",
                oracle.NrOfShipConnectors() == connectorsBefore);

            // POSITIVE: the peer collects its own Linker, which reaches us as a real EvClaim.
            Powerup linker = Plant(bin, game, planted, Powerup.PowerupType.Linker);
            if (!Claim(peer, wire, bin, ref eventSeq, linker, (byte)peerSlot, planted))
            {
                Check("PRECONDITION the planted Linker got a netId", false);
                return;
            }
            Check("the peer's Linker claim armed its puppet", puppet.NetReadyToConnect);

            // ---- 2. the OwnsSlot gate ----------------------------------------------------
            // The host runs ApplyRemotePowerup for a CLIENT's claim, so a claim naming a slot we
            // own would re-run a pickup our own CollidesWith already ran -- a second batch of
            // Options every time. The HUD half is ungated (it is idempotent), which is what makes
            // this a gate rather than an early return, and is asserted as the positive control.
            sb.Append(" 2. the OwnsSlot gate -- a claim naming a slot WE own runs no ship effect\n");
            int ownOptionsBefore = owner.NetOptionCount;
            score.RemovePowerup(NetSession.HostPrimarySlot);
            Powerup ours2 = Plant(bin, game, planted, Powerup.PowerupType.Option);
            if (Claim(peer, wire, bin, ref eventSeq, ours2, NetSession.HostPrimarySlot, planted))
            {
                Check("no Option was spawned on our own ship (" + ownOptionsBefore + " -> "
                    + owner.NetOptionCount + ")", owner.NetOptionCount == ownOptionsBefore);
                Check("CONTROL the HUD indicator still landed, so the gate is not an early return",
                    score.NetPowerupActive(NetSession.HostPrimarySlot));
            }

            // ---- 3. the connector can form, and breaks on both peers (card 83271f3d) -----
            sb.Append(" 3. with both ships armed the connector forms, and EvTetherBreak breaks it\n");
            connectorsBefore = oracle.NrOfShipConnectors();
            owner.CollidesWith(puppet);
            bin.TopOfTickFlush();
            Check("flying into the armed puppet forms a connector ("
                + connectorsBefore + " -> " + oracle.NrOfShipConnectors() + ")",
                oracle.NrOfShipConnectors() == connectorsBefore + 1);
            // The peer saw the tether hit on its screen and sent EvTetherBreak. Pre-card the base
            // GameScene body was empty, so only TeamChallenge ever acted on it and a Linker
            // connector survived on this peer alone -- one player pulled toward an anchor the
            // other had already let go of.
            NetScene.Current?.NetApplyTetherBreak();
            bin.TopOfTickFlush();
            Check("the peer's EvTetherBreak breaks it here too ("
                + oracle.NrOfShipConnectors() + " connectors)",
                oracle.NrOfShipConnectors() == connectorsBefore);

            // ---- 4. the combined-path option arithmetic (card 10f9dba4) ------------------
            sb.Append(" 4. option count -- owner vs observer over the FULL remote sequence\n");
            // CONTROL: one owner pickup mirrored the PRE-CARD way (the HUD icon and nothing else).
            score.RemovePowerup(NetSession.HostPrimarySlot);
            int ownerBase = owner.NetOptionCount;
            int obsBase = puppet.NetOptionCount;
            TakeLocalPickup(owner, score, Powerup.PowerupType.Option);
            score.SetPowerup(Powerup.PowerupType.Option, peerSlot); // the whole pre-card mirror
            int ownerCtl = owner.NetOptionCount - ownerBase;
            int obsCtl = puppet.NetOptionCount - obsBase;
            Check("CONTROL the pre-card mirror leaves the observer behind (owner +" + ownerCtl
                + ", observer +" + obsCtl + ")", ownerCtl > 0 && obsCtl == 0);

            // FIXED: the same shape, but the observer gets what a real one gets -- an EvClaim per
            // pickup and the owner's MsgHudState content per level-up, in that order.
            ownerBase = owner.NetOptionCount;
            obsBase = puppet.NetOptionCount;
            int levelUps = 0;
            for (int round = 0; round < 3; round++)
            {
                TakeLocalPickup(owner, score, Powerup.PowerupType.Option);
                Powerup mirrored = Plant(bin, game, planted, Powerup.PowerupType.Option);
                Claim(peer, wire, bin, ref eventSeq, mirrored, (byte)peerSlot, planted);
                if (DriveOneLevelUp(score, owner, Powerup.PowerupType.Option))
                {
                    levelUps++;
                }
                ReplicateHudState(score, NetSession.HostPrimarySlot, peerSlot);
            }
            int ownerDelta = owner.NetOptionCount - ownerBase;
            int obsDelta = puppet.NetOptionCount - obsBase;
            Check("PRECONDITION the sequence really exercised both paths (3 pickups, "
                + levelUps + " level-ups)", levelUps > 0 && ownerDelta > 3);
            Check("owner and observer moved by the SAME amount (owner +" + ownerDelta
                + ", observer +" + obsDelta + ")", ownerDelta == obsDelta);
            Check("... and the two ships agree on the Option level ("
                + score.GetPowerupLevel(Powerup.PowerupType.Option, NetSession.HostPrimarySlot)
                + " / " + score.GetPowerupLevel(Powerup.PowerupType.Option, peerSlot) + ")",
                score.GetPowerupLevel(Powerup.PowerupType.Option, NetSession.HostPrimarySlot)
                    == score.GetPowerupLevel(Powerup.PowerupType.Option, peerSlot));

            // ---- 5. the level-up sparkle (card d53431b4) ---------------------------------
            sb.Append(" 5. the sparkle -- shown on a real remote level-up, not on a catch-up\n");
            int[] levels = new int[NetProtocol.HudLevelCount];
            SetRemoteOptionLevel(score, peerSlot, levels, 0);
            int fxBefore = Census<PowerupEffect>(game);
            SetRemoteOptionLevel(score, peerSlot, levels, 1);
            int fxOneStep = Census<PowerupEffect>(game) - fxBefore;
            Check("a ONE-step remote level-up plays exactly one PowerupEffect (+" + fxOneStep + ")",
                fxOneStep == 1);
            SetRemoteOptionLevel(score, peerSlot, levels, 0);
            fxBefore = Census<PowerupEffect>(game);
            SetRemoteOptionLevel(score, peerSlot, levels, 4);
            int fxClimb = Census<PowerupEffect>(game) - fxBefore;
            Check("a 4-step CATCH-UP climb plays none (+" + fxClimb
                + ") -- those level-ups happened before we were watching", fxClimb == 0);
            Check("CONTROL the catch-up still applied, so the leg is not vacuous ("
                + score.GetPowerupLevel(Powerup.PowerupType.Option, peerSlot) + ")",
                score.GetPowerupLevel(Powerup.PowerupType.Option, peerSlot) == 4);
        }

        // ---- helpers -----------------------------------------------------------------------

        // The REAL local pickup path: PlayerShip.CollidesWith's powerup branch. The scratch
        // powerup is never added to the bin (it must not take a netId or be drawn), and the kill
        // note the pickup leaves behind is taken straight back so the 64-entry attribution map is
        // handed over as it was found.
        private static void TakeLocalPickup(PlayerShip ship, ScoreVisualiser score,
            Powerup.PowerupType type)
        {
            Powerup pu = Powerup.NewPowerup(
                ServiceHelper.Get<IComponentBinService>().ComponentBin, ship.Game);
            pu.MakeType(type);
            pu.taken = false;
            ship.CollidesWith(pu);
            NetSession.TakeKillNote(pu);
        }

        // A real Powerup in the LIVE bin so NetIdRegistry allocates it a real id through the real
        // ComponentAdded seam, then the peer's claim for it off the wire -- i.e. exactly what an
        // observing peer receives when the other player collects one.
        private static Powerup Plant(ComponentBin bin, Game game, List<GameComponent> planted,
            Powerup.PowerupType type)
        {
            Powerup powerup = Powerup.NewPowerup(bin, game);
            powerup.Setup(Nowhere);
            powerup.MakeType(type); // after Setup (which rolls a type), before Add
            bin.Add((GameComponent)(object)powerup);
            planted.Add((GameComponent)(object)powerup);
            return powerup;
        }

        private static bool Claim(InMemoryTransport peer, NetWire wire, ComponentBin bin,
            ref ushort eventSeq, Powerup powerup, byte slot, List<GameComponent> planted)
        {
            if (!NetIdRegistry.TryGetByComp((GameComponent)(object)powerup, out NetIdRegistry.Entry e))
            {
                return false;
            }
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, e.Id, slot));
            wire.Pump();
            NetSession.Update();
            bin.TopOfTickFlush();
            planted.Remove((GameComponent)(object)powerup);
            return true;
        }

        // Drive the owner's REAL combo -> AddExp -> onLevelUp chain until the level moves. Bounded
        // rather than looped forever: AddExp's gain is difficulty-scaled, and a level already at 4
        // never advances.
        private static bool DriveOneLevelUp(ScoreVisualiser score, PlayerShip owner,
            Powerup.PowerupType type)
        {
            int before = score.GetPowerupLevel(type, owner.Owner);
            for (int i = 0; i < 400 && score.GetPowerupLevel(type, owner.Owner) == before; i++)
            {
                score.SustainCombo(owner.Owner, owner.Position);
            }
            return score.GetPowerupLevel(type, owner.Owner) != before;
        }

        // What MsgHudState carries, read off the owner's slot and applied to the observer's
        // through the very members NetSession's send and rx paths use. The byte layout is
        // eaNetCombo.test's job; what matters here is that the level half of the mirror runs
        // beside the pickup half.
        private static void ReplicateHudState(ScoreVisualiser score, int from, int to)
        {
            int[] levels = new int[NetProtocol.HudLevelCount];
            score.NetReadHudState(from, levels, out int combo, out Powerup.PowerupType? type,
                out float progress);
            score.NetSetHudState(to, combo, type, progress, levels);
        }

        private static void SetRemoteOptionLevel(ScoreVisualiser score, int slot, int[] levels,
            int level)
        {
            Array.Clear(levels, 0, levels.Length);
            levels[(int)Powerup.PowerupType.Option] = level;
            score.NetSetHudState(slot, 0, Powerup.PowerupType.Option, 0f, levels);
        }

        private static int Census<T>(Game game)
        {
            int n = 0;
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T)
                {
                    n++;
                }
            }
            return n;
        }

        private static PlayerShip FindShip(Oracle oracle, int slot)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Owner == slot)
                {
                    return s;
                }
            }
            return null;
        }

        private static byte[] ShipFrame(ref ushort seq, ref uint senderMs)
        {
            senderMs += 33;
            return NetProtocol.EncodeShipState(seq++, senderMs, new Vector2(400f, 300f),
                Vector2.Zero, 4.712389f, alive: true, firing: false, 8, 450f);
        }

        private static void Teardown(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, List<GameComponent> planted, int playersBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("pickup suite teardown");
                }
                Check("the session is stopped", !NetSession.Active);
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                // The puppet ship and its seat, which Stop() does not unwind -- only the
                // peer-loss paths (RevertToSinglePlayer) do, and nothing here goes through one.
                // Left standing, the level plays on with a frozen ghost ship in slot 1.
                foreach (PlayerShip s in new List<PlayerShip>(oracle.GetShips()))
                {
                    if (s.Controller == ControlDevice.Remote
                        || s.Controller == ControlDevice.RemoteFriend)
                    {
                        bin.Remove((GameComponent)(object)s);
                    }
                }
                oracle.ReleasePlayer(ControlDevice.Remote);
                oracle.ReleasePlayer(ControlDevice.RemoteFriend);
                bin.TopOfTickFlush();
                planted.Clear();
                Check("no Remote seat is left squatting the roster (players=" + oracle.Players
                    + ", was " + playersBefore + ")",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote)
                    && oracle.Players == playersBefore);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + Describe(ex) + ")", false);
            }
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static string Tally(int pass, int fail)
        {
            return "[netpickup] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
