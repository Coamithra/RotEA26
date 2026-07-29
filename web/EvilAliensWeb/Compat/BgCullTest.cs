using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console oracle for BackgroundImage's per-tile on-screen cull (card 5216412d). Invoke with
// eaBgCull() from the browser console.
//
// This exists because the fix it guards changes NO shipping pixel: mirrorX/mirrorY are never
// set true, and every live tile is square or wider than tall, so the old expression only ever
// over-drew. A screenshot therefore cannot tell the broken cull from the fixed one -- the
// decisions have to be read as DATA instead. Written against the REAL predicate and the REAL
// Draw path (the eaNetSim.test / eaBinTest rule): a python mirror of arithmetic this small
// would drift and prove nothing.
//
// Four parts, because the defects fail in different places:
//  1. the predicate itself, swept over shapes/scales/positions -- catches the vertical term
//     measuring the tile's WIDTH (a TALL tile is culled while visible);
//  2. whole scenario layers walked through the real Draw -- catches the missing * size, which
//     is a CALL-SITE defect in the mirrorX blocks and so invisible to part 1;
//  3. a census of the live layers -- the integration evidence, and where the Mars ground's
//     wasted off-screen row shows up as a number;
//  4. a differential against the pre-ef55b76e `>=` predicate, swept across scroll phases -- the
//     standing guard that tightening the boundary drops only tiles with nothing to draw.
//
// READ THIS BEFORE READING ANY GREEN TICK HERE AS EVIDENCE. Three of the four parts assert
// something that, against the CURRENT predicate, cannot fail:
//   * parts 1 and 3, because card ef55b76e tightened the cull to a strict `> 0`, which makes
//     TileOnScreen and Intersects the SAME float expression -- so the tightness check and the
//     census' "of which off-screen 0" are now tautologies;
//   * part 4's own assertion, because KeptByOldCull differs from TileOnScreen ONLY by `>=` vs
//     `>` on the same arguments, so a flipped decision REQUIRES `x+w == 0 || y+h == 0` -- the
//     difference set is a subset of the zero-area set algebraically, not empirically.
// None of that makes them worthless, but their value is as SENTINELS against a future edit, not
// as proof of today's behaviour. A margin or an inset added to TileOnScreen would start producing
// flips with real area and part 4 would catch it; part 4 also re-derives the extents from the
// trace rather than the call site, so it catches argument drift there.
//
// The actual proof that the tightening is pixel-neutral is the half-open-interval argument in
// BackgroundImage.TileOnScreen's comment -- a tile spanning [-w, 0] contains no pixel centre, so
// it rasterises nothing. It was corroborated by a 40-image pre/post pixel diff over five
// backgrounds x eight scroll phases (all identical), not by this suite.
//
// What carries weight here on its own: part 2, where the predicate's arguments come from the REAL
// Draw call sites while the truth is computed from independently recorded extents, so a call site
// passing the wrong texture, offset or scale still fails.
internal static class BgCullTest
{
	private const float ScreenW = 800f;

	private const float ScreenH = 600f;

	// The truth the cull must respect: a tile is worth drawing iff it has POSITIVE area on
	// screen. Deliberately not written in terms of TileOnScreen -- checking the predicate
	// against a restatement of itself would be a tautology.
	private static bool Intersects(float x, float y, float w, float h)
	{
		return x + w > 0f && x < ScreenW && y + h > 0f && y < ScreenH;
	}

	// The cull as it stood BEFORE card ef55b76e: `>= 0` on the right/bottom edge, so a tile
	// touching the screen edge with zero on-screen area was kept and drawn. Deliberately a
	// verbatim copy rather than a call into anything -- it is the negative control part 4 diffs
	// the live predicate against (the eaTeamSeat / eaNetScore.test idiom), and it has to keep
	// stating the old behaviour even as TileOnScreen moves on.
	private static bool KeptByOldCull(float x, float y, float w, float h)
	{
		return x + w >= 0f && x < ScreenW && y + h >= 0f && y < ScreenH;
	}

