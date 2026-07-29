using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace AiWallNavBench;

// Headless bench for the AI's WALL NAVIGATION (PlayerShip.SteerThroughWall and friends).
//
// Everything under test is the REAL game code, reached by reflection into the built
// EvilAliensWeb.dll: SteerThroughWall / ChooseGapColumn / ColumnScore / DistanceToBlockedRow /
// ClampIntoWallSpace, the real CollisionLevelMap, and the real Wall.Setup grids. There is no
// mirror of the algorithm anywhere in this file, so there is nothing that can drift from the
// shipped behaviour -- which is what a tools/sim python mirror could not have promised.
//
// Why it exists: card f4d1721f tuned the wall navigation against Level 3's grids with only the
// in-browser ?aibench to measure by, so OwnLevel's grid (Walls(game, 2)) was never in that loop.
// Card b4972696 asked why OwnLevel churns more, and answering it needed an instrument that could
// A/B a grid without booting the game. See tools/CLAUDE.md.
//
// WHAT IT CANNOT DO -- read this before quoting a number from it:
//   * It drives the WALL TERM ONLY. The live `turn deg/s` / `revs/s` in ?aibench are the heading
//     of the WHOLE steering sum (threats, seek, screen edges, the adaptive low-pass) and this
//     bench models none of that. It cannot produce those figures and must never be cited as if
//     it had. Its metrics are about the wall term's own decisions.
//   * The ship model is deliberately crude: full-speed motion along the steer angle, no
//     acceleration ramp and no smoothing. That makes it MORE responsive than the real ship, so
//     treat its contact counts as an optimistic floor, not a prediction.
internal static class Program
{
    private const float Dt = 1000f / 60f;
    private const float ShipMaxSpeed = 0.33f;   // PlayerShip.Setup
    private const float ShipHalf = 14.5f;       // the ~29px player box
    private const float StartY = 480f;

    // EVERY real wall section in both levels scrolls at Background.SetSpeed(4.3 * difficultyValue
    // / 16.667) -- Level3.speedup and OwnLevel.setspeed are the same expression -- so the only
    // speeds a player ever flies a wall at are this ladder, one per difficulty tier. (The 0.43
    // variant is Level3.popTestSlow, reached only under ?wallpoptest and documented there as "10%
    // of the normal wall-section speed"; benching it would weight the table with a speed nothing
    // in play uses.) Default is the Very_Hard rung, which is what the AI matrix is measured at.
    private static readonly (float Scroll, string Tier)[] SpeedLadder =
    {
        (4.3f * 0.35f / 16.666666f, "Easy"),
        (4.3f * 0.60f / 16.666666f, "Medium"),
        (4.3f * 0.80f / 16.666666f, "Hard"),
        (4.3f * 1.00f / 16.666666f, "Very_Hard"),
        (4.3f * 1.20f / 16.666666f, "Inzane"),
    };

    private const int VeryHardRung = 3;

    // Level 3 flies 0, 1, 3 and 4 (and 1/0/3 under ?wallsonly). OwnLevel flies 2 and nothing else.
    private static readonly (int Variation, string Owner)[] Grids =
    {
        (0, "Level3"), (1, "Level3"), (2, "OwnLevel"), (3, "Level3"), (4, "Level3"),
    };

    private static Assembly asm;
    private static Type tShip, tWall, tOracle;
    private static MethodInfo miSteer, miClamp;
    private static FieldInfo fiGapCol, fiPos, fiMaxSpeed, fiBoundBox, fiOracle, fiPlayer, fiTL, fiBR, fiObsVel, fiPlayers;

