# Slot grant onto an occupied local seat — fail loudly, and stop being reachable

Card `c0229c57`. Found during card `c0398370`'s `?netjip` two-window JIP pass.

## Context

`MsgWelcome` (v5+) carries the seat the host granted the joiner.
`NetSession.AdoptGrantedPrimarySlot` takes it. When we are already seated somewhere it
also has to move the registration (and any live ship) across, because claiming a slot our
ship is not in silently kills the primary stream.

That move goes through `Oracle.MovePlayerSlot(from, to)`, which refuses when
`players[to].isPlaying` — i.e. when the **granted** seat is occupied. On refusal the
current code logs

```
[net] could not move local primary 0 -> 1 (slot busy) -- staying put
```

and carries on into a permanently half-connected session with nothing surfaced to the
player: `pri=0/1` on the host vs `pri=0/0` on the joiner, non-mirrored rosters, no remote
puppet on the joiner (`remoteShip=0`, `buf=0ms`), and no way back — `peerPrimarySlot` is
assigned *before* the early return, which satisfies the `peerPrimarySlot == SlotNone` term
in `Update`'s retry condition and silences the 1 Hz hello on **both** peers.

## Q1: is it reachable by a real player? — YES, and easily

The precondition is "the joiner's slot 0 **and** slot 1 are both seated when the welcome
lands". The joiner is at the menu at that moment, so this is about the roster the menu
carries — and the menu's roster is **whatever the last scene left behind**:

- `GameScene.Terminate` (`GameScene.cs:1352`) purges components, drops banners, clears
  blocked peers — but never touches `oracle`'s player table.
- `Game1.gameScene_OnFinished` just re-adds `menuScene`. No reset there either.
- The roster is only ever cleared on the way *into* a level: `Game1.MenuFinished:705`,
  `Game1.LaunchLevelDirect:840`, `TeamChallenge.Initialize:67` all `ResetPlayers()` first.

So every seat from the previous scene survives at the menu. And the **attract demo seats
more than one**: `mainMenu_DemoSelected` launches `Demo1/2/3` with `starter =
ControlDevice.AI` (slot 0), then the demo's own `Initialize` adds extra AI players —
`Demo1.cs:37-47` adds **3** more on a 20% roll and **1** more on a further 40% roll (Demo2
and Demo3 have the same shape). So ~60% of attract demos leave slot 1 seated.

Repro with no debug flags at all:

> idle at the main menu → attract demo plays → press a key to get out → Online Co-op →
> Join Online Game → pick a game.

A couch co-op session that backed out to the menu reaches the same state deterministically.
This is a genuine user-facing bug, not a test artifact. (The card's "Start → Controls and
backed out" story is the same thing: backing out of Start does not seat anyone, but idling
long enough to reach Controls is exactly how the attract demo gets in.)

## Design

Prevent the bad grant in the handshake; keep the unrecoverable case a clean refusal that
does not end the host's game.

### 0. The host grants a seat that is free on BOTH peers (protocol v7 -> v8)

The host allocates out of *its own* free slots and cannot see that the joiner's slot 1 is
taken, so the grant is a guess. Give it the missing input: the handshake gains a
`blockedSlots` byte (a 4-bit mask, slots 0..3).

- **Client -> host:** the slots this peer cannot seat its primary in right now. Only
  meaningful with a scene up; at the menu it sends 0 (see 1). Our own current seat is
  excluded — that is the seat we would move *out* of, not a blocker.
- **Host -> client:** always 0. The host allocates; it has no constraint to report.

`ReserveRemotePrimarySlot` then picks the first slot free on its roster AND not in the
mask. If it already holds a grant the client's mask now says is blocked, it releases that
seat and re-picks. If nothing is available on both sides: `SendRejectOnce(RejectFull)` —
the existing "Game full" notice, no new reject reason.

**The host's own game survives that refusal.** `Stop()` does not force-exit a level, and
`NetListing.ComputeEligible` requires `!NetSession.Active`, so a listed JIP host drops back
to plain single-player and re-lists a tick or two later. (A menu-lobby host does end its
lobby, which is correct — there is no game there to keep.)

A mask rather than "the slot I refused" because it resolves in ONE round and *prevents*
the mismatch instead of recovering from it. Wire layout changes (`HelloBytes` 21 -> 22), so
the version byte moves with it — same rule v5/v6/v7 followed. No compat concern: the
build-hash gate already guarantees both peers run the identical binary.

### 1. Distinguish a LIVE seat from stale menu bookkeeping

