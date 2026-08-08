using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat;

// Telemetry for the ControlDevice.AI ships (card f4d1721f "Improve AI"). Booting the game and
// WATCHING the bot is the wrong rig for "is the AI better" — everything moves, a screenshot is
// meaningless, and the interesting moments (a wall clip, a stalled boss) can't be timed. So the
// AI is judged as DATA: run it behind ?aibench and read the counters.
//
// The three numbers that answer the card:
//   contacts  wall touches. Counted in PlayerShip.CollidesWith BEFORE the invulnerability gate,
//             so a ?invuln run still scores every clip AND survives to measure all six wall
//             sections. Rising-edge only (a collision fires every tick while overlapping).
//   revs/s    heading REVERSALS per second — the jitter metric. A smooth turn keeps the sign of
//             its per-tick heading delta; oscillation alternates it every tick or two. Mean turn
//             rate alone can't tell "flying a curve" from "vibrating", the sign flip can.
//   prog      how far the level's event list got, and the run verdict (VICTORY / GAME OVER /
//             the event index it stalled on) — the card's "gives up somewhere in level 3".
//
// Console: eaAiBench() dumps the table, eaAiBench.reset() rearms. A summary line also prints
// every AiBench.ReportIntervalMs while a run is live, so an unattended soak leaves a trace.
// Nothing here is built unless ?aibench is on.
internal static class AiBench
{
	private const double ReportIntervalMs = 10000.0;

	// A wall collision re-fires every tick the boxes overlap; only a fresh touch is a mistake.
	// Long enough to cover one clip through a tower, short enough that two separate clips on the
	// same section still score twice.
	private const double ContactDebounceMs = 400.0;

	// Below this per-tick heading change the ship is flying straight and the sign of the delta is
	// noise, not a reversal (~0.6 degrees).
	private const float ReversalDeadbandRad = 0.01f;

	// Enough for any soak anyone runs here (the worst matrix row is ~90 deaths); the cap exists so
	// an unattended run cannot grow the list without bound.
	private const int MaxDeathPositions = 4000;

	private sealed class ShipRec
	{
		public int Slot;
		public int Contacts;
		public int Deaths;
		public int Shots;
		public long SteerTicks;
		public long CoastTicks;
		// Ticks on which the repulsion resultant fell at or below RepulseCancelDelta and every
		// repellent was therefore dropped (card ada9e839). Counted only when something was
		// actually pushing, so a calm screen does not read as constant cancellation -- the
		// question the metric answers is "how often do the threat fields argue themselves to a
		// standstill", and an empty field arguing with nothing is not that.
		public long RepelTicks;
		public long RepelZeroedTicks;
		public readonly Dictionary<string, ThreatTermRec> ThreatTerms = new Dictionary<string, ThreatTermRec>();
		public Vector2 LastPos;
		public Vector2 LastSteer;
		public double SteerMs;
		public float TurnRadTotal;
		public int Reversals;
		public float LastHeading;
		public int LastDeltaSign;
		public bool HasHeading;
		public double LastContactMs;
		// Ticks with a shootable alien on screen and no shot fired — the "stands there doing
		// nothing while the boss it can't see gates the level" signature.
		public long IdleWithTargetTicks;
		public long TicksWithTarget;
		// Powerups this ship actually collected (card ada9e839). Read against the run-wide
		// PowerupsSpawned below: a pickup COUNT alone cannot tell "the bot ignores powerups"
		// from "this run dropped two".
		public int Pickups;

		// Ticks the top-edge push stood down for a live powerup dash into the band (card
		// 13960838). The yield's ONLY observable: suppressing a push changes no pixel, so
		// without this a yield that silently stopped (or never) firing is invisible until a
		// full sweep notices the pickup rate moved.
		public int TopEdgeYieldTicks;
		// The boss-approach term (card 31ceb6ff), measured where it acts. `idle%` and `prog` are
		// both too far downstream to see it: a boss fight has a dozen other things pushing, so
		// the outcome moves for reasons that have nothing to do with whether the ship CLOSED.
		// What the card is about is distance, so distance is what is counted.
		public long BossTicks;
		public double BossDistTotal;
		public long BossOutOfRangeTicks;
		// The solved attractor weight itself (card b56633fb). It is recomputed every tick from the
		// live weapon range and the tier's own repellent, so it is the one number that says whether
		// the term is CALIBRATED -- a distance alone cannot tell "pulling hard and being out-voted"
		// from "not pulling at all", which is exactly how the pre-card 1.1 hid for two cards.
		public double BossWeightTotal;
		// What killed it, by type name (card b56633fb). `Deaths` alone cannot answer "does the
		// bot still fly into the grounded spider boss" — a death to a stray bullet and a death
		// to the boss are the same number, which is why that report was unverifiable.
		public readonly Dictionary<string, int> Killers = new Dictionary<string, int>();
		// WHERE each death happened, in 800x600 design space (card ada9e839). `killers=` says what
		// killed the bot and `deaths=` how often; neither can tell edge-hugging from a mid-field
		// lane collision, and those want opposite fixes. Rendered by
		// tools/sim/ai_death_heatmap.py. Capped so a long soak cannot grow it without bound.
		public readonly List<Vector2> DeathPositions = new List<Vector2>();
	}

