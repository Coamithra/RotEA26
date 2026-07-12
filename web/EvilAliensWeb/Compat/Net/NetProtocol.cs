using System;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The 3-layer wire protocol (plans/stage11-online-coop.md), little-endian binary:
    //   1. Ship stream (unreliable lane, ~30 Hz): MsgShipState -- each peer's own ship
    //      STATE (pos, velocity, last aim, fire/alive flags, fire-rate loadout). The wire
    //      carries state, never inputs.
    //   2. World snapshot (host -> clients, unreliable lane): MsgWorldSnapshot -- type
    //      RESERVED, encode/decode stubbed until card 11.3 (host world authority).
    //   3. Events (reliable lane): MsgHello/MsgWelcome handshake, MsgEvent envelope with a
    //      monotonically increasing sequence (EvSpawn/EvDeath from the host's NetIdRegistry,
    //      EvBlast from either peer).
    public static class NetProtocol
    {
        public const byte MsgHello = 0x01;
        public const byte MsgWelcome = 0x02;
        public const byte MsgShipState = 0x10;
        public const byte MsgWorldSnapshot = 0x20; // reserved -- card 11.3
        public const byte MsgEvent = 0x30;

        public const byte EvSpawn = 1;
        public const byte EvDeath = 2;
        public const byte EvBlast = 3;

        public const byte ShipFlagAlive = 1 << 0;
        public const byte ShipFlagFiring = 1 << 1;

        // ---- ship stream --------------------------------------------------------------

        // [type][flags][shotsPerSec][bulletLife/10][seq:2][senderMs:4][posX:4][posY:4]
        // [velX:4][velY:4][aim:4] = 31 bytes. Velocity is design px per MILLISECOND (the
        // component system's native unit, see AlienDrawableGameComponent.Update).
        // senderMs is SESSION-RELATIVE (uint ms since the sender's NetSession.Start) --
        // an absolute machine-uptime tick in float32 loses ms precision within hours.
        public static byte[] EncodeShipState(ushort seq, uint senderMs, Vector2 pos, Vector2 vel, float aim, bool alive, bool firing, int shotsPerSec, float bulletLife)
        {
            byte[] b = new byte[31];
            b[0] = MsgShipState;
            b[1] = (byte)((alive ? ShipFlagAlive : 0) | (firing ? ShipFlagFiring : 0));
            b[2] = (byte)Math.Clamp(shotsPerSec, 1, 255);
            b[3] = (byte)Math.Clamp((int)(bulletLife / 10f), 0, 255);
            WriteU16(b, 4, seq);
            WriteU32(b, 6, senderMs);
            WriteF32(b, 10, pos.X);
            WriteF32(b, 14, pos.Y);
            WriteF32(b, 18, vel.X);
            WriteF32(b, 22, vel.Y);
            WriteF32(b, 26, aim);
            return b;
        }

        public static bool TryDecodeShipState(byte[] b, out ushort seq, out ShipSample sample, out int shotsPerSec, out float bulletLife)
        {
            seq = 0;
            sample = default;
            shotsPerSec = 8;
            bulletLife = 450f;
            if (b.Length < 31 || b[0] != MsgShipState)
            {
                return false;
            }
            shotsPerSec = b[2];
            bulletLife = b[3] * 10f;
            seq = ReadU16(b, 4);
            sample.T = ReadU32(b, 6);
            sample.Pos = new Vector2(ReadF32(b, 10), ReadF32(b, 14));
            sample.Vel = new Vector2(ReadF32(b, 18), ReadF32(b, 22));
            sample.Aim = ReadF32(b, 26);
            sample.Alive = (b[1] & ShipFlagAlive) != 0;
            sample.Firing = (b[1] & ShipFlagFiring) != 0;
            return true;
        }

        // ---- handshake ----------------------------------------------------------------

        // [type][protocolVersion][isHost]
        public static byte[] EncodeHello(byte protocolVersion, bool isHost)
        {
            return new byte[] { MsgHello, protocolVersion, (byte)(isHost ? 1 : 0) };
        }

        public static byte[] EncodeWelcome(byte protocolVersion, bool isHost)
        {
            return new byte[] { MsgWelcome, protocolVersion, (byte)(isHost ? 1 : 0) };
        }

        // ---- reliable events ------------------------------------------------------------

        // Common envelope: [MsgEvent][eventType][eventSeq:2] then the per-type payload.
        private static byte[] EventHeader(byte eventType, ushort eventSeq, int payloadBytes)
        {
            byte[] b = new byte[4 + payloadBytes];
            b[0] = MsgEvent;
            b[1] = eventType;
            WriteU16(b, 2, eventSeq);
            return b;
        }

        // EvSpawn: [netId:2][typeHash:4]
        public static byte[] EncodeSpawnEvent(ushort eventSeq, ushort netId, uint typeHash)
        {
            byte[] b = EventHeader(EvSpawn, eventSeq, 6);
            WriteU16(b, 4, netId);
            WriteU32(b, 6, typeHash);
            return b;
        }

        // EvDeath: [netId:2]
        public static byte[] EncodeDeathEvent(ushort eventSeq, ushort netId)
        {
            byte[] b = EventHeader(EvDeath, eventSeq, 2);
            WriteU16(b, 4, netId);
            return b;
        }

        // EvBlast: [posX:4][posY:4][level]
        public static byte[] EncodeBlastEvent(ushort eventSeq, Vector2 pos, int level)
        {
            byte[] b = EventHeader(EvBlast, eventSeq, 9);
            WriteF32(b, 4, pos.X);
            WriteF32(b, 8, pos.Y);
            b[12] = (byte)Math.Clamp(level, 0, 255);
            return b;
        }

        // Stable name hash for spawn events (FNV-1a over the component type name). 11.3
        // replaces this with a real replicable-type registry keyed to New*+Setup factories.
        public static uint TypeHash(string typeName)
        {
            uint h = 2166136261u;
            foreach (char c in typeName)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        // ---- primitives -----------------------------------------------------------------

        private static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        private static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        private static void WriteF32(byte[] b, int o, float v)
        {
            WriteU32(b, o, (uint)BitConverter.SingleToInt32Bits(v));
        }

        public static ushort ReadU16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        public static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        public static float ReadF32(byte[] b, int o)
        {
            return BitConverter.Int32BitsToSingle((int)ReadU32(b, o));
        }
    }

    // One received ship-stream sample. T is the SENDER's session-relative millisecond
    // clock -- the interpolation render clock is derived from it (newest - delay), so
    // peers' clocks never need to agree, only each sender's needs to be monotonic.
    // Double so long sessions never lose ms precision in the buffer math.
    public struct ShipSample
    {
        public double T;
        public Vector2 Pos;
        public Vector2 Vel; // design px per ms
        public float Aim;
        public bool Alive;
        public bool Firing;
    }
}
