using System;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    // `?minelog` -- the StarMine's decision trace, and the death-spot registry it is read
    // against (card 745728f9).
    //
    // THE REPORT: *"space mines (lvl 3, aka death stars) seem to also explode when they reach a
    // dead player's location"*. Every term in that sentence is invisible in a frame. A locked
    // mine and a free one draw the same sprite; a mine that ran out its 1800 ms detonation clock
    // and one set off by a neighbour's blue blast produce the identical pair of explosions; and
    // "a dead player's location" is not recorded ANYWHERE in the game once that player respawns.
    // So the claim can only be tested as DATA, which is what this file is for -- the
    // `?skullvolley` shape (card d8344c17), one level up because the correlation needs a second
    // fact the mine does not own.
    //
    // WHY THE REGISTRY IS SEPARATE FROM `Oracle`. `Oracle` keeps a per-slot cached position, but
    // it is written by a LIVE ship's Update, so it holds a death spot only until that slot
    // respawns -- and the respawn is the interesting moment, because `PlayerShipSummon` puts the
    // new ship back exactly where the old one died. An instrument that forgets the spot at the
    // respawn is blind to the whole second half of the report. (An earlier cut of this read
    // `Oracle` and was blind in exactly that way.)
    //
    // Diagnostic only: nothing here is read by game logic, every entry point is a no-op unless
    // `DebugFlags.MineLog` is set, and the registry is written on the death paths regardless so
    // a flag flipped mid-session cannot report a spot it never recorded... which cannot happen
    // (the flag is parsed once at boot) but costs one array write to be true anyway.
    internal static class MineLog
    {
        // Last known death position per oracle slot, and the world time it happened, or NaN for
        // a slot that has not died this session.
        private static readonly Vector2[] deathAt = new Vector2[Oracle.MaxPlayers];

        private static readonly double[] deathSeconds = InitSeconds();

        private static double[] InitSeconds()
        {
            double[] s = new double[Oracle.MaxPlayers];
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = double.NaN;
            }
            return s;
        }

        internal static bool On => DebugFlags.MineLog;

        // Called from every path that takes a player ship out of a world that keeps running:
        // `PlayerShip.Asplode`, `PlayerShip.AsplodeWall` and -- for the peer's ship in an online
        // session -- `NetSession.ExplodePuppet`, which removes a puppet WITHOUT `Die()` and so
        // reaches neither of the other two.
        internal static void NoteShipDeath(int slot, Vector2 at)
        {
            if (slot < 0 || slot >= deathAt.Length)
            {
                return;
            }
            deathAt[slot] = at;
            deathSeconds[slot] = WorldTime.Seconds;
            if (On)
            {
                Console.WriteLine("[mine] shipdied slot=" + slot + " at=" + Fmt(at));
            }
        }

        // Forget everything. A level change reuses slots, and a death spot from the PREVIOUS
        // level would otherwise be reported against this level's mines -- 800x600 is small
        // enough that such a ghost lands plausibly close to something.
        internal static void Reset()
        {
            for (int i = 0; i < deathAt.Length; i++)
            {
                deathAt[i] = Vector2.Zero;
                deathSeconds[i] = double.NaN;
            }
        }

        internal static string Fmt(Vector2 v)
        {
            return v.X.ToString("0") + "," + v.Y.ToString("0");
        }

        // ` deathspot=slot<n> d=<px> ago=<s>` for the nearest recorded death spot, or
        // ` deathspot=none`. The AGE is what makes the field readable: a mine detonating 30 px
        // from where somebody died four minutes ago is a coincidence, and one detonating there
        // two seconds later is the report.
        internal static string NearestDeathSpot(Vector2 from)
        {
            int slot = -1;
            float best = float.MaxValue;
            for (int i = 0; i < deathAt.Length; i++)
            {
                if (double.IsNaN(deathSeconds[i]))
                {
                    continue;
                }
                float d = (deathAt[i] - from).Length();
                if (d < best)
                {
                    best = d;
                    slot = i;
                }
            }
            if (slot < 0)
            {
                return " deathspot=none";
            }
            return " deathspot=slot" + slot
                + " d=" + best.ToString("0.0")
                + " ago=" + (WorldTime.Seconds - deathSeconds[slot]).ToString("0.00");
        }
    }
}
