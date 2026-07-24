using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

	private sealed class ShipRec
	{
		public int Slot;
		public int Contacts;
		public int Deaths;
		public int Shots;
		public long SteerTicks;
		public long CoastTicks;
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
	}

	private static readonly Dictionary<int, ShipRec> ships = new Dictionary<int, ShipRec>();

	private static double runMs;
	private static double nextReportMs;
	private static string verdict;
	private static double verdictMs;
	private static int peakEventPos;

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
		headlessTotal = TimeSpan.Zero;
		lastScene = null;
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

	internal static void NoteDeath(PlayerShip ship)
	{
		if (!Enabled)
		{
			return;
		}
		Rec(ship).Deaths++;
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
			// Where the ship is and what it last asked for. A jitter number cannot distinguish a
			// smooth flier from a bot wedged in a corner pushing into the wall; these two can.
			sb.Append(" ticks=").Append(r.SteerTicks);
			sb.Append(" pos=").Append(Fmt(r.LastPos.X, 0)).Append(',').Append(Fmt(r.LastPos.Y, 0));
			sb.Append(" steer=").Append(Fmt(r.LastSteer.X, 1)).Append(',').Append(Fmt(r.LastSteer.Y, 1));
			if (r.TicksWithTarget > 0)
			{
				sb.Append(" idle=").Append(Fmt(100.0 * (double)r.IdleWithTargetTicks / (double)r.TicksWithTarget, 0)).Append('%');
			}
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
		sb.Append(" idle=").Append(Fmt((r.TicksWithTarget > 0L)
			? (100.0 * (double)r.IdleWithTargetTicks / (double)r.TicksWithTarget)
			: 0.0, 0));
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
