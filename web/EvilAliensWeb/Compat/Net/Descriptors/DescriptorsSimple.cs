using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch A: simple / ballistic common types (contract: NetTypeRegistry.cs; worked example:
    // UfoDescriptor.cs). A frozen puppet is built by the type's real New*+Setup and never runs
    // gameplay Update; only its Draw (+ CollisionType, for local bullet hit-tests) runs. Each
    // descriptor pins whatever Setup args / random picks decide the LOOK and, if the frozen Draw
    // reads a field Update would maintain that the base block doesn't carry, replicates it.

    // EvilBullet (EvilBullet.cs) -- BASE-ONLY.
    //   State surface: single fixed sheet "GFX/Sprites/bulletevil" (loaded in the ctor, no
    //   variants); Draw is pure base.Draw (Position + rotation + curframe + color=White). Setup
    //   only sets Position + Direction, and Direction just seeds the velocity -- pos / velocity /
    //   rotation / curframe all ride NetBaseState. Ballistic, no per-instance look or damage
    //   state. Nothing to pin; nothing beyond the base fields for Draw to read.
    internal sealed class EvilBulletDescriptor : NetTypeDescriptor<EvilBullet>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            EvilBullet b = EvilBullet.NewEvilBullet(bin, game);
            b.Setup(state.Pos, 0f); // Direction seeds velocity only; NetBaseState overrides vel/rotation
            return b;
        }
    }

    // Asteroid (Asteroid.cs) -- SPAWN-EXTRA-ONLY (no state extra).
    //   State surface: Setup picks the SHEET -- reallyBig -> hi-res "large_asteroid" (scale 3),
    //   else one of four "AsteroidSmall{1..4}" at RANDOM (scale 0.45); SetBackground() then greys
    //   the tint (color 0.3), shrinks, and drops DrawOrder to 1 for belt decoration. Not a
    //   KillableAlien: bullets only DEFLECT it (CollidesWith nudges Direction/Speed), it has no HP
    //   and never splits/dies from damage, so Draw has no damage state -- just base.Draw with the
    //   chosen sheet + replicated pos/rotation/scale. Position/rotation/scale all ride the base
    //   block; only the sheet pick and the grey/sunk background flag can't be reconstructed from
    //   it, so both go in the spawn extra (fixed at spawn, never changes).
    //   Spawn extra: [flags:1]  bit0=reallyBig, bit1=background, bits2-3=small sheet index (0..3)
    internal sealed class AsteroidDescriptor : NetTypeDescriptor<Asteroid>
    {
        private const byte FlagBig = 1;
        private const byte FlagBackground = 2;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            Asteroid a = C(c);
            byte flags = 0;
            if (a.NetReallyBig)
            {
                flags |= FlagBig;
            }
            if (a.NetIsBackground)
            {
                flags |= FlagBackground;
            }
            flags |= (byte)((a.NetSmallSheetIndex & 0x3) << 2);
            buf[off++] = flags;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte flags = len >= 1 ? buf[off] : (byte)0;
            bool big = (flags & FlagBig) != 0;
            bool background = (flags & FlagBackground) != 0;
            int smallIndex = (flags >> 2) & 0x3;
            Asteroid a = Asteroid.NewAsteroid(bin, game);
            // randomSpeedOffset:false -- puppet motion comes from NetBaseState, not the Setup RNG.
            a.Setup(state.Pos, 0f, 0f, big, false);
            a.NetForceSheet(big, smallIndex); // Setup re-randomises the small variant; force the host's pick
            if (background)
            {
                a.SetBackground(); // grey tint + DrawOrder 1 + Collides=false (base state re-drives scale/vel)
            }
            return a;
        }
    }

    // SweepUFO (SweepUFO.cs) -- CHARGE-STATE ONLY.
    //   State surface: single fixed sheet "GFX/Sprites/mediumship"; KillableAlien, so hit-blink is
    //   a `timers` entry the driver ticks and HP rides the base block (NetApplyHp). Setup(targetplayer,
    //   number, total) uses number/total ONLY to compute the entry Position (which NetBaseState
    //   carries) and targetplayer ONLY to aim the lazer -- so no Setup arg affects the puppet's Draw.
    //   The damaging beam is its own replicated Lazer entity (registry #11). The ONE Draw ingredient
    //   the frozen Update would otherwise spawn is the charge-swarm glow (`g`, a child LazerGenerator
    //   the host draws by hand) -- replicated as a charge state extra + rebuilt locally on the client
    //   (Compat/Net/NetChargeGlow) so the windup telegraph shows before the beam.
    // Spawn extras: none. State extras: [flags:1] (bit0 = charging) + the 7-byte NetChargeWire block
    //   while charging.
    internal sealed class SweepUfoDescriptor : NetTypeDescriptor<SweepUFO>
    {
        private const byte FlagCharging = 1;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            SweepUFO u = SweepUFO.NewSweepUFO(bin, game);
            // Args are look-irrelevant; total>=2 avoids the harness's (total-1) divide-by-zero. The
            // Setup-computed Position is overwritten by NetBaseState on the spawn snapshot anyway.
            u.Setup(false, 0, 2);
            return u;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            SweepUFO u = C(c);
            if (u.NetCharging)
            {
                buf[off++] = FlagCharging;
                off = NetChargeWire.Encode(buf, off, u.NetChargeOffset, u.NetChargeWindup, u.NetChargeSize);
            }
            else
            {
                buf[off++] = 0;
            }
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            SweepUFO u = C(c);
            if ((buf[off] & FlagCharging) != 0 && len >= 1 + NetChargeWire.Bytes)
            {
                NetChargeWire.Decode(buf, off + 1, out Vector2 chargeOffset, out float windup, out float size);
                u.NetApplyCharge(true, chargeOffset, windup, size);
            }
            else
            {
                u.NetApplyCharge(false, Vector2.Zero, 2.5f, 1f);
            }
        }
    }

    // StarMine (StarMine.cs) -- BASE-ONLY.
    //   State surface: single fixed sheet "GFX/Sprites/deathstarsheet2" (4x8 animated -- curframe
    //   rides the base block + driver NetAdvanceFrame); KillableAlien (HP via NetApplyHp, hit-blink
    //   via ticked `timers`). CollisionType is a constant-radius circle (r=24). Draw is pure
    //   base.Draw -- the free / attracted-to-player / attracted-to-boss states change movement, not
    //   appearance (no per-state pulse or arming visual). Nothing beyond the base fields for Draw.
    internal sealed class StarMineDescriptor : NetTypeDescriptor<StarMine>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            StarMine m = StarMine.NewStarMine(bin, game);
            m.Setup(); // random spawn X + Speed 0 -- both overwritten by NetBaseState
            return m;
        }
    }

    // Powerup (Powerup.cs) -- SPAWN-EXTRA-ONLY (no state extra).
    //   State surface: Setup(pos) calls Randomize() which picks a RANDOM PowerupType; MakeType(type)
    //   sets both the tint `color` AND the letter `p` -- and Draw reads both (base.Draw tints the
    //   powerupbw sprite by color, then draws `p` twice with the font). type is fixed after spawn,
    //   so it MUST be pinned or the two screens show different powerups. The cosmetic drift + scale
    //   pulse live in the frozen Update; scale rides the base block (host encodes it per snapshot),
    //   so the pulse replicates (lightly smoothed). `taken` is set on pickup but Draw never reads it
    //   (pickup removes the powerup -> EvDeath). No field beyond type + base fields matters.
    //   Spawn extra: [type:1]  (Powerup.PowerupType)
    internal sealed class PowerupDescriptor : NetTypeDescriptor<Powerup>
    {
        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)C(c).type;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Powerup p = Powerup.NewPowerup(bin, game);
            p.Setup(state.Pos); // Randomize() picks a random type...
            if (len >= 1)
            {
                p.MakeType((Powerup.PowerupType)buf[off]); // ...pin the host's actual type (sets color + letter)
            }
            return p;
        }
    }
}
