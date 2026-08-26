namespace EvilAliensWeb.Compat.Net
{
    // A free-running BODY LOOP is the CLIENT'S to run -- card 5f506d11.
    //
    // `AlienDrawableGameComponent.NetFrameLocal` already says this about `curframe`: a puppet
    // whose sheet just loops at a constant fps must advance it locally off the driver's real dt,
    // not take a replicated copy once per snapshot turn. This is the same rule for the types that
    // do NOT animate through `curframe` at all -- they keep their own `animationProgress`
    // accumulator, draw `sprite.Draw((int)animationProgress, ...)`, and so needed a per-type
    // `NetAnimFrame` state extra instead of the base seam.
    //
    // WHY IT MATTERS EXACTLY AS MUCH HERE. The wire byte only changes on that entity's own
    // round-robin TURN, which is 60 ms in a small world and up to ~1.2 s in a big one, against a
    // 20 fps (50 ms) loop -- so the animation does not slow down, it STAIRCASES: it stands still
    // and then jumps several frames at once. Measured on `BattleSkull` over 60 driven ticks with
    // a 150 ms turn: the host advanced on 20 of them in steps of 1, the puppet on 6 in steps of
    // 3. That is the card's "quite jerky", and it gets worse with the world size. The stream lane
    // is also unordered and unsequenced, so a late entry hands back an OLDER frame and the loop
    // kicks BACKWARD -- the same disturbance `NetFrameLocal`'s header describes.
    //
    // ---- THE AUDIT IS THE WHOLE RISK, and it is `NetFrameLocal`'s two questions ---------------
    //
    // A type may own its loop only if BOTH hold:
    //   (i)  nothing but `Draw` reads the accumulator -- so a local phase that differs from the
    //        host's by a frame or two can never change a decision, only a pixel; and
    //   (ii) it really is a free-running loop at a CONSTANT fps that no state machine writes --
    //        so a puppet, whose `Update` never runs, cannot drift away from what the host is
    //        doing (the `MarsBoss` failure mode: its fps is re-derived from HP every `Update`).
    //
    // All four types carrying a `NetAnimFrame` seam were audited for this card:
    //
    //   BattleSkull   `+= dt * 20f`, unconditional in Update; read only by Draw     -> LOCAL
    //   ClassicBoss   same                                                          -> LOCAL
    //   FakeBoss      same                                                          -> LOCAL
    //   SpiderBoss    the rear-up/launch/land choreography assigns it outright
    //                 (`animationProgress = 0f` in four places), `animFps` varies,
    //                 `currentAnimation` swaps between four sheets, and Update/DoMove
    //                 READ it (`animationProgress > 30f` gates the walk)  -> STAYS REPLICATED
    //
    // The audit is greppable and worth re-running if a type is added: `animationProgress` outside
    // a Draw, and any `= ` assignment to one that is not this helper.
    //
    // THE HOST STILL ENCODES THE BYTE. Nothing is removed from the wire, so the protocol is
    // unchanged and an older peer keeps animating exactly as it did -- the `NetFrameLocal`
    // precedent again, where `NetBaseState.CurFrame` still ships for everyone and the types that
    // own their frame simply ignore it.
    internal static class NetBodyAnim
    {
        // Advance a puppet's own animation accumulator by one driver tick. `frames` is the
        // sheet's length; a non-positive one (a puppet whose LoadContent has not run yet, which
        // the driver can reach on the tick a puppet is built) leaves the value alone rather than
        // dividing by it.
        internal static float Advance(float progress, float dtSeconds, float fps, int frames)
        {
            if (frames <= 0)
            {
                return progress;
            }
            return EvilAliens.MyMath.Mod(progress + dtSeconds * fps, frames);
        }
    }
}
