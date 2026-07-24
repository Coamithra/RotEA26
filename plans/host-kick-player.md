# Host kick / kick+block for online co-op (card `0b8a300b`)

## Context

> If a remote player joins and pauses the game, the host should have the option to kick that
> player and resume, or even a kick+ban option (for that session only) so the player also can't
> rejoin. Basic anti-griefing stuff :)

The griefing hole is real and total. Today:

- Either peer's pause replicates (`EvPause`). The receiver freezes via `Collection.Push()` under
  `NetPauseOverlay` — a **non-interactive** "OTHER PLAYER PAUSED" curtain
  (`GameScene.NetSetRemotePaused`, [GameScene.cs:466](web/EvilAliensWeb/Game/EvilAliens/GameScene.cs:466)).
- `ComponentBin.Push()` sets `Enabled = false` on every collection component bar a tiny exempt
  list, and `GameScene` is one of them — so the host's own pause trigger
  ([GameScene.cs:901](web/EvilAliensWeb/Game/EvilAliens/GameScene.cs:901)) **does not run**. The
  host cannot open a menu, cannot exit, cannot do anything.
- The peer-drop failsafe won't save them either: while either side holds a pause the timeout
  widens to the `PausedPeerTimeoutMs` **120 s** backstop (deliberately — a paused tab is throttled).

So a stranger who joins a publicly-listed game and holds pause freezes the host's run for two
minutes at a time, repeatedly. The host's only escape is to kill the tab.

This matters most for the **join-in-progress / public browser** path (card `2001fbd8`), where the
peer is a stranger off the public list — which is exactly the card's framing.

## Design

Two independent pieces: **kick** (agency while frozen) and **block** (make it stick).

### 1. The kick UI — make the remote-pause curtain interactive for the host

`NetPauseOverlay` is added *after* the `Push`, so it stays `Enabled` while the world is frozen —
the same seam the local pause menu uses. A menu added there is live and works normally.

New `NetKickMenu : ConfirmationMenu` (`Compat/Net/NetKickMenu.cs`) — `ConfirmationMenu` already
gives a full-screen dim + a centred prompt + `AddEntry`/`AddEntryEvent` entries, and its entries
are keyboard-navigable *and* mouse-clickable for free.

Prompt: `The other player has paused the game.`
Entries, in this order:

