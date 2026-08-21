# `eahl` — the headless desktop test host

Runs **the real game** with no browser, no dev server, no visible window and no `requestAnimationFrame` —
as a plain desktop exe that steps the loop at a fixed dt and writes PNG frames. Built so an agent can
verify rendering *and* behaviour in the background while the user gets on with something else.

```sh
dotnet build tools/headless -c Debug

# boot to the main menu, run 150 frames, screenshot
tools/headless/bin/Debug/net8.0/eahl.exe --flags "?menu" --frames 150 --out shot.png
```

It compiles the **same** `web/EvilAliensWeb/Game/**` + `Compat/**` sources the browser build does
(linked, not copied — see the csproj), on the same KNI version, and reads content **live** from
`web/EvilAliensWeb/wwwroot`. Verified pixel-faithful against Chrome on `?harness=spider`.

## What it is not

It does not replace the browser pass. WASM-only failures (trimming, IndexedDB saves, WebGL-specific
shader behaviour, the JS layer in `index.html`) can only show up in Chrome, and the co-op net stack
needs real WebRTC. **The verification gate in `CONTRIBUTING.md` is unchanged** — this is a faster way
to reach the frame or the number, not a substitute for the final smoke check.

---

## One-shot mode

```sh
# a level, past the intro fade
eahl --flags "?level=Level1&invuln&noattract" --frames 400 --out lvl1.png

# the sprite harness — same as ?harness=spider&frame=3 in the browser
eahl --flags "?harness=spider&frame=3" --frames 150 --out spider.png

# a sequence: writes menu_0030.png, menu_0090.png, menu_0150.png
eahl --flags "?menu" --frames 150 --shot-at 30,90,150 --out menu.png

# behaviour soak, no rendering at all (~1 ms/frame)
eahl --flags "?level=Level2&flyspiders&invuln" --frames 3600 --nodraw --jscalls
```

`--flags` takes **the exact URL query you would type in the address bar** — every flag in
`Compat/DebugFlags.cs` works unchanged (`?level=`, `?harness=`, `?spiderboss`, `?brainboss`,
`?aibench`, `?invuln`, …). The JS-parsed FPS-HUD flags (`?fpshud`, `?nofps`) are the exception: that
HUD lives in `index.html`, so it does not exist here — which is why captures are never contaminated
by it.

## Profiling headlessly

`FrameProfiler`'s per-phase brackets live inside `Game1`, so they already run here — but a sample
only enters its window on `EndFrame()`, which in the browser is called from `Pages/Index.razor.cs`,
code this host does not run. So the profiler used to report a permanently stale window headlessly.
The step loop now times each tick and calls it, which makes the FPS HUD's numbers available with no
Chrome:

```sh
eahl --flags "?level=Level3&brainboss&invuln" --repl
> eval FpsProfile true
> step 300
> eval FpsStatsLine      # ms/frame + the per-phase split (components / collision / net / scene / …)
> eval Census true       # SpriteBatch batches per frame + live components by type
```

**Treat the ms as RELATIVE, never absolute** — this is desktop CLR on desktop GL, which is far
faster than WASM on WebGL, and `--present` is off by default so GPU execution is not in the tick at
all. What it answers well is *which phase and which wave is hot*. `glCalls` reads 0: that counter is
patched into the JS WebGL prototypes and there is no JS here — `eval Census true` is the headless
stand-in, counting the batches the wrapper opens.

## REPL mode — boot once, then drive it

Boot costs ~1 s, so for anything interactive pay it once:

```sh
eahl --repl                     # line protocol on stdin
eahl --script probe.txt         # same commands from a file; first failure exits 1
```

| command | |
|---|---|
| `step [n] [nodraw]` | advance n frames at the fixed dt (default 1) |
| `shot <path.png>` | render the **current** state to a PNG — does not advance time |
| `eval <method> [args…]` | call a `Compat.DebugInput` method |
| `info` | frame counter, sim time, buffer sizes, scene |
| `audio` | `silenced=` / `device=` / `lib=` + the gain OpenAL itself reports |
| `mark` | start a fresh assertion window (drop what was captured) |
| `expect <regex>` | fail unless some captured line matches |
| `expect-not <regex>` | fail if any captured line matches — quotes the offender |
| `help` | list the `eval` methods with their signatures |
| `quit` | |

Every reply is one line starting `ok ` or `err `, so a driver never has to guess when a command
finished. Example script:

