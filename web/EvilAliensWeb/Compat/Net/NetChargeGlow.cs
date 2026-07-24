using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Client-side upkeep for an enemy's laser-charge glow (coverage-gaps follow-up to card 11.2).
    //
    // SweepUFO / MarsBoss / SpiderHelperMothership each wind up a LazerGenerator "energy well" swarm
    // just before firing a Lazer. It is a CHILD component the emitter owns (Visible=false) and draws
    // BY HAND in its own Draw. On a JOIN peer the emitter is a FROZEN puppet: its gameplay Update
    // never runs, so it never spawns that child -> the client saw the beam appear with no windup
    // telegraph (the fired Lazer already replicates as its own puppet; only the charge glow was
    // missing). The fix keeps the glow OUT of the wire (making LazerGenerator itself replicable would
    // wrongly also replicate the player-ship summon glow + the preload prime, which are the same
    // type): instead the descriptor replicates a tiny per-emitter CHARGE STATE, and the puppet
    // re-creates a local, silent LazerGenerator that self-animates.
    //
    // The child is stored in the emitter's OWN generator field (SweepUFO.g / MarsBoss.lazerGenerator
    // / SpiderHelperMothership.windup), so the emitter's existing hand-draw + its OnComponentRemoved
    // Free() both already cover it -- no Draw/removal edits. On the host this field holds the real
    // generator and Drive is never called (the puppet driver is client-only); the two never overlap.
    internal static class NetChargeGlow
    {
        // Called once per puppet tick from the emitter's NetDriveExtras (UPDATE phase). Creates the
        // child on the charge-on edge, tracks the muzzle while charging, and frees it on charge-off.
        // Spawning here (not in ApplyStateExtra) honours the descriptor contract's "never spawn from
        // ApplyStateExtra" rule -- ApplyStateExtra only records the flags this reads.
        public static void Drive(ref LazerGenerator child, bool charging, Vector2 offset, float windupSeconds, float size, ComponentBin collection, Game game, Vector2 emitterPos)
        {
            if (charging)
            {
                if (child == null)
                {
                    child = LazerGenerator.NewLazerGenerator(collection, game);
                    // Setup clears the silent flag, so SetupSilent MUST follow it: a puppet must never
                    // play the "lazercharge" cue (it is not the local shooter).
                    child.Setup(emitterPos + offset, size, 1f, 0f, 0f);
                    child.SetupSilent();
                    child.SetWindup(windupSeconds, loop: false); // ramp fills the replicated windup exactly
                    collection.Add((GameComponent)(object)child);
                }
                else
                {
                    child.SetPosition(emitterPos + offset);
                }
            }
            else if (child != null)
            {
                child.Free(); // self-removes next Update; the emitter's Draw stops drawing it at once
                child = null;
            }
        }
    }
}
