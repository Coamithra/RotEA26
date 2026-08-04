using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch E: world-authority coverage-gap follow-ups (deferred from card 11.2 / PR #130).
    // Contract: NetTypeRegistry.cs; worked example: UfoDescriptor.cs. These extend the replicable
    // set to the enemy/hazard types that the original 11.2 table missed -- they were host-only, so
    // a JOIN peer saw nothing where they stood (a paratrooper's plasma ball, a fake/spider/brain
    // boss, the spider-boss helper mothership). Every puppet is built by the type's real New*+Setup
    // (each is already proven by the sprite harness -- see Compat/HarnessRegistry) and then FROZEN;
    // its gameplay Update never runs, only Draw. Bosses are best-effort (deep Update-reached attack
    // poses may diverge until their state extras grow); the divergences are documented per type.

    // PlasmaBall (PlasmaBall.cs) -- BASE-ONLY. The hazard a landed paratrooper brain vomits at the
    //   player, and the final boss's "electricity balls" (BrainBoss.Update spawns them too). Draw is
    //   a fixed additive "lightning ball": the plasmaball2 sheet drawn twice at two spinning
    //   rotation angles. Those angles are spun only in Update, so on a frozen puppet they used to
    //   hold at their spawn values and the orb was a STILL IMAGE (card 435db27f). They are now
    //   simulated LOCALLY in PlasmaBall.NetDriveExtras -- the spin is a fixed +-PI/2 rad/s and the
    //   angles were already per-instance random, so they never matched across peers anyway, which
    //   makes a local copy exactly as correct as the host's and costs no wire bytes.
    //   Everything that matters for dodging it --
    //   position, the entry scale ramp (0.025 -> 0.25) and flight -- rides the base fields (Pos, Vel,
    //   Scale x256). Setup(pos, direction) only seeds the entry velocity, which NetBaseState overrides.
    internal sealed class PlasmaBallDescriptor : NetTypeDescriptor<PlasmaBall>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            PlasmaBall p = PlasmaBall.NewAlien(bin, game);
            p.Setup(state.Pos, 0f); // direction seeds velocity only; NetBaseState re-drives pos/vel/scale
            return p;
        }
    }

    // ParatrooperAlien (ParatrooperAlien.cs) -- BASE-ONLY. A mediumship-sheet KillableAlien (the
    //   paratrooper that drifts down before deploying a brain). Draw is pure base.Draw; Setup() takes
    //   no args and picks no random look. Position/rotation/curframe/scale/hp all ride the base block;
    //   hit-blink is a ticked `timers` entry. Nothing beyond the base fields.
    internal sealed class ParatrooperAlienDescriptor : NetTypeDescriptor<ParatrooperAlien>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            ParatrooperAlien a = ParatrooperAlien.NewAlien(bin, game);
            a.Setup();
            return a;
        }
    }

    // ParatrooperBrain (ParatrooperBrain.cs) -- BASE-ONLY. The animated-brain KillableAlien a
    //   paratrooper drops; it chutes down, lands, and merges/grows. Draw is base.Draw plus the
    //   additive BrainGlow, whose scale/alpha derive from DrawScale and a per-instance phase (a
    //   cosmetic shimmer, no state to carry). The whole merge/grow choreography only ever writes
    //   Position / scale (0.5 -> 1.0 -> 1.65) / rotation
    //   directly in Update, all of which the host encodes into the base block every snapshot, so the
    //   grow + fall + tumble replicate from the base fields alone. curframe rides the base block too
    //   (card c25883a2 moved it onto the 20-frame sheet). Not Colorized (no HP redden). The
    //   Parachute + PlasmaBall it spawns replicate as their own types. Nothing beyond the base fields.
    internal sealed class ParatrooperBrainDescriptor : NetTypeDescriptor<ParatrooperBrain>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            ParatrooperBrain b = ParatrooperBrain.NewAlien(bin, game);
            b.Setup(state.Pos);
            return b;
        }
    }

    // Parachute (Parachute.cs) -- BASE-ONLY. The little chute above a descending paratrooper brain.
    //   During its life Draw is base.Draw at White; the appear ramp is a scale grow (0.001 -> 0.25)
    //   the host writes each Update and the base block carries (Scale). Setup(owner) only wires the
    //   OnComponentRemoved sever -- a null owner is safe on a puppet (the owner check simply never
    //   fires; the Ball-with-no-JunkBoss precedent). Best-effort: the ~100ms disappear fade (an alpha
    //   ramp + horizontal sway from a timer the frozen Update never advances) does not play -- an
    //   attributed remote death removes the puppet instead. Nothing beyond the base fields for life.
    internal sealed class ParachuteDescriptor : NetTypeDescriptor<Parachute>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Parachute p = Parachute.NewAlien(bin, game);
            p.Setup(null); // no live owner -- puppet never Updates, so the owner is never dereferenced
            return p;
        }
    }

    // FakeBoss (FakeBoss.cs) -- mirrors ClassicBoss/BattleSkull. Draw is an AnimatedSprite
    //   (GFX/alienboss/alienboss) at `animationProgress` (its OWN 20fps clock, Update-advanced),
    //   tinted by `color` (HP redden, Colorize) with a hit blink. scale + the redden ride the base
    //   state (Scale, Hp -> NetApplyHp; initialhitpoints is a fixed 500 either side so the redden
    //   matches exactly). animationProgress is the one Draw ingredient a frozen puppet can't reach ->
    //   STATE EXTRA so the body keeps animating. Setup() takes no args. The Update-spawned
    //   bullets/UFOs replicate as their own types.
    // Spawn extras: none. State extras: [animFrame:1].
    internal sealed class FakeBossDescriptor : NetTypeDescriptor<FakeBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            FakeBoss f = FakeBoss.NewFakeBoss(bin, game);
            f.Setup();
            return f;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)C(c).NetAnimFrame;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetAnimFrame = buf[off];
        }
    }

    // SpiderHelperMothership (SpiderHelperMothership.cs) -- mirrors MarsBoss/Boss. The friendly
    //   mothership the SpiderBoss fight summons to laser the (Lazer-only-damageable) boss. Draw is
    //   base.Draw of the 4x4 mothershipB sheet, which ALTERNATES between the mothershipA/mothershipB
    //   halves each animation wrap in Update -- that A/B choice is the one Draw-visible bit the base
    //   fields (curframe/Hp) don't carry -> STATE EXTRA. HP-redden colorize rides the base Hp
    //   (NetApplyHp); the pool is difficulty-scaled but the client shares the session difficulty, so
    //   it matches. Setup's many args only steer the Update-only movement/aim (inert on a frozen
    //   puppet) -- Position rides the base block -- so a benign default Setup reconstructs it.
    // Spawn extras: none. State extras: [flags:1] (bit0 = second/mothershipB half, bit1 = charging)
    //   followed by the 7-byte NetChargeWire block while charging.
    // The charge-swarm windup glow is a child LazerGenerator the host draws by hand; it is rebuilt on
    // the client from the replicated charge state (Compat/Net/NetChargeGlow), not a separate wire type.
    // Best-effort: the crash-land death sequence does not play (an attributed remote death removes it).
    internal sealed class SpiderHelperMothershipDescriptor : NetTypeDescriptor<SpiderHelperMothership>
    {
        private const byte FlagSecondHalf = 1;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            SpiderHelperMothership h = SpiderHelperMothership.NewHelper(bin, game);
            // Look-irrelevant defaults (mirror HarnessRegistry): every arg only feeds the Update-only
            // entry/charge/fire path, and the Setup-computed Position is overwritten by NetBaseState.
            h.Setup(10f, 0.3f, 4500f, 150f, 2500f, null);
            return h;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            SpiderHelperMothership h = C(c);
            byte flags = 0;
            if (h.NetSecondHalf)
            {
                flags |= FlagSecondHalf;
            }
            if (h.NetCharging)
            {
                flags |= NetChargeWire.FlagChargingBit1;
            }
            buf[off++] = flags;
            if (h.NetCharging)
            {
                off = NetChargeWire.Encode(buf, off, h.NetChargeOffset, h.NetChargeWindup, h.NetChargeSize);
            }
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            SpiderHelperMothership h = C(c);
            h.NetSetSpritesheetHalf((buf[off] & FlagSecondHalf) != 0);
            if ((buf[off] & NetChargeWire.FlagChargingBit1) != 0 && len >= 1 + NetChargeWire.Bytes)
            {
                NetChargeWire.Decode(buf, off + 1, out Vector2 chargeOffset, out float windup, out float size);
                h.NetApplyCharge(true, chargeOffset, windup, size);
            }
            else
            {
                h.NetApplyCharge(false, Vector2.Zero, 2.5f, 2f);
            }
        }
    }

    // SpiderBoss (SpiderBoss.cs) -- best-effort. Draws `currentAnimation` (one of four AnimatedSprites:
    //   fly/stand/jump/land) at `animationProgress` (its own clock), with a horizontal flip + a draw
    //   offset that both depend on `state`; it draws Color.White (no HP redden -- not a KillableAlien).
    //   All of that is reached only by the frozen Update, so three things beyond the base block are
    //   streamed: the state (for the Draw flip/offset AND the state-keyed collision box), which of the
    //   four sprites is current (they don't track state 1:1), and the animation frame.
    // Spawn extras: none (Setup(intro) only seeds the initial state/pos, which NetBaseState overrides).
    // State extras: [state:1][animIndex:1][animFrame:1].
    // The `dead` debris burst DOES play on the client since card ad9c8f8b -- this boss is not a
    //   KillableAlien, so it announces its own death entry (NetSession.OnHostDeathBegan from
    //   CollidesWith) and re-runs BeginDeathThroes locally off the beat, through the
    //   INetEntity.NetIsDying / NetBeginDeferredDeath seam. Best-effort remainder: the summoned
    //   SpiderHelperMothership replicates as its own type.
    internal sealed class SpiderBossDescriptor : NetTypeDescriptor<SpiderBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            SpiderBoss s = SpiderBoss.NewSpiderBoss(bin, game);
            s.Setup(false); // non-intro land pose; the first snapshot supplies the real state/anim
            return s;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            SpiderBoss s = C(c);
            buf[off++] = s.NetState;
            buf[off++] = s.NetAnimIndex;
            buf[off++] = s.NetAnimFrame;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 3)
            {
                return;
            }
            SpiderBoss s = C(c);
            // Order: state + animIndex (picks currentAnimation) BEFORE the frame, so the frame indexes
            // the sprite the host measured it against.
            s.NetState = buf[off];
            s.NetAnimIndex = buf[off + 1];
            s.NetAnimFrame = buf[off + 2];
        }
    }

    // BrainBoss (BrainBoss.cs) -- best-effort. The final boss: a single static brainbosshd frame
    //   tinted by `color` (HP redden, Colorize; initialhitpoints a fixed 1700 either side, so the
    //   redden matches exactly) with animated overlay patches + a BrainAura glow LAYERED on top. Both
    //   the overlays and the aura animate off gameTime IN DRAW (not Update), and the aura is respawned
    //   by the puppet's own Initialize, so both animate correctly on a frozen puppet -- scale + redden
    //   ride the base block. The one Draw ingredient the frozen Update gates is the overlay "exhaust
    //   pods" vent (keyed off BossState.spawnstuff) -> STATE EXTRA bit so the pods vent on the client
    //   while the host is spawning a wave. Setup(challenge) only affects the host-side music-rate sweep.
    // Spawn extras: none. State extras: [flags:1]  (bit0 = spawning a wave / pods venting).
    // The multi-phase asplode DOES play on the client (the deferred-death release, PR #267;
    //   watched and pinned by eaNetDeathFx section 8 since card ad9c8f8b). Best-effort remainder:
    //   the spawned brainz/skullz/mines/bullets replicate as their own types.
    internal sealed class BrainBossDescriptor : NetTypeDescriptor<BrainBoss>
    {
        private const byte FlagVenting = 1;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            BrainBoss b = BrainBoss.NewBrainBoss(bin, game);
            b.Setup(false);
            return b;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)(C(c).NetVenting ? FlagVenting : 0);
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetVenting = (buf[off] & FlagVenting) != 0;
        }
    }

    // Shared wire format for the enemy laser-charge glow (SweepUFO/MarsBoss/SpiderHelperMothership).
    // The charging BIT lives in each descriptor's own leading flags byte (bit0 is already taken by
    // MarsBoss/SpiderHelper's A/B sheet half, so charging is bit1 there; SweepUFO has no half, so it
    // uses bit0). When set, this 7-byte block follows: the muzzle offset from the emitter centre
    // (signed px), the windup duration (centiseconds), and the swarm size -- everything
    // Compat/Net/NetChargeGlow needs to rebuild + ramp + spread the client's local copy identically.
    internal static class NetChargeWire
    {
        public const byte FlagChargingBit1 = 2; // charging bit when bit0 is the A/B half (bosses)
        public const int Bytes = 7;

        public static int Encode(byte[] buf, int off, Vector2 offset, float windupSeconds, float size)
        {
            WriteI16(buf, ref off, (int)offset.X);
            WriteI16(buf, ref off, (int)offset.Y);
            WriteU16(buf, ref off, (int)(windupSeconds * 100f));
            buf[off++] = (byte)System.Math.Clamp((int)System.Math.Round(size), 0, 255);
            return off;
        }

        public static void Decode(byte[] buf, int off, out Vector2 offset, out float windupSeconds, out float size)
        {
            offset = new Vector2(ReadI16(buf, off), ReadI16(buf, off + 2));
            windupSeconds = NetProtocol.ReadU16(buf, off + 4) / 100f;
            size = buf[off + 6];
        }

        private static void WriteI16(byte[] b, ref int o, int v)
        {
            ushort u = (ushort)(short)System.Math.Clamp(v, short.MinValue, short.MaxValue);
            b[o++] = (byte)u;
            b[o++] = (byte)(u >> 8);
        }

        private static short ReadI16(byte[] b, int o)
        {
            return (short)(b[o] | (b[o + 1] << 8));
        }

        private static void WriteU16(byte[] b, ref int o, int v)
        {
            ushort u = (ushort)System.Math.Clamp(v, 0, 65535);
            b[o++] = (byte)u;
            b[o++] = (byte)(u >> 8);
        }
    }
}

