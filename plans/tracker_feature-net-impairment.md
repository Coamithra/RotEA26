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
- [x] Read linked plan (plans/stage11-online-coop.md, relevant sections)

## Phase 2: Research
- [x] Read the 11.1 transport seam: `INetTransport` (or equivalent) + BroadcastChannel dev transport
- [x] Read `NetSession.Update` — clock source, lane usage, where rx is drained
- [x] Read `NetProtocol` — lane split (stream vs reliable), sequence numbers
- [x] Read the `[net]` metrics line (extrap / pops / seqGaps) — where produced, how surfaced
- [x] Read `DebugFlags.cs` — URL flag parsing conventions
- [x] Read an existing live slider panel (`?wctune` / `eaWalls`) — outside-`#app` HTML panel pattern
- [x] Check overlap with wt1's in-flight 11.4 edits (NetSession/NetProtocol/WebRtcTransport)
- [x] Summarize findings

## Phase 3: Design
- [x] Write `plans/net-impairment.md` (context / design file-by-file / verification / out of scope)
- [x] User approval
- [x] Comment approach TLDR on card 40334a8f

## Phase 4: Implement
- [x] `NetImpairment` wrapper around `INetTransport` (delay queue + RNG drop, rx-only)
- [x] Per-lane policy: stream lane = loss + delay; reliable lane = delay only (stays ordered/guaranteed)
- [x] Drain delay queue on `NetSession.Update` real-time clock
- [x] `?netlag=<ms>&netloss=<0-100>` URL flags in `DebugFlags.cs`
- [x] `eaNetSim` live panel (index.html, outside `#app`) + `eaNetSim(lag, loss)` console entry
- [x] Zero impact when flags absent (shipped builds unchanged)
- [x] Update `web/EvilAliensWeb/CLAUDE.md` with the new flags/panel

## Phase 5: Verify
- [x] Clean Debug build
- [x] Isolation sim of the delay queue / drop policy (data, not frames) — per verification rules
- [x] Two-tab BroadcastChannel session: `[net]` extrap/pops/seqGaps rise under impairment,
      return to healthy at 0/0
- [x] Reliable lane: delayed but never dropped, never reordered
- [x] Zero console exceptions in real Chrome (foreground), port 5283
- [x] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review`, fix every finding
- [ ] Merge main into branch (expect conflicts with 11.4 if it landed), re-verify
- [ ] PR + self-merge, fast-forward root
- [ ] Kill dev server, remove worktree + branch, delete tracker + plan doc
- [x] Card → Done + closing comment; follow-up cards

## Phase 7: Clean up
- [ ] Stop dev server on 5283, close verification Chrome tabs

## Notes / risks
- **wt1 conflict risk:** Stage 11.4 (card f74a2317) is in flight in `wt1` with uncommitted edits
  to `NetSession.cs` / `NetProtocol.cs` and new `WebRtcTransport.cs`. This card wraps the same
  seam. Expect conflicts on the Phase 6 merge; the "works identically over WebRTC" half can only
  be verified once 11.4 lands.

## Review findings (2026-07-24, /review cold agent)

9 findings: 0 blockers, 3 should-fix, 6 nits. All actioned.
- FIXED (real bug): Pump's back-to-front due-scan + ReleaseAt-only sort delivered
  same-millisecond stream packets in REVERSED order. NetSession sends MsgShipState and
  MsgWorldSnapshot on the stream lane in the SAME Update tick, so this fired constantly and
  fabricated seqGaps -- inside the very tool built to measure packet loss. Now a forward
  compacting scan + an arrival-counter tiebreaker (List.Sort is an unstable introsort).
  The self-test now injects two same-ms stream packets per iteration as the regression guard.
- FIXED: SelfTest packet count clamped (2-byte seq aliased past ~20k); panel clamps
  URL-seeded values to the slider bounds; [net] line reports the wrapper's effective
  settings rather than DebugFlags; MathHelper.Clamp + NaN guard replaces two hand-rolled
  clamps; Close() is a full reset; bounded panel push-retry; CLAUDE.md re-wrapped;
  plan doc's "release order" claim corrected (per-lane, reliable drained first).
- DISAGREED: slider bounds duplicated between the panel markup and the C# consts -- that is
  the house pattern for every existing panel (eaWalls/eaSpider/eaWcTune); unifying it is a
  cross-cutting change, not this card's business.

## NOT verified (user cut the browser pass short)

The post-review fixes are BUILD-VERIFIED ONLY. Not re-run: eaNetSim.test(...) and the
two-peer [net] run. Highest residual risk is the Pump ordering rewrite -- one console call
(`eaNetSim.test(150, 0, 0, 400)`) on any ?net boot settles it: stream reorder must read 0
at jitter 0.
