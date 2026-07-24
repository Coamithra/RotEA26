# Net: close the standing-purge-filter races against the puppet layer

Card `74403f83` (follow-up from `9009a1c4` / PR #149). Branch `fix/net-purge-filter-races`.

## Context

`Game1.UpdateInner` runs the net rx drain **after** the component update phase, in the same tick:

```
TopOfTickFlush()      Game1.cs:1017   <- clears pendingPurges
base.Update()         Game1.cs:1028   <- GameScene.UpdateWin / UpdateResetting / Terminate purge here
NetSession.Update()   Game1.cs:1038   <- rx drain: puppet spawns, remote-ship spawn, replicated banners
```

`ComponentBin.Purge<T>(standing: true)` arms `pendingPurges` until the *next* tick's
`TopOfTickFlush`, and `ComponentBin.Add` diverts any matching add to the recycle pool
(`ComponentBin.cs:342`). So every add the net layer makes in `NetSession.Update` sits inside the
live window of any purge armed earlier in the same tick.

The purge sites that overlap the net layer:

| Site | Types purged (standing) |
|---|---|
| `GameScene.UpdateWin` :1085 | `AlienDrawableGameComponent` |
| `GameScene.UpdateResetting` :1116-1118 | `AlienDrawableGameComponent`, `AnimatedMessage`, `TutorialMessage` |
| `GameScene.Terminate` :1211-1213 | same three |
| `GameScene.LoseLife` :284-296 / `NetApplyReset` :319-320 | `PlayerShip`, `PlayerShipSummon` |
| `BrainBoss.KilledBy` :533-539 | `EvilBullet` `Braineroid` `EvilSkull` `StarMine` `UFO` `Lazer` `PlasmaBall` |
| `ClassicBoss` :195 / `InsaneBossI` :269 | `EvilBullet` / `Ball` |

`NetApplyReset` is the sharpest one: it purges from *inside* the rx drain, so anything the drain
adds after it — in the very same `NetSession.Update` call — is eaten.

The card reasoned these from the code and explicitly did **not** observe them; the `?binlog`
detector was armed for a full two-tab session and never fired for a real game type. Everything
below was re-confirmed by reading the current code (line numbers had drifted from the card).

## The races

**R1 — puppet eaten (silent, permanent).** `NetPuppets.OnSpawn` sets `constructing` so the add
clears `SuppressWorldSpawn` (`ComponentBin.cs:333`), but it is **not** exempt from
`IsPendingPurged` (`:342`). A puppet built in the window is diverted to the recycle pool, yet
`OnSpawn` registers it in `byId`/`idByComp`/`live` unconditionally (`NetPuppets.cs:171-173`).
Result: a ghost — never drawn, never collidable, and because the id **is** in `byId`,
`OnSnapshotEntry`'s self-heal (`:184-195`) never rebuilds it and `snapUnk` never climbs. Nothing
recovers it short of an `EvDeath`.

**R2 — remote ship eaten (permanent invisible player).** `SpawnPuppet` does `bin.Add(ship)`
(`NetSession.cs:2160`) then `puppet = ship` (`:2161`) unconditionally. A diverted add leaves
`puppet` non-null pointing at a ship outside the world, and the retry guard is `puppet == null`
(`:2131`) — so the remote player is invisible for the rest of the session.

**R2b — friend/couch ship eaten. Not in the card.** `NetSession.Friends.cs:219-220` has the
identical shape (`bin.Add(ship)` then `ch.Puppet = ship`). Its retry guard is the same
`ch.Puppet == null` shape, so a diverted add strands that couch player the same way. Worth more
than R2 in practice: couch players hit resets constantly (per web `CLAUDE.md`), and a reset is
exactly what arms `Purge<PlayerShip>`.

**R3 — replicated banners. Analysed and NOT a bug; see below.**

## Design

### R1 — exempt the puppet layer from the filter, and make a ghost impossible

Two changes, belt and braces:

1. `ComponentBin.Add` skips `IsPendingPurged` when `NetPuppets.Constructing` — directly
   symmetric with the `SuppressWorldSpawn` exemption immediately above it, and using a flag
   that already exists and is already public.
2. `NetPuppets.OnSpawn` verifies the add actually landed before registering. If it did not, it
   calls `MarkRemoved(netId)` and returns false — the exact path the "descriptor declined" case
   already takes (`:150-154`), so the snapshot self-heal retries after the suppression window
   instead of the registry holding a ghost.

(1) is the fix; (2) makes the *class* of bug unreachable even if a future purge path sidesteps
the exemption. Without (2) the registry can still be poisoned; without (1) every spawn in the
window would bounce through the 3s self-heal delay.

**Why the exemption is safe at scene teardown.** The obvious hazard is a puppet added to a
terminating scene, orphaning like the `NetWaitOverlay` bug web `CLAUDE.md` documents. It cannot
happen: `EvSpawn` (`NetSession.cs:1743`) and the snapshot path (`:1693`) are both gated on
`GameScene.NetActiveScene != null`, and `Terminate` nulls `NetActiveScene` at `:1209`
**before** its purges at `:1211-1213`. So during `Terminate` the puppet layer is already
switched off. The live windows are `UpdateWin` / `UpdateResetting` / the boss purges, where the
scene is genuinely up and the host's world is authoritative — precisely where the puppet must
survive.

### R2 / R2b — verify the add landed before adopting

New `ComponentBin.TryAdd(GameComponent) : bool` — performs the normal `Add`, then reports whether
the component actually ended up in `Game.Components`. Both spawn sites use it and only adopt on
true:

- `NetSession.SpawnPuppet` — on false, leave `puppet` null so the existing `puppet == null`
  retry re-fires next tick, when the filter has expired.
- `NetSession.Friends.DriveFriendShip` — same, for `ch.Puppet`.

Note the ship here **should** be purged: a reset wipes all ships and `SpawnAllPlayers` respawns
every seated slot, puppet slots included. The bug is adopting a ship that isn't in the world, not
the purge itself — so verify-and-retry is right and exempting would be wrong.

The oracle seat taken just above (`AddPlayerAt`) is left in place on the retry path: the next
call's `DeviceIsPlaying(Remote)` short-circuits to `GetPlayerIndex`, so the seat is reused, not
re-allocated.

### R3 — replicated banners: correct as-is, documented not changed

Both sites (`NetSession.cs:1854`, `:1889`) are already gated on `GameScene.NetActiveScene != null`,
which rules out the `Terminate` window entirely. That leaves `UpdateWin` and `UpdateResetting` —
and in both, eating the banner is what **matches the host**:

- The level script is host-only and only runs in `GameState.Normal` (`eventList.Update` is
  skipped otherwise), so the host cannot emit a beat while it is in Win or Resetting.
- Both peers enter Win (`EvVictory`) and Resetting (`EvReset`) from the host's own broadcast,
  and each purges its own banners at its own mark. A banner the host is showing is a banner the
  host has not purged.

So triggering R3 needs the two state machines to already have diverged — which would be a
different bug, and one this change could only mask. Unlike R1/R2 there is no corrupt state: a
banner is one-shot and self-expiring, and nothing holds a reference past the `Add`. Changing it
would be a speculative behaviour change to the visible playfield with no reproducing case, which
is the opposite of what the repo's verification rule asks for.

Action: a short comment at both sites recording the analysis, so the next reader doesn't re-derive
it. `?binlog` already prints `[bin] purge-filter diverted AnimatedMessage` if it ever does fire.

## Verification

`plans/net-headless-sim.md` (the card's suggested rig) is **design-only** and a medium-large card
of its own — it needs a de-static refactor plus an `INetHost`/`INetEntity` seam. Not this card.

The right rig is the one the repo already has for exactly this policy: `Compat/BinTest.cs` /
`eaBinTest()` — a scripted scenario suite that drives the **real** code synchronously in the
browser and prints PASS/FAIL. Its header states the rule explicitly ("written in place of an
offline sim on purpose … the policy under test IS ComponentBin.cs — a mirror would drift and
prove nothing"). Same reasoning applies here, so the suite is extended rather than mirrored.

New scenarios (`eaBinTest()`, run from the main menu):

1. **R1 end-to-end through the real puppet path.** `NetPuppets.Enable(game)` (it needs only a
   `Game` + the ServiceHelper bin/score — no transport, no session), arm
   `bin.Purge<AlienDrawableGameComponent>()`, call the real `NetPuppets.OnSpawn(...)` with a real
   descriptor, and assert the puppet **is** in `Game.Components` and `LiveCount` counted it.
   *This scenario fails on the current code and passes after the fix* — the reproducing case the
   card said was missing.
2. ~~**R1 no-ghost invariant.** Same setup with the exemption defeated…~~ **DROPPED — deviation
   from this plan, recorded deliberately.** With `Constructing` set there is no longer any divert
   path to trigger, so the scenario could only be written by adding a test-only seam to defeat
   the exemption — more production surface than the branch it guards. What shipped instead: the
   weaker but honest invariant (`registry agrees with the world`, scenario 3 below) plus an
   unconditional `[net] puppet add was diverted by the bin` log on the `!landed` branch, so the
   defence-in-depth path is *observable* rather than *asserted*. Stated as an untested branch in
   the code comment and in the card's closing note.
3. **R2/R2b primitive.** `TryAdd` returns **false** under an armed filter and **true** once the
   filter has expired at `TopOfTickFlush` — the exact contract both spawn sites now branch on.
4. Existing scenarios must keep passing (the filter still eats ordinary late spawns —
   `eaBinTest` scenario 3 is the regression guard that the exemption did not punch a hole in the
   filter for normal game code).

Leave-no-trace discipline as in the existing suite: `NetPuppets.Disable()` afterwards and
`PruneIdle` every scratch component.

**Honest limit, stated up front:** scenario 3 proves the primitive both R2 sites branch on, not
the two call sites end-to-end — driving `SpawnPuppet` for real needs a paired session with a
granted peer slot and buffered samples, which is the headless-sim card. The call-site change is a
two-line guard on that primitive and is reviewed as such. Timing a one-tick race in a live
two-window session is explicitly what the repo's verification rules rule out.

Plus the standard gate: clean `dotnet build -c Debug`, real-Chrome smoke with zero console
exceptions, and a two-tab `?net=` session with `?binlog` to confirm nothing regressed.

## Out of scope

- The headless two-peer sim (`plans/net-headless-sim.md`) — its own card.
- The boss purge sites (`BrainBoss`/`ClassicBoss`/`InsaneBossI`). They arm the filter for
  replicable enemy types, so R1's exemption covers them for free; no per-site change needed.
- Any change to the standing filter's behaviour for ordinary game code.
- `Explosion` and other cosmetic adds diverted during a purge — cosmetics are never replicated,
  nothing holds a reference, and a wipe clearing in-flight explosions is the intended behaviour.
