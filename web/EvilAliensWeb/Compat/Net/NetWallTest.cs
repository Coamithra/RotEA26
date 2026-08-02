using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the Level-3 WALL's replication (cards 4392bd30 / 80749dc4).
    // Invoke with eaNetWalls() / `eval NetWalls`; MENU-runnable and leave-no-trace.
    //
    // WHY IT EXISTS. Two reported symptoms, one root cause, and none of it is visible in a frame
    // taken on either peer alone:
    //
    //   * "lvl3 walls go out of sync" -- NetBaseState.Scale rides the wire as a u16 at 1/256 and
    //     the cast TRUNCATES, so the absolute error is up to 1/256 whatever the value. A wall's
    //     scale is 800 / (1248 * gridWidth) -- 0.0534 for the 12-wide Level-3 grid, which
    //     quantizes to 13/256 = 0.0508, a 4.9% error. Wall.Draw sizes every block as
    //     LogicalWidth/Height * scale, so the joiner drew 63.4px rows against the host's 66.7px
    //     and the two peers were ~400px apart by the bottom of that 122-row grid. On EACH screen
    //     the wall looks like a perfectly ordinary wall; only a side-by-side comparison shows it.
    //   * "I hit walls way before I actually do / my bullets vanish before the wall" -- and it is
    //     NOT host-side collision authority (PlayerShip.CollidesWith refuses damage to a
    //     ControlDevice.Remote puppet outright, and a joiner's bullets are never replicated:
    //     both collide against the joiner's OWN wall puppet). CollisionLevelMap sized its tile
    //     from the grid alone, which agreed with the DRAWN block size only because Wall.Setup
    //     derives `scale` from the same 800/width -- so the moment the wire changed `scale`, the
    //     collision rows reached further DOWN the screen than the towers, by 3.3px per row.
    //
    // Both are fixed at the source: Wall.NetScaleLocal keeps the puppet on the scale its own
    // Setup derived from the replicated grid variation, and CollisionLevelMap now takes its tile
    // size from the owner so the two can never disagree silently again. Wall.NetPathAnchored is
    // the third change and belongs to the stutter half -- see section 4.
    //
    // Leave-no-trace, the eaNetSnap shape: the host walls are CONSTRUCTED and never added to the
    // bin or to Game.Components; the puppets go through the real NetPuppets and are removed
    // again, and Disable() clears the id maps.
    internal static class NetWallTest
    {
        // Far above any id a live session reaches (AllocId counts from 1).
        private const ushort IdWall = 60301;
        private const ushort IdWallB = 60302;
        private const ushort IdBullet = 60303;

        // Asserted against the live table below rather than trusted: the wire typeIdx IS the
        // registry order, so a reorder would make the whole suite drive the wrong descriptor.
        private const byte TypeWall = 14;
        private const byte TypeEvilBullet = 0;

        // The grid widths Wall.Setup builds, by variation. Restated here rather than widened out
        // of Wall: the point of section 1 is that the ERROR is a function of the width, so a
        // divergence between this list and Setup's grids fails loudly on the scale comparison
        // (which reads the real wall's own scale, never this table).
        private static readonly int[] VariationWidths = { 12, 7, 7, 9, 3 };

        public static string Run()
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netwalls] Level-3 wall replication (cards 4392bd30 / 80749dc4)\n");

            // Enable/Disable would tear a real session's puppet layer down mid-flight, and a
            // puppet built into a live world would leave a stray wall scrolling through it.
            // Report the skip rather than let an unrun suite read as a pass (the eaBinTest rule).
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                return sb.ToString();
            }

            List<Wall> scratch = new List<Wall>();
            try
            {
                Check("registry index " + TypeWall + " is the Wall descriptor",
                    NetTypeRegistry.Get(TypeWall) is Descriptors.WallDescriptor);
                Check("registry index " + TypeEvilBullet + " is the EvilBullet descriptor",
                    NetTypeRegistry.Get(TypeEvilBullet) is Descriptors.EvilBulletDescriptor);

                sb.Append(" 1. the wire's scale is LOSSY for a wall -- the defect, measured\n");
                SectionWireScale(bin, game, scratch, sb, Check);

                sb.Append(" 2. the opt-out predicate\n");
                SectionPredicate(game, Check);

                NetPuppets.Enable(game);

                sb.Append(" 3. a real puppet keeps its DERIVED scale, and draws where it collides\n");
                SectionPuppetScale(bin, game, scratch, sb, Check);

                sb.Append(" 4. anchored motion -- the scroll speed, not a finite difference\n");
                SectionAnchoredMotion(bin, game, Check);
            }
            finally
            {
                // THE PUPPETS HAVE TO GO BY HAND. NetPuppets.Disable() clears the id maps and the
                // recently-removed ledger; it does NOT remove the components the layer built, so
                // without this every run leaves its walls and its bullet in Game.Components --
                // drawn, in the Oracle scans, and one more set per run. Collected BEFORE Disable,
                // since FindPuppet reads the maps it clears. Same shape as NetSnapshotTest's and
                // NetMotionTest's own cleanups.
                foreach (ushort id in new ushort[] { IdWall, IdWallB, IdBullet })
                {
                    INetEntity puppet = NetPuppets.FindPuppet(id);
                    if (puppet != null)
                    {
                        bin.Remove((GameComponent)(object)puppet);
                    }
                }
                NetPuppets.Disable();
                foreach (Wall w in scratch)
                {
                    bin.Remove(w);
                }
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[netwalls] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        // ---- 1. the wire's scale ------------------------------------------------------------
        //
        // The NEGATIVE CONTROL for the whole card, and it has to come first: everything below
        // asserts that the puppet ignores this number, which means nothing unless the number is
        // shown to be wrong. Driven through the REAL NetProtocol.WriteBaseState/ReadBaseState --
        // an encode/decode pair written here would only agree with itself.
        private static void SectionWireScale(ComponentBin bin, Game game, List<Wall> scratch,
            StringBuilder sb, System.Action<string, bool> check)
        {
            bool anyBig = false;
            bool tileIdentityHolds = true;
            float worstPct = 0f;
            for (int v = 0; v < VariationWidths.Length; v++)
            {
                Wall host = NewHostWall(bin, game, v, scratch);
                float exact = host.scale;

                // OFFLINE IDENTITY, asserted rather than assumed: for a wall on its own derived
                // scale, the drawn block size and the old hard-coded 800/width are BIT-EQUAL --
                // float32 rounding included, at every shipped width. That is what makes taking
                // the tile size from the owner a behaviour-neutral change in single-player, and
                // it is also the coincidence the wire broke.
                float drawn = (float)host.texture.LogicalWidth() * exact;
                float hardCoded = 800f / (float)VariationWidths[v];
                if (drawn != hardCoded) { tileIdentityHolds = false; }

                NetBaseState state = default(NetBaseState);
                state.Scale = exact;
                byte[] buf = new byte[NetProtocol.BaseStateBytes];
                int off = 0;
                NetProtocol.WriteBaseState(buf, ref off, state);
                NetBaseState back = default(NetBaseState);
                off = 0;
                NetProtocol.ReadBaseState(buf, ref off, ref back);

                float pct = System.Math.Abs(back.Scale - exact) / exact * 100f;
                if (pct > worstPct) { worstPct = pct; }
                if (pct > 1f) { anyBig = true; }
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "       variation {0} (width {1}): scale {2:F6} -> wire {3:F6}, {4:F2}% out\n",
                    v, VariationWidths[v], exact, back.Scale, pct));
            }

            check("offline, the drawn block size IS the old 800/width, bit for bit",
                tileIdentityHolds);
            // CollisionLevelMap divides X and Y by ONE tile size (it always has), while Draw sizes
            // rows as LogicalHeight * scale -- so the grid lines up with the towers only while the
            // sheet is square. 756-v1 is 1248x1248 and is the only wall sheet; this is what makes
            // a non-square replacement fail loudly rather than mis-align every row.
            check("the wall sheet is SQUARE, which the single tile size assumes",
                scratch[0].texture.LogicalWidth() == scratch[0].texture.LogicalHeight());
            check("the wire ROUNDS every wall scale (u16 at 1/256 truncates)", worstPct > 0f);
            check("...and by more than 1% on at least one shipped grid", anyBig);

            // The specific figure the card's screenshot is: variation 0 is the 12-wide, 122-row
            // Level-3 grid, and a 4.9% error on a 66.7px row is 3.3px, which ACCUMULATES down the
            // grid. This is what the two peers were looking at.
            Wall v0 = NewHostWall(bin, game, 0, scratch);
            float rowExact = (float)v0.texture.LogicalHeight() * v0.scale;
            NetBaseState s0 = default(NetBaseState);
            s0.Scale = v0.scale;
            byte[] b0 = new byte[NetProtocol.BaseStateBytes];
            int o0 = 0;
            NetProtocol.WriteBaseState(b0, ref o0, s0);
            NetBaseState r0 = default(NetBaseState);
            o0 = 0;
            NetProtocol.ReadBaseState(b0, ref o0, ref r0);
            float rowWire = (float)v0.texture.LogicalHeight() * r0.Scale;
            float driftAtBottom = (rowExact - rowWire) * 122f;
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "       variation 0 rows: {0:F2}px exact vs {1:F2}px off the wire"
                + " -> {2:F0}px apart by row 122\n", rowExact, rowWire, driftAtBottom));
            check("the pre-card divergence down variation 0 exceeds half a screen (>300px)",
                driftAtBottom > 300f);
        }

        // ---- 2. the predicate ---------------------------------------------------------------
        private static void SectionPredicate(Game game, System.Action<string, bool> check)
        {
            check("Wall is scale-local (it derives scale from the replicated variation)",
                ((INetEntity)new Wall(game)).NetScaleLocal);
            check("Wall is path-anchored (it moves by Speed/Direction and nothing else)",
                ((INetEntity)new Wall(game)).NetPathAnchored);

            // The CONTROLS, and they are not decoration: a predicate hard-wired to true would
            // satisfy both positives above AND be actively wrong. A UFO's scale is caller-chosen
            // per spawn and its flight is a scripted Position curve, so it must keep taking the
            // replicated scale and the observed velocity.
            check("a UFO is NOT scale-local -- control",
                !((INetEntity)new UFO(game)).NetScaleLocal);
            check("a UFO is NOT path-anchored -- control",
                !((INetEntity)new UFO(game)).NetPathAnchored);
        }

        // ---- 3. the puppet -------------------------------------------------------------------
        //
        // END TO END through the real NetPuppets: a spawn carrying the grid variation, then a
        // snapshot turn carrying the QUANTIZED scale, which is exactly what a host sends. The
        // puppet must end up on the number its own Setup derived, and -- the leg that carries the
        // hit-before-you-touch-it half of the card -- its collision tile must equal its DRAWN
        // block. The EvilBullet beside it is the control: an ordinary type still adopts the wire's
        // scale, so this is an opt-out and not a layer that stopped replicating scale at all.
        private static void SectionPuppetScale(ComponentBin bin, Game game, List<Wall> scratch,
            StringBuilder sb, System.Action<string, bool> check)
        {
            Wall reference = NewHostWall(bin, game, 0, scratch);
            float exact = reference.scale;
            float quantized = ThroughWire(exact);

            NetBaseState state = default(NetBaseState);
            state.Pos = new Vector2(0f, -9000f); // far above the screen: never drawn, never collides
            state.Scale = quantized;
            byte[] extras = new byte[1];
            extras[0] = 0; // variation 0

            SpawnRejectKind reject = NetPuppets.OnSpawn(IdWall, TypeWall, state, extras, 0, 1);
            check("the wall puppet was built from the spawn extras", reject == SpawnRejectKind.None);

            Wall puppet = NetPuppets.FindPuppet(IdWall) as Wall;
            check("...and it is a Wall", puppet != null);
            if (puppet == null)
            {
                return;
            }

            // A snapshot turn, through the real entry point, carrying the same quantized scale a
            // host really sends -- this is the write the fix refuses.
            bool popped;
            SnapUnknownKind kind;
            NetPuppets.OnSnapshotEntry(IdWall, TypeWall, NetProtocol.NetSnapshotFlags.None,
                state, extras, 0, 0, out popped, out kind);
            // Drive well past the scale lerp's ~100ms time constant: the pre-card code converged
            // on TargetScale rather than assigning it, so a short drive would pass either way.
            for (int i = 0; i < 60; i++)
            {
                NetPuppets.Drive(16.7f);
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "       puppet scale {0:F6} (host {1:F6}, wire offered {2:F6})\n",
                puppet.scale, exact, quantized));
            check("the puppet is on the host's EXACT scale, not the wire's",
                Near(puppet.scale, exact, 1e-6f));
            check("...and the wire's offer really was different (control)",
                System.Math.Abs(quantized - exact) > 1e-6f);

            // THE INVARIANT THE SECOND SYMPTOM WAS ABOUT. Read off the live objects, not restated
            // from a formula -- a formula here would agree with whichever copy of it broke.
            float drawnBlock = (float)puppet.texture.LogicalWidth() * puppet.scale;
            CollisionLevelMap map = (CollisionLevelMap)puppet.GetCollisionType();
            check("the puppet's collision tile IS its drawn block size",
                Near(map.TileSize, drawnBlock, 1e-4f));
            // The pre-card numbers for the same puppet: it would have DRAWN on the wire's scale
            // while the grid still collided on the exact 800/width, so the collision rows reached
            // that much further down the screen with every row.
            float preCardDrawn = (float)puppet.texture.LogicalWidth() * quantized;
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "       drawn block {0:F3}px, collision tile {1:F3}px"
                + " (pre-card: drew {2:F3}px against a {3:F3}px collision tile,"
                + " {4:F2}px of drift per row)\n",
                drawnBlock, map.TileSize, preCardDrawn, 800f / 12f, 800f / 12f - preCardDrawn));

            // THE GUARD, ASSERTED SEPARATELY BECAUSE THE FIX ABOVE MAKES IT VACUOUS. With the
            // puppet on its derived scale, 800/width and LogicalWidth*scale agree again -- which
            // is exactly the coincidence that let the two drift apart in the first place, so the
            // invariant leg above cannot tell a derived tile size from the old hard-coded one.
            // Forcing a scale onto the wall by hand reproduces the pre-card condition (that is
            // what the wire used to do) and requires the grid to FOLLOW it.
            Wall forced = NewHostWall(bin, game, 0, scratch);
            float forcedExact = forced.scale;
            ((INetEntity)forced).NetScale = quantized;
            CollisionLevelMap forcedMap = (CollisionLevelMap)forced.GetCollisionType();
            check("the collision tile tracks the wall's scale whatever sets it",
                Near(forcedMap.TileSize, (float)forced.texture.LogicalWidth() * quantized, 1e-4f));
            check("...and that really is a different number from the derived one (control)",
                System.Math.Abs(forcedExact - quantized) > 1e-6f);

            // The CONTROL for the opt-out: an ordinary replicable type still takes the wire's
            // scale, so this is Wall's decision and not the layer having stopped applying scale.
            NetBaseState bulletState = default(NetBaseState);
            bulletState.Pos = new Vector2(-4000f, -4000f);
            bulletState.Scale = 0.5f;
            byte[] none = new byte[1];
            NetPuppets.OnSpawn(IdBullet, TypeEvilBullet, bulletState, none, 0, 0);
            EvilBullet bullet = NetPuppets.FindPuppet(IdBullet) as EvilBullet;
            if (bullet == null)
            {
                check("control: an EvilBullet puppet was built", false);
            }
            else
            {
                bulletState.Scale = 0.25f;
                NetPuppets.OnSnapshotEntry(IdBullet, TypeEvilBullet,
                    NetProtocol.NetSnapshotFlags.None, bulletState, none, 0, 0, out popped, out kind);
                for (int i = 0; i < 60; i++)
                {
                    NetPuppets.Drive(16.7f);
                }
                check("control: an ordinary puppet DOES adopt the wire's scale",
                    Near(bullet.scale, 0.25f, 0.01f));
            }
        }

        // ---- 4. anchored motion ---------------------------------------------------------------
        //
        // The stutter half. A wall scrolls straight down at |oracle.BackgroundSpeed| and moves by
        // Speed/Direction alone, so its DECLARED velocity is honest and the host has no business
        // differencing two positions across a snapshot turn on the real clock to guess at it --
        // that difference carries the host's frame pacing, and a level's `speedup` reaches the
        // client a whole turn late. Anchoring it also makes a speed change a step the velocity
        // ease absorbs, which is the "resync on a scroll-speed change" the card asked for.
        private static void SectionAnchoredMotion(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            // 4a. the HOST's decision, on synthetic samples (the pure part -- NetMotionTest's
            //     ResolveBaseVelocity idiom). The declared vector is the true scroll; the sampled
            //     positions carry a little pacing noise, which is what an anchored entity must
            //     not pick up.
            Vector2 declared = new Vector2(0f, 0.31f); // Level 3 at Very_Hard
            Vector2 last = new Vector2(0f, -5000f);
            Vector2 now = new Vector2(0f, -4968f); // 32px over 100ms, not 31

            Vector2 anchored = NetSession.ResolveBaseVelocity(
                declared, anchored: true, teleported: false, now, true, last, 0L, 100L);
            Vector2 observed = NetSession.ResolveBaseVelocity(
                declared, anchored: false, teleported: false, now, true, last, 0L, 100L);
            check("the host sends a wall's DECLARED scroll velocity", anchored == declared);
            check("...where the pre-card path differenced the samples instead (control)",
                observed != declared);

            // 4b. the CLIENT's half, end to end: the puppet dead-reckons at the sent scroll speed,
            //     an ordinary drift is blended rather than snapped, and a scroll-speed change
            //     arrives as a nudge instead of a step.
            NetBaseState state = default(NetBaseState);
            state.Pos = new Vector2(0f, -9000f);
            state.Scale = 0.05f;
            state.Vel = declared;
            byte[] extras = new byte[1];
            extras[0] = 1; // variation 1 -- a different grid from section 3's, so no id reuse
            NetPuppets.OnSpawn(IdWallB, TypeWall, state, extras, 0, 1);
            Wall puppet = NetPuppets.FindPuppet(IdWallB) as Wall;
            if (puppet == null)
            {
                check("a second wall puppet was built for the motion legs", false);
                return;
            }

            float before = puppet.Position.Y;
            NetPuppets.Drive(100f);
            check("a wall puppet dead-reckons at the sent scroll speed",
                Near(puppet.Position.Y - before, 31f, 0.5f));

            // An ordinary correction BLENDS. Stated with a real error in it -- a snapshot placed
            // exactly where the puppet already is cannot pop whatever the layer does, so asserting
            // !popped there would pass on a build that snapped on every entry. 40px is a plausible
            // turn's drift and is under SnapThresholdPx (100); the 400px entry beside it is the
            // control that makes the first line mean something.
            bool popped;
            SnapUnknownKind kind;
            state.Pos = puppet.Position + new Vector2(0f, 40f);
            NetPuppets.OnSnapshotEntry(IdWallB, TypeWall, NetProtocol.NetSnapshotFlags.None,
                state, extras, 0, 0, out popped, out kind);
            check("a wall's ordinary drift is BLENDED, not snapped", !popped);

            state.Pos = puppet.Position + new Vector2(0f, 400f);
            NetPuppets.OnSnapshotEntry(IdWallB, TypeWall, NetProtocol.NetSnapshotFlags.None,
                state, extras, 0, 0, out popped, out kind);
            check("...while a 400px error DOES snap (the control)", popped);

            // Drain that correction before measuring the velocity ease, or the two would be
            // superimposed and the step below would read whatever the blend happened to add.
            for (int i = 0; i < 60; i++)
            {
                NetPuppets.Drive(16.7f);
            }

            // The speedup: Level 3 multiplies the scroll by 4.3, so this is the real shape of the
            // event, not a synthetic one. The position is left exactly where the puppet already
            // is, so the correction contributes nothing and this leg is about velocity alone.
            state.Pos = puppet.Position;
            state.Vel = declared * 2f;
            NetPuppets.OnSnapshotEntry(IdWallB, TypeWall, NetProtocol.NetSnapshotFlags.None,
                state, extras, 0, 0, out popped, out kind);

            float atChange = puppet.Position.Y;
            NetPuppets.Drive(16.7f);
            float step = puppet.Position.Y - atChange;
            float oldStep = 0.31f * 16.7f;
            float newStep = 0.62f * 16.7f;
            check("a scroll-speed change EASES in rather than stepping",
                step > oldStep && step < oldStep + (newStep - oldStep) * 0.5f);

            // ...and it does converge, so the ease is not a leak that leaves the wall permanently
            // behind the host's.
            for (int i = 0; i < 60; i++)
            {
                NetPuppets.Drive(16.7f);
            }
            float settled = puppet.Position.Y;
            NetPuppets.Drive(100f);
            check("...and converges on the new speed, so the ease is not a leak",
                Near(puppet.Position.Y - settled, 62f, 1f));
        }

        // ---- helpers ---------------------------------------------------------------------------

        // A "host" wall: built through the real factory + Setup, exactly as the Walls event does,
        // but never added to the world. Recycle<Wall>() may hand one out of the pool, so every one
        // of these goes back through bin.Remove on the way out.
        private static Wall NewHostWall(ComponentBin bin, Game game, int variation, List<Wall> scratch)
        {
            Wall w = Wall.NewWall(bin, game);
            w.Setup(variation);
            scratch.Add(w);
            return w;
        }

        private static float ThroughWire(float scale)
        {
            NetBaseState s = default(NetBaseState);
            s.Scale = scale;
            byte[] buf = new byte[NetProtocol.BaseStateBytes];
            int off = 0;
            NetProtocol.WriteBaseState(buf, ref off, s);
            NetBaseState back = default(NetBaseState);
            off = 0;
            NetProtocol.ReadBaseState(buf, ref off, ref back);
            return back.Scale;
        }

        private static bool Near(float a, float b, float tol)
        {
            return System.Math.Abs(a - b) < tol;
        }
    }
}