	// Zero on-screen area: the tile's right or bottom edge lands exactly on the screen's origin
	// edge. Painting one is a no-op, which is why part 4 can call the tightening pixel-neutral.
	private static bool TouchesOriginEdgeExactly(float x, float y, float w, float h)
	{
		return x + w == 0f || y + h == 0f;
	}

	private static readonly (int W, int H, string Name)[] Shapes =
	{
		(512, 512, "square 512 (756)"),
		(30, 30, "square 30 (grid3)"),
		(1024, 768, "wide 1024x768 (Starfield2)"),
		(1587, 971, "wide 1587x971 (marsloop)"),
		(64, 256, "TALL 64x256"),
		(100, 400, "TALL 100x400")
	};

	private static readonly float[] Scales = { 1f / 3.238f, 1f, 1.5f, 2f, 2.4f };

	// Scroll phases for part 4. `Move` wraps position into [0, realsize), so these are the
	// fractions a scrolling layer really passes through. 0 is the case this card is about and
	// must stay in the set; the rest are the fractional coverage a single ?bgfreeze frame cannot
	// give, including one just short of a full wrap.
	//
	// Part 2 deliberately keeps its own SHORTER inline set rather than using this one: it runs 72
	// configurations and sweeps both axes, so 9x9 phases each would be 5832 dry runs for no extra
	// coverage of what part 2 is actually for (the mirrorX/mirrorY call sites). Don't "unify" them.
	private static readonly float[] Phases = { 0f, 1f / 64f, 0.037f, 0.13f, 0.25f, 0.5f, 0.61f, 0.87f, 0.999f };

	private static string F(float v)
	{
		return v.ToString("0.##", CultureInfo.InvariantCulture);
	}

	// Positions worth testing along one axis: a coarse sweep from well before the tile can
	// touch the screen to well past it, plus the exact boundaries, where an off-by-one in the
	// comparison lives.
	private static List<float> Axis(float extent, float screen)
	{
		List<float> v = new List<float>();
		for (float p = 0f - extent - 40f; p < screen + 40f; p += 13f)
		{
			v.Add(p);
		}
		v.Add(0f - extent - 0.5f);
		v.Add(0f - extent);
		v.Add(0f - extent + 0.5f);
		v.Add(-1f);
		v.Add(-0.5f);
		v.Add(0f);
		v.Add(screen - 0.5f);
		v.Add(screen);
		v.Add(screen + 0.5f);
		return v;
	}

