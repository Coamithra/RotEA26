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
    // / SpiderHelperMothership.windup / UFO.lazerGenerator / JunkBoss.suckeffect), so the emitter's
    // existing hand-draw + its OnComponentRemoved
    // Free() both already cover it -- no Draw/removal edits. On the host this field holds the real
    // generator and Drive is never called (the puppet driver is client-only); the two never overlap.
    internal static class NetChargeGlow
    {
        // Called once per puppet tick from the emitter's NetDriveExtras (UPDATE phase). Creates the
        // child on the charge-on edge, tracks the muzzle while charging, and frees it on charge-off.
        // Spawning here (not in ApplyStateExtra) honours the descriptor contract's "never spawn from
        // ApplyStateExtra" rule -- ApplyStateExtra only records the flags this reads.
        // `lifetime` is the emitter's own Setup argument, NOT a wire field: it is a per-emitter
        // CONSTANT that both peers already have in their copy of the code, so streaming it would
        // be paying for something the client can read locally. It is a parameter rather than a
        // literal because the emitters disagree -- JunkBoss's suck swarm passes 0.5 where the four
        // laser windups pass 1, and a hard-coded 1 gave the client's suck particles twice the
        // host's life.
        public static void Drive(ref LazerGenerator child, bool charging, Vector2 offset, float windupSeconds, float size, float lifetime, ComponentBin collection, Game game, Vector2 emitterPos)
        {
            if (charging)
            {
                if (child == null)
                {
                    child = LazerGenerator.NewLazerGenerator(collection, game);
                    child.Setup(emitterPos + offset, size, lifetime, 0f, 0f);
                    // AUDIBLE, and it used to be SetupSilent() -- reversed by cards 57ea30cd /
                    // c146422f. The old reasoning ("a puppet is not the local shooter") is the
                    // right rule for a remote PLAYER's private business, which is why a remote
                    // powerup pickup is silent (card d53431b4). An ENEMY windup is the opposite
                    // kind of thing: it is a world event both players are dodging, and the whole
                    // point of a telegraph is that you hear it coming. Muting it on the join peer
                    // was a gameplay disadvantage, not politeness. The looped "lazercharge" cue
                    // stops with the child on Free(), same as the host's.
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
