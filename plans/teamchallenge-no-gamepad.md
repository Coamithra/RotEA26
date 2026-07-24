# TeamChallenge without a gamepad (card `e6927ef8`)

## Context

`TeamChallenge.Initialize` seats the second player slot as `ControlDevice.PadOne`
**unconditionally** (inherited verbatim from the 2008 Xbox build, which seated
`Keyboard` + `PadOne`). `GameScene.Update` raises `pauseRequested` on every tick that a
seated pad device reads `!InputHandler.PadConnected(i)` — the disconnected-controller
guard. On this port, with no gamepad attached, that fires forever: the world is pushed
into the pause menu, the player dismisses it, it re-pauses on the next tick. Measured
with the AI driving: `ticks=0`, `prog=2/52` over 37 sim-seconds — the world never
advanced at all.

So a shipped level is unplayable for a keyboard-only player, and the failure mode is a
silent pause loop rather than an explanation.

Research findings that shape the fix:

- **This is the only unconditional pad seat in the game.** Every other seat comes from
  `GameScene.CheckPlayerJoins`, which requires a real pad `Start` press — so the pad is
  connected by construction. The sibling hole in `MenuScene`'s mouse-click device picker
  (enum default `(ControlDevice)0` == `PadOne`) was already closed with an explicit
  `else … Keyboard` at both sites. Nothing else to fix elsewhere.
- **Gamepads do work on this port** (KNI ships `_content/nkast.Wasm.Dom/js/Gamepad.8.0.5.js`),
  so a pad owner's experience is the original two-human co-op and must not change.
- **`ControlDevice.Generic` cannot be the fix on its own.** It is a first-class local
  device everywhere *except* `PlayerShip.Update` (menus can start with it,
  `CheckPlayerJoins` seats it on `Generic_Start`, the pause guard handles it,
  `PlayerSettingsMenu` correctly ignores it) — but **no key produces
  `MyKeys.Generic_Start` on this port**: `InputHandler.keysToCheck[8]` is an empty array,
  so the device is unreachable outside `eaPress`. Giving it a driver means also giving it
  a key map, i.e. designing a whole second-keyboard-player device. That is the follow-up
  card, not this one.
- **The shipped AI can already fly this level.** The completion matrix (card 9391f95a)
  ran TeamChallenge with both ships on the AI branch: `ticks=1682 shots=1029 prog=6/52`
  in a short soak, and a full `?invuln` control run was a VICTORY in 402s.
- Offline the tether is RIGID (`ShipConnector.Update` pins both ships to the midpoint
  ±39px), so the pair flies as a dumbbell: each ship's own thrust contributes and either
  ship alone can drag the pair. A partner that thrusts and shoots is all the level needs.

## Decision (user, this session)

Seat the partner from **what is actually plugged in**, with an **auto-pilot AI partner**
as the fallback; the full second-keyboard-player device becomes a follow-up card.

## Design

### 1. `Game/EvilAliens/TeamChallenge.cs` — resolve the partner seat

Replace the unconditional `AddPlayer(PadOne)` (and the `?aiteam` Generic branch) with:

```
first CONNECTED pad (0..3)  ->  PadOne..PadFour   (unchanged two-human co-op)
nothing connected           ->  ControlDevice.AI  (auto-pilot partner)
```

The net-session branch (`if (!NetSession.Active)`) is untouched: online, the partner is
the remote peer and no local second device is seated at all.

The decision lives in a **pure** `internal static ControlDevice ResolvePartnerSeat(
Func<int,bool> padConnected, DebugFlags.TeamPartnerSeat forced)` — the `OwnsSlotCore` /
`AiBench.Row` idiom — so the console self-test can table-drive every case instead of
needing a live level and four physical gamepads.

Invariant it guarantees: **the resolved device is never a pad that is not connected**
(unless explicitly forced by the debug flag). That is exactly the precondition of
`GameScene.Update`'s force-pause, so the pause loop becomes unreachable by construction.

`GameScene.Update`'s guard itself is **left alone**: for a pad that vanishes *mid-run* it
is correct and wanted behaviour (tell the player their controller died).

### 2. `?teampartner=ai|pad` replaces `?aiteam`

`?aiteam` (seat `Generic`) existed only because the level could not be benched at all; it
is obsolete the moment the no-pad seat is a driving AI — `?level=TeamChallenge&aiplayer`
now benches with no special flag. It is removed (flag, `Active` bit, log field) and
replaced by an A/B override:

- `?teampartner=ai` — force the AI partner even with a pad connected.
- `?teampartner=pad` — force `PadOne` with nothing connected, i.e. **reproduce the bug**;
  the only deliberate way to reach the disconnected-pad force-pause.

