using System.Collections.Generic;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the decorative-swarm replication (card 9a3175d0), in the
    // eaKickTest / eaSlotTest / eaNetBgTest idiom: run `eaNetCosmetic()` and read PASS/FAIL.
    //
    // WHY a data test and not two windows. The whole point of this feature is that the two
    // peers' scenery is in DIFFERENT places, so a screenshot diff -- the usual co-op check --
    // cannot say anything at all here. What can go wrong is invisible instead:
    //   * the instance predicate says "cosmetic" for something that is actually a live hazard
    //     (or, the other way, quietly drops a real enemy from replication);
    //   * a byte-layout slip in EvCosmeticSwarm, which would decode as the wrong kind or an
    //     absurd spawn rate;
    //   * the client's own spawns being swallowed by the bin's SuppressWorldSpawn filter, which
    //     shows up as "the joiner has no scenery" and moves NO counter anywhere.
    //
    // It drives the REAL objects (each type's own Setup/SetBackground) and the REAL codec, and
    // it always runs a POSITIVE CONTROL beside every negative one -- a predicate that answered
    // "not replicated" for everything would pass a test that only checked the fog spiders, and
    // that predicate would silently stop replicating the whole game.
    //
    // Leave-no-trace: nothing is added to the bin, and the apply leg (which needs a GameScene)
    // restores whatever swarms were running. Safe at the menu and mid-level.
    internal static class NetCosmeticTest
    {
        public static string Run()
        {
            List<string> fails = new List<string>();
            int checks = 0;

            void Check(bool ok, string what)
            {
                checks++;
                if (!ok)
                {
                    fails.Add(what);
                }
            }

            // ---- 1. the wire codec ------------------------------------------------------

            byte[] on = NetProtocol.EncodeCosmeticSwarmEvent(
                7, (byte)NetCosmeticKind.FlyingSpiderBackground, on: true, 5.5f);
            Check(on.Length == 10, "EvCosmeticSwarm is 10 bytes");
            Check(on[0] == NetProtocol.MsgEvent && on[1] == NetProtocol.EvCosmeticSwarm,
                "EvCosmeticSwarm encodes [MsgEvent][EvCosmeticSwarm][seq:2][kind][on][rate:4]");
            Check(NetProtocol.ReadU16(on, 2) == 7, "event seq round-trips");
            Check(on[4] == (byte)NetCosmeticKind.FlyingSpiderBackground && on[5] == 1,
                "kind + on round-trip");
            Check(NetProtocol.ReadF32(on, 6) == 5.5f, "rate round-trips exactly");

            byte[] off = NetProtocol.EncodeCosmeticSwarmEvent(
                8, (byte)NetCosmeticKind.BackgroundAsteroids, on: false, 4f);
            Check(off[4] == (byte)NetCosmeticKind.BackgroundAsteroids && off[5] == 0,
                "the second kind + off round-trip");
            Check(NetProtocol.ReadF32(off, 6) == 0f, "an off beat carries no rate");

            // The wire value IS the enum value and the table is append-only -- a reorder would
            // turn one peer's fog spiders into the other's asteroids.
            Check((byte)NetCosmeticKind.FlyingSpiderBackground == 0
                && (byte)NetCosmeticKind.BackgroundAsteroids == 1, "NetCosmeticKind wire values are pinned");

            // ---- 2. the instance predicate ----------------------------------------------

            // Built with `new` rather than the NewX factories on purpose: those Recycle<T>() out
            // of the bin's idle pool, and this test never Adds, so it would consume a pooled
            // instance the game meant to reuse. The construction that MATTERS -- each type's own
            // Setup/SetBackground, which is what decides the flag -- is identical either way.
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            FlyingSpider fog = new FlyingSpider(game);
            fog.Setup(isbackground: true);
            FlyingSpider realSpider = new FlyingSpider(game);
            realSpider.Setup(isbackground: false);

            Check(!NetTypeRegistry.IsReplicableInstance((GameComponent)(object)fog),
                "a background FlyingSpider is NOT replicated per entity");
            Check(NetTypeRegistry.IsReplicableInstance((GameComponent)(object)realSpider),
                "a FOREGROUND FlyingSpider still is (positive control)");

            Asteroid decoration = new Asteroid(game);
            decoration.Setup(Vector2.Zero, 0f, 0.38f, reallyBig: false);
            decoration.SetBackground();
            Asteroid realRock = new Asteroid(game);
            realRock.Setup(Vector2.Zero, 0f, 0.38f, reallyBig: false);

            Check(!NetTypeRegistry.IsReplicableInstance((GameComponent)(object)decoration),
                "a SetBackground() Asteroid is NOT replicated per entity");
            Check(NetTypeRegistry.IsReplicableInstance((GameComponent)(object)realRock),
                "an ordinary Asteroid still is (positive control)");

            // The opt-out is per INSTANCE. If it had leaked into the type table instead, the
            // foreground forms would stop replicating too and the wire typeIdx numbering would
            // shift under every other descriptor.
            Check(NetTypeRegistry.IsReplicable((GameComponent)(object)fog)
                && NetTypeRegistry.IsReplicable((GameComponent)(object)decoration),
                "both types are still in the wire table (the opt-out is per instance)");

            // The two conditions the opt-out is only allowed under (AlienDrawableGameComponent
            // .NetCosmeticOnly documents them): nothing that can hurt anyone, and nothing the AI
            // can see. Collides is what both reduce to, and it is the AI's own gate.
            Check(!fog.Collides && !decoration.Collides,
                "every opted-out instance is Collides=false");
            Check(realSpider.Collides && realRock.Collides,
                "the replicated forms are collidable (positive control)");

            // ---- 3. the client apply path (needs a level) --------------------------------

            string applyReport = GameScene.NetActiveScene?.NetCosmeticSelfTest();

            StringBuilder sb = new StringBuilder();
            sb.Append("[cosmetictest] ").Append(fails.Count == 0 ? "PASS" : "FAIL")
              .Append(" (").Append(checks - fails.Count).Append('/').Append(checks).Append(" checks)");
            foreach (string f in fails)
            {
                sb.Append("\n  FAILED: ").Append(f);
            }
            sb.Append("\n  covers: the EvCosmeticSwarm codec + the instance opt-out predicate.");
            sb.Append(applyReport != null
                ? "\n  " + applyReport
                // A skipped leg must never read as a passed one.
                : "\n  SKIPPED (no level up): the client apply path. Re-run from inside a level to cover it.");
            sb.Append("\n  NOT covered (two-window run): the beat actually reaching the peer, and"
                + "\n    the host's liveIds / snapTurn dropping. Read those off the [net] line.");
            return sb.ToString();
        }
    }
}
