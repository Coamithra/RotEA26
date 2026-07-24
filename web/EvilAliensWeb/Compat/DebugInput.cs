using System;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
	// Reliable input injection for automated/headless testing, immune to the rAF
	// frame-timing miss that makes synthetic key events so painful here.
	//
	// The root cause of the churn: InputHandler polls Keyboard.GetState() ONCE per game
	// tick and edge-detects. A scripted keydown+keyup fired between two ticks is added
	// and removed before any poll observes it, so the press is silently dropped (the
	// classic "stuck on Press Start, tapping Enter does nothing" loop). Holding a real OS
	// key across a frame works but is fiddly to time and impossible to script reliably.
	//
	// Fix: don't race the poll. JS pushes a key here as a per-key tick COUNTER, and
	// InputHandler drains it from INSIDE the tick (Consume). Because the forced-down
	// state lives in C# and is only ever read/decremented by the game loop, it cannot
	// fall between polls — the next tick is guaranteed to see it.
	//
	// Drive it from the browser console or automation (eaPress is the JS wrapper in
	// wwwroot/index.html):
	//   eaPress('Enter')        // single-frame tap  (menu select / Press Start)
	//   eaPress('Up')           // navigate up one entry
	//   eaPress('Esc')          // back / cancel
	//   eaPress('Left', 30)     // HOLD Left ~30 ticks (gameplay movement)
	// Keys: Up Down Left Right Enter Esc Mouse1 Mouse2 Generic_Start, plus aliases
	// (w/a/s/d, start/select/confirm -> Enter, back/cancel -> Esc, fire/shoot -> Mouse1).
	public static class DebugInput
	{
		// Per-MyKeys countdown of ticks still to force "down". Sized to the enum.
		private static readonly int[] holdTicks =
			new int[Enum.GetValues(typeof(EvilAliens.MyKeys)).Length];

		// Per-MyKeys persistent "held" flag for the on-screen touch controls (Stage 9).
		// Unlike holdTicks (a tick countdown for scripted taps), these stay down until JS
		// clears them on touchend/cancel — an on-screen D-pad/fire button held with a
		// finger behaves like a physical key held across many frames.
		private static readonly bool[] touchHeld =
			new bool[Enum.GetValues(typeof(EvilAliens.MyKeys)).Length];

		// Esc-suppression window (card b0a2f525). The browser RESERVES Esc to leave DOM
		// fullscreen and it cannot be preventDefault'd -- but that same Esc keydown is ALSO
		// delivered to KNI's keyboard state, so InputHandler would read it and ALSO step back
		// in the menu (Esc doing two things at once). On fullscreenchange->exit the JS side
		// (eaSuppressEsc in index.html) opens a short window here; InputHandler masks the raw
		// keyboard Esc while it's active so leaving fullscreen doesn't also navigate back.
		// escGrace = a minimum window (covers the exit keydown arriving a tick or two AFTER
		// the fullscreenchange event); escGuard = a hard cap so a genuinely held Esc can't
		// keep Esc dead forever (fail-open).
		private static int escGraceTicks;
		private static int escGuardTicks;

		// JS bridge: DotNet.invokeMethod('EvilAliensWeb', 'debugPress', key, frames).
		// `frames` is how many ticks to hold the key down (>=1; 1 == a single tap).
		// Re-pressing extends to the longest pending hold.
		[JSInvokable("debugPress")]
		public static void Press(string key, int frames)
		{
			if (!TryMap(key, out EvilAliens.MyKeys mk))
			{
				Console.WriteLine("[debug] eaPress: unknown key '" + key + "'");
				return;
			}
			if (frames < 1)
			{
				frames = 1;
			}
			int idx = (int)mk;
			if (frames > holdTicks[idx])
			{
				holdTicks[idx] = frames;
			}
			Console.WriteLine("[debug] eaPress " + mk + " x" + frames + " frame(s)");
		}

		// JS bridge for the on-screen touch controls (eaHold in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHold', key, down). Sets/clears the
		// persistent held state for `key` so it reads as down for as long as the finger
		// stays on the button. Unknown keys are ignored.
		[JSInvokable("debugHold")]
		public static void Hold(string key, bool down)
		{
			if (TryMap(key, out EvilAliens.MyKeys mk))
			{
				touchHeld[(int)mk] = down;
			}
		}

		// JS bridge for QA/demo of the cinematic slow-motion effect (eaSlowmo in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugSlowmo', seconds).
		// Triggers the same slow-motion burst the fully-powered 1up does (Oracle.SetSlowmotion)
		// so the ghost-trail look can be seen on demand without grinding a powerup. The Oracle
		// service is registered for the whole game's life, so this only no-ops meaningfully in a
		// menu because Oracle.Update resets slowmo to 1f whenever no player ship is alive — i.e.
		// it bites only inside a level with a live ship. Not gameplay input. The null guard
		// below is purely defensive (before the game is constructed).
		[JSInvokable("debugSlowmo")]
		public static void Slowmo(float seconds)
		{
			if (seconds <= 0f)
			{
				seconds = 12f;
			}
			EvilAliens.IOracleService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>();
			if (svc?.Oracle == null)
			{
				Console.WriteLine("[debug] eaSlowmo: oracle not ready (game not constructed yet)");
				return;
			}
			svc.Oracle.SetSlowmotion(seconds);
			Console.WriteLine("[debug] eaSlowmo " + seconds + "s");
		}

		// JS bridge for QA/demo of the screen shake (eaShake in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugShake', trauma). Adds shake trauma
		// (0..1; 0/omitted => a solid 0.6 burst) so the camera shake can be seen/tuned on
		// demand anywhere — it's a pure present-blit effect, so it works even in a menu.
		[JSInvokable("debugShake")]
		public static void Shake(float trauma)
		{
			if (trauma <= 0f)
			{
				trauma = 0.6f;
			}
			Juice.AddTrauma(trauma > 1f ? 1f : trauma);
			Console.WriteLine("[debug] eaShake " + trauma);
		}

		// JS bridge for QA/demo of the hit-stop (eaHitstop in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHitstop', ms). Freezes game time for
		// `ms` milliseconds of real time (0/omitted => 120ms) — most visible in a level
		// with things moving, e.g. ?level=Level1.
		[JSInvokable("debugHitstop")]
		public static void Hitstop(float ms)
		{
			if (ms <= 0f)
			{
				ms = 120f;
			}
			Juice.AddHitStop(ms / 1000f);
			Console.WriteLine("[debug] eaHitstop " + ms + "ms");
		}

		// JS bridge for the wall-tower cost meter (eaWallPerf / eaWallStats in wwwroot/index.html,
		// polled by the eaWalls slider panel). WallPerf(on) arms the accumulators; WallStats() returns
		// one formatted line (fps, frame ms + p95, tower-pass ms, slice draws). Off by default, so a
		// normal boot never touches the stopwatch.
		//
		// The panel POLLS this ~4x/second rather than pushing per frame: a per-frame JS interop call
		// would itself cost more than the thing being measured.
		[JSInvokable("debugWallPerf")]
		public static void WallPerf(bool on)
		{
			WallProfiler.SetEnabled(on);
		}

		[JSInvokable("debugWallStats")]
		public static string WallStats()
		{
			return WallProfiler.Report();
		}

		// JS bridge for the join-in-progress scenery diff (eaNetBg in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetBg'). Returns the live deep-state the
		// JIP catch-up replays (card 45a4e48d) as one parseable line, so a joiner's scenery is
		// verified by DIFFING the two peers' output rather than by screenshotting a fly-by that
		// moves every frame (root CLAUDE.md: never verify motion with timed live screenshots).
		// Run it in both windows' consoles once the joiner is up; the lines must match.
		[JSInvokable("debugNetBg")]
		public static string NetBg()
		{
			EvilAliens.GameScene scene = EvilAliens.GameScene.NetActiveScene;
			return scene == null ? "[netbg] no level" : "[netbg] " + scene.NetCatchUpStateLine();
		}

		// JS bridge for the JIP catch-up round-trip self-test (eaNetBgTest in index.html).
		// One tab, no peer, no timing: capture the burst, wipe the scenery to a fresh joiner's,
		// replay through the real client apply path, diff. See GameScene.NetCatchUpSelfTest --
		// it is destructive (the screen re-runs the hyperspace entry), so it is a console
		// command, never something a boot flag arms.
		[JSInvokable("debugNetBgTest")]
		public static string NetBgTest()
		{
			EvilAliens.GameScene scene = EvilAliens.GameScene.NetActiveScene;
			return scene == null ? "[netbgtest] no level" : scene.NetCatchUpSelfTest();
		}

		// JS bridge for the dev-build FPS HUD (eaFps in wwwroot/index.html; card 22e655b5).
		// FpsProfile(on) arms the per-phase accumulators, FpsStats() returns the HUD's JSON
		// payload and FpsStatsLine() the one-line console form. Same polling contract as the
		// wall meter above — the HUD reads this ~4x/second, never per frame.
		[JSInvokable("debugFpsProfile")]
		public static void FpsProfile(bool on)
		{
			FrameProfiler.SetEnabled(on);
		}

		[JSInvokable("debugFpsStats")]
		public static string FpsStats()
		{
			return FrameProfiler.Report();
		}

		[JSInvokable("debugFpsStatsLine")]
		public static string FpsStatsLine()
		{
			return FrameProfiler.StatsLine();
		}

		// Mean GL draw calls per frame, pushed from JS (the HUD patches drawElements/drawArrays
		// — see index.html eaFps). Counted there because BlazorGL's cost is per-CALL and JS sees
		// every source of them at once (sprite batches, bloom passes, the walls' 3D primitives)
		// without touching SpriteBatchWrapper. The HUD renders its own copy; this push exists so
		// the console one-liner is complete, and rides the 4Hz poll — NOT per frame, which would
		// cost more interop than the thing being measured.
		[JSInvokable("debugFpsGlCalls")]
		public static void FpsGlCalls(int calls)
		{
			FrameProfiler.NoteGlCalls(calls);
		}

		// eaFps.test(): run the frame-window maths over a synthetic frame series and report
		// measured vs expected. The point is the vsync trap itself — `work` ms of work
		// delivered every `interval` ms must read as 1000/interval fps and 1000/work headroom.
		[JSInvokable("debugFpsSelfTest")]
		public static string FpsSelfTest(double workMs, double intervalMs, int frames)
		{
			return FrameProfiler.SelfTest(workMs, intervalMs, frames);
		}

		// JS bridge for the hitbox debug overlay (eaHitboxes in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHitboxes', on). Toggles the ?hitboxes
		// overlay at runtime (draws every collidable's collision shape colour-coded by kind);
		// same as booting with ?hitboxes but flippable live from the console.
		[JSInvokable("debugHitboxes")]
		public static void Hitboxes(bool on)
		{
			DebugFlags.SetShowHitboxes(on);
			Console.WriteLine("[debug] eaHitboxes " + (on ? "ON" : "OFF"));
		}

		// JS bridge for the ComponentBin lifecycle scenario suite (eaBinTest in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugBinTest'). Runs
		// Compat/BinTest.Run() against the live bin and returns the PASS/FAIL report.
		[JSInvokable("debugBinTest")]
		public static string BinTest()
		{
			return EvilAliensWeb.Compat.BinTest.Run();
		}

		// JS bridge for the AI telemetry (eaAiBench in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugAiBench'). Returns Compat/AiBench's report
		// -- wall contacts, the heading-reversal jitter rate, fire idleness, level progress and
		// the run verdict. Only meaningful on a ?aibench boot (card f4d1721f).
		[JSInvokable("debugAiBench")]
		public static string AiBench()
		{
			return EvilAliensWeb.Compat.AiBench.Report();
		}

		// eaAiBench.reset() -- rearm the counters mid-run (e.g. to score one wall section
		// rather than the whole soak). Does not touch the game.
		[JSInvokable("debugAiBenchReset")]
		public static string AiBenchReset()
		{
			EvilAliensWeb.Compat.AiBench.Reset();
			return "[aibench] counters reset";
		}

		// eaAiBench.world() -- census of the live components vs what the AI's world model
		// (Oracle.GetBaddies) actually contains. The answer to "the level is stalled and the bot
		// is shooting something -- what is it blind to?".
		[JSInvokable("debugAiBenchWorld")]
		public static string AiBenchWorld()
		{
			return EvilAliensWeb.Compat.AiBench.World();
		}

		// eaAiBench.soak(seconds) -- headless AI soak: tick the real game loop at a fixed 60Hz
		// dt with no Draw, in bounded chunks. The ONLY way to soak the AI reliably from
		// automation: a background tab throttles rAF to ~1Hz, so a rendered run measures nothing.
		[JSInvokable("debugAiBenchRun")]
		public static string AiBenchRun(double chunkSeconds)
		{
			return EvilAliensWeb.Compat.AiBench.RunHeadless(chunkSeconds);
		}

		// JS bridge for eaScore() -- the per-slot score/combo dump. Card b0ab09ec's two-window
		// comparison is "do the peers agree on the tally", which reading HUD pixels answers
		// badly (the panels are small, chrome-shaded and mid-animation); this prints the
		// numbers, plus the provisional total still riding on top of the host's score.
		[JSInvokable("debugScoreDump")]
		public static string ScoreDump()
		{
			EvilAliens.ScoreVisualiser sv = EvilAliens.ServiceHelper.Get<EvilAliens.IScoreService>().Score;
			EvilAliens.Oracle oracle = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>().Oracle;
			var sb = new System.Text.StringBuilder("[score] lives=").Append(sv.Lives);
			for (int i = 0; i < EvilAliens.Oracle.MaxPlayers; i++)
			{
				sb.Append(" | s").Append(i).Append(oracle.IsSeated(i) ? "=" : "(empty)=")
					.Append((int)sv.PointScore(i)).Append(" combo=").Append(sv.Combo(i));
				float pending = EvilAliensWeb.Compat.Net.NetPuppets.UnsettledFor(i);
				if (pending != 0f)
				{
					sb.Append(" unsettled=").Append(pending.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
				}
				// Card 1a3ad45a: whose simulation the slot's combo and powerup levels come from,
				// and what they are. `own` is the whole point of the two-window comparison -- the
				// SAME slot must read own=1 on one console and own=0 on the other, and the combo
				// and lv= figures beside it must agree across the pair.
				if (i < EvilAliens.ScoreVisualiser.SlotCount)
				{
					int[] levels = new int[EvilAliensWeb.Compat.Net.NetProtocol.HudLevelCount];
					sv.NetReadHudState(i, levels, out _, out byte activeType, out float progress);
					sb.Append(" own=").Append(EvilAliensWeb.Compat.Net.NetSession.OwnsSlot(i) ? 1 : 0)
						.Append(" pu=").Append(activeType == EvilAliensWeb.Compat.Net.NetProtocol.HudPowerupNone
							? "none"
							: ((EvilAliens.Powerup.PowerupType)activeType).ToString() + "@" + ((int)(progress * 100f)) + "%")
						.Append(" lv=").Append(string.Join(",", levels));
				}
			}
			return sb.ToString();
		}

		// JS bridge for the co-op score-reconciliation self-test (eaNetScore in
		// wwwroot/index.html, card b0ab09ec). Drives NetScoreLedger -- the real policy -- on a
		// VIRTUAL clock against a synthetic two-peer kill stream, running the old max() adoption
		// over the identical stream first so the drift it fixes is demonstrated, not asserted;
		// then round-trips a real EvDeath through ApplyAwards against the live ScoreVisualiser.
		// Needs no session and no second tab: the failure is a slow tally drift, and a
		// backgrounded peer tab throttles to ~1 tick/sec so two windows cannot show it anyway.
		[JSInvokable("debugNetScoreTest")]
		public static string NetScoreTest(int kills, int comboSkew, int rttMs, int seed)
		{
			return EvilAliensWeb.Compat.Net.NetScoreLedger.SelfTest(kills, comboSkew, rttMs, seed)
				+ "\n\n" + EvilAliensWeb.Compat.Net.NetPuppets.WireRoundTripTest();
		}

		// JS bridge for the world-snapshot unknown-id attribution (eaNetSnap in
		// wwwroot/index.html, card 48ab9b2f). Drives the real NetPuppets.OnSnapshotEntry so the
		// three branches that all `return false` -- rebuilt / left dead / refused -- are proved
		// to report the kind they took, and pins the derived snapTurn arithmetic. A
		// classification is invisible in any frame, so this is data, not a two-window run.
		[JSInvokable("debugNetSnapTest")]
		public static string NetSnapTest()
		{
			return EvilAliensWeb.Compat.Net.NetSnapshotTest.Run();
		}

		// JS bridge for the co-op per-slot combo/powerup self-test (eaNetCombo in
		// wwwroot/index.html, card 1a3ad45a). Round-trips the real MsgHudState wire format,
		// then drives the real PowerupData exp curve over two divergent combo streams -- running
		// the OLD ungated behaviour first, so the slow motion and the stray powerup levels it
		// used to inflict on a slot this peer does not own are demonstrated, not asserted.
		[JSInvokable("debugNetComboTest")]
		public static string NetComboTest()
		{
			return EvilAliensWeb.Compat.Net.NetComboTest.Run();
		}

		// JS bridge for the co-op kick/block rules (eaKickTest in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugKickTest'). Runs
		// Compat/Net/NetKickTest.Run() and returns the PASS/FAIL report.
		[JSInvokable("debugKickTest")]
		public static string KickTest()
		{
			return EvilAliensWeb.Compat.Net.NetKickTest.Run();
		}

		// JS bridge for the primary-slot negotiation (eaSlotTest in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugSlotTest'). Runs
		// Compat/Net/NetSlotTest.Run() and returns the PASS/FAIL report.
		[JSInvokable("debugSlotTest")]
		public static string SlotTest()
		{
			return EvilAliensWeb.Compat.Net.NetSlotTest.Run();
		}

		// JS bridge for the texture-load probe (eaTexProbe in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugTexProbe', 'GFX/Base/756'). Reports which
		// precompiled sibling shipped, which file the texture ACTUALLY came from, its actual vs
		// logical size, and its mip level count -- and on failure the whole exception chain,
		// which is the one thing KNI's own FileNotFoundException throws away. See
		// Compat/TexProbe.cs.
		[JSInvokable("debugTexProbe")]
		public static string TexProbeRun(string assetName)
		{
			return EvilAliensWeb.Compat.TexProbe.Run(assetName);
		}

		// JS bridge for the background tile-cull oracle (eaBgCull in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugBgCull'). Sweeps the real cull predicate,
		// dry-runs scenario layers (incl. mirrored and TALL ones, which no shipped background
		// is) through the real Draw, and censuses the live layers. See Compat/BgCullTest.cs --
		// the cull's correctness is invisible to a screenshot, so it is read as data.
		[JSInvokable("debugBgCull")]
		public static string BgCull()
		{
			return EvilAliensWeb.Compat.BgCullTest.Run();
		}

		// JS bridge for the death/reset path (eaKillShips in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugKillShips'). Asplodes every
		// LOCALLY-OWNED PlayerShip through the real Asplode()->Die() path, so the scene's
		// AllShipsDead check fires LoseLife (host) / the host's EvReset mirrors (client).
		// Asplode's only guard is !IsDead, so this bites through ?invuln and the post-respawn
		// invulnerability window alike -- it is a scripted death, NOT a simulated hazard hit.
		// Written for the two-tab co-op gate (card 9009a1c4): a death/reset is the
		// standing-purge-filter window worth testing, it needs BOTH co-op ships down, and
		// waiting for the ?aiplayer AI to die is neither timely nor repeatable.
		// Remote/RemoteFriend puppets are skipped -- their owner decides their deaths, and
		// asploding one here would fake a death the owning peer never sent.
		[JSInvokable("debugKillShips")]
		public static string KillShips()
		{
			Microsoft.Xna.Framework.Game game =
				EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>().ComponentBin.Game;
			// Collect BEFORE asploding: Asplode() adds two Explosions, and bin adds are instant
			// (card 02d9ad67), so killing inside the enumeration would mutate Game.Components
			// mid-foreach.
			System.Collections.Generic.List<EvilAliens.PlayerShip> targets =
				new System.Collections.Generic.List<EvilAliens.PlayerShip>();
			foreach (Microsoft.Xna.Framework.IGameComponent item
				in (System.Collections.ObjectModel.Collection<Microsoft.Xna.Framework.IGameComponent>)(object)game.Components)
			{
				if (item is EvilAliens.PlayerShip ship
					&& ship.Controller != EvilAliens.ControlDevice.Remote
					&& ship.Controller != EvilAliens.ControlDevice.RemoteFriend)
				{
					targets.Add(ship);
				}
			}
			foreach (EvilAliens.PlayerShip ship in targets)
			{
				ship.Asplode();
			}
			return "[debug] eaKillShips asploded " + targets.Count + " local ship(s)";
		}

		// JS bridge for the on-demand roster dump (eaNetRoster in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetRoster'). Prints the same roster=
		// string the 5s [net] metrics line carries, plus resets=, at the instant it is called.
		// Written for the reset-with-couch-players gate (card af0eb00a): the assertion is
		// before-vs-after a ~2.7s reset, which the metrics cadence can straddle entirely.
		[JSInvokable("debugNetRoster")]
		public static string NetRoster()
		{
			return EvilAliensWeb.Compat.Net.NetSession.RosterDump();
		}

		// JS bridge for a couch join RIGHT NOW (eaNetCouchJoin in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetCouchJoin'). Makes the same
		// NetSession.TrySeatLocalJoin call a real gamepad Start press makes. HOST-SIDE that
		// works before a peer has paired, which ?netlocal cannot do (TickLocalJoinSim is gated
		// behind PeerUp -- correctly: pre-pairing, AllocateSeat cannot yet know which seat the
		// joiner needs). That pre-pairing window is the only way to fill the roster ahead of a
		// joiner, i.e. the sole trigger for the host's RejectFull path (card af0eb00a). On a
		// CLIENT it is still PeerUp-gated, because a client seat has to be asked for.
		[JSInvokable("debugNetCouchJoin")]
		public static string NetCouchJoin()
		{
			if (!EvilAliensWeb.Compat.Net.NetSession.Active)
			{
				return "[debug] eaNetCouchJoin: no net session (needs a ?net= boot)";
			}
			string outcome = EvilAliensWeb.Compat.Net.NetSession.DebugCouchJoin();
			return "[debug] eaNetCouchJoin: " + outcome + "\n  "
				+ EvilAliensWeb.Compat.Net.NetSession.RosterDump();
		}

		// JS bridge for the live colorize-tuner slider panel (eaHue in wwwroot/index.html,
		// shown on the ?harness=battleskull page): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetHue', start, end, target, trackHp, cycle, loop). Overrides the BattleSkull
		// hue band/target in real time so the sliders retune without a page reload — same
		// effect as the ?huestart/?hueend/?huetarget/?huecycle/?hueloop URL flags, just live.
		// trackHp == true pins nothing (Target follows HP, the shipped default); false pins the
		// Target to `target`. Only bites while the harness is up (HarnessColorize gates on it).
		[JSInvokable("debugSetHue")]
		public static void SetHue(double start, double end, double target, bool trackHp, bool cycle, double loop)
		{
			DebugFlags.SetHueOverride(
				(float)start,
				(float)end,
				trackHp ? (float?)null : (float)target,
				cycle,
				(float)loop);
		}

		// JS bridge for the live laser-tuner slider panel (eaLazer in wwwroot/index.html, shown on
		// the ?lazershot showcase): DotNet.invokeMethod('EvilAliensWeb', 'debugSetLazer',
		// chargeScale, capScale, arcRate, tendrilSpeed). Overrides the four Quad/LazerGenerator FX
		// knobs in real time so the sliders retune without a page reload — same effect as the
		// ?lazerchargescale/?lazercapscale/?lazerarcs/?lazertendrilspeed URL flags, just live.
		[JSInvokable("debugSetLazer")]
		public static void SetLazer(double chargeScale, double capScale, double arcRate, double tendrilSpeed)
		{
			DebugFlags.SetLazerOverride(
				(float)chargeScale,
				(float)capScale,
				(float)arcRate,
				(float)tendrilSpeed);
		}

		// JS bridge for the live holo-sim tuner slider panel (eaHolo in wwwroot/index.html, shown on
		// ?level=Tutorial / ?level=ClassicAliens / a bare ?holotune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetHolo', green, greenPulse, burst, staticRate, filter). Overrides the simulator
		// filter knobs in real time — same effect as the ?hologreen/?hologreenpulse/?holoburst/
		// ?holostaticrate/?holofilter URL flags, just live (HoloSim reads them every frame).
		[JSInvokable("debugSetHolo")]
		public static void SetHolo(double green, double greenPulse, double burst, double staticRate, double filter)
		{
			DebugFlags.SetHoloOverride(
				(float)green,
				(float)greenPulse,
				(float)burst,
				(float)staticRate,
				(float)filter);
		}

		// JS bridge for the live connector-tuner slider panel (eaConnector in wwwroot/index.html, shown
		// on ?level=TeamChallenge / a bare ?connectortune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetConnector', boltCount, arcRate, jitter, pulse, glow). Overrides the ShipConnector
		// lightning knobs in real time — same effect as the ?connectorbolts/?connectorarcs/
		// ?connectorjitter/?connectorpulse/?connectorglow URL flags, just live.
		[JSInvokable("debugSetConnector")]
		public static void SetConnector(double boltCount, double arcRate, double jitter, double pulse, double glow)
		{
			DebugFlags.SetConnectorOverride(
				(int)System.Math.Round(boltCount),
				(float)arcRate,
				(float)jitter,
				(float)pulse,
				(float)glow);
		}

		// JS bridge for the live wall-tower slider panel (eaWalls in wwwroot/index.html, shown on
		// ?level=Level3&wallsonly / a bare ?walltune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetWalls', towers, depth, fog, sideDark, sideTile, faceLight, faceAngle, topLift,
		// bands, wisps, wispSpeed). Overrides the Level-3 tower knobs in real time so the sliders
		// retune without a page reload — same effect as the ?walltowers/?walldepth/?wallfog/
		// ?wallsidedark/?wallsidetile/?wallfacelight/?wallfaceangle/?walltoplift/?wall3dbands/
		// ?wallwisps/?wallwispspeed URL flags, just live. (?wallfogcolor stays URL-only — a colour
		// picker is a different widget and the haze reads fine off the two brightness knobs.)
		[JSInvokable("debugSetWalls")]
		public static void SetWalls(bool towers, double depth, double fog, double sideDark, double sideTile, double faceLight, double faceAngle, double topLift, double bands, double wisps, double wispSpeed)
		{
			DebugFlags.SetWallsOverride(
				towers,
				(float)depth,
				(float)fog,
				(float)sideDark,
				(float)sideTile,
				(float)faceLight,
				(float)faceAngle,
				(float)topLift,
				(float)bands,
				(float)wisps,
				(float)wispSpeed);
		}

		// JS bridge for the live spider-tuner slider panel (eaSpider in wwwroot/index.html, shown on
		// ?harness=spiderjump / ?level=Level2&spiders / ?spidertune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetSpider', jumpFrame, landFrame, jumpX, pinJumpX, shadowX, shadowY, shadowScale, airX, airY).
		// Overrides the Mars jumping-spider alignment knobs in real time so the sliders retune without a
		// page reload — same effect as the ?spiderjumpframe/?spiderlandframe/?spiderjumpx/?spidershadow*/
		// ?spiderair* URL flags, just live. airX/airY nudge the airborne flying sprite so the launch +
		// landing transitions line up. pinJumpX == false => leave the launch X RANDOM per spider (the
		// shipped behaviour, jumpX ignored); true => pin it to `jumpX` so dialing is repeatable.
		[JSInvokable("debugSetSpider")]
		public static void SetSpider(double jumpFrame, double landFrame, double jumpX, bool pinJumpX, double shadowX, double shadowY, double shadowScale, double airX, double airY, double phase, bool freezePhase)
		{
			DebugFlags.SetSpiderOverride(
				(float)jumpFrame,
				(float)landFrame,
				pinJumpX ? (float?)(float)jumpX : null,
				(float)shadowX,
				(float)shadowY,
				(float)shadowScale,
				(float)airX,
				(float)airY,
				freezePhase ? (float?)(float)phase : null);
		}

		// JS bridge for the live webcam-tuner stepper panel (eaWcTune in wwwroot/index.html,
		// shown on the webcam level when ?wctune is set): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetWcTune', hearts, kills, saucers, saucerSpeed, plasmaSpeed, spawnInterval,
		// armDelay, chargeTime). Overrides the eight WebcamLevel.Tunings[] knobs in real time
		// — ABSOLUTE final values (what you'd bake into the table), unlike the ?wc* URL
		// multipliers. WebcamLevel picks the change up on its next Update via WebcamTuneVersion.
		[JSInvokable("debugSetWcTune")]
		public static void SetWcTune(int hearts, int kills, int saucers, double saucerSpeed, double plasmaSpeed, double spawnInterval, double armDelay, double chargeTime, int mineMax, double mineSpawn)
		{
			DebugFlags.SetWebcamTuneOverride(hearts, kills, saucers, (float)saucerSpeed, (float)plasmaSpeed, (float)spawnInterval, (float)armDelay, (float)chargeTime, mineMax, (float)mineSpawn);
		}

		// Companion: drop all runtime webcam-tuner overrides (the panel's "Reset to tier"
		// button) so the level falls back to its shipped Tunings[] row + any ?wc* URL flags.
		[JSInvokable("debugClearWcTune")]
		public static void ClearWcTune()
		{
			DebugFlags.ClearWebcamTuneOverride();
		}

		// JS bridge for the live network-impairment panel (eaNetSim in wwwroot/index.html, shown
		// on a ?net boot that also passes ?netsim, or via eaNetSim.show()/eaNetSim(...) from the
		// console): DotNet.invokeMethod('EvilAliensWeb', 'debugSetNetSim', lagMs, lossPct,
		// jitterMs). Overrides the artificial impairment applied to INBOUND net traffic in real
		// time -- same effect as ?netlag=/?netloss=, just live, plus jitter which has no URL flag
		// (panel-only by design: it is the knob that makes the stream lane actually REORDER, so
		// it belongs next to the other two rather than in a boot URL).
		[JSInvokable("debugSetNetSim")]
		public static void SetNetSim(double lagMs, double lossPct, double jitterMs)
		{
			DebugFlags.SetNetSimOverride((float)lagMs, (float)lossPct, (float)jitterMs);
		}

		// Companion self-test (the panel's "Self-test" button / console eaNetSim.test(...)):
		// runs N synthetic packets per lane through a real NetImpairment on a VIRTUAL clock and
		// returns the measured delay / drop rate / per-lane reorder count as one line. This is
		// the card's primary verification -- impairment is behaviour over time, so the repo rule
		// is to read the data, not a frame.
		[JSInvokable("debugNetSimTest")]
		public static string NetSimTest(double lagMs, double lossPct, double jitterMs, int packets)
		{
			return Net.NetImpairment.SelfTest((float)lagMs, (float)lossPct, (float)jitterMs, packets);
		}

		// JS bridge for the ?texviewer control panel (eaTexViewer in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugSetTexViewer', cmd). Enqueues a panel
		// command ("next"/"prev"/"flip:1"/"mode:1"/"pick:0"/"zoom:2.5"/"fit") that
		// TexViewerScene drains each Update. Save is done JS-side (POST /api/texdecide),
		// so it never routes through here.
		[JSInvokable("debugSetTexViewer")]
		public static void SetTexViewer(string cmd)
		{
			TexViewerInterop.Post(cmd);
		}

		// JS bridge (eaSuppressEsc in index.html), fired from a fullscreenchange listener when
		// the browser LEAVES fullscreen. Opens the Esc-swallow window so the Esc that exited
		// fullscreen doesn't also step back a menu. Idempotent -- re-fires just refresh it.
		[JSInvokable("debugSuppressEsc")]
		public static void SuppressEsc()
		{
			escGraceTicks = 8;    // ~130ms min: covers the exit keydown landing a tick or two late
			escGuardTicks = 40;   // ~0.66s hard cap so a held Esc can't kill Esc permanently
		}

		// Called once per InputHandler tick for the Esc key with its RAW keyboard-down state.
		// Returns true while the post-fullscreen-exit Esc should be swallowed: through the grace
		// window, then for as long as Esc stays CONTINUOUSLY held (the exit press), up to the
		// guard cap. The first tick with the grace elapsed AND Esc released ENDS the window --
		// both counters are zeroed -- so a deliberate second Esc press is never swallowed by a
		// leftover guard; the guard only bounds the continuous-hold case (fail-open).
		internal static bool EscSuppressActive(bool rawEscDown)
		{
			if (escGraceTicks <= 0 && !rawEscDown)
			{
				escGuardTicks = 0;
				return false;
			}
			if (escGuardTicks <= 0)
			{
				escGraceTicks = 0;
				return false;
			}
			if (escGraceTicks > 0)
			{
				escGraceTicks--;
			}
			escGuardTicks--;
			return true;
		}

		// Called once per MyKeys per InputHandler tick: returns true (and decrements)
		// while injected ticks remain. Folded into the keyboard `flag`, so the existing
		// press/hold edge detection treats it exactly like a held physical key — first
		// forced tick = a fresh Pressed edge, the rest = Down, then a clean release.
		internal static bool Consume(int idx)
		{
			if (idx < 0 || idx >= holdTicks.Length)
			{
				return false;
			}
			// A scripted tap (countdown) OR a held touch button both read as "down".
			if (holdTicks[idx] > 0)
			{
				holdTicks[idx]--;
				return true;
			}
			return touchHeld[idx];
		}

		private static bool TryMap(string key, out EvilAliens.MyKeys mk)
		{
			mk = default(EvilAliens.MyKeys);
			if (string.IsNullOrWhiteSpace(key))
			{
				return false;
			}
			string k = key.Trim();
			// Enum.TryParse ALSO accepts raw numeric strings ("42") and any undefined
			// underlying value, which would flow straight into holdTicks[(int)mk]/
			// touchHeld[(int)mk] in Press/Hold and throw IndexOutOfRangeException. Only a
			// real, defined member name may map — reject a leading digit/sign and verify
			// the parsed value is actually defined.
			if (k.Length > 0 && !char.IsDigit(k[0]) && k[0] != '+' && k[0] != '-'
				&& Enum.TryParse<EvilAliens.MyKeys>(k, ignoreCase: true, out mk)
				&& Enum.IsDefined(typeof(EvilAliens.MyKeys), mk))
			{
				return true;
			}
			// Clear any bogus value a successful-but-out-of-range TryParse left in mk before
			// falling through to the alias switch (which sets mk itself on a hit).
			mk = default(EvilAliens.MyKeys);
			switch (k.ToLowerInvariant())
			{
			case "up":
			case "w":
				mk = EvilAliens.MyKeys.Up;
				return true;
			case "down":
			case "s":
				mk = EvilAliens.MyKeys.Down;
				return true;
			case "left":
			case "a":
				mk = EvilAliens.MyKeys.Left;
				return true;
			case "right":
			case "d":
				mk = EvilAliens.MyKeys.Right;
				return true;
			case "return":
			case "start":
			case "select":
			case "confirm":
			case "ok":
				mk = EvilAliens.MyKeys.Enter;
				return true;
			case "escape":
			case "back":
			case "cancel":
				mk = EvilAliens.MyKeys.Esc;
				return true;
			case "fire":
			case "shoot":
			case "mouse":
				mk = EvilAliens.MyKeys.Mouse1;
				return true;
			default:
				return false;
			}
		}
	}
}
