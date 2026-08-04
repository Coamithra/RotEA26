# Committed regression probes

```sh
dotnet build tools/headless -c Debug
python tools/headless/probes/run_probes.py          # 0 = all passed, 1 = a probe failed
python tools/headless/probes/run_probes.py --list
python tools/headless/probes/run_probes.py --only preload_*
python tools/headless/probes/run_probes.py --build  # build eahl first, then run
```

**The runner REFUSES to run against a stale `eahl` (exit 2), and that is the point** (card
74998f22). These probes exercise the game through `eahl.exe`, which source-links `Game/**` +
`Compat/**` — so after a source edit, or after a `dotnet build` that FAILED, a probe run tests the
PREVIOUS binary and prints a green suite for code that does not compile. That happened (card
4a3b22b7) and was caught only by separately grepping the build output. Before the first probe the
runner compares eahl's build time against the newest file it is built from and, if the sources win,
prints both timestamps and stops:

```
STALE BINARY -- refusing to run the probes.
  eahl built     2026-08-02 20:52:05   tools/headless/bin/Debug/net8.0/eahl.exe
  newest source  2026-08-02 20:59:51   web/EvilAliensWeb/Game/EvilAliens/OwnLevel.cs
```

`--build` cures it; `--allow-stale` prints the same block as a WARNING and runs anyway. **Exit 2 is
the runner refusing, exit 1 is a probe failing — keep them apart when scripting.** The rule itself
is tested by `run_probes.py --selftest` (no dotnet, no eahl, no probes), which is worth re-running
if you touch the scan: it covers the bin/obj skip, the equal-mtime boundary and the dll-vs-exe
pair, each of which has a mutation listed in its docstring. Note the check dates only what is
COMPILED IN — `wwwroot/Content` and the probe files themselves are read live off disk, so
regenerating an asset never reads as stale.

