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

        rc = ProbeCollisionBoxLine(asm);
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
        Func<string, string> run = query =>
        {
            System.IO.TextWriter saved = Console.Out;
            System.IO.StringWriter buf = new System.IO.StringWriter();
            Console.SetOut(buf);
            try
            {
                parse.Invoke(null, new object[] { query });
            }
            finally
            {
                Console.SetOut(saved);
            }
            return buf.ToString();
        };
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

        Action<string> run = query =>
        {
            System.IO.TextWriter saved = Console.Out;
            Console.SetOut(System.IO.TextWriter.Null);
            try { parse.Invoke(null, new object[] { query }); }
            finally { Console.SetOut(saved); }
        };
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

        // NOTE, deliberately not asserted as a diagnostic: unlike ?flyspider*, the ?ai* family
        // rejects a bad value SILENTLY -- ?aireact, ?aigapmargin and the other ten all do. These
        // two follow their family rather than splitting it. The guarded path is the one that
        // produces published numbers: tools/sim/aiwallnav's --scanrows= exits 2 on a non-integer
        // instead of benching the default. Making the whole ?ai* family report is its own card.

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
    // the line's Length" -- so the set is built as PAIRS that isolate one term each, and the
    // sensitivity section below asserts each pair actually splits. A set that could not fail
    // would be worth nothing here, and with no old behaviour to contrast that is the only
    // sensitivity evidence available inside the probe.
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

        // 3. SENSITIVITY. Each pair below differs in exactly ONE input, so a live term must make
        // the two answers differ. Without this the section above could be satisfied by a predicate
        // that had lost a term (or by one that always answered `false`), and a green run would be
        // worth nothing -- the eaNetScore.test() rule, in the only form available for a change
        // with no behaviour delta to contrast.
        Check("CONTROL: only Length differs => the length term is live",
            hits(0f, 150f, 1000f, right) != hits(0f, 150f, 50f, right),
            "same origin and heading, 1000 vs 50");
        Check("CONTROL: only direction differs => the intersection term is live",
            hits(0f, 150f, 1000f, right) != hits(0f, 150f, 1000f, left),
            "same origin and length, toward vs away");
        Check("CONTROL: only origin Y differs => the box bounds are live",
            hits(0f, 150f, 1000f, right) != hits(0f, 50f, 1000f, right),
            "same heading and length, y=150 vs y=50");
        return 0;
    }
}
