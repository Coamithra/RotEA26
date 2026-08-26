using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for card 745728f9's two halves -- the StarMine's target lock, and the
    // lock-on cue a joining client never heard.
    //
    // THE REPORT: *"space mines (lvl 3, aka death stars) seem to also explode when they reach a
    // dead player's location"*, plus *"also the homing sound doesnt play for joining clients"*.
    // Level 3 spawns `StarMine` and nothing else (`StarMineSpawner` x9); `DeathStar` is
    // ClassicSpawner's. They share the `deathstarsheet2` sprite, which is where "aka death stars"
    // comes from, and `targetacquired` -- the homing sound -- is `StarMine`'s. Both halves of the
    // ticket land on the same class.
    //
    // WHY A SUITE. **The lock is invisible.** A locked mine and a free one draw the same sprite;
    // which ship it is pulling toward, and whether its 1800 ms detonation clock is running, is
    // private state that no frame and no counter shows. And the CUE is worse -- headlessly there
    // is no mixer at all, so the only thing that can be observed is the REQUEST, which is exactly
    // what card 8732568e's per-cue counters make readable.
    //
    // **WHAT THIS SUITE DOES AND DOES NOT PIN.** The `IsDead` guards it covers are HARDENING of a
    // ONE-TICK window, not the fix for the report. Section 3b measures why: `StarMine` already
    // watched `ComponentRemoved` and nulled `target` there, so from the removal FLUSH onward the
    // pre-card build dropped a dead target by itself. The card's first half is still OPEN, and the
    // hypotheses this suite has REFUTED are recorded in `StarMine.Update`'s `attracted_to_player`
    // comment so nobody re-runs them.
    //
    // SIX SECTIONS:
    //   1. THE LOCK, and the POSITIVE CONTROL first: a mine parked on a LIVE ship acquires it and
    //      detonates on schedule. Without that leg every assertion below passes on a mine that
    //      simply stopped working.
    //   2. THE SAME-TICK WINDOW: the target dies and the world has NOT flushed yet, which is the
    //      only moment the guards are load-bearing -- the mine must drop the lock anyway.
    //   3. THE ACQUIRE LOOP: a DEAD ship in range is not a target at all -- the other half of the
    //      same rule, and the one that stops the mine re-acquiring the body it just let go.
    //   3b. THE FLUSHED WORLD, the honesty leg: with the flush the real game runs every tick, the
    //      target is already null before the mine's next Update, with the guards' help or without
    //      it. This is what bounds the claim sections 2 and 3 are allowed to make.
    //   4. THE JOIN PEER's cue, over a real client session: an EvFx beat on a frozen mine puppet
    //      must ask for `targetacquired`, and must still leave the hit BLINK working (StarMine is
    //      a KillableAlien, so its NetPlayFx override sits on top of the one that blinks).
    //   5. THE SEND HALF: a real host session over a NetWire, reading the frames a scripted peer
    //      actually received, and the once-per-SOUND cadence.
    //
    // IT TICKS THE MINE AND NOTHING ELSE (`Tick`), which is what makes sections 2 and 3 possible
    // at all: a real player death advances `GameScene` into a world WIPE a tick later, and that
    // purge would take the mine with it before anything could be read off it. Section 3b is the
    // deliberate exception -- it flushes, because the flush is the thing it measures.
    //
    // **DESTRUCTIVE** -- it kills the local player's ship for real and respawns it through the
    // scene's own seam. Run it in a throwaway `?level=Level2&invuln` boot.
    internal static class MineTargetTest
    {
        private const ushort IdMinePuppet = 63001;
        private const string HomingCue = "targetacquired";
        private const string Room = "minetarget";
        private const ulong PeerToken = 0x0DEADFEEDUL;

        // The mine's own acquire radius is `250 * DifficultyFactorized(0.5)`, so parking it ON the
        // ship is inside it at every tier -- the suite must not depend on the boot's difficulty.
        private static readonly Vector2 Offset = new Vector2(4f, 0f);

        // 1800 ms of detonation clock at the suite's fixed 60 Hz dt, plus a margin.
        private const int DetonateTicks = 130;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[minetarget] the StarMine's lock, and its cue on a join peer (card 745728f9)\n");

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            SoundManager sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
            Game game = bin.Game;
            INetScene scene = NetScene.Current;
            if (scene == null || NetSession.Active)
            {
                sb.Append("  SKIP (needs a live level and NO session -- try ?level=Level2&invuln)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }
            PlayerShip ship = FindLiveShip(oracle);
            if (ship == null)
            {
                sb.Append("  SKIP (no live player ship -- the level is still starting up?)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            List<GameComponent> planted = new List<GameComponent>();
            int deadSlot = ship.Owner;
            // `PlayerShip.Asplode` spawns real Explosions, which add real screen TRAUMA -- a suite
            // that leaves the level rattling is not leave-no-trace (card 8732568e measured 0.93
            // after one run of a suite that did).
            float traumaBefore = Juice.TraumaNow;
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            try
            {
                Section1LockAndDetonate(sb, Check, bin, game, ship, planted);
                Section2TargetDies(sb, Check, bin, game, oracle, ship, planted);
                Section3DeadShipIsNotAcquired(sb, Check, bin, game, oracle, sound, planted);
                Section3bFlushedWorld(sb, Check, bin, game, oracle, scene, deadSlot, planted);
                Section4JoinPeerCue(sb, Check, bin, game, sound, planted);
                Section5HostEmission(sb, Check, bin, game, oracle, scene, deadSlot, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 6. teardown\n");
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                NetPuppets.Disable();
                bin.TopOfTickFlush();
                foreach (GameComponent comp in CollectType<Explosion>(game))
                {
                    bin.Remove(comp);
                }
                bin.TopOfTickFlush();
                // The ship this suite killed, back through the scene's OWN spawn -- the same seam
                // NetResetSpawnTest uses, so the level is handed back a real ship rather than a
                // hand-rolled stand-in.
                if (FindLiveShip(oracle) == null && oracle.IsSeated(deadSlot))
                {
                    scene.SpawnPlayer(oracle.Controller(deadSlot), deadSlot);
                }
                Check("the ship this suite killed is alive again in its seat",
                    FindLiveShip(oracle) != null);
                int left = 0;
                foreach (GameComponent comp in planted)
                {
                    if (InWorld(game, comp)) { left++; }
                }
                Check("every mine this suite planted left the world (" + left + " left)", left == 0);
                Juice.SetTraumaForTest(traumaBefore);
                Check("the screen shake this suite's deaths added was put back",
                    Math.Abs(Juice.TraumaNow - traumaBefore) < 0.001f);
                sound.SfxResetCounters();
                NetHost.Current = hostBefore;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. THE POSITIVE CONTROL, first ------------------------------------------------------
        //
        // Everything below asserts that a mine STOPS doing something. Without this leg all of it
        // passes on a mine that never locks on or never detonates at all -- which would be a far
        // worse bug than the one being fixed.
        private static void Section1LockAndDetonate(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, PlayerShip ship, List<GameComponent> planted)
        {
            sb.Append(" 1. CONTROL -- a mine on a LIVE ship locks on and detonates on schedule\n");
            StarMine mine = Plant(bin, game, ship.Position + Offset, planted);
            Check("PRECONDITION a mine was planted beside the live ship", mine != null);
            if (mine == null)
            {
                return;
            }
            Tick(mine, 1);
            Check("it LOCKED ON to the live ship", mine.NetLockedOn && mine.NetTarget == ship);
            Check("...and its detonation clock is running", mine.NetDetonationClockRunning);
            Tick(mine, DetonateTicks);
            Check("...and it detonated on schedule (" + DetonateTicks + " ticks = 2.17 s against"
                + " the mine's 1800 ms)", mine.IsDead);
        }

        // ---- 2. THE SAME-TICK WINDOW ---------------------------------------------------------------
        //
        // The world is deliberately NOT flushed here, and that is the whole point: between `Die()`
        // and the removal flush the corpse is still in `GetShips()` with `IsDead` true, and
        // `target` still points at it. That tick is the only moment the guards do any work -- see
        // section 3b for the measurement that bounds this.
        private static void Section2TargetDies(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, Oracle oracle, PlayerShip ship, List<GameComponent> planted)
        {
            sb.Append(" 2. THE SAME-TICK WINDOW -- the target dies before the flush, and the lock"
                + " must go with it\n");
            StarMine mine = Plant(bin, game, ship.Position + Offset, planted);
            Check("PRECONDITION a second mine was planted", mine != null);
            if (mine == null)
            {
                return;
            }
            Tick(mine, 1);
            Check("PRECONDITION it is locked on before the death", mine.NetLockedOn);

            // THE REAL DEATH PATH -- `Asplode()` is what a collision calls, so `isdead`, the queued
            // removal and the death FX are all genuine. The SCENE is deliberately not ticked:
            // GameScene.UpdateNormal would see AllShipsDead and wipe the world a tick later, and
            // that purge takes the mine too (Purge<AlienDrawableGameComponent>), destroying the
            // observation this leg exists to make. The teardown respawns the ship and sweeps the
            // explosions.
            ship.Asplode();
            Check("PRECONDITION the target really is dead now", ship.IsDead);

            int boomBefore = CountType<Explosion>(game);
            Tick(mine, 1);
            Check("the mine DROPPED the dead target", !mine.NetLockedOn && mine.NetTarget == null);
            Check("...and its detonation clock is not running any more",
                !mine.NetDetonationClockRunning);
            Check("...and it did NOT detonate on the corpse", !mine.IsDead);
            // The clock is not merely stopped, it is not CONSULTED: `free` never tests it. Ticking
            // well past the original 1800 ms is what says a mine that lost its lock cannot go off
            // on the old timer a second later.
            //
            // ASSERTED ON THE DETONATION FX, not on IsDead, and the difference is real: a freed
            // mine re-attaches to the background scroll, so over these 2.2 s it can legitimately
            // leave the screen and Die() on `OffScreen(100f)` -- which is a mine flying away, not a
            // mine exploding. `Asplode` spawns exactly two blue Explosions and the fly-off spawns
            // none, so the FX count is what tells the two apart. (Measured: an earlier cut of this
            // leg asserted `!IsDead` and failed for exactly that reason.)
            Tick(mine, DetonateTicks);
            Check("...and no detonation FX ever appeared, " + DetonateTicks + " ticks past the old"
                + " deadline (+" + (CountType<Explosion>(game) - boomBefore) + " explosions)",
                CountType<Explosion>(game) == boomBefore);
        }

        // ---- 3. THE ACQUIRE LOOP ------------------------------------------------------------------
        private static void Section3DeadShipIsNotAcquired(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, Oracle oracle, SoundManager sound,
            List<GameComponent> planted)
        {
            sb.Append(" 3. ...and a DEAD ship in range is not acquired in the first place\n");
            PlayerShip dead = null;
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.IsDead) { dead = s; break; }
            }
            // The corpse from section 2 is still in GetShips(): the oracle drops a ship at the
            // ComponentBin's removal FLUSH, and this suite has deliberately not flushed. That is
            // the exact window the acquire loop had to be taught about -- a ship that is in the
            // list AND dead.
            Check("PRECONDITION section 2's corpse is still in the roster, which IS the window",
                dead != null);
            if (dead == null)
            {
                return;
            }
            StarMine mine = Plant(bin, game, dead.Position + Offset, planted);
            Check("PRECONDITION a third mine was planted on top of the corpse", mine != null);
            if (mine == null)
            {
                return;
            }
            // EXACTLY ONE TICK, and the count is load-bearing. The two guards mask each other over
            // several ticks: with the acquire-loop skip removed the mine locks on the corpse on
            // tick 1 and the `attracted_to_player` death test drops it again on tick 2, so a leg
            // that ticked four times and read the END state passed on the broken build (measured).
            // One tick is the only moment the two are distinguishable.
            sound.SfxResetCounters();
            long cueBefore = sound.SfxRequestsOf(HomingCue);
            Tick(mine, 1);
            Check("it did NOT acquire the corpse it is sitting on",
                !mine.NetLockedOn && mine.NetTarget == null);
            // The INDEPENDENT witness, and it does not depend on the tick count at all: an acquire
            // plays the homing cue, so a lock that came and went within the window still leaves
            // this behind. Card 8732568e's per-cue counters are what make it readable with no mixer.
            Check("...and no homing cue was played, which a lock-and-drop would still leave behind"
                + " (+" + (sound.SfxRequestsOf(HomingCue) - cueBefore) + ")",
                sound.SfxRequestsOf(HomingCue) == cueBefore);
            Tick(mine, 4);
            Check("...and it is still free four ticks later, having started no detonation clock",
                !mine.NetLockedOn && !mine.NetDetonationClockRunning && !mine.IsDead);
        }

        // ---- 3b. THE FLUSHED WORLD -- what the guards are NOT ---------------------------------------
        //
        // THE HONESTY LEG, and the one that stops this suite overstating its own subject. Sections
        // 2 and 3 hold the world at the single tick between `Die()` and the removal flush. The real
        // game flushes every tick (`ComponentBin.TopOfTickFlush`), and `StarMine` has ALWAYS watched
        // `ComponentRemoved` -- `OnComponentRemoved` nulls `target`, and `Oracle` drops the ship out
        // of `GetShips()` off the same event. So from the flush onward the PRE-CARD build let a dead
        // target go all by itself.
        //
        // This leg asserts exactly that, and it asserts it with the mine NEVER TICKED after the
        // flush: no guard in `Update` can have run, so a pass here is attributable to the removal
        // path alone. Delete every `IsDead` clause the card added and this section still passes --
        // which is the point. It is why the card's first half is NOT claimed as fixed, and why
        // `StarMine.Update` carries the refuted hypotheses rather than a fix note.
        private static void Section3bFlushedWorld(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, Oracle oracle, INetScene scene, int slot,
            List<GameComponent> planted)
        {
            sb.Append(" 3b. THE FLUSHED WORLD -- the removal path drops a dead target on its own\n");
            // Sections 2 and 3 left a corpse in the roster on purpose; clear it and get a live ship
            // back through the scene's own spawn, the same seam section 5 and the teardown use.
            bin.TopOfTickFlush();
            PlayerShip ship = FindLiveShip(oracle);
            if (ship == null && oracle.IsSeated(slot))
            {
                scene.SpawnPlayer(oracle.Controller(slot), slot);
                ship = FindLiveShip(oracle);
            }
            Check("PRECONDITION a live ship is back in the world", ship != null);
            if (ship == null)
            {
                return;
            }
            StarMine mine = Plant(bin, game, ship.Position + Offset, planted);
            Check("PRECONDITION a mine was planted beside it", mine != null);
            if (mine == null)
            {
                return;
            }
            Tick(mine, 1);
            Check("PRECONDITION it locked on to the live ship", mine.NetLockedOn && mine.NetTarget == ship);

            ship.Asplode();
            // The window sections 2 and 3 live in, asserted here as a POSITIVE so this leg cannot
            // silently become vacuous: before the flush the corpse is still addressable.
            Check("before the flush the corpse is still in GetShips() and still the target",
                oracle.GetShips().Contains(ship) && mine.NetTarget == ship);

            bin.TopOfTickFlush();
            // THE MEASUREMENT. The mine has not ticked since the flush, so no guard in `Update`
            // has run -- this is `OnComponentRemoved` and nothing else.
            Check("after the flush, and with the mine NEVER ticked, the target is already null"
                + " -- the removal path, not the card's guards", mine.NetTarget == null);
            Check("...and the corpse has left GetShips() off that same event",
                !oracle.GetShips().Contains(ship));
        }

        // ---- 4. THE JOIN PEER'S CUE ---------------------------------------------------------------
        //
        // A puppet mine is FROZEN, so its own Update -- and the cue in it -- never runs on the
        // joiner. The beat is the only way that sound reaches the other screen, and a requested cue
        // is the only observable it has here: eahl silences the mixer, and in a container there is
        // no audio device at all.
        private static void Section4JoinPeerCue(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, SoundManager sound, List<GameComponent> planted)
        {
            sb.Append(" 4. THE JOIN PEER -- an EvFx beat plays the homing cue on a frozen puppet\n");
            NetPuppets.Enable(game);
            byte mineType = TypeIdxOf(new StarMine(game));
            StarMine puppet = (StarMine)BuildPuppet<StarMine>(game, IdMinePuppet, mineType, planted);
            Check("PRECONDITION a mine puppet was built", puppet != null);
            if (puppet == null)
            {
                return;
            }
            Check("PRECONDITION the puppet is FROZEN, so its own Update can never play the cue",
                !puppet.Enabled);

            sound.SfxResetCounters();
            long before = sound.SfxRequestsOf(HomingCue);
            ((INetEntity)puppet).NetPlayFx(NetFxKind.MineTargetAcquired);
            Check("the beat asked for the homing cue (" + HomingCue + " +"
                + (sound.SfxRequestsOf(HomingCue) - before) + ")",
                sound.SfxRequestsOf(HomingCue) - before == 1);

            // NEGATIVE: the override must not swallow the kinds it does not own. StarMine is a
            // KillableAlien, and KillableAlien.NetPlayFx is what plays the 35 ms HIT BLINK -- an
            // override that just returned would delete the blink for every mine on the joiner's
            // screen, a silent regression in the feature this beat is modelled on.
            long cueBefore = sound.SfxRequestsOf(HomingCue);
            Check("PRECONDITION the puppet is not blinking yet", !puppet.NetHitBlinking);
            ((INetEntity)puppet).NetPlayFx(NetFxKind.EnemyHitFlash);
            Check("NEGATIVE the hit-flash beat still reaches KillableAlien's blink",
                puppet.NetHitBlinking);
            Check("...and did NOT also play the homing cue",
                sound.SfxRequestsOf(HomingCue) == cueBefore);
            // Hand the puppet layer back HERE, not in Run's outer finally. Section 5 starts a real
            // HOST session, and a host with the puppet layer still enabled -- and this section's
            // parked mine puppet still in the world -- is a state the game never reaches. The
            // teardown's own Disable() stays as the belt-and-braces for an early return above.
            NetPuppets.Disable();
        }

        // ---- 5. THE SEND HALF ----------------------------------------------------------------------
        //
        // Section 4 drives `NetPlayFx` directly, which pins the RECEIVE half and nothing else: a
        // build that stopped EMITTING the beat would leave every leg up there green while the
        // joiner heard nothing, which is the reported symptom exactly. So this runs a real HOST
        // session over a `NetWire` and reads the frames the peer really RECEIVED -- the
        // NetDeathFxTest section-2 shape.
        //
        // It also pins the CADENCE, which is the part that could go wrong quietly: the emission is
        // gated on the same `soundtimer` as the local cue, so an ongoing lock is ONE beat and not
        // one per tick. Ungated, a mine sitting on a player would stream ~60 reliable events a
        // second at the joiner.
        private static void Section5HostEmission(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, Oracle oracle, INetScene scene, int slot,
            List<GameComponent> planted)
        {
            sb.Append(" 5. HOST -- the acquire really goes out on the wire, ONCE per lock\n");
            // Section 2 killed the ship on purpose and nothing has put it back yet, so this section
            // does it FIRST, through the scene's own spawn -- the same seam the teardown uses. The
            // corpse is flushed out of the roster on the way so `FindLiveShip` cannot pick it.
            bin.TopOfTickFlush();
            PlayerShip ship = FindLiveShip(oracle);
            if (ship == null && oracle.IsSeated(slot))
            {
                scene.SpawnPlayer(oracle.Controller(slot), slot);
                ship = FindLiveShip(oracle);
            }
            Check("PRECONDITION a live ship is back in the world for the host legs", ship != null);
            if (ship == null)
            {
                return;
            }
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> beats = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= 2 && payload[0] == NetProtocol.MsgEvent
                    && payload[1] == NetProtocol.EvFx)
                {
                    beats.Add(payload);
                }
            }
            // The roster AS FOUND, so the scripted peer's granted seat can be released precisely.
            // NOT `ResetPlayers()`, which the menu-only suites use: this one runs inside a LIVE
            // LEVEL, and wiping the roster there un-seats the actual player -- after which the
            // teardown's respawn cannot find a seat and the level is left with no ship at all.
            // (Measured: a second run of this suite in one process reported three failures for
            // exactly that reason.)
            bool[] seatedBefore = new bool[Oracle.MaxPlayers];
            for (int i = 0; i < Oracle.MaxPlayers; i++)
            {
                seatedBefore[i] = oracle.IsSeated(i);
            }
            try
            {
                NetSession.StartForTest(game, host: true, ours, Room);
                peer.Open(Room);
                peer.OnData += Sniff;
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                Check("PRECONDITION the scripted client paired with a real host session",
                    NetSession.IsHost && NetSession.PeerUp);
                if (!NetSession.PeerUp)
                {
                    return; // OnGameFx early-returns with no peer; the legs below would be vacuous
                }
                StarMine mine = Plant(bin, game, ship.Position + Offset, planted);
                Check("PRECONDITION a mine was planted beside the live ship", mine != null);
                if (mine == null)
                {
                    return;
                }
                beats.Clear();
                Tick(mine, 1);
                wire.Pump();
                Check("PRECONDITION it locked on", mine.NetLockedOn);
                Check("the acquire broadcast exactly one EvFx (" + beats.Count + ")",
                    beats.Count == 1);
                Check("...carrying MineTargetAcquired, addressed to the mine's own netId",
                    beats.Count == 1
                    && NetProtocol.TryDecodeFxEvent(beats[0], out NetFxKind kind, out ushort id, out _)
                    && kind == NetFxKind.MineTargetAcquired
                    && NetIdRegistry.TryGetByComp((GameComponent)(object)mine, out NetIdRegistry.Entry e)
                    && id == e.Id);
                // THE CADENCE, and it has to be driven through a RE-ACQUIRE to mean anything.
                // Merely holding the lock adds no beats however the emission is written, because
                // the acquire loop lives in the `free` branch and a locked mine never enters it --
                // measured: moving the emission outside the `soundtimer` gate left a hold-the-lock
                // leg perfectly green. What the gate is actually for is a mine that keeps LOSING
                // and RETAKING a lock (a target crossing the release ring, a swarm around one
                // ship), which without it streams one reliable event per tick at the joiner.
                Vector2 home = ship.Position + Offset;
                Vector2 away = new Vector2(-3000f, -3000f);
                beats.Clear();
                mine.NetParkForTest(away);
                Tick(mine, 1);
                Check("PRECONDITION parking the mine far away drops the lock", !mine.NetLockedOn);
                mine.NetParkForTest(home);
                Tick(mine, 1);
                wire.Pump();
                Check("PRECONDITION it re-acquired inside the 300 ms sound window",
                    mine.NetLockedOn);
                Check("...and that re-acquire sent NOTHING -- one beat per SOUND, not per lock (+"
                    + beats.Count + ")", beats.Count == 0);
                // ...and the gate really expires, or the leg above would pass on a build that
                // sends the beat exactly once per mine and then never again.
                beats.Clear();
                mine.NetParkForTest(away);
                Tick(mine, 25); // 417 ms, past the 300 ms soundtimer
                mine.NetParkForTest(home);
                Tick(mine, 1);
                wire.Pump();
                Check("CONTROL a re-acquire PAST the window sends again (+" + beats.Count + ")",
                    beats.Count == 1);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("mine target harness finished");
                bin.TopOfTickFlush();
                Check("the host session was stopped and left nothing Active", !NetSession.Active);
                for (int i = 0; i < Oracle.MaxPlayers; i++)
                {
                    if (!seatedBefore[i] && oracle.IsSeated(i))
                    {
                        oracle.RemovePlayerAt(i, oracle.Controller(i));
                    }
                }
                bool rosterBack = true;
                for (int i = 0; i < Oracle.MaxPlayers; i++)
                {
                    rosterBack &= seatedBefore[i] == oracle.IsSeated(i);
                }
                Check("...and the roster is exactly as this section found it", rosterBack);
            }
        }

        // ---- helpers -------------------------------------------------------------------------------

        private static PlayerShip FindLiveShip(Oracle oracle)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (!s.IsDead)
                {
                    return s;
                }
            }
            return null;
        }

        private static StarMine Plant(ComponentBin bin, Game game, Vector2 at,
            List<GameComponent> planted)
        {
            StarMine mine = StarMine.NewStarMine(bin, game);
            // Parked, not launched -- see NetParkForTest: the production entries either drop it at
            // a random x above the screen or give it MaxSpeed, and at MaxSpeed it drifts out of
            // its own release range long before the 1800 ms clock is up.
            mine.SetupLaunch(at, 0f);
            mine.NetParkForTest(at);
            if (!bin.TryAdd((GameComponent)(object)mine))
            {
                return null;
            }
            planted.Add((GameComponent)(object)mine);
            return mine;
        }

        // The mine's own Update at a fixed 60 Hz dt, and NOTHING else in the world -- the
        // isolation-sim pattern. Ticking the MINE alone is what makes sections 2 and 3 observable
        // at all: a real player death advances `GameScene` into a world WIPE a tick later, and
        // that purge (`Purge<AlienDrawableGameComponent>`) takes the mine with it before anything
        // could be read off it. `Update` is public, so this needs no seam on `StarMine`.
        private static void Tick(StarMine mine, int ticks)
        {
            TimeSpan step = TimeSpan.FromTicks(166667); // 16.6667 ms
            TimeSpan total = TimeSpan.Zero;
            for (int i = 0; i < ticks; i++)
            {
                total += step;
                mine.Update(new GameTime(total, step));
            }
        }

        private static byte TypeIdxOf(GameComponent probe)
        {
            NetTypeRegistry.TryGet(probe, out byte idx, out _);
            return idx;
        }

        private static GameComponent BuildPuppet<T>(Game game, ushort netId, byte typeIdx,
            List<GameComponent> planted) where T : GameComponent
        {
            HashSet<GameComponent> before = new HashSet<GameComponent>(CollectType<T>(game));
            NetBaseState state = default(NetBaseState);
            state.Pos = new Vector2(-4000f, -4000f);
            state.Scale = 1f;
            state.Hp = 0;
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None,
                state, new byte[1], 0, 0, out _, out _);
            foreach (GameComponent item in CollectType<T>(game))
            {
                if (!before.Contains(item))
                {
                    planted.Add(item);
                    return item;
                }
            }
            return null;
        }

        private static int CountType<T>(Game game)
        {
            return CollectType<T>(game).Count;
        }

        private static List<GameComponent> CollectType<T>(Game game)
        {
            List<GameComponent> found = new List<GameComponent>();
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T && item is GameComponent gc)
                {
                    found.Add(gc);
                }
            }
            return found;
        }

        private static bool InWorld(Game game, GameComponent comp)
        {
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (ReferenceEquals(item, comp))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Tally(int pass, int fail)
        {
            return "[minetarget] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
