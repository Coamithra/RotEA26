# A finished level no longer ends a JOIN-IN-PROGRESS match (card 51566427)

## Context

The card: *"in a multiplayer match, as long as the host doesnt disconnect we don't need to end the game"*.

The 11.9 match-end policy (card `87242257`) already says exactly that for peer DEPARTURE — a client leaving frees its seats and everyone else plays on; only the host leaving ends the match. Cards `3b6c12e7` (won) and `c600c55a` (lost) then said it for a level that plays itself out: the pairing survives and both peers walk back to the lobby, so the host can pick the next mission.

**One shape was deliberately left out of that, and it is the one this card is about.** `NetSession.UpdateSceneEdges` guards the survival branch with `endedCleanly && menuSession`, so a **listed / join-in-progress** session (`listedSession` — a stranger joined our LISTED single-player game, card `2001fbd8`) still falls through to the ordinary match end:

```csharp
if (menuSession || listedSession)
{
    if (PeerUp) SendEventToSessionPeers(EvLeave);
    Stop("match ended");
}
```

So: host lists their game, a stranger joins mid-level, they finish the level together — and the match is torn down and the stranger is ejected to their main menu with the host still sitting right there. The code says why:

> `listedSession` is excluded deliberately: a join-in-progress host has no lobby to return to, so its level ending is still a match end

**That reason has expired.** It was written before card `0257f8ba` (11.10): a listed session holds a real `sessionRoom` on the real WebRTC transport, `MenuScene.EnterNetLobby()` is a public door, `NetLobby.HostLobbyText` renders a room + roster off the live session, and rooms take four machines. A listed host at the menus is indistinguishable from a menu-lobby host.

## Design

**One behaviour change, in `NetSession.UpdateSceneEdges` (`Compat/Net/NetSession.cs`).** A clean level end keeps a LISTED session alive exactly as it keeps a menu one alive, and the session becomes a menu-lobby session from that point on:

```csharp
if (endedCleanly && (menuSession || listedSession))
{
    // ... existing: ResetPerMatchState(); pendingLobbyReturn = true;
    // NEW: a listed session that outlives its level IS a lobby session now.
    menuSession = true;
    listedSession = false;
}
```

**Why convert the flag rather than add `|| listedSession` to each site.** `listedSession` differs from `menuSession` in exactly one live decision once the level is down — `ReleaseDepartedPeer`'s tail, where `menuSession && isHost` keeps the room open for new players and anything else `Stop()`s with *"The other player left / Match ended"*. Leaving the flag as-is would re-open the card's own bug one step later: the host is in a lobby, the guest disconnects, and the host is thrown to the main menu with a notice. Every other `listedSession` reader is already `menuSession || listedSession` (`HostOpenToJoinInProgress`, `PeerConnected`, `PeerLost`, the `EvLeave` rx) or start-time-only (the log line). Nothing keys off "this session began as a listing" after its level is gone, so the conversion is truthful rather than a fib to reach a branch.

No protocol change — nothing new crosses the wire, and one message that used to (the `EvLeave`) stops. The `?net=jiphost` dev re-arm is untouched: it fires off `!Active`, which this branch no longer reaches.

**The client half needs nothing.** A JIP joiner is a normal menu-session client (`StartListedSession` is always host-side), so its own clean level end already survives and already raises `pendingLobbyReturn`. Today it survives and is then killed by the host's `EvLeave`; with that gone, both ends walk to the lobby on their own beat — which is exactly the flow `net_level_end.txt` already pins for the client.

## Verification

`Compat/Net/NetLevelEndTest.cs` already owns this claim for menu sessions in four arms. Add a fifth, the listed one, and pin it:

- `NetSession.StartForTest` gains an `asListedSession` opt-in beside the existing `asMenuSession` one (same shape, same reason).
- `NetLevelEndTest.ArmListed()` — `ArmHost()`'s twin with `asListedSession: true`. A host arm, because `listedSession` only ever exists host-side; and a host victory only ever comes from the level's own `?win` script, never from a beat a rig can inject.
- Phase 2 reuses `MenuCheck()` (session survived, peer still up, host on its lobby pick menu, main menu not live underneath) plus a leg the host arms did not need before: **no `EvLeave` reached the scripted peer** — the discriminator, since that is the message the pre-card build sends. `NetSession.IsMenuSession` is added as a read seam so the conversion itself is asserted, not inferred.
- `tools/headless/probes/net_level_end_listed.txt`, sibling of `net_level_end_lobby.txt`, boots `?level=Level2&invuln&noattract&win&seed=1234`. No `?netallowdebug`: `HandleHello`'s debug refusal is `menuSession`-only, so a listed session pairs under `?level=` as it does in production (where the debug bit stops the LISTING, not the session).
- Mutation test: revert the branch guard to `endedCleanly && menuSession` and the probe must fail on the session/peer/EvLeave legs; that is the run that proves it discriminates.
- `net_level_end.txt`, `net_level_end_lobby.txt`, `net_level_lost_lobby.txt` and `net_lobby_panel.txt` must all stay green (the menu-session shapes are unchanged).
- Phase-5 gate: clean `dotnet build`, then the foreground-Chrome smoke check with zero console exceptions.

## Out of scope

- **Host migration.** The card's premise is that the host stays; a host that DOES disconnect still ends the match for everyone, which is the 11.9 policy's other half and unchanged.
- **`RevertToSinglePlayer`.** When the last client leaves mid-level the host keeps playing its level and the session stops so `NetListing` can re-list — the game does not end there, so the card does not reach it.
- **A quit / force-exit (`FinishedMode.exit`).** That really is someone leaving.