The move only matters when the seat is load-bearing — i.e. when a `GameScene` is up (the
dev `?net=join` flow boots into a level at slot 0 and its live ship must move with it). At
the **menu**, which is where both the menu-lobby joiner and the JIP joiner hello from, the
roster is leftover bookkeeping that the launch path's `ResetPlayers()` is about to wipe
before it seats us at the granted slot. There is nothing to move, and a busy destination
means nothing.

The branch condition gains `GameScene.NetActiveScene != null`. That alone removes the
entire reachable-by-a-real-player case: the joiner adopts slot 1, both peers print
`pri` mirror images, and the launch seats it correctly.

### 2. When the move fails anyway, RENEGOTIATE — and never silence the retry

With the mask in place, a grant landing on an occupied seat is a genuine race: our roster
changed between our hello and the host's welcome. The answer is not to end the session —
it is to say hello again with a fresh mask and let the host pick another seat.

That falls out of the handshake for free, provided we **do not settle**:
`peerPrimarySlot = HostPrimarySlot` moves to *after* a settled adoption. `Update`'s retry
condition is `!PeerUp || peerPrimarySlot == SlotNone`, so an unsettled adopt keeps the
1 Hz hello going on both peers — the exact term the current code satisfies too early,
which is why the bug cannot self-heal. That invariant outlives this particular branch: no
future early return here can silently strand the session.

The loop is bounded. Each round either seats the client or adds a slot to the mask, and
the host converges naturally (it re-picks against the mask, so it never re-offers a slot
the client just refused). After at most four, `RejectFull`.

### 3. Make the branch a named decision, so it can be tested without a fixture

`AdoptGrantedPrimarySlot` needs `oracle`, `transport`, a live session and a `GameScene` —
untestable as-is. Extract the choice into a pure function in the `NetListing.ComputeEligible`
/ `PlayerShip.IsAiShootable` house style:

```cs
internal enum SlotAdopt { Settled, TakeSlot, MoveSeat, Renegotiate }

internal static SlotAdopt DecideSlotAdopt(byte localSlot, byte granted, byte peerSlot,
                                          bool sceneUp, bool localSeated, bool grantedSeated)
```

`AdoptGrantedPrimarySlot` becomes a switch over it. A `MoveSeat` whose `MovePlayerSlot`
still fails falls through to the same `Renegotiate` handling rather than to a silent
return — the failure mode this card is about must not be reintroducible by a future caller.

### 4. Verification tool: `eaSlotTest()`

New `Compat/Net/NetSlotTest.cs`, in the `eaKickTest()` / `eaNetScore.test()` idiom, wired
through `DebugInput` + `index.html`. Data, not screenshots — the symptom is two consoles
disagreeing about a byte, which no frame can show, and reaching it live needs two windows
plus a 60%-of-the-time RNG roll in an attract demo.

It covers:

1. `DecideSlotAdopt` over the full truth table, named per case.
2. The three cases that matter end to end, against a **scratch `new Oracle(game)`** (the
   ctor seats a full 4-slot table; it is never added to the game, so the live roster is
   untouched and the test is leave-no-trace at any point in play):
   - destination free, scene up → `MoveSeat`, and the real `MovePlayerSlot` moves it;
   - destination busy, no scene → `TakeSlot` (the card's real-player case);
   - destination busy, scene up → `Renegotiate`, and `peerPrimarySlot` must still read
     `SlotNone` (the anti-regression assertion for the silenced retry).
3. The **allocator against the mask**: the host picks a slot free on both sides; it releases
   and re-picks when its held grant becomes blocked; it converges rather than re-offering a
   refused slot; and it runs out only when every slot is blocked on one side or the other.
   Driven against a scratch `Oracle`, including the attract-demo roster shape (0+1 seated).
4. A **legacy control**: the old policy replayed over cases 2 and 3, asserted to produce the
   broken state (peers disagreeing, "settled" set anyway). Per the `eaNetScore.test()`
   precedent — a green tick proves nothing unless the same input is shown to break the old
   code.
5. The **v8 handshake codec**: `blockedSlots` round-trips at both message types and both
   roles, and a v7 (21-byte) hello is refused rather than read short.

Plus the standard gate: clean `dotnet build -c Debug`, the tool green, and a real-Chrome
smoke boot with zero console exceptions.

## Out of scope

- **Clearing the roster on scene exit.** It is the root of the stale-seat state, but every
  launch path already `ResetPlayers()`es before seating, so nothing else observably depends
  on it, and a `ResetPlayers()` in `Terminate` is a broad behavioural change (credits, brag,
  score panels, `NetListing` eligibility) with no demonstrated benefit here. Noted as a
  follow-up card instead.
- The two-window `?netjip` run. This is a data-level fix; a live pass is a smoke check.
- Anything about `ReserveRemotePrimarySlot` (host side) — it already refuses correctly.
