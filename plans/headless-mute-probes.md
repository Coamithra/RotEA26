# eahl: real silence + a committed-probe convention (card 1e476668)

## Context

Two halves, same file area, one PR. (This card absorbed card `3581f0ca`.)

### (a) eahl plays audio out of the user's speakers

`tools/headless/HeadlessAudio.cs` has shipped since the original eahl commit (`992b138`), and
both `tools/headless/README.md` and `tools/CLAUDE.md` state that eahl is silent by default and
opens no audio device. **That claim was false**, which is how a background soak came to blast a
wave of sped-up bullet SFX at the user.

Measured on `eahl --flags "?level=Level1&invuln" --frames 600 --nodraw` with `ALSOFT_LOGLEVEL=3`:

| run | what OpenAL Soft logged |
|---|---|
| default ("silenced") | `GetConfigValue: Key drivers not found` · `Added "mmdevapi" for playback` · `alcOpenDevice: Created device …, "OpenAL Soft on Headphones (Black Diamond)"` · `(EE) alc_cleanup: 1 device not closed` |
| same binary, `ALSOFT_DRIVERS=null` exported **in the shell** | `Initialized backend "null"` · `Added "null" for playback` · `alcOpenDevice: Created device …, "No Output"` |

So the delivery was broken. (The *mechanism* looked correct too at this point. It is not —
see Design §1: once actually delivered, the null backend crashes the run.)

**Root cause.** `runtimes/win-x64/native/soft_oal.dll` is OpenAL Soft **1.18.1** and imports
**`msvcrt.dll`**; it reads the variable with `getenv`. .NET's `Environment.SetEnvironmentVariable`
calls Win32 `SetEnvironmentVariableW`, which updates the process environment block but **not**
msvcrt's already-initialised `_environ` table — so `getenv` never sees it. Proven in isolation:

```
msvcrt getenv(PATH) len = 1689                            # the import resolves
after Environment.SetEnvironmentVariable: msvcrt sees <null>
_putenv rc = 0
after _putenv:                            msvcrt sees null
```

There is exactly one `msvcrt.dll` per process, so writing through `msvcrt!_putenv` writes the
table `soft_oal` reads.

(`SDL_AUDIODRIVER=dummy` was also being set. KNI drives `SoundEffect` through OpenAL, not SDL
audio, so it never had anything to do with the silence. Dropped.)

### (b) There is no way to commit a regression check

Raised by `/review` on card `74b30beb`, which was a pure DATA change
(`Content/preload/manifest.txt`) whose failure mode is completely silent: delete the `gfx/marsbg`
lines and there is no build error, no failing test, and nothing to notice until someone plays
Level 2 and feels a 1.2 s hitch.

`eahl --script <file>` already exits non-zero on the first failure, so the shape was close, but
the needed assertion is *"this console line must NOT appear"*, which the line protocol could not
express — a command either replies `ok` or `err`, and `LoadProfiler`'s `Console.WriteLine` output
was unreachable.

## Design

### 1. Real silence — `tools/headless/HeadlessAudio.cs`

**The plan was `msvcrt!_putenv("ALSOFT_DRIVERS=null")`. It works, and it must not be used.**
Delivering the variable through msvcrt's own `_putenv` did reach OpenAL Soft — the log flipped
to `Initialized backend "null"` / device `"No Output"` — and with the null backend actually
engaged for the first time, a 90-simulated-second Level 2 soak died with

```
Fatal error. System.AccessViolationException: Attempted to read or write protected memory.
   at EvilAliens.SoundManager.GetEffect(System.String)
```

deterministically (3/3), while the same script on the real `mmdevapi` backend was clean (3/3).
An `AccessViolationException` is a corrupted-state exception, so `GetEffect`'s `catch (Exception)`
cannot save it and the process simply dies. Confirmed to be the backend and not the new plumbing
by setting `ALSOFT_DRIVERS=null` in the **shell** with the in-process path disabled: still 2/2
crashes. So the mechanism the original file reached for is not merely undelivered, it is unusable
at this OpenAL Soft version — and the only reason that was never discovered is that it had never
once taken effect.

**Shipped mechanism instead:**

- `SoundEffect.MasterVolume = 0f`, applied after `Boot()` — before any sound has played, so no
  source has latched a gain. Nothing in `Game/` or `Compat/` writes `MasterVolume` (checked), and
  `SoundManager.Spawn` sets per-instance `Volume` on **every** branch, which XNA multiplies by it.
- `alListenerf(AL_GAIN, 0)` as a second, global mute at the mixer, applied lazily from
  `HeadlessHost.Step` because KNI brings OpenAL up on the *first sound* — there is no context at
  boot. Safe rather than leaky: `MasterVolume` is already in force, so the sound that triggers
  initialisation is itself silent.
- `HeadlessAudio.ListenerGain` reads the gain **back out of OpenAL**. That is the assertable
  fact; `MasterVolume` is only what we asked for, and this card exists because what was asked for
  and what happened had diverged silently for eahl's whole life. Seeded with `NaN`, and gated on
  `alcGetCurrentContext() != NULL`, so an unreadable gain reports `<unreadable>` and **fails** a
  probe rather than reading as 0 and passing vacuously (it did exactly that on the first cut).