	public static string Run()
	{
		StringBuilder sb = new StringBuilder();
		int pass = 0;
		int fail = 0;
		void Check(string name, bool ok, string detail)
		{
			if (ok)
			{
				pass++;
			}
			else
			{
				fail++;
			}
			sb.Append(ok ? "PASS " : "FAIL ").Append(name);
			if (detail != null)
			{
				sb.Append("  ").Append(detail);
			}
			sb.Append('\n');
		}

		// ---- 1. the predicate, swept ------------------------------------------------
		sb.Append("[bgcull] 1. predicate sweep (BackgroundImage.TileOnScreen)\n");
		int cases = 0;
		int flipped = 0;
		int flippedWithArea = 0;
		int combosNeverAtBoundary = 0;
		string firstFlipped = null;
		string firstVacuousCombo = null;
		foreach ((int W, int H, string Name) shape in Shapes)
		{
			foreach (float scale in Scales)
			{
				float w = (float)shape.W * scale;
				float h = (float)shape.H * scale;
				int here = 0;
				int culledVisible = 0;
				int keptOffScreen = 0;
				int flippedHere = 0;
				string firstCulled = null;
				string firstKept = null;
				foreach (float x in Axis(w, ScreenW))
				{
					foreach (float y in Axis(h, ScreenH))
					{
						cases++;
						here++;
						bool visible = Intersects(x, y, w, h);
						bool kept = BackgroundImage.TileOnScreen(x, y, shape.W, shape.H, scale);
						if (visible && !kept)
						{
							culledVisible++;
							if (firstCulled == null)
							{
								firstCulled = "e.g. at (" + F(x) + "," + F(y) + ") the tile covers x " + F(x) + ".." + F(x + w) + " y " + F(y) + ".." + F(y + h);
							}
						}
						else if (kept && !visible)
						{
							keptOffScreen++;
							if (firstKept == null)
							{
								firstKept = "e.g. at (" + F(x) + "," + F(y) + ") the tile covers x " + F(x) + ".." + F(x + w) + " y " + F(y) + ".." + F(y + h);
							}
						}
						// What the tightening actually changed. Counted per (shape, scale) as well as
						// in total, so a combination whose sweep never reaches the boundary shows up
						// instead of hiding behind another one's count.
						if (KeptByOldCull(x, y, w, h) != kept)
						{
							flipped++;
							flippedHere++;
							if (!TouchesOriginEdgeExactly(x, y, w, h))
							{
								flippedWithArea++;
								if (firstFlipped == null)
								{
									firstFlipped = "at (" + F(x) + "," + F(y) + ") the tile covers x " + F(x) + ".." + F(x + w) + " y " + F(y) + ".." + F(y + h);
								}
							}
						}
					}
				}
				string what = shape.Name + " x" + F(scale);
				if (flippedHere == 0)
				{
					combosNeverAtBoundary++;
					if (firstVacuousCombo == null)
					{
						firstVacuousCombo = what;
					}
				}
				// Soundness: never drop a tile the player would have seen. This is the property
				// the old expression violated.
				Check("sound  " + what, culledVisible == 0 && here > 0, culledVisible == 0 ? null : culledVisible + " VISIBLE tiles culled; " + firstCulled);
				// Tightness: and never keep one that is wholly off screen. Without this a cull
				// that simply returned true -- i.e. no cull at all -- would pass the suite.
				Check("tight  " + what, keptOffScreen == 0 && here > 0, keptOffScreen == 0 ? null : keptOffScreen + " tiles kept with no on-screen area; " + firstKept);
			}
		}
		sb.Append("  info  ").Append(cases).Append(" cases; ").Append(flipped)
			.Append(" decisions differ from the pre-ef55b76e `>= 0` cull\n");
		// The tightening may only ever drop a tile that had no area to draw. The POSITIVE CONTROL
		// is per (shape, scale): every combination must actually reach the boundary, or its leg of
		// the assertion proved nothing. Note the failure message must never restate the claim --
		// a vacuous run and a violated run are different problems and read identically otherwise.
		Check("differential: every flipped decision is zero-area",
			flippedWithArea == 0 && combosNeverAtBoundary == 0,
			flippedWithArea != 0
				? flippedWithArea + " of " + flipped + " flipped tiles had REAL on-screen area; " + firstFlipped
				: combosNeverAtBoundary != 0
					? "VACUOUS: " + combosNeverAtBoundary + " shape/scale combinations never reached the boundary (first: " + firstVacuousCombo + ") -- nothing was compared there"
					: flipped + " flipped across all " + (Shapes.Length * Scales.Length) + " combinations, all zero-area");

		// ---- 2. scenario layers through the real Draw --------------------------------
		sb.Append("[bgcull] 2. scenario layers, dry-run through the real Draw\n");
		Game game = ServiceHelper.Get<IComponentBinService>().ComponentBin.Game;
		Texture2D tall = new Texture2D(game.GraphicsDevice, 64, 256);
		Texture2D wide = new Texture2D(game.GraphicsDevice, 256, 64);
		Texture2D square = new Texture2D(game.GraphicsDevice, 128, 128);
		try
		{
			// Grid shapes matter as well as tile shapes: DrawBackground advances a column by
			// row 0's width and accumulates Y per row, so a 1x1 grid cannot tell correct
			// multi-cell indexing from broken. 2x2 exercises it (the only real multi-cell
			// layer, the Mars ground, is [12,1] and is never mirrored).
			foreach ((Texture2D Tex, string Name) tile in new[] { (tall, "tall 64x256"), (wide, "wide 256x64"), (square, "square 128") })
			{
				foreach (int cells in new[] { 1, 2 })
				{
					foreach (float scale in new[] { 0.5f, 1f, 2f })
					{
						for (int m = 0; m < 4; m++)
						{
							bool mx = (m & 1) != 0;
							bool my = (m & 2) != 0;
							int visited = 0;
							int culledVisible = 0;
							int keptOffScreen = 0;
							string firstCulled = null;
							// Sweep scroll phases: position is wrapped into [0, realsize) by
							// Move, so these are the fractions a scrolling layer really passes
							// through.
							foreach (float phase in new[] { 0f, 0.13f, 0.5f, 0.87f })
							{
								List<BackgroundImage.TracedTile> log = DryRun(tile.Tex, cells, scale, mx, my, phase);
								foreach (BackgroundImage.TracedTile t in log)
								{
									visited++;
									if (Intersects(t.X, t.Y, t.W, t.H) && !t.Drawn)
									{
										culledVisible++;
										if (firstCulled == null)
										{
											firstCulled = "e.g. tile at (" + F(t.X) + "," + F(t.Y) + ") spanning " + F(t.W) + "x" + F(t.H);
										}
									}
									else if (t.Drawn && !Intersects(t.X, t.Y, t.W, t.H))
									{
										keptOffScreen++;
									}
								}
							}
							string label = tile.Name + " " + cells + "x" + cells + " x" + F(scale) + " mirrorX=" + (mx ? "1" : "0") + " mirrorY=" + (my ? "1" : "0");
							bool ok = culledVisible == 0 && keptOffScreen == 0 && visited > 0;
							Check(label, ok, ok ? visited + " tiles visited, cull exact" : culledVisible + " of " + visited + " VISIBLE tiles culled (" + keptOffScreen + " kept off-screen); " + firstCulled);
						}
					}
				}
			}
		}
		finally
		{
			tall.Dispose();
			wide.Dispose();
			square.Dispose();
		}

		// ---- 3. live layer census ----------------------------------------------------
		sb.Append("[bgcull] 3. live layer census (per frame, per layer)\n");
		Background live = FindLiveBackground(game);
		if (live == null)
		{
			sb.Append("  (no Background in the component list -- run this from a level, not the menu)\n");
		}
		else
		{
			CensusList(sb, "bg", live.CullTestBackgroundLayers);
			CensusList(sb, "fg", live.CullTestForegroundLayers);
		}

		// ---- 4. the live layers, swept across every scroll phase ----------------------
		// The card's "check nothing pops at a screen edge across a scroll phase sweep" -- as data,
		// and over ALL phases rather than the one a ?bgfreeze screenshot happens to freeze. Each
		// live layer is walked through the real Draw at a grid of phases and the tightened cull is
		// diffed against the pre-card one; a flipped decision on a tile with real area is a pixel
		// that would have popped.
		sb.Append("[bgcull] 4. live layers, cull differential across scroll phases\n");
		if (live == null)
		{
			sb.Append("  (no Background -- run this from a level, not the menu)\n");
		}
		else
		{
			int sweptTiles = 0;
			int sweptFlipped = 0;
			int sweptWithArea = 0;
			int layersNeverAtBoundary = 0;
			string firstSweptFlipped = null;
			string firstVacuousLayer = null;
			void Sweep(string which, IReadOnlyList<BackgroundImage> layers)
			{
				for (int i = 0; i < layers.Count; i++)
				{
					BackgroundImage layer = layers[i];
					string label = which + " " + i + " " + (layer.texturenames != null && layer.texturenames.GetLength(0) > 0 ? layer.texturenames[0, 0] : "?");
					int flippedHere = 0;
					// Leave no trace: the live scroll position is restored exactly, and a dry run
					// is already barred from advancing the layer's switchTimer.
					Vector2 saved = layer.position;
					try
					{
						foreach (float px in Phases)
						{
							foreach (float py in Phases)
							{
								layer.position = new Vector2(layer.realsize.X * px, layer.realsize.Y * py);
								foreach (BackgroundImage.TracedTile t in Capture(layer))
								{
									sweptTiles++;
									if (KeptByOldCull(t.X, t.Y, t.W, t.H) == t.Drawn)
									{
										continue;
									}
									sweptFlipped++;
									flippedHere++;
									if (!TouchesOriginEdgeExactly(t.X, t.Y, t.W, t.H))
									{
										sweptWithArea++;
										if (firstSweptFlipped == null)
										{
											// Name the layer AND the phase: a failure here is a pixel
											// popping somewhere in a live background, and the position
											// alone does not say where to go looking.
											firstSweptFlipped = label + " at phase (" + F(px) + "," + F(py) + "): tile at (" + F(t.X) + "," + F(t.Y) + ") spanning " + F(t.W) + "x" + F(t.H);
										}
									}
								}
							}
						}
					}
					finally
					{
						layer.position = saved;
					}
					// Per-layer coverage, REPORTED not asserted -- a layer that never reaches the
					// boundary contributes nothing to the assertion, and the total would otherwise
					// hide that behind the layers that did. It is NOT a failure: whether the tie is
					// reachable at all is a property of the layer's float geometry. The Mars ground
					// is the standing example -- 12 different-width tiles at size 1/3.238, so its
					// accumulated widths never land exactly on realsize.X, and on Y its band misses
					// too (971/3.238 = 299.876, not the 300 it sits at). Its census correctly reads
					// `drawn 3 (pre-ef55b76e 3)`: the tightening does not affect it either way.
					if (flippedHere == 0)
					{
						layersNeverAtBoundary++;
						if (firstVacuousLayer == null)
						{
							firstVacuousLayer = label;
						}
					}
				}
			}
			Sweep("bg", live.CullTestBackgroundLayers);
			Sweep("fg", live.CullTestForegroundLayers);
			sb.Append("  info  ").Append(sweptTiles).Append(" tile visits over ").Append(Phases.Length * Phases.Length)
				.Append(" phases per layer; ").Append(sweptFlipped).Append(" decisions differ from the pre-ef55b76e cull");
			if (layersNeverAtBoundary != 0)
			{
				sb.Append("; ").Append(layersNeverAtBoundary).Append(" layer(s) never reach the boundary at any phase (first: ")
					.Append(firstVacuousLayer).Append(") -- unaffected by the tightening, see the comment above");
			}
			sb.Append('\n');
			// The assertion is global: no flipped tile may have had area. Its positive control is
			// that SOME layer reached the boundary -- a run where none did compared nothing at all
			// and must not report PASS.
			Check("phase sweep: no flipped tile had on-screen area",
				sweptWithArea == 0 && sweptFlipped > 0,
				sweptWithArea != 0
					? sweptWithArea + " of " + sweptFlipped + " would have POPPED; " + firstSweptFlipped
					: sweptFlipped == 0
						? "VACUOUS: no layer reached the boundary at any phase -- nothing was compared"
						: sweptFlipped + " flipped, all zero-area -- the tightening cannot change a pixel at any phase");
		}

		sb.Append("PASS ").Append(pass).Append("  FAIL ").Append(fail).Append('\n');
		return sb.ToString();
	}

