# Net: per-slot combo + powerup state disagree between peers

Card `1a3ad45a`. Follow-up to `b0ab09ec` (score reconciliation) and `4717d3cf` (remote powerup
indicator).

## Context

The card asks whether the combo COUNTER drift is worth fixing, lists options (a) document /
(b) replicate for display / (c) hide remote slots, and — crucially — asks to **check first
whether remote-slot POWERUP LEVELS drift for the same reason, since that part would not be
cosmetic.**

They do. The answer changes the shape of the card.

### Why every peer simulates every slot's combo

A remote ship is a puppet, but its shots are **re-fired locally through the real weapon path**:
`NetSession.DriveRemoteShip` → `PlayerShip.NetApplyRemoteState` → `FireAt(aim)`
([PlayerShip.cs:705](web/EvilAliensWeb/Game/EvilAliens/PlayerShip.cs:705)). Those are ordinary
local `Bullet`s stamped with that slot's owner id, so when one hits,
[Bullet.cs:164](web/EvilAliensWeb/Game/EvilAliens/Bullet.cs:164) calls
`Score.SustainCombo(player)` for a slot this peer does **not** own.

On a client those bullets hit frozen puppets interpolated ~100 ms behind the host's real
entities, so hit sequencing differs; add the 1 s `combotimer` lapse and the two simulations
routinely disagree.

### What that drives — the part that is not cosmetic

`ScoreVisualiser.increasecombo`
([ScoreVisualiser.cs:367](web/EvilAliensWeb/Game/EvilAliens/ScoreVisualiser.cs:367)) feeds
`powerupDatas[..].AddExp(combo)` **while that slot's `powerupactive` is set** — and card
`4717d3cf`'s `ApplyRemotePowerup` sets exactly that for a remote collector
([NetSession.cs:932](web/EvilAliensWeb/Compat/Net/NetSession.cs:932)). So since that card, a
peer levels up powerups for a slot it does not own, off a combo it invented.

`AddExp` filling the bar fires `onLevelUp` → `ScoreVisualiser_onLevelUp`
([ScoreVisualiser.cs:246](web/EvilAliensWeb/Game/EvilAliens/ScoreVisualiser.cs:246)), which
finds the ship in that slot — **the puppet** — and calls `PlayerShip.PowerUp(type, newLevel,
doEffect: true)`. On the non-owning peer that means:

| Powerup | `PowerUp` does | Divergence |
|---|---|---|
| `OneUp` | `Oracle.SetSlowmotion(12f)` ([PlayerShip.cs:2208](web/EvilAliensWeb/Game/EvilAliens/PlayerShip.cs:2208)) | **12 s of global slow motion fired unilaterally on one peer**, at a moment the owner never agrees with. `SetSlowmotion` is a whole-sim time scale (`slowmotion = 0.4f`). |
| `Option` | spawns a real `Option` component on the puppet | the remote player visibly has a different number of option ships on each screen |
| `FirePower` / `Range` | raises `asplodingbulletspercentage` / `bouncebulletspercentage` / `bulletsSplit` on the puppet | the puppet's re-fired bullets behave differently from the owner's real ship — different bullet populations, hence different local kills and claims |
| any | `checkPowerupAchievement(player)` | `Awardment.FullPower` can be granted off another slot's *simulated* progress |
| any | the HUD powerbar + level readout under that slot | differs between screens |

The `OneUp` case is the serious one: a locally-invented combo can drop one peer into twelve
seconds of slow motion while the other plays at full speed.

So the card is really two bugs sharing a root cause: a cosmetic counter, and a slot's powerup
progression being simulated by a peer that has no business simulating it.

## Design

The unifying principle: **a slot's combo and powerup progression belong to whoever owns the
ship. Nobody else may simulate them.** That is the same rule `b0ab09ec` applied to score
(replicate the owner's number, don't re-derive it locally).

Effectively the card's option (b), extended to cover the powerup state the card flagged.

### 1. Stop simulating a slot you don't own

`ScoreVisualiser.increasecombo` keeps incrementing the counter (harmless, and the local value
is the fallback), but the `AddExp` + `checkPowerupAchievement` branch is gated on a new
`NetSession.OwnsSlot(slot)`:

```
Active ? (oracle.IsSeated(slot) && Controller(slot) is not Remote/RemoteFriend) : true
```

