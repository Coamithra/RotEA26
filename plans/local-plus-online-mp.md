# Local multiplayer AND online multiplayer (card `4d904410`)

## Context

The card's scenario: I host, a remote player joins online, then someone on **my** couch picks up
a controller and presses Start, then someone on the **remote** couch does the same. Four ships,
two of them on each side of the wire.

Today the net layer is hard-wired to **exactly one local ship per peer**. Nothing stops a couch
player from joining (`GameScene.CheckPlayerJoins` runs unconditionally in `UpdateNormal`), so the
scenario is reachable — it just silently misbehaves. Verified by reading the code:

| # | What breaks | Where |
|---|---|---|
| 1 | Extra couch ships are **never streamed** — invisible on the other screen. `SendShipState` streams `FindLocalShip()` (the *first* non-Remote/non-AI ship); `SendFriendStates` only streams `Controller == AI`. | `NetSession.cs:535,567`, `NetSession.Friends.cs:57` |
| 2 | **Kill/score attribution collides.** Wire slots ≥2 pass through `TranslateSlot` untouched (identity), but both peers seat their couch players at their *own* next free slot — so the client's P2 and the host's P2 are both "slot 2". A client claim with `killerSlot=2` credits the **host's** P2. | `NetSession.cs:790,949` |
| 3 | **Bombs are mis-attributed.** `EvBlast` carries no slot; the receiver applies it to the primary puppet, so a couch player's bomb detonates on the peer's *primary* ship. | `NetSession.cs:581,1342` |
| 4 | **Score sync only carries slots 0 and 1** — couch players' scores never true up. | `NetSession.cs:935` |
| 5 | A client couch player **squats an AI-friend slot**: `SpawnFriend`'s `AddPlayerAt(2, RemoteFriend)` refuses, so that friend puppet never appears and retries forever. | `NetSession.Friends.cs:178` |
| 6 | `GameScene.SpawnPlayer` gives the new ship `Owner = oracle.Players - 1`, not the slot it was actually seated in — wrong whenever the slot table has a hole (a friend puppet died and freed a lower slot). | `GameScene.cs:978` |
| 7 | Latent, pre-existing: after a reset, `SpawnAllPlayers` re-spawns a `RemoteFriend` slot's ship itself, but `DriveFriendShip` only drives a ship it spawned (`ReferenceEquals(ch.Puppet, ship)`) and never adopts — so the puppet freezes. The primary remote path *does* adopt (`DriveRemoteShip`). Couch players hit resets constantly, so this stops being a corner case. | `NetSession.Friends.cs:215` vs `NetSession.cs:1736` |

No crash, no desync of the enemy world — the failure is "ghost ships and scrambled credit".

## Design

The engine of the fix is the **already-proven AI-friend path** — it is exactly the shape we need:
a slot-tagged, multi-ship stream driving per-slot puppets. What changes around it is the slot
model, which today is two hard-wired special cases bolted onto an identity-mapped tail.

**One slot model, everywhere: the oracle slot *is* the wire slot, and the host allocates it.**

Today wire slots 0/1 (the two primaries) are host-relative and mirrored per side by
`TranslateSlot`, while slots 2/3 (AI friends) are identity-mapped. That mirror only exists because
each peer seats *its own* player first. Once the host allocates every slot, the mirror is
unnecessary: **`TranslateSlot` and `ApplyJoinHues` are deleted**, and per-slot hues agree by
construction instead of by a compensating swap.

This is what the listing decision forces. With a couch game listable, a joiner can arrive at a
host whose slot 1 is already a couch player — so the remote primary can no longer be pinned to
wire slot 1, and no "reserve slot 1 for the online seat" rule survives contact with
`AllowOnlineJoins` being toggled mid-game. Host-allocated identity is the model that just works.

In the ordinary 1v1 lobby case the allocation is host = 0, client = 1 — i.e. **the visible result
is identical to today's post-`ApplyJoinHues` state** (host white, joiner purple).

### 1. Host is the slot allocator

