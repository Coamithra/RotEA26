using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the SAME-TICK SFX COALESCER (card 8732568e).
    //
    // Reported as *"multiplayer games (on a joining peer side) seem to have a lot of loud
    // explosion effect sounds. I suspect we get a big packet with a bunch of dead enemies and
    // play the sound a couple of times in the same frame perhaps?"* -- and that suspicion is
    // right, both about the mechanism and about why it reads as LOUD rather than as busy: N
    // copies of the SAME sample started at the SAME instant are phase-identical and sum
    // COHERENTLY, amplitude x N, i.e. +20*log10(N) dB. Ten simultaneous `expl1` is one explosion
    // twenty decibels louder, not ten explosions.
    //
    // WHY THIS IS A SUITE AND NOT A LISTEN. An SFX decision has no pixels; headlessly it has no
    // sound either (eahl silences the mixer, and in a container there is no audio device at all,
    // so `SoundManager.GetEffect` caches null and nothing ever reaches a mixer); and in the
    // browser a human ear cannot count starts. The DECISION read back as data is the only honest
    // observable, which is why `SoundManager` counts requests / starts / coalesced and takes the
    // decision BEFORE it loads the effect.
    //
    // THREE SECTIONS, and the third is the one that matters:
    //   1. THE DECISION, in isolation -- a burst inside one tick, the same burst spread across
    //      ticks (the control that says the window is a TICK and not "this cue, forever"), two
    //      different cues in one tick (the control that says it is PER CUE and not a global
    //      one-sound cap), the `Play`/`PlayText` SURFACES (never coalesced -- those callers KEEP
    //      the instance, and `PlayText` stops the in-flight line before assigning it, so a null
    //      would leave the announcer silent), a LOOPING cue, and the one deliberate opt-out.
    //   2. THE FLAG -- `?sfxcoalesce=0` restores the pre-card behaviour, and is the A/B seam and
    //      this suite's negative control. Driven through the real `DebugFlags` property.
    //   3. THE REPORTED PATH -- a batch of REMOTE DEATHS applied in ONE tick, through the real
    //      `NetPuppets.OnRemoteDeath`, on real `UFO` puppets whose real death path plays the real
    //      cue. Sections 1 and 2 could both pass on an audio layer that is never reached by the
    //      thing the ticket is about; this is what says it is.
    //
    // MENU-ONLY and leave-no-trace, which for section 3 takes REAL WORK and not just pruning:
    // a UFO's real death path awards score, spawns Explosions into the live bin and adds screen
    // TRAUMA (measured at 0.93 after one run, i.e. a visibly rattling main menu, before the
    // teardown below restored it). Scores are saved and restored the way `NetDeathFxTest` does,
    // Explosions are swept as collateral, and the trauma is put back -- each asserted, because
    // "leave-no-trace" that nothing checks is just a claim. It refuses to run with a session, a
    // level or an attract demo up.
    internal static class SfxBurstTest
    {
        // Far above any id a live session reaches (AllocId counts from 1).
        private const ushort IdUfoFirst = 62001;
        private const int DeathBatch = 8;
        private static readonly Vector2 Nowhere = new Vector2(-4000f, -4000f);

        // A cue that is NOT looping (so it is subject to coalescing) and IS a real bank cue, so
        // the suite exercises the same path the game does. `expl1` is the UFO death cue below.
        private const string Impulse = "expl1";
        private const string Impulse2 = "expl2";

        // A LOOPING cue -- `_cfg` marks `bees` loop:true. Exempt by construction: `Play` hands
        // the caller a handle it stops later, so a coalesced second one would strand a live loop.
        private const string Looping = "bees";

        // The cue SpiderBoss deliberately doubles (verbatim 2008), i.e. the one call site in the
        // game that opts out of coalescing.
        private const string Boss = "bugdies";

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

            sb.Append("[sfxburst] same-tick SFX coalescing (card 8732568e)\n");

            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            SoundManager sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
            Game game = bin.Game;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
            List<GameComponent> planted = new List<GameComponent>();
            bool flagBefore = DebugFlags.SfxCoalesce;
            // Captured BEFORE anything runs, so the teardown restores what this suite found
            // rather than what it happens to leave. Section 3's real deaths move all three.
            float[] scoreBefore = new float[NetProtocol.MaxSlots];
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                scoreBefore[i] = score.PointScore(i);
            }
            float traumaBefore = Juice.TraumaNow;
            int explosionsBefore = CountType<Explosion>(game);
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            try
            {
                Section1Decision(sb, Check, sound);
                Section2Flag(sb, Check, sound);
                Section3RemoteDeathBatch(sb, Check, bin, game, sound, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 4. teardown\n");
                DebugFlags.SetSfxCoalesceForTest(flagBefore);
                Check("the ?sfxcoalesce flag was put back", DebugFlags.SfxCoalesce == flagBefore);
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                NetPuppets.Disable();
                bin.TopOfTickFlush();
                // COLLATERAL, none of it in `planted`: section 3 drives REAL deaths, and
                // `UFO.KilledBy` adds one or two `Explosion`s to the live bin per kill. Sweeping
                // them is the `NetDeathFxTest` precedent (whose own comment records paying for
                // sweeping only one collateral type).
                foreach (GameComponent comp in CollectType<Explosion>(game))
                {
                    bin.Remove(comp);
                }
                bin.TopOfTickFlush();
                int leaked = 0;
                foreach (GameComponent comp in planted)
                {
                    if (InWorld(game, comp)) { leaked++; }
                }
                Check("every planted puppet left the world (" + leaked + " leaked)", leaked == 0);
                Check("...and every Explosion the real deaths spawned went with them ("
                    + CountType<Explosion>(game) + " left, " + explosionsBefore + " before)",
                    CountType<Explosion>(game) <= explosionsBefore);
                // THE SCORE. `UFO.KilledBy` awards for real -- measured at +80 per run before this
                // was here, i.e. +240 over the probe's three runs, on the menu's own panels.
                for (int i = 0; i < NetProtocol.MaxSlots; i++)
                {
                    score.NetSetScore(i, scoreBefore[i]);
                }
                bool scoresBack = true;
                for (int i = 0; i < NetProtocol.MaxSlots; i++)
                {
                    scoresBack &= Math.Abs(score.PointScore(i) - scoreBefore[i]) < 0.01f;
                }
                Check("every score panel was put back where it was found", scoresBack);
                // THE SHAKE. `Explosion.Initialize` adds real trauma, so a menu run left the main
                // menu visibly rattling -- measured 0.000 -> 0.930 after ONE run.
                Juice.SetTraumaForTest(traumaBefore);
                Check("the screen shake was put back (" + Juice.TraumaNow.ToString("0.000",
                    System.Globalization.CultureInfo.InvariantCulture) + ")",
                    Math.Abs(Juice.TraumaNow - traumaBefore) < 0.001f);
                sound.SfxResetCounters();
                NetHost.Current = hostBefore;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the decision, with no world involved --------------------------------------------
        private static void Section1Decision(StringBuilder sb, Action<string, bool> Check,
            SoundManager sound)
        {
            sb.Append(" 1. THE DECISION -- one start per cue per tick\n");
            DebugFlags.SetSfxCoalesceForTest(true);

            // A BURST INSIDE ONE TICK. This is the shape the ticket describes.
            NextTick(sound);
            sound.SfxResetCounters();
            for (int i = 0; i < 12; i++)
            {
                sound.PlayCue(Impulse);
            }
            Check("12 requests for one cue in ONE tick start it ONCE (admitted="
                + sound.SfxAdmitted + ")", sound.SfxAdmitted == 1);
            Check("...and the other 11 are counted as coalesced, not lost silently (coalesced="
                + sound.SfxCoalesced + ")", sound.SfxCoalesced == 11);
            Check("...and the cue is NAMED, so a report says which sound piled up (byCue="
                + sound.SfxCoalescedByCue() + ")", sound.SfxCoalescedByCue() == Impulse + "=11");
            Check("...and every request is accounted for (requests=" + sound.SfxRequests + ")",
                sound.SfxRequests == 12);

            // THE WINDOW IS A TICK. Without this leg the suite passes on a build that plays a
            // cue once and then never again -- which is a far worse bug than the one being fixed.
            sound.SfxResetCounters();
            for (int i = 0; i < 3; i++)
            {
                NextTick(sound);
                sound.PlayCue(Impulse);
            }
            Check("CONTROL the same 3 requests spread over 3 TICKS all start (admitted="
                + sound.SfxAdmitted + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 3 && sound.SfxCoalesced == 0);

            // PER CUE, not a global "one sound at a time". A global cap would silence deliberate
            // layering -- SpiderBoss.BeginDeathThroes plays two cues together on purpose.
            NextTick(sound);
            sound.SfxResetCounters();
            sound.PlayCue(Impulse);
            sound.PlayCue(Impulse2);
            Check("CONTROL two DIFFERENT cues in one tick BOTH start -- the cap is per cue, not"
                + " global (admitted=" + sound.SfxAdmitted + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 2 && sound.SfxCoalesced == 0);

            // THE `Play` SURFACE IS NEVER COALESCED. It RETURNS the instance, so its callers keep
            // a handle (`Lazer`'s beam, `LazerGenerator`'s charge, `StarMine`'s targetacquired)
            // and a null would strand or break one. The handles are stopped straight away, so the
            // leg leaves nothing sounding.
            NextTick(sound);
            sound.SfxResetCounters();
            Microsoft.Xna.Framework.Audio.SoundEffectInstance a = sound.Play(Impulse);
            Microsoft.Xna.Framework.Audio.SoundEffectInstance b = sound.Play(Impulse);
            sound.Stop(a);
            sound.Stop(b);
            Check("CONTROL the Play() surface is NEVER coalesced -- its callers keep the handle"
                + " (admitted=" + sound.SfxAdmitted + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 2 && sound.SfxCoalesced == 0);

            // ...AND NEITHER IS `PlayText`, which is where a null would actually BITE: it stops
            // the in-flight announcer line and THEN assigns Spawn's result, so a coalesced second
            // call would stop the first line and play nothing -- total silence, worse than the
            // overlap being removed. (This leg exists because review found exactly that.)
            NextTick(sound);
            sound.SfxResetCounters();
            sound.PlayText(SoundManager.Texts.Warning, 0);
            sound.PlayText(SoundManager.Texts.Warning, 0);
            Check("CONTROL PlayText is NEVER coalesced -- it stops the previous line first, so a"
                + " null would silence the announcer (admitted=" + sound.SfxAdmitted
                + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 2 && sound.SfxCoalesced == 0);

            // LOOPING CUES ARE EXEMPT TOO, independently of the surface rule. Nothing in the game
            // PlayCues one today, so this is defence in depth -- but it has to be tested through
            // PlayCue, which is the surface the rule guards. `SfxStopCueForTest` is why that is
            // safe: PlayCue discards the handle and ReapStopped never reaps a Playing instance,
            // so without it this leg would leave two unstoppable `bees` loops per run -- six over
            // the probe's three runs, in a real browser, with no way to silence them.
            NextTick(sound);
            sound.SfxResetCounters();
            sound.PlayCue(Looping);
            sound.PlayCue(Looping);
            Check("CONTROL a LOOPING cue is exempt even through PlayCue (admitted="
                + sound.SfxAdmitted + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 2 && sound.SfxCoalesced == 0);
            sound.SfxStopCueForTest(Looping);

            // THE ONE DELIBERATE OPT-OUT. `SpiderBoss.CollidesWith` plays "bugdies" twice in a
            // row -- verbatim 2008 code, i.e. an authored +6 dB emphasis on landing a beam on the
            // boss rather than an accident -- so it opts out. Asserted with the SAME cue through
            // the ordinary surface beside it, or the leg would pass on a build where "bugdies"
            // simply never coalesces.
            NextTick(sound);
            sound.SfxResetCounters();
            sound.PlayCue(Boss, allowSameTick: true);
            sound.PlayCue(Boss, allowSameTick: true);
            Check("the deliberate opt-out plays BOTH copies (admitted=" + sound.SfxAdmitted
                + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 2 && sound.SfxCoalesced == 0);
            NextTick(sound);
            sound.SfxResetCounters();
            sound.PlayCue(Boss);
            sound.PlayCue(Boss);
            Check("CONTROL the SAME cue through the ordinary surface still coalesces -- the"
                + " opt-out is the CALL, not the cue (admitted=" + sound.SfxAdmitted
                + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 1 && sound.SfxCoalesced == 1);
        }

        // ---- 2. the ?sfxcoalesce=0 A/B seam ------------------------------------------------------
        private static void Section2Flag(StringBuilder sb, Action<string, bool> Check,
            SoundManager sound)
        {
            sb.Append(" 2. THE FLAG -- ?sfxcoalesce=0 restores the pre-card behaviour\n");
            DebugFlags.SetSfxCoalesceForTest(false);
            NextTick(sound);
            sound.SfxResetCounters();
            for (int i = 0; i < 12; i++)
            {
                sound.PlayCue(Impulse);
            }
            Check("with coalescing OFF all 12 start, exactly as before the card (admitted="
                + sound.SfxAdmitted + ", coalesced=" + sound.SfxCoalesced + ")",
                sound.SfxAdmitted == 12 && sound.SfxCoalesced == 0);
            // Restored HERE, not only in the teardown: section 3 measures the shipped behaviour
            // and would otherwise measure the flag instead.
            DebugFlags.SetSfxCoalesceForTest(true);
            Check("...and the flag really flips back on", DebugFlags.SfxCoalesce);
        }

        // ---- 3. THE REPORTED PATH: a batch of remote deaths in ONE tick --------------------------
        //
        // Sections 1 and 2 prove the audio layer's decision. This proves the decision is on the
        // path the ticket is about: `NetPuppets.OnRemoteDeath` runs the real per-type death, a
        // `UFO`'s real death plays `expl1`, and a joining client applies a whole batch of EvDeaths
        // inside ONE `DrainRx` -- so without the coalescer every one of them starts its own copy.
        private static void Section3RemoteDeathBatch(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, SoundManager sound, List<GameComponent> planted)
        {
            sb.Append(" 3. THE REPORTED PATH -- a batch of remote deaths applied in ONE tick\n");
            NetPuppets.Enable(game);
            byte ufoType = TypeIdxOf(new UFO(game));
            List<UFO> ufos = new List<UFO>();
            for (int i = 0; i < DeathBatch; i++)
            {
                UFO u = (UFO)BuildPuppet<UFO>(game, (ushort)(IdUfoFirst + i), ufoType, planted);
                if (u != null)
                {
                    ufos.Add(u);
                }
            }
            Check("PRECONDITION " + DeathBatch + " UFO puppets were built (" + ufos.Count + ")",
                ufos.Count == DeathBatch);
            if (ufos.Count != DeathBatch)
            {
                return;
            }

            // ONE TICK, then the whole batch -- exactly what a client's rx drain does.
            NextTick(sound);
            sound.SfxResetCounters();
            for (int i = 0; i < DeathBatch; i++)
            {
                NetPuppets.OnRemoteDeath((ushort)(IdUfoFirst + i), 0, Nowhere);
            }
            long asked = sound.SfxRequests;
            Check("PRECONDITION the batch really reached the audio layer -- " + DeathBatch
                + " deaths asked for " + asked + " cue starts", asked >= DeathBatch);
            // EXACTLY ONE, not "fewer than asked". All eight are `expl1` (UFO.KilledBy's small
            // branch), so a coalescer that folded only some of them would still satisfy a `<`
            // and the leg's own prose would be false.
            Check("...and the tick started EXACTLY ONE copy (admitted="
                + sound.SfxAdmitted + " for " + asked + " requests, coalesced="
                + sound.SfxCoalesced + "; byCue=" + sound.SfxCoalescedByCue() + ")",
                sound.SfxAdmitted == 1 && sound.SfxCoalesced == asked - 1);
            Check("...and the cue that piled up is an EXPLOSION, i.e. the reported sound (byCue="
                + sound.SfxCoalescedByCue() + ")",
                sound.SfxCoalescedByCue().Contains("expl"));
        }

        // ---- helpers -----------------------------------------------------------------------------

        // One game tick as far as the coalescer is concerned. `SoundManager.Update` is what
        // advances the window and Game1 calls it once per tick; the GameTime it is handed is
        // unread, so a zero one is honest here rather than a fake.
        private static void NextTick(SoundManager sound)
        {
            sound.Update(new GameTime());
        }

        private static byte TypeIdxOf(GameComponent probe)
        {
            NetTypeRegistry.TryGet(probe, out byte idx, out _);
            return idx;
        }

        // Build through the REAL snapshot self-heal, then identify it as "the T that was not
        // there before" -- a bare type scan would latch onto one the world already owns.
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
            return "[sfxburst] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
