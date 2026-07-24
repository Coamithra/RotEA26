# AI: difficulty-scaled skill

Card `c10e3e7f`. Follow-up to `f4d1721f` ("Improve AI"), which deliberately left the bot
difficulty-blind.

## Context

One bot drives three things: the **attract demos**, the **Mechanical Friends** cheat and
`?aiplayer`. It has exactly one set of reaction/steering constants
(`PlayerShip.Default*`), so it plays identically whatever tier is in play.

The knobs already exist as baked `Default*` consts with nullable `?ai*` overrides, so this
is a per-tier value table plus a lookup, not new mechanism.

## Research findings that constrain the design

1. **`Settings.CurrentDifficulty` is the WRONG key.** `Demo1/2/3.Initialize` call
   `LockDifficulty(DifficultyLevel.Hard)` and `TutorialLevel` locks `Very_Hard`.
   `LockDifficulty` only redirects `DifficultyModifier` (what the *enemies* scale by);
   `CurrentDifficulty` keeps returning the player's menu choice. Keying AI skill off
   `CurrentDifficulty` would put an Easy-tier pilot against a Hard-tier attract demo
   whenever the player's saved setting is Easy — incoherent, and invisible until someone
   changed their menu setting and wondered why the demo got worse.
   → add **`Settings.EffectiveDifficulty`** = `_difficultyLocked ? _difficultyLockedAt :
   _difficultyLevel`, and key off that. The demos then get the Hard row, matching the
   enemies they actually face.
2. **`DifficultyModifier` is also wrong** — it ramps with elapsed play time and adapts on
   death. A bot that silently got smarter the longer a run went on would make every bench
   number a function of run length, and `?aibench` is how this card is verified at all.
   The tier is static; use it.
3. **The tier values are** `Easy 0.35 / Medium 0.6 / Hard 0.8 / Very_Hard 1.0 / Inzane 1.2`
   (`Settings.GetDifficultyValue`). The AI table is indexed by the enum, not derived from
   these — enemy scaling and pilot skill are not the same curve.
4. Card `f4d1721f` measured its baked values **on Very Hard**. Whatever row inherits those
   numbers verbatim is the only row with a proven baseline.

## Design

### Mechanism

`PlayerShip.AiTuning[]`, one row per `DifficultyLevel` (Easy..Inzane), in the
`WebcamLevel.Tunings[]` idiom — **absolute final values, no divisor, no ramp**. Resolution
order stays:

```
?ai<knob>=   (explicit override, wins)
  else  AiTuning[Settings.EffectiveDifficulty].<knob>
```

The existing `Default*` consts stay and become the Very_Hard row's source, so there is one
place each number lives. A shipped build with no query string is unchanged **on the anchor
tier** (see the open question below) and byte-identical in structure elsewhere.

### Knobs that scale, and why each reads as *skill*

| Knob | Today | Skill reading when degraded |
|---|---|---|
| `WallReactionMs` | 420 | how far ahead it sees a wall. Less warning = clips more. The headline `contacts` metric moves with it. |
| `ThreatLeadMs` | 700 | prediction horizon for fast movers. Less = dodges where the thing *is*, not where it's going. |
| `ThreatFieldBasePx` | 190 | personal space. Less = flies closer to things that kill it. |
| `aimSpread` (**new** `DefaultAimSpreadRad`, PI/12) | 15° | the most legible dial of all: a worse pilot misses. Currently a bare local in `DoAIFire`; promote it to a const + `?aiaim` for consistency with the other nine. |

### Knobs that deliberately do NOT scale

- **`SteerSmoothMs` / `SteerSmoothUrgentMs` / `ParkDemand`.** Jitter and idle fidget are the
  **bugs** card `f4d1721f` fixed (heading churn ~1050 deg/s → 70). Degrading these does not
  produce a worse *pilot*, it reproduces the defect — a vibrating ship reads as broken, not
  as novice, and would look like the last card regressed.
- **`PriorityTargetBias`.** Degrading it stops the bot prioritising the boss that *halts the
  level*. The failure mode is a demo that never progresses — strictly worse than one that
  plays badly.
- **`ThreatFieldSizeScale` / `ThreatFieldFalloff` / `GapSwitchMargin`.** Field *shape* and
  gap hysteresis, not competence; moving them mostly manufactures oscillation.

### Out of scope

- A reaction **latency** (buffering the steer by N ms) — the most human skill model, but new
  mechanism, and the card scopes this to picking values. Follow-up card if wanted.
- Bomb discipline (`doAIBomb` `minTargets`) — discrete, and bombs are scarce enough that the
  tier spread would be invisible in a bench run.
- Difficulty scaling for anything other than the AI.

### Notes

- `DoAIFire`'s aim jitter already draws from the **shared** game RNG. That is pre-existing;
  the AI is host-only in a net session, so nothing changes about desync exposure. Not
  "fixed" here (changing the RNG source is a behaviour change unrelated to this card).
- Net co-op: `EvLaunch` locks the client to the host's difficulty, and AI ships are
  host-only, so both peers resolve the same row by construction.

## Verification

Per the repo rule, **as data, never by watching it play**:

1. Clean `dotnet build -c Debug`.
2. `eaAiBench.soak()` headless runs per tier, driven by `?difficulty=<tier>` +
   `?aiplayer&aibench&invuln`, on the fast-boot fights that isolate each knob:
   - `?level=Level3&wallsonly` → `contacts` (the wall lookahead knob).
   - `?level=Level2&spiderboss` → `deaths` (the threat lead + field knobs).
   - `?level=Level1` → `prog` / verdict + shots (the aim knob).
