using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE SCENARIO HARNESS (card 25ad0659 step 4; design plans/net-headless-sim.md). Run
    // `eaNetScenarios()` from the MAIN MENU, or `eval NetScenarios` under eahl. Committed as a
    // leg of tools/headless/probes/net_selftests.txt.
    //
    // WHAT IT IS. Five of the design doc's six scenarios, each driving ONE REAL NetSession over
    // one endpoint of an in-process NetWire while a SCRIPTED peer drives the other end with real
    // NetProtocol.Encode* frames. Scenario 6 (reset/pause ordering) needs a live GameScene and so
    // lives in NetSceneOrderTest, with its own destructive probe.
    //
    // WHY ONE REAL SIDE AND NOT TWO. The doc's original plan was two NetContexts in one process;
    // measured, that is unreachable -- ComponentBin's only ctor does `collection = game.Components`
    // and Oracle + CollisionHandler bind to that same collection, so two contexts under one Game
    // share one WORLD and the host context's NetIdRegistry would allocate ids for the client
    // context's puppets. None of these scenarios needs both sides real: what is under test is one
    // peer's REACTION to traffic, and traffic is exactly what a script can supply. The cost is
    // that the scripted side can drift from the encoder it stands in for -- which is why every
    // frame below is built with the REAL codec and never hand-rolled.
    //
    // THE TWO SESSIONS, and why they are sequential rather than one. Scenarios 1-4 are about the
    // HOST honouring claims (NetSession.HandleClaim); scenario 5 is about the CLIENT self-healing
    // through id churn (NetPuppets). A session is one role, so the suite runs a host session,
    // stops it, then runs a client session. `Stop()` resets every piece of per-session state,
    // which is exactly what makes that safe.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE, the eaNetSnap shape rather than eaNetResetSpawn's. Two
    // measurements are why: `HandleClaim` reads no scene (it reaches NetIdRegistry / bin / score /
    // sound / Explosion / NetPuppets.KillerAgent, and the per-type `KilledBy` it runs is scene-free
    // for the types used here), and the client rx paths gate on `NetScene.Current != null` rather
    // than on a real GameScene -- so scenario 5 supplies a recording stand-in for the seam. Every
    // entity built is taken back out, the roster is restored and asserted restored.
    //
    // THE ASSERTIONS ARE PROPERTIES OF THE GENEROUS-CLAIM CONTRACT, not a restatement of the
    // implementation: "every distinct claimant is paid, nobody is paid twice, the entity leaves
    // the world once". Each scenario reads the real NetMetrics counters the `[net]` line prints,
    // so a green run here is evidence about the same numbers a two-window playtest reads.
    internal static class NetScenarioTest
    {
        private const string Room = "scenarios";

        // The slot the scripted peer claims as. >= 1 so it can never be the host's own primary.
        private const byte PeerSlot = 1;

        // A SECOND claimant, for the double-claim and OneUp-overlap scenarios -- a couch player
        // on the peer's console. Distinct from PeerSlot is the whole point: the ledgers are keyed
        // per (netId, slot), so two claims from ONE slot must pay once while two from two slots
        // must pay twice.
        private const byte PeerSlot2 = 2;

        // A THIRD claimant, used only as leg 2c's negative control: a slot that was never paid
        // for that entity must still be paid, which is what stops an over-broad PaidMask fold
        // passing the two refusals above.
        private const byte PeerSlot3 = 3;

        // The host's own primary seat. Only leg 3b needs it: it kills an entity the way the HOST
        // does rather than by honouring a claim, so the kill must be attributed somewhere that is
        // neither claimant.
        private const byte HostSlot = 0;

        private const ulong PeerToken = 0x5CE7A5C0UL;

        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // The descriptor the churn scenario spawns through. EvilBullet is the simplest replicable
        // -- no spawn extras, no state extras -- and index 0 of NetTypeRegistry is its descriptor.
        // Named because the cleanup sweep has to agree with it, and two bare 0s in two places with
        // nothing tying them together is how that drifts.
        private const byte ChurnTypeIdx = 0;
        // A typeIdx no descriptor claims -- the dupBad negative control (card 4c9448c8). The
        // registry is dense from 0 and far shorter than this, and the scenario asserts that
        // rather than assuming it, so appending descriptors can never quietly make it valid.
        private const byte UnknownTypeIdx = 254;

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

            sb.Append("[netscen] step-4 scenarios 1-5 (card 25ad0659)\n");

            // The eaBinTest / eaNetSnap gate: this suite starts and stops REAL sessions and adds
            // real entities to the live bin, so a live session, level or attract demo is not
            // something to work around -- it is a reason to report a SKIP rather than let an
            // unrun suite read as a pass.
            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
            Game game = bin.Game;

            // Everything the scenarios add, so the finally can take back exactly what it put in
            // even after a throw. The bin is the LIVE one; nothing here may outlive the run.
            List<GameComponent> planted = new List<GameComponent>();

            // Every score/lives assertion below is a DELTA against these, never an absolute --
            // the menu carries whatever the last play left in the panels.
            float[] scoreBefore = new float[ScoreVisualiser.SlotCount];
            for (int i = 0; i < scoreBefore.Length; i++)
            {
                scoreBefore[i] = score.PointScore(i);
            }
            int livesBefore = score.Lives;
            int playersBefore = oracle.Players;
            // The OneUp scenario settles a real pickup, and HandleClaim's pickup branch drives
            // ApplyRemotePowerup -> SetPowerup, which raises `powerupactive` on the claimant's
            // panel. That flag is the gate ScoreVisualiser.increasecombo reads to feed AddExp, so
            // leaving it set would not merely draw a stray icon -- it would change the next real
            // game. Snapshot it with the rest.
            bool[] powerupBefore = new bool[ScoreVisualiser.SlotCount];
            for (int i = 0; i < powerupBefore.Length; i++)
            {
                powerupBefore[i] = score.NetPowerupActive(i);
            }

            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunClaimScenarios(sb, Check, bin, score, game, planted, scoreBefore);
                RunChurnScenario(sb, Check, bin, game, planted, clock);
            }
            catch (Exception ex)
            {
                Check("the scenarios ran (" + Describe(ex) + ")", ok: false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" 9. teardown -- what this suite must hand back\n");
                Teardown(sb, Check, oracle, bin, score, planted, scoreBefore, livesBefore,
                    playersBefore, powerupBefore);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back (no override left standing)", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- scenarios 1-4: the HOST honouring claims -------------------------------------
        private static void RunClaimScenarios(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, ScoreVisualiser score, Game game,
            List<GameComponent> planted, float[] scoreBefore)
        {
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            sb.Append(" 0. rig -- a real HOST session with a scripted client on the wire\n");
            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            Check("session started as the HOST", NetSession.IsHost);

            // The scripted client says hello. It carries LocalBuildHash (READ, never recomputed --
            // re-deriving it here would drift from StartWith's own ?netfakehash-aware expression)
            // and a blockedSlots mask of 0, which is what a peer at the menu really sends.
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("the scripted client paired (peer=" + (NetSession.PeerUp ? "up" : "down") + ")",
                NetSession.PeerUp);

            NetMetrics m = NetSession.Metrics;

            // ---- 1. KILL CLAIM, happy path -----------------------------------------------
            // The client hit-tested a live puppet on its own screen and claimed it. The host's
            // copy is still alive, so the claim runs the REAL per-type death -- and that is the
            // half a hand-rolled fake could never cover, which is why the entity is a real UFO
            // built by its own New*+Setup factory (the harness-proven path).
            sb.Append(" 1. kill claim (happy path) -- a live entity, one claimant\n");
            long claimsRxBefore = m.ClaimsRx;
            long honoredBefore = m.ClaimsHonored;
            long paidDeadBefore = m.ClaimsPaidDead;
            UFO victim = Plant(bin, game, planted);
            bool gotId = NetIdRegistry.TryGetByComp((GameComponent)(object)victim, out NetIdRegistry.Entry vEntry);
            Check("PRECONDITION the host registry allocated a netId for the planted entity"
                + (gotId ? " (id=" + vEntry.Id + ")" : " -- NONE"), gotId);
            if (!gotId)
            {
                // Everything below indexes that id; a wrong-id claim would silently no-op and
                // every assertion under it would read as a pass for the wrong reason.
                TeardownSession(sb, Check);
                return;
            }
            ushort victimId = vEntry.Id;
            float pointValue = victim.NetPointValue;

            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, victimId, PeerSlot));
            wire.Pump();
            NetSession.Update();

            // A DELTA, not `> 0`: NetSession.metrics is process-lifetime and nothing resets it,
            // so an absolute test would already be satisfied by an earlier suite in the same run.
            Check("the host counted the claim (rx +1)", m.ClaimsRx == claimsRxBefore + 1);
            Check("... and HONOURED it as a live kill (honored +1, was " + honoredBefore + ")",
                m.ClaimsHonored == honoredBefore + 1);
            Check("... and did NOT take the already-dead branch (paidDead unchanged)",
                m.ClaimsPaidDead == paidDeadBefore);
            bin.TopOfTickFlush();
            Check("the entity left the world", !InWorld(game, (GameComponent)(object)victim));
            Check("... and left the id registry", !NetIdRegistry.TryGetById(victimId, out _));
            // The claimant is paid ONCE. PointValue is the base; the real death path runs
            // AwardScore, which combo-modifies it, so the assertion is "credited, and by at
            // least the base value" rather than an exact figure the combo state could move.
            float paid1 = score.PointScore(PeerSlot) - scoreBefore[PeerSlot];
            Check("the claimant slot was credited (+" + Round(paid1) + ", base " + Round(pointValue) + ")",
                paid1 >= pointValue);
            Check("no OTHER slot was credited",
                Math.Abs(score.PointScore(PeerSlot2) - scoreBefore[PeerSlot2]) < 0.01f);
            planted.Remove((GameComponent)(object)victim);

            // ---- 2. DOUBLE CLAIM -- both peers, same target, inside the RTT ---------------
            // The generous-pay proof. Two claims for ONE netId from DISTINCT slots: the first
            // settles the live entity, the second arrives once it is already dead and must be
            // paid from the recent-death record. Neither may be paid twice, and the entity may
            // only leave the world once.
            //
            // THE TWO CLAIMS ARE A TICK APART, which is what makes this the CONTROL for 2b
            // below. The death RECORD a second claimant is paid from is written by OnHostDeath
            // at the ComponentRemoved seam, i.e. at the next flush, so the flush between these
            // two claims is what puts the second one on the record's side of that seam. 2b sends
            // the same pair with the flush taken away and is paid from the Entry's ledger
            // instead (card 1bfcd705); both must hold, and they are different code.
            sb.Append(" 2. double claim -- one target, two distinct claimants inside the RTT\n");
            honoredBefore = m.ClaimsHonored;
            paidDeadBefore = m.ClaimsPaidDead;
            float s1Before = score.PointScore(PeerSlot);
            float s2Before = score.PointScore(PeerSlot2);
            UFO shared = Plant(bin, game, planted);
            Check("PRECONDITION the shared target got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)shared, out NetIdRegistry.Entry sEntry));
            ushort sharedId = sEntry?.Id ?? 0;

            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sharedId, PeerSlot));
            wire.Pump();
            NetSession.Update();
            bin.TopOfTickFlush();
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sharedId, PeerSlot2));
            wire.Pump();
            NetSession.Update();

            Check("exactly ONE claim settled the live entity (honored +1)",
                m.ClaimsHonored == honoredBefore + 1);
            Check("the SECOND was paid from the death record (paidDead +1) -- the generous-pay proof",
                m.ClaimsPaidDead == paidDeadBefore + 1);
            float d1 = score.PointScore(PeerSlot) - s1Before;
            float d2 = score.PointScore(PeerSlot2) - s2Before;
            Check("claimant A was paid (+" + Round(d1) + ")", d1 > 0f);
            Check("claimant B was paid too (+" + Round(d2) + ")", d2 > 0f);
            Check("the entity left the world exactly once",
                !InWorld(game, (GameComponent)(object)shared) && !NetIdRegistry.TryGetById(sharedId, out _));
            planted.Remove((GameComponent)(object)shared);

            // A THIRD claim from a slot already paid must be a no-op. This is the PaidMask, and
            // it is the assertion that stops "generous" meaning "unbounded" -- without it a peer
            // re-sending its claim would farm points.
            paidDeadBefore = m.ClaimsPaidDead;
            s2Before = score.PointScore(PeerSlot2);
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sharedId, PeerSlot2));
            wire.Pump();
            NetSession.Update();
            Check("a REPEAT claim from an already-paid slot pays nothing (paidDead unchanged)",
                m.ClaimsPaidDead == paidDeadBefore);
            Check("... and moves no score",
                Math.Abs(score.PointScore(PeerSlot2) - s2Before) < 0.01f);

            // ---- 2b. THE SAME-TICK PAIR ---------------------------------------------------
            // Scenario 2 with the flush between the two claims taken away: both land in ONE
            // DrainRx. The first settles the entity; the second finds it in the registry but
            // already dead, and its recentDeaths record does not exist yet -- OnHostDeath writes
            // that at the ComponentRemoved seam, one ComponentBin flush later. Until card
            // 1bfcd705 the second claimant was therefore paid NOTHING, breaking "every distinct
            // claimant is credited" for the whole width of that window; the Entry's own ledger
            // (NetIdRegistry.Entry.ClaimPaidMask) is what pays it now. Scenario 2 above is the
            // tick-separated control for exactly this.
            sb.Append(" 2b. same-tick double claim -- one DrainRx, no flush between\n");
            paidDeadBefore = m.ClaimsPaidDead;
            // Re-captured rather than reusing scenario 2's baseline: this leg asserts "+1", and
            // reading it off a counter last taken two scenarios ago would make the message and
            // the arithmetic disagree the moment anything above changes.
            honoredBefore = m.ClaimsHonored;
            s1Before = score.PointScore(PeerSlot);
            s2Before = score.PointScore(PeerSlot2);
            UFO sameTick = Plant(bin, game, planted);
            Check("PRECONDITION the same-tick target got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)sameTick, out NetIdRegistry.Entry stEntry));
            ushort sameTickId = stEntry?.Id ?? 0;
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickId, PeerSlot));
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickId, PeerSlot2));
            wire.Pump();
            NetSession.Update();
            float sameTickPaidA = score.PointScore(PeerSlot) - s1Before;
            float sameTickPaidB = score.PointScore(PeerSlot2) - s2Before;
            Check("the FIRST same-tick claimant was paid by the live kill (+"
                + Round(sameTickPaidA) + ")", sameTickPaidA > 0f);
            Check("the SECOND was paid from the Entry's ledger, before any flush (paidDead +1)",
                m.ClaimsPaidDead == paidDeadBefore + 1);
            Check("... and its slot really moved (+" + Round(sameTickPaidB) + ")", sameTickPaidB > 0f);
            Check("... and it settled as ONE live kill, not two (honored +1)",
                m.ClaimsHonored == honoredBefore + 1);
            bin.TopOfTickFlush();
            Check("the same-tick pair still removed the entity exactly once",
                !InWorld(game, (GameComponent)(object)sameTick)
                && !NetIdRegistry.TryGetById(sameTickId, out _));
            planted.Remove((GameComponent)(object)sameTick);

            // ---- 2c. THE LEDGER SURVIVES THE FLUSH ----------------------------------------
            // The other half of the fix, and the ONLY leg that pins it. OnHostDeath builds the
            // death record from the Entry's mask (RecordDeath's prepaidMask), so both slots paid
            // in the pre-flush window above are already masked in the record written for the same
            // id a flush later. Without that fold, `recentDeaths[id] = rec` would overwrite the
            // mask with the kill note's single bit and BOTH of these repeats would be paid a
            // second time -- the generous contract turning into a farm at exactly the seam the
            // card was about.
            sb.Append(" 2c. the pre-flush ledger survives into the death record\n");
            paidDeadBefore = m.ClaimsPaidDead;
            s1Before = score.PointScore(PeerSlot);
            s2Before = score.PointScore(PeerSlot2);
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickId, PeerSlot));
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickId, PeerSlot2));
            wire.Pump();
            NetSession.Update();
            Check("neither pre-flush payee is paid again from the record (paidDead unchanged)",
                m.ClaimsPaidDead == paidDeadBefore);
            Check("... and neither slot moved (A +" + Round(score.PointScore(PeerSlot) - s1Before)
                + ", B +" + Round(score.PointScore(PeerSlot2) - s2Before) + ")",
                Math.Abs(score.PointScore(PeerSlot) - s1Before) < 0.01f
                && Math.Abs(score.PointScore(PeerSlot2) - s2Before) < 0.01f);
            // The negative control, without which a fold of the WRONG mask passes everything
            // above: a record built with a blanket 0xFF (or the prepaid bits smeared) refuses
            // both slots just as convincingly. A slot that was never paid for this entity must
            // still be paid, i.e. the fold carried those two bits and no others.
            paidDeadBefore = m.ClaimsPaidDead;
            float s3Before = score.PointScore(PeerSlot3);
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickId, PeerSlot3));
            wire.Pump();
            NetSession.Update();
            Check("... while a slot NEVER paid for that entity still IS (paidDead +1, +"
                + Round(score.PointScore(PeerSlot3) - s3Before) + ")",
                m.ClaimsPaidDead == paidDeadBefore + 1
                && score.PointScore(PeerSlot3) - s3Before > 0f);

            // ---- 3. LATE CLAIM -- the host reaped it first --------------------------------
            // Same ledger, reached from the other direction: here the HOST kills the entity
            // (broadcasting its own EvDeath) and the client's claim arrives afterwards. It is a
            // distinct path from scenario 2's -- that one's first claim wrote the death record,
            // this one's is written by the host's own death broadcast.
            sb.Append(" 3. late claim -- the host already reaped it\n");
            honoredBefore = m.ClaimsHonored;
            paidDeadBefore = m.ClaimsPaidDead;
            s1Before = score.PointScore(PeerSlot);
            UFO reaped = Plant(bin, game, planted);
            Check("PRECONDITION the reaped target got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)reaped, out NetIdRegistry.Entry rEntry));
            ushort reapedId = rEntry?.Id ?? 0;
            // A TEARDOWN-STYLE removal, deliberately -- no Die(), no KilledBy, no award. What
            // this scenario needs is only that the id is out of the registry and its death record
            // written before the claim lands, and the removal seam does both either way. Calling
            // it "the host's own kill" would claim fidelity to a path this does not take: the net
            // layer distinguishes the two by IsDead, which stays false here.
            bin.Remove((GameComponent)(object)reaped);
            bin.TopOfTickFlush();
            Check("PRECONDITION the host's copy is gone before the claim lands",
                !NetIdRegistry.TryGetById(reapedId, out _));

            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, reapedId, PeerSlot));
            wire.Pump();
            NetSession.Update();
            Check("the late claim did NOT run a live kill (honored unchanged)",
                m.ClaimsHonored == honoredBefore);
            Check("the claimant was paid from recentDeaths once (paidDead +1)",
                m.ClaimsPaidDead == paidDeadBefore + 1);
            planted.Remove((GameComponent)(object)reaped);

            paidDeadBefore = m.ClaimsPaidDead;
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, reapedId, PeerSlot));
            wire.Pump();
            NetSession.Update();
            Check("a SECOND late claim for the same (netId, slot) is a no-op (PaidMask)",
                m.ClaimsPaidDead == paidDeadBefore);
            // ... but a DIFFERENT slot on the same dead id still gets paid. Without this, a
            // PaidMask that latched per ID rather than per (id, slot) would pass everything
            // above -- it is the negative control for the assertion just made.
            paidDeadBefore = m.ClaimsPaidDead;
            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, reapedId, PeerSlot2));
            wire.Pump();
            NetSession.Update();
            Check("... while a DIFFERENT slot on the same dead id IS paid (the ledger is per"
                + " (netId, slot), not per netId)", m.ClaimsPaidDead == paidDeadBefore + 1);

            // ---- 3b. THE HOST'S OWN KILL, CLAIMED IN THE SAME TICK ------------------------
            // Scenario 3 with the flush taken away, and the one shape of card 1bfcd705 that is
            // reachable on TODAY'S 2-peer wire rather than only at N peers. Game1.UpdateInner
            // runs TopOfTickFlush -> base.Update -> collectionHelper.Update -> DetectCollisions
            // -> NetSession.Update, so a host kill in the COLLISION phase leaves the entity dead
            // with its removal still queued when that same tick's DrainRx runs. A client claim
            // landing in that one-tick window used to find the entity dead and no record yet, and
            // was paid nothing -- the player lost the points for a kill they legitimately
            // claimed, silently. The Entry's ledger is what pays it now.
            //
            // The kill goes through NetKill because that is the forced-kill entry the harness
            // can reach; it runs the same real per-type KilledBy + Die as a host bullet's HitBy
            // (it only bypasses the hittimer gate), and what this leg is about is the TIMING --
            // dead, removal queued, no record -- which is identical either way.
            sb.Append(" 3b. the host's own kill, claimed in the SAME tick -- reachable today\n");
            honoredBefore = m.ClaimsHonored;
            paidDeadBefore = m.ClaimsPaidDead;
            s1Before = score.PointScore(PeerSlot);
            UFO hostKill = Plant(bin, game, planted);
            Check("PRECONDITION the host-killed target got a netId",
                NetIdRegistry.TryGetByComp((GameComponent)(object)hostKill, out NetIdRegistry.Entry hkEntry));
            ushort hostKillId = hkEntry?.Id ?? 0;
            ((INetEntity)hostKill).NetKillable?.NetKill(
                NetPuppets.KillerAgent(HostSlot, ((INetEntity)hostKill).Position), isComboGenerator: true);
            // No flush: this is the whole point. If either half of this stops holding the leg
            // below is vacuous -- a live entity would take the live-kill branch instead, and a
            // deregistered one would be scenario 3 over again.
            Check("PRECONDITION the host's copy is DEAD but still registered -- the removal has"
                + " not flushed", ((INetEntity)hostKill).IsDead
                && NetIdRegistry.TryGetById(hostKillId, out _));

            peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, hostKillId, PeerSlot));
            wire.Pump();
            NetSession.Update();
            Check("the same-tick claim did NOT run a second live kill (honored unchanged)",
                m.ClaimsHonored == honoredBefore);
            Check("the claimant was paid from the Entry's ledger (paidDead +1)",
                m.ClaimsPaidDead == paidDeadBefore + 1);
            Check("... and its slot really moved (+"
                + Round(score.PointScore(PeerSlot) - s1Before) + ")",
                score.PointScore(PeerSlot) - s1Before > 0f);
            bin.TopOfTickFlush();
            Check("... and the host's own kill removed it exactly once",
                !InWorld(game, (GameComponent)(object)hostKill)
                && !NetIdRegistry.TryGetById(hostKillId, out _));
            planted.Remove((GameComponent)(object)hostKill);

            // ---- 4. ONEUP OVERLAP ---------------------------------------------------------
            // Lives are host-authoritative -- the next EvScoreSync sends them verbatim -- so a
            // OneUp collected on the client only survives if the HOST applies it. Two collectors
            // inside the RTT window must each add one, and neither may be reverted by the sync
            // the host sends afterwards.
            sb.Append(" 4. OneUp overlap -- two collectors inside the RTT window\n");
            int livesAtStart = score.Lives;
            Powerup oneUp = PlantOneUp(bin, game, planted);
            bool gotPickup = NetIdRegistry.TryGetByComp((GameComponent)(object)oneUp, out NetIdRegistry.Entry pEntry);
            Check("PRECONDITION the OneUp is registered and reads as a pickup"
                + (gotPickup ? "" : " -- NO ENTRY"),
                gotPickup && ((INetEntity)oneUp).NetPickup != null
                && ((INetEntity)oneUp).NetPickup.NetPickupType == Powerup.PowerupType.OneUp);
            if (gotPickup)
            {
                ushort oneUpId = pEntry.Id;
                // A tick apart, for scenario 2's reason: the second collector is paid from the
                // death record, which the removal seam writes at the flush between them.
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, oneUpId, PeerSlot));
                wire.Pump();
                NetSession.Update();
                bin.TopOfTickFlush();
                Check("the first collector's claim took the powerup out of the world",
                    !InWorld(game, (GameComponent)(object)oneUp)
                    && !NetIdRegistry.TryGetById(oneUpId, out _));
                Check("... and added exactly one life (lives " + livesAtStart + " -> "
                    + score.Lives + ")", score.Lives == livesAtStart + 1);
                planted.Remove((GameComponent)(object)oneUp);

                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, oneUpId, PeerSlot2));
                wire.Pump();
                NetSession.Update();
                Check("the OVERLAPPING collector is paid its own life from the record (lives "
                    + score.Lives + ")", score.Lives == livesAtStart + 2);

                // ---- 4b. THE REPEAT, a tick apart ------------------------------------------
                // The other half of the PaidMask, on the branch where getting it wrong hands out
                // free LIVES rather than points. It holds here because the live branch's
                // NoteKillSlot attribution is what the removal seam writes into the record -- so
                // the slot paid live is already masked by the time a repeat arrives. 4c below is
                // the same pair with the flush taken away.
                sb.Append(" 4b. repeat claim from an already-paid collector\n");
                int livesAfterOverlap = score.Lives;
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, oneUpId, PeerSlot));
                wire.Pump();
                NetSession.Update();
                Check("a repeat from the LIVE-branch collector leaks no life -- the removal"
                    + " seam masked it (lives " + livesAfterOverlap + " -> " + score.Lives + ")",
                    score.Lives == livesAfterOverlap);
                // The bound that DOES hold, and the one that matters: the ledger still refuses a
                // slot the RECORD has already paid, so the leak is one life per collector, not
                // an unbounded farm.
                int livesAfterRepeat = score.Lives;
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, oneUpId, PeerSlot));
                wire.Pump();
                NetSession.Update();
                Check("a repeat from a slot the RECORD has paid adds nothing -- the leak is"
                    + " bounded, not a farm (lives " + livesAfterRepeat + " -> " + score.Lives + ")",
                    score.Lives == livesAfterRepeat);

                // ---- 4c. THE SAME-TICK REPEAT + OVERLAP, ON A PICKUP ----------------------
                // The half of card 1bfcd705 that cost LIVES rather than points, and the worst of
                // it. A Powerup's settle path calls NetMarkTaken() -- which sets `taken`, NOT
                // `isdead` -- and queues the removal, so before the fix every same-tick claim
                // re-entered the LIVE branch in full: measured lives +3 for (A, A, B), i.e. one
                // free life per claim frame a peer could cram into one DrainRx, plus three
                // ClaimsHonored for one settlement. Entry.ClaimSettled is what makes the live
                // branch run once; Entry.ClaimPaidMask is what refuses A's repeat while still
                // paying B. The generic non-killable arm (an EvilBullet swept by a blast: an
                // explosion and its NetPointValue per claim) sits behind the SAME gate on the
                // SAME line, so it needs no leg of its own.
                sb.Append(" 4c. same-tick (A, A, B) on a pickup -- the live branch runs once\n");
                int livesBeforeSameTick = score.Lives;
                long honoredBeforeSameTick = m.ClaimsHonored;
                paidDeadBefore = m.ClaimsPaidDead;
                Powerup sameTickUp = PlantOneUp(bin, game, planted);
                Check("PRECONDITION the same-tick OneUp got a netId",
                    NetIdRegistry.TryGetByComp((GameComponent)(object)sameTickUp,
                        out NetIdRegistry.Entry stuEntry));
                ushort sameTickUpId = stuEntry?.Id ?? 0;
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickUpId, PeerSlot));
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickUpId, PeerSlot));
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, sameTickUpId, PeerSlot2));
                wire.Pump();
                NetSession.Update();
                Check("TWO collectors, THREE claims, exactly two lives (lives "
                    + livesBeforeSameTick + " -> " + score.Lives + ")",
                    score.Lives == livesBeforeSameTick + 2);
                Check("... settled as ONE live pickup (honored +1)",
                    m.ClaimsHonored == honoredBeforeSameTick + 1);
                Check("... with the overlapping collector paid from the ledger (paidDead +1)",
                    m.ClaimsPaidDead == paidDeadBefore + 1);
                bin.TopOfTickFlush();
                Check("... and the powerup left the world and the registry",
                    !InWorld(game, (GameComponent)(object)sameTickUp)
                    && !NetIdRegistry.TryGetById(sameTickUpId, out _));
                planted.Remove((GameComponent)(object)sameTickUp);
            }

            TeardownSession(sb, Check);
        }

        // ---- scenario 5: the CLIENT self-healing through id churn --------------------------
        // The item-1 residual probe. The host purges N replicables and spawns M fresh ids in one
        // tick while the unreliable stream lane is reordered AHEAD of the ordered reliable one --
        // which is the ordinary shape of a checkpoint replay, and the transition the first-wipe
        // pupPops burst was last seen in. What is asserted is that the client SELF-HEALS: bounded
        // snapNew, bounded DupSpawns, no leaked puppets, and pupPops reported rather than assumed.
        private static void RunChurnScenario(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted, PinnedNetHost clock)
        {
            const int Churn = 12;

            sb.Append(" 5. id churn -- purge + replay with the stream lane reordered\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            // The client rx paths (EvSpawn / EvDeath / snapshots) gate on "is a scene up", which
            // the seam answers -- so this needs the SEAM, not a GameScene. A recording stand-in
            // is honest here for the same reason a blank one would not be in NetResetSpawnTest:
            // nothing in this scenario is about what the scene DOES, only that one exists.
            ScenarioScene scene = new ScenarioScene();
            NetScene.Current = scene;

            NetSession.StartForTest(game, host: false, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, PeerSlot, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("session started as a CLIENT and paired", NetSession.IsClient && NetSession.PeerUp);

            NetMetrics m = NetSession.Metrics;
            long dupBefore = m.DupSpawns;
            long dupLiveBefore = m.DupLive;
            long dupBadBefore = m.DupBad;
            long ordViolBefore = m.OrderViolations;
            long snapNewBefore = m.SnapNew;
            long snapBadBefore = m.SnapBad;
            long popsBefore = m.PuppetPops;
            int liveBefore = NetPuppets.LiveCount;

            // A first generation of puppets, spawned the ordinary way.
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            byte[] noExtras = new byte[1];
            for (int i = 0; i < Churn; i++)
            {
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, (ushort)(9000 + i), ChurnTypeIdx,
                    state, noExtras, 0));
            }
            wire.Pump();
            NetSession.Update();
            // Track them from here on. A throw anywhere below unwinds to Run's catch, and Run's
            // teardown only removes what `planted` holds -- so without this a failed run would
            // leave twelve frozen puppets in Game.Components at the main menu, permanently.
            TrackPuppets(game, planted);
            Check("generation 1 built " + Churn + " puppets (live=" + NetPuppets.LiveCount + ")",
                NetPuppets.LiveCount == liveBefore + Churn);
            Check("... with no duplicate spawns", m.DupSpawns == dupBefore);

            // THE CHURN. Generation 1 dies and generation 2 spawns in the SAME tick, and the
            // stream lane carries snapshots for the NEW ids that arrive BEFORE their EvSpawns --
            // the lane race the self-heal exists for. Sending the snapshot frames first is what
            // reorders them: the reliable lane is drained in the same Update, so a snapshot
            // queued ahead of a spawn really is seen first.
            for (int i = 0; i < Churn; i++)
            {
                peer.SendStream(SnapshotFor((ushort)(9100 + i), state));
            }
            for (int i = 0; i < Churn; i++)
            {
                peer.SendReliable(NetProtocol.EncodeDeathEvent(eventSeq++, (ushort)(9000 + i),
                    NetProtocol.KillerNone, Nowhere, new float[NetProtocol.MaxSlots]));
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, (ushort)(9100 + i), ChurnTypeIdx,
                    state, noExtras, 0));
            }
            wire.Pump();
            NetSession.Update();
            bin.TopOfTickFlush();

            long snapNewDelta = m.SnapNew - snapNewBefore;
            Check("the stream outran the reliable lane and the self-heal REBUILT those ids"
                + " (snapNew +" + snapNewDelta + ") -- the positive control for this scenario",
                snapNewDelta > 0);
            Check("... and nothing was REFUSED (snapBad unchanged, was " + snapBadBefore + ")",
                m.SnapBad == snapBadBefore);
            // The self-heal rebuilds an id it has never seen, so the EvSpawn that follows for the
            // same id is a duplicate BY CONSTRUCTION -- bounded by the churn, not zero. Asserting
            // zero here would be asserting the race did not happen.
            long dupDelta = m.DupSpawns - dupBefore;
            Check("duplicate spawns are BOUNDED by the churn (+" + dupDelta + " over " + Churn
                + " reordered ids)", dupDelta <= Churn);

            // THE SPLIT (card 4c9448c8). Every duplicate this scenario produces is the benign
            // already-live shape, so the whole delta must land in dupLive and dupBad must not
            // move. This is the leg that makes `dup` readable: it is the live race, not a
            // synthetic one, and before the split these were indistinguishable at the counter.
            long dupLiveDelta = m.DupLive - dupLiveBefore;
            Check("... and EVERY one is the benign already-live shape (dupLive +" + dupLiveDelta
                + " == dup +" + dupDelta + ")", dupLiveDelta == dupDelta);
            Check("... with dupBad UNMOVED (was " + dupBadBefore + ", now " + m.DupBad + ")",
                m.DupBad == dupBadBefore);

            // NEGATIVE CONTROL, and the split is worthless without it: a classifier hard-wired
            // to answer "already live" would pass every assertion above. An EvSpawn carrying a
            // typeIdx no descriptor claims is the registry/protocol mismatch dupBad exists for,
            // so it must move dupBad and leave dupLive alone. 254 is unregistered by
            // construction -- the table is dense from 0 and nowhere near that long -- and the
            // check below asserts that rather than trusting it.
            Check("typeIdx " + UnknownTypeIdx + " really is unregistered (the control's premise)",
                NetTypeRegistry.Get(UnknownTypeIdx) == null);
            long dupLiveBeforeBad = m.DupLive;
            long dupBadBeforeBad = m.DupBad;
            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, 9500, UnknownTypeIdx,
                state, noExtras, 0));
            wire.Pump();
            NetSession.Update();
            Check("an unknown typeIdx counts as dupBad (+" + (m.DupBad - dupBadBeforeBad) + ")",
                m.DupBad == dupBadBeforeBad + 1);
            Check("... and NOT as dupLive (unchanged at " + m.DupLive + ")",
                m.DupLive == dupLiveBeforeBad);
            Check("... and built no puppet (live=" + NetPuppets.LiveCount + ")",
                NetPuppets.LiveCount == liveBefore + Churn);
            Check("no death arrived for an id that was never spawned (ordViol unchanged)",
                m.OrderViolations == ordViolBefore);
            Check("NO PUPPETS LEAKED -- generation 1 is gone and generation 2 is live (live="
                + NetPuppets.LiveCount + ", expected " + (liveBefore + Churn) + ")",
                NetPuppets.LiveCount == liveBefore + Churn);

            // THE ITEM-1 RESIDUAL PROBE. The first-wipe pupPops burst had a time-scaling half
            // (fixed: the driver dead-reckons on real time) and a reset/id-churn half, which is
            // this transition. The card's scope is PROPOSE, not fix -- so this REPORTS the count
            // and asserts only the bound the design claims: a pop is a snapshot correction past
            // the threshold, and an id that was rebuilt FROM a snapshot starts on it, so churn
            // alone must not produce one per churned id.
            //
            // THE COUNTER IS ONLY REACHABLE FOR AN ID THE CLIENT ALREADY KNOWS, and that is what
            // makes the naive form of this assertion vacuous: PuppetPops is incremented inside
            // OnSnapshotEntry's APPLIED branch, so every entry above -- all of which were for
            // never-seen ids the self-heal rebuilt -- returned false and could not have popped
            // whatever the layer did. Reading zero there would say nothing at all. So the probe
            // is two more rounds of snapshots for the NOW-KNOWN generation-2 ids: one where the
            // host agrees with where the client rebuilt them (must not pop) and one displaced far
            // past the threshold (must pop). The second is the control: without it the first is
            // "a counter that cannot move did not move".
            long popsAfterChurn = m.PuppetPops - popsBefore;
            sb.Append("  info  pupPops across the churn itself: +").Append(popsAfterChurn)
                .Append(" over ").Append(Churn).Append(" ids (snapTurn ")
                .Append(NetSession.SnapshotTurnMs(NetPuppets.LiveCount)).Append("ms)\n");

            long popsBeforeAgree = m.PuppetPops;
            for (int i = 0; i < Churn; i++)
            {
                peer.SendStream(SnapshotFor((ushort)(9100 + i), state));
            }
            wire.Pump();
            NetSession.Update();
            Check("a correction AGREEING with where the churn rebuilt each puppet pops none (+"
                + (m.PuppetPops - popsBeforeAgree) + " over " + Churn + " known ids)",
                m.PuppetPops == popsBeforeAgree);

            long popsBeforeJump = m.PuppetPops;
            NetBaseState far = state;
            far.Pos = state.Pos + new Vector2(1000f, 0f); // far past PuppetPopPx
            peer.SendStream(SnapshotFor(9100, far));
            wire.Pump();
            NetSession.Update();
            Check("... while a correction 1000px away DOES pop -- the control that makes the line"
                + " above mean something (+" + (m.PuppetPops - popsBeforeJump) + ")",
                m.PuppetPops == popsBeforeJump + 1);

            // Tear the generation-2 puppets back out. Disable() clears the id maps but leaves
            // live puppets to a scene's Terminate purge -- and this suite has no scene.
            TrackPuppets(game, planted);
            foreach (GameComponent comp in CollectPuppets(game))
            {
                bin.Remove(comp);
                planted.Remove(comp);
            }
            bin.TopOfTickFlush();

            TeardownSession(sb, Check);
            NetScene.Current = null;
            Check("the churn scenario left no puppets behind (live=" + NetPuppets.LiveCount + ")",
                NetPuppets.LiveCount == 0);
        }

        // ---- rig helpers -------------------------------------------------------------------

        // A real replicable enemy, built by its own factory the way the sprite harness does, and
        // parked far off-screen so it is never drawn and never collides with anything. Added to
        // the LIVE bin on purpose: that is what makes NetIdRegistry allocate it a real id through
        // the real ComponentAdded seam, which is the whole point of driving the real thing.
        private static UFO Plant(ComponentBin bin, Game game, List<GameComponent> planted)
        {
            UFO ufo = UFO.NewUFO(bin, game);
            ufo.Setup(Nowhere, isBig: false, EnemyBehaviour.normal);
            bin.Add((GameComponent)(object)ufo);
            planted.Add((GameComponent)(object)ufo);
            return ufo;
        }

        // As above, forced to OneUp. Setup rolls a random type, so MakeType re-picks -- the SAME
        // call the real bonus-drop site (UFO.KilledBy) makes, rather than a bare `type` write,
        // which would leave the sprite and colour of the roll on a powerup claiming to be a OneUp.
        // AFTER Setup and BEFORE Add: the configure-then-Add contract tools/audit_add_order.py
        // lints, and the only reason the roll cannot make this flaky.
        private static Powerup PlantOneUp(ComponentBin bin, Game game, List<GameComponent> planted)
        {
            Powerup powerup = Powerup.NewPowerup(bin, game);
            powerup.Setup(Nowhere);
            powerup.MakeType(Powerup.PowerupType.OneUp);
            bin.Add((GameComponent)(object)powerup);
            planted.Add((GameComponent)(object)powerup);
            return powerup;
        }

        // One snapshot packet carrying one entry, built through the real encoder. Hand-rolling
        // the frame is exactly what the design doc forbids: a scripted peer that drifts from the
        // encoder it stands in for tests the script, not the game.
        private static byte[] SnapshotFor(ushort id, in NetBaseState state)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, ChurnTypeIdx, state, new byte[1], 0);
            scratch[0] = NetProtocol.MsgWorldSnapshot;
            scratch[1] = 1;
            byte[] packet = new byte[off];
            Array.Copy(scratch, packet, off);
            return packet;
        }

        private static bool InWorld(Game game, GameComponent comp)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (ReferenceEquals(item, comp))
                {
                    return true;
                }
            }
            return false;
        }

        // Add any churn puppet not already tracked, so Run's teardown can sweep them on a path
        // that never reaches this scenario's own cleanup.
        private static void TrackPuppets(Game game, List<GameComponent> planted)
        {
            foreach (GameComponent comp in CollectPuppets(game))
            {
                if (!planted.Contains(comp))
                {
                    planted.Add(comp);
                }
            }
        }

        private static List<GameComponent> CollectPuppets(Game game)
        {
            List<GameComponent> list = new List<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is EvilBullet)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        private static void TeardownSession(StringBuilder sb, Action<string, bool> Check)
        {
            NetSession.Stop("scenario harness finished");
            Check("the session is stopped", !NetSession.Active);
        }

        // Hand the menu back what it lent us. Lives and score are RESTORED, not merely reported:
        // this suite pays real claims into the live panels, and a run that left them raised would
        // silently inflate the next play's HUD -- and make a second back-to-back run read
        // different numbers from the first, which is the property that makes a self-test worth
        // anything.
        private static void Teardown(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, ScoreVisualiser score, List<GameComponent> planted,
            float[] scoreBefore, int livesBefore, int playersBefore, bool[] powerupBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("scenario harness teardown");
                }
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                bin.TopOfTickFlush();
                planted.Clear();

                // The pairing seats the peer's primary somewhere above slot 0 -- WHERE is the
                // allocator's choice, so this sweeps rather than naming a slot. (The sibling
                // suite's `RemovePlayerAt(HostPrimarySlot, Remote)` line is correct THERE and
                // would be a no-op here: this suite is the host, so slot 0 is its own seat.)
                for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
                {
                    if (oracle.IsSeated(slot)
                        && (oracle.Controller(slot) == ControlDevice.Remote
                            || oracle.Controller(slot) == ControlDevice.RemoteFriend))
                    {
                        oracle.RemovePlayerAt(slot, oracle.Controller(slot));
                    }
                }
                Check("no Remote seat is left squatting the roster (players " + playersBefore
                    + " -> " + oracle.Players + ")",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote) && oracle.Players == playersBefore);

                for (int slot = 0; slot < ScoreVisualiser.SlotCount; slot++)
                {
                    score.NetSetScore(slot, scoreBefore[slot], 0f);
                }
                score.Lives = livesBefore;
                bool restored = true;
                for (int slot = 0; slot < ScoreVisualiser.SlotCount; slot++)
                {
                    restored &= Math.Abs(score.PointScore(slot) - scoreBefore[slot]) < 0.01f;
                }
                bool puRestored = true;
                for (int slot = 0; slot < ScoreVisualiser.SlotCount; slot++)
                {
                    if (score.NetPowerupActive(slot) && !powerupBefore[slot])
                    {
                        score.RemovePowerup(slot);
                    }
                    puRestored &= score.NetPowerupActive(slot) == powerupBefore[slot];
                }
                Check("the score panels, lives and powerup indicators are back where the suite"
                    + " found them", restored && score.Lives == livesBefore && puRestored);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + Describe(ex) + ")", false);
            }
        }

        private static string Round(float v)
        {
            return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
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

        private static string Frames(Exception ex)
        {
            string trace = ex.StackTrace;
            if (string.IsNullOrEmpty(trace))
            {
                return "  (no stack trace)\n";
            }
            const int MaxFrames = 8;
            string[] lines = trace.Split('\n');
            StringBuilder frames = new StringBuilder();
            for (int i = 0; i < lines.Length && i < MaxFrames; i++)
            {
                frames.Append("  ").Append(lines[i].Trim()).Append('\n');
            }
            if (lines.Length > MaxFrames)
            {
                frames.Append("  (trace truncated after ").Append(MaxFrames).Append(" frames)\n");
            }
            return frames.ToString();
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netscen] {0} passed, {1} failed\n", pass, fail);
        }

        // The minimum INetScene a scenario needs: something non-null, so the client rx paths'
        // "is a scene up" gate opens. Every world call is COUNTED rather than performed --
        // there is no scene to perform it on, and scenario 5 asserts about puppets, not about
        // the world. Scenario 6's ordering assertions need the counts to be an ORDER, which is
        // why NetSceneOrderTest carries its own recorder over a real GameScene instead.
        private sealed class ScenarioScene : INetScene
        {
            public Levels Level => Levels.Level1;

            public bool NetEndingNormally => false;

            public bool JoinWouldSpawnNow => false;

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
