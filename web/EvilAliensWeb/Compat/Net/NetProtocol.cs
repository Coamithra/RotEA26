using System;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The 3-layer wire protocol (plans/stage11-online-coop.md), little-endian binary:
    //   1. Ship stream (unreliable lane, ~30 Hz): MsgShipState -- each peer's own ship
    //      STATE (pos, velocity, last aim, alive flag, cumulative shot count, fire-rate
    //      loadout). The wire carries state, never inputs.
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
        // Card 1a3ad45a: the per-slot HUD state its OWNER is authoritative for -- combo counter,
        // active powerup, bar progress and per-type levels. Bidirectional (each peer sends the
        // slots it owns), stream lane, ~10 Hz. Loss-tolerant by construction: a dropped packet
        // only means the readout is one interval staler.
        public const byte MsgHudState = 0x12;
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
        // Card 0b8a300b (anti-griefing): host -> client, "you are out of this match".
        // [blocked:1] -- 1 also means the host blocked our peer id for the rest of its level,
        // so a rejoin will be refused at the hello (RejectBanned). Payload via EncodeByteEvent.
        public const byte EvKick = 20;
        // Card 9a3175d0: a purely DECORATIVE swarm is replicated as one "effect on/off" beat
        // instead of per entity -- each peer runs its own spawner and its own scenery.
        // [kind:1][on:1][rate:f32]. See NetCosmeticKind.
        public const byte EvCosmeticSwarm = 21;
        // Card group "transient feedback never crosses the wire" (43e85936 / c146422f): a ONE-SHOT
        // cosmetic beat -- a hit flash, a chunk detaching, an enemy beam firing. These are
        // host-side events a frozen puppet can never reach, and unlike a sheet swap or a charge
        // state they CANNOT ride the snapshot's state extras: the round robin corrects an entity
        // only every `live/16*60ms` (SnapshotTurnMs, 60ms at best and ~1.2s in a big world) while
        // a KillableAlien hit blink lasts 35ms, so a sampled bit would miss the event outright and
        // a sampled cue would double-fire or drop. [kind:1][netId:2][param:1].
        // See NetFxKind.
        public const byte EvFx = 22;
        // Card f62116b5: "the killing blow landed and this type's death is going to TAKE A
        // WHILE" -- the host's KilledBy returned without removing the component (BattleSkull's
        // 2.5s dying state, the surviving MarsBoss's 5s crash). It is emitted at the moment the
        // deferred state is entered, so the joiner can release its frozen puppet immediately
        // instead of inferring the death from hp==0 on the entity's next round-robin snapshot
        // turn (up to ~1.2s in a big world). `[netId:2]`, reliable lane, 6 bytes.
        // KillableAlien.NoteDeathBegan has the census of which types do this.
        //
        // It is NOT the death's settlement: the eventual EvDeath still carries the killer and
        // the per-slot awards, exactly as before. This says only "it has begun".
        public const byte EvDying = 23;

        // "No slot" -- a refused join grant. 0xFF can never be a real slot (Oracle.MaxPlayers is 4)
        // and matches KillerNone's convention.
        public const byte SlotNone = 0xFF;

        public const byte ShipFlagAlive = 1 << 0;
        // Bit 1 used to be ShipFlagFiring -- the fire INTENT as a level, deleted by card a45b78f6
        // in favour of MsgShipState's cumulative shotCount byte. Free for the next flag.

        // ---- wire enum validation (card 88f87ba2) -------------------------------------
        //
        // CONTRACT. Every enum that crosses the wire is validated HERE, at the decode
        // boundary. A consumer of a decoded value MAY ASSUME it is in range and must not
        // add a defensive default of its own; a raw wire byte must never be cast to an
        // enum anywhere else.
        //
        // Adding a wire enum means adding its validator here AND a row in logic_probe's
        // ProbeWireEnums. Both halves are required -- see the maintenance note below.
        //
        // Three policies. Pick by what the field DOES, not by how bad the value looks:
        //   REJECT   -- the decoder returns false and the whole message is dropped. Use
        //               when the field is EXECUTED and no substitute is correct, or when
        //               the raw value could reach a SAVE FILE (see below).
        //   CLAMP    -- substitute a safe in-range value. Use for presentation-only fields
        //               where dropping the message loses more than degrading it.
        //   SENTINEL -- keep the raw value and expose a checked nullable beside it. Use
        //               where an unknown value is a NORMAL production case that must still
        //               be displayed (the public game browser's listings).
        //
        // A FIELD THAT CAN KILL A SAVE FILE REJECTS rather than clamps.
        // XmlSerializer refuses to serialize an enum value that is not a declared member,
        // and both Settings and Unlockables open their StreamWriter BEFORE serializing --
        // so the file is truncated and the write then throws. Savable.SaveInner swallows
        // that into Storage.ShowSaveError, so the player's settings or unlocks silently
        // stop persisting for the rest of the session and the file on disk is corrupt:
        //   - EvLaunch difficulty -> Settings.SetDifficultyTo -> Settings.xml
        //
        // EvUnlock's item WAS the second such path and is not any more (card 125490d9): the
        // join peer is a guest now and never grants, so the decoded value reaches no
        // Unlockables.Collection key and no save. ITS REJECT POLICY STAYS ANYWAY, for three
        // reasons -- the decoder still casts a raw wire byte to an enum, which this region is
        // the single place allowed to do; the protection must already be in place if the grant
        // is ever restored; and ProbeWireEnums asserts the bound. Do not relax it to a clamp on
        // the grounds that nothing consumes the value.
        //
        // VALIDATION IS CLIENT-SIDE BY DESIGN. The signaling server does not bound any of
        // these values and a server check would not be a security boundary -- gameplay is
        // peer-to-peer, so a peer can put any byte on the wire whatever the server saw.
        //
        // MAINTENANCE. The range tests below assume each enum is CONTIGUOUS from 0 and
        // APPEND-ONLY, so the bound names the last declared member. Nothing in the compiler
        // enforces that, and an appended member would otherwise be silently REFUSED off the
        // wire. logic_probe's ProbeWireEnums cross-checks every validator against
        // Enum.IsDefined across the whole 0..255 domain, which fails in BOTH directions --
        // a member added past the bound, or a gap/explicit value breaking contiguity.

        internal static bool TryLevel(int raw, out Levels level)
        {
            level = default;
            if (raw < 0 || raw > (int)Levels.WebcamAliens)
            {
                return false;
            }
            level = (Levels)raw;
            return true;
        }

        internal static bool TryDifficulty(int raw, out Settings.DifficultyLevel difficulty)
        {
            difficulty = default;
            if (raw < 0 || raw > (int)Settings.DifficultyLevel.Inzane)
            {
                return false;
            }
            difficulty = (Settings.DifficultyLevel)raw;
            return true;
        }

        internal static bool TryUnlockItem(int raw, out Unlockables.Items item)
        {
            item = default;
            if (raw < 0 || raw > (int)Unlockables.Items.Awardments)
            {
                return false;
            }
            item = (Unlockables.Items)raw;
            return true;
        }

        internal static bool TryUnlockType(int raw, out AnimatedMessage.UnlockType unlockType)
        {
            unlockType = default;
            if (raw < 0 || raw > (int)AnimatedMessage.UnlockType.difficulty)
            {
                return false;
            }
            unlockType = (AnimatedMessage.UnlockType)raw;
            return true;
        }

        internal static bool TryCosmeticKind(int raw, out NetCosmeticKind kind)
        {
            kind = default;
            if (raw < 0 || raw > (int)NetCosmeticKind.BeesLoop)
            {
                return false;
            }
            kind = (NetCosmeticKind)raw;
            return true;
        }

        // REJECT: the kind SELECTS which effect to run on which entity, so an unknown one has no
        // stand-in -- playing the wrong cue or flashing the wrong thing is worse than silence, and
        // dropping the frame degrades to exactly the pre-card behaviour (no feedback) with nothing
        // desynced, since an FX beat carries no gameplay state.
        internal static bool TryFxKind(int raw, out NetFxKind kind)
        {
            kind = default;
            if (raw < 0 || raw > (int)NetFxKind.EnemyLazerFire)
            {
                return false;
            }
            kind = (NetFxKind)raw;
            return true;
        }

        internal static bool TryPowerupType(int raw, out Powerup.PowerupType type)
        {
            type = default;
            if (raw < 0 || raw > (int)Powerup.PowerupType.OneUp)
            {
                return false;
            }
            type = (Powerup.PowerupType)raw;
            return true;
        }

        // CLAMP. A banner style we do not know still has readable text, and the level script
        // beat it belongs to only reaches the joiner once -- dropping the message would lose
        // the story text outright, which is worse than showing it in the default style.
        internal static AnimatedMessage.MessageType ClampMessageType(int raw)
        {
            return TryMessageType(raw, out AnimatedMessage.MessageType t)
                ? t
                : AnimatedMessage.MessageType.starwarsblue;
        }

        internal static bool TryMessageType(int raw, out AnimatedMessage.MessageType msgType)
        {
            msgType = default;
            if (raw < 0 || raw > (int)AnimatedMessage.MessageType.devcomment)
            {
                return false;
            }
            msgType = (AnimatedMessage.MessageType)raw;
            return true;
        }

        // CLAMP onto the enum's own "no speech" member, for the same reason as the banner
        // style: the text is the payload, the voice line is dressing.
        internal static SoundManager.Texts SpeechOrNone(int raw)
        {
            return TrySpeech(raw, out SoundManager.Texts speech) ? speech : SoundManager.Texts.Nothing;
        }

        internal static bool TrySpeech(int raw, out SoundManager.Texts speech)
        {
            speech = default;
            if (raw < 0 || raw > (int)SoundManager.Texts.GameOver)
            {
                return false;
            }
            speech = (SoundManager.Texts)raw;
            return true;
        }

        // REJECT: the op SELECTS a Background primitive to run, so an unknown one has no
        // stand-in -- substituting a different scene or doodad would put the joiner's world
        // somewhere the host's is not. Dropping the message leaves it on the backdrop it
        // already had, which is the pre-replication behaviour and desyncs nothing.
        internal static bool TryBackgroundOp(int raw, out NetBackgroundOp op)
        {
            op = default;
            if (raw < 0 || raw > (int)NetBackgroundOp.SetSceneAlienBase)
            {
                return false;
            }
            op = (NetBackgroundOp)raw;
            return true;
        }

        // EvDeath's killerSlot is not an enum, but it IS a raw wire byte with a three-way
        // meaning (a payable slot / KillerSelf / KillerNone), so it is validated here with
        // everything else rather than by an `is it 0xFF` test at each of its four readers.
        //
        // CLAMP, not REJECT: the message also carries the REMOVAL and the award array, so
        // dropping it would strand a puppet the host has already deleted -- permanently, since
        // the id is gone from the host's registry and no later snapshot will mention it. An
        // unrecognised value degrades to KillerNone, the FX-free silent despawn, which is the
        // least the receiver can do rather than crediting a slot 0x42 that does not exist.
        //
        // The payable bound is PayableSlots (8), the width of the claim ledgers' PaidMask
        // (NetPuppets.MarkPaid / IsPaid), NOT MaxSlots -- NetSession.NoteKill already admits
        // 0..7, so bounding at 4 here would silently reclassify a value the host is able to
        // emit. Slots 4..7 are unreachable today (Oracle.MaxPlayers is 4); this keeps the two
        // ends agreeing anyway. Every writer and reader of a killer/claim slot bounds against
        // THIS const -- it was spelled as a bare 8 at six sites before it had a name.
        internal const int PayableSlots = 8;

        internal static byte ClampKillerSlot(int raw)
        {
            if (raw == KillerSelf)
            {
                return KillerSelf;
            }
            return (raw >= 0 && raw < PayableSlots) ? (byte)raw : KillerNone;
        }

        // ---- ship stream --------------------------------------------------------------

        // [type][flags][shotsPerSec][bulletLife/10][seq:2][senderMs:4][posX:4][posY:4]
        // [velX:4][velY:4][aim:4][shotCount:1] = 31 bytes. Velocity is design px per MILLISECOND
        // (the component system's native unit, see AlienDrawableGameComponent.Update).
        // senderMs is SESSION-RELATIVE (uint ms since the sender's NetSession.Start) --
        // an absolute machine-uptime tick in float32 loses ms precision within hours.
        //
        // shotCount (card a45b78f6, protocol v12) is a CUMULATIVE wrapping u8 of the shots the
        // sender's ship has actually spawned -- incremented inside PlayerShip.FireAt's cadence
        // gate, beside the Bullet it counts. It REPLACED the `firing` LEVEL flag, which could
        // only ever be sampled at packet rate: the receiver takes the wrapped delta against the
        // last count it applied, so a lost or reordered stream packet costs nothing (the next
        // one carries the total) and two taps inside one cadence period are one increment,
        // exactly as they are one bullet for the owner.
        public static byte[] EncodeShipState(ushort seq, uint senderMs, Vector2 pos, Vector2 vel, float aim, bool alive, byte shotCount, int shotsPerSec, float bulletLife)
        {
            byte[] b = new byte[31];
            b[0] = MsgShipState;
            b[1] = (byte)(alive ? ShipFlagAlive : 0);
            b[2] = (byte)Math.Clamp(shotsPerSec, 1, 255);
            b[3] = (byte)Math.Clamp((int)(bulletLife / 10f), 0, 255);
            WriteU16(b, 4, seq);
            WriteU32(b, 6, senderMs);
            WriteF32(b, 10, pos.X);
            WriteF32(b, 14, pos.Y);
            WriteF32(b, 18, vel.X);
            WriteF32(b, 22, vel.Y);
            WriteF32(b, 26, aim);
            b[30] = shotCount;
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
            sample.ShotCount = b[30];
            return true;
        }

        // Friend (host AI ship) stream: MsgShipState body shifted one byte right for the leading
        // player-slot. `alive` is implicit (a live friend is streamed; a dead/gone one simply stops
        // being sent, and the client's per-slot timeout explodes its puppet), so no alive flag --
        // and since card a45b78f6 no flags byte at all, the firing level having been the only bit
        // it ever carried.
        public static byte[] EncodeFriendState(byte slot, ushort seq, uint senderMs, Vector2 pos, Vector2 vel, float aim, byte shotCount, int shotsPerSec, float bulletLife)
        {
            byte[] b = new byte[31];
            b[0] = MsgFriendState;
            b[1] = slot;
            b[2] = shotCount;
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
            sample.ShotCount = b[2];
            return true;
        }

        // ---- per-slot HUD state (card 1a3ad45a, stream lane) ------------------------------

        // How many powerup LEVELS ride the wire. Powerup.PowerupType is Blast, Option, FirePower,
        // Range, Linker, OneUp -- OneUp's level is pinned at 3 and never increments (PowerupData's
        // ctor), so only the leading 5 are replicated and the wire index IS the enum value. That
        // makes the enum append-only for this message too: a new type must go AFTER OneUp, or
        // widen this and bump ProtocolVersion.
        public const int HudLevelCount = 5;
        public const int HudSlotBytes = 5 + HudLevelCount;  // slot+combo:2+activeType+progress+levels
        // 0xFF = "this slot has no powerup active", so the receiver blanks the bar instead of
        // leaving a stale one lit. A real Powerup.PowerupType is 0..5 and can never collide.
        public const byte HudPowerupNone = 0xFF;

        // MsgHudState: [0x12][count:1] then `count` fixed-width HudSlotBytes entries:
        //   [slot:1][combo:2][activeType:1][progress:1][level x HudLevelCount]
        //
        // combo is a USHORT, not a byte, and that is load-bearing rather than generous: the host
        // pays every slot's boss share with THAT slot's own multiplier (AwardScoreToAll ->
        // comboModify = amount * (1 + combo/20)), so the figure it adopts here is spent, not just
        // drawn. A byte would silently cap a client's real 400x combo at 255 and underpay it --
        // and combos well past 255 are expected (ScoreVisualiser precaches 1000 combo strings and
        // drawPlayerScore has an explicit >= 1000 fallback). Saturation at ushort is unreachable
        // in play. progress is the active bar's 0..1 fill quantised to a byte.
        public static byte[] EncodeHudState(byte[] slots, int[] combos, byte[] activeTypes, float[] progress, int[][] levels, int count)
        {
            byte[] b = new byte[2 + HudSlotBytes * count];
            b[0] = MsgHudState;
            b[1] = (byte)count;
            int off = 2;
            for (int i = 0; i < count; i++)
            {
                b[off++] = slots[i];
                WriteU16(b, off, (ushort)Math.Clamp(combos[i], 0, ushort.MaxValue));
                off += 2;
                b[off++] = activeTypes[i];
                b[off++] = (byte)Math.Clamp((int)MathF.Round(progress[i] * 255f), 0, 255);
                for (int t = 0; t < HudLevelCount; t++)
                {
                    b[off++] = (byte)Math.Clamp(levels[i][t], 0, 4);
                }
            }
            return b;
        }

        // Reads entry `index` out of a validated packet. Levels are written into `levels` (length
        // must be >= HudLevelCount) rather than allocated, so the ~10 Hz rx path stays garbage-free.
        //
        // `activeType` comes back as a checked nullable rather than the raw byte: null is "this
        // slot has no powerup active", which covers the explicit HudPowerupNone sentinel and any
        // value we do not recognise in one answer, so the consumer has one case to handle
        // instead of two tests it could get individually wrong.
        internal static bool TryDecodeHudState(byte[] b, int index, int[] levels, out byte slot, out int combo, out Powerup.PowerupType? activeType, out float progress)
        {
            slot = 0;
            combo = 0;
            activeType = null;
            progress = 0f;
            if (!TryDecodeHudCount(b, out int count) || index < 0 || index >= count || levels == null || levels.Length < HudLevelCount)
            {
                return false;
            }
            int off = 2 + HudSlotBytes * index;
            slot = b[off];
            combo = ReadU16(b, off + 1);
            activeType = TryPowerupType(b[off + 3], out Powerup.PowerupType active) ? active : (Powerup.PowerupType?)null;
            progress = b[off + 4] / 255f;
            for (int t = 0; t < HudLevelCount; t++)
            {
                levels[t] = b[off + 5 + t];
            }
            return true;
        }

        // Whole-packet validation: the declared count must exactly account for the bytes present,
        // so a truncated or padded frame is rejected once here rather than per entry.
        public static bool TryDecodeHudCount(byte[] b, out int count)
        {
            count = 0;
            if (b == null || b.Length < 2 || b[0] != MsgHudState)
            {
                return false;
            }
            count = b[1];
            return b.Length == 2 + HudSlotBytes * count;
        }

        // ---- handshake ----------------------------------------------------------------

        // v8: [type][protocolVersion][isHost][buildHash:8][flags:1][primarySlot:1][peerId:8]
        // [blockedSlots:1] = 22 bytes. The
        // build hash (FNV-1a 64 of the eaBuildHash string deploy.yml stamps) enforces "peers run
        // the identical published binary" -- a stale-cached client is REJECTED, not subtly
        // desynced. Flags currently carry only the DebugFlags.Active bit (menu-lobby
        // sessions refuse gameplay-hijacking flags; the ?net= dev path is anything-goes).
        // primarySlot (v5, card 4d904410) is the HOST granting the client its primary roster
        // slot -- the host allocates every slot, so the oracle slot IS the wire slot on both
        // sides and no host-relative translation exists any more. The client sends SlotNone
        // (it has nothing to grant); the host's own primary is always slot 0.
        //
        // peerId (v6, card 0b8a300b) is the sender's own identity -- an FNV-1a 64 of a random
        // token webrtc.js mints once and keeps in localStorage. It exists ONLY so a host can
        // refuse a peer it kicked+blocked (RejectBanned) for the rest of its level; nothing
        // else reads it, and it never reaches the signaling server -- only a peer we are
        // already connected to P2P. It is SELF-REPORTED, so it is a speed bump against casual
        // griefing, not authentication: clearing site data mints a new one. Do not build
        // anything that needs to trust it on top of this.
        //
        // blockedSlots (v8, card c0229c57) is the CLIENT telling the host which slots it cannot
        // seat its primary ship in, so the host can grant one that is free on BOTH rosters
        // instead of guessing from its own. Without it the host's grant was a guess, and a guess
        // that landed on a seat the joiner already held desynced the pairing silently and
        // permanently. Host -> client the byte is always 0: the host allocates, so it has no
        // constraint to report. A bit mask (slots 0..3) rather than "the slot I refused" so the
        // negotiation resolves in ONE round and prevents the bad grant instead of recovering
        // from it.
        public const byte HelloFlagDebugActive = 1 << 0;
        public const int HelloBytes = 22;

        // Bit `slot` of a slot mask. Slots are 0..MaxSlots-1, so the mask fits a nibble today and
        // a byte for any plausible MaxSlots. Named for the mask, not for either side's meaning of
        // it: the same predicate reads the peer's BLOCKED slots and our own OCCUPIED ones.
        public static byte SlotBit(int slot)
        {
            return (byte)(1 << slot);
        }

        // Bounded by MaxSlots, not a literal, for the reason its comment gives: the mask builders
        // iterate Oracle.MaxPlayers, so a raised roster would set bits this predicate then read as
        // clear -- and the host would hand the joiner an occupied seat, silently.
        public static bool SlotInMask(byte mask, int slot)
        {
            return slot >= 0 && slot < MaxSlots && (mask & SlotBit(slot)) != 0;
        }

        public static byte[] EncodeHello(byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot, ulong peerId, byte blockedSlots)
        {
            return EncodeHandshake(MsgHello, protocolVersion, isHost, buildHash, flags, primarySlot, peerId, blockedSlots);
        }

        public static byte[] EncodeWelcome(byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot, ulong peerId, byte blockedSlots)
        {
            return EncodeHandshake(MsgWelcome, protocolVersion, isHost, buildHash, flags, primarySlot, peerId, blockedSlots);
        }

        private static byte[] EncodeHandshake(byte type, byte protocolVersion, bool isHost, ulong buildHash, byte flags, byte primarySlot, ulong peerId, byte blockedSlots)
        {
            byte[] b = new byte[HelloBytes];
            b[0] = type;
            b[1] = protocolVersion;
            b[2] = (byte)(isHost ? 1 : 0);
            WriteU32(b, 3, (uint)buildHash);
            WriteU32(b, 7, (uint)(buildHash >> 32));
            b[11] = flags;
            b[12] = primarySlot;
            WriteU32(b, 13, (uint)peerId);
            WriteU32(b, 17, (uint)(peerId >> 32));
            b[21] = blockedSlots;
            return b;
        }

        public static bool TryDecodeHandshake(byte[] b, out byte version, out bool isHost, out ulong buildHash, out byte flags, out byte primarySlot, out ulong peerId, out byte blockedSlots)
        {
            version = 0;
            isHost = false;
            buildHash = 0;
            flags = 0;
            primarySlot = SlotNone;
            peerId = 0;
            blockedSlots = 0;
            if (b.Length < HelloBytes)
            {
                return false;
            }
            version = b[1];
            isHost = b[2] != 0;
            buildHash = ReadU32(b, 3) | ((ulong)ReadU32(b, 7) << 32);
            flags = b[11];
            primarySlot = b[12];
            peerId = ReadU32(b, 13) | ((ulong)ReadU32(b, 17) << 32);
            blockedSlots = b[21];
            return true;
        }

        // MsgReject: [type][reason] -- the pairing is refused; both sides surface a
        // human-readable notice and end the session.
        public const byte RejectVersion = 1; // protocol version mismatch
        public const byte RejectBuild = 2;   // build hash mismatch ("update required")
        public const byte RejectFlags = 3;   // gameplay debug flags active in a menu session
        // Card 4d904410: the host has no free roster slot for the joiner's primary ship. Reachable
        // now that a COUCH game can be listed -- a local player can take the last seat between the
        // listing and the pairing. Must be refused, never left hanging: a joiner with no granted
        // slot would keep slot 0, which is the host's own player, and cross-credit everything.
        public const byte RejectFull = 4;
        // Card 0b8a300b: this peer was kicked+blocked by the host earlier in its current level.
        // Refused at the hello, which is the ONE choke point both rejoin routes pass through
        // (the public game browser and a typed room code).
        public const byte RejectBanned = 5;

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

        // EvDeath v7: [netId:2][killerSlot:1 (KillerSelf = unattributed real death,
        // KillerNone = despawn/off-screen)][posX:4][posY:4]
        // [award:f32 x MaxSlots] -- killer/pos let the receiver pay death FX even when its local
        // copy is already gone; the award array is what it credits.
        //
        // v7 replaced a single [points:2] BASE point value (card b0ab09ec). Two reasons it had
        // to widen, not just change meaning: a combo-modified award overflows a ushort (a 10000
        // -point boss at a routine 40x combo is 30000, and comboModify has no ceiling), and a
        // boss pays EVERY seated slot with that slot's own multiplier, so one number cannot
        // describe the payout. Same fixed f32-per-slot shape as EvScoreSync, for the same
        // reason -- most kills leave three of the four at zero, and 12 bytes per death is not
        // worth a variable-length mask.
        public const byte KillerNone = 0xFF;

        // "It really DIED, and nobody earned it" (cards 4e406eba / 303bfb5b / 13aa596c).
        // A self-detonating space mine, a scripted mothership crash, an enemy flying into a
        // wall: the host ran the type's real death path -- explosions, a cue, an animation --
        // with no killing blow to attribute. The receiver must run that same path for the FX
        // and pay NOBODY.
        //
        // It needs a value of its own because KillerNone means the OPPOSITE thing on the FX
        // axis while meaning the same thing on the score axis: an off-screen fly-off and a
        // teardown purge are also unattributed, and exploding those would put a bang (and a
        // sound) where the host had silence. The host decides which of the two it is -- see
        // NetSession.OnHostDeath.
        //
        // NO PROTOCOL CHANGE RIDES ON THIS. It reuses the existing killerSlot byte's reserved
        // space (Oracle.MaxPlayers is 4, so 0x08..0xFE were all dead values): no new field, no
        // width change, no new message or event type, and ProtocolVersion does not move. A peer
        // that would misread it cannot exist -- MsgHello's build-hash handshake refuses to pair
        // two different binaries, so both ends of any session are the same build.
        public const byte KillerSelf = 0xFE;

        public const int DeathEventBytes = 4 + 11 + 4 * MaxSlots;

        public static byte[] EncodeDeathEvent(ushort eventSeq, ushort netId, byte killerSlot, Vector2 pos, float[] awards)
        {
            byte[] b = EventHeader(EvDeath, eventSeq, 11 + 4 * MaxSlots);
            WriteU16(b, 4, netId);
            b[6] = killerSlot;
            WriteF32(b, 7, pos.X);
            WriteF32(b, 11, pos.Y);
            for (int i = 0; i < MaxSlots; i++)
            {
                WriteF32(b, 15 + 4 * i, (awards != null && i < awards.Length) ? awards[i] : 0f);
            }
            return b;
        }

        public static void ReadDeathAwards(byte[] b, float[] into)
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                into[i] = ReadF32(b, 15 + 4 * i);
            }
        }

        // EvDying (host -> client, reliable): [netId:2]. See the EvDying constant for what it
        // means and why it exists. No killer and no award: this is the death BEGINNING, and the
        // EvDeath that lands when the animation ends is still what settles who was paid.
        public const int DyingEventBytes = 4 + 2;

        public static byte[] EncodeDyingEvent(ushort eventSeq, ushort netId)
        {
            byte[] b = EventHeader(EvDying, eventSeq, 2);
            WriteU16(b, 4, netId);
            return b;
        }

        public static bool TryDecodeDyingEvent(byte[] b, out ushort netId)
        {
            netId = 0;
            if (b == null || b.Length < DyingEventBytes || b[0] != MsgEvent || b[1] != EvDying)
            {
                return false;
            }
            netId = ReadU16(b, 4);
            return true;
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
        //
        // MUST EQUAL Oracle.MaxPlayers. It is duplicated rather than referenced on purpose: this
        // is a WIRE width, so it may only change with a protocol version bump, whereas the game
        // constant is free to move. If they ever diverge, score sync silently truncates or
        // over-reads -- so change both together, and bump ProtocolVersion when you do.
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

        // EvMessage: [msgType:1][speech:1][angle:f32][textLen:1][utf8 text:N][short:1] -- the exact
        // AnimatedMessage.Setup args the host-side emitter spawned with (angle only
        // meaningful for the redwarning type's direction arrow).
        //
        // The trailing `short` byte (AnimatedMessage.MakeShort, the compact warning arrow the
        // bosses spawn) is APPENDED PAST the variable-length text and is OPTIONAL on decode, which
        // is what keeps this backwards- AND forwards-compatible with no protocol bump: the
        // pre-existing decoder bounds with `b.Length < 11 + textLen`, so an older peer reads the
        // text and ignores the extra byte, and a frame from an older peer simply decodes as
        // isShort=false, which is the pre-card behaviour. Do not move it in front of the text.
        public static byte[] EncodeMessageEvent(ushort eventSeq, byte msgType, byte speech, float angle, string text, bool isShort = false)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            int textLen = Math.Min(utf8.Length, 255);
            byte[] b = EventHeader(EvMessage, eventSeq, 8 + textLen);
            b[4] = msgType;
            b[5] = speech;
            WriteF32(b, 6, angle);
            b[10] = (byte)textLen;
            Array.Copy(utf8, 0, b, 11, textLen);
            b[11 + textLen] = (byte)(isShort ? 1 : 0);
            return b;
        }

        // Both enums CLAMP rather than reject -- see the wire-enum contract above.
        internal static bool TryDecodeMessageEvent(byte[] b, out AnimatedMessage.MessageType msgType, out SoundManager.Texts speech, out float angle, out string text, out bool isShort)
        {
            msgType = AnimatedMessage.MessageType.starwarsblue;
            speech = SoundManager.Texts.Nothing;
            angle = 0f;
            text = null;
            isShort = false;
            if (b.Length < 11 || b.Length < 11 + b[10])
            {
                return false;
            }
            msgType = ClampMessageType(b[4]);
            speech = SpeechOrNone(b[5]);
            angle = ReadF32(b, 6);
            text = System.Text.Encoding.UTF8.GetString(b, 11, b[10]);
            // Optional trailing byte -- absent means an older peer's frame, i.e. not short.
            isShort = b.Length >= 12 + b[10] && b[11 + b[10]] != 0;
            return true;
        }

        // EvFx (the transient-feedback beats): [kind:1][netId:2][param:1].
        // `netId` 0 means "no entity" -- a purely positional kind (NetIdRegistry never allocates 0).
        // `param` is per-kind and is 0 for every kind shipped so far; it exists so a later kind can
        // carry a level/size/on-off without a second event type.
        //
        // NO POSITION FIELD, deliberately. The two entity kinds resolve their target by netId and
        // draw on the puppet, whose position is already replicated and NEWER than anything a beat
        // could carry; the one entity-free kind plays a 2D cue. A position was carried in the first
        // cut of this event and every consumer ignored it -- if a future kind genuinely needs one,
        // add it then rather than shipping eight bytes per beat that nothing reads.
        public static byte[] EncodeFxEvent(ushort eventSeq, byte kind, ushort netId, byte param)
        {
            byte[] b = EventHeader(EvFx, eventSeq, 4);
            b[4] = kind;
            WriteU16(b, 5, netId);
            b[7] = param;
            return b;
        }

        internal static bool TryDecodeFxEvent(byte[] b, out NetFxKind kind, out ushort netId, out byte param)
        {
            kind = default;
            netId = 0;
            param = 0;
            if (b.Length < 8 || !TryFxKind(b[4], out kind))
            {
                return false;
            }
            netId = ReadU16(b, 5);
            param = b[7];
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

        // `item` and `unlockType` REJECT the whole message. Since card 125490d9 the join peer
        // neither grants nor announces an unlock -- it is a guest, and its own save is
        // untouched -- so this decode has no consumer beyond validating the frame. That is
        // exactly why it is still called: it is the only live caller, so removing it would
        // leave the wire-enum bound above (and ProbeWireEnums' row for it) covering dead code,
        // and a malformed frame would stop being refused. See the EvUnlock case in
        // NetSession.HandleEvent.
        internal static bool TryDecodeUnlockEvent(byte[] b, out Unlockables.Items item, out AnimatedMessage.UnlockType unlockType, out SoundManager.Texts speech, out string text)
        {
            item = default;
            unlockType = default;
            speech = SoundManager.Texts.Nothing;
            text = null;
            if (b.Length < 8 || b.Length < 8 + b[7])
            {
                return false;
            }
            if (!TryUnlockItem(b[4], out item) || !TryUnlockType(b[5], out unlockType))
            {
                return false;
            }
            speech = SpeechOrNone(b[6]);
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

        internal static bool TryDecodeBackgroundEvent(byte[] b, out NetBackgroundOp op, out Vector2 v)
        {
            op = default;
            v = Vector2.Zero;
            if (b.Length < 13 || !TryBackgroundOp(b[4], out op))
            {
                return false;
            }
            v = new Vector2(ReadF32(b, 5), ReadF32(b, 9));
            return true;
        }

        // EvCosmeticSwarm (card 9a3175d0): [kind:1][on:1][rate:f32] -- turn a decorative swarm on
        // or off on the peer, with the spawn rate the host's own spawner is running at. `rate` is
        // meaningless when off and written as 0.
        public static byte[] EncodeCosmeticSwarmEvent(ushort eventSeq, byte kind, bool on, float rate)
        {
            byte[] b = EventHeader(EvCosmeticSwarm, eventSeq, 6);
            b[4] = kind;
            b[5] = (byte)(on ? 1 : 0);
            WriteF32(b, 6, on ? rate : 0f);
            return b;
        }

        // REJECT: the kind selects which spawner to BUILD, so an unknown one has no sensible
        // stand-in (announcing the wrong swarm would put scenery on the joiner's screen that
        // the host is not running). The rate stays clamped where it is applied.
        internal static bool TryDecodeCosmeticSwarmEvent(byte[] b, out NetCosmeticKind kind, out bool on, out float rate)
        {
            kind = default;
            on = false;
            rate = 0f;
            if (b.Length < 10 || !TryCosmeticKind(b[4], out kind))
            {
                return false;
            }
            on = b[5] != 0;
            rate = ReadF32(b, 6);
            return true;
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

        // BOTH fields REJECT the message (see the wire-enum contract above). There is no
        // correct substitute for either: a clamped level launches a DIFFERENT level from the
        // one the host is playing and the two peers then replicate into mismatched worlds --
        // a silent desync, strictly worse than a refused join -- and a clamped difficulty
        // joins the match with enemy scaling that differs on one screen only. They ride one
        // message because together they ARE the match, so one reject covers both.
        //
        // Beyond the desync: an out-of-enum level reaches Game1.AddLevelComponent, whose
        // default arm throws AFTER MenuFinished has already frozen and removed the menu and
        // reset the roster -- the joiner is left on a black screen for the rest of the
        // session. The difficulty is the Settings.xml save-poisoning field.
        internal static bool TryDecodeLaunchEvent(byte[] b, out Levels level, out Settings.DifficultyLevel difficulty)
        {
            level = default;
            difficulty = default;
            if (b.Length < 6)
            {
                return false;
            }
            return TryLevel(b[4], out level) && TryDifficulty(b[5], out difficulty);
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
    // Background.cs; wire value = enum value). APPEND-ONLY.
    //
    // That includes the whole-SCENE setters (SetScene* below): a scene setter run at level
    // Initialize is not replicated -- both peers run their own -- but one run MID-level by the
    // script is, because a client's event list never runs and would otherwise keep the level's
    // opening backdrop for the rest of the run. Background tracks which case a call is; see its
    // NoteScene.
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
        // Join-in-progress catch-up only (card 45a4e48d), never fired by a live script beat:
        // places an already-crossing doodad at the host's current position, sent straight after
        // the Queue* op that re-creates it (which parks it back at its entry point). Its own op
        // rather than a magic non-zero Vector2 on Queue*, so neither carries two meanings.
        SetDoodadPos = 11,
        // The whole-scene swaps InsaneBossI drives between boss phases (card ca4fd94f). Named
        // SetScene* rather than after the setters, because SetAlienBase2..6 above are the
        // floor-TEXTURE switches within an alien-base scene -- a different thing entirely.
        // The scenes with no op here (holodeck / classic variants) are never swapped to
        // mid-level; Background reports it loudly if one ever is.
        SetSceneSpace = 12,
        SetSceneMars = 13,
        SetSceneAlienBase = 14,
    }

    // The decorative swarms replicated as one on/off beat rather than per entity (card
    // 9a3175d0). Wire value = enum value; APPEND-ONLY.
    //
    // A kind belongs here only if EVERY entity it spawns is cosmetic by construction -- it can
    // never become collidable, and nothing gameplay-visible reads it. Both current members
    // spawn with Collides=false and every AI consumer of Oracle.GetBaddies gates on Collides,
    // so the two peers' copies being in different places is invisible.
    public enum NetCosmeticKind : byte
    {
        // FlyingSpiderEvent(isbackground: true) -- Level 2's fog swarm and the ?flyspiders rig.
        FlyingSpiderBackground = 0,
        // The SetBackground() pair AsteroidSpawner emits alongside each real asteroid (Level 1's
        // belt, AsteroidChase, Demo1). The client's copy runs the spawner's own
        // SetBackGroundOnly() seam, so it never produces the collidable ones.
        BackgroundAsteroids = 1,
        // Level 2's looping "bees" ambience (Level2.beesSoundOn/Off). It is the SOUND of the fog
        // swarm above and is turned on and off by the same stretch of host-only level script, so
        // it rides this on/off lane rather than growing one of its own -- which buys it the latch,
        // the JIP catch-up replay, the checkpoint clear and the eaNetBg() state line for free.
        // The client "spawner" for this kind is the looping cue itself; `rate` is meaningless and
        // the emitter sends 1 (a positive, so the shared `rate <= 0 means off` guard is unchanged).
        BeesLoop = 2,
    }

    // Card group "transient feedback never crosses the wire": which one-shot cosmetic beat an
    // EvFx frame carries. APPEND-ONLY (the value is the wire byte) and bounded by
    // NetProtocol.TryFxKind -- a new member must move that bound AND its ProbeWireEnums row.
    //
    // Every kind here is DRAW/AUDIO ONLY on the receiving peer: nothing an EvFx applies may
    // damage, kill, award, spawn a replicable entity or move gameplay state, or the two worlds
    // diverge. They are also all IDEMPOTENT against the client's own local simulation -- a client
    // hit-tests puppets with its own bullets, so it may already have run the same feedback, and
    // each apply no-ops when the effect it would start is already running.
    public enum NetFxKind : byte
    {
        // The host landed a hit on the entity `netId`: light it up (and play its own per-type hit
        // cue). Covers KillableAlien's 35ms blink, SpiderBoss's Lazer hit and Ball's chip hit.
        EnemyHitFlash = 0,
        // A JunkBoss orbit Ball took its last chip and broke away: the detach explosion + "expl1".
        BallDetach = 1,
        // An enemy fired a single-shot Lazer: the "lazershotnoloop" cue (2D, so it needs no
        // position). Emitted at the
        // host's real firing moment rather than off the beam's EvSpawn, because ReplayLive
        // re-sends EvSpawn for the WHOLE live set at a join-in-progress catch-up and the puppet
        // layer cannot tell that from a fresh spawn -- which would salvo every live beam's cue at
        // the joiner the instant it arrives.
        EnemyLazerFire = 2,
    }

    public struct ShipSample
    {
        public double T;
        public Vector2 Pos;
        public Vector2 Vel; // design px per ms
        public float Aim;
        public bool Alive;
        // Cumulative wrapping count of the shots the OWNER's ship has actually spawned (card
        // a45b78f6). The receiver fires the wrapped delta; it is not a rate and never resets
        // except with the ship itself.
        public byte ShotCount;
    }
}
