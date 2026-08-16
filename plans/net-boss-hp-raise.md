# net: two-way hp clamp on client puppets (card 87310afa)

## Context

`KillableAlien.NetApplyHp` is the receive side of the replicated `Hp` field in every world-snapshot entry. It is currently a **one-way (downward-only) clamp**:

```csharp
if (dead || hitpoints <= 0) { return; }
hp = (int)MathHelper.Max(hp, 1f);
if (hp >= hitpoints) { return; }   // <-- a snapshot carrying MORE hp than we hold is discarded
hitpoints = hp;
```

A client's own bullets run the real `KillableAlien.HitBy` against puppets locally — puppets are `Enabled=false` but stay hit-testable via `NetPuppets.CollidableOverride` (`CollisionHandler.IsActive`), which is what client-owned kill claims are built on. So the client spends hp on hits that the host's own per-entity 35 ms `hittimer` may have refused (the two peers run independent timers over different hit sequences, ~100 ms apart). Those over-predictions are never corrected back, so the client's copy ratchets permanently below the host's.

Consequence: the client kills locally at `hitpoints <= 0`, files an unconditional `EvClaim`, and `HandleClaim` → `NetKill` **bypasses the hittimer gate** ("a claim is already a confirmed kill"). A boss can therefore be claimed dead while the host's copy still has HP.

## Design

Make the clamp two-way — the host is authoritative for hp in both directions. Delete the `hp >= hitpoints` early-out; **keep** the `dead || hitpoints <= 0` guard and the floor at 1, so deaths still arrive exclusively as events or local kills and a snapshot can never resurrect a dead puppet.

No wire change, no protocol bump: the same `Hp` byte, applied differently on receive.

### Accepted cost (user's call, recorded)

hp also drives draw-side state: the colorize redden (`KillableAlien.HitBy` and the identical recompute inside `NetApplyHp`), `BattleSkull`'s green→red hue remap, and `MarsBoss`'s `fps = Lerp(32, 16, HitPointsNormalized)`. An **in-order but ~half-RTT-stale** snapshot will now nudge those back up while the client is shooting. Judged barely noticeable — a shade less red than it is about to be, and a small animation-rate wobble.

This is *not* the reorder case. Card f5cf7a5c's per-netId monotone seq already refuses a stale entry in `NetPuppets` **before** `ApplySnapshotState` runs, so a late or reordered packet cannot raise hp at all. The two guards stay distinct and the probe asserts that.

### What the clamp direction was NOT doing

Card a5c2a39b closed with "monotone-down on the client … so a boss cannot lose life twice as fast." The conclusion holds but the mechanism cited is wrong: what prevents double-rate damage is **host authority plus the per-entity 35 ms `hittimer` gate at the top of `HitBy`** — the host's boss is one real entity, and both players' bullets (the remote peer's re-spawned from the replicated cumulative shot count) contend for that one gate. The clamp direction only made the client's copy pessimistic. This card makes it accurate; it does not touch the anti-double-damage property.

### The `net_jip_sync` invariant survives

That suite asserts, with no tolerance, that **a puppet's live hp may never EXCEED its `hpwire`** (`PuppetInfo.LastAppliedHp`, the value received). Under a two-way clamp a raise sets `hitpoints = state.Hp`, and `LastAppliedHp` is set to that same `state.Hp` — so live hp becomes *equal to* `hpwire`, never above it, and local damage only lowers from there. The invariant is unchanged; what changes is that the previously measured one-directional signature (132 gaps, all client-lower) should now mostly collapse to equality.

## Files

| File | Change |
|---|---|
| `Game/EvilAliens/KillableAlien.cs` | drop the early-out; rewrite the `NetApplyHp` header |
| `Compat/Net/NetEntityTest.cs` | flip the "refuses to RAISE" assertion to "raises"; fix the `ProbeKillable.KilledBy` rationale |
| `Compat/Net/NetSnapshotTest.cs` | section 7: flip the raise leg; **add** a stale-seq raise leg (via `NetPuppets.OnSnapshotEntry` with an explicit older seq) so the seq guard and the clamp stay separately pinned |
| `Compat/Net/NetPuppets.cs` | the `LastAppliedHp` comment justifying "record the received value, not the read-back" |
| `Compat/Net/CLAUDE.md` | the "ONLY EVER LOWERS" bullet and the hp row of the JIP-sync table |

## Verification

- `dotnet build web/EvilAliensWeb -c Debug` clean.
- `python tools/headless/probes/run_probes.py --build` — full suite green, `net_selftests.txt` tally up by the new leg.
- Mutation-test the new probe legs: restore the early-out and confirm the raise leg goes RED; drop the stale-seq guard (`?netstaleguard=0`) and confirm the stale leg goes RED. A leg that cannot fail proves nothing.
- Foreground Chrome smoke check: boots, zero console exceptions.

## Out of scope

- Any change to claim arbitration, the `hittimer` windows, or boss HP values.
- The quiescence-gated raise (accept an upward correction only after ~RTT of no local hits) — considered and declined for this card; the plain two-way clamp is what was asked for, and the flicker it trades against is cosmetic.
