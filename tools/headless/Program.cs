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
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
        internal bool FakeNoAudioDevice;
        // Hand the game the DEVELOPER'S DESKTOP mouse, as every run did before card 83054936.
        // Off by default -- see HeadlessHost.Boot and DebugInput.SuppressPhysicalMouse.
        internal bool RealMouse;
        // --nettime game: run the net layer's clock on GAME time (one --fps step per frame)
        // instead of the wall clock. OFF by default so every existing probe is unchanged.
        // Card 054947f3: --nodraw runs ~17x real time, so the wire's cadences (60 ms snapshots,
        // 30 Hz ship state, 1 Hz score sync) fire ~17x too rarely PER UNIT OF WORLD MOTION and a
        // two-process world diff measures that artifact rather than the code.
        internal bool NetTimeGame;
        // --net-port: override the port LocalSocketNet derives from ?room=. 0 = derive.
        internal int NetPort;
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

            // Everything the run prints from here on is captured, so `expect` / `expect-not`
            // can assert on the game's own console diagnostics ([loadprofile], [hitch], [net],
            // ...). Installed before boot, because a level's preload -- the thing the preload
            // probes assert about -- happens during it.
            ConsoleCapture.Install();

            // Silent by default: a background soak must not play the game's SFX through the
            // user's speakers. BEFORE the host is constructed, because Boot() runs a whole
            // Update+Draw frame (RunOneFrame) and a sound played in it would otherwise be
            // audible. SoundEffect.MasterVolume is a plain static and needs no audio device;
            // the mixer-level half of the mute is what has to wait, and does (HeadlessAudio.Pump).
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

            // Before the device is ever opened, i.e. before boot. Writes an alsoft.ini that makes
            // OpenAL Soft genuinely fail to open one -- see NoAudioDeviceSim.
            if (opt.FakeNoAudioDevice)
            {
                string why = NoAudioDeviceSim.Install();
                if (why != null)
                {
                    // Its own exit code, not 2: this is a runtime failure, and a caller that sees
                    // 2 goes looking for a typo in its argv. Same reasoning as --software's 3.
                    Console.Error.WriteLine("err --fake-no-audio-device: " + why);
                    return 4;
                }
            }

            try
            {
                using (var host = new HeadlessHost(opt))
                {
                    host.Boot();

                    // Open the device HERE rather than leaving KNI to do it lazily on the first
                    // sound: that is what makes a device-less box report itself instead of just
                    // going quiet, and what makes alGain readable from frame 0 instead of only
                    // after something has played (HeadlessAudio's header has the measurements).
                    HeadlessAudio.BringUp();
                    Console.WriteLine("[eahl] audio    " + AudioStatus());
                    if (HeadlessAudio.Device != HeadlessAudio.DeviceState.Ok)
                    {
                        Console.WriteLine("[eahl] audio    NO AUDIO DEVICE (" + DeviceWord(HeadlessAudio.Device)
                            + ") -- the run CONTINUES with audio dead: no SFX, and silence cannot be"
                            + " confirmed at the mixer. See tools/headless/HeadlessAudio.cs."
                            + (HeadlessAudio.Failure != null ? " Cause: " + HeadlessAudio.Failure : ""));
                        // A box that HAS a device reporting none is almost always this.
                        string stranded = NoAudioDeviceSim.StrandedIni();
                        if (stranded != null)
                            Console.WriteLine("[eahl] audio    NOTE a leftover " + stranded + " from an earlier"
                                + " --fake-no-audio-device run is what disabled the device. Delete it, or pass"
                                + " --fake-no-audio-device once to have it cleaned up on exit.");
                    }

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
            finally
            {
                NoAudioDeviceSim.Remove();
                // After the host is disposed, so nothing is still holding a save file open.
                TempSaveDir.Release();
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
                    case "audio":
                        // Silence as DATA. alGain is read back out of OpenAL itself, so it is
                        // what the mixer is actually doing rather than what we asked for. See
                        // HeadlessAudio, trap 2, for why that distinction is the whole reason
                        // this command exists.
                        Console.WriteLine("ok audio " + AudioStatus());
                        return true;
                    case "mark":
                        // Start a fresh assertion window. Assertions deliberately do NOT reset
                        // it (asserting twice over one window is the common case), so this is
                        // the only reset.
                        ConsoleCapture.Mark();
                        Console.WriteLine("ok mark");
                        return true;
                    case "expect":
                    case "expect-not":
                    {
                        string pattern = Remainder(line);
                        if (pattern.Length == 0)
                            throw new ArgumentException("usage: " + cmd + " <regex>");
                        bool want = cmd == "expect";
                        string[] hits = ConsoleCapture.Match(pattern, out bool truncated);
                        // Truncation invalidates ABSENCE only. A match that was found is still
                        // a match, so a positive `expect` that already matched is unaffected --
                        // it is `expect-not`, and an `expect` that found nothing, that cannot be
                        // trusted over output which was thrown away.
                        if (truncated && (!want || hits.Length == 0))
                            throw new InvalidOperationException(
                                "the capture window overflowed and older output was dropped, so "
                                + cmd + " /" + pattern + "/ cannot be trusted -- add a `mark` closer "
                                + "to what you assert on");
                        if (want && hits.Length == 0)
                            throw new InvalidOperationException("expect /" + pattern + "/ matched nothing");
                        if (!want && hits.Length > 0)
                            throw new InvalidOperationException("expect-not /" + pattern + "/ matched "
                                + hits.Length + " line(s), first: " + hits[0]);
                        Console.WriteLine("ok " + cmd + " " + hits.Length + " match(es)");
                        return true;
                    }
                    case "help":
                        Console.WriteLine("ok " + DebugBridge.List());
                        return true;
                    case "quit":
                    case "exit":
                        quit = true;
                        Console.WriteLine("ok bye");
                        return true;
                    default:
                        throw new ArgumentException("unknown command '" + cmd
                            + "' (try: step, shot, eval, info, audio, mark, expect, expect-not, help, quit)");
                }
            }
            catch (Exception ex)
            {
                // Never let a bad command kill the session -- an agent should be able to
                // recover by sending a corrected one.
                Console.WriteLine("err " + Describe(ex));
                foreach (string frame in Trace(ex))
                    Console.WriteLine("    " + frame);
                return false;
            }
        }

        // `eval` calls DebugInput by reflection, so ANY failure inside the game surfaces as
        // "TargetInvocationException: Exception has been thrown by the target of an invocation."
        // -- which names neither the fault nor where it happened. That is not a hypothetical:
        // screenshot_alpha.txt failed exactly once in a batch run and card de82597f could say
        // nothing about it beyond the wrapper's name, because the wrapper is all that was
        // printed. So the chain is unwrapped and the INNERMOST cause leads, since that is the
        // one that says what actually went wrong.
        //
        // TargetInvocationException itself is dropped from the report: its message is boilerplate
        // and its type only restates that `eval` uses reflection. Any other wrapper is kept --
        // an AggregateException or a rethrow carries real context about the layer it crossed.
        private static string Describe(Exception ex)
        {
            var sb = new StringBuilder();
            foreach (Exception e in Chain(ex))
            {
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            // Everything was reflection plumbing (an eval whose target threw a bare
            // TargetInvocationException). Report it rather than an empty `err`.
            return sb.Length > 0 ? sb.ToString() : ex.GetType().Name + ": " + ex.Message;
        }

        // The innermost exception's stack, and only when there WAS an inner one -- a plain bad
        // command ("unknown command 'stpe'") needs no trace and would only bury the message.
        //
        // SOURCE-LOCATED FRAMES FIRST, and that ordering is the whole value here. run_probes.py's
        // failure_tail prints exactly ONE line after the `err` it stopped on, so whichever frame
        // leads is the only one an agent reading a suite failure ever sees. A raw stack leads with
        // BCL plumbing (SafeFileHandle.CreateFile, OSFileStreamStrategy..ctor, ...) and says
        // nothing; the frames carrying `in <file>:line <n>` are this repo's, and one of those
        // names the call site. Both sets are printed -- only the order changes -- and the whole
        // thing is bounded so a deep stack cannot bury the `err` line in a scrolled log.
        private static IEnumerable<string> Trace(Exception ex)
        {
            if (ex.InnerException == null)
                yield break;
            Exception innermost = ex;
            while (innermost.InnerException != null)
                innermost = innermost.InnerException;
            string trace = innermost.StackTrace;
            if (string.IsNullOrEmpty(trace))
                yield break;

            var located = new List<string>();
            var rest = new List<string>();
            foreach (string line in trace.Split('\n'))
            {
                string frame = line.Trim();
                if (frame.Length == 0)
                    continue;
                // " in <path>:line <n>" is present only for a frame whose PDB shipped, i.e. ours.
                (frame.Contains(":line ") ? located : rest).Add(frame);
            }
            // The two groups are each in stack order, but printing them back to back would read
            // as one stack and mis-attribute the caller of the last located frame. The separator
            // says where the reordering happened -- and it can only ever appear AFTER a located
            // frame, so the one line run_probes prints after the `err` is still a real frame.
            int shown = 0;
            foreach (string frame in located)
            {
                yield return frame;
                if (++shown == 8)
                    yield break;
            }
            if (located.Count > 0 && rest.Count > 0)
                yield return "-- outer frames --";
            foreach (string frame in rest)
            {
                yield return frame;
                if (++shown == 8)
                    yield break;
            }
        }

        // Innermost first, skipping the reflection wrappers. See Describe.
        private static List<Exception> Chain(Exception ex)
        {
            var chain = new List<Exception>();
            for (Exception e = ex; e != null; e = e.InnerException)
                if (!(e is TargetInvocationException))
                    chain.Add(e);
            chain.Reverse();
            return chain;
        }

        // Everything after the command word, verbatim. `expect` takes a REGEX, which may hold
        // spaces, '|', '"' and anything else Split() would mangle, so it must not go through
        // Split at all. One layer of surrounding quotes is stripped for the people who add
        // them out of habit.
        //
        // Split at the first whitespace rather than by the command NAME's length: Split()
        // strips quotes, so `"expect" foo` would leave cmd two chars shorter than the text it
        // came from and the offset would silently slice the pattern mid-way.
        private static string Remainder(string line)
        {
            int sp = line.IndexOf(' ');
            if (sp < 0) sp = line.IndexOf('\t');
            string rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            if (rest.Length >= 2 && rest[0] == '"' && rest[rest.Length - 1] == '"')
                rest = rest.Substring(1, rest.Length - 2);
            return rest;
        }

        // silenced= is what we ASKED for; alGain= is what OpenAL itself reports. Printing both
        // is the point: the previous silencing mechanism asked and was ignored, silently, for
        // the whole of eahl's first life (HeadlessAudio, trap 2).
        //
        // device= and lib= exist to make an unreadable gain DIAGNOSABLE. It has three causes that
        // used to look identical -- the device never opened, OpenAL is simply not up yet, or the
        // OpenAL binary did not resolve at all -- and only the middle one is benign.
        private static string AudioStatus()
        {
            float? gain = HeadlessAudio.ListenerGain;
            return "silenced=" + HeadlessAudio.Silenced
                + " masterVolume=" + HeadlessAudio.MasterVolume.ToString("0.###", CultureInfo.InvariantCulture)
                + " device=" + DeviceWord(HeadlessAudio.Device)
                + " lib=" + (HeadlessAudio.ResolvedLibrary ?? "<unresolved>")
                + " alGain=" + (gain.HasValue ? gain.Value.ToString("0.###", CultureInfo.InvariantCulture) : "<unreadable>");
        }

        // Every member is spelled out and the fallback is derived, so a state added later reports
        // its own name rather than silently borrowing NotTried's -- a wrong answer in the one
        // field whose entire job is diagnosis.
        private static string DeviceWord(HeadlessAudio.DeviceState state)
        {
            switch (state)
            {
                case HeadlessAudio.DeviceState.NotTried: return "nottried";
                case HeadlessAudio.DeviceState.Ok: return "ok";
                case HeadlessAudio.DeviceState.None: return "none";
                case HeadlessAudio.DeviceState.NoLibrary: return "nolib";
                default: return state.ToString().ToLowerInvariant();
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
                    case "--fake-no-audio-device": opt.FakeNoAudioDevice = true; break;
                    case "--real-mouse": opt.RealMouse = true; break;
                    case "--net-port": opt.NetPort = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--nettime":
                    {
                        string mode = Next(args, ref i);
                        if (mode == "game") opt.NetTimeGame = true;
                        else if (mode == "wall") opt.NetTimeGame = false;
                        else throw new ArgumentException("--nettime wants game or wall, got '" + mode + "'");
                        break;
                    }
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

            // A default so --jscalls / --nodraw runs still have somewhere to put a download,
            // PER PROCESS -- see TempSaveDir.
            opt.SaveDir = opt.SaveDir ?? TempSaveDir.Claim();
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
    audio                  silenced= / device= / lib= + the gain OpenAL itself reports
    mark                   start a fresh assertion window (drop captured output)
    expect <regex>         FAIL unless some captured line matches
    expect-not <regex>     FAIL if any captured line matches (quotes the offender)
    help                   list the eval methods
    quit

  Committed regression probes live in tools/headless/probes/ and run through
  tools/headless/probes/run_probes.py -- see probes/README.md.

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
  --saves <dir>     persist saves here (default: a PER-PROCESS temp dir, removed on exit,
                    so runs start clean and concurrent runs cannot wipe each other)
  --audio           let the game make noise (default: silent -- the mixer, the decodes and
                    every source still run, the gain is just zero)
  --fake-no-audio-device
                    make the audio device genuinely fail to open (an alsoft.ini naming a
                    backend that does not exist), so the no-sound-card path can be tested
                    on a box that has one. The run continues, deaf, and says so.
  --real-mouse      let the game read the DESKTOP mouse. By default it cannot: KNI's SDL2
                    backend answers Mouse.GetState() from SDL_GetGlobalMouseState, so a
                    headless run otherwise samples your real pointer AND your real buttons,
                    focus or no focus -- which silently flaked the probe suite (card
                    83054936). This restores that, and is the negative control for it.
  --nettime <game|wall>
                    which clock the net layer runs on. wall (default) is production's
                    Environment.TickCount64. game advances it by one --fps step per frame, so
                    the wire's cadences stay in step with world motion however fast the run
                    goes -- required for a two-process co-op run, since --nodraw is ~17x real
                    time and would otherwise starve the wire (card 054947f3)
  --net-port <n>    port for the ?net= localhost loopback (default: derived from ?room=, so
                    two processes sharing a room agree with no extra configuration)
  --software        rasterize on the CPU via Mesa llvmpipe, for machines with no GPU
  --mesa <dll>      path to Mesa's opengl32.dll (implies --software)
  --jscalls         dump which browser (ea*) calls the game made
  --verbose         report unhandled JS calls and full stack traces");
        }
    }

    // -----------------------------------------------------------------------------------
    // ConsoleCapture — tees everything the run prints into an in-process buffer.
    //
    // WHY: a committed probe's most valuable assertion is usually "this diagnostic must NOT
    // appear" -- no `COLD decode in Level2`, no `[hitch]`, no `[net] desync`. Those are
    // Console.WriteLine output from the GAME, not the reply to any command, so the ok/err line
    // protocol could not reach them and a script could not assert on them at all. Teeing the
    // console makes every diagnostic the game already prints assertable, with no game-side
    // code and no second surface to keep in sync (see probes/README.md).
    //
    // Assertions match PER LINE, so `^`/`$` anchor to a line without RegexOptions.Multiline
    // and a failure can quote the offending line rather than the whole transcript.
    //
    // The buffer is capped, and an overflow is treated as a FAILURE rather than trimmed
    // quietly: absence cannot be proven over output that was thrown away, so a truncated
    // window must never let an `expect-not` pass.
    // -----------------------------------------------------------------------------------
    internal static class ConsoleCapture
    {
        private const int MaxChars = 8 * 1024 * 1024;

        private static readonly StringBuilder Buffer = new StringBuilder();
        private static readonly object Gate = new object();
        private static bool _overflowed;
        private static bool _installed;

        internal static void Install()
        {
            if (_installed)
                return;
            _installed = true;
            Console.SetOut(new Tee(Console.Out));
            Console.SetError(new Tee(Console.Error));
        }

        internal static void Mark()
        {
            lock (Gate)
            {
                Buffer.Clear();
                _overflowed = false;
            }
        }

        internal static string[] Match(string pattern, out bool truncated)
        {
            var re = new Regex(pattern, RegexOptions.CultureInvariant);
            string text;
            lock (Gate)
            {
                truncated = _overflowed;
                text = Buffer.ToString();
            }
            var hits = new List<string>();
            foreach (string ln in text.Split('\n'))
            {
                // Blank lines are matched too, so `expect-not ^$` means what it says. (Skipping
                // them would make a whole class of pattern silently unmatchable.)
                string s = ln.TrimEnd('\r');
                if (re.IsMatch(s))
                    hits.Add(s);
            }
            return hits.ToArray();
        }

        private static void Record(string s)
        {
            if (s == null)
                return;
            lock (Gate)
            {
                if (Buffer.Length + s.Length > MaxChars)
                {
                    _overflowed = true;
                    return;
                }
                Buffer.Append(s);
            }
        }

        // Forwards to the real console AND records. Write(char) is what the TextWriter base
        // routes every unoverridden overload through, so overriding it alone would be correct;
        // the string overloads are here because they are the hot path.
        private sealed class Tee : TextWriter
        {
            private readonly TextWriter _inner;
            internal Tee(TextWriter inner) { _inner = inner; }

            public override Encoding Encoding => _inner.Encoding;
            public override IFormatProvider FormatProvider => _inner.FormatProvider;

            public override void Write(char value) { _inner.Write(value); Record(value.ToString()); }
            public override void Write(string value) { _inner.Write(value); Record(value); }
            public override void WriteLine(string value) { _inner.WriteLine(value); Record(value); Record("\n"); }
            public override void WriteLine() { _inner.WriteLine(); Record("\n"); }
            public override void Flush() { _inner.Flush(); }
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
