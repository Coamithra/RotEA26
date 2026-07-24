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

    // Both Level 3 and OwnLevel drive Background.SetSpeed(4.3 * difficultyValue / 16.667), which
    // is 0.258 px/ms at Very_Hard; Level 3's earlier sections run the 0.43 variant, ten times
    // slower. Sweeping the range rather than picking one is what stops a scroll speed being
    // cherry-picked -- OwnLevel's grid takes every one of its wall contacts at 0.258 and above.
    private static readonly float[] Scrolls = { 0.026f, 0.10f, 0.18f, 0.258f, 0.31f };

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
        var only = new List<int>();
        foreach (string a in args)
        {
            if (a.StartsWith("--react=")) react = Num(a);
            else if (a.StartsWith("--grid=")) only.Add((int)Num(a));
            else if (a == "--help" || a == "-h") { Usage(); return 0; }
            else { Console.Error.WriteLine("unknown argument: " + a); Usage(); return 2; }
        }

        try { asm = typeof(EvilAliens.CollisionLevelMap).Assembly; }
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

        if (react.HasValue)
        {
            // Through the REAL ?aireact knob, so the override path being exercised is the shipped
            // one rather than a second copy of the resolution rule.
            asm.GetType("EvilAliensWeb.Compat.DebugFlags")
               .GetProperty("AiWallReactionMs", BindingFlags.Public | BindingFlags.Static)
               .SetValue(null, react);
        }

        Console.WriteLine("ai wall-nav bench -- real PlayerShip code, difficulty=Very_Hard, ship box="
            + (ShipHalf * 2).ToString(CultureInfo.InvariantCulture) + "px"
            + (react.HasValue ? ", WallReactionMs=" + react.Value.ToString(CultureInfo.InvariantCulture) : ""));
        Console.WriteLine("scroll sweep: " + string.Join(", ", Array.ConvertAll(Scrolls, f => f.ToString("0.000", CultureInfo.InvariantCulture))) + " px/ms");
        Console.WriteLine();
        Console.WriteLine("grid                w  rows | gapSw/s  latFlip/s  clampX/s  contacts | urgency%");
        Console.WriteLine("--------------------------------------------------------------------------------");

        foreach (var (variation, owner) in Grids)
        {
            if (only.Count > 0 && !only.Contains(variation)) continue;
            bool[,] grid = GridForVariation(variation);
            if (grid == null) { Console.WriteLine("var" + variation + ": could not extract grid"); continue; }
            Run("var" + variation + " (" + owner + ")", grid);
        }

        Console.WriteLine();
        Console.WriteLine("gapSw/s   ChooseGapColumn changed its committed column");
        Console.WriteLine("latFlip/s SteerThroughWall's lateral push changed sign");
        Console.WriteLine("clampX/s  ClampIntoWallSpace reversed X -- the hard override, written back into aiSteer");
        Console.WriteLine("contacts  ticks starting inside a blocked tile (the ship respawns in a clear cell)");
        Console.WriteLine("urgency%  ticks with a blocked row inside the look-ahead reach");
        Console.WriteLine();
        Console.WriteLine("Level 3 flies variations 0/1/3/4 (1/0/3 under ?wallsonly). OwnLevel flies 2 only.");
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("usage: dotnet run --project tools/sim/aiwallnav [--react=<ms>] [--grid=<n>]");
        Console.WriteLine("  --react=<ms>  override PlayerShip's WallReactionMs (the real ?aireact knob)");
        Console.WriteLine("  --grid=<n>    bench only wall variation n (repeatable)");
        Console.WriteLine();
        Console.WriteLine("Build the game first: dotnet build web/EvilAliensWeb -c Debug");
    }

    private static float Num(string arg) =>
        float.Parse(arg.Substring(arg.IndexOf('=') + 1), CultureInfo.InvariantCulture);

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
            (fiPlayers, "Oracle.players"),
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

    private static void Run(string label, bool[,] grid)
    {
        int rows = grid.GetLength(0), width = grid.GetLength(1);
        float tile = 800f / width;

        int gapSw = 0, latFlip = 0, clampX = 0, contacts = 0, urgentTicks = 0, ticks = 0;
        float sec = 0f;

        foreach (float scroll in Scrolls)
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
            "{0,-18} {1,2} {2,5} | {3,7:0.00} {4,10:0.00} {5,9:0.00} {6,9} | {7,7:0.0}%",
            label, width, rows, gapSw / s, latFlip / s, clampX / s, contacts,
            ticks > 0 ? 100f * urgentTicks / ticks : 0f));
    }
}
