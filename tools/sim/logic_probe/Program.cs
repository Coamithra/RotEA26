// Headless oracle for PURE game logic: reflection-loads the built EvilAliensWeb.dll into the
// DESKTOP CLR and calls a static method directly, so a decision can be verified with no browser,
// no WASM runtime and no rig at all.
//
// WHY THIS EXISTS. Almost everything in this port is verified in a browser (harness scene, scrub
// flag, slider panel, console self-test) because almost everything needs the engine. A pure static
// function needs none of it -- and the game assembly is ordinary IL whose whole dependency closure
// (nkast.*, the BCL) sits next to it in bin/Debug/net8.0, so `AssemblyLoadContext` can load it on
// the desktop and invoke the method for real. The value over a python mirror is that there is no
// mirror: the code under test IS the shipped code. Use it whenever the thing to prove is a
// decision rather than a picture, and it does not touch ServiceHelper, content or the GPU.
//
// LIMITS -- read before trusting a green tick:
//   * anything reaching ServiceHelper / Game / GraphicsDevice / content will throw or NRE here.
//     Keep the probed method pure and inject its inputs (delegates are fine -- see below).
//   * loading a TYPE resolves its base types, so a method on a scene class pulls in the XNA
//     assemblies. That works (they are managed), but a static constructor doing engine work
//     would not. Prefer probing the smallest type that owns the logic.
//   * it proves the FUNCTION, never the wiring. That a browser boot reads the flag, calls it and
//     seats the result still needs a live pass; the point is that the DECISION no longer does.
//
// USAGE
//   dotnet build web/EvilAliensWeb -c Debug          # the assembly this loads
//   dotnet run --project tools/sim/logic_probe -- <path-to-bin/Debug/net8.0>
// Exit code 0 = every case passed, 1 = a mismatch, 2 = could not reflect the target.
//
// TO ADD A CASE SET for the next card: write another Probe* method below and call it from Main.
// Keep the EXPECTATION independent of the implementation where you can (a restatement of the code
// proves little); where a restatement is unavoidable, add a NEGATIVE CONTROL that runs the OLD
// behaviour over the same inputs and must fail -- the eaNetScore.test() rule.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private static int failures;

    private static void Check(string label, bool ok, string detail)
    {
        if (!ok)
        {
            failures++;
        }
        Console.WriteLine((ok ? "  PASS " : "  FAIL ") + label + (detail != null ? "  " + detail : ""));
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: logic_probe <path to web/EvilAliensWeb/bin/Debug/net8.0>");
            return 2;
        }
        // LoadFromAssemblyPath demands an ABSOLUTE path (and so does the Resolving handler below),
        // while every documented invocation passes a repo-relative one.
        string binDir = Path.GetFullPath(args[0]);
        string asmPath = Path.Combine(binDir, "EvilAliensWeb.dll");
        if (!File.Exists(asmPath))
        {
            Console.WriteLine("FAIL: " + asmPath + " not found -- run `dotnet build web/EvilAliensWeb -c Debug` first");
            return 2;
        }
        // Resolve the game's whole dependency closure out of the same folder.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(binDir, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(asmPath);

        int rc = ProbeTeamPartnerSeat(asm);
        if (rc != 0)
        {
            return rc;
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // Card e6927ef8 -- TeamChallenge's two seat decisions. The bug was a seating decision whose
    // consequence is a permanent pause loop (GameScene.Update force-pauses every tick a seated pad
    // reads !PadConnected), so what has to be proven is that no pad-connection mask can seat a pad
    // that is not there. Exhaustive: every launching device and every one of the 16 masks, times
    // the three ?teampartner values for the partner.
    //
    // The PROPERTIES asserted are Compat/TeamSeatTest.cs's own -- its PrimaryViolation /
    // PartnerViolation / WouldForcePause are invoked by reflection rather than re-written here, so
    // this run also exercises the browser suite's table and the two cannot drift apart.
    private static int ProbeTeamPartnerSeat(Assembly asm)
    {
        Type team = asm.GetType("EvilAliens.TeamChallenge", true);
        Type device = asm.GetType("EvilAliens.ControlDevice", true);
        Type seatEnum = asm.GetType("EvilAliensWeb.Compat.DebugFlags+TeamPartnerSeat", true);
        Type test = asm.GetType("EvilAliensWeb.Compat.TeamSeatTest", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo primarySeat = team.GetMethod("ResolvePrimarySeat", anyStatic);
        MethodInfo partnerSeat = team.GetMethod("ResolvePartnerSeat", anyStatic);
        MethodInfo primaryBad = test.GetMethod("PrimaryViolation", anyStatic);
        MethodInfo partnerBad = test.GetMethod("PartnerViolation", anyStatic);
        MethodInfo forcePause = test.GetMethod("WouldForcePause", anyStatic);
        if (primarySeat == null || partnerSeat == null || primaryBad == null || partnerBad == null || forcePause == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (ResolvePrimarySeat=" + (primarySeat != null)
                + " ResolvePartnerSeat=" + (partnerSeat != null) + " PrimaryViolation=" + (primaryBad != null)
                + " PartnerViolation=" + (partnerBad != null) + " WouldForcePause=" + (forcePause != null)
                + ") -- renamed or moved?");
            return 2;
        }

        string[] starters = { "Keyboard", "PadOne", "PadTwo", "PadThree", "PadFour", "Generic", "AI", "Remote" };

        Console.WriteLine("[logic_probe] TeamChallenge.ResolvePrimarySeat (card e6927ef8)");
        foreach (string starterName in starters)
        {
            object starter = Enum.Parse(device, starterName);
            int bad = 0;
            string first = null;
            for (int mask = 0; mask < 16; mask++)
            {
                int m = mask;
                Func<int, bool> connected = i => (m & (1 << i)) != 0;
                object seat = primarySeat.Invoke(null, new object[] { starter, connected });
                string why = (string)primaryBad.Invoke(null, new object[] { seat, starter, mask });
                if (why != null)
                {
                    bad++;
                    if (first == null)
                    {
                        first = "mask " + Convert.ToString(mask, 2).PadLeft(4, '0') + " -> " + seat + ": " + why;
                    }
                }
            }
            Check("starter " + starterName, bad == 0, bad == 0 ? "16/16 drivable and never an absent pad" : bad + " bad; " + first);
        }

        Console.WriteLine("[logic_probe] TeamChallenge.ResolvePartnerSeat");
        foreach (string name in new[] { "None", "Ai", "Pad" })
        {
            object forced = Enum.Parse(seatEnum, name);
            int bad = 0;
            int loops = 0;
            int humans = 0;
            string first = null;
            foreach (string primaryName in new[] { "Keyboard", "PadOne", "PadTwo" })
            {
                object primary = Enum.Parse(device, primaryName);
                for (int mask = 0; mask < 16; mask++)
                {
                    int m = mask;
                    Func<int, bool> connected = i => (m & (1 << i)) != 0;
                    object seat = partnerSeat.Invoke(null, new object[] { primary, connected, forced });
                    bool pauses = (bool)forcePause.Invoke(null, new object[] { seat, mask });
                    if (pauses)
                    {
                        loops++;
                    }
                    else if (!seat.ToString().StartsWith("AI"))
                    {
                        humans++;
                    }
                    string why = (string)partnerBad.Invoke(null, new object[] { seat, primary, mask, forced });
                    if (why != null)
                    {
                        bad++;
                        if (first == null)
                        {
                            first = "primary " + primaryName + ", mask " + Convert.ToString(mask, 2).PadLeft(4, '0')
                                + " -> " + seat + ": " + why;
                        }
                    }
                }
            }
            string what = "teampartner=" + name.ToLowerInvariant();
            Check("properties " + what, bad == 0, bad == 0 ? "48/48 cases hold" : bad + " violated; " + first);
            if (name == "None")
            {
                // A resolver that always returned the bot would satisfy every other property.
                Check("seats real humans " + what, humans > 0, humans + "/48 cases seat a present pad");
            }
            // Only the bug-reproduction override may seat a device that is not there.
            int wantLoops = (name == "Pad") ? 24 : 0;
            Check("force-pause seats " + what, loops == wantLoops, loops + "/48 (expected " + wantLoops + ")");
        }

        // Negative control: the pre-card policy took no arguments -- Keyboard in slot 0, PadOne in
        // slot 1 -- so it seated a dead device in every mask without pad 0. A green suite above
        // means nothing without this.
        object padOne = Enum.Parse(device, "PadOne");
        int oldLoops = 0;
        for (int mask = 0; mask < 16; mask++)
        {
            if ((bool)forcePause.Invoke(null, new object[] { padOne, mask }))
            {
                oldLoops++;
            }
        }
        Check("negative control (pre-card Keyboard + PadOne)", oldLoops == 8,
            oldLoops + "/16 masks force-pause every tick" + (oldLoops == 8 ? " -- the bug, reproduced" : " (expected 8)"));
        return 0;
    }
}