Offline and for our own slots this is `true`, so **single-player and local co-op are
byte-identical**. This alone kills the slow-motion, option-count, bullet-behaviour and
achievement divergences.

### 2. Replicate the owner's per-slot HUD state instead

New stream-lane message `MsgHudState = 0x12` (joins `MsgShipState`/`MsgFriendState` in the
0x1x stream group), sent by each peer at ~10 Hz for **every slot it owns** (primary, couch
players, AI friends). Protocol `v7 → v8`.

Body: `[type][slotCount]` then per slot
`[slot:1][combo:1][activeType:1][progress:1][lvl × 5:5]` = 9 B/slot, ≤ 38 B/packet,
≈ 380 B/s. Stream lane, so loss-tolerant by construction — a dropped packet just means the
readout is 100 ms staler.

Bidirectional, because ownership is: the host owns its primary + its couch/AI ships, the
client owns its own. Same shape as `MsgFriendState` after card `4d904410`.

Receiving side applies it **only to slots it does not own** (`!OwnsSlot`), via new
`ScoreVisualiser.NetSetHudState(...)`:

- `combo` → straight into `scores[slot].combo`. **Display only**: it cannot reach `AddExp`
  (gated by step 1) and the score is already reconciled by `EvScoreSync` + the unsettled
  ledger, so it never re-derives an award.
- `activeType` + `progress` → the powerbar fill, so the bar matches instead of drifting.
- `levels` → raise each type's level through the existing
  `PlayerShip.PowerUp(type, lvl, doEffect: false)` path so the puppet's re-fired bullets
  match the owner's real loadout. **`OneUp` is excluded** — slow motion is an unreplicated
  local effect today and must stay the owner's alone.

Bounds: `slot` is a raw wire byte, so reject `>= ScoreVisualiser.SlotCount` (4), clamp levels
to 0..4 — same rule and reasoning as `ApplyRemotePowerup`'s comment.

### Deliberate side effect, called out

`AwardScoreToAll` (every boss) pays each seated slot with **that slot's own multiplier**. The
host currently uses its own invented combo for the client's slot; after this it uses the
client's real one. That is a genuine payout change — and a correction, since the host was
paying a share computed from a number the client never had. Flagging it because it is a
gameplay change riding along with a "cosmetic" card.

## Verification

No unit tests in this repo; per the root `CLAUDE.md` rules this is a data problem, not a
visual one — two windows cannot show a slow drift, and the failure mode (a slot levelling up
on the wrong peer) has no frame that proves it. So: a console self-test, following the
`eaNetScore.test()` precedent exactly.

`eaNetCombo.test()` (`Compat/Net/NetComboTest.cs`), leave-no-trace, against the live
`ScoreVisualiser`:

1. **Wire round trip** — `EncodeHudState` → `TryDecodeHudState` → `NetSetHudState` applied for
   real; assert combo/type/progress/levels land at the right offsets, multi-slot packets
   decode, and the bounds rejections fire (`slot >= SlotCount`, level clamp, short buffer).
2. **The divergence demonstration** — drive two independent combo streams (owner vs. a peer
   with lag-shifted hit sequencing) through the real `increasecombo`. **Run the OLD ungated
   behaviour over the identical stream first** and show it levels a non-owned slot's
   `PowerupData` (and would have reached the `OneUp` trigger), then show the gated path does
   not. A green tick means nothing unless the same input is shown to break the old code —
   the rule `eaNetScore.test()` established.
3. **Ownership predicate** as data — `OwnsSlot` over a synthetic roster: own primary, couch
   slot, `Remote`, `RemoteFriend`, unseated, out-of-range; plus the offline case returning
   `true` for everything.

Plus: `eaScore()` gains the replicated-vs-local combo per slot (the readable two-peer
comparison), a clean `dotnet build -c Debug`, and a final real-Chrome smoke boot with zero
console exceptions.

## Out of scope

- Replicating slow motion itself (`Oracle.SetSlowmotion`) — it is local by design and the
  puppet driver already runs on real time precisely so local time scaling cannot poison
  replication.
- The bomb/Blast count, deliberately unmirrored (`ApplyRemotePowerup`'s existing reasoning:
  the spend side does not decrement either).
- Anything about score reconciliation — `b0ab09ec` settled it and this must not disturb it.
- 3–4 separate machines (`plans/4p-online-coop.md`); the wire format here is per-slot and
  already N-slot shaped, so it does not add a new blocker.
