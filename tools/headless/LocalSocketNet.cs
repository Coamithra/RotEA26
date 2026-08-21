// ---------------------------------------------------------------------------
// LocalSocketNet — the `eaNet` JS facade, backed by a localhost TCP socket instead of a
// BroadcastChannel, so TWO eahl PROCESSES can hold a real co-op session between them.
//
// WHY THIS AND NOT A FOURTH INetTransport (card 054947f3). `BroadcastChannelTransport` already
// IS the project's instant-local transport; what it lacked headlessly was a backend -- eahl
// stubbed `eaNet.open/send/close` as no-ops, so a `?net=` boot opened a channel nobody was on
// the other end of. Backing those three calls is therefore the whole job: nothing under
// `INetTransport` changes, nothing shipped changes, and every existing two-tab `?net=` recipe
// becomes two-process-runnable for free.
//
// WHY TWO PROCESSES AT ALL. One process cannot hold two worlds -- `ComponentBin`'s only ctor
// binds `game.Components`, `Oracle`/`CollisionHandler` bind that same collection, and
// `ServiceHelper` is a process-global registry (net CLAUDE.md, "TWO PEERS WITH INDEPENDENT
// WORLDS IN ONE PROCESS IS UNREACHABLE"). So a genuine host-vs-client world diff needs two.
//
// THIS FILE IS eahl-ONLY. It lives under tools/headless/ deliberately: `System.Net.Sockets` is
// meaningless in browser-wasm, and putting it in Compat/ would ship a type that can only throw.
//
// SOCKET REUSE, stated explicitly because the soak kills and respawns the CLIENT process at
// every join. The HOST binds the listener ONCE and keeps it for the process lifetime, and
// `Close()` deliberately does NOT stop it -- that call is `eaNet.close`, which a listed host
// reaches on every match teardown (`NetSession.Stop`), so stopping the listener there would
// rebind it on the next re-arm and expose the port to a lingering TIME_WAIT. It serves up to
// `--net-peers` clients at once (card 583a3ef8; DEFAULT 1, which is exactly the old
// one-peer-at-a-time behaviour every existing rig was written against): when a peer's socket
// closes its bye is raised and the listener is still accepting, so join N+1 connects to the
// same port with no teardown at all. The CLIENT only ever dials out on an ephemeral port, which
// is the side that may cycle freely. A client arriving OVER capacity is refused and CLOSED
// rather than queued -- silently holding an extra socket open would make a rig bug look like a
// network stall. Accepted peers get monotone ids ("peer1", "peer2", ...) that are never reused
// in a process, mirroring the signal server's no-reuse rule; the dialling side's one remote
// keeps the legacy id "peer". `Shutdown()` is the process-exit door, and nothing in a run
// calls it.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EvilAliensWeb.Compat.Net;

namespace EvilAliensWeb.Headless
{
    internal static class LocalSocketNet
    {
        // Frame header: [lane:1][len:4 little-endian], then `len` bytes of the base64 payload
        // string the eaNet facade already deals in on both sides. Length-prefixed because TCP is
        // a byte stream: without it two sends coalesce and the far end decodes garbage.
        private const int HeaderBytes = 5;
        private const byte LaneStream = 0;
        private const byte LaneReliable = 1;

        // How long a dialling client keeps retrying before giving up. The orchestrator starts the
        // host first, but process startup is not instantaneous and a fixed sleep would either be
        // slow or flaky; this is a bounded retry instead. Wall clock, not game time -- it covers
        // process startup, which no virtual clock knows about.
        private const int ConnectTimeoutMs = 15000;

        private sealed class PeerSock
        {
            internal Socket Socket;
            internal string Id;
            internal readonly List<byte> Rx = new List<byte>(4096);
        }

        private static bool _enabled;
        private static bool _listen;
        private static int _port;
        private static string _room;
        private static TcpListener _listener;
        private static readonly List<PeerSock> _peers = new List<PeerSock>();
        private static readonly byte[] _scratch = new byte[8192];
        private static int _portOverride;
        private static int _maxPeers = 1;
        private static int _nextPeerNo = 1;   // monotone per process, never reused

