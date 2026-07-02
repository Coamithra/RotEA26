# Game juice — research → application (card: game juice / screen shake)

## The research (the canon)

Three sources define "juice" as it's practised:

1. **Jan Willem Nijman (Vlambeer), "The Art of Screenshake"** — live-codes ~30 tweaks
   onto a boring 2D shooter. The recurring moves: bigger/faster bullets, muzzle flash,
   weapon kickback, enemy hit-animations, **permanence** (corpses/shells stay), a camera
   that leads the player, **sleep frames on impact** (pause everything 2–3 frames when
   something dies), and — the namesake — **screenshake** on every meaningful event.
   His thesis: impact is communicated by making the WORLD react, not the numbers.
2. **Martin Jonasson & Petri Purho, "Juice it or lose it" (GDC Europe 2012)** — a breakout
   clone "juiced" live: tweening/easing on everything, particles, sound layering, color
   flashes, wobble. Thesis: juice = maximum output for minimum input; a juicy game gives
   continuous feedback for every action. Their caution (echoed later by the authors):
   juice is seasoning, not a substitute for mechanics — and it can be overdone.
3. **Squirrel Eiserloh, "Juicing Your Cameras With Math" (GDC)** — the now-standard
   **trauma model** for screenshake: events add *trauma* (0..1), shake strength =
   trauma² (or ³), offsets/roll sampled from noise each frame, trauma decays linearly.
   The nonlinearity makes small hits subtle and stacked big events dramatic; rotational
   shake reads "bigger" than translation for the same displacement.

## Audit — what this game already had vs missed

Already present (most of the canon, in fact — the 2008 game was well-juiced for XBLIG):
- Hit flash: `KillableAlien.Draw` lighten-shader blink (35ms) on every hit + red-shift
  as HP drops; player invuln blink.
- Particles: `Explosion`/`MiniExplosion`/`BloodExplosion` + smoke, additive blends.
- Rumble: `Vibrator` + `Explosion.Vibrate()` — distance-attenuated, per-player.
- Slow motion (1up powerup) + Stage-12-era **ghost-trail motion blur**, bloom preset swap.
- Floating text (score pops, "Power Up!", combo), chrome/metal sheen, event-driven glint.
- Sound: XACT-faithful mix + subtle per-cue pitch/volume humanize.

Missing — the two signature "impact" effects:
- **Screen shake** — nothing moved the camera, ever.
- **Hit-stop / sleep frames** — kills and deaths passed without a beat.

## What was added (`web/EvilAliensWeb/Compat/Juice.cs`)

**Screen shake, trauma model** (Eiserloh), applied at the present blit in `Game1.Draw`
(offset + small roll + slight zoom to keep the letterbox covered). Purely a camera
effect — gameplay coords, collision and mouse mapping untouched. Sources of trauma:

| Event | Trauma |
|---|---|
| Any `Explosion` (size 1 → 3.5) | 0.11 → 0.26 (`0.05 + size*0.06`) |
| Bomb `Blast` (power 1..5) | 0.25..0.45; mini-blast 0.08 |
| Any kill (`KillPunch`) | 0.05 (boss: 0.3) |
| Player death | 0.35 + its two explosions' ≈0.43 |

Strength = trauma² × `?shake=` multiplier; max 14 design px offset, 2° roll; decays in
~0.7s. One kill barely nudges; a bomb-cleared wave (stacked trauma) genuinely rattles —
that superlinearity is the point of the model.

**Hit-stop** (Nijman's sleep frames), folded into `Game1.Update`'s existing
turbo×slowmotion time scale (`Juice.TimeScale`, 0 while frozen; decremented on UNSCALED
real dt so it always thaws). Kills = ~1.5 frames (25ms) via `KillableAlien.HitBy`,
rate-limited (250ms cooldown) so kill chains read as one punch; boss kills 90ms
(cooldown-exempt); player death 180ms (`PlayerShip.Asplode`/`AsplodeWall`). Overlaps
take the max, never the sum. Draw-time cosmetics (Blast rim spin) keep animating during
a freeze — Draw receives raw time, which is the game's existing convention.

**Deliberately not done** (per the sources' own "don't over-season" warning + faithfulness
to the original): weapon kickback/knockback and camera-leads-player (both change gameplay
physics on a fixed-camera arena game), muzzle flash (art), per-shot shake (18 shots/sec
would be permanent rattle), extra permanence (perf on WASM).

## Tuning / QA

- URL: `?shake=<0..3>` (0 off, 1 default, >1 exaggerate), `?hitstop=0`.
- Console (anywhere, like `eaSlowmo`): `eaShake()` / `eaShake(1)` — shake burst;
  `eaHitstop()` / `eaHitstop(500)` — freeze N ms. Both wired via `Compat/DebugInput.cs`.
- Feel constants live at the top of `Compat/Juice.cs` (decay, max offset/roll, stop
  lengths, cooldown); trigger amounts at the call sites listed above.

Verified headless (Chromium + Playwright against `dotnet run`): shake displaces the
frame and decays back; hit-stop produces pixel-identical frames for its duration and
resumes exactly; zero console exceptions; `?shake=0`/`?hitstop=0` paths compile-time
default to the shipped feel.