	// Walk one layer through its REAL Draw with the trace armed and every graphics call
	// suppressed, and hand back what the cull decided.
	private static List<BackgroundImage.TracedTile> DryRun(Texture2D tile, int cells, float scale, bool mirrorX, bool mirrorY, float phase)
	{
		BackgroundImage layer = new BackgroundImage();
		layer.textures = new Texture2D[cells, cells];
		layer.texturenames = new string[cells, cells];
		for (int i = 0; i < cells; i++)
		{
			for (int j = 0; j < cells; j++)
			{
				layer.textures[i, j] = tile;
				layer.texturenames[i, j] = "scenario";
			}
		}
		layer.size = scale;
		layer.mirrorX = mirrorX;
		layer.mirrorY = mirrorY;
		// A mirrored layer repeats over BOTH halves, which is how a real one would be set up
		// (the old marsloop code did exactly this before the strip became self-closing).
		layer.realsize = new Vector2((float)tile.LogicalWidth() * scale * (float)cells * (mirrorX ? 2f : 1f), (float)tile.LogicalHeight() * scale * (float)cells * (mirrorY ? 2f : 1f));
		layer.position = new Vector2(layer.realsize.X * phase, layer.realsize.Y * phase);
		return Capture(layer);
	}

	private static List<BackgroundImage.TracedTile> Capture(BackgroundImage layer)
	{
		List<BackgroundImage.TracedTile> log = new List<BackgroundImage.TracedTile>();
		BackgroundImage.CullTraceLog = log;
		BackgroundImage.CullTraceDryRun = true;
		try
		{
			layer.Draw(null, null);
		}
		finally
		{
			BackgroundImage.CullTraceLog = null;
			BackgroundImage.CullTraceDryRun = false;
		}
		return log;
	}