In `DebugFlags.Active` (it changes the shared run, same reasoning `AiTeam` was in).
`wwwroot/index.html`'s `eaAiBench.matrix()` drops its `&aiteam` for TeamChallenge.

### 3. Tell the player — `MenuScene` carousel briefing

`"Fly the new MX2 Dual Pilot Vessel to victory!\nRequires two players"` →
mentions the auto-pilot partner, so the fallback is stated where the player decides to
launch. Deliberately NOT an in-level banner: a banner added during `GameState.Startup` is
eaten by `UpdateStartup`'s 1300ms `Purge<AnimatedMessage>(standing: false)`, and one added
in `Normal` collides with the script's own "Get ready!" beat. (An in-level line is a
possible follow-up if the briefing proves too easy to miss.)

### 4. Verification seam — `eaTeamSeat()`

New `Compat/TeamSeatTest.cs`, wired through `DebugInput` ([JSInvokable]) →
`window.eaTeamSeat()` in `index.html`. It drives the real `ResolvePartnerSeat` over
**all 16 pad-connection masks × 3 override values** and asserts:

1. the result is never an absent pad (except under `?teampartner=pad`);
2. a connected pad is always preferred over the AI, lowest index first;
3. **negative control** — the OLD policy (always `PadOne`) is run over the same table and
   must FAIL property 1 in the 8 masks where pad 0 is absent. Per the `eaNetScore.test()`
   rule: a green tick means nothing unless the same input is shown to break the old
   behaviour.

It also prints the live pad mask and, if a level is up, the seated roster — so the later
live pass is one console call.

## Verification

Per the user's instruction this session (**no live browser testing -- they are overwatching**).
The decision itself did NOT have to be deferred:

- **`dotnet run --project tools/sim/logic_probe -- web/EvilAliensWeb/bin/Debug/net8.0` -> ALL
  PASS.** New tool (this card): it `AssemblyLoadContext`-loads the built `EvilAliensWeb.dll` into
  the desktop CLR and calls the REAL `TeamChallenge.ResolvePartnerSeat` over all 16 pad-connection
  masks x 3 `?teampartner` values, through `TeamSeatTest`'s own private `Expected` /
  `WouldForcePause` helpers (so the browser test's table is executed too, not just the resolver).
  Results: `none` and `ai` seat a present device in 16/16 masks; `pad` seats an absent one in
  exactly 1 (the no-pad mask, by design); resolution matches the spec in 48/48 cases; and the
  negative control -- the pre-card always-`PadOne` policy -- force-pauses in 8/16 masks, i.e. the
  bug reproduced.
- **Mutation-tested, so the green run means something:** changing `if (padConnected(i))` to
  `if (true)` turned 7 PASS lines into 4 FAIL (`mask 0000 -> PadOne, expected AI`) while leaving
  the `?teampartner=ai` legs correctly green. Reverted, re-run, green.
- clean `dotnet build web/EvilAliensWeb -c Debug` (0 errors; 37 pre-existing warnings unchanged);
- diff spot-check per `CONTRIBUTING.md` (no `content/` casing slip, no `BlendState` change, no
  codegen re-run, no csproj/trim change);
- `/review` pass on the branch diff.

**Deferred to a live session (explicitly unproven here):**

- the WIRING, which the probe cannot see: that a boot actually reads `PadConnected`, calls the
  resolver and seats the result -- i.e. boot `?level=TeamChallenge` with no pad and confirm the
  level plays instead of pause-looping;
- that the AI partner flies + fires acceptably under the RIGID offline tether (the matrix evidence
  is from a run where the partner was inert, so this is genuinely new behaviour to look at);
- `eaTeamSeat()` printing all-PASS in the browser (its part-3 live-state section touches
  `ServiceHelper` and so cannot run in the probe);
- `?teampartner=pad` reproducing the pause loop live;
- the carousel briefing's three lines fitting the panel;
- `eaAiBench.matrix()` on TeamChallenge without `?aiteam` -> non-zero `ticks`/`prog`, and a
  re-measure of that stale matrix row;
- zero console exceptions.

## Out of scope (follow-up cards)

- **The second keyboard player** (`ControlDevice.Generic` key map + `PlayerShip.Update`
  case + a `Generic_Start` binding): real keyboard local co-op on *every* level, which
  this port has never had. The card's option 2.
- A pad player joining mid-level takes slot 2 and flies **untethered** (the connector
  links `GetShips()[0]`/`[1]`, i.e. the AI keeps the tether); it does not displace the
  auto-pilot partner.
- Achievement/unlock policy: finishing with an auto-pilot partner still counts —
  `Settings.CheckForCheats()` reads the Settings cheat flags only, and this seats AI
  directly (as `Demo1/2/3` do) without touching `Settings.Friends`. Deliberate: the
  alternative denies keyboard-only players the unlock entirely.
