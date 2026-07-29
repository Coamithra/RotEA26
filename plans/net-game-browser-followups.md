# Net game browser (card 2001fbd8) — JIP follow-up cards to file

Card 2001fbd8 shipped the full public game browser INCLUDING join-in-progress. Per its scope
call ("known JIP gaps get follow-up cards rather than blocking this one"), file these four on
the local Trello board (`trello --backend local --board 10989a3d`, list `Backlog` `79158996`).
They are documented here because the board CLI runs only on the author's machine.

1. ~~**JIP: deep mid-level background/doodad state.**~~ **DONE** (card 45a4e48d) -- the host now
   replays the latching ops + any in-flight doodad (with its current position) + the current song
   in an `EvReady`-triggered burst. See `web/EvilAliensWeb/Compat/Net/CLAUDE.md` -> "Deep mid-level scenery
   catch-up". RESIDUAL: the whole-scene setters (`SetSpace`/`SetMars`/`SetAlienBase`) are still
   unhooked, and `InsaneBossI` swaps scenes MID-level via them, so a joiner arriving after a
   `GoMars`/`GoSpace`/`GoAlienBase` still sees the level's starting scene. Original text: The join-in-progress catch-up sends
   `EvLaunch` + replays the live entity set + trues up score/lives, but the joiner's fresh level
   `Initialize` starts from the level's INITIAL background/music. A fly-by / asteroid belt /
   music switch already in progress on the host is not replayed. Replay the host's last
   `Background` op + current music cue in the JIP burst (`NetSession.PeerConnected`, listedSession
   branch). Needs a "current background op" / "current song" accessor the host can read.

2. **JIP: mechanical-friend ships not replicated.** Listing is currently REFUSED while
   `Settings.Friends > 0` (folded into `CheckForCheats()` in `NetListing.ComputeEligible`),
   because friend ships aren't on the wire. Replicate mechanical-friend ships, then relax the
   listing gate to allow `Friends > 0`.

3. **JIP: mid-boss arrival puppet fidelity.** A joiner arriving mid-boss gets the boss's
   best-effort puppet pose (the known 11.2 limit) — more visible now that arrival can happen at
   any moment. Grow the boss descriptors' state extras so a mid-fight arrival reconstructs the
   attack pose.

4. **Public-list abuse surface.** Beyond `MAX_ROOMS` / the per-browser-socket ping rate limit
   there is no protection: a room can't be reported or hidden by anyone but its host, and the
   list is fully open. Add a hide/report path + tighter server-side bounds (per-IP room caps,
   listing rate limits).

(A fifth gap -- the JIP joiner leaving left the host's Remote player slot seated, so the game
never re-listed -- was fixed in review via `Oracle.ReleasePlayer(ControlDevice.Remote)` in the
listedSession teardown, so it is NOT a follow-up.)
