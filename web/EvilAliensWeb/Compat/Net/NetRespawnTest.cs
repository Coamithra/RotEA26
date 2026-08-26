using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE RESPAWN INDICATOR CROSSES THE WIRE (card 37f3a663). Run `eaNetRespawn()` from the MAIN
    // MENU, or `eval NetRespawn` under eahl. A leg of tools/headless/probes/net_selftests.txt.
    //
    // Before this card the respawn clock existed only on the dying player's own screen: the far
    // peer watched its buddy's ship explode and then nothing at all for ten seconds --
    // NetSession.ExplodePuppet removes the puppet WITHOUT Die(), precisely so it does not raise a
    // local summon for a ship it does not own. EvRespawn is the announcement that fills that gap,
    // and it is COSMETIC on the receiving side: the peer's real ship still arrives through the
    // ordinary remoteAlive edge, so a lost frame costs the indicator, never the ship.
    //
    // SECTION 1 -- TX, through the REAL death path. Two real PlayerShips are planted and one is
    // Asplode()d, so what is asserted is the whole chain OnDeath -> ShouldSummon -> Setup ->
    // OnLocalRespawnSummon, not a hand-called sender. Its negative is a PUPPET dying: no summon
    // and no frame, because a puppet's respawn belongs to the other peer.
    //
    // SECTION 2 -- RX. A scripted peer's EvRespawn must raise a COSMETIC summon at the announced
    // position for the announced duration; the same frame naming a slot WE own must be refused,
    // or a slot disagreement would park a phantom clock over a living player and then drop a free
    // bomb into our world.
    //
    // SECTION 3 -- the POP. The cosmetic summon must drop the reward blast and retire itself
    // WITHOUT spawning a PlayerShip. That last half is the one that would be catastrophic and
    // silent: a cosmetic summon that spawned a ship would give the peer's player a second body on
    // this screen, which the roster would then argue with for the rest of the match.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE, the NetLocalFxTest shape: it plants into the LIVE bin and
    // seats REAL roster slots, so it skips itself over a session, level or attract demo, and it
    // sweeps by TYPE at teardown (a death spawns explosions, and the pop spawns a real Blast).
    internal static class NetRespawnTest
    {
        private const string Room = "netrespawn";

        // Ours and theirs. Both seated explicitly -- an UNSEATED slot also answers false to
        // OwnsSlot, so seating only one would let the refusal leg pass for the wrong reason.
        private const byte OurSlot = 1;
        private const byte TheirSlot = 0;

        private const ulong PeerToken = 0x2E5A1F00UL;

        // Off-screen, so nothing this suite plants or pops is ever drawn.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // Where the peer says its respawn is happening. Deliberately NOT Nowhere and not the
        // planted ships' position, so a leg reading the wrong vector cannot pass.
        private static readonly Vector2 PeerRespawnAt = new Vector2(-543f, -321f);

        // Short enough that ONE scripted Update finishes the cosmetic clock (section 3), and not
        // a round second, so a receiver that rounded to whole seconds would show.
        private const int PeerRespawnMs = 750;

        // The "2" (Linker) level section 1 gives our slot before the death, and the one section 3
        // puts on the wire. Deliberately NEITHER 0 (what a build that lost the read produces) nor
        // 3 (the pre-card constant), so the reward legs cannot pass on either -- card ed32efe1.
        private const int RewardLinkerLevel = 2;

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

            sb.Append("[netrespawn] the respawn indicator crosses the wire (card 37f3a663)\n");

            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            int playersBefore = oracle.Players;
            List<GameComponent> planted = new List<GameComponent>();

            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            float? phaseBefore = DebugFlags.RespawnPhase;
            try
            {
                // A parked phase would make every fill this suite reads a constant. Nothing here
                // asserts on the fill, but a stray ?respawnphase= on the boot must not silently
                // change what the pop legs are driving.
                DebugFlags.SetRespawnPhaseOverride(null);
                RunLegs(sb, Check, oracle, bin, game, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + Describe(ex) + ")", ok: false);
            }
            finally
            {
                sb.Append(" 9. teardown -- what this suite must hand back\n");
                Teardown(sb, Check, oracle, bin, game, planted, playersBefore);
                DebugFlags.SetRespawnPhaseOverride(phaseBefore);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            // Every EvRespawn the PEER received. The wire is the only place the announcement is
            // observable -- a summon looks identical whether or not it was announced.
            List<byte[]> respawnFrames = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= 2 && payload[0] == NetProtocol.MsgEvent
                    && payload[1] == NetProtocol.EvRespawn)
                {
                    respawnFrames.Add(payload);
                }
            }

            sb.Append(" 0. rig -- a real HOST session, a scripted peer, and two seats\n");

            NetScene.Current = new RespawnScene();
            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            peer.OnData += Sniff;
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("the scripted peer paired with a real host session",
                NetSession.IsHost && NetSession.PeerUp);
            if (!NetSession.PeerUp)
            {
                peer.OnData -= Sniff;
                return; // OnLocalRespawnSummon early-returns with no peer; every leg would be vacuous
            }

            Seat(oracle, OurSlot, ControlDevice.Keyboard);
            Seat(oracle, TheirSlot, ControlDevice.Remote);
            Check("PRECONDITION OwnsSlot separates the two seats (ours="
                + NetSession.OwnsSlot(OurSlot) + " theirs=" + NetSession.OwnsSlot(TheirSlot) + ")",
                NetSession.OwnsSlot(OurSlot) && !NetSession.OwnsSlot(TheirSlot));

            sb.Append(" 1. tx -- our ship's death raises a summon AND announces it\n");

            // Both ships first: ShouldSummon needs somebody else still flying, and that
            // precondition is the whole point of section 1 (with only one ship the death is a
            // wipe and the correct behaviour is NO summon at all -- the card's side-fix).
            PlayerShip ourShip = PlantShip(bin, game, planted, OurSlot);
            PlayerShip theirShip = PlantShip(bin, game, planted, TheirSlot);
            bin.TopOfTickFlush();
            Check("PRECONDITION both ships are in the oracle (ships=" + oracle.LiveShips + ")",
                oracle.LiveShips == 2);

            // Give OUR slot a "2" powerup level AFTER the plant -- PlantShip's Initialize runs
            // Score.ResetPowerup on it, so setting it earlier would be wiped. This is what makes
            // the reward legs below non-vacuous: RewardLinkerLevel is neither 0 (what a broken
            // read gives) nor 3 (the pre-card constant).
            ServiceHelper.Get<IScoreService>().Score
                .DebugSetPowerupLevel(OurSlot, Powerup.PowerupType.Linker, RewardLinkerLevel);
            Check("PRECONDITION our slot holds a level-" + RewardLinkerLevel + " \"2\" ("
                + ServiceHelper.Get<IScoreService>().Score
                    .GetPowerupLevel(Powerup.PowerupType.Linker, OurSlot) + ")",
                ServiceHelper.Get<IScoreService>().Score
                    .GetPowerupLevel(Powerup.PowerupType.Linker, OurSlot) == RewardLinkerLevel);

            respawnFrames.Clear();
            int summonsBefore = CountSummons(game);
            ourShip.Asplode();
            bin.TopOfTickFlush();
            wire.Pump();
            PlayerShipSummon raised = FindSummon(game);
            Check("our ship's death raised a respawn summon (summons " + summonsBefore + " -> "
                + CountSummons(game) + ")", CountSummons(game) == summonsBefore + 1 && raised != null);
            Check("...and it is the REAL one, not a cosmetic copy",
                raised != null && !raised.IsCosmetic);
            byte txSlot = 0;
            Vector2 txPos = Vector2.Zero;
            int txMs = 0;
            int txReward = 0;
            bool txOk = respawnFrames.Count == 1
                && NetProtocol.TryDecodeRespawnEvent(respawnFrames[0], out txSlot, out txPos, out txMs,
                    out txReward);
            Check("...and put exactly one EvRespawn on the peer's wire (" + respawnFrames.Count
                + " frames, slot=" + txSlot + ")", txOk && txSlot == OurSlot);
            // The DURATION needs its own leg: it is what the far peer's clock runs for, and it is
            // not a constant -- it falls out of the dying player's respawntimebonus and the
            // difficulty, so a sender that shipped a hard-coded value would look fine here
            // without it.
            Check("...carrying the summon's OWN duration (" + txMs + "ms vs "
                + (raised != null ? raised.DurationMs : -1) + "ms)",
                txOk && raised != null && txMs == raised.DurationMs);
            Check("...and the position the ship died at (" + Fmt(txPos) + " vs "
                + Fmt(Nowhere) + ")",
                txOk && txPos.X == Nowhere.X && txPos.Y == Nowhere.Y);
            // The REWARD LEVEL, its own leg for the same reason the duration has one (card
            // ed32efe1, v26): it is the dying player's "2" powerup level, not a constant, and it
            // must be the value the SUMMON latched -- a sender that re-read Score here would ship
            // whatever the slot holds at send time instead. Driven to a non-default value above
            // via Score.DebugSetPowerupLevel so a hard-coded 0 or 3 cannot pass.
            Check("...and the reward level the summon latched (" + txReward + " vs "
                + (raised != null ? raised.RewardBlastLevel : -1) + ")",
                txOk && raised != null && txReward == raised.RewardBlastLevel);
            Check("...which is the LINKER level we gave that slot, not a constant ("
                + txReward + " vs " + RewardLinkerLevel + ")", txOk && txReward == RewardLinkerLevel);

            // 1a. THE CLOCK ONLY EVER RUNS DOWN. `base.Update` ticks the timers AFTER this class
            // tests `Finished`, so between the tick that rings the 1 Hz timer and the tick that
            // acts on it, TimeLeft has wrapped back to ~1000 while `countdown` is still the old
            // value -- and Draw runs in that window. Unfixed, the ring un-filled by a tenth once
            // a second and the last frame before the ship arrived showed no flare at all.
            // NOTHING ELSE CAN SEE THIS: ?respawnphase= parks the fill, `eval RespawnState`
            // samples between ticks, and a live capture cannot be timed to the one frame per
            // second that was wrong. 200 ticks is ~3.3 s of a 10 s clock, so it never completes.
            if (raised != null)
            {
                GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16.67));
                float prev = raised.DebugRemainingMs;
                float worstRise = 0f;
                float startedAt = prev;
                for (int i = 0; i < 200; i++)
                {
                    ((GameComponent)(object)raised).Update(tick);
                    float now = raised.DebugRemainingMs;
                    worstRise = Math.Max(worstRise, now - prev);
                    prev = now;
                }
                // The positive control: a clock that never moved would satisfy "never rises".
                Check("PRECONDITION the clock actually ran (" + Fmt(startedAt) + " -> "
                    + Fmt(prev) + "ms over 200 ticks)", startedAt - prev > 2000f);
                Check("...and never ran BACKWARD (worst rise " + Fmt(worstRise) + "ms)",
                    worstRise <= 0.01f);
            }

            // Retire it before the negative leg, so "no new summon" cannot be confused with
            // "the first one is still there".
            RetireSummons(bin, game, planted);
            bin.TopOfTickFlush();

            // 1b. THE NEGATIVE: a puppet dying is the OTHER peer's respawn. It must raise nothing
            // locally (we draw its cosmetic copy off ITS announcement instead) and announce
            // nothing back, or the two peers would each announce the same respawn.
            respawnFrames.Clear();
            summonsBefore = CountSummons(game);
            theirShip.Asplode();
            bin.TopOfTickFlush();
            wire.Pump();
            Check("a PUPPET's death raises no local summon (summons " + summonsBefore + " -> "
                + CountSummons(game) + ")", CountSummons(game) == summonsBefore);
            Check("...and announces nothing back (" + respawnFrames.Count + " EvRespawn frames)",
                respawnFrames.Count == 0);

            sb.Append(" 2. rx -- the peer's announcement draws the indicator here\n");

            RetireSummons(bin, game, planted);
            bin.TopOfTickFlush();
            summonsBefore = CountSummons(game);
            peer.SendReliable(NetProtocol.EncodeRespawnEvent(eventSeq++, TheirSlot, PeerRespawnAt,
                PeerRespawnMs, RewardLinkerLevel));
            wire.Pump();
            NetSession.Update();
            PlayerShipSummon mirrored = FindSummon(game);
            Check("the peer's EvRespawn raised an indicator here (summons " + summonsBefore
                + " -> " + CountSummons(game) + ")",
                CountSummons(game) == summonsBefore + 1 && mirrored != null);
            Check("...marked COSMETIC, so it will spawn no ship",
                mirrored != null && mirrored.IsCosmetic);
            Check("...at the announced position (" + (mirrored != null ? Fmt(mirrored.Position) : "none")
                + " vs " + Fmt(PeerRespawnAt) + ")",
                mirrored != null && mirrored.Position.X == PeerRespawnAt.X
                    && mirrored.Position.Y == PeerRespawnAt.Y);
            Check("...for the announced duration (" + (mirrored != null ? mirrored.DurationMs : -1)
                + "ms vs " + PeerRespawnMs + "ms)",
                mirrored != null && mirrored.DurationMs == PeerRespawnMs);
            // ...and with the announced REWARD LEVEL (card ed32efe1, v26). It has to come off the
            // wire: this peer's own view of THEIR powerups arrives over the ~10 Hz MsgHudState and
            // would be stale (or, for a join-in-progress peer, absent). Non-vacuous because
            // nothing here ever gave slot TheirSlot a Linker level -- PlantShip's Initialize
            // zeroed it -- so a build that re-derived it locally reads 0 and fails.
            Check("...and with the announced reward level ("
                + (mirrored != null ? mirrored.RewardBlastLevel : -1) + " vs " + RewardLinkerLevel
                + ")", mirrored != null && mirrored.RewardBlastLevel == RewardLinkerLevel);
            Check("PRECONDITION our local view of THEIR \"2\" is 0, so that level can only have"
                + " come off the wire ("
                + ServiceHelper.Get<IScoreService>().Score
                    .GetPowerupLevel(Powerup.PowerupType.Linker, TheirSlot) + ")",
                ServiceHelper.Get<IScoreService>().Score
                    .GetPowerupLevel(Powerup.PowerupType.Linker, TheirSlot) == 0);
            // Card 045c5a92's numeral, on the COSMETIC path. PeerRespawnMs is deliberately not a
            // round second, which is exactly what makes this worth asserting: the countdown must
            // still read a whole 1 here (ceil of 0.75), never a 0 and never a fraction. The owned
            // mode's numeral is pinned by tools/headless/probes/respawn_digit.txt, which cannot
            // reach this clock -- it is a different Timer, fed a duration off the wire.
            Check("...and its countdown numeral reads whole seconds ("
                + (mirrored != null ? mirrored.DebugShownSeconds : -1) + " for "
                + PeerRespawnMs + "ms)",
                mirrored != null && mirrored.DebugShownSeconds == 1);
            // ...and the punch is settled 250ms past that (non-round) boundary, so the animation is
            // driven by this clock rather than stuck on for every cosmetic summon that appears.
            Check("...with the digit punch settled (" + (mirrored != null
                ? mirrored.DebugDigitPunch.ToString("0.000") : "none") + ")",
                mirrored != null && mirrored.DebugDigitPunch == 0f);

            // 2b. The refusal. A frame naming a slot we own must be dropped: otherwise a slot
            // disagreement parks a phantom clock over a player who is alive and flying, and drops
            // a free bomb into our world when it pops.
            int summonsNow = CountSummons(game);
            peer.SendReliable(NetProtocol.EncodeRespawnEvent(eventSeq++, OurSlot, PeerRespawnAt,
                PeerRespawnMs, RewardLinkerLevel));
            wire.Pump();
            NetSession.Update();
            Check("an EvRespawn naming a slot WE own is refused (summons " + summonsNow + " -> "
                + CountSummons(game) + ")", CountSummons(game) == summonsNow);

            sb.Append(" 3. the pop -- a reward blast, and NO ship\n");

            if (mirrored == null)
            {
                Check("section 3 needs the mirrored summon from section 2", false);
                peer.OnData -= Sniff;
                return;
            }
            int blastsBefore = CountOfType<Blast>(game);
            int shipsBefore = CountOfType<PlayerShip>(game);
            // TWO Updates past the whole announced window, and the second is not padding:
            // AlienDrawableGameComponent ticks the timers in base.Update, which this override
            // calls AFTER testing Finished -- so the first Update is what expires the clock and
            // the second is what acts on it. That is the shipped ordering, not a rig detail.
            GameTime past = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(PeerRespawnMs + 16));
            ((GameComponent)(object)mirrored).Update(past);
            Check("PRECONDITION one Update past the window has not popped it yet -- the clock"
                + " expires on this tick and the pop lands on the next",
                CountSummons(game) == 1);
            ((GameComponent)(object)mirrored).Update(past);
            bin.TopOfTickFlush();
            Check("the cosmetic summon popped and retired itself (summons "
                + CountSummons(game) + ")", CountSummons(game) == 0);
            Check("...dropping the reward blast (blasts " + blastsBefore + " -> "
                + CountOfType<Blast>(game) + ")", CountOfType<Blast>(game) == blastsBefore + 1);
            // THE LOAD-BEARING ONE. A cosmetic summon that spawned a ship would give the peer's
            // player a second body on this screen, which the roster would argue with for the rest
            // of the match -- and every other assertion here would still be green.
            Check("...and spawning NO PlayerShip (ships " + shipsBefore + " -> "
                + CountOfType<PlayerShip>(game) + ")", CountOfType<PlayerShip>(game) == shipsBefore);

            peer.OnData -= Sniff;
        }

        // ---- rig helpers -------------------------------------------------------------------

        // A real PlayerShip through its own recycle path, parked off-screen and added to the LIVE
        // bin so the Oracle registers it through the real ComponentAdded seam -- which is what
        // makes CountOtherLiveShips (and therefore ShouldSummon) non-vacuous.
        private static PlayerShip PlantShip(ComponentBin bin, Game game, List<GameComponent> planted, int slot)
        {
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, Nowhere, startup: false, invulnerable: false, 4.712389f);
            bin.Add((GameComponent)(object)ship);
            planted.Add((GameComponent)(object)ship);
            return ship;
        }

        private static void Seat(Oracle oracle, int slot, ControlDevice device)
        {
            if (oracle.IsSeated(slot))
            {
                oracle.RemovePlayerAt(slot, oracle.Controller(slot));
            }
            oracle.AddPlayerAt(slot, device);
        }

        private static int CountSummons(Game game)
        {
            return CountOfType<PlayerShipSummon>(game);
        }

        private static int CountOfType<T>(Game game)
        {
            int n = 0;
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T)
                {
                    n++;
                }
            }
            return n;
        }

        private static PlayerShipSummon FindSummon(Game game)
        {
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is PlayerShipSummon summon)
                {
                    return summon;
                }
            }
            return null;
        }

        // Take every live summon out of the world between legs, so a count is always about the
        // leg that just ran.
        private static void RetireSummons(ComponentBin bin, Game game, List<GameComponent> planted)
        {
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is PlayerShipSummon)
                {
                    GameComponent comp = (GameComponent)(object)item;
                    bin.Remove(comp);
                    if (!planted.Contains(comp))
                    {
                        planted.Add(comp);
                    }
                }
            }
        }

        // Hand the menu back what it lent us. The deaths spawn real explosions and the pop spawns
        // a real Blast, so the sweep is by TYPE rather than by the planted list alone.
        private static void Teardown(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, List<GameComponent> planted, int playersBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("netrespawn teardown");
                }
                foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
                {
                    if ((item is PlayerShip || item is PlayerShipSummon || item is Blast
                            || item is Explosion || item is Powerup)
                        && !planted.Contains(item))
                    {
                        planted.Add(item);
                    }
                }
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                bin.TopOfTickFlush();
                int left = 0;
                foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
                {
                    if (planted.Contains(item))
                    {
                        left++;
                    }
                }
                Check("every entity this suite built or spawned is out of the world ("
                    + planted.Count + " tracked, " + left + " left)", left == 0);
                planted.Clear();

                for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
                {
                    if (oracle.IsSeated(slot))
                    {
                        oracle.RemovePlayerAt(slot, oracle.Controller(slot));
                    }
                }
                Check("the roster is empty again (players " + playersBefore + " -> "
                    + oracle.Players + ")", oracle.Players == playersBefore);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + Describe(ex) + ")", false);
            }
        }

        private static string Fmt(float f)
        {
            return f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Fmt(Vector2 v)
        {
            return v.X.ToString("0.#", CultureInfo.InvariantCulture) + ","
                + v.Y.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string Describe(Exception ex)
        {
            string s = ((object)ex).GetType().Name + ": " + ex.Message;
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                s += " <- " + ((object)inner).GetType().Name + ": " + inner.Message;
            }
            return s;
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netrespawn] {0} passed, {1} failed\n", pass, fail);
        }

        // The minimum INetScene, so the rx path's "is a scene up" gate opens. Nothing here is
        // about what a scene DOES -- the indicator is a plain bin component.
        private sealed class RespawnScene : INetScene
        {
            public Levels Level => Levels.Level1;

            public bool NetEndingNormally => false;

            public bool JoinWouldSpawnNow => false;

            public float PlayerSpawnDirection => 4.712389f;

            public bool NetScriptHoldsShipSpawn => false;

            public void NetApplyIntroVolley(int seed) { }

            public void NetApplyReset(byte mode) { }

            public void NetApplyVictory() { }

            public void NetApplyCheckpoint() { }

            public void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v) { }

            public void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate) { }

            public void NetApplyTetherBreak() { }

            public void NetApplyPeerLeft() { }

            public void NetSetRemotePaused(bool on) { }

            public void NetSetPeerStalled(bool on) { }

            public void NetReplayCatchUp() { }

            public bool NetShowKickMenu() => false;

            public void SpawnPlayer(ControlDevice controlDevice, int slot) { }
        }
    }
}