```
step 150 nodraw          # settle past the intro fade cheaply
eval Press fire 6        # the same DebugInput.Press that eaPress() calls
step 10
shot out/after_fire.png
eval ScoreDump
quit
```

### Assertions, and committed probes

`expect` / `expect-not` match **per line** against everything the run has printed since the last
`mark` — the game's own `Console.WriteLine` diagnostics (`[loadprofile]`, `[hitch]`, `[net]`, …)
as well as command replies, because the console is teed into a capture buffer at boot. That is
what turns `--script` from "a macro" into "a test": the assertion a regression check usually
needs is *this diagnostic must NOT appear*, which the `ok`/`err` protocol alone cannot express.

```
step 150 nodraw
mark                                    # drop the boot noise
eval Press enter 1
step 600 nodraw
expect \[loadprofile\] Level2 preload:   # prove we got where we claim
expect-not COLD decode in Level2         # the actual assertion
```

The regex is the rest of the line verbatim (no quoting needed, though one surrounding pair is
stripped), and blank lines are matched too, so `expect-not ^$` means what it says. The buffer is
capped; an overflow fails any assertion that would rest on the discarded part — `expect-not`
always, and an `expect` that found nothing — because absence cannot be proven over output that
was thrown away. An `expect` that already matched is unaffected.

Checks worth re-running later live in **`probes/`** and run as a set with
`python tools/headless/probes/run_probes.py`. Conventions, the four rules for writing one, and
the menu-navigation crib: [`probes/README.md`](probes/README.md).

`eval` binds by reflection to the **public statics on `Compat/DebugInput.cs`** — the same curated
surface the browser console exposes (`eaPress`, `eaAiBench`, `eaTexProbe`, `eaTeamSeat`, `eaBinTest`,
`eaScoreDump`, …). Nothing is mirrored, so a method added there is callable here immediately, and
its return value is printed. `help` lists what is currently available.

## Exit codes

`0` ok · `1` a run/script failure · `2` bad arguments · `3` `--software` was asked for and is
unavailable · `4` `--fake-no-audio-device` could not install its `alsoft.ini`.

---

## Gotchas that will otherwise cost you an hour

- **A screenshot in the first ~2 seconds is a WHITE RECTANGLE, and nothing is broken.** Every scene
  that calls `Background.Reset()` — level entry *and* the debug scenes (`?harness=`, `?textshot`) —
  starts in `BackgroundState.LeavingHyperspace` with `fadeFactor = 0.998`, a white flash that decays
  at `0.0005/ms`, i.e. **~120 frames at 60 Hz**. Step past it (`--frames 150`, or `step 150 nodraw`,
  which costs ~150 ms) before shooting. The host prints a `NOTE` when it captures a near-white frame
  inside that window, but do not rely on that — just settle first.
- **`--fps` is not a speed limit.** It is the dt the game is *told* it got. The loop always runs flat
  out: ~5.5 ms/frame rendered, ~1 ms/frame with `--nodraw`, i.e. roughly 3× and 17× real time.
- **Keep `--size` at 4:3** (800x600, 1600x1200). `RenderScale` letterboxes anything else, and the
  black bars land in your PNG. Non-4:3 is allowed, just rarely what you want.
- **Saves start empty every run, by design** — a leftover save silently changes unlock state,
  difficulty and the attract flow between runs. `--saves <dir>` keeps a persistent profile when
  that is the point of the test.
- **The default save dir is PER PROCESS, and it had to become so** (card de82597f). It was the one
  fixed path `%TEMP%/eahl-saves`, shared by every eahl on the box, and `HeadlessSaveStore`'s ctor
  recursively DELETES its dir at boot — so with several runs in flight (eight parallel worktree
  agents each running a 50-probe suite is the normal condition here) one run's boot deleted
  another's save tree mid-write. `ScreenshotSaver.SaveScreenShot` opens
  `<saves>/fs/EvilAliens/<Level>.dat` with `FileShare.None`, so the write threw and
  `screenshot_alpha.txt` failed *after* printing a perfectly correct `[shot] … alphaMin=255` —
  the once-seen failure that card records. Measured with a churner performing the same delete a
  concurrent boot performs: **10/10 runs failed** when it targeted the shared dir, **0/10** when it
  targeted a per-process one, same binary, ~780 wipes per trial. Each process now claims
  `%TEMP%/eahl-saves/<pid>-<ticks>` and removes it on exit; a run that dies without unwinding
  leaks one, swept after 6 h by the next run. `--saves <dir>` is unchanged and never swept.
