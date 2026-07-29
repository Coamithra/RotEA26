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
| `silence.txt` | a default `eahl` run is silent, confirmed at OpenAL's listener gain — not merely requested. **Windows-only**: the readback P/Invokes `soft_oal.dll`, so elsewhere it reports `alGain=<unreadable>` and fails. Add the platform library names to `HeadlessAudio` rather than relaxing the assertion |
| `preload_level2.txt` | Level 2's `Content/preload/manifest.txt` section: no texture decodes during gameplay |
| `preload_paratrooper.txt` | the same, for the Paratrooper challenge (49 manifest entries) |
| `preload_insanebossi.txt` | the same, for the Boss Train challenge (`InsaneBossI`, 82 entries — the largest section); soaks the level OUT (720 s) because the bosses arrive in sequence — see its header for the two assets a shorter window provably missed |
| `boot_cold.txt` | card 57555583's two lazy boot decodes (splash flip variants, `AwardmentBlade`) stay lazy |
| `stockshots_warm.txt` | `ScreenshotSaver.StockShots` covers every carousel entry (card 8d6883f3): no level-select art decodes when either carousel is opened |

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
negative control. `stockshots_warm` goes red naming the dropped asset when a level is deleted
from `LevelArt.HasCarouselEntry` — tested on BOTH carousels (`Level1` →
`gfx/screenshots/level1empty`, `WebcamAliens` → `gfx/screenshots/webcamss`), because an
earlier revision that opened only the challenge carousel passed the `Level1` mutation.

**`preload_insanebossi` additionally has a two-line mutation, and it is the sharp one:** deleting
just `InsaneBossI|gfx/base/756` and `|gfx/base/2331-v5` goes red only because the soak runs the
level out. It PASSED at the 180 s length the probe was first written with — those two assets are
reached late — which is how the soak length was chosen. Re-run that mutation, not the broad one,
if you ever shorten the window. `silence` goes red
under `--audio` (`masterVolume=1 alGain=1`), which is also its standing negative control.

**`Demo2` has no probe, deliberately** (card 454cbeae, measured — do not "finish the set"
without re-measuring). Two independent blockers:

1. **It cannot be reached deterministically by the attract path a valid preload probe
   requires.** (`?level=Demo2` boots it fine — and walks straight into the `QueueIdleWarm` trap
   above, which is the whole reason the attract path is the only usable route.)
   `MenuScene.mainMenu_DemoSelected` picks
   `RandomHelper.Random.Next(3)` → Demo1/2/3 uniformly on every attract launch, off an
   unseeded `new Random()`. There is no "demo 1 first" ordering and no debug seam to force one,
   and a `--script` file cannot branch or retry, so any Demo2 probe is a `(2/3)^attempts` coin
   flip.
2. **It is not cold-free on `main` anyway.** A run that did land on Demo2 logged **10** COLD
   decodes (`gfx/sprites/playersheet` 1260x680, `explosion`, `smoke`, `photocamera`,
   `bombicon`, `gfx/hud/barlit`/`barunlit2`/`barlitedge`, `gfx/menu/powerbar`,
   `gfx/game/blank`); Demo1 logs those 10 plus `ufometpootjes` and `smallship_landed`. The
   `Demo*` manifest sections have a real gap — a probe would be red on a clean tree. That gap
   is its own card.