        internal static bool Enabled => _enabled;
        internal static int Port => _port;
        internal static bool PeerConnected
        {
            get
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    if (_peers[i].Socket != null && _peers[i].Socket.Connected)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // Called from Program's argument parsing; 0 = derive the port from the room name.
        internal static void SetPortOverride(int port) => _portOverride = port;

        // --net-peers: how many clients the listening side serves at once. Clamped 1..3 (the
        // 4-machine ceiling is host + 3); 1 is the old behaviour and the default. The clamp is
        // REPORTED -- a run silently serving a different peer count than it was asked for is
        // exactly the mislabelled-measurement class the repo's flag rules exist for.
        internal static void SetMaxPeers(int max)
        {
            _maxPeers = Math.Max(1, Math.Min(3, max));
            if (_maxPeers != max)
            {
                Log("--net-peers " + max + " is out of range -- clamped to " + _maxPeers + " (1..3)");
            }
        }

        // Which end dials. Derived from the boot role rather than from who opened first, so the
        // two processes cannot both try to bind (or both try to connect) when a race decides the
        // order. ?net=host / ?net=jiphost listen; ?net=join / ?net=jipjoin dial.
        internal static void ConfigureFromRole(Compat.NetRole role)
        {
            switch (role)
            {
            case Compat.NetRole.Host:
            case Compat.NetRole.JipHost:
                _enabled = true;
                _listen = true;
                break;
            case Compat.NetRole.Join:
            case Compat.NetRole.JipJoin:
                _enabled = true;
                _listen = false;
                break;
            default:
                _enabled = false;
                break;
            }
        }

        // The port BOTH processes must agree on with no extra configuration, so the orchestrator
        // only has to pass one matching `?room=` to each. FNV-1a over the room name into the
        // IANA dynamic range; --net-port overrides it when a box has something squatting there.
        internal static int PortForRoom(string room)
        {
            if (_portOverride > 0)
            {
                return _portOverride;
            }
            uint h = 2166136261u;
            foreach (char c in room ?? "")
            {
                h = (h ^ c) * 16777619u;
            }
            return 49152 + (int)(h % 12000u);
        }

        // ---- the three eaNet calls -----------------------------------------------------------

        internal static void Open(string room)
        {
            if (!_enabled || _listener != null || _peers.Count > 0)
            {
                return;
            }
            _room = room ?? "dev";
            _port = PortForRoom(_room);
            if (_listen)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, _port);
                    _listener.Start(2);
                    Log("listening on 127.0.0.1:" + _port + " (room '" + _room + "')");
                }
                catch (SocketException ex)
                {
                    // Reported and survived rather than thrown. This box runs up to eight
                    // worktree agents, so two runs sharing a room name is a real way to collide
                    // -- and killing the game over it would turn a port clash into a mysterious
                    // dead probe. The peer simply never arrives, and net_jip_sync reports that
                    // as "the host never paired", with this line above it naming the cause.
                    _listener = null;
                    Log("COULD NOT BIND 127.0.0.1:" + _port + " (room '" + _room + "'): "
                        + ex.SocketErrorCode + " -- no peer can reach this host. Another run on "
                        + "the same room? Use a different ?room= or --net-port.");
                }
            }
            else
            {
                // Announced BEFORE the loop: this blocks Game1.Initialize for up to
                // ConnectTimeoutMs, and a mistyped ?room= or a host that never came up otherwise
                // looks like a silent 15-second hang.
                Log("dialling 127.0.0.1:" + _port + " (room '" + _room + "')...");
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < ConnectTimeoutMs)
                {
                    var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        s.NoDelay = true;
                        s.Connect(IPAddress.Loopback, _port);
                        _peers.Add(new PeerSock { Socket = s, Id = "peer" });
                        break;
                    }
                    catch (SocketException)
                    {
                        // DISPOSED on the way round: ~300 attempts against a dead port is 300
                        // leaked handles, and dialling a dead port is exactly what
                        // net_jip_sync's --no-pair vacuity control does on every level.
                        s.Dispose();
                        System.Threading.Thread.Sleep(50);
                    }
                }
                if (_peers.Count == 0)
                {
                    Log("FAILED to reach 127.0.0.1:" + _port + " (room '" + _room + "') after "
                        + ConnectTimeoutMs + "ms -- is the host process up?");
                }
                else
                {
                    Log("connected to 127.0.0.1:" + _port + " (room '" + _room + "')");
                }
            }
        }

        // `to` null/empty = every connected peer (the eaNet broadcast); a peer id = only the
        // matching socket (the addressed-send plumbing, card 583a3ef8). An unknown id is a
        // silent drop, per the INetTransport contract.
        internal static void Send(string b64, bool reliable, string to = null)
        {
            if (_peers.Count == 0 || string.IsNullOrEmpty(b64))
            {
                return;
            }
            byte[] body = Encoding.ASCII.GetBytes(b64);
            byte[] frame = new byte[HeaderBytes + body.Length];
            frame[0] = reliable ? LaneReliable : LaneStream;
            frame[1] = (byte)(body.Length & 0xFF);
            frame[2] = (byte)((body.Length >> 8) & 0xFF);
            frame[3] = (byte)((body.Length >> 16) & 0xFF);
            frame[4] = (byte)((body.Length >> 24) & 0xFF);
            Buffer.BlockCopy(body, 0, frame, HeaderBytes, body.Length);
            // Backwards over a list DropPeer removes from, and re-checked per peer: a send
            // failure mid-loop must not skip the remaining peers.
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                PeerSock p = _peers[i];
                if (p.Socket == null || !p.Socket.Connected)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(to) && p.Id != to)
                {
                    continue;
                }
                try
                {
                    int sent = 0;
                    while (sent < frame.Length)
                    {
                        sent += p.Socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                    }
                }
                catch (SocketException)
                {
                    DropPeer(p);
                }
            }
        }

        // eaNet.close -- the SESSION is over, not the process. A listed host reaches this on every
        // match teardown and then re-arms, so the listener stays up (see the header); only the
        // peer sockets go.
        internal static void Close()
        {
            DropAllPeers();
        }

        // Process exit. Unused by the game -- kept so the listener has a defined door out and the
        // asymmetry with Close() is a decision rather than an omission.
        internal static void Shutdown()
        {
            DropAllPeers();
            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
            }
        }

        // ---- the per-frame pump ---------------------------------------------------------------

        // Called once per frame from HeadlessGame.UpdateFrame, BEFORE Update -- so a frame the
        // peer sent is in NetSession's rx queue by the time NetSession.Update drains it, exactly
        // as the browser's JS callback delivers ahead of the tick. One bool test when unused.
        internal static void Pump()
        {
            if (!_enabled)
            {
                return;
            }
            AcceptIfWaiting();
            // Backwards: a dead socket is dropped (removed) mid-loop. The bound is re-checked
            // per iteration because PumpPeer delivers into game code synchronously, and a
            // handler that reaches eaNet.close drops EVERY peer (DropAllPeers) -- an index
            // taken before that would throw.
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                if (i < _peers.Count)
                {
                    PumpPeer(_peers[i]);
                }
            }
        }

        private static void PumpPeer(PeerSock p)
        {
            try
            {
                while (p.Socket.Available > 0)
                {
                    int n = p.Socket.Receive(_scratch, 0, Math.Min(_scratch.Length, p.Socket.Available), SocketFlags.None);
                    if (n <= 0)
                    {
                        DropPeer(p);
                        return;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        p.Rx.Add(_scratch[i]);
                    }
                }
                // A graceful close reads as "readable, zero bytes". Available == 0 with the
                // socket still polling readable is exactly that, and is how a killed client
                // process is noticed at all -- there is no pagehide out here.
                if (p.Socket.Poll(0, SelectMode.SelectRead) && p.Socket.Available == 0)
                {
                    DropPeer(p);
                    return;
                }
            }
            catch (SocketException)
            {
                DropPeer(p);
                return;
            }
            DeliverComplete(p);
        }

        private static void AcceptIfWaiting()
        {
            while (_listener != null && _listener.Pending())
            {
                Socket s = _listener.AcceptSocket();
                s.NoDelay = true;
                if (_peers.Count >= _maxPeers)
                {
                    // See the socket-reuse note in the file header: refuse rather than queue.
                    Log("refused peer " + (_peers.Count + 1) + " -- capacity is " + _maxPeers
                        + " (raise with --net-peers)");
                    s.Close();
                    continue;
                }
                var p = new PeerSock { Socket = s, Id = "peer" + _nextPeerNo++ };
                _peers.Add(p);
                Log("peer connected (" + p.Id + ")");
            }
        }

        private static void DeliverComplete(PeerSock p)
        {
            List<byte> rx = p.Rx;
            while (rx.Count >= HeaderBytes)
            {
                int len = rx[1] | (rx[2] << 8) | (rx[3] << 16) | (rx[4] << 24);
                if (len < 0 || rx.Count < HeaderBytes + len)
                {
                    return;
                }
                bool reliable = rx[0] == LaneReliable;
                var body = new char[len];
                for (int i = 0; i < len; i++)
                {
                    body[i] = (char)rx[HeaderBytes + i];
                }
                rx.RemoveRange(0, HeaderBytes + len);
                // The SAME entry point the browser's JS callback uses, so the frame reaches
                // BroadcastChannelTransport through production code rather than a side door.
                NetInterop.Data(new string(body), reliable, p.Id);
            }
        }

        private static void DropPeer(PeerSock p)
        {
            if (!_peers.Remove(p))
            {
                return;
            }
            try { p.Socket.Close(); } catch (SocketException) { }
            p.Rx.Clear();
            Log("peer disconnected (" + p.Id + ")");
            // The transport contract's peer-bye. On the host this is what ends a match when the
            // orchestrator kills the client process between joins; the listener stays up, so the
            // next client connects with nothing to reset.
            NetInterop.PeerBye(p.Id);
        }

        private static void DropAllPeers()
        {
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                DropPeer(_peers[i]);
            }
        }

        private static void Log(string s) => Console.WriteLine("[eahl] net      " + s);
    }
}
