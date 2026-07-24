# JIP: replay deep mid-level background/doodad state (card 45a4e48d)

Follow-up #1 to card 2001fbd8 (public game browser + join-in-progress).
Design context: `plans/net-game-browser.md`; gap list: `plans/net-game-browser-followups.md`.

## Context

The JIP catch-up sends `EvLaunch` + `NetIdRegistry.ReplayLive()` + a 1 Hz `EvScoreSync`, so a
stranger joining a running game gets the right level, the live entity set and the right
score/lives. What it does NOT get is everything the host's level SCRIPT already did to the
scenery, because the joiner's own `GameScene.Initialize` starts from the level's INITIAL
background and music and the script never runs on a client (11.2 sim-split).

Concretely, joining Level 1 twenty seconds in gives you: default scroll speed instead of the
belt's sideways scroll, the un-switched alien-base floor tile, no earth fly-by (or worse — the
level's music restarted from the top while the host is on the boss track).

Card 11.3 already replicates each of these ops **as they happen** (`NetBackgroundOp` +
`EvMusic`); the gap is purely that a peer arriving LATER missed the ones that already fired.
So this card is a catch-up burst, not new replication.

## Key finding: the burst belongs at `EvReady`, not `PeerConnected`

The card text says "in the JIP burst (`NetSession.PeerConnected`, listedSession branch)". That
is the wrong seam: at `PeerConnected` the joiner has no `GameScene` at all — it has just been
sent `EvLaunch` and still has to warm and Initialize. Its `Initialize` sets the level's initial
background + music, so anything sent at `PeerConnected` would be **clobbered a second later**.

`EvReady` (the client's scene-up edge, `NetSession.UpdateSceneEdges` -> host handler at
`NetSession.cs:1497`) is the existing seam that already exists for exactly this reason — it is
what triggers `ReplayLive()` for a client that out-warmed the host. The deep-state burst goes
next to that `ReplayLive()` call.

Bonus: `EvReady` is session-kind agnostic, so this also covers the menu-lobby launch race (a
joiner whose warm finishes after the host has already run a script beat) and the URL `?net=`
loopback rig — which is what makes it testable without a signaling server (see Verification).

## Design

### What is replayed (host -> joiner, reliable lane, once, in this order)

| # | State | Source | Wire |
|---|---|---|---|
| 1 | scroll speed | last `SetSpeed` argument (null until the script calls it) | `EvBackground(SetSpeed, v)` |
| 2 | alien-base tile switch | last `SetAlienBaseN` fired | `EvBackground(SetAlienBaseN)` |
| 3 | belt slowdown | `beltSlowActive` | `EvBackground(EngageBeltSlowdown)` if true |
| 4 | in-flight doodad | `showdoodad` + `doodadname` | `EvBackground(Queue*)` then `EvBackground(SetDoodadPos, doodadPos)` |
| 5 | current music | last `PlayMusic`/`StopMusic` | `EvMusic(song)` |

Ordering matters: speed first (a doodad's entry/exit direction is read off `scrollspeed.Y`),
doodad kind before its position.

### Why the tracking lives in `Background`, not in `NetSession`

`NetSession.OnBackgroundOp` early-returns when `!IsHost || !PeerUp` — and in the JIP case there
IS no peer while the host plays alone, which is exactly the window whose ops we need to
remember. So the last-op state has to be latched by `Background` itself (which runs regardless),
not sniffed off the send path.

`Background` gains three private fields (`netLastSpeed` as a `Vector2?`, `netLastAlienBase` as a
`NetBackgroundOp?`, both null = "the script never touched it") set alongside the existing
`OnBackgroundOp` calls, cleared in `Background.Reset()` (level entry) for the speed but NOT for
the tile switch — `Reset()` restores `scrollspeed` but deliberately leaves the switched floor
texture alone, so the tracking mirrors that.

Nullable is what avoids the trap of sending `targetscrollspeed` blind: before the first
`SetSpeed` it is `default(Vector2)` = zero, while the real `scrollspeed` is whatever
`SetSpace()`/`SetMars()` put there at Initialize — replaying that zero would freeze the joiner's
starfield.

### The doodad position

Replaying only the `Queue*` op restarts the fly-by from its entry point, so the joiner would
watch the earth descend from the top while the host's is already halfway down (and its
`doodadStarSlowdown` star-freeze would run for a full extra crossing). Sending the position too
is a few lines and makes the joiner's scenery actually match.

Rather than overloading the existing `Queue*` ops' `Vector2` payload with a
"non-zero means catch-up" sentinel, append ONE new op:

```
NetBackgroundOp.SetDoodadPos = 11   // catch-up only: place the in-flight doodad
```

