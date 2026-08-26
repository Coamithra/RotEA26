using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch D: bosses + gnarly world entities (Mars/base side). Contract: NetTypeRegistry.cs;
    // worked example: UfoDescriptor.cs. Internal Net* seams live at the bottom of each type's
    // own file (StationaryBoss has none -- base-only; MarsBoss.cs / Lazer.cs / Wall.cs).

    // StationaryBoss (landed Mothership_landed still, AlienDrawableGameComponent) state surface:
    //   Draw reads base.Position plus the STATIC LandedOffsets.Landed nudge (loaded
    //   deterministically from Content/data/landed_offsets.json in Initialize) and the base
    //   scale. The sprite is a single frame (LoadAnimation with no grid -> 1x1, no animation)
    //   and the mothership never lifts off, so there is no sheet / stance / landed-vs-flying
    //   state beyond the base fields. The only Draw state NOT carried is fakehittimer's white
    //   hit-flash -- a purely cosmetic bullet-hit blink each side drives from its OWN local
    //   bullet collisions (correctly off the wire), so it may differ frame-to-frame between
    //   peers; an accepted divergence.
    // Wire format: BASE-ONLY (no spawn or state extras). CollisionType is a box around
    //   base.Position, so client bullet hit-testing is faithful from the base fields alone.
    internal sealed class StationaryBossDescriptor : NetTypeDescriptor<StationaryBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            StationaryBoss b = StationaryBoss.NewAlien(bin, game);
            b.Setup();
            return b;
        }
    }

    // MarsBoss (KillableAlien, Mars boss mothershipB) state surface:
    //   Setup(BossPosition) only steers the entry flight path (Update-only) -- inert on a frozen
    //   puppet, but replicated for a faithful reconstruction. Draw reads the animated 4x4 sheet
    //   (curframe, a base field) which ALTERNATES between the mothershipA / mothershipB halves
    //   each 16-frame wrap in Update; that A/B swap is NOT a base field, so it is the one piece
    //   of state carried. HP-driven colorize redden tracks via the base Hp block (NetApplyHp).
    //   The charge-up LazerGenerator glow is a child the host draws by hand; it is replicated as a
    //   charge state extra and rebuilt locally on the client (Compat/Net/NetChargeGlow) so the windup
    //   telegraph shows before the beam. The fired Lazer itself replicates as its own puppet.
    // Spawn extras: [bossPosition:1]   (0 = left, 1 = right)
    // State extras: [flags:1] (bit0 = mothershipB / second sheet half, bit1 = charging) + the 7-byte
    //   NetChargeWire block while charging.
    internal sealed class MarsBossDescriptor : NetTypeDescriptor<MarsBoss>
    {
        private const byte FlagSecondSheet = 1;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = C(c).NetBossPosition;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte pos = len >= 1 ? buf[off] : (byte)0;
            MarsBoss mb = MarsBoss.NewMarsBoss(bin, game);
            mb.Setup((MarsBoss.BossPosition)pos);
            return mb;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            MarsBoss m = C(c);
            byte flags = 0;
            if (m.NetSecondHalf)
            {
                flags |= FlagSecondSheet;
            }
            if (m.NetCharging)
            {
                flags |= NetChargeWire.FlagChargingBit1;
            }
            buf[off++] = flags;
            if (m.NetCharging)
            {
                off = NetChargeWire.Encode(buf, off, m.NetChargeOffset, m.NetChargeWindup, m.NetChargeSize);
            }
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            MarsBoss m = C(c);
            m.NetSetSpritesheetHalf((buf[off] & FlagSecondSheet) != 0);
            if ((buf[off] & NetChargeWire.FlagChargingBit1) != 0 && len >= 1 + NetChargeWire.Bytes)
            {
                NetChargeWire.Decode(buf, off + 1, out Vector2 chargeOffset, out float windup, out float size);
                m.NetApplyCharge(true, chargeOffset, windup, size);
            }
            else
            {
                m.NetApplyCharge(false, Vector2.Zero, 2.5f, 2f);
            }
        }
    }

    // Lazer (enemy beam, AlienDrawableGameComponent driving a Quad) state surface:
    //   A growing line collidable that HURTS the local player and is Quad-Draw-heavy -- so it
    //   matters for fairness (the local player dodges it). The AIM (Direction) plus the growing
    //   LENGTH and LEAD are ALL streamed as state extras: they drive both the collision line
    //   (CollisionType: origin = Position + lead*dir, length = len - lead) and the drawn Quad.
    //   The aim is carried in STATE (not spawn) because the puppet driver rewrites base.Direction
    //   from the observed velocity every snapshot (NetSpeedVector setter) -- so the true aim has
    //   to be re-applied last, in ApplyStateExtra. The muzzle Position rides the base fields.
    //   Perfectly-VERTICAL beams are replicated exactly (PiOver2) -- NO tilt is added (the old
    //   DDA hang was fixed with a step cap; a tilt would only desync from the host). The puppet
    //   is built sound-free (SetupSingleShot playSound:false) and ApplyStateExtra plays nothing /
    //   spawns nothing.
    // Spawn extras: [ownerNetId:2]  (card 9ccfe295, protocol v18; 0 = no emitter)
    //   THE EMITTER HAS TO BE ON THE WIRE, and leaving it off was a real defect rather than a
    //   fidelity gap. `Lazer.owner` is written only by `Setup`, which no puppet ever runs, so a
    //   client's beam had none -- and `UFO.CollidesWith` damages itself off any Lazer whose
    //   `owner != this`. So on the joiner a big laser UFO was hit by ITS OWN beam, 11 hit points
    //   at a 35 ms hittimer, and the unattributed claim that followed deleted the host's copy
    //   silently: "large laser-firing UFOs randomly disappear". Same shape for every other
    //   `Setup` emitter -- MarsBoss, the sweeping Boss, and SpiderHelperMothership, which READS
    //   `lazer.owner == this` in three places.
    //   Resolved through the live puppet map, so ordering matters and holds: the emitter's own
    //   EvSpawn always precedes its beam's on the ORDERED reliable lane (it existed first), and
    //   `NetIdRegistry.ReplayLive` walks `liveList` in spawn order for a join-in-progress peer.
    //   An id that does not resolve leaves `owner` null, i.e. exactly the pre-card behaviour.
    // State extras: [angle:2][len:2][lead:2][lenRate:2][leadRate:2][angleRate:2]
    //   angle u16 normalised [0,2pi); len/lead u16 px; the three RATES scaled i16 (card
    //   c1a38ef9) -- px/ms x1000 for the two growth rates, rad/ms x10000 for the sweep.
    //
    // THE RATES ARE WHAT LET A FROZEN BEAM MOVE BETWEEN TURNS. Without them the client only ever
    // saw the three VALUES, once per SnapshotTurnMs, and a beam growing at 0.4 px/ms jumped in
    // ~24 px steps. They are sent rather than differenced out of consecutive values: the host
    // knows them exactly, and it applies Settings.DifficultyModifier before sending so the client
    // never has to (its own modifier is a different number -- see Lazer's header).
    internal sealed class LazerDescriptor : NetTypeDescriptor<Lazer>
    {
        private const float TwoPi = 6.2831855f;
        // The pre-rate layout. A frame carrying only these six bytes is still applied in full --
        // the beam just holds its aim and length between turns, i.e. the pre-card behaviour.
        private const int ValuesBytes = 6;
        private const int RatesBytes = 12;
        // The spawn-extra block (card 9ccfe295): [ownerNetId:2].
        private const int OwnerBytes = 2;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            // 0 when the beam has no emitter (every SetupSingleShot shooter) OR when the emitter
            // is not itself replicated -- both mean "nothing for the client to point at".
            ushort ownerId = 0;
            if (C(c).NetOwner is AlienDrawableGameComponent emitter
                && NetIdRegistry.TryGetByComp(emitter, out NetIdRegistry.Entry e))
            {
                ownerId = e.Id;
            }
            buf[off++] = (byte)ownerId;
            buf[off++] = (byte)(ownerId >> 8);
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Lazer z = Lazer.NewLazer(bin, game);
            // playSound:false -- a puppet must never fire the beam SFX (it is not the local
            // shooter). Aim 0 / lead 0 for a frame until the first snapshot streams the beam.
            // SetupSingleShot also CLEARS `owner`, so the adopt below is the only thing that can
            // set one and a recycled beam cannot inherit the last emitter.
            z.SetupSingleShot(state.Pos, 0f, 0f, playSound: false);
            // Length-guarded: the snapshot self-heal constructs with len 0 (card de4d5d65), and
            // that puppet is PROVISIONAL -- the reliable EvSpawn rebuilds it with these extras.
            if (len >= OwnerBytes)
            {
                ushort ownerId = NetProtocol.ReadU16(buf, off);
                if (ownerId != 0 && NetPuppets.FindPuppet(ownerId) is AlienDrawableGameComponent emitter)
                {
                    z.NetSetOwner(emitter);
                }
            }
            return z;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            Lazer z = C(c);
            WriteAngle(buf, ref off, z.NetAngle);
            NetProtocol.WriteU16Px(buf, ref off, z.NetLen);
            NetProtocol.WriteU16Px(buf, ref off, z.NetLead);
            NetProtocol.WriteScaledI16(buf, ref off, z.NetLenRate, NetProtocol.RatePxPerMsScale);
            NetProtocol.WriteScaledI16(buf, ref off, z.NetLeadRate, NetProtocol.RatePxPerMsScale);
            NetProtocol.WriteScaledI16(buf, ref off, z.NetAngleRate, NetProtocol.RateRadPerMsScale);
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < ValuesBytes)
            {
                return;
            }
            float angle = NetProtocol.ReadU16(buf, off) / 65535f * TwoPi;
            float length = NetProtocol.ReadU16(buf, off + 2);
            float leadValue = NetProtocol.ReadU16(buf, off + 4);
            Lazer z = C(c);
            // ORDER: the rates first, then the values. NetApplyBeam is what re-asserts the aim the
            // driver's NetSpeedVector write just clobbered (see its header), so it has to run
            // LAST -- and NetApplyRates resets the integration budget, which must not be spent
            // against the previous turn's values.
            if (len >= RatesBytes)
            {
                z.NetApplyRates(
                    NetProtocol.ReadScaledI16(buf, off + 6, NetProtocol.RatePxPerMsScale),
                    NetProtocol.ReadScaledI16(buf, off + 8, NetProtocol.RatePxPerMsScale),
                    NetProtocol.ReadScaledI16(buf, off + 10, NetProtocol.RateRadPerMsScale));
            }
            z.NetApplyBeam(angle, length, leadValue);
        }

        private static void WriteAngle(byte[] buf, ref int off, float angle)
        {
            float a = angle % TwoPi;
            if (a < 0f)
            {
                a += TwoPi;
            }
            ushort v = (ushort)(a / TwoPi * 65535f);
            buf[off++] = (byte)v;
            buf[off++] = (byte)(v >> 8);
        }

    }

    // Wall (Level-3 scrolling tower grid, AlienDrawableGameComponent) state surface:
    //   Setup(variation) picks one of five block grids (0-4). Everything the frozen Draw and
    //   the CollisionLevelMap need then follows from base.Position (the vertical scroll offset,
    //   dead-reckoned by the driver from the base Vel) plus that fixed grid: scale is computed
    //   deterministically from the variation inside Setup (and also arrives in the base fields),
    //   and the wall is a single texture with no animation. So the variation is the only
    //   caller-chosen input that must be pinned.
    //   BEST-EFFORT: Setup halves the grid HEIGHT on Easy/Medium (difficulty-dependent). The
    //   client rebuilds from ITS Settings difficulty -- exact when the co-op session shares a
    //   difficulty (it does: TeamChallenge locks the menu-chosen difficulty), divergent
    //   otherwise. Grid files (variation 2) load via TitleContainer -- fine on the client.
    // Spawn extras: [variation:1]
    // State extras: none (base Position drives the scroll; the grid is fixed at spawn).
    //   THE `v < 0 -> 0` FALLBACK IS LOAD-BEARING SINCE cards 4392bd30 / 80749dc4, where it was
    //   merely lossy before. SetupFromFile (the ?wallpoptest path) leaves NetVariation at -1, so
    //   such a wall replicates as variation 0 -- and now that Wall.NetScaleLocal keeps the client
    //   on its OWN derived scale, the puppet takes variation 0's block size rather than adopting
    //   the host's, which for a 3-wide poptest grid is a 4x size error on top of the wrong grid.
    //   Only reachable on a dev ?net= boot (?wallpoptest always pairs with ?level=, which sets
    //   DebugFlags.Active and so refuses a menu session); a file-driven wall would need its own
    //   spawn-extra encoding to replicate honestly.
    internal sealed class WallDescriptor : NetTypeDescriptor<Wall>
    {
        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            int v = C(c).NetVariation;
            buf[off++] = (byte)(v < 0 ? 0 : v);
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            // DECLINE a build with no variation byte -- that is the snapshot self-heal's shape
            // (card de4d5d65 constructs with a literal extras length of 0; every real EvSpawn
            // carries the byte). The "generically-dressed puppet beats no puppet" trade the
            // self-heal makes elsewhere INVERTS for a wall: `len == 0` used to build variation 0,
            // which is a full screen of the FIRST section's grid -- drawn AND collidable, the
            // CollisionLevelMap is derived from the blocks -- at whatever scroll offset the wire
            // reported. On a joining peer that is "a different set of walls, looks like a section
            // from previously in the game" (card 430494a7), plus local collisions against
            // geometry the host does not have. No wall for a beat beats a wrong wall: the decline
            // arms only the SELF-HEAL lane's retry window (OnSpawn never consults it), so the
            // reliable EvSpawn builds the real grid the moment it lands -- and a JIP joiner's
            // walls arrive in the addressed catch-up burst anyway. NetWallTest section 5 pins it.
            if (len < 1)
            {
                return null;
            }
            int variation = buf[off];
            Wall w = Wall.NewWall(bin, game);
            w.Setup(variation);
            return w;
        }
    }
}
