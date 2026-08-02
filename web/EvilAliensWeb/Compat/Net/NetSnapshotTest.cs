using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the world-snapshot UNKNOWN-ID attribution (card 48ab9b2f).
    // Invoke with eaNetSnap() from the browser console -- best from the main menu.
    //
    // WHY THIS EXISTS. A snapshot entry whose netId has no puppet takes one of three branches
    // in NetPuppets.OnSnapshotEntry, and they all `return false`. They used to share the single
    // `snapUnk` counter, which made a JIP pass' reading undecidable: a joiner logging
    // snapUnk=344 could be watching perfectly ordinary lane races (the unreliable stream lane
    // outrunning the ordered reliable one) or staring at a fault, and nothing in the log said
    // which. The counters are now split (snapNew / snapDead / snapBad) and this asserts each
    // branch reports the kind it actually took.
    //
    // It is a self-test rather than a two-window run for the usual reason: what is being
    // checked is a CLASSIFICATION, invisible in any frame, and a second peer tab throttles to
    // ~1 tick/sec anyway. It drives the REAL OnSnapshotEntry -- a mirror of the branch logic
    // would agree with itself and prove nothing.
    //
    // Leave-no-trace: every puppet it builds is taken back out of the world and Disable()
    // clears the id maps + the recently-removed ledger, so back-to-back runs read identically.
    internal static class NetSnapshotTest
    {
        // Far above any id a live session realistically reaches (AllocId counts from 1), so a
        // scenario can never collide with a real entry.
        private const ushort IdRebuild = 60101;
        private const ushort IdRefused = 60102;
        private const ushort IdPowerup = 60103;
        private const ushort IdUfo = 60104;

        // The two registry indices section 6 drives. The wire typeIdx IS the registry order
        // (NetTypeRegistry.BuildTable), so these are asserted against the live table rather than
        // trusted -- a reorder would otherwise make the whole section test the wrong descriptor.
        private const byte TypePowerup = 20;
        private const byte TypeUfo = 1;

        // UfoDescriptor's spawn-extra flag bits (private there, restated here rather than
        // widened -- this suite is the only outside reader and a mismatch fails loudly).
        private const byte UfoFlagBonus = 4;
        private const byte UfoFlagUfoSheet = 8;

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

            sb.Append("[netsnap] snapshot unknown-id attribution\n");

            // Enable/Disable here would tear down a real session's puppet layer mid-flight, and
            // building a puppet into a live world would leave a stray enemy in it. Report the
            // skip rather than let an unrun suite read as a pass (the eaBinTest rule).
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                return sb.ToString();
            }

            EvilBullet puppet = null;
            // Section 6 identifies its puppets as "the Powerup/UFO that was NOT there before",
            // exactly as scenario 1 does for its bullet -- a bare type scan would latch onto one
            // the game already owns and the cleanup would then evict it.
            HashSet<GameComponent> preexisting = new HashSet<GameComponent>();
            try
            {
                NetPuppets.Enable(game);
                // Seeded HERE, not inside section 6, so the cleanup in `finally` is correct even
                // if an earlier scenario throws -- an empty set would make it evict every live
                // Powerup and UFO in the world instead of only the ones this suite built.
                preexisting.UnionWith(CollectType<Powerup>(game));
                preexisting.UnionWith(CollectType<UFO>(game));
                NetBaseState state = default(NetBaseState);
                state.Pos = new Vector2(-400f, -400f); // off-screen: never drawn, never collides
                state.Scale = 1f;
                byte[] noExtras = new byte[1];

                // 1. REBUILT -- an id we have never seen. The self-heal builds it from the
                //    snapshot itself. This is what a fresh spawn whose EvSpawn is still in the
                //    reliable lane's queue looks like, i.e. ordinary traffic at the world's
                //    spawn rate. typeIdx 0 = EvilBulletDescriptor, the simplest replicable.
                //    Identify the puppet as the EvilBullet that was NOT there beforehand: a
                //    bare "is there one?" scan would pass vacuously on a pre-existing bullet
                //    (and the cleanup below would then evict a bullet the game still owns).
                HashSet<GameComponent> before = CollectBullets(game);
                bool applied = NetPuppets.OnSnapshotEntry(IdRebuild, 0, state, noExtras, 0, 0,
                    out bool popped, out SnapUnknownKind kind);
                foreach (GameComponent item in CollectBullets(game))
                {
                    if (!before.Contains(item))
                    {
                        puppet = (EvilBullet)item;
                    }
                }
                Check("unknown id reports Rebuilt (was " + kind + ")", kind == SnapUnknownKind.Rebuilt);
                Check("unknown id does not report as applied", !applied);
                // The positive control: a Rebuilt verdict is only meaningful if construction
                // genuinely happened. Without this, a puppet layer that silently built nothing
                // would still satisfy every "kind" assertion below.
                Check("Rebuilt actually put a puppet in the world", puppet != null);
                Check("registry agrees with the world", NetPuppets.LiveCount == (puppet != null ? 1 : 0));

                // 2. NONE -- the same id again, now that it IS puppeted. The entry applies
                //    normally and must not be counted as unknown at all.
                applied = NetPuppets.OnSnapshotEntry(IdRebuild, 0, state, noExtras, 0, 0, out popped, out kind);
                Check("a known id reports None (was " + kind + ")", kind == SnapUnknownKind.None);
                Check("a known id reports as applied", applied);

                // 3. LEFT DEAD -- removed HERE, then streamed again. The host keeps snapshotting
                //    an entity for a turn or two while the death settles, and resurrecting it
                //    would undo the death. NOTE the removal path is the REAL one (the
                //    ComponentRemoved seam), and it fires for host-authoritative EvDeaths just
                //    as much as for our own claims -- which is why snapDead tracks the world's
                //    TOTAL removal rate and NOT clTx, the assumption that made the old
                //    "flat clTx with climbing snapUnk" heuristic unusable on an idle joiner.
                bin.Remove((GameComponent)(object)puppet);
                bin.Update();
                int liveAfterRemoval = NetPuppets.LiveCount;
                applied = NetPuppets.OnSnapshotEntry(IdRebuild, 0, state, noExtras, 0, 0, out popped, out kind);
                Check("a just-removed id reports LeftDead (was " + kind + ")", kind == SnapUnknownKind.LeftDead);
                Check("a just-removed id is NOT resurrected",
                    NetPuppets.LiveCount == liveAfterRemoval && liveAfterRemoval == 0);
                // It was really gone from the world, not merely unregistered -- otherwise the
                // "not resurrected" check above could pass with a live orphan still drawing.
                Check("the removed puppet left the world", !CollectBullets(game).Contains((GameComponent)(object)puppet));
                puppet = null;

                // 4. REFUSED -- a typeIdx with no descriptor. Unlike the two above this is a
                //    fault: nothing can ever be built for it.
                byte badType = (byte)NetTypeRegistry.Count;
                applied = NetPuppets.OnSnapshotEntry(IdRefused, badType, state, noExtras, 0, 0, out popped, out kind);
                Check("an unbuildable typeIdx reports Refused (was " + kind + ")", kind == SnapUnknownKind.Refused);
                Check("Refused builds nothing", NetPuppets.LiveCount == 0);

                // 5. ...and for THIS cause it repeats immediately, which is the whole reason the
                //    split is worth having: Rebuilt happens once per id and LeftDead decays
                //    after RecentRemovalWindowMs, so both are bounded per entity, while an
                //    unbuildable typeIdx re-counts on every snapshot turn for as long as the
                //    host streams that id. (The other two Refused causes mark the id removed
                //    first, so they tick more slowly -- not covered here, hence the narrow
                //    assertion name.) A climbing snapBad is the one shape that means trouble.
                NetPuppets.OnSnapshotEntry(IdRefused, badType, state, noExtras, 0, 0, out popped, out kind);
                Check("an unbuildable typeIdx re-counts on the very next turn (was " + kind + ")",
                    kind == SnapUnknownKind.Refused);

                // ---- 6. A SELF-HEALED PUPPET IS REBUILT BY THE LATER EvSpawn (card de4d5d65) --
                // The self-heal above constructs with NO spawn extras, so every variant the
                // extras pin is the descriptor's DEFAULT: a Powerup keeps Randomize()'s local
                // random type instead of the host's, a bonus UFO gets no SetAsBonus and so draws
                // untinted. Those extras are on the reliable EvSpawn that lands moments later,
                // and dropping it as a duplicate is what froze the wrong look in permanently --
                // the reported symptom being a powerup-carrying UFO whose colour did not match
                // its powerup on the joiner's screen.
                sb.Append("[netsnap] self-healed puppets are corrected by the EvSpawn\n");
                Check("registry index " + TypePowerup + " is the Powerup descriptor",
                    NetTypeRegistry.Get(TypePowerup) is Descriptors.PowerupDescriptor);
                Check("registry index " + TypeUfo + " is the UFO descriptor",
                    NetTypeRegistry.Get(TypeUfo) is Descriptors.UfoDescriptor);

                // 6a. POWERUP -- the type IS the entity's identity (colour AND letter).
                Powerup healed = (Powerup)BuildBySelfHeal<Powerup>(game, IdPowerup, TypePowerup, state, noExtras, Check, preexisting);
                if (healed != null)
                {
                    // Pick a target the self-heal did NOT land on, or the leg passes vacuously
                    // on a lucky roll.
                    Powerup.PowerupType want = healed.type == Powerup.PowerupType.Blast
                        ? Powerup.PowerupType.Linker
                        : Powerup.PowerupType.Blast;
                    byte[] extras = new byte[] { (byte)want };
                    // The stale one is only bin.Remove'd, and that is DEFERRED -- it is still in
                    // Game.Components until the flush below, so park it here or "the new Powerup"
                    // is ambiguous for the next two assertions.
                    preexisting.Add(healed);
                    SpawnRejectKind reject = NetPuppets.OnSpawn(IdPowerup, TypePowerup, state, extras, 0, 1);
                    Powerup rebuilt = (Powerup)SoleOfType<Powerup>(game, preexisting);
                    Check("the EvSpawn for a self-healed id is NOT rejected (was " + reject + ")",
                        reject == SpawnRejectKind.None);
                    Check("the rebuild produced a NEW Powerup", rebuilt != null);
                    Check("the rebuilt Powerup carries the host's type (want " + want
                        + ", was " + (rebuilt != null ? rebuilt.type.ToString() : "none") + ")",
                        rebuilt != null && rebuilt.type == want);
                    Check("the rebuild leaves exactly one puppet registered", NetPuppets.LiveCount == 1);

                    // 6b. THE DETACH-BEFORE-REMOVE ORDERING. bin.Remove is DEFERRED, so the stale
                    //     component's ComponentRemoved fires on this flush -- AFTER the
                    //     replacement took the same netId. Leave the maps in place and that late
                    //     event evicts the REPLACEMENT and marks the id removed, and every
                    //     following snapshot entry reads LeftDead (the puppet stops being
                    //     corrected at all, which no frame would show).
                    bin.Update();
                    Check("the stale self-healed Powerup left the world",
                        !CollectType<Powerup>(game).Contains((GameComponent)(object)healed));
                    NetPuppets.OnSnapshotEntry(IdPowerup, TypePowerup, state, noExtras, 0, 0, out popped, out kind);
                    Check("the rebuilt puppet survives the stale component's deferred removal (was "
                        + kind + ")", kind == SnapUnknownKind.None);

                    // 6c. NEGATIVE CONTROL -- this puppet is no longer self-healed, so a second
                    //     EvSpawn is an ordinary duplicate and must still be refused. Without
                    //     this a rebuild-on-every-duplicate would pass 6a just as well.
                    Powerup live = (Powerup)SoleOfType<Powerup>(game, preexisting);
                    reject = NetPuppets.OnSpawn(IdPowerup, TypePowerup, state, extras, 0, 1);
                    Check("a duplicate EvSpawn for a corrected puppet still reports AlreadyLive (was "
                        + reject + ")", reject == SpawnRejectKind.AlreadyLive);
                    Check("...and rebuilds nothing",
                        ReferenceEquals(SoleOfType<Powerup>(game, preexisting), live));
                    Prune<Powerup>(bin, game, preexisting);
                }

                // 6d. UFO -- the carrier itself. The bonus can only ever be turned OFF by the
                //     state extras, so a self-healed carrier can never regain its tint any other
                //     way. The sheet pick rides along as a second distinguishing property.
                UFO ufo = (UFO)BuildBySelfHeal<UFO>(game, IdUfo, TypeUfo, state, noExtras, Check, preexisting);
                if (ufo != null)
                {
                    Check("a self-healed UFO starts with no bonus", !ufo.NetHasBonus);
                    Check("a self-healed UFO starts on the default (smallship) sheet", !ufo.NetSmallUfoSheet);
                    byte[] extras = new byte[]
                    {
                        UfoFlagBonus | UfoFlagUfoSheet,
                        (byte)Powerup.PowerupType.OneUp,
                    };
                    preexisting.Add(ufo); // deferred removal -- see 6a
                    SpawnRejectKind reject = NetPuppets.OnSpawn(IdUfo, TypeUfo, state, extras, 0, 2);
                    UFO rebuilt = (UFO)SoleOfType<UFO>(game, preexisting);
                    Check("the EvSpawn for a self-healed UFO is NOT rejected (was " + reject + ")",
                        reject == SpawnRejectKind.None);
                    Check("the rebuilt UFO carries a bonus", rebuilt != null && rebuilt.NetHasBonus);
                    Check("the rebuilt UFO's bonus is the host's type (want OneUp, was "
                        + (rebuilt != null ? ((Powerup.PowerupType)rebuilt.NetBonusType).ToString() : "none") + ")",
                        rebuilt != null && rebuilt.NetBonusType == (byte)Powerup.PowerupType.OneUp);
                    Check("the rebuilt UFO is on the host's sheet", rebuilt != null && rebuilt.NetSmallUfoSheet);
                    Prune<UFO>(bin, game, preexisting);
                }
            }
            catch (Exception ex)
            {
                Check("attribution scenarios ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                NetPuppets.Disable();
                // Disable deliberately leaves live puppets to the scene's Terminate purge, but
                // this suite has no scene -- take ours back out ourselves.
                if (puppet != null)
                {
                    bin.Remove((GameComponent)(object)puppet);
                }
                // Section 6's puppets, if a Check short-circuited before its own Prune.
                foreach (GameComponent stray in CollectNew<Powerup>(game, preexisting))
                {
                    bin.Remove(stray);
                }
                foreach (GameComponent stray in CollectNew<UFO>(game, preexisting))
                {
                    bin.Remove(stray);
                }
                bin.Update();
            }

            // ---- the derived snapTurn number the [net] line prints ---------------------------
            // pupPops cannot be judged without this: the snapshot cursor round-robins a fixed
            // number of entries per packet, so a big world stretches how long every puppet
            // dead-reckons blind between corrections, and anything not moving in a straight
            // line then pops on a perfectly healthy link. Pinned here because the docs quote
            // these figures and tools/sim/net_puppet_drive_sim.py sweeps them.
            sb.Append("[netsnap] snapshot turn interval (round-robin cursor)\n");
            Check("no live entities -> 0ms", NetSession.SnapshotTurnMs(0) == 0);
            Check("1 entity -> the packet cadence floor, 60ms", NetSession.SnapshotTurnMs(1) == 60);
            Check("a full packet (16) still 60ms", NetSession.SnapshotTurnMs(16) == 60);
            // The MEAN, not whole packets rounded up: the cursor wraps continuously, so 17
            // entities average 17/16 of a packet interval (~63ms), NOT the 120ms a second whole
            // packet would suggest. Getting this wrong overstates the blind window ~2x on
            // exactly the small worlds it gets read for.
            Check("17 entities -> the MEAN 63ms, not a whole second packet (120ms)",
                NetSession.SnapshotTurnMs(17) == 63);
            Check("32 entities -> 120ms", NetSession.SnapshotTurnMs(32) == 120);
            Check("320 entities -> 1200ms blind between corrections", NetSession.SnapshotTurnMs(320) == 1200);

            sb.Append("[netsnap] ").Append(fail == 0 ? "PASS" : "FAIL")
                .Append(" (").Append(pass).Append(" passed, ").Append(fail).Append(" failed)\n");
            return sb.ToString();
        }

        // ---- section 6 helpers ----------------------------------------------------------------

        // Drive the REAL self-heal for one id and hand back the T it built, recording everything
        // that was already in the world so later scans can tell the puppet apart from it.
        private static GameComponent BuildBySelfHeal<T>(Game game, ushort netId, byte typeIdx,
            in NetBaseState state, byte[] noExtras, Action<string, bool> check,
            HashSet<GameComponent> preexisting) where T : GameComponent
        {
            NetPuppets.OnSnapshotEntry(netId, typeIdx, state, noExtras, 0, 0,
                out bool _, out SnapUnknownKind kind);
            GameComponent built = SoleOfType<T>(game, preexisting);
            // The positive control, as in scenario 1: every assertion below is about what the
            // self-heal produced, so a self-heal that produced nothing must fail here loudly
            // rather than skip quietly.
            check("the self-heal built a " + typeof(T).Name + " (kind " + kind + ")",
                kind == SnapUnknownKind.Rebuilt && built != null);
            return built;
        }

        // The one live T that is not in `preexisting`; null if there are none or more than one
        // (either is a rig fault, and returning it would make the caller's assertion vacuous).
        private static GameComponent SoleOfType<T>(Game game, HashSet<GameComponent> preexisting)
            where T : GameComponent
        {
            GameComponent found = null;
            foreach (GameComponent item in CollectNew<T>(game, preexisting))
            {
                if (found != null)
                {
                    return null;
                }
                found = item;
            }
            return found;
        }

        private static List<GameComponent> CollectNew<T>(Game game, HashSet<GameComponent> preexisting)
            where T : GameComponent
        {
            List<GameComponent> list = new List<GameComponent>();
            foreach (GameComponent item in CollectType<T>(game))
            {
                if (!preexisting.Contains(item))
                {
                    list.Add(item);
                }
            }
            return list;
        }

        private static void Prune<T>(ComponentBin bin, Game game, HashSet<GameComponent> preexisting)
            where T : GameComponent
        {
            foreach (GameComponent item in CollectNew<T>(game, preexisting))
            {
                bin.Remove(item);
            }
            bin.Update();
        }

        private static List<GameComponent> CollectType<T>(Game game) where T : GameComponent
        {
            List<GameComponent> list = new List<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        // The live EvilBullets in the world, so a puppet this suite built can be told apart
        // from any the game already owned.
        private static HashSet<GameComponent> CollectBullets(Game game)
        {
            HashSet<GameComponent> set = new HashSet<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is EvilBullet)
                {
                    set.Add(item);
                }
            }
            return set;
        }
    }
}
