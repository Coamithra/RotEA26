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
// rebind it on the next re-arm and expose the port to a lingering TIME_WAIT. It serves ONE peer
// at a time: when that peer's socket closes, the peer-bye is raised and the listener is still
// accepting, so join N+1 connects to the same port with no teardown at all. The CLIENT only ever
// dials out on an ephemeral port, which is the side that may cycle freely. A second client
// arriving while one is connected is refused and CLOSED rather than queued: the protocol is
// 2-peer, and silently holding a third socket open would make a rig bug look like a network
// stall. `Shutdown()` is the process-exit door, and nothing in a run calls it.
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

        private static bool _enabled;
        private static bool _listen;
        private static int _port;
        private static string _room;
        private static TcpListener _listener;
        private static Socket _peer;
        private static readonly List<byte> _rx = new List<byte>(4096);
        private static readonly byte[] _scratch = new byte[8192];
        private static int _portOverride;

        internal static bool Enabled => _enabled;
        internal static int Port => _port;
        internal static bool PeerConnected => _peer != null && _peer.Connected;

        // Called from Program's argument parsing; 0 = derive the port from the room name.
        internal static void SetPortOverride(int port) => _portOverride = port;

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
            if (!_enabled || _listener != null || _peer != null)
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
                        _peer = s;
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
                if (_peer == null)
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

        internal static void Send(string b64, bool reliable)
        {
            if (_peer == null || !_peer.Connected || string.IsNullOrEmpty(b64))
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
            try
            {
                int sent = 0;
                while (sent < frame.Length)
                {
                    sent += _peer.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                }
            }
            catch (SocketException)
            {
                DropPeer();
            }
        }

        // eaNet.close -- the SESSION is over, not the process. A listed host reaches this on every
        // match teardown and then re-arms, so the listener stays up (see the header); only the
        // peer socket goes.
        internal static void Close()
        {
            DropPeer();
        }

        // Process exit. Unused by the game -- kept so the listener has a defined door out and the
        // asymmetry with Close() is a decision rather than an omission.
        internal static void Shutdown()
        {
            DropPeer();
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
            if (_peer == null)
            {
                return;
            }
            try
            {
                while (_peer.Available > 0)
                {
                    int n = _peer.Receive(_scratch, 0, Math.Min(_scratch.Length, _peer.Available), SocketFlags.None);
                    if (n <= 0)
                    {
                        DropPeer();
                        return;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        _rx.Add(_scratch[i]);
                    }
                }
                // A graceful close reads as "readable, zero bytes". Available == 0 with the
                // socket still polling readable is exactly that, and is how a killed client
                // process is noticed at all -- there is no pagehide out here.
                if (_peer.Poll(0, SelectMode.SelectRead) && _peer.Available == 0)
                {
                    DropPeer();
                    return;
                }
            }
            catch (SocketException)
            {
                DropPeer();
                return;
            }
            DeliverComplete();
        }

        private static void AcceptIfWaiting()
        {
            if (_listener == null || !_listener.Pending())
            {
                return;
            }
            Socket s = _listener.AcceptSocket();
            s.NoDelay = true;
            if (_peer != null)
            {
                // See the socket-reuse note in the file header: refuse rather than queue.
                Log("refused a second peer -- the protocol is 2-peer and one is already connected");
                s.Close();
                return;
            }
            _peer = s;
            _rx.Clear();
            Log("peer connected");
        }

        private static void DeliverComplete()
        {
            while (_rx.Count >= HeaderBytes)
            {
                int len = _rx[1] | (_rx[2] << 8) | (_rx[3] << 16) | (_rx[4] << 24);
                if (len < 0 || _rx.Count < HeaderBytes + len)
                {
                    return;
                }
                bool reliable = _rx[0] == LaneReliable;
                var body = new char[len];
                for (int i = 0; i < len; i++)
                {
                    body[i] = (char)_rx[HeaderBytes + i];
                }
                _rx.RemoveRange(0, HeaderBytes + len);
                // The SAME entry point the browser's JS callback uses, so the frame reaches
                // BroadcastChannelTransport through production code rather than a side door.
                NetInterop.Data(new string(body), reliable, "peer");
            }
        }

        private static void DropPeer()
        {
            if (_peer == null)
            {
                return;
            }
            try { _peer.Close(); } catch (SocketException) { }
            _peer = null;
            _rx.Clear();
            Log("peer disconnected");
            // The transport contract's peer-bye. On the host this is what ends a match when the
            // orchestrator kills the client process between joins; the listener stays up, so the
            // next client connects with nothing to reset.
            NetInterop.PeerBye("peer");
        }

        private static void Log(string s) => Console.WriteLine("[eahl] net      " + s);
    }
}
