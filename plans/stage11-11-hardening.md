# Stage 11.11 — N-peer hardening (card `6fb406bc`)

Final card of the 3-4-machine online co-op epic (`plans/4p-online-coop.md`). The TURN go/no-go the card title carries is **already decided** — STUN-only stays (owner ruling 2026-08-21, recorded in the design doc's banner: "resolved as a deferred follow-up card, gated on real-world lobby-formation failure reports — 11.11 loses that item"). That deferred follow-up card was never actually filed; filing it is part of this card's close-out, not an implementation item.

## Context

11.7-11.10 shipped the N-peer transport, session, relay and lobby. What 11.9 explicitly deferred here:

1. **Relayed-channel interp delay.** On the star topology a client's view of ANOTHER client's ship goes client → host → client, adding ~½(RTT_A+RTT_B) plus a host re-send quantum (up to one 33 ms `StreamIntervalMs` beat). Every ship channel renders `InterpDelayMs` (100 ms) behind its newest sample; on a relayed channel that cushion is too small, so those puppets ride the extrapolation cap instead of the buffer. `RelayPeerShips`'s own comment says "the added latency budget is card 6fb406bc's".
2. **Bandwidth: measure, don't estimate.** The design doc's N=4 host uplink estimate is ~33 KB/s payload (~70-100 KB/s with SCTP/DTLS/UDP/IP headers at these packet sizes). Nothing measures actual bytes anywhere.
3. **The N=4 multi-process rig.** `tools/sim/net_npeer_smoke.py` runs three eahl processes (host + 2 joiners); its own header says "the full N=4 soak/bandwidth pass is card 6fb406bc".
4. **`bufferedAmount` back-pressure.** `webrtc.js` sends unconditionally. When a link stalls, SCTP queues even unreliable-channel sends, so stream frames arrive as an ever-later backlog instead of being dropped — the stream lane's whole contract is that consumers tolerate drops, and a stale ship frame is worse than none.

## Design

### A. Relayed-channel interp delay — a wire bit + a 150 ms budget

- `NetProtocol.ShipFlagRelayed = 1 << 3` (bit 3 of `MsgShipState`'s flags byte is free). `EncodeShipState` gains `bool relayed = false`; `TryDecodeShipState` surfaces it as `ShipSample.Relayed`. **No protocol bump** — a spare bit in an existing byte, degrading to the pre-card behaviour (100 ms cushion) in both directions; the `ShipFlagScriptGate` precedent, and no peer ever sees a mismatched build anyway (hash handshake).
- `RelayShipSample` (the one host hub re-send) sets it. Nothing else does.
- `ShipChannel` gains `public bool Relayed`, latched in `HandleExtraShipFrame` from the newest sample (only extras channels can be relayed — a client's primary channel is the host's own ship, one hop; a bus-medium client refuses non-host senders entirely, so the relay is the only writer of a relayed channel).
- `AdvanceShipClock` targets `NewestMs - InterpDelayFor(ch)` where `InterpDelayFor` = `ch.Relayed ? RelayedInterpDelayMs (150) : InterpDelayMs (100)`. Fixed 150 per the design doc's own number, not jitter-derived — the extra hop is a known ~½RTT + one relay beat, and an adaptive delay would move the 2-peer feel that is already tuned.

### B. Bandwidth accounting — counters at the transport decorator

- `NetImpairment` is already the one choke point every session send/receive passes through (it decorates whatever transport the session runs on). It gains cumulative byte counters per lane (`TxStreamBytes`/`TxReliableBytes`/`RxStreamBytes`/`RxReliableBytes`) and an `int BroadcastFanout` (default 1): an addressed send counts `payload.Length` once, an UNADDRESSED send counts it × fanout — because the broadcast really goes out once per connected peer at the JS/socket layer, and that multiplication is exactly what the N=4 estimate is about. RX counts at arrival, before the impairment's own loss roll (impDrop is reported separately).
- `NetSession.Update` refreshes `impairment.BroadcastFanout = UpPeerCount()` on the send cadence; the 5 s metrics block copies totals into `NetMetrics` and computes rates over the report interval. The `[net]` line gains ` txB= rxB= txBps= rxBps=` (appended fields; every existing probe greps named tokens, so appending is safe). Payload bytes only — header overhead stays a documented ×2-3 multiplier; the wire's actual UDP framing is not visible from C# and does not need to be for a go/no-go on "is the host's uplink sane".

### C. `bufferedAmount` back-pressure — JS-side, stream lane drops

- `webrtc.js`: one `chanSend(ch, rel, bytes)` helper used by `send`/`sendTo`. Stream lane: if `ch.bufferedAmount > STREAM_BUF_LIMIT` (16 KB ≈ 1.5 s of a whole N=4 uplink), drop the frame and count it — the stream lane is declared drop-tolerant and every consumer already self-heals. Reliable lane: never dropped (contract), but its `bufferedAmount` high-water mark is tracked and a one-shot `console.warn` fires past 256 KB, so a wedged link names itself.
- `eaRtc.netStats()` returns `{streamDropped, streamPeak, relPeak}`; `eaRtcBackpressureTest()` drives `chanSend` over fake channel objects (open/closed × under/over threshold × both lanes) in the `eaFps.test` idiom — the JS layer has no headless runner, so a pure-function self-test callable from any boot is the regression guard, plus the Chrome smoke.

### D. The N=4 rig — `net_npeer_smoke.py` grows a third joiner + the soak

- The smoke becomes host + **3** joiners (`--net-peers 3`), i.e. the full four-machine star on LocalSocketNet. Phases: (1) four mirror-image four-seat rosters, every world holding all four ships, `[netpeers] n=3`, `dupBad=0` everywhere; (2) a ~30 sim-second soak that reads the host's new `txBps`/`rxBps` off the `[net]` line — the measured N=4 payload figure, asserted nonzero and printed for the record; (3) joiner2 killed mid-level — host and BOTH survivors free exactly its seats and play on.
- `net_jip_sync.py` stays 2-process (its own header's rule).

## Verification

- `NetWireTest` section 5: round-trip the relayed bit (floor raised) → covers `ProbeNetWire` / `net_wire.txt` / `net_selftests.txt`. New section: `NetImpairment` byte counters (addressed vs broadcast×fanout, rx counted before a loss drop) — Game-free, so it runs under logic_probe too.
- `NetNPeerTest` client-role section: a relayed extras frame latches the channel and its render clock targets 150 ms behind newest; an unflagged frame (host couch) stays at 100 — the negative control. Tally raised in `net_selftests.txt`.
- `python tools/sim/net_npeer_smoke.py` — the N=4 pass above.
- `python tools/sim/net_jip_sync.py --level Level2` — the 2-peer regression gate stays green.
- Full `run_probes.py`; Chrome smoke: normal boot, `eaRtcBackpressureTest()`, zero console exceptions.

## Close-out (no code)

- File the deferred TURN card the 2026-08-21 owner ruling asked for (gated on real-world lobby-formation failure reports; coturn on the Hetzner box is the option on the table), cross-referencing 4717d3cf + this card.
- Docs: net CLAUDE.md (new 11.11 section + "Remaining" para), `plans/4p-online-coop.md` banner (11.10/11.11 shipped status).

## Out of scope

- TURN itself (decided: STUN-only stays).
- Adaptive/jitter-derived interp delay (fixed 150 ms budget; revisit only off real-network playtest reports).
- Host-side relay pacing/coalescing, reliable-lane throttling, host migration.
