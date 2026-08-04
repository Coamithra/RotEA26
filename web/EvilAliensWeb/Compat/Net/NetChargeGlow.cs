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
        // `easedOffset` is this emitter's own SWEEPING copy of the replicated aim (card eb057163).
        // The replicated `offset` only changes on that emitter's round-robin snapshot turn -- 60ms
        // at best, ~150ms in a real Level 2, longer in a big world -- so assigning it made the glow
        // a STAIRCASE: dead still for a turn, then a jump. That is what "the twin motherships do
        // not change where they are aiming as their target moves" is; MEASURED (NetChargeAimTest
        // section 1) as 15 moving ticks out of 144 over one 2500ms charge, in 7.62px steps.
        //
        // It is EASED toward the newest value rather than EXTRAPOLATED from the rate between the
        // last two. An aim is a CHASE, so its angular rate reverses whenever the player does, and
        // an extrapolated glow would point somewhere the host never aimed and then snap when the
        // real beam fires along the host's true angle -- a telegraph that LIES is worse than one
        // that lags. (The sent-rate treatment in ANCHORED MOTION works for Lazer because a beam's
        // growth rate is a genuine step function; this is not that shape.)
        //
        // `dtMs` is real driver time, not game time, because the whole point is to track a cadence
        // that is stamped on real time -- see NetPuppets.Drive's header.
        public static void Drive(ref LazerGenerator child, ref Vector2 easedOffset, bool charging, Vector2 offset, float windupSeconds, float size, float lifetime, ComponentBin collection, Game game, Vector2 emitterPos, float dtMs)
        {
            if (charging)
            {
                if (child == null)
                {
                    // A CHARGE-ON EDGE STARTS AT THE REPLICATED AIM, never at whatever the previous
                    // charge left behind. The emitters are pooled and this field lives on the
                    // emitter, so a boss winding up a second time would otherwise sweep in from its
                    // LAST beam's aim -- the recycle trap Lazer.SetupSingleShot's `owner` clear and
                    // FlyingSpider's anchor reset both document.
                    easedOffset = offset;
                    child = LazerGenerator.NewLazerGenerator(collection, game);
                    child.Setup(emitterPos + easedOffset, size, lifetime, 0f, 0f);
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
                    easedOffset = EaseToward(easedOffset, offset, dtMs);
                    child.SetPosition(emitterPos + easedOffset);
                }
            }
            else if (child != null)
            {
                child.Free(); // self-removes next Update; the emitter's Draw stops drawing it at once
                child = null;
            }
        }

        // One tick of the sweep. The window is NetPuppets' own cadence-derived correction window
        // (max(150ms, 2 x SnapshotTurnMs)) rather than a constant: the aim arrives on exactly the
        // same round-robin turn the position error does, so it has exactly the same staleness, and
        // a fixed window would degrade with the world size in exactly the way CorrectionWindowFor
        // exists to stop. Read live rather than latched -- unlike a position correction there is no
        // fixed error being drained, just a target being chased, so a spawn burst mid-sweep can
        // rescale the rate with nothing to be inconsistent about.
        //
        // A FRACTION OF WHAT REMAINS, so it converges on the target and never overshoots it, and so
        // the glow comes to REST when the host's aim does -- an emitter whose offset never moves
        // (the big UFO's lazor, the JunkBoss' suck swarm) has target == current and this is a
        // provable no-op for it, which is what lets the five emitters share one rule.
        private static Vector2 EaseToward(Vector2 current, Vector2 target, float dtMs)
        {
            if (!NetHost.Current.ChargeAimEase)
            {
                return target; // ?netaimease=0 -- the pre-card assignment, verbatim
            }
            float window = NetPuppets.CorrectionWindowMsNow;
            if (window <= 0f || dtMs <= 0f)
            {
                return target;
            }
            float take = (dtMs >= window) ? 1f : (dtMs / window);
            return current + (target - current) * take;
        }
    }
}