Rejected: stubbing `SoundManager`/`SoundEffect`, which *is* skipping the code path the card
requires be kept live. What is lost with the null backend: no audio device is opened on a box
with no sound card. That property is not available at OpenAL Soft 1.18.1 — follow-up card.

`--audio` opts back in and doubles as the probe's negative control.

**Second finding, kept in the file's header because it is why (1) hid for so long:**
`Environment.SetEnvironmentVariable` cannot configure OpenAL Soft, and fails silently.
`soft_oal.dll` imports `msvcrt.dll` and reads with `getenv`; .NET writes the Win32 environment
*block* via `SetEnvironmentVariableW`, not msvcrt's already-initialised `_environ` *table*.
Measured in isolation: `<null>` after `SetEnvironmentVariable`, `null` after `_putenv`.

### 2. An assertion primitive — `tools/headless/Program.cs`

The card offered a new `DebugInput` method returning cold-decode counts. Not needed:
`DebugInput.PreloadExport` already exists and `LoadProfiler` already prints
`[loadprofile] COLD decode in <Level>: …`. Only the *assertion* was missing. So the card's other
option, generalised:

- `Console.Out` / `Console.Error` are teed into an in-process capture buffer from boot.
- New script/REPL commands:

  | command | |
  |---|---|
  | `mark` | discard the capture so far (start a fresh window) |
  | `expect <regex>` | at least one match in the window, else `err` |
  | `expect-not <regex>` | no match in the window; the failure reply quotes the offending line(s) |
  | `audio` | `ok audio silenced=<bool> masterVolume=<f> alGain=<f>` — the last read back out of OpenAL |

  Assertions deliberately do **not** reset the window (two assertions over one window is the
  common case); `mark` is the explicit reset.

This is general on purpose: `[hitch]`, `[bin] …`, `[net] …` and every other console diagnostic
become assertable by future probes at no extra cost.

### 3. The probe convention — `tools/headless/probes/`

- One `.txt` per probe, header comment saying what it pins and what a failure means.
- Per-probe boot flags live in a directive comment that the runner parses and eahl ignores as an
  ordinary `#` line, so every probe also runs standalone under `eahl --script <file>`:

  ```
  # eahl: --flags "?menu&noattract&loadlog"
  ```

- `tools/headless/probes/run_probes.py` runs each probe in its **own process** (fresh boot; no
  run inherits another's state), prints `PASS`/`FAIL` per probe, dumps the failing probe's output
  tail, and exits non-zero on any failure. `--only <glob>`, `--list`, `--build`.

**Committed probes**

1. `silence.txt` — pins the mute where it counts: the gain OpenAL itself reports must be 0.
2. `preload_level2.txt` — drives the **menu** path into Level 2 with `?loadlog` and asserts no
   `COLD decode in Level2`.

**Capture rule the next probe author would otherwise walk into:** a `?level=` boot has no splash,
so `Game1`'s `QueueIdleWarm` assets drain into gameplay and are recorded as that level's COLD
decodes (~20 spurious `gfx/game/space/*` lines). A preload probe must therefore drive the MENU
path. Rehearsed headlessly: `?menu&noattract&loadlog` → `Press enter` (mission carousel) →
`Press right`/`left` to the mission → `Press enter` → difficulty → `Press enter`.

Extension to `Paratrooper` / `InsaneBossI` / `Demo2` is measured, not assumed: only levels that
are reliably COLD-free within a sane runtime get a committed probe; the rest get a follow-up card.
A flaky green tick is worse than no probe.

## Verification

All headless — no dev server, no browser. `eahl` is both the thing under test and the harness.

1. `dotnet build tools/headless -c Debug` clean.
2. **Silence, positive:** `audio` reports `silenced=True masterVolume=0 alGain=0` (4/4 runs).
3. **Silence, negative control:** the same run with `--audio` reports
   `silenced=False masterVolume=1 alGain=1` — so the assertion is not vacuous and the opt-in
   genuinely works.
4. **Audio path still runs:** the 90-simulated-second Level 2 soak that kills the null backend
   is clean 3/3 here, exit 0.
5. **Probes green:** `python tools/headless/probes/run_probes.py` exits 0 (2/2).
6. **Probes red on broken input** (mutation test — the standard this repo holds an oracle to):
   - deleting the 17 `Level2|gfx/marsbg*` lines from `manifest.txt` → `preload_level2` FAILS,
     `expect-not ... matched 17 line(s), first: ... gfx/marsbg/clouds-background`; restored via
     `git checkout`.
   - `silence.txt` against an `--audio` boot → FAILS,
     `expect /^ok audio silenced=True masterVolume=0 alGain=0$/ matched nothing`.

## Out of scope

- Any change under `web/EvilAliensWeb/**` — this card touches zero game-side files.
- The `(boot)` COLD population (card `4d47c5ba`).
- Growing `manifest.txt` / capturing new levels.
- Upgrading OpenAL Soft; a non-Windows silence path (guarded and documented, not implemented —
  no non-Windows box here to verify it on).
