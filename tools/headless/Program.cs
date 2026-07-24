// ---------------------------------------------------------------------------
// eahl — Evil Aliens HeadLess. Entry point + the agent-facing command surface.
//
// Two ways to drive it:
//
//   ONE-SHOT   eahl --flags "?level=Level1&invuln" --frames 300 --out shot.png
//              Boot, run N frames, write a PNG, exit. Everything an agent needs for
//              "what does X look like" in a single background command.
//
//   REPL       eahl --repl   (or --script <file>)
//              Line protocol on stdin/stdout: step / shot / eval / info / quit. Boot
//              cost is paid ONCE and then any number of frames can be stepped and any
//              number of screenshots taken -- which is the difference between probing a
//              sequence interactively and re-booting the game per frame.
//
// Every reply is a single line starting `ok ` or `err `, so a driver never has to guess
// whether a command finished. Diagnostics go out as `[eahl] ...` / `[debug] ...` lines
// and are never confused with a reply.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using EvilAliensWeb.Compat;

namespace EvilAliensWeb.Headless
{
    internal sealed class Options
    {
        internal string Flags = "";
        internal int Frames = 1;
        internal int Width = 800;
        internal int Height = 600;
        internal double Fps = 60.0;
        internal string OutPath;
        internal readonly List<int> ShotAt = new List<int>();
        internal string ContentDir;
        internal string SaveDir;
        internal bool WipeSaves = true;
        internal bool Repl;
        internal string ScriptPath;
        internal bool Software;
        internal string MesaPath;
        internal bool Verbose;
        internal bool JsCalls;
        internal bool NoDraw;
        internal bool Present;
        internal bool Audio;
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Options opt;
            try
            {
                if (!TryParseArgs(args, out opt))
                {
                    Usage();
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("err bad arguments: " + ex.Message);
                Usage();
                return 2;
            }

            // Silent by default: a background soak must not play the game's SFX through
            // the user's speakers, and a box with no sound card must not fail in audio
            // init. Before the Game is constructed -- the device is opened during boot.
            if (!opt.Audio)
                HeadlessAudio.Silence();

            // Must happen before the graphics device exists (SDL loads GL lazily).
            if (opt.Software)
            {
                string why = SoftwareGl.Apply(opt.MesaPath);
                if (why != null)
                {
                    // --software was asked for explicitly; silently using the GPU instead
                    // would make a "works on my machine" result meaningless.
                    Console.Error.WriteLine("err --software requested but unavailable: " + why);
                    return 3;
                }
            }

            try
            {
                using (var host = new HeadlessHost(opt))
                {
                    host.Boot();
                    int rc = opt.Repl || opt.ScriptPath != null ? RunCommands(host, opt) : RunOneShot(host, opt);
                    if (opt.JsCalls)
                        host.DumpJsCalls();
                    return rc;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("err " + ex.GetType().Name + ": " + ex.Message);
                if (opt.Verbose)
                    Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        // ---- one-shot ------------------------------------------------------------------

        private static int RunOneShot(HeadlessHost host, Options opt)
        {
            var shots = new List<int>(opt.ShotAt);
            shots.Sort();
            int next = 0;

            for (int i = 1; i <= opt.Frames; i++)
            {
                host.Step(1, !opt.NoDraw);
                while (next < shots.Count && shots[next] == i)
                {
                    Console.WriteLine("ok shot " + host.Shot(NumberedPath(opt.OutPath, i)));
                    next++;
                }
            }

            // --out with no --shot-at means "the final frame", the common case.
            if (opt.OutPath != null && shots.Count == 0)
                Console.WriteLine("ok shot " + host.Shot(opt.OutPath));

            Console.WriteLine("ok " + host.Info());
            return 0;
        }

        // shots/x.png + frame 120 -> shots/x_0120.png, so a multi-shot run sorts in order.
        private static string NumberedPath(string path, int frame)
        {
            path = path ?? "out.png";
            string dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            string name = stem + "_" + frame.ToString("0000", CultureInfo.InvariantCulture) + ext;
            return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
        }

        // ---- repl ----------------------------------------------------------------------

        private static int RunCommands(HeadlessHost host, Options opt)
        {
            TextReader input = opt.ScriptPath != null ? new StreamReader(opt.ScriptPath) : Console.In;
            try
            {
                Console.WriteLine("ok ready " + host.Info());
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    if (!Execute(host, line, out bool quit))
                        if (opt.ScriptPath != null)
                            return 1; // a script is a test: the first failure fails the run
                    if (quit)
                        break;
                }
            }
            finally
            {
                if (opt.ScriptPath != null) input.Dispose();
            }
            return 0;
        }

        // Returns false if the command failed. `quit` asks the loop to stop.
        private static bool Execute(HeadlessHost host, string line, out bool quit)
        {
            quit = false;
            string[] parts = Split(line);
            string cmd = parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "step":
                    {
                        // step [n] [nodraw]  -- nodraw skips rendering; much faster, and the
                        // right thing for behaviour soaks where no pixels are wanted.
                        int n = parts.Length > 1 && parts[1] != "nodraw" ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                        bool draw = Array.IndexOf(parts, "nodraw") < 0;
                        host.Step(n, draw);
                        Console.WriteLine("ok step " + host.Info());
                        return true;
                    }
                    case "shot":
                    {
                        if (parts.Length < 2) throw new ArgumentException("usage: shot <path.png>");
                        Console.WriteLine("ok shot " + host.Shot(parts[1]));
                        return true;
                    }
                    case "eval":
                    {
                        if (parts.Length < 2) throw new ArgumentException("usage: eval <DebugInput method> [args...]");
                        string res = DebugBridge.Invoke(parts, 1);
                        Console.WriteLine("ok eval " + (res ?? ""));
                        return true;
                    }
                    case "info":
                        Console.WriteLine("ok " + host.Info());
                        return true;
                    case "help":
                        Console.WriteLine("ok " + DebugBridge.List());
                        return true;
                    case "quit":
                    case "exit":
                        quit = true;
                        Console.WriteLine("ok bye");
                        return true;
                    default:
                        throw new ArgumentException("unknown command '" + cmd + "' (try: step, shot, eval, info, help, quit)");
                }
            }
            catch (Exception ex)
            {
                // Never let a bad command kill the session -- an agent should be able to
                // recover by sending a corrected one.
                Console.WriteLine("err " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // Whitespace split honouring "quoted runs", so `eval Press "left" 30` works.
        private static string[] Split(string line)
        {
            var outp = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            foreach (char c in line)
            {
                if (c == '"') { q = !q; continue; }
                if (!q && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) { outp.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) outp.Add(sb.ToString());
            if (outp.Count == 0) outp.Add("");
            return outp.ToArray();
        }

        // ---- args ----------------------------------------------------------------------

        private static bool TryParseArgs(string[] args, out Options opt)
        {
            opt = new Options();
            if (args.Length == 0)
                return false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--flags": opt.Flags = Next(args, ref i); break;
                    case "--frames": opt.Frames = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--fps": opt.Fps = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--out": opt.OutPath = Next(args, ref i); break;
                    case "--content": opt.ContentDir = Next(args, ref i); break;
                    case "--saves": opt.SaveDir = Next(args, ref i); opt.WipeSaves = false; break;
                    case "--repl": opt.Repl = true; break;
                    case "--script": opt.ScriptPath = Next(args, ref i); break;
                    case "--software": opt.Software = true; break;
                    case "--mesa": opt.MesaPath = Next(args, ref i); opt.Software = true; break;
                    case "--verbose": opt.Verbose = true; break;
                    case "--jscalls": opt.JsCalls = true; break;
                    case "--nodraw": opt.NoDraw = true; break;
                    case "--present": opt.Present = true; break;
                    case "--audio": opt.Audio = true; break;
                    case "--help":
                    case "-h":
                        return false;
                    case "--size":
                    {
                        string[] wh = Next(args, ref i).Split('x', 'X');
                        if (wh.Length != 2) throw new ArgumentException("--size wants WxH, e.g. 1600x1200");
                        opt.Width = int.Parse(wh[0], CultureInfo.InvariantCulture);
                        opt.Height = int.Parse(wh[1], CultureInfo.InvariantCulture);
                        break;
                    }
                    case "--shot-at":
                        foreach (string s in Next(args, ref i).Split(','))
                            if (s.Trim().Length > 0)
                                opt.ShotAt.Add(int.Parse(s.Trim(), CultureInfo.InvariantCulture));
                        break;
                    default:
                        throw new ArgumentException("unknown option '" + a + "'");
                }
            }

            if (opt.ShotAt.Count > 0)
            {
                int last = 0;
                foreach (int f in opt.ShotAt) if (f > last) last = f;
                if (last > opt.Frames) opt.Frames = last;   // run far enough to take them all
                if (opt.NoDraw) throw new ArgumentException("--nodraw cannot be combined with --shot-at");
            }
            if (opt.NoDraw && opt.OutPath != null)
                throw new ArgumentException("--nodraw cannot be combined with --out");

            // A default so --jscalls / --nodraw runs still have somewhere to put a download.
            opt.SaveDir = opt.SaveDir ?? Path.Combine(Path.GetTempPath(), "eahl-saves");
            return true;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("'" + args[i] + "' needs a value");
            return args[++i];
        }

        private static void Usage()
        {
            Console.WriteLine(@"eahl - headless Evil Aliens: runs the real game with no browser,
                   no dev server and no visible window, and writes PNG frames.

ONE-SHOT
  eahl --flags ""?level=Level1&invuln"" --frames 300 --out shot.png
  eahl --flags ""?harness=spider"" --frames 5 --out spider.png
  eahl --flags ""?menu"" --frames 120 --shot-at 30,60,120 --out menu.png
  eahl --flags ""?level=Level2&flyspiders"" --frames 1800 --nodraw --jscalls

REPL (boot once, then drive it)
  eahl --repl
  eahl --script probe.txt
    step [n] [nodraw]      advance n frames at the fixed dt (default 1)
    shot <path.png>        render the CURRENT state to a PNG (does not advance)
    eval <method> [args]   call a Compat.DebugInput method (the eaPress/eaAiBench surface)
    info                   frame counter, sim time, buffer sizes, scene
    help                   list the eval methods
    quit

OPTIONS
  --flags <query>   the URL query the browser would get, e.g. ""?level=Level3&brainboss&invuln""
  --frames <n>      frames to run in one-shot mode (default 1)
  --shot-at <list>  comma-separated frame numbers to capture (writes <out>_0120.png)
  --out <path.png>  where the screenshot goes; with no --shot-at, captures the final frame
  --size WxH        back buffer (default 800x600). Keep it 4:3 or the shot is letterboxed.
  --fps <n>         simulated ticks per second (default 60). Not a speed limit - the loop
                    runs flat out; this is the dt the game is told it got.
  --nodraw          update only, no rendering. Much faster for behaviour/timing soaks.
  --content <dir>   path to web/EvilAliensWeb/wwwroot (found automatically by default)
  --saves <dir>     persist saves here (default: a temp dir, wiped, so runs start clean)
  --audio           let the game make noise (default: silent, and no audio device is
                    opened at all, so a box with no sound card still runs)
  --software        rasterize on the CPU via Mesa llvmpipe, for machines with no GPU
  --mesa <dll>      path to Mesa's opengl32.dll (implies --software)
  --jscalls         dump which browser (ea*) calls the game made
  --verbose         report unhandled JS calls and full stack traces");
        }
    }

    // -----------------------------------------------------------------------------------
    // DebugBridge — reaches Compat.DebugInput by reflection.
    //
    // DebugInput is already the curated console surface the browser exposes (eaPress,
    // eaAiBench, eaTexProbe, eaTeamSeat, eaBinTest, ...) via [JSInvokable] methods. Rather
    // than hand-maintaining a second command table that would drift from it, `eval` binds
    // straight to those methods -- so anything reachable from the browser console is
    // reachable headlessly, including methods added after this file was written.
    // -----------------------------------------------------------------------------------
    internal static class DebugBridge
    {
        internal static string Invoke(string[] parts, int start)
        {
            string name = parts[start];
            int argc = parts.Length - start - 1;

            MethodInfo chosen = null;
            foreach (MethodInfo m in typeof(DebugInput).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (m.GetParameters().Length != argc)
                    continue;
                chosen = m;
                break;
            }
            if (chosen == null)
                throw new MissingMethodException(
                    "no DebugInput." + name + " taking " + argc + " arg(s). Try `help`.");

            ParameterInfo[] ps = chosen.GetParameters();
            var call = new object[argc];
            for (int i = 0; i < argc; i++)
                call[i] = Coerce(parts[start + 1 + i], ps[i].ParameterType, ps[i].Name);

            object result = chosen.Invoke(null, call);
            return result?.ToString();
        }

        private static object Coerce(string raw, Type t, string paramName)
        {
            try
            {
                if (t == typeof(string)) return raw;
                if (t == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
                if (t == typeof(bool))
                    return raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
            }
            catch (FormatException)
            {
                throw new ArgumentException("'" + raw + "' is not a valid " + t.Name + " for '" + paramName + "'");
            }
            throw new ArgumentException("unsupported parameter type " + t.Name + " for '" + paramName + "'");
        }

        internal static string List()
        {
            var names = new List<string>();
            foreach (MethodInfo m in typeof(DebugInput).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var ps = m.GetParameters();
                var sig = new StringBuilder(m.Name).Append('(');
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sig.Append(' ');
                    sig.Append('<').Append(ps[i].Name).Append(':').Append(ps[i].ParameterType.Name).Append('>');
                }
                names.Add(sig.Append(')').ToString());
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("  ", names);
        }
    }
}