- **An `eval` failure names its own cause now, innermost first** (same card). `eval` binds to
  `DebugInput` by reflection, so *every* failure inside the game used to surface as
  `err TargetInvocationException: Exception has been thrown by the target of an invocation.` — a
  line that says only that reflection was involved. The chain is unwrapped (the
  `TargetInvocationException` layers dropped, any other wrapper kept) and the innermost stack
  follows, **source-located frames first**: `run_probes.py` prints exactly one line after the
  `err` it stopped on, so a raw stack would show `SafeFileHandle.CreateFile` where the useful
  frame is `ScreenshotSaver.cs:line 254`.
- **The `[hitch]` frame watchdog does not run here.** `LoadProfiler.NoteFrame` is called only from
  `Pages/Index.razor.cs`, which this host replaces — so a run is silent about long ticks no matter
  how long they are. `?loadlog`'s `COLD decode` lines DO work (they are a preload-bracket fact, not
  a timing one), as does `eval PreloadExport`. Anything phrased as "confirm the hitches stop" needs
  Chrome.
- **The PHYSICAL MOUSE is suppressed, and it was not always** (card 83054936). KNI's SDL2 backend
  answers `Mouse.GetState()` from `SDL_GetGlobalMouseState` — the desktop pointer AND the desktop
  button mask, with **no focus check** — so a headless run used to sample whatever the developer's
  hand was doing and feed it into menus, the back tip and mouse-aim. It is the ONE physical input
  that gets in: `Keyboard.GetState()` reads a key list filled from window key *events*, and this
  host never pumps the SDL event loop. That silently flaked the committed probe suite
  (`menu_backtip.txt` failed 15 of 20 runs), because `MenuSub1.HandleMouse` hover-selects on cursor
  movement *and* swallows that tick's keypress, and a held button eats a scripted click's rising
  edge. Every run now says which it is:
  ```
  [eahl] input    physical mouse suppressed
  ```
  The cursor parks at `-1000,-1000` in design space and both buttons read released. `eval MouseAt`
  / `Press` / `Hold` are unaffected — scripted input never came from `Mouse.GetState()`.
  `eval MouseState` reads the whole thing back; **`--real-mouse`** restores the old behaviour and
  is the mutation control for the probe assertions that pin it. See `probes/README.md`.
- **Reproducibility: `?seed=<n>` gets you most of the way, and the residual is the boot frame.**
  Two runs of the same gameplay flags are otherwise different worlds — measured on
  `?level=OwnLevel&noattract`, mean |diff| **0.2**, **MAX 210** of 255, which is a bigger signal
  than most changes an A/B is trying to see. `?seed=<n>` (card d937c721) seeds `RandomHelper`, the
  gameplay RNG, and is what makes a level-level A/B measure the change instead of the divergence.
  - **Same seed ⇒ one of a HANDFUL of discrete worlds, not one world — and how many depends on
    how busy the machine is.** Two measurements of the same rig, same binary: on a quiet box, 10
    consecutive runs byte-identical; with sibling builds hammering the CPU, 10 runs landed in **4
    distinct states** (modal one 6/10, the others 2/1/1). The odd ones sit at mean 0.45 / MAX 203
    — the unseeded noise floor — so an unlucky pair looks exactly like a real effect. A cold
    binary is the most reliable way to draw a rare state, so the first run after `dotnet build`
    is the one most likely to be odd.
  - **WORKING PRACTICE — capture each side of an A/B TWICE and require the same-side pair to be
    byte-identical before you compare sides.** That is valid whichever state the lottery hands
    you, and it is the only cheap check that cannot be fooled; "run it twice and it matched" on a
    single side is not it. Prefer an in-process A/B (two `shot`s off ONE boot with no `step`
    between) whenever the rig allows, since it skips the lottery entirely. Controls for the
    numbers above, same rig: a different seed diverges (1.48) and an unseeded pair diverges
    (1.08), so a zero diff really is the seed doing the work.
  - **Why the residual exists.** `RunOneFrame()` ticks once off the wall clock to build the device
    and run `LoadContent`; every frame after it is exactly `1/--fps`. That boot dt is now pinned to
    one fixed step (`IsFixedTimeStep` + `TargetElapsedTime`, restored right after — see
    `HeadlessHost.Boot`), which matters because `RandomHelper.RandomFromAverage` is dt-PROPORTIONAL,
    so a variable boot dt spends a different amount of even a seeded stream. What is left is that a
    fixed-step `Tick` runs `accumulated / TargetElapsedTime` catch-up updates and the boot's
    accumulated wall time still varies, so an occasional run starts one step further on. Refuted
    fixes, do not retry them: `MaxElapsedTime = _step` throws (KNI enforces a 0.5 s floor), and
    `ResetElapsedTime()` from a `BeginRun` override made every run diverge again.
  - `?seed=` reaches `RandomHelper` ONLY. Quad's and ShipConnector's FX RNGs, Juice's shake RNG and
    SplashScene's `rng` are separate instances by design and stay unseeded, so a rig showing a
    laser, the connector, a shake or the splash keeps some jitter of its own.
  - Settling to a steady state still beats pinning an exact frame where the rig allows it.