| Entry | Effect |
|---|---|
| `Keep Waiting` | remove the menu, restore the plain `NetPauseOverlay`, stay frozen (today's behaviour) |
| `Kick Player` | disconnect the peer, unfreeze, host plays on solo |
| `Kick and Block` | same + block that peer from rejoining this level |

The client keeps the plain overlay unchanged — you can't kick the host — and `Keep Waiting` is
entry 0 and preselected, so a stray Enter is a no-op and kicking takes deliberate navigation.

**The offer is delayed by `KickOfferDelayMs` (4 s).** A short, innocent pause (someone answers the
door) keeps today's plain `OTHER PLAYER PAUSED` curtain untouched; only a pause that outlasts the
delay turns into the kick menu. Declining with `Keep Waiting` restores the curtain and **re-arms**
the timer, so a host who waits once still gets the option again 4 s later.

The timer lives in `NetSession.Update` on `NowMs` real time, **not** in `GameScene` or the overlay:
the whole world (`GameScene` included) is `Enabled = false` under the Push, and `NetSession.Update`
is driven straight from `Game1.UpdateInner`, so it is the one clock guaranteed to still be running.
`GameScene` exposes `NetShowKickMenu()` / `NetHideKickMenu()` for the swap and calls back into
`NetSession.RearmKickOffer()` on decline.

### 2. The kick wire + teardown

- New reliable event `EvKick = 20`, payload `[blocked:1]`.
- **Client** on `EvKick` → the existing `EndMatchPeerGone` shape: `Stop()` + `NetApplyPeerLeft()`
  (which already unwinds any local pause-menu depth and force-exits to the main menu), with the
  notice `Removed from the game` / `...and blocked from rejoining`.
- **Host** `NetSession.KickPeer(bool block)`:
  1. send the reliable `EvKick`;
  2. **immediately** revert to single-player — unfreeze (`RemotePaused = false` +
     `NetSetRemotePaused(false)`), `ExplodePuppet()`, `oracle.ReleasePlayer(ControlDevice.Remote)`,
     `ReleaseAllFriendPuppets()`, `PeerUp = false`;
  3. defer only the transport teardown by the existing `RejectGraceMs` (1 s) grace, because
     `Stop() -> transport.Close() -> pc.close()` is **abortive on WebRTC and would discard the
     still-buffered `EvKick`** — the exact problem `RejectGraceMs` was introduced for. The host's
     screen is already running again during that second.

Step 2 is the `listedSession` branch of `PeerLost` verbatim, so it gets **extracted into
`RevertToSinglePlayer()`** and both call it. After `Stop()`, `NetListing` re-lists next tick
(new room code) exactly as it does when a JIP joiner leaves normally.

A kick reverts the host to single-player for **every** session kind, not just listed ones — "kick
that player and resume" is the card's requirement, and it is the same outcome either way.

### 3. The block list — identity

There is no peer identity on the wire today; the room code is the *host's*. So the handshake gains
one, and the protocol goes **v5 → v6**.

- `eaRtc.peerId` (webrtc.js): a random 128-bit token minted once via `crypto.getRandomValues` and
  persisted in `localStorage` (`eaPeerId`), mirroring how `eaRtc.buildHash` is already exposed.
- `WebRtcInterop.PeerId()` → `NetProtocol.HashBuildString(token)` → 8 wire bytes appended to the
  hello/welcome (`HelloBytes` 13 → 21). Reuses the existing FNV-1a helper; no new machinery.
- Host keeps `static readonly HashSet<ulong> blockedPeers`. It deliberately **survives
  `NetSession.Stop()`** (the whole point is to outlive the kicked session and the re-list) and is
  cleared in `GameScene.Terminate` — i.e. scoped to the host's current level run, which is the
  card's "for that session only".
- `HandleHello`, host-side, before slot reservation: blocked id → `SendRejectOnce(RejectBanned)`
  (new reject reason 5). The existing reject grace makes the refusal actually reach them.
  Enforcing at the hello is the single choke point that covers **both** rejoin routes (public
  browser and a typed room code).

**Honest limitation, to be documented in `web/…/CLAUDE.md`:** this is a speed bump, not
authentication. The token is self-reported, so clearing site data or an incognito window defeats
it. That is proportionate to "basic anti-griefing" — and it is why the option is *kick* first,
block second. It is never sent to the signaling server, only to a peer already connected P2P.

Dev flag `?netfakepeer=<s>` (mirroring the existing `?netfakehash=`) overrides this tab's token —
**required** for any two-tab test, since both dev tabs share one `localStorage` and would
otherwise present the same id (blocking one would block yourself).

### Files

| File | Change |
|---|---|
| `Compat/Net/NetKickMenu.cs` | **new** — the host's remote-pause menu |
| `Compat/Net/NetProtocol.cs` | v6: `EvKick`, `RejectBanned`, peerId in the handshake |
| `Compat/Net/NetSession.cs` | `KickPeer`, `RevertToSinglePlayer`, block set, hello gate, `EvKick` rx, `pendingStopReason` |
| `Game/EvilAliens/GameScene.cs` | host shows the kick menu; `Terminate` clears the block list |
| `Compat/Net/WebRtcInterop.cs` | `PeerId()` |
| `wwwroot/webrtc.js` | `eaRtc.peerId` |
| `Compat/DebugFlags.cs` | `?netfakepeer=`, `?netkickmenu` |
| `Compat/DebugInput.cs` | `eaKickTest()` console entry point |
| `web/EvilAliensWeb/CLAUDE.md` | the net-layer bullet + the limitation |

## Verification — RESULTS

All four legs ran. Findings that came out of them are folded into the design above.

1. **`eaKickTest()` — PASS (29/29)**, with the survives-`Stop()` leg included (run from the
   menu). **Proved sensitive**, not just green: deliberately breaking `ApplyKickBlock` to ignore
   its `block` flag failed exactly the two legs that rule owns (27/29, "kick without block leaves
   them joinable" + "records nothing") and nothing else. Reverted.
2. **`?netkickmenu` screenshot** — menu renders over a live, frozen Level 2 with the prompt, all
   three entries and `Keep Waiting` preselected.
3. **Two windows, the real path** — client `Esc` -> host froze -> kick menu appeared after the
   delay -> `Kick and Block` -> host unfroze and played on solo (peer's score panel gone, score
   still climbing), client exited to the menu showing "Removed from the game / The host blocked
   you from rejoining". Host console: `[net] kicked the peer (blocked for this level)` then
   `[net] session stop (kick teardown)` **exactly 1 s later** — the deferred teardown working as
   designed. Zero console errors on either side.
4. **Clean `dotnet build -c Debug`**, zero console exceptions.

Two real bugs the run caught, both fixed:

- **The protocol version was never bumped.** The handshake layout changed 13 -> 21 bytes but
  `ProtocolVersion` still read 5 — the `[net] session start ... protocol=v5` line is what exposed
  it. Two builds would both have claimed v5 and mis-decoded each other. Now v6, with a comment
  tying the bump to the layout.
- **`KickPeer` originally called `RevertToSinglePlayer`, whose `Stop()` closes the transport** —
  the exact abortive close the grace exists to avoid, which would have discarded the `EvKick` it
  had just queued. Split into `ReleasePeerSeats()` (immediate) + a deferred `Stop()`.

Also corrected in passing: `MenuSub1.Show()` already does its own `Collection.Add`, so the extra
explicit add was dropped; and the `?netkickmenu` trigger moved from `Initialize` to a real-time
one-shot in `Update` (freezing in `Initialize` parked the level before it drew, and a tick counter
was a bad clock — 120 ticks measured ~47 s through the level intro).

One caveat worth stating plainly: **the rejoin refusal itself was not exercised live.** The
loopback rig has no re-listing path (a URL `?net=` session simply ends on both sides after a
kick), so `RejectBanned` is covered by `eaKickTest`'s predicate legs plus the already-proven
`SendRejectOnce` machinery it shares with the `?netfakehash` reject flow — not by an end-to-end
rejoin. Exercising that needs the real WebRTC + signaling path.

## Verification — plan (as designed)

Per the project rules — tool first, real game only as the final smoke check.

1. **Decision logic as DATA — `eaKickTest()`** (the `eaNetSim.test` / `eaBinTest` / `eaNetBgTest`
   idiom, console-only, prints PASS/FAIL). Drives the real code: an unblocked hello is accepted;
   the same id after `KickPeer(block: true)` is refused with `RejectBanned`; after
   `KickPeer(block: false)` it is **accepted** (kick ≠ block); a *different* id is always accepted;
   the block set survives `Stop()` and is emptied by the level-exit clear. This is the gate — the
   ban rule is a pure predicate and a screenshot cannot prove it.
2. **Menu appearance — `?netkickmenu`** parks the host-side kick menu over a booted level with no
   peer (the `?gamebrowser` fake-entry precedent), giving a static, reliable screenshot. The menu
   is static UI, so a plain screenshot is valid here.
3. **End-to-end, two windows** on the BroadcastChannel rig with distinct `?netfakepeer=` ids:
   client pauses → host's menu appears → Kick and Block → host unfreezes and plays on, client
   exits to the menu with the notice → client rejoins → refused. Read both consoles (`[net]`
   lines), not screenshots — the subject is a state machine.
4. Clean `dotnet build -c Debug`, zero console exceptions, final smoke boot.

## Out of scope (follow-up cards)

- **Kicking a peer who is *not* pausing** — the host's own pause menu is the natural home for a
  "Kick Other Player" entry, but the card's scenario is the frozen one, and `pausedScene`'s entries
  are built once in `Initialize` so a conditional entry needs its own care.
- **Server-side / durable bans.** Blocks die with the level and are defeated by clearing site data.
- **Kicking individual couch players** brought by the peer — a kick takes the whole peer with it.
- Rate-limiting repeated rejoin attempts (each costs the host a brief re-pair; the world is never
  disturbed, since a refusal happens at the hello, before `PeerUp`).
