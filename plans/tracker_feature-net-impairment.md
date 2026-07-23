# Tracker: feature/net-impairment (card 40334a8f)

Card: **Net: fake lag + packet loss dev knobs** — artificial network impairment behind the
`INetTransport` seam so interpolation / claims / reliable-lane behaviour can be tested under
bad conditions.

Slot: `wt3` · dev port `5283` (no launch config — run DevServer by hand).

## Phase 1: Pick Up the Card
- [x] Claim card 40334a8f (atomic `grab`) → In Progress
- [x] Pull latest main
- [x] Read card description
- [x] Create worktree wt3 + branch feature/net-impairment, push -u
- [ ] Read linked plan (plans/stage11-online-coop.md, relevant sections)

## Phase 2: Research
- [ ] Read the 11.1 transport seam: `INetTransport` (or equivalent) + BroadcastChannel dev transport
- [ ] Read `NetSession.Update` — clock source, lane usage, where rx is drained
- [ ] Read `NetProtocol` — lane split (stream vs reliable), sequence numbers
- [ ] Read the `[net]` metrics line (extrap / pops / seqGaps) — where produced, how surfaced
- [ ] Read `DebugFlags.cs` — URL flag parsing conventions
- [ ] Read an existing live slider panel (`?wctune` / `eaWalls`) — outside-`#app` HTML panel pattern
- [ ] Check overlap with wt1's in-flight 11.4 edits (NetSession/NetProtocol/WebRtcTransport)
- [ ] Summarize findings

## Phase 3: Design
- [ ] Write `plans/net-impairment.md` (context / design file-by-file / verification / out of scope)
- [ ] User approval
- [ ] Comment approach TLDR on card 40334a8f

## Phase 4: Implement
- [ ] `NetImpairment` wrapper around `INetTransport` (delay queue + RNG drop, rx-only)
- [ ] Per-lane policy: stream lane = loss + delay; reliable lane = delay only (stays ordered/guaranteed)
- [ ] Drain delay queue on `NetSession.Update` real-time clock
- [ ] `?netlag=<ms>&netloss=<0-100>` URL flags in `DebugFlags.cs`
- [ ] `eaNetSim` live panel (index.html, outside `#app`) + `eaNetSim(lag, loss)` console entry
- [ ] Zero impact when flags absent (shipped builds unchanged)
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` with the new flags/panel

## Phase 5: Verify
- [ ] Clean Debug build
- [ ] Isolation sim of the delay queue / drop policy (data, not frames) — per verification rules
- [ ] Two-tab BroadcastChannel session: `[net]` extrap/pops/seqGaps rise under impairment,
      return to healthy at 0/0
- [ ] Reliable lane: delayed but never dropped, never reordered
- [ ] Zero console exceptions in real Chrome (foreground), port 5283
- [ ] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review`, fix every finding
- [ ] Merge main into branch (expect conflicts with 11.4 if it landed), re-verify
- [ ] PR + self-merge, fast-forward root
- [ ] Kill dev server, remove worktree + branch, delete tracker + plan doc
- [ ] Card → Done + closing comment; follow-up cards

## Phase 7: Clean up
- [ ] Stop dev server on 5283, close verification Chrome tabs

## Notes / risks
- **wt1 conflict risk:** Stage 11.4 (card f74a2317) is in flight in `wt1` with uncommitted edits
  to `NetSession.cs` / `NetProtocol.cs` and new `WebRtcTransport.cs`. This card wraps the same
  seam. Expect conflicts on the Phase 6 merge; the "works identically over WebRTC" half can only
  be verified once 11.4 lands.
