using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // WORKED EXAMPLE for the per-type descriptor farm-out (contract: NetTypeRegistry.cs).
    //
    // UFO state surface (UFO.cs):
    //   Draw reads: stationary (landed still vs flying sheet), hasbonus + bonus.type
    //   (colorize hue -- the tinted bonus saucer), invincibilityTimer (brief blink,
    //   cosmetic, not replicated), plus the base fields (curframe animates the sheet).
    //   Setup(pos, isBig, behaviour) picks the sheet (ufosheet vs smallship); the medium
    //   ship is a Level-2 stationary variant reached via MakeMedium-style internals and is
    //   covered by the landed state byte.
    // Spawn extras: [flags:1][bonusType:1]  (flags: 1=isBig, 2=classic behaviour, 4=hasbonus)
    // State extras: [flags:1]               (1=stationary/landed, 2=charging, 4=hasbonus -- bonus
    //   can only ever turn OFF in play (dropped on death), so late clearing is cosmetic-safe)
    //   + the 7-byte NetChargeWire block while charging.
    //
    // Card 57ea30cd: a BIG ufo winds up a child LazerGenerator for 2500ms before firing
    // (UFOState.lazor) and draws it by hand, so the join peer saw the beam appear with no
    // telegraph. Bit 2 of the EXISTING flags byte carries it -- no new field and no wire-width
    // change, so no protocol bump: an older peer reads bit 0 and 2 as before and ignores bit 1,
    // and the trailing block is inside this entry's own length prefix.
    internal sealed class UfoDescriptor : NetTypeDescriptor<UFO>
    {
        private const byte FlagBig = 1;
        private const byte FlagClassic = 2;
        private const byte FlagBonus = 4;
        private const byte FlagUfoSheet = 8; // MakeSmall's random sheet pick, forced to match
        private const byte FlagStationary = 1;
        private const byte FlagCharging = 2;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            UFO u = C(c);
            byte flags = 0;
            if (u.IsBig)
            {
                flags |= FlagBig;
            }
            if (u.NetBehaviour == EnemyBehaviour.classic)
            {
                flags |= FlagClassic;
            }
            if (u.NetHasBonus)
            {
                flags |= FlagBonus;
            }
            if (!u.IsBig && u.NetSmallUfoSheet)
            {
                flags |= FlagUfoSheet;
            }
            buf[off++] = flags;
            buf[off++] = u.NetBonusType;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte flags = len >= 1 ? buf[off] : (byte)0;
            UFO u = UFO.NewUFO(bin, game);
            u.Setup(state.Pos, (flags & FlagBig) != 0, (flags & FlagClassic) != 0 ? EnemyBehaviour.classic : EnemyBehaviour.normal);
            u.NetForceSmallSheet((flags & FlagUfoSheet) != 0);
            // Unrecognised bonus type -> drop the bonus, keep the enemy (see the Braineroid
            // descriptor and the wire-enum contract in NetProtocol).
            if ((flags & FlagBonus) != 0 && len >= 2
                && NetProtocol.TryPowerupType(buf[off + 1], out Powerup.PowerupType bonus))
            {
                u.SetAsBonus(bonus);
            }
            return u;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            UFO u = C(c);
            byte flags = 0;
            if (u.NetStationary)
            {
                flags |= FlagStationary;
            }
            if (u.NetHasBonus)
            {
                flags |= FlagBonus;
            }
            bool charging = u.NetCharging;
            if (charging)
            {
                flags |= FlagCharging;
            }
            buf[off++] = flags;
            if (charging)
            {
                off = NetChargeWire.Encode(buf, off, u.NetChargeOffset, u.NetChargeWindup, u.NetChargeSize);
            }
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            UFO u = C(c);
            if ((buf[off] & FlagCharging) != 0 && len >= 1 + NetChargeWire.Bytes)
            {
                NetChargeWire.Decode(buf, off + 1, out Vector2 chargeOffset, out float windup, out float size);
                u.NetApplyCharge(true, chargeOffset, windup, size);
            }
            else
            {
                u.NetApplyCharge(false, Vector2.Zero, 2.5f, 1f);
            }
            bool landed = (buf[off] & FlagStationary) != 0;
            if (landed && !u.NetStationary)
            {
                u.SetStationary();
            }
            else if (!landed && u.NetStationary)
            {
                u.NetLiftOff();
            }
            if ((buf[off] & FlagBonus) == 0 && u.NetHasBonus)
            {
                u.NetClearBonus();
            }
        }
    }
}
