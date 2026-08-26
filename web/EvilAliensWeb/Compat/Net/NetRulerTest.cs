using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The level-3 "parade of alien rulers" (BattleSkull) on a JOINING PEER -- card 5f506d11.
    // Run `eaNetRuler()` from the MAIN MENU, or `eval NetRuler` under eahl.
    internal static class NetRulerTest
    {
        private const char NL = '\n';
        private static readonly Vector2 Nowhere = new Vector2(-4000f, -4000f);
        private const ushort IdSkullA = 61101;
        private const ushort IdSkullB = 61102;
        private const ushort IdSkullC = 61103;
        private const ushort IdSkullD = 61104;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netruler] the level-3 alien ruler (BattleSkull) on a joining peer (card 5f506d11)\n");
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            List<GameComponent> planted = new List<GameComponent>();
            INetHost hostBefore = NetHost.Current;
            PinnedNetHost clock = new PinnedNetHost();
            NetHost.Current = clock;
            try
            {
                NetPuppets.Enable(game);
                SectionAnim(sb, Check, bin, game, planted);
                SectionPause(sb, Check, bin, game, planted);
                SectionGhost(sb, Check, bin, game, planted, clock);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 9. teardown\n");
                foreach (GameComponent c in planted)
                {
                    if (InWorld(game, c)) { bin.Remove(c); }
                }
                NetPuppets.Disable();
                bin.TopOfTickFlush();
                foreach (GameComponent c in CollectType<Explosion>(game)) { bin.Remove(c); }
                bin.TopOfTickFlush();
                bool anyLeft = false;
                foreach (GameComponent c in planted) { anyLeft |= InWorld(game, c); }
                Check("every entity this suite planted left the world", !anyLeft);
                Check("the puppet layer is disabled again", NetPuppets.LiveCount == 0);
                Check("no death FX component was left in the world", CountType<Explosion>(game) == 0);
                NetHost.Current = hostBefore;
            }
            // TAGGED, so a probe can anchor on it -- an untagged "16 passed" is what a dozen
            // other suites print too (the NetDeathFxTest.Tally shape).
            sb.Append("[netruler] ").Append(pass).Append(" passed, ").Append(fail)
              .Append(" failed").Append(NL);
            return sb.ToString();
        }

        // ---- 1. the body animation -------------------------------------------------------
        private static void SectionAnim(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 1. body animation -- a puppet must run its own 20fps loop\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));

            // HOST CONTROL: a real BattleSkull, ticked at 60Hz for 1s.
            BattleSkull host = BattleSkull.NewBattleSkull(bin, game);
            host.Setup(Nowhere);
            bin.Add((GameComponent)(object)host);
            planted.Add((GameComponent)(object)host);
            host.Position = Nowhere;
            List<int> hostFrames = new List<int>();
            for (int i = 0; i < 60; i++)
            {
                Tick((GameComponent)(object)host, 1);
                hostFrames.Add(host.NetAnimFrame);
            }

            BattleSkull pup = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkullA, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", pup != null);
            if (pup == null) { return; }

            // 60 driven ticks with a snapshot turn every 9 ticks (~150ms), each carrying the
            // frame the host would be showing at that moment.
            int loop = pup.NetAnimFrameCount;
            sb.Append("    the alienboss loop is ").Append(loop).Append(" frames").Append(NL);
            List<int> pupFrames = new List<int>();
            for (int i = 0; i < 60; i++)
            {
                if (i % 9 == 0)
                {
                    SnapshotWithExtra(IdSkullA, skullType, hp: 25, extra: new byte[] { (byte)hostFrames[i] });
                }
                NetPuppets.Drive(16.6667f);
                pupFrames.Add(pup.NetAnimFrame);
            }

            sb.Append("    host   frames: ").Append(Describe(hostFrames)).Append(NL);
            sb.Append("    puppet frames: ").Append(Describe(pupFrames)).Append(NL);
            int hostAdv = Advances(hostFrames), pupAdv = Advances(pupFrames);
            sb.Append("    host advanced on ").Append(hostAdv).Append("/60 ticks, max step ")
              .Append(MaxStep(hostFrames, loop)).Append("; puppet advanced on ").Append(pupAdv)
              .Append("/60 ticks, max step ").Append(MaxStep(pupFrames, loop)).Append(NL);
            Check("the puppet advances its body animation as often as the host does (host "
                + hostAdv + ", puppet " + pupAdv + ")", Math.Abs(hostAdv - pupAdv) <= 1);
            Check("...one frame at a time, never a multi-frame jump (puppet max step "
                + MaxStep(pupFrames, loop) + ")", MaxStep(pupFrames, loop) <= 1);

            // 1b. THE LOOP IS OURS, so a wire frame no longer moves it. In steady state the two
            // peers run the same 20fps loop and agree, so this is the leg that discriminates:
            // the host's clock is GAME time and the driver's is REAL time, they drift, and a
            // per-turn re-snap would kick the animation once every snapshot turn forever.
            // A frame four ahead of what the puppet is showing, i.e. a fifth of a second of drift.
            int frameBefore = pup.NetAnimFrame;
            SnapshotWithExtra(IdSkullA, skullType, hp: 25,
                extra: new byte[] { (byte)((frameBefore + 4) % pup.NetAnimFrameCount) });
            sb.Append("    a wire frame 4 ahead: ").Append(frameBefore).Append(" -> ")
              .Append(pup.NetAnimFrame).Append(NL);
            Check("a snapshot frame does NOT move the puppet's own loop (" + frameBefore + " -> "
                + pup.NetAnimFrame + ")", pup.NetAnimFrame == frameBefore);
        }

        // ---- 2. a release under a PAUSE --------------------------------------------------
        private static void SectionPause(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 2. a ruler released mid-death while the game is PAUSED\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));
            BattleSkull pup = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkullB, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", pup != null);
            if (pup == null) { return; }

            bin.Push();                    // the pause freezes the world
            Check("PRECONDITION the pause froze the world", !pup.Enabled);
            int boom = CountType<Explosion>(game);
            NetPuppets.OnDeathBegan(IdSkullB);   // the host's EvDying lands DURING the pause
            sb.Append("    release under pause: enabled=").Append(pup.Enabled)
              .Append(" (+").Append(CountType<Explosion>(game) - boom).Append(" explosions)\n");
            // Tick it exactly as the world would: only while it is ENABLED and still IN the
            // world. A component keeps `Enabled` after its own Die(), so ticking past the
            // removal would spend the rest of the budget on the dying branch's finale (whose
            // `DeathTimer.Finished` stays true forever) and report a number the game cannot
            // produce -- the pre-card figure reads ~40, not ~150.
            for (int i = 0; i < 200; i++)
            {
                if (pup.Enabled && InWorld(game, (GameComponent)(object)pup))
                {
                    Tick((GameComponent)(object)pup, 1);
                }
                bin.TopOfTickFlush();
            }
            int during = CountType<Explosion>(game) - boom;
            sb.Append("    after 200 paused ticks: +").Append(during).Append(" explosions, inWorld=")
              .Append(InWorld(game, (GameComponent)(object)pup)).Append('\n');
            Check("the death animation does NOT run while the game is paused (+" + during
                + " explosions past the opening pop)", during <= 1);
            bin.Pop();
        }

        // ---- 3. the ghost rebuild --------------------------------------------------------
        private static void SectionGhost(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted, PinnedNetHost clock)
        {
            sb.Append(" 3. a released ruler must never be self-healed back, however long the host streams it\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));
            BattleSkull pup = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkullC, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", pup != null);
            if (pup == null) { return; }
            NetPuppets.OnDeathBegan(IdSkullC);
            Check("PRECONDITION the puppet was released", pup.Enabled);
            int ticks = TickUntilGone((GameComponent)(object)pup, bin, game, 400);
            sb.Append("    the released ruler finished dying in ").Append(ticks).Append(" ticks\n");

            // A LONG PAUSE: the host's world is frozen too, so its copy never finishes dying and
            // it keeps streaming the id. The clock jump is what the pre-card real-time window
            // could not survive -- five minutes, i.e. an order of magnitude past the 30 s it used
            // to lapse after, so this cannot pass by being just inside a bigger constant.
            clock.Now += 300000L;
            int worldBefore = CountType<BattleSkull>(game);
            int boom = CountType<Explosion>(game);
            SnapshotKind(IdSkullC, skullType, hp: 0, out SnapUnknownKind kind);
            sb.Append("    snapshot after the window lapsed: ").Append(kind)
              .Append(", BattleSkulls ").Append(worldBefore).Append("->")
              .Append(CountType<BattleSkull>(game)).Append('\n');
            Check("the id still reports LeftDead, not Rebuilt (was " + kind + ")",
                kind == SnapUnknownKind.LeftDead);
            Check("...and no replacement ruler is built", CountType<BattleSkull>(game) == worldBefore);
            SnapshotKind(IdSkullC, skullType, hp: 0, out _);
            SnapshotKind(IdSkullC, skullType, hp: 0, out _);
            Check("...and no second death plays (+" + (CountType<Explosion>(game) - boom)
                + " explosions)", CountType<Explosion>(game) == boom);

            // NEGATIVE: the self-heal itself still works for an id we never held.
            int before = CountType<BattleSkull>(game);
            SnapshotKind(IdSkullD, skullType, hp: 25, out SnapUnknownKind kind2);
            Check("NEGATIVE an unknown live id is still self-healed into a puppet (was "
                + kind2 + ")", kind2 == SnapUnknownKind.Rebuilt
                && CountType<BattleSkull>(game) == before + 1);
            foreach (GameComponent c in CollectType<BattleSkull>(game))
            {
                if (!planted.Contains(c)) { planted.Add(c); }
            }
        }

        // ---- helpers (the NetDeathFxTest shapes) -----------------------------------------
        private static string Describe(List<int> frames)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < frames.Count && i < 30; i++)
            {
                if (i > 0) { s.Append(','); }
                s.Append(frames[i]);
            }
            return s.ToString();
        }

        private static int Advances(List<int> frames)
        {
            int n = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                if (frames[i] != frames[i - 1]) { n++; }
            }
            return n;
        }

        // The distance the frame actually MOVED, taken the short way round the loop -- so the
        // sheet's own wrap does not read as a huge jump, and neither does a backward kick.
        private static int MaxStep(List<int> frames, int loop)
        {
            int max = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                int d = Math.Abs(frames[i] - frames[i - 1]);
                if (loop > 0 && d > loop / 2) { d = loop - d; }
                if (d > max) { max = d; }
            }
            return max;
        }

        private static GameComponent BuildPuppet<T>(Game game, ushort netId, byte typeIdx,
            List<GameComponent> planted) where T : GameComponent
        {
            HashSet<GameComponent> before = new HashSet<GameComponent>(CollectType<T>(game));
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = 0;
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None,
                state, new byte[1], 0, 0, out _, out _);
            foreach (GameComponent item in CollectType<T>(game))
            {
                if (!before.Contains(item)) { planted.Add(item); return item; }
            }
            return null;
        }

        private static void SnapshotWithExtra(ushort netId, byte typeIdx, int hp, byte[] extra)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = hp;
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None,
                state, extra, 0, extra.Length, out _, out _);
        }

        private static void SnapshotKind(ushort netId, byte typeIdx, int hp, out SnapUnknownKind kind)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = hp;
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None,
                state, new byte[1], 0, 0, out _, out kind);
        }

        private static byte TypeIdxOf(GameComponent probe)
        {
            return NetTypeRegistry.TryGet(probe, out byte idx, out _) ? idx : (byte)0;
        }

        private static List<GameComponent> CollectType<T>(Game game)
        {
            List<GameComponent> found = new List<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T) { found.Add(item); }
            }
            return found;
        }

        private static int CountType<T>(Game game)
        {
            return CollectType<T>(game).Count;
        }

        private static bool InWorld(Game game, GameComponent comp)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (ReferenceEquals(item, comp)) { return true; }
            }
            return false;
        }

        private static void Tick(GameComponent comp, int ticks)
        {
            TimeSpan step = TimeSpan.FromTicks(166667);
            TimeSpan total = TimeSpan.Zero;
            for (int i = 0; i < ticks; i++)
            {
                total += step;
                comp.Update(new GameTime(total, step));
            }
        }

        private static int TickUntilGone(GameComponent comp, ComponentBin bin, Game game, int cap)
        {
            for (int i = 0; i < cap; i++)
            {
                Tick(comp, 1);
                bin.TopOfTickFlush();
                if (!InWorld(game, comp)) { return i + 1; }
            }
            return cap;
        }
    }
}
