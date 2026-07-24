# Net: verify a full reset with couch players aboard

Card `af0eb00a` · follow-up to `4d904410` (PR #153) · branch `feature/net-reset-couch-verify`

## Context

Card 4d904410 unified the slot model so couch (local) players and online players coexist.
Everything was verified with the two-tab `?netlocal` rig **except a full death/checkpoint
reset while couch players are seated** — reaching it needs every ship dead at once, which
four `?aiplayer`-driven ships rarely manage, so it was never observed firing.

The path at issue is `NetSession.Friends`' **adopt** logic: after a reset,
`GameScene.SpawnAllPlayers` respawns *every seated slot* — puppet slots included — and
`SpawnFriend`/`DriveFriendShip` must ADOPT that scene-spawned ship rather than spawn a
second one or freeze on the spawn pose.

## Research findings (what already exists vs. what's missing)

The card proposes building "a debug flag that kills all live ships on command (e.g.
`?netkillall` or an `eaNet` console hook)". **That tool already exists** — `eaKillShips()`
(`Compat/DebugInput.cs:262`, JS bridge `index.html:189`) landed with the ComponentBin card
(02d9ad67, commits `8e3f4ef`/`669f89f`) and asplodes every locally-owned `PlayerShip`
through the real `Asplode()`→`Die()` path, skipping `Remote`/`RemoteFriend` puppets. The
web `CLAUDE.md` already prescribes it for exactly this: *"For a death/reset, KEEP `?invuln`
on both and call `eaKillShips()` in each console."*

**The "simultaneously" premise is weaker than the card assumes**, which is what makes this
tractable with no new kill tooling:

- `Oracle.AllShipsDead => playerShips.Count == 0` (`Oracle.cs:65`) — *all* ships, puppets
  included.
- Nothing respawns until `AllShipsDead` fires, so **dead ships stay dead**. Firing
  `eaKillShips()` in one console and then the other is sufficient; the two kills need not
  land in the same frame.
- After the second tab fires, each peer's puppets die via the existing paths — the primary
  remote on the `alive=false` flag edge, the couch puppet on the 500 ms
  `FriendTimeoutMs` (`NetSession.Friends.cs:46`) — and `AllShipsDead` then trips
  `LoseLife` (`GameScene.cs:1191`).

Traced reset → adopt sequence (this is what the run must confirm):

1. `LoseLife` (host) purges `PlayerShip`, sets `Resetting`, broadcasts `EvReset`;
   `NetApplyReset` mirrors it on the client (`GameScene.cs:288`/`317`).
2. The friend channel loses its puppet — either `TickFriends`' scene-purge check
   (`Friends.cs:134`) nulls it, or the 500 ms timeout runs `ExplodeFriend`, which also
   does `friendChannels.Remove(slot)`. **The oracle seat is deliberately kept** either way.
3. 1300 ms into `Resetting`, `SpawnAllPlayers` walks every *seated* slot and respawns the
   puppet seat too (`GameScene.cs:1413`).
4. Re-adoption has two paths, and the reset uses the second:
   - `DriveFriendShip` adopts by slot when the channel still exists but lost its `Puppet`
     ref (`Friends.cs:265`);
   - once the channel was removed by `ExplodeFriend`, the peer's resumed stream recreates
     it and `SpawnFriend` adopts `oracle.GetPlayerShip(slot)` (`Friends.cs:206`).

**Duplication is guarded, but only by one line**: `SpawnAllPlayers` skips a slot when
`oracle.IsAlive(i)` (`Oracle.cs:366`, scans live ships by `Owner`). So if the peer's stream
resumes *before* the local `SpawnAllPlayers` runs, `SpawnFriend` creates the ship and
`SpawnAllPlayers` correctly skips the seat. This ordering is the corner the card wants
proven, and it is order-dependent — worth observing rather than reasoning about.

`RejectFull` needs **no new tooling** either: `?net=host&netlocal=3` seats host primary +
3 couch players before any peer pairs (`AllocateSeat` only skips `localPrimarySlot` and a
not-yet-assigned `peerPrimarySlot`), so a later joiner hits `FirstFreeSlot(1) < 0` →
`SendRejectOnce(RejectFull)` (`NetSession.cs:1187-1195`).

