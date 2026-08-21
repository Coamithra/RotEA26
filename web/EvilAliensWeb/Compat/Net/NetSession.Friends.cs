using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Replication of every ship a peer owns BEYOND its primary -- the host's AI "friend" ships
    // (Mechanical Friends cheat) and, since card 4d904410, either peer's COUCH players.
    //
    // Originally this existed only for AI friends (coverage-gaps follow-up to card 11.2): the host
    // runs them (real AI, real bullets, host-authoritative enemy kills that already replicate) and
    // streams each one to the client, which shows it as a ControlDevice.RemoteFriend puppet whose
    // bullets re-fire locally -- the single remote-ship scheme, generalised to several ships keyed
    // by slot. That generalisation is exactly what a couch player needs, so card 4d904410 made the
    // stream BIDIRECTIONAL rather than inventing a second mechanism: `ControlDevice.RemoteFriend`
    // now means "network-driven extra ship", whoever owns it.
    //
    // Since card b2828be8 (Stage 11.8) the extras ride the SAME MsgShipState layout as the
    // primary (slot-keyed, ShipFlagPrimary clear), live on the peer's channel
    // (PeerChannel.Extras, keyed by slot -- the shape FriendChannel always had) and are driven
    // by the same DriveShip the primary uses. The deliberate asymmetries with the primary are
    // BEHAVIOUR, kept: a dead extra simply stops being streamed and the per-slot timeout
    // explodes its puppet (no alive edge), and a resume after a would-be-fatal gap clears the
    // buffer (the card-14c5943e resume-gap rule) where the primary's respawn clear rides its
    // alive edge instead.
    //
    //   * IDENTITY SLOT MAPPING: the puppet lands in the SAME oracle slot its owner runs it in, so
    //     per-slot score/lives (EvScoreSync sends them verbatim) line up. Since card 4d904410 the
    //     host allocates every slot, so this holds for the primaries too and there is no
    //     host-relative translation left anywhere. AddPlayerAt / RemovePlayerAt still guard against
    //     ever squatting or freeing a live human/remote slot.
    //   * A ship that dies / leaves simply STOPS being streamed; a per-slot timeout explodes its
    //     puppet (no explicit death event needed), and a later stream re-spawns it.
    //   * With no AI friends and no couch players the path stays DORMANT: nothing is streamed and
    //     the peer's Extras stay empty, so a plain two-player session is unchanged.
    public static partial class NetSession
    {
        // No sample for this long -> the friend died / left / the level ended: explode its puppet.
        // Generous vs the 33 ms stream cadence (~15 missed packets) so a transient gap never flickers
        // it; stretched to the paused backstop while either side holds a pause (the stream stalls then).
        private const long FriendTimeoutMs = 500;

        // TX side, one per slot we STREAM: the slot's wire shot count, plus which ship it was last
        // read from. Session-level scratch, NOT per-peer -- our frames are broadcast, so every
        // peer sees the same counters. See NetSession's lastTxShotCount comment for why the count
        // has to belong to the slot rather than to the ship: a couch player's ship dies and
        // respawns inside the 500 ms FriendTimeoutMs, so the puppet (and its baseline) survives
        // the counter restarting at 0.
        private sealed class FriendTxShots
        {
            public PlayerShip Ship;
            public byte ShipShots;
            public byte WireCount;
        }

        private static readonly Dictionary<byte, FriendTxShots> friendTxShots = new Dictionary<byte, FriendTxShots>();
        private static ushort friendTxSeq; // separate from txSeq so the primary stream's seq stays contiguous
        private static readonly List<byte> friendScratchSlots = new List<byte>(4);

        // ---- stream each live extra ship we own (called on the ship-stream cadence) ------------
        private static void SendFriendStates(long now)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                // Everything we simulate except our primary (which rides the primary-flagged
                // frame): AI friends and couch players. ?aiplayer forces the LOCAL ship's Update
                // branch to AI but leaves its Controller (Keyboard/pad), so the primary is still
                // excluded by slot.
                if (!IsLocallyOwned(s) || s.Owner == localPrimarySlot)
                {
                    continue;
                }
                int slot = s.Owner;
                if (slot < 0 || slot > 255)
                {
                    continue;
                }
                // The cumulative shot count, exactly as the primary ship streams it (card
                // a45b78f6) -- both feed the identical DriveShip/NetApplyRemoteState, so a
                // couch player's tap and an AI friend's burst are replicated shot for shot.
                if (!friendTxShots.TryGetValue((byte)slot, out FriendTxShots tx))
                {
                    tx = new FriendTxShots();
                    friendTxShots[(byte)slot] = tx;
                }
                byte shotCount = AdvanceTxShots(s, ref tx.Ship, ref tx.ShipShots, ref tx.WireCount);
                // alive is unconditionally true here: a dead extra is simply not in GetShips()
                // any more, so it stops being streamed -- the receiver's timeout is its death.
                transport.SendStream(NetProtocol.EncodeShipState((byte)slot, primary: false, friendTxSeq++, (uint)(now - sessionStartAt),
                    s.GetPosition(), s.NetVelocity, s.NetLastFireAim, alive: true, shotCount, s.NetShotsPerSec, s.NetBulletLife,
                    scriptGate: false, s.NetAsplodeBits, s.NetBounceBits));
            }
        }

        // Does the extra ship streamed for `slot` have an adopted puppet? The SpawnFriend half of
        // NetResetSpawnTest's subject -- same reasoning as HasRemotePuppet, and this is the site
        // that bites more often, since couch players hit the resets that arm Purge<PlayerShip>.
        internal static bool HasFriendPuppet(byte slot)
        {
            return peer != null && peer.Extras.TryGetValue(slot, out ShipChannel ch) && ch.Puppet != null;
        }

        // Is this slot actively streamed to us? (Host-side grant bookkeeping asks.)
        private static bool FriendChannelExists(byte slot)
        {
            return peer != null && peer.Extras.ContainsKey(slot);
        }

        // ---- receive (either role) --------------------------------------------------------------
        //
        // The extra-ship half of HandleShipFrame, which has already refreshed the peer's
        // heartbeat and (if needed) run PeerConnected before routing here.
        private static void HandleExtraShipFrame(PeerChannel p, byte slot, ShipSample sample, int shots, float life)
        {
            if (!p.Extras.TryGetValue(slot, out ShipChannel ch))
            {
                ch = new ShipChannel(isPrimary: false);
                p.Extras[slot] = ch;
            }
            // Card 14c5943e: a sample arriving after a gap the channel would have timed out of at
            // its NORMAL 500 ms means the ship died/left and respawned, or the whole link dropped
            // and recovered -- either way the buffered samples describe a previous life (or a
            // pose from before the gap) and interpolating across them drags the puppet from the
            // old position to the new one, the primary remote's card-df72b051 slide. A channel
            // can only still be alive across such a gap because the stall / pause / link-quiet
            // arms above protected it, so the resume starts the puppet from its own samples.
            // The clear must run BEFORE LastRxAt is refreshed (the gap is what it reads), and it
            // is what keeps the protective arms from re-opening the bridge they exist to ride out.
            // The PRIMARY channel deliberately has no equivalent: its respawn case is closed
            // by the alive-edge clear in HandleShipFrame, and after a live-ship hiccup its lerp
            // across the gap IS the catch-up (the ship really moved), so a friend puppet snaps
            // where the primary glides -- a known, chosen asymmetry.
            if (ch.Buffer.HasSamples && NowMs - ch.LastRxAt > FriendTimeoutMs)
            {
                ch.ClearSamples();
            }
            ch.ShotsPerSec = shots;
            ch.BulletLife = life;
            ch.LastRxAt = NowMs;
            ch.Buffer.Add(sample);
            grantsAwaitingStream.Remove(slot); // the peer took the grant -- stop the claim clock
        }

        // ---- per-tick puppet management + interpolation clock (either role) --------------------
        private static void TickFriends(PeerChannel p)
        {
            if (p.Extras.Count == 0)
            {
                return;
            }
            long now = NowMs;
            // While the peer is PAUSED its stream stops entirely; while it is STALLED (card 11.5's
            // grace window) the link is unwell but the session is deliberately being ridden out --
            // in neither case may a 500 ms gap blow up the puppets, or a wifi hiccup would kill the
            // peer's couch players while the run itself survives. Enemy puppets park for the same
            // reason (NetPuppets' PeerStalled check).
            //
            // The LINK-QUIET arm is what actually delivers that promise (card 14c5943e). The
            // Stalled flag only arms at PeerStallMs (1200 ms) of total stream silence, and
            // every channel's own 500 ms boundary is crossed FIRST -- so before this arm, the
            // "stalled" timeout was structurally unreachable and a 0.5-1.2 s hiccup exploded
            // every couch/AI-friend puppet, against this very comment's stated intent. The whole
            // link being quiet past the channel's own threshold is a hiccup, not a ship death (a
            // death stops ONE slot's stream while the primary heartbeat keeps flowing), so it is
            // recognised at the same 500 ms the channel itself times out at. `Stalled` is
            // kept in the disjunction for INTENT, not effect -- it arms strictly later than the
            // link-quiet test, so today it can never change the result; it stays so the ladder
            // still reads (and behaves) right if the stall flag ever gains hysteresis.
            long timeout = (p.RemotePaused || localPaused) ? PausedPeerTimeoutMs
                : (p.Stalled || now - p.LastRxStreamAt > FriendTimeoutMs) ? PeerTimeoutMs + PeerGraceMs
                : FriendTimeoutMs;
            friendScratchSlots.Clear();
            foreach (byte slot in p.Extras.Keys)
            {
                friendScratchSlots.Add(slot);
            }
            foreach (byte slot in friendScratchSlots)
            {
                if (!p.Extras.TryGetValue(slot, out ShipChannel ch))
                {
                    continue;
                }
                // Adopt/release: the scene can purge the puppet out from under us (reset/terminate).
                if (ch.Puppet != null && !oracle.GetShips().Contains(ch.Puppet))
                {
                    ch.Puppet = null;
                    ch.RenderMs = double.NaN;
                }
                if (now - ch.LastRxAt > timeout)
                {
                    // The stream stopped: mirror the death, but KEEP the seat -- the owning peer
                    // holds it for the respawn (exactly like the primary remote puppet, whose
                    // "slot stays reserved" comment says the same). Freeing it here would let the
                    // host's own next AddPlayer(AI)/couch join take the slot, and the returning
                    // ship's stream would then be stuck retrying SpawnFriend forever: an
                    // invisible ship with cross-credited kills, the very bug this card removes.
                    // Seats are released on peer loss / teardown (ReleaseAllFriendPuppets).
                    if (ch.Puppet != null)
                    {
                        ExplodeFriend(p, ch, slot);
                    }
                    else
                    {
                        p.Extras.Remove(slot);
                    }
                    continue;
                }
                AdvanceShipClock(ch);
                if (ch.Puppet == null && p.Up && ch.Buffer.HasSamples && FindLocalShip() != null)
                {
                    SpawnFriend(ch, slot);
                }
            }
        }

        private static void SpawnFriend(ShipChannel ch, byte slot)
        {
            // Identity slot: seat the puppet in the SAME slot its owner runs it in (score/lives sync
            // lines up). AddPlayerAt refuses a busy slot, so this never squats a human/remote ship --
            // but the host RESERVES a granted couch slot as RemoteFriend when it answers the join
            // request, so a seat we already hold for this very puppet is the expected case too.
            if (!oracle.AddPlayerAt(slot, ControlDevice.RemoteFriend)
                && !(oracle.IsSeated(slot) && oracle.Controller(slot) == ControlDevice.RemoteFriend))
            {
                return; // slot busy with someone else -- retry
            }
            // The scene may already have put a ship in this seat (SpawnAllPlayers respawns every
            // seated slot after a reset) -- adopt it rather than adding a second ship to the slot.
            PlayerShip existing = oracle.GetPlayerShip(slot);
            if (existing != null)
            {
                ch.Puppet = existing;
                ch.RenderMs = double.NaN;
                return;
            }
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, ch.Buffer.Newest.Pos, startup: false, invulnerable: false, PuppetSpawnDirection());
            if (!bin.TryAdd((GameComponent)(object)ship))
            {
                // Same standing-Purge<PlayerShip> race as the primary remote ship in
                // SpawnPuppet (see the fuller note there: the reachable arming site is
                // NetApplyReset, which purges from inside the rx drain), and the one likelier
                // to bite -- couch players hit the resets that arm it constantly. Leave
                // ch.Puppet clear so the caller's null check retries next tick (card 74403f83).
                return;
            }
            ch.Puppet = ship;
            Console.WriteLine("[net] friend ship joined slot=" + slot);
        }

        // Mirror the death LOOK locally (explosions + cue). Never Die() -- that would fire the local
        // respawn-summon for a ship we don't own; a real respawn arrives as a new stream. The oracle
        // seat is deliberately NOT freed (see the timeout comment above).
        private static void ExplodeFriend(PeerChannel p, ShipChannel ch, byte slot)
        {
            PlayerShip exploded = ch.Puppet;
            ch.Puppet = null;
            p.Extras.Remove(slot);
            Vector2 at = exploded.GetPosition();
            Explosion explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 2f, 2f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 3.5f, 3.5f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            sound.PlayCue("expl2");
            bin.Remove((GameComponent)(object)exploded);
            Console.WriteLine("[net] friend ship died slot=" + slot);
        }

        // Called from PlayerShip.Update for ControlDevice.RemoteFriend ships (mirrors
        // DriveRemoteShip): resolve this ship's channel, then the shared DriveShip does the work.
        public static void DriveFriendShip(PlayerShip ship, GameTime gameTime)
        {
            if (!Active || peer == null)
            {
                return;
            }
            ShipChannel ch = null;
            foreach (ShipChannel candidate in peer.Extras.Values)
            {
                if (ReferenceEquals(candidate.Puppet, ship))
                {
                    ch = candidate;
                    break;
                }
            }
            // ADOPT a ship the scene spawned into this slot behind our back -- SpawnAllPlayers
            // respawns every seated slot after a death/checkpoint reset, puppet slots included.
            // Without this the re-spawned puppet matches no channel and freezes on its spawn pose
            // forever (the primary remote path has always adopted; this one didn't).
            if (ch == null && peer.Extras.TryGetValue((byte)ship.Owner, out ShipChannel bySlot))
            {
                bySlot.Puppet = ship;
                bySlot.RenderMs = double.NaN;
                ch = bySlot;
            }
            if (ch == null)
            {
                return;
            }
            DriveShip(ch, ship);
        }

        // Peer loss in a LISTED session (card 4d904410): the host keeps playing its own level, so
        // nothing purges the departed joiner's couch puppets -- without this they stay frozen on
        // screen in seats that never free, and oracle.Players never falls back for re-listing.
        // (Every other peer-loss path ends the match and the scene teardown does it.)
        private static void ReleaseAllFriendPuppets(PeerChannel p)
        {
            friendScratchSlots.Clear();
            foreach (byte slot in p.Extras.Keys)
            {
                friendScratchSlots.Add(slot);
            }
            foreach (byte slot in friendScratchSlots)
            {
                if (p.Extras.TryGetValue(slot, out ShipChannel ch) && ch.Puppet != null)
                {
                    ExplodeFriend(p, ch, slot);
                }
                else
                {
                    p.Extras.Remove(slot);
                    oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
                }
            }
        }

        // Session teardown / per-match reset: the TX half of the extra-ship stream. The RX half
        // (the channels themselves) dies with the PeerChannel -- ResetPerSessionState drops the
        // peer, ResetPerMatchState calls PeerChannel.ResetMatchState.
        private static void ResetFriendTx()
        {
            friendTxShots.Clear();
            friendTxSeq = 0;
        }

        // Session teardown: puppet COMPONENTS are torn down by the scene's own purge (like the
        // primary remote puppet); the channels go with the peer in ResetPerSessionState. This
        // clears the slot bookkeeping that is not per-peer.
        private static void ResetFriends()
        {
            if (peer != null)
            {
                foreach (ShipChannel ch in peer.Extras.Values)
                {
                    ch.Puppet = null;
                }
                peer.Extras.Clear();
            }
            ResetFriendTx();
        }
    }
}