- **Client's primary:** the host's `MsgWelcome` carries the client's granted slot (first free,
  normally 1). The client seats its primary there.
  - In the menu-lobby and join-in-progress flows the client seats its ship *after* the grant
    (`EvLaunch` → `Game1.MenuFinished`), so this is just "seat at slot N".
  - In the dev `?net=join` URL flow the client is already mid-level at slot 0 when it pairs, so it
    **re-seats live**: `Oracle.MovePlayerSlot(from, to)` + `PlayerShip.NetSetOwner(slot)` + hue
    re-apply. The host never moves.
- **Host couch join:** allocate the first free slot, seat with `AddPlayerAt`, spawn. No
  announcement needed — the stream itself creates the peer's puppet at that slot.
- **Client couch join:** the client sends reliable `EvJoinRequest`; the host allocates the first
  free slot, **immediately seats it as `RemoteFriend`** (which is what makes the allocator
  collision-proof — its own `AddPlayer(AI)` and any later grant see the slot as busy) and replies
  `EvSlotGrant(slot)`. The client seats + spawns on the grant; roster full → refusal byte, Start
  is a no-op.
- One reliable round-trip (~50–150 ms) before a client couch ship appears. Acceptable: joining is
  a discrete, rare action, and it is what keeps the roster collision-free.
- `CheckPlayerJoins` gains a net-session branch; offline behaviour is byte-identical.

### 1a. Sparse rosters become legal

Host allocation and mid-level departures both put **holes** in the slot table, which the game
currently cannot represent — three places assume slots are densely filled `0..Players-1`:

- `ScoreVisualiser` draws a score panel for `i < oracle.Players` and "Press Start" otherwise, so a
  hole hides a real player's score and shows an empty panel.
- `GameScene.SpawnAllPlayers` respawns `for i < oracle.Players`, so a seated high slot is skipped
  after a reset.
