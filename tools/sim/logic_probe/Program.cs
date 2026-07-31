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

    // Run one query through the real DebugFlags.Parse and hand back everything it PRINTED. Four
    // case sets need this now (card 4e401005 would have been the fourth copy), and it is the only
    // way to assert about a DIAGNOSTIC rather than a resulting value -- silence is the bug these
    // flag sets exist to catch, and silence has no other observable.
    private static string RunParse(MethodInfo parse, string query)
    {
        System.IO.TextWriter saved = Console.Out;
        System.IO.StringWriter buf = new System.IO.StringWriter();
        Console.SetOut(buf);
        try { parse.Invoke(null, new object[] { query }); }
        finally { Console.SetOut(saved); }
        return buf.ToString();
    }

    // A captured Parse() run is usually two lines (Parse tails into Hint() whenever nothing in
    // `Active` was set) and the diagnostic under test is the first. Detail strings are printed on
    // the same line as their PASS/FAIL, so a raw capture would break that format -- and splitting
    // on '\n' alone leaves a trailing '\r' on Windows.
    private static string FirstLine(string captured)
    {
        string[] lines = captured.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0] : "(nothing printed)";
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

        rc = ProbeFlySpiderFlags(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeAiWallScanFlags(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeAiFlagRejection(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeFlagRejectionSweep(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeCollisionBoxLine(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeLevelArt(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeGameBrowserFlag(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeWireEnums(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeNetWire(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeNetHost(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeMouseLatch(asm);
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

    // Card 6eb8dc9e -- the ?flyspider* value-carrying flags, driven through the REAL
    // DebugFlags.Parse. A bench flag's failure mode is not a wrong picture, it is a run that
    // measures the default path while being LABELLED as the variant under test, so what has to be
    // proven is that a malformed value is rejected, says so, and names the setting actually left
    // in force. Parse is pure string -> static property (no ServiceHelper, no Game), which is what
    // makes it reachable here at all.
    //
    // Note the statics PERSIST across Parse calls in one process, exactly as they would across a
    // repeated flag in one query -- that is the property the "staying on <in-force value>" wording
    // depends on, so the sequence below is deliberate and order-sensitive.
    private static int ProbeFlySpiderFlags(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        MethodInfo isOn = flags.GetMethod("IsOn", anyStatic);
        MethodInfo isOff = flags.GetMethod("IsExplicitlyOff", anyStatic);
        if (parse == null || isOn == null || isOff == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (Parse=" + (parse != null)
                + " IsOn=" + (isOn != null) + " IsExplicitlyOff=" + (isOff != null) + ") -- renamed or moved?");
            return 2;
        }

        // Run one query through Parse and hand back everything it printed, so an assertion can be
        // made about the DIAGNOSTIC and not just the resulting value. Silence is the old bug.
        Func<string, string> run = query => RunParse(parse, query);
        // Parse tails into Hint() whenever nothing in `Active` got set, so the capture is usually
        // two lines; the diagnostic under test is the first one.
        Func<string, string> firstLine = s =>
            s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } lines
                ? lines[0] : "(nothing printed)";
        Func<string, object> get = name => flags.GetProperty(name, anyStatic).GetValue(null);

        Console.WriteLine("[logic_probe] DebugFlags ?flyspider* value parsing (card 6eb8dc9e)");

        run("?flyspidercount=40&flyspiderbox=250&flyspiderscale=0.6&flyspiderflatten=per");
        Check("valid values land", Equals(get("FlySpiderCount"), 40) && Equals(get("FlySpiderBox"), 250f)
            && Equals(get("FlySpiderScale"), 0.6f) && get("FlySpiderFlatten").ToString() == "PerSpider",
            "count=" + get("FlySpiderCount") + " box=" + get("FlySpiderBox") + " scale=" + get("FlySpiderScale")
            + " flatten=" + get("FlySpiderFlatten"));

        // The core claim, one flag at a time: a bad value changes NOTHING and is REPORTED. The
        // "staying on" clause must name the value in force (250/0.6/PerSpider from above), never
        // the baked default -- a diagnostic that can state the wrong condition is the finding.
        string outCount = run("?flyspidercount=4O");
        Check("bad ?flyspidercount= rejected + reported", Equals(get("FlySpiderCount"), 40)
            && outCount.Contains("unknown ?flyspidercount=") && outCount.Contains("40"),
            "count=" + get("FlySpiderCount") + " said: " + firstLine(outCount));

        string outBox = run("?flyspiderbox=xx");
        Check("bad ?flyspiderbox= rejected + names 250 not 200", Equals(get("FlySpiderBox"), 250f)
            && outBox.Contains("unknown ?flyspiderbox=") && outBox.Contains("250") && !outBox.Contains("200"),
            "box=" + get("FlySpiderBox") + " said: " + firstLine(outBox));

        string outScale = run("?flyspiderscale=0.6O");
        Check("bad ?flyspiderscale= rejected + reported", Equals(get("FlySpiderScale"), 0.6f)
            && outScale.Contains("unknown ?flyspiderscale="),
            "scale=" + get("FlySpiderScale") + " said: " + firstLine(outScale));

        string outFlat = run("?flyspiderflatten=swrm");
        Check("bad ?flyspiderflatten= rejected + names PerSpider not swarm",
            get("FlySpiderFlatten").ToString() == "PerSpider" && outFlat.Contains("unknown ?flyspiderflatten=")
            && outFlat.Contains("PerSpider"),
            "flatten=" + get("FlySpiderFlatten") + " said: " + firstLine(outFlat));

        // N=0 is a documented baseline (an empty Level 2 to subtract), so it must NOT be swept up
        // with the rejections; the ceiling and negatives must be.
        string outZero = run("?flyspidercount=0");
        Check("?flyspidercount=0 accepted as the baseline", Equals(get("FlySpiderCount"), 0)
            && !outZero.Contains("unknown"), "count=" + get("FlySpiderCount"));
        run("?flyspidercount=99999999");
        Check("?flyspidercount= ceiling rejected", Equals(get("FlySpiderCount"), 0),
            "one fat-fingered zero must not spend the boot building components; count=" + get("FlySpiderCount"));
        run("?flyspidercount=-5");
        Check("?flyspidercount= negative rejected", Equals(get("FlySpiderCount"), 0),
            "count=" + get("FlySpiderCount"));

        // IsOn / IsExplicitlyOff: this card reordered the two and moved a doc comment between
        // them, so pin the truth table. The row that matters is null (a BARE flag) -- ON, but NOT
        // explicitly off, which is the whole reason the second predicate exists and precisely what
        // the orphaned comment had claimed otherwise.
        var rows = new[]
        {
            new { Val = (string)null, On = true,  Off = false },
            new { Val = "",           On = true,  Off = false },
            new { Val = "1",          On = true,  Off = false },
            new { Val = "true",       On = true,  Off = false },
            new { Val = "ON",         On = true,  Off = false },
            new { Val = " yes ",      On = true,  Off = false },
            new { Val = "0",          On = false, Off = true  },
            new { Val = "false",      On = false, Off = true  },
            new { Val = "OFF",        On = false, Off = true  },
            new { Val = " no ",       On = false, Off = true  },
            new { Val = "swarm",      On = false, Off = false },
            new { Val = "typo",       On = false, Off = false },
        };
        int bad = 0;
        string firstBad = null;
        foreach (var row in rows)
        {
            bool on = (bool)isOn.Invoke(null, new object[] { row.Val });
            bool off = (bool)isOff.Invoke(null, new object[] { row.Val });
            if (on != row.On || off != row.Off)
            {
                bad++;
                firstBad ??= "'" + (row.Val ?? "<bare>") + "' -> on=" + on + " off=" + off;
            }
        }
        Check("IsOn / IsExplicitlyOff truth table", bad == 0,
            bad == 0 ? rows.Length + "/" + rows.Length + " rows hold" : bad + " wrong; " + firstBad);
        // The distinction, stated as its own assertion: !IsOn is NOT IsExplicitlyOff. If it were,
        // the second predicate would be dead code and an unrecognised value would silently run the
        // "off" path -- the conflation both of this card's parse fixes exist to prevent.
        int conflated = 0;
        foreach (var row in rows)
        {
            bool on = (bool)isOn.Invoke(null, new object[] { row.Val });
            bool off = (bool)isOff.Invoke(null, new object[] { row.Val });
            if (!on == off)
            {
                conflated++;
            }
        }
        Check("!IsOn is not IsExplicitlyOff", conflated < rows.Length,
            (rows.Length - conflated) + "/" + rows.Length + " rows where they genuinely differ (bare + unrecognised)");
        return 0;
    }

    // Card b174b00f -- ?aiscanrows= / ?aicrosspenalty=, the promotion of PlayerShip's last two
    // bare wall-nav consts to the Default* + nullable-override convention.
    //
    // WHY A PROBE AND NOT verify_il_identical.py, WHICH THE CARD ASKED FOR: that oracle hashes the
    // whole assembly, and this change ADDS two consts, two DebugFlags properties, two resolving
    // properties and two Parse cases -- so it reports DIFFERENT by construction and proves
    // nothing. The card's real claim is BEHAVIOURAL (a null override resolves to the baked const,
    // so a shipped build plays identically), and that splits in two: the RESOLUTION is pinned
    // below, and the wall term's actual numbers are pinned by tools/sim/aiwallnav's default table
    // being character-identical across the change (it is deterministic and calls the only two
    // consumers of these constants for real).
    //
    // The failure mode being guarded is the same one card 6eb8dc9e named for ?flyspider*: a bench
    // run that measures the DEFAULT path while carrying the label of the variant under test. No
    // screenshot can show that, and this card's own blocks 2 and 3 quote numbers from these flags.
    private static int ProbeAiWallScanFlags(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        Type ship = asm.GetType("EvilAliens.PlayerShip", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        // The RESOLVING properties are private -- they are the thing under test, so bind them by
        // name and fail loudly rather than benching nothing, the way aiwallnav does.
        PropertyInfo rows = ship.GetProperty("WallScanRows", anyStatic);
        PropertyInfo penalty = ship.GetProperty("WallCrossPenalty", anyStatic);
        FieldInfo defRows = ship.GetField("DefaultWallScanRows", anyStatic);
        FieldInfo defPenalty = ship.GetField("DefaultWallCrossPenalty", anyStatic);
        if (parse == null || rows == null || penalty == null || defRows == null || defPenalty == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (Parse=" + (parse != null)
                + " WallScanRows=" + (rows != null) + " WallCrossPenalty=" + (penalty != null)
                + " DefaultWallScanRows=" + (defRows != null) + " DefaultWallCrossPenalty=" + (defPenalty != null)
                + ") -- renamed or moved?");
            return 2;
        }

        Action<string> run = query => RunParse(parse, query);
        Func<object> liveRows = () => rows.GetValue(null);
        Func<object> livePenalty = () => penalty.GetValue(null);
        object bakedRows = defRows.GetValue(null);
        object bakedPenalty = defPenalty.GetValue(null);

        Console.WriteLine("[logic_probe] DebugFlags ?aiscanrows= / ?aicrosspenalty= (card b174b00f)");

        // 1. The shipped configuration. Both overrides are null at process start (DebugFlags.Parse
        // never RESETS a property -- it only assigns ones the query names, which is also why the
        // checks below can rely on a previous case still standing), so this reads the state a
        // shipped boot is in. It must resolve to the baked consts. NB this is a PRECONDITION of
        // the case set, not an assertion about Parse: it holds only while nothing earlier in the
        // process has touched these two, which is why it runs first.
        Check("no override => the baked Default* consts",
            Equals(liveRows(), bakedRows) && Equals(livePenalty(), bakedPenalty),
            "rows=" + liveRows() + " (Default " + bakedRows + "), penalty=" + livePenalty()
            + " (Default " + bakedPenalty + ")");
        // ... and the consts are still the values card f4d1721f tuned against. A promotion that
        // silently moved one would pass every other check here.
        Check("the baked values are unchanged by the promotion",
            Equals(bakedRows, 4) && Equals(bakedPenalty, 4f),
            "DefaultWallScanRows=" + bakedRows + " DefaultWallCrossPenalty=" + bakedPenalty);

        // 2. An override actually reaches the resolving property. Without this the flags would be
        // inert and every sweep would quietly re-measure the default.
        run("?aiscanrows=9&aicrosspenalty=2.5");
        Check("overrides win over the consts",
            Equals(liveRows(), 9) && Equals(livePenalty(), 2.5f),
            "rows=" + liveRows() + " penalty=" + livePenalty());

        // 3. THE int-vs-float POINT, which is why the card called out int? explicitly. `4.7` is not
        // a number of grid rows. Truncating it to 4 would hand back the DEFAULT depth while the
        // reader believes a deeper scan is in force -- a sweep that cannot move, reported as a
        // result. So it must be refused, leaving the previous value (9) standing.
        run("?aiscanrows=4.7");
        Check("?aiscanrows=4.7 refused, not truncated to 4", Equals(liveRows(), 9),
            "rows=" + liveRows() + " (a float here must not silently become the baked 4)");
        run("?aiscanrows=8x");
        Check("?aiscanrows=8x refused", Equals(liveRows(), 9), "rows=" + liveRows());
        run("?aicrosspenalty=2.5q");
        Check("?aicrosspenalty=2.5q refused", Equals(livePenalty(), 2.5f), "penalty=" + livePenalty());

        // The DIAGNOSTIC these two now print is asserted with the other twelve ?ai* tuning knobs
        // in ProbeAiFlagRejection below, so the family is covered in one place.

        // 4. Clamps. 0 scan rows is ALLOWED on purpose -- it is "does not look ahead at all", the
        // floor end of a look-ahead sweep and the same kind of deliberate skill floor ?aiaim=Pi is.
        run("?aiscanrows=0");
        Check("?aiscanrows=0 accepted as the no-look-ahead floor", Equals(liveRows(), 0),
            "rows=" + liveRows());
        run("?aiscanrows=99999");
        Check("?aiscanrows= ceiling clamps", Equals(liveRows(), 64),
            "the scan runs per column per tick, so it must stay bounded; rows=" + liveRows());
        run("?aiscanrows=-3");
        Check("?aiscanrows= negative refused", Equals(liveRows(), 64), "rows=" + liveRows());
        run("?aicrosspenalty=99999");
        Check("?aicrosspenalty= ceiling clamps", Equals(livePenalty(), 100f),
            "penalty=" + livePenalty());
        run("?aicrosspenalty=-1");
        Check("?aicrosspenalty= negative refused", Equals(livePenalty(), 100f),
            "penalty=" + livePenalty());

        // 5. NEGATIVE CONTROL. Every check above would also pass if the resolving properties simply
        // ignored DebugFlags and returned the consts -- checks 1 and 4 trivially, and 2/3 are only
        // meaningful if the override path is live. Assert the two are genuinely different readings
        // of the same knob: with an override in force the resolved value must NOT be the const.
        run("?aiscanrows=7&aicrosspenalty=1.25");
        Check("resolved != baked while an override is in force",
            !Equals(liveRows(), bakedRows) && !Equals(livePenalty(), bakedPenalty)
            && Equals(liveRows(), 7) && Equals(livePenalty(), 1.25f),
            "rows=" + liveRows() + " vs const " + bakedRows
            + ", penalty=" + livePenalty() + " vs const " + bakedPenalty);

        // Hand the process back in the state it was found in. Parse can only ASSIGN, never clear,
        // so a Probe* added after this one would otherwise inherit rows=7 / penalty=1.25 with no
        // way to reach the defaults -- and would be measuring an override it never set.
        flags.GetProperty("AiWallScanRows", anyStatic).SetValue(null, null);
        flags.GetProperty("AiWallCrossPenalty", anyStatic).SetValue(null, null);
        Check("case set leaves no override behind",
            Equals(liveRows(), bakedRows) && Equals(livePenalty(), bakedPenalty),
            "rows=" + liveRows() + " penalty=" + livePenalty());
        return 0;
    }

    // Card 48b7c6b1 -- the ?ai* TUNING knobs' REJECTION diagnostic. Fourteen flags parsed as
    // `TryParse` plus an optional range guard, with no else, so `?aireact=420x` left the baked
    // default in force and said nothing. That is the failure card 6eb8dc9e named for ?flyspider*:
    // a run that measures the DEFAULT path while carrying the label of the variant under test --
    // and these fourteen are the ones whose readings get published as sweep rows, where a
    // silently-ignored value reads as "the knob did nothing".
    //
    // NOT every flag whose name starts with "ai": `?aifriends=<0-3>` (a co-op soak seam, not a
    // tuning knob) is still silent, and so is the boolean `?aiplayer`/`?aibench` pair, which
    // cannot have a bad value. Say "the 14 tuning knobs", never "the whole ?ai* family".
    //
    // Silence is invisible in any frame or number, so the assertion has to be made about the
    // OUTPUT. Three legs per flag, driven through the real DebugFlags.Parse:
    //   1. a valid value lands AND reports no rejection  (the negative control -- a helper that
    //      printed unconditionally, or an else placed on the wrong branch, fails here and only
    //      here; note Parse tails into Hint() on these queries, so the capture is never EMPTY);
    //   2. an unparseable value changes nothing, is reported, and the "staying on" clause names
    //      THE VALUE JUST SET rather than the baked default -- the part of the ?flyspider*
    //      precedent that is easy to get wrong, since Parse never resets a property and a repeated
    //      flag must keep the earlier valid value;
    //   3. a NEGATIVE value -- parseable but refused by the range guard, i.e. the second way into
    //      the else, which a TryParse-only test would miss.
    // Then the two per-tier knobs' wording, which cannot be a number (see below).
    private static int ProbeAiFlagRejection(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        if (parse == null)
        {
            Console.WriteLine("FAIL: could not reflect DebugFlags.Parse -- renamed or moved?");
            return 2;
        }

        // Parse one query and hand back everything it printed. Parse tails into Hint() when nothing
        // in `Active` is set, so a capture is usually two lines; the diagnostic is the first.
        Func<string, string> run = query => RunParse(parse, query);
        Func<string, object> get = name => flags.GetProperty(name, anyStatic).GetValue(null);
        Action<string, object> set = (name, v) => flags.GetProperty(name, anyStatic).SetValue(null, v);

        // flag, the DebugFlags property it writes, a valid value (chosen != the baked default, so
        // leg 2's "names the value in force" claim is not vacuous), what that value must read back
        // as, and the baked default as it renders in a message -- which must NOT appear.
        var rows = new[]
        {
            new { Flag = "aismooth",       Prop = "AiSteerSmoothMs",       Good = "111", Want = (object)111f,  Baked = "90"   },
            new { Flag = "aismoothurgent", Prop = "AiSteerSmoothUrgentMs", Good = "22",  Want = (object)22f,   Baked = "15"   },
            new { Flag = "aipark",         Prop = "AiParkDemand",          Good = "3",   Want = (object)3f,    Baked = "0.95" },
            new { Flag = "aireact",        Prop = "AiWallReactionMs",      Good = "333", Want = (object)333f,  Baked = "420"  },
            new { Flag = "aigapmargin",    Prop = "AiGapSwitchMargin",     Good = "7",   Want = (object)7f,    Baked = "1.5"  },
            new { Flag = "aiscanrows",     Prop = "AiWallScanRows",        Good = "9",   Want = (object)9,     Baked = ""     },
            new { Flag = "aicrosspenalty", Prop = "AiWallCrossPenalty",    Good = "33",  Want = (object)33f,   Baked = ""     },
            new { Flag = "aithreatlead",   Prop = "AiThreatLeadMs",        Good = "555", Want = (object)555f,  Baked = "700"  },
            new { Flag = "aibossbias",     Prop = "AiPriorityBias",        Good = "0.75",Want = (object)0.75f, Baked = "0.45" },
            new { Flag = "aiaim",          Prop = "AiAimSpreadRad",        Good = "2",   Want = (object)2f,    Baked = ""     },
            new { Flag = "aifieldpx",      Prop = "AiThreatFieldPx",       Good = "321", Want = (object)321f,  Baked = ""     },
            new { Flag = "aifieldsize",    Prop = "AiThreatFieldSize",     Good = "6",   Want = (object)6f,    Baked = "1.8"  },
            new { Flag = "aifieldfall",    Prop = "AiThreatFieldFalloff",  Good = "8",   Want = (object)8f,    Baked = "3"    },
            new { Flag = "aiff",           Prop = "AiFastForward",         Good = "7",   Want = (object)7,     Baked = ""     },
        };
        // Baked "" = no default-absence check available for that row: aiscanrows/aicrosspenalty
        // bake 4, aifieldfall bakes 3 and aiff sits at 0, all single digits that occur inside the
        // "(expected ...)" clause or the value being quoted back, so the check would fire on text
        // that is not the default at all. aiaim/aifieldpx have no single baked number (per tier).

        // Bind every property up front and fail LOUDLY, the way the other case sets do -- a
        // renamed override would otherwise surface as a puzzling value mismatch inside one leg.
        string missing = null;
        foreach (var row in rows)
        {
            if (flags.GetProperty(row.Prop, anyStatic) == null) { missing ??= row.Prop; }
        }
        if (missing != null)
        {
            Console.WriteLine("FAIL: could not reflect DebugFlags." + missing + " -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] DebugFlags ?ai* value rejection, all 14 knobs (card 48b7c6b1)");

        // One counter and its OWN first-problem detail per leg: a shared sink attaches the
        // diagnosis to whichever Check happens to print it, which in a mutation run put the only
        // useful line on a PASS and left the FAIL as a bare count.
        int landed = 0, quiet = 0, reported = 0, named = 0, negatives = 0;
        string badLanded = null, badQuiet = null, badReported = null, badNamed = null, badNeg = null;
        foreach (var row in rows)
        {
            // 1. valid value: lands, and reports no rejection.
            string outGood = run("?" + row.Flag + "=" + row.Good);
            if (Equals(get(row.Prop), row.Want)) { landed++; }
            else { badLanded ??= row.Flag + " read back " + get(row.Prop) + ", wanted " + row.Want; }
            if (!outGood.Contains("unknown")) { quiet++; }
            else { badQuiet ??= row.Flag + "=" + row.Good + " was REPORTED: " + FirstLine(outGood); }

            // 2. unparseable: unchanged, reported, names the value in force (not the baked default).
            string outBad = run("?" + row.Flag + "=xx");
            bool unchanged = Equals(get(row.Prop), row.Want);
            bool says = outBad.Contains("unknown ?" + row.Flag + "= value 'xx'") && outBad.Contains("-- ignored, staying on ");
            bool inForce = outBad.Contains(row.Good) && (row.Baked.Length == 0 || !outBad.Contains(row.Baked));
            if (unchanged && says) { reported++; }
            else { badReported ??= row.Flag + ": prop=" + get(row.Prop) + " said: " + FirstLine(outBad); }
            if (inForce) { named++; }
            else { badNamed ??= row.Flag + " does not name the in-force " + row.Good + ": " + FirstLine(outBad); }

            // 3. negative: parses, fails the range guard, must be refused the same way. ?aiff is
            //    the one flag with no range guard (it CLAMPS to 0..64), so -1 is accepted there.
            if (row.Flag != "aiff")
            {
                string outNeg = run("?" + row.Flag + "=-1");
                if (Equals(get(row.Prop), row.Want) && outNeg.Contains("unknown ?" + row.Flag + "=")) { negatives++; }
                else { badNeg ??= row.Flag + "=-1: prop=" + get(row.Prop) + " said: " + FirstLine(outNeg); }
            }
        }
        Check("a valid value lands on all 14", landed == rows.Length,
            landed + "/" + rows.Length + (badLanded != null ? "; " + badLanded : ""));
        Check("a valid value reports NO rejection (the control)", quiet == rows.Length,
            quiet + "/" + rows.Length + " clean -- a helper that printed unconditionally fails here"
            + (badQuiet != null ? "; " + badQuiet : ""));
        Check("a bad value is refused AND reported on all 14", reported == rows.Length,
            reported + "/" + rows.Length + (badReported != null ? "; " + badReported : ""));
        Check("the message names the value IN FORCE, not the baked default", named == rows.Length,
            named + "/" + rows.Length + " (Parse never resets a property, so a repeated flag keeps the"
            + " earlier value)" + (badNamed != null ? "; " + badNamed : ""));
        Check("a NEGATIVE value is refused AND reported on all 13 guarded flags", negatives == rows.Length - 1,
            negatives + "/" + (rows.Length - 1) + " (?aiff has no range guard -- it clamps)"
            + (badNeg != null ? "; " + badNeg : ""));

        // ?aiaim and ?aifieldpx resolve through PlayerShip.AiSkillByDifficulty at PLAY time, off a
        // difficulty this parse has not settled, so with no override standing there is no number to
        // name -- and a diagnostic that can state the wrong condition is worse than one that states
        // none. They must say which table is in force instead. (With an override standing they name
        // it like the rest, which the table above already covered.)
        set("AiAimSpreadRad", null);
        set("AiThreatFieldPx", null);
        string outAim = run("?aiaim=xx");
        string outPx = run("?aifieldpx=xx");
        Check("per-tier knobs name the SKILL ROW when no override stands",
            get("AiAimSpreadRad") == null && get("AiThreatFieldPx") == null
            && outAim.Contains("staying on the per-tier skill row")
            && outPx.Contains("staying on the per-tier skill row"),
            "aiaim said: " + FirstLine(outAim) + " | aifieldpx said: " + FirstLine(outPx));

        // Hand the process back as it was found. Parse can only ASSIGN, so a Probe* added after
        // this one would otherwise inherit fourteen overrides with no way to reach the defaults.
        foreach (var row in rows)
        {
            set(row.Prop, row.Flag == "aiff" ? (object)0 : null);
        }
        int leaked = 0;
        foreach (var row in rows)
        {
            object v = get(row.Prop);
            if (row.Flag == "aiff" ? !Equals(v, 0) : v != null) { leaked++; }
        }
        Check("case set leaves no override behind", leaked == 0, leaked + " still set");
        return 0;
    }

    // Card 4e401005 -- the SWEEP: every other value-carrying flag in DebugFlags.cs, after cards
    // 6eb8dc9e (?flyspider*) and 48b7c6b1 (the 14 ?ai* knobs) established the convention. Same
    // failure mode as those two, over ~80 more flags: `?wallsidetile=4x` ran the baked tiling
    // while the run carried the label of the variant under test.
    //
    // WHY THIS SET IS SHAPED DIFFERENTLY FROM ProbeAiFlagRejection. That one knows each knob's
    // baked default (PlayerShip exposes them as public consts) and asserts the message names the
    // in-force value rather than that default. Here the defaults live in a dozen different game
    // classes -- Wall, HoloSim, WebcamLevel.Tunings[], Spider, ... -- and are not reachable from
    // Parse at all, which is exactly why these call sites say "the shipped default" instead of a
    // number they would have to guess. So the in-force claim is proven WITHOUT restating any
    // constant, by READING BACK what the flag actually set and requiring the message to name that:
    //   leg 0  no override standing  -> the message says "the shipped default"   (nullable only)
    //   leg 1  a valid value         -> it lands, and NO rejection is reported    (the control)
    //   leg 2  an unparseable value  -> unchanged, reported, names the READ-BACK value from leg 1
    //                                   and no longer says "the shipped default"
    //   leg 3  a negative value      -> the same, for the flags whose guard refuses one
    // Reading back also sidesteps every inline clamp (?holofilter caps at 2, ?aifriends at 3, ...)
    // without this file having to know a single one of them.
    private static int ProbeFlagRejectionSweep(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        if (parse == null)
        {
            Console.WriteLine("FAIL: could not reflect DebugFlags.Parse -- renamed or moved?");
            return 2;
        }

        Func<string, string> run = query => RunParse(parse, query);

        // Flag, the DebugFlags property it writes, a value its guard accepts, and whether that
        // guard refuses a negative (derived from the "(expected ...)" clause the call site states,
        // so the two cannot drift). Good values are deliberately distinctive -- 0.375 / 9 -- so
        // leg 2's "the message contains it" is not satisfied by a digit in the expected clause.
        var rows = new[]
        {
            new { Flag = "slowmotraildecay", Prop = "SlowmoTrailDecay", Good = "0.375", RejectsNeg = false },
            new { Flag = "slowmotrailstrength", Prop = "SlowmoTrailStrength", Good = "0.375", RejectsNeg = false },
            new { Flag = "holofilter", Prop = "HoloFilter", Good = "0.375", RejectsNeg = false },
            new { Flag = "holoburst", Prop = "HoloBurst", Good = "0.375", RejectsNeg = false },
            new { Flag = "hologreen", Prop = "HoloGreen", Good = "0.375", RejectsNeg = false },
            new { Flag = "hologreenpulse", Prop = "HoloGreenPulse", Good = "0.375", RejectsNeg = false },
            new { Flag = "holostaticrate", Prop = "HoloStaticRate", Good = "0.375", RejectsNeg = false },
            new { Flag = "blastactive", Prop = "BlastActiveAlpha", Good = "0.375", RejectsNeg = false },
            new { Flag = "blasthit", Prop = "BlastHitFactor", Good = "0.375", RejectsNeg = true },
            new { Flag = "reticlesize", Prop = "ReticleSize", Good = "0.375", RejectsNeg = true },
            new { Flag = "blastloop", Prop = "BlastLoopSeconds", Good = "0.375", RejectsNeg = true },
            new { Flag = "lazerchargescale", Prop = "LazerChargeScale", Good = "0.375", RejectsNeg = true },
            new { Flag = "lazercapscale", Prop = "LazerCapScale", Good = "0.375", RejectsNeg = true },
            new { Flag = "lazerarcs", Prop = "LazerArcRate", Good = "0.375", RejectsNeg = true },
            new { Flag = "lazertendrilspeed", Prop = "LazerTendrilSpeed", Good = "0.375", RejectsNeg = true },
            new { Flag = "lazerarclife", Prop = "LazerArcLife", Good = "0.375", RejectsNeg = true },
            new { Flag = "connectorbolts", Prop = "ConnectorBoltCount", Good = "9", RejectsNeg = true },
            new { Flag = "connectorarcs", Prop = "ConnectorArcRate", Good = "0.375", RejectsNeg = true },
            new { Flag = "connectorjitter", Prop = "ConnectorJitter", Good = "0.375", RejectsNeg = true },
            new { Flag = "connectorpulse", Prop = "ConnectorPulse", Good = "0.375", RejectsNeg = true },
            new { Flag = "connectorglow", Prop = "ConnectorGlow", Good = "0.375", RejectsNeg = true },
            new { Flag = "wall3dbands", Prop = "Wall3DBands", Good = "9", RejectsNeg = true },
            new { Flag = "walldepth", Prop = "WallDepth", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallfog", Prop = "WallFog", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallsidedark", Prop = "WallSideDark", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallsidetile", Prop = "WallSideTile", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallfacelight", Prop = "WallFaceLight", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallfaceangle", Prop = "WallFaceAngle", Good = "0.375", RejectsNeg = false },
            new { Flag = "walltoplift", Prop = "WallTopLift", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallwisps", Prop = "WallWisps", Good = "0.375", RejectsNeg = true },
            new { Flag = "wallwispspeed", Prop = "WallWispSpeed", Good = "0.375", RejectsNeg = true },
            new { Flag = "wchearts", Prop = "WebcamHearts", Good = "9", RejectsNeg = true },
            new { Flag = "wckills", Prop = "WebcamKills", Good = "9", RejectsNeg = true },
            new { Flag = "wcsaucers", Prop = "WebcamSaucers", Good = "9", RejectsNeg = true },
            new { Flag = "wcsaucerspeed", Prop = "WebcamSaucerSpeed", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcplasmaspeed", Prop = "WebcamPlasmaSpeed", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcspawn", Prop = "WebcamSpawnInterval", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcarm", Prop = "WebcamArmDelay", Good = "0.375", RejectsNeg = true },
            new { Flag = "wccharge", Prop = "WebcamChargeTime", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcminemax", Prop = "WebcamMineMax", Good = "9", RejectsNeg = true },
            new { Flag = "wcminespawn", Prop = "WebcamMineSpawn", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcminelife", Prop = "WebcamMineLife", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcmothership", Prop = "WebcamMothership", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcmothershipfreeze", Prop = "WebcamMothershipFreeze", Good = "0.375", RejectsNeg = true },
            new { Flag = "wchitleeway", Prop = "WebcamHitLeeway", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcavoid", Prop = "WebcamAvoid", Good = "0.375", RejectsNeg = true },
            new { Flag = "wcreturndelay", Prop = "WebcamReturnDelay", Good = "0.375", RejectsNeg = true },
            new { Flag = "huestart", Prop = "HueStart", Good = "0.375", RejectsNeg = false },
            new { Flag = "hueend", Prop = "HueEnd", Good = "0.375", RejectsNeg = false },
            new { Flag = "hue", Prop = "HueTarget", Good = "0.375", RejectsNeg = false },
            new { Flag = "hueloop", Prop = "HueLoopSeconds", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderhelpercycles", Prop = "SpiderHelperCycles", Good = "9", RejectsNeg = true },
            new { Flag = "spiderhelperhp", Prop = "SpiderHelperHitPoints", Good = "9", RejectsNeg = true },
            new { Flag = "spiderhelperhovery", Prop = "SpiderHelperHoverY", Good = "0.375", RejectsNeg = false },
            new { Flag = "spiderhelperspeed", Prop = "SpiderHelperSpeed", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderhelperwindup", Prop = "SpiderHelperWindupSeconds", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderhelperfire", Prop = "SpiderHelperFireSeconds", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderhelperlead", Prop = "SpiderHelperFireLead", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderhelperenterpower", Prop = "SpiderHelperEnterPower", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderbosshp", Prop = "SpiderBossHp", Good = "9", RejectsNeg = true },
            new { Flag = "aifriends", Prop = "AiFriends", Good = "9", RejectsNeg = false },
            new { Flag = "netlocal", Prop = "NetLocal", Good = "9", RejectsNeg = false },
            new { Flag = "netlag", Prop = "NetLagMs", Good = "0.375", RejectsNeg = true },
            new { Flag = "netloss", Prop = "NetLossPct", Good = "0.375", RejectsNeg = true },
            new { Flag = "castbrainscale", Prop = "CastBrainScale", Good = "0.375", RejectsNeg = true },
            new { Flag = "castbrainfps", Prop = "CastBrainFps", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderloop", Prop = "SpiderLoopSeconds", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderjumpframe", Prop = "SpiderJumpFrame", Good = "0.375", RejectsNeg = false },
            new { Flag = "spiderlandframe", Prop = "SpiderLandFrame", Good = "0.375", RejectsNeg = false },
            new { Flag = "spiderjumpx", Prop = "SpiderJumpX", Good = "0.375", RejectsNeg = false },
            new { Flag = "spidershadowx", Prop = "SpiderShadowX", Good = "0.375", RejectsNeg = false },
            new { Flag = "spidershadowy", Prop = "SpiderShadowY", Good = "0.375", RejectsNeg = false },
            new { Flag = "spidershadowscale", Prop = "SpiderShadowScale", Good = "0.375", RejectsNeg = true },
            new { Flag = "spiderairx", Prop = "SpiderAirX", Good = "0.375", RejectsNeg = false },
            new { Flag = "spiderairy", Prop = "SpiderAirY", Good = "0.375", RejectsNeg = false },
            new { Flag = "spiderphase", Prop = "SpiderPhase", Good = "0.375", RejectsNeg = false },
            new { Flag = "frame", Prop = "HarnessFrame", Good = "9", RejectsNeg = false },
            new { Flag = "size", Prop = "HarnessScale", Good = "0.375", RejectsNeg = true },
            new { Flag = "rotation", Prop = "HarnessRot", Good = "0.375", RejectsNeg = false },
            new { Flag = "animfps", Prop = "HarnessFps", Good = "0.375", RejectsNeg = true },
        };

        Console.WriteLine("[logic_probe] DebugFlags value rejection, the remaining " + rows.Length
            + " flags (card 4e401005)");

        string missing = null;
        foreach (var row in rows)
        {
            if (flags.GetProperty(row.Prop, anyStatic) == null) { missing ??= row.Flag + " -> " + row.Prop; }
        }
        if (missing != null)
        {
            Console.WriteLine("FAIL: could not reflect DebugFlags." + missing + " -- renamed or moved?");
            return 2;
        }

        // Every property this set touches, as it stood on entry -- the restore at the end puts
        // them back from here (the non-nullable ones have no "unset" to null out).
        var entry = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var row in rows)
        {
            entry[row.Prop] = flags.GetProperty(row.Prop, anyStatic).GetValue(null);
        }

        int shipped = 0, shippedN = 0, landed = 0, quiet = 0, reported = 0, named = 0, negatives = 0, negN = 0;
        string badShipped = null, badLanded = null, badQuiet = null, badReported = null, badNamed = null, badNeg = null;
        foreach (var row in rows)
        {
            PropertyInfo p = flags.GetProperty(row.Prop, anyStatic);
            bool nullable = Nullable.GetUnderlyingType(p.PropertyType) != null;

            // 0. Nothing standing yet -> the message must SAY so rather than invent a number. This
            //    runs before anything sets this flag, which is why the leg order matters.
            if (nullable)
            {
                shippedN++;
                string outFresh = run("?" + row.Flag + "=xx");
                if (p.GetValue(null) == null && outFresh.Contains("staying on the shipped default")) { shipped++; }
                else { badShipped ??= row.Flag + " with no override said: " + FirstLine(outFresh); }
            }

            // 1. A valid value lands, and reports no rejection. Asserted as a CHANGE from what
            //    stood before, not as "not null": the seven non-nullable properties box to a
            //    float/int that can never BE null, so a null test passes them vacuously -- and
            //    since leg 2 derives its expectation from this same read, a deleted assignment
            //    would then satisfy every check in the set.
            object before = p.GetValue(null);
            string outGood = run("?" + row.Flag + "=" + row.Good);
            object landedVal = p.GetValue(null);
            string inForce = Convert.ToString(landedVal, System.Globalization.CultureInfo.InvariantCulture);
            if (!Equals(landedVal, before)) { landed++; }
            else { badLanded ??= row.Flag + "=" + row.Good + " did not change the override (still " + before + ")"; }
            if (!outGood.Contains("unknown")) { quiet++; }
            else { badQuiet ??= row.Flag + "=" + row.Good + " was REPORTED: " + FirstLine(outGood); }

            // 2. An unparseable value: unchanged, reported, and it names what leg 1 just set.
            string outBad = run("?" + row.Flag + "=xx");
            bool unchanged = Equals(p.GetValue(null), landedVal);
            bool says = outBad.Contains("unknown ?" + row.Flag + "= value 'xx'")
                && outBad.Contains("-- ignored, staying on ");
            if (unchanged && says) { reported++; }
            else { badReported ??= row.Flag + ": prop=" + p.GetValue(null) + " said: " + FirstLine(outBad); }
            if (outBad.Contains(inForce) && !outBad.Contains("the shipped default")) { named++; }
            else { badNamed ??= row.Flag + " does not name the in-force " + inForce + ": " + FirstLine(outBad); }

            // 3. A negative value -- parseable, refused by the range guard. The second way into the
            //    else, which a TryParse-only test would never reach.
            if (row.RejectsNeg)
            {
                negN++;
                string outNeg = run("?" + row.Flag + "=-1");
                if (Equals(p.GetValue(null), landedVal) && outNeg.Contains("unknown ?" + row.Flag + "=")) { negatives++; }
                else { badNeg ??= row.Flag + "=-1: prop=" + p.GetValue(null) + " said: " + FirstLine(outNeg); }
            }
        }
        Check("with no override standing, the message says so", shipped == shippedN,
            shipped + "/" + shippedN + " nullable flags" + (badShipped != null ? "; " + badShipped : ""));
        Check("a valid value lands on all " + rows.Length, landed == rows.Length,
            landed + "/" + rows.Length + (badLanded != null ? "; " + badLanded : ""));
        Check("a valid value reports NO rejection (the control)", quiet == rows.Length,
            quiet + "/" + rows.Length + " clean" + (badQuiet != null ? "; " + badQuiet : ""));
        Check("a bad value is refused AND reported on all " + rows.Length, reported == rows.Length,
            reported + "/" + rows.Length + (badReported != null ? "; " + badReported : ""));
        Check("the message names the value IN FORCE, not the shipped default", named == rows.Length,
            named + "/" + rows.Length + (badNamed != null ? "; " + badNamed : ""));
        Check("a NEGATIVE value is refused AND reported wherever a guard refuses one",
            negatives == negN, negatives + "/" + negN + " guarded flags"
            + (badNeg != null ? "; " + badNeg : ""));

        // Hand the process back as it was found -- Parse can only ASSIGN, so a Probe* added after
        // this one would otherwise inherit eighty overrides with no way to reach the defaults.
        // The non-nullable seven are restored from the values captured at entry, not nulled: they
        // have no "unset", and leaving them swept is how the alias check below used to print
        // `staying on 0.375` for a flag it had never touched.
        foreach (var row in rows)
        {
            flags.GetProperty(row.Prop, anyStatic).SetValue(null, entry[row.Prop]);
        }
        int leaked = 0;
        foreach (var row in rows)
        {
            if (!Equals(flags.GetProperty(row.Prop, anyStatic).GetValue(null), entry[row.Prop])) { leaked++; }
        }
        Check("case set leaves no override behind", leaked == 0, leaked + " still set");

        // The five whose value space is not a number, so their "expected" clause and their in-force
        // wording had to be written by hand. Each states a DIFFERENT thing in place of a number, so
        // a copy-paste slip between them would show up nowhere else.
        string outDir = run("?wcmothershipdir=verticl");
        Check("?wcmothershipdir= names the orientation roll",
            outDir.Contains("expected vertical or horizontal")
            && outDir.Contains("staying on the random orientation roll"), FirstLine(outDir));
        string outDiff = run("?difficulty=2");
        Check("?difficulty= refuses the ordinal and says which spelling it wants",
            outDiff.Contains("a tier name") && outDiff.Contains("not a number")
            && outDiff.Contains("staying on the saved menu difficulty"), FirstLine(outDiff));
        string outWcd = run("?wcdiff=Hrd");
        Check("?wcdiff= names the level's own tier", outWcd.Contains("staying on the level's own tier"),
            FirstLine(outWcd));
        string outCol = run("?wallfogcolor=nothex");
        Check("?wallfogcolor= states a hex example", outCol.Contains("a hex colour like #4080c8")
            && outCol.Contains("staying on the shipped default"), FirstLine(outCol));
        string outHarness = run("?harness");
        Check("a BARE ?harness is reported, not silently a normal boot",
            outHarness.Contains("unknown ?harness= value ''")
            && outHarness.Contains("staying on no harness (a normal boot)"), FirstLine(outHarness));
        // ... and that an ALIASED spelling reports under the name the author actually typed, which
        // is why every call site passes `key` rather than a literal.
        string outAlias = run("?objscale=xx");
        Check("an alias reports under the spelling used (?objscale, not ?size)",
            outAlias.Contains("unknown ?objscale="), FirstLine(outAlias));

        // The four sites that do NOT report from a plain `else`, each of which was a behaviour bug
        // rather than only a missing message -- so each is asserted on the STATE as well as the
        // text.
        //
        // ?shake and ?bgfreeze accept a number OR an on/off spelling, and used to route anything
        // else through IsOn, i.e. read a typo as OFF: the run then measured no shake / an
        // unfrozen background while carrying the label of the sweep it was meant to be.
        object shakeBefore = flags.GetProperty("ShakeAmount", anyStatic).GetValue(null);
        string outShake = run("?shake=1.5O");
        Check("?shake= typo is reported and does NOT turn shake off",
            Equals(flags.GetProperty("ShakeAmount", anyStatic).GetValue(null), shakeBefore)
            && outShake.Contains("unknown ?shake=") && outShake.Contains("a number 0..3, or on/off"),
            "shake=" + flags.GetProperty("ShakeAmount", anyStatic).GetValue(null) + " said: " + FirstLine(outShake));
        string outShakeOff = run("?shake=off");
        Check("?shake=off still means off (the on/off spellings are untouched)",
            Equals(flags.GetProperty("ShakeAmount", anyStatic).GetValue(null), 0f) && !outShakeOff.Contains("unknown"),
            "shake=" + flags.GetProperty("ShakeAmount", anyStatic).GetValue(null));
        flags.GetProperty("ShakeAmount", anyStatic).SetValue(null, shakeBefore);

        run("?bgfreeze=250");
        string outFreeze = run("?bgfreeze=40O");
        Check("?bgfreeze= typo is reported and does NOT unfreeze",
            Equals(flags.GetProperty("BgFreeze", anyStatic).GetValue(null), 250f)
            && outFreeze.Contains("unknown ?bgfreeze="),
            "bgfreeze=" + flags.GetProperty("BgFreeze", anyStatic).GetValue(null) + " said: " + FirstLine(outFreeze));
        run("?bgfreeze=false");
        Check("?bgfreeze=false still disables it", flags.GetProperty("BgFreeze", anyStatic).GetValue(null) == null,
            "bgfreeze=" + flags.GetProperty("BgFreeze", anyStatic).GetValue(null));

        // ?pos reports per AXIS, so a half-usable pair says which half was dropped -- and the
        // usable half must still land.
        run("?pos=123,456");
        string outPos = run("?pos=400,3O0");
        Check("?pos= reports the bad AXIS and keeps the good one",
            Equals(flags.GetProperty("HarnessX", anyStatic).GetValue(null), 400f)
            && Equals(flags.GetProperty("HarnessY", anyStatic).GetValue(null), 456f)
            && outPos.Contains("unknown ?pos= value '3O0'") && outPos.Contains("for y in ?pos=x,y"),
            "x=" + flags.GetProperty("HarnessX", anyStatic).GetValue(null)
            + " y=" + flags.GetProperty("HarnessY", anyStatic).GetValue(null) + " said: " + FirstLine(outPos));
        flags.GetProperty("HarnessX", anyStatic).SetValue(null, null);
        flags.GetProperty("HarnessY", anyStatic).SetValue(null, null);

        // A bare ?level used to dereference a null `val`: the NRE took the headless host down and,
        // in the browser, was caught one level up as a single "flag read failed" line that
        // silently dropped EVERY LATER FLAG in the query. So the assertion that matters is not the
        // message -- it is that a flag after it still lands.
        string outBareLevel = run("?level&aiscanrows=5");
        Check("a bare ?level does not throw, and later flags still parse",
            outBareLevel.Contains("unknown level ''")
            && Equals(flags.GetProperty("AiWallScanRows", anyStatic).GetValue(null), 5),
            "scanrows=" + flags.GetProperty("AiWallScanRows", anyStatic).GetValue(null)
            + " said: " + FirstLine(outBareLevel));
        flags.GetProperty("AiWallScanRows", anyStatic).SetValue(null, null);

        return 0;
    }

    // Card 64967ea5 -- CollisionBox's box-vs-LINE predicate. The card collapsed a duplicated
    // `(val).Intersects(val2)` (the ray-box test ran TWICE per call, once for .HasValue and once
    // for the comparison) into one cached call. That is a COST fix, not a behaviour fix: the
    // answer is unchanged by construction, so there is no old-vs-new behaviour to contrast and
    // the usual "run the pre-card policy as a negative control" shape does not apply here.
    //
    // What this set is for, then, is the OTHER half of the claim -- that collapsing the call did
    // not quietly change the predicate. It is a regression oracle, and it is run DIFFERENTIALLY:
    // point it at a merge-base build of EvilAliensWeb.dll and at the branch build and the two
    // verdict tables must be identical. (The "one call, not two" half is proven by
    // verify_decompiled_diff.py, which shows the single surviving call site.)
    //
    // Expectations are derived from ray-AABB geometry, never restated from the code. The
    // predicate has exactly two terms -- "the ray meets the box at all" and "it does so within
    // the line's Length" -- so the set is built as PAIRS that isolate one term each: see the
    // sensitivity note at the bottom for why that pairing IS the evidence and why an explicit
    // control section here would only restate it.
    private static int ProbeCollisionBoxLine(Assembly asm)
    {
        Type boxType = asm.GetType("EvilAliens.CollisionBox", true);
        Type lineType = asm.GetType("EvilAliens.CollisionLine", true);
        Type iface = asm.GetType("EvilAliens.ICollisionType", true);
        PropertyInfo topLeft = boxType.GetProperty("TopLeft");
        if (topLeft == null)
        {
            Console.WriteLine("FAIL: could not reflect CollisionBox.TopLeft -- renamed or moved?");
            return 2;
        }
        Type vec2 = topLeft.PropertyType;
        ConstructorInfo boxCtor = boxType.GetConstructor(new[] { vec2, vec2 });
        ConstructorInfo lineCtor = lineType.GetConstructor(new[] { vec2, typeof(float), typeof(float) });
        // TestCollision is the PUBLIC entry point and dispatches to the private TestCollisionLine
        // for a CollisionLine, so the edited method is reached without binding a private member.
        MethodInfo test = boxType.GetMethod("TestCollision", new[] { iface });
        if (boxCtor == null || lineCtor == null || test == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (CollisionBox(Vector2,Vector2)="
                + (boxCtor != null) + " CollisionLine(Vector2,float,float)=" + (lineCtor != null)
                + " TestCollision(ICollisionType)=" + (test != null) + ") -- renamed or moved?");
            return 2;
        }

        Func<float, float, object> vec = (x, y) => Activator.CreateInstance(vec2, new object[] { x, y });
        // The box under test: design-space (100,100)..(200,200).
        object box = boxCtor.Invoke(new object[] { vec(100f, 100f), vec(200f, 200f) });
        // CollisionLine's DirectionalVector is MyMath.AngleToVector(direction) = (cos, sin), a UNIT
        // vector, so the ray parameter Intersects returns is a distance in world units and compares
        // directly against Length. Screen Y grows downward; none of the cases below depend on that
        // beyond naming.
        const float right = 0f;
        const float down = (float)Math.PI / 2f;
        const float left = (float)Math.PI;
        const float downRight = (float)Math.PI / 4f;
        Func<float, float, float, float, bool> hits = (ox, oy, len, dir) =>
            (bool)test.Invoke(box, new object[] { lineCtor.Invoke(new object[] { vec(ox, oy), len, dir }) });

        Console.WriteLine("[logic_probe] CollisionBox vs CollisionLine (card 64967ea5)");

        // 1. Geometry: does the ray meet the box at all, given a length that cannot be the reason.
        // Distances are chosen well clear of the box edges so no case rides a float boundary.
        Check("aimed at the box from the left, long enough", hits(0f, 150f, 1000f, right), "enters at t=100");
        Check("aimed AWAY from the box", !hits(0f, 150f, 1000f, left), "box is behind the origin");
        Check("passes above the box", !hits(0f, 50f, 1000f, right), "y=50 never enters [100,200]");
        Check("passes below the box", !hits(0f, 300f, 1000f, right), "y=300 never enters [100,200]");
        Check("aimed at the box from above", hits(150f, 0f, 1000f, down), "enters at t=100");
        Check("origin INSIDE the box", hits(150f, 150f, 10f, right), "a ray starting inside meets it at t=0");
        Check("diagonal through the box", hits(50f, 60f, 1000f, downRight), "crosses x=100 at y=110");

        // 2. The length term, in isolation: same origin and heading as a case above, shortened so
        // the box is out of reach. A predicate that dropped `< collisionLine.Length` answers these
        // exactly as it answers their long counterparts.
        Check("too short to reach it (from the left)", !hits(0f, 150f, 50f, right), "needs 100, has 50");
        Check("too short to reach it (from above)", !hits(150f, 0f, 50f, down), "needs 100, has 50");
        Check("too short to reach it (diagonal)", !hits(50f, 60f, 50f, downRight), "needs ~70.7, has 50");
        // ... and the boundary is generous rather than exact -- just past the entry distance is a
        // hit. Kept off the exact tie (100 vs 100), which no caller depends on and which would
        // pin a float comparison this card has no business fixing.
        Check("just long enough", hits(0f, 150f, 100.5f, right), "needs 100, has 100.5");

        // SENSITIVITY comes from the PAIRING above, and is not a separate section. Sections 1 and
        // 2 are three matched pairs (left / above / diagonal) whose members differ in exactly one
        // input -- Length -- and are asserted to opposite answers, so a predicate that dropped
        // `< collisionLine.Length` cannot satisfy both halves; section 1's aimed-at vs aimed-away
        // and its two passes-by cases do the same for the intersection term. Mutation-tested at
        // HEAD: dropping the length term turns 3 lines FAIL, an always-true predicate turns 6.
        //
        // An earlier revision added three explicit `hits(A) != hits(B)` CONTROL lines on top of
        // that. They were REDUNDANT and are deliberately gone: both sides of each were already
        // individually asserted above, so no mutant could fail a control without first failing one
        // of those, and their only real effect was to inflate the mutation counts (4 and 9) into
        // looking like more discrimination than the set has. If a control is ever added back here,
        // it has to compare a pair whose individual answers are NOT pinned elsewhere in the set --
        // otherwise it is a restatement, not evidence.
        return 0;
    }

    // Card 0d166364 -- LevelArt.ScreenshotPath is the ONE membership list. It used to have a twin,
    // a HasCarouselEntry predicate spelling out the same twelve levels, with a
    // `_ => "GFX/Screenshots/level1empty"` default on the paths; a level in the predicate but
    // missed in the table fell through that default, ScreenshotSaver deduped the duplicate away,
    // and the carousel silently drew Mission 1's art. The collapse makes the drift unwritable, and
    // this is where that claim is checked -- a pure static lookup, so no browser and no rig.
    //
    // A lookup table can only be restated, so the twelve rows below ARE a restatement and prove
    // little on their own. The evidence is in the other three sections: the derivation
    // (StockShots is the distinct non-null paths and nothing else), the negative control (the
    // pre-card default is GONE, which is the mutation this card could regress into), and the
    // off-the-wire values, which no test in the repo covered before.
    private static int ProbeLevelArt(Assembly asm)
    {
        Type levelArt = asm.GetType("EvilAliens.LevelArt", true);
        Type levels = asm.GetType("EvilAliens.Levels", true);
        MethodInfo path = levelArt.GetMethod("ScreenshotPath", new[] { levels });
        FieldInfo defaultPath = levelArt.GetField("DefaultScreenshotPath",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (path == null || defaultPath == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (ScreenshotPath(Levels)="
                + (path != null) + " DefaultScreenshotPath=" + (defaultPath != null)
                + ") -- renamed or moved?");
            return 2;
        }
        string theDefault = (string)defaultPath.GetValue(null);
        Func<string, string> pathOf = name =>
            (string)path.Invoke(null, new[] { Enum.Parse(levels, name) });
        // An out-of-enum value is a legal input here: a listed game's Level arrives as an int off
        // the wire, so a NEWER peer's build can name a level this one has never heard of.
        Func<int, string> pathOfRaw = raw =>
            (string)path.Invoke(null, new[] { Enum.ToObject(levels, raw) });

        Console.WriteLine("[logic_probe] LevelArt.ScreenshotPath (card 0d166364)");

        // 1. The twelve carousel levels and their art. A restatement -- see the header.
        Check("Level1", pathOf("Level1") == "GFX/Screenshots/level1empty", pathOf("Level1"));
        Check("Level2", pathOf("Level2") == "GFX/Screenshots/level2empty", pathOf("Level2"));
        Check("Level3", pathOf("Level3") == "GFX/Screenshots/level3empty", pathOf("Level3"));
        Check("SpaceDodge", pathOf("SpaceDodge") == "GFX/Screenshots/SpaceDodge", pathOf("SpaceDodge"));
        Check("Braineroids", pathOf("Braineroids") == "GFX/Screenshots/ss1", pathOf("Braineroids"));
        Check("ClassicAliens", pathOf("ClassicAliens") == "GFX/Screenshots/classicss", pathOf("ClassicAliens"));
        Check("Paratrooper", pathOf("Paratrooper") == "GFX/Screenshots/Paratrooper", pathOf("Paratrooper"));
        Check("OwnLevel", pathOf("OwnLevel") == "GFX/Screenshots/OwnLevel", pathOf("OwnLevel"));
        Check("CrazyGame", pathOf("CrazyGame") == "GFX/Screenshots/crazygamess", pathOf("CrazyGame"));
        Check("InsaneBossI", pathOf("InsaneBossI") == "GFX/Screenshots/InsaneBossI", pathOf("InsaneBossI"));
        Check("TeamChallenge", pathOf("TeamChallenge") == "GFX/Screenshots/teamchallengess", pathOf("TeamChallenge"));
        // WebcamAliens is called out because it is the asset the whole lineage of these cards is
        // about: the one ScreenshotSaver.Init originally missed (card 4d47c5ba), and the one a
        // General.ScreenshotEnabled-based membership test would drop again (card 8d6883f3).
        Check("WebcamAliens", pathOf("WebcamAliens") == "GFX/Screenshots/webcamss", pathOf("WebcamAliens"));

        // 2. The levels with no carousel slot answer NULL -- Tutorial launches from the main menu,
        // Demo1/2/3 are the attract rotation.
        Check("Tutorial has no art", pathOf("Tutorial") == null, pathOf("Tutorial") ?? "null");
        Check("Demo1 has no art", pathOf("Demo1") == null, pathOf("Demo1") ?? "null");
        Check("Demo2 has no art", pathOf("Demo2") == null, pathOf("Demo2") ?? "null");
        Check("Demo3 has no art", pathOf("Demo3") == null, pathOf("Demo3") ?? "null");

        // 3. OFF THE WIRE. Nothing in the repo covered these before this card; they are what the
        // deleted `_ =>` default was protecting, and the reason the fallback had to move to the
        // call sites rather than simply vanish.
        Check("an int beyond the enum has no art", pathOfRaw(9999) == null, pathOfRaw(9999) ?? "null");
        Check("a negative int has no art", pathOfRaw(-1) == null, pathOfRaw(-1) ?? "null");

        // 4. NEGATIVE CONTROL, and the only section that discriminates the mutation this card is
        // about: the pre-card behaviour returned DefaultScreenshotPath for every unmapped level
        // instead of null. Section 2 already pins those to null, so this states the property the
        // OLD code violated -- no level outside the table may answer the default, even though the
        // default's own string is still a legal answer for Level1, whose art it is. Mutation-
        // tested: restoring `_ => "GFX/Screenshots/level1empty"` turns these six FAIL, plus
        // section 2's four and section 3's two -- 12 in all. No other edit in the file can.
        Check("Tutorial is not the default", pathOf("Tutorial") != theDefault, "old code: " + theDefault);
        Check("Demo1 is not the default", pathOf("Demo1") != theDefault, "old code: " + theDefault);
        Check("Demo2 is not the default", pathOf("Demo2") != theDefault, "old code: " + theDefault);
        Check("Demo3 is not the default", pathOf("Demo3") != theDefault, "old code: " + theDefault);
        Check("9999 is not the default", pathOfRaw(9999) != theDefault, "old code: " + theDefault);
        Check("-1 is not the default", pathOfRaw(-1) != theDefault, "old code: " + theDefault);
        // ... and the default is still the right STRING for the two call sites that draw it.
        Check("the default is Mission 1's empty shot", theDefault == "GFX/Screenshots/level1empty", theDefault);

        // 5. THE DERIVATION, which is the actual subject: ScreenshotSaver.StockShots must be
        // exactly the distinct non-null paths, in enum order. This is NOT a restatement -- it
        // recomputes the set from ScreenshotPath and compares against the field the game really
        // warms, so a StockShots that grew a hardcoded entry back, lost one, or stopped deduping
        // fails here. (ScreenshotSaver's static init touches no engine service; it only walks the
        // enum and sizes an array.)
        Type saver = asm.GetType("EvilAliens.ScreenshotSaver", true);
        FieldInfo stockField = saver.GetField("StockShots",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (stockField == null)
        {
            Console.WriteLine("FAIL: could not reflect ScreenshotSaver.StockShots -- renamed or moved?");
            return 2;
        }
        string[] stock = (string[])stockField.GetValue(null);
        System.Collections.Generic.List<string> expected = new System.Collections.Generic.List<string>();
        foreach (object level in Enum.GetValues(levels))
        {
            string p = (string)path.Invoke(null, new[] { level });
            if (p != null && !expected.Contains(p))
            {
                expected.Add(p);
            }
        }
        Check("StockShots is the distinct non-null paths",
            string.Join(",", stock) == string.Join(",", expected),
            "got " + stock.Length + ": " + string.Join(",", stock));
        // A tripwire on the count, not a coverage claim -- it is DISTINCT PATHS, and two levels
        // sharing one bundled image is legal (see ScreenshotSaver), so twelve carousel levels
        // could legitimately yield eleven. It exists only so adding a thirteenth level has to
        // come past this line; the check above is what actually proves the derivation.
        Check("StockShots has twelve distinct paths", stock.Length == 12,
            "got " + stock.Length);
        return 0;
    }

    // Card 0d166364 follow-up -- ?gamebrowser gained a VALUE. It used to be a plain on/off flag
    // (`GameBrowser = IsOn(val)`); it now also answers `=fallback`, which adds two listed games on
    // levels with no bundled art so SubMenuOnlineGames.EnsureArt's fallback is exercised rather
    // than assumed. Two rigs on one flag, and they want opposite things: an APPEARANCE screenshot
    // of the carousel wants every row to look like a real game.
    //
    // The whole point is the SPLIT, so what is asserted is that the two spellings disagree.
    // The ?flyspiderflatten shape, and the same reason: a typo would otherwise silently run the
    // appearance rig while the run is labelled as the fallback one, and its missing rows look
    // exactly like the bug.
    //
    // MUTATION-TESTED THREE WAYS, each hitting a different check:
    //   * the whole pre-card case body (`GameBrowser = IsOn(val)`, no value, no diagnostic):
    //     6 FAIL -- `=fallback` stops booting the browser at all (IsOn says no), and the typo
    //     path loses both its rejection message and its browser.
    //   * deleting `GameBrowserFallback = false` from the bare-flag branch: 1 FAIL, the CLEARS
    //     check alone.
    //   * deleting it from the off branch: 3 FAIL, including the restore guard at the bottom.
    // The last two are the reason for the ordering rule below -- before an earlier revision was
    // corrected, BOTH of those deletions still gave ALL PASS.
    private static int ProbeGameBrowserFlag(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        PropertyInfo browserProp = flags.GetProperty("GameBrowser", anyStatic);
        PropertyInfo fallbackProp = flags.GetProperty("GameBrowserFallback", anyStatic);
        if (parse == null || browserProp == null || fallbackProp == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (Parse=" + (parse != null)
                + " GameBrowser=" + (browserProp != null)
                + " GameBrowserFallback=" + (fallbackProp != null) + ") -- renamed or moved?");
            return 2;
        }
        Func<string, object> get = name => flags.GetProperty(name, anyStatic).GetValue(null);
        Func<string, bool> browser = query =>
        {
            RunParse(parse, query);
            return (bool)browserProp.GetValue(null);
        };

        Console.WriteLine("[logic_probe] ?gamebrowser= (card 0d166364 follow-up)");

        // ORDER MATTERS IN THIS SET, and the two "does NOT add" checks are why. Read them as
        // "this spelling CLEARS the fallback", not merely "leaves it unset": each is preceded by
        // a =fallback run that turns it ON, because GameBrowserFallback starts false and an
        // assertion made from that state passes on a build with the assignment deleted (measured
        // -- an earlier revision of this set had exactly that hole, twice). The scenario is real:
        // ?gamebrowser=fallback&gamebrowser would otherwise strand the unmapped rows in an
        // appearance shot.
        Check("?gamebrowser=fallback boots the browser", browser("?gamebrowser=fallback"), null);
        Check("?gamebrowser=fallback adds the unmapped entries",
            (bool)get("GameBrowserFallback"), null);
        Check("bare ?gamebrowser boots the browser", browser("?gamebrowser"), null);
        Check("bare ?gamebrowser CLEARS the unmapped entries",
            !(bool)get("GameBrowserFallback"), "this is the appearance rig");
        // It hijacks the boot either way -- the two rigs differ ONLY in the two entries.
        Check("=fallback still implies SkipSplash + AutoStart",
            (bool)get("SkipSplash") && (bool)get("AutoStart"), null);
        // Same ordering rule: turn it on, THEN assert the off spelling clears it.
        RunParse(parse, "?gamebrowser=fallback");
        Check("?gamebrowser=0 turns the browser off", !browser("?gamebrowser=0"), null);
        Check("?gamebrowser=0 clears the fallback too",
            !(bool)get("GameBrowserFallback"), "an off spelling must not strand it set");

        // An unrecognised value is REPORTED and treated as bare -- never silently the fallback rig,
        // and never silently OFF (which `!IsOn` alone would give and would look like the flag
        // being ignored entirely).
        string outBad = FirstLine(RunParse(parse, "?gamebrowser=falback"));
        Check("bad ?gamebrowser= is reported", outBad.Contains("unknown ?gamebrowser="), outBad);
        Check("bad ?gamebrowser= still boots the browser", (bool)get("GameBrowser"), null);
        Check("bad ?gamebrowser= does not enable the fallback entries",
            !(bool)get("GameBrowserFallback"), null);
        // ... and it does not CLEAR them either: a repeated flag keeps the earlier VALID value,
        // so the typo is genuinely ignored rather than quietly resetting the rig. This pair is
        // what makes "ignored" in the message true; the two halves need opposite prior states,
        // which is why the typo is run twice.
        RunParse(parse, "?gamebrowser=fallback");
        string outBadAfter = FirstLine(RunParse(parse, "?gamebrowser=falback"));
        Check("bad ?gamebrowser= preserves the fallback already in force",
            (bool)get("GameBrowserFallback"), null);
        Check("... and the message names what is in force, not what the typo would have set",
            outBadAfter.Contains("the unmapped entries too"), outBadAfter);
        // CONTROL: a VALID value reports nothing, so a helper that printed unconditionally fails
        // here and only here.
        Check("?gamebrowser=fallback reports nothing",
            !RunParse(parse, "?gamebrowser=fallback").Contains("unknown ?gamebrowser="), null);

        // Hand the process back as it was found. Parse can only ASSIGN, so a Probe* added after
        // this one would otherwise inherit a browser-hijacked boot.
        RunParse(parse, "?gamebrowser=0&skipsplash=0&autostart=0");
        Check("restored: gamebrowser + its boot hijack are off",
            !(bool)get("GameBrowser") && !(bool)get("GameBrowserFallback")
                && !(bool)get("SkipSplash") && !(bool)get("AutoStart"), null);
        return 0;
    }

    // Card 88f87ba2 -- the wire-enum validation boundary (contract: NetProtocol.cs).
    //
    // WHY IT IS HERE AND NOT IN A BROWSER. Every validator and every decoder this drives is a
    // pure `byte[] -> out` static on NetProtocol: no ServiceHelper, no Game, no content. So the
    // real decoder can be fed real encoded frames on the desktop CLR, which is the strongest
    // oracle available for this card and needs no rig at all.
    //
    // Four sections, and the shape of each matters:
    //   1. Every declared member of every covered enum must be ACCEPTED and come back
    //      UNCHANGED. The acceptance half is subsumed by section 2 (which compares against
    //      IsDefined in both directions, so a refuse-everything validator fails it on every
    //      member); what this section adds that nothing else checks is that the value handed
    //      back is the one that went in -- a validator that accepted correctly but returned
    //      default(T) would pass section 2 and every refusal row.
    //   2. CONTIGUITY CROSS-CHECK against Enum.IsDefined across the whole 0..255 wire-byte
    //      domain. This is the expectation stated INDEPENDENTLY of the implementation: the
    //      validators use an explicit `raw <= (int)LastMember` bound, which silently assumes the
    //      enum is contiguous from 0 and append-only. It fails in BOTH directions -- a member
    //      appended past the bound (accepted by IsDefined, refused by the validator) and a gap or
    //      explicit value breaking contiguity (the reverse) -- so it is what keeps the bounds
    //      honest as the enums grow.
    //   3. THE REAL DECODERS, driven with real Encode* frames. This is what the card is actually
    //      about: a hostile or newer peer's bytes going in one end.
    //   4. NEGATIVE CONTROL. The values section 3 refuses must genuinely be outside their enums,
    //      i.e. the pre-card bare cast really did admit something no member matches.
    private static int ProbeWireEnums(Assembly asm)
    {
        Type proto = asm.GetType("EvilAliensWeb.Compat.Net.NetProtocol", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // validator name -> the enum it guards. Adding a wire enum means adding a ROW here; the
        // contract in NetProtocol.cs says so, and section 2 is why that is not busywork.
        (string Method, string EnumType)[] table =
        {
            ("TryLevel",        "EvilAliens.Levels"),
            ("TryDifficulty",   "EvilAliens.Settings+DifficultyLevel"),
            ("TryUnlockItem",   "EvilAliens.Unlockables+Items"),
            ("TryUnlockType",   "EvilAliens.AnimatedMessage+UnlockType"),
            ("TryCosmeticKind", "EvilAliensWeb.Compat.Net.NetCosmeticKind"),
            ("TryPowerupType",  "EvilAliens.Powerup+PowerupType"),
            ("TryMessageType",  "EvilAliens.AnimatedMessage+MessageType"),
            ("TrySpeech",       "EvilAliens.SoundManager+Texts"),
            ("TryBackgroundOp", "EvilAliensWeb.Compat.Net.NetBackgroundOp"),
        };

        MethodInfo launchEnc = proto.GetMethod("EncodeLaunchEvent", anyStatic);
        MethodInfo launchDec = proto.GetMethod("TryDecodeLaunchEvent", anyStatic);
        MethodInfo unlockEnc = proto.GetMethod("EncodeUnlockEvent", anyStatic);
        MethodInfo unlockDec = proto.GetMethod("TryDecodeUnlockEvent", anyStatic);
        MethodInfo msgEnc = proto.GetMethod("EncodeMessageEvent", anyStatic);
        MethodInfo msgDec = proto.GetMethod("TryDecodeMessageEvent", anyStatic);
        MethodInfo swarmEnc = proto.GetMethod("EncodeCosmeticSwarmEvent", anyStatic);
        MethodInfo swarmDec = proto.GetMethod("TryDecodeCosmeticSwarmEvent", anyStatic);
        if (launchEnc == null || launchDec == null || unlockEnc == null || unlockDec == null
            || msgEnc == null || msgDec == null || swarmEnc == null || swarmDec == null)
        {
            Console.WriteLine("FAIL: could not reflect the wire codecs (EncodeLaunchEvent="
                + (launchEnc != null) + " TryDecodeLaunchEvent=" + (launchDec != null)
                + " EncodeUnlockEvent=" + (unlockEnc != null) + " TryDecodeUnlockEvent=" + (unlockDec != null)
                + " EncodeMessageEvent=" + (msgEnc != null) + " TryDecodeMessageEvent=" + (msgDec != null)
                + " EncodeCosmeticSwarmEvent=" + (swarmEnc != null) + " TryDecodeCosmeticSwarmEvent=" + (swarmDec != null)
                + ") -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] wire enum validation (card 88f87ba2)");

        // Invoke one `bool Try*(int, out TEnum)` validator; Value is the boxed enum it produced.
        Func<MethodInfo, int, ValueTuple<bool, object>> call = (m, raw) =>
        {
            object[] a = { raw, null };
            bool ok = (bool)m.Invoke(null, a);
            return new ValueTuple<bool, object>(ok, a[1]);
        };

        // ---- 1. every declared member is accepted and comes back UNCHANGED -------------------
        foreach ((string method, string enumTypeName) in table)
        {
            Type et = asm.GetType(enumTypeName, true);
            MethodInfo m = proto.GetMethod(method, anyStatic);
            if (m == null)
            {
                Check(method + " reflects", false, "renamed or moved?");
                continue;
            }
            bool all = true;
            string firstBad = null;
            foreach (object member in Enum.GetValues(et))
            {
                int raw = Convert.ToInt32(member);
                ValueTuple<bool, object> got = call(m, raw);
                if (!got.Item1 || !Equals(got.Item2, member))
                {
                    all = false;
                    firstBad = firstBad ?? (member + " (=" + raw + ") -> " + (got.Item1 ? got.Item2.ToString() : "REFUSED"));
                }
            }
            Check(method + " accepts every declared member and returns it unchanged", all, firstBad);
        }

        // ---- 2. CONTIGUITY CROSS-CHECK vs Enum.IsDefined over the whole wire-byte domain ------
        foreach ((string method, string enumTypeName) in table)
        {
            Type et = asm.GetType(enumTypeName, true);
            MethodInfo m = proto.GetMethod(method, anyStatic);
            if (m == null)
            {
                continue;
            }
            Type underlying = Enum.GetUnderlyingType(et);
            bool agree = true;
            string firstBad = null;
            for (int raw = 0; raw <= 255; raw++)
            {
                // IsDefined demands the enum's own underlying type, so a byte-backed enum
                // (NetCosmeticKind) would throw on a boxed int.
                bool defined = Enum.IsDefined(et, Convert.ChangeType(raw, underlying));
                bool accepted = call(m, raw).Item1;
                if (defined != accepted)
                {
                    agree = false;
                    firstBad = firstBad ?? (raw + ": IsDefined=" + defined + " but " + method + "=" + accepted);
                }
            }
            Check(method + " agrees with Enum.IsDefined across 0..255", agree,
                firstBad ?? ("guards " + et.Name));
        }

        // A negative int cannot be an enum member and must be refused by the validators the
        // LISTING path uses (the browser's level/difficulty arrive as JSON ints, not wire bytes,
        // so the byte domain above does not cover them).
        Check("TryLevel refuses a negative", !call(proto.GetMethod("TryLevel", anyStatic), -1).Item1, null);
        Check("TryDifficulty refuses a negative", !call(proto.GetMethod("TryDifficulty", anyStatic), -1).Item1, null);
        Check("TryLevel refuses the browser's out-of-enum fake (9999)",
            !call(proto.GetMethod("TryLevel", anyStatic), 9999).Item1, null);

        // ---- 3. THE REAL DECODERS over real frames -------------------------------------------
        Type levels = asm.GetType("EvilAliens.Levels", true);
        Type diff = asm.GetType("EvilAliens.Settings+DifficultyLevel", true);
        Type items = asm.GetType("EvilAliens.Unlockables+Items", true);
        Type msgTypeEnum = asm.GetType("EvilAliens.AnimatedMessage+MessageType", true);

        byte goodLevel = Convert.ToByte(Enum.Parse(levels, "Level2"));
        byte goodDiff = Convert.ToByte(Enum.Parse(diff, "Hard"));

        Func<byte[], ValueTuple<bool, object, object>> decodeLaunch = frame =>
        {
            object[] a = { frame, null, null };
            bool ok = (bool)launchDec.Invoke(null, a);
            return new ValueTuple<bool, object, object>(ok, a[1], a[2]);
        };

        // POSITIVE CONTROL first: a valid launch must still be ACCEPTED and carry the right
        // values. A decoder that refused everything would pass every refusal below.
        ValueTuple<bool, object, object> okLaunch =
            decodeLaunch((byte[])launchEnc.Invoke(null, new object[] { (ushort)1, goodLevel, goodDiff }));
        Check("EvLaunch: a valid frame decodes to the host's level+difficulty",
            okLaunch.Item1 && Equals(okLaunch.Item2, Enum.Parse(levels, "Level2"))
                && Equals(okLaunch.Item3, Enum.Parse(diff, "Hard")),
            okLaunch.Item1 ? okLaunch.Item2 + "/" + okLaunch.Item3 : "REFUSED");

        // The headline case: a level this build has never heard of. Unvalidated it reaches
        // Game1.AddLevelComponent's throwing default arm AFTER the menu has been torn down.
        Check("EvLaunch: an out-of-enum LEVEL is refused",
            !decodeLaunch((byte[])launchEnc.Invoke(null, new object[] { (ushort)2, (byte)200, goodDiff })).Item1, null);
        // The save-poisoning case: this value would land in the XML-serialized
        // Settings.CurrentDifficulty and kill every later Settings.xml write.
        Check("EvLaunch: an out-of-enum DIFFICULTY is refused",
            !decodeLaunch((byte[])launchEnc.Invoke(null, new object[] { (ushort)3, goodLevel, (byte)200 })).Item1, null);
        Check("EvLaunch: a truncated frame is refused",
            !decodeLaunch(new byte[] { 0x30, 15, 0, 0, 5 }).Item1, null);

        // The Item2/Item3 payloads matter as much as the bool: a decoder that returned true
        // with default(T) in the out params would satisfy a boolean-only control while
        // handing the caller the WRONG unlock.
        Func<byte[], ValueTuple<bool, object, object>> decodeUnlock = frame =>
        {
            object[] a = { frame, null, null, null, null };
            bool ok = (bool)unlockDec.Invoke(null, a);
            return new ValueTuple<bool, object, object>(ok, a[1], a[2]);
        };
        byte goodItem = Convert.ToByte(Enum.Parse(items, "Challenges"));
        Type unlockTypeEnum = asm.GetType("EvilAliens.AnimatedMessage+UnlockType", true);
        ValueTuple<bool, object, object> okUnlock = decodeUnlock((byte[])unlockEnc.Invoke(null,
            new object[] { (ushort)4, goodItem, Convert.ToByte(Enum.Parse(unlockTypeEnum, "cheat")), (byte)0, "x" }));
        Check("EvUnlock: a valid frame decodes to the item and unlock type sent",
            okUnlock.Item1 && Equals(okUnlock.Item2, Enum.Parse(items, "Challenges"))
                && Equals(okUnlock.Item3, Enum.Parse(unlockTypeEnum, "cheat")),
            okUnlock.Item1 ? okUnlock.Item2 + "/" + okUnlock.Item3 : "REFUSED");
        // Same class as the difficulty above: an unknown item becomes a dictionary KEY in
        // Unlockables.Collection and kills every later Unlockables.xml write.
        Check("EvUnlock: an out-of-enum ITEM is refused",
            !decodeUnlock((byte[])unlockEnc.Invoke(null, new object[] { (ushort)5, (byte)200, (byte)0, (byte)0, "x" })).Item1, null);
        Check("EvUnlock: an out-of-enum UNLOCK TYPE is refused",
            !decodeUnlock((byte[])unlockEnc.Invoke(null, new object[] { (ushort)6, goodItem, (byte)200, (byte)0, "x" })).Item1, null);

        // EvMessage takes the CLAMP policy instead, and the difference is deliberate: dropping a
        // script beat would lose the level's story text on the joiner only, where an unknown
        // banner STYLE still renders readable text. So this one must SUCCEED.
        Func<byte[], ValueTuple<bool, object, object>> decodeMsg = frame =>
        {
            object[] a = { frame, null, null, null, null };
            bool ok = (bool)msgDec.Invoke(null, a);
            return new ValueTuple<bool, object, object>(ok, a[1], a[2]);
        };
        ValueTuple<bool, object, object> badStyle =
            decodeMsg((byte[])msgEnc.Invoke(null, new object[] { (ushort)7, (byte)200, (byte)200, 0f, "hello" }));
        Check("EvMessage: an out-of-enum style is CLAMPED, not refused",
            badStyle.Item1 && Equals(badStyle.Item2, Enum.Parse(msgTypeEnum, "starwarsblue")),
            badStyle.Item1 ? badStyle.Item2.ToString() : "REFUSED");
        Check("EvMessage: an out-of-enum speech cue clamps to Nothing",
            badStyle.Item1 && Convert.ToInt32(badStyle.Item3) == 0, null);
        // ... and a VALID style is not clamped away, or the row above would pass vacuously.
        ValueTuple<bool, object, object> goodStyle = decodeMsg((byte[])msgEnc.Invoke(null, new object[]
            { (ushort)8, Convert.ToByte(Enum.Parse(msgTypeEnum, "redwarning")), (byte)1, 0f, "hello" }));
        Check("EvMessage: a valid style survives the clamp untouched",
            goodStyle.Item1 && Equals(goodStyle.Item2, Enum.Parse(msgTypeEnum, "redwarning")),
            goodStyle.Item1 ? goodStyle.Item2.ToString() : "REFUSED");
        // ... and so does a valid SPEECH cue. Without this the clamp row above passes on a
        // SpeechOrNone hard-wired to Nothing, which would silently mute every replicated beat.
        Check("EvMessage: a valid speech cue survives the clamp untouched",
            goodStyle.Item1 && Convert.ToInt32(goodStyle.Item3) == 1,
            goodStyle.Item1 ? goodStyle.Item3.ToString() : "REFUSED");

        // Payload asserted for the same reason as EvUnlock above.
        Func<byte[], ValueTuple<bool, object, object, object>> decodeSwarm = frame =>
        {
            object[] a = { frame, null, null, null };
            bool ok = (bool)swarmDec.Invoke(null, a);
            return new ValueTuple<bool, object, object, object>(ok, a[1], a[2], a[3]);
        };
        Type kindEnum = asm.GetType("EvilAliensWeb.Compat.Net.NetCosmeticKind", true);
        ValueTuple<bool, object, object, object> okSwarm = decodeSwarm((byte[])swarmEnc.Invoke(null,
            new object[] { (ushort)9, Convert.ToByte(Enum.Parse(kindEnum, "BackgroundAsteroids")), true, 5.5f }));
        Check("EvCosmeticSwarm: a valid frame decodes to the kind, on-flag and rate sent",
            okSwarm.Item1 && Equals(okSwarm.Item2, Enum.Parse(kindEnum, "BackgroundAsteroids"))
                && (bool)okSwarm.Item3 && Math.Abs((float)okSwarm.Item4 - 5.5f) < 1e-6f,
            okSwarm.Item1 ? okSwarm.Item2 + "/" + okSwarm.Item3 + "/" + okSwarm.Item4 : "REFUSED");
        Check("EvCosmeticSwarm: an out-of-enum kind is refused",
            !decodeSwarm((byte[])swarmEnc.Invoke(null, new object[] { (ushort)10, (byte)200, true, 5.5f })).Item1, null);

        // ---- 3b. THE SENTINEL'S DISPLAY CONTRACT ---------------------------------------------
        // The browser LISTING keeps an unknown value rather than refusing it, so what has to
        // hold is that the two things drawn from it stay sensible for null. Rig for the same
        // pair end to end: tools/headless/probes/gamebrowser_fallback.txt.
        Type levelArt = asm.GetType("EvilAliens.LevelArt", true);
        Type nullableLevels = typeof(Nullable<>).MakeGenericType(levels);
        Type nullableDiff = typeof(Nullable<>).MakeGenericType(diff);
        MethodInfo titleOf = levelArt.GetMethod("Title", anyStatic, null, new[] { nullableLevels }, null);
        MethodInfo diffName = levelArt.GetMethod("DifficultyName", anyStatic, null, new[] { nullableDiff }, null);
        if (titleOf == null || diffName == null)
        {
            Check("LevelArt.Title(Levels?) / DifficultyName(DifficultyLevel?) reflect", false,
                "Title=" + (titleOf != null) + " DifficultyName=" + (diffName != null)
                    + " -- still taking a raw int/enum?");
        }
        else
        {
            Check("an unknown level titles as the generic \"Mission\"",
                (string)titleOf.Invoke(null, new object[] { null }) == "Mission", null);
            Check("an unknown difficulty renders as \"?\"",
                (string)diffName.Invoke(null, new object[] { null }) == "?", null);
            // Positive controls: neither may answer the unknown string for a value we DO know,
            // or the two rows above would pass on a function that ignored its argument.
            Check("... but a known level still titles properly",
                (string)titleOf.Invoke(null, new[] { Enum.Parse(levels, "Level2") }) == "Mission 2", null);
            Check("... and a known difficulty still names its tier",
                (string)diffName.Invoke(null, new[] { Enum.Parse(diff, "Very_Hard") }) == "Very Hard", null);
        }

        // ---- 4. NEGATIVE CONTROL -------------------------------------------------------------
        // Every refusal above is only meaningful if the value really is outside its enum -- i.e.
        // the pre-card bare cast admitted something no member matches. Stated via IsDefined so it
        // does not restate the validators' own bounds.
        Check("negative control: 200 is not a Levels member", !Enum.IsDefined(levels, 200), null);
        Check("negative control: 200 is not a DifficultyLevel member", !Enum.IsDefined(diff, 200), null);
        Check("negative control: 200 is not an Unlockables.Items member", !Enum.IsDefined(items, 200), null);
        return 0;
    }

    // Card 25ad0659 (step 1) -- the in-process net wire and the wire-level codec round trips.
    //
    // Unlike every case set above, this does NOT restate any expectation: it invokes
    // Compat/Net/NetWireTest.Run(), which is the SAME suite eaNetWire.test() runs in the browser
    // and `eval NetWireTest` runs under eahl. That is deliberate, and it is the ProbeTeamPartnerSeat
    // precedent -- one suite, three runners, nothing to drift. What this adds is a BROWSERLESS
    // runner with an exit code, which is what makes the wire a CI-able gate; NetWireTest is
    // Game-free precisely so it survives this loader's limits (no ServiceHelper, no GraphicsDevice).
    // The two guards that make a green run mean something live in RunBrowserSuite below.
    private static int ProbeNetWire(Assembly asm)
    {
        string[] sections =
        {
            "1. transport contract",
            "2. NetImpairment composed over an endpoint",
            "3. codec round trips through the wire",
            "4. stream-lane reorder + dedup",
        };
        return RunBrowserSuite(asm, "EvilAliensWeb.Compat.Net.NetWireTest", sections, minAssertions: 69);
    }

    // Card 25ad0659 (step 2a) -- the INetHost seam: the clock, the two build/identity
    // fingerprints and the debug flags the net cores read, behind one injected interface.
    //
    // Same shape as ProbeNetWire above (invoke the browser suite, do not restate it), and for the
    // same reason. What it buys over the browser run is that the seam's whole point -- a virtual
    // clock the layer actually obeys -- is now provable with an exit code and no browser, which is
    // what makes it a gate for steps 2b/2c/3/4 rather than something to remember to click.
    private static int ProbeNetHost(Assembly asm)
    {
        string[] sections =
        {
            "1. NetHost.Current contract",
            "2. ServiceHelperNetHost maps 1:1",
            "3. the injected clock reaches NetImpairment",
            "4. impairment knobs come from the host",
        };
        return RunBrowserSuite(asm, "EvilAliensWeb.Compat.Net.NetHostTest", sections, minAssertions: 32);
    }

    // Card 724f2abc -- the sub-tick mouse click. InputHandler polls Mouse.GetState() once per
    // tick and edge-detects, so a mousedown/mouseup pair that both land BETWEEN two polls is
    // never observed and Pressed(MyKeys.Mouse1) never fires; the cursor POSITION survives it, so
    // the symptom is a menu row that hover-highlights and never invokes. Compat/MouseLatch.cs
    // takes the DOM mousedown edge from JS and InputHandler folds it into `held` for one tick.
    //
    // WHY HERE AND NOT UNDER tools/headless: the bug is a DOM/game-loop timing race and eahl has
    // no DOM at all -- SDL2 polls the mouse exactly the way the browser path does, so a --script
    // probe could not tell the fixed code from the broken code. There is deliberately NO headless
    // probe for this; this case set is the regression guard, and the card's Chrome leg is the
    // evidence about the shipped build.
    //
    // Section 2 is what makes it more than a restatement of a three-line method: it drives the
    // REAL InputHandler.Update over the REAL latch and asserts the end-to-end claim -- a latched
    // press produces Pressed(Mouse1) on the very next tick and on exactly one tick. That covers
    // the `held |= MouseLatch.Consume(i)` wiring, which is the line a future edit would drop.
    private static int ProbeMouseLatch(Assembly asm)
    {
        Type latch = asm.GetType("EvilAliensWeb.Compat.MouseLatch", true);
        Type handlerType = asm.GetType("EvilAliens.InputHandler", true);
        Type keys = asm.GetType("EvilAliens.MyKeys", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo down = latch.GetMethod("OnMouseDown", anyStatic);
        MethodInfo consume = latch.GetMethod("Consume", anyStatic);
        MethodInfo update = handlerType.GetMethod("Update", anyInstance);
        MethodInfo pressed = handlerType.GetMethod("Pressed", anyInstance);
        if (down == null || consume == null || update == null || pressed == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (OnMouseDown=" + (down != null)
                + " Consume=" + (consume != null) + " InputHandler.Update=" + (update != null)
                + " InputHandler.Pressed=" + (pressed != null) + ") -- renamed or moved?");
            return 2;
        }
        // Read the indices off the enum rather than writing 6/7 here, so a reordered MyKeys fails
        // loudly instead of quietly latching the wrong key.
        int mouse1 = (int)Enum.Parse(keys, "Mouse1");
        int mouse2 = (int)Enum.Parse(keys, "Mouse2");
        int enter = (int)Enum.Parse(keys, "Enter");

        Console.WriteLine("[logic_probe] Compat/MouseLatch -- the sub-tick click (card 724f2abc)");

        // ---- 1. the latch itself -------------------------------------------------------------
        // The two ints are DIFFERENT SPACES and are named apart on purpose: pressButton takes a
        // DOM button number (left 0, middle 1, right 2, back/forward 3/4), consumeKey takes a
        // MyKeys index. They are silently interchangeable to the compiler.
        const int LeftButton = 0;
        const int MiddleButton = 1;
        const int RightButton = 2;
        const int BackButton = 3;
        Func<int, bool> consumeKey = idx => (bool)consume.Invoke(null, new object[] { idx });
        Action<int> pressButton = button => down.Invoke(null, new object[] { button });

        // Nothing pressed yet: the latch must not invent a click. This runs FIRST, before anything
        // sets it -- an "is clear" assertion made after a Consume would only be re-reading the
        // clear it is meant to be testing.
        Check("an untouched latch reports nothing", !consumeKey(mouse1) && !consumeKey(mouse2), null);

        // The positive control. Without it every other check here is satisfied by a Consume that
        // returns false unconditionally, which is precisely the regression being guarded.
        pressButton(LeftButton);
        Check("a latched left press is reported", consumeKey(mouse1), null);
        // ... and for exactly ONE tick. A latch that stayed set would hold Mouse1 down forever:
        // menus would auto-invoke and the ship would fire without input.
        Check("the press is consumed, not sticky", !consumeKey(mouse1), null);

        // The two buttons are independent -- a left click must not fire the right-click path.
        pressButton(LeftButton);
        Check("a left press does not latch Mouse2", !consumeKey(mouse2) && consumeKey(mouse1), null);
        pressButton(RightButton);
        Check("a right press does not latch Mouse1", !consumeKey(mouse1) && consumeKey(mouse2), null);

        // Consume's trailing "not a mouse button" arm. DEFENSIVE, not a reachable failure today:
        // InputHandler folds the latch in from inside `case 6:`/`case 7:` only, so unlike
        // DebugInput.Consume -- which really is called for every index -- nothing else reaches
        // here. It is pinned because the arm is cheap to lose and Enter is what it would cost:
        // Enter is menu SELECT, so a false positive there invokes the selected entry by itself.
        pressButton(LeftButton);
        pressButton(RightButton);
        Check("a non-mouse key index never latches", !consumeKey(enter), "MyKeys.Enter = " + enter);
        consumeKey(mouse1);
        consumeKey(mouse2);

        // An unmapped button (middle, back/forward) is neither of the two the game reads.
        pressButton(MiddleButton);
        pressButton(BackButton);
        Check("an unmapped mouse button latches nothing",
            !consumeKey(mouse1) && !consumeKey(mouse2), null);

        // ---- 2. end to end through the real InputHandler --------------------------------------
        // The claim the card actually makes. InputHandler.Update reads the Keyboard/Mouse/GamePad
        // statics; under this loader they answer with a disconnected default rather than throwing,
        // which is exactly the "no button is physically down" baseline this needs.
        object handler;
        try
        {
            handler = Activator.CreateInstance(handlerType);
            update.Invoke(handler, null);
        }
        catch (Exception ex)
        {
            // NOT a skip: the wiring leg is the reason this case set is worth more than a
            // restatement, so losing it is a failure to be looked at, never a quiet pass.
            Console.WriteLine("  FAIL InputHandler could not be driven here: "
                + (ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException.ToString() : ex.ToString()));
            failures++;
            return 0;
        }
        Func<int, bool> isPressed = idx => (bool)pressed.Invoke(handler, new object[] { Enum.ToObject(keys, idx) });

        // Baseline for BOTH buttons: no latch and no physical button, so a tick must report no
        // press. This is also the guard on the one host-state dependency here -- Update reads the
        // real Mouse.GetState(), and KNI does install a concrete input strategy under this loader,
        // so running logic_probe with a mouse button physically held would fail HERE rather than
        // letting the assertions below pass vacuously.
        update.Invoke(handler, null);
        Check("no latch => no press on a plain tick", !isPressed(mouse1) && !isPressed(mouse2),
            "fails if a real mouse button is held down while logic_probe runs");

        // THE ASSERTION. A press+release that never overlapped a poll still produces a press.
        pressButton(LeftButton);
        update.Invoke(handler, null);
        bool tick1 = isPressed(mouse1);
        update.Invoke(handler, null);
        bool tick2 = isPressed(mouse1);
        Check("a sub-tick click presses Mouse1 on the next tick", tick1,
            "this is the whole card -- the pre-fix build reads false here");
        Check("and on exactly ONE tick", !tick2, "a sticky press would fire menus and the ship forever");

        // The same for the right button, whose case is a separate line and so a separate omission.
        pressButton(RightButton);
        update.Invoke(handler, null);
        Check("a sub-tick right click presses Mouse2", isPressed(mouse2), null);
        update.Invoke(handler, null);

        // NEGATIVE CONTROL -- the pre-card build over the same input (the eaNetScore.test() rule;
        // section 1 is admittedly a restatement of a three-line method, so without this the set
        // discriminates only as far as a prose account of a mutation run). The pre-card
        // InputHandler never consulted the latch, which is reproduced exactly by draining it
        // BEFORE the tick: the DOM edge happened, and `held` is derived from the poll alone.
        // It must then miss the click, which is the defect.
        pressButton(LeftButton);
        consumeKey(mouse1);
        update.Invoke(handler, null);
        Check("negative control: without the fold-in the same click is LOST", !isPressed(mouse1),
            "a pass here means the latch is not what carried the press above");

        // Leave nothing latched for a Probe* added after this one. The Check runs FIRST -- put
        // the drains before it and it can only re-test the clear, never report a leak.
        Check("case set leaves no press latched", !consumeKey(mouse1) && !consumeKey(mouse2), null);
        return 0;
    }

    // Shared runner for the case sets that RESTATE NOTHING and instead invoke a browser suite's
    // own Run() (the ProbeTeamPartnerSeat precedent -- one suite, three runners, nothing to
    // drift). It works only for suites that are deliberately Game-free; anything touching
    // ServiceHelper / Game / GraphicsDevice dies inside this loader's documented limits.
    //
    // Two guards make a green run mean something. (a) Every section header must be present, so a
    // suite that threw or returned early cannot pass on the FAIL lines it never printed. (b) A
    // floor on the PASS count, so deleting assertions is a failure rather than a faster pass --
    // the number is the count at the time of writing and is meant to be raised when legs are
    // added, never lowered to make a run green.
    private static int RunBrowserSuite(Assembly asm, string typeName, string[] sections, int minAssertions)
    {
        string shortName = typeName.Substring(typeName.LastIndexOf('.') + 1);
        Type suite = asm.GetType(typeName, true);
        MethodInfo run = suite.GetMethod("Run", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (run == null)
        {
            Console.WriteLine("FAIL: could not reflect " + shortName + ".Run -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] Compat/Net/" + shortName + " (card 25ad0659)");
        string report;
        try
        {
            report = (string)run.Invoke(null, null);
        }
        catch (TargetInvocationException ex)
        {
            // A throw here is a real failure, not a reflection problem: the suite is Game-free, so
            // nothing in this loader's documented limits can reach it. Name the inner exception --
            // TargetInvocationException's own message says nothing.
            Console.WriteLine("  FAIL " + shortName + " threw: "
                + (ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString()));
            failures++;
            return 0;
        }

        if (report == null)
        {
            // Same class as the "could not reflect" bail above: a Run() changed to return null (or
            // to void, which Invoke reports as null) would otherwise NRE out with a stack trace --
            // the one failure the TargetInvocationException catch was written to avoid.
            Console.WriteLine("FAIL: " + shortName + ".Run returned null -- signature changed?");
            return 2;
        }

        int passes = 0;
        foreach (string line in report.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            Console.WriteLine("  " + trimmed);
            if (trimmed.StartsWith("PASS ", StringComparison.Ordinal))
            {
                passes++;
            }
            else if (trimmed.StartsWith("FAIL ", StringComparison.Ordinal))
            {
                failures++;
            }
        }

        foreach (string section in sections)
        {
            Check("section ran: " + section, report.Contains(section, StringComparison.Ordinal), null);
        }
        Check(shortName + " assertion count did not shrink", passes >= minAssertions,
            "passes=" + passes + " floor=" + minAssertions);
        return 0;
    }
}
