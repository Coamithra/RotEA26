# AI: measure the challenge levels (completion matrix)

Card `9391f95a`, follow-up to `f4d1721f` ("Improve AI"). That card's stated bar was "the AI
finishes every level and challenge on Very Hard"; only the three story levels were ever
measured. This card measures the nine unmeasured ones and produces the honest matrix.

Levels in scope: **Tutorial, Braineroids, SpaceDodge, OwnLevel, ClassicAliens, InsaneBossI,
TeamChallenge, CrazyGame, Paratrooper**. WebcamAliens is excluded by design (camera-driven,
no `ControlDevice.AI` ship path).

## Context — what the research turned up

All nine derive from `GameScene`, so `?aibench`'s progress/verdict seams
(`BenchEventPos`/`BenchEventCount`/`BenchVerdict`) work on them unchanged. Four things
found while reading the code change what "measure" has to mean:

1. **Eight of the nine have INFINITE lives, so `GAME OVER` is unreachable on them.**
   `GameScene.Initialize` sets `score.Lives = -1` (GameScene.cs:736) and only the story
   levels (`ApplyDifficultyPolicy` → 7, called by Level1/2/3 only) and `InsaneBossI` (5 on
   Hard/Very Hard, 1 on Inzane — unlimited at Easy/Medium) override it. `TutorialLevel`
   declares an `InitialLives = 7` const but never reads it, so the Tutorial is unlimited too. `LoseLife`'s decrement/game-over block is gated on `score.Lives >= 0`, so at
   -1 a death just respawns forever. Confirmed live: a 1200 sim-second ClassicAliens run took
   **14 deaths** and never produced a verdict.
   → `AiBench`'s two-value verdict cannot express failure on these levels. The matrix needs a
   third outcome — **TIMEOUT, with the event index it was stuck on** — plus deaths and
   sim-seconds, or a "fail" is indistinguishable from "still going".
2. **TeamChallenge cannot be measured at all as things stand.** `TeamChallenge.Initialize`
   seats slot 1 as `ControlDevice.PadOne`; `GameScene.Update` force-pauses every tick when a
   seated pad device reports `!InputHandler.PadConnected(i)`. With no gamepad attached a soak
   sits in the pause menu forever — zero progress, no verdict, and nothing in the bench line
   says why.
3. **Braineroids and CrazyGame use the ordinary ship.** The card's "unusual control schemes"
   caution is speculative — no level-specific control branch exists outside the level classes
   themselves. **Paratrooper is the real special case**: `Paratrooper.Update` pins every ship
   to `(400,500)` each tick and clamps bullet angles upward, i.e. it is a TURRET. Only
   `DoAIFire` matters there and every steering metric (`contacts`, `revs/s`, `coast%`) is
   vacuous by construction — the matrix must say so rather than print a flattering zero.
4. **A headless soak runs ~60x realtime** (measured: 10 sim-seconds in 163 ms wall on
   ClassicAliens), so a 3-run × 9-level sweep is minutes, not hours. Cost is dominated by the
   ~10-15 s WASM boot per run.

## Design

### A. `eaAiBench.matrix(...)` — the sweep runner (`wwwroot/index.html`)

The card's recipe is one manual boot per level: type a URL, wait for WASM, call
`eaAiBench.soak(1200)`, read one line. Twenty-seven of those by hand is where the honesty
goes. The runner automates exactly that recipe and nothing more:

- `eaAiBench.matrix(levels, simSeconds, runs, difficulty)` writes a run PLAN into
  `sessionStorage` and navigates to the first run's URL.
- On every boot the `eaAiBench` IIFE checks for a pending plan; if there is one it waits for
  the game to be up, runs `soak`, appends the result row, and navigates to the next run.
- **A fresh page load per run** — not an in-process relaunch. It is the same recipe a human
  would type, so nothing carries over between runs (RNG, achievements, locked difficulty,
  `score.Lives`), and a run that wedges costs one row, not the sweep.
- `eaAiBench.matrix.results()` re-prints the table, `.stop()` clears the plan, `.status()`
  reports progress. **Fire-and-forget** — the runner must never be awaited from an
  automation harness (the CDP eval cap is 45 s, well under one run).
- Output is a markdown table: level · run · verdict · sim-seconds · deaths · prog · contacts ·
  revs/s · coast% · idle%.

### B. A third verdict + the numbers behind it (`Compat/AiBench.cs`)

`RunHeadless` returns `"<simSeconds> <verdict-or-running>"`. Extend the JS side to record
`TIMEOUT` when the cap is reached with `running`, and add an `AiBench.Row()` seam returning
the machine-readable counters for one run (verdict, sim seconds, deaths, prog/total, contacts,
revs/s, coast%, idle%) so the runner does not have to regex the human report line.

### C. `?aiteam` — make TeamChallenge measurable (`TeamChallenge.cs` + `DebugFlags.cs`)

Seat the second slot as `ControlDevice.Generic` instead of `ControlDevice.PadOne` when the
flag is set. `Generic` has no connected-check, so the force-pause never arms, and `?aiplayer`
is what actually drives both ships through `EffectiveController` — `PlayerShip.Update` has no
`Generic` input case, so the flag alone leaves that slot inert. It is a bench seam, **not** a
fix for TeamChallenge being unplayable without a pad (that needs a real input case → follow-up
card). Debug-gated and added to `DebugFlags.Active`; shipped behaviour is unchanged.

### D. Measure

3 runs × 9 levels at `?difficulty=Very_Hard`, 1800 sim-second cap (ClassicAliens is 20 waves
and reached only 8 in 1200 s, so 1200 is too tight to call a stall). No `?invuln` — a run must
be able to fail. Record the matrix; single runs of a stochastic fight vary a lot, so the row
carries all three runs and the write-up treats differences under ~30% as noise.

### E. Triage what the matrix shows

The card says "expect some to need AI work rather than just measurement". The rule for this
card: fix what is a **defect** (a type missing from `Oracle.GetBaddies`, a shoot-list gap, a
level the AI cannot even attempt), and open a follow-up card for anything that needs genuine
AI capability work — the same split the parent card used for "dies to sustained bullet fire".
Scope-fixing is the user's call, so anything large gets flagged, not silently attempted.

### F. Docs

`web/EvilAliensWeb/CLAUDE.md` → the AI section: the measured matrix, the infinite-lives
gotcha (a `revs/s=0` row can mean "never finished", not "smooth"), Paratrooper's vacuous
steering metrics, `?aiteam`, and `eaAiBench.matrix`. Root `CLAUDE.md` gets the two new flags
in the AI-bench bullet.

## Verification

The runner IS the verification tool — this card's deliverable is data, so the gate is that the
data is reproducible and honestly labelled:

- Clean `dotnet build -c Debug`.
- The full sweep runs end to end in real Chrome with **zero console exceptions**, and prints
  the matrix.
- `?aiteam` proven by TeamChallenge reaching non-zero `prog` and non-zero `ticks` where it
  previously sat at 0 in a pause menu — before/after on the same boot URL.
- A spot re-run of one level reproduces its verdict class (not its exact numbers — the fights
  are stochastic).

## Out of scope

- WebcamAliens (no AI ship path).
- The three story levels (already measured on the parent card).
- Difficulty tiers other than Very Hard.
- **Actually making the AI beat everything.** That is the parent card's still-open goal; this
  card measures, fixes defects, and opens cards for the rest.