	private static readonly Dictionary<int, ShipRec> ships = new Dictionary<int, ShipRec>();

	private static double runMs;
	private static double nextReportMs;
	private static string verdict;
	private static double verdictMs;
	private static int peakEventPos;
	// Powerups that ENTERED the world this run, whoever (if anyone) took them. The denominator
	// for the per-ship Pickups counter — spawns are stochastic, so a raw pickup count between
	// two builds is not a comparison.
	private static int powerupsSpawned;

	private static GameScene lastScene;

	internal static bool Enabled => DebugFlags.AiBench;

	internal static void Reset()
	{
		ships.Clear();
		runMs = 0.0;
		nextReportMs = ReportIntervalMs;
		verdict = null;
		verdictMs = 0.0;
		peakEventPos = 0;
		powerupsSpawned = 0;
		headlessTotal = TimeSpan.Zero;
		lastScene = null;
	}

	// One repellent path for one threat TYPE: how often it fired and how hard. Mean strength
	// is the number to compare against the 0.8 seek; Max says whether it ever bites at all.
	private sealed class ThreatTermRec
	{
		public long Count;
		public double StrengthTotal;
		public float StrengthMax;
		// Field RANGE and the ship's EDGE distance, so the warning perimeter can be read off a
		// real run instead of derived from constants that may not be what the type actually gets.
		public double RangeTotal;
		public double EdgeDistTotal;
	}

	private static ShipRec Rec(PlayerShip ship)
	{
		int slot = ship.Owner;
		if (!ships.TryGetValue(slot, out ShipRec rec))
		{
			rec = new ShipRec { Slot = slot };
			ships[slot] = rec;
		}
		return rec;
	}

	// ---- hooks (all no-ops unless ?aibench) ------------------------------------------------

