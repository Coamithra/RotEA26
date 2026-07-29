# Committed regression probes

```sh
dotnet build tools/headless -c Debug
python tools/headless/probes/run_probes.py          # 0 = all passed, 1 = a probe failed
python tools/headless/probes/run_probes.py --list
python tools/headless/probes/run_probes.py --only preload_*
```

## What a probe is for

Most changes in this repo are verified once, by a screenshot or a number, and the proof
evaporates with the session. That is fine when the code would break loudly. It is **not**
fine for a change whose failure mode is silent — a data file, a manifest, a host default.
Delete the `gfx/marsbg` lines from `Content/preload/manifest.txt` and there is no build
error, no failing test, and nothing to notice until someone plays Level 2 and feels a
~1.2 s hitch. Let `eahl` play audio and nobody notices either, because an agent cannot hear it.

A probe is that check, committed, so the next change to the same data fails loudly.

**A probe is not a unit test and this is not a test suite.** It boots the real game and
asserts on what the real game printed. Add one when a check is worth *re-running later*;
a one-off "does this frame look right" stays a screenshot.

## Writing one

A probe is a plain `eahl --script` file. The runner adds nothing to the language — it only
supplies per-probe boot flags and runs the set — so any probe can be run by hand:

```sh
tools/headless/bin/Debug/net8.0/eahl.exe --script tools/headless/probes/silence.txt \
    --flags "?level=Level1&invuln"
```

Layout:

```
# PROBE: <one line, shown by --list>
#
# PINS:   what this defends and which card it came from
# FAILURE MEANS: how to read the quoted line and what to do about it
#
# eahl: --flags "?menu&noattract&loadlog"     <- the runner reads this; eahl sees a comment

step 150 nodraw
mark
...
expect      <regex>
expect-not  <regex>
quit
```

The assertion commands (`mark` / `expect` / `expect-not`, plus `audio`) are documented in
`../README.md`. `expect*` match **per line** against everything the run has printed since the
last `mark` — the game's own `Console.WriteLine` diagnostics (`[loadprofile]`, `[hitch]`,
`[net]`, …) as well as command replies. A failure quotes the offending line, so a red probe
tells you what broke rather than just that something did.

Four rules, each of which has already cost something:

1. **Assert the positive too.** `expect-not "COLD decode in Level2"` passes beautifully on a
   run that never reached Level 2. Pair it with an `expect` that proves the run got where it
   claims (`expect \[loadprofile\] Level2 preload:`). A probe that cannot fail is worse than
   no probe, because it reads as coverage.
2. **`mark` away the boot noise.** Boot decodes ~20 assets and logs them under `(boot)`; other
   cards own that population. Scope the window to your subject.
3. **Never assert absence over a truncated window.** You do not have to police this — the
   capture buffer is capped and an overflow fails `expect-not` (and a fruitless `expect`)
   rather than passing on evidence it threw away — but if you see that error, add a `mark`
   closer to the assertion.
4. **Mutation-test it before committing.** Break the thing it defends and watch it go red; the
   probe's header should say what you broke. Same standard `tools/sim/logic_probe` and the
   texture canary are held to.

## The trap that will otherwise cost you an afternoon

**A preload/`COLD` probe must drive the MENU, never `?level=<Name>`.** A `?level=` boot has no
splash, so `Game1`'s `QueueIdleWarm` assets drain into gameplay and are recorded as *that
level's* cold decodes: exactly **20** spurious `gfx/game/space/*` lines, measured identically on
`Level2`, `Paratrooper` and `InsaneBossI`. A probe booted that way either fails always or needs
a whitelist covering the very assets a real regression would show up in.

The menu path, rehearsed headlessly (see `preload_level2.txt`):

| | |
|---|---|
| main menu order | Start · Options · Tutorial · Challenges · Online Co-op · Awardments · Cheats · Exit |
| into a mission | `Press enter` → mission carousel (opens on Mission 1) → `Press right` per mission → `Press enter` → difficulty → `Press enter` |
| into a challenge | `Press down` ×3 → `Press enter` → challenge carousel (opens on **Space Dodge**) |

`?unlockall` so a wiped save can still select the mission; `?invuln` + `?aiplayer` so the level
is actually played rather than sitting on a dead ship.

## The probes

| file | pins |
|---|---|
| `silence.txt` | a default `eahl` run is silent, confirmed at OpenAL's listener gain — not merely requested. **Windows-only**: the readback P/Invokes `soft_oal.dll`, so elsewhere it reports `alGain=<unreadable>` and fails. Add the platform library names to `HeadlessAudio` rather than relaxing the assertion |
| `preload_level2.txt` | Level 2's `Content/preload/manifest.txt` section: no texture decodes during gameplay |
| `boot_cold.txt` | card 57555583's two lazy boot decodes (splash flip variants, `AwardmentBlade`) stay lazy |

Both are mutation-tested. `preload_level2` goes red (17 lines, `gfx/marsbg/clouds-background`
first) when the `Level2|gfx/marsbg` manifest lines are deleted; `silence` goes red under
`--audio` (`masterVolume=1 alGain=1`), which is also its standing negative control.

`Paratrooper`, `InsaneBossI` and `Demo2` are the other three levels with substantial manifest
sections and deserve the same probe — they need challenge-carousel / attract-rotation navigation
and their own measured baselines, so they are a follow-up card rather than an untested guess here.