**The one genuine tooling gap is `ExpireUnclaimedGrants`** (`NetSession.cs:1332`): it
releases a seat the host granted but the peer never streamed into. `?netlocal` always
*takes* its grant, so the expiry path has no trigger at all.

## Design

Deliberately small — this is a verification card, and three of its four paths are already
reachable with shipped flags. Two narrow seams, then the runs.

### 1. `?netdropgrant` — the missing grant-expiry trigger (`DebugFlags.cs` + `NetSession.cs`)

A client-side flag: the next `EvSlotGrant` the client receives is acknowledged as pending
and then **deliberately dropped** instead of seated, exactly as a real failed take does
(device got seated meanwhile, scene changed). Implemented as an early return in
`HandleSlotGrant` after clearing `joinRequestPending`, logging
`[net] ?netdropgrant: dropping granted couch slot=<n>`.

Host side then has a granted seat nobody streams into → `ExpireUnclaimedGrants` releases it
after `GrantClaimTimeoutMs` and prints the existing
`[net] released unclaimed couch grant slot=<n>`. Asserted from the console + the seat
reappearing in `roster=`.

### 2. `eaNetRoster()` — on-demand roster dump (`DebugInput.cs` + `index.html`)

The `[net]` line only prints every 5 s, but the assertion the card asks for is
*before-vs-after a reset* — a 5 s cadence straddles the whole ~2.7 s reset and can miss the
transition entirely. `eaNetRoster()` prints the existing `RosterReport()` string (plus
`resets=`) on demand, so the before/after pair is exact rather than sampled. Mirrors
`eaNetBg()`, which exists for the same reason.

No new state: it calls the existing `RosterReport()` and reads `metrics.Resets`.

### 3. The verification runs

All in two side-by-side Chrome windows (both foreground — a backgrounded tab's rAF drops to
~1 Hz), fresh `?room=` per pair, `Level2` (Level1 hands the ship spawn to a script beat).

**Run A — full reset with couch players aboard (the card's headline):**
```
:5289/?level=Level2&net=host&aiplayer&invuln&netlocal=1&room=<r>
:5289/?level=Level2&net=join&aiplayer&invuln&netlocal=1&room=<r>
```
Wait for a 4-slot mirror-image roster, `eaNetRoster()` in both → `eaKillShips()` in both →
`eaNetRoster()` in both again once play resumes.

Assertions:
- `resets` increments by exactly 1 on **both** peers;
- `roster=` (the slot→device seat map) is **identical before and after** on both peers, and
  still mirror-image across them;
- `ships=` returns to 4 entries with owners matching the seats — **no missing slot**
  (frozen/never-adopted puppet) and **no duplicate owner** (double spawn);
- both puppets visibly move after the reset (adopted, not parked on the spawn pose);
- `drop`/`sgap`/`ordViol`/`seqGap` stay 0; zero console exceptions.

**Run B — granted-seat expiry:** same pair, client adds `&netdropgrant`. Assert the host
logs the grant, then `released unclaimed couch grant slot=`, and that `roster=` loses the
reserved seat instead of leaking it.

**Run C — roster-full reject:** `?net=host&netlocal=3` (roster fills to 4) + a later
`?net=join`. Assert the host logs `no free roster slot for the joiner -- rejecting` and the
joiner surfaces the `RejectFull` notice, with the graceful `RejectGraceMs` teardown (not a
bare channel close).

## Out of scope

- Changing the adopt logic itself. This card **verifies**; a defect it surfaces becomes a
  fix here only if small, otherwise a follow-up card.
- The `?netlocal` couch-join mechanism, the reset state machine, and `eaKillShips` — all
  shipped and unchanged.
- Mid-boss puppet fidelity, TURN, interpolation feel (other cards).

## Verification of the tooling itself

`?netdropgrant` and `eaNetRoster()` are debug-only seams; the gate is that a **normal boot
is byte-identical** (both are `DebugFlags`-gated / console-only) plus a clean Debug build
and zero console exceptions in the runs above.