	// End of PlayerShip.DoAIMove: `direction` is the summed steering vector, and Move() consumes
	// only its ANGLE — so the angle IS the AI's decision and the thing to measure.
	internal static void NoteSteer(PlayerShip ship, Vector2 direction, GameTime gameTime)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		rec.SteerTicks++;
		rec.LastPos = ship.GetPosition();
		rec.LastSteer = direction;
		rec.SteerMs += gameTime.ElapsedGameTime.TotalMilliseconds;
		if (direction == Vector2.Zero)
		{
			rec.CoastTicks++;
			// Coasting is not a heading; break the chain so the next real steer can't be scored
			// as a reversal of a stale one.
			rec.HasHeading = false;
			return;
		}
		float heading = MyMath.VectorToAngle(direction);
		if (rec.HasHeading)
		{
			float delta = MathHelper.WrapAngle(heading - rec.LastHeading);
			rec.TurnRadTotal += Math.Abs(delta);
			int sign = (delta > ReversalDeadbandRad) ? 1 : ((delta < 0f - ReversalDeadbandRad) ? -1 : 0);
			if (sign != 0)
			{
				if (rec.LastDeltaSign != 0 && sign != rec.LastDeltaSign)
				{
					rec.Reversals++;
				}
				rec.LastDeltaSign = sign;
			}
		}
		rec.LastHeading = heading;
		rec.HasHeading = true;
	}

	// DoAIMove, at the repulsion cancellation floor (card ada9e839). `preFloor` is the repulsion
	// resultant BEFORE the floor is applied, which is the only form that can answer the question:
	// a tick with nothing repelling and a tick where two threats cancelled each other out are
	// both Vector2.Zero AFTERWARDS, and only the second is this mechanism doing its job. Ticks
	// where nothing pushed at all are counted in NEITHER total -- folding them in would turn the
	// rate into a measure of how empty the screen was.
	internal static void NoteRepel(PlayerShip ship, Vector2 preFloor, bool zeroed)
	{
		if (!Enabled || preFloor == Vector2.Zero)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		rec.RepelTicks++;
		if (zeroed)
		{
			rec.RepelZeroedTicks++;
		}
	}

	// DoAIMove's baddy loop, once per THREAT that actually contributed a repellent (card
	// ada9e839). Answers the question a total-magnitude counter cannot: a threat type is handled
	// either by EvadeMovingThreat (steer off its projected PATH) or by the radial distance field,
	// never both -- the evade returns true and the caller skips the field. So "the bot ignores
	// asteroids" has two completely different causes with the same symptom, and this says which:
	// a type that never appears under `field` is being handled as a MOVER, and its radial
	// magnitude is irrelevant no matter how it is tuned.
	// `strength` is the term's magnitude before it joins the repulsion sum, so the mean is
	// directly comparable to the 0.8 seek it has to out-vote.
	// Card e425781b added the two DIRECTIONAL paths, and they are not exclusive with `Field` the
	// way `Evade` is: a mover contributes its radial term AND its cone (and its wedge, in a lane),
	// which is the point -- the shape is a circle with a hat on it, not a replacement circle. So
	// reading this breakdown means asking which path carries the WEIGHT, not which one appears.
	internal enum ThreatPath
	{
		Field,
		Evade,
		Cone,
		Wedge
	}

	private static string PathLabel(ThreatPath path)
	{
		switch (path)
		{
		case ThreatPath.Evade:
			return "(evade)";
		case ThreatPath.Cone:
			return "(cone)";
		case ThreatPath.Wedge:
			return "(wedge)";
		default:
			return "(field)";
		}
	}

	internal static void NoteThreatTerm(PlayerShip ship, AlienDrawableGameComponent baddy, ThreatPath path, float strength, float range = 0f, float edgeDist = 0f)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		string key = baddy.GetType().Name + PathLabel(path);
		if (!rec.ThreatTerms.TryGetValue(key, out ThreatTermRec t))
		{
			t = new ThreatTermRec();
			rec.ThreatTerms[key] = t;
		}
		t.Count++;
		t.StrengthTotal += strength;
		t.RangeTotal += range;
		t.EdgeDistTotal += edgeDist;
		if (strength > t.StrengthMax)
		{
			t.StrengthMax = strength;
		}
	}

	// PlayerShip.CollidesWith, `other is Wall`, BEFORE the invulnerability gate.
	internal static void NoteWallContact(PlayerShip ship)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		// Refresh the stamp on EVERY call, not only when one is counted: a collision re-fires
		// every tick the boxes overlap, so stamping only on a count turns one sustained scrape
		// into a fresh "contact" every ContactDebounceMs. The debounce has to mean "no new
		// contact until 400ms after the overlap ENDS", or the headline metric of this whole card
		// counts how LONG the ship leaned on a wall rather than how OFTEN it hit one.
		bool fresh = runMs - rec.LastContactMs >= ContactDebounceMs || rec.Contacts == 0;
		rec.LastContactMs = runMs;
		if (fresh)
		{
			rec.Contacts++;
		}
	}

	// `killer` is whatever the ship recorded as the cause (PlayerShip.asplosionCauser for a
	// queued asplosion, null for a scripted/forced kill such as eaKillShips). Typed as object so
	// this stays a diagnostic and not another type list to keep in sync with IsAiThreat -- and a
	// plain STRING is taken as the name verbatim, for the one path (AsplodeWall) that knows what
	// killed the ship without holding a reference to it.
	internal static void NoteDeath(PlayerShip ship, object killer)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		rec.Deaths++;
		// The ONE type split out by state, because card b56633fb's whole claim is about which
		// state: flying into a screen-wide sweep is a dodge that lost, while walking into a
		// PARKED boss is the bot not seeing something that has not moved in seconds.
		string name = (killer is SpiderBoss boss)
			? (boss.AiStanding ? "SpiderBoss(standing)" : "SpiderBoss")
			: ((killer as string) ?? ((killer != null) ? killer.GetType().Name : "unknown"));
		rec.Killers[name] = (rec.Killers.TryGetValue(name, out int n) ? n : 0) + 1;
		if (rec.DeathPositions.Count < MaxDeathPositions)
		{
			rec.DeathPositions.Add(ship.GetPosition());
		}
	}

	// DoAIMove, once per tick per AI ship while a level-HALTING boss is on screen. Since card
	// b56633fb both distances are EDGE distances and `anchor` is r*, firing range -- so
	// `dist > anchor` is precisely "the ship cannot shoot the boss from here", and `weight` is
	// the solved attractor the term is pulling with at that distance.
	// PROXIMITY IS DESCRIPTIVE, NEVER A GATE (user ruling on card b56633fb): the bot moving closer
	// to a boss to dodge, collect or line up a shot is the field composition working. Gate boss
	// work on OUTCOMES.
	internal static void NoteBossApproach(PlayerShip ship, float dist, float anchor, float weight)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		rec.BossTicks++;
		rec.BossDistTotal += dist;
		rec.BossWeightTotal += weight;
		if (dist > anchor)
		{
			rec.BossOutOfRangeTicks++;
		}
	}

	// PlayerShip.CollidesWith, the `other is Powerup` branch — the ship actually took one.
	internal static void NotePickup(PlayerShip ship)
	{
		if (!Enabled)
		{
			return;
		}
		Rec(ship).Pickups++;
	}

	// PlayerShip.DoAIMove, the top-edge band — the yield (card 13960838) suppressed the push
	// this tick because the ship's live steer target is a powerup inside the band.
	internal static void NoteTopEdgeYield(PlayerShip ship)
	{
		if (!Enabled)
		{
			return;
		}
		Rec(ship).TopEdgeYieldTicks++;
	}

	// Powerup.Initialize — a pickup entered the world. Run-wide rather than per ship: nothing
	// about a spawn belongs to a slot, and both ships compete for the same drop.
	internal static void NotePowerupSpawned()
	{
		if (!Enabled)
		{
			return;
		}
		powerupsSpawned++;
	}

	// DoAIFire, once per tick per AI ship: did it have something worth shooting, and did it shoot?
	internal static void NoteFireDecision(PlayerShip ship, bool hadTarget, bool fired)
	{
		if (!Enabled)
		{
			return;
		}
		ShipRec rec = Rec(ship);
		if (fired)
		{
			rec.Shots++;
		}
		if (hadTarget)
		{
			rec.TicksWithTarget++;
			if (!fired)
			{
				rec.IdleWithTargetTicks++;
			}
		}
	}

	// Game1.UpdateInner, once per tick. Advances the run clock, latches level progress, and
	// prints the periodic summary.
	internal static void Update(GameTime gameTime, Game game)
	{
		if (!Enabled)
		{
			return;
		}
		runMs += gameTime.ElapsedGameTime.TotalMilliseconds;
		GameScene scene = CurrentScene();
		if (scene != null)
		{
			// A soak that runs into a second level must not report the previous level's index
			// against the new level's total.
			if (!ReferenceEquals(scene, lastScene))
			{
				lastScene = scene;
				peakEventPos = 0;
			}
			int pos = scene.BenchEventPos;
			if (pos > peakEventPos)
			{
				peakEventPos = pos;
			}
			if (verdict == null)
			{
				string v = scene.BenchVerdict;
				if (v != null)
				{
					verdict = v;
					verdictMs = runMs;
					Log("[aibench] " + v + " — " + Line(scene));
				}
			}
		}
		if (runMs >= nextReportMs)
		{
			nextReportMs += ReportIntervalMs;
			Log("[aibench] " + Line(scene));
		}
	}

	// The live GameScene, or null in a menu. Uses the static GameScene already maintains for
	// the net layer rather than scanning Game.Components: this runs EVERY tick, and a boss fight
	// can hold hundreds of components (blood explosions alone), so a per-tick O(n) scan here
	// would make the bench itself a measurable part of what it is measuring.
	// NetActiveScene is misleadingly named: GameScene.Initialize sets it unconditionally, not
	// only in a net session, so it is simply "the live scene".
	private static GameScene CurrentScene()
	{
		return GameScene.NetActiveScene;
	}

	// ---- reporting -------------------------------------------------------------------------

	private static string Line(GameScene scene)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("t=").Append(Fmt(runMs / 1000.0, 1)).Append("s ");
		if (scene != null)
		{
			sb.Append(scene.Level).Append(' ');
			sb.Append("prog=").Append(peakEventPos).Append('/').Append(scene.BenchEventCount).Append(' ');
		}
		sb.Append(verdict ?? "running");
		foreach (KeyValuePair<int, ShipRec> kv in ships)
		{
			ShipRec r = kv.Value;
			double sec = r.SteerMs / 1000.0;
			sb.Append(" | p").Append(r.Slot);
			sb.Append(" contacts=").Append(r.Contacts);
			sb.Append(" deaths=").Append(r.Deaths);
			sb.Append(" revs/s=").Append(Fmt((sec > 0.0) ? ((double)r.Reversals / sec) : 0.0, 1));
			sb.Append(" turn=").Append(Fmt((sec > 0.0) ? (MathHelper.ToDegrees(r.TurnRadTotal) / sec) : 0.0, 0)).Append("deg/s");
			// Coast share: ticks the AI issued NO steer at all. A jitter rate near zero only means
			// "smooth" if the ship was actually steering -- a bot standing still also scores zero,
			// and that failure mode has already been mistaken for a fix once on this card.
			sb.Append(" coast=").Append(Fmt((r.SteerTicks > 0L) ? (100.0 * (double)r.CoastTicks / (double)r.SteerTicks) : 0.0, 0)).Append('%');
			// Share of PUSHED ticks on which the repulsion floor fired, i.e. the repellents
			// cancelled each other out and none was applied (card ada9e839). The mechanism that
			// replaced the 0.95 park is otherwise unobservable: it changes no pixel and moves no
			// other counter, so a build where it never fires and one where it fires constantly
			// look identical without this.
			sb.Append(" repelzero=").Append(Fmt((r.RepelTicks > 0L) ? (100.0 * (double)r.RepelZeroedTicks / (double)r.RepelTicks) : 0.0, 0)).Append('%');
			// Death POSITIONS, design space, semicolon-separated. Space-free and bracket-free so
			// eaAiBench.matrix's `split(' ')` parser is unaffected. Read by
			// tools/sim/ai_death_heatmap.py straight off the eahl transcript.
			if (r.DeathPositions.Count > 0)
			{
				sb.Append(" deathpos=");
				for (int d = 0; d < r.DeathPositions.Count; d++)
				{
					if (d > 0)
					{
						sb.Append(';');
					}
					sb.Append(Fmt(r.DeathPositions[d].X, 0)).Append(',').Append(Fmt(r.DeathPositions[d].Y, 0));
				}
			}
			// Per-type repellent breakdown: `<Type>(<path>)=<n>@<mean>/<max>`. The PATH is the point --
			// a type appearing only as (evade) is never touched by the radial field's tuning.
			if (r.ThreatTerms.Count > 0)
			{
				sb.Append(" threats=");
				bool first = true;
				foreach (KeyValuePair<string, ThreatTermRec> tt in r.ThreatTerms.OrderByDescending(k => k.Value.Count))
				{
					if (!first)
					{
						sb.Append(',');
					}
					first = false;
					sb.Append(tt.Key).Append('=').Append(tt.Value.Count).Append('@')
						.Append(Fmt(tt.Value.StrengthTotal / (double)tt.Value.Count, 2)).Append('/')
						.Append(Fmt(tt.Value.StrengthMax, 2))
						.Append("r").Append(Fmt(tt.Value.RangeTotal / (double)tt.Value.Count, 0))
						.Append("d").Append(Fmt(tt.Value.EdgeDistTotal / (double)tt.Value.Count, 0));
				}
			}
			// Where the ship is and what it last asked for. A jitter number cannot distinguish a
			// smooth flier from a bot wedged in a corner pushing into the wall; these two can.
			sb.Append(" ticks=").Append(r.SteerTicks);
			sb.Append(" pos=").Append(Fmt(r.LastPos.X, 0)).Append(',').Append(Fmt(r.LastPos.Y, 0));
			sb.Append(" steer=").Append(Fmt(r.LastSteer.X, 1)).Append(',').Append(Fmt(r.LastSteer.Y, 1));
			if (r.TicksWithTarget > 0)
			{
				sb.Append(" idle=").Append(Fmt(100.0 * (double)r.IdleWithTargetTicks / (double)r.TicksWithTarget, 0)).Append('%');
			}
			// Always printed, both halves: a bare "pickups=0" reads as a bug, and "0 of 0" reads
			// as the run the level never dropped one — which is the difference the card is about.
			// The PERCENTAGE is what a probe can assert -- `expect` matches a regex per line and
			// cannot divide two capture groups, so a ratio printed only as `n/m` is unassertable.
			// Both halves stay, because the rate alone hides "0 of 0".
			sb.Append(" pickups=").Append(r.Pickups).Append('/').Append(powerupsSpawned)
				.Append('(').Append(Fmt((powerupsSpawned > 0) ? (100.0 * r.Pickups / powerupsSpawned) : 0.0, 0)).Append("%)");
			// Always printed too, and for the same reason as pickups: `topyield=0` under
			// `?aitopedgeyield=0` is the negative control's whole assertion (card 13960838).
			sb.Append(" topyield=").Append(r.TopEdgeYieldTicks);
			if (r.BossTicks > 0)
			{
				sb.Append(" boss=").Append(Fmt(r.BossDistTotal / r.BossTicks, 0)).Append("px");
				sb.Append(" bossfar=").Append(Fmt(100.0 * r.BossOutOfRangeTicks / r.BossTicks, 0)).Append('%');
				// Line() only, like the pickup percentage above -- Row()'s parser is `split(. .)`
				// then the first `=`, and one more field there is one more thing to keep in step
				// with `eaAiBench.matrix`'s contract for no gain (card b56633fb).
				sb.Append(" bossw=").Append(Fmt(r.BossWeightTotal / r.BossTicks, 2));
			}
			if (r.Killers.Count > 0)
			{
				sb.Append(" killers=").Append(KillerHistogram(r));
			}
		}
		return sb.ToString();
	}

	// `Type:count` pairs, commonest killer first, comma-separated and SPACE-FREE — the Row()
	// contract (see there: a value with a space truncated the verdict column once already).
	// A CLR type name cannot contain a space, so the shape is safe by construction; the sort is
	// so the answer to "what is killing this bot" is the first token rather than a hunt.
	private static string KillerHistogram(ShipRec rec)
	{
		List<KeyValuePair<string, int>> pairs = new List<KeyValuePair<string, int>>(rec.Killers);
		pairs.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
		{
			int byCount = b.Value.CompareTo(a.Value);
			return (byCount != 0) ? byCount : string.CompareOrdinal(a.Key, b.Key);
		});
		StringBuilder sb = new StringBuilder();
		foreach (KeyValuePair<string, int> kv in pairs)
		{
			if (sb.Length > 0)
			{
				sb.Append(',');
			}
			sb.Append(kv.Key).Append(':').Append(kv.Value);
		}
		return sb.ToString();
	}

	// eaAiBench.world() -- census of what is alive right now, and what the AI makes of it: is
	// each type in the bot's world model at all (Oracle.GetBaddies), and does it read as
	// shootable / as a threat. "The level is stalled and the bot is shooting SOMETHING" is
	// otherwise unanswerable without a debugger; this turns it into one line.
	internal static string World()
	{
		Game game = ServiceHelper.Get<IComponentBinService>().ComponentBin.Game;
		Dictionary<string, int> all = new Dictionary<string, int>();
		foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
		{
			string name = item.GetType().Name;
			all[name] = (all.TryGetValue(name, out int n) ? n : 0) + 1;
		}
		Dictionary<string, int> modelled = new Dictionary<string, int>();
		Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
		foreach (AlienDrawableGameComponent baddy in oracle.GetBaddies())
		{
			string name = baddy.GetType().Name;
			modelled[name] = (modelled.TryGetValue(name, out int n) ? n : 0) + 1;
		}
		StringBuilder sb = new StringBuilder();
		sb.Append("[aibench] world — components the AI can see (Oracle.GetBaddies) vs all:\n");
		foreach (KeyValuePair<string, int> kv in all)
		{
			bool seen = modelled.ContainsKey(kv.Key);
			// Only the interesting rows: anything the AI models, plus anything that looks like an
			// enemy but is NOT modelled (the blind spot that stalls a level).
			if (!seen && !LooksLikeEnemy(kv.Key))
			{
				continue;
			}
			sb.Append("  ").Append(kv.Key).Append(" x").Append(kv.Value)
				.Append(seen ? "  [in model]" : "  [NOT IN MODEL — add to Oracle.GetBaddies]")
				.Append('\n');
		}
		return sb.ToString();
	}

	// Name-shaped heuristic, used only to decide whether an UNMODELLED component is worth
	// printing. Deliberately not a type list: the whole point is to surface types nobody
	// remembered to register anywhere.
	private static bool LooksLikeEnemy(string name)
	{
		return name.Contains("Boss") || name.Contains("Alien") || name.Contains("UFO")
			|| name.Contains("Skull") || name.Contains("Spider") || name.Contains("Brain")
			|| name.Contains("Mine") || name.Contains("Ball") || name.Contains("Asteroid")
			|| name.Contains("Bullet") || name.Contains("Lazer") || name.Contains("Wall")
			|| name.Contains("Parachute") || name.Contains("Paratrooper") || name.Contains("Star");
	}

	// ---- headless soak ---------------------------------------------------------------------

	// eaAiBench.soak(totalSeconds) drives this in chunks. Each call ticks the REAL game loop
	// (Game1.BenchTick -> UpdateScaled -> UpdateInner) at a fixed 60 Hz dt with no Draw, so a
	// whole level's worth of AI play costs a fraction of the wall clock and -- unlike anything
	// rAF-driven -- is unaffected by the tab being in the background. Chunked rather than one
	// long call so the page stays responsive and the JS side can stop on a verdict.
	// Returns "<simSecondsRun> <verdict-or-running>" so JS can decide whether to keep going.
	internal static string RunHeadless(double chunkSeconds)
	{
		if (!Enabled)
		{
			return "0 OFF";
		}
		if (verdict != null)
		{
			return "0 " + verdict;
		}
		Game game = ServiceHelper.Get<IComponentBinService>().ComponentBin.Game;
		if (!(game is Game1 g))
		{
			return "0 NOGAME";
		}
		// A chunk is bounded so one JS call can never wedge the page; the caller loops.
		int steps = (int)MathHelper.Clamp((float)(chunkSeconds * 60.0), 1f, 3600f);
		TimeSpan step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60L);
		int ran = 0;
		for (int i = 0; i < steps; i++)
		{
			headlessTotal += step;
			g.BenchTick(new GameTime(headlessTotal, step));
			ran++;
			if (verdict != null)
			{
				break;
			}
		}
		return Fmt((double)ran / 60.0, 2) + " " + (verdict ?? "running");
	}

	private static TimeSpan headlessTotal = TimeSpan.Zero;

	// eaAiBench.row() — ONE run's counters as machine-readable `key=value` pairs, for the
	// sweep runner (card 9391f95a) to append to its matrix. Report() is written for a human and
	// its shape is free to change; regex-scraping it from JS would make the matrix hostage to
	// that formatting. Slot 0 only: every level in the sweep is single-ship except
	// TeamChallenge, whose second ship is the same bot flying the same tether.
	//
	// `verdict` is deliberately NOT resolved here. AiBench cannot tell "the cap was reached"
	// from "still going" — only the caller knows the budget it set — and on the eight challenge
	// levels that run with score.Lives = -1 (GameScene.Initialize; only InsaneBossI overrides it)
	// a GAME OVER can never arrive, so "no verdict" is the NORMAL way for those to fail. The
	// runner supplies TIMEOUT.
	internal static string Row()
	{
		if (!Enabled)
		{
			return "off=1";
		}
		GameScene scene = CurrentScene();
		ships.TryGetValue(0, out ShipRec r);
		StringBuilder sb = new StringBuilder();
		// Space-free: the row is parsed as space-separated key=value, and the verdict is the one
		// value with a space in it ("GAME OVER"), which silently truncated to "GAME" in the
		// sweep's table. The runner puts the space back for display.
		sb.Append("verdict=").Append((verdict ?? "running").Replace(' ', '_'));
		sb.Append(" sim=").Append(Fmt(runMs / 1000.0, 1));
		sb.Append(" verdictAt=").Append(Fmt(verdictMs / 1000.0, 1));
		sb.Append(" prog=").Append(peakEventPos);
		sb.Append(" progTotal=").Append((scene != null) ? scene.BenchEventCount : 0);
		sb.Append(" level=").Append((scene != null) ? scene.Level.ToString() : "none");
		sb.Append(" difficulty=").Append(Settings.GetInstance().CurrentDifficulty);
		if (r == null)
		{
			// No ship ever steered. On a level whose ships are all AI that is itself the
			// finding (the TeamChallenge force-pause), so it must be a reportable row and not
			// an absent one.
			return sb.Append(" ticks=0 noship=1").ToString();
		}
		double sec = r.SteerMs / 1000.0;
		sb.Append(" ticks=").Append(r.SteerTicks);
		sb.Append(" deaths=").Append(r.Deaths);
		sb.Append(" contacts=").Append(r.Contacts);
		sb.Append(" shots=").Append(r.Shots);
		sb.Append(" revs=").Append(Fmt((sec > 0.0) ? ((double)r.Reversals / sec) : 0.0, 2));
		sb.Append(" turn=").Append(Fmt((sec > 0.0) ? (MathHelper.ToDegrees(r.TurnRadTotal) / sec) : 0.0, 0));
		sb.Append(" coast=").Append(Fmt((r.SteerTicks > 0L) ? (100.0 * (double)r.CoastTicks / (double)r.SteerTicks) : 0.0, 0));
		sb.Append(" repelzero=").Append(Fmt((r.RepelTicks > 0L) ? (100.0 * (double)r.RepelZeroedTicks / (double)r.RepelTicks) : 0.0, 0));
		sb.Append(" idle=").Append(Fmt((r.TicksWithTarget > 0L)
			? (100.0 * (double)r.IdleWithTargetTicks / (double)r.TicksWithTarget)
			: 0.0, 0));
		// APPEND-ONLY: eaAiBench.matrix's parseRow is `split(' ')` then the first '=', so a new
		// key is free and a value containing a space is what breaks it. `none` rather than an
		// omitted key, so a consumer never has to tell "no deaths" from "old build".
		sb.Append(" pickups=").Append(r.Pickups);
		sb.Append(" poffered=").Append(powerupsSpawned);
		sb.Append(" topyield=").Append(r.TopEdgeYieldTicks);
		sb.Append(" boss=").Append(Fmt((r.BossTicks > 0L) ? (r.BossDistTotal / r.BossTicks) : 0.0, 0));
		sb.Append(" bossfar=").Append(Fmt((r.BossTicks > 0L) ? (100.0 * r.BossOutOfRangeTicks / r.BossTicks) : 0.0, 0));
		sb.Append(" killers=").Append((r.Killers.Count > 0) ? KillerHistogram(r) : "none");
		return sb.ToString();
	}

	// eaAiBench() — the full report, including the per-ship table and the run verdict.
	internal static string Report()
	{
		if (!Enabled)
		{
			return "[aibench] off — boot with ?aibench (pair with ?aiplayer, e.g. "
				+ "?level=Level3&wallsonly&aiplayer&invuln&aibench&aiff=8)";
		}
		GameScene scene = CurrentScene();
		StringBuilder sb = new StringBuilder();
		sb.Append("[aibench] ").Append(Line(scene)).Append('\n');
		if (verdict != null)
		{
			sb.Append("  verdict ").Append(verdict).Append(" at ").Append(Fmt(verdictMs / 1000.0, 1)).Append("s\n");
		}
		if (ships.Count == 0)
		{
			sb.Append("  no AI ship steered yet — is ?aiplayer (or the Mechanical Friends cheat) on?\n");
		}
		sb.Append("  difficulty=").Append(Settings.GetInstance().CurrentDifficulty);
		sb.Append(" fastforward=").Append(DebugFlags.AiFastForward).Append('\n');
		// The difficulty-scaled skill row actually in force (card c10e3e7f). `effective` is the
		// LOCK-aware tier and is the one that picks the row -- it differs from `difficulty` above
		// during an attract demo (locks Hard) and the tutorial (locks Very_Hard), which is exactly
		// the case that would otherwise be silently wrong. Values are post-override, so a ?ai*
		// flag shows up here too.
		// A trailing * marks a value that came from a ?ai* override rather than the tier row --
		// without it `?difficulty=Inzane&aifieldpx=150` prints exactly what the Easy row prints,
		// and the line stops answering the one question it exists to answer.
		PlayerShip.GetAiSkillReadout(out float fieldPx, out float aimRad);
		sb.Append("  skill effective=").Append(Settings.GetInstance().EffectiveDifficulty);
		sb.Append(" field=").Append(Fmt(fieldPx, 0)).Append("px");
		sb.Append((DebugFlags.AiThreatFieldPx.HasValue ? "*" : ""));
		sb.Append(" aim=").Append(Fmt(MathHelper.ToDegrees(aimRad), 1)).Append("deg");
		sb.Append((DebugFlags.AiAimSpreadRad.HasValue ? "*" : "")).Append('\n');
		return sb.ToString();
	}

	private static string Fmt(double v, int decimals)
	{
		return v.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}

	private static void Log(string s)
	{
		Console.WriteLine(s);
	}
}