(the enum is APPEND-ONLY, so 11 is the next free value). The receiver applies it as a plain
`Background.NetSetDoodadPos(v)` that no-ops when no doodad is showing. Two single-purpose events
on an ordered reliable lane beat one overloaded one.

### Current song accessor

`SoundManager` gets `private int _currentSong = -1`, set in `PlayMusic` **above** the
`Settings.PlayMusic` mute check (mirroring where `OnMusic` already sits — a muted host still
replicates) and cleared in `StopMusic`; exposed as `internal int NetCurrentSong`. The existing
`NetApplyMusic` cue dedupe makes the replay a no-op whenever the joiner already happens to be on
the right track, which is the common case (both peers ran the same level's Initialize).

### Files

| File | Change |
|---|---|
| `Game/EvilAliens/Background.cs` | latch last-op state; `NetReplayCatchUp()`; `NetSetDoodadPos(v)` |
| `Game/EvilAliens/SoundManager.cs` | `_currentSong` + `NetCurrentSong` |
| `Game/EvilAliens/GameScene.cs` | `NetReplayDeepState()`; `SetDoodadPos` case in `NetApplyBackgroundOp` |
| `Compat/Net/NetProtocol.cs` | append `NetBackgroundOp.SetDoodadPos = 11` |
| `Compat/Net/NetSession.cs` | call the burst from the `EvReady` handler |
| `Compat/DebugInput.cs` + `wwwroot/index.html` | `eaNetBg()` verification dump |

## Verification

Per the project rules this is BEHAVIOUR over time on a wire, so the gate is **data, not a timed
screenshot** — and the state under test is a fly-by that moves every frame, which a screenshot
cannot honestly capture.

**The tool: `eaNetBg()`** — a console helper printing one parseable line of the exact catch-up
state (`speed=`, `base=`, `belt=`, `doodad=<name>@x,y`, `song=`) on whichever side it is run.
The test is then a straight comparison of the two peers' lines.

**1. Round-trip self-test — `eaNetBgTest()` (the primary gate, and it is exact).** One tab, no
peer, no timing: capture the burst, `Background.Reset()` to what a fresh joiner's Initialize
leaves, replay through the real `NetApplyBackgroundOp` path, diff the state line.

Run on `?level=Level1&netscript&aiplayer&invuln` once the script had fired its background beats:

```
[netbgtest] PASS ops=3
  host   : speed=0,0.06 base=- belt=0 doodad=QueueAndromeda@400,113.2 song=1
  joiner : speed=- base=- belt=0 doodad=- song=1          <- the bug, verbatim
  caught : speed=0,0.06 base=- belt=0 doodad=QueueAndromeda@400,113.2 song=1
```

The middle line IS the gap this card closes; the third is the fix, reproducing the host exactly
including the andromeda's mid-crossing Y.

**2. Two-window loopback — the real wire.** Host `?level=Level1&netscript&aiplayer&invuln&
net=host&room=jip1`, joiner opened as a separate VISIBLE window ~30 s later (a hidden tab's rAF
stops entirely, so a second tab is useless — use a popup/second window). The joiner, which did
not exist when the op fired and whose own script never runs, read:

```
JOINER [netbg] speed=0,0.06 base=- belt=0 doodad=- song=4
```

`speed=0,0.06` is the catch-up burst crossing the real transport: it was set by the host's
script long before this peer connected, and nothing else could have put it there. (`doodad=-`
because that run's fly-by had already left the screen by the time the joiner paired.)

**3. Smoke**: clean Debug build (0 errors, no new warnings), plain `/` boot reaches the splash,
zero console errors.

**Executed vs inspected** — the doodad+position leg is proven exactly by (1) and the wire by
(2), but the two were not captured in a *single* live run: catching a ~15 s fly-by with a ~12 s
WASM cold boot on the joiner is a race, and MCP input stopped reaching the host window once the
popup took focus. The `SetAlienBaseN` and belt legs were exercised through the same mechanism in
(1) but no live run reached a level that fires them (Level 3's base switches / Level 1's belt sit
deep in their scripts); their latches were verified by reading the patched code. All five legs
are the same three lines (nullable latch -> emit -> the pre-existing apply switch).

## Out of scope

- **Music RATE** (`SoundManager.SetMusicRate`): the BrainBoss HP sweep is driven per-tick from
  `BrainBoss.Update`, which never runs on a client (the boss is a frozen puppet), so the rate is
  a general 11.3 gap rather than a JIP one — it belongs to follow-up #3 (mid-boss arrival puppet
  fidelity), which already owns deep boss state.
- Mechanical-friend ships (follow-up #2), boss attack poses (#3), abuse surface (#4).
- Doodad state beyond position (scale/colour/blend are all deterministic per doodad kind).
- Level-script PROGRESS: the joiner still cannot know which spawner beat is mid-flight; the
  replicated live entity set is the existing answer to that and is unchanged here.
