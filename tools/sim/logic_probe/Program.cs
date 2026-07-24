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
        string binDir = args[0];
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

    // Card e6927ef8 -- TeamChallenge's partner seat. The bug was a seating decision whose
    // consequence is a permanent pause loop (GameScene.Update force-pauses every tick a seated pad
    // reads !PadConnected), so what has to be proven is that no pad-connection mask can resolve to
    // an ABSENT pad. Sixteen masks x three ?teampartner values, exhaustive.
    //
    // The comparison run here is the SAME one Compat/TeamSeatTest.cs (eaTeamSeat()) performs in the
    // browser -- its own private Expected/WouldForcePause helpers are invoked, not re-written --
    // so this also checks the browser test's table rather than only the resolver.
    private static int ProbeTeamPartnerSeat(Assembly asm)
    {
        Type team = asm.GetType("EvilAliens.TeamChallenge", true);
        Type device = asm.GetType("EvilAliens.ControlDevice", true);
        Type seatEnum = asm.GetType("EvilAliensWeb.Compat.DebugFlags+TeamPartnerSeat", true);
        Type test = asm.GetType("EvilAliensWeb.Compat.TeamSeatTest", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo resolve = team.GetMethod("ResolvePartnerSeat", anyStatic);
        MethodInfo expected = test.GetMethod("Expected", anyStatic);
        MethodInfo forcePause = test.GetMethod("WouldForcePause", anyStatic);
        if (resolve == null || expected == null || forcePause == null)
        {
            Console.WriteLine("FAIL: could not reflect the target (ResolvePartnerSeat=" + (resolve != null)
                + " Expected=" + (expected != null) + " WouldForcePause=" + (forcePause != null)
                + ") -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] TeamChallenge.ResolvePartnerSeat (card e6927ef8)");
        foreach (string name in new[] { "None", "Ai", "Pad" })
        {
            object forced = Enum.Parse(seatEnum, name);
            int loops = 0;
            int mismatches = 0;
            string firstMismatch = null;
            for (int mask = 0; mask < 16; mask++)
            {
                int m = mask;
                Func<int, bool> connected = i => (m & (1 << i)) != 0;
                object seat = resolve.Invoke(null, new object[] { connected, forced });
                object want = expected.Invoke(null, new object[] { mask, forced });
                if ((bool)forcePause.Invoke(null, new object[] { seat, mask }))
                {
                    loops++;
                }
                if (!seat.Equals(want))
                {
                    mismatches++;
                    if (firstMismatch == null)
                    {
                        firstMismatch = "mask " + Convert.ToString(mask, 2).PadLeft(4, '0') + " -> " + seat + ", expected " + want;
                    }
                }
            }
            // Only ?teampartner=pad may seat an absent pad -- it exists to reproduce the bug, and
            // exactly one mask (no pads at all) can do it.
            int wantLoops = (name == "Pad") ? 1 : 0;
            Check("teampartner=" + name.ToLowerInvariant() + " force-pause masks", loops == wantLoops,
                loops + "/16 seat an absent pad (expected " + wantLoops + ")");
            Check("teampartner=" + name.ToLowerInvariant() + " resolution", mismatches == 0,
                mismatches == 0 ? "16/16 as specified" : mismatches + " unexpected; " + firstMismatch);
        }

        // Negative control: the pre-card policy took no arguments -- always PadOne -- so it seats a
        // dead device in every mask without pad 0. A green suite above means nothing without this.
        object padOne = Enum.Parse(device, "PadOne");
        int oldLoops = 0;
        for (int mask = 0; mask < 16; mask++)
        {
            if ((bool)forcePause.Invoke(null, new object[] { padOne, mask }))
            {
                oldLoops++;
            }
        }
        Check("negative control (pre-card always-PadOne)", oldLoops == 8,
            oldLoops + "/16 masks force-pause every tick" + (oldLoops == 8 ? " -- the bug, reproduced" : " (expected 8)"));
        return 0;
    }
}
