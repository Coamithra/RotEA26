using System;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The 3-layer wire protocol (plans/stage11-online-coop.md), little-endian binary:
    //   1. Ship stream (unreliable lane, ~30 Hz): MsgShipState -- each peer's own ship
    //      STATE (pos, velocity, last aim, fire/alive flags, fire-rate loadout). The wire
    //      carries state, never inputs.
    //   2. World snapshot (host -> clients, unreliable lane, ~16.7 Hz): MsgWorldSnapshot --
    //      round-robin length-prefixed entries of the generic base block + per-type state
    //      extras (card 11.2, host world authority).
    //   3. Events (reliable lane): MsgHello/MsgWelcome handshake, MsgEvent envelope with a
    //      monotonically increasing sequence (EvSpawn/EvDeath from the host's NetIdRegistry,
    //      EvBlast from either peer, EvClaim from clients, EvScoreSync from the host).
    public static class NetProtocol
    {
        // 0x00 is RESERVED: webrtc.js uses a 1-byte 0x00 frame as its JS-level "bye"
        // (consumed in JS, never surfaced to C#). Message types must start at 0x01.
        public const byte MsgHello = 0x01;
        public const byte MsgWelcome = 0x02;
        public const byte MsgReject = 0x03;
        public const byte MsgShipState = 0x10;
        // Every locally-owned ship that ISN'T the sender's primary: the host's AI "friend" ships
        // (Mechanical Friends cheat) and, since card 4d904410, either peer's couch players. Same
        // body as MsgShipState with a leading slot byte so several stream in parallel;
        // BIDIRECTIONAL (it was host -> client while AI friends were the only case).
        // Stream lane, ~30 Hz.
        public const byte MsgFriendState = 0x11;
        public const byte MsgWorldSnapshot = 0x20;
        public const byte MsgEvent = 0x30;

        public const byte EvSpawn = 1;
        public const byte EvDeath = 2;
        public const byte EvBlast = 3;
        public const byte EvClaim = 4;
        public const byte EvScoreSync = 5;
        // Card 11.3: level-script beats + shared state-machine transitions (host -> client
        // unless noted). All ride the same reliable MsgEvent envelope.
        public const byte EvMessage = 6;      // script AnimatedMessage (MessageEvent)
        public const byte EvUnlock = 7;       // script unlock banner + grant (UnlockEvent)
        public const byte EvBackground = 8;   // Background op (opcode + vec2 param)
        public const byte EvMusic = 9;        // PlayMusic(song) / StopMusic (song = MusicStop)
        public const byte EvCheckpoint = 10;  // level checkpoint reached -> client score.Save()
        public const byte EvReset = 11;       // host LoseLife branch -> client mirrors it
        public const byte EvVictory = 12;     // host Victory() -> client Victory()
        public const byte EvPause = 13;       // either peer's local pause/resume (payload on/off)
        public const byte EvTetherBreak = 14; // either peer broke the TeamChallenge tether
        // Card 11.4: menu-lobby session flow.
        public const byte EvLaunch = 15;      // host -> client: [level:1][difficulty:1] -- mirror the launch
        public const byte EvReady = 16;       // client -> host: my GameScene is up, replay the live world
        public const byte EvLeave = 17;       // either peer quit the match -> the match ends for both
        // Card 4d904410: local (couch) players joining a peer that is already online. The HOST
        // allocates every roster slot, so a client-side join has to ask for one.
        public const byte EvJoinRequest = 18; // client -> host: a couch player pressed Start, give me a slot
        public const byte EvSlotGrant = 19;   // host -> client: [slot:1] (SlotNone = roster full, refused)

        // "No slot" -- a refused join grant. 0xFF can never be a real slot (Oracle.MaxPlayers is 4)
        // and matches KillerNone's convention.
        public const byte SlotNone = 0xFF;

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

        // Friend (host AI ship) stream: MsgShipState body shifted one byte right for the leading
        // player-slot. `alive` is implicit (a live friend is streamed; a dead/gone one simply stops
        // being sent, and the client's per-slot timeout explodes its puppet), so no alive flag.
        public static byte[] EncodeFriendState(byte slot, ushort seq, uint senderMs, Vector2 pos, Vector2 vel, float aim, bool firing, int shotsPerSec, float bulletLife)
        {
            byte[] b = new byte[31];
            b[0] = MsgFriendState;
            b[1] = slot;
            b[2] = (byte)(firing ? ShipFlagFiring : 0);
            b[3] = (byte)Math.Clamp(shotsPerSec, 1, 255);
            b[4] = (byte)Math.Clamp((int)(bulletLife / 10f), 0, 255);
            WriteU16(b, 5, seq);
            WriteU32(b, 7, senderMs);
            WriteF32(b, 11, pos.X);
            WriteF32(b, 15, pos.Y);
            WriteF32(b, 19, vel.X);
            WriteF32(b, 23, vel.Y);
            WriteF32(b, 27, aim);
            return b;
        }

        public static bool TryDecodeFriendState(byte[] b, out byte slot, out ushort seq, out ShipSample sample, out int shotsPerSec, out float bulletLife)
        {
            slot = 0;
            seq = 0;
            sample = default;
            shotsPerSec = 8;
            bulletLife = 450f;
            if (b.Length < 31 || b[0] != MsgFriendState)
            {
                return false;
            }
            slot = b[1];
            shotsPerSec = b[3];
            bulletLife = b[4] * 10f;
            seq = ReadU16(b, 5);
            sample.T = ReadU32(b, 7);
            sample.Pos = new Vector2(ReadF32(b, 11), ReadF32(b, 15));
            sample.Vel = new Vector2(ReadF32(b, 19), ReadF32(b, 23));
            sample.Aim = ReadF32(b, 27);
            sample.Alive = true;
            sample.Firing = (b[2] & ShipFlagFiring) != 0;
            return true;
        }

        // ---- handshake ----------------------------------------------------------------

        // v5: [type][protocolVersion][isHost][buildHash:8][flags:1][primarySlot:1] = 13 bytes. The
        // build hash (FNV-1a 64 of the eaBuildHash string deploy.yml stamps) enforces "peers run
        // the identical published binary" -- a stale-cached client is REJECTED, not subtly
        // desynced. Flags currently carry only the DebugFlags.Active bit (menu-lobby
        // sessions refuse gameplay-hijacking flags; the ?net= dev path is anything-goes).
        // primarySlot (v5, card 4d904410) is the HOST granting the client its primary roster
        // slot -- the host allocates every slot, so the oracle slot IS the wire slot on both
        // sides and no host-relative translation exists any more. The client sends SlotNone
        // (it has nothing to grant); the host's own primary is always slot 0.
        public const byte HelloFlagDebugActive = 1 << 0;
        public const int HelloBytes = 13;

        public static byte[] EncodeHello(byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot)
        {
            return EncodeHandshake(MsgHello, protocolVersion, isHost, buildHash, flags, primarySlot);
        }

        public static byte[] EncodeWelcome(byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot)
        {
            return EncodeHandshake(MsgWelcome, protocolVersion, isHost, buildHash, flags, primarySlot);
        }

        private static byte[] EncodeHandshake(byte type, byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot)
        {
            byte[] b = new byte[HelloBytes];
            b[0] = type;
            b[1] = protocolVersion;
            b[2] = (byte)(isHost ? 1 : 0);
            WriteU32(b, 3, (uint)buildHash);
            WriteU32(b, 7, (uint)(buildHash >> 32));
            b[11] = flags;
            b[12] = primarySlot;
            return b;
        }

        public static bool TryDecodeHandshake(byte[] b, out byte version, out bool isHost, out ulong buildHash, out byte flags, out byte primarySlot)
        {
            version = 0;
            isHost = false;
            buildHash = 0;
            flags = 0;
            primarySlot = SlotNone;
            if (b.Length < HelloBytes)
            {
                return false;
            }
            version = b[1];
            isHost = b[2] != 0;
            buildHash = ReadU32(b, 3) | ((ulong)ReadU32(b, 7) << 32);
            flags = b[11];
            primarySlot = b[12];
            return true;
        }

        // MsgReject: [type][reason] -- the pairing is refused; both sides surface a
        // human-readable notice and end the session.
        public const byte RejectVersion = 1; // protocol version mismatch
        public const byte RejectBuild = 2;   // build hash mismatch ("update required")
        public const byte RejectFlags = 3;   // gameplay debug flags active in a menu session

        public static byte[] EncodeReject(byte reason)
        {
            return new byte[] { MsgReject, reason };
        }

        // FNV-1a 64 over the eaBuildHash string -- cheap, stable, and 8 wire bytes.
        public static ulong HashBuildString(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "")
            {
                h = (h ^ c) * 1099511628211UL;
            }
            return h;
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

        // EvScoreSync (host -> client, authoritative): [lives:1 signed][score:f32 x MaxSlots].
        // v5 widened this from 2 slots to the full roster -- couch players (card 4d904410) sit in
        // the high slots and would otherwise never true up.
        public const int MaxSlots = 4;

        public static byte[] EncodeScoreSync(ushort eventSeq, int lives, float[] scores)
        {
            byte[] b = EventHeader(EvScoreSync, eventSeq, 1 + 4 * MaxSlots);
            b[4] = (byte)(sbyte)Math.Clamp(lives, -128, 127);
            for (int i = 0; i < MaxSlots; i++)
            {
                WriteF32(b, 5 + 4 * i, i < scores.Length ? scores[i] : 0f);
            }
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

        // ---- level-script beat events (card 11.3) -------------------------------------------

        // EvMessage: [msgType:1][speech:1][angle:f32][textLen:1][utf8 text:N] -- the exact
        // AnimatedMessage.Setup args the host-side MessageEvent spawned with (angle only
        // meaningful for the redwarning type's direction arrow).
        public static byte[] EncodeMessageEvent(ushort eventSeq, byte msgType, byte speech, float angle, string text)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            int textLen = Math.Min(utf8.Length, 255);
            byte[] b = EventHeader(EvMessage, eventSeq, 7 + textLen);
            b[4] = msgType;
            b[5] = speech;
            WriteF32(b, 6, angle);
            b[10] = (byte)textLen;
            Array.Copy(utf8, 0, b, 11, textLen);
            return b;
        }

        public static bool TryDecodeMessageEvent(byte[] b, out byte msgType, out byte speech, out float angle, out string text)
        {
            msgType = 0;
            speech = 0;
            angle = 0f;
            text = null;
            if (b.Length < 11 || b.Length < 11 + b[10])
            {
                return false;
            }
            msgType = b[4];
            speech = b[5];
            angle = ReadF32(b, 6);
            text = System.Text.Encoding.UTF8.GetString(b, 11, b[10]);
            return true;
        }

        // EvUnlock: [item:1][unlockType:1][speech:1][textLen:1][utf8 text:N] -- banner + the
        // unlock itself (generous: the join peer played the level too).
        public static byte[] EncodeUnlockEvent(ushort eventSeq, byte item, byte unlockType, byte speech, string text)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            int textLen = Math.Min(utf8.Length, 255);
            byte[] b = EventHeader(EvUnlock, eventSeq, 4 + textLen);
            b[4] = item;
            b[5] = unlockType;
            b[6] = speech;
            b[7] = (byte)textLen;
            Array.Copy(utf8, 0, b, 8, textLen);
            return b;
        }

        public static bool TryDecodeUnlockEvent(byte[] b, out byte item, out byte unlockType, out byte speech, out string text)
        {
            item = 0;
            unlockType = 0;
            speech = 0;
            text = null;
            if (b.Length < 8 || b.Length < 8 + b[7])
            {
                return false;
            }
            item = b[4];
            unlockType = b[5];
            speech = b[6];
            text = System.Text.Encoding.UTF8.GetString(b, 8, b[7]);
            return true;
        }

        // EvBackground: [op:1][x:f32][y:f32] (vec2 only used by the SetSpeed op).
        public static byte[] EncodeBackgroundEvent(ushort eventSeq, byte op, Vector2 v)
        {
            byte[] b = EventHeader(EvBackground, eventSeq, 9);
            b[4] = op;
            WriteF32(b, 5, v.X);
            WriteF32(b, 9, v.Y);
            return b;
        }

        // EvMusic: [song:1] (MusicStop = StopMusic). EvCheckpoint/EvVictory/EvTetherBreak carry
        // no payload; EvReset carries [mode:1]; EvPause carries [on:1] -- all use EncodeByteEvent.
        public const byte MusicStop = 0xFF;

        public static byte[] EncodeByteEvent(ushort eventSeq, byte eventType, byte value)
        {
            byte[] b = EventHeader(eventType, eventSeq, 1);
            b[4] = value;
            return b;
        }

        public static byte[] EncodeEmptyEvent(ushort eventSeq, byte eventType)
        {
            return EventHeader(eventType, eventSeq, 0);
        }

        // EvLaunch (host -> client, card 11.4): [level:1][difficulty:1] -- the menu-lobby
        // host picked; the client mirrors the launch with the host's locked difficulty.
        public static byte[] EncodeLaunchEvent(ushort eventSeq, byte level, byte difficulty)
        {
            byte[] b = EventHeader(EvLaunch, eventSeq, 2);
            b[4] = level;
            b[5] = difficulty;
            return b;
        }

        // EvBlast: [slot:1][posX:4][posY:4][level]. The slot (v5, card 4d904410) is which of the
        // sender's ships bombed -- without it a couch player's bomb detonated on the peer's
        // PRIMARY puppet.
        public static byte[] EncodeBlastEvent(ushort eventSeq, byte slot, Vector2 pos, int level)
        {
            byte[] b = EventHeader(EvBlast, eventSeq, 10);
            b[4] = slot;
            WriteF32(b, 5, pos.X);
            WriteF32(b, 9, pos.Y);
            b[13] = (byte)Math.Clamp(level, 0, 255);
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

    // The Background side-effect primitives the level scripts drive mid-level (hooked in
    // Background.cs; wire value = enum value). Initialize-time setters (SetSpace/SetMars/...)
    // are NOT here -- both peers run their own scene Initialize. APPEND-ONLY.
    public enum NetBackgroundOp : byte
    {
        SetSpeed = 0,
        QueueEarth = 1,
        QueueSmallEarth = 2,
        QueueAndromeda = 3,
        EngageBeltSlowdown = 4,
        DisengageBeltSlowdown = 5,
        SetAlienBase2 = 6,
        SetAlienBase3 = 7,
        SetAlienBase4 = 8,
        SetAlienBase5 = 9,
        SetAlienBase6 = 10,
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
