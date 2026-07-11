# Tracker: feature/holosim-tuner

Card: `2382b514` — Holo-sim filter: green-phosphor look + live tuner panel + ClassicAliens
Worktree: `wt3` · Dev port: `5283`

Scope (user): (1) shader pulls toward classic monochrome phosphor green, with a pulse that
swings between green and true colour; (2) live overlay slider panel to tweak green pull /
pulse strength / static strength / static rate (current static too harsh); (3) the filter
also runs in the Evil Aliens Classic challenge.

## Phase 1: Pick Up
- [x] Card created + In Progress; main pulled; worktree wt3 + branch pushed

## Phase 2/3: Research + Design
- [x] Read ClassicAliens.Update (Jump call site), index.html eaLazer panel + DebugInput.SetLazer (template)
- [x] Design settled: shader gets ONE new `Green` param (pulse computed C#-side where Time
      lives); "static strength" = the existing holoburst scale (whole spike incl. grain, grain
      coefficient baked softer 0.30->0.20); "static rate" = HoloSim.HiccupRate driving both
      levels' RandomFromAverage; panel = green pull / pulse / glitch strength / glitch rate
      (per min) / intensity.

## Phase 4: Implement
- [x] holosim.fx: Green mono-green lerp (P1 phosphor, +8% lift), grain 0.20, contrast 0.40; rebuilt
- [x] HoloSim.cs: DefaultGreenPull 0.6 / DefaultGreenPulse 0.4 / GreenPulseHz 0.18 /
      DefaultHiccupRate 0.12; Green + HiccupRate props; ?holofilter=0 = whole-filter kill
- [x] Game1.ApplyHoloSim: hsGreen cached param
- [x] DebugFlags: HoloGreen/HoloGreenPulse/HoloStaticRate + parse + SetHoloOverride
- [x] DebugInput: [JSInvokable] debugSetHolo
- [x] index.html: eaHolo console fn + slider panel (gate level=tutorial|classicaliens|holotune)
- [x] ClassicAliens: Poke + hiccup bursts + Activating/Terminating Training bursts
- [x] CLAUDE.md: tutorial punch-up bullet extended

## Phase 5: Verify
- [x] Build clean (0 errors); shaders rebuilt
- [x] Tutorial: green phosphor look + panel live (screenshot); eaHolo(0,...) snaps back to
      true colour instantly (live drive proven)
- [x] ClassicAliens: filter + panel + caught the "Activating Training Mode..." burst mid-spike
- [x] Normal boot (?menu): true colour, no panel, no filter
- [x] Zero console exceptions

## Phase 6: Ship
- [ ] Commit/push, /review + fix, pull main, PR + merge, cleanup worktree/branch/tracker,
      card Done + comment, follow-ups, overview
