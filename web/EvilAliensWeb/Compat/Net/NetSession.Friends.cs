using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Host AI "friend" ship replication -- coverage-gaps follow-up to card 11.2.
    //
    // The "Mechanical Friends" cheat (Settings.Friends 1..3) fills empty slots with AI helper ships.
    // 11.2 DISABLED that in every net session because an AI friend's bullets would only exist on the
    // host (invisible on the client). The design doc's "host runs AI friends" is now REALISED: the
    // host does run them (real AI, real bullets, host-authoritative enemy kills that already
    // replicate), AND streams each one to the client, which shows it as a ControlDevice.RemoteFriend
    // puppet whose bullets re-fire locally -- exactly the single remote-ship scheme, generalised to
    // several ships keyed by slot.
    //
    // Design (deliberately ISOLATED from the working single-ship remote path so it can't regress it):
    //   * The client's auto-join stays OFF (GameScene) -- only the host adds AI friends; the client
    //     receives them. So this whole path is DORMANT unless the cheat is on: no friend streams are
    //     sent, friendChannels stays empty, and a default co-op session is byte-identical to before.
    //   * IDENTITY SLOT MAPPING: the client puppet lands in the SAME oracle slot the host runs the
    //     friend in, so per-slot score/lives (EvScoreSync sends them verbatim) line up. AddPlayerAt /
    //     RemovePlayerAt guard against ever squatting or freeing a live human/remote slot.
    //   * A friend that dies / leaves simply STOPS being streamed; a per-slot timeout explodes its
    //     puppet (no explicit death event needed), and a later stream re-spawns it.
    public static partial class NetSession
    {
        // Per host AI-friend slot: its own jitter buffer + interpolation clock (a copy of the primary
        // remote's, so the puppet is just as smooth) + latest fire state + the client-side puppet.
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

        private static readonly Dictionary<byte, FriendChannel> friendChannels = new Dictionary<byte, FriendChannel>();
        private static ushort friendTxSeq; // separate from txSeq so the primary stream's seq stays contiguous
        private static readonly List<byte> friendScratchSlots = new List<byte>(4);

        // ---- host: stream each live AI friend (called on the ship-stream cadence) --------------
        private static void SendFriendStates(long now)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                // Only the REAL AI friends. ?aiplayer forces the LOCAL ship's Update branch to AI but
                // leaves its Controller (Keyboard/pad), so it is streamed as the primary ship, not here.
                if (s.Controller != ControlDevice.AI)
                {
                    continue;
                }
                int slot = s.Owner;
                if (slot < 0 || slot > 255)
                {
                    continue;
                }
                bool firing = now - s.NetLastFireMs < FiringHoldMs;
                float aim = (firing || s.NetLastFireMs > 0) ? s.NetLastFireAim : 4.712389f;
                transport.SendStream(NetProtocol.EncodeFriendState((byte)slot, friendTxSeq++, (uint)(now - sessionStartAt),
                    s.GetPosition(), s.NetVelocity, aim, firing, s.NetShotsPerSec, s.NetBulletLife));
            }
        }

        // ---- client: receive -------------------------------------------------------------------
        private static void HandleFriendState(byte[] data)
        {
            if (isHost)
            {
                return; // a host never drives friend puppets -- it runs the real AI
            }
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
            ch.ShotsPerSec = shots;
            ch.BulletLife = life;
            ch.LastRxAt = NowMs;
            ch.Buffer.Add(sample);
        }

        // ---- client: per-tick puppet management + interpolation clock --------------------------
        private static void TickFriends()
        {
            if (isHost || friendChannels.Count == 0)
            {
                return;
            }
            long now = NowMs;
            long timeout = (RemotePaused || localPaused) ? PausedPeerTimeoutMs : FriendTimeoutMs;
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
                    if (ch.Puppet != null)
                    {
                        ExplodeFriend(ch, slot);
                    }
                    else
                    {
                        friendChannels.Remove(slot);
                        oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
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
            // Identity slot: seat the puppet in the SAME slot the host runs the friend in (score/lives
            // sync lines up). AddPlayerAt refuses a busy slot, so this never squats a human/remote ship.
            if (!oracle.AddPlayerAt(slot, ControlDevice.RemoteFriend))
            {
                return; // slot busy (host friends are the high slots, so this is not expected) -- retry
            }
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, ch.Buffer.Newest.Pos, startup: false, invulnerable: false, 4.712389f);
            bin.Add((GameComponent)(object)ship);
            ch.Puppet = ship;
            Console.WriteLine("[net] friend ship joined slot=" + slot);
        }

        // Mirror the death LOOK locally (explosions + cue) and free the slot. Never Die() -- that would
        // fire the local respawn-summon for a ship we don't own; a real respawn arrives as a new stream.
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
            oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
            Console.WriteLine("[net] friend ship died slot=" + slot);
        }

        // Called from PlayerShip.Update for ControlDevice.RemoteFriend ships (mirrors DriveRemoteShip):
        // position from this slot's interpolation buffer, shots re-fired locally from the fire state.
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
            if (ch == null || !ch.Buffer.HasSamples || double.IsNaN(ch.RenderMs))
            {
                return; // hold the spawn pose until the first sample lands
            }
            Vector2 pos = ch.Buffer.Sample(ch.RenderMs, out _);
            ShipSample newest = ch.Buffer.Newest;
            ship.NetApplyRemoteState(pos, newest.Aim, newest.Firing, ch.ShotsPerSec, ch.BulletLife);
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
            friendTxSeq = 0;
        }
    }
}