---

## How it works (and why it is not a fork of the game)

- **No window.** KNI's SDL2 backend creates its window with `SDL_WINDOW_HIDDEN` and shows it in
  exactly one place: `ConcreteGame.RunGameLoop`, the blocking loop behind `Game.Run()`. This host
  never calls `Run()` — it calls `RunOneFrame()` to build the device and run `Initialize`/
  `LoadContent`, then drives `Update`/`Draw` itself. A GL context needs a window on Windows, so one
  exists; it is never shown, focused or pumped.
- **No presenting.** `EndDraw()` (`SDL_GL_SwapWindow`) on that hidden window measured **~32 ms a
  frame** — 41 ms/frame with it, 5.5 ms without. Nothing consumes the swap chain, and the capture
  reads the back buffer *before* the swap anyway, so it is off by default. `--present` restores it.
- **The capture is the finished frame.** `Game1.Draw` ends by blitting `sceneTarget` to the back
  buffer, letterboxed. `HeadlessGame` reads the back buffer between `Draw()` and `EndDraw()`
  — after bloom, post FX and the present blit, before the swap.
- **Silent by default, at the mixer** (`HeadlessAudio.cs`): `SoundEffect.MasterVolume = 0` plus a
  direct `alListenerf(AL_GAIN, 0)`, both in force before the first sound can play, so a background
  soak does not play SFX through the speakers. The device, the mixer, the `.wav` decodes and every
  source stay real and running — only the gain is zero — so an audio-path crash still surfaces
  instead of being hidden. `audio` reports the gain **read back out of OpenAL**, which is the only
  part of this you should believe; `--audio` opts back in. **Do not "simplify" this to
  `ALSOFT_DRIVERS=null`** — that looks like the elegant answer, is what this file used to do, and
  kills the process with an `AccessViolationException` under sustained SFX play (deterministic,
  3/3). Full autopsy of that and of why an environment variable cannot configure OpenAL Soft from
  managed code: the header comment in `HeadlessAudio.cs`.
- **A box with no audio device still runs, and says so** (card 72297923). `HeadlessAudio.BringUp()`
  opens the device once at boot inside a try/catch and reports the outcome in the `[eahl] audio`
  line as `device=ok|none|nolib`; a machine with no sound card (CI container, SSH session,
  driverless VM) prints a loud `NO AUDIO DEVICE` and plays on, deaf. It never fails the run. Opening
  it at boot rather than lazily on the first sound is what makes `alGain` readable from frame 0 and
  lands the mixer mute *before* the first sound rather than just after it. **`--fake-no-audio-device`
  reaches that path on a box that HAS a device**, by writing an `alsoft.ini` naming a backend that
  does not exist so OpenAL genuinely fails to open one — it refuses rather than clobber an
  `alsoft.ini` that is already there, and removes its own on exit. Pinned by
  `probes/no_audio_device.txt`.
- **The gain readback is no longer Windows-only.** The P/Invokes resolve OpenAL through KNI's own
  candidate list (`soft_oal.dll`, `libopenal.so.1`, `libopenal.1.dylib`, `openal`) instead of
  naming `soft_oal.dll`, and whichever one answered is reported as `lib=`. Nobody has run `eahl`
  off Windows yet; if you do and the gain still will not read, add the name in `HeadlessAudio`
  rather than relaxing `probes/silence.txt`.
