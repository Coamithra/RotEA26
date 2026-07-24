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
// Three parts, because the two defects fail in different places:
//  1. the predicate itself, swept over shapes/scales/positions -- catches the vertical term
//     measuring the tile's WIDTH (a TALL tile is culled while visible);
//  2. whole scenario layers walked through the real Draw -- catches the missing * size, which
//     is a CALL-SITE defect in the mirrorX blocks and so invisible to part 1;
//  3. a census of the live layers -- the integration evidence, and where the Mars ground's
//     wasted off-screen row shows up as a number.
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

	// The one case where keeping a zero-area tile is legitimate: it lands exactly on the top
	// or left edge, which the cull's `>= 0` admits by design (tightening that to `> 0` is its
	// own change -- see the plan's Out of scope).
	private static bool TouchesEdgeExactly(float x, float y, float w, float h)
	{
		return x + w >= 0f && x < ScreenW && y + h >= 0f && y < ScreenH && (x + w == 0f || y + h == 0f);
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
		int slack = 0;
		foreach ((int W, int H, string Name) shape in Shapes)
		{
			foreach (float scale in Scales)
			{
				float w = (float)shape.W * scale;
				float h = (float)shape.H * scale;
				int here = 0;
				int culledVisible = 0;
				int keptOffScreen = 0;
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
							slack++;
							if (!TouchesEdgeExactly(x, y, w, h))
							{
								keptOffScreen++;
								if (firstKept == null)
								{
									firstKept = "e.g. at (" + F(x) + "," + F(y) + ") the tile covers x " + F(x) + ".." + F(x + w) + " y " + F(y) + ".." + F(y + h);
								}
							}
						}
					}
				}
				string what = shape.Name + " x" + F(scale);
				// Soundness: never drop a tile the player would have seen. This is the property
				// the old expression violated.
				Check("sound  " + what, culledVisible == 0 && here > 0, culledVisible == 0 ? null : culledVisible + " VISIBLE tiles culled; " + firstCulled);
				// Tightness: and never keep one that is wholly off screen. Without this a cull
				// that simply returned true -- i.e. no cull at all -- would pass the suite.
				Check("tight  " + what, keptOffScreen == 0 && here > 0, keptOffScreen == 0 ? null : keptOffScreen + " tiles kept with no on-screen area; " + firstKept);
			}
		}
		sb.Append("  info  ").Append(cases).Append(" cases; ").Append(slack)
			.Append(" kept with no on-screen area (the >= 0 touching case -- deliberate slack, out of scope)\n");

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
									else if (t.Drawn && !Intersects(t.X, t.Y, t.W, t.H) && !TouchesEdgeExactly(t.X, t.Y, t.W, t.H))
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
			foreach (BackgroundImage.TracedTile t in log)
			{
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
