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
| into a challenge | `Press down` ×3 → `Press enter` → challenge carousel (opens on **Space Dodge**) → `Press right` per entry → `Press enter` → difficulty → `Press enter` |

Challenge carousel order — the index is how many rights you need, and the level name is often
not the menu label. **`MenuScene.cs`'s run of `challengeSelector.AddEntry` calls is the
authority; this is a copy and only the two bold entries are defended by a probe**, so re-read it
rather than trusting this table if a right-count lands you somewhere unexpected:

| index | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| entry | Space Dodge! | Braineroids | Evil Aliens Classic | **Paratrooper** | Base Pressure (`OwnLevel`) | Crazy Game | **Boss Train** (`InsaneBossI`) | Team Challenge | I Made This! (`WebcamAliens`) |

**`Press <key> <n>` is an n-frame HOLD, not n taps.** `eval Press down 3` moves the menu ONCE.
Every tap is its own `eval Press` with a `step` between — this cost a rehearsal run that never
left the main menu, and would have been an invisible vacuous pass on a probe with no `expect`.

`?unlockall` so a wiped save can still select the mission; `?invuln` + `?aiplayer` so the level
is actually played rather than sitting on a dead ship.

`info`'s `scene=menu` does **not** mean the run failed to reach the level — the menu component
persists. Read `[loadprofile] <Level> preload:` and `eval ScoreDump` for that instead.

## The probes