- **A fake browser, not 13 stubs** (`HeadlessJsRuntime.cs`). `Microsoft.JSInterop` is a plain
  netstandard package, so every `Compat/*Interop.cs` compiles here unchanged and this class answers
  the ~37 `ea*` calls. Stubbing the interop classes instead would have forked exactly the logic
  worth testing — `WebcamInterop`'s hit tests, `LoadProfiler`'s manifests, and above all
  `DebugInput`, which is the whole `eval` surface.
- **Content is not copied.** `HeadlessTitleContainer` registers a `TitleContainerFactory` pointing
  at `wwwroot`, so the 282 MB `Content/` tree is read in place and a regenerated `.dds`/`.mgfxo` is
  picked up on the next run with no copy step.
- **Nothing in `web/` knows this exists.** Same isolation rule as `tools/sim/logic_probe`: no
  `ProjectReference` either way (that project targets browser-wasm and cannot be referenced from a
  desktop exe), and CI only publishes `web/EvilAliensWeb`. The shipped build is unaffected.

## Two processes, one co-op session (`?net=`, `--nettime game`, `--net-port`)

The `eaNet` loopback -- the `BroadcastChannelTransport` every `?net=` boot uses -- was stubbed
here as three no-ops, so a headless `?net=host` opened a channel with nobody on it.
`LocalSocketNet.cs` backs `eaNet.open/send/close` with a **localhost TCP socket**, so two eahl
PROCESSES can hold a real session between them (card 054947f3). Which end dials is decided by the
boot role, never by who started first: `?net=host`/`?net=jiphost` listen, `?net=join`/`?net=jipjoin`
dial. The port is derived from `?room=` so both sides agree with no configuration; `--net-port`
overrides it, and a bind clash is reported and survived rather than killing the run.

**`--net-peers <1..3>` raises how many clients the listening side serves at once** (card
583a3ef8; default 1 = the classic two-process rig, byte-identical behaviour). Accepted peers get
monotone ids (`peer1`, `peer2`, ... -- never reused in a process); a client arriving over
capacity is refused and closed, with the refusal log line naming the flag. This is the plumbing
for the 3-4-process N-peer rigs (`plans/4p-online-coop.md`); until the session layer is N-peer
(card 87242257) a >1 setting only exercises the transport.

**`--nettime game` is required for such a run.** It advances the net layer's clock by one `--fps`
step per frame instead of reading the wall clock -- without it `--nodraw`'s ~17x real time makes
the wire's cadences fire ~17x too rarely per unit of world motion, and any comparison between the
two peers measures that artifact. Off by default, so every existing probe is unchanged.

Two processes must be stepped in TURN: each advances only when told to, and a peer left behind in
net time is dropped (3 s + 5 s grace = 480 frames), so keep interleave chunks well under that.
`python tools/sim/net_jip_sync.py` is the driver that does all of it, plus the world diff.

Nothing here is linked into the WASM build -- `System.Net.Sockets` is meaningless in the browser,
which is why the file lives under `tools/headless/` rather than in `Compat/`.

## `--software` (CPU rasterization)

You do not need this on a dev box — the default path is already headless and background-safe, it
just runs its GL on the installed driver. `--software` is for a machine with **no usable GPU at
all** (CI container, SSH session, driverless VM), where creating a context would otherwise fail.

It sets `SDL_VIDEO_GL_DRIVER` to a Mesa **llvmpipe** `opengl32.dll`, routing every GL call through
the CPU. Mesa is not vendored (a ~30 MB third-party binary with no business in a game repo), so
fetch a Windows llvmpipe build and either drop it at `tools/headless/mesa/opengl32.dll` or point
`--mesa` / `$EAHL_MESA` at it. Expect roughly an order of magnitude slower *rendering*; `--nodraw`
work is unaffected. If it cannot be resolved the run **fails with exit 3** rather than quietly
falling back to the GPU, which would make a "works headlessly" result meaningless.

## Keeping it working

The csproj pins the same `nkast.*` `4.1.9001.*` versions as `web/EvilAliensWeb.csproj` — **keep them
in lockstep**, the whole point is that this runs the same code on the same engine. If a new
`ea*` JS function is added to `index.html`, give it a case in `HeadlessJsRuntime` (unhandled calls
return `default` and are reported under `--verbose`, which is safe but silent). New `Game/` or
`Compat/` files are picked up automatically by the recursive `Compile Include`.