3. **The gate is a MONOTONIC gradient across tiers**, not a single good number — Easy must
   measurably clip/die more than Inzane on the same fight, with the run long enough that the
   difference clears the noise floor. `f4d1721f` warns differences under ~30% on a single
   stochastic run are noise, so: repeat runs per tier, report the spread, and only claim a
   gradient that survives it.
4. **Anchor-tier no-regression check**: the anchor row must reproduce today's numbers within
   noise on the same fights (it is the same constants, so this is a wiring check — proving
   the lookup didn't silently swap a value).
5. Final smoke: real game boots, zero console exceptions, foreground Chrome, port 5281.

## Decisions (settled with the user)

**Anchor = Very_Hard.** Its row IS card `f4d1721f`'s constants verbatim, so the measured
configuration stays exactly where it was measured.

**Spread = SUBTLE, and that is a design constraint, not timidity.** The user's reason:
*"bots don't need to be noticeably bad at the game, then the whole reason for having
mechanical friends join you is moot"* — an AI teammate that visibly can't play is worse
than no scaling at all. So the gradient must be legible **on the bench** and hard to spot
**by eye**. In particular the attract demos (locked to Hard) sit one small step below
baseline, not in novice territory.

Practical consequence for verification: a subtle gradient is *below* the ~30% single-run
noise floor `f4d1721f` warns about, so the monotonicity claim **must** come from repeated
runs per tier, not one run each. Anything that only shows up once is not a result.

### The table AS SHIPPED (absolute final values; Very_Hard = today's `Default*` consts)

| | Easy | Medium | Hard | Very_Hard | Inzane |
|---|---|---|---|---|---|
| `ThreatFieldBasePx` | 150 | 163 | 176 | **190** | 200 |
| `AimSpreadRad` | PI/8 | PI/10 | PI/11 | **PI/12** | PI/16 |

Two further knobs were planned and **dropped after measurement** — see Results.

## Results

### The measurement problem, and what actually works

The plan's original gate ("soak each tier and compare") **cannot work, and that is not a
tooling limitation**: the enemies scale with the same tier the pilot does, and Level 3's
wall scroll speed is literally `0.43 * GetDifficultyValue(...)`. An outcome delta between
two tiers is therefore unattributable — it could be the pilot or the level.

Two things replace it:

1. **Mechanism, observed directly.** `eaAiBench()` gained a
   `skill effective=<tier> field= aim=` row reporting the RESOLVED values. This is the only
   non-confounded observation of the lookup, and it is exact rather than statistical.
2. **Effect, isolated.** Hold the tier (and so the whole level) fixed and move ONE `?ai*`
   override. That measures the pilot's contribution with everything else constant.

### Mechanism — PASS

All five rows resolve correctly (`?menu&noattract&aibench&difficulty=<tier>`):

| tier | field | aim |
|---|---|---|
| Easy | 150px | 22.5° |
| Medium | 163px | 18.0° |
| Hard | 176px | 16.4° |
| **Very_Hard** | **190px** | **15.0°** |
| Inzane | 200px | 11.2° |

Very_Hard reads back exactly the pre-existing constants — the anchor is intact.

**The attract-demo case, end to end:** booting `?menu&aibench&difficulty=Easy` shows
`effective=Easy` (150px / 22.5°) on the menu, and the instant `Demo1` starts it flips to
`effective=Hard` (176px / 16.4°). The bot flies the Hard row against the Hard-scaled
enemies instead of the Easy row the menu setting would have given. That is the whole reason
`EffectiveDifficulty` exists, demonstrated rather than argued.

### Effect — two knobs kept, two dropped

Each candidate isolated at a fixed `?difficulty=Very_Hard`:

| knob | isolation | result | verdict |
|---|---|---|---|
| `AimSpreadRad` | Level1, 15° → 57.3° | progress 50/64 → **45/64** | **kept** |
| `ThreatFieldBasePx` | spiderboss, 190 → 30px | deaths 11 → **14** | **kept** (weak) |
| `WallReactionMs` | wallsonly, 420 → 80ms | contacts 0 → 0, turn 22 → 18°/s, prog 7/8 → 7/8 | **dropped** |
| `ThreatLeadMs` | spiderboss, 700 → 80ms | deaths 11 → **10** | **dropped** |

The two dropped knobs moved *nothing* at a 5–9× degradation, so tiering them would have
shipped dials that do nothing. Their consts and `?aireact` / `?aithreatlead` overrides are
untouched from `f4d1721f`.

**Why `contacts` cannot see wall look-ahead:** `ClampIntoWallSpace` is a hard "do not fly
into that" override applied after the steering low-pass, and it runs regardless of how far
ahead the bot looked. It floors the contact count at 0 — confirmed by a positive control at
`?aireact=60`, which still scored 0 contacts and won the fight. So the metric was never
capable of grading that knob, which is why the original plan's headline gate was vacuous.

### Honest bound on the shipped behaviour change

At the shipped (deliberately subtle) spread the outcome difference is **below the noise
floor of every instrument available** — repeat baseline runs alone varied 11/13 deaths, and
even the 5–9× exaggerations above only reached 15. So: the tier lookup is proven exact, the
two retained knobs are proven to be real axes, and the shipped difference between Easy and
Inzane is real but small by design. It should not be claimed as a visible gameplay change.