	private static void CensusList(StringBuilder sb, string which, IReadOnlyList<BackgroundImage> layers)
	{
		for (int i = 0; i < layers.Count; i++)
		{
			BackgroundImage layer = layers[i];
			List<BackgroundImage.TracedTile> log = Capture(layer);
			int drawn = 0;
			int wasted = 0;
			int drawnByOldCull = 0;
			foreach (BackgroundImage.TracedTile t in log)
			{
				// The pre-ef55b76e count is carried alongside the live one so the size of that
				// card's win stays a REPRODUCIBLE number. Without it "drawn 108 (was 130)" could
				// only ever be quoted from a build that no longer exists.
				if (KeptByOldCull(t.X, t.Y, t.W, t.H))
				{
					drawnByOldCull++;
				}
				if (t.Drawn)
				{
					drawn++;
					if (!Intersects(t.X, t.Y, t.W, t.H))
					{
						wasted++;
					}
				}
			}
			string name = layer.texturenames != null && layer.texturenames.GetLength(0) > 0 ? layer.texturenames[0, 0] : "?";
			sb.Append("  ").Append(which).Append(' ').Append(i).Append("  ")
				.Append(name)
				.Append("  size ").Append(F(layer.size))
				.Append("  visited ").Append(log.Count)
				.Append("  drawn ").Append(drawn)
				.Append(" (pre-ef55b76e ").Append(drawnByOldCull).Append(')')
				.Append("  of which off-screen ").Append(wasted);
			// Mid-crossfade (the 5s window after SetAlienBase2..6) Draw runs the outgoing AND
			// incoming pass, so these counts read ~2x steady state under a name that only
			// covers the outgoing texture. Say so rather than let it read as a regression.
			if (layer.switchTimer.Active)
			{
				sb.Append("   [CROSSFADING -> ").Append(layer.new_texturenames != null ? layer.new_texturenames[0, 0] : "?").Append(", counts cover both passes]");
			}
			sb.Append('\n');
		}
	}

	private static Background FindLiveBackground(Game game)
	{
		Collection<IGameComponent> components = (Collection<IGameComponent>)(object)game.Components;
		foreach (IGameComponent c in components)
		{
			if (c is Background bg)
			{
				return bg;
			}
		}
		return null;
	}
}