| file | pins |
|---|---|
| `silence.txt` | a default `eahl` run is silent, confirmed at OpenAL's listener gain — not merely requested, and that the OpenAL binary resolved at all (`lib=`). A box with genuinely no audio device FAILS it, deliberately: the suite's contract is *this box can confirm mixer silence*, and one that cannot should be loud rather than green — `no_audio_device.txt` is what covers such a box |
| `no_audio_device.txt` | a box with **no audio device** still runs the game and REPORTS that audio is dead (card 72297923). Uses `--fake-no-audio-device`, which makes `alcOpenDevice` genuinely fail rather than mocking anything |
| `preload_level2.txt` | Level 2's `Content/preload/manifest.txt` section: no texture decodes during gameplay |
| `preload_paratrooper.txt` | the same, for the Paratrooper challenge (49 manifest entries) |
| `preload_insanebossi.txt` | the same, for the Boss Train challenge (`InsaneBossI`, 82 entries — the largest section); soaks the level OUT (720 s) because the bosses arrive in sequence — see its header for the two assets a shorter window provably missed |
| `preload_demo{1,2,3}.txt` | the same, for the three attract demos (`?demo=<n>` pins which one the idle menu drops into). Each also asserts the `(boot)` sentinel stays clean -- see the block below for why that third line is the one that matters |
| `boot_cold.txt` | card 57555583's two lazy boot decodes (splash flip variants, `AwardmentBlade`) stay lazy |
| `stockshots_warm.txt` | `ScreenshotSaver.StockShots` covers every carousel entry (cards 8d6883f3, 0d166364): no level-select art decodes when either carousel is opened, and no entry falls back to the default art |
| `stockshots_pump.txt` | the OTHER half of card 4d47c5ba: on a boot that lets the warm pump run (a real player's), the Press-Start -> menu handoff decodes nothing. Card cccd763a -- it is the only probe that can see that half, see the block below |
| `gamebrowser_fallback.txt` | the online game browser draws the default shot for a level it has no bundled art for (card 0d166364) — the unmapped and out-of-enum levels that arrive off the wire from a stranger's build. Note the flag is `?gamebrowser=fallback`; the bare flag is the appearance rig and lists no unmapped entries |

All are mutation-tested. Each `preload_*` goes red, naming the first missing asset, when that
level's manifest lines are deleted — `Level2|gfx/marsbg` → 17 lines from
`gfx/marsbg/clouds-background`; `Paratrooper|gfx/marsbg` → 17 from the same;
`InsaneBossI|gfx/sprites` → 10 from `gfx/sprites/playersheet` (only 10 of the 53 deleted
entries decode cold — the rest are already in the shared content cache from the boot warm,
which is why a mutation test asserts "red, naming an asset", not a count). `boot_cold` goes red
on either half it defends — restoring `AwardmentBlade`'s eager load in `LoadContent` trips its
`awardmentblade` `expect-not`, and re-adding a `flipPureName`/`flipGlassesName` load to
`SplashScene.LoadContent` trips the `-revenged-pure` one.
`silence` goes red under `--audio` (`masterVolume=1 alGain=1`), which is also its standing
negative control. Its `lib=<unresolved>` line (added by card 72297923, which made the readback
resolve OpenAL by candidate list) goes red when that list is replaced with names that do not
exist. That line discriminates no mutant the `alGain=0` line below it would miss — a failed
resolve makes the gain unreadable too — so it is ordered FIRST purely to make the failure name
the cause; it earns its place as diagnosis, not as coverage. `no_audio_device` goes red both ways that matter:
drop `BringUp`'s catch and the run dies with `err NoAudioHardwareException`, and make
`--fake-no-audio-device` a no-op and it fails on its `NO AUDIO DEVICE` expect rather than
passing on a run that had a device all along. `stockshots_warm` goes red when a level is deleted from `LevelArt.ScreenshotPath` — tested on
BOTH carousels (`Level1`, `WebcamAliens`), because an earlier revision that opened only the
challenge carousel passed the `Level1` mutation. **Since card 0d166364 its `expect-not COLD`
line can no longer catch that on its own**: collapsing `HasCarouselEntry` into a nullable
`ScreenshotPath` means a dropped level now resolves to `null` in the carousel too and falls back
to the already-warm `level1empty`, so *nothing decodes cold* — with only that line, the
`WebcamAliens` mutation passes green (measured). Its three assertions now catch three different
things and none is redundant: `stockshots warm: 12 textures` counts the derived set and fires
first on a deleted level (reads 11); `[levelart] carousel entry` is the only one that catches
the drift the card is about — a `MenuScene` carousel entry authored for a level with no
`ScreenshotPath` row, where `StockShots` is still twelve and the count passes happily
(measured with `Levels.Tutorial`: boot count and COLD both green, that line alone red); and
`COLD` still covers the `ScreenshotSaver`/`QueueMenuWarm` side neither of the others sees.
`gamebrowser_fallback` goes red when `EnsureArt`'s null guard is reverted — the
`[gamebrowser] rebuilt` line stops printing entirely, since it is reported only when something
resolved to no bundled art. Note what that mutation does NOT produce: an exception. `EnsureArt` wraps its
`Content.Load` in `catch (Exception)`, so a broken fallback throws, gets absorbed, and draws the
identical picture — which is why the probe reads a line reported from what `EnsureArt` recorded
while resolving, and why that line must never be re-derived from `LevelArt` at the report site.

**`stockshots_pump` exists because its two neighbours provably CANNOT catch the regression it
pins, and that is worth stating rather than rediscovering.** Delete the
`foreach (StockShots) EnqueueWarm` tail from `Game1.QueueMenuWarm` and every player boot decodes
all twelve level-select thumbnails synchronously on the last beat before the menu -- the
~350-470ms Chrome stall card 4d47c5ba removed. Measured on that exact mutation:
`stockshots_warm.txt` **PASS**, `boot_cold.txt` **PASS**, `stockshots_pump.txt` **FAIL**, quoting
the restored `stockshots warm: 12 textures` line. The two neighbours are blind for different
reasons -- `stockshots_warm` boots `?menu`, which auto-presses Start on frame 1, so the twelve
decode at `ScreenshotSaver.Init` whether or not they were ever queued; `boot_cold` runs a full
splash but never presses Start, so `Init` does not run there at all. Only a boot that lets the
pump run AND then presses Start separates them.
Its second mutation is the rule-1 one: drop the two Enter taps and the run never leaves the
splash, at which point `expect-not stockshots warm:` passes on **0 matches** while the
`[loadprofile] Level1 preload:` control goes red. That control is the whole reason the probe
navigates into a level at all.

**`preload_insanebossi` additionally has a two-line mutation, and it is the sharp one:** deleting
just `InsaneBossI|gfx/base/756` and `|gfx/base/2331-v5` goes red only because the soak runs the
level out. It PASSED at the 180 s length the probe was first written with — those two assets are
reached late — which is how the soak length was chosen. Re-run that mutation, not the broad one,
if you ever shorten the window. `silence` goes red
under `--audio` (`masterVolume=1 alGain=1`), which is also its standing negative control.

**The three attract demos each have a probe now (card e63601a4), and the two blockers that
used to make them impossible are both gone.** This block used to say `Demo2` could not be
probed; what follows is what changed, because both halves generalise.

1. **Reaching one deterministically.** `MenuScene.mainMenu_DemoSelected` picks Demo1/2/3 with
   `RandomHelper.Random.Next(3)` off an unseeded `Random`, and a `--script` file cannot branch
   or retry -- so any demo probe was a `(2/3)^attempts` coin flip. **`?demo=<1|2|3>` pins the
   roll.** It is NOT the off-switch of `?nodemo`/`?noattract` (those unwire the idle timeout so
   no demo launches at all). `?level=Demo2` is still the wrong route -- it walks into the
   `QueueIdleWarm` trap above, which is why the attract path is the only usable one.
2. **They were not cold-free.** Demo1 logged 12 COLD decodes, Demo2 10, Demo3 12; the manifest
   sections were short. Fixed by the same card.

**Demo3 is why every one of these probes asserts on `(boot)` as well as on its own level.**
Demo3 had NO manifest section, and `WarmThenLaunch` returns EARLY on an empty one -- so no
preload bracket opened and its 12 decodes were logged against the `(boot)` sentinel. It
therefore READ as the clean demo. `expect-not COLD decode in Demo3` passes vacuously in that
state, and the `expect \[loadprofile\] Demo3 preload:` above it still matches, because that
summary line comes from `GameScene.LoadContent`'s `BeginPreload`/`EndPreload` bracket, which
runs whatever the manifest section holds (`PreloadGraphicalContent` is called inside it, not
the opener).
Only `expect-not COLD decode in \(boot\): gfx/sprites/playersheet` catches it -- verified by
deleting the whole section, which goes red on exactly that line while the other two pass.

Each `preload_demo*` is therefore mutation-tested TWO ways. A PARTIAL delete (one line) goes red
on the level's own `expect-not`, naming the asset -- `Demo1|gfx/sprites/playersheet`,
`Demo2|gfx/sprites/explosion`, `Demo3|gfx/base/756`. A WHOLE-section delete goes red on the
`(boot)` guard instead, with the level's own `expect-not` passing on 0 matches. **Re-run the
whole-section one if you ever touch these** -- it is the case the guard exists for, and it is the
one a partial delete cannot reach.