- `GameScene.SpawnPlayer` stamps `Owner = oracle.Players - 1` rather than the seated slot (#6).

All three switch to a new `Oracle.IsSeated(slot)` over the full `0..MaxPlayers-1` range. This is a
prerequisite, not a side quest — and it removes a latent class of bugs that already existed
whenever an AI-friend puppet died and freed a low slot.

### 2. Streams (`MsgFriendState` broadened, both directions)

`MsgFriendState` already carries a slot byte and drives per-slot buffers/puppets. Broaden it from
"host AI friends" to **"every locally-owned ship that isn't our primary"**, sent by *both* peers:

- `SendFriendStates`: filter changes from `Controller == AI` to "locally owned, not the primary".
- `HandleFriendState` / `TickFriends` / `DriveFriendShip`: drop the `isHost` early-returns.
- `SpawnFriend`: tolerate a slot this peer already seated as `RemoteFriend` (the grant reserves it).
- `DriveFriendShip`: **adopt** a scene-spawned ship by `ship.Owner` → channel slot, mirroring
  `DriveRemoteShip`. Fixes breakage #7.

`ControlDevice.RemoteFriend`'s meaning broadens to "network-driven extra ship" (AI friend *or* the
peer's couch player). The enum is append-only and unchanged.

### 3. Attribution fixes

- `EvBlast` gains a slot byte; the receiver applies it to that slot's ship. (#3)
- `EvScoreSync` widens from 2 to 4 slots. (#4)
- `GameScene.SpawnPlayer` takes the seated slot explicitly instead of `oracle.Players - 1`. (#6)
- `TranslateSlot` is **deleted** — every wire slot is already the local oracle slot. (#2)
- Protocol **v4 → v5** (hello/welcome version byte; the signaling server already filters listings
  by protocol version, so mismatched builds simply don't see each other).

### 3a. Listing eligibility opens up (approved)

`NetListing.ComputeEligible` returns `Players() == 1` today ("exactly one active player, slot 2
free"). It becomes **`Players() < Oracle.MaxPlayers`** — a couch game with a free seat can be
listed too. The same predicate still drives the listing, the beacon and the pause indicator, so
they cannot disagree. `NetListing.Players()` already feeds the browser's players column, which now
genuinely varies (1–3) instead of always reading 1.

### 4. Verification tool — `?netlocal=<n>`

Couch joins can't be driven from a script (`eaPress` can't synthesize a *gamepad* Start, and the
rig has no physical pads), so the card builds its own seam:

`?netlocal=<1-2>` queues *n* synthetic couch joins on **this** peer, firing a few seconds after
the session goes live — the exact "someone picks up a controller" edge, on both the host and the
client, unattended. Combined with `?aiplayer` the extra ships fly themselves (they are not
puppets, so `EffectiveController` puts them on the AI branch).

The `[net]` metrics line gains a **`roster=`** field: `slot:controller` for every seated slot,
plus per-slot claim counters. That is the actual proof — the two consoles must show the *same*
four-slot roster with the same owners, and kills must land on the right slot.

## Verification (Phase 5) — results

Per the repo's rules — data over frames, no timed live screenshots.

1. **`dotnet build -c Debug` clean** (0 errors; only the pre-existing `StorageStub` CS0436 noise).
2. **Two-tab rig**, fresh room:
   `?level=Level2&net=host&aiplayer&invuln&netlocal=1&room=<r>` + the same with `net=join`.
   (Level2, *not* Level1: Level1's intro hands the ship spawn to a script beat, so the host has
   no ship for the first minute and nothing to test.) Both consoles printed **mirror-image
   rosters**, which is the property the card is about:

   | slot | host | client |
   |---|---|---|
   | 0 | `Keyboard*` | `Remote` |
   | 1 | `Remote` | `Keyboard*` |
   | 2 | `Generic*` (its couch player) | `RemoteFriend` |
   | 3 | `RemoteFriend` | `Generic*` (its couch player) |

   (`*` = simulated locally.) Host `pri=0/1`, client `pri=1/0`; four ships alive on both sides.
   Link health at steady state: `drop=0 sgap=0 ordViol=0 seqGap=0 extrap=0`, `buf` 85–130 ms
   (target ~100), host `pupPops=0`. Claims flowed across the widened roster —
   `clRx=74 clKill=32 clPaid=14` host-side against the client's climbing `clTx`.
3. **Attribution** is now structural rather than assertable-after-the-fact: because the host
   allocates every seat, the two peers can never hold the same slot number, which is exactly
   what used to cross-credit (both independently picked "slot 2"). The agreeing rosters above
   *are* that proof. The HUD confirmed it end-to-end: **four score panels, one per seated slot,
   all four scoring** — which also exercises the sparse-roster `IsSeated` fix.
4. **Puppet death/respawn** across the wire: `friend ship died slot=2` → `friend ship joined
   slot=2` on the client as the host's couch player died and respawned.
5. Bugs this rig actually caught (both fixed): `Game1.LaunchLevelDirect` still seated slot 0
   regardless of the grant (a `?net=join` tab pairs *while* it boots, so the grant lands before
   the seat is taken — the ship ended up in a slot the wire didn't know about); and a listed
   session leaked the departed joiner's couch puppets, since the host keeps playing and nothing
   purges them.

**Not directly observed:** a full death/checkpoint *reset* with couch players aboard (it needs
all four ships dead at once, which four AI-driven `?aiplayer` ships rarely manage). The
`DriveFriendShip`/`SpawnFriend` adopt path that covers it is reasoned + exercised in part by the
death/respawn cycle above, but it has not been seen firing after a `SpawnAllPlayers` reset.

**Rig caveat:** two tabs in one Chrome window means one is always backgrounded; its rAF
throttling produced occasional `peer lost (timeout)` + reconnect flaps and inflated `drop`/`sgap`
during level load. That is the rig, not the change — the steady-state numbers above are from
after both tabs settled. Two separate Chrome *windows* side by side avoid it (popups are blocked
from the automation context, so this run could not do that).

## Out of scope (flag as follow-ups)

- **AI-friend budget formula** (`oracle.Players < Settings.Friends + 1`) is left alone. Making it
  count humans separately would change the *offline* game's behaviour.
- **TeamChallenge** keeps its "net session seats only the local device" rule (the tether is a
  two-ship construct; a third ship has no defined tether).
- Roster is still exactly **two peers**.
