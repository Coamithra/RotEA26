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
        public const byte EvClaim = 4;
        public const byte EvScoreSync = 5;

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

        // EvSpawn v2: [netId:2][typeIdx:1][base:24][extraLen:1][spawnExtra:N] -- carries the
        // full base state so a client can construct + place the puppet from the event alone
        // (snapshots are unreliable-lane and may lag the spawn).
        public static byte[] EncodeSpawnEvent(ushort eventSeq, ushort netId, byte typeIdx, in NetBaseState state, byte[] extra, int extraLen)
        {
            byte[] b = EventHeader(EvSpawn, eventSeq, 4 + BaseStateBytes + extraLen);
            WriteU16(b, 4, netId);
            b[6] = typeIdx;
            int off = 7;
            WriteBaseState(b, ref off, state);
            b[off++] = (byte)extraLen;
            for (int i = 0; i < extraLen; i++)
            {
                b[off++] = extra[i];
            }
            return b;
        }

        public static bool TryDecodeSpawnEvent(byte[] b, out ushort netId, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen)
        {
            netId = 0;
            typeIdx = 0;
            state = default;
            extraOff = 0;
            extraLen = 0;
            if (b.Length < 8 + BaseStateBytes)
            {
                return false;
            }
            netId = ReadU16(b, 4);
            typeIdx = b[6];
            int off = 7;
            ReadBaseState(b, ref off, ref state);
            extraLen = b[off++];
            extraOff = off;
            return b.Length >= extraOff + extraLen;
        }

        // EvDeath v2: [netId:2][killerSlot:1 (KillerNone = despawn/off-screen)][posX:4][posY:4]
        // [points:2] -- killer/pos/points let the receiver pay death FX + generous score even
        // when its local copy is already gone.
        public const byte KillerNone = 0xFF;

        public static byte[] EncodeDeathEvent(ushort eventSeq, ushort netId, byte killerSlot, Vector2 pos, ushort points)
        {
            byte[] b = EventHeader(EvDeath, eventSeq, 13);
            WriteU16(b, 4, netId);
            b[6] = killerSlot;
            WriteF32(b, 7, pos.X);
            WriteF32(b, 11, pos.Y);
            WriteU16(b, 15, points);
            return b;
        }

        // EvClaim (client -> host, generous at-least-once): [netId:2][killerSlot:1] -- "this
        // replicated entity died on my screen, killed by slot k (or despawned)".
        public static byte[] EncodeClaimEvent(ushort eventSeq, ushort netId, byte killerSlot)
        {
            byte[] b = EventHeader(EvClaim, eventSeq, 3);
            WriteU16(b, 4, netId);
            b[6] = killerSlot;
            return b;
        }

        // EvScoreSync (host -> client, authoritative): [lives:1 signed][score0:f32][score1:f32]
        public static byte[] EncodeScoreSync(ushort eventSeq, int lives, float score0, float score1)
        {
            byte[] b = EventHeader(EvScoreSync, eventSeq, 9);
            b[4] = (byte)(sbyte)Math.Clamp(lives, -128, 127);
            WriteF32(b, 5, score0);
            WriteF32(b, 9, score1);
            return b;
        }

        // ---- world snapshot (host -> clients, stream lane) --------------------------------

        // MsgWorldSnapshot: [0x20][count:1] then `count` length-prefixed entries:
        //   [len:1][netId:2][typeIdx:1][base:24][per-type state extra:(len-28)]
        // The len prefix makes entries for not-yet-spawned ids skippable without knowing the
        // type's extra size (stream lane may outrun the reliable spawn).
        public const int SnapshotHeaderBytes = 2;
        public const int SnapshotEntryBaseBytes = 4 + BaseStateBytes; // len+netId+typeIdx+base

        public static void WriteSnapshotEntry(byte[] b, ref int off, ushort netId, byte typeIdx, in NetBaseState state, byte[] extra, int extraLen)
        {
            b[off++] = (byte)(SnapshotEntryBaseBytes + extraLen);
            WriteU16(b, off, netId);
            off += 2;
            b[off++] = typeIdx;
            WriteBaseState(b, ref off, state);
            for (int i = 0; i < extraLen; i++)
            {
                b[off++] = extra[i];
            }
        }

        public static bool TryReadSnapshotEntry(byte[] b, ref int off, out ushort netId, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen)
        {
            netId = 0;
            typeIdx = 0;
            state = default;
            extraOff = 0;
            extraLen = 0;
            if (off >= b.Length)
            {
                return false;
            }
            int len = b[off];
            if (len < SnapshotEntryBaseBytes || off + len > b.Length)
            {
                return false;
            }
            int p = off + 1;
            netId = ReadU16(b, p);
            p += 2;
            typeIdx = b[p++];
            ReadBaseState(b, ref p, ref state);
            extraOff = p;
            extraLen = off + len - p;
            off += len;
            return true;
        }

        // ---- shared base-state block (24 bytes) --------------------------------------------
        // [posX:f32][posY:f32][velX:f32][velY:f32][rot:u16][curframe:u16 x64][scale:u16 x256][hp:u16]
        // Velocity is the host's OBSERVED position delta in design px per ms (many enemies move
        // Position directly rather than via Speed/Direction, so SpeedVector would lie).

        public const int BaseStateBytes = 24;

        private const float FrameScale = 64f;
        private const float ScaleScale = 256f;
        private const float TwoPi = 6.2831855f;

        public static void WriteBaseState(byte[] b, ref int off, in NetBaseState s)
        {
            WriteF32(b, off, s.Pos.X);
            WriteF32(b, off + 4, s.Pos.Y);
            WriteF32(b, off + 8, s.Vel.X);
            WriteF32(b, off + 12, s.Vel.Y);
            float rot = s.Rotation % TwoPi;
            if (rot < 0f)
            {
                rot += TwoPi;
            }
            WriteU16(b, off + 16, (ushort)(rot / TwoPi * 65535f));
            WriteU16(b, off + 18, (ushort)Math.Clamp(s.CurFrame * FrameScale, 0f, 65535f));
            WriteU16(b, off + 20, (ushort)Math.Clamp(s.Scale * ScaleScale, 0f, 65535f));
            WriteU16(b, off + 22, (ushort)Math.Clamp(s.Hp, 0, 65535));
            off += BaseStateBytes;
        }

        public static void ReadBaseState(byte[] b, ref int off, ref NetBaseState s)
        {
            s.Pos = new Vector2(ReadF32(b, off), ReadF32(b, off + 4));
            s.Vel = new Vector2(ReadF32(b, off + 8), ReadF32(b, off + 12));
            s.Rotation = ReadU16(b, off + 16) / 65535f * TwoPi;
            s.CurFrame = ReadU16(b, off + 18) / FrameScale;
            s.Scale = ReadU16(b, off + 20) / ScaleScale;
            s.Hp = ReadU16(b, off + 22);
            off += BaseStateBytes;
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
    // The generic replicator's per-entity base fields (see NetProtocol.WriteBaseState).
    public struct NetBaseState
    {
        public Vector2 Pos;
        public Vector2 Vel; // design px per ms, host-observed
        public float Rotation;
        public float CurFrame;
        public float Scale;
        public int Hp; // 0 = not killable / unknown
    }

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