    private static int Main(string[] args)
    {
        float? react = null;
        int? scanRows = null;
        float? crossPenalty = null;
        bool ladder = false;
        var only = new List<int>();
        foreach (string a in args)
        {
            if (a.StartsWith("--react="))
            {
                if (!TryNum(a, out float v)) { Console.Error.WriteLine("not a number: " + a); Usage(); return 2; }
                react = v;
            }
            else if (a.StartsWith("--scanrows="))
            {
                // INT, like ?aiscanrows= -- it counts grid rows, so refusing `4.7` here keeps a
                // mislabelled sweep (one that silently benched the default depth) impossible.
                if (!TryInt(a, out int n)) { Console.Error.WriteLine("not a whole number of rows: " + a); Usage(); return 2; }
                scanRows = n;
            }
            else if (a.StartsWith("--crosspenalty="))
            {
                if (!TryNum(a, out float v)) { Console.Error.WriteLine("not a number: " + a); Usage(); return 2; }
                crossPenalty = v;
            }
            else if (a.StartsWith("--grid="))
            {
                if (!TryNum(a, out float v)) { Console.Error.WriteLine("not a number: " + a); Usage(); return 2; }
                only.Add((int)v);
            }
            else if (a == "--ladder") ladder = true;
            else if (a == "--help" || a == "-h") { Usage(); return 0; }
            else { Console.Error.WriteLine("unknown argument: " + a); Usage(); return 2; }
        }

        foreach (int v in only)
        {
            if (Array.Exists(Grids, g => g.Variation == v)) continue;
            // Silently printing an empty table would read as "this grid is clean".
            Console.Error.WriteLine("no such wall variation: " + v + " (have 0, 1, 2, 3, 4)");
            return 2;
        }

        try { asm = LoadGameAssembly(); }
        catch (Exception e)
        {
            Console.Error.WriteLine("could not load EvilAliensWeb.dll -- build the game first:");
            Console.Error.WriteLine("  dotnet build web/EvilAliensWeb -c Debug");
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        if (!Bind()) return 1;

        // Very_Hard: Wall.Setup halves every grid at Easy/Medium, and the card's figures are all
        // Very_Hard, so anything else would bench half-length walls nobody flies.
        Type tSettings = asm.GetType("EvilAliens.Settings");
        object settings = tSettings.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        tSettings.GetField("_difficultyLevel", BindingFlags.NonPublic | BindingFlags.Instance)
                 .SetValue(settings, Enum.Parse(tSettings.GetNestedType("DifficultyLevel"), "Very_Hard"));

        // Applied by handing the REAL DebugFlags.Parse a synthesized query -- the same code path
        // the URL flags take, so the clamps and the reject-out-of-range rules are the shipped ones
        // and not a second copy that can drift. Writing the properties directly (as this did
        // originally) skips every one of those guards: --scanrows=-3 was accepted, benched
        // identically to 0, and printed "-3" in the header, which is precisely the mislabelled run
        // this tool exists to make impossible.
        var query = new List<string>();
        if (react.HasValue) query.Add("aireact=" + react.Value.ToString(CultureInfo.InvariantCulture));
        if (scanRows.HasValue) query.Add("aiscanrows=" + scanRows.Value.ToString(CultureInfo.InvariantCulture));
        if (crossPenalty.HasValue) query.Add("aicrosspenalty=" + crossPenalty.Value.ToString(CultureInfo.InvariantCulture));
        if (query.Count > 0)
        {
            MethodInfo parse = asm.GetType("EvilAliensWeb.Compat.DebugFlags")
                .GetMethod("Parse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            TextWriter savedOut = Console.Out;
            Console.SetOut(TextWriter.Null);
            try { parse.Invoke(null, new object[] { "?" + string.Join("&", query) }); }
            finally { Console.SetOut(savedOut); }
        }

        // Read the values back OUT of PlayerShip and report those, never the ones asked for. The
        // resolving properties are the arbiter: anything Parse refused or clamped shows up here as
        // the value actually in force, so the header can no longer disagree with the table under
        // it. A run is labelled with what it benched or it is not labelled at all.
        string inForce = ""
            + (react.HasValue ? ", WallReactionMs=" + Resolved("WallReactionMs") : "")
            + (scanRows.HasValue ? ", WallScanRows=" + Resolved("WallScanRows") : "")
            + (crossPenalty.HasValue ? ", WallCrossPenalty=" + Resolved("WallCrossPenalty") : "");
        if (scanRows.HasValue && Resolved("WallScanRows") != scanRows.Value.ToString(CultureInfo.InvariantCulture))
        {
            Console.Error.WriteLine("--scanrows=" + scanRows.Value + " was refused or clamped by DebugFlags"
                + " -- benching " + Resolved("WallScanRows") + " rows instead");
        }
        if (crossPenalty.HasValue && Resolved("WallCrossPenalty") != crossPenalty.Value.ToString(CultureInfo.InvariantCulture))
        {
            Console.Error.WriteLine("--crosspenalty=" + crossPenalty.Value.ToString(CultureInfo.InvariantCulture)
                + " was refused or clamped by DebugFlags -- benching " + Resolved("WallCrossPenalty") + " instead");
        }

        // Grids are extracted BEFORE the table starts printing: Wall.Setup drives KNI's
        // TitleContainer, whose loader writes three lines to stdout, and mid-table that noise
        // travels with any copy-pasted result.
        var extracted = new List<(string Label, bool[,] Grid)>();
        TextWriter stdout = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            foreach (var (variation, owner) in Grids)
            {
                if (only.Count > 0 && !only.Contains(variation)) continue;
                bool[,] grid = GridForVariation(variation);
                if (grid == null)
                {
                    Console.SetOut(stdout);
                    Console.Error.WriteLine("var" + variation + ": could not extract grid");
                    return 1;
                }
                extracted.Add(("var" + variation + " (" + owner + ")", grid));
            }
        }
        finally { Console.SetOut(stdout); }

        Console.WriteLine();
        Console.WriteLine("ai wall-nav bench -- real PlayerShip code, grid difficulty=Very_Hard, ship box="
            + (ShipHalf * 2).ToString(CultureInfo.InvariantCulture) + "px"
            + inForce);

        foreach (int rung in ladder ? new[] { 0, 1, 2, 3, 4 } : new[] { VeryHardRung })
        {
            var (scroll, tier) = SpeedLadder[rung];
            Console.WriteLine();
            Console.WriteLine("scroll " + scroll.ToString("0.000", CultureInfo.InvariantCulture)
                + " px/ms (4.3 * " + tier + ")");
            Console.WriteLine("grid                w  rows |  secs | gapSw/s latFlip/s clampX/s clampUp/s | contact/s  n | urgency%");
            Console.WriteLine("-------------------------------------------------------------------------------------------------------");
            foreach (var (label, grid) in extracted) Run(label, grid, scroll);
        }

        Console.WriteLine();
        Console.WriteLine("gapSw/s    ChooseGapColumn changed its committed column");
        Console.WriteLine("latFlip/s  SteerThroughWall's lateral push changed sign");
        Console.WriteLine("clampX/s   ClampIntoWallSpace reversed X (its two horizontal probes)");
        Console.WriteLine("clampUp/s  ClampIntoWallSpace forced Y down -- the ungated upward probe, 3x the reach");
        Console.WriteLine("contact/s  ticks starting inside a blocked tile; n is the raw count. Grids differ ~2x in");
        Console.WriteLine("           length, so only the per-second figure is comparable ACROSS rows.");
        Console.WriteLine("urgency%   ticks with a blocked row inside the look-ahead reach");
        Console.WriteLine();
        Console.WriteLine("Level 3 flies variations 0/1/3/4 (1/0/3 under ?wallsonly). OwnLevel flies 2 only.");
        Console.WriteLine("--ladder repeats the table at all five difficulty scroll speeds.");
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("usage: dotnet run --project tools/sim/aiwallnav [--react=<ms>] [--scanrows=<n>]");
        Console.WriteLine("                                               [--crosspenalty=<c>] [--grid=<n>] [--ladder]");
        Console.WriteLine("  --react=<ms>        set PlayerShip's WallReactionMs   (DebugFlags property ?aireact writes)");
        Console.WriteLine("  --scanrows=<n>      set PlayerShip's WallScanRows     (?aiscanrows -- a whole number of rows)");
        Console.WriteLine("  --crosspenalty=<c>  set PlayerShip's WallCrossPenalty (?aicrosspenalty)");
        Console.WriteLine("  --grid=<n>          bench only wall variation n (repeatable)");
        Console.WriteLine("  --ladder            repeat the table at all five difficulty scroll speeds");
        Console.WriteLine();
        Console.WriteLine("Build the game first: dotnet build web/EvilAliensWeb -c Debug");
    }

    private static bool TryNum(string arg, out float value) =>
        float.TryParse(arg.Substring(arg.IndexOf('=') + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryInt(string arg, out int value) =>
        int.TryParse(arg.Substring(arg.IndexOf('=') + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    // What PlayerShip's own resolving property returns right now -- i.e. the value the methods
    // under test will actually read, after DebugFlags has had its say. Private, like everything
    // else this bench reaches for.
    private static string Resolved(string property) =>
        Convert.ToString(
            asm.GetType("EvilAliens.PlayerShip")
               .GetProperty(property, BindingFlags.NonPublic | BindingFlags.Static)
               .GetValue(null),
            CultureInfo.InvariantCulture);

    // The typeof lives behind a non-inlined call so a missing//stale EvilAliensWeb.dll surfaces as
    // the caller's friendly "build the game first" rather than a JIT-time type-load failure while
    // Main itself is being prepared.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Assembly LoadGameAssembly() => typeof(EvilAliens.CollisionLevelMap).Assembly;

    private static bool Bind()
    {
        tShip = asm.GetType("EvilAliens.PlayerShip");
        tWall = asm.GetType("EvilAliens.Wall");
        tOracle = asm.GetType("EvilAliens.Oracle");

        const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags API = BindingFlags.Public | BindingFlags.Instance;
        miSteer = tShip.GetMethod("SteerThroughWall", NPI);
        miClamp = tShip.GetMethod("ClampIntoWallSpace", NPI);
        fiGapCol = tShip.GetField("aiGapColumn", NPI);
        fiBoundBox = tShip.GetField("boundBox", NPI);
        fiTL = tShip.GetField("TopLeft", API);
        fiBR = tShip.GetField("BottomRight", API);
        fiOracle = FindField(tShip, "oracle");
        fiPlayer = FindField(tShip, "player");
        fiPos = FindField(tShip, "_position");
        fiMaxSpeed = FindField(tShip, "_maximumSpeed");
        fiObsVel = FindField(tWall, "_observedVelocity");
        fiPlayers = tOracle.GetField("players", NPI);

        var required = new (object Member, string Name)[]
        {
            (miSteer, "SteerThroughWall"), (miClamp, "ClampIntoWallSpace"),
            (fiGapCol, "aiGapColumn"), (fiBoundBox, "boundBox"), (fiTL, "TopLeft"), (fiBR, "BottomRight"),
            (fiPos, "_position"), (fiMaxSpeed, "_maximumSpeed"), (fiObsVel, "_observedVelocity"),
            // oracle is required even though NewShip guards on it: SteerThroughWall dereferences
            // it unconditionally, so a rename would surface as a TargetInvocationException out of
            // Invoke rather than the clean bind error this guard promises.
            (fiOracle, "PlayerShip.oracle"), (fiPlayers, "Oracle.players"),
        };
        bool ok = true;
        foreach (var (member, name) in required)
        {
            if (member != null) continue;
            // A rename in the game is the expected cause, and a bench that silently benched
            // nothing would be far worse than one that refuses to start.
            Console.Error.WriteLine("could not bind " + name + " -- did it get renamed in PlayerShip/Wall/Oracle?");
            ok = false;
        }
        return ok;
    }

    private static FieldInfo FindField(Type t, string name)
    {
        for (Type c = t; c != null; c = c.BaseType)
        {
            FieldInfo f = c.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f != null) return f;
        }
        return null;
    }

    // Wall.Setup assigns `blocks` from its switch and only then touches texture/oracle/Settings,
    // which are browser-side -- so it throws here with the grid already in place. Run it and read
    // the field back rather than copying 700 lines of grid literals into this file.
    private static bool[,] GridForVariation(int variation)
    {
        object wall = RuntimeHelpers.GetUninitializedObject(tWall);
        MethodInfo setup = tWall.GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        FieldInfo blocks = tWall.GetField("blocks", BindingFlags.NonPublic | BindingFlags.Instance);
        try { setup.Invoke(wall, new object[] { variation }); }
        catch (Exception) { /* expected -- see above */ }
        bool[,] grid = (bool[,])blocks.GetValue(wall);

        // Variation 2 is the one OwnLevel flies, and it is the one Setup CANNOT give us: it reads
        // Content/levels/level3.txt through TitleContainer, which only exists in the browser, so
        // Setup lands in its own catch and hands back the hard-coded 5x19 emergency grid instead.
        // Benching that silently would answer a question nobody asked. Parse the file here --
        // identical to Setup's case 2, and it is rig plumbing, not the algorithm under test.
        if (variation == 2)
        {
            string path = FindLevelGrid("level3.txt");
            if (path == null)
            {
                Console.Error.WriteLine("var2: could not find Content/levels/level3.txt -- run from the repo root");
                return null;
            }
            string[] lines = File.ReadAllLines(path);
            int gw = int.Parse(lines[0].Substring(6), CultureInfo.InvariantCulture);
            var rows = new List<string>();
            for (int i = 1; i < lines.Length && !lines[i].Contains("end"); i++) rows.Add(lines[i]);
            var real = new bool[rows.Count, gw];
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < gw; j++)
                    real[i, j] = j < rows[i].Length && rows[i][j] != ' ';
            grid = real;
        }
        return grid;
    }

    private static string FindLevelGrid(string file)
    {
        string rel = Path.Combine("web", "EvilAliensWeb", "wwwroot", "Content", "levels", file);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, rel);
            if (File.Exists(candidate)) return candidate;
        }
        return File.Exists(rel) ? rel : null;
    }

    private static object NewShip()
    {
        // No ctor: PlayerShip's needs a Game, a GraphicsDevice and the whole service graph. The
        // wall-nav methods read only the handful of fields set below.
        object ship = RuntimeHelpers.GetUninitializedObject(tShip);
        fiGapCol.SetValue(ship, -1);
        fiBoundBox.SetValue(ship, new EvilAliens.CollisionBox(Vector2.Zero, Vector2.Zero));
        fiTL.SetValue(ship, new Vector2(-ShipHalf, -ShipHalf));
        fiBR.SetValue(ship, new Vector2(ShipHalf, ShipHalf));
        fiMaxSpeed.SetValue(ship, ShipMaxSpeed);
        fiPlayer?.SetValue(ship, 0);
        if (fiOracle != null)
        {
            // An empty roster reads as Players == 0, which keeps SteerThroughWall's co-op seat
            // spread out of the picture -- it only engages above one seated player.
            object oracle = RuntimeHelpers.GetUninitializedObject(tOracle);
            Type infoT = asm.GetType("EvilAliens.PlayerInfo");
            fiPlayers.SetValue(oracle, Activator.CreateInstance(typeof(List<>).MakeGenericType(infoT)));
            fiOracle.SetValue(ship, oracle);
        }
        return ship;
    }

    private static void Run(string label, bool[,] grid, float scroll)
    {
        int rows = grid.GetLength(0), width = grid.GetLength(1);
        float tile = 800f / width;

        int gapSw = 0, latFlip = 0, clampX = 0, clampUp = 0, contacts = 0, urgentTicks = 0, ticks = 0;
        float sec = 0f;

        {
            object ship = NewShip();
            object wall = RuntimeHelpers.GetUninitializedObject(tWall);
            fiObsVel.SetValue(wall, new Vector2(0f, scroll));

            float offsetY = -(rows + 2) * tile;
            var pos = new Vector2(400f, StartY);
            var map = new EvilAliens.CollisionLevelMap(new Vector2(0f, offsetY), grid);
            int prevCol = -1, prevLatSign = 0;

            while (offsetY < 600f)
            {
                map.SetOffset(new Vector2(0f, offsetY));
                fiPos.SetValue(ship, pos);

                int mx = 0, my = 0;
                map.GetMapCoords(ref mx, ref my, pos);
                if (my >= -4 && my < rows + 1)
                {
                    // Exactly how DoAIMove composes the wall term: SteerThroughWall adds into the
                    // running direction, then ClampIntoWallSpace overrides it last.
                    object[] steerArgs = { Vector2.Zero, wall, map };
                    miSteer.Invoke(ship, steerArgs);
                    var dir = (Vector2)steerArgs[0];

                    int col = (int)fiGapCol.GetValue(ship);
                    if (prevCol >= 0 && col != prevCol) gapSw++;
                    prevCol = col;

                    int latSign = Math.Sign(dir.X);
                    if (latSign != 0 && prevLatSign != 0 && latSign != prevLatSign) latFlip++;
                    if (latSign != 0) prevLatSign = latSign;
                    if (dir.Y > 0.01f) urgentTicks++;

                    object[] clampArgs = { dir, map };
                    miClamp.Invoke(ship, clampArgs);
                    var clamped = (Vector2)clampArgs[0];
                    if (clamped.X != 0f && Math.Sign(clamped.X) != Math.Sign(dir.X)) clampX++;
                    // The upward probe is ungated on direction and reaches 3x further, and by the
                    // game's own comment it is the more dangerous axis -- so it gets its own column
                    // rather than hiding inside a single "the clamp fired" number.
                    if (clamped.Y > dir.Y) clampUp++;
                    dir = clamped;

                    if (dir != Vector2.Zero)
                    {
                        dir.Normalize();
                        pos += dir * ShipMaxSpeed * Dt;
                    }
                    pos.X = MathHelper.Clamp(pos.X, ShipHalf, 800f - ShipHalf);
                    pos.Y = MathHelper.Clamp(pos.Y, ShipHalf, 600f - ShipHalf);

                    if (map.TileIsOccupied(mx, my))
                    {
                        // Touching a wall tile is instant death in this game. The respawn has to
                        // land in a CLEAR cell: respawning at a fixed point drops the ship back
                        // inside the same slab, so one death is counted as dozens and the metric
                        // stops responding to anything (it read a flat 226 across four look-ahead
                        // depths before this was fixed -- a rig artifact that looked like a result).
                        contacts++;
                        pos = new Vector2(400f, 500f);
                        for (int c = 0; c < map.Width; c++)
                        {
                            int rx = 0, ry = 0;
                            var cand = new Vector2(map.ColumnCentreX(c), 500f);
                            map.GetMapCoords(ref rx, ref ry, cand);
                            if (!map.TileIsOccupied(rx, ry)) { pos = cand; break; }
                        }
                        fiGapCol.SetValue(ship, -1);
                        prevCol = -1;
                        prevLatSign = 0;
                    }

                    ticks++;
                    sec += Dt / 1000f;
                }
                offsetY += scroll * Dt;
            }
        }

        float s = Math.Max(sec, 0.001f);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-18} {1,2} {2,5} | {3,5:0.0} | {4,7:0.00} {5,9:0.00} {6,8:0.00} {7,9:0.00} | {8,9:0.00} {9,2} | {10,7:0.0}%",
            label, width, rows, sec, gapSw / s, latFlip / s, clampX / s, clampUp / s,
            contacts / s, contacts, ticks > 0 ? 100f * urgentTicks / ticks : 0f));
    }
}
