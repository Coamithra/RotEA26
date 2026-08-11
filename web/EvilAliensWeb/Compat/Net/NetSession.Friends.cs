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
    // Design (deliberately ISOLATED from the single-ship primary path so it can't regress it):
    //   * IDENTITY SLOT MAPPING: the puppet lands in the SAME oracle slot its owner runs it in, so
    //     per-slot score/lives (EvScoreSync sends them verbatim) line up. Since card 4d904410 the
    //     host allocates every slot, so this holds for the primaries too and there is no
    //     host-relative translation left anywhere. AddPlayerAt / RemovePlayerAt still guard against
    //     ever squatting or freeing a live human/remote slot.
    //   * A ship that dies / leaves simply STOPS being streamed; a per-slot timeout explodes its
    //     puppet (no explicit death event needed), and a later stream re-spawns it.
    //   * With no AI friends and no couch players the path stays DORMANT: nothing is streamed and
    //     friendChannels stays empty, so a plain two-player session is unchanged.
    public static partial class NetSession
    {
        // Per replicated extra ship: its own jitter buffer + interpolation clock (a copy of the
        // primary remote's, so the puppet is just as smooth) + latest fire state + the puppet.
        private sealed class FriendChannel
        {
            public readonly ShipStateBuffer Buffer = new ShipStateBuffer();
            public double RenderMs = double.NaN;
            public int ShotsPerSec = 8;
            public float BulletLife = 450f;
            public long LastRxAt;
            public PlayerShip Puppet;
        }

        // No sample for this long -> the friend died / left / the level ended: explode its puppet.
        // Generous vs the 33 ms stream cadence (~15 missed packets) so a transient gap never flickers
        // it; stretched to the paused backstop while either side holds a pause (the stream stalls then).
        private const long FriendTimeoutMs = 500;

        // TX side, one per slot we STREAM: the slot's wire shot count, plus which ship it was last
        // read from. Separate from FriendChannel, which is the RX half -- a peer both sends and
        // receives friend states, and the two halves are about different slots.
        // See NetSession's lastTxShotCount comment for why the count has to belong to the slot
        // rather than to the ship: a couch player's ship dies and respawns inside the 500 ms
        // FriendTimeoutMs, so the puppet (and its baseline) survives the counter restarting at 0.
        private sealed class FriendTxShots
        {
            public PlayerShip Ship;
            public byte ShipShots;
            public byte WireCount;
        }

        private static readonly Dictionary<byte, FriendTxShots> friendTxShots = new Dictionary<byte, FriendTxShots>();

        private static readonly Dictionary<byte, FriendChannel> friendChannels = new Dictionary<byte, FriendChannel>();
        private static ushort friendTxSeq; // separate from txSeq so the primary stream's seq stays contiguous
        private static readonly List<byte> friendScratchSlots = new List<byte>(4);

        // ---- stream each live extra ship we own (called on the ship-stream cadence) ------------
        private static void SendFriendStates(long now)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                // Everything we simulate except our primary (which rides MsgShipState): AI friends
                // and couch players. ?aiplayer forces the LOCAL ship's Update branch to AI but
                // leaves its Controller (Keyboard/pad), so the primary is still excluded by slot.
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
                // a45b78f6) -- DriveFriendShip feeds the identical NetApplyRemoteState, so a
                // couch player's tap and an AI friend's burst are replicated shot for shot.
                if (!friendTxShots.TryGetValue((byte)slot, out FriendTxShots tx))
                {
                    tx = new FriendTxShots();
                    friendTxShots[(byte)slot] = tx;
                }
                byte shotCount = AdvanceTxShots(s, ref tx.Ship, ref tx.ShipShots, ref tx.WireCount);
                transport.SendStream(NetProtocol.EncodeFriendState((byte)slot, friendTxSeq++, (uint)(now - sessionStartAt),
                    s.GetPosition(), s.NetVelocity, s.NetLastFireAim, shotCount, s.NetShotsPerSec, s.NetBulletLife,
                    s.NetAsplodeBits, s.NetBounceBits));
            }
        }

        // Does the extra ship streamed for `slot` have an adopted puppet? The SpawnFriend half of
        // NetResetSpawnTest's subject -- same reasoning as HasRemotePuppet, and this is the site
        // that bites more often, since couch players hit the resets that arm Purge<PlayerShip>.
        internal static bool HasFriendPuppet(byte slot)
        {
            return friendChannels.TryGetValue(slot, out FriendChannel ch) && ch.Puppet != null;
        }

        // Is this slot actively streamed to us? (Host-side grant bookkeeping asks.)
        private static bool FriendChannelExists(byte slot)
        {
            return friendChannels.ContainsKey(slot);
        }

        // ---- receive (either role) --------------------------------------------------------------
        private static void HandleFriendState(byte[] data)
        {
            if (!NetProtocol.TryDecodeFriendState(data, out byte slot, out _, out ShipSample sample, out int shots, out float life))
            {
                return;
            }
            lastRxStreamAt = NowMs; // friend traffic is also proof the peer is alive
            if (!PeerUp)
            {
                PeerConnected();
            }
            if (!friendChannels.TryGetValue(slot, out FriendChannel ch))
            {
                ch = new FriendChannel();
                friendChannels[slot] = ch;
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
            if (ch.Buffer.HasSamples && NowMs - ch.LastRxAt > FriendTimeoutMs)
            {
                ch.Buffer.Clear();
                ch.RenderMs = double.NaN;
            }
            ch.ShotsPerSec = shots;
            ch.BulletLife = life;
            ch.LastRxAt = NowMs;
            ch.Buffer.Add(sample);
            grantsAwaitingStream.Remove(slot); // the peer took the grant -- stop the claim clock
        }

        // ---- per-tick puppet management + interpolation clock (either role) --------------------
        private static void TickFriends()
        {
            if (friendChannels.Count == 0)
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
            // PeerStalled flag only arms at PeerStallMs (1200 ms) of total stream silence, and
            // every channel's own 500 ms boundary is crossed FIRST -- so before this arm, the
            // "stalled" timeout was structurally unreachable and a 0.5-1.2 s hiccup exploded
            // every couch/AI-friend puppet, against this very comment's stated intent. The whole
            // link being quiet past the channel's own threshold is a hiccup, not a ship death (a
            // death stops ONE slot's stream while the primary heartbeat keeps flowing), so it is
            // recognised at the same 500 ms the channel itself times out at.
            long timeout = (RemotePaused || localPaused) ? PausedPeerTimeoutMs
                : (PeerStalled || now - lastRxStreamAt > FriendTimeoutMs) ? PeerTimeoutMs + PeerGraceMs
                : FriendTimeoutMs;
            friendScratchSlots.Clear();
            foreach (byte slot in friendChannels.Keys)
            {
                friendScratchSlots.Add(slot);
            }
            foreach (byte slot in friendScratchSlots)
            {
                if (!friendChannels.TryGetValue(slot, out FriendChannel ch))
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
                        ExplodeFriend(ch, slot);
                    }
                    else
                    {
                        friendChannels.Remove(slot);
                    }
                    continue;
                }
                AdvanceFriendClock(ch);
                if (ch.Puppet == null && PeerUp && ch.Buffer.HasSamples && FindLocalShip() != null)
                {
                    SpawnFriend(ch, slot);
                }
            }
        }

        // Mirrors AdvanceRenderClock (the primary remote): render ~InterpDelayMs behind the newest
        // sample on REAL time, softly servoing, snapping on a big error.
        private static void AdvanceFriendClock(FriendChannel ch)
        {
            if (!ch.Buffer.HasSamples)
            {
                ch.RenderMs = double.NaN;
                return;
            }
            double target = ch.Buffer.NewestMs - InterpDelayMs;
            if (double.IsNaN(ch.RenderMs))
            {
                ch.RenderMs = target;
                return;
            }
            ch.RenderMs += realDtMs;
            double err = target - ch.RenderMs;
            if (Math.Abs(err) > RenderClockSnapMs)
            {
                ch.RenderMs = target;
            }
            else
            {
                ch.RenderMs += err * 0.1;
            }
        }

        private static void SpawnFriend(FriendChannel ch, byte slot)
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
        private static void ExplodeFriend(FriendChannel ch, byte slot)
        {
            PlayerShip p = ch.Puppet;
            ch.Puppet = null;
            friendChannels.Remove(slot);
            Vector2 at = p.GetPosition();
            Explosion explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 2f, 2f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 3.5f, 3.5f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            sound.PlayCue("expl2");
            bin.Remove((GameComponent)(object)p);
            Console.WriteLine("[net] friend ship died slot=" + slot);
        }

        // Called from PlayerShip.Update for ControlDevice.RemoteFriend ships (mirrors DriveRemoteShip):
        // position from this slot's interpolation buffer, shots respawned locally from the newest
        // sample's cumulative shot count.
        public static void DriveFriendShip(PlayerShip ship, GameTime gameTime)
        {
            if (!Active)
            {
                return;
            }
            FriendChannel ch = null;
            foreach (FriendChannel candidate in friendChannels.Values)
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
            if (ch == null && friendChannels.TryGetValue((byte)ship.Owner, out FriendChannel bySlot))
            {
                bySlot.Puppet = ship;
                bySlot.RenderMs = double.NaN;
                ch = bySlot;
            }
            if (ch == null || !ch.Buffer.HasSamples || double.IsNaN(ch.RenderMs))
            {
                return; // hold the spawn pose until the first sample lands
            }
            Vector2 pos = ch.Buffer.Sample(ch.RenderMs, out _);
            ShipSample newest = ch.Buffer.Newest;
            ship.NetApplyRemoteState(pos, newest.Aim, newest.ShotCount, ch.ShotsPerSec, ch.BulletLife, newest.AsplodeBits, newest.BounceBits);
        }

        // Peer loss in a LISTED session (card 4d904410): the host keeps playing its own level, so
        // nothing purges the departed joiner's couch puppets -- without this they stay frozen on
        // screen in seats that never free, and oracle.Players never falls back for re-listing.
        // (Every other peer-loss path ends the match and the scene teardown does it.)
        private static void ReleaseAllFriendPuppets()
        {
            friendScratchSlots.Clear();
            foreach (byte slot in friendChannels.Keys)
            {
                friendScratchSlots.Add(slot);
            }
            foreach (byte slot in friendScratchSlots)
            {
                if (friendChannels.TryGetValue(slot, out FriendChannel ch) && ch.Puppet != null)
                {
                    ExplodeFriend(ch, slot);
                }
                else
                {
                    friendChannels.Remove(slot);
                    oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
                }
            }
        }

        // Session teardown: puppet COMPONENTS are torn down by the scene's own purge (like the primary
        // remote puppet); this just drops the channels + slot bookkeeping so nothing dangles.
        private static void ResetFriends()
        {
            foreach (FriendChannel ch in friendChannels.Values)
            {
                ch.Puppet = null;
            }
            friendChannels.Clear();
            friendTxShots.Clear();
            friendTxSeq = 0;
        }
    }
}