**A failing probe's FULL output is kept — read the log, not just the tail** (card de82597f). The
runner prints a 12-line window around the `err` and writes everything the run produced to
`tools/headless/probes/_failures/<probe>-<timestamp>.log` (gitignored, one file per failure, the
command line at the top), naming the path under the tail. That exists because the summary throws
away exactly what a rare flake needs: this card was filed with no evidence beyond "`[netmotion] 32
passed, 0 failed` never printed", because the run that did not print it was gone the moment the
runner moved on. `--verbose` is not a substitute — it streams all 50 probes, which is unreadable
during a soak and keeps nothing afterwards. **`--keep-output` logs passing runs too**, which is
what you want when soaking for a flake you have not caught yet.

`_failures/` is never pruned automatically. Delete it when you are done soaking.

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

Five rules, each of which has already cost something:

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
5. **Make the PRECONDITION deterministic, and assert it separately** (card af4c3694). A probe
   that needs the world in some state before it acts — a live ship, a boss on screen, a level
   past its intro — must not merely wait long enough and hope. `RandomHelper` is unseeded, so
   "long enough" is a probability: `death_fade.txt` parked an un-invulned ship on a level with a
   live enemy spawner and flaked in **3 of 30** runs when the ship happened to die first, always
   with a downstream-looking message (`asploded 0 local ships`). Pin the state (`?invuln` there),
   then `expect` it BEFORE the step that depends on it, so a run that lost it fails as a
   precondition rather than as the defect the probe is about.
   **Pick an observable that cannot silently drop your subject**: `eval Census` prints only the
   fourteen most populous component types, so `PlayerShip=1` vanishes on a busy scene and reads
   as "no ship". `eval OracleRoster`'s `aliveSlots=` is the ship-liveness readout (a bracketed slot list:
   `aliveSlots=[0]` is slot 0 flying, `aliveSlots=[]` is a shipless world).

## The physical mouse is OFF, and that is why menu navigation is repeatable

**`eahl` does not read your desktop mouse, and you must not put it back.** KNI's SDL2 backend
answers `Mouse.GetState()` from `SDL_GetGlobalMouseState` — the desktop pointer **and** the
desktop button mask, focus-independent, minus the hidden window's origin. So before card
83054936 every headless run sampled whatever the human's hand happened to be doing, and it broke
probes two ways:

* **Position** — `MenuSub1.HandleMouse` hover-selects whatever entry the cursor moves over *and*
  returns `true`, which makes `HandleInput` return early and **swallow that tick's keypress**. A
  `Press down` / `Press enter` walk therefore landed somewhere else: `menu_backtip.txt` failed
  **15 of 20** runs, on a leg that varied between runs, and once launched the Tutorial from a
  single `down` off Start.
* **Buttons** — a physically-held left button keeps `pressedAndIdle[Mouse1]` true, so a scripted
  rising edge is eaten and a scripted `Hold("Mouse1", down: false)` release is a no-op. That is
  what made `net_single_tap.txt`'s "two taps inside one cadence period" leg read as one continuous
  hold.

`HeadlessHost.Boot` now sets `DebugInput.SuppressPhysicalMouse`: the position parks off the design
surface at `-1000,-1000` and the buttons read released. **Scripted input is untouched** —
`eval MouseAt` still wins on position, `eval Press` / `eval Hold` still supply the buttons — so a
probe that drives the mouse works exactly as before. Every run announces it
(`[eahl] input    physical mouse suppressed`), and `eval MouseState` reads it back as
`[mousestate] physical=<suppressed|live> override=<x,y|none> pos=<x,y>`.

Three consequences worth knowing:

1. **`eval MouseAt <corner>` in an existing probe is now belt-and-braces, not the fix.**
   `net_menumode_reset.txt`'s "not optional" note predates this and is left as written; do not
   copy the pattern into new probes as if it were required.
2. **A `?level=` probe's mouse-aim is now deterministic** — it points at one fixed off-screen
   place instead of at wherever you left your pointer.
3. **`--real-mouse` restores the old behaviour**, and its real job is to be the mutation for
   `menu_backtip.txt`'s `[mousestate]` assertion — the only thing in the suite that looks at the
   guard at all. It pins both of that line's fields (`physical=` and `pos=`) and goes red
   deterministically, which no amount of re-running a flake can.

**This does NOT make eahl deterministic**, and the bar was never that (card d937c721): the
gameplay RNG is unseeded without `?seed=`, and the boot `Tick`'s catch-up step count still varies
with machine load. It removes one *external* input, which is the class a probe can never defend
against on its own.

## The trap that will otherwise cost you an afternoon

**A preload/`COLD` probe must drive the MENU, never `?level=<Name>`.** A `?level=` boot has no
splash, so `Game1`'s `QueueIdleWarm` assets drain into gameplay and are recorded as *that
level's* cold decodes: exactly **20** spurious `gfx/game/space/*` lines, measured identically on
`Level2`, `Paratrooper` and `InsaneBossI`. A probe booted that way either fails always or needs
a whitelist covering the very assets a real regression would show up in.

The menu path, rehearsed headlessly (see `preload_level2.txt`):

| | |
|---|---|
| main menu order (**`?unlockall` only** — see below) | Start · Options · Tutorial · Challenges · Online Co-op · Awardments · Cheats · Exit |
| into a mission | `Press enter` → mission carousel (opens on Mission 1) → `Press right` per mission → `Press enter` → difficulty → `Press enter` |
| into a challenge | `Press down` ×3 → `Press enter` → challenge carousel (opens on **Space Dodge**) → `Press right` per entry → `Press enter` → difficulty → `Press enter` |

Challenge carousel order — the index is how many rights you need, and the level name is often
not the menu label. **`MenuScene.cs`'s run of `challengeSelector.AddEntry` calls is the
authority; this is a copy and only the two bold entries are defended by a probe**, so re-read it
rather than trusting this table if a right-count lands you somewhere unexpected:

| index | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| entry | Space Dodge! | Braineroids | Evil Aliens Classic | **Paratrooper** | Base Pressure (`OwnLevel`) | Crazy Game | **Boss Train** (`InsaneBossI`) | Team Challenge | I Made This! (`WebcamAliens`) |

**THE MAIN MENU HIDES LOCKED ENTRIES, so that row is the `?unlockall` order and a down-count from
it is WRONG on any other boot.** `eahl`'s saves are wiped per run (card 36db5d75), so a bare
`?menu&noattract` boot draws **five** rows — Start · Options · Tutorial · Online Co-op · Exit —
and `down` x4 lands on **Exit**, not Online Co-op (measured, screenshot-confirmed). Challenges,
Awardments and Cheats are `Unlockables`-gated and simply are not drawn. Either pass `?unlockall`
so the table holds, or screenshot the menu and count what is actually on it.

**The MOUSE is drivable too, and needs BOTH halves.** `eval Press Mouse1 1` supplies only the
button; the cursor comes from the real mouse, which under `eahl` is wherever SDL happens to report,
so a click alone lands on nothing. `eval MouseAt <x> <y>` parks it in **design space** (800x600) --
the same coordinates the menus record their hit boxes in, so a box read off a `[backtip]` line can
be clicked directly. Then `eval MouseClear` (which reports what it released). The idiom:

```
eval MouseAt 93 552
step 2
eval Press Mouse1 1
step 120
```

Worked example, including a negative control and what to assert on (`eaMenuCensus`, since a menu
you return to prints no new `[backtip]` line): `menu_backtip.txt` section 2.

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
| `netbg_catchup.txt` | the join-in-progress scenery catch-up replays every leg, the mid-level whole-scene swap included (cards 45a4e48d, ca4fd94f): a peer joining mid-level must end up looking at the host's scenery, which nothing else can detect — it fails silently and only a second player ever sees it. Drives `eaNetBgTest`'s one-tab round trip over `?netscript`; its third assertion is an anti-vacuity guard on the wipe, and the header says why it takes an `expect-not` |
| `stockshots_pump.txt` | the OTHER half of card 4d47c5ba: on a boot that lets the warm pump run (a real player's), the Press-Start -> menu handoff decodes nothing. Card cccd763a -- it is the only probe that can see that half, see the block below |
| `net_reset_spawn.txt` | card 74403f83's two ship-puppet spawn sites, END TO END (card 25ad0659 step 1b): an `EvReset` purging from inside the rx drain must not let `NetSession.SpawnPuppet` / `SpawnFriend` adopt a ship the `ComponentBin` diverted. The fix was previously proven only at the primitive (`eaBinTest` scenario 5's bare `TryAdd` pair) — reaching the real call sites needs a live session with a host-granted peer slot and buffered ship samples. **The only DESTRUCTIVE probe here**: the suite pairs a real session onto the live level and leaves the scene in its reset branch (it restores the roster and asserts it did) |
| `gamebrowser_fallback.txt` | the online game browser draws the default shot for a level it has no bundled art for (card 0d166364) — the unmapped and out-of-enum levels that arrive off the wire from a stranger's build. Also the out-of-range DIFFICULTY on the same row (card 88f87ba2): the boundary refuses it (`unknownDifficulty=7`) and the row is still listed. Note the flag is `?gamebrowser=fallback`; the bare flag is the appearance rig and lists no unmapped entries |
| `flyspider_bench.txt` | the `?flyspidercount=` bench spawns its full population with a PINNED pose (card 1cd47879). Every flatten measurement in web CLAUDE.md is a diff between two bench captures, so a spider that started rolling its flap phase again would change no behaviour and no other output while quietly making every future capture pair incomparable. `pose=` is read back off the spiders, not restated from the predicate; the count is asserted beside it because `pose=pinned` is vacuously true of an empty bench |
| `death_fade.txt` | the death cross-fade actually dissolves (card b7e9b106): `SealAlpha` clamps its white pixel to `LogicalBounds()`, so the snapshot's alpha is sealed over the WHOLE target rather than the top-left ~100x75 px a stretched 112x112 `--padtest` canvas covered. Reads the `[xfade] seal ... src=` line, derived from the rect handed to `Draw`. The failure is silent and every intermediate is correct — timer, blend state and tint alpha all fine, only the composite wrong — and no screenshot A/B can assert it (`RandomHelper` is unseeded, so the run-to-run noise floor exceeds the effect). Reads the `[xfade] seal ... src=` line, which is derived from the rect handed to `Draw`. Runs on a CHALLENGE level: a story level boots at Easy on eahl's wiped saves, and `DirectRespawn` skips the fade entirely |
| `net_menumode_reset.txt` | a level launch leaves the menu OUT of the Online Co-op flow (card c337222a). `MenuScene` is a singleton re-ADDED on every return from a level, so `netMode` used to survive a launch and every reader of it after a lobby match read a lie -- worst of all `difficultyMenu_difficultySelected`, which then silently aborted the next ordinary launch after the main menu and the selector were already gone (reload-only). Plants the flag with `eaMenuNetMode`, round-trips through the Tutorial (the one launch path that reads no `netMode`, so it works on both sides of the fix), then requires the flag CLEAR *and* an ordinary Mission 1 launch to still launch. Nothing here is visible in a frame -- `eaMenuNetState` is the observable |
| `respawn_summon.txt` | the co-op branch of card 37f3a663's summon gate: one death with a partner still flying raises the indicator (with its duration), and the death that WIPES the world raises none. Both branches come out of one `eval KillShips` on TeamChallenge, the one level that seats a second local ship with no gamepad -- deliberately NOT the tether, whose timing `?seed=` does not pin |
| `respawn_singleplayer.txt` | the same predicate's other branch, as the card actually reported it: a lone player's death raises no summon, so there is nothing for `LoseLife` to purge a frame later |
| `net_scene_order.txt` | scenario 6 of the step-4 harness (card 25ad0659): reset / pause / checkpoint reach a REAL GameScene in the order the ordered lane carried them, a repeated pause on-edge does not latch a freeze one off-edge cannot clear, and a reset arriving mid-pause neither unfreezes the world early nor survives the peer's resume. DESTRUCTIVE, like `net_reset_spawn.txt` -- it pairs a real client session onto the running scene and applies a real `EvReset`. Scenarios 1-5 are menu-runnable and ride `net_selftests.txt` instead |

All are mutation-tested. Each `preload_*` goes red, naming the first missing asset, when that
level's manifest lines are deleted — `Level2|gfx/marsbg` → 17 lines from
`gfx/marsbg/clouds-background`; `Paratrooper|gfx/marsbg` → 17 from the same;
`InsaneBossI|gfx/sprites` → 10 from `gfx/sprites/playersheet` (only 10 of the 53 deleted
entries decode cold — the rest are already in the shared content cache from the boot warm,
which is why a mutation test asserts "red, naming an asset", not a count). `boot_cold` goes red
on either half it defends — restoring `AwardmentBlade`'s eager load in `LoadContent` trips its
`awardmentblade` `expect-not`, and re-adding a `flipPureName`/`flipGlassesName` load to
`SplashScene.LoadContent` trips the `-revenged-pure` one.
`netbg_catchup` goes red three ways, one per assertion: latch the scene BEFORE the setter's own
`Reset()` (the trap card ca4fd94f was about) or drop `GameScene.UpdateStartup`'s
`NetNoteEntryScene` call and the scene op leaves `ops=`; make `NetTestWipe` skip rebuilding the
entry scene and the run still prints **PASS** with the full `ops=` list, caught only by the
`joiner :` `expect-not`.
`flyspider_bench` goes red on the two mutations that would actually unpin a bench: dropping
`benchIndex.HasValue` from `FlyingSpider.PosePinned` and forcing the tilt back to its roll. Two
mutants it does NOT catch, both correctly — an unconditional `flaptimer.Randomize()` is undone by
the pinned branch's `Reset()` (so behaviour is unchanged), and removing that `Reset()` only shows
on a RECYCLED spider, which a bench spawned at level entry never has.
`net_menumode_reset` is mutation-tested by deleting `ResetNetFlowState()`'s call from
`MenuScene.Initialize` -- the shape the regression takes -- and BOTH of its subject legs go red
independently: the `netMode=False ...` line first, and (with that line removed so the run gets
further) the `[music] play Level1 cue=stage1` line, because the stale flag aborts the launch.
That second leg matters: the same card added a defensive `mainMenu.Show()` to the abort, so a
probe asserting only "some menu is alive" would be MASKED by the guard and pass on the bug.
`net_notice_menu` stays green under that mutation, which is what says the two probes cover
different things rather than one thing twice.
`net_scene_order` goes red when `NetSession`'s `EvPause` handler stops forwarding to
`NetScene.Current` (the recorder sees no pause edges, so leg 1's ORDER assertion fails naming what
it got), and `net_selftests`' `netscen` leg goes red when `HandleClaim`'s `PaidMask` test is
dropped -- a repeat claim from an already-paid slot then pays again, which is the whole point of
that ledger.
`death_fade` goes red both ways that matter, each isolated: reverting `SealAlpha`'s
`DrawStretched` call to the bare `spriteBatch.Draw(whitePixel, dest, Color.White)` drops the line
entirely, and WIDENING the clamp to `texture.Bounds` — the shape a real regression takes, since it
still compiles and still passes a source rect — prints `src=112x112` and fails both its lines.
That second mutation is why the `expect-not` names the padded canvas rather than a sentinel: it is
a value this code can actually produce. **Rebuild `tools/headless` after either**, or the probe tests the old
binary and passes — since card 74998f22 the runner refuses that run rather than letting it pass.
Its third mutation is its PRECONDITION (card af4c3694): kill the ship before the `aliveSlots=`
assertion (an extra `eval KillShips` above it) and that line goes red FIRST, which is the shape of
every run that used to flake here — it was booted without `?invuln` on a level with a live enemy
spawner, so the parked ship sometimes died before frame 600 and sat in its ~10 s respawn countdown
(3 of 30 runs; 30 of 30 pass with `?invuln`). Its companion is
`tools/audit_unclamped_draw.py`, which lints the SHAPE across
the whole wrapper rather than one call's output.
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
`gamebrowser_fallback`'s difficulty half goes red when `GameEntry.KnownDifficulty` is reverted to
a bare cast: `unknownDifficulty=7` stops printing entirely, since it is reported only for a value
the boundary refused. (Rebuild `tools/headless` after the mutation — the probe runs `eahl.exe`,
which links the game sources in, so a game-only rebuild leaves it testing the old binary and
passing.) Its art half goes red when `EnsureArt`'s null guard is reverted — the
`[gamebrowser] rebuilt` line stops printing entirely, since it is reported only when something
resolved to no bundled art. Note what that mutation does NOT produce: an exception. `EnsureArt` wraps its
`Content.Load` in `catch (Exception)`, so a broken fallback throws, gets absorbed, and draws the
identical picture — which is why the probe reads a line reported from what `EnsureArt` recorded
while resolving, and why that line must never be re-derived from `LevelArt` at the report site.

`net_reset_spawn` is mutation-tested SIX ways, each isolated, each a revert of something it claims
(counts are failing LEGS; the probe's own header lists them with the reasoning): SpawnPuppet back
to the pre-card `bin.Add` + unconditional adopt → **1**; SpawnFriend the same → **1**; dropping
`Collection.Purge<PlayerShip>()` from `GameScene.NetApplyReset` → **6** (the one that proves leg 2
is not passing for another reason); dropping `FindLocalShip() != null` from `ManagePuppet`'s spawn
gate → **1** and from `TickFriends`' → **1** (the NEGATIVE leg); dropping `pendingPurges.Clear()`
from `ComponentBin.TopOfTickFlush` → **12**. **The first mutation being only ONE leg is a
finding, not a weak assertion** — `ManagePuppet` releases a puppet the oracle does not hold, and
that block predates the fix (Stage 11.1, `6f36aae`), so the pre-card bug self-heals on the next
tick and the "stranded for the rest of the session" claim it carried was overstated. Its leg 3 is
also legs 1 and 2's positive control, which is why "nothing happened" there cannot pass on a run
whose frames never arrived.

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
