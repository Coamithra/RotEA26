# `?harnessrun` — let the sprite harness UNFREEZE the object under test (card d1ee8761)

## Context

`?harness=<key>` freezes the object under test. That freeze is the harness' entire value: nothing changes between frames, so a screenshot at any moment is identical. But it makes the harness useless for any component whose `Update` **is** the thing under test — you can park a phase and photograph it, and you cannot watch it run.

Card 045c5a92 hit this and built a one-off: a second registry key, `["respawnrun"]`, constructing the same `PlayerShipSummon` as `["respawn"]`, with `HarnessScene` special-casing that literal string. This card generalises it into one seam and deletes the duplicate.

## Design

**A separate `?harnessrun` flag, not a `<key>run` naming convention.** The card offered both; they are not equivalent. A suffix convention collides silently with any future registry key that legitimately ends in `run` — and `HarnessRegistry`'s stated design is "add an object in ONE line", so it will grow. The failure mode is the worst kind: the harness draws a plausible frozen object while you believe it is running. A flag composes with every key including ones added later, keeps `HarnessRegistry.Names` and the unknown-key error caption clean, and gives `harness.html` one checkbox instead of a duplicate `<option>` per runnable key.

**"Unfrozen" is ONE bit with THREE enforcement sites.** This is the part that would have half-worked silently:

1. the initial `obj.Enabled = false` in `Initialize`;
2. the defensive per-frame re-assert in `Update`;
3. the `Update` dispatch chain, whose frozen path re-parks `obj.Position` and overwrites `obj.curframe` every frame.

Miss (3) and the object ticks and is then dragged back. The old code gated all three off `harnessSummon != null`; the new `harnessRun` bool gates the same three explicitly. Nothing on the run path was ever type-specific — the field only ever took part in `!= null` tests — which is why a plain `bool` replaces a `PlayerShipSummon` reference and the seam applies to every key.

**Mutually exclusive with the phase scrubbers and `?play`**, which are by definition alternative drivers. Under `?harnessrun` the blast and spiderjump scrubbers are not installed and `?play` does not step `curframe`.

**The resolution is printed, not silently applied.** `HarnessScene.ReportMode()` prints `[harness] <key>: frozen …` / `[harness] <key>: RUNNING …` once, naming anything it overrode and why. The `[debug]` dump reports only the *parse*; a run that believed it was unfrozen but was frozen produces a plausible, wrong table — the `[aiwallnav] steering:` rule. The on-screen label gains `(RUNNING)` so a screenshot says so too.

## Files

| File | Change |
|---|---|
| `Compat/DebugFlags.cs` | `HarnessRun` property, `case "harnessrun"`, ` run` in the `[debug]` harness segment, and a post-parse notice when the flag is set with no `?harness=` (checked after parsing, since flags are parsed in URL order) |
| `Compat/HarnessScene.cs` | `harnessSummon` → `harnessRun`; the three enforcement sites; scrubber suppression; `ReportMode()`; label; header comment |
| `Compat/HarnessRegistry.cs` | `["respawnrun"]` deleted; its shared-fate knowledge folded into `["respawn"]` |
| `wwwroot/harness.html` | `respawnrun` option removed; "Run it live" checkbox added |
| `tools/headless/probes/respawn_reward_level.txt` | retargeted to `?harness=respawn&harnessrun` |
| `tools/headless/probes/harness_run{,_absent,_generic,_generic_absent}.txt` | new; two pairs |
| `tools/headless/probes/README.md`, root + web `CLAUDE.md` | docs |

## Verification

- **The frozen default, byte-identical.** `verify_il_identical.py` does **not** apply — the assembly changes by construction. Instead: a PNG A/B of six un-flagged harness modes (`spider` frozen, `spider&play&fps=4`, `blast` scrubber, `spiderjump&spiderphase=0.4`, `respawn&respawnphase=0.5`, `battleskull`) between this branch and `main`, each captured **twice per side with the same-side pair required to match first**. All six: controls established, sides byte-identical. `?bg=holodeck` on every case, because the space background's star field comes off a separate deliberately-unseeded RNG that `?seed=` does not reach — without that the control leg fails for a reason unrelated to this card.
- **Scoping.** `verify_decompiled_diff.py --ref main` reports exactly ten members, all deliberately edited, no others.
- **The seam.** Two committed probe pairs, all four mutation-tested against four source mutations (flag forced false, flag forced true, per-frame re-assert restored, dispatch chain re-parking restored). Every prediction held, and the matrix showed the generic pair is the **only** one that catches enforcement site 3 — a respawn summon never moves.
- **Regression.** 68/68 probes, clean Debug build with no new warnings.
- **Browser.** Foreground Chrome on the worktree's own dev server: `harness.html` is edited JS and the WASM/JS layer is invisible to `eahl`.

## Out of scope

New registry entries; making the phase scrubbers driveable *while* running; merging `?play` with run; any `Game/` change; `.claude/launch.json`.
