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
using System.Collections.Generic;
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

        rc = ProbeAiFieldComposition(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeAiConeShape(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeAiBossApproach(asm);
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

        rc = ProbeListingLevels(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeHostMenu(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeSpawnDirection(asm);
        if (rc != 0)
        {
            return rc;
        }

        rc = ProbeRespawnSummon(asm);
        if (rc != 0)
        {
            return rc;
        }

        // LAST ON PURPOSE -- it is the only set that seeds RandomHelper, and it cannot unseed
        // afterwards (there is no un-seed API and adding one for a probe would be a production
        // change made for a test). Nothing above draws from RandomHelper, so the order costs
        // nothing; its own first leg asserts the pristine state it needs.
        rc = ProbeSeedFlag(asm);
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
            new { Flag = "airepeldelta",   Prop = "AiRepelCancelDelta",    Good = "3",   Want = (object)3f,    Baked = "0.2"  },
            new { Flag = "ainoisefloor",   Prop = "AiSteerNoiseFloor",     Good = "4",   Want = (object)4f,    Baked = "0.2"  },
            new { Flag = "aiseekdeadzone", Prop = "AiSeekDeadzonePx",      Good = "77",  Want = (object)77f,   Baked = "30"   },
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
            new { Flag = "aiseekpowerup",  Prop = "AiSeekPowerupWeight",  Good = "2.5", Want = (object)2.5f,  Baked = "0.8"  },
            new { Flag = "aiseekapproach", Prop = "AiSeekApproachWeight", Good = "2.6", Want = (object)2.6f,  Baked = "1.1"  },
            new { Flag = "aipowerupreach", Prop = "AiPowerupReachPx",      Good = "444", Want = (object)444f,  Baked = "150"  },
            // The directional repellent shapes (card e425781b). The three on/off members of the
            // family -- ?aicone= ?aiwedge= ?ailaneescape= -- are deliberately absent, following
            // ?aievade=: the IsOn/IsExplicitlyOff spelling has its own convention and this table's
            // "names the value in force" leg does not describe it.
            new { Flag = "aiconelead",     Prop = "AiConeLeadMs",          Good = "456", Want = (object)456f,  Baked = "700"  },
            new { Flag = "aiconemaxlen",   Prop = "AiConeMaxLenPx",        Good = "654", Want = (object)654f,  Baked = "800"  },
            new { Flag = "aiconewidth",    Prop = "AiConeWidthPx",         Good = "271", Want = (object)271f,  Baked = "300"  },
            new { Flag = "aiconespread",   Prop = "AiConeSpread",          Good = "6.4", Want = (object)6.4f,  Baked = ""     },
            new { Flag = "aiconewidthmin", Prop = "AiConeWidthMinPx",      Good = "77",  Want = (object)77f,   Baked = "120"  },
            new { Flag = "aiconetaper",    Prop = "AiConeTaper",           Good = "0.25",Want = (object)0.25f, Baked = ""     },
            new { Flag = "aiconefallalong",Prop = "AiConeFallAlong",       Good = "5",   Want = (object)5f,    Baked = ""     },
            new { Flag = "aiconefallacross",Prop = "AiConeFallAcross",     Good = "6",   Want = (object)6f,    Baked = ""     },
            new { Flag = "aiconescale",    Prop = "AiConeScale",           Good = "2.75",Want = (object)2.75f, Baked = ""     },
            new { Flag = "aiwedgestrength",Prop = "AiLaneWedgeStrength",   Good = "29",  Want = (object)29f,   Baked = "18"   },
            new { Flag = "aiwedgefall",    Prop = "AiLaneWedgeFallAlong",  Good = "7",   Want = (object)7f,    Baked = ""     },
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

        Console.WriteLine("[logic_probe] DebugFlags ?ai* value rejection, all 30 knobs (card 48b7c6b1)");

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
        Check("a valid value lands on all " + rows.Length, landed == rows.Length,
            landed + "/" + rows.Length + (badLanded != null ? "; " + badLanded : ""));
        Check("a valid value reports NO rejection (the control)", quiet == rows.Length,
            quiet + "/" + rows.Length + " clean -- a helper that printed unconditionally fails here"
            + (badQuiet != null ? "; " + badQuiet : ""));
        Check("a bad value is refused AND reported on all " + rows.Length, reported == rows.Length,
            reported + "/" + rows.Length + (badReported != null ? "; " + badReported : ""));
        Check("the message names the value IN FORCE, not the baked default", named == rows.Length,
            named + "/" + rows.Length + " (Parse never resets a property, so a repeated flag keeps the"
            + " earlier value)" + (badNamed != null ? "; " + badNamed : ""));
        Check("a NEGATIVE value is refused AND reported on all " + (rows.Length - 1) + " guarded flags", negatives == rows.Length - 1,
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
    // The AI steering field's COMPOSITION rules (cards ada9e839 / 31ceb6ff / f4d1721f).
    //
    // WHAT THE FIELD DOES, so the assertions below read as more than arithmetic. DoAIMove sums
    // two families of force. REPELLENTS (every threat field, the lazer terms, the spider boss's
    // lane escapes, the screen edges) accumulate on their own and are dropped wholesale if their
    // resultant falls to DefaultRepulseCancelDelta or below -- opposing pushes that cancel leave
    // a vector whose DIRECTION is noise, and Move() discards magnitude and thrusts at full
    // acceleration along the angle. ATTRACTORS (the idle station, a powerup, a halting boss's
    // standoff) are never floored; each stops pulling inside its own DEADZONE instead. The two
    // are then summed, and DefaultSteerNoiseFloor catches the leftover equilibrium case.
    //
    // WHAT THIS PROBE IS AND IS NOT. It is a set of ORDERING properties over constants that live
    // hundreds of lines apart and are each individually plausible, not a restatement of any one
    // of them. It cannot invoke DoAIMove (that needs a Game, an Oracle and a live scene), so it
    // proves the CONFIGURATION is coherent, never that the field is wired up;
    // `tools/headless/probes/ai_boss_approach.txt` covers the wiring by soaking the real bot.
    //
    // WHY IT MATTERS THAT IT IS PINNED HERE. This replaces ProbeAiSeekWeights, whose premise --
    // that a 0.95 "park" SHOULD zero the station and the powerup and should NOT zero the boss
    // standoff -- was the bug, not the contract. That park sat ABOVE the 0.8 seek, so a lone seek
    // produced no motion at all and every deliberate destination the bot had was silently
    // deleted; the boss-approach weight only ever "worked" by being raised clear of it. Raising a
    // floor back above the weakest attractor would reintroduce that exactly: no error, no visual
    // difference, no console line, and the only symptom a bot that quietly stops going places.
    // Assertion 2 is the guard for it.
    private static int ProbeAiFieldComposition(Assembly asm)
    {
        Type ship = asm.GetType("EvilAliens.PlayerShip", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        string[] names =
        {
            // DefaultSeekApproachWeight is GONE (card b56633fb) -- the boss approach carries no
            // constant weight any more, it is solved per tick. Its two claims here (below the
            // threat field, above a detour) moved to ProbeAiBossApproach, where they are asserted
            // about the live curve instead of about a literal.
            "SeekWeight", "DefaultSeekPowerupWeight",
            "DefaultPowerupReachPx", "DefaultRepulseCancelDelta", "DefaultSteerNoiseFloor",
            "DefaultSeekArriveDeadzonePx", "ShipMaxSpeed", "ShipDeceleration",
            "SweepLaneAvoidStrength",
            "LazerAvoidStrength", "LazerDodgeStrength"
        };
        var vals = new Dictionary<string, float>();
        foreach (string n in names)
        {
            FieldInfo f = ship.GetField(n, anyStatic);
            if (f == null || !f.IsLiteral)
            {
                Console.WriteLine("FAIL: could not reflect PlayerShip." + n + " as a const -- renamed, moved, or no longer const?");
                return 2;
            }
            vals[n] = (float)f.GetRawConstantValue();
        }

        Console.WriteLine("[logic_probe] AI steering field composition (card ada9e839)");
        float station = vals["SeekWeight"], powerup = vals["DefaultSeekPowerupWeight"];
        float repelDelta = vals["DefaultRepulseCancelDelta"], noiseFloor = vals["DefaultSteerNoiseFloor"];
        float deadzone = vals["DefaultSeekArriveDeadzonePx"];

        // 1. THE DEADZONE COVERS THE STOPPING DISTANCE. This is the property that makes the
        // attractors' hard-edged deadzone sound instead of an oscillator: `Move(null, ...)`
        // applies deceleration alone, so a ship entering at full speed coasts v^2 / 2a further.
        // If the deadzone is smaller than that the ship sails out the FAR side still under the
        // attractor's pull, turns round, and pingpongs -- which is the symptom the 0.95 park was
        // wrongly reached for. Derived from the real motion constants rather than restated, so
        // retuning the flight model re-derives the bound instead of silently invalidating it.
        float stoppingPx = vals["ShipMaxSpeed"] * vals["ShipMaxSpeed"] / (2f * vals["ShipDeceleration"]);
        Check("the seek deadzone covers the ship's stopping distance",
            deadzone > stoppingPx,
            "DefaultSeekArriveDeadzonePx " + deadzone + " vs a stopping distance of "
            + stoppingPx.ToString("0.0") + "px (ShipMaxSpeed " + vals["ShipMaxSpeed"]
            + " / ShipDeceleration " + vals["ShipDeceleration"]
            + ") -- below it the ship coasts out the far side and pingpongs about its target");

        // 2. NO FLOOR CAN CENSOR A LONE DELIBERATE FORCE. The whole-sum floor must sit below the
        // weakest ATTRACTOR (they are never floored on their own, but they still cross this one)
        // and below the weakest full-strength REPELLENT. Both bounds together are what make the
        // floor an equilibrium guard rather than the veto this port shipped for two cards.
        float weakestAttractor = Math.Min(station, powerup);
        Check("the whole-sum floor is below the weakest ATTRACTOR",
            weakestAttractor > noiseFloor,
            "weakest attractor " + weakestAttractor + " vs DefaultSteerNoiseFloor " + noiseFloor
            + " -- at or below it a lone seek is zeroed and the bot stops going places, which is"
            + " precisely the 0.95 park of cards ada9e839 / 31ceb6ff");
        // The repellents' full-strength magnitudes. maxSteerStrength (4) is a DoAIMove local, so
        // the threat field's and the screen edges' shared peak is spelled here; the rest are
        // reflected.
        // TopEdgeAvoidStrength is deliberately NOT in this min: it is added AFTER the low-pass
        // and so never passes through RepulseCancelDelta at all. Folding it in would mix the two
        // populations this card just separated, and it could not fail today (20 against a min of
        // 4), which is exactly how a wrong invariant gets copied.
        const float MaxSteerStrength = 4f;
        float weakestRepellent = Math.Min(MaxSteerStrength,
            Math.Min(vals["SweepLaneAvoidStrength"],
            Math.Min(vals["LazerAvoidStrength"], vals["LazerDodgeStrength"])));
        Check("every REPELLENT's full strength clears the repulsion cancellation delta",
            weakestRepellent > repelDelta,
            "weakest repellent " + weakestRepellent + " vs DefaultRepulseCancelDelta " + repelDelta
            + " -- at or below it a lone threat at point-blank range is dropped and the bot stops"
            + " dodging that type entirely");
        Check("a repellent that survives its own floor also clears the whole-sum floor",
            repelDelta >= noiseFloor,
            "DefaultRepulseCancelDelta " + repelDelta + " vs DefaultSteerNoiseFloor " + noiseFloor
            + " -- otherwise a repellent can pass the first floor and be eaten by the second,"
            + " which is a veto wearing two names");

        // 3. The boss approach's two ordering claims (below the threat field, above a detour) are
        // ProbeAiBossApproach's now -- it has no constant to compare here since card b56633fb.
        Check("the powerup reach is its own quantity, not the screen-edge margin",
            vals["DefaultPowerupReachPx"] > 0f,
            "DefaultPowerupReachPx " + vals["DefaultPowerupReachPx"]
            + " (baked at the 2008 steerRange value -- 300 was measured inert, see the const)");

        // NEGATIVE CONTROL. Every bound above is a one-sided inequality a build could satisfy
        // vacuously by making the floors tiny, so run the PRE-CARD configuration -- the 0.95 park
        // as a whole-sum floor -- through assertion 2's own predicate and require it to FAIL.
        // That is the discriminating claim: this build's floor admits the weakest attractor and
        // the one shipped for two cards did not.
        const float PreCardParkDemand = 0.95f;
        Check("control: the pre-card 0.95 park FAILS the floor bound this probe enforces",
            !(weakestAttractor > PreCardParkDemand) && weakestAttractor > noiseFloor,
            "weakest attractor " + weakestAttractor + " is at or below the pre-card park "
            + PreCardParkDemand + " (so that build zeroed it) and above the shipped floor "
            + noiseFloor + " (so this build does not) -- that gap is the whole fix");
        return 0;
    }

    // ---- the solved boss-approach attractor (card b56633fb) --------------------------------
    //
    // PlayerShip.BossApproachWeight is a pure function of the boss's own repellent parameters, so
    // the whole design is checkable here over EVERY difficulty tier and the whole bulletlifetime
    // range with no game running -- which matters because the properties that make the design
    // sound (the crossing sits at firing range, the parked band is wider than the ship's stopping
    // distance) hold or fail per weapon and per tier, and no single run can visit more than one
    // combination.
    //
    // The repellent side is the SHIPPED ThreatFieldStrength, reflected rather than transcribed --
    // a mirrored curve here would agree with itself forever while the field drifted, and the whole
    // point of the design is that the two are solved against each other.
    private static int ProbeAiBossApproach(Assembly asm)
    {
        Type ship = asm.GetType("EvilAliens.PlayerShip", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        MethodInfo weightM = ship.GetMethod("BossApproachWeight", anyStatic);
        MethodInfo strengthM = ship.GetMethod("ThreatFieldStrength", anyStatic, null,
            new[] { typeof(float), typeof(float), typeof(float), typeof(bool) }, null);
        MethodInfo priorityM = ship.GetMethod("IsAiPriorityTarget", anyStatic);
        if (weightM == null || strengthM == null || priorityM == null)
        {
            Console.WriteLine("FAIL: could not reflect PlayerShip.BossApproachWeight / ThreatFieldStrength"
                + " / IsAiPriorityTarget -- renamed or moved?");
            return 2;
        }

        float Const(string n)
        {
            FieldInfo f = ship.GetField(n, anyStatic);
            if (f == null || !f.IsLiteral)
            {
                throw new InvalidOperationException("could not reflect PlayerShip." + n + " as a const");
            }
            return (float)f.GetRawConstantValue();
        }

        float minAnchor, maxWeight, exponent, noiseFloor, sizeScale, falloff, bulletPerMs, stoppingPx;
        float[] tierFieldPx;
        try
        {
            minAnchor = Const("BossApproachMinAnchorPx");
            maxWeight = Const("BossApproachMaxWeight");
            exponent = Const("BossApproachExponent");
            noiseFloor = Const("DefaultSteerNoiseFloor");
            sizeScale = Const("DefaultThreatFieldSizeScale");
            falloff = Const("DefaultThreatFieldFalloff");
            bulletPerMs = Const("BulletRangePerMs");
            stoppingPx = Const("ShipMaxSpeed") * Const("ShipMaxSpeed") / (2f * Const("ShipDeceleration"));
            // The per-tier field radius, read out of the real ladder rather than restated -- a tier
            // added or retuned is then swept by this probe automatically.
            FieldInfo skillF = ship.GetField("AiSkillByDifficulty", anyStatic);
            Array skills = (Array)skillF.GetValue(null);
            FieldInfo fieldPxF = skills.GetType().GetElementType()
                .GetField("FieldPx", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            tierFieldPx = new float[skills.Length];
            for (int i = 0; i < skills.Length; i++)
            {
                tierFieldPx[i] = (float)fieldPxF.GetValue(skills.GetValue(i));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL: " + e.Message);
            return 2;
        }

        Console.WriteLine("[logic_probe] AI boss-approach attractor (card b56633fb)");

        const float MaxSteer = 4f;
        // BOTH CURVE FAMILIES are swept (`?aifieldcurve=classic` restores the 2008 `max*(1-t^2)`
        // plateau -- card e88e21ca), because the weight is solved against whichever one the boss's
        // repellent is using and the plateau is a much flatter shape near r*. `classic` is a
        // captured local rather than a parameter so the whole sweep below reads unchanged.
        bool classic = false;
        float A(float d, float anchor, float range) => (float)weightM.Invoke(null,
            new object[] { d, anchor, range, falloff, classic, 1f, MaxSteer, noiseFloor });
        float Repel(float d, float range) => d >= range
            ? 0f
            : (float)strengthM.Invoke(null, new object[] { d / range, MaxSteer, falloff, classic });

        // The whole reachable domain: every tier x the whole bulletlifetime range (450 base to the
        // 1500 cap a maxed Range powerup reaches) x every level-halting boss's hull.
        float[] lifetimes = { 450f, 547f, 738f, 1000f, 1500f };
        // EACH HULL IS A (body, radius) PAIR, and they are NOT interchangeable: `body` is the
        // centre->edge term the anchor is measured in (ThreatBodyTerm), `radius` is the half-extent
        // the FIELD size scales with (ThreatRadius). For a box they differ by sqrt(2); for a CIRCLE
        // they are the same number, which is why JunkBoss cannot be derived from the others.
        // Measured from each boss's own CollisionType: JunkBoss 60 (circle), ClassicBoss/BattleSkull
        // 106, FakeBoss 127, MarsBoss/StationaryBoss ~141, BrainBoss 233 -> 257 (hw 165 * sqrt2,
        // with `scale` pulsing 1.00 -> 1.10 as its HP drops -- the widest in the game, and the one
        // configuration the exponent damping exists for). 320 and 400 are unreachable today and are
        // swept anyway: the bound has to hold for a boss someone adds later, not just for today's.
        float Sq2 = (float)Math.Sqrt(2.0);
        (float body, float radius)[] hulls =
        {
            (0f, 0f), (60f, 60f), (106f, 106f / Sq2), (127f, 127f / Sq2), (141f, 141f / Sq2),
            (233f, 233f / Sq2), (245f, 245f / Sq2), (257f, 257f / Sq2),
            (320f, 320f / Sq2), (400f, 400f / Sq2)
        };
        int crossings = 0, bandsChecked = 0;
        float worstBand = float.MaxValue;
        bool crossingOk = true, bandOk = true, pushedOutOk = true, boundedOk = true;
        string crossingDetail = "", bandDetail = "", pushedOutDetail = "", boundedDetail = "";
        foreach (bool curve in new[] { false, true })
        {
        classic = curve;
        for (int tier = 0; tier < tierFieldPx.Length; tier++)
        {
            foreach (float life in lifetimes)
            {
                foreach ((float body, float radius) in hulls)
                {
                    float range = tierFieldPx[tier] + radius * sizeScale;
                    float anchorRaw = life * bulletPerMs - body;
                    float anchor = Math.Max(anchorRaw, minAnchor);
                    string where = (classic ? "classic curve, " : "") + "tier " + tier + " life " + life
                        + "ms body " + body + "px"
                        + " (anchor " + anchor.ToString("0.0") + "px, field " + range.ToString("0") + "px)";

                    // 1. AT FIRING RANGE THE NET NEVER POINTS OUT. Where the repellent is still
                    // audible there the two are EQUAL -- that is what the weight is solved for --
                    // and where it has decayed under the floor the attractor holds at the floor and
                    // keeps closing, which is the Range-powerup case the solve alone could not
                    // survive (a solved w of 0 is an inert term).
                    float net = A(anchor, anchor, range) - Repel(anchor, range);
                    if (net < -0.0005f)
                    {
                        crossingOk = false;
                        crossingDetail = where + ": net " + net.ToString("0.000") + " points AWAY at firing range";
                    }
                    else if (Repel(anchor, range) >= noiseFloor)
                    {
                        crossings++;
                        if (Math.Abs(net) > 0.0005f)
                        {
                            crossingOk = false;
                            crossingDetail = where + ": net " + net.ToString("0.000")
                                + " at firing range, expected 0 (the weight is solved for exactly this)";
                        }
                    }

                    // 2. THE PARKED BAND IS WIDER THAN THE SHIP'S STOPPING DISTANCE. The whole-sum
                    // floor turns the crossing into a band of |net| <= floor; a ship entering it at
                    // full speed coasts `stoppingPx` before it halts, so a narrower band is one it
                    // sails through -- the ping-pong the deadzones elsewhere exist to prevent.
                    // Measured on the real curves rather than from the derivative, so it stays true
                    // for whatever shape a later card gives either side.
                    float lo = -1f, hi = -1f;
                    for (float d = 1f; d <= 1400f; d += 0.5f)
                    {
                        if (Math.Abs(A(d, anchor, range) - Repel(d, range)) <= noiseFloor)
                        {
                            if (lo < 0f)
                            {
                                lo = d;
                            }
                            hi = d;
                        }
                        else if (lo >= 0f)
                        {
                            break;
                        }
                    }
                    float band = (lo < 0f) ? 0f : (hi - lo);
                    bandsChecked++;
                    if (band < worstBand)
                    {
                        worstBand = band;
                        bandDetail = where + ": band " + band.ToString("0.0") + "px";
                    }
                    if (band <= stoppingPx)
                    {
                        bandOk = false;
                    }

                    // 3. INSIDE FIRING RANGE THE BOSS PUSHES BACK OUT. The attractor has quieted
                    // (it is anchored at the crossing and grows with distance), so at half firing
                    // range the repellent must win -- that is what makes the equilibrium
                    // self-limiting instead of a point the ship has to hit. Skipped where the
                    // repellent is genuinely zero at the anchor: there is nothing to push with, and
                    // the term correctly keeps closing until there is.
                    float half = anchor * 0.5f;
                    if (Repel(anchor, range) >= noiseFloor && A(half, anchor, range) >= Repel(half, range))
                    {
                        pushedOutOk = false;
                        pushedOutDetail = where + ": at half firing range the attractor "
                            + A(half, anchor, range).ToString("0.00") + " is not out-voted by the repellent "
                            + Repel(half, range).ToString("0.00");
                    }

                    // 4. CLOSING NEVER OUTRANKS NOT DYING, at any distance the world can hold.
                    // Move() keeps only the ANGLE, so a seek that can out-vote a full-strength
                    // threat field is a bot that flies into things to reach them.
                    if (A(1400f, anchor, range) >= MaxSteer)
                    {
                        boundedOk = false;
                        boundedDetail = where + ": " + A(1400f, anchor, range).ToString("0.00")
                            + " at 1400px reaches the threat field's " + MaxSteer;
                    }
                }
            }
        }
        }
        classic = false;

        Check("the net force never points AWAY at firing range, over every tier x weapon x hull",
            crossingOk, crossingDetail.Length > 0 ? crossingDetail
                : (bandsChecked + " combinations, " + crossings + " of them with a repellent still"
                   + " audible at r* (where the crossing is required to be exact)"));
        // THE ASSERTION THE EXPONENT DAMPING EXISTS FOR. Mutation-tested: forcing k to
        // BossApproachExponent (i.e. deleting the damping) fails this and nothing else: the worst
        // cell drops 20.5px -> 3.0px, and the BrainBoss's own cells go 22.2px -> 13.5px at rest and
        // 10.0px at its pulse peak, i.e. through the 11.3px stopping distance. That is the whole
        // reason the damping is not dead code.
        Check("the parked band is wider than the ship's stopping distance, over the WHOLE domain",
            bandOk, "worst " + bandDetail + " vs a stopping distance of " + stoppingPx.ToString("0.0")
            + "px -- below it the ship coasts through the equilibrium and pingpongs"
            + " (" + bandsChecked + " combinations: every tier x weapon x boss hull, including"
            + " BrainBoss's 233->257px pulse and two hulls wider than anything in the game)");
        Check("inside firing range the boss repellent wins, so the equilibrium is self-limiting",
            pushedOutOk, pushedOutDetail.Length > 0 ? pushedOutDetail : "over every combination");
        Check("closing never outranks not dying (the pull stays under the threat field's 4)",
            boundedOk, boundedDetail.Length > 0 ? boundedDetail
                : "ceiling max(BossApproachMaxWeight " + maxWeight + ", the solved anchor weight)");

        // 5. THE SHIPPED CONFIGURATION, spelled out so a future reader can see the actual numbers
        // rather than only the inequalities: the top tier, base weapon, and BRAINBOSS's hull --
        // a boss the approach can actually target. (An earlier revision used the spider boss's
        // 170px here, which section 7 forty lines below asserts is never a boss-approach target
        // at all, so the worked example described a configuration the code cannot enter.)
        // The tier is read off the END of the ladder rather than by index, so inserting a tier
        // cannot silently re-label this example.
        float brainBody = 233f;
        float vhRange = tierFieldPx[tierFieldPx.Length - 1] + (brainBody / Sq2) * sizeScale;
        float vhAnchor = 450f * bulletPerMs - brainBody;
        float vhW = A(vhAnchor, vhAnchor, vhRange);
        Check("a COMMITMENT outranks a DETOUR at engagement range (approach > the 0.8 powerup seek)",
            A(vhAnchor * 1.5f, vhAnchor, vhRange) > 0.8f,
            "top tier / base weapon / BrainBoss: pull " + A(vhAnchor * 1.5f, vhAnchor, vhRange).ToString("0.00")
            + " at 1.5x firing range (anchor " + vhAnchor.ToString("0.0") + "px, solved weight "
            + vhW.ToString("0.000") + ") -- a halting boss stops the level advancing at all,"
            + " a pickup does not");

        // 6. NEGATIVE CONTROL -- the PRE-CARD configuration over the same curve. The standoff was
        // clamp(gunRange * 0.6, 130, 300) as a CENTRE distance carrying a flat 1.1, and the defect
        // was that the boss's own repellent AT THAT POINT is 2.9: the net force pointed away from
        // the very place the ship was being sent. Run it here and require it to FAIL, or every
        // inequality above could be satisfied by a build that never changed anything.
        const float PreCardWeight = 1.1f;
        float preStandoffEdge = Math.Min(Math.Max(450f * bulletPerMs * 0.6f, 130f), 300f) - brainBody;
        float preRepel = Repel(preStandoffEdge, vhRange);
        Check("control: the pre-card standoff+1.1 is OUT-VOTED at its own destination, and this build is not",
            PreCardWeight < preRepel && Math.Abs(A(vhAnchor, vhAnchor, vhRange) - Repel(vhAnchor, vhRange)) <= 0.0005f,
            "pre-card: weight " + PreCardWeight + " against a repellent of " + preRepel.ToString("0.00")
            + " at its " + preStandoffEdge.ToString("0.0") + "px edge standoff (net points AWAY, which is why"
            + " bossfar read ~99%); this build: net 0 at its " + vhAnchor.ToString("0.0") + "px anchor");

        // 7. SPIDERBOSS IS EXCLUDED FROM BOSS APPROACH, EXPLICITLY. It was excluded by omission,
        // which is the same behaviour and no protection: adding it to the list is the obvious edit,
        // and it would make the card's own symptom -- the bot walking into the PARKED boss, its
        // largest single killer -- dramatically worse with nothing failing. BrainBoss is the
        // positive control, or a predicate returning false for everything would pass this.
        Type spiderT = asm.GetType("EvilAliens.SpiderBoss", true);
        Type brainT = asm.GetType("EvilAliens.BrainBoss", true);
        object spider = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(spiderT);
        object brain = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(brainT);
        bool spiderPriority = (bool)priorityM.Invoke(null, new object[] { spider });
        bool brainPriority = (bool)priorityM.Invoke(null, new object[] { brain });
        Check("SpiderBoss is NOT a boss-approach target (it must be dodged, not sought)",
            !spiderPriority, "IsAiPriorityTarget(SpiderBoss) = " + spiderPriority);
        Check("control: BrainBoss IS one, so the predicate is not simply refusing everything",
            brainPriority, "IsAiPriorityTarget(BrainBoss) = " + brainPriority);
        return 0;
    }

    // ---- the directional repellent shapes (card e425781b) ----------------------------------
    //
    // PlayerShip.EvaluateSweptShape is a pure function of geometry, so the whole design is
    // checkable here with no game, no browser and no rig -- and it MUST be checked at FIXED
    // POINTS rather than by any aggregate. The card's own readout trap says why: a field's mean
    // strength over a run is a selection effect (far contributions stop existing rather than
    // getting weaker), so two shapes can only be compared by evaluating both at the same places.
    //
    // Reflection rather than a mirrored formula, deliberately: a transcription of the maths here
    // would agree with itself forever while the shipped shape drifted.
    private static int ProbeAiConeShape(Assembly asm)
    {
        Type ship = asm.GetType("EvilAliens.PlayerShip", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo eval = ship.GetMethod("EvaluateSweptShape", anyStatic);
        if (eval == null)
        {
            Console.WriteLine("FAIL: could not reflect PlayerShip.EvaluateSweptShape -- renamed or no longer static?");
            return 2;
        }
        Type shapeType = eval.ReturnType;
        // Off the method signature, not by name: Vector2 lives in the KNI assembly, not in the
        // one being probed, so a name lookup here always misses.
        Type vec2 = eval.GetParameters()[0].ParameterType;
        object V(float x, float y) => Activator.CreateInstance(vec2, x, y);
        float Field(object shape, string name) =>
            (float)shapeType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(shape);
        object VField(object shape, string name) =>
            shapeType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(shape);
        float VX(object v) => (float)vec2.GetField("X").GetValue(v);
        float VY(object v) => (float)vec2.GetField("Y").GetValue(v);

        const float MaxSteerStrength = 4f;
        // A ship-sized half-extent, so the wedge's survivable-gap arithmetic is exercised at a
        // realistic value rather than zero.
        const float ShipHalf = 20f;

        // Evaluate at a point expressed in the SHAPE's own frame: `ahead` px along the travel
        // direction, `sideways` px across it. The mover here travels +X from the origin, so the
        // frame is the identity and a reader can check any row by hand.
        object At(float ahead, float sideways, float speed, float halfWidth, bool wedge,
            float anchorX = 0f, float anchorY = 300f)
        {
            return eval.Invoke(null, new object[]
            {
                V(anchorX + ahead, anchorY + sideways), V(0f, 0f), V(anchorX, anchorY),
                V(speed, 0f), halfWidth, ShipHalf, MaxSteerStrength, wedge
            });
        }

        Console.WriteLine("[logic_probe] AI directional repellent shapes (card e425781b)");

        // The asteroid case the card is designed around: 0.38 px/ms, a ~30px half-extent.
        const float AsteroidSpeed = 0.38f;
        const float AsteroidHalf = 30f;
        float LeadMs = (float)ship.GetField("DefaultConeLeadMs", anyStatic).GetRawConstantValue();
        float coneLen = AsteroidSpeed * LeadMs;

        // 1. THE MESA. Full strength anywhere inside the swept body, because a repellent's
        // meaningful domain starts at the collision EDGE -- inside is death, and curve values
        // there are wasted dynamic range. So the on-axis value and the value at the corridor's
        // own edge must be identical, and both must be the peak.
        object onAxis = At(1f, 0f, AsteroidSpeed, AsteroidHalf, false);
        object atEdge = At(1f, AsteroidHalf * 0.995f, AsteroidSpeed, AsteroidHalf, false);
        Check("the cone is a MESA: full strength on the axis and at the swept body's edge alike",
            Math.Abs(Field(onAxis, "ConeStrength") - Field(atEdge, "ConeStrength")) < 0.05f
                && Field(onAxis, "ConeStrength") > MaxSteerStrength * 0.98f,
            "on-axis " + Field(onAxis, "ConeStrength").ToString("0.00") + " vs at the body edge "
            + Field(atEdge, "ConeStrength").ToString("0.00") + " of a peak " + MaxSteerStrength);

        // 2. THE BAND THE CARD EXISTS FOR, and it is measured against the REAL radial field
        // rather than against a number copied out of the card. The circle falls under the 0.8
        // seek at 199px while the bot's measured mean edge distance from an asteroid is 252px --
        // i.e. it spends its life outside the only warning it has. The cone's whole job is to
        // have authority out there, ALONG the trajectory, while leaving the transverse direction
        // cheap. Note this does NOT claim the cone reaches 252px on its own: its own perimeter is
        // ~238px, and what carries the rest is that the term is measured from a mover's PATH, so
        // the ship meets it long before the circle grows.
        MethodInfo fieldStrength = ship.GetMethod("ThreatFieldStrength", anyStatic, null,
            new[] { typeof(float), typeof(float), typeof(float), typeof(bool) }, null);
        if (fieldStrength == null)
        {
            Console.WriteLine("FAIL: could not reflect PlayerShip.ThreatFieldStrength(t, max, falloff, classic)");
            return 2;
        }
        // The asteroid's shipped radial field: ThreatFieldBasePx 190 + halfExtent * 1.8, on the
        // (1-t)^3 curve. Reflected, so retuning the field re-derives the comparison.
        float fieldPx = (float)ship.GetField("VeryHardThreatFieldBasePx", anyStatic).GetRawConstantValue();
        float sizeScale = (float)ship.GetField("DefaultThreatFieldSizeScale", anyStatic).GetRawConstantValue();
        float falloff = (float)ship.GetField("DefaultThreatFieldFalloff", anyStatic).GetRawConstantValue();
        float radialRange = fieldPx + AsteroidHalf * sizeScale;
        float Radial(float edgeDist) => edgeDist >= radialRange ? 0f : (float)fieldStrength.Invoke(
            null, new object[] { edgeDist / radialRange, MaxSteerStrength, falloff, false });
        // Where each shape stops out-voting the seek. `Cone` is read on the axis, where its
        // along-distance and the circle's edge-distance are the same quantity.
        float Cone(float ahead) => Field(At(ahead, 0f, AsteroidSpeed, AsteroidHalf, false), "ConeStrength");
        float Perimeter(Func<float, float> f)
        {
            float last = 0f;
            for (float x = 1f; x < 900f; x += 1f)
            {
                if (f(x) >= 0.8f)
                {
                    last = x;
                }
            }
            return last;
        }
        float conePerimeter = Perimeter(Cone), radialPerimeter = Perimeter(Radial);
        Check("the cone's warning perimeter reaches FURTHER than the circle's",
            conePerimeter > radialPerimeter,
            "the cone holds 0.8 out to " + conePerimeter.ToString("0") + "px along the path, the"
            + " radial field to " + radialPerimeter.ToString("0") + "px from the hull -- and the"
            + " bot's measured mean edge distance is 252px, outside both");
        Check("and in the 200-250px band it is worth several times the circle",
            Cone(200f) > Radial(200f) * 2f && Cone(200f) > 0.8f,
            "at 200px: cone " + Cone(200f).ToString("0.00") + " vs radial "
            + Radial(200f).ToString("0.00") + "; at 250px: cone " + Cone(250f).ToString("0.00")
            + " vs radial " + Radial(250f).ToString("0.00"));

        // 3. ACROSS THE AXIS IT MUST GET CHEAP FAST, or threading a gap between two rocks stops
        // being possible and the shape is just a wider circle -- which is the failure mode three
        // separate radial sweeps already measured. Offsets are FRACTIONS of the across-axis
        // width, not pixels: that width is a tunable, and a probe pinned to the value it happened
        // to be swept to would fail on the next honest retune instead of on a broken shape.
        float coneWidth = (float)ship.GetField("DefaultConeWidthPx", anyStatic).GetRawConstantValue();
        // Read at 1px ahead so the corridor has not tapered yet and the offset from the body edge
        // IS the across-axis distance -- otherwise the taper quietly shifts every reading.
        float across0 = Field(At(1f, AsteroidHalf, AsteroidSpeed, AsteroidHalf, false), "ConeStrength");
        float acrossHalf = Field(At(1f, AsteroidHalf + coneWidth * 0.5f, AsteroidSpeed, AsteroidHalf, false), "ConeStrength");
        float acrossFull = Field(At(1f, AsteroidHalf + coneWidth, AsteroidSpeed, AsteroidHalf, false), "ConeStrength");
        Check("across the axis the cone decays far faster than along it",
            acrossHalf < across0 * 0.2f && acrossFull == 0f,
            "at the corridor edge " + across0.ToString("0.00") + ", at half the across-axis width ("
            + (coneWidth * 0.5f).ToString("0") + "px) " + acrossHalf.ToString("0.00")
            + ", at the full width " + acrossFull.ToString("0.00")
            + " -- against 75% of peak at half the cone's LENGTH");

        // 4. DIRECTION. The push is purely TRANSVERSE -- following the mesa's along-axis gradient
        // would send the ship further down the mover's own track, and it cannot outrun an
        // asteroid (0.38 px/ms against ShipMaxSpeed 0.33). A component along the travel direction
        // would be that mistake.
        object above = At(120f, -50f, AsteroidSpeed, AsteroidHalf, false);
        object below = At(120f, 50f, AsteroidSpeed, AsteroidHalf, false);
        object dirAbove = VField(above, "ConeDir");
        object dirBelow = VField(below, "ConeDir");
        Check("the cone pushes ACROSS the path, never along it",
            Math.Abs(VX(dirAbove)) < 0.001f && Math.Abs(VX(dirBelow)) < 0.001f,
            "push at 50px above the axis = (" + VX(dirAbove).ToString("0.00") + ", "
            + VY(dirAbove).ToString("0.00") + "), below = (" + VX(dirBelow).ToString("0.00")
            + ", " + VY(dirBelow).ToString("0.00") + ")");
        Check("and it pushes AWAY from the axis on whichever side the ship is",
            VY(dirAbove) < 0f && VY(dirBelow) > 0f, "as above");

        // 5. BEHIND A MOVER IS SAFE. Nothing is coming that way, and the body itself is the
        // radial field's business.
        Check("nothing is projected behind the mover",
            Field(At(-100f, 0f, AsteroidSpeed, AsteroidHalf, true), "ConeStrength") == 0f
                && Field(At(-100f, 0f, AsteroidSpeed, AsteroidHalf, true), "WedgeStrength") == 0f,
            "100px behind an asteroid on its own axis");

        // 6. LENGTH SCALES WITH SPEED, which is what makes one rule cover a drifting rock and a
        // screen-crossing boss with no per-type code.
        float slowLen = Field(At(1f, 0f, 0.2f, AsteroidHalf, false), "ConeLength");
        float fastLen = Field(At(1f, 0f, 0.8f, AsteroidHalf, false), "ConeLength");
        Check("cone length scales with the mover's speed",
            fastLen > slowLen * 3.9f && fastLen < slowLen * 4.1f,
            "0.2px/ms -> " + slowLen.ToString("0") + "px, 0.8px/ms -> " + fastLen.ToString("0")
            + "px (a 4x speed for a 4x length)");

        // ---- the LANE WEDGE ----
        // The spider boss's top lane: a 186.67px band snapped to y=93.3, i.e. hugging the ceiling.
        const float LaneHalf = 186.66667f / 2f;
        const float TopLaneY = 186.66667f * 0.5f;
        const float BossSpeed = 0.78f;

        // 7. A LANE FLYBY IS ASYMMETRIC, and it must force the ship AWAY from the hugged edge.
        // A symmetric cone would offer the gap between the path and the ceiling as an escape, and
        // that gap is a trap -- the ship dodges into it and is crushed as the boss arrives.
        object inLane = At(300f, 0f, BossSpeed, LaneHalf, true, 0f, TopLaneY);
        object wedgeDir = VField(inLane, "WedgeDir");
        Check("a lane flyby hugging the TOP edge forces the ship DOWN, out of the lane",
            Field(inLane, "WedgeStrength") > 0f && VY(wedgeDir) > 0.99f,
            "wedge " + Field(inLane, "WedgeStrength").ToString("0.00") + " pushing ("
            + VX(wedgeDir).ToString("0.00") + ", " + VY(wedgeDir).ToString("0.00") + ")");

        // 8. THE TRAPPED SIDE IS CLOSED AT FULL STRENGTH ALL THE WAY TO THE EDGE, which is the
        // sketch: everything between the flight path and the nearer screen edge is off limits, so
        // the only downhill direction is out.
        float trapped = Field(At(300f, -LaneHalf - 40f, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength");
        float centre = Field(At(300f, 0f, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength");
        Check("the wedge is FLAT across the whole trapped side (path to hugged edge)",
            Math.Abs(trapped - centre) < 0.001f && trapped > 0f,
            "40px above the band " + trapped.ToString("0.00") + " vs on the centre line "
            + centre.ToString("0.00"));

        // 9. AND IT DEGRADES ON THE FAR SIDE. This is the subtlest part of the shape and the
        // likeliest to regress silently: a ship that has ALREADY escaped must be nudged, not
        // shoved, or the wedge becomes a wall on the safe side too. Offsets are fractions of the
        // across-axis width, for the reason given at check 3.
        float justOut = Field(At(300f, LaneHalf + coneWidth * 0.05f, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength");
        float wellOut = Field(At(300f, LaneHalf + coneWidth * 0.5f, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength");
        float farOut = Field(At(300f, LaneHalf + coneWidth, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength");
        Check("past the band's far edge the wedge falls off, strictly and to nothing",
            justOut < centre && wellOut < justOut && farOut == 0f,
            "on the centre line " + centre.ToString("0.00") + " -> just out "
            + justOut.ToString("0.00") + " -> half the width out " + wellOut.ToString("0.00")
            + " -> a full width out " + farOut.ToString("0.00"));
        // The far-side falloff must BE the cone's across-axis one rather than a second rule of its
        // own -- both read at the same fraction of the width, and the cone's at 1px ahead so its
        // corridor has not tapered.
        float coneRatio = acrossHalf / Math.Max(across0, 0.0001f);
        Check("control: the far-side falloff is the CONE's across-axis one, not a second rule",
            Math.Abs(wellOut / Math.Max(centre, 0.0001f) - coneRatio) < 0.01f,
            "wedge decays to " + (wellOut / Math.Max(centre, 0.0001f)).ToString("0.000")
            + " of peak half a width out, the cone to " + coneRatio.ToString("0.000"));

        // 10. THE MIDDLE LANE IS NOT A LANE. It hugs neither edge, so it gets the symmetric cone
        // and the ship may leave either way. Stating it because the hand-rolled escape this shape
        // replaced forced the middle lane DOWNWARD unconditionally, and that difference is
        // exactly the kind of quiet behavioural change a future reader will come hunting for.
        const float MidLaneY = 186.66667f * 1.5f;
        Check("the MIDDLE lane raises no wedge -- it hugs no edge, so either side is an escape",
            Field(At(300f, 0f, BossSpeed, LaneHalf, true, 0f, MidLaneY), "WedgeStrength") == 0f,
            "band centred at y=" + MidLaneY.ToString("0") + " leaves "
            + (MidLaneY - LaneHalf).ToString("0") + "px above and "
            + (600f - MidLaneY - LaneHalf).ToString("0") + "px below");

        // 11. NEGATIVE CONTROL, and the one that stops the wedge eating the game. A band narrower
        // than the room a ship needs is an OBSTACLE, not a corridor -- the ship can cross its path
        // -- so it must raise no wedge however close to an edge it drifts. Before this gate
        // existed every UFO in SpaceDodge wedged at mean 4.25 simply for entering from the top,
        // which out-votes the entire rest of the field.
        Check("control: a small mover hugging the ceiling raises NO wedge, however close",
            Field(At(120f, 0f, AsteroidSpeed, AsteroidHalf, true, 0f, 10f), "WedgeStrength") == 0f,
            "a " + AsteroidHalf + "px half-extent asteroid centred 10px from the top edge;"
            + " the survivable gap is 2*(ship 20px + an 11.3px stopping distance)");
        Check("control: the same geometry at a LANE's half-extent does raise one",
            Field(At(120f, 0f, BossSpeed, LaneHalf, true, 0f, TopLaneY), "WedgeStrength") > 0f,
            "so the discriminator is the band's width, not its position");

        // 12. AND THE WEDGE MUST OUT-VOTE THE FIELD IT SITS IN. The whole band is simply death,
        // so it has to beat the station pull, a powerup detour and the edge pushes combined --
        // which is why it is held at the strength the hand-rolled escapes used.
        float wedgeStrength = (float)ship.GetField("DefaultLaneWedgeStrength", anyStatic).GetRawConstantValue();
        Check("the wedge out-ranks every other steering term",
            wedgeStrength > MaxSteerStrength,
            "DefaultLaneWedgeStrength " + wedgeStrength + " against the threat field's peak "
            + MaxSteerStrength + " -- being in the lane is not a risk to weigh, it is a death");
        return 0;
    }

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
            new { Flag = "ripple", Prop = "Ripple", Good = "0.375", RejectsNeg = false },
            new { Flag = "rippleamp", Prop = "RippleAmp", Good = "0.375", RejectsNeg = false },
            new { Flag = "rippleradius", Prop = "RippleRadius", Good = "0.375", RejectsNeg = true },
            new { Flag = "rippleduration", Prop = "RippleDuration", Good = "0.375", RejectsNeg = true },
            new { Flag = "ripplewidth", Prop = "RippleWidth", Good = "0.375", RejectsNeg = true },
            new { Flag = "ripplefalloff", Prop = "RippleFalloff", Good = "0.375", RejectsNeg = true },
            new { Flag = "ripplerim", Prop = "RippleRim", Good = "0.375", RejectsNeg = false },
            // ?ripplephase= is deliberately absent from this table: a NEGATIVE value is a
            // legal spelling there (it means "live, not parked" -- see the case's comment),
            // so the sweep's shared "a negative is either clamped or refused" shape does not
            // describe it. Its own leg is below, next to ?ripplecenter's.
            new { Flag = "ripplepower", Prop = "RipplePower", Good = "0.375", RejectsNeg = true },
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
            new { Flag = "netjitter", Prop = "NetJitterMs", Good = "0.375", RejectsNeg = true },
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

        // ?ripplecenter= is the OPPOSITE call on the same shape (card 5f38ed35): it is ONE
        // Vector2, so half of a pair is not a usable setting -- taking the good axis would park
        // the screenshot ring somewhere nobody asked for. Both axes or neither, and the earlier
        // valid value must survive the refusal.
        run("?ripplecenter=123,456");
        string outRc = run("?ripplecenter=400,3O0");
        object rcHeld = flags.GetProperty("RippleCenter", anyStatic).GetValue(null);
        Check("?ripplecenter= refuses a HALF-valid pair and keeps the whole earlier value",
            rcHeld != null && rcHeld.ToString().Contains("123") && rcHeld.ToString().Contains("456")
            && outRc.Contains("unknown ?ripplecenter= value '400,3O0'")
            && outRc.Contains("staying on 123,456"),
            "center=" + rcHeld + " said: " + FirstLine(outRc));
        flags.GetProperty("RippleCenter", anyStatic).SetValue(null, null);

        // ?ripplephase= has a THIRD state the sweep table cannot express: negative means
        // "live" (un-parked), the same spelling eaRipple.park(-1) and the panel's slider use.
        // Clamping it to 0 instead would PARK on the exact value a user copies out of the
        // panel to stop parking -- a silent wrong-way-round bug -- so pin all three states.
        run("?ripplephase=0.4");
        Check("?ripplephase= parks on a value in range",
            Equals(flags.GetProperty("RipplePhase", anyStatic).GetValue(null), 0.4f),
            "phase=" + flags.GetProperty("RipplePhase", anyStatic).GetValue(null));
        run("?ripplephase=-1");
        Check("?ripplephase= NEGATIVE un-parks rather than parking at 0",
            flags.GetProperty("RipplePhase", anyStatic).GetValue(null) == null,
            "phase=" + flags.GetProperty("RipplePhase", anyStatic).GetValue(null));
        run("?ripplephase=0.4");
        string outPhase = run("?ripplephase=hlf");
        Check("?ripplephase= reports a bad value and keeps the parked one",
            Equals(flags.GetProperty("RipplePhase", anyStatic).GetValue(null), 0.4f)
            && outPhase.Contains("unknown ?ripplephase= value 'hlf'")
            && outPhase.Contains("staying on 0.4"),
            "phase=" + flags.GetProperty("RipplePhase", anyStatic).GetValue(null)
            + " said: " + FirstLine(outPhase));
        flags.GetProperty("RipplePhase", anyStatic).SetValue(null, null);

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
            ("TryFxKind",       "EvilAliensWeb.Compat.Net.NetFxKind"),
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
            // Six outs since the transient-feedback cards: the trailing `short` flag.
            object[] a = { frame, null, null, null, null, null };
            bool ok = (bool)msgDec.Invoke(null, a);
            return new ValueTuple<bool, object, object>(ok, a[1], a[2]);
        };
        ValueTuple<bool, object, object> badStyle =
            decodeMsg((byte[])msgEnc.Invoke(null, new object[] { (ushort)7, (byte)200, (byte)200, 0f, "hello", false }));
        Check("EvMessage: an out-of-enum style is CLAMPED, not refused",
            badStyle.Item1 && Equals(badStyle.Item2, Enum.Parse(msgTypeEnum, "starwarsblue")),
            badStyle.Item1 ? badStyle.Item2.ToString() : "REFUSED");
        Check("EvMessage: an out-of-enum speech cue clamps to Nothing",
            badStyle.Item1 && Convert.ToInt32(badStyle.Item3) == 0, null);
        // ... and a VALID style is not clamped away, or the row above would pass vacuously.
        ValueTuple<bool, object, object> goodStyle = decodeMsg((byte[])msgEnc.Invoke(null, new object[]
            { (ushort)8, Convert.ToByte(Enum.Parse(msgTypeEnum, "redwarning")), (byte)1, 0f, "hello", false }));
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
            "5. scaled-i16 motion rates",
        };
        return RunBrowserSuite(asm, "EvilAliensWeb.Compat.Net.NetWireTest", sections, minAssertions: 88);
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
        return RunBrowserSuite(asm, "EvilAliensWeb.Compat.Net.NetHostTest", sections, minAssertions: 36);
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

        // ---- 3. off-canvas clicks are not game input (card 0fe23476) --------------------------
        // KNI's own mouse listeners are on the WINDOW, so a click on any outside-#app DOM overlay
        // still reaches the game's button state at that cursor position -- which is how clicking
        // the room-code prompt's JOIN button hit the CANCEL row of the NetStatusMenu behind it.
        // JS flags the press as off-canvas; Filter is what the flag actually does.
        //
        // This leg CANNOT be checked in the browser by clicking, either: the failure it guards
        // (the phantom press on release) needs the button held across the moment the flag lifts,
        // which is a drag, and its evidence is the absence of an event. Here it is three calls.
        MethodInfo suppress = latch.GetMethod("SetSuppressed", anyStatic);
        MethodInfo filter = latch.GetMethod("FilterOffCanvas", anyStatic);
        if (suppress == null || filter == null)
        {
            Console.WriteLine("  FAIL could not reflect the suppression targets (SetSuppressed="
                + (suppress != null) + " FilterOffCanvas=" + (filter != null) + ") -- renamed or moved?");
            failures++;
            return 0;
        }
        Action<bool> setSuppressed = on => suppress.Invoke(null, new object[] { on });
        Func<int, bool, bool> filterKey = (idx, raw) => (bool)filter.Invoke(null, new object[] { idx, raw });

        // Positive control FIRST: with nothing suppressed the filter is a pass-through, so every
        // assertion below is about the flag rather than about a filter that always says false.
        Check("un-suppressed, a held button reads held", filterKey(mouse1, true), null);
        Check("un-suppressed, a released button reads released", !filterKey(mouse1, false), null);

        setSuppressed(true);
        Check("a press that began off-canvas reads released", !filterKey(mouse1, true), null);
        // THE PHANTOM-EDGE ASSERTION. The flag lifts on pointerup, but a drag can end over the
        // canvas with the button still down for a tick or two; a plain flag would then hand
        // InputHandler a rising edge and land the click it just refused.
        setSuppressed(false);
        Check("the tail of that same press is still swallowed", !filterKey(mouse1, true),
            "a pass here means an overlay drag ending on the canvas fires a click");
        Check("still swallowed on a later tick", !filterKey(mouse1, true), null);
        Check("a physical release clears it", !filterKey(mouse1, false), null);
        Check("and the NEXT press is honoured again", filterKey(mouse1, true),
            "a fail here is a dead mouse, which is worse than the bug being fixed");
        filterKey(mouse1, false);

        // Per-button, because the two are filtered in the same tick with independent states: a
        // shared flag would let whichever button was polled first clear the other one's swallow.
        setSuppressed(true);
        filterKey(mouse1, true);
        filterKey(mouse2, true);
        setSuppressed(false);
        Check("releasing one button does not un-swallow the other",
            !filterKey(mouse2, false) && !filterKey(mouse1, true), null);
        filterKey(mouse1, false);

        // An ALREADY-HELD button is not collateral. The flag is per-gesture in JS but applied
        // per-button here, so without the carve-out, right-clicking the FPS HUD while holding
        // fire on the canvas would stop the ship shooting mid-hold until you released and
        // re-pressed -- a suppression fix that breaks the input it was protecting.
        filterKey(mouse1, true);                       // a press established on the canvas
        setSuppressed(true);                           // ... then something off-canvas is pressed
        Check("an already-held button keeps reporting held", filterKey(mouse1, true),
            "a fail here means an off-canvas click cancels an unrelated held button");
        Check("but a button pressed DURING suppression is still swallowed",
            !filterKey(mouse2, true), null);
        setSuppressed(false);
        filterKey(mouse1, false);
        filterKey(mouse2, false);
        Check("both buttons settle back to released",
            !filterKey(mouse1, false) && !filterKey(mouse2, false), null);

        // Suppression must also drop anything the CANVAS latch had already banked this tick --
        // otherwise the sub-tick rescue would smuggle through exactly the press being refused.
        pressButton(LeftButton);
        setSuppressed(true);
        Check("suppression drops a banked sub-tick latch", !consumeKey(mouse1), null);
        setSuppressed(false);
        filterKey(mouse1, false);
        filterKey(mouse2, false);

        // ---- 4. the clickable back tip is an EDGE, and lives one frame (card 2a4110d0) -------
        // Compat/BackTipHit turns a click on the bottom-left "(B) back" label into a synthetic
        // Esc. Two properties carry it, and BOTH are invisible in a screenshot:
        //  - it must fire on the PRESS EDGE, not the button level. A level fires on a press that
        //    began somewhere else: mouse-down on a menu row and drag to the corner backs out,
        //    and in-game a held fire button un-pauses the frame the pause overlay draws the tip
        //    under the resting cursor. A browser cannot settle this either -- an automated drag
        //    is sub-tick, so it passes on the broken code by luck.
        //  - the box must live exactly ONE frame, or a screen that stopped drawing the tip (i.e.
        //    gameplay) still has a live back-target sitting in its bottom-left corner.
        Type backTip = asm.GetType("EvilAliensWeb.Compat.BackTipHit", true);
        MethodInfo record = backTip.GetMethod("Record", anyStatic);
        MethodInfo consumeTip = backTip.GetMethod("ConsumeClick", anyStatic);
        if (record == null || consumeTip == null)
        {
            Console.WriteLine("  FAIL could not reflect BackTipHit (Record=" + (record != null)
                + " ConsumeClick=" + (consumeTip != null) + ") -- renamed or moved?");
            failures++;
            return 0;
        }
        // Vector2 comes off the loaded assembly's own reference -- logic_probe deliberately does
        // not compile against XNA (see the header), so it is built by reflection like the
        // CollisionBox set above.
        Type tipVec2 = consumeTip.GetParameters()[0].ParameterType;
        Func<float, float, object> tipVec = (x, y) => Activator.CreateInstance(tipVec2, new object[] { x, y });
        // The real MenuScene geometry: icon at SafeZone.Left, label to its right, on the tips
        // baseline, down to SafeZone.Bottom.
        Action recordTip = () => record.Invoke(null, new object[] { 40f, 146f, 534f, 570f });
        Func<float, float, bool, bool> clickTip = (x, y, pressed) =>
            (bool)consumeTip.Invoke(null, new object[] { tipVec(x, y), pressed });

        recordTip();
        Check("a press INSIDE the tip is a back", clickTip(93f, 552f, true), null);
        recordTip();
        Check("a press OUTSIDE it is not", !clickTip(400f, 300f, true), null);

        // THE EDGE ASSERTION. Same cursor, same box, button merely HELD -- the drag case.
        recordTip();
        Check("the button merely being HELD is not a back", !clickTip(93f, 552f, false),
            "a pass on `true` here means dragging onto the tip backs out");

        // One frame only: a screen that draws no tip must offer nothing to hit.
        recordTip();
        clickTip(0f, 0f, false);                       // the tick that spends the recording
        Check("an unrecorded frame has no back target", !clickTip(93f, 552f, true),
            "a pass here means the corner stays clickable during gameplay");

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
    // `card` defaults to 25ad0659, which is where this runner and its first two callers came
    // from; a suite belonging to a different card passes its own (card 0d6ffe70 -- a heading
    // naming the wrong card sends the next reader to the wrong write-up).
    private static int RunBrowserSuite(Assembly asm, string typeName, string[] sections, int minAssertions,
                                       string card = "25ad0659")
    {
        string shortName = typeName.Substring(typeName.LastIndexOf('.') + 1);
        Type suite = asm.GetType(typeName, true);
        MethodInfo run = suite.GetMethod("Run", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (run == null)
        {
            Console.WriteLine("FAIL: could not reflect " + shortName + ".Run -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] Compat/Net/" + shortName + " (card " + card + ")");
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

    // Card df8f1ef7 -- which LEVELS may be advertised in the public game browser. The decision
    // this covers is NetListing.IsNetEligibleLevel, the pure half of ComputeEligible (the rest
    // reaches ServiceHelper and a live GameScene, which this tool cannot construct).
    //
    // It is verified here rather than by eye because the failure is silent and remote: a level
    // that should not be listable simply appears in a stranger's browser, on a screen nobody
    // running the game is looking at. The sweep is EXHAUSTIVE over the Levels enum, so a level
    // ADDED later is judged too -- it will show up as eligible, and whoever added it has to say
    // whether that is right.
    // Card 0d6ffe70 -- which rows the host pause menu's "Online Play" submenu offers. The whole
    // card is a predicate over five booleans that are each expensive to reach in a live game (a
    // real peer, a real signaling listing, a level, a pause) and free to state as data, so the
    // suite sweeps all 32 combinations of the REAL NetHostMenu.Entries. Runnable here because it
    // touches no Game, no ServiceHelper and no clock -- the same property that makes eaNetHost a
    // case set. Note NetHostMenuTest's sections 1-3 are per-STATE loops, so its assertion count
    // is dominated by the sweep and a shrunk state space fails the minAssertions floor here.
    private static int ProbeHostMenu(Assembly asm)
    {
        string[] sections =
        {
            "1. the exhaustive state sweep",
            "2. entry 0 is never destructive",
            "3. Available agrees with Entries",
            "4. the two shapes never coexist",
            "5. non-degeneracy + the pre-card control",
            "6. labels",
        };
        return RunBrowserSuite(asm, "EvilAliensWeb.Compat.Net.NetHostMenuTest", sections, minAssertions: 44, card: "0d6ffe70");
    }

    // Card b4a9fe60 -- the angle a ship flies IN on and, at level end, flies OUT on.
    // GameScene.SpawnDirectionFor is the one source for it now; both net puppet spawn sites used
    // to hard-code South instead, so on a West level the remote ship left upward while every
    // local ship left to the right, on BOTH peers' screens.
    //
    // WHY THIS IS HERE AND NOT ONLY IN net_level_end.txt. That probe drives the decision END TO
    // END, which is the stronger evidence -- but only on the level it boots, so it covers South
    // (the constant) and West (Level 2). NORTH ships on ClassicAliens, a challenge level whose
    // victory a rig cannot reach, so the third arm has no end-to-end route at all. A pure sweep
    // is what covers it, and it needs no Game, no browser and no level.
    //
    // The angles are asserted against the VECTORS they have to produce rather than restated as
    // the same three literals: screen Y grows downward, so South must point UP the screen. A
    // transcription that swapped two arms would satisfy any literal-vs-literal comparison.
    private static int ProbeSpawnDirection(Assembly asm)
    {
        Type scene = asm.GetType("EvilAliens.GameScene", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags anyNested = BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo forType = scene.GetMethod("SpawnDirectionFor", anyStatic);
        Type spawnType = scene.GetNestedType("PlayerSpawnType", anyNested);
        if (forType == null || spawnType == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (SpawnDirectionFor="
                + (forType != null) + " PlayerSpawnType=" + (spawnType != null) + ") -- renamed or moved?");
            return 2;
        }

        Console.WriteLine("[logic_probe] GameScene.SpawnDirectionFor (card b4a9fe60)");

        Func<string, float> dir = name =>
            (float)forType.Invoke(null, new object[] { Enum.Parse(spawnType, name) });
        // The game's own convention (MyMath.AngleToVector): (cos, sin), screen Y downward.
        Func<float, string> vec = a => "("
            + Math.Round(Math.Cos(a), 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", "
            + Math.Round(Math.Sin(a), 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        Func<float, double> dx = a => Math.Cos(a);
        Func<float, double> dy = a => Math.Sin(a);

        float south = dir("South");
        float west = dir("West");
        float north = dir("North");

        Check("South leaves UPWARD -- +x flat, -y " + vec(south),
            Math.Abs(dx(south)) < 0.001 && dy(south) < -0.999, south.ToString());
        Check("West leaves RIGHTWARD " + vec(west),
            dx(west) > 0.999 && Math.Abs(dy(west)) < 0.001, west.ToString());
        Check("North leaves DOWNWARD " + vec(north),
            Math.Abs(dx(north)) < 0.001 && dy(north) > 0.999, north.ToString());

        // NON-DEGENERACY. Every leg above is a shape test, so an implementation collapsing two
        // arms onto one angle would still have to fail one of them -- but a FOURTH arm added
        // later and quietly given an existing value would not, and that is exactly how the
        // shipped bug looked (every puppet on South, whatever the level said).
        Check("the three arms are three DISTINCT angles",
            south != west && west != north && south != north,
            south + " / " + west + " / " + north);

        // Every value of the enum is covered -- so a new PlayerSpawnType added without an arm
        // (which would fall through to the switch's South default and silently ship the very bug
        // this card fixed) is caught here rather than by someone watching a level end.
        string[] known = Enum.GetNames(spawnType);
        Check("the sweep covers the whole enum (" + string.Join(",", known) + ")",
            known.Length == 3, known.Length.ToString());

        return 0;
    }

    private static int ProbeListingLevels(Assembly asm)
    {
        Type listing = asm.GetType("EvilAliensWeb.Compat.Net.NetListing", true);
        Type levels = asm.GetType("EvilAliens.Levels", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo eligible = listing.GetMethod("IsNetEligibleLevel", anyStatic);
        if (eligible == null)
        {
            Console.WriteLine("FAIL: could not reflect NetListing.IsNetEligibleLevel -- renamed or moved?");
            return 2;
        }
        Func<string, bool> listable = name =>
            (bool)eligible.Invoke(null, new object[] { Enum.Parse(levels, name) });

        Console.WriteLine("[logic_probe] NetListing.IsNetEligibleLevel (card df8f1ef7)");

        // The refusal set, stated independently of the implementation's shape (it tests three
        // separate conditions; this is one list).
        string[] refused = { "Tutorial", "WebcamAliens", "TeamChallenge", "Demo1", "Demo2", "Demo3" };

        // Every name below is fed to Enum.Parse, which THROWS on a miss -- and a stack trace
        // instead of a FAIL line is the one way this set could report nothing useful. A renamed
        // or deleted level is exactly the change that should land here loudly.
        foreach (string name in refused)
        {
            if (Array.IndexOf(Enum.GetNames(levels), name) < 0)
            {
                Check("refusal-set level '" + name + "' still exists", false,
                    "renamed or removed from Levels -- update the set");
                return 0;
            }
        }

        // The card itself.
        Check("Tutorial is NOT listable", !listable("Tutorial"),
            "a solo scripted walkthrough advertised to strangers");

        // The pre-existing refusals must survive the refactor -- IsNetEligibleLevel was extracted
        // out of ComputeEligible, so the whole set is under test, not just the new member.
        foreach (string name in refused)
        {
            if (name == "Tutorial")
            {
                continue;
            }
            Check(name + " is still NOT listable", !listable(name), null);
        }

        // POSITIVE CONTROL, and the point of sweeping the ENUM rather than a hand-written list:
        // a predicate stuck at false would satisfy every assertion above. Every level not in the
        // refusal set must still be listable, and a level appended to the enum lands here.
        int eligibleCount = 0;
        foreach (string name in Enum.GetNames(levels))
        {
            if (Array.IndexOf(refused, name) >= 0)
            {
                continue;
            }
            bool ok = listable(name);
            if (ok)
            {
                eligibleCount++;
            }
            else
            {
                Check(name + " should be listable", false, "not in the refusal set");
            }
        }
        // The sweep above already FAILS per level, so this only has to catch the degenerate
        // shape it cannot: a predicate stuck at false, which produces no per-level failure the
        // reader can distinguish from "the refusal set grew".
        Check("the refusal set is not everything", eligibleCount > 0,
            "eligible=" + eligibleCount + " of " + Enum.GetNames(levels).Length);

        // NEGATIVE CONTROL -- the pre-card predicate over the same inputs. The assertions above
        // are close to a restatement of three `if`s, so they would pass on a build with the
        // Tutorial arm deleted unless something shows the OLD behaviour differing. This runs it:
        // it must accept Tutorial (i.e. the bug is reproduced) and must agree everywhere else,
        // which also pins that the extraction changed nothing but the one level.
        Func<string, bool> preCard = name =>
            name != "WebcamAliens" && name != "TeamChallenge"
            && name != "Demo1" && name != "Demo2" && name != "Demo3";
        Check("pre-card predicate DID list the Tutorial", preCard("Tutorial"),
            "the control must reproduce the bug or the check above proves nothing");
        int diffs = 0;
        foreach (string name in Enum.GetNames(levels))
        {
            if (listable(name) != preCard(name))
            {
                diffs++;
            }
        }
        Check("exactly ONE level changed verdict", diffs == 1, "changed=" + diffs);

        return 0;
    }

    // Card d937c721 -- ?seed=<n>, the flag that makes the gameplay RNG reproducible.
    //
    // WHY HERE AND NOT IN A RIG. The claim is "two runs of the same boot draw the same numbers",
    // which is a property of a SEQUENCE, not of a picture: an eahl A/B can only show that two
    // frames happen to match, and the card's own measurements are the reason that is not
    // evidence (?level=Level3&wallsonly matched on 5 of 6 unseeded runs). Here the real
    // RandomHelper is driven directly, so the sequence is the observable.
    //
    // Every leg draws through the REAL DebugFlags.Parse, and each pair of legs is built so the
    // implementation cannot be its own expectation:
    //   * reproducibility is asserted against a SECOND run, never against a captured constant;
    //   * a different seed must DIVERGE, or a Reseed that ignored its argument would pass;
    //   * a Parse with no ?seed= must leave the stream CONTINUING mid-sequence -- compared
    //     against an unbroken draw of the same length, which is what makes it insensitive to
    //     whatever seed a previous leg left in force (statics persist across Parse calls in one
    //     process exactly as a repeated flag does in one query);
    //   * the rejection legs assert the message AND that the stream was untouched, because
    //     "reported" and "ignored" are separate promises and a run can keep one while breaking
    //     the other.
    // Card 37f3a663 -- "should a dying ship raise a respawn summon at all?".
    //
    // The bug it defends against is invisible and one frame long: before this card every death
    // raised one, and in single-player (or when the last two co-op ships die in the same tick)
    // GameScene.LoseLife purged it again on the NEXT tick -- so the clock flashed for exactly one
    // Draw. Nothing throws, no counter moves, and no screenshot can be timed to it; the only
    // thing that can be asserted is the DECISION, which is why it lives here rather than in a
    // frame rig.
    //
    // The pre-card rule ("always summon") is run beside it as the negative control -- without it
    // a predicate hard-wired to true passes the positive leg perfectly.
    private static int ProbeRespawnSummon(Assembly asm)
    {
        Type summon = asm.GetType("EvilAliens.PlayerShipSummon", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo should = summon.GetMethod("ShouldSummon", anyStatic);
        if (should == null)
        {
            Console.WriteLine("FAIL: could not reflect PlayerShipSummon.ShouldSummon -- renamed or moved?");
            return 2;
        }

        Func<int, bool> rule = n => (bool)should.Invoke(null, new object[] { n });
        // The pre-card behaviour, verbatim: PlayerShip_OnDeath spawned one unconditionally.
        Func<int, bool> preCard = n => true;

        Console.WriteLine("[logic_probe] respawn summon suppression (card 37f3a663)");

        // 1. THE FIX. Single player is `others == 0` -- one ship in the world, and it just died.
        Check("single player (others=0): NO summon -- the death is a wipe",
            !rule(0), "the 1-frame flash the card reports");
        // A same-tick double death in co-op reaches this with others=0 too: Die() only QUEUES the
        // removal, so the second ship is still in the oracle's list, but IsDead is already true
        // on it -- which is why PlayerShip.CountOtherLiveShips counts IsDead rather than
        // membership, and why this case is not a separate arm here.
        Check("co-op, both ships down in the same tick (others=0): NO summon", !rule(0), null);

        // 2. THE CASE THE INDICATOR EXISTS FOR. One player dies, the other flies on.
        Check("co-op, a partner still flying (others=1): summon", rule(1), null);
        Check("four seats, three still flying (others=3): summon", rule(3), null);

        // 3. THE NEGATIVE CONTROL. Every leg above passes on a predicate stuck at true, so the
        // discriminating statement is that the two rules DISAGREE exactly where the card says.
        bool disagreesAtWipe = preCard(0) != rule(0);
        bool agreesWithPartner = preCard(1) == rule(1);
        Check("the pre-card rule disagrees at others=0 (that IS the bug)", disagreesAtWipe, null);
        Check("...and agrees everywhere else, so nothing else changed behaviour",
            agreesWithPartner && preCard(2) == rule(2) && preCard(3) == rule(3), null);

        // 4. A negative count cannot arise from CountOtherLiveShips, but a predicate that read
        // `!= 0` rather than `> 0` would summon on one -- and that is the shape a later refactor
        // would plausibly introduce.
        Check("a negative count does not summon", !rule(-1), "'!= 0' rather than '> 0'");

        return 0;
    }

    private static int ProbeSeedFlag(Assembly asm)
    {
        Type flags = asm.GetType("EvilAliensWeb.Compat.DebugFlags", true);
        Type helper = asm.GetType("EvilAliens.RandomHelper", true);
        const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo parse = flags.GetMethod("Parse", anyStatic);
        PropertyInfo seedProp = flags.GetProperty("Seed", anyStatic);
        PropertyInfo activeProp = flags.GetProperty("Active", anyStatic);
        PropertyInfo randProp = helper.GetProperty("Random", anyStatic);
        PropertyInfo seededWithProp = helper.GetProperty("SeededWith", anyStatic);
        if (parse == null || seedProp == null || activeProp == null || randProp == null || seededWithProp == null)
        {
            Console.WriteLine("FAIL: could not reflect the targets (Parse=" + (parse != null)
                + " Seed=" + (seedProp != null) + " Active=" + (activeProp != null)
                + " RandomHelper.Random=" + (randProp != null)
                + " RandomHelper.SeededWith=" + (seededWithProp != null) + ") -- renamed or moved?");
            return 2;
        }

        // Re-fetch the property every time: Reseed REPLACES the instance, so a cached one would
        // keep drawing from the pre-seed stream and every reproducibility leg would fail.
        Func<int, string> draw = n =>
        {
            var vals = new System.Collections.Generic.List<string>(n);
            for (int i = 0; i < n; i++)
            {
                vals.Add(((Random)randProp.GetValue(null)).Next(1000000)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join(",", vals);
        };

        Console.WriteLine("[logic_probe] ?seed= (card d937c721)");

        // 1. PRISTINE STATE, and the "staying on ..." wording that is only reachable from it.
        // Nothing above this set passes ?seed=, so the shipped default is still in force here --
        // and the check says so out loud, so a future probe that seeded first FAILS rather than
        // silently turning this leg into a weaker one.
        Check("pristine: no ?seed= has been parsed yet",
            seedProp.GetValue(null) == null && seededWithProp.GetValue(null) == null,
            "if this fails, an earlier Probe* now seeds -- move this set back to the front");
        // Sampled HERE, in that pristine window, for leg 8 at the bottom -- and the ordering is
        // the whole point. Read after a ?seed= had already been parsed, the comparison is
        // vacuous: `Seed` is a persistent static, so a build with `|| Seed.HasValue` folded into
        // the Active expression would read true on BOTH sides and pass. Measured exactly that.
        bool activeUnseeded = (bool)activeProp.GetValue(null);
        string outVirgin = FirstLine(RunParse(parse, "?seed=abc"));
        Check("a malformed ?seed= is reported", outVirgin.Contains("unknown ?seed="), outVirgin);
        Check("... naming the unseeded default as what stands",
            outVirgin.Contains("an unseeded Random (the shipped default)"), outVirgin);
        Check("... and it did NOT seed anything",
            seedProp.GetValue(null) == null && seededWithProp.GetValue(null) == null, null);

        // 2. THE CLAIM: same seed, same sequence. Asserted between two runs rather than against a
        // baked-in expected list, so it stays true if the BCL's generator ever changes.
        RunParse(parse, "?seed=12345");
        Check("?seed=12345 records the seed",
            (int?)seedProp.GetValue(null) == 12345 && (int?)seededWithProp.GetValue(null) == 12345, null);
        string runA = draw(8);
        RunParse(parse, "?seed=12345");
        string runB = draw(8);
        Check("same seed => same sequence", runA == runB, runA + " vs " + runB);

        // 3. DIVERGENCE CONTROL. Without it, a Reseed ignoring its argument (or seeding a
        // constant) passes leg 2 perfectly.
        RunParse(parse, "?seed=999");
        string runC = draw(8);
        Check("a DIFFERENT seed diverges", runC != runA, runC + " vs " + runA);

        // 4. Negatives are legal seeds -- there is no range predicate here, which is why this
        // flag is absent from ProbeFlagRejectionSweep's table (its shared shape is "a negative is
        // clamped or refused"). int.MinValue is called out because the legacy Math.Abs(seed)
        // implementation threw on it.
        RunParse(parse, "?seed=-7");
        string negA = draw(4);
        RunParse(parse, "?seed=-7");
        // Drawn on its own statement, never inside the `&&`: a short-circuit would skip the four
        // draws and leave the stream where no later leg expects it.
        string negB = draw(4);
        Check("a negative seed is accepted and reproducible",
            (int?)seedProp.GetValue(null) == -7 && negB == negA, negA + " vs " + negB);
        string outMin = RunParse(parse, "?seed=-2147483648");
        Check("int.MinValue is accepted (no Math.Abs overflow)",
            (int?)seedProp.GetValue(null) == int.MinValue && !outMin.Contains("unknown ?seed="), null);

        // 5. A Parse WITHOUT ?seed= must not touch the stream. Compared against an unbroken draw
        // of the same length from the same seed, so it cannot be satisfied by a reseed that
        // happens to restore the same value.
        RunParse(parse, "?seed=4242");
        string unbroken = draw(10);
        RunParse(parse, "?seed=4242");
        string firstHalf = draw(5);
        RunParse(parse, "?noattract");
        string secondHalf = draw(5);
        Check("a Parse with no ?seed= leaves the stream running",
            firstHalf + "," + secondHalf == unbroken, firstHalf + "," + secondHalf + " vs " + unbroken);

        // 6. REJECTION with a seed already in force: reported, naming the seed actually standing
        // (not the typo, not a baked default), and genuinely ignored -- the stream keeps running.
        RunParse(parse, "?seed=4242");
        string half = draw(5);
        string outBad = FirstLine(RunParse(parse, "?seed=42x2"));
        Check("a malformed ?seed= is reported when one is in force",
            outBad.Contains("unknown ?seed="), outBad);
        Check("... naming the seed in force", outBad.Contains("staying on 4242"), outBad);
        Check("... and the stream was untouched", half + "," + draw(5) == unbroken,
            "a typo must not re-seed, nor clear the seed");
        Check("... and Seed still reads the valid value",
            (int?)seedProp.GetValue(null) == 4242, null);
        // CONTROL: a VALID value reports nothing, so a diagnostic that printed unconditionally
        // fails here and only here.
        Check("a valid ?seed= reports no rejection",
            !RunParse(parse, "?seed=4242").Contains("unknown ?seed="), null);

        // 7. The announcement line, which is the ONLY record that a capture came from a pinned
        // world -- ?seed is deliberately out of `Active`, so the flag dump need never print.
        string announce = RunParse(parse, "?seed=31337");
        Check("a seeded boot announces itself", announce.Contains("[debug] ?seed=31337"), FirstLine(announce));

        // 8. OUT OF `Active` -- the ruling that keeps a seeded peer able to pair and to list
        // (NetSession.HandleHello / NetListing.ComputeEligible both refuse on that bit).
        // Compared against `activeUnseeded`, sampled before this set parsed its first ?seed= --
        // see there for why a locally-sampled "before" cannot fail.
        RunParse(parse, "?seed=555");
        Check("?seed= does not set Active", (bool)activeProp.GetValue(null) == activeUnseeded,
            "Active=" + activeProp.GetValue(null) + " unseeded=" + activeUnseeded);

        return 0;
    }
}
