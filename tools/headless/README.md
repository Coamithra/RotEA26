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
| `audio` | `silenced=` + the gain OpenAL itself reports |
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

`0` ok · `1` a run/script failure · `2` bad arguments · `3` `--software` was asked for and is unavailable.

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
- **The `[hitch]` frame watchdog does not run here.** `LoadProfiler.NoteFrame` is called only from
  `Pages/Index.razor.cs`, which this host replaces — so a run is silent about long ticks no matter
  how long they are. `?loadlog`'s `COLD decode` lines DO work (they are a preload-bracket fact, not
  a timing one), as does `eval PreloadExport`. Anything phrased as "confirm the hitches stop" needs
  Chrome.
- **Only the boot frame has a non-synthesised dt.** `RunOneFrame()` ticks once off the wall clock to
  build the device and run `LoadContent`; every frame after it is exactly `1/--fps`. Note that the
  game itself is not fully deterministic across runs regardless (RNG seeding), so identical flags
  do not guarantee identical PNGs — settle to a steady state rather than pinning an exact frame.

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
  buffer through the gamma shader, letterboxed. `HeadlessGame` reads the back buffer between
  `Draw()` and `EndDraw()` — after bloom, post FX, gamma and the present blit, before the swap.
- **Silent by default, at the mixer** (`HeadlessAudio.cs`): `SoundEffect.MasterVolume = 0` plus a
  direct `alListenerf(AL_GAIN, 0)` once OpenAL is up, so a background soak does not play SFX
  through the speakers. The device, the mixer, the `.wav` decodes and every source stay real and
  running — only the gain is zero — so an audio-path crash still surfaces instead of being hidden.
  `audio` reports the gain **read back out of OpenAL**, which is the only part of this you should
  believe; `--audio` opts back in. **Do not "simplify" this to `ALSOFT_DRIVERS=null`** — that
  looks like the elegant answer, is what this file used to do, and kills the process with an
  `AccessViolationException` under sustained SFX play (deterministic, 3/3). Full autopsy of that
  and of why an environment variable cannot configure OpenAL Soft from managed code: the header
  comment in `HeadlessAudio.cs`. **Two limits came with it:** a real audio device is now opened
  (the null backend's one genuine merit was that none was, so a box with *no* sound card is no
  longer covered — there is no try/catch around KNI's audio bring-up), and the gain readback is
  Windows-only, so off Windows the run is quiet but `probes/silence.txt` cannot confirm it.
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
