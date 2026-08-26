# CLAUDE.md — web/EvilAliensWeb/Compat/Net (the online co-op net layer)

Moved out of `web/EvilAliensWeb/CLAUDE.md` so it loads only when working on the net layer.
The parent file has the rest of the game/engine notes; `NetStatusMenu.cs` lives in
`Game/EvilAliens/` but belongs to this feature. Design doc: `plans/stage11-online-coop.md`.

Path bases below, since the text was written one level up: `plans/`, `tools/` and `server/`
are repo-root-relative; `Compat/`, `Game/` and `wwwroot/` are relative to `web/EvilAliensWeb/`.

Distributed-authority state replication (NOT lockstep): each peer owns its own ship
completely (input read untouched, zero added latency); the wire carries ship STATE, never
inputs; the other peer's ship is an interpolated puppet.
**Shipped so far** -- grounded in the board's Done list; most card ids also name a bullet below.

- **Stages 11.1-11.4:** net skeleton + ship mirroring over a BroadcastChannel loopback (11.1);
  host world authority -- client enemy puppets, world snapshots, generous claims, score sync
  (11.2); level-script beat replication, host-broadcast reset/victory, replicated pause,
  TeamChallenge soft tether (11.3); real WebRTC transport, room-code signaling on the shared
  VPS, menu-driven Host/Join lobby, build-hash handshake, match-end semantics (11.4).
- **Stage 11.8 -- `PeerChannel` + ONE ship path (card `b2828be8`, protocol v23):** the ~20
  per-peer singletons (`PeerUp`, liveness, stall, remote pause + kick clock, the granted primary
  slot, identity token, script gate, event seq, the ship buffer/puppet) live on an internal
  `PeerChannel` keyed by transport senderId, and `ShipChannel` unifies `FriendChannel` with the
  primary-remote state -- the static public API is a facade over it. On the wire EVERY ship frame
  is the slot-keyed `MsgShipState` (34 B, leading slot byte, `ShipFlagPrimary` marks the sender's
  heartbeat frame); `MsgFriendState` is RETIRED (0x11 reserved). One receive path routes by the
  flag, one `DriveShip` drives every remote ship; the primary-vs-extra asymmetries (alive-edge vs
  timeout death, respawn clear vs resume-gap clear) are per-CHANNEL behaviour now. Deliberately
  BEHAVIOUR-NEUTRAL at 2 peers. Also folded
  in (card a5b1e941): `MsgHudState` carries the combo TIMER's remaining time, so the observer's
  combo readout fades in phase with the owner's.
- **Stage 11.9 -- the N-PEER SESSION (card `87242257`, protocol v24):** the session genuinely
  holds a peer SET on the star topology -- a host hub with up to 3 clients, per-peer
  hello/welcome + roster grants, per-peer liveness, pause as a set, the host relay of client
  ship/HUD state and symmetric events, ADDRESSED catch-up, and the NEW MATCH-END POLICY (host
  leaves -> match ends; a client leaving frees its seats everywhere -- `EvPeerLeft` -- and play
  continues). See the "N-PEER SESSION" section below for all of it.
- **Stage 11.10 -- the LOBBY + BROWSER UX for 3-4 machines (card `0257f8ba`, protocol v25):**
  the REAL rooms hold four machines now (the menu lobby hosts at `Oracle.MaxPlayers`;
  listed/JIP rooms open at webrtc.js `LIST_ROOM_MAX` 4; a >2 host KEEPS its signaling ws for
  the room's whole life, so a freed seat is replaceable). The host waits in a LOBBY PANEL
  (room code + live roster + Start Game) instead of launching on the first pairing; the join
  side's waiting panel shows who is in (`EvLobbyRoster`, event 28); a peerless menu lobby
  SURVIVES instead of Stopping. LISTING lost its `!NetSession.Active` term -- a HOST session
  with a free seat stays advertised (a menu-lobby game mid-level ADOPTS its own signaling room
  as the listing), so a 3rd/4th stranger joins the running match: `PeerConnected` JIP-launches
  a late arrival on any menu/listed session with a live scene. The host pause menu's kick rows
  are PER PEER ("Kick Player 2", `KickPeerAt`) and its room toggle rides along mid-session.
  See the LOBBY & CAPACITY section below.
- **Stage 11.5 round 1** (card `4717d3cf`): the hardening pass -- powerup pickups replicate to
  the collector's HUD slot, ONE match-end path, a drop-verdict grace window with a
  waiting-for-peer banner, and the WebcamAliens net-lobby refusal explains itself. The graceful
  reject is card `3dedf206`, an 11.4 review follow-up folded into this stage.
- **Public game browser + join-in-progress** (`2001fbd8`; two-window pass `c0398370`): a running
  single-player game LISTS itself, strangers browse and join mid-level, ping is measured; a late
  joiner is caught up on deep mid-level background/doodad state (`45a4e48d`).
- **Four seats -- local co-op AND online co-op at once** (`4d904410`): host-allocated,
  identity-mapped roster slots, couch players on either peer, 4-wide score sync. Hardened by the
  slot-grant negotiation (`c0229c57`, protocol v8) and the stale-menu-roster reset (`ee96ea61`).
  **4-player online already works** as two peers with a couch partner each (`2e0f908b`); 3-4
  separate MACHINES do not.
- **Host kick / kick+block** (`0b8a300b`): the host's only agency under a remote pause, on a
  self-reported peer-identity token (protocol v6). **The host's pause menu now reaches it
  deliberately** (`0d6ffe70`, closing `98217618`), alongside an open/close-room toggle -- no
  new protocol, both halves are second doors onto existing machinery.
- **Score + per-slot HUD correctness:** a slot's combo and powerup progression belong to its
  OWNER (`1a3ad45a`, v9, `MsgHudState`); a late powerup claim no longer strands a HUD icon
  (`a8c92fb9`); the OPTION SHIP population is owner-authoritative too, per orbit layer
  (`c5228350`, v16); and since **`af96bcc2` (v20) the SCORE follows the same rule** -- one
  writer per slot, the owner's declared total riding `MsgHudState`, with the whole
  provisional-ledger reconciliation (`b0ab09ec`, v7) deleted. See the score bullet under
  "Claims, score & per-slot HUD".
- **Transient feedback -- the beats a frozen puppet could never reach** (`43e85936` / `57ea30cd` /
  `ee939dd1` / `8d063d33` / `c146422f`): boss + asteroid hit flashes, the Ball detach burst, enemy
  laser fire, the DANGER/WARNING arrows the bosses spawn, the big-UFO and JunkBoss charge glows,
  and Level 2's bees ambience. **One new event type (`EvFx`) for the whole family and NO protocol
  bump** -- see the transient-feedback bullet under "Protocol, NetIds & the replicable set".
- **The vanishing laser UFO** (`9ccfe295`, with `54e9a590`, v18): a client's replicated beam
  had no EMITTER, so a big laser UFO shot itself dead on the joiner -- and the unattributed
  claim that followed deleted the host's live copy with no death FX at all. See the
  unattributed-claim bullet under "Claims, score & per-slot HUD".
- **Per-peer presentation effects** (`7a8ec0d3` / `a66e190a`, v15): a floating score is the
  KILLER's alone, and the 1up slow motion is the WORLD's -- see the presentation-effects
  bullet at the end of "Claims, score & per-slot HUD".
- **The respawn indicator crosses the wire** (`37f3a663`, v17): a dead player's clock ring is
  drawn on BOTH screens, so you can see your buddy coming back and where -- see the
  respawn-indicator bullet at the end of "Claims, score & per-slot HUD".
- **A lobby pairing survives a level that PLAYED ITSELF OUT** (`3b6c12e7` for a win, `c600c55a`
  for a Mission Failed; no protocol change either time): both peers walk back to the lobby with
  the session up and the host picks the next mission, instead of the match ending -- and the
  remote ship now flies off in the level's own spawn direction rather than always upward
  (`b4a9fe60`). Three bullets at the end of "Signaling, menu lobby & handshake".
- **Level 1's intro cinematic plays on BOTH peers** (`8a7772d6`): the host's scripted no-ship
  phase is replicated as a `MsgShipState` flag bit, so neither ship is on screen until the
  cutscene ends and then both fly in together -- and the hail of bullets, which cannot replicate
  at all, is mirrored as a seeded cosmetic volley (`EvIntroVolley`). No version bump.
- **Diagnostics + rigs:** fake lag/loss/jitter (`40334a8f`), the snapshot unknown-id split and
  `snapTurn` (`48ab9b2f`), decorative swarms as one on/off beat (`9a3175d0`, v10), the
  standing-purge-filter races (`74403f83`), the signaling server deployed (`8c3c18da`).
- **Level-3 walls stop diverging** (`4392bd30` / `80749dc4`): a wall DERIVES its scale from the
  grid variation the wire already carries, so the base state's u16-at-1/256 copy (4.9% out on the
  12-wide grid, 402px of divergence down it) stops being applied; the collision grid takes its tile
  size from the wall, closing the joiner-local hit-before-you-touch-it gap; and the scroll is
  anchored. No protocol change -- see the LEVEL-3 WALLS section.
- **The snapshot lane stops dragging puppets backwards** (`f5cf7a5c`, v19): `MsgWorldSnapshot`
  carries a monotone per-PACKET seq and an entry older than the one already applied to that netId
  is refused -- the guard `NetFrameLocal` gave animation frames, which positions never had. Same
  card raises `NetBaseState.Scale` 1/256 -> 1/4096 and makes it ROUND, in the same u16. See the
  SNAPSHOT STALENESS section.
- **The scripted-position bosses announce their own velocity** (`76ec8bdb`, no protocol change):
  a type that writes `Position` directly gets a third source of truth, so a SpiderBoss fly-by
  stops being dead-reckoned on a zero or on a whole-turn-stale difference -- mean puppet lag and
  pops both roughly halve. See the SCRIPTED MOTION section.
- **Puppet smoothness** (`c92f3817` / `0dfc4495` / `d3add86f` / `8dabe812` / `0108d1fc`), and its
  wire-first successor: the host now MARKS a reposition instead of the observed-velocity estimator
  guessing at one (`e79bb994`, v13) -- see the teleport-marker bullet under "Puppet SMOOTHNESS".

**Remaining.** THE EPIC IS COMPLETE -- 11.7-11.11 all shipped (the hardening pass `6fb406bc`,
11.11, closed it: relayed-channel interp delay, the measured N=4 bandwidth soak, the
four-process rig, `bufferedAmount` back-pressure -- see "STAGE 11.11 HARDENING" below). What is
left is REAL-WORLD, not code: the four-machine WebRTC flow has had no four-real-browsers
playtest, and interpolation/jitter FEEL still needs real-network play (card `4717d3cf`, "For
me"). TURN is DECIDED deferred (owner rulings 2026-08-21 + 2026-08-23: STUN-only stays, and NO
self-hosted coturn ever -- the shared VPS has no bandwidth budget for relaying game traffic; if
real-world lobby-formation failure reports land, the candidates are a free-tier managed TURN
service wired into the ICE config, or accepting the failure rate with its clean error). Open
net cards in Backlog: `ac375753`
(two-window net pass), `25ad0659` (headless net sim + de-static refactor) and `1cd47879` (a
single-tab live browser pass -- only its IndexOutOfRange block is net). Deferred to "Later" rather than
stage-sequenced: `816a8286` (replicate mechanical-friend ships), `1ec29347` (mid-boss arrival
puppet fidelity), `2da92af9` (public-list abuse bounds), `6451ceaf` (a second KEYBOARD player for local co-op).
`98217618` (kick a peer who is not pausing) was in that list and is DONE -- card `0d6ffe70`
shipped its UI half as the host pause menu's Online Play row; see the kick section.

## Core debug flags (per-feature flags live with their feature)

- **Flags:** `?net=host` / `?net=join` opt a session in (in `Active`); `?room=<name>` picks
  the loopback room (BroadcastChannel `eanet-<room>`, default `dev` -- parallel test pairs
  must use distinct rooms); `?netlog` = verbose per-event logging; `?aiplayer` forces the
  LOCAL ship onto the existing AI branch (`PlayerShip.EffectiveController`) for unattended
  soak tests; `?aifriends=<0-3>` (pair with a `?level=` boot) seeds `Settings.Friends` so the
  host's Mechanical-Friends AI ships auto-join without the cheats menu -- the two-tab seam for
  AI-friend replication (note the budget is `Friends+1` TOTAL ships incl. the remote, so with a
  peer connected you need `aifriends>=2` to spawn any AI friend); `?netscript` (pair with `?level=Level1`) replaces the level's event list with
  a compressed ~60s script firing every replicated beat type (message, warning, background
  ops incl. a whole-SCENE swap to the alien base, checkpoints, music switch, victory) -- the
  purpose-built two-tab verification for script replication
  (`GameScene.PopulateNetScriptTest`). It looks nothing like Level 1 on purpose: it is a beat
  rig, and the alien-base swap is what gives the floor-texture and scene legs any coverage. Card 11.4 adds `?rtc` (a
  `?net=` boot uses the REAL WebRtcTransport: host prints its room code to the console,
  join passes it via `?code=ABCDE`) and `?signal=<url>` (override the signaling server;
  a local rig runs `uvicorn main:app --port 8091` in `server/signal` and boots with
  `?signal=ws://localhost:8091/ws`). Card 40334a8f adds `?netlag=<ms>` / `?netloss=<0-100>`
  (impair INBOUND traffic -- see the impairment bullet below) and `?netsim` (show the live
  impairment panel; the knobs work without it). `?netfakehash=<s>` (card
  4717d3cf) overrides THIS tab's build-hash fingerprint so two dev tabs disagree, driving the
  real `peerHash`-mismatch -> reject flow (`RejectBuild` -> "update required") on the
  BroadcastChannel rig -- otherwise both tabs read `'dev'` and never mismatch (the two-tab
  verification for the reject handshake + its teardown grace). **No `?net` flag = the net layer is never constructed
  -- a plain boot is byte-identical single-player, and single-player NEVER contacts any
  server. Hard invariants; keep them.**

## Transport & artificial impairment

- **Transport is an interface** (`Compat/Net/INetTransport`): a STREAM lane
  (unreliable-class -- consumers must tolerate drops/reorder) + a RELIABLE lane (ordered,
  guaranteed), `OnData`/`OnPeerBye` events. Impl #1 `BroadcastChannelTransport` ->
  `NetInterop` ([JSInvokable] shim, the WebcamInterop pattern) -> `eaNet` in `index.html`
  (channel only constructed when opened; still the default dev rig). Impl #2 (card 11.4)
  `WebRtcTransport` -> `WebRtcInterop` -> `eaRtc` in `wwwroot/webrtc.js`: JS owns the
  RTCPeerConnection + signaling WS + the join-code overlay; two DataChannels map to the
  lanes ("s" unordered `maxRetransmits:0`, "r" reliable). A 1-byte `0x00` reliable frame
  is the JS-level pagehide "bye" (0x00 is reserved -- C# msg types start at 0x01). STUN =
  free Google servers, NO TURN (~10-15% of NAT pairs get a clean "could not connect"; the
  go/no-go is DECIDED deferred -- owner rulings 2026-08-21 + 2026-08-23, no self-hosted
  coturn ever; the "Remaining" paragraph at the top of this file has the standing terms).
  Nothing above the interface may assume loopback
  reliability.
- **The transport layer is N-PEER since card `583a3ef8` (Stage 11.7) -- the SESSION above it is
  still 2-peer.** The first card of the `plans/4p-online-coop.md` epic, and deliberately
  behaviour-neutral for every shipped flow (protocol version unchanged, `NetSession` untouched).
  What changed, outermost first:
  - **`INetTransport` grew addressed sends** -- `SendStreamTo(peerId, ...)` /
    `SendReliableTo(peerId, ...)`. The senderId `OnData` reports IS the address (no separate
    naming scheme); an unknown/departed/self/wrong-room target is a SILENT drop, the
    closed-DataChannel semantic. The unaddressed pair is documented as fan-out over all
    connected peers (≡ unicast at one peer). `OnPeerBye` now carries the DEPARTING PEER's id on
    every per-peer departure -- with one exception: `WebRtcTransport`'s TERMINAL whole-link
    failure keeps its legacy "phase:reason" string (all peers are gone in that case), so a
    consumer routing byes by id must treat an unrecognized string as "every peer".
    (`NetSession` keys its channels off the senderId since `b2828be8` and routes byes by it --
    honouring the unrecognized-string rule -- since `87242257`.)
  - **`webrtc.js` holds a MAP of peer entries, not singletons** -- host: one `{pc, chS, chR}`
    triple per joiner, keyed by the server's joiner id (1..3, monotone, never reused); joiner:
    exactly one entry (the host). SenderIds: `"1".."3"` host-side, `"h"` joiner-side.
    `eaRtc.host(url, max)` requests room capacity (clamped 2..4, default 2). For a MAX-2 room
    the signaling WS closes when the room is full (`connectedCount == max-1`) -- exactly the
    old first-peer close; since card `0257f8ba` a >2 host NEVER closes it (not even while
    momentarily full: seats free when a peer leaves mid-match, and a closed ws would mean the
    server room and its code died with the third arrival), so a lost peer IS replaceable. A
    single peer's loss on a bigger host is the `peergone` phase (detail = its id) and the JS
    layer plays on. Every max-2 flow (joiner, max-2 host, last-peer-gone) keeps the old
    terminal `closed`/`failed` behaviour exactly. Listed/JIP rooms open at `LIST_ROOM_MAX` 4
    and the menu lobby hosts at `Oracle.MaxPlayers` since card `0257f8ba`; `eaRtc.list` on a
    socket a live room already owns ADOPTS it as the listing (advertise + beat on the same
    room) -- the menu-lobby game mid-level listing itself under the code its friends joined by.
    **GOTCHA (found live on the 3-tab rig, fixed): a `{t:gone,id}` from the server means "that
    SIGNALING seat emptied", not "that peer left"** -- a joiner deliberately closes its ws the
    moment P2P is up (the shipped flow), so post-connect the frame is EXPECTED and must be
    ignored (the link's own liveness governs: bye frame, channel close, the C# stream timeout);
    only a PRE-connect gone tears the pending pc down. Routing it to `peerGone` unconditionally
    killed every freshly-connected peer ~instantly. Consequence: an N-room's signaling seats
    only ever hold pre-P2P joiners (each vacates on connect and the room re-advertises), so seat
    count is NOT session membership -- session capacity is the host roster's job (card `87242257`).
  - **The signal server holds host + up to 3 joiners, capacity HOST-REQUESTED** (optional `max`
    in `{t:host}`, clamped 2..4, default 2 -- so a shipped client's room can never admit a 3rd
    machine; the card's gotcha). Joiners get monotone ids; the host's `{t:peer}` gains `id` (the
    one wire-visible delta to old clients, provably ignored); relay frames carry `from`
    (joiner->host, max>2 rooms) / require `to` (host->joiner, max>2 rooms) while max-2 rooms
    keep today's VERBATIM relay byte-for-byte; a joiner dropping from a max>2 room frees its
    seat (`{t:gone,id}`, room survives and re-lists) where max-2 teardown is unchanged. Either
    deploy order (site first / server first) is protocol-safe -- enumerated in
    `plans/4p-online-coop.md` and pinned by `server/signal/test_signal.py` (50 cases).
  - **`tools/headless/LocalSocketNet` serves up to `--net-peers <1..3>` clients** (default 1 =
    the old behaviour, so `net_jip_sync.py` and every committed probe run unchanged); accepted
    peers get monotone ids `peer1..`, the dialling side's one remote keeps `"peer"`.
  - Pinned by `NetWireTest` section 1b (addressed sends at N=4 -- reach EXACTLY the target, the
    others get NOTHING, the silent-drop counter arithmetic, room isolation, the impairment
    pass-through; floor 118 since card 6fb406bc, in `ProbeNetWire` / `net_wire.txt` / `net_selftests.txt`) and by
    `test_signal.py`'s N-join/leave/fan-out/capacity cases with the old byte-equality cases kept
    verbatim as the shipped-protocol mutation controls.
- **Impl #3 `InMemoryTransport` (card 25ad0659) is the HEADLESS one: N endpoints in ONE process,
  no browser and no JS.** Created only by `NetWire(int peers)` (its owner and switch, max 8);
  `wire[i]` is the endpoint. `NetSession.StartWith` already takes an arbitrary `INetTransport`, so
  a scenario can put one end into a live session and drive the other by hand (the reachable entry
  points are `StartMenuSession` / `StartListedSession` and, for a scenario, `StartForTest`;
  `StartWith` itself is private).
  - **The peer count is a PARAMETER, not 2.** Per-`(src,dst)` queues and a fan-out `Dispatch`,
    even though the protocol is 2-peer, so the N-peer stages (`plans/4p-online-coop.md`,
    11.7-11.11) add contexts rather than rebuilding the rig. Anything written against it must
    index peers, never name `a`/`b`.
  - **Delivery is on the RECEIVING endpoint's `Pump`, never inline on the send.** That is what
    makes event ORDERING assertable, and `NetWire.Pump()` captures EVERY endpoint's budget before
    draining ANY of them: draining in index order while capturing per endpoint as its turn came
    round still let a send to a HIGHER-indexed endpoint arrive inside the same `Pump` (a same-tick
    round trip no real transport can do). Measured -- with the per-endpoint budget, a
    "reply waits for the next Pump" assertion passed whether the budget was there or not, and
    only the upward direction ever discriminated. `Pump(int budget)` drains ONE peer, which is how
    a peer is made to lag a tick behind.
  - Payload is cloned **per recipient**; rooms isolate (a send only reaches endpoints opened on the
    same room string, so one wire can host two pairings and `?room=`'s property is tested rather
    than assumed -- and re-`Open`ing an endpoint on a DIFFERENT room THROWS rather than silently
    staying put); a closed endpoint is inert both ways. `TxSent` counts calls and `TxFanout` the
    enqueues they produced -- **neither is a delivery count** (delivery moves `RxDelivered` on the
    recipient's Pump, and `Close()` drops what was still queued), so never assert
    `TxFanout == RxDelivered` as "everything got through".
  - **The bye is NOT ordered against data** -- `Close()` raises `OnPeerBye` inline (to room-mates
    only), so it jumps ahead of anything still queued at the recipient. That matches
    `BroadcastChannelTransport` (a separate JS pagehide event) and is the OPPOSITE of
    `WebRtcTransport`, whose bye rides the ORDERED reliable channel as a `0x00` frame. So a
    scenario asserting "the peer's last `EvLeave` arrives before its bye" passes here and fails in
    play; do not write one against this transport.
  - **Verify with `eaNetWire.test()`** (`Compat/Net/NetWireTest.cs`, 77 assertions): the transport
    contract at N=2 and N=4, `NetImpairment` composed over a real endpoint (the chain production
    always builds, previously never executed outside a browser), and every codec's real frames put
    ON the wire and decoded from what the far endpoint received -- which an encode/decode pair
    cannot do, since a matching pair of wrong offsets passes one. Each positive has a truncated
    or mistyped frame beside it.
    **It is deliberately Game-free and reads NO real clock**, which is what lets it also run under
    `tools/sim/logic_probe` (`ProbeNetWire`, browserless + exit code) and makes it non-flaky;
    keep it that way. Committed as `tools/headless/probes/net_wire.txt`.
- **A scenario needing a LIVE session over that wire uses `NetSession.StartForTest` (card
  25ad0659), and `eaNetResetSpawn()` is the first one.** `StartForTest` is neither a menu nor a
  listed session, deliberately: `menuSession` makes `HandleHello` refuse a peer while
  `DebugFlags.Active` is set, and every scenario needing a live world boots with `?level=`, so a
  menu session would reject its own scripted pairing. Three read-only seams go with it --
  `LocalBuildHash` (a scripted hello must carry it; READ, never recomputed, or it drifts from
  `StartWith`'s own `?netfakehash`-aware expression), `HasRemotePuppet` and
  `HasFriendPuppet(slot)`.
  - **The scenario: `Compat/Net/NetResetSpawnTest.cs`** -- one real CLIENT session on `wire[0]`,
    a scripted host on `wire[1]`, and card 74403f83's two ship-puppet spawn sites driven END TO
    END. Its subject is that `SpawnPuppet` / `SpawnFriend` must not ADOPT a ship
    `ComponentBin.TryAdd` refused. **Only `NetApplyReset` can reach that branch**, because it
    purges from inside the rx drain where the local ship's death is still merely QUEUED, so
    `WorldTakesPuppets()` is true and the caller's gate is open; the `LoseLife` / `UpdateWin` /
    `UpdateResetting` purges are flushed by `collectionHelper.Update()` before the drain -- they
    take the local ship AND the respawn summon with them, so both gates are shut -- which the
    suite asserts as its NEGATIVE leg, by the SEATS (neither `Remote` nor `RemoteFriend` is even
    allocated, which is what tells "the caller was never entered" from "TryAdd refused").
    **Leg 1b is that leg's PAIR** (card c1cdd3e5): identically shipless, one bit different -- our
    own respawn summon is up, so both puppets ARE adopted. See "A PEER'S SHIPS BELONG IN A WORLD
    WE ARE RESPAWNING INTO" below.
  - **It is the one DESTRUCTIVE suite in this directory.** It needs a live `GameScene`, moves the
    local player's seat and applies a real `EvReset`, so the scene ends in its reset branch. It
    restores the roster and asserts it did, and refuses to run with a real session up -- but run
    it in a throwaway `?level=Level2&invuln` boot, never in a game you care about. That is also
    why it is **absent from `net_selftests.txt`** and has its own
    `tools/headless/probes/net_reset_spawn.txt`, like `netbg_catchup.txt`.
  - **It cannot run under `logic_probe`** (`Game`, `ServiceHelper` and a `GameScene`, all three
    of that tool's documented limits) -- unlike `eaNetWire.test`. eahl is its only headless
    runner.
  - **It runs on a `PinnedNetHost` since step 2a, so it reads NO real clock.** It used to (that is
    what `NetSession.Update` read), and it out-ran both windows that could bite -- the 500 ms
    `FriendTimeoutMs` and the 8 s drop verdict -- by re-sending both streams immediately before
    every `Update`; measured deterministic 31/31 over 10 consecutive runs, which is what let it
    commit as a probe. The re-sends STAY (they also keep each leg's buffers fresh, a separate
    job). Leg **3b** was added with the pin and is the only leg here that discriminates on the
    clock: it moves ONLY the virtual clock past `FriendTimeoutMs` and requires the friend puppet
    to explode while the primary remote, on the 3 s peer timeout, does not -- so it pins WHICH
    deadline fired. Leg **0b** came with step 2b and is the seam's other half: it counts the
    four services' reads THROUGH `INetHost` during `StartForTest`, which is the only place in the
    repo a real session starts headlessly and therefore the only place that read can be counted.
    42/42 now. Leg **3c** is the scene twin from 2c-i: a `RecordingNetScene` DECORATOR over the
    LIVE scene counts `EvReset`'s arrival through `NetScene.Current`. A handler left on
    `GameScene.NetActiveScene` does identical work today, so counting is the only thing that
    can see it.
- **The net cores read the clock, the dev flags AND the four services through ONE injected seam,
  `INetHost`** (card 25ad0659 steps 2a + 2b; the plan's 2a/2b/2c split is in
  `plans/net-headless-sim.md`). `NetSession`, `NetPuppets` and `NetImpairment` no longer touch
  `Environment.TickCount64`, `DebugFlags`, `WebRtcInterop` or `ServiceHelper` directly -- they go
  through `NetHost.Current`, whose production value is `ServiceHelperNetHost` and holds each
  expression verbatim. **2c was split three ways** -- `2c-i` the scene (`INetScene`, SHIPPED),
  `2c-ii` the ENTITY (`INetEntity`, SHIPPED), `2c-iii` entity creation (**MEASURED AND DECLINED**,
  next bullet). It is STILL STATIC and single-instance, and after the re-plan it stays that way
  unless step 3 is ever justified.
  - **THE SEAM IS FINISHED AT 2c-ii. `2c-iii` (entity CREATION) was measured and declined, and
    the reason generalises: its motivation was "the sim never constructs a `Game`", which is
    dead.** The harness runs under `eahl`, which HAS one, and `NetSnapshotTest` /
    `NetPuppetBench` already build and drive REAL replicable entities headlessly. Measured on
    `9bdbc5a`: moving `INetTypeDescriptor`'s four entity-typed members onto `INetEntity` is
    **~80 signature edits** (4 declarations + 6 sites in `NetTypeDescriptor<T>` + **70
    overrides** across the six descriptor files -- 29 `CreatePuppet`, 15 `ApplyStateExtra`, 15
    `EncodeStateExtra`, 11 `EncodeSpawnExtra`) for no behaviour change and no capability, since
    the sim uses the production descriptor table anyway. The 11 creation calls (6
    `Explosion.NewExplosion`, 1 `new Bullet`, 2 `new PlayerShip`, 2 `bin.Recycle<PlayerShip>()`,
    production only) all read the `game`/`bin` fields already, and faking them would make the real
    death paths vacuous -- strictly worse evidence.
  - **The three remaining `(AlienDrawableGameComponent)e.Comp` downcasts are SAFE BY
    CONSTRUCTION -- that is the invariant, not an accident to tidy up later.**
    `NetTypeRegistry.TryGet` matches the EXACT runtime type against a table whose every entry is
    an `AlienDrawableGameComponent` subclass, and `CreatePuppet` returns that type, so
    `NetPuppets.ApplySnapshotState` / `NetSession.OnHostSpawn` / `NetSession.SendWorldSnapshot`
    cannot fail. An `INetEntity` implementer that is NOT one could only reach them by being added
    to that table -- which is what would have to change first.
  - **TWO PEERS WITH INDEPENDENT WORLDS IN ONE PROCESS IS UNREACHABLE, and knowing why saves the
    next person a step-3 sizing.** `ComponentBin`'s only ctor does `collection = game.Components`,
    and `Oracle` (2 subscriptions + 5 scans) and `CollisionHandler` (2 subscriptions) bind to that
    same collection -- so two contexts under one `Game` share one world and the host context's
    `NetIdRegistry` would allocate ids for the client context's puppets. The other three services
    are already fine per-instance (`ScoreVisualiser` has ZERO `Components` references,
    `SoundManager` only stores the `Game`, `Oracle` ships `DetachFromComponents()` for
    `NetSlotTest`'s scratch roster). **So a scenario drives ONE real context and scripts its peers
    onto the wire** -- step 1b's shape -- and step 3's de-static move is off the critical path.
    Full re-plan, including what a second-collection `ComponentBin` would cost, in
    `plans/net-headless-sim.md`.
  - **`NetSession.HandleClaim` reads NO scene** -- it reaches `NetIdRegistry` / `bin` / `score` /
    `Explosion` / `NetPuppets.KillerAgent`, and `sound` on the pickup branch (card 06ac5df2 put
    the remote pickup cue back; `killable.NetKill` below plays its own cues besides). Sound is
    not a scene, so the claim scenarios stay MENU-runnable and leave-no-trace-able (the
    `eaNetSnap` shape -- audible now, like `eaNetDeathFx`'s cues), NOT
    destructive like `eaNetResetSpawn`. **That is about the transitive closure, so it was checked
    one level deeper**: `killable.NetKill` runs the real per-type `KilledBy` (explosions, cues,
    `AwardScoreToAll`), and `Boss.KilledBy` is scene-free -- but check the specific types a new
    scenario kills rather than assuming it of all of them.
  - **The entity is the THIRD seam, `INetEntity` (card 25ad0659 step 2c-ii).** 18 members
    (17 as shipped; `NetScaleLocal` joined them for the wall cards -- see LEVEL-3 WALLS)
    (the card's census measured 16 distinct ones over 42 call sites; `GetType()` is one of them
    and comes free from `object`, and the two discriminants below replace type tests rather than
    calls), implemented DIRECTLY on `AlienDrawableGameComponent` -- never
    via an adapter, which would allocate per entity on a per-puppet-per-tick path. Plus two
    sub-interfaces, `INetKillable` and `INetPickup`, because the layer's `is KillableAlien` (4
    sites) and `is Powerup` (3) are TYPE TESTS an interface cannot carry: each subtype answers
    the discriminant with `this`, the base with null.
    `PuppetInfo.Comp`, `NetIdRegistry.Entry.Comp` and the kill-note table now hold `INetEntity`.
  - **Implemented EXPLICITLY, which is the OPPOSITE of 2c-i's choice, for the opposite reason.**
    `INetScene`'s 15 members were widened to `public` because `GameScene` is itself internal, so
    widening widened nothing. `AlienDrawableGameComponent` is PUBLIC, so an implicit
    implementation would add a dozen net-only names to a game type's API to satisfy an internal
    seam. `Position` / `Enabled` / `IsDead` are already public and satisfy their members
    implicitly, for free. `scale` / `rotation` / `curframe` are public FIELDS (2008 code), which
    is the only reason `NetScale` / `NetRotation` / `NetCurFrame` exist -- an interface cannot
    expose a field. `NetCurFrame` is READ-ONLY on the seam: both writers wrap into the type's
    active frame range, and a bare setter would let a caller index off the sheet.
  - **THREE THINGS ARE OFF THE SEAM ON PURPOSE, and `INetEntity`'s header says so. They were
    filed as 2c-iii's; 2c-iii measured them and DECLINED, so they are PERMANENT, not pending.**
    (a) COLLECTION IDENTITY -- the bin add/remove calls and the two `GameComponent`-keyed maps
    cast back, VISIBLY, rather than the interface exposing a `GameComponent` and defeating itself
    in one member; that coupling is about the shared `Game.Components` (see the bullet above for
    why it is unreachable rather than deferred). (b) The DESCRIPTOR extras
    (`EncodeSpawnExtra`/`EncodeStateExtra`/`ApplyStateExtra`),
    which would mean editing a parameter type in 41 overrides across six descriptor files (eight in all,
    counting `DescriptorBase`'s three virtuals and `NetTypeRegistry`'s three declarations) for no
    behaviour change -- 70 overrides and ~80 edits once `CreatePuppet`'s return type and the
    declarations are counted, which is what the 2c-iii census weighed. So the three call sites
    cast, permanently and safely. (c) The INBOUND hooks `NoteKill` / `NotePowerupTaken` keep their concrete parameter
    types -- they are the GAME calling the net layer, and a concrete argument converts for free,
    which is why **no game call site outside `Compat/Net` changed**.
  - **VERIFICATION IS SHAPED DIFFERENTLY FROM 2a/2b/2c-i, and knowing why matters.** Those three
    redirected a lookup through a holder, so a missed site did IDENTICAL work and had to be
    COUNTED. Here the core fields changed TYPE, so a missed site does not compile: **the compiler
    is the exhaustiveness check.** What it cannot see is (i) an explicit forward wired to the
    wrong member of the same type -- `float INetEntity.NetScale => rotation;` compiles and
    silently swaps two floats -- and (ii) a subtype that stops answering a discriminant, which
    would turn every remote powerup pickup into an explosion. `NetEntityTest` (`eaNetEntity()`,
    38 assertions, a leg of `net_selftests.txt`) covers exactly those two: every member driven to
    a DISTINCT value and compared against the member it claims to front, and the `is` tests run
    beside the discriminants as the control, on four entity shapes, with a non-degeneracy check
    so a discriminant hard-wired either way cannot pass. Mutation-tested four ways, each isolated
    and each failing only the legs naming its member. Not a `logic_probe` case set, unlike
    `eaNetHost`: constructing an entity needs a `Game`.
  - **MEASURED FIRST, and the plan named the WRONG INSTRUMENT.** `plans/net-headless-sim.md` says
    to read `FrameSection.UpdNet`, but that bracket covers `NetSession.Update` + `NetListing.Tick`
    only: `NetPuppets.Drive` is called from `NetPuppetDriver.Update`, i.e. inside
    `base.Update(gameTime)`, so it lands in **`UpdComponents`** under the whole world where a few
    percent is unreadable -- while `UpdNet` sees the host's <=16-entry snapshot encode at ~16 Hz,
    a tiny phase, which is precisely the "10% of 0.3 ms is nothing" trap the plan warns about two
    sentences later. **`NetPuppetBench` (`eaNetPuppetBench(n, iters)`) is the instrument that did
    not exist**: n real puppets built through the real self-heal path, then the real `Drive` timed
    in a plain loop, reported in ABSOLUTE microseconds and as a share of the 16.7 ms budget. It
    carries a positive control (the puppets must actually have MOVED -- a `Drive` that
    early-returned would otherwise time at a beautiful 0 us) and asserts its own population.
  - **The numbers, and read the WASM row -- the desktop one understates it.** Per puppet, before
    -> after, at MATCHED run ordinals: desktop CLR (eahl) **+4% to +19% depending on N**, WASM
    (Chrome) **+25% at N=128 and +28% at N=512** (780 -> 972, 769 -> 984 ns).
    WASM is ~12x the desktop cost per puppet AND takes a bigger relative hit, so a desktop-only
    reading would have been the wrong evidence. In absolute terms at **N=512** -- far past any
    real world, since the `?flyspiders` JIP rig measures `liveIds` 17-19 and a big world is ~320
    -- the seam costs **+0.11 ms/frame in WASM, i.e. +0.66% of the frame budget**; at N=128 it is
    +0.02 ms. So the plan's DEFAULT stands: the simple direct interface wins and **the
    generic-core fallback did not earn it**. Do not re-open that without re-running the bench.
  - **`eaNetPuppetBench` is MENU-ONLY; `eaNetEntity` is NOT, and the difference is deliberate.**
    The bench builds real puppets into the world and so skips itself over a live session, level
    or attract demo exactly as `eaNetSnap` does. The entity suite constructs its four entities
    and never adds one to the bin or to `Game.Components`, so it is safe at any point in play and
    carries no gate; run it wherever you like. Both are still driven from the menu by
    `net_selftests.txt`, which is what the tallies there are measured under.
  - **GOTCHA -- the bench's FIRST invocation in a process reads ~24% high**, and the 64-call
    warm-up does not remove it (measured, desktop n=128: 8.87 us then 7.06 / 7.13 / 7.15,
    reproduced across two processes; later runs settle to well under 1%). So compare LIKE WITH
    LIKE -- both sides of an A/B at the same run ordinal, or discard each process's first run.
    The desktop delta above is of the same order as that bias, which is why **the WASM row is the
    one the verdict rests on**; it was taken at matched ordinals in fresh page loads.
    Best run under eahl either way -- no rAF paces the loop there.
  - **The scene is a SECOND seam, `NetScene.Current` (card 25ad0659 step 2c-i).** `GameScene`
    implements `INetScene` (15 members: the host-broadcast transitions, the catch-up replay, the
    three readbacks `NetSession` branches on, the kick menu and `SpawnPlayer`), and the session's
    32 `GameScene.NetActiveScene` reads go through the holder instead. **Its production value is
    DERIVED, not copied** -- `override ?? GameScene.NetActiveScene` -- so that field keeps its
    concrete type for its non-net readers (`AiBench`, `DebugInput`, `NetListing`, `BinTest`,
    `NetSnapshotTest` and `NetCosmeticTest` -- that last one NEEDS it, since
    `NetCosmeticSelfTest` is not on the interface) and there is no second source of truth
    for "is a scene up", which every world message is gated on. Null hands the seam back, as with
    `NetHost`; unlike `NetHost` there is no production instance, because "no scene" is a real
    production answer and null IS it.
  - **`NetResetSpawnTest`'s respawn stand-in is GONE with it** -- `SpawnPlayer` is on the seam, so
    both retry legs drive the real `GameScene.SpawnPlayer` and the four ways the fake differed
    from it (no `Recycle`, no `spawnType` position, `startup: false`, no cursor bookkeeping) are
    gone. That was step 1b's last outstanding debt.
  - **2b is FOUR members (`Oracle`, `ComponentBin`, `Score`, `SoundManager`), not the ~31 the
    plan's seam table implies, and the reason generalises.** The cores make 79 calls on those
    services across 27 distinct members, but they all read a field cached ONCE in
    `NetSession.StartWith` / `NetPuppets.Enable` -- so what has to move is the six
    `ServiceHelper.Get<>()` LOOKUPS (`ServiceHelper` being a process-global registry is the actual
    blocker), not the calls. Forwarding all 27 would also drag `PlayerShip` /
    `AlienDrawableGameComponent` into this interface, which is 2c's job to do properly.
    It buys nothing toward Game-freedom and is not meant to: all four service constructors take a
    `Game`, and the harness runs under eahl, which has one.
  - **A core left reading `ServiceHelper` is INVISIBLE today** -- it changes no behaviour until
    step 3 puts two peers in one process and they quietly share an `Oracle`. So the leg that
    covers 2b COUNTS reads through the seam rather than comparing values: `NetResetSpawnTest`
    leg 0b installs a read-counting host over the pinned clock during a real `StartForTest` and
    requires oracle 1, bin 2, sound 1, score 2 (bin and score twice because a client session also
    runs `NetPuppets.Enable`). Exact counts, not a floor. Copy the instrument at 2c.
  - **`eaNetHost()` deliberately does NOT cover the four**, and adding a leg for them would cost
    it the Game-freedom that makes it a `logic_probe` case set -- `ServiceHelper.Get<T>()`
    dereferences a static `Game` that loader never sets, and no `Oracle`/`ComponentBin`/
    `ScoreVisualiser`/`SoundManager` can be built there to compare against. Its header says so.
  - **A scenario swaps it and hands it back: `NetHost.Current = new PinnedNetHost()`, restore in a
    `finally`.** Assigning `null` restores production, so teardown needs no bookkeeping and the
    layer can never hold a null host (a frozen or absent clock reads as "the peer stopped
    sending"). `PinnedNetHost` is a DECORATOR -- it pins the clock (and optionally the impairment
    triple) and forwards the rest to production, so a rig made deterministic in TIME does not
    silently also change the build hash or the flags out from under the code it is testing.
  - **THREE THINGS ARE DELIBERATELY OUT OF THE SEAM.** `NetSession.Start()` still reads
    `?net=` / `?rtc` / `?room=` directly -- it is the composition root and decides whether a
    session exists at all, which no injected host can answer. `NetListing` / `NetLobby` /
    `NetGameBrowser` / `WebRtcTransport` are lobby-and-listing plumbing a scenario never
    constructs (`NetListing` keeps its own `NowMs` and, since 2b, its own oracle lookup).
    `NetWaitOverlay`'s clock read is a Draw-time pulse alpha, not cadence.
    2b adds two more of the same kind: `NetPauseOverlay`/`NetWaitOverlay`'s Draw-time
    `IContentManagerService` + `ISpriteBatchWrapperService` lookups (not among the four services
    the cores reach through), and the five sibling test SUITES -- `NetComboTest`,
    `NetCosmeticTest`, `NetResetSpawnTest`, `NetSlotTest`, `NetSnapshotTest` -- which read the
    registry on purpose, because asserting against the LIVE world is their job. Step 4's
    scenarios are the ones that go through a host.
  - **Verify with `eaNetHost()`** (`Compat/Net/NetHostTest.cs`, 32 assertions; also `ProbeNetHost`
    under `logic_probe` and a leg of `net_selftests.txt`). It asserts the two halves separately,
    because they fail differently: the production host maps 1:1 onto what each call site read
    (the impairment triple driven to three DISTINCT values, so a swap among them cannot pass),
    and the injected clock genuinely reaches the live `NetImpairment` queue over a real `NetWire`
    endpoint. **That second section is the discriminator, and its POSITIVE assertion is what
    discriminates** -- the virtual clock starts at 0, so a wall-clock read stamps arrival at the
    machine's uptime and the packet is never delivered at all. The boolean flag legs compare
    equal-to-source and so cannot tell two members wired to each other apart on a boot where both
    are false; that is stated in the suite rather than papered over.
- **THE SCENARIO HARNESS (card 25ad0659 step 4) -- `eaNetScenarios()` + `eaNetSceneOrder()`.**
  The six scenarios the design doc specced, each driving ONE REAL `NetSession` over one endpoint
  of a `NetWire` while a SCRIPTED peer drives the other with real `NetProtocol.Encode*` frames.
  Two entry points because they cost different things:
  - **`eaNetScenarios()` (`Compat/Net/NetScenarioTest.cs`, 67 assertions) is MENU-ONLY and
    leave-no-trace**, the `eaNetSnap` shape. Scenarios 1-4 run a real HOST session (the three
    generous-claim shapes plus the OneUp overlap); scenario 5 stops it and runs a real CLIENT
    session for the id churn. Real `UFO`s and `Powerup`s are planted into the LIVE bin so
    `NetIdRegistry` allocates real ids through the real `ComponentAdded` seam -- which is what
    makes the claim path non-vacuous, since `HandleClaim`'s live branch runs the real per-type
    `KilledBy`. The roster, the score panels and `Lives` are restored AND asserted restored.
    A leg of `net_selftests.txt`.
  - **`eaNetSceneOrder()` (`NetSceneOrderTest.cs`, 15 assertions) needs a LEVEL and is
    DESTRUCTIVE**, like `eaNetResetSpawn`: reset/pause/checkpoint ordering is about what a REAL
    `GameScene` does with the transitions, so a stand-in scene would make every assertion about
    the stand-in. Own probe, `net_scene_order.txt`.
  - **Production cost is ONE getter** (`NetSession.Metrics`) plus `ComponentBin.FreezeDepth`.
    `NetMetrics` has no reset, so every scenario asserts on DELTAS across its own frames rather
    than zeroing a counter the `[net]` line is also reporting.
  - **Scenario 5 supplies its own `INetScene`, and that is honest here** -- the client rx paths
    gate on "is a scene up", and nothing in that scenario is about what a scene DOES. Scenario 6
    is the opposite case and decorates the live one.
  - **The PRE-FLUSH claim window is legs 2b / 2c / 3b / 4c (card 1bfcd705).** The harness first
    reported it as two measured gaps; the fix landed and they are assertions now. Each is a
    tick-separated scenario with the flush between the claims taken away -- 2b pays a second
    same-tick claimant, 2c requires the mask to survive into the death record, 4c requires the
    pickup's live branch to run ONCE, and 3b is the host's own in-tick kill, the one shape of it
    that was reachable on the 2-peer wire. Scenarios 2, 3 and 4 above are their tick-separated
    controls. The contract itself is in the generous-claim bullet ("Claims, score & per-slot HUD").
  - **The id-churn scenario IS the item-1 residual `pupPops` probe.** It reports the count across
    a purge+replay with the stream lane reordered ahead of the reliable one, and asserts only the
    bound the design claims (churn alone must not pop a puppet per churned id). Measured 0 over
    12 ids at `snapTurn` 60ms. The card's scope on that burst is PROPOSE, not fix.
- **`tools/headless/probes/net_selftests.txt` runs every menu-runnable net self-test as one exit
  code** (card 25ad0659): `eaNetWire.test`, `eaNetHost`, `eaNetEntity`, `eaSlotTest`, `eaKickTest`, `eaNetSnap`,
  `eaNetCombo.test`, `eaNetScore.test`, `eaNetCosmetic`, `eaBinTest`, `eaTeamSeat`, `eaNetScenarios`,
  `eaNetFx`, `eaNetRespawn`, `eaNetTeleport`. They were
  console calls a human made once; this is what re-runs them. Asserted as TALLIES with their
  counts, never `expect-not FAIL` -- an absence assertion passes on a run where the `eval` never
  happened, and several of these suites SKIP legs they cannot reach, which is not a pass. Raise a
  count when checks are added. **`eaNetBgTest` is deliberately absent** (needs a level; it has
  `netbg_catchup.txt`).
- **Artificial impairment (`Compat/Net/NetImpairment`, card 40334a8f) is what makes the
  drop-tolerance paths testable at all.** BroadcastChannel never loses or reorders a packet, so
  until this landed the interpolation underrun, the snapshot unknown-id self-heal, the claim
  ledgers and the peer timeout had NEVER executed -- every one of `sgap`/`extrap`/`pops`/
  `pupPops` was structurally pinned at 0. It DECORATES `INetTransport` (so it impairs the
  WebRTC transport unchanged) and is always in the chain inside a net session, forwarding
  inline at 0/0. **RX-ONLY** -- impairing our own inbound == the peer's outbound being bad, so
  an asymmetric link is just two tabs with different settings, and tx is untouched.
  - **Per lane: the STREAM lane takes delay + loss + jitter; the RELIABLE lane takes delay
    ONLY.** Dropping or reordering the reliable lane would break the contract everything above
    the interface assumes and could only manufacture fake bugs. Its release times are clamped
    monotone, so jitter can never reorder it.
  - The held stream packets are a LIST scanned for everything due, not a head-first FIFO: with
    jitter a late-stamped packet must not block a later one that came due earlier, or jitter
    silently degrades back into pure delay. Loss with no lag releases inline (queuing it would
    add a hidden tick of latency and make loss impossible to isolate).
  - `Pump(now)` runs at the top of `NetSession.Update` BEFORE `DrainRx`, on the same
    `TickCount64` clock as the rest of the cadence -- so **delay granularity is one tick
    (~16ms)**; a lag below that is indistinguishable from 0.
  - Private `Random`, never the shared game RNG (the `Quad`/`ShipConnector` rule) -- a dev knob
    must not be able to desync a co-op session.
  - Flags `?netlag=<ms>` (0-500) / `?netloss=<0-100>`; **jitter is panel-only** (no URL flag) --
    it is the knob that actually makes the stream lane REORDER. Live panel `eaNetSim` (built
    outside `#app`), **opt-in via `?netsim`** on top of the `?net` boot -- it sits over a co-op
    session you are usually trying to watch, and most `?net=` boots never impair anything. A bare
    `?net=` boot still defines the console entry points `eaNetSim(lag, loss, jitter)` /
    `eaNetSim.test(...)` / `eaNetSim.show()`+`.hide()` (summon the panel with no reload), and
    `?netlag=`/`?netloss=` are parsed C#-side in `DebugFlags`, so they impair panel or no panel.
  - **`?netloss=100` starves the ship stream so the stall banner raises after ~1.2s and the
    peer-drop verdict lands ~8s in, while the handshake
    stays alive on the reliable lane -- that is a simulated silent disconnect, not a bug.**
  - The `[net]` line gains `impLag/impLoss/impJit/impDrop/impHeld` ONLY while impairment is on,
    so a deliberately degraded log can never be mistaken for a genuinely broken one.
  - **Verify with `eaNetSim.test(lag, loss, jitter, n)`** -- pushes n synthetic packets per lane
    through the real wrapper on a VIRTUAL clock and prints measured delay/drop/per-lane reorder.
    Written in place of a `tools/sim/` python mirror on purpose: the policy is small enough that
    a mirror would drift from the C# and prove nothing. Reliable lane must read `drop=0
    reorder=0` in every configuration, including `loss=100`.

## Signaling, menu lobby & handshake

- **Signaling (card 11.4): room codes on the shared Hetzner VPS** (root CLAUDE.md has the
  box details). `server/signal/` in THIS repo = a FastAPI/uvicorn dumb relay (mints 5-char
  codes, no 0/O/1/I; relays SDP/ICE between exactly 2 peers; room TTL 10 min; `python
  test_signal.py` covers the protocol). Deployed at `/opt/rotea` (unit `rotea`, port
  8091 localhost) behind nginx `location /rotea/ws` in the `notzelda.haraldmaassen.com`
  443 vhost (existing cert; health check: `https://notzelda.haraldmaassen.com/rotea/health`).
  Deploy = scp `server/signal/*.py` + `requirements.txt` to `/opt/rotea/server`,
  `systemctl restart rotea` (full first-install steps: `server/signal/README.md`). The
  signaling WS closes once the DataChannels connect -- gameplay is pure P2P.
- **Menu lobby (card 11.4; the 4-player lobby panel is card 0257f8ba):** main menu
  "Online Co-op" -> Host Game (shows the room code + "waiting for players") / Join by Code
  (HTML code-entry overlay outside `#app` -- `eaRtc.promptCode`).
  `Compat/Net/NetLobby` owns the pre-session flow (JS phase queue drained by
  `MenuScene.NetUpdate` on the game tick; `NetStatusMenu` = the re-textable
  ConfirmationMenu panel). On the FIRST connect the HOST lands on the LOBBY PANEL
  (`NetLobbyMenu`: room code + live roster + Start Game/Cancel -- launch is no longer
  implicit on pairing; more friends keep joining the same code meanwhile, and the join side's
  panel shows the roster via `EvLobbyRoster`). Start Game leads to the NORMAL select screens
  (netPickMenu -> the shared selectors; their OnExit reroutes in net mode; netPickMenu's
  Cancel backs out to the lobby panel rather than ending the session; WebcamAliens selection
  is refused, and the carousel swaps its briefing for the reason) and `EvLaunch` mirrors the
  launch on every client (`MenuScene.NetLaunchMirror` -- same fade/warm path, difficulty
  locked, starter Keyboard). Turbo is forced to 100 while a session is Active (`Game1.Update`).
- **v4 handshake + match-end (card 11.4):** hello/welcome carry an 8-byte build hash
  (FNV-1a of `window.eaBuildHash`; deploy.yml stamps a sha256 of `blazor.boot.json` at
  publish, dev builds read 'dev') + a flags byte. Hash mismatch -> `MsgReject` -> "Update
  required" notice both sides (a stale-cached client can never desync a session); menu
  sessions also reject if EITHER side has `DebugFlags.Active` (dev `?net=` sessions are
  anything-goes). **Rejection is graceful (card 4717d3cf, `RejectGraceMs` 1s):**
  `SendRejectOnce` queues the reliable `MsgReject` but defers `NetSession.Stop()` by a tick
  budget instead of closing instantly -- an immediate `Stop()->transport.Close()->pc.close()`
  is ABORTIVE on WebRTC and would discard the still-buffered reject frame, leaving the peer to
  see only a channel close ("other player disconnected") instead of the real reason. Holding
  the session open for the grace keeps SCTP alive so the reject (and our hello, which drives
  the peer's own symmetric detection) actually egress; the peer's inbound reject during the
  grace ends our side early. The detection itself is symmetric (each side derives the notice
  from the peer's hello), so the frame is belt-and-braces; the grace is what makes it land.
  Match-end, AS 11.4 SHIPPED IT: any player leaving a MENU session (quit, tab close, drop,
  game-over wind-down) ends it for both -- scene-down edge or `PeerLost` sends
  `EvLeave`/notice, `NetSession.Stop()` tears down (registries disabled, state reset,
  restartable), `GameScene.NetApplyPeerLeft` force-exits a running level (except in
  Victory/GameOver, which finish locally), and the menus surface `TakeMenuNotice()`.
  **A level that PLAYED ITSELF OUT is the one exception -- won since card 3b6c12e7, LOST since
  card c600c55a. See the level-end bullets below; the game-over wind-down used to be in that
  list, and "one match per lobby" is no longer true either way.**
  **AND SINCE CARD 87242257 (Stage 11.9) THE "ends it for both" HALF IS SUPERSEDED TOO: only
  the HOST leaving ends the match; a CLIENT leaving frees its seats and everyone else plays
  on** -- the N-PEER SESSION section below owns the policy, N=2 included (a menu-session host
  whose partner drops now keeps playing solo, reverting to single-player like a listed host).
  `EvReady` (client scene-up edge -> host `ReplayLive`) covers the lobby launch race
  where one peer out-warms the other; world messages are gated client-side while no
  GameScene is up. URL `?net=` sessions keep the old semantics (session survives peer
  loss, reconnect works).
  - **A NOTICE CLOSES THE MAIN MENU ON BOTH BRANCHES (card 72143c11).** `NetUpdate`'s notice
    branch used to call `CloseNetFlowMenus()` on the `netMode` branch only, and that does not
    touch `mainMenu`, so the panel could go up over a LIVE main menu: the text overlapped the
    rows, and since **`MenuSub1` has no modality at all** -- every menu in the collection runs
    `HandleInput` every tick -- arrows moved two selections and Enter invoked two entries.
    `mainMenu.RemoveInstantly()` now runs unconditionally (`Collection.Remove` of a menu that is
    not shown is a no-op, so the lobby paths are unaffected). Card c337222a changed WHICH branch a
    post-level notice arrives on, not whether that line is needed -- it now arrives with `netMode`
    false and `mainMenu` live, i.e. the case it closes.
    Verify with `eaMenuCensus()` / `tools/headless/probes/net_notice_menu.txt`, never a
    screenshot: `NetStatusMenu` has DrawOrder 2000 and draws its own 50% darken, so a menu live
    underneath it looks merely dim while still eating every keypress. `eaNetNotice(text)` parks
    the notice with no peer and `eaMenuNetMode()` supplies the flag -- the one precondition a
    headless run cannot otherwise produce.
  - **`MenuScene.netMode` IS MENU-NAVIGATION STATE AND DIES WITH THE VISIT TO THE MENU (card
    c337222a).** `MenuScene` is a singleton -- `Game1` builds it once and re-ADDS it to the
    collection on every return from a level, re-running `Initialize` -- so nothing on it is fresh
    unless something clears it, and nothing cleared `netMode`. A lobby co-op match therefore came
    back to a main menu that still believed it was inside the Online Co-op flow, and every reader
    was reading a lie: `difficultyMenu_difficultySelected` silently ABORTED the next ordinary
    launch after `mainMenu` and the selector were already gone (a reload-only dead end, the
    sharpest of the four), the two selector `OnExit`s backed out to `netPickMenu`, the carousel
    refused `WebcamAliens` offline, and `NetUpdate` ran the lobby pump. `MenuScene.Initialize` now
    calls `ResetNetFlowState()` -- `netMode`, `netNoticeUp`, `browsingGames` and the status panel
    as ONE lifecycle -- placed before the `?gamebrowser` block so that flag's own
    `netMode = true` still wins. **`netNoticeUp` is a second real fix, not a ride-along**:
    `netStatus_CancelSelected` is its only clearer, so a notice still up at a level launch left it
    set forever and `if (!netMode || netNoticeUp) return;` then killed the lobby pump for the rest
    of the process -- every later Host/Join stuck on "Contacting server...". (`browsingGames` and
    `netStatusShown` really are near-unreachable today; they ride along because one lifecycle
    beats three near-misses.)
    - **It is SESSION-FREE on purpose** (no `NetLobby.Cancel`, no `NetGameBrowser.Stop`): a caller
      that returns to the menus with a session STILL UP must be able to re-enter deliberately.
    - **That entry point is `MenuScene.EnterNetLobby()`, and it is THE way to reach the net-lobby
      menu state programmatically.** Its one caller is card 3b6c12e7's level-end -> lobby flow
      (the seam landed first, so that card came through a defined door instead of inventing one
      against private state). It sets `netMode`, clears `netNoticeUp`,
      **removes `mainMenu` itself** (`Initialize`
      re-adds it unconditionally and neither `netPickMenu` nor `NetStatusMenu` is modal, so a live
      main menu underneath would eat every keypress -- the 72143c11 lesson; a caller must NOT be
      relied on to do this), then mirrors the lobby's own `Connected` branch: host to the level
      pick, client to the waiting panel.
    - **Deriving it from `NetSession` instead was evaluated and is IMPOSSIBLE** -- the lobby's
      `Contacting`/`Prompting`/`Connecting` phases have no session at all, so `NetSession.Active`
      under-reports exactly where the pump must run; and the selector-exit routing is a
      "where did I come from" question, not a "is a session up" one.
    - `difficultyMenu_difficultySelected`'s abort now also recovers (`mainMenu.Show()`) when there
      is neither a session nor a pending notice, so that branch can no longer strand the player.
      `NetSession.MenuNotice` is PEEKED there, never taken -- `TakeMenuNotice` consumes.
    - **Verify with `eaMenuNetState()` / `eval MenuNetState`**, pinned by
      `tools/headless/probes/net_menumode_reset.txt` (plant the flag, round-trip through the
      Tutorial, require it clear AND require an ordinary Mission 1 launch to still launch). None
      of those four fields changes a pixel, so no screenshot can see any of this.

- **FINISHING A LEVEL RETURNS BOTH PEERS TO THE LOBBY WITH THE SESSION ALIVE (card 3b6c12e7).**
  The host then picks the next mission and the pair keeps playing. Before it, `UpdateSceneEdges`'
  scene-down branch was the single teardown trigger for EVERY normal level end -- `EvLeave` +
  `Stop("match ended")` -- so a finished level dropped both players to the main menu and a second
  level meant re-signalling from scratch. **Since card c600c55a the same is true of a level you
  LOSE -- see the bullet after this one, which is where the latch's real name and its timing
  rule live.**
  - **NO PROTOCOL CHANGE AND NO NEW WIRE MESSAGE, which is the design's whole shape.** Both peers
    already reach the end through the existing host-broadcast `EvVictory`, so each one
    independently keeps its own session and walks to its own lobby. Nothing has to be negotiated,
    and the two peers need not arrive together -- which matters, because story levels come back
    through `CreditsScene` and either player can be seconds behind the other. Protocol stays v15.
  - **THE LATCH IS KEYED OFF THE TERMINATE MODE, NOT `_state`.** The seam is `GameScene.Terminate`
    calling `NetSession.OnLevelFinished()` on `FinishedMode.finishedlevel`, IMMEDIATELY ABOVE the
    `NetActiveScene = null` that raises the edge. The edge itself cannot ask: by the time it fires
    the scene is already gone, so `NetEndingNormally` (the existing discriminator, read only inside
    `EndMatchPeerGone`) is unreachable there. `_state` would also be the wrong question -- it reads
    `Victory` for a quit taken DURING the victory choreography, which is an ordinary match end. The
    latch is SPENT by the edge, so it can never survive into a later level.
  - **`listedSession` (join-in-progress) is DELIBERATELY EXCLUDED and keeps the old teardown**: a
    JIP host has no lobby to return to -- it was playing single-player when a stranger arrived --
    so its level ending is still a match end, and its joiner still sees the `EvLeave`.
  - **`ResetPerMatchState()` is what makes a SECOND level correct, and the split is the interesting
    part.** It is the WORLD-scoped subset of `ResetPerSessionState`: the interpolation buffer (or
    the next level's puppet spawns at the LAST level's final position), the rx queue, the puppet +
    friend channels, the kill/death ledgers, and a `Disable`/`Enable` cycle of `NetIdRegistry`
    (host) / `NetPuppets` (client) to drop the dead level's id maps -- exactly what a
    `Stop()`/`Start()` pair does, and `NetIdRegistry`'s `next` counter keeps counting across it by
    design, so the next level's ids cannot collide. What it KEEPS is what describes the PAIRING
    rather than the match: the transport, `PeerUp`/`menuSession`, the peer identity and block list,
    the roster grants (the same two peers keep their seats), the monotone tx/rx event sequences,
    and `pendingLaunch*` -- the host may already have picked the next level while this peer was
    still watching the crawl, and that latch is what lets the client mirror it on arrival.
  - **The menus come back in through card c337222a's `MenuScene.EnterNetLobby()`** -- this is its
    first caller. `NetSession.TakeLobbyReturn()` is a take-once latch polled at the END of
    `MenuScene.Initialize`, after `ResetNetFlowState()` and after `mainMenu` has been re-added
    (`EnterNetLobby` removes it, so nothing may re-add it afterwards). `NetLobby.Phase` is
    untouched -- `Stop()`, its only resetter, deliberately did not run -- so the client's status
    panel gets its text from the same `Connected` branch a fresh pairing does, and both peers'
    Cancel still leaves the match through `NetLobby.Cancel`.
  - **Verify with `tools/headless/probes/net_level_end.txt` (the SESSION) and
    `net_level_end_lobby.txt` (the MENUS)**; console `eaNetLevelEnd.arm()`/`.check()` and
    `.armHost()`/`.menu()`. Two probes because the halves need OPPOSITE roles: only a CLIENT
    applies an `EvVictory` off the wire, and only a HOST lands on `netPickMenu` -- and a host wins
    from its own SCRIPT, never from a beat a rig can inject, which is why the lobby probe boots
    `?win` (Level-2-only). **Both are DESTRUCTIVE** -- they drive the live level to its END -- and
    both need `?level=Level2`, the one shipped level with a non-default `spawnType` and so the only
    boot on which the fly-off legs below discriminate. `?netallowdebug` is required (a real menu
    session, and `?level=` sets `DebugFlags.Active`). **The discriminator is the ABSENCE of an
    `EvLeave` on the peer's queue**, with the peer's delivery count beside it as the positive
    control; the three session legs alone pass on a run that never terminated its scene, which is
    why the scene-down is asserted separately. Do NOT pump the WIRE before reading that queue --
    `NetWire.Pump` drains every endpoint with no collector attached and the evidence is gone
    (`Close()` clears only the CLOSING endpoint's own inbound, so `Stop()` does not destroy it).
- **A NET PUPPET LEAVES THE WAY THE SCENE SAYS, NOT ALWAYS UPWARD (card b4a9fe60).** At level end
  `PlayerShip.Update`'s `hasWon` arm thrusts at the ship's `startdir` forever -- the angle it flew
  IN on -- and both puppet spawn sites (`NetSession.SpawnPuppet`, `NetSession.Friends.SpawnFriend`)
  hard-coded `4.712389f`, i.e. `PlayerSpawnType.South` = up the screen. `GameScene.spawnType` is
  per-LEVEL, so on **Level 2 (West)** the remote ship flew UP while every local ship flew RIGHT, on
  BOTH peers' screens; `ClassicAliens` (North) and `InsaneBossI`'s Mars section had it too. It is
  not a replication desync -- once `hasWon` is set, `PlayerShip.Update` never reaches its `Remote`
  case again, so `DriveRemoteShip` stops running and each peer simulates every ship's departure
  LOCALLY off that one field.
  - The scene owns the angle now: `GameScene.SpawnDirectionFor` is the one source (the three
    literals used to be duplicated across `SpawnPlayer` and `SpawnAllPlayers`), exposed on the
    seam as `INetScene.PlayerSpawnDirection` and read by both puppet sites through
    `NetSession.PuppetSpawnDirection()`. The fallback only covers a spawn with no scene up, which
    the callers' own gates exclude.
  - **It is a per-ship field with no other observable**, so it is verified as DATA:
    `PlayerShip.NetStartDirection` is the read seam, `net_level_end.txt` compares the puppet's
    against the live LOCAL ship's on a West level -- with the pre-card South constant beside every
    leg as the negative control, because on a South level the bug and the fix agree and every
    direction assertion would pass on the broken build -- and `logic_probe`'s
    **`ProbeSpawnDirection`** sweeps the whole `PlayerSpawnType` enum against the VECTORS each arm
    must produce. NORTH has no end-to-end route at all (it ships on `ClassicAliens`, a challenge
    level whose victory a rig cannot reach), so the pure sweep is the only thing covering it.
- **LOSING A LEVEL DOES THE SAME (card c600c55a), AND IT TOOK TWO CHANGES, NOT ONE.** The card:
  *"mission failed in multiplayer dumps everyone back to the menu :) -- should be the same as when
  you beat a level, the host can select a new level (or the same) to try."* Before it, the
  Mission Failed wind-down fell through to the ordinary match-end road and both players landed on
  the main menu, the client with a session-ended notice.
  - **THE LATCH IS THE EASY HALF.** `GameScene.Terminate` now latches on `FinishedMode.lostlevel`
    as well as `finishedlevel`, and the latch is renamed to say what it actually means:
    `NetSession.OnLevelEndedCleanly()` / `levelEndedCleanly`, i.e. *this level ended on its own
    terms on both peers*, not *this level was won*. It is sound to read `lostlevel` that way
    because it has exactly ONE producer -- `Defeat()`, reached only from
    `defeatmessage_OnFinished` -- so every other way out of a level (the pause menu's Exit,
    `NetApplyPeerLeft`'s force-exit, a demo ending) is `FinishedMode.exit` and still ends the
    match. No protocol change, exactly as in 3b6c12e7 and for the same reason: a game over is
    already host-authoritative and broadcast (`LoseLife` -> `OnHostReset(ResetModeGameOver)` ->
    the client's `NetApplyReset`), so each peer reaches its own `Terminate` off a beat both
    already ran.
  - **THE HALF THAT IS ACTUALLY INTERESTING: THE EDGE WAS ONE FRAME LATE.** `UpdateSceneEdges`
    fires on the next `NetSession.Update()`, but `Game1.gameScene_OnFinished` adds `MenuScene`
    SYNCHRONOUSLY for any ending that does not route through `CreditsScene` -- and
    `MenuScene.Initialize` polls the take-once `TakeLobbyReturn()` as its very last act. So with
    the latch alone the session survived and the host still sat on the MAIN MENU with a live
    pairing behind it, unable to pick the next mission. `NetSession.OnLevelEndSceneDown()`, called
    from `Terminate` below the purges and above `OnFinished`, raises that edge there instead. It
    is a no-op unless the latch is set, so a quit / drop / force-exit keeps its old timing (and
    its `Stop()` + menu notice still land on the following `Update`); `UpdateSceneEdges` is
    edge-guarded by `sceneWasUp`, so that `Update` sees no change.
  - **CARD 3b6c12e7 HAD THE SAME HOLE AND GOT AWAY WITH IT.** The three story levels return
    through `CreditsScene`, which puts seconds between the scene going down and the menus coming
    up, so the late edge never showed. A won CHALLENGE level takes `gameScene_OnFinished`'s
    `default:` arm straight to `MenuScene` and did not -- that pre-existing case is fixed here
    too, by the same call.
  - **Verify with `tools/headless/probes/net_level_lost.txt` (the SESSION, client side) and
    `net_level_lost_lobby.txt` (the MENUS, host side)**; console `eaNetLevelEnd.armLost()` /
    `.checkLost()` and `.armLostHost()` (whose phase 2 is `.menu()`, reused from the victory
    half -- what a lobby return has to look like does not depend on how the level ended). Both
    DESTRUCTIVE. The two probes catch DIFFERENT halves: dropping `lostlevel` from the latch fails
    session legs on both, while dropping only `OnLevelEndSceneDown()` leaves every session leg
    green and is caught solely by the menu assertions. The shared legs live in
    `NetLevelEndTest.CheckLevelEndSurvival` so the win and loss halves cannot drift.
  - **There is no `?lose` to pair with `?win`, deliberately.** A host's game over comes out of the
    level (`UpdateNormal` -> `AllShipsDead` -> `LoseLife` -> the lives-exhausted branch), so
    `ArmDefeatHost` puts the level in the state a Hard+ run reaches on its last life -- `Lives` 0,
    no `InfiniteLives`, no `DirectRespawn` (Easy's respawn-in-place arm returns from `LoseLife`
    BEFORE the game over) -- and asplodes every locally-owned ship through the real
    `Asplode()`->`Die()` path. Note the consequence for hand testing: on Easy/Medium a story
    level runs at `score.Lives = -1` and **cannot reach a game over at all**; only Hard+ sets
    `Lives = 7` (`ApplyDifficultyPolicy`).
  - **A RIG ARTIFACT TO KNOW ABOUT ON THE CLIENT SIDE.** `NetSession.StartForTest` never goes
    through `NetLobby`, so `NetLobby.Phase` is `Idle` and `MenuScene.NetUpdate`'s `Idle` arm
    swaps the client's waiting panel for the Host/Join menu the moment `EnterNetLobby` shows it.
    That is a property of the rig's session, not of the card, which is why `net_level_lost.txt`
    asserts `netMode`/`noticeUp` off `eaMenuNetState()` and claims no menu census on that side.

## N-PEER SESSION (card 87242257, Stage 11.9, protocol v24)

The session layer holds a peer SET on the star `plans/4p-online-coop.md` fixed: the host is the
hub (up to `MaxRemotePeers` 3 channels -- `Oracle.MaxPlayers` minus its own seat), a client holds
exactly one channel (its host). World authority is untouched -- snapshots/events fan out, claims
arrive from any client, the ledgers were already per-(netId, slot). Since card `0257f8ba`
(11.10) the REAL rooms hold four machines too -- the menu lobby, the listed/JIP rooms and the
in-level joins all reach this session layer; the dev transports (`NetWire`, BroadcastChannel
tabs, LocalSocketNet `--net-peers`) remain how it is exercised headlessly.

- **THE MATCH-END POLICY (the card's design decision, adopted as proposed and UNIFORM, N=2
  included).** Host leaves -> the match ends for every client, no host migration (host
  scene-down sends `EvLeave` to all + `Stop()`; a client losing its host takes the existing
  `EndMatchPeerGone`). A CLIENT leaving -- clean `EvLeave`, drop verdict, bye, kick -- frees its
  seats and play continues for everyone else: `ReleaseDepartedPeer` releases its primary + couch
  seats (SLOT-keyed -- see below), removes the channel, tells the remaining clients with
  `EvPeerLeft` (slot mask; their `ExplodeFriend`-kept seats would otherwise leak) and
  recomputes the pause/stall aggregates. When the LAST client goes: mid-level the host reverts
  to plain single-player (`RevertToSinglePlayer` -- a listed game re-lists); at the menus a
  MENU-LOBBY host keeps its session and its room and just waits for new players (card
  `0257f8ba` -- before 11.10 a peerless lobby was a dead end and Stopped with the notice; the
  Stop survives only for the non-lobby shapes, e.g. a scenario session).
  **Consequence at N=2, deliberate:** a menu-session host whose partner drops now keeps playing
  solo instead of being thrown to the menu. A level that plays itself out still keeps every
  pairing alive, won or lost (cards 3b6c12e7 / c600c55a; `ResetPerMatchState` loops the
  channels).
- **EVERY SLOT DECISION KEYS OFF `p.PrimarySlot`, never a `ControlDevice.Remote` scan** --
  `GetPlayerIndex(Remote)` / `DeviceIsPlaying(Remote)` / `ReleasePlayer(Remote)` are ambiguous
  with two remote peers, and every one of them was load-bearing in the 2-peer code
  (`ReserveRemotePrimarySlot`'s leftover-seat reuse, `SpawnPuppet`'s seat take, `ManagePuppet`'s
  adoption, the seat release). Grants are serialized by construction: each hello's reservation
  lands in the oracle before the next hello drains, so two joiners in one tick get distinct
  seats; `AllocateSeat` additionally excludes every channel's primary, and the leftover-Remote
  reuse only takes a seat no OTHER channel claims (`FindLeftoverRemoteSeat`).
- **PER-RECIPIENT RELIABLE EVENT SEQS (`PeerChannel.TxEventSeq`).** Addressed sends under one
  global counter would open a false `seqGap` at every peer that was not the target, and
  seqGap=0 is a health bar. Every reliable event goes through `SendEventToPeer` /
  `SendEventToSessionPeers` (encoder = `seq => Encode*(seq, ...)`); a "broadcast" is N addressed
  sends, each contiguous on its own channel, byte-identical to the old broadcast at one peer.
  The `replayTarget` latch inside the session helper is the ADDRESSED CATCH-UP: `EvReady` (and
  `PeerConnected`'s initial replay + JIP `EvLaunch`, and `MsgWelcome`, and `EvSlotGrant`) reach
  only the peer they are about, so a late joiner's `ReplayLive`/`NetReplayCatchUp` burst no
  longer re-blasts every already-caught-up peer -- the 4p plan's named hazard.
- **THE HOST RELAY (the hub duty; `RelayPeerShips` + the `HandleHudState` relay +
  `RelayFromClient`).** On the 33 ms cadence the host re-encodes every up peer's primary (only
  while its alive latch is set) and each fresh extras channel as NON-primary slot-keyed
  `MsgShipState` frames -- host clock, own relay seq, samples off the channel's NEWEST buffered
  frame so the cumulative shot count and roll rings cross UNALTERED -- and `SendStreamTo`s every
  other up peer. A recipient cannot tell a relayed client primary from a host couch ship, which
  is the point of v23's one ship path; death propagates as the extras semantic (the relay stops,
  the 500 ms timeout explodes the puppet). A client's `MsgHudState` is relayed verbatim to the
  others; `EvBlast` / `EvRespawn` / `EvSlowmo` / `EvTetherBreak` are re-emitted under each
  recipient's own seq, addressed so the SOURCE never hears its own event back. The relayed
  channel's extra interpolation hop SHIPPED with card `6fb406bc` (11.11): the relay marks its
  frames `ShipFlagRelayed` and the receiving channel renders 150 ms behind newest instead of
  100 -- see "STAGE 11.11 HARDENING" below.
- **PAUSE IS A SET.** `p.RemotePaused` per channel; the scene freezes on the AGGREGATE's edges
  (`SyncRemotePauseToScene`; the scene setters self-guard). The host relays each client X the
  per-recipient aggregate `localPaused || anyOtherClientPaused(X)` as `EvPause` edges
  (`PauseSentTo` per channel) -- exactly the semantic a client's single bool already implements,
  and what keeps B frozen through A-pauses/B-pauses/A-unpauses. At N=2 the wire is
  byte-identical to the old direct announce. The 120 s paused-peer backstop widens on ANY held
  pause (a frozen world backgrounds everyone's tab). Kick offers tick per paused channel and
  latch their TARGET (`kickOfferPeer`); `KickPeer` kicks that peer only -- addressed `EvKick`,
  seats freed, `EvPeerLeft` to the rest, the whole session wound down (after the egress grace)
  only when nobody remains. The pause-driven `NetKickMenu` still acts on the offer's subject;
  the host pause menu's deliberate kick is PER PEER since card `0257f8ba` ("Kick Player 2" ->
  `KickPeerAt(slot, block)`, the seat read off `UpPeerPrimarySlotsMask`).
- **PER-PEER LIVENESS.** The stall/timeout ladder runs per channel; the `NetWaitOverlay` banner
  rides the aggregate (any up peer stalled). A timeout verdict is `PeerLost(p)` -- on a host in
  a menu/listed session that is the client-departure path above, not a match end. The `?net=`
  dev shape keeps its channel-preserved resume semantics per peer (`DevSessionPeerDown`).
- **THE DOOR (`GetOrCreatePeer`) IS ROLE-ASYMMETRIC, and the client half is the bus rule.** A
  HOST creates a channel from any first frame (stream-first reconnect kept), re-keys a down
  unrefused channel for a reconnecting identity, and refuses an over-cap sender with an
  addressed `RejectFull` + one console line. A CLIENT only creates its one channel from a
  Hello/Welcome whose role byte says HOST: on a bus medium (BroadcastChannel) a client sees its
  fellow clients' hellos and streams directly and must not bind to one -- and it ADDRESSES its
  own post-pairing traffic to the host (`SendStreamToSession` / the event helpers), so a 3-tab
  loopback rig carries no client-to-client noise. Belt-and-braces on top:
  `HandleExtraShipFrame` refuses a slot the receiver owns (by NUMBER for the primary slot too --
  the grant exists before the seat does), so nothing off the wire can ever drive a locally-owned
  ship.
- **PER-PEER REJECTS.** With another peer already up, a refused pairing (version/build/flags/
  banned/full) is PER-PEER: addressed `MsgReject`, channel latched `Refused` (frames dropped
  cheaply, swept after 30 s or on its bye), session untouched -- a blocked griefer knocking on a
  live 3-player game costs it nothing. With nobody up the pre-11.9 whole-session wind-down +
  notice stands (client role, empty lobby, listed first joiner).
- **METRICS.** The `[net]` line is unchanged at <=1 peer (every probe that greps it is
  untouched); `pri=` grows `+slot` per extra peer (`pri=0/1+2`). A second `[netpeers]` line
  prints on the 5 s cadence whenever the session holds >1 channel: per peer -- id, state
  (up/stalled/paused/down/refused), granted seat, stream quiet, primary buffer depth, extras
  count, both event seqs.
- **VERIFY with `eaNetNPeer()` / `eval NetNPeer`** (`Compat/Net/NetNPeerTest.cs`, 57 assertions;
  a leg of `net_selftests.txt`): one real HOST session with TWO scripted joiners (plus a
  straggler for the per-peer reject legs) on a
  `NetWire(4)`, then a real CLIENT with a scripted host -- menu-runnable and leave-no-trace, the
  `eaNetScenarios` shape. Its observables are the COLLECTORS on the scripted endpoints, because
  most of what this card changed is WHO receives WHAT, which is invisible in this process's
  world. Mutation-tested five ways failing disjoint legs (dropping `EvPeerLeft`: 1; the relay
  echoing to its source: 1; a global event seq: 2; the pre-card match-end policy: 8; the
  pre-review reject handling -- whole-session Stop on any inbound reject, channel-gated
  delivery -- legs 1b/8b). The
  mid-level halves live in **`python tools/sim/net_npeer_smoke.py`** -- FOUR eahl processes
  since card `6fb406bc` (`--net-peers 3`, the full star; the `net_jip_sync` rig shape:
  `--nettime game`, `?net=jiphost` + three real `?net=jipjoin` menu-session joiners, all
  `&invuln` -- an input-less joiner ship dies to Level 2 mid-soak otherwise and the structural
  check becomes a timing lottery): mirror-image FOUR-seat rosters on all four consoles, every
  world holding all four ships (the relay's only end-to-end proof), `dupBad=0` throughout, the
  ~30 sim-second bandwidth soak reading the measured `txBps`/`rxBps` off the host's `[net]`
  line, then joiner2 killed mid-level -- host and BOTH surviving joiners free exactly its seats
  (`EvPeerLeft` end to end) and the match plays on with no `session stop`. A smoke, not a
  differ -- entity-level convergence stays `net_jip_sync.py`'s (2-process).

## LOBBY & CAPACITY -- the 3-4 player UX (card 0257f8ba, Stage 11.10, protocol v25)

The step that makes 11.9's N-peer session REACHABLE by a player: four-machine rooms, a host
lobby that waits, a join panel that shows the room, listing that survives a session, and a
per-peer kick. Design: `plans/4p-online-coop.md` section G.

- **ROOMS HOLD FOUR MACHINES.** `NetLobby.HostGame` hosts at `Oracle.MaxPlayers`; `eaRtc.list`
  opens at `LIST_ROOM_MAX` 4. A >2 host KEEPS its signaling ws for the room's whole life (even
  momentarily full -- webrtc.js has the reasoning), so late joiners keep arriving and a freed
  seat is replaceable; the max-2 flows are byte-identical to what shipped. The server side has
  been capacity-aware since 11.7 (`{t:host,max}`, seat ids, `listable()`).
- **START-WHEN-READY.** The host's first pairing no longer falls through to the level pick:
  `MenuScene` mounts the LOBBY PANEL (`NetLobbyMenu` -- its own type so `eaMenuCensus` can see
  it; entries Start Game / Cancel; text `NetLobby.HostLobbyText` = room code + per-seat roster
  + the start hint, re-texted every tick). Start is GATED on a peer being up, and backs the
  level pick out to the panel rather than tearing the session down (`netPick_CancelSelected`).
  The post-level lobby return (cards 3b6c12e7 / c600c55a) still lands the host on `netPickMenu`
  -- the crew is already aboard -- and Cancel from there now reaches the roster panel.
- **THE JOIN SIDE SEES THE ROOM: `EvLobbyRoster` (event 28, host -> clients, reliable,
  `[slotMask:1]`).** A waiting client cannot see its fellow joiners any other way (grants are
  host-side, no ships exist to relay, the menu oracle is local bookkeeping). Edge-triggered on
  the mask changing plus an ADDRESSED copy to each newcomer at `PeerConnected`; the receiver
  stores the byte and draws it (`NetLobby.ClientLobbyText`), so it is presentation-only by
  construction -- a lost beat is a stale line, never a desync, and -1 (no beat yet) degrades to
  the pre-card wording. The host derives its own mask live (`NetSession.LobbyRosterMask`).
- **A PEERLESS LOBBY SURVIVES.** The last guest leaving a menu-lobby host no longer Stops the
  session with "match ended": the session idles (the broadcast hello resumes, which is how the
  next pairing initiates), the room stays registered, and the panel reads "waiting for players"
  again (`ReleaseDepartedPeer`'s menu branch). Cancel is still the way OUT.
- **LISTING COMPOSES WITH A SESSION** -- the `!NetSession.Active` term is gone; see the game
  browser section for the predicate and `NetListing` itself for the adoption mechanics (a
  menu-lobby game mid-level lists on the very room its friends joined by; the session's
  teardown closes that room, caught by NetListing's Active-edge detector). Consequences worth
  knowing: the pause "Listed online" line and the corner beacon can now show DURING a session,
  and `NetListing.CouldList` is true mid-session, which is what puts the room toggle in the
  host pause menu's session shape.
- **JIP INTO SLOTS 3/4.** `PeerConnected` sends the addressed `EvLaunch` for ANY menu/listed
  host session with a live scene (it used to be listed-only), so a stranger off the browser --
  or a friend arriving by code mid-level -- lands in the running match through the ordinary
  catch-up (`EvReady` replay, scenery, scores). At the menus (scene null) nothing is sent: the
  lobby launch stays `SendLaunch`.
- **PER-PEER KICK.** `NetSession.KickPeerAt(slot, block)` + `UpPeerPrimarySlotsMask`; the host
  pause menu's rows read "Kick Player N" per settled seat (slotless fallback pair while a seat
  has not settled). The remote-pause `NetKickMenu` still acts on the offer's subject.
- **VERIFY:** `eaHostMenu.test()` (98 assertions -- the swept table now covers the seat masks
  and the composed session+room shape; `ProbeHostMenu` floor 95) and `eaHostMenu.live()` (the
  live seat mask + `KickPeerAt` end to end); `logic_probe`'s **`ProbeLobbyText`** sweeps the
  panel text pure (who reads "you", open seats, the start hint, the -1 degradation);
  **`tools/headless/probes/net_lobby_panel.txt`** drives the real panel offline through the
  **`eaNetLobbyShow(mask)`** / `eval NetLobbyShow <mask>` seam (census, the Start refusal with
  nobody aboard, Esc's cancel path -- mutation-tested both ways, see its header). The panel's
  REAL trigger needs a WebRTC pairing, so the multi-machine flow itself remains a browser pass:
  a local `uvicorn` signaling rig + three tabs is the recipe (JIP traps 1-5 apply), and no
  four-real-networks playtest has run yet.

## STAGE 11.11 HARDENING (card 6fb406bc, no protocol bump -- the epic's last card)

Four pieces, none changing what replicates -- only how well the star holds up at N=4.

- **RELAYED-CHANNEL INTERP DELAY: `ShipFlagRelayed` (flags bit 3) + a 150 ms cushion.** A
  client's view of ANOTHER client's ship takes the star's second hop (client -> host -> client),
  adding ~half(RTT_A+RTT_B) plus up to one 33 ms relay re-send beat -- so the one-hop
  `InterpDelayMs` (100 ms) left those puppets living on the extrapolation cap instead of the
  buffer. `RelayShipSample` (the ONLY setter) marks its re-encoded frames; `HandleExtraShipFrame`
  latches the bit onto the channel (`ShipChannel.Relayed` -- a LEVEL off the newest sample, and a
  channel is per (peer, slot) so it never flaps) and `AdvanceShipClock` renders it
  `RelayedInterpDelayMs` (150 ms, the 4p design doc's own budget) behind newest via
  `InterpDelayFor`. FIXED rather than jitter-derived, so the tuned 2-peer feel cannot drift; only
  an EXTRAS channel can be relayed (a client's primary channel is the host's own ship, one hop by
  construction; the host receives everything direct). **No protocol bump** -- a spare bit in an
  existing byte, degrading to the pre-card 100 ms in both directions (the `ShipFlagScriptGate`
  precedent). Pinned by `NetWireTest`'s codec leg (direct legs pin the bit CLEAR beside set
  primary/alive, so a mis-wired mask cannot pass) and `NetNPeerTest`'s relay-bit +
  direct-vs-relayed cushion legs (`NetSession.FriendInterpDelayMs(slot)` is the readback seam --
  the latch changes no pixel and moves no counter, so it is the only observable).
- **BANDWIDTH IS MEASURED, NOT ESTIMATED.** `NetImpairment` -- the one choke point every session
  send/receive already passes -- carries per-lane PAYLOAD byte counters
  (`TxStreamBytes`/`TxReliableBytes`/`RxStreamBytes`/`RxReliableBytes`) plus `BroadcastFanout`,
  which `NetSession.Update` refreshes to the up-peer count each send cadence: an unaddressed
  send really goes out once per connected peer at the JS/socket layer (webrtc.js loops the peer
  map, LocalSocketNet writes every client socket), so a broadcast counts payload x fanout while
  an addressed send counts once. RX counts at ARRIVAL, before the impairment's own loss roll.
  The `[net]` line gains **`txB= rxB=`** (cumulative totals) and **`txBps= rxBps=`** (rate over
  the report interval; the session's first line reads 0 rather than a boot-stretch mean).
  Neither is a health bar -- they describe the level, the population and the peer count.
  **MEASURED at N=4 on the smoke rig (Level 2, all four ships live): host uplink ~20-29 KB/s
  payload run to run (population-dependent), joiners ~1.2 up / ~9.5 down** -- the design doc's
  ~33 KB/s estimate confirmed sane. Real wire cost adds SCTP/DTLS/UDP/IP framing, ~2-3x at
  these packet sizes, so the host budget stands at roughly 60-90 KB/s up: comfortable on any
  home connection, no pacing work needed. CAVEAT: `txB` counts what the session OFFERED, one
  layer above the JS back-pressure gate below -- on a stalled real-WebRTC link read
  `eaRtc.netStats().streamDropped` beside `txBps` before quoting an uplink figure. Pinned by `NetWireTest` section 2b (fanout x3 vs
  addressed x1, rx-counts-before-the-loss-roll, `Close()` zeroing).
- **`bufferedAmount` BACK-PRESSURE (webrtc.js).** SCTP queues even unreliable-channel sends, so
  a stalled link does not DROP the stream lane -- it BACKLOGS it, and every ship/snapshot frame
  then arrives late by whatever is queued ahead, which is strictly worse than the loss the
  lane's consumers are all built to tolerate. `chanSend` (both `send` and `sendTo` route through
  it) SKIPS a stream send while `ch.bufferedAmount > 16 KB` and counts it -- bufferedAmount is
  PER CHANNEL (per peer), where a single peer's measured N=4 share is ~7-10 KB/s, so 16 KB is
  ~1.5-2 s of backlog on the channel the gate reads: a genuinely stalled link, not jitter; the RELIABLE lane is never
  dropped (the `INetTransport` contract) but tracks its high-water mark and names a backlog past
  256 KB once. **`eaRtc.netStats()`** reads `{streamDropped, streamPeak, relPeak}`;
  **`eaRtc.testBackpressure()`** is the regression guard (the `eaFps.test` idiom -- the JS layer
  has no headless runner, so the gate is a pure function driven over fake channel objects,
  callable from any boot; it restores the counters it touched). eahl never sees any of this
  (its `eaRtc` is stubbed), so the Chrome pass is the only place it runs for real.
- **THE FOUR-PROCESS RIG + the N=4 soak** -- `net_npeer_smoke.py` grew to the full star; see the
  N-PEER SESSION section's rig bullet for the shape and the joiner-`&invuln` gotcha.

- **PROTOCOL CHANGES ARE CHEAP -- never contort a design to avoid wire bytes (user ruling,
  2026-08-02).** The game is in active development and the build-hash handshake already refuses
  any mismatched pairing, so backward compatibility across builds is a non-goal: if the clean
  design wants a new event type, a state-extra field or a spawn-extra anchor, put it on the wire.
  The conventions below (append-only indices, decode-boundary validators, a version bump when an
  old peer would misbehave) still apply -- they are about correctness and dev-tab sanity, not
  compatibility. Four shipped designs were bent around avoiding the wire by a one-batch
  coordination rule; their straighter wire-first replacements are CHARTERED at the top of the
  Backlog (analysis on the original cards' comments): `a45b78f6` a cumulative shot counter in
  `MsgShipState` -- **SHIPPED, protocol v12**, replacing `FiringHoldMsFor` and both its residuals
  (card a5c2a39b); **`f62116b5` an explicit death-began event -- SHIPPED, `EvDying` + protocol
  v11, see the deferred-death bullet under "Claims"**; **`e79bb994` a teleport marker (replaces
  the observed-velocity plausibility cap, card 8dabe812) -- SHIPPED, protocol v13, and the
  ruling's sharpest worked example so far: the straight design was SMALLER than the heuristic it
  replaced AND found a fourth defective type (`Ball`) the estimator had been covering by luck**;
  **`c1a38ef9` motion parameters on the wire (sent Lazer rates, card 0108d1fc +
  deterministic-path spawn anchors, card 0dfc4495) -- SHIPPED, protocol v14: the playtest
  gate on the second half was waived and both halves landed. See the ANCHORED MOTION
  section**.
  Serializing WHO edits `NetProtocol.cs` in a parallel batch is an orchestration concern; it must
  not shape the design.

- **THE CLIENT TRUSTS THE HOST -- no client-side defense against host bugs or version drift
  (user ruling, 2026-08-04).** Assume matched builds and a correct host: the build-hash
  handshake refuses mismatches, the host's own code and suites are responsible for the host not
  sending garbage, and the client does not re-verify or gracefully degrade what it receives --
  anything outside those assumptions may simply break. Consequently malformed-wire-input
  hardening is CAPPED: do not file or accept cards growing it, and do not add fallback paths
  (a defensive random/default when a block looks short, etc.) to new receive code. What EXISTS
  stays (the decode-boundary validators, the length gates, their tests -- several double as
  vacuity controls on positive legs, e.g. NetMotionTest's spawn-anchor control), but this class
  of code and test does not grow.

- **Protocol (`Compat/Net/NetProtocol`, little-endian binary, 1-byte type, v23):** the 3
  layers -- `MsgShipState` (~30 Hz real-time cadence, EVERY locally-owned ship slot-keyed since
  v23: slot, flags (alive / scriptGate / PRIMARY), pos, vel px/ms, last-fire aim, CUMULATIVE
  shot count, shotsPerSec, bulletLife, roll rings -- 34 B), `MsgWorldSnapshot` (see the
  World-snapshots bullet below), `MsgEvent` envelope with a monotone ushort seq
  (EvSpawn full base state + spawn extras / EvDeath netId+killer+pos+per-slot award / EvBlast
  pos+level / EvClaim netId+killerSlot / EvScoreSync lives+scores) + `MsgHello`/
  `MsgWelcome` handshake (protocol version byte; both sides Hello until paired, opposite
  role replies Welcome; **v5** adds the host-granted primary slot byte -- card 4d904410;
  **v6** appends the peer-identity token to the handshake -- card 0b8a300b;
  **v7** widens EvDeath's trailing `points:u16` into an `f32 x MaxSlots` AWARD array --
  card b0ab09ec, see the Score/lives bullet;
  **v8** appends a `blockedSlots` mask to the handshake (HelloBytes 21 -> 22) so the host can
  grant a seat that is free on BOTH rosters -- card c0229c57, see the roster-slots bullet;
  **v9** adds `MsgHudState` (0x12) -- the owner-authoritative per-slot combo + powerup state,
  card 1a3ad45a, see the per-slot HUD state bullet;
  **v10** adds `EvCosmeticSwarm` -- a decorative swarm replicates as one on/off beat and its
  entities stop being replicated individually, card 9a3175d0, see the decorative-swarm bullet.
  No existing layout changed, but a v9 peer would ignore the beat AND still expect the
  per-entity spawns, i.e. see empty scenery -- a real incompatibility, hence the version move.
  The transient-feedback cards add **`EvFx`** and deliberately STAY ON v10 -- two bullets down
  says why that is a decision and not an oversight.
  **v11** adds `EvDying` (event 23) -- the host announces that a DEFERRED death has begun, at
  the moment `KilledBy` returns without removing the component, card f62116b5, see the
  deferred-death bullet under "Claims". A v10 peer would ignore it and fall back to the hp==0
  snapshot trigger, i.e. the pre-card latency rather than a desync -- so that bump is the
  cheap-protocol ruling being taken at its word, not a forced incompatibility.
  **v12** replaces the `firing` LEVEL flag with a cumulative u8 shot count in `MsgShipState` AND
  `MsgFriendState` -- card a45b78f6, see the shot-counter bullet under the remote ship. Both
  ship LAYOUTS changed (the count took `MsgFriendState`'s flags byte outright), so a v11 peer
  fires every couch/AI-friend puppet at random rather than degrading: the least ambiguous bump
  in this list.
  **v13** adds a per-SAMPLE flags byte to every world-snapshot ENTRY
  (`[len][netId][typeIdx][flags][base][extra]`, `NetProtocol.NetSnapshotFlags`) carrying the host's
  teleport marker -- card e79bb994, see the teleport-marker bullet. Like v12, it MOVED AN EXISTING
  LAYOUT rather than appending something an old peer could ignore, so a v12 peer would mis-parse
  every snapshot entry it received: the handshake refusing the pairing is the only thing between a
  stale peer and a garbage world.
  Card 8a7772d6 adds **`ShipFlagScriptGate`** (bit 1 of `MsgShipState`'s flags byte, the one v12
  freed) and **`EvIntroVolley`** (event 24) and STAYS ON v13 -- both degrade to the pre-card
  behaviour on an old peer in either direction, the `EvFx` bump test; see the intro-cinematic
  bullet under "World authority".
  **v14** widens `LazerDescriptor`'s state extras 6 -> 12 (three sent RATES) and gives
  `FlyingSpiderDescriptor` a 5-byte spawn anchor plus 4-byte state extras where it had
  none -- card c1a38ef9, see the ANCHORED MOTION section. Unlike v12 and v13 both blocks
  are LENGTH-GUARDED and APPEND-ONLY, so an older peer degrades to exactly the pre-card
  behaviour (a beam that holds between turns, a wasp on its own phase) rather than
  mis-parsing -- so like card 8a7772d6 above it did NOT have to bump, and did only as the
  parallel batch's convention.)
  **v15** adds `EvSlowmo` (event 25) -- either peer announces its 1up slow motion so both
  worlds scale together, card a66e190a. A v14 peer ignores the unknown event and falls back
  to the pre-card unilateral slowdown, so like v14 the bump is the batch convention rather
  than a forced incompatibility.
  **v16** appends the owner's per-LAYER Option ship COUNT to every `MsgHudState` entry
  (`HudSlotBytes` 10 -> 12) -- card c5228350, see the option-count bullet under the remote
  pickup. Like v13 this MOVED A FIXED-WIDTH LAYOUT rather than appending something an old peer
  could ignore, so a v15 peer mis-parses every entry after the first: a forced bump, not a
  convention one.
  **v17** adds `EvRespawn` (event 26) -- either peer announces that one of its ships has started
  its respawn clock, so the other peer draws the indicator too, card 37f3a663. A v16 peer ignores
  the unknown event and simply does not draw it, i.e. the pre-card behaviour, so like v14 and v15
  the bump is the batch convention rather than a forced incompatibility.
  **v26** appends `[rewardLevel:1]` to `EvRespawn` -- card ed32efe1, the respawn pop's reward
  bomb is the dying player's own "2" powerup level now, and the blast is not itself replicated (see
  the `EvRespawn` bullet under "THE RESPAWN INDICATOR"). Unlike v14/v15/v17/v25 this WIDENS AN
  EXISTING EVENT, so a v25 peer's length check would refuse the frame outright and lose the whole
  announcement -- a forced bump, not a convention one, though as the note at the version constant
  says no such peer can reach us.
  **v19** puts a monotone `[seq:2]` on the MsgWorldSnapshot HEADER and raises
  `NetBaseState.Scale`'s quantum from 1/256 to 1/4096 with a ROUNDING cast -- card f5cf7a5c, see
  the SNAPSHOT STALENESS section. Like v13 and v16 both changes MOVE AN EXISTING LAYOUT rather
  than appending something an old peer could ignore (the header grew 2 -> 4 bytes, so a v18 peer
  reads every entry two bytes early; the scale is in the shared base block `EvSpawn` also writes,
  so it would decode 16x too large). A forced bump, not a convention one.
  **v18** gives `LazerDescriptor` a `[ownerNetId:2]` SPAWN extra where it had none -- card
  9ccfe295, see the unattributed-claim bullet under "Claims". Like v14 the block is
  APPEND-ONLY and LENGTH-GUARDED, so an older peer degrades to exactly the pre-card behaviour
  (an ownerless beam) rather than mis-parsing; the bump is the batch convention.
  **v20** is the ONE-WRITER-PER-SLOT score redesign (card af96bcc2): `EvDeath` drops its
  `f32 x MaxSlots` award array (the v7 widening, reversed), `MsgHudState` entries gain a
  trailing `[score:f32]` (`HudSlotBytes` 12 -> 16) carrying the owner's declared TOTAL, and
  `EvScoreSync` shrinks to `[lives:1]`. Three layouts moved at once, so a v19 peer would
  mis-parse all of them: a forced bump, not a convention one.
  **v21** appends two ROLL-RING bytes to `MsgShipState` AND `MsgFriendState` (31 -> 33) -- card
  950bb70a, see the roll-ring bullet under the remote ship's shot counter. Bit i = the owner's
  asplode / bounce roll for the shot whose cumulative count is `ShotCount-i`, so the puppet
  spends the owner's per-bullet OUTCOME instead of re-rolling its own percentage. Like v12 the
  two ship layouts are FIXED WIDTH and their decoders' length gates moved 31 -> 33 with them,
  so a v20 peer's ship frames would be refused wholesale (a frozen puppet, not a graceful
  re-roll) -- a real bump, not a convention one; only the reverse direction tolerates the
  extra bytes.
  **v22** gives `BallDescriptor` its first STATE EXTRAS, `[flags:1]`, bit0 = the ball is
  CONNECTED to the junkboss -- card 1210e14e, see "THE SAME ROCKS, A 20%-SMALL HITBOX". The
  block is APPEND-ONLY and length-guarded (snapshot entries are length-prefixed and
  `ApplyStateExtra` gates on `len`), which keeps the DECODER robust and the bump mechanical.
  **v23** (card b2828be8, Stage 11.8) folds the two ship messages into ONE slot-keyed
  `MsgShipState` (34 B: leading slot byte, flags gaining `ShipFlagPrimary` for the sender's
  heartbeat frame) and RETIRES `MsgFriendState` (0x11 reserved, never reuse); `MsgHudState`'s
  fixed-width entry also grows a `[comboLeft:1]` remaining-time byte (HudSlotBytes 16 -> 17,
  folding card a5b1e941). A FORCED bump three ways over: a v22 peer mis-parses every ship frame
  in both directions and every HUD entry after the first.
  **v24** (card 87242257, Stage 11.9) adds `EvPeerLeft` (event 27, `[slotMask:1]` via the byte
  event) -- the host tells the remaining clients a departed peer's seats are free, which the
  new match-end policy makes a fact they cannot infer from the relay going quiet. The same card
  makes the host->client `EvPause` carry a per-recipient AGGREGATE ("someone besides you holds
  a pause") rather than the host's own pause alone; payload unchanged.
  **A BUMP IS BOOKKEEPING, NOT COMPATIBILITY -- and several notes in this list can read
  otherwise.** `OnHandshake` refuses `ver != ProtocolVersion` outright with `RejectVersion`
  before a single snapshot is exchanged, and the build-hash equality check behind it would
  refuse regardless. No peer ever receives a layout it does not itself speak, in either
  direction, so there is no graceful-degradation path to weigh when deciding to bump; the
  version names the wire layout for the next reader and nothing more.
  Card 11.3 bumps the protocol to v3 and adds the shared-state
  events: EvMessage/EvUnlock/EvBackground/EvMusic/EvCheckpoint (script beats), EvReset
  (host LoseLife branch), EvVictory, EvPause (either peer), EvTetherBreak (either peer).
  Peer loss = JS `pagehide` bye OR a stream timeout (PeerTimeoutMs 3s + PeerGraceMs 5s of
  continuous silence; past PeerStallMs 1.2s a non-freezing "waiting for other player"
  banner goes up, so a hiccup or a backgrounded tab recovers instead of ending the run);
  the ship stream doubles as the
  heartbeat (sent even with no live ship, alive=false). **While either side holds a pause
  the timeout stretches to a 120s backstop** -- a paused tab is usually backgrounded AND
  the pause muffle ducks its audio, which revokes Chrome's audio exemption from intensive
  timer throttling, so its ticks arrive in ~1/min bursts; without the wide window the link
  flaps and the designed peer-lost failsafe silently unfreezes the world. A held local
  pause is re-announced on reconnect (`PeerConnected`).
- **TRANSIENT FEEDBACK: `EvFx`, a one-shot cosmetic beat** (cards 43e85936 / 57ea30cd /
  ee939dd1 / 8d063d33 / c146422f). Ten reported symptoms, one root cause and five mechanisms --
  and the interesting part is how few of them needed new protocol.
  - **The root cause.** A puppet is frozen for life, so anything the HOST's `Update` does that is
    neither a sheet swap nor a base-state field simply never happened on the joiner's screen: a
    hit flash, a chunk breaking away, a warning banner, a windup glow, a looping ambience. It
    fails INTERMITTENTLY rather than cleanly, which is the tell -- a client hit-tests puppets with
    its own bullets, so a hit IT observed ran the real code and a hit only the host observed
    produced nothing. That is card 43e85936's "missing sfx/anims **sometimes**" exactly.
  - **`EvFx` (event 22, reliable): `[kind:1][netId:2][param:1]`, 8 bytes.** `netId` 0 means
    "no entity" (the registry never allocates 0); `param` is per-kind and 0 for every kind shipped
    so far. **It carries NO POSITION**: the entity kinds draw on a puppet whose position is already
    replicated and NEWER than a beat could be, and the entity-free kind plays a 2D cue. The first
    cut carried one and every consumer ignored it. `NetFxKind` is APPEND-ONLY and takes the **REJECT** policy -- the kind
    selects an effect to EXECUTE, so a substituted one is worse than silence.
  - **AN `EnemyHitFlash` IS SENT ONLY WHEN THE THING SURVIVES THE HIT (card f6fc1d97).**
    `KillableAlien.HitBy` used to announce it for EVERY hit, and the announcement sits ABOVE the
    `hitpoints <= 0` branch -- so a LETHAL hit told the peer "flash" and then, an `EvDeath` later,
    "explode". On a ONE-HIT-POINT enemy that is every single kill, which is how it was reported:
    *"1 hp ufo's blink white before they blow up (the hit effect for enemies with multiple hit
    points)"* -- the ordinary small UFO is `SetHitPoints(1)`, so that is the most common enemy in
    the game.
    - **THE PREDICATE IS `hitpoints > 0`, WHICH IS THE TERM `isBlinking()` ALREADY CARRIES**
      (`hittimer.Active & (hitpoints > 0)`). That is the argument, not "a kill is louder": the HOST
      does not draw a blink on its own killing blow either, so the beat was asking the joiner to
      draw something no screen in the session was drawing. Send side and draw side now agree by
      construction. `SpiderBoss`'s own emitter already sat in the `else` of its death test; this
      makes `KillableAlien` consistent with it.
    - **It also covers a hit landing on something already DYING, and that case is LIVE.**
      `SpiderHelperMothership.KilledBy` only flags `dying` and never clears `Collides`, so the host
      keeps hitting it for seconds with `hitpoints` at or below 0 -- showing nothing -- while the
      joiner's copy is TRACKED rather than released (`NetDyingStaysReplicated`), so `dead` is still
      false there, `hitpoints` is still positive from its last snapshot, and `NetPlayFx` accepted
      every one of those beats. `(hitpoints <= 0) & !dead`, the shape of the branch below the send,
      would keep sending exactly there.
    - **The reporter's other half -- "for 1 hp enemies I'd like the joining clients to just
      immediately blow the monster up and send a message to the host" -- is ALREADY the shipped
      architecture and was not rebuilt.** A client's bullets hit-test puppets for real
      (`NetPuppets.CollidableOverride`) and run the real `HitBy`, so the client kills locally and
      files an `EvClaim`; the host kills its own copy on that claim, or pays the claimant from the
      recent-death record if it had already died. That is the GENEROUS at-least-once claim design.
      What the report was actually seeing is the host's beat, above.
    - **A NON-lethal beat can still swallow the receiver's own next shot**, and that is unchanged:
      `HitBy` opens with `if (hittimer.Active) return;` and `NetPlayFx` starts that same timer, so
      for 35 ms after a host blink the client's own hit on that puppet is refused. Shared-gate
      behaviour, deliberate (it is what makes the apply idempotent against the client's own
      simulation); suppressing the lethal beat removes the case where it mattered most.
  - **WHY IT CANNOT BE A STATE EXTRA -- answer this before adding a kind.** The snapshot round
    robin corrects an entity every `live/16*60ms` (`SnapshotTurnMs`): 60 ms at best, ~1.2 s in a
    big world. A `KillableAlien` hit blink is **35 ms**. A sampled bit would miss the event
    outright and a sampled cue would double-fire or drop. Anything that PERSISTS (a charge state,
    a sheet swap) belongs in the state extras; anything that is an EVENT belongs here.
  - **EVERY APPLY IS DRAW/AUDIO ONLY AND IDEMPOTENT. Both halves are contract.** An apply may not
    damage, kill, award, spawn a replicable entity or move gameplay state -- `NetFxTest` asserts a
    hit beat spends no hp. And because the client may already have run the same effect off its own
    collision, each apply no-ops when what it would start is already running:
    `KillableAlien`/`SpiderBoss` gate on their own `hittimer.Active` (the same gate `HitBy` opens
    with), `Ball` on a `netDetached` latch.
    **`Ball` uses a LATCH rather than a `state == connected` test on purpose** -- a puppet's
    `state` never advances past `startup` (Initialize sets it, Update is frozen for life), so a
    state test refuses every beat and the feature silently does nothing at all. It was written the
    wrong way first and the suite caught it.
  - **The per-type knowledge lives on the ENTITY (`INetEntity.NetPlayFx`), never on the wire.**
    The base is a no-op; `KillableAlien` starts its blink, `SpiderBoss` adds its `bugdies` pair and
    bleed spray, `Ball` its blink and its detach burst. A new type costs one override, and the wire
    never has to name a cue or a sprite.
  - **NO PROTOCOL VERSION BUMP -- and the four graceful-degradation claims were VERIFIED against
    the decode code, not assumed.** (a) `HandleEvent`'s switch has NO `default:` arm, so an unknown
    event type falls through and returns, with `EventsRx`/`lastRxEventSeq` still advancing so no
    false `seqGap`; (b) `TryDecodeCosmeticSwarmEvent` returns false on an unknown kind and the case
    drops the frame cleanly; (c) `TryDecodeMessageEvent` bounds with `b.Length < 11 + textLen`, so
    a trailing byte is tolerated; (d) `EvSpawn` and snapshot entries are both length-prefixed, so a
    longer state-extra block cannot shift the following parse, and every `ApplyStateExtra` reads
    named bits behind a `len` guard. Each degrades to the PRE-CARD behaviour, which is the bump
    test (card `ca4fd94f`'s precedent, not `9a3175d0`'s). **If any of those four stops holding,
    the bump is back on the table.**
  - **The four symptoms that needed NO new lane are the more useful half to remember:**
    - **The boss DANGER/WARNING arrows ride `EvMessage`.** `MessageEvent` (the script banner)
      already hooks it, and `SpiderBoss`'s three sweep arrows + its helper warning and `JunkBoss`'s
      meteor warning are the same `AnimatedMessage.Setup` call from host-only boss code -- so they
      call `NetSession.OnGameMessage`. The only thing missing from the payload was `MakeShort`,
      appended PAST the variable-length text as an OPTIONAL byte, which is what keeps it compatible
      in both directions. **Do not move that byte in front of the text.**
    - **The big-UFO and JunkBoss charge glows ride the existing `NetChargeGlow` seam** -- the same
      child-`LazerGenerator` shape `SweepUFO`/`MarsBoss`/`SpiderHelperMothership` already used,
      carried in a SPARE BIT of each descriptor's existing flags byte plus the 7-byte
      `NetChargeWire` block. No new field and no width change.
    - **Level 2's bees ambience rides `EvCosmeticSwarm`** as a new `NetCosmeticKind` -- see the
      looping-cue sub-bullet under the decorative-swarm bullet.
  - **ENEMY TELEGRAPHS ARE AUDIBLE ON THE JOINER NOW**, reversing `NetChargeGlow`'s `SetupSilent`
    and the intent behind `LazerDescriptor`'s `playSound:false`. **The rule that replaced them is
    PLAYER-vs-WORLD, and it is the line to hold:** silence is for a remote PLAYER's private
    business (their summon glow), sound is for WORLD events both players are dodging. (The
    pickup cue sat on the silent side of that line for one card -- muted by d53431b4, reversed
    by the user in 06ac5df2 -- so treat the rule as a default the user overrides per effect,
    not a law.)
    An inaudible windup or beam is a gameplay DISADVANTAGE for the joiner, not politeness. All four
    enemy charge glows and the enemy single-shot beam are audible; the player-ship summon glow
    (`PlayerShipSummon`, and `GameScene`'s preload prime) never went through this seam and stays
    silent. `LazerDescriptor` still builds its puppet `playSound:false` -- the CONSTRUCTION is not
    the event, and the report rides its own beat instead.
  - **`EnemyLazerFire` is emitted at `Lazer.SetupSingleShot`, NOT off the beam's `EvSpawn`, and the
    reason generalises to any future spawn-time cue.** `NetIdRegistry.ReplayLive` re-sends
    `EvSpawn` for the WHOLE live set at an `EvReady` catch-up and the puppet layer cannot tell that
    from a fresh spawn -- so a cue on the spawn path would salvo every live beam at a
    join-in-progress peer the instant it arrived. A beat fired at the real moment is simply missed
    by a peer who was not there yet, which is correct.
  - **Verify with `eaNetFx()`** (`Compat/Net/NetFxTest.cs`, 37 assertions; a leg of
    `net_selftests.txt`). Real `EvFx` frames from a scripted host over a `NetWire` into a REAL
    client session, asserting the EFFECT on the live puppet -- `eaNetWire.test` covers the layout,
    and the layout was never what was broken. MENU-runnable and leave-no-trace.
    **Its observables are private state that moves NO metric** (a 35 ms timer read only by `Draw`,
    an `Explosion` entering the bin), which is the same fact that made these bugs invisible in the
    first place, so the entity types expose narrow `Net*` readbacks for it. Mutation-tested five
    ways. **The second Ball exists to stop a vacuous leg**: the first is already blinking, so
    `!hittimer.Active` would refuse a post-detach chip beat whatever the latch said.
  - **`MineTargetAcquired` (kind 3, card 745728f9) is the family's fourth kind and its first
    PURE-AUDIO one -- "the homing sound doesnt play for joining clients".** A `StarMine` plays
    `targetacquired` from its own `Update` when it locks onto a ship; a puppet mine is FROZEN, so
    that Update -- and that cue -- never ran on the joiner. It is a WORLD event both players are
    dodging, so by the player-vs-world rule above it is audible on both screens (the enemy-charge
    precedent, card c146422f); `StarMine.NetPlayFx` plays it and **falls through to base for every
    other kind**, which is load-bearing -- `StarMine` IS a `KillableAlien`, so an override that
    returned would silently delete the hit blink for every mine on the joiner's screen.
  - **THE BEAT IS GATED ON THE SAME `soundtimer` AS THE LOCAL CUE, and the emission is INSIDE that
    gate rather than beside it.** `StarMine`'s 300 ms `soundtimer` is what stops a mine that keeps
    losing and retaking a lock (a target crossing the release ring, a swarm around one ship) from
    re-playing the cue every tick -- so one beat per SOUND, not one per lock. Ungated, that same
    mine streams a reliable event per tick at the joiner.
    **A cadence assertion here has to drive a real RE-ACQUIRE or it measures nothing**: the acquire
    loop lives in the `free` branch and a locked mine never re-enters it, so "hold the lock and
    require no further beats" is true whatever the emission is gated on (measured -- the mutation
    passed). Park the mine away, tick, park it back; inside the window that must send nothing, past
    it (417 ms) it must send again.
  - **NO PROTOCOL BUMP for a new kind** -- `NetFxKind` is APPEND-ONLY, the `EvFx` frame is
    unchanged, and the `eaBuildHash` compat key pins peers to an identical binary, so a peer that
    can receive kind 3 is by construction one that knows it. What DOES have to move is
    `NetProtocol.TryFxKind`'s bound; `logic_probe`'s `ProbeWireEnums` cross-checks every validator
    against `Enum.IsDefined` over the whole byte domain, so a missed bound fails there rather than
    silently refusing the kind off the wire.
  - **Verify with `eaMineTarget()` / `eval MineTarget`** (`Compat/Net/MineTargetTest.cs`, 43
    assertions; `tools/headless/probes/starmine_dead_target.txt`). **DESTRUCTIVE** -- it kills the
    local player's ship for real -- so a throwaway `?level=Level2&invuln` boot. Its section 4
    drives `NetPlayFx` directly and section 5 reads the frames a scripted peer really RECEIVED over
    a `NetWire`: section 4 alone leaves a build that stopped EMITTING perfectly green while the
    joiner hears nothing, which is the reported symptom exactly. Headlessly there is no mixer, so
    the only thing observable about a cue is that it was REQUESTED -- card 8732568e's per-cue
    counters are what make that readable at all.
  - **THIS CUE IS THE HALF OF CARD 745728f9 THAT IS ACTUALLY FIXED.** The card's other half
    ("mines explode at a dead player's location") is offline, is NOT closed, and its `IsDead`
    guards turned out to be one-tick hardening rather than the fix -- web CLAUDE.md has the
    measurement and the two refuted hypotheses. Do not read this section as evidence for it.
  - **KNOWN LIMIT, pre-existing and NOT introduced by these cards: a client's `Ball` keeps its own
    hp.** It can therefore detach on its own schedule, and in principle twice. The beats are
    idempotent against that (whichever lands first latches), but the two peers' chip COUNTS still
    diverge; closing it means putting Ball hp on the wire, which it has never been on.
- **Every wire enum is validated at the DECODE boundary, and nowhere else (card 88f87ba2).**
  The validators and the contract live in one region of `NetProtocol.cs`. **Never cast a raw
  wire byte to an enum outside it**, and a consumer of a decoded value may ASSUME it is in
  range -- do not add a per-site defensive default. A new wire enum needs its validator there
  AND a row in `logic_probe`'s `ProbeWireEnums`.
  - **`NetBackgroundOp`'s range check and `NetApplyBackgroundOp`'s scene guard are DIFFERENT
    checks -- keep both.** `TryBackgroundOp` refuses a value outside the enum; the guard inside
    `NetApplyBackgroundOp` refuses an in-enum op that is wrong for the CURRENT scene (a
    `SetAlienBase2..6` arriving while the client is in a space scene indexes an empty layer
    list, and on MARS layer 0 is the sky, so it would paint a base tile over it). Neither
    subsumes the other.
  - Three policies, chosen by what the field DOES. **REJECT** (decoder returns false, message
    dropped) when the field is EXECUTED and no substitute is correct, or when the raw value can
    reach a save file. **CLAMP** for presentation-only fields, where dropping the message loses
    more than degrading it. **SENTINEL** (keep the raw value, expose a checked nullable beside
    it) for the public game browser's listings, where an unknown value is a normal production
    case that must still be displayed.
  - **A field that can silently kill a save file REJECTS.** `XmlSerializer`
    refuses an undeclared enum value, and `Settings`/`Unlockables` open their `StreamWriter`
    BEFORE serializing -- so the file is truncated and the write then throws into
    `Savable.SaveInner`'s catch-all. Settings then stop persisting for the session
    with nothing said and a corrupt file on disk. The live path is `EvLaunch` difficulty ->
    `Settings.SetDifficultyTo`.
    **`EvUnlock`'s item was the second such path and is not any more (card 125490d9)** -- the
    join peer no longer grants, so the value reaches no `Unlockables.Collection` key. Its REJECT
    policy STAYS regardless: the decoder still casts a raw wire byte to an enum, the protection
    must pre-exist any future re-grant, and `ProbeWireEnums` asserts the bound. Do not relax it
    to a clamp because nothing consumes the value.
  - **`EvLaunch` rejects the whole message, and ENDS the pairing with a notice.** A clamped
    level or difficulty replicates into a mismatched world, which is worse than a refused join;
    an out-of-enum level also reaches `Game1.AddLevelComponent`'s throwing default arm AFTER
    `MenuFinished` has removed the menu, leaving a black screen for the session. Ignoring it
    silently would strand the joiner on "the host is choosing a mission" forever.
  - **The bounds assume each enum is CONTIGUOUS from 0 and APPEND-ONLY.** Nothing enforces
    that; `ProbeWireEnums` cross-checks every validator against `Enum.IsDefined` over 0..255,
    which is what catches an appended member the bound does not know about (silently REFUSED
    off the wire) as well as a gap.
  - **Validation is client-side by design.** The signaling server does not bound these values,
    and a server check would not be a security boundary -- gameplay is peer-to-peer, so a peer
    can put any byte on the wire whatever the server saw.
- **NetIds (`Compat/Net/NetIdRegistry`):** host-side, on the ComponentBin seam
  (`Game.Components` ComponentAdded/Removed -- the same events Oracle uses, fired when a
  component actually enters/leaves the world). Replicable set = the `NetTypeRegistry`
  descriptor table (Oracle.GetBaddies' enemy types minus Explosion -- cosmetics never
  cross the wire -- plus Powerup). Emits spawn/death events; replays the live set to a
  late-joining peer; tracks per-entity OBSERVED velocity (position deltas between an
  entity's snapshot turns -- Speed/Direction lies for enemies that move Position directly).
  Minus the per-INSTANCE opt-outs -- see the decorative-swarm bullet below.
- **Decorative swarms replicate as one "effect on/off" beat, NOT per entity (card 9a3175d0).**
  Purely cosmetic entities were taking NetIds, `EvSpawn`/`EvDeath` pairs and a share of the
  16-per-60ms snapshot round robin for nothing: the `?flyspiders` rig measured `liveIds` 17-19,
  i.e. essentially the WHOLE budget spent on scenery, which directly stretches `snapTurn` --
  the mean blind dead-reckoning window of every enemy that DOES matter. Two halves:
  - **`AlienDrawableGameComponent.NetCosmeticOnly`** -- an INSTANCE-level opt-out (the
    `NetSpinPerMs` idiom), because the same `FlyingSpider` type is a real killable enemy in its
    foreground form and fog in its background one. Overridden by `FlyingSpider`
    (`isbackground`) and `Asteroid` (`SetBackground()`'s `DrawOrder == 1` marker). Read at the
    ComponentAdded seam, so it must be FINAL before `ComponentBin.Add` -- the configure-then-Add
    rule `tools/audit_add_order.py` already lints.
    **Two conditions, both required: the instance can never become collidable, and nothing
    gameplay-visible reads it.** Both members are in `Oracle.GetBaddies` -- the AI's whole world
    model -- and are invisible to it only because of `Collides`: `PlayerShip.IsAiShootable` has
    an explicit `baddy is FlyingSpider && baddy.Collides` and excludes `Asteroid` outright, and
    the threat scan gates at its CALL SITE (`PlayerShip.cs` `if (!baddy.Collides ||
    !IsAiThreat(baddy))`) rather than inside `IsAiThreat` -- so a future caller of `IsAiThreat`
    that forgets the gate would start dodging fog.
  - **`NetTypeRegistry.IsReplicableInstance`** is the predicate the LIVE world asks;
    `IsReplicable` is just the type table. Every decision site uses it -- and
    **`NetSession.SuppressWorldSpawn` is the load-bearing one**: with the type-level test there,
    the bin would divert the CLIENT'S OWN cosmetic spawns into the recycle pool and the joiner
    would see no scenery at all, with no counter moving anywhere.
  - **The SPAWNER replicates instead.** `EvCosmeticSwarm` (protocol **v10**,
    `[kind:1][on:1][rate:f32]`, `NetCosmeticKind` APPEND-ONLY) is announced by
    `FlyingSpiderEvent` / `AsteroidSpawner` from their first `Update` and from `OnFinished`
    (Level 2 ends its fog swarm by `LinkWith`, so lifetime alone would never fire). The client
    builds its own spawner and ticks it in `GameScene.UpdateNormal`, **in the very branch that
    skips `eventList.Update`** -- which is what gets pause / victory / resetting for free
    (`UpdateNormal` only runs in `GameState.Normal`, and a pause `Push` disables the scene).
    The asteroid copy uses the spawner's own `SetBackGroundOnly()` + `startWithBig:false`, so it
    never produces the collidable ones -- those still arrive as puppets.
  - **Latched on `GameScene`, replayed from the `EvReady` catch-up seam** next to
    `Background.NetReplayCatchUp`. Latched at the ANNOUNCE, not off the send path:
    `NetSession.OnCosmeticSwarm` early-returns with no peer connected, which for a LISTED
    single-player game is exactly the window a JIP peer must be caught up from (the same
    reasoning as Background's `netLast*`). Cleared at the checkpoint revert on BOTH peers -- the
    host's eventList drops active events without terminating them, so no "off" is ever sent --
    and in `Initialize`/`Terminate` (re-added singletons).
  - **The latch is REFCOUNTED per kind** and only emits on the 0<->1 edge. The beat is per kind
    but each spawner tracks its own announce, so two overlapping spawners of one kind (nothing
    ships that, but a level script is one line from it) would otherwise have the first one's
    `Terminate` send an "off" while the second still spawns -- the joiner's scenery gone for the
    rest of the level, silently, with the host's own screen full.
  - **A rate off the wire is clamped** (`NetCosmeticMaxRate` 12/s) and non-finite/negative
    refused: it drives `GenericSpawner`'s `while (num >= 1f) DoEvent()` loop, and a publicly
    listed game has a stranger on the far end. The ceiling bounds the AUTHORED rate, which is not
    the rate in flight -- `GenericSpawner` multiplies by `DifficultyModifier` and
    `MultiPlayerDifficultyModifier` per tick -- so it sits near the shipped rates (5.5/s fog,
    5/s belt), not at a round big number.
  - **KNOWN LIMIT (asteroids only), accepted:** `AsteroidSpawner` sweeps its entry HEADING on its
    own timers from `Reset`, so a peer's grey rocks fly parallel to the replicated real ones only
    while the two cycles stay in phase. A live pairing starts them within an RTT; a
    JOIN-IN-PROGRESS peer starts its cycle when the catch-up beat lands and is out of phase for
    the rest of that belt. Keeping them aligned would mean streaming the angle -- the per-entity
    cost this card exists to remove -- so it is a decoration-vs-decoration mismatch taken on
    purpose.
  - **A kind can be a LOOPING CUE rather than a spawner (card 8d063d33).**
    `NetCosmeticKind.BeesLoop` is Level 2's bees ambience -- two script events (`beesSoundOn` /
    `beesSoundOff`) wrapped around the fog swarm, host-only like the rest of the script, so the
    spiderwasp wave played out in SILENCE on the joiner while its screen filled with the fog that
    same swarm beat had already replicated. It takes this lane rather than one of its own because
    it IS that swarm's sound, it is switched by the same stretch of script, and the lane already
    carries the join-in-progress catch-up and the checkpoint-revert clear a looping cue needs.
    Client-side its "own copy of the effect" is a `SoundEffectInstance`
    (`GameScene.netCosmeticLoop`), so its entry carries a **null `Spawner`** exactly as a host
    latch does, and `rate` is meaningless (the emitter sends 1 -- a positive, so the lane's shared
    "rate <= 0 means off" guard needs no special case).
    **Its failure mode is the OPPOSITE of the swarms', which is what its legs assert:** not
    scenery that fails to appear, but a loop that fails to STOP and outlives the level. So every
    entry-dropping path goes through `NetStopCosmeticLoop` (an "off" beat, the checkpoint revert,
    `Initialize`, `Terminate`), and the test reads the live INSTANCE rather than the entry --
    dropping the entry while the sound plays on forever IS the leak. No version bump: an older
    peer rejects the unknown kind and gets no sound, i.e. the pre-card behaviour.
    **KNOWN ASYMMETRY at a checkpoint revert:** the clear stops the CLIENT's loop, but the host
    keeps its own `Level2.bees` instance playing -- the script's `beesSoundOff` never runs on a
    revert, which is the very reason the entries are cleared there. That host-side behaviour
    (including `beesSoundOn` overwriting `bees` without stopping the old instance) predates these
    cards; what changed is that it is now audibly ASYMMETRIC rather than silently one-sided.
  - **Verify with `eaNetCosmetic()`** (`Compat/Net/NetCosmeticTest.cs`) -- codec, the instance
    predicate (every check beside its positive control, since a predicate answering "not
    replicated" for everything would pass a fog-spiders-only test and silently stop replicating
    the whole game), and the client apply path (skipped with a printed SKIP outside a level).
    **A screenshot diff cannot check this feature at all** -- the two peers' scenery is SUPPOSED
    to be in different places. `eaNetBg()`'s state line gains `cosmetic=<kind@rate,...>`, which
    both peers hold (host = latch, client = live spawners) and which IS diffable; `eaNetBgTest()`
    gained the matching round-trip leg, and `?netscript` fires both kinds so the two-window run
    covers them.

## World authority, puppets & snapshots

- **World authority (card 11.2): the host runs the real sim, a join peer mirrors it.**
  Client sim-split at two choke points: `GameScene.UpdateNormal` skips `eventList.Update`
  (spawners/the level script only act in GameEvent.Update) and `ComponentBin.Add` swallows
  any replicable-type add not made by the puppet layer (KilledBy side effects: asteroid
  splits, bonus powerup drops, stray spawns) into the recycle pool -- the host's
  authoritative copy replicates in instead. AI-friend auto-join is HOST-ONLY in a net
  session (the host runs the AI friends and streams them; the client shows them as
  `ControlDevice.RemoteFriend` puppets -- see the AI-friend bullet below). A SCRIPTED NO-SHIP
  PHASE is the one thing the split cannot simply drop: `GameScene.spawnPlayerNormally` used to
  read `_spawnplayernormally || IsClient`, which kept the joiner from being stranded shipless but
  also made it spawn during Level 1's cutscene -- replaced by a REPLICATED hold, see the
  intro-cinematic bullet below (card 8a7772d6). Initial
  background/music are local; mid-level script beats (messages, music switches,
  boss-phase choreography) do NOT replicate yet -- that is the next card.
- **LEVEL 1'S INTRO CINEMATIC RUNS ON BOTH PEERS (card 8a7772d6).** Level 1 opens with ~10.5 s of
  scripted cutscene (`Lvl1StartDemoEvent`: twenty UFOs fly in, then a hail of bullets, THEN the
  player), and the script is host-only -- so the joiner spawned 1.3 s in and flew around for the
  whole thing, invisible to the host besides (`ManagePuppet` gates the remote ship puppet on
  having a local ship, which during the intro it has not got). User's ruling: mirror the host
  fully -- neither ship on screen until the fly-in, then both together.
  - **PART A, the spawn gate: a BIT IN `MsgShipState`, not an event.** `ShipFlagScriptGate`
    (flags bit 2) carries the host's `!spawnPlayerNormally`; the client reads it as
    `NetSession.PeerHoldsShipSpawn` and the scene's getter becomes
    `_spawnplayernormally || (IsClient && !PeerHoldsShipSpawn)`. Sampled rather than an `Ev*`
    beat because the state PERSISTS for the whole phase (the `EvFx` bullet's own rule), because a
    30 Hz resend is self-healing against loss and reorder, and because it needs no level-entry
    ordering and no JIP catch-up leg -- the stream is flowing long before a joiner's level loads,
    which is exactly the "few seconds a JIP joiner also needs" the card asked for.
  - **TWO CONTRACTS, both asserted rather than argued.** **FAIL-OPEN**: `PeerHoldsShipSpawn` is
    false offline, false on the host and false while the peer is down, so a lost bit, a dropped
    peer or a torn-down session degrades to the PRE-CARD behaviour, never to a joiner with no
    ship. **A LATE GATE NEVER YANKS A SHIP**: the hold only refuses a spawn, it never removes one,
    so a bit arriving after the joiner already spawned just means that peer missed the cutscene.
  - **The RELEASE is the interesting edge, and it is POLLED on the scene's own tick**
    (`GameScene.NetUpdateScriptShipGate`, outside the state switch) rather than pushed from the rx
    handler -- a push would have to land on a scene, and the packet routinely arrives while a JIP
    peer is still warming its level and has none. On the falling edge the client mirrors
    `Level1.demo_OnFinished` (`SpawnAllPlayers(invulnerable: true)`) and LATCHES
    `spawnPlayerNormally` locally, so the rest of the level no longer depends on the wire.
    `UpdateNormal` has no spawn path of its own -- that is why the edge has to do it.
  - **PART B, the volley: `EvIntroVolley` (event 24, reliable, `[seed:4]`).** `Bullet` is NOT in
    `NetTypeRegistry` -- player bullets are never replicated, a remote ship's are re-fired locally
    off the fire stream -- so a correctly gated joiner would watch the twenty intro UFOs pop with
    nothing visibly killing them. The host announces the volley plus a seed and the joiner runs
    its own copy through the shared `Lvl1StartDemoEvent.Volley`, ticked in `UpdateNormal`'s
    `SuppressLevelScript` branch beside the decorative swarms (which is what gets pause, victory
    and resetting for free).
  - **THE CLIENT COPY IS COSMETIC, AND THAT IS A CONTRACT.** `Collides = false` (set AFTER the
    `Add`, since `Bullet.Initialize` sets it and KNI runs `Initialize` inside the `Add`) and NO
    `SetAsploding`, so it can neither kill a puppet, nor file an `EvClaim` for a kill the host's
    own volley is already credited with, nor drop a damaging mini-`Blast`. The visible cost: no
    ricochets, so the joiner's copies fly straight out of the top instead of scattering. **The
    seed cannot make the two volleys identical and does not claim to** -- a bounce re-rolls off
    the shared RNG and the client's copies never bounce; it matches the launch angles, i.e. it is
    the same volley, not the same trajectories. A UFO visibly dying slightly out of step with a
    cosmetic bullet is accepted for a cutscene.
  - **NO PROTOCOL VERSION BUMP.** The gate bit is a previously-unused bit of an existing byte and
    the event is a new type, so both degrade to the pre-card behaviour on an old peer in both
    directions (`HandleEvent`'s switch has no `default:` arm -- the `EvFx` bullet's own bump
    test). `Volley` also moves the host's 70 launch angles off `RandomHelper.Random` onto a
    private seeded `Random`, the `Quad`/`ShipConnector` rule.
  - **Verify with `eaNetIntroGate()`** (`Compat/Net/NetIntroGateTest.cs`, 32 assertions;
    `tools/headless/probes/net_intro_gate.txt`). **DESTRUCTIVE and LEVEL-1-ONLY** -- the host-side
    read is `!spawnPlayerNormally` on a REAL scene and Level 1's intro is the only shipped script
    that sets it, so on any other level every leg is vacuous; it pairs real sessions onto the live
    level, ticks the real scene and spawns the local ship, so use a throwaway
    `?level=Level1&invuln` boot and run it EARLY. Mutation-tested five ways, each failing one
    disjoint assertion. The one caveat worth carrying: the pre-card `|| IsClient` mutation fails
    the GATE assertion and not the 120-tick one, because the suite attaches to a level already in
    `GameState.Normal` where `UpdateStartup`'s 1300 ms branch cannot fire either way -- the gate
    reading closed is what that branch consumes, so it is the leg that carries the bug.
- **Client enemies = NetPuppets (`Compat/Net/NetPuppets`):** real game objects built by
  their own `New*+Setup` factories (the harness-proven path) on `EvSpawn`, then FROZEN --
  `Enabled=false` for life (gameplay Update/AI never runs; `ComponentBin.Pop` is patched to
  not thaw them on unpause) while Draw renders normally and a `CollisionHandler.IsActive`
  seam keeps them hit-testable by the local player's bullets. One `NetPuppetDriver`
  (UpdateOrder -1000, disabled by pause like everything else -- which also freezes puppet
  collisions) dead-reckons `Position += vel*dt`, advances `curframe` at the type's own fps,
  blends snapshot corrections over ~150ms (error > 100px snaps + counts a `pupPops`
  metric), lerps scale, ticks each puppet's `timers` (hit-blink decay), re-applies hp.
  **The driver ticks on REAL time (`Environment.TickCount64` delta, clamped 200ms), never the
  turbo/slow-mo/hit-stop-scaled `gameTime` Game1 folds into components** -- the host mirrors
  its world at its own real pace and stamps every snapshot's observed velocity on real time,
  so a client time-scale window (the wipe's 180ms death hit-stop, a 1-up slow-motion) must not
  stall the dead-reckoning or the correction blend, or the puppets fall behind the real-time
  snapshots and repeatedly snap (this was the first-wipe `pupPops` burst; same rule the
  remote-ship puppet follows). Characterised in `tools/sim/net_puppet_drive_sim.py`.
  - **`pupPops` is meaningless without `snapTurn`, which the `[net]` line now prints** (card
    48ab9b2f). The snapshot cursor round-robins 16 entries per 60ms packet, so an entity is
    corrected only every `live/16*60ms` on average (`NetSession.SnapshotTurnMs`) and dead-reckons
    blind in between. A big world stretches that -- 1.2s at 320 entities -- and how much a pop
    rate SHOULD be expected depends entirely on it. It is the MEAN, deliberately: the cursor
    wraps continuously instead of restarting per cycle, so rounding up to whole packets would
    report 120ms for a 17-entity world whose real blind window is 64ms. **The two peers derive
    it from different counts** -- the host from its authoritative `NetIdRegistry`, the joiner
    from its own puppet count, which lags during spawn bursts and JIP catch-up; the host's line
    is the one to trust.
  - **The 200ms dt clamp is what makes a starved client pop, and it is worth knowing about
    before blaming the link.** The clamp is deliberate (a pause Pop or tab refocus must advance
    the world by at most one over-long frame, never a fling), but a client ticking slower than
    5Hz silently loses `gap - 200ms` of real motion EVERY tick; the error integrates and the
    next snapshot snaps. `--population` measures it: at N=128 a client at 60/40/30/10/5 **and
    3** Hz logs **0 pops/s** -- 3Hz is already losing 133ms per tick to the clamp and still pops
    nothing -- while 1Hz logs **128/s**. So the cliff is between 3Hz and 1Hz, i.e. an OCCLUDED
    or hidden window (rAF paused, timers ~1Hz -- JIP trap 1). A merely SLOW client is fine.
  - **A long `snapTurn` hurts PERIODIC motion by resonance, not big worlds as such.** Same
    sweep, client healthy: a `?flyspiders` swarm logs 0 pops/s at every N from 16 to 2048 --
    flat zero, not a curve -- **except** at N=512, where `snapTurn` (1920ms) lands near half the
    spiders' 4000ms swivel period, the phase at which a velocity measured by finite difference
    across the interval is most wrong about where the entity goes next. There it jumps to 7.2/s
    on Very_Hard and **92.6/s on Inzane**, whose swivel is 20% bigger. Off that resonance the
    +/-25px swivel is simply too small to miss by 100px however long the turn grows, and the X
    drift is exactly linear so it costs a healthy client nothing. Worth remembering when a new
    replicated type moves on a cycle.
  - **But it was NOT the JIP pass' problem, and "the swarm was dense" explains nothing there.**
    `?flyspiders` spawns 5.5/s yet they die at `Position.X < -100`, so it settles at a MEASURED
    `liveIds` 17-19 -- `snapTurn` ~64-71ms, i.e. the floor. (It does not accumulate: the
    "background spiders have `Collides=false` so they pile up" reasoning is about kills, and
    off-screen death is what actually bounds them.) That is three orders of N away from the
    resonance, in the region where the sweep reads a flat zero.
- **World snapshots (`MsgWorldSnapshot` 0x20, stream lane, host->client, 60ms cadence):**
  round-robin cursor over the live NetId set, <=16 length-prefixed entries/packet (~500B).
  Entry = netId + typeIdx + the generic base block (`NetBaseState`: pos, observed vel
  px/ms, rotation, curframe x64, scale x256, hp) + per-type state extras. A snapshot entry
  for an unknown id self-heals: it REBUILDS the puppet from the snapshot (default spawn
  extras) unless that id was removed locally < 3s ago (a death still settling -- ours OR the
  host's). Which of those (plus a REFUSED rebuild) happened is reported per entry as a
  `SnapUnknownKind` and counted separately -- see the `snapNew`/`snapDead`/`snapBad` bullet
  under "Verify with LOGGED METRICS"; they all return "not applied", so the single total they
  used to share could not be judged.
  - **A SELF-HEALED PUPPET IS PROVISIONAL, and the reliable `EvSpawn` REBUILDS it (card
    de4d5d65).** The self-heal calls `OnSpawn` with a literal extras length of **0**, so
    `CreatePuppet` runs on the descriptor's DEFAULTS -- `PowerupDescriptor` skips its `MakeType`
    and the puppet keeps `Randomize()`'s LOCAL RANDOM TYPE (wrong colour AND wrong letter, and
    `ApplyRemotePowerup` then drives the wrong HUD slot when the other peer collects it);
    `UfoDescriptor` gets no `SetAsBonus`, so a bonus carrier draws untinted and its state extras
    can only ever turn a bonus OFF. That was permanent, because the `EvSpawn` carrying the real
    extras arrived second and was dropped as `AlreadyLive`. `PuppetInfo.SelfHealed` now marks
    those puppets and the later `EvSpawn` tears the stale one down and reconstructs it from the
    host's extras (reporting `None`, not `AlreadyLive`); a puppet that already carries its extras
    still refuses the duplicate. **The reported symptom was the UFO/powerup colour mismatch on
    P2; the fix is type-agnostic, so size, sheet, behaviour and every boss variant come with it.**
  - **THE STALE PUPPET IS NOT TOUCHED UNTIL THE REPLACEMENT HAS BEEN BUILT AND LANDED.** The
    spawn extras are bytes off a stranger's wire (public game browser), and a descriptor can
    genuinely decline them -- `PowerupDescriptor` returns null for an unrecognised type byte.
    Tearing the puppet down first would let one bad byte DELETE a working enemy and
    `MarkRemoved` its id, after which every snapshot for `RecentRemovalWindowMs` reads
    `LeftDead`. So all three failure branches (no descriptor, declined, `TryAdd` refused) report
    `AlreadyLive` and keep what they have: a generically-dressed puppet beats no puppet.
  - **The rebuild then DETACHES FROM THE MAPS BEFORE `bin.Remove`, and that order is the subtle
    part.** `bin.Remove` is DEFERRED, so the stale component's `ComponentRemoved` fires on a
    later flush -- by which point the replacement is registered under the same netId. Dropping
    `idByComp`/`byId`/`live` first makes that late event a complete no-op (the handler
    early-returns on an unmapped component); leave them and it evicts the REPLACEMENT and
    `MarkRemoved`s the id, after which every snapshot entry reads `LeftDead` and the puppet is
    never corrected again -- silent, and invisible in any frame.
  - **The corrected POSE is carried across the rebuild.** The `EvSpawn`'s base state is the
    spawn-time one and the snapshot that self-healed the id is by definition newer -- that lane
    skew is the whole reason the puppet existed -- so `Position`/`Vel`/`Correction` come off the
    stale `PuppetInfo` after `ApplySnapshotState`. Without it the enemy teleports back to where
    it entered the world and dead-reckons from there, collidable, until its next round-robin
    turn (up to `snapTurn`, ~1.2 s in a big world).
  - Pinned by `eaNetSnap()` section 6 (40 checks now, up from 18), mutation-tested three ways
    that fail DISJOINT legs -- the pre-card never-rebuild fails 9, the missing detach fails 2,
    the tear-down-before-construction fails 2.
- **Per-type descriptors (`Compat/Net/NetTypeRegistry` + `Compat/Net/Descriptors/`):**
  the wire typeIdx IS the registry order -- append-only, never reorder. A descriptor owns
  (a) puppet CONSTRUCTION: spawn extras pin every random/caller-chosen look (e.g. UFO's
  random small-sheet pick, Powerup's random type); (b) STATE extras: the fields a frozen
  Draw reads that the base block doesn't carry (sheet swaps, phases, landed stills). Types
  needing neither are explicit base-only descriptors with a justification. Private fields
  are reached via small `internal Net*` accessors at the bottom of the game type itself
  (see UFO.cs). Contract + author rules: NetTypeRegistry.cs header; worked example:
  UfoDescriptor.cs.

## Claims, score & per-slot HUD

- **A PEER'S SHIPS BELONG IN A WORLD WE ARE RESPAWNING INTO (card c1cdd3e5).**
  Both puppet spawners gated on `FindLocalShip() != null` -- and OUR ship being absent says
  nothing about whether THEIRS should be drawn. In co-op a death is not a world wipe: the level
  keeps running and the peer really is flying around out there. Reported as *"on a joining client,
  while I was dead and respawning, the other players' ships (who respawned before me) did not
  appear on the playing field until mine did."*
  - **BOTH SPAWNERS, and that is not a detail.** `ManagePuppet` draws the peer's PRIMARY ship;
    `TickFriends` draws its couch players (`?netlocal`, card 4d904410) and the host's AI friends.
    All of them are "the other players' ships" to whoever is reading the screen, and a room holds
    four machines plus couch seats (card 0257f8ba) -- so a fix reaching only `ManagePuppet` leaves
    the report standing for most of a full room. The first round of this card did exactly that;
    the probe now carries that shape as its own mutation.
  - **The discriminator is our own RESPAWN SUMMON** (`NetSession.WorldTakesPuppets`), and it is
    exact rather than convenient -- but **the load-bearing fact is NOT "every wipe purges the
    summon"**. It is that **every wipe arms a standing `Purge<T>` filter, and the filter and that
    wipe's queued removals expire together in the SAME `TopOfTickFlush`**: so while a summon of
    ours is still in `Game.Components` after a wipe, the filter that ate it is still armed and
    `bin.TryAdd` refuses the puppet anyway. (`Purge` matches with `Type.IsInstanceOfType`, so the
    base-typed `Purge<AlienDrawableGameComponent>` in `Terminate` / `UpdateWin` /
    `UpdateResetting` covers `PlayerShip` too.) `PlayerShipSummon.ShouldSummon` seals the other
    end: a summon is only raised while ANOTHER ship still flies, so in single player -- where a
    death IS the wipe -- there is never one and the second arm can never open.
  - **THE ONE WIPE THAT ARMS NOTHING is a CLIENT's own `GameScene.LoseLife`**, which early-returns
    on `NetSession.IsClient` before both its purges -- a joining client's wipe only ever arrives as
    the host's `EvReset`. Between "our world went shipless" and that EvReset landing there is
    genuinely no filter. Still safe, but for a different reason: a wipe means every peer reported
    dead, so `ch.Alive` is false on every channel and neither spawner is reached. **Do not restate
    the purge argument for that window.**
  - **`!IsCosmetic` is NOT redundant with the seat test.** `HandleRespawnEvent` refuses to raise a
    cosmetic summon over a slot we own, but `OwnsSlot` is DEVICE-based, so an UNSEATED slot answers
    "not ours" -- and `SlotAdopt.TakeSlot` assigns `localPrimarySlot` without seating it. That is
    the reconnect-race window the respawn handler names, so the two terms are separable state.
  - **The gate was never the real guard against a standing purge; `bin.TryAdd` in `SpawnPuppet` /
    `SpawnFriend` is** (card 74403f83), which is what makes it safe to relax at all.
  - **Evaluated ONCE per `NetSession.Update`**, not per peer: it asks about OUR world, and nothing
    the peer sweep does can change it (`FindLocalShip` matches a locally-owned ship in
    `localPrimarySlot`; the spawners only seat `Remote`/`RemoteFriend` in a PEER's slot).
  - **KNOWN NEW BEHAVIOUR: victory.** `UpdateWin` does not purge until t+4 s, so if we die and a
    partner wins, their ship can now pop in during the victory choreography and be purged four
    seconds later. Arguably right -- their ship really is flying the victory thrust -- but it is
    new, visible, and nothing pins it.
  - Pinned by `NetResetSpawnTest` leg 1b, the PAIR of leg 1: identical shiplessness, one bit
    different, both spawners asserted, opening with ONE NEAR MISS PER TERM (a real summon on
    somebody else's seat, then a COSMETIC summon on our own). Mutation-tested five ways --
    including the ManagePuppet-only fix and deleting the gate outright, which reddens legs 1 and 2
    instead, so the suite bounds the fix from both sides.

- **GENEROUS at-least-once claims -- no arbitration, no rejection path.** Kills: local
  hit-testing runs the REAL per-type death on whichever peer observed it (explosion,
  sound, score, combo paid locally); the client's removal seam sends `EvClaim(netId,
  killerSlot)` for every gameplay death (`IsDead` distinguishes `Die()` from teardown
  purges). Host on a claim: entity alive -> real kill via `KillableAlien.NetKill` with a
  scratch-Bullet killer carrying the claimant's slot (authoritative children spawn there
  and replicate); already dead -> pay the claimant once from a bounded recent-death
  record. Host broadcasts `EvDeath(netId, killerSlot, pos, points)` for every replicable
  removal (killerSlot from the `NetSession.NoteKill` hook in KillableAlien.HitBy);
  client: live puppet + killer -> local NetKill (FX + credit), `KillerSelf` -> the type's
  own death FX and nobody paid (next bullet), no killer -> silent
  despawn, already dead -> pay the killer once. Per-(netId, slot) paid ledgers both sides
  = every distinct claimant credited, nobody credited twice. Powerups are the same claim
  shape: the real PlayerShip pickup runs instantly on the collector (a
  `NetSession.NotePowerupTaken` hook attributes it), first claim despawns the entity,
  overlapping collectors inside the RTT window BOTH keep it.
  - **A DEATH NOBODY LANDED IS `KillerSelf` (0xFE), NOT `KillerNone` (cards 4e406eba /
    303bfb5b / 13aa596c).** `StarMine.Asplode()` is a real death -- two blue bursts and a cue --
    that never runs `KillableAlien.HitBy`, so `NoteKill` never fires and the host used to
    broadcast `KillerNone`, whose client branch is a bare `bin.Remove`: **the mine simply blinked
    out on P2.** The two cases cannot share a value, because `KillerNone` is also every
    off-screen fly-off and every teardown purge, and exploding those puts a bang AND A SOUND
    where the host showed nothing.
    - **NO PROTOCOL CHANGE AND NO VERSION BUMP.** It reuses `EvDeath`'s existing killerSlot
      byte, whose 0x08..0xFE were all dead values (`Oracle.MaxPlayers` is 4). A peer that would
      misread it cannot exist: the build-hash handshake refuses to pair two different binaries.
      Validated at the decode boundary like every other raw wire value --
      `NetProtocol.ClampKillerSlot`, which **CLAMPS rather than REJECTS** (the message also
      carries the REMOVAL, and dropping it strands a puppet the host has deleted, permanently,
      since no later snapshot will mention that id). Its payable bound is **8, the PaidMask
      width, not `MaxSlots`** -- `NoteKill` already admits 0..7.
    - **It is OPT-IN at the death site (`NetSession.NoteSelfDestruct`), never inferred.** The
      obvious alternative -- "`IsDead` at the removal seam" -- cannot tell a self-destruct from
      the dozens of FX-free `Die()` sites that mean "I have left the world" (`OffScreen`
      despawns, `Parachute`'s fade-out, `ParatrooperBrain`'s merge, a `Lazer` eaten by the spider
      boss), so it would explode all of them. A hook says exactly what the game meant.
      **`OnHostDeath` still gates it on the entity being ON SCREEN** (±100 px, the same buffer
      the self-despawning types pass to `OffScreen`), so a self-destruct the host itself showed
      nothing of stays silent.
    - **The client replays the type's OWN self-destruct look**, via
      `KillableAlien.NetReplayUnattributedDeath` -- default `NetKill`, overridden by `StarMine`
      to run `Asplode()`, because being shot (one small white burst, `expl1`) looks nothing like
      detonating (two big blue bursts, `expl2`). Award-suppressed first, per the b0ab09ec rule.
  - **A DEFERRED death RELEASES its puppet from the freeze, and the host says so EXPLICITLY
    (cards 303bfb5b / 13aa596c for the release, `f62116b5` for the trigger).** `BattleSkull` and
    the surviving `MarsBoss` put their WHOLE death in an Update-driven state machine (2.5 s of
    shrink-and-flicker; a 5 s crash to the ground) -- and a puppet is `Enabled=false` for life, so
    none of it ran. Worse, their `EvDeath` does not arrive until that animation ENDS on the host,
    so the peer saw an intact enemy and then, seconds later, one frame of removal.
    - **`EvDying(netId)` (event 23, reliable, 6 B, protocol v11) is the trigger: the host emits it
      the moment a `KilledBy` returns with the component STILL IN THE WORLD.** The discriminant is
      `!IsDead` after `KilledBy`, which is exactly the test the client already made to spot the
      same thing, so the two ends agree by construction and an ordinary type -- whose `KilledBy`
      ends in `Die()` -- sends nothing. Two call sites, both in `KillableAlien`
      (`HitBy` and `NetKill`, the latter because the host also kills through it when the CLIENT
      landed the blow), so a new deferred-death type costs nothing. `NetSession.OnHostDeathBegan` ->
      `NetPuppets.OnDeathBegan` -> the shared `BeginDeferredDeath`.
      **It carries no killer and no award: this is the death BEGINNING**, and the `EvDeath` that
      lands when the animation ends still settles who was paid, exactly as before.
    - **`OnHostDeathBegan` has NO `NetScene.Current` gate, unlike `OnGameFx` and the script beats.**
      It is an entity-lifecycle event off the `NetIdRegistry`, like `OnHostSpawn`/`OnHostDeath`,
      and the registry's own enablement is what decides whether a world exists. Adding
      one would also make `eaNetDeathFx`'s host section unreachable, since that suite plants real
      entities from the MENU. The client's rx handler IS scene-gated, as `EvDeath`'s is.
    - **`NetBaseState.Hp == 0` on a puppet we know is KILLABLE also means the host has killed it,
      and that stays as the FALLBACK** -- `Initialize` floors hit points at 1, `NetApplyHp` floors
      at 1, and `HitBy` reaches 0 only on the killing blow. The **discriminant is `NetKillable`,
      not the value** (`Hp` is also 0 for every non-killable, which is what its own "0 = not
      killable / unknown" comment means). It covers the two cases a live beat cannot: a peer that
      JOINED IN PROGRESS after the death began, and any future deferred-death path that does not
      go through `KillableAlien`.
    - **The fallback now needs TWO CONSECUTIVE hp==0 turns, and that is what removed the
      one-tick-early residual.** The host's `ComponentBin` defers removal, so an ORDINARY kill is
      still in the registry for the one tick between the killing blow and the flush -- and a
      snapshot turn landing in that tick used to run the death here, award-free and with the
      `KillerNone` scratch agent, a tick before the attributed `EvDeath`. That was accepted while
      this was the only fast trigger, because narrowing it cost the deferred case a whole
      `snapTurn`; it does not any more, since `EvDying` owns the live case and the extra turn is
      only ever paid on a path nothing reaches today. A `PuppetInfo.SawZeroHp` latch, cleared by
      any hp>0 turn.
    - **A peer JOINING IN PROGRESS mid-animation gets the beat with its catch-up spawn** --
      `NetIdRegistry.ReplayLive` sends one for every live entry already at zero hit points and
      not yet dead. Without it the joiner would be the one case paying the two-turn rule above,
      and paying it twice over (up to ~2.4 s of a 2.5 s animation) -- i.e. the very symptom the
      release exists to fix, on the very peer it exists for.
    - **A deferred-death type the CLIENT killed itself is released too, and at RTT rather than at
      the end of the animation.** Its own `KilledBy` ran locally, so its hp is already 0 and it
      has been standing frozen mid-animation; `BeginDeferredDeath` skips the FX (a second
      `NetKill` is a no-op anyway) and releases. Pre-card only the late `EvDeath` reached it.
    - `NetPuppets.ReleaseDyingPuppet` drops the entity from `byId`/`live`/`idByComp`, clears
      `Collides`, sets `Enabled = true`, and its own `Update` finishes dying locally -- which is
      what card 13aa596c's note asked for ("animation doesnt need to be syncd and can be done
      locally"). Dropping `idByComp` is what makes the eventual local removal a no-op at the
      claim seam (the host already knows).
    - **...which is exactly why `MarkRemoved` has to run BY HAND in the release.** That seam is
      what normally marks the id, and skipping it leaves the next snapshot entry an UNKNOWN id:
      the self-heal then rebuilds a fresh, intact, collidable enemy standing on top of the one
      that is visibly dying.
    - **AND THAT SUPPRESSION NEEDS ITS OWN, LONGER WINDOW -- the flat one is NOT enough, and this
      bullet used to say it was (card 444eb614).** It read "short window (the host stops streaming
      the id within a turn or two), so it would have been a rare unreproducible ghost". **False,
      for exactly the deaths that reach this line**: a dying entity is still in the host's world,
      so it is still in `NetIdRegistry.Live`, so the host keeps streaming its id for the WHOLE
      animation. `SpiderBoss`'s debris fall is `5000 / DifficultyFactorized(0.5)` -- **5.0 s at
      Very_Hard, up to 7.4 s on Easy**, ~3.3 s once the modifier has ramped -- and the surviving
      `MarsBoss`'s crash is 5 s, the `FakeBoss`'s is 4 s, the `JunkBoss`'s 25 explosions run
      2.5-3.75 s, and the **`BrainBoss`'s asplode is TWENTY seconds**. `RecentRemovalWindowMs` is
      **3000**. So the ghost was not rare at all: it appeared partway through most boss deaths and
      stood until the host's `EvDeath`.
      Reported as *"spider boss (lvl 2) -- on a joining client, after the boss was defeated and
      its death animation played, the original sprite still appeared for a few frames"*, and it
      names the SpiderBoss because once `dead` its `Draw` shows only debris, so the rebuilt puppet
      is the only thing on screen still wearing the boss's own sprite. (On a `KillableAlien` the
      rebuild is worse than cosmetic: hp arrives as 0, so `ApplyHostKilledFromSnapshot` re-runs
      `BeginDeferredDeath` and the client plays a SECOND death.)
      - `NetPuppets.releasedDying` flags the ids `ReleaseDyingPuppet` marked, and
        `IsRecentlyRemoved` gives those `DyingReleaseWindowMs` (**30 s**, 1.5x the BrainBoss's
        20 s) instead. **The honest deadline is an EVENT, not a duration**: the host stops
        streaming when its own copy leaves
        its world and says so with `EvDeath` on the reliable lane, so the flag is CLEARED there
        (and on a successful `OnSpawn`, for an id the host re-used) and the 30 s is only the
        backstop for an `EvDeath` that never comes. Erring long costs only a refused self-heal,
        and while the entity is still dying the host IS still streaming it, so there is nothing to
        rebuild anyway. **`NetDeathFxTest` measures each boss's real animation against the
        constant**, so a future longer death fails there instead of quietly ghosting.
      - **It is its OWN ledger, not a flag beside `recentlyRemoved`, and that is correctness.**
        Read off that dictionary's timestamp it would have to be evicted with it -- and
        `MarkRemoved` fires on EVERY local puppet removal against a 512-deep FIFO, while
        `BrainBoss.KilledBy` purges bullets, braineroids, skulls, mines, UFOs, lazers and plasma
        balls on its way into that twenty-second death. The boss's own slot would be pushed out by
        the churn of its own opening frame.
      - **The long window is for RELEASED deaths ONLY** -- widening `RecentRemovalWindowMs`
        itself would leave a genuinely-missed spawn missing for thirty seconds. That is a
        separate leg in the suite, not an argument.
      - Pinned by `NetDeathFxTest` section 9's GHOST legs: the same call at three clock readings
        (inside the flat window, past it, and after the host's `EvDeath`) plus an
        ordinarily-removed id as the control, plus the OnSpawn clear on its own leg -- and
        `NetDeathFxTest`'s per-boss legs additionally require each MEASURED animation to fit the
        window. Mutation-tested four ways; reverting the fix reproduces the report in numbers
        (`4 vs 3` SpiderBosses in the world). The ledger's own eviction cap is the one part with
        no leg: reaching it needs 64 concurrent deferred deaths, and it degrades to the
        pre-card behaviour, which shipped.
    - **`OnRemoteDeath` makes the same decision** when neither the beat nor the snapshot got
      there first -- the last-resort fallback, and the only one before card 303bfb5b.
    - **A DEFERRED DEATH THAT DOES NOT RUN THROUGH `KillableAlien` AT ALL: the SpiderBoss, and
      the `NetIsDying`/`NetBeginDeferredDeath` seam it needed (card ad9c8f8b).** The bullet
      above says the snapshot fallback's remaining job is "a deferred-death path that reaches
      its dying state WITHOUT going through KillableAlien, i.e. nothing today". That was wrong:
      **`SpiderBoss` derives from `AlienDrawableGameComponent`, not `KillableAlien`** -- only a
      `Lazer` hurts it and its whole death lives in `CollidesWith`. So `HitBy`/`KilledBy`/
      `NoteDeathBegan` never ran (no `EvDying`), its `NetKillable` was null so
      `ApplyHostKilledFromSnapshot` returned before the hp test (the fallback was structurally
      unreachable, not merely slow), and the `EvDeath` at the end of its 5 s debris fall carried
      `KillerNone` because nothing had called `NoteKill`. **The join peer saw an intact boss
      stand there for five seconds and then silently vanish** -- no debris, no explosions, no
      cues.
      - **Two members on `INetEntity`, no protocol change, protocol stays v18.** `NetIsDying`
        (the host's "announce this" discriminant, which `NetIdRegistry.ReplayLive` also reads
        for the join-in-progress re-announce) and `NetBeginDeferredDeath()` (the client's "run
        your own death"). **The BASE derives both from the killable discriminant** -- a
        `KillableAlien` at zero hit points still in the world -- which is exactly the test
        `ReplayLive` used to spell out, so every other type is unchanged by construction and a
        future such type costs one override.
      - Host side, `SpiderBoss.CollidesWith` calls `NetSession.OnHostDeathBegan(this)` at its
        death entry; client side it re-runs `BeginDeathThroes` (the death entry lifted out of
        `CollidesWith`), **idempotently** -- this peer hit-tests puppets with its own beams, so
        it may already have run the same death, and a second burst would restart the 5 s fall.
      - **`NetPuppets.BeginDeferredDeath` now accepts a NULL killable**, claims the award slot
        and asks the entity; a `false` answer releases NOTHING, or a stray beat would un-freeze
        live enemies into the client's world. **The hp==0 snapshot fallback still needs the
        killable discriminant** and is untouched -- hp is 0 for every non-killable, so there is
        nothing there to read. `EvDying` rides the reliable lane and `ReplayLive` covers the
        join-in-progress case, so the fallback is not missed.
      - **`SpiderBoss.NetState` still clamps a wire `dead` back to `standing`, and must**: the
        wire only ever describes a live boss and the death arrives as the beat.
  - **A DEFERRED DEATH THAT STAYS FUNCTIONALLY ALIVE: the SpiderHelperMothership (card
    1878b321), and the `NetDyingStaysReplicated` seam it needed.** The helper's `KilledBy` only
    FLAGS the death -- the ship keeps flying its charge/fire mission for seconds, erupting booms,
    and `Die()`s at `CrashImpact` -- so "release and finish dying locally" was wrong for it twice
    over: its `HelperState` is not replicated, so a released puppet restarted at Setup's `enter`
    and TELEPORTED off-screen left to REPLAY the whole entrance/charge/fire (the joiner's "hangs
    around when dead"), and the host's copy is still a live, laser-firing world entity for the
    whole remnant, which a released local crash cannot mirror. Three parts, no protocol bump:
    - **`INetEntity.NetDyingStaysReplicated` (helper only): the death-began beat does NOT
      release.** The host streams the id for the whole dying remnant, so the frozen puppet keeps
      tracking it -- position, charge glow and hp-redden already ride the wire, and a replicated
      DYING BIT (the descriptor's flags byte, bit2) drives the same death booms locally through
      `NetDriveExtras` (private RNG, the PlasmaBall rule). The hp==0 snapshot fallback declines
      the release the same way.
    - **The final `EvDeath` plays the CRASH IMPACT locally**: `OnRemoteDeath` consults
      `NetBeginDeferredDeath` before releasing a deferred killable, and the helper's override
      runs `CrashImpact()` -- three explosions + `expl2` at the replicated crash-end position,
      `Die()` included, so nothing is released. A `true` that did not die still releases (the
      safe default); base types answer false and release exactly as before.
    - **A CLIENT's own kill of ANY deferred-death type now files its claim at death-began**
      (`KillableAlien.HitBy` -> `NetSession.OnClientDeferredKill`): the claim normally rides the
      removal seam, which a frozen puppet's deferred `KilledBy` never reaches, so the kill was
      PHANTOM -- the joiner's 50-hp investment left a red, unresponsive zombie while the host's
      copy flew on untouched. Ordinary types are untouched (`IsDead` is already true at the call
      site), and no double claim is possible (the puppet is only ever removed after
      `ReleaseDyingPuppet` unmapped it or `OnRemoteDeath` guarded it). **`HandleClaim` in turn no
      longer force-removes a killable whose `NetKill` deferred** -- the old `bin.Remove` deleted
      a claimed helper mid-mission where the host's own kill let it finish; `NetKill`'s
      `NoteDeathBegan` already announces `EvDying` to the claimant.
  - **Verify with `eaNetDeathFx()`** (`Compat/Net/NetDeathFxTest.cs`, 187 assertions;
    `tools/headless/probes/net_death_fx.txt`). MENU-ONLY and leave-no-trace, the `eaNetSnap`
    shape -- section 2 runs a real HOST session over a `NetWire` and reads the frames the peer
    RECEIVED (including the `EvDying` trigger-latency legs: the beat is on the wire while no
    `EvDeath` is, because the host will not remove the entity for another 2.5 s); sections 3-6
    need no session, only `NetPuppets.Enable`, and **section 6 delivers NO snapshot at all**,
    which is what makes it a latency assertion rather than a duplicate of section 4. Everything it plants sits
    far off-screen, so nothing it does is drawn, its explosions included. **The observable is the
    WORLD** (live `Explosion` count, membership of `Game.Components`, `Enabled`, the score
    panels): the symptom is the ABSENCE of a one-to-five-second effect, so a timed screenshot
    proves nothing and a backgrounded joiner tab ticks at ~1 Hz. Every positive has its negative
    beside it -- **the `Enabled` assertions are the load-bearing ones**, since a puppet left
    frozen is still in the world and would satisfy a survival-only check, which IS the bug.
    Mutation-tested sixteen ways, failing DISJOINT legs across the two defects, the trigger and
    the coverage --
    notably, making `NetPuppets.OnDeathBegan` a no-op fails section 6 and ONLY section 6, which
    is what proves the two fallbacks are still real rather than dead code behind the fast path.
    **Sections 8 and 9 are card ad9c8f8b's, and they are what a release-only assertion cannot
    do**: 8 releases BrainBoss / FakeBoss / JunkBoss (the last being card c146422f's elongated
    25-explosion death) and TICKS the released component's real `Update` on a fixed 60 Hz dt --
    the isolation-sim pattern, because a 3-to-20-second choreography is exactly what "never
    verify motion with timed live screenshots" is about -- asserting the tally keeps CLIMBING at
    an intermediate checkpoint, that the boss is still in the world there, and that it `Die()`s
    on its own rather than leaving a corpse in every client's world; 9 is the SpiderBoss seam
    above, with an `EvilBullet` as the negative that a non-killable with no deferred death of its
    own is released by nothing. **`BloodExplosion` is not an `Explosion` subclass** and the
    SpiderBoss's entire debris death is made of it, so the suite counts both. **Leave-no-trace
    is asserted by RUNNING THE WHOLE SUITE THREE TIMES in one process** (the `eaBinTest` rule):
    the teardown sweeps `Explosion`/`BloodExplosion`/`BrainAura`, restores the score panels and
    restarts the music `BrainBoss.KilledBy` stops, and a leak would surface as phantom failures
    in the second run.
    **Deliberately absent from `net_selftests.txt` despite being menu-runnable** -- unlike the
    suites there it has its own probe, which carries this card's write-up and mutation matrix, so
    listing it in both would run it twice for nothing. (It is NOT absent for `eaNetBgTest`'s
    reason: it needs no level.)
  - **THE HOST'S LEDGER OPENS AT THE CLAIM, NOT AT THE REMOVAL FLUSH (card 1bfcd705), and it
    takes TWO stores to say that.** `recentDeaths` cannot be the whole ledger, because
    `OnHostDeath` only writes a record at the `ComponentRemoved` seam -- one `ComponentBin`
    flush after the claim that settled the entity. So `NetIdRegistry.Entry` carries the ledger
    for that window (`ClaimSettled` + `ClaimPaidMask`) and `OnHostDeath` folds the mask into the
    record it writes (`RecordDeath`'s `prepaidMask`). Fold, never merge: `recentDeaths[id] = rec`
    stays a straight assignment, so a wrapped netId can never inherit a stale mask, and the Entry
    dies with the entity so nothing needs bounding.
    - **`IsDead` is NOT "already settled".** A `Powerup`'s settle path calls `NetMarkTaken()`
      (which sets `taken`, not `isdead`) and a plain non-killable is only `bin.Remove`d, so
      neither flips it -- which is why `ClaimSettled` exists and is what keeps the live branch to
      one run per entity. Without it, every same-tick claim re-ran the whole pickup path,
      `AddLife` included: measured **one free life per claim frame** a peer could fit in one
      `DrainRx`.
    - **The window is one tick wide and it is reachable on the 2-peer wire**, not only at N
      peers. `Game1.UpdateInner` runs `TopOfTickFlush` -> `base.Update` ->
      `collectionHelper.Update` -> `DetectCollisions` -> `NetSession.Update`, so a host kill in
      the COLLISION phase is dead-with-removal-queued when that same tick's `DrainRx` runs.
      Scenario 3b is that shape; 2b/2c/4c are the N-peer same-tick ones.
- **AN UNATTRIBUTED CLAIM MEANS THE JOINER LOST THE ENTITY, NOT THAT IT KILLED IT (cards
  9ccfe295 / 54e9a590, protocol v18).** Reported as "large (laser firing) ufo's seem to randomly
  disappear" -- on the HOST's screen -- and "P2 does in fact shoot these, but sometimes they do
  not play the explosion effect on P1's screen". The card guessed at a late kill message for a
  reused object id; **the id scheme is innocent**, and the defect is a four-step chain in which
  no single step is a bug.
  - **The chain.** (1) `Lazer.owner` is written ONLY by `Lazer.Setup`, which no puppet runs --
    `LazerDescriptor` builds every client beam through `SetupSingleShot`, so a joiner's beam had
    NO emitter. (2) `UFO.CollidesWith` damages itself off any `Lazer` whose `owner != this`,
    which with a null owner is TRUE for the ship that fired it. (3) The killing blow's
    `NoteKill(this, other)` gets a `Lazer`, which is not an `IAlienKiller`, so the note is
    `KillerNone`. (4) The removal seam sent `EvClaim(netId, KillerNone)` anyway and
    `HandleClaim`'s live branch fell through to the NON-KILLABLE arm's bare `bin.Remove` -- the
    host's healthy UFO deleted with no explosion, no cue, no award, and a `KillerNone` `EvDeath`
    whose client branch is also a silent despawn.
  - **WHY IT WAS INTERMITTENT, and the number that settles it.** At the exact fire pose the
    beam's 75 px lead CLEARS the emitter's hitbox -- they do not meet. What closes the gap is
    DRIFT: the UFO and its beam are separate puppets, corrected on separate snapshot round-robin
    turns and dead-reckoned blind in between. Measured by the suite rather than assumed:
    **11 px** of relative drift along the beam is enough, against a `SnapThresholdPx` of 100 and
    corrections that blend over >= 150 ms. So it is easily reached, and reached more often as
    `snapTurn` grows -- i.e. in the dense waves the report described.
  - **FIX 1 -- the emitter goes on the wire.** `LazerDescriptor` gains `[ownerNetId:2]` spawn
    extras (0 = none), resolved client-side through `NetPuppets.FindPuppet`. Ordering holds by
    construction: the emitter's `EvSpawn` always precedes its beam's on the ORDERED reliable
    lane (it existed first), and `ReplayLive` walks `liveList` in spawn order for a JIP peer. An
    id that does not resolve leaves `owner` null, i.e. the pre-card behaviour.
    **`SetupSingleShot` now CLEARS `owner`** -- `Lazer` is pooled, so a recycled beam otherwise
    inherited the previous emitter and would spare the WRONG enemy, on BOTH peers. Same recycle
    trap `netSweepRadPerMs` documents two lines above it -- and `Lazer.OnComponentRemoved` closes
    its MIRROR IMAGE, the EMITTER being recycled out from under a live beam, which is a latent
    OFFLINE bug the puppet owner merely made easier to reach.
    **A null owner is a real answer, and only `JunkBoss` (plus `GameScene`'s off-screen warm-up
    prime) produces one** -- both motherships fire through `Setup`, and `SpiderHelperMothership`
    READS `lazer.owner == this`.
  - **FIX 2 -- an unattributed claim never settles a live entity, and it is the half that
    generalises.** `payable` is false for `KillerNone`, `KillerSelf` and any out-of-range slot;
    `HandleClaim` now returns before the live branch, keeps the entity, counts
    `ClaimsUnattributed` and **RE-ANNOUNCES it with `OnHostSpawn`** -- the same call the JIP
    catch-up makes, so no new protocol. That last part is not decoration: the joiner has already
    dropped its puppet and `MarkRemoved` the id, so without it the enemy blanks for
    `RecentRemovalWindowMs` and then self-heals into a GENERICALLY DRESSED puppet (card
    de4d5d65's provisional shape) with no later `EvSpawn` to correct it.
    **THE CLIENT STILL SENDS IT, and that is the repair path rather than an oversight.** It has
    already `MarkRemoved` the id, so a client that stayed quiet would leave the enemy missing for
    `RecentRemovalWindowMs` and then self-heal it into a generically-dressed provisional puppet
    no later `EvSpawn` corrects -- i.e. suppressing the send makes the two halves of this fix
    cancel out. It was written that way first and reverted.
    **`KillerSelf` is deliberately NOT routed here**, though it is equally unpayable: it is an
    OPT-IN report that a real death happened (a `StarMine`'s own `Asplode`, card 4e406eba), so it
    settles on its pre-card path and simply credits nobody. A guard written as a bare `!payable`
    swallows it, and the claimant then watches the mine it just detonated pop back onto its
    screen off the re-announce -- which is why the guard reads
    `killerSlot != KillerSelf && !payable` and the arm below KEEPS its `payable` tests.
  - **The self-hit was the loudest route to step 4, not the only one** -- which is why fix 2
    ships even though fix 1 removes the reported symptom. `UFO.CollidesWith` calls `KilledBy`
    DIRECTLY for `Floorbottom`, `Spider`, `FlyingSpider` and `SpiderBoss`, none of which
    attribute anything, and a dead-reckoned puppet reaches the first of those on its own.
  - **A BLANKET PUPPET-vs-PUPPET COLLISION FILTER WAS CONSIDERED AND DECLINED (user ruling) --
    do not re-file it.** It looks like the tidy root fix ("the client should not simulate
    enemy-vs-enemy at all, the host owns it"), and it has a known regression: `Floor.CollidesWith`
    is what casts every enemy's ground shadow, so filtering the pair by "both are puppets" or by
    "the puppet ignores non-player contacts" silently drops shadows on Mars. With fix 1 removing
    the observed mis-simulation and fix 2 removing its destructive consequence, there is no
    remaining victim to justify the risk.
  - **Verify with `eaNetIdReuse()`** (`Compat/Net/NetIdReuseTest.cs`, 48 assertions;
    `tools/headless/probes/net_id_reuse.txt`). MENU-ONLY and leave-no-trace, the `eaNetFx` shape.
    **The leg that carries the card is a PAIR** -- the ownerless configuration DAMAGES the
    emitter over the identical geometry and the owned one does not -- because "the UFO survived"
    passes on a build where the two never collided at all; "another ship's beam still hurts" sits
    beside them as the over-correction control. Mutation-tested five ways, each failing DISJOINT
    legs, and the fifth is the evidence the card rests on: `HandleClaim` restored to its **exact**
    pre-card shape deletes the entity, broadcasts its `EvDeath`, and leaves "no explosion was
    spawned" still PASSING -- i.e. it reproduces the silent vanish verbatim.
  - **KNOWN, ACCEPTED: the re-announce is 1:1 with inbound claims.** A peer spamming
    `EvClaim(KillerNone)` makes the host emit one `EvSpawn` each -- strictly less traffic than
    the reliable frame the attacker already sent, so it adds no amplification. Bounding a
    hostile peer's message RATE is card `2da92af9`'s surface, not this one's.

- **SCORE: ONE WRITER PER SLOT (card af96bcc2, protocol v20).** Each slot's score has exactly
  one writer -- its owner. A kill is credited instantly and FINALLY by whoever observed it, on
  their own slots, with their own combo multiplier (`AwardScore`/`AwardScoreToAll` gate on
  `NetSession.OwnsSlot`, which is unconditionally true offline); the peer's copy of a slot is a
  plain replica, adopting the owner's declared TOTAL off `MsgHudState` (~10 Hz, `[score:f32]`
  per owned-slot entry) verbatim in `ScoreVisualiser.NetSetScore`. Nothing is arbitrated,
  nothing settles, nothing is provisional -- and double credit for one kill (each peer paying
  its own side, `AwardScoreToAll` bosses included) is intended, as are double powerup pickups.
  - **A replica of one writer cannot drift, only be one packet (~100 ms) stale.** Drift needs a
    second writer to disagree with; the whole machinery below existed to reconcile two.
  - **What this DELETED, kept as one history bullet so nobody re-derives it:** every kill used
    to be credited on BOTH peers, each recomputing the award with its own drifting combo, and
    three generations of reconciliation patched the consequences -- the original `max(local,
    host)` adoption (a ratchet: an unbiased per-kill error integrated into one-way drift), card
    b0ab09ec's `NetScoreLedger` (provisional booking, settle-on-`EvDeath` via a per-slot award
    array on the wire, 3 s `AwardSettleWindowMs` expiry, `host + unsettled` adoption at the
    1 Hz sync), and card 94001db7's displayed-vs-authoritative oracle correction in the JIP
    suite. All deleted: the ledger class, the `EvDeath` award array (v7's widening, reversed),
    `NetSuppressAward`, the settle choreography in `NetPuppets`, and the score half of
    `EvScoreSync` (now `[lives:1]` -- lives were never per-slot and stay host-authoritative).
  - **The host does NOT credit a claimant's slot** (`HandleClaim` settles the entity, plays the
    world FX and still `AddLife`s a claimed OneUp -- lives are the host's pool); the claimant
    credited itself at its own kill observation. Kill/pickup FX, the claim ledgers'
    at-most-once SETTLE bookkeeping and the re-announce path are all unchanged.
  - **A JIP joiner adopts the running totals from its first `MsgHudState` packet** -- no award
    history to replay, which is what made the old design's join-in-progress corrections
    necessary in the first place.
  - **Verify with `eaNetScore.test()`** (`Compat/Net/NetScoreTest.cs`, 11 checks): the policy
    on a virtual clock against a synthetic two-peer kill stream, with BOTH superseded designs
    run over the identical stream as negative controls (the max() ratchet must drift, the naive
    two-writer must disagree), plus the v20 wire legs against the live `ScoreVisualiser` --
    including verbatim adoption DOWNWARD, the move the ratchet refused. `eaScore()` still dumps
    per-slot score/combo (`unsettled` is gone from it, with the ledger).
- **A remote pickup runs the collector's SHIP effect too, not just its HUD icon (cards
  83271f3d / 10f9dba4 / d53431b4).** `ApplyRemotePowerup` used to be `score.SetPowerup` plus a
  sound cue, so the other player's ship -- a puppet on this screen -- got the readout and none of
  what the powerup DOES. It now calls `PlayerShip.NetApplyRemotePickup`, the mirrorable subset of
  the local `DoSpecial(pickup: true)`. Only two types need it; the rest are inert there for
  reasons that are already elsewhere:
  - **`Linker` (the "2") -- `readyToConnect` is written NOWHERE else**, so the puppet never lit
    its `singleconnectorglow` AND `PlayerShip.CollidesWith`'s `(readyToConnect &
    other.readyToConnect)` was false on BOTH peers: **the ship connector was unreachable in any
    online session**, which is what card 83271f3d reported. Formation stays a symmetric LOCAL
    simulation -- each peer forms its own connector off its own collision, `isConnectedWith`
    dedupes, and `NetPullOwnShip` (the TeamChallenge tether path) already handles the net case --
    so there is no form event and no protocol change.
  - **`Option` -- NO LONGER ON THIS PATH AT ALL (card c5228350, protocol v16). The option
    population is OWNER-AUTHORITATIVE: `MsgHudState` carries the per-LAYER COUNT and the observer
    reconciles its puppet to it** (`PlayerShip.NetSetOptionCounts`, called from
    `NetSession.HandleHudState` AFTER `NetSetHudState`, whose level loop spawns the level-driven
    ones itself). The `case Option:` here is deleted; do not re-add it as a low-latency estimate.
    - **Why the two-derivations design could not be finished.** It ADDED UP correctly in steady
      state (a pickup never changes a level, so `DoSpecial` and `PowerUp` are disjoint), but both
      halves were DERIVED, and a JOIN-IN-PROGRESS peer replays no `EvClaim` -- so it reconstructed
      the level half alone and was permanently short, whatever the steady-state arithmetic did
      (card 10f9dba4's own residual, filed as c5228350). The same fix absorbs PR #264's other
      residual (a pickup landing within one ~10 Hz packet of a level-up to 3/4 derived the count
      from a stale `optionLevel`): there is no second derivation left to be stale.
    - **PER LAYER, not a total**: `options[0]`/`options[1]` are two orbits at radius 40 and 60, so
      one number would let the observer hang the owner's outer ring on the inner one.
    - **It reconciles DOWNWARD as well**, which is a real fix beyond the card: `Option` is a 2-hp
      `KillableAlien` that local enemy bullets shoot off, so the two peers lost them
      independently and nothing ever corrected it.
    - The count is SHIP state where the rest of the entry is roster state (which outlives a
      death); no ship reports 0/0, and the peer's puppet is gone at the same moment anyway --
      an Option dies with its owner. Clamped at the decode boundary
      (`NetProtocol.HudMaxOptionsPerLayer` 32): the byte is off a stranger's wire and it drives
      real component spawns.
    - Two costs, both taken deliberately over an estimate that can be visibly WRONG and then pop.
      A remote player's pickup options appear up to one HUD interval (~50 ms mean) later than
      they used to; and because a dead owner reports 0/0 while its puppet is still ~100 ms of
      interpolation behind, the orbit blinks out slightly before the ship it belongs to does.
    - **The spawn goes through `ComponentBin.TryAdd`, not `Add`** -- this caller adopts what it
      adds, and the rx drains inside a tick where a `Purge<AlienDrawableGameComponent>` can be
      standing. Adopting a diverted option would satisfy the list count with a component the
      world does not have and the reconcile would never notice; a refusal simply waits for the
      next packet.
  - `FirePower`/`Range` already ride `MsgShipState`; `Blast` and `OneUp` stay unmirrored for the
    reasons in the hardening bullet above and in `HandleClaim`.
  - **`OwnsSlot(slot)` gates the SHIP half and the HUD half stays ungated.** The host also runs
    this path for a CLIENT's claim, so a claim naming a slot we own would re-run a pickup our own
    `CollidesWith` already ran -- a second batch of Options each time. The icon is idempotent.
  - **`GameScene.NetApplyTetherBreak` gained a real default body** (`PlayerShip`'s own
    `connectors` list, which holds ONLY Linker connectors -- `ShipConnector.Setup` does not
    register the scripted TeamChallenge tether with either endpoint). It was an empty virtual
    outside `TeamChallenge`, while `ShipConnector.TakeHit` sends `EvTetherBreak` unconditionally
    -- so once a Linker connector CAN form, a hit only one screen saw would leave the other
    player tethered and pulled toward an anchor already let go of. `TeamChallenge`'s override now
    calls `base` as well as breaking its own scripted tether.
    **It breaks EVERY connector on the ship, not only the ones with a puppet endpoint**: with
    couch players a pair of LOCALLY-owned ships can be connected on this peer while the same pair
    is two puppets on the other, so a puppet-endpoint filter would break that link when we saw
    the hit and not when they did -- a one-directional break. The cost is a known OVER-break
    (`EvTetherBreak` carries no connector identity, being the or-of-either-peer event
    TeamChallenge's single tether was designed around, so one peer breaking one of two live
    connectors breaks both here); fixing that means putting endpoint slots on the wire.
  - **A remote pickup is AUDIBLE (card 06ac5df2, the user's ruling -- reversing card d53431b4,
    which was also the user's ruling).** `ApplyRemotePowerup` plays the `"powerup"` cue inside its
    `!OwnsSlot` branch, so the other player's pickup makes the same noise on your screen a local
    one does, on both settle paths (host `HandleClaim`, client `NetPuppets.OnRemoteDeath`). The
    gate is what keeps the host settling a claim for its OWN slot from doubling the cue its ship
    already played in `CollidesWith`. **The LATE claim is covered too (06ac5df2 follow-up)**: the
    `recentDeaths` record carries the pickup TYPE now (it was a bare `OneUp` bool), so a claim
    landing after the removal flush -- both ships grabbing one powerup inside the RTT window is
    the ordinary route -- runs the same remote-pickup apply through `PayDeadClaim` (HUD icon,
    ship mirror, cue) instead of paying an extra life at most and silently dropping the rest.
    That closes a pre-existing gap the HUD icon and the Linker ship mirror shared; pinned by
    `eaNetPickup` leg 2b. The cue itself is still NOT asserted anywhere -- `SoundManager` has no
    cue counter, the ruling d53431b4 made and this card keeps.
  - **A remote LEVEL-UP now shows the `PowerupEffect` sparkle**, which
    `ScoreVisualiser.NetSetPowerupLevel` deliberately suppressed. `doEffect` is true **only on a
    climb of exactly ONE step**: a multi-step climb is a CATCH-UP (a JIP peer adopting a slot
    already at 4, or the first HUD packet for a slot) and would fire four sparkles in a tick for
    events from before we were watching. Stateless, because a genuine level-up is always one step.
  - **Verify with `eaNetPickup()`** (`Compat/Net/NetPickupTest.cs`, 33 assertions;
    `tools/headless/probes/net_pickup.txt`). **DESTRUCTIVE** like `eaNetResetSpawn` -- it pairs a
    real HOST session onto the live level, adopts a real ship puppet off a scripted client's
    stream and drives real `EvClaim` frames at it -- so run it in a throwaway
    `?level=Level2&invuln` boot. The legs the suite rests on are the option ones, driven over the
    FULL remote sequence (claims AND a REAL `MsgHudState` packet through the production encoder,
    decoder and rx drain -- the hand-written stand-in that packet used to be is gone, precisely
    because it could drift from production with every leg green): measured owner +6 / observer
    +6 over 3 pickups + 3 level-ups, and +3 -- exactly the level-derived half -- with the
    reconcile mutated out. **Leg 6 is card c5228350's own subject**: the same shape with NO claim
    delivered, i.e. what a join-in-progress peer gets, asserting the observer falls behind and
    that ONE packet catches it up per layer; leg 7 pins PR #264's race residual against the
    pre-card arithmetic transcribed beside it, leg 8 the DOWNWARD direction plus the outer orbit
    layer (unreachable otherwise -- the owner would have to be at option level 4).
    The connector leg leads with the NEGATIVE (unarmed puppet -> no connector)
    so the positive cannot read as "the rig always makes one". Mutation-tested seven ways.
    **Re-adding the deleted claim-side Option spawn fails NOTHING** (the reconcile absorbs it
    within one packet) -- the single-source property is a review invariant here, not a probed one.
    **The mute half is NOT covered** -- `SoundManager` has no cue counter and adding one for a
    test would be a production field with no other reader.
- **Per-slot HUD state: a slot's combo and powerup progression belong to its OWNER (card
  1a3ad45a).** Every peer used to simulate BOTH -- a remote ship's shots are re-fired locally
  through the real `FireAt` path, so they are ordinary local `Bullet`s stamped with that slot's
  owner and `Bullet.CollidesWith` sustains its combo. On a client those bullets hit frozen
  puppets interpolated ~100ms behind the host's real entities, so the sims diverge routinely.
  - **The counter diverging is cosmetic; feeding `AddExp` with it was not.** Card `4717d3cf`
    set `powerupactive` for a remote collector, which is exactly the gate on
    `ScoreVisualiser.increasecombo` -- so a peer levelled up powerups for a slot it did not own,
    and `ScoreVisualiser_onLevelUp` then called `PlayerShip.PowerUp` on the PUPPET. For `OneUp`
    that is `Oracle.SetSlowmotion(12f)`: **twelve seconds of global slow motion fired
    unilaterally on one peer**, off an invented combo. `Option` spawned a real extra Option ship;
    `FirePower`/`Range` gave the puppet a weapon its owner did not have; `checkPowerupAchievement`
    could grant `FullPower` off another slot's simulated progress.
  - **`NetSession.OwnsSlot(slot)` is the gate, and it sits on `SustainCombo` -- the whole
    simulation, not just the `AddExp` branch.** Gating only `AddExp` leaves `AddCombo`
    incrementing between the owner's 100ms packets and the 1s `combotimer` zeroing a live combo
    whenever OUR re-fired bullets miss, i.e. the replicated value fighting a local one.
    `NetSetHudState` therefore also PARKS that slot's `combotimer` at the owner's replicated
    remaining time while the owner reports a live combo (v23; it used to refresh it to FULL,
    which kept the readout lit but ran the fade-out up to ~1 s late and out of phase),
    because the readout's alpha is driven by its `TimeLeft`.
    It asks the ROSTER, not a live ship -- a slot's combo and levels outlive its ship (they
    persist across a death and respawn), so a ship-keyed test would flip while the player waits
    to come back. **Offline it is true for every slot**, which is what keeps single-player and
    local co-op byte-identical. The decision is split into a pure `OwnsSlotCore(active, seat)`
    so the test can table-drive `Remote`/`RemoteFriend`/unseated -- offline the predicate is
    unconditionally true, so a live-roster-only test could never reach those cases at all.
  - **`MsgHudState` (0x12, stream lane, ~10 Hz, BIDIRECTIONAL) carries the owner's version**:
    `[type][count]` then `[slot][combo:2][comboLeft:1][activeType][progress][level x 5]
    [optionCount x 2][score:f32]` per owned slot. Protocol **v9**, the option counts **v16**
    (card c5228350 -- the bullet above), `comboLeft` **v23** (card b2828be8 folding a5b1e941:
    the combo timer's remaining fraction, so the observer parks its timer in phase with the
    owner's). **combo is a USHORT and that is load-bearing** -- the host SPENDS the
    adopted figure (`AwardScoreToAll` -> `comboModify`), so a byte would cap a client's real
    400x combo at 255 and underpay it; combos past 255 are expected (1000 precached combo
    strings, an explicit `>= 1000` draw fallback). Levels cover the leading 5 `Powerup.PowerupType` values -- `OneUp`'s level is
    pinned at 3 and never increments, so the wire index IS the enum value and a NEW TYPE MUST GO
    AFTER `OneUp` (or widen `HudLevelCount` and bump the version). Stream lane because it is a
    readout: a dropped packet only means one interval of staleness.
  - Received state applies only to slots we do NOT own (a peer claiming one of ours is ignored,
    not trusted), bounded against `ScoreVisualiser.SlotCount` like `ApplyRemotePowerup`. The
    receiving peer never re-derives its OWN awards from the adopted combo (its score is
    reconciled by `EvScoreSync` + the unsettled ledger) -- but the HOST does spend it, which is
    the point of the side effect below.
    Levels go through the real `PlayerShip.PowerUp(..., doEffect: false)` one step at a time, so
    the puppet's re-fired bullets match its owner's actual loadout. **`OneUp` is unreachable
    there and must stay so**: since card a66e190a the slow motion's EFFECT replicates as
    `EvSlowmo`, but its TRIGGER is still the owner's alone -- a peer must never fire one off a
    slot it does not own.
  - **Side effect, deliberate:** `AwardScoreToAll` (every boss) pays each slot with THAT slot's
    own multiplier, so the host used to compute the client's boss share from a combo the client
    never had. It now uses the real one -- a payout change, and a correction.
  - **Verify with `eaNetCombo.test()`** (`Compat/Net/NetComboTest.cs`), not two windows: the
    failure is a peer levelling a powerup it does not own, minutes into a fight, and its visible
    consequence reads as a hiccup rather than a desync. Section 2 drives the REAL
    `PowerupData.AddExp` over two divergent combo streams and runs the OLD ungated behaviour over
    the identical stream FIRST, asserting it levels the slot and reaches the `OneUp` trigger --
    a green tick means nothing otherwise (the `eaNetScore.test()` rule). Section 1 round-trips
    the wire format against the live `ScoreVisualiser` on the unseated slot 3 and restores it;
    section 3 pins `OwnsSlot`'s offline answer. `eaScore()` gained `own=`/`pu=`/`lv=` per slot,
    and the `[net]` line `hudTx`/`hudRx` (`hudRx` counts ENTRIES, not packets -- a peer with a
    couch partner sends two slots per packet).
  - **GOTCHA -- a two-window co-op run cannot be driven at full rate from this rig.** A
    backgrounded tab throttles to ~1 tick/sec (measured: `txStream` advanced 43 in 40s where
    30Hz would be ~1200), `?fpsuncapped` does NOT defeat it, and two tabs in one window can
    never both be visible. BroadcastChannel does not cross browser profiles either, so two
    separate browsers can only pair via `?rtc` + signaling. Plan net verification around
    one-tab round trips (this test, `eaNetBgTest`) and treat a two-window run as a
    smoke check whose absolute rates are meaningless.
- **PRESENTATION EFFECTS: whose screen does this belong to? (cards 7a8ec0d3 + a66e190a.)** Two
  reports, opposite answers, one question -- and the question is worth asking of any new effect.
  - **A FLOATING SCORE IS THE KILLER'S ALONE.** `ScoreVisualiser.AddScore`'s POSITIONAL overload
    is the one place a "+10" is born, and it now spawns one only for a slot `NetSession.OwnsSlot`
    answers for. FOUR net paths reach it for a slot this peer does not own -- the host locally
    re-fires a remote player's bullets (so their kill runs `AwardScore` here), the host pays an
    `EvClaim` (`HandleClaim` / `PayDeadClaim`), and the client pays the host's `EvDeath` award
    array (`NetPuppets.ApplyAwards`) -- so ONE gate at the choke point rather than four that can
    drift. The score itself is credited unchanged; only the popup is gated. Offline and for couch
    slots `OwnsSlot` is unconditionally true, so single-player and local co-op are byte-identical
    and a partner sharing your screen keeps their own popups. The other two floater kinds needed
    nothing: `CheckPowerup`'s "Power Up!" and the combo pops only run inside `SustainCombo`, which
    card 1a3ad45a already gated on the same predicate.
    - **`AwardScoreToAll` hands its ONE positional figure to the first seated slot WE OWN**, not
      the first seated slot. Otherwise a boss kill shows a joiner nothing at all -- its own payout
      goes out through the non-positional overload while the one floater is spent on a slot whose
      popup is suppressed. Offline the two readings are the same slot.
  - **THE 1UP SLOW MOTION IS THE WORLD'S, and it now crosses the wire: `EvSlowmo` (event 25,
    reliable, `[durationMs:2]`, protocol v15), sent by EITHER peer.** `PlayerShip.PowerUp`'s
    `OneUp` case is `Oracle.SetSlowmotion(12f)`, a whole-sim time scale, and it used to be purely
    local -- so the peer who filled the bar crawled for 12 s while the other ran at full speed.
    On a CLIENT it was worse than one-sided: enemies there are host-driven puppets, so its own
    ship slowed while the enemies it was dodging did not.
    - **The send sits INSIDE `Oracle.SetSlowmotion`** -- the one place a local slow motion begins
      (the 1up bar and the `eaSlowmo()` QA seam both land there), so there is no per-caller
      plumbing. The rx side calls **`Oracle.NetSetSlowmotion`**, which does the identical work
      WITHOUT the send: no echo BY CONSTRUCTION rather than by a latch, and no `fromNet` bool for
      a caller to get wrong. Two peers that each re-announced what they received would slow-motion
      each other for as long as the session lasted -- and since `SetSlowmotion` EXTENDS a running
      window, the symptom would be a world that never speeds up again, not a crash.
    - **WHY THIS IS SAFE WHERE `Juice.AddHitStop` IS REFUSED (card 68f62e92), because the two
      look alike and are not.** A hit-stop is scale ZERO on ONE peer: that peer stops producing
      motion while the wire keeps streaming, and the other peer's puppets are corrected BACKWARD
      (measured 23 px). This is 0.4 on BOTH, and every net clock -- the cadence,
      `NetPuppets.Drive`, the observed velocities -- reads REAL time and is untouched by the game
      time scale, so the wire carries the slowed truth and nothing is corrected backward. It
      REPLACES a 12-second divergence with the one-way-trip skew at each end of the window. So
      the rule is not "no time scaling in a session"; it is **no ASYMMETRIC time scaling**.
    - **Cosmetic-only replication was considered and cannot fix the card at all.** Mirroring the
      bloom preset and the ghost trails without the time scale would leave the non-triggering peer
      smeared while everything moved at full speed -- and on a client, the only thing that can
      slow the ENEMIES is the host slowing its authoritative world.
    - The `?nethitstop=1` reproduction seam is unrelated and untouched: it re-enables `Juice`'s
      freeze, which is still the asymmetric case.
    - **The received duration is CLAMPED at the decode boundary to `NetProtocol.MaxSlowmoMs`
      (12000, what `PlayerShip.PowerUp` can produce), not rejected** -- the field is
      presentation-shaped, so degrading a silly value beats dropping the message, and a bare u16
      is a 65.5 s hold off a stranger's wire (`AllowOnlineJoins` defaults ON). The clamp bounds
      ONE frame and nothing bounds REPETITION; that surface is card `2da92af9`'s, not this one's.
  - **Verify with `eaNetLocalFx()`** (`Compat/Net/NetLocalFxTest.cs`, 24 assertions;
    `tools/headless/probes/net_local_fx.txt`), MENU-runnable and leave-no-trace. A screenshot
    cannot see either half: a floater moves no score, no metric and no component, so its absence
    is exactly as invisible as its presence, and the effects belong to the peer whose console you
    are not reading. **The two legs that carry the cards** are "no floater appeared AND the score
    still moved" (a gate that suppressed the whole payout, or a claim that never arrived, would
    pass the absence alone) and the no-echo assertion. Mutation-tested four ways, each failing one
    leg -- including the ms->seconds conversion, which needs a leg of its own because `Slowmotion`
    is a flat 0.4 whatever the window. **Deliberately absent from `net_selftests.txt`** -- it has its own probe, which carries
    the write-up and the mutation matrix, the `eaNetDeathFx` precedent.
  - **NOT VERIFIED ON TWO REAL SCREENS.** Whether 12 s of shared bullet-time from one player's
    1up FEELS right is a playtest question this rig cannot answer.
- **THE RESPAWN INDICATOR IS THE WORLD'S TOO: `EvRespawn` (event 26, reliable,
  `[slot:1][posX:f32][posY:f32][durationMs:u16][rewardLevel:1]`, 16 B, protocol v17 / v26,
  EITHER PEER) -- card 37f3a663, `rewardLevel` card ed32efe1.** A dead player's respawn clock existed only on their own screen: the other peer
  watched the ship explode and then had ten seconds of nothing, with no idea their buddy was coming
  back or where. That is structural rather than an oversight -- `NetSession.ExplodePuppet` takes a
  puppet out WITHOUT `Die()`, precisely so it does not raise a local summon for a ship it does not
  own, so nothing on the far side ever knew a respawn had begun.
  - **The receiving copy is COSMETIC and the same TYPE** (`PlayerShipSummon.SetupRemote`): same
    ring, same pop, same reward blast, but it never spawns a `PlayerShip`. The peer's real ship
    still arrives through the ordinary `remoteAlive` edge (`SpawnPuppet`), which stays the only way
    a puppet is born -- so a lost or ignored frame costs the indicator, never the ship. Being the
    same type is what makes every existing `Purge<PlayerShipSummon>` (`LoseLife` / `NetApplyReset`
    / `Terminate`) clean it up for free.
  - **The REWARD BLAST does not ride `EvBlast`, and that is the design decision worth keeping.**
    Reusing it looks free -- a bomb already replicates -- but its rx handler resolves
    `oracle.GetPlayerShip(slot)` and returns if null, and at a respawn the far peer's puppet may
    not have been born yet (a reliable event against a ~30 Hz alive edge), so the bomb would drop
    silently; it also attaches the blast to the puppet's LAGGING interpolated position. Each side's
    own summon fires its own `Blast` at its own pop instead, from the announced position -- the
    `EvIntroVolley` idiom. Damage stays fair because that is already how an ordinary co-op bomb
    works: both peers spawn a real `Blast`, the host's is authoritative and the client's kills go
    through the generous claims.
  - **...WHICH IS EXACTLY WHY THE REWARD LEVEL HAD TO GO ON THE WIRE (card ed32efe1, v26).** The
    blast is not replicated, so while its level was a CONSTANT the two peers' copies matched by
    construction and there was nothing to send. Once it became the owner's "2" (`Linker`) powerup
    level, an observer re-deriving it from its own `Score` could disagree: that slot's levels reach
    this peer over the ~10 Hz `MsgHudState`, so a peer who takes their fourth "2" and dies inside
    the next packet's window latches the stale 3, and a JOIN-IN-PROGRESS peer that receives this
    event before its first HUD packet latches 0. Not cosmetic -- `Blast.Setup` makes the lifetime
    `1000ms * (level+1)` and the blast KILLS, so wherever the observer is the host its copy is
    authoritative for what dies. One byte restores the by-construction identity, and the sender
    reads it off the SUMMON (`PlayerShipSummon.RewardBlastLevel`) rather than re-reading `Score`,
    so the announcement cannot describe a different bomb from the one that peer will drop.
    Clamped 0..4 at the decode boundary rather than refused, the `ClampKillerSlot` ruling: the
    frame also carries the announcement itself, and dropping the indicator and its position over
    one bad byte is the worse trade. Pinned by `NetRespawnTest` sections 1 and 2 (whose section-2
    leg asserts this peer's own view of that slot's "2" is 0, so a re-derived build reads 0 and
    fails) and by `NetWireTest`'s round-trip + clamp legs.
  - **NOT an `EvFx`.** That lane is HOST-ONLY and keyed on a `netId`; this is neither -- either
    peer's ship can die, and a summon is not a replicated entity at all, so it needs its own
    position. `EvSlowmo` is the shape it follows.
  - **The DURATION is sent rather than re-derived**, because it is not a function of anything the
    receiver knows: it falls out of the dying player's own `respawntimebonus` (a powerup
    progression) as well as the difficulty.
  - **Slot-tagged, and a frame naming a slot WE own is REFUSED** (`OwnsSlot`) -- the `EvBlast`
    precedent and for the same reason: a slot disagreement would otherwise park a phantom clock
    over a player who is alive and flying, and drop a free bomb into our world when it popped. Sent
    for any LOCALLY OWNED ship, so a couch player's or an AI friend's respawn replicates too.
  - **Verify with `eaNetRespawn()`** (`Compat/Net/NetRespawnTest.cs`, 25 assertions; a leg of
    `net_selftests.txt`). Menu-runnable and leave-no-trace. Section 1 drives the REAL death path --
    two planted ships, one `Asplode()`d -- so what is asserted is the chain `OnDeath` ->
    `ShouldSummon` -> `OnLocalRespawnSummon` rather than a hand-called sender, with a PUPPET death
    beside it as the negative. **The load-bearing leg is section 3's "and spawning NO
    PlayerShip"**: a cosmetic summon that spawned one would give the peer's player a second body on
    this screen for the rest of the match, and every other assertion would still be green.
    Mutation-tested three ways (dropping the `OwnsSlot` refusal, not marking the cosmetic mode,
    and dropping the pending-second term from `RemainingMs`), each failing disjoint legs.
    **Section 1a is the odd one out and is not about the wire at all**: it ticks the REAL summon
    200 times and requires the clock to run only DOWN. `base.Update` ticks the timers after this
    class tests `Finished`, so for one frame a second the ring un-filled by a tenth -- and no
    screenshot rig can see it (`?respawnphase=` parks the fill, `eval RespawnState` samples between
    ticks). It lives here because this is the only suite that already drives a real summon. The wire layout is `eaNetWire.test` section 5's, round-tripped by
    VALUE since it has a real `Try*` decoder.
  - The SINGLE-PLAYER half of the same card (a wipe raises no summon at all) is offline and lives
    in `web/EvilAliensWeb/CLAUDE.md`.

## Roster slots, seating & the remote ship

- **Roster slots are HOST-ALLOCATED and identity-mapped (card 4d904410 -- local co-op AND
  online co-op at once).** The oracle slot IS the wire slot on both peers; there is no
  host-relative translation anywhere (the old `TranslateSlot` 0<->1 mirror and the
  `ApplyJoinHues` compensating hue swap are both GONE -- per-slot hues now agree by
  construction). The host's own primary is always slot 0; the joiner's primary slot rides in
  `MsgWelcome` (v5); a couch player joining the CLIENT asks with `EvJoinRequest` and the host
  answers `EvSlotGrant(slot)`, reserving that seat as `RemoteFriend` the moment it grants so
  its own `AddPlayer(AI)` / a later grant can't reuse it. Host-side couch joins allocate
  locally. `GameScene.AddPlayer` routes to `NetSession.TrySeatLocalJoin` while a session is up;
  offline behaviour is byte-identical.
  - **The primary grant is a NEGOTIATION, not a guess (card c0229c57, protocol v8).** The host
    allocates out of its OWN free slots and cannot see the joiner's, so it used to grant a seat
    the joiner might already hold -- which desynced the pairing silently and permanently (JIP
    pass trap 3 has the full story). The client's hello now carries a `blockedSlots` mask of the
    slots it cannot seat its primary in, and `NetSession.FirstMutuallyFreeSlot(hostOccupied,
    peerBlocked)` picks one free on both. Three rules hold it together:
    - **The mask is only non-zero while a `GameScene` is up.** At the menu -- where BOTH the
      menu-lobby and the join-in-progress joiner hello from -- the roster is leftover
      bookkeeping from the last level or attract demo, which the launch path's `ResetPlayers()`
      wipes before seating us. Reporting it would refuse seats for no reason.
    - **`peerPrimarySlot` is assigned ONLY on a settled adoption.** `Update`'s retry condition
      is `!PeerUp || peerPrimarySlot == SlotNone`, so setting it on a FAILED adopt silences the
      1 Hz hello on both peers and the pairing can never recover. This is the bug the card was
      about; treat it as an invariant of `AdoptGrantedPrimarySlot`, not a fact about one branch.
    - **It terminates.** Each round either seats the joiner or adds a slot to the mask, and the
      host never re-offers a blocked seat; when nothing works on both sides it sends
      `RejectFull` ("Game full"). The host's own game SURVIVES that -- `Stop()` does not exit a
      level and `NetListing.ComputeEligible` needs `!NetSession.Active`, so a listed host drops
      back to single-player and re-lists. Verify with `eaSlotTest()`.
  - **The roster is cleared on the way OUT of a scene as well as in (card ee96ea61).**
    `GameScene.Terminate` ends with `oracle.ResetPlayers()`. Before that only the launch paths
    reset it, so between a scene ending and the next launch the roster held whatever the last
    level or attract demo left behind -- and that window is where BOTH menu-lobby handshakes and
    the join-in-progress joiner hello run. The client side was already guarded
    (`LocalBlockedSlots` returns 0 with no `GameScene`), but **`HostOccupiedSlots` reads the
    roster raw**, so an attract demo could make a host answer a good joiner with `RejectFull`
    ("Game full") with no real players aboard, or grant them slot 2 instead of 1 for the whole
    session. Safe because `PlayerInfo.Reset()` only clears `isPlaying` -- score lives in
    `ScoreVisualiser`, unlocks in `Achievements`, and the hue is deliberately left alone. It is
    LAST in `Terminate`: `OnFinished` fires mid-method and has already queued the next scene
    (credits/menu), neither of which seats anyone.
    **Do not add a second menu-guard to `HostOccupiedSlots` instead** -- the reset is the root
    cause and covers `AllocateSeat`/`HandleJoinRequest` too.
  - **Read the OFFLINE roster with `eaOracleRoster()`** (`eval OracleRoster` under `eahl`).
    `eaNetRoster()` early-returns without a net session, so it cannot see the menu roster at
    all -- which is exactly where a stale seat does its damage. Needs no session, level or
    gamepad. (`eaScore()` also shows seated-ness; what this adds is the DEVICE per seat, which
    is what tells an attract demo's leftover AI seats from a real player.)
    **The repro, headless and flag-free:** `eahl --repl --flags "?menu"`, `step 1500 nodraw` to
    idle past the 20s attract timeout, `eval Press esc 2` back to the menu, `eval OracleRoster`.
    Pre-fix the demo's seats are still there afterwards (`players=1 seated=0:AI`, or more --
    slot 0 always, plus 3 on a 20% roll and 1 on a further 40%); post-fix `players=0`.
    Do NOT read `info`'s `scene=` to tell whether the demo ran -- it reports the booted level and
    stays `Level2`/`menu` throughout; the roster dump is the signal. `eaSlotTest()` covers what that stale
    roster then COSTS at the allocator, but it seats its scratch roster by hand and never
    reaches `Terminate` -- so it cannot substitute for this run.
  - **Every seat-taking path must use `NetSession.LocalPrimarySlot`**, not "the first free
    slot": `Game1.MenuFinished`, `Game1.LaunchLevelDirect` (the `?level=` boot -- a `?net=join`
    tab pairs WHILE it boots, so the grant can land before the seat is taken) and
    `TeamChallenge.Initialize`. Getting this wrong is silent: the ship sits in a slot the wire
    doesn't know about and simply never replicates.
  - **Sparse rosters are legal now** -- a hole is normal (a granted seat not yet filled, a
    friend puppet that died). Anything walking the roster asks `Oracle.IsSeated(slot)` over
    `0..MaxPlayers-1` instead of assuming `0..Players-1`: `ScoreVisualiser`'s score-vs-
    "Press Start" panels and `GameScene.SpawnAllPlayers` (which spreads spawns by the player's
    ORDINAL among seated slots, so a dense offline roster spawns exactly where it always did).
    `Oracle.AddPlayer` returns the slot it seated; `GameScene.SpawnPlayer` takes it explicitly
    (`oracle.Players - 1` only agreed while dense).
  - The extra-ship stream is BIDIRECTIONAL and carries every locally-owned non-primary ship
    (AI friends *and* couch players; since v23 it rides `MsgShipState` with the primary flag
    clear) -- `ControlDevice.RemoteFriend` means "network-driven
    extra ship", whoever owns it. `EvBlast` gained a slot byte (a couch player's bomb used to
    detonate on the peer's PRIMARY puppet) and `EvScoreSync` widened from 2 slots to 4.
  - `DriveFriendShip` ADOPTS a ship the scene spawned into its slot (`SpawnAllPlayers` respawns
    every seated slot after a reset, and since card b4d0ba1d `RemoteFriend` is the only puppet
    slot it still fills -- see the death/reset pair) -- without it the re-spawned puppet
    matched no channel and froze on its spawn pose. The primary remote path always adopted;
    this one didn't, which only stopped being a corner case once couch players (who hit resets
    constantly) could exist.
  - **Verify with `?netlocal=<1-3>`**: queues that many synthetic couch joins on this peer a few
    seconds after the session goes live. A real couch join is a gamepad Start press, which the
    rig cannot produce -- no physical pads, and seating a Pad device with none connected trips
    GameScene's disconnected-gamepad force-pause every tick -- so it seats `Generic` (a real
    human device with no connected-check) then `AI`. The `[net]` line gained
    `roster=<slot:device[*]> pri=<local>/<peer> ships=<owner:device>`; **the two consoles must
    print mirror-image rosters** (`*` = ours). Recipe:
    `?level=Level2&net=host&aiplayer&invuln&netlocal=1&room=<r>` + the same with `net=join`
    (Level2, not Level1 -- Level1's intro hands the ship spawn to a script beat, so the host has
    no ship for the first minute). Expect
    `roster=0:Keyboard*,1:Remote,2:Generic*,3:RemoteFriend` on the host and
    `0:Remote,1:Keyboard*,2:RemoteFriend,3:Generic*` on the join side.
  - **A full RESET with couch players aboard is reached with `eaKillShips()` on both tabs**
    (card af0eb00a) -- and needs no new tooling, because the "all four ships dead at once"
    framing is weaker than it looks: `Oracle.AllShipsDead` is `playerShips.Count == 0` and
    NOTHING respawns until it fires, so **dead ships stay dead** and the two console calls need
    not land in the same frame. After the second tab fires, each peer's puppets die on their own
    existing paths (the primary remote on the `alive=false` edge, the couch puppet on the 500ms
    `FriendTimeoutMs`) and `AllShipsDead` then trips `LoseLife`. Read the result with
    **`eaNetRoster()`** on both peers either side of the kill: the 5s `[net]` cadence can
    straddle the whole ~2.7s reset, so a sampled before/after can show nothing. The gate is
    `resets` +1 on both, `roster=` (the seat map) IDENTICAL across the reset and still mirror-
    image, and `ships=` back to one entry per seat -- a **missing** owner is a puppet that never
    re-adopted (frozen on its spawn pose), a **duplicate** owner is a double spawn.
    **`ships=` alone cannot tell "adopted" from "frozen"** -- a never-adopted puppet still shows
    as a ship in its seat. That is what the dump's `at=<owner>:<device>@x,y` is for: sample it
    twice a second or so apart and check (a) the slot MOVES and (b) the two peers agree per slot
    within interpolation lag. Observed clean: slot 3 read `592,305|592,305` -> `521,283|521,283`
    -> `389,362|389,362` host|join across the field after a reset. Caveat: right after the reset
    the purge leaves no enemies, so the `?aiplayer` AI parks every ship on the spawn ladder
    (`y ~ 120/240/360/480`) for a few seconds -- that is the AI having no target, NOT a frozen
    puppet. Wait for the spawners to replay before reading motion.
  - **GOTCHA -- an OCCLUDED window freezes the whole run, and it fails silently.** Chrome marks a
    fully covered window `visibilityState:'hidden'` (even with `document.hasFocus()` true) and
    stops rAF entirely, so a peer parked behind another window simply stops ticking; the peers
    then time each other out and every metric is garbage. Two side-by-side windows is the
    documented answer, but when the surrounding tooling covers them (an automated run driving
    Chrome from another app) **add `?fpsuncapped` to BOTH peers** -- it drives the loop off a
    `MessageChannel` instead of rAF, so both keep ticking while occluded. Verified: an occluded
    `?fpsuncapped` pair ran a full reset cycle with `drop=0 sgap=0 ordViol=0 seqGap=0`. It is a
    LOOP flag, so it needs neither the HUD nor `?nofps`. Cost: the client runs far above vsync,
    which inflates `pupPops`/`dup`/`snapUnk` around id churn -- read those as not comparable to
    a normal-rate run, while roster/adopt/`resets` assertions stay valid.
  - **`?netdropgrant` (client) is the only trigger for `ExpireUnclaimedGrants`, and it is
    ONE-SHOT (card ee96ea61).** The host holds
    a granted couch seat as `RemoteFriend` until the peer's first stream for it lands; a client
    that silently fails to take the grant would otherwise leak that seat for the session (and the
    game stops being re-listable). `?netlocal` always TAKES its grant, so the expiry path had no
    trigger at all -- this flag drops the **first** `EvSlotGrant` of a session after clearing
    `joinRequestPending`, leaving this side exactly as a genuine failed take does, and lets every
    later grant through. Expect the host to log
    `granted peer couch join slot=N` then `released unclaimed couch grant slot=N` ~10s later
    (`GrantClaimTimeoutMs`), and the seat to leave `roster=` rather than leak.
    - **It dropped EVERY grant until card ee96ea61**, so a run could only show the DROP half and
      "the reclaimed seat is re-usable" went unverified. `?netlocal=2` now covers both halves in
      one run. Note the second join lands ~3s after the first while `GrantClaimTimeoutMs` is 10s,
      so it is handed a DIFFERENT free seat -- proving recovery, not reuse. For reuse proper,
      wait out the release and call `eaNetCouchJoin()`, or just read `eaSlotTest()`, which drives
      the whole reserve -> hold -> expire -> reallocate cycle as data.
    - **The latch is per SESSION and the clearing is the load-bearing half** -- a flag outliving
      the thing that set it is the exact bug class this seam exists to hunt, so it lives in
      `NetSession.ResetPerSessionState` beside `joinRequestPending`, and `eaSlotTest()` asserts a
      teardown clears it (driving `ResetPerSessionState` directly, since `Stop()` early-returns
      with nothing Active and would make the leg vacuous -- the `eaKickTest()` precedent).
  - **`RejectFull` needs `eaNetCouchJoin()`, NOT `?netlocal`.** Reaching it means the host roster
    is already full when a joiner says hello, which means couch players seated BEFORE pairing --
    and `TickLocalJoinSim` is deliberately gated behind `PeerUp` (pre-pairing, `AllocateSeat`
    cannot yet know which seat the joiner's primary will need, the very hazard its comment warns
    about). So `?netlocal=3` can never fill the roster in time: the joiner is the peer that
    ungates it, and it already holds a seat by then. `eaNetCouchJoin()` makes the same
    `TrySeatLocalJoin` call a real gamepad Start makes, which is NOT PeerUp-gated -- call it 3x
    on a `?net=host` boot to reach `roster=0:Keyboard*,1:Generic*,2:AI*,3:AI* peer=down`, then
    pair a `?net=join`. Host logs `no free roster slot for the joiner -- rejecting` +
    `session stop (pairing rejected)`; the joiner logs `peer rejected the pairing (reason=4)`
    (4 = `RejectFull`) + `session stop (rejected by peer)` -- an explicit reject rather than a
    bare channel close is what proves the `RejectGraceMs` deferral let the reliable frame out.
- **THE DEATH/RESET ARTIFACT PAIR (cards 68f62e92 + b4d0ba1d), both pinned by
  `tools/headless/probes/net_reset_spawn.txt` legs 0a / 5 / 6.**
  - **NO HIT-STOP RUNS INSIDE A SESSION (card 68f62e92).** `Juice.AddHitStop` early-returns while
    `NetSession.Active` -- the death stop, the `?hitstop=1` kill/boss stops and `eaHitstop()`
    alike. **This is a desync fix, not a feel decision.** `Game1.UpdateScaled` folds
    `Juice.TimeScale` into the gameTime it hands `UpdateInner`, so a freeze halts that peer's
    WHOLE world (every host-authoritative enemy included) while `NetSession.Update` sits OUTSIDE
    that scaled path and keeps streaming snapshots of the frozen positions on the real clock. The
    other peer's puppets keep dead-reckoning forward -- which they must, see the real-time driver
    rule above -- so the corrections that follow walk every replicated enemy BACKWARD at once,
    over a background that never stopped scrolling. That was "when P1 dies, the whole game rewinds
    a bit". Measured by `python tools/sim/net_puppet_drive_sim.py --hoststall`: **23 px of
    backward glide** at N=64 and a typical 0.15 px/ms enemy, 45 px for a fast diver, 0 hard pops
    (a glide, not a teleport), against a stall=0 control that never steps backward at all. It
    saturates in stall LENGTH and scales with POPULATION -- a small world is corrected several
    times inside a 180 ms freeze.
    - **Suppressed for BOTH roles, not just the host.** A client freeze stalls its own ship stream
      and the host's `ShipStateBuffer` pays the same price.
    - **Replicating the halt instead was weighed and lost.** The peer only learns of the death
      after RTT/2 + a stream interval, so the two halts are offset by ~30-80 ms and a residual
      divergence of exactly that size survives; it would also mean freezing `NetPuppets.Drive`,
      reverting the invariant that exists to stop `pupPops` bursts; and it makes P2's world stutter
      for a death P2 did not have.
    - **Shake is deliberately NOT suppressed** -- it is applied at the present blit and touches no
      gameplay time, so a co-op death still reads as an impact.
    - **`?nethitstop=1` restores the pre-card behaviour and is IN `DebugFlags.Active`**, unlike
      `?hitstop`. Since this card `?hitstop` cannot degrade a session at all (`AddHitStop` refuses
      regardless), so this is the one flag whose entire purpose is reintroducing a net-desync bug
      and it must never reach a public lobby or a listed game. Every legitimate use is a dev
      `?net=` boot, which is anything-goes.
      **It has its OWN probe, `tools/headless/probes/net_hitstop_flag.txt`, and the reason
      generalises to any bug-reproduction seam**: `net_reset_spawn.txt` runs with the flag OFF, so
      it only ever exercises the SUPPRESSING side -- `DebugFlags.NetHitstop` has exactly one
      reader, and dropping it would leave the flag silently inert with every existing assertion
      still green (mutation-tested: that revert fails the new probe and NOT the old one). The new
      probe re-runs the SAME suite with the flag on and requires the two hit-stop assertions to
      FLIP, bounded by the `61 passed, 2 failed` tally so a flag that broke something else is
      caught too. A reproduction seam that has quietly stopped reproducing is worse than none --
      the next person concludes the bug is gone.
  - **THE PEER'S DEATH FX FIRES ON THE ALIVE EDGE, NOT THE LEVEL (card b4d0ba1d).** Reported as:
    P1 and P2 both die, the level restarts, and on P1's screen both ships fly in -- then P2
    explodes instantly again and flies in again. `ManagePuppet` tested `!remoteAlive && puppet !=
    null` as a LEVEL, and `SpawnAllPlayers` respawns every SEATED slot while the peer's seat is
    deliberately reserved across a death -- so ~1.3 s into the reset a ship arrived in the Remote
    seat while the peer, still running its OWN reset, honestly reported alive=false, and the
    session played the full explosion + `expl2` on a death that never happened. Two halves:
    - `SpawnAllPlayers` SKIPS a slot seated to `ControlDevice.Remote` while a session is active --
      the net layer owns that ship's whole lifecycle (`SpawnPuppet` / `ExplodePuppet`), so it
      appears when the peer's own ship does. **`RemoteFriend` is deliberately NOT skipped**:
      `SpawnFriend` adopts a scene-spawned couch ship by design and its death is stream-timeout
      driven, so the friend path never had this bug.
    - `ExplodePuppet` now needs `puppetSeenAlive` -- the peer must have reported alive=true while
      we held THIS puppet. Set in `SpawnPuppet` (which is already gated on it) rather than only in
      `ManagePuppet`'s per-tick refresh, or a peer dying in the very next tick would be released
      quietly. A puppet adopted while the peer is dead is released QUIETLY instead
      (`ReleasePuppetQuietly`): the peer is dead, so its ship does not belong in our world, but
      nothing died either. Defence in depth for the skip above.
    - `NetMetrics.RemoteShipExplosions` is the observable the probe reads -- the FX leaves no
      other headless trace (two `Explosion`s and a cue into a live world). Counts `ExplodePuppet`
      only; `ExplodeFriend` is a different lifecycle and is not folded in. Not on the `[net]`
      line: it is a per-death event, not a health rate.
- **A RESPAWN STARTS THE PUPPET FROM ITS OWN SAMPLES -- the ship buffer is CLEARED on the
  dead->alive rising edge (card df72b051).** While a peer's ship is dead, `SendShipState` keeps
  streaming as the heartbeat with `alive=false` and `pos = lastTxPos` -- the position the ship
  DIED at, repeated every send interval for the whole death -- and every one of those samples
  landed in the interpolation buffer. On the respawn the render clock (~`InterpDelayMs` behind
  the newest sample) read the dead-period samples FIRST, so the puppet materialised at the old
  death spot and lerped fast across the screen to the real spawn point: "the other player appears
  on the wrong position, then gets sync'd". A both-players-die reset makes the gap seconds long
  and the two points arbitrarily far apart. `HandleShipFrame` now clears the buffer (+ render
  clock + the pop-metric baseline) on the rising edge, BEFORE adding the first alive sample.
  - **Skipping the dead `Add`s instead is NOT enough** -- `ShipStateBuffer.Add`'s trim always
    keeps the last two samples, so the bracketing pair straddling the death gap survives whatever
    the gap length and the bridge remains. The edge clear is the whole fix.
  - **Both the edge and the `remoteAlive` latch are gated on the sample being IN-ORDER** (the
    same `T > NewestMs` test `buffer.Add` applies). The stream lane is unordered, so a stale dead
    heartbeat delivered after the respawn's first alive packets would otherwise read as a fake
    falling edge -- exploding the healthy puppet -- and the next alive packet's fake rising edge
    would wipe the fresh buffer. Pre-card the raw latch merely flickered a bool; with an edge
    CLEAR hanging off it, the gate is load-bearing.
  - **The friend/couch channel has no alive edge, so its half of this card is the TIMEOUT LADDER
    (card 14c5943e), and fixing it took TWO pieces.** A dead extra ship simply stops being
    streamed; the 500 ms `FriendTimeoutMs` destroys the channel -- buffer included -- so a normal
    respawn always streams into a fresh one and can never bridge. The card's stall/pause premise
    turned out to be UNREACHABLE, but only because the stall protection was broken: `PeerStalled`
    arms at 1200 ms of TOTAL silence while every channel's own 500 ms boundary fires first, so
    the protective 8 s arm was structurally dead and **a 0.5-1.2 s wifi hiccup exploded every
    couch/AI-friend puppet, against `TickFriends`' own stated intent**. The two pieces:
    - **The LINK-QUIET arm**: total stream silence past the channel's own 500 ms threshold is a
      hiccup, not a ship death (a death stops ONE slot while the primary heartbeat keeps
      flowing), so it takes the stalled 8 s timeout. Couch puppets now ride a hiccup out.
    - **The RESUME-GAP CLEAR** (`HandleFriendState`): a sample arriving after a gap the channel
      would normally have died of clears the buffer + render clock, so the puppet starts from
      its own samples. This is what keeps the protection above from re-opening the bridge for a
      couch ship that died AND respawned inside a protected window -- and for a live ship it
      just turns the post-hiccup catch-up lerp into a clean snap.
  - Receiver-side only, both roles, no wire change, offline byte-identical. Verify with
    **`eaNetRespawnPos()`** / `eval NetRespawnPos` (`Compat/Net/NetRespawnPosTest.cs`, 23
    assertions) -- **DESTRUCTIVE** (it seats and explodes real Remote + RemoteFriend puppets),
    so run it in a throwaway `?level=Level2&invuln` boot; committed as
    `tools/headless/probes/net_respawn_pos.txt`. Its section 1 models the bridge on a scratch
    buffer through the real codec as the negative control; mutation-tested four ways failing
    disjoint legs (clear disabled -> the two distance legs, puppet driven straight back to A;
    in-order gate dropped -> the reorder leg, fake explosion; link-quiet arm dropped -> the
    hiccup leg, puppet dead at 500 ms of total silence; resume-gap clear dropped -> the resume
    leg, at the predicted 76 px bridge offset). `NetResetSpawnTest` leg 3b changed shape with
    this: a friend timeout now needs the link otherwise ALIVE, so its clock advance is stepped
    with primary heartbeats -- the real shape of a couch-ship death.
- **Remote ship:** `ControlDevice.Remote` (APPEND-ONLY enum position). Joins via
  `oracle.AddPlayer(Remote)` on the first alive stream. (It used to be spawned by the GameScene's
  own SpawnAllPlayers reset flow as well, with NetSession adopting either -- that is what card
  b4d0ba1d removed; see the death/reset pair above.) `PlayerShip.Update` case
  Remote -> `NetSession.DriveRemoteShip`: position sampled from `ShipStateBuffer`
  ~100 ms behind the newest sample (velocity-extrapolated max 250 ms on underrun), speed
  zeroed; shots respawned locally through the ship's own shot construction, paced by the
  replicated cumulative shot COUNT (next bullet); bombs arrive as EvBlast -> `NetDoBlast` (no local bomb-count gate). Remote ships
  take NO local damage (owner decides its own hits; death arrives as the alive-flag edge ->
  local explosion FX, slot stays reserved for respawn) and CANNOT take powerups locally --
  the owning peer collects on its own screen and the pickup arrives as a claim. Hues need no
  fixing up since card 4d904410: slots are host-allocated and identity-mapped, so a slot's
  colour is the same on both screens by construction and the old join-side hue swap is gone.
  (Caveat: `MenuScene.changeColor` lets a player recolour a slot and `PlayerInfo.Reset` doesn't
  restore it, so "host white / joiner purple" holds for DEFAULT colours; nothing normalises the
  two peers' hue tables.) The puppet's render clock advances on REAL time (never turbo/slowmo/
  hit-stop-scaled game time) -- a local hit-stop must not drag the interpolation point.
  - **THE WIRE CARRIES A CUMULATIVE SHOT COUNT, NOT A FIRE INTENT (card a45b78f6), and the
    history is worth 30 seconds because the same trap is one design away in any state stream.**
    `MsgShipState` carries a wrapping u8 of the shots the owner's ship has actually SPAWNED,
    stamped inside `PlayerShip.FireAt`'s cadence gate beside the `Bullet` it counts; the receiver
    (`NetApplyRemoteState`) takes the wrapped DELTA against the last count it applied and spends it
    through the ship's own `SpawnShot` -- **with no second cadence gate**, because the owner's
    counter is already the pacing. `NetSession.Friends.cs` streams the same field for couch players
    and AI friends. It is the delta that means anything; the absolute value belongs to one ship and
    wraps every 256 shots.
    - **WHAT IT REPLACED.** `firing` used to be a LEVEL sampled at packet rate, and
      `DriveRemoteShip` read `buffer.Newest.Firing` EVERY tick -- so **N packets marked firing=true
      = N SEND INTERVALS of firing=true in front of the re-fire gate over there**, and that gate
      was `1000/shotsPerSec`, set from the SAME packet. The peer therefore spawned
      `1 + floor(window / period)` bullets for one tap: a flat 150 ms hold against a 125 ms default
      period is **exactly two bullets per tap**, three at the maxed 18/s. They are real bullets in
      the peer's world and damage what they hit, which is why the card also reported "P1 can kill
      an enemy on P2's screen that is alive on P1's" -- one symptom, not two bugs, with the
      generous-claim design working as intended underneath. Card a5c2a39b bounded the sender's hold
      at `P/2` (floored at one send interval, capped at 150), which held -- and left two residuals
      that could not be fixed inside a level at all.
    - **BOTH RESIDUALS ARE GONE, and they went for ONE reason: a cumulative count says what
      HAPPENED, where a level says what is happening NOW.** (a) At the top fire rates the hold was
      one packet wide, so a stream-lane DROP silently lost that bullet on the peer; a count is
      carried in full by the next packet, so loss, reorder and lateness all cost nothing. (b) The
      intent was stamped BEFORE the owner's own gate, so two taps inside one cadence period were
      one bullet for the owner and TWO for the peer; the increment now happens where the bullet
      does, so it is one of each on both screens.
    - **THE COUNT ON THE WIRE BELONGS TO THE SLOT, NOT TO THE SHIP -- `NetSession.AdvanceTxShots`
      is what keeps that true, and it is not decoration.** `PlayerShip.NetShotCount` restarts at 0
      with every ship (it is POOLED, so it must), while the receiver holds one baseline for as long
      as it holds a puppet. A ship that died at 252 and respawned at 0 is a wrapped delta of FOUR
      -- inside the catch-up bound, so the peer would spawn four bullets nobody fired. The sender
      therefore advances its own per-slot counter by the SHIP's delta and takes no delta at all
      across a ship swap (reference identity), which makes what goes on the wire monotone per slot
      however often the ship behind it is replaced. The primary keeps `lastTxShip`/`lastTxShotCount`
      in `NetSession`; the friend stream keeps one `FriendTxShots` per slot it sends. **The
      RECEIVE-side `FriendChannel` is not the same thing** -- a peer both sends and receives friend
      states, and the two halves are about different slots.
    - **THE RECEIVER SPENDS AT MOST ONE OWED SHOT PER TICK.** A delta that arrives bunched (after
      loss, or a late packet) drains over the next few ticks instead of stacking bullets on one
      point. It is not a rate limit -- nothing is ever dropped.
    - **A delta past `NetMaxCatchUpShots` (6) is a RESYNC, not catch-up: the count is adopted and
      nothing is fired.** Six shots is ~330 ms of continuous loss even at 18/s, past the point
      where exactness means anything. **It is NOT the respawn guard** -- that is the per-slot tx
      counter above, precisely because a respawn can land INSIDE this bound. A resync also drops
      whatever the puppet still owed, since a discontinuous counter makes the backlog in front of
      it meaningless.
    - **`ShipFlagFiring` and `ShipSample.Firing` are DELETED**, and `NetSession` holds no
      sender-side timing on this path at all. That is what let the suite grow a real SENDER leg:
      the old stamp read `Environment.TickCount64`, so driving it end to end would have needed a
      clock seam on `FireAt` whose only reader was the test -- the call card d53431b4 declined for
      the same reason.
    - **THE PER-SHOT ROLLS RIDE THE SAME STREAM AS ROLL RINGS (card 950bb70a, protocol v21).**
      `PlayerShip.SpawnShot` decides per bullet whether it asplodes (a mini `Blast` on death /
      per bounce; FirePower levels set 15/30/60/75%) and whether it bounces+splits (Range
      levels), by rolling the shared unseeded RNG -- and the puppet path re-runs that same
      construction, so each peer used to roll its OWN dice: the RATE matched (the levels
      replicate via `MsgHudState`) but WHICH bullets popped a mini-blast was an independent
      coin flip per screen, which is exactly "are mini-explosions synced properly? Seems
      random". `MsgShipState`/`MsgFriendState` now carry two 8-bit rings beside the count
      (bit i = the roll of shot `ShotCount-i`, shifted in `SpawnShot` beside the rolls they
      record, reset with `NetShotCount` per life -- a pooled ship must not hand its successor's
      shots the previous life's bits); the receiver spends an owed shot with the owner's
      outcome through `SpawnShotForced`, its distance back from the packet's newest shot being
      exactly the backlog still in front of it. Eight bits cover every owed shot by
      construction (`NetMaxCatchUpShots` is 6). The quiet packets between shots repeat the same
      count and ring, so still-owed shots keep their bits under loss. Both `Next(100)` draws in
      `SpawnShot` stay unconditional and ordered, so the offline RNG stream is byte-identical.
      RESIDUALS, stated: the observer's mini lands where ITS re-fired bullet's own collision
      does (~100 ms interpolated world -- same bullet, near-same place, not pixel-identical);
      post-first-bounce trajectories still diverge (the bounce angle re-roll and clone split
      angles stay local); and `asplodingbulletssize`/`bounceamount`/`bulletsSplit` derive from
      the replicated levels, worst case one HUD packet stale. Verified by `eaNetFire()` legs
      2 (sender: every tap's ring bit 0 equals its own bullet's flags, with Range's 100%
      bounce as the deterministic row) and 7 (puppet: single-shot asplode/bounce, a +3 step
      spending bits 2/1/0 in spawn order, and a dropped packet whose successor's ring covers
      both shots -- the puppet's local percentages are ZERO in the rig, so an asploding puppet
      bullet can only have come off the wire, which is what discriminates against the pre-card
      re-roll).
    - **Verify with `eaNetFire()`** (`Compat/Net/NetFireTest.cs`, 36 assertions;
      `tools/headless/probes/net_single_tap.txt`). **DESTRUCTIVE** like `eaNetPickup` -- it pairs a
      real host session onto the live level, fires real bullets into it AND drives the local
      player's ship through scripted input, so use a throwaway `?level=Level2&invuln` boot. Six
      legs: the wrapped-delta decision over the whole 0..255 domain plus the per-slot tx counter's
      ship swap (the rigorous, phase-independent half), the SENDER's counter against the bullets it
      really spawned (including the two-taps-in-one-period case), then one shot = one bullet end to
      end, a +2 step, a burst with four of ten packets dropped, and an exact sustained cadence. **The negative control is a
      REFERENCE IMPLEMENTATION rather than the old code**, which is deleted: `PreCardTapBullets` /
      `PreCardLossBullets` mirror the firing-LEVEL rule on the same inputs and must give the wrong
      answers (2 for one tap, 6 of 10 under the loss pattern). Mutation-tested three ways, each
      failing disjoint legs -- the intent-side stamp fails ONLY the sender leg, one-bullet-per-
      counter-change fails only the +2 and loss legs, and a signed (unwrapped) delta fails only
      leg 1's arithmetic. A fourth puts the raw ship count on the wire (dropping the swap branch
      of `AdvanceTxShotCount`) and fails only the two tx legs.

## Metrics & verification

- **Verify with LOGGED METRICS, not screenshots** (`Compat/Net/NetMetrics`): a parseable
  `[net] role=... pops=... snapTx=... clRx=...` line every 5s. Healthy: buf ~100ms,
  extrap ~0, pops 0 (pop = a step no ship could physically make: > 2x MaxSpeed x realDt
  + 3px), drop/ordViol/seqGap 0 and **dupBad** 0 (`dup` itself is NOT a 0 bar -- read its
  split, below); on the world side, host `snapTx` climbing, client
  `snapRx/snapEnt` climbing with `snapUnk` small and non-climbing at steady state (but read
  its split -- see below), `pupPops` near 0 **judged against `snapTurn`** (next bullet),
  and the claim counters telling the kill story (`clTx` client-side ~=
  `clRx` host-side; `clKill` = claims that settled a live enemy, `clPaid` = generous
  payouts for already-dead enemies -- a nonzero `clPaid` IS the double-claim proof).
  **Two-tab test recipe:** the tabs must BOTH be visible (a backgrounded tab's rAF drops
  to ~1Hz and its peer times out / crawls) -- use two Chrome WINDOWS side by side:
  `?level=Level1&net=host&aiplayer&invuln&room=<r>` + same with `net=join`; both ships play
  themselves via `?aiplayer`, then read both consoles. `?room=` must be fresh per test pair.
  Add `?binlog` to both when the run is about lifecycle (it is the detector for a pause freeze,
  or for the purge filter eating a BANNER -- no longer for it eating a PUPPET, since card
  74403f83 exempted the puppet layer from the filter and the bin's divert log sits inside the
  branch that exemption skips; a puppet add that somehow still gets swallowed prints its own
  `[net] puppet add was diverted by the bin` line instead). For a death/reset, KEEP `?invuln`
  on both and call
  `eaKillShips()` in each console -- `Asplode()` only guards on `!IsDead`, so the helper bites
  through invulnerability, and leaving the flag on is what keeps the rest of the run from
  dying at random. `AllShipsDead` needs BOTH ships down, so fire it on both tabs.
  **`dup` is NOT a 0 bar either, and this line used to say it was (card 4c9448c8).** An
  EvSpawn that builds no puppet has FOUR causes and they used to share one counter, so the
  number the co-op gate asserts on could not be judged -- exactly the snapUnk mistake one layer
  down. Now split as `dupLive`/`dupDecl`/`dupBad`, with `dup` kept as their sum:
  - `dupLive` = the id was already ours. **BENIGN, and bursty by nature rather than steady:**
    the snapshot self-heal rebuilds ids off the unreliable stream lane, so the ordered EvSpawn
    for one of those lands second, and a checkpoint revert re-spawns ids across a purge the
    client is still settling. **Measured on a real WebRTC pairing:** a joiner arriving DURING a
    host reset read `dup=15` on its first `[net]` line and stayed flat at 15 while `evRx` climbed
    75 -> 134; a joiner arriving in steady state read **0 for a whole 100s soak**. Host reads 0
    in both. So a nonzero `dup` at a join or a reset is traffic, not a leak -- and the old
    reading that "every real session starts at ~10" was simply wrong.
  - `dupDecl` = the descriptor declined to construct. Benign by construction; the id is marked
    removed and the self-heal retries it after `RecentRemovalWindowMs`.
  - `dupBad` = **the fault shape, and the one to assert at 0.** No descriptor for the typeIdx --
    a registry/protocol mismatch, i.e. the peer is sending a type this build does not have --
    plus the two shapes that are unreachable today and would be news if they fired (the bin
    swallowing the add, the puppet layer not running). Note the build-hash handshake already
    refuses a mismatched peer before a session starts, so `dupBad` is a second line of defence
    rather than the only one; that is why it is a counter and not a teardown.
  Classified by `NetPuppets.SpawnRejectKind` and pinned by `eaNetScenarios()` scenario 5, whose
  churn legs assert the whole delta lands in `dupLive` with `dupBad` unmoved, **beside a negative
  control** (an EvSpawn carrying an unregistered typeIdx must move `dupBad` and NOT `dupLive`) --
  without which a classifier hard-wired to "already live" would pass. Mutation-tested both ways.

  **`snapUnk` climbing is not by itself a leak -- read the SPLIT, never the total** (card
  48ab9b2f). Three unrelated things make a snapshot entry "unknown", and the `[net]` line breaks
  them out as `snapNew`/`snapDead`/`snapBad` (`snapUnk` remains their sum):
  - `snapNew` = an id we had never seen, which the self-heal REBUILT from the snapshot. The
    unreliable stream lane routinely outruns the ordered reliable one, so a fresh spawn's first
    correction can beat its `EvSpawn`. **Benign, and it tracks the world's SPAWN rate** -- in a
    continuously spawning fight it never stops climbing, which is not a fault.
  - `snapDead` = an id removed HERE recently enough to still be settling, deliberately left
    dead -- the 3 s `RecentRemovalWindowMs` for an ordinary removal, and the longer
    `DyingReleaseWindowMs` for a puppet RELEASED to finish a deferred death (card 444eb614), so a
    boss death reads here for as long as the host keeps streaming it. **Benign, and it tracks the world's TOTAL removal rate.** The old note here tied this
    to `clTx`, which was WRONG and cost card 48ab9b2f's JIP pass its verdict: `MarkRemoved`
    fires on every local removal, host-authoritative `EvDeath`s included, so an IDLE joiner
    watching the host's AI clear a field logs plenty of `snapDead` with `clTx` pinned at 0.
  - `snapBad` = the rebuild was REFUSED (no descriptor for the typeIdx, the descriptor declined,
    or the bin swallowed the add). **This is the one that means trouble** -- it re-counts on
    every turn the host streams that id. An unknown typeIdx re-counts on literally every turn;
    the other two mark the id removed first, so they show as one `snapBad` then `snapDead` for
    3s, then another retry -- i.e. a slow, steady tick rather than a burst. Any sustained
    `snapBad` deserves a look.
  Attribution is pinned by **`eaNetSnap()`** (`Compat/Net/NetSnapshotTest.cs`), which drives the
  real `OnSnapshotEntry` through all four outcomes from the main menu -- a classification is
  invisible in any frame, and a second peer tab throttles too hard to show it anyway.
  **A STRUCTURAL check (roster, slots, who-owns-what) is the one thing two HIDDEN tabs in one
  window can still do**, which is how the four-seat roster in the `?netlocal` bullet was
  captured without hand-arranging windows. Two things make it survive: `index.html` falls back
  to `setTimeout(tickJS, 33)` while `document.hidden` (a REQUESTED ~30Hz -- Chrome clamps
  hidden-tab timers after ~10s and much harder past 5 min, so treat it as a short window, not a
  rate you hold), and the roster simply does not depend on cadence -- once the link goes quiet
  past the channel's own 500 ms threshold (the card-14c5943e link-quiet arm; `PeerStalled`
  itself only ever arms LATER, at 1200 ms) the friend timeout stretches to
  `PeerTimeoutMs + PeerGraceMs`, and a timed-out friend **keeps its seat** by design
  (`NetSession.Friends.cs`). It does NOT extend to anything timing-derived:
  `pops`/`pupPops`/`buf`/`extrap` off a hidden or unfocused tab are meaningless (the FPS HUD
  says so on its own readout), so every smoothness or feel verdict still needs two focused
  windows.

## Level-script beats, reset & victory

- **Script beats replicate at the side-effect PRIMITIVES (card 11.3), never per level:**
  the level script only runs on the host, so its observable side effects are hooked where
  they happen and mirrored as reliable events -- `MessageEvent`/`UnlockEvent` at their
  banner spawns (**the join peer is a GUEST: `EvUnlock` neither grants nor announces there
  -- card 125490d9, see the guest bullet below**),
  the mid-level `Background` ops (`SetSpeed`/`Queue*`/belt slowdowns/`SetAlienBase2..6`, and
  since card ca4fd94f the whole-SCENE setters when a SCRIPT runs one mid-level; the wire opcode
  enum `NetBackgroundOp` is APPEND-ONLY; a scene setter run at Initialize is still not
  replicated -- both peers run their own), `SoundManager.PlayMusic/StopMusic`
  (client applies via `NetApplyMusic`, deduped against the playing cue so the boot-time
  track never restarts), and the checkpoint callback (client mirrors `score.Save()` so a
  later reset restores the same baseline). Any future boss code calling these primitives
  replicates for free. `CrossFade` is deliberately NOT hooked (it belongs to the reset
  flow, which each side runs itself).
- **THE JOIN PEER IS A GUEST -- `EvUnlock` does NOTHING there (card 125490d9).** No grant, no
  `SaveThreaded`, no banner. The host still emits the beat and the joiner still DECODES it (see
  below); it simply has no effect.
  - **What it used to do, and why that stopped being right.** The handler granted the item plus
    its pair-ups (`HarderDifficulties` -> `InsaneDifficulty`, `UnlockType.cheat` -> `Cheats`,
    `UnlockType.challenge` -> `Challenges`) and then called `Unlockables.SaveThreaded()`. The
    reasoning -- "the join peer played the level too" -- dates from card 11.3, when a session was
    two people who had deliberately swapped a room code. The **public game browser** changed the
    population: anyone can join a listed game and `Settings.AllowOnlineJoins` defaults **ON**, so
    a stranger could write `HarderDifficulties` / `Level2` / `Level3` / the challenge levels /
    `Cheats` / `Awardments` into your `Unlockables.xml`. **A joiner is NOT a couch player** -- it
    is a separate machine with its own save file, which is the fact that makes this different
    from local co-op.
  - **The user's ruling, and it is a PRODUCT call, not a security one** (nothing here is a trust
    boundary): joining a game online makes you a guest for that game, and your personal unlock
    state is wholly unaffected. The banner went with the grant deliberately -- announcing an
    unlock the joiner did not receive is worse than saying nothing.
  - **The DECODE stays and must stay.** `NetSession`'s EvUnlock case is the only live caller of
    `NetProtocol.TryDecodeUnlockEvent`, so dropping it would leave that codec's wire-enum bound
    (and `ProbeWireEnums`' row for it) asserting about dead code, and a malformed frame would
    stop being refused. `BeatsRx` is still counted: it means "beats received off the wire", not
    "effects applied", and zeroing it would make a healthy session look like the host had gone
    quiet.
  - **No protocol change and no version bump** -- the host still sends `EvUnlock`, and an older
    peer still applies it, so a mixed pairing degrades to the old generous behaviour on that
    peer rather than desyncing.
  - **Not affected:** `EvMessage` (ordinary script banners still replicate), and the joiner can
    still PLAY a level it has not unlocked -- `MenuScene.NetLaunchMirror` never consults
    `Unlockables`, so being a guest costs it nothing in the session it is actually in.
- **Death/checkpoint reset + victory are host-authoritative broadcasts (card 11.3):**
  `LoseLife` no-ops on a client; the host broadcasts the branch it took (EvReset:
  respawn / reset / game over) and `GameScene.NetApplyReset` mirrors the exact state
  transition -- the client then runs its own purge-and-replay flow while the host's
  post-revert spawner replay rebuilds the puppets. `Victory()` broadcasts EvVictory (the
  win trigger lives in the host-only script); the client runs its own `Victory()` from it,
  achievements included. `GameScene.NetActiveScene` (static, set in Initialize / cleared
  in Terminate) is how NetSession reaches the private state machine.

## Host kick / block

- **Host kick / kick+block (card 0b8a300b) -- the host's ONLY agency under a remote pause.**
  A remote pause freezes our world via `ComponentBin.Push`, which disables every collection
  component **including `GameScene`** -- so the host's own pause trigger never runs, and the
  drop failsafe can't help either (a held pause widens the timeout to the 120s
  `PausedPeerTimeoutMs` backstop). Before this card a stranger off the public game browser
  could freeze someone's run indefinitely.
  - **`NetKickMenu`** (a `ConfirmationMenu`) replaces `NetPauseOverlay` for the HOST once the
    pause outlasts `NetSession.KickOfferDelayMs` (4s): `Keep Waiting` / `Kick Player` /
    `Kick and Block`. It works for the same reason the local pause menu does -- **added AFTER
    the Push, so it stays `Enabled`**. Entry 0 is `Keep Waiting` and preselected, so a
    reflexive Enter over a suddenly-appearing menu is harmless. Declining **re-arms** the
    offer (`NetSession.RearmKickOffer`), so waiting once never forfeits it. The client keeps
    the plain overlay -- there is nobody for it to kick.
  - **The offer timer lives in `NetSession.Update`, not `GameScene`** -- `GameScene` is frozen
    by the Push, so it cannot time its own escape hatch. Real time (`NowMs`), like the rest of
    the net layer; `gameTime` means nothing in a frozen world.
  - **`KickPeer(block)` splits the teardown deliberately:** everything visible happens now
    (unfreeze, `ExplodePuppet`, `oracle.ReleasePlayer(Remote)` + `ReleaseAllFriendPuppets`),
    but `Stop()` waits out `RejectGraceMs` -- `Stop() -> pc.close()` is ABORTIVE on WebRTC and
    would discard the still-buffered `EvKick`, leaving the kicked player with a generic
    "disconnected" instead of a reason. Do NOT collapse it back into one call.
    The client's `EvKick` handler reuses `EndMatchPeerGone` -> `NetApplyPeerLeft`, which
    already unwinds its own pause-menu depth (it is almost certainly sitting in it) and exits.
    A kick applies to EVERY session kind and is never a match end for the KICKER: the host
    reverts to single-player and plays on (`RevertToSinglePlayer`, shared with JIP peer-loss).
  - **The block needs an identity, so the handshake gained one -- protocol v5 -> v6.**
    `eaRtc.peerId` = a random 128-bit token minted once into `localStorage`, FNV-hashed to 8
    wire bytes (`HelloBytes` 13 -> 21). **It is SELF-REPORTED: a speed bump against casual
    re-joining, not authentication** -- clearing site data or incognito mints a new one. Never
    sent to the signaling server, only to an already-connected peer. Don't build anything that
    must trust it on this. `peerId` 0 (JS could not produce one) is never recorded and never
    matched, so one broken `localStorage` can't get every such peer refused.
  - Enforced in `HandleHello` (`RejectBanned`) -- the ONE choke point both rejoin routes pass
    through (public browser AND a typed room code), and before `PeerConnected`/slot
    reservation, so a blocked peer re-pairing never touches the world. `blockedPeers`
    deliberately **survives `NetSession.Stop()`** (a kick stops the session and the host
    re-lists seconds later; the block must outlive that) and is cleared in
    `GameScene.Terminate` = the card's "for that session only".
  - **Verify with `eaKickTest()`** (`Compat/Net/NetKickTest.cs`) -- the block predicate + the
    v6 codec as DATA, because both dangerous failures are invisible in play: a block that
    fails to persist across the kick's own `Stop()`, and a wire-layout slip that decodes the
    wrong bytes as a peer id. It restores the live set, and SKIPS the survives-`Stop()` leg
    over a live session rather than ending a real match (it says so; a skipped leg is not a
    pass). `?netkickshot` (pair with `?level=`) parks the menu over a live level for a
    screenshot. **`?netfakepeer=<s>` is REQUIRED for any two-tab test** -- both dev tabs share
    one `localStorage`, so they present the SAME peer id and blocking the joiner would block
    yourself (the `?netfakehash=` trick, same reason).
- **The HOST PAUSE MENU reaches the same kick deliberately, and owns the room switch (card
  0d6ffe70).** `NetKickMenu` above only ever appears unprompted, under a REMOTE pause -- so a
  peer who simply never pauses (blocking shots, hogging powerups, idling) was unkickable, which
  is deferred card `98217618`. `pausedScene` now carries an **"Online Play"** row into
  `Compat/Net/NetHostMenu`, and that submenu is where both halves live.
  - **NO new protocol, NO wire bytes, NO server call.** Both halves are second doors onto
    machinery that already existed: `NetSession.KickPeer(bool)` (card 0b8a300b) and
    `Settings.AllowOnlineJoins`, which `NetListing` already watches -- off unlists while KEEPING
    the room code, on re-lists the SAME code, so closing and re-opening mid-run renumbers nobody.
  - **The row toggles the OPTIONS SETTING, not a per-run override.** So closing the room persists
    into later games, exactly as if the player had gone to Options; the row is labelled with that
    menu's own wording (`Allow Online Joins: Enabled/Disabled`) rather than a verb, for that
    reason. `SaveThreaded()` fires on the toggle.
  - **The two shapes COMPOSE since card 0257f8ba (they used to be mutually exclusive):** a HOST
    session with a free seat is itself listable now, so a paused host can close its room against
    further strangers AND kick a peer from the same menu -- rows `Back, RoomToggle, Kick@...`,
    with the toggle always after Back so entry 0 stays non-destructive. The old exclusivity note
    ("the day listing during a session becomes possible...") came due exactly as written; the
    NetHostMenuTest sweep now pins the composed shape instead.
  - **`NetListing.CouldList` is the new predicate half** -- eligible EXCEPT for the setting.
    `Eligible` cannot answer "is this game one toggle away from joinable", since it is already
    false whenever the setting is off.
  - **Entry 0 is never destructive**: the room shape leads with the toggle, the kick shape leads
    with `Back`. `MenuSub1.Reset()` forces `selectedEntry = 0` and this menu opens over a frozen
    world -- same reasoning as `NetKickMenu` preselecting "Keep Waiting".
  - **The pause rows are REBUILT on every pause** (`GameScene.BuildPauseEntries`), not fixed in
    the constructor: `AddEntry` only appends, so a conditional row added later would sit past
    "Exit to Main Menu". Callers must `Reset()` after, since the list can get SHORTER.
    `NetApplyPeerLeft` unwinds the submenu too -- a peer leaving while it is open would otherwise
    strand it over a level that is running again.
  - **The kick is per PEER and the wording says so.** The protocol is 2-peer, so there is one
    other MACHINE; any couch players it brought leave with it. There is no per-seat kick.
  - **Verify with `eaHostMenu()` / `.test()` / `.live()`, never a screenshot** -- a missing row
    looks exactly like a game with nothing to offer. `.test()` (`NetHostMenuTest`, 47 assertions)
    sweeps the pure decision over all 32 states and runs under `logic_probe` as `ProbeHostMenu`;
    `.live()` (`NetHostMenuLiveTest`, 18) is what that sweep structurally cannot see -- a real
    host session with a scripted peer, so `CurrentState()`'s live reads are covered, plus the
    real `KickPeer` call and the row RETRACTING afterwards. Both are legs of `net_selftests.txt`.
    The MENU WIRING is `tools/headless/probes/net_host_menu.txt` (the row opens the submenu; the
    toggle really unlists) with `net_host_menu_absent.txt` as its negative control (a plain
    `?level=` boot is `DebugFlags.Active`, so nothing is listable and the row must be gone).

## Pause, tether & 11.2 replication follow-ups

- **Pause is a replicated event; the triggers stay local (card 11.3):** the local pause
  push / every resume path sends EvPause on/off. The receiving side freezes via
  `Collection.Push()` under a `NetPauseOverlay` ("OTHER PLAYER PAUSED") -- no interactive
  menu. Overlaps resolve in `GameScene.NetSetRemotePaused` + `NetLocalPauseReleased`:
  the world unfreezes only when BOTH sides are clear; a scene that Initializes while
  `NetSession.RemotePaused` picks the freeze up at the end of Initialize (level-load
  race). GOTCHA kept: net TeamChallenge seats ONLY the local device -- the offline
  `AddPlayer(PadOne)` would trip the disconnected-gamepad force-pause every tick and
  squat the remote slot.
- **TeamChallenge tether online = a LOCAL first-order pull (card 11.3):** the rigid
  midpoint +/-39px `SetPosition` pinning would fight the interpolation buffer, so in a net
  session each peer softly pulls only its OWN ship toward the puppet's on-screen position
  (`ShipConnector.NetPullOwnShip`; consts `NetRestPx` 78 / `NetPullK` 0.0018/ms /
  `NetMaxPullPxPerMs` 0.22, picked by `tools/sim/tether_sim.py` -- first-order, no
  velocity state, overdamped to 300ms one-way; if it ever wobbles SOFTEN K, never
  stiffen; the clamp sits below ship MaxSpeed so players can always fight the pull).
  Tether break is an or-of-either-peer idempotent event (local cause sends EvTetherBreak,
  the receiver breaks silently via `NetBreakSilently`); shared-fate death asplodes only
  locally-owned ships and defers the life/reset to the host. Connector creation waits for
  BOTH ships (the puppet joins a beat late -- `netConnectorPending` in TeamChallenge).
- **That soft pull was UNBOUNDED, and the hard cap is card 2cfab019.** "Ships can fly further and further away from each other." **It is a GAIN problem, not a latency problem** -- measured identical at one-way 0/50/100/200/300ms -- so the card's own guessed cause ("only the host moves itself back towards the client") is wrong twice over: both peers always ran `NetPullOwnShip`, and each always pulled only the ship it owns. The soft pull saturates at `NetMaxPullPxPerMs` 0.22 while a ship thrusts at `ShipMaxSpeed` 0.33, so any **one-sided pull budget** separates without bound: both players thrusting apart (0.22px/ms forever), or -- the everyday trigger -- one thrusting while the other is **pinned against the 800x600 clamp in `PlayerShip.Update`**, which is what backing into a corner to escape being dragged produces. A LONE thruster was never broken: the idle partner's own pull covers the shortfall.
  - **WRITE THIS NUMBER DOWN: ordinary online drag play already sits at ~167px, against offline's rigid 78px.** `REST + (ShipMaxSpeed/2)/NetPullK` = **169.7px perceived, 166.9px measured true** -- the same discrete-tick offset as the 220/214.5 pair below, so quote the formula and the measurement as a pair; the formula does not evaluate to the measured figure. So the tether is ~2x looser online than off *before* the cap, and that is untouched here -- it lives in the soft law the header tells you not to stiffen, and the user's ruling was about the runaway. It is the thing a player reports next as "the connector feels loose online"; the number is here so the next person does not re-measure it.
  - **The cap is a RATE, not a position clamp, and that distinction IS the design.** A clamp (`if (dist > MAX) SetPosition(anchor +/- MAX)`) has mutual loop gain exactly 1 -- the two peers' clamp equations substitute to `x_A(t) = x_A(t-2D)`, a pure delay at unity gain, marginally stable, ringing forever against each other's stale views. That is precisely the mutual stale-anchor loop `ShipConnector`'s header warns about. Instead the pull's SPEED ceiling rises above ship thrust past `NetHardPx`, so thrust is OUT-RUN rather than refused: per-tick loop gain `NetHardK * dt` = 0.09, and the bound is an equilibrium of SPEEDS, hence latency-independent. Consts `NetHardPx` 200 / `NetHardK` 0.0055/ms / `NetMaxHardPullPxPerMs` 0.55. Equilibrium `NetHardPx + (ShipMaxSpeed - NetMaxPullPxPerMs)/NetHardK` = 220px perceived, **214.5px true, which the sim and the real game code agree on independently**. `NetPullK`/`NetMaxPullPxPerMs` are untouched and the knee is *derived* from where the soft cap saturates (`REST + 0.22/0.0018` = 200.2), which is what makes "unchanged below the knee" exact rather than approximate.
  - **NOT gated on peer freshness, deliberately.** A pull toward a stalled peer's frozen anchor is a CONTRACTION with a fixed point at `NetRestPx`, not an integrator: total travel is `dist - NetRestPx` however long the stall runs (measured 136.5px, *identical* with the cap and without). That is what separates it from `ShipStateBuffer.ExtrapolateCapMs` and `Lazer.NetExtrapolateCapMs`, which bound `pos + vel*t` integrators and so must be bounded in TIME. Gating it would restore the runaway for the whole 1200ms `PeerStallMs` grace -- when it is most likely: across one stall the pre-card law adds 264px of escape and keeps going, the cap leaves a ~20px correction on recovery.
  - **`NetMaxHardPullPxPerMs` 0.55 is a guard, not a feel value, and it DOES bind:** an online TeamChallenge builds its tether once both ships exist, and they enter from fixed off-screen points **567-696px apart**, where the raw hard term would ask for 37-49px per FRAME. 0.55 is 1.67x `ShipMaxSpeed`, deliberately under the 2x `NetSession`'s own correction-pop detector calls "a step no real ship could make" (696 -> 220px in ~883ms). Offline that same spawn is slammed rigid on frame 1, so this is the gentler of the two.
  - **One honest caveat, measured:** the law reads the PERCEIVED separation, and in a steady drag both ships move at the same speed, so each peer's stale anchor is displaced by `v * (one_way + interp)` along the direction of travel -- the LEADING peer perceives `true + v*delay`. Past ~200ms one-way that is enough to cross the knee while the TRUE gap is only ~162px, so the cap engages on a stale reading. Bounded, non-ringing, and it acts in the TIGHTENING direction (181 -> 162px, toward rest), so it is accepted. **Up to 100ms one-way nothing moves at all**, and `tether_sim.py` asserts exactly that split.
  - **Same card, second defect: two LOCALLY-owned endpoints now take the RIGID offline law.** In a session with couch players (`?netlocal=`) a pair of local ships could be connected, and `NetPullOwnShip` applied its one-sided soft pull to only ONE of them -- so it ran away with a single thruster, with no staleness anywhere to justify being soft. On the other peer both are puppets, so its `NetPullOwnShip` returns early and the rigid positions arrive unchallenged; no peer fights another. The endpoint pick also moved from `Controller != Remote` to `!IsNetPuppet` (`PlayerShip.IsNetPuppet` is now `internal`) -- a `RemoteFriend` passed the old test and would have been moved by us.
  - **Verify with `eaNetTether()`** (`Compat/Net/NetTetherTest.cs`, 14 assertions; `eval NetTether`). **DESTRUCTIVE** like `eaNetPickup` -- it pairs a real HOST session onto the live level and adopts a real puppet -- so run it in a throwaway `?level=Level2&invuln` boot. Three instruments split the card and none subsumes another: `tools/sim/tether_sim.py` owns the SCENARIOS (two coupled peers, stale anchors, the latency sweep, ringing, the stall), `logic_probe`'s `ProbeTetherWall` owns the pure LAW, and the suite owns the WIRING. **`?nettetherwall=0` restores the runaway** (in `DebugFlags.Active`, the `?netstaleguard=0` idiom) and is the negative control; because it is parsed at boot the probe pair is two BOOTS -- `tools/headless/probes/net_tether_wall.txt` + `net_tether_wall_absent.txt`, 214.5px vs 1010.4px. **The mutation run earned its keep**: the endpoint-pick mutation initially passed both files, because the leg put OUR ship in endpoint A and the pick takes A when A is ours, so the puppet was never examined. No protocol change.
- **World-authority coverage gaps (follow-up to card 11.2):** the replicable set was extended
  to the enemy/boss types 11.2 left host-only -- PlasmaBall, the paratrooper family
  (ParatrooperAlien/ParatrooperBrain/Parachute), FakeBoss, SpiderBoss, BrainBoss,
  SpiderHelperMothership -- as `NetTypeRegistry` descriptors 21-28 (append-only;
  `Compat/Net/Descriptors/DescriptorsCoverage.cs`). The enemy laser-CHARGE glow (a child
  `LazerGenerator` the emitter draws by hand) now replicates too: rather than making
  LazerGenerator itself replicable (it is also the player-summon glow), the SweepUFO / MarsBoss /
  SpiderHelperMothership descriptors stream a tiny charge state and the puppet rebuilds a local
  copy into the emitter's own generator field (`AlienDrawableGameComponent.NetDriveExtras`
  driver hook + `Compat/Net/NetChargeGlow`). The fired beam already replicated as its own `Lazer`.
  **Cards 57ea30cd / c146422f extend that seam to the big `UFO` (`UFOState.lazor`) and the
  JunkBoss' asteroid-attraction swarm, and make all of them AUDIBLE** -- see the transient-feedback
  bullet under "Protocol, NetIds & the replicable set" for the player-vs-world rule that replaced
  the old blanket "a puppet is never the shooter".
- **AI "friend" ships replicate (host-authoritative), follow-up to card 11.2:** the Mechanical
  Friends cheat is re-enabled in net sessions -- but ONLY the host adds AI friends (it runs the
  real AI, whose enemy kills already replicate), and only after the client's Remote ship has
  taken its slot. The host streams each friend (a slot-tagged `MsgShipState` with the primary
  flag clear since v23) and the client
  shows it as a `ControlDevice.RemoteFriend` puppet (`Compat/Net/NetSession.Friends.cs`): its own
  per-slot jitter buffer/interpolation clock (`ShipChannel`, the same class the primary uses
  since card b2828be8 -- the asymmetries are per-channel behaviour), IDENTITY slot mapping (the puppet lands in the host's slot so per-slot
  score/lives sync lines up), bullets re-fired locally, death via a per-slot stream timeout. The
  budget is `Settings.Friends + 1` TOTAL ships incl. the remote (so a 2-human session needs the
  cheat >= 2 to spawn any AI friend). The whole path is dormant unless the cheat is on.
  `ControlDevice.RemoteFriend` is APPEND-ONLY. (NOTE: the game-browser JIP attach path below is a
  separate session and does not stream friends -- its listing stays refused while `Friends>0`.)

## Hardening & known limits

- **Hardening pass (card 4717d3cf / 11.5):**
  - **A powerup collected by EITHER peer drives that peer's HUD slot.** `PlayerShip.CollidesWith`
    is the only `SetPowerup` caller and is gated to the local ship, and each peer numbers its
    OWN ship slot 0 -- so the icon used to move only on the P1 panel, and a remote pickup
    settled as a bare despawn. Both settle paths (host `HandleClaim`, client
    `NetPuppets.OnRemoteDeath`) now call `NetSession.ApplyRemotePowerup`. This also restores the
    remote player's powerup LEVEL, since `ScoreVisualiser.increasecombo` only feeds `AddExp`
    while that slot's `powerupactive` is set. **Only the INDICATOR was mirrored, and that turned
    out to be a bug in its own right -- see the remote-pickup bullet below** (cards 83271f3d /
    10f9dba4 / d53431b4). The Blast/bomb count is STILL deliberately unmirrored, because the
    spend side (`NetDoBlast`) does not decrement it either. A slot off the wire must be bounded
    by `ScoreVisualiser.SlotCount` (4), NOT the 8 of the claim ledgers' PaidMask.
  - **`AlienDrawableGameComponent.NetSpinPerMs` opts a type out of REPLICATED rotation** and
    spins its puppets locally instead (Asteroid). A puppet's Update is frozen, so a
    continuously spinning type could only advance at its ~16.7 Hz round-robin snapshot turn --
    visibly choppy. Only override where rotation is cosmetic and no hitbox reads it.
  - **Peer stall != peer lost.** `PeerStallMs` raises `NetWaitOverlay` (banner only -- it does
    NOT push the collection, because the world staying live is the point and dimming a
    playfield the player is still dodging in would be worse than the hiccup) and parks puppet
    dead-reckoning (`NetSession.PeerStalled`; without that, the wider grace would let stale
    velocities fling the enemy world seconds off and then snap). The verdict only lands at
    `PeerTimeoutMs + PeerGraceMs`, through the single `EndMatchPeerGone` path shared by
    EvLeave / drop timeout / pagehide bye. GOTCHA: `GameScene.Terminate` must drop the banner
    BEFORE nulling `NetActiveScene` -- it is a plain `DrawableGameComponent` in the global bin
    that no `Purge<T>` covers, and level scenes are re-added singletons, so an orphan would
    both draw over the menus and poison the next play of that level.
- **Known limits (by design -- next cards):** a dead local player will NOT respawn while the
  remote puppet lives (LoseLife triggers on AllShipsDead); DevCommentEvent commentary is not
  replicated (profile-local setting).
  - **The SESSION holds up to four machines since card 87242257 (Stage 11.9), and since card
    `0257f8ba` (Stage 11.10) the REAL-NETWORK path reaches it too** -- four-machine rooms in
    the menu lobby and the public browser, JIP into slots 3/4, the lobby roster panels. The
    epic closed with 11.11's hardening (card `6fb406bc`: relayed-channel interp delay, the
    measured N=4 bandwidth soak, the four-process rig, `bufferedAmount` back-pressure) -- and
    note the real-WebRTC four-machine flow has NOT had a
    four-real-browsers playtest yet; the rigs here cover the session and the menus, not NAT
    reality. The player dimension was already 4-wide everywhere
    (`Oracle.MaxPlayers`, `ScoreVisualiser.SlotCount`, the slot-keyed ship stream,
    `EvScoreSync`, the claim ledgers -- and 4-player online as two consoles with a couch
    partner each has worked since card 2e0f908b). The design is `plans/4p-online-coop.md`
    (star/host-relay, forced by the no-TURN connection math). Boss puppets are
  best-effort (the harness caveat): deep Update-reached attack poses may diverge until their
  state extras grow. **The multi-phase DEATHS are no longer part of that** (cards 303bfb5b /
  13aa596c): a remote death whose `KilledBy` defers its own removal now RELEASES the puppet to
  finish dying locally instead of deleting it mid-animation -- see the deferred-death bullet
  under "Claims". **All four bosses have now been WATCHED on a client and are pinned (card
  ad9c8f8b)**: BrainBoss, FakeBoss and JunkBoss were covered by construction and are verified
  end to end by `eaNetDeathFx` section 8; SpiderBoss was NOT covered and is the hole that
  coverage found -- see the not-a-KillableAlien bullet under "Claims". The time-scaling half of
  the old first-wipe `pupPops` burst is FIXED (the puppet driver now dead-reckons on real time,
  above); if a residual first-wipe burst ever shows, it's the reset/id-churn transition (purge +
  checkpoint replay), reproducible in the headless two-peer net sim's reset scenario, not the
  puppet clock.

## Puppet SMOOTHNESS (cards c92f3817 / 0dfc4495 / d3add86f / 8dabe812 / 0108d1fc)

**Read the ANCHORED MOTION section after this one (card c1a38ef9): it REPLACES the Lazer
estimator below outright, and it corrects the "~100x host" jerk floor quoted here and in
`CorrectionWindowFor`'s header -- most of that figure was a rig artifact (a spawn transient), not
the steady state.**


A family, not four bugs: everything a puppet does between snapshot turns was either not simulated
at all or corrected as if the blind window were fixed. **`pupPops` cannot see any of it** -- every
one of these stutters happens while the error stays well under `SnapThresholdPx`, so the counter
reads a contented 0 throughout. The instrument is
`python tools/sim/net_puppet_drive_sim.py --smoothness`, which measures the SHAPE of the motion
(*jerk* = stddev of successive per-tick step deltas) against the host as a control, and asserts.

- **`AlienDrawableGameComponent.NetFrameLocal` (default TRUE) opts a puppet out of REPLICATED
  FRAMES** -- the `NetSpinPerMs` idiom one field over. `curframe` is pinned once at spawn and the
  driver's `NetAdvanceFrame` owns it from there; `ApplySnapshotState` stops calling `NetSetFrame`.
  In STEADY state the correction was already a no-op (both peers advance the same loop at the same
  fps), so what it removes is the DISTURBANCES: `MsgWorldSnapshot` rides the stream lane, which is
  unordered `maxRetransmits:0` and carries **no sequence and no timestamp**, so a reordered or late
  entry hands the driver an OLDER frame than the one being shown and the animation kicks BACKWARD.
  Nothing but Draw reads `curframe`, so there is no correctness case on the other side.
  - **THE AUDIT IS THE WHOLE RISK, and it is two questions, not one.** Override to `false` when
    either half of "a free-running loop at a constant fps" fails. Only two types in the registry
    do, and each fails a different half: **`Spider`** writes `curframe` outright from its
    rear-up/launch/land choreography (so the frame IS the pose, and a local loop would pounce on
    its own schedule), and **`MarsBoss`** re-derives `fps = Lerp(32, 16, HitPointsNormalized)`
    every `Update`, so a puppet -- whose Update never runs -- would free-run at Initialize's 16
    forever and drift. Everything else keeps the default. The audit is greppable and worth
    re-running if a type is added: `curframe =` and `fps =` outside `AlienDrawableGameComponent`.
  - **`SpiderHelperMothership` is the near-miss that makes the distinction concrete** -- it is
    MarsBoss's twin (4x4 sheet, A/B half flipped on the wrap) and it DOES qualify, because its
    `fps` is a constant 16 set once in `Initialize`. It is card c92f3817's subject.
  - Types whose Draw ignores `curframe` are unaffected either way and keep the default:
    `SpiderBoss` / `FakeBoss` / `BattleSkull` animate an `AnimatedSprite` through their own
    replicated `animFrame` state extra, and `Wall` / `Lazer` / `StationaryBoss` / `BrainBoss` /
    `Powerup` are single-frame.
  - Covered by `eaNetEntity()` (47 checks now, up from 43 -- the four added are `NetScaleLocal`'s,
    whose polarity is the OPPOSITE inversion; see LEVEL-3 WALLS) -- the base answer, an override, and the
    two real opt-outs with a UFO beside them as the control, since a predicate hard-wired to
    `false` would otherwise pass.
- **The correction window is `max(150ms, 2 x SnapshotTurnMs)`, not a constant 150ms.** The window
  was fixed while the thing it absorbs is not: an entity is corrected only every `snapTurn`, which
  scales with the world (60ms at 16 live entities, 480ms at 128). Draining faster than corrections
  arrive leaves the puppet on a stale dead-reckon most of its life and then lurching. Measured
  (FlyingSpider-shaped motion, host control 0.0008):

  | N | turn | fixed 150ms | 2x turn |
  |---|---|---|---|
  | 16 | 60ms | 0.089 | 0.089 |
  | 32 | 120ms | 0.114 | 0.092 |
  | 64 | 240ms | 0.180 | 0.091 |
  | 128 | 480ms | 0.327 | 0.090 |

  Flat in the world size instead of degrading 3.7x, and identical at N=16 where the 150ms FLOOR is
  what is in force. **An exponential / critically-damped drain was proposed first, measured, and
  REFUTED** -- worse at every N (0.132 / 0.187 / 0.317 / 0.653), because its tail keeps a velocity
  offset alive to be re-hit by the next correction. The sim asserts that, so it stays refuted.
  The window is held PER PUPPET (`PuppetInfo.CorrectionMs`) rather than read live in `Drive`: a
  spawn burst mid-blend would otherwise rescale the fraction already applied and jump.
- **THE TELEPORT MARKER (card e79bb994, replacing card 8dabe812's plausibility cap).** A finite
  difference cannot tell motion from a REPOSITION, and the SpiderBoss is parked at the far screen
  edge to start each fly-by. Differentiating that ~800px jump stamped 42-57 px/ms onto the wire,
  and the client snapped (correctly) and then **dead-reckoned onward at teleport speed** -- puppets
  are collidable, so the boss crossed the screen and killed the local player, in card 8dabe812's
  "2-3 frames". **The host KNOWS when it teleports something, so it says so**: the reposition sites
  call `AlienDrawableGameComponent.NetNoteTeleport()`, `CaptureBaseState` read-and-clears the latch
  and stamps the entity's declared `NetSpeedVector` instead of differentiating, and the marker
  rides the wire so the client snaps rather than blends.
  - **THE DECLARED SPEED IS THE BEST THE HOST HAS, NOT A GOOD ANSWER** -- half the replicable set
    (the SpiderBoss included) moves by writing `Position` directly and never assigns
    `Speed`/`Direction`, so its `NetSpeedVector` is **ZERO**: a marked park sends zero velocity and
    the puppet stands still until its next turn, up to ~1.2 s in a big world. That is the correct
    trade against flinging it across the screen collidably, and it is exactly what card 8dabe812's
    cap already did. Do not read the fallback as informative.
    **FIXED for `SpiderBoss` by card 76ec8bdb -- see the SCRIPTED MOTION section**, which gives a
    scripted-position type a third source of truth (its own announced velocity) rather than either
    of the two that lie. The sentence above still describes every type that has NOT taken that
    seam, which is all of them bar one; note also that at these three parks the zero is momentarily
    CORRECT (the boss is genuinely held for the 1000 ms warning), and it is the pause -> sweep
    boundary after it that costs the joiner.
  - **The reposition sites are the whole feature, and there are exactly four types.** Found by
    auditing every direct `Position =` on the 29 replicable types: `Braineroid`'s four wrap
    branches, `EvilSkull`'s random respawn in `CollidesWith`, `SpiderBoss`'s three fly-by parks,
    and **`Ball`'s three screen wraps -- a NEW find**, which the old cap covered only by luck (its
    wrap happens to imply ~13 px/ms). Everything else that writes `Position` outright is either a
    spawn-time write (fresh netId, `HasLastPos` false, harmless) or a small end-of-lerp clamp
    (`FakeBoss` 169, `SweepUFO` 127, `JunkBoss` 286, `PunchingBag` 66, `BrainBoss` 331, `StarMine`
    285, `SpiderBoss`'s landing snap at 539 -- all under one tick of motion).
  - **THE LATCH IS READ-AND-CLEAR, and both halves matter.** It must survive from the reposition
    until that entity's next snapshot TURN (up to ~1.2 s in a big world) and then be spent exactly
    once -- a latch left set refuses the FOLLOWING turn's velocity too, freezing the puppet's dead
    reckoning, which is a worse bug than the one being fixed and invisible without the suite's
    "the marker is SPENT" leg. `OnHostSpawn` consumes it too and discards the flag: `EvSpawn`
    carries the shared base-state block, which has no flags byte, but it has just advanced the
    velocity baseline so an unspent latch would poison the next turn.
  - **The wire byte is per-SAMPLE and sits on the snapshot ENTRY, not in `NetBaseState`**
    (`NetProtocol.NetSnapshotFlags`, protocol **v13**): `EvSpawn` shares `WriteBaseState`, and a
    spawn is by definition a first observation, so the flag would be permanently zero there. It is
    a BITMASK, so it takes no decode-boundary validator and no `ProbeWireEnums` row -- an unknown
    BIT is masked and ignored, which is the correct degradation, where a wire ENUM must reject.
  - **The client SNAPS a marked entry whatever the error, and does NOT count `pupPops`.** Two wins
    the host-side cap could not reach: a reposition SHORTER than `SnapThresholdPx` (100) was
    BLENDED, so the entity slid across the screen instead of reappearing -- reachable, since
    EvilSkull respawns at a random point -- and every SpiderBoss fly-by used to inflate a counter
    that is supposed to mean "an error the layer could not account for".
  - **The 5.0 px/ms figure SURVIVES, DEMOTED to a reporting-only diagnostic**
    (`NetSession.NoteIfUnmarkedTeleport` -> the shared `ReportUnmarkedTeleport`). Nothing above it
    reads the number, so it can no longer alter a byte on the wire -- which inverts its risk: as a
    cap, a value set too LOW silently clipped a genuinely fast enemy and recreated this whole
    family's stutter one type at a time; as a diagnostic the worst case is a spurious line. All it
    does now is print, once per type, `[net] UNMARKED teleport suspected: <Type> at <n> px/ms --
    add NetNoteTeleport() at its reposition site`. **That line's shape is an interface** --
    `net_velguard.txt` greps it.
  - **The threshold is still MEASURED, because a diagnostic that cries wolf is worse than none.**
    Genuine motion tops out at ~2.5 px/ms (MarsBoss's entry PowerCurve 2.404 measured, EvilSkull's
    launched `MaxSpeed` 2.5 declared); the repositions sit an order of magnitude up (SpiderBoss
    42-57, a `wrapping` Braineroid 13.5, EvilSkull's random respawn 11.6). 5.0 is the log midpoint.
    **Do not tighten it toward the measurements** -- gameplay RNG is unseeded and three runs of one
    rig read MarsBoss at 1.777 / 2.013 / 2.404, so a threshold inside that band is a coin flip.
  - **`eaNetVelScan(on?)` / `eval NetVelScan true` audits BOTH halves, and needs NO net session.**
    Arm, soak, read. Per type it reports SUSTAINED speed (a plateau: a neighbouring sample --
    either side -- at least half as fast) beside the raw peak, because a reposition is a
    one-interval spike by definition; plus **`marked=<n>`**, how many repositions that type
    ANNOUNCED. Types that reposition in ordinary play are named in
    `NetVelocityScan.RepositioningTypes` **with the code line that proves it** and excluded from
    the speed verdict. **It REFUSES to arm inside a live session** -- it read-and-clears the same
    latch, so a scan running alongside a real host would eat the markers before
    `CaptureBaseState` saw them, i.e. the diagnostic would reintroduce the exact bug it audits.
  - **THE SCAN CARRIES THE AUDIT BECAUSE `CaptureBaseState`'S COPY IS UNREACHABLE HEADLESSLY, and
    the first cut of the probe was VACUOUS for exactly that reason.** `NoteIfUnmarkedTeleport`
    only runs inside a live HOST SESSION, so a 30000-frame Level-2 soak produces not one line
    whether the markers are intact or deleted (measured). Two detectors, one wording, and the
    scan's is the one `tools/headless/probes/net_velguard.txt` asserts.
  - **`net_velguard.txt` now asserts COVERAGE, which is the leg the marker made possible**:
    `expect-not UNMARKED teleport` over the soak says every reposition site reachable in real play
    is marked -- something no cap and no scan could ever check, since the cap swallowed a missed
    site indistinguishably from a marked one. **Its positive control is not optional**: a build
    with every marker deleted prints no UNMARKED lines either (it just stops announcing), so
    `expect SpiderBoss .* marked=[1-9]` is what makes the absence mean something. Mutation-tested
    by deleting SpiderBoss's three calls -- `marked=0` and
    `UNMARKED teleport suspected: SpiderBoss at 28.5 px/ms` both fire.
  - **TWO RIG TRAPS in the scan, both of which produced confident wrong numbers before being
    fixed.** (a) It keys its position history on `ComponentRemoved`, mirroring `NetIdRegistry` --
    every replicable type is POOLED, so without that it differences across a recycle and reports an
    `EvilBullet` whose declared speed is 0.24 px/ms at a SUSTAINED 14.9. (b) It samples on GAME
    time, not `NowMs`: `eahl --nodraw` runs ~17x real time, so a wall-clock cadence took ~10
    samples out of 5000 frames and read a UFO at 17 px/ms.
  - **The soak has to be LONG (~8 sim-minutes).** The bosses that set the ceiling arrive deep into
    a level; a 5000-frame Level-2 run never reaches MarsBoss and reports UFO 0.758 as the fastest
    thing in the game -- a PASS for the wrong reason, which is why the probe asserts `MarsBoss` is
    in the table as its other positive control.
  - `[net]` gains **`teleports=`** (samples that went out marked) and **`tpUnmarked=`**.
    `teleports` counts REPOSITIONS, so 0 on a level with none is correct and it is not a health
    metric; what makes it worth printing is that it is the only externally visible sign the path
    ran (a marked sample looks, on the wire and on the client, exactly like an entity standing
    still). **`tpUnmarked` IS a 0 bar** -- every nonzero is a type whose puppets dead-reckon at
    teleport speed on the other player's screen.
  - **Sim-measured** (`python tools/sim/net_puppet_drive_sim.py --smoothness`): client peak
    **158 -> 3.1 px/tick** on an 800px reposition with `pupPops` 2 -> 0, and on a 60px one
    (under the snap threshold) maxstep **15.6 -> 3.1** -- i.e. the slide is gone and the motion is
    host-like. **Both legs assert the puppet still ARRIVES**; a marker that HID the reposition
    would be worse than the lurch, and since the pop counter deliberately no longer moves, the
    ARRIVAL rather than the pop is what the negative leg reads.
  - **`eaNetTeleport()` / `eval NetTeleport`** (`Compat/Net/NetTeleportTest.cs`, 25 assertions, a
    leg of `net_selftests.txt`) is the end-to-end suite: a real HOST session's snapshot frames read
    off a `NetWire` (flag set, DECLARED velocity rather than the jump's 13 px/ms difference, and
    the latch spent), then a real CLIENT session's puppet snapping instead of blending. **Every
    positive has the identical jump left UNMARKED beside it**, because the pre-card code also ended
    up in the right PLACE -- "the puppet is at the target" passes on the broken build, so what
    discriminates is the wire's velocity, the blend-vs-snap, and `pupPops`. Menu-only and
    leave-no-trace. It deliberately PRINTS one `UNMARKED teleport suspected: UFO` line (its
    section-1c control), which is why `net_velguard.txt` does not run it.
- **Per-type LOCAL SIMULATION, via the existing `NetDriveExtras` hook -- no wire bytes, no
  protocol change.**
  - **`Lazer` (card 0108d1fc, REPLACED by the sent rates below -- card c1a38ef9):** aim, length and
    lead are state extras, so a frozen beam only moved on its round-robin turn -- a beam growing at
    0.4 px/ms jumped in ~24px steps. The first cut ESTIMATED the three rates by differencing
    consecutive `NetApplyBeam` calls (the `CaptureBaseState` observed-velocity idiom, one layer
    down) and `NetDriveExtras` extrapolated on real dt. **That estimator is gone**; the rates are
    on the wire now. Its header argued a wired rate would be WRONG because `len` is scaled by
    `Settings.DifficultyModifier`, which is not equal on the two peers -- the answer is to scale at
    SEND time, which is what ships. The rest of the design (the 250 ms cap, the recycle reset) is
    unchanged and is described below.
  - **`PlasmaBall` (card 435db27f, "the final boss's electricity balls"):** `BrainBoss.Update`
    spawns these, and both crackle angles only advance in `Update`, so a puppet was a STILL IMAGE.
    `NetDriveExtras` runs the +-PI/2 rad/s counter-spin and the re-roll locally. The angles were
    already per-instance random and never matched across peers, so local is exactly as correct as
    the host's copy. **Private `Random`, never `RandomHelper.Random`** -- the `Quad`/`ShipConnector`
    rule; this runs once per puppet per tick and would otherwise pull the shared generator out from
    under every other consumer on that peer.
- **Cost:** `NetPuppets.Drive`'s per-tick body is unchanged bar a field load where a const was
  (the frame branch lives in `ApplySnapshotState`, which runs per snapshot ENTRY, not per tick, and
  now does strictly less work for most types). `eaNetPuppetBench(128, 2000)` reads 8.6-9.7 us/call
  (~67 ns/puppet) across runs in one process, so any delta here is below the instrument's own
  resolution -- which is the honest claim, not a measured 0.

## ANCHORED MOTION -- the motion model on the wire (card c1a38ef9, protocol v11)

The second half of the smoothness family and its REPLACEMENT for two of the fixes above: carry the
motion MODEL instead of estimating it from position differences. Chartered by the cheap-protocol
ruling; the two shipped estimators it replaces were both bent around avoiding wire bytes.

- **THE "~100x HOST" JERK FLOOR IN `CorrectionWindowFor`'s HEADER WAS MOSTLY A SPAWN TRANSIENT,
  and correcting that came first.** The `--smoothness` rig gave a new puppet `vel = (0,0)` for its
  first snapshot turn, so it stood still for a whole turn and then ate one large correction --
  which dominated every jerk figure the mode printed. A real `EvSpawn` carries
  `CaptureBaseState`'s velocity, and on a first observation that is the entity's DECLARED
  `NetSpeedVector`, so a puppet is born already moving. With the rig's spawn fixed (and the host's
  first-observation fallback modelled as `declared` rather than zero), the same runs read:

  | truth shape | N | pre-fix (the quoted figure) | steady state |
  |---|---|---|---|
  | linear (asteroid) | 16 | 0.0843 | **0.0000** |
  | linear | 128 | 0.0884 | **0.0011** |
  | swivel (wasp) | 16 | 0.0886 | 0.0034 |
  | swivel | 128 | 0.0904 | **0.0174** |

  Host control 0.00084. So the real steady-state penalty is ~4x (small world) to ~21x (big world)
  for a PERIODIC mover and **essentially zero for a LINEAR one** -- not the ~100x the header
  implied. **Fix a rig's spawn model before quoting anchored-vs-not rows**, or the anchor wins on
  a rig artifact rather than on the defect.
- **The seam is a DE-TRENDED baseline plus a local path offset**, two members on `INetEntity`
  (the `NetSpinPerMs` / `NetFrameLocal` idiom):
  - **`NetPathAnchored`** (default false) makes the HOST send the entity's DECLARED
    `NetSpeedVector` instead of the finite difference. A difference taken over a whole snapshot
    turn measures a CHORD of any periodic component, so for the wasp it is wrong by construction
    and worse the longer the turn.
  - **`NetPathOffset`** (default zero) is the type's zero-mean periodic offset from that baseline,
    evaluated from its OWN locally-running state -- which is possible because the driver already
    ticks a frozen puppet's `timers`. `NetPuppets.Drive` adds its DELTA across the tick, so a
    constant offset contributes nothing and a puppet adopted mid-cycle does not jump.
  - **ONLY OVERRIDE IT WHERE THE DECLARED VELOCITY IS HONEST.** `Speed`/`Direction` lie for every
    type that writes `Position` directly -- the very reason the observed baseline exists -- so a
    scripted position curve (a UFO) must NOT take this path or it dead-reckons at a stale
    `SpeedVector`. The two users both move by `Speed`/`Direction` and nothing else.
    **The ban STANDS, and card 76ec8bdb is not an exception to it**: a scripted type still may not
    anchor, it announces a velocity that does not come from `Speed`/`Direction` at all
    (`TryGetNetScriptedVelocity`). Anchored outranks scripted where a type somehow claims both, and
    no type does -- the SCRIPTED MOTION section has the census.
- **Velocity is EASED, not assigned, for an anchored puppet** -- over the SAME
  `CorrectionWindowFor(live)` the position error already drains over, so it inherits the cadence
  and needs no second constant. That is the whole asteroid fix: a declared velocity is a STEP
  function (constant for a rock's life until a bullet tweaks its heading), and assigning it puts
  the whole step into one tick. `PuppetInfo.VelTarget`/`VelEaseMsLeft`. The ease takes a fraction
  of what REMAINS, so it lands exactly on the target whatever the dt pattern was.
- **CORRECTIONS ARE STILL ALWAYS APPLIED -- there is deliberately NO divergence deadband**, which
  is a narrowing of the card's "corrections only on genuine divergence" wording, taken on the
  user's nudge ruling. A deadband lets error accumulate to the edge of the band and then move;
  what ships is corrections that are tiny and eased. `SnapThresholdPx` is untouched.
- **The two halves, and what each type sends:**
  - **`Lazer`** -- state extras `6 -> 12` bytes: `[angle:2][len:2][lead:2]` gains
    `[lenRate:2][leadRate:2][angleRate:2]` as scaled i16. `NetLenRate`/`NetLeadRate` read Update's
    own expressions INCLUDING its gates (`stopped` zeroes the length rate, `freed` starts the lead
    one), so a client stops extending the moment the host does rather than a turn later, and the
    host multiplies by `DifficultyModifier` before sending. `NetAngleRate` is the sweeper's
    constant, handed over by `Boss.LazerSweepRadPerMs` via `Lazer.SetSweepRate` -- exact, where an
    estimator could only ever approach it. The rates are ASSIGNED, not eased: they are step
    functions, and easing across a step would leave a COLLIDABLE beam growing after the host's had
    stopped.
  - **`FlyingSpider`** (the foreground wasp) -- spawn extras `1 -> 5`
    (`[flags][startHeight:2][swivelPhase:2]`) and state extras `0 -> 4`
    (`[amplitude:2][swivelPhase:2]`). `startheight` and the phase are ROLLED by `Initialize`, so
    both must be pinned; the amplitude (`50 * DifficultyModifier`) and the phase both DRIFT, so
    both are re-sent every turn and eased. The swivel DURATION needs nothing -- it is 2700/4000
    keyed off the `isbackground` bit already in the flags.
  - **`Asteroid`** takes the flag and NOTHING ELSE -- no spawn anchor, no offset, no new bytes. Its
    steady linear path is already dead-reckoned exactly (a finite difference of a straight line IS
    that line; measured 0.000-0.001 jerk above), so an anchor has nothing to improve. The kink at a
    shot-induced heading change is the whole defect and the velocity easing is what removes it.
- **THE SPAWN ANCHOR RIDES THE PRE-`Add` SEAM, inverted from the usual rule.** `Initialize` WRITES
  both anchored quantities and `ComponentBin.Add` runs it synchronously, so a value written after
  the Add is the one that survives and one written before it is clobbered.
  `FlyingSpider.NetForceAnchor` stores it and `Initialize` applies it at its very end -- exactly
  the `NetForceColor` shape. Both are cleared in `Setup`, the per-spawn reset seam: `FlyingSpider`
  and `Lazer` are both POOLED, so a recycled puppet would otherwise spend the previous life's
  half-finished phase correction (or integrate the previous beam's rates) against the new one, on
  a collidable hitbox.
- **The drifting parameters are spent in `NetDriveExtras`, NOT in `ApplyStateExtra`, and that is
  load-bearing.** The driver DIFFERENCES `NetPathOffset` across the tick, so anything applied in
  one step moves the puppet by that much in one frame -- the very artefact the card removes -- and
  it would land after that turn's position error was measured, so the correction blend could not
  absorb it either. The phase correction walks the WRAPPED SHORTEST ARC; a naive difference swings
  a wasp almost a whole 2.7 s period the wrong way whenever the pair straddles the 1 -> 0 wrap.
- **`NetSession.ResolveBaseVelocity` is a PURE decision, split out of `CaptureBaseState`** (the
  `OwnsSlotCore` precedent) -- and the split is not tidiness. A mutation dropping its anchored
  branch passed the whole probe suite AND every other leg of `eaNetMotion` until it existed;
  a host that goes back to differentiating an anchored entity makes the client count the periodic
  part TWICE. The teleport guard (card 8dabe812) lives inside it unchanged.
- **The two scales are NOT interchangeable** (`NetProtocol.RatePxPerMsScale` 1000,
  `RateRadPerMsScale` 10000). The miniboss sweep is -0.0007 rad/ms: 7 wire units at the rad scale,
  0.7 at the px one, i.e. sharing a scale leaves every swept beam turning 43% wrong, silently. The
  field SATURATES rather than wrapping -- a wrapping cast flips the SIGN, turning a sweep into a
  counter-sweep.
- **VERIFY IN TWO PLACES, and they answer different questions.**
  **`eaNetMotion()` / `tools/headless/probes/net_motion.txt`** (33 assertions,
  `Compat/Net/NetMotionTest.cs`) asserts the mechanism is WIRED AND EXACT: the predicate with a
  UFO as the control, both descriptors' real byte layouts, a driven puppet growing/sweeping/bobbing
  at the SENT parameters, the ease being a nudge, and the host's velocity decision -- each with the
  PRE-CARD block beside it as its control. Mutation-tested six ways, each isolated (the sixth is
  deliberately non-disjoint -- see below).
  **Section 2's spawn-anchor negative control asserts over FOUR independent puppets** (card
  c41a89a2): host and puppet both roll `RandomHelper.RandomNextFloat(0, 475)` for the entry
  height, so a single pair agrees inside the 1.5px band ~0.6% of the time -- a spurious FAILURE,
  seen once in-suite. The regression it guards (`FlyingSpiderDescriptor.CreatePuppet` losing its
  `len >= SpawnAnchorBytes` gate) is deterministic across all four, so requiring only ONE to keep
  its own roll keeps the band and every bit of sensitivity while the coincidence collapses to
  0.006^4. The leg beside it is a SENTINEL on that independence, not on the wire.
  **`python tools/sim/net_puppet_drive_sim.py --smoothness`** asserts it is WORTH HAVING: anchored
  rows at N=16/32/64/128 (0.0076 / 0.0049 / 0.0025 / 0.0014 against the estimator's flat ~0.013,
  i.e. within 1.6x of the host control at the biggest world) plus a SHOT-NUDGE scenario with
  instant velocity assignment as the refuted control.
  **The shot nudge has to be read on a VECTOR jerk** (`_stddev_of_vector_deltas`) -- the scalar
  metric differences |step|, so an asteroid nudged 11 degrees keeps almost exactly its old speed
  and reads as nothing at all (measured 0.01682 vs 0.01681 between the two policies, against
  0.04010 vs 0.02445 on the vector one). The sim ASSERTS that the scalar metric stays blind, so
  the vector one cannot quietly be simplified away.
  The wire FIELD itself is in `eaNetWire.test` section 5 (and so runs under `logic_probe`); the
  entity-level legs cannot be, because constructing an entity needs a `Game`.
- **`eaNetMotion` is deliberately ABSENT from `net_selftests.txt`** -- it has its own probe, which
  carries this card's mutation matrix, so listing it in both would run it twice for nothing. The
  `eaNetDeathFx` precedent.

## SCRIPTED MOTION -- the bosses announce their own velocity (card 76ec8bdb, no protocol change)

The third and last member of the smoothness family, and the one the two above kept pointing at.
A type that moves by writing `Position` directly never assigns `Speed`/`Direction`, so **both**
velocities the layer already had are wrong for it -- and the fix is neither of them.

- **THE TWO FALLBACKS AND WHY EACH FAILS.** Its declared `NetSpeedVector` is a flat **ZERO**, which
  is what a marked teleport falls back to (card e79bb994) -- so a parked SpiderBoss went out at
  velocity (0,0). And the FINITE DIFFERENCE is a whole snapshot turn **LATE**: a difference
  reported at turn T describes `[T-turn, T]` while the client dead-reckons over `[T, T+turn]`, so
  every phase change of a scripted set-piece is driven on the PREVIOUS phase's velocity for up to
  a turn -- `SnapshotTurnMs` is `live * 60 / 16`, i.e. 60 ms at 16 live entities, **480 ms at 128**
  and ~1.2 s at 320.
- **THE SECOND IS THE BIGGER HALF, AND THE CARD'S TITLE UNDERSTATES IT.** The card is filed as
  "a marked teleport freezes the puppet", and at SpiderBoss's three parks the zero is momentarily
  CORRECT -- each reposition immediately starts `waittimer` for the 1000 ms "Danger!" hold, so the
  boss really is stationary. What actually costs the joiner is the pause -> sweep boundary right
  after it: the boss steps from 0 to `0.78 * DifficultyModifier` px/ms and the puppet drives the
  old value for a turn. **Measured** (`--smoothness`, boss-fly-by shape, mean puppet lag / pops
  past `SnapThresholdPx`): N=16 10.1 px / 0, N=64 22.7 px / 20, **N=128 51.7 px / 33**.
- **THE FIX IS A THIRD SOURCE OF TRUTH: `AlienDrawableGameComponent.TryGetNetScriptedVelocity`,**
  the type's own answer to "what will Update do next tick", used on EVERY turn rather than only
  where the fallbacks would have caught it. It is FORWARD-looking where a difference is
  backward-looking, which is the direction dead reckoning needs.
  **`NetSession.ResolveBaseVelocity` gains a fourth branch**, ranked below `anchored` and above
  both fallbacks. Any per-peer factor (`DifficultyModifier`, the oracle's scroll) is applied
  HOST-SIDE so the wire carries real px/ms -- the Lazer sent-rate rule.
- **NO WIRE BYTES AND NO PROTOCOL BUMP: it is a better number in `NetBaseState.Vel`, which already
  ships.** **The CLIENT is untouched too** -- the
  scripted velocity is ASSIGNED, not eased, exactly as the Lazer rates are: a phase velocity is a
  genuine step function, and easing across it would delay a collidable sweep's start further.
- **THIS IS WHAT `NetPathAnchored` COULD NOT DO, so read the two together.** Anchoring makes the
  host send the DECLARED vector, and for these types that vector is the thing that lies -- card
  c1a38ef9's header forbids them the seam for exactly that reason, and the ban stands. This adds a
  vector that does not come from `Speed`/`Direction` at all. **Anchored still outranks scripted**
  and no type may hold both (a type claiming both is a contradiction, not a blend); the suite
  asserts nobody does, over the whole registry.
- **ONE OVERRIDE SHIPS, and every additional one is a decision to re-make.** `SpiderBoss` is the
  card's named type and the only marked-teleport type whose declared vector is zero -- audited:
  `Braineroid`, `EvilSkull` and `Ball` all assign `Speed`/`Direction`, so their marked repositions
  already carried an honest velocity. A wrong value here is dead-reckoned onto a COLLIDABLE puppet
  on the other player's screen, which is this family's recurring failure mode, so the census in
  section 1 of the suite asserts the list is exactly one name long.
- **ITS CONTRACT IS NOT `TryGetAiSweptPath`'S, and wiring one to the other is the mistake to
  avoid.** The AI seam deliberately announces the velocity a FROZEN boss is about to move at,
  because leaving the lane during the warning is the whole point of the warning. Here a paused
  entity is genuinely not moving and must report ZERO, or the puppet slides out of its park a
  second before the host does. The two disagree on exactly one case, on purpose.
- **THE UNMARKED-TELEPORT SAFETY NET STAYS ARMED FOR SCRIPTED TYPES -- deliberately ASYMMETRIC
  with the anchored branch, which skips it.** Both send an announced rather than an observed
  velocity, so neither is fit to judge; the difference is that `SpiderBoss` holds three of the
  game's four reposition sites and is the type most likely to grow a fourth, where neither
  anchored type repositions at all. `CaptureBaseState` therefore recomputes the raw finite
  difference purely to hand to `NoteIfUnmarkedTeleport`, and it never reaches the wire. Only
  reached on an UNMARKED turn, where that difference describes genuine motion.
- **THE RESIDUAL, STATED AND ASSERTED: this fixes WHAT the puppet dead-reckons with, never WHEN it
  hears about a phase change.** That is still up to one snapshot turn, so at N=128 the MEAN lag
  falls 51.7 -> 35.0 px and the pops 33 -> 20, while the PEAK lag does not improve at all
  (352.7 -> 356.0 px -- the two are inside the noise of where a boundary happens to fall in a
  turn, and the peak is set by that latency rather than by the velocity's accuracy). Closing the
  timing half means a per-phase model on the wire -- phase
  id plus elapsed ms, so the client can advance the hold and start the sweep on schedule -- which
  was designed, costed and DECLINED on this card: it buys a second-order gain in exchange for wire
  bytes on the lane card f5cf7a5c just guarded, a client-side mirror of a script the host owns (a
  new divergence surface in a distributed-authority design), and a compat split. The sim ASSERTS
  the peak barely moves, so if that leg ever fails someone has closed the gap and this text is
  stale.
- **DO NOT READ THE JERK FIGURE HERE.** It REWARDS a puppet that ignores the choreography: the
  pre-card column reads a vector jerk of ~1.02 against the host's own ~1.00 while sitting over
  350 px behind, because a velocity that never steps is beautifully smooth and simply wrong. Read
  the ERROR and the POPS. (Same trap as reading the AI bench's `turn` with no survival column.)
- **VERIFY IN THREE PLACES, and they answer different questions.**
  **`eaNetScriptedMotion()` / `tools/headless/probes/net_scripted_motion.txt`** (35 assertions,
  `Compat/Net/NetScriptedMotionTest.cs`) asserts the mechanism is WIRED AND EXACT.
  **`logic_probe`'s `ProbeScriptedVelocity`** pins the pure RANKING with no Game at all.
  **`python tools/sim/net_puppet_drive_sim.py --smoothness`** asserts it is WORTH HAVING, with
  the pre-card differencing policy as the refuted control on identical host truth.
  - **SECTION 2 OF THE SUITE IS THE ONE THAT CANNOT BE FAKED, and it is the shape to copy for any
    future override.** The override transcribes `SpiderBoss.Update`'s own switch, so an
    expectation TABLE written from the same reading would prove only that two copies of one
    misreading agree. Instead the REAL `Update` is driven through a full choreography cycle and
    its ACTUAL per-tick displacement is finite-differenced to produce the expected value -- ground
    truth from the game, not from a reader. The pause reading zero falls out of it for free, and
    it doubles as a standing tripwire on the choreography. Two classes of tick are excluded on
    EVIDENCE rather than by being listed: a marked teleport (detected by the entity's own latch)
    and a phase boundary (detected BOTH by the state byte changing and by the announced velocity
    stepping -- neither alone is enough, since `jump` -> `flyup` announces the same vector on both
    sides while moving zero, and the climb boundary INSIDE `jump` changes no state). Both
    exclusions are then asserted to be rare and non-empty.
  - **GOTCHA -- "sweeping" is a FRACTION of the boss's own top speed, never a px/ms literal.**
    `moveSpeed` is `0.78 * Settings.DifficultyModifier`, which is 0.27 on Easy and ramps within a
    fight, so a hard-coded threshold silently means "did it move at all" on one tier and "did it
    sweep" on another. An early cut of this suite reported five spurious failures exactly that way.
  - Mutation-tested five ways, each failing a different leg; the matrix is in the probe's header.
    The pre-card build (the override answering false) fails 12.

## ROTATION FIDELITY -- two reports, two unrelated causes (cards d6645119 / 566474ae)

The fourth member of the smoothness family, and the first about ANGLE rather than position. Two
reports that read as one bug ("the joining player sees it rotating wrong") and share no code:
the Level-1 mothership's swept beam, and the junkboss rocks. **Neither needed a wire change** --
protocol stayed at **v21** across both -- which is worth recording, because the first design for
the second one spent a byte, a new `INetEntity` member and a version bump before a measurement
took all three back.

- **THE MOTHERSHIP BEAM: the host stopped sweeping and never said so** (card d6645119). `Boss`
  keeps at most three beams in `lazors` and sweeps every beam **still in that list**; firing a
  third evicts the oldest with `lazors[0].Free(); lazors.RemoveAt(0)`, which stops `ChangeAim`
  reaching it. `Lazer.NetAngleRate` returned `netSweepRadPerMs` ungated -- a constant handed over
  once by the sweeper and cleared only in the two `Setup*` entry points -- so the beam went on
  DECLARING a sweep it was no longer performing. The client integrated it, the next snapshot's
  aim snapped it back, and it repeated every turn: the reporter's "rotate, then get placed back,
  rotate some more, get placed back, etc.".
  - **THE FIX IS THE GATE THE FILE'S OWN IDIOM ALREADY ASKS FOR**: `NetAngleRate => freed ? 0f :
    netSweepRadPerMs`, beside `NetLenRate`'s `stopped` gate and `NetLeadRate`'s `freed` one. The
    card asks for "some stop-rotating event from the host" and this IS it -- the rate has ridden
    the state extras since v14 and was simply lying.
  - **`freed` IS COMPLETE BY ENUMERATION, not by resemblance**, and that is what makes a one-line
    change safe. `ChangeAim` and `SetSweepRate` have exactly one caller in the tree (`Boss`), which
    stops sweeping a beam at exactly two sites -- the eviction and its own `OnComponentRemoved` --
    each calling `Free()` in the SAME statement pair that drops the beam from `lazors`. `SweepUFO`
    fires through `Setup` but never sweeps, so it honestly reports 0 either side.
  - **The timing independently reproduces the report.** `lazertimer` is 10000 ms once and then 800
    ms repeating, so the first eviction lands **1.6 s after the first shot** ("after some
    rotation"), and an abandoned beam then lives ~2.9 s / `DifficultyModifier` more -- `lead` only
    catches `len` once the beam is `stopped`, so it is the 1200px lead cap that ends it.
  - **RESIDUAL, stated: the client hears one snapshot turn late**, so it over-rotates by
    `rate x SnapshotTurnMs` ONCE (~2.4 deg at a 60 ms turn, ~19 deg at 480) and is corrected once,
    where the defect was an endless sawtooth. **Do NOT close that by routing an immediate beat
    through `NetFxKind`/`NetPlayFx`** -- that contract is draw and audio only and idempotent, and
    this moves a COLLIDABLE hitbox's extrapolation. Same residual card 76ec8bdb states and declines.
- **THE JUNKBOSS ROCKS: `Ball` had no local-rotation seam at all** (card 566474ae). They are
  `Ball`, not `Asteroid` -- a distinction the card's own "should use same system as the asteroids
  earlier in the mission" hides. `Asteroid` overrides `NetSpinPerMs`; `Ball` overrode nothing, so a
  frozen puppet stepped to the replicated angle once per turn, up to 13.7 degrees every 240 ms.
  `Ball.NetSpinPerMs => rotationspeed` is the whole fix, unconditional, identical to Asteroid's.
- **THE READING THAT COST THE FIRST DESIGN, and the reason section 6a of the suite exists.**
  `Ball` looked like it needed more than Asteroid's one-liner, because `BallState.connected`
  appears to LOCK the angle: it picks the sign of its step to chase the bearing to its owner the
  short way. **It does not lock.** Both branches step by exactly `rotationspeed * dt` and only the
  SIGN is conditional -- a bang-bang controller with a fixed step, which can dither about its
  target or lag behind it but can never settle. **Measured off the real `Update`: a connected ball
  turns at 1.00x its own free-spin rate, 16 of 16 balls across two runs, with 5-124 direction
  reversals per 10 s.**
  - **That measurement is what makes the seam unconditional and the wire unnecessary.** A puppet
    free-spinning on its own roll has the right angular SPEED in all four states; only the phase
    and the occasional reversal differ, on a tumble that reaches nothing but Draw (`Ball`'s
    `CollisionType` is a `CollisionSimpleCircle`, so the decorative-rotation argument is STRONGER
    here than in `Asteroid`, whose own comment makes it).
  - **THE DESIGN IT REPLACED IS RECORDED BECAUSE IT WILL BE RE-PROPOSED.** The first cut had the
    host declare per turn whether the ball was free-spinning (one flags byte, `BallDescriptor`'s
    first state extras, protocol v22) and a connected puppet take the replicated angle. Measured
    against the truth above it is **worse than doing nothing for the balls that matter**: the low
    end of that reversal spread is a ball turning continuously for ~2 s, which at 0.001 rad/ms is
    over 100 degrees, still stepped 13.7 degrees per turn. It would have fixed the rain-in and left
    the fight. A second cut added a `NetRotationLocal` seam on top, to stop the rate falling to
    zero from ALSO re-arming the driver's per-turn assignment (which would snap a puppet by up to
    PI at the moment it joins the boss) -- that seam is unnecessary once the rate never falls to
    zero, and was reverted with the rest.
  - **Sending the RATE was rejected too, on quantisation**: `rotationspeed` tops out at 0.001
    rad/ms, i.e. TEN units at `RateRadPerMsScale`, so a slow ball rounds to "not spinning at all".
    A finer scale would be a third rate scale to keep straight, and would still not make the two
    peers agree on PHASE.
- **`NetJipDump.LocalSeams` needed nothing**, and that is a consequence worth stating rather than a
  coincidence: it keys `rot` off `NetSpinPerMs != 0f`, which under an unconditional override is
  true in every state, so `net_jip_sync.py` skips ball rotation always and cannot false-fail on it.
  The reverted design would have made that label flap between the two peers.
- **VERIFIED IN ONE PLACE, deliberately.** `eaNetMotion()` /
  `tools/headless/probes/net_motion.txt` grew section 4's released-beam legs and a new section 6
  (33 -> 56 assertions). **The sawtooth is measured there, not in the sim**: 0.1680 rad of drift
  every turn pre-card against exactly 0 after. `tools/sim/net_puppet_drive_sim.py --smoothness`
  was NOT extended -- its `SmoothPuppet` models 2D POSITION, and bolting an angular metric family
  onto a Python model of the driver would be worse evidence than section 4's leg, which drives the
  real driver through the real descriptor. That omission is deliberate; do not "fix" it.
  - **Section 6a is not about the net layer at all** and is the leg to keep if any are ever cut:
    it drives a real `Ball` against a real `JunkBoss` to a genuinely `connected` state (the
    precondition is asserted, not waited out -- card af4c3694) and measures the turn rate the game
    actually produces. Its free-spin baseline is OBSERVED by differencing `rotation`, not read off
    `NetSpinPerMs`, so it keeps meaning something in a build where the seam is wrong.
  - Mutation-tested three ways beyond the existing matrix, each tripping its OWN named `expect`:
    the Lazer gate (5 legs), `Ball.NetSpinPerMs -> 0` (4), and `connected` no longer flipping its
    sign (1 -- the reversal leg only, since the rate half still reads 1.00x, which is why both
    halves are asserted).
  - **The probe's named `expect` lines are anchored on `PASS`**, and that is load-bearing: the
    suite prints `PASS <text>` and `FAIL <text>`, so a bare `expect <text>` matches the FAILING
    line too and asserts only that the leg ran. Without the anchor all four still matched under a
    mutation that reddened them, and only the tally caught it.

## THE SAME ROCKS, A 20%-SMALL HITBOX (card 1210e14e, protocol v22)

Found while working the rotation cards above and deliberately left out of both. Same entity, same joiner, unrelated defect -- and unlike the rotation ones this one is a GAMEPLAY divergence, not a cosmetic one.

- **THE DEFECT.** `Ball.CollisionType` picks its circle radius from a per-state factor: `connected` 1.0, `startup` / `attracted` / `freed` 0.8. On the host a latched rock is `connected`. On the JOINER it never is -- a puppet is frozen so its `Update` never runs and `state` never leaves `Initialize`'s `startup`, and `CheckOwner` (which runs at the top of `CollidesWith`) flips the null owner `BallDescriptor.CreatePuppet` gave it straight to `freed`. Both are 0.8, so the junkboss's whole body was hit-tested 20% small on p2's screen.
- **FOUR LOCAL READERS, all of them on the joiner, and the direction is not uniform** -- which is why the report reads as "my shots miss" rather than as one clean symptom. `Bullet.CollidesWith` twice (the bullet dies on the rock; and via `IsConnected()`, whether the hit SUSTAINS THE COMBO -- so p2's combo silently dropped on rocks p1's kept), `PlayerShip.CollidesWith` (p2's ship survived a band p1's screen called a collision -- an *advantage*), and `Option.CollidesWith`.
- **THE FIX MOVES NO AUTHORITY, and that is worth stating because the symptom sounds like lost damage.** It is not: `Bullet` is not in `NetTypeRegistry`, the host re-fires the remote ship's shots locally, and the host's copy chips its own real `Ball` at 1.0 throughout. Only the joiner's LOCAL reads were wrong. One bit (`BallDescriptor`'s first state extras, `[flags:1]`) carries the host's `connected` answer; the host is untouched.
- **THE BIT MAY NOT BE WRITTEN INTO `Ball.state`, AND THE REASON IS NOT THE OBVIOUS ONE.** The card predicted an NRE -- a puppet re-entering `case BallState.connected:` and calling `owner.RemoveChild()` on its null owner. **Measured, that is wrong: the naive design is silently USELESS, not crashy.** `CheckOwner` runs FIRST in `CollidesWith` and flips a null-owner ball back to `freed` before the switch dispatches, so nothing throws, nothing blinks -- and the radius reverts on the very first collision test, staying small until the next snapshot turn (60 ms to ~1.2 s). The conclusion is unchanged (do not replicate into `state`); only the reason is. So the two questions are SPLIT: `ConnectedForCollision` (a replicated field behind a has-it-arrived latch) answers "which RADIUS do I use", `state` still answers "which gameplay ARM do I run", and a puppet enters no arm at all.
  - **The latch is what keeps the HOST bit-identical**: nothing calls `NetSetConnected` there, so it falls through to `state == connected` exactly as before. A puppet reads 0.8 until its first turn, which is the right answer -- a just-spawned ball really is in `startup`.
  - **The two remaining raw `state == connected` reads inside `CollidesWith` are deliberate and commented in place** (the connected balls' mutual push-out, and the attract->connect latch-on). Both MOVE things or call `owner.AddChild()`, i.e. both are gameplay, and both are unreachable on a joiner for the same `CheckOwner` reason. Do not "make them consistent" with `ConnectedForCollision`.
- **BALL WAS THE LAST ONE -- the class is closed, checked rather than assumed.** All 40 `CollisionType` overrides were enumerated; exactly two read a state enum a frozen puppet cannot reach, and `SpiderBoss` already replicates its `NetState`/`NetAnimIndex`/`NetAnimFrame`. (`SpiderBoss` can take the naive route precisely because its `CollidesWith` does NOT switch on `state`.) `EvilSkull.Fading`, the other frozen-state predicate a hit-test reads, is likewise already carried by `NetFadePhase` + `NetTickTimers`.
- **The state extras ride EVERY snapshot turn, not a change edge** (`SendWorldSnapshot` encodes unconditionally per round-robin entry), which is what makes a ball that latches on mid-fight and a join-in-progress puppet both correct on their next turn rather than only on the next transition.
- **Verified in `eaNetMotion()` section 7** (`tools/headless/probes/net_motion.txt`, 56 -> 71 assertions) -- the same suite as the rotation half, one eahl boot, deliberately. 7a OBSERVES the 1.25x off a real `Ball` driven to a real `connected` state (6a's doctrine: never read the ratio off the constants the fix uses); 7b then requires a puppet to reach that same figure through the real descriptor and the real apply path, come back DOWN on a clear bit, and take no gameplay arm on a hit. Mutation-tested three ways, each tripping its own named `expect`: the fix reverted (4 legs, section 6 untouched), the encoder stuck at 0 (the host-encode leg alone), and the wrong `state` design (the radius-after-a-hit leg alone -- which is where the "useless, not crashy" measurement above comes from).

## LEVEL-3 WALLS -- derived scale, and the collision/draw coincidence (cards 4392bd30 / 80749dc4)

Two reports -- "lvl3 walls go out of sync" and "walls stutter, and I hit them before I touch
them" -- with ONE root cause, and it is not in the wall replication design at all. **The design
was already what the second card proposed**: a `Walls` GameEvent spawns ONE `Wall` entity per
section, `WallDescriptor` sends the grid VARIATION as a spawn extra, `CreatePuppet` rebuilds the
identical grid locally, and the scroll is dead-reckoned from the base velocity. Nothing was ever
sent per block or per frame.

- **THE ROOT CAUSE IS `NetBaseState.Scale`'s PRECISION, and the lesson generalises past walls.**
  It rides the wire as a u16 at 1/256 and the cast TRUNCATES, so the error is up to 1/256 in
  ABSOLUTE terms **whatever the value** -- ~0.2% for a sprite drawn near scale 1 (invisible, which
  is why it went unnoticed for the whole replicable set) and catastrophic for a type whose scale is
  SMALL. `Wall.Setup` computes `800 / (LogicalWidth * gridWidth)` off the 1248px `756-v1` sheet:

  | variation | grid width | true scale | wire scale | error |
  |---|---|---|---|---|
  | 0 (Level 3) | 12 | 0.053419 | 13/256 = 0.050781 | **4.94%** |
  | 1 / 2 | 7 | 0.091575 | 23/256 = 0.089844 | 1.89% |
  | 3 | 9 | 0.071225 | 18/256 = 0.070312 | 1.28% |
  | 4 | 3 | 0.213675 | 54/256 = 0.210938 | 1.28% |

  `Wall.Draw` sizes every block as `LogicalWidth/Height * scale`, so the joiner drew **63.38px rows
  against the host's 66.67px** -- and variation 0 is **122 rows tall**, so the two peers were
  **402px apart** by the bottom of the section. That is the "out of sync" screenshot: not a lag, a
  vertically COMPRESSED grid showing different rows.
- **`AlienDrawableGameComponent.NetScaleLocal` (default FALSE) is the fix, the `NetFrameLocal`
  idiom one field over.** A true answer means the type DERIVES its scale from something already
  replicated, so `NetPuppets` keeps what its own `CreatePuppet`/`Setup` computed and never applies
  the wire's copy -- for `Wall` that is the grid variation, already in the spawn extras, so the
  client computes the byte-for-byte number the host did. **Only `Wall` overrides it.** A type whose
  scale is ROLLED, tweened or driven by host-side state must keep taking the replicated value, or
  the two peers simply draw it at different sizes; a UFO is the standing control.
  - **It is skipped in THREE places, and the third is the subtle one**: `ApplySnapshotState`'s
    per-turn write, `OnSpawn`'s initial `TargetScale`, and the SELF-HEAL REBUILD's pose carry-over.
    A self-healed puppet is built from DEFAULT spawn extras (card de4d5d65), so its derived scale is
    the wrong grid's -- carrying it onto the `EvSpawn` rebuild would defeat the whole thing.
- **THE SECOND SYMPTOM IS A JOINER-LOCAL COLLISION/DRAW MISMATCH -- NOT host-side authority.**
  Worth stating flatly because the card asked and the wrong answer is the intuitive one:
  `PlayerShip.CollidesWith` refuses damage to a `ControlDevice.Remote` puppet outright ("you never
  die to something you dodged on your screen"), and a joiner's bullets are never replicated. Both
  collide against the joiner's OWN wall puppet, on the joiner's own screen. Interpolation lag is
  real but SYMMETRIC -- the joiner's whole world is one-way-latency behind, its own ship included --
  so it cannot produce an asymmetry between hitting and seeing.
  What could, and did: **`CollisionLevelMap` sized its tile as the literal `800/width`**, which
  equals the drawn block size ONLY because `Setup` derives `scale` from that same expression -- an
  agreement by coincidence of two formulas in different files. Once the wire changed `scale`, the
  collision rows reached **3.29px further DOWN the screen per row** than the towers did: ~33px by
  row 10, ~100px by row 30, worsening deeper into a section, which is exactly "I hit walls way
  before I actually do" and "my bullets disappear before hitting". The grid now takes its tile size
  FROM THE WALL (re-pushed beside the offset every `CollisionType` read), so the two cannot drift
  again. **Offline that is bit-for-bit neutral** -- `LogicalWidth*scale == 800/width` in float32 at
  every shipped width, asserted rather than assumed.
- **`Wall.NetPathAnchored => true` is the third change and belongs to the STUTTER half.** A wall
  moves by `Speed`/`Direction` and nothing else (`Setup` and `Update` both assign them; `ADC.Update`
  moves by exactly those), so its declared velocity is honest and it meets the anchored-motion rule.
  The host now sends the real scroll speed instead of differencing two positions across a snapshot
  turn on the real clock -- which carried the host's frame pacing, and made a level `speedup` arrive
  a whole turn late. A speed change is now a step the velocity ease absorbs, which is the
  "resync on a scroll-speed change" the card asked for, with no new wire bytes.
- **LATENCY FAST-FORWARD WAS PROPOSED BY THE CARD AND DECLINED (user ruling via the overseer).**
  The joiner's ENTIRE world -- every enemy, every wall -- is uniformly one-way-latency behind, and
  their own ship interacts with that world consistently. Pushing only the walls forward would make
  them inconsistent with the enemies beside them and put collidable geometry AHEAD of where the
  host has it. Two screens not matching side by side is not a gameplay defect; the felt problems
  were the three above.
- **NO PROTOCOL CHANGE and no version bump.** Every change here is a host-side decision about what
  goes in existing base-state fields, or a client-side decision about what to do with them.
- **CLOSED by card `f5cf7a5c` (protocol v19) -- the snapshot packet carries a seq now, and the
  wire's scale is 32x finer.** Both halves of what this section left open; see the
  SNAPSHOT STALENESS section below. Note its verdict on the second half: the card asked to WIDEN
  `NetBaseState.Scale`, the census found nobody left who needed the bytes, and what shipped is a
  precision RAISE inside the same u16. **`Wall.NetScaleLocal` STAYS** -- deriving the scale from
  the replicated grid variation is the right answer at any precision, and section 1 of
  `NetWallTest` now measures its >1% claim against a transcription of the old encoder rather than
  the live one.
- **Verify with `eaNetWalls()` / `eval NetWalls`** (`Compat/Net/NetWallTest.cs`, 28 assertions;
  `tools/headless/probes/net_walls.txt`). MENU-only and leave-no-trace. **A screenshot cannot see
  any of this**: on EACH screen a mis-scaled wall looks like a perfectly ordinary wall, which is why
  the bug was reported from a two-window capture and reproducible from neither half of it. Section 1
  is the NEGATIVE CONTROL for the whole suite -- it drives the real `WriteBaseState`/`ReadBaseState`
  and PRINTS the table above, because everything after it asserts the puppet IGNORES the wire's
  scale, which means nothing unless the wire's scale is shown to be wrong. Mutation-tested three
  ways, each failing DISJOINT legs; note what the scale mutation does NOT fail, and why, in the
  probe's header -- with the scale fixed, `800/width` and the drawn block AGREE again, so the
  invariant leg cannot tell a derived tile size from the old hard-coded one and the guard leg forces
  a scale on by hand to reproduce the pre-card condition.

## SNAPSHOT STALENESS + SCALE PRECISION (card f5cf7a5c, protocol v19)

Two changes to the same packet, filed together because they share one rig. Both were left open by
the LEVEL-3 WALLS section above.

- **THE GUARD. `MsgWorldSnapshot` now carries a monotone per-PACKET `[seq:2]` and the receiver
  refuses an entry that is not NEWER than the last one it applied FOR THAT netId.** The stream
  lane is unordered with `maxRetransmits:0`, so a reordered or late entry used to hand
  `NetPuppets` an OLDER position than the one on screen: the puppet sagged BACKWARDS and was then
  blended forward again over the correction window. Exactly the defect `NetFrameLocal` fixed for
  animation FRAMES; positions had no equivalent guard at all.
  - **NOTHING COUNTED IT, which is why it was reported from a playtest and from no log.**
    `pupPops` only moves on a correction past `SnapThresholdPx` (100px) and a reorder's error is
    far below that, so every metric read clean throughout. The new counter is **`snapStale`** on
    the `[net]` line: NOT a fault counter and NOT a 0 bar -- it tracks the LINK's reorder rate, so
    an unimpaired BroadcastChannel or in-process run reads 0 while a real lossy WebRTC pairing
    reads whatever that connection does. Deliberately NOT folded into `snapUnk`: nothing about
    the id was unknown, and folding it in would re-break the split card 48ab9b2f made.
  - **PER netId, not one global high-water mark.** The round robin gives an entity a turn every
    `live/16` packets, so a packet can be older than the newest one seen and still carry the
    freshest sample THAT entity has; a global guard would throw away good data for every entity
    not in the newer packet. An UNKNOWN id is never judged stale (there is nothing to compare
    against, and refusing it would strand a puppet the host has, permanently), and a REBUILT
    puppet starts with no mark, so its first entry is always accepted.
  - **THE WHOLE ENTRY GOES, `ApplyHostKilledFromSnapshot` included**, which is the question the
    next reader asks. Safe because a newer entry has already been applied and is authoritative
    over every field this one carries, and because death does not ride this lane -- `EvDying` and
    `EvDeath` are RELIABLE events and the hp==0 path is only the fallback trigger, whose own
    two-consecutive-turns rule re-offers a real death on every remaining turn.
  - **Wrap-safe on the SIGNED difference.** The counter is a u16 and a busy host rolls it over
    about every 65 minutes at 16.7 Hz; a naive `seq > last` would refuse every entry for the rest
    of the session from that moment, silently, with the puppets simply dead-reckoning on.
  - **A seq rather than a send-time ms** (the `MsgShipState` choice): the receiver only ever asks
    is-this-newer, never how-long-ago -- the entity's own dead reckoning owns the time axis -- so
    a u16 costs half a u32 stamp and matches the `MsgEvent` convention. Per PACKET rather than per
    entry for the same economy: 2 bytes once against 2 bytes x16.
  - **`?netstaleguard=0` restores the pre-card behaviour**, IN `DebugFlags.Active` for the
    `?nethitstop=1` reason -- it is a deliberate bug reproduction and must never reach a public
    lobby. It is one of the two booleans in `DebugFlags` that default TRUE *and*
    sit in `Active` (`?netaimease` is the other -- see CHARGE-GLOW AIM), so `Active` tests its
    negation. **The `snapStale` count is identical either way**: the entry is reported stale
    whether or not it is then refused, so the flag changes the drag and never the measurement it
    exists to let you take. That was not true of the first cut, and the mutation run -- not
    review -- is what found it.

- **THE SCALE. The card asked to WIDEN `NetBaseState.Scale`; it was MEASURED and superseded by a
  precision RAISE inside the same u16 -- quantum 1/256 -> 1/4096, and the cast ROUNDS.** The card
  chartered exactly that outcome (identify who is left; if the answer is nothing, say so).
  - **The census, over all 29 replicable types** (`NetStaleTest` section 1 prints it): nobody left
    DERIVES geometry from the replicated scale the way a `Wall` does -- `CollisionLevelMap` is the
    only scale-derived grid in the set and `Wall.NetScaleLocal` already owns it. Every other type
    is a single sprite whose hitbox is `texture * scale`, so the error is sub-pixel and does not
    accumulate. At the old quantum: 0% on every scale-1.0 type, 0.17% Asteroid, 0.21% ClassicBoss,
    0.39% ParatrooperAlien, and -- read out of the source, since the sweep reads CONSTRUCTED
    scales -- 0.67% small Braineroid, up to 2.1% small Ball, 6.25% at the bottom of PlasmaBall's
    entry telegraph, and Parachute's fade quantizing to literally 0 below 1/256.
  - **Why not the f32 the card named.** It would add 2 bytes per entity per snapshot turn (+32B on
    a 16-entry packet, ~7%) on the ONE lane whose loss is the other half of this card, to buy
    precision nobody needs beyond this. The raise is free in bytes and still 32x better.
  - **The ROUNDING is the half precision alone would not have fixed.** Truncation is
    one-directional -- every puppet in the world was systematically SMALLER than the host's copy,
    never larger -- which is exactly why the Level-3 wall's error accumulated down 122 rows
    instead of averaging out. Max absolute error 1/256 -> 1/8192.
  - **The ceiling is 65535/4096 = 15.999** against a measured maximum of 3.0 (Asteroid huge), and
    `WriteBaseState` CLAMPS silently, so the sweep asserts every type stays inside it rather than
    trusting a comment.
  - **`Wall.NetScaleLocal` STAYS.** Deriving from the replicated grid variation is right at any
    precision. `NetWallTest` section 1 keeps its >1% claim by measuring a verbatim transcription
    of the PRE-CARD encoder (`PreCardWire`) -- at the new quantum the live figure is 0.090%, and
    deleting the leg would delete the negative control the rest of that suite rests on.

- **Verify with `eaNetStale()` / `eval NetStale`** (`Compat/Net/NetStaleTest.cs`, 24 assertions;
  `tools/headless/probes/net_stale.txt`). MENU-only and leave-no-trace. It generalises
  `NetWallTest`'s wire harness one level up: the seq lives in the packet HEADER, so section 2 runs
  a REAL client `NetSession` over a `NetWire` with a scripted host writing real packets (the
  `NetScenarioTest` scenario-5 shape), while sections 3-5 drive `OnSnapshotEntry` directly with
  explicit seqs. **Section 3 measures the drag** across three runs of one frame sequence -- late
  entry undelivered / delivered-and-refused / delivered-and-applied -- because the guarded run not
  sagging passes on a build where the entry never arrived. Mutation-tested five ways.
  **Deliberately absent from `net_selftests.txt`** (it has its own probe, the `eaNetDeathFx`
  precedent).
  - **GOTCHA for any suite that hand-builds a snapshot packet:** stamp a MONOTONE seq through
    `NetProtocol.WriteSnapshotHeader`. Three suites wrote the header by hand and so left the seq
    at a fixed 0, which now makes every packet after the first stale -- `NetScenarioTest`,
    `NetFxTest` and `NetTeleportTest` were all fixed with this card, and two of them went GREEN on
    their must-not-pop halves while silently delivering nothing.
  - `NetPuppets.OnSnapshotEntry` keeps a 9-argument SUITE OVERLOAD that supplies the next seq
    automatically, so the suites that are not about ordering (`NetSnapshotTest`, `NetWallTest`,
    `NetDeathFxTest`, `NetPuppetBench`) keep saying what they mean. There is exactly one
    production caller, `NetSession.HandleWorldSnapshot`, and it passes the real packet seq.

## CHARGE-GLOW AIM -- the telegraph sweeps instead of stepping (card eb057163)

Reported as "the twin motherships in level 2 do not change where they are aiming visually as their
target moves while they charge their laser", seen on P2. **The mechanism was MEASURED before
anything was changed, because two faults produce that sentence and they need OPPOSITE fixes** --
and the measurement stays in the suite forever as the control.

- **IT IS STALENESS, NOT A FREEZE.** `MarsBoss.Update`'s charge case recomputes the aim EVERY tick
  (`normalize(target - Position) * 100`), `MarsBossDescriptor.EncodeStateExtra` re-reads it live off
  `lazerGenerator.Position - Position`, and `NetChargeGlow.Drive` re-applies it every client tick --
  so nothing latches anywhere. What is wrong is the CADENCE: the value only CHANGES on that
  emitter's round-robin snapshot turn. Measured over one full 2500 ms windup at a representative
  150 ms turn, the pre-card client moved its aim on **15 of 144 ticks, in 7.62px jumps**, with all
  16 aims the host sent arriving and being applied.
- **THE FIX IS AN EASE, and the card's own analysis proposed extrapolation instead -- superseded
  deliberately, do not re-derive it.** The glow now SWEEPS toward each newly replicated aim rather
  than teleporting to it. An aim is a **chase**, so its angular rate reverses whenever the player
  does: an extrapolated glow points somewhere the host never aimed and then SNAPS when the real beam
  fires along the host's true angle. A telegraph that LIES is worse than one that lags -- and the
  repo's grain runs away from difference-estimators anyway (card e79bb994 replaced the
  observed-velocity estimator with declared truth, card c1a38ef9 deleted the Lazer rate estimator).
  The sent-rate treatment in ANCHORED MOTION works for `Lazer` because a beam's growth rate is a
  genuine step function; an aim is not that shape.
- **NO WIRE CHANGE, protocol stays v19.** Everything here is a client-side decision about what to do
  with a value the snapshot already carried.
- **THE WINDOW IS `NetPuppets.CorrectionWindowMsNow`, not a new constant** -- `max(150ms,
  2 x SnapshotTurnMs)`, the same window the position error drains over, because the aim arrives on
  the same turn and therefore has the same staleness. One definition, exposed rather than
  re-derived: a second copy would drift the moment either constant moved, and a fixed window would
  degrade with the world size in exactly the way `CorrectionWindowFor` exists to stop.
- **THE DRAIN SHAPE IS `NetPuppets.Drive`'S, VERBATIM, and that is the point rather than a
  coincidence** -- a fraction of what remains **of the window** (`take / MsLeft`), so the sweep
  lands exactly on the target on the window's last tick whatever the dt pattern was, and so it is
  FRAME-RATE INDEPENDENT. The obvious one-liner -- a fraction of the WHOLE window each tick -- is an
  exponential decay that never lands and whose speed depends on the frame rate, i.e. precisely the
  drain `CorrectionWindowFor`'s own header records as measured and REJECTED. It shipped that way in
  the first cut and review caught it; suite section 6 is what would have.
- **A ZERO-LENGTH TICK HOLDS.** `NetPuppetDriver` derives dt from `TickCount64`, an INTEGER-ms
  clock, so two ticks inside one millisecond -- routine on a high-refresh display or under
  `?fpsuncapped` -- give exactly 0. Reading that as "the window is over" would teleport the glow on
  those frames, i.e. put the staircase back on a subset of them, and no constant-dt rig can see it.
- **It converges and can never overshoot**, so the glow comes to REST when the host's aim does. That
  also makes it **an ALGEBRAIC no-op on the two emitters that do not aim** (the big UFO's `lazor`
  and the JunkBoss' suck swarm sit at a FIXED offset, so `target == current` and the update is
  identically the current value for any fraction) -- which is what lets all five `NetChargeGlow`
  call sites share ONE rule with no per-type code. Being algebraic, it is not something a test can
  discriminate: suite section 4 catches a missing charge-on reset, not a bad ease, and says so.
- **THE WINDOW IS LATCHED WHEN A NEW AIM ARRIVES, not re-read every tick** -- rescaling mid-drain
  would move the fraction already applied, the same reason `PuppetInfo.CorrectionMs` is held per
  puppet rather than re-read inside `Drive`. A review invariant rather than a probed one: the rig
  holds one puppet, so the window is constant there and the mutation is invisible.
- **THE EASED VALUE LIVES ON THE EMITTER, and the charge-ON edge RESETS it** -- the emitters and
  their child generators are POOLED, so without the reset a boss winding up a second time would
  sweep its telegraph in from wherever its PREVIOUS beam pointed. Same recycle trap
  `Lazer.SetupSingleShot`'s `owner` clear and `FlyingSpider`'s anchor reset both document; the
  mutation run shows it also makes the FIXED-offset emitters sweep in from (0,0), i.e. it is not a
  corner case.
- **Cost:** a 20-byte `NetChargeGlow.AimEase` per emitter INSTANCE (not per type) and one lerp per
  CHARGING puppet per tick. Nothing is added to a non-charging puppet's tick, and `Drive` is
  client-only -- the host never calls it.
- **Verify with `eaNetChargeAim()` / `eval NetChargeAim`** (`Compat/Net/NetChargeAimTest.cs`, 28
  assertions; `tools/headless/probes/net_charge_aim.txt`). MENU-only and leave-no-trace. **No frame
  can see any of this** -- a stepping aim and a sweeping aim are the same still picture, the glow is
  draw-only so no counter moves, and the symptom is on the OTHER peer's screen; data is the only
  evidence there is. **The negative control is a shipped flag, `?netaimease=0`** (in
  `DebugFlags.Active`, the `?netstaleguard=0` deliberate-bug-repro idiom, and the SECOND boolean in
  `DebugFlags` defaulting TRUE). It is driven through the injected `INetHost` (`ChargeAimEase`), so
  sections 1 and 2 are literally the same frames with the fix off and on -- one session, one puppet,
  no confound and no reboot. Mutation-tested six ways, five failing disjoint legs. **It is not
  silent** (the glow is deliberately audible on a join peer), and it is leave-no-trace only because
  it stops its own session before planting anything: while a CLIENT session is up,
  `NetSession.SuppressWorldSpawn` DIVERTS any replicable-type `ComponentBin.Add` into the recycle
  pool, so three sections were quietly testing entities the world did not hold until review found
  it -- each now asserts its plant landed.
- **HONEST CAVEAT, worth keeping: the pre-card staircase was VISIBLE MOTION, not stillness.** 16
  steps of 7.62px over the charge is a glow that does track, jerkily. So the ease is a
  well-evidenced fix for the mechanism that was measured, and whether it fully accounts for the
  user's "do not change" is NOT settled by it -- if the report persists after a two-screen look,
  the next suspects are the aim's LAG and the fact that `MarsBoss` aims at
  `oracle.GetRandomPlayerShip()`, so half the time the boss genuinely is not tracking the peer who
  is watching it. **On the lag, note the window is bigger in play than in the rig**: the suite
  measures with ONE live puppet, where `CorrectionWindowFor` returns its 150 ms floor, while the
  ~40-entity Level 2 the section quotes gives `max(150, 2 x 150) = 300 ms`. So the shipped sweep
  trails the host's aim by up to a window plus RTT on top of the turn -- deliberately, since that
  is the price of never pointing where the host did not, and no client-side smoothing can remove
  it.

## Public game browser & join-in-progress

- **Public game browser + join-in-progress (card 2001fbd8, design `plans/net-game-browser.md`):**
  a running single-player game can be LISTED so strangers find + join it, with NO `NetSession`
  constructed until someone actually arrives.
  - **One eligibility predicate drives everything** (`Compat/Net/NetListing.ComputeEligible`):
    any empty player slot (`oracle.Players < Oracle.MaxPlayers` -- card 4d904410 relaxed this
    from `== 1`, so a COUCH game with a spare seat lists too and the browser's players column
    genuinely varies 1..3) + `Settings.AllowOnlineJoins` (new Option,
    **default ON**) + no cheats/`DebugFlags.Active` + a net-eligible LEVEL. The old "+ no
    session already up" term is GONE (card 0257f8ba): a HOST session on the real WebRTC
    transport with a free seat keeps advertising (`NetSession.HostOpenToJoinInProgress` bounds
    which sessions qualify -- a client, or a session on any dev transport, never lists), so a
    3rd/4th stranger joins the RUNNING match through the same room and `PeerConnected`
    JIP-launches it. **The level test is split out as the pure
    `NetListing.IsNetEligibleLevel(Levels)`** so it can be verified as data (`logic_probe`'s
    `ProbeListingLevels` sweeps the whole enum, with the pre-card predicate as the negative
    control -- the failure here is silent and REMOTE, a level appearing in a stranger's browser
    on a screen nobody playing is looking at). Refused: `WebcamAliens` / `TeamChallenge`,
    `Demo1..3`, and `Tutorial` since card df8f1ef7 (a solo scripted walkthrough is never what
    a player meant to advertise). It bounds the PUBLIC LISTING only -- it does not stop a host
    deliberately picking a level for a join-by-code game, and neither carousel offers
    `Levels.Tutorial` anyway (it is reachable only from the main menu's own Tutorial
    entry). The SAME predicate gates the listing, the beacon, and the pause indicator,
    so they can't disagree. `NetListing.Tick` runs each tick from
    `Game1.UpdateInner` (right after `NetSession.Update`).
  - **Listing != session.** A listed game keeps ONE lightweight signaling WS open (via
    `eaRtc.list`, reusing the 11.4 host machinery: `{t:host}` -> code -> `{t:list}` + a ~30 s
    `{t:beat}`, auto-answering browser `{t:ping}`s). It stays plain single-player (AI friends,
    no score sync, no Turbo lock) until a stranger pairs. This knowingly breaks 11.4's
    "single-player never touches a server" invariant -- the card's default-on premise; the
    Options toggle + pause "Listed online -- room XYZAB" indicator are the mitigation.
  - **Join-in-progress:** on pairing (`eaRtc` drives the host handshake -> "connected"),
    `NetSession.StartListedSession` attaches a HOST session to the running `GameScene`, sends
    the joiner `EvLaunch(currentLevel, difficulty)` + relies on the existing `EvReady`
    ->`ReplayLive` + 1 Hz `EvScoreSync` catch-up. The joiner is a normal menu-session client
    (`NetLobby.JoinWithCode`). A `listedSession` differs from a menu session ONLY in peer-loss:
    the joiner leaving reverts the host to single-player (NetListing re-lists) instead of
    force-exiting the host's own level.
  - **Ping is MEASURED, not estimated** (`server/signal/main.py` relays browser->host->back;
    `webrtc.js` auto-pongs in JS). Drop the old self-reported rtt idea entirely.
  - **Browser UI:** `SubMenuOnlineGames` (a `SubMenuCarousel`, the geometry extracted verbatim
    from `SubMenuLevelChoice` -- both now derive from it) shows one entry per open game with the
    level's screenshot art (`LevelArt`) + difficulty/players/ping/room-code. `NetGameBrowser`
    opens the browse socket, parses the room list, and fills each ping as its pong lands ("--"
    until then). Reached from the main-menu "Online Co-op" submenu's "Join Online Game".
  - **Beacon:** `ScoreVisualiser.drawPressStart`'s `Player X` <-> `Press Start` blink gains a
    third string carrying the ROOM CODE BARE (no "Room code:" label -- card 10d9f8e3: it is a
    narrow corner prompt sharing a column with "Player 2"/"Press Start", and the label pushed
    the code itself off the right edge of the screen on the top-right slot; the LABELLED
    spellings in the lobby panel and the browser carousel stay -- those are full-width panels
    where the label is the only thing saying what the five characters are) while listed, and its
    4-cycle stop is suppressed, so the
    code surfaces ~every 15 s (the existing intermittent rhythm, never a static banner). The
    `bool showPressStart` became an index `promptPhase` (drawn `% (listed ? 3 : 2)`).
  - **`?netfakelisted=<code>` is the OFFLINE rig for the two places a listing SURFACES** (card
    d1a0559b): `NetListing.Tick` short-circuits on it, reporting `Listed`/`RoomCode` with no
    socket and no server, so the pause line and the corner beacon can be screenshot headlessly.
    Nothing is registered and no stranger can join. Out of `DebugFlags.Active` -- no session
    exists, so it cannot alter a shared run.
  - **The pause "Listed online -- room XYZAB" line is positioned from the row layout, not a
    magic y** (card d1a0559b). It sat at a hard-coded design y=400, which is INSIDE a four-entry
    centred list (~322..442), so it drew across "Instructions"/"Exit to Main Menu" every time it
    was shown. `PausedScene.DrawMenu` now derives it from `GetListCentre()` + the same +75
    yoffset it just drew the rows at, so a font or entry-count change carries it along.
  - **Flags:** `?gamebrowser` boots straight to the carousel with injected FAKE entries (no
    server) for a screenshot -- four real-looking games; `?gamebrowser=fallback` appends two on
    levels with NO bundled art (card 0d166364), the only offline rig for `EnsureArt`'s fallback,
    kept off the bare flag because they would be junk rows in an appearance shot;
    `?netjip` lets a `?level=` (`DebugFlags.Active`) host list anyway
    for the two-window JIP metrics test (it also drops the debug-flag bit from its hello so a
    clean joiner won't reject it).
  - **Verify:** `server/signal/test_signal.py` (registry/browse/build-filter/ping-relay/full->
    delist, all standalone); `?gamebrowser` for the carousel; the eligibility predicate as data;
    `?netjip` two windows -> `[net]` metrics. The full two-window pass was RUN in card c0398370;
    the five traps that make it hard, and the recipe, are the next five bullets.
  - **JIP pass trap 1 -- it needs two genuinely VISIBLE OS WINDOWS. Two TABS cannot work, and
    `?fpsuncapped` does not rescue them.** A background tab's rAF is *paused* outright (measured
    0 ticks), and the `MessageChannel` pump `?fpsuncapped` swaps in still ran at only ~3 ticks in
    3 s in one measurement -- roughly 1 Hz, nowhere near the ~30 Hz ship stream
    (`StreamIntervalMs` 33). Chrome's *documented* intensive throttling targets timers rather
    than `MessageChannel` macrotasks, so treat the exact mechanism as unconfirmed inference; the
    observation (rAF 0, uncapped ~1 Hz, both useless) is what matters. An OCCLUDED or MINIMISED
    window counts as hidden too, so the two windows must be tiled non-overlapping AND kept above
    everything else: pin exactly the two peers `HWND_TOPMOST` via Win32 and make sure the window
    DRIVING them is **not** topmost, or every interaction with the driver raises it over a peer
    and silently freezes that peer mid-run. Both peers ticking at the SAME rate is the check that
    the rig is honest.
  - **JIP pass trap 2 -- the joiner must boot FLAG-CLEAN.** The reject is
    `menuSession && (peer debug bit || DebugFlags.Active)` (`NetSession.cs`), and the joiner IS a
    menu session, so its OWN `Active` bit rejects the pairing. The net-relevant flags still open
    to it are `?noattract`, `?signal=`, `?binlog`, `?netlog`, `?netlag=` and `?netloss=` (none are
    in the `Active` expression), plus the JS-owned `?fpsuncapped`/`?nofps`, which never reach C#.
    **`?netsim` is NOT usable on a joiner**: it is parsed only in `index.html`, and that block
    early-returns unless `?net=` is present -- which sets `NetRole` -> `Active` -> rejected. The
    host is fine: `?netjip` drops its debug bit (`LocalHelloFlags`) and the check is
    `menuSession`-gated, so a `listedSession` host never rejects. **Put `?noattract` on the
    joiner's URL** (out of `Active` since card af63f958) rather than driving its lobby against a
    20s idle timer.
  - **JIP pass trap 3 -- a grant whose TARGET seat was taken used to desync SILENTLY and
    permanently. FIXED in card c0229c57 (protocol v8); the trap is recorded because the shape is
    instructive.** `Oracle.MovePlayerSlot` refuses when `players[to].isPlaying`, so it was the
    *granted* slot being occupied that bit -- a joiner merely seated in slot 0 with slot 1 free
    moves across fine and logs `moved local primary slot 0 -> 1`. On refusal
    `AdoptGrantedPrimarySlot` logged `... (slot busy) -- staying put` and the peers disagreed
    forever (`pri=0/0` vs `pri=0/1`), the joiner never built a remote puppet (`remoteShip=0`,
    `buf=0ms`), and NOTHING surfaced to the player.
    **It was reachable with no debug flags at all**, which is the part worth remembering: the
    menu's roster was whatever the last scene left behind (`GameScene.Terminate` did NOT reset
    it; only the launch paths' `ResetPlayers()` did -- card ee96ea61 has since made Terminate
    reset it too, see the roster-slots bullet), and the attract demo seats MORE than one --
    `mainMenu_DemoSelected` seats slot 0, then `Demo1/2/3.Initialize` adds 3 more on a 20% roll
    and 1 more on a further 40% roll. So "idle at the menu -> attract demo -> key out -> Online
    Co-op -> Join" left slot 1 seated ~60% of the time, and a couch session backed out to the
    menu did it every time.
    The fix is three things. (a) The host no longer GUESSES: the v8 handshake carries a
    `blockedSlots` mask (client -> host) so `ReserveRemotePrimarySlot` grants a seat free on
    BOTH rosters -- see the roster-slots bullet. (b) The client only moves a seat when a
    `GameScene` is up; at the menu the roster is bookkeeping `ResetPlayers()` is about to wipe,
    so there is nothing to move. (c) A grant that still lands badly RENEGOTIATES rather than
    settling -- `peerPrimarySlot` is now assigned only on a settled adoption, which is what keeps
    the 1 Hz hello alive so the host can re-grant. **That last one is the general lesson: any
    early return in `AdoptGrantedPrimarySlot` that leaves `peerPrimarySlot` set silences the
    retry on BOTH peers and makes the session unrecoverable.** Verify with `eaSlotTest()`.
    (Note the `?noattract` point in trap 2 is about the TEST RIG only -- a real player never
    passes flags, so the attract-demo roster is exactly how this reached them.)
  - **JIP pass trap 4 -- use a LOCAL signaling rig, not the deployed one.** All four entry points
    read `DebugFlags.NetSignal` (`NetListing.Tick`, `NetGameBrowser.Start`, `NetLobby` host/join,
    `WebRtcTransport`), so `uvicorn main:app --port 8091` in `server/signal` +
    `?signal=ws://localhost:8091/ws` on BOTH windows exercises the identical client code. The
    server is also the best non-perturbing STATE ORACLE: `GET /health` (`rooms`/`listed`/
    `browsers`) tells you the host listed and the joiner reached the carousel without touching a
    window, and a one-shot `{t:browse}` client prints the live room code.
  - **JIP pass trap 5 -- pick a host fight that does not END.** `?level=Level2&flyspiders` (the
    endless swarm) is ideal; a plain `?level=Level2&aiplayer` host finished the level on its own
    partway through one run (how fast depends on difficulty, AI and RNG), at which point the scene
    goes down, `NetListing` drops the room, and the joiner's carousel correctly falls back to
    "Searching for open games..." mid-test.
  - **JIP pass recipe:** host `?level=Level2&flyspiders&netjip&aiplayer&invuln&binlog&signal=...`,
    joiner `?signal=...&noattract&binlog&netlog` -> menu -> Online Co-op -> Join Online Game ->
    pick the room. **Pass looks like:** `session start role=host ... (join-in-progress)` +
    `... role=join ... (menu lobby)`, `granted joiner primary slot=1`, **mirror-image rosters**
    (`0:Keyboard*,1:Remote` `pri=0/1` vs `0:Remote,1:Keyboard*` `pri=1/0`), `localShip=1
    remoteShip=1` and `buf=` ~100ms BOTH sides, `drop`/`sgap`/`ordViol`/`seqGap`/`extrap` 0
    and `dupBad` 0 (a nonzero `dup` at the join itself is the benign `dupLive` catch-up burst,
    card 4c9448c8),
    **zero `[bin] purge-filter diverted`**, and identical `eaNetBg()` state lines.
  - **JIP pass trap 6 -- `pupPops`/`snapUnk` from this rig were UNREADABLE until card 48ab9b2f,
    and the two traps that made them so are still live.** The first pass logged `pupPops 207` /
    `snapUnk 344` over ~25s and could conclude nothing. (a) `snapUnk` was one counter for three
    unrelated causes -- now split into `snapNew`/`snapDead`/`snapBad`, and note the old "judge it
    against `clTx`" rule was simply wrong (see the metrics bullet). (b) `?flyspiders` looks like a
    dense-swarm explanation for the pops but is NOT one: `--population` shows that swarm logging
    0 pops/s across the whole range bar one far-off resonance, and the rig's live count measures
    only 17-19 (`snapTurn` at its 60ms floor) anyway. What DOES produce hundreds is a client
    ticking at ~1Hz -- i.e. trap 1 (an occluded window) intermittently biting, which the rig
    cannot rule out after the fact. So on a re-measure: read `snapTurn` alongside `pupPops`, keep
    both windows genuinely visible, and treat a pop rate from a run whose tick rate you did not
    watch as no evidence at all.
  - **Known JIP gaps -> follow-up cards (`plans/net-game-browser-followups.md`):** mechanical-friend
    ships unreplicated (listing refused while `Friends>0`); a mid-boss arrival hits the
    best-effort puppet limit; public-list abuse surface (rate limiting / hiding a room). The deep
    mid-level scenery gap is closed -- see the catch-up bullet below and the scene-swap bullet
    under it.
- **Deep mid-level scenery catch-up for a late joiner (card 45a4e48d):** a peer arriving
  mid-level runs its OWN scene Initialize, so it holds the level's INITIAL background + music and
  -- the script being host-only -- can never reach the beats that already fired. The host replays
  them once as ordinary reliable `NetBackgroundOp`/`EvMusic` events, so the client applies them
  through the same paths the live ops use.
  - **The seam is the `EvReady` handler, NOT `PeerConnected`** (next to the existing
    `ReplayLive()`). At pairing time a JIP joiner has no `GameScene` at all, and the Initialize
    that gives it one would clobber anything sent earlier. Being at `EvReady` also covers the
    menu-lobby launch race and the `?net=` loopback rig for free.
  - Replayed, in order (the order that matters is doodad kind before its position -- `Queue*`
    parks a doodad back at its entry point and `SetDoodadPos` then moves it to the host's; speed
    leading is readability, since `SetSpeed` only retargets a 1333ms lerp and so has NOT moved
    the `scrollspeed` that `Queue*` reads): last `SetSpeed`, last `SetAlienBaseN`,
    `EngageBeltSlowdown` if engaged, any in-flight doodad + `SetDoodadPos` (appended op 11,
    catch-up only) so the joiner picks the fly-by up MID-CROSSING, then the current song.
  - **The last-op state is latched by `Background` itself, not sniffed off the send path** --
    `NetSession.OnBackgroundOp` early-returns while no peer is connected, which for a listed
    single-player game is exactly the window whose ops must be remembered. The latches are
    `Vector2?`/`NetBackgroundOp?`: null means "the script never touched it", which is NOT the
    same as the default (before the first `SetSpeed`, `targetscrollspeed` is still zero while the
    real `scrollspeed` is whatever `SetSpace()`/`SetMars()` set -- replaying that zero would
    freeze the joiner's starfield). Cleared in `Background.Reset()`.
  - `QueueEarthSim` (holodeck) shares `QueueEarth`'s TEXTURE but has no wire op, so the doodad
    kind is tracked explicitly at queue time rather than inferred from `doodadname`; sim-earth
    sets the latch to null and is simply not replayed.
  - **Verify with `eaNetBgTest()`, not two windows:** the subject is a fly-by that moves every
    frame, so the gate is the one-tab round-trip self-test (capture the burst -> `NetTestWipe()`
    -> replay through the real client apply path -> diff the state line; prints PASS/FAIL, the
    ops it replayed, and all three lines). The state line deliberately reports the state the ops
    CONSUME (`targetscrollspeed`, the live layer-0 texture name), never the `netLast*` latches --
    printing the latches would make the round trip a tautology. It names the replayed ops because
    a leg the level never fired is simply absent, and a PASS must not be read as covering it.
    `eaNetBg()` alone dumps the live state for a two-window comparison. Both are
    console-only; the self-test is destructive (Reset re-runs the hyperspace entry).
    **`?netscript` reaches all five legs at once, and the order of its beats is why**: it swaps
    the whole scene to the ALIEN BASE first, which is what lets the `SetAlienBase2` beat after it
    switch a floor layer at all -- on Level 1, whose own `SetSpace` scene has none. Committed as
    `tools/headless/probes/netbg_catchup.txt`.
  - Music RATE (`SetMusicRate`, the BrainBoss HP sweep) still does NOT replicate -- it is driven
    per-tick from a client-frozen boss `Update`, so it belongs to the mid-boss puppet-fidelity
    follow-up, not here.
- **A whole-SCENE swap mid-level replicates too (card ca4fd94f, ops `SetSceneSpace`/`SetSceneMars`/
  `SetSceneAlienBase`).** A scene setter run at level Initialize is not replicated -- both peers
  run their own -- but `InsaneBossI` calls `SetSpace`/`SetMars`/`SetAlienBase` MID-level between
  boss phases, and a client's event list never runs, so without this it kept the level's opening
  backdrop for the rest of the run. **Not only a JIP gap:** a peer paired at the lobby was equally
  wrong, so the fix is the ordinary emit-at-the-primitive one and the catch-up latch falls out of it.
  - **The latch is assigned AFTER the setter's own `Reset()`, and that is the whole trick** --
    every scene setter ends with `Reset()`, which clears the catch-up latches (rightly: level
    entry goes through it too), so a latch set before it is wiped by the setter it describes.
    `netLastScene` is therefore NOT cleared in `Reset()`; `GameScene.Initialize` clears it via
    `Background.NetBeginLevel()`.
  - **"Initialize-time" is not decidable inside a setter**, so `Background` is TOLD: the entry
    scene is captured once per play at the Startup -> Normal edge (`NetNoteEntryScene`), when the
    script is about to run. `NetActiveScene` is already non-null during a level's own Initialize;
    the levels call their setter before or after `base.Initialize()` inconsistently (Level1 before,
    InsaneBossI after); and a checkpoint revert re-enters Startup mid-level, which is why the
    capture is one-shot per `Initialize` rather than per Startup.
  - **The scene op replays FIRST**, before speed/base/belt/doodad: applying it runs `Reset()`,
    which would wipe any leg sent before it, and it is what guarantees `SetAlienBaseN` lands on a
    scene that has a base layer. `NetTestWipe` rebuilds the ENTRY scene for the same reason --
    without it the self-test compares the host's backdrop against itself and passes vacuously.
  - A swap also re-points `targetscrollspeed` at the scene's own baseline: `Reset()` clears
    `netLastSpeed` while leaving the old target, so a `SetSpeed` before the swap left the host
    reporting a scroll target no joiner could reach (and an in-flight ramp dragging the new
    scene's scroll toward the old scene's). Found by the InsaneBossI round trip -- `halt()`
    then `GoAlienBase`.
  - **A level whose mid-level swap has side effects beyond the backdrop overrides
    `GameScene.NetApplySceneChange`** -- InsaneBossI mirrors its `Floor` add/remove there. Only
    LOCAL side effects belong in it: the music already replicates as its own `EvMusic` beat, and
    its `Purge<Ball>` is host-authoritative (the host's purge broadcasts an `EvDeath` per removal,
    so a local purge would strand puppet ids). **KNOWN LIMIT:** `spawnType` is not mirrored, so a
    client respawning inside the Mars section enters from the south rather than the west.
    Its mirror is observable rather than invisible: `GameScene.NetSceneChangeState` puts it in the
    `eaNetBg()` line (`floor=`) and `NetSceneChangeTestWipe` removes it for the round trip, so the
    leg is non-vacuous when you run `eaNetBgTest()` inside the Mars section. Both read "live NEXT
    tick" -- `ComponentBin.Remove` is queued, so a membership test alone reports a floor the wipe
    has already dropped. Verified that way (`?level=InsaneBossI&invuln&aiplayer`, soak to the swap:
    host `floor=1` / joiner `floor=0` / caught `floor=1`).
    **It has no COMMITTED probe, and the reason is worth knowing before writing one**: reaching the
    Mars section means an AI fight of nondeterministic length (`RandomHelper.Random` is
    time-seeded), so a probe asserting at a fixed frame is flaky -- measured landing in the
    preceding SPACE section on roughly one run in six at frame 7200, where six other runs read
    Mars. A committed probe here needs a fast-boot flag into that section first, the way
    `?spiderboss`/`?brainboss` do for theirs.
  - The scenes with NO wire op (holodeck / classic variants) are Initialize-only. A mid-level swap
    to one is REPORTED on the `[net]` line rather than silently latching null -- silence there
    would reproduce this very card's bug one level up.
  - **No protocol bump.** The enum is append-only and an older peer ignores an unknown op, so it
    degrades to exactly the pre-card behaviour (the level's opening backdrop) with no desync --
    unlike `EvCosmeticSwarm`, which had an old peer expecting per-entity spawns that stopped
    coming and so forced v10.

## Room thumbnails -- the browse carousel shows the real game (card e7404647)

A listed room carries a small JPEG of the host's game in progress, and
`SubMenuOnlineGames` draws it instead of stock level art. **The thumbnail is a picture of the
GAME FIELD only** -- the resolved scene render target, never a camera, a canvas or the page.

- **THE SERVER OWNS THE SCHEDULE, and that is the whole design.** Clients never upload
  unsolicited: the matchmaker PULLS (`{"t":"shot"}` on the room's existing signaling socket) on a
  **global** budget of one pull per second across ALL rooms, round-robin by oldest-pulled; beyond
  ~15 listed rooms the per-room interval stretches by itself. **Degradation is staleness, never
  load** -- do not scale the budget with the room count.
  The rotation is keyed on a COUNTER stamped when a pull is SENT, so a host that never answers
  forfeits its own turn and can never starve anyone; `server/signal/main.py` says why it is not a
  clock.
  - **The schedule is GATED twice (card 97b31562), and both gates answer the "screenshots stutter
    the game" report.** (a) **No pull is ever sent while no browser socket is connected** -- the
    thumbnails exist only for the browse carousel, so an idle listed game pays for no captures at
    all; once someone browses the rotation resumes on the next 1 s tick, though a room still
    inside its floor waits the floor out first (deliberately not reset on the transition -- a
    flapping browser socket could otherwise re-arm the every-second pulls). (b) **No single room
    is re-pulled within a 15 s floor** (`SHOT_ROOM_MIN_INTERVAL_SECONDS`). Without the floor the
    "~15 s per-room refresh" only emerged at 15+ rooms: a LONE listed room -- the reported
    configuration, one host + one browsing peer on one machine -- was round-robined to EVERY 1 s
    budget tick, and each pull is a `ResolveBackBuffer` + synchronous GPU readback in
    `NetRoomShot.Capture` plus a `toDataURL` JPEG encode in JS, i.e. a visible 1 Hz hitch. With
    both gates a capture is at most one frame's cost per 15 s, only while someone is actually
    browsing -- so no threading of the capture is needed, and none was added. A PAIRED session was
    never pulled either way (`listable()` requires an empty joiner slot). Server-side only, no
    protocol change; both gates are mutation-tested in `server/signal/test_signal.py` (a case per
    gate). **Deploying it is manual** -- `server/signal/README.md`'s update recipe; merging ships
    nothing.
- **CAPTURE IS C#, ENCODE IS JS, and the split is what makes it verifiable.**
  `Compat/Net/NetRoomShot` books one `Game1.onPostDraw`, `ResolveBackBuffer`s the scene,
  `DrawPresent`s it into a 200x150 RT (the `ScreenshotSaver.SaveScreenShot` recipe, alpha seal
  included -- `toDataURL('image/jpeg')` composites a translucent canvas over BLACK, so an unsealed
  frame reaches the server visibly darkened) and hands the RGBA to `eaRtc.sendShot`, which does the
  JPEG. **`canvas.toDataURL` was the obvious route and is the wrong one**: no
  `preserveDrawingBuffer`, the canvas carries the letterbox bars rather than the field, and none of
  it runs under eahl. Coming back, JS decodes to RGBA too, so no C# code handles compressed bytes
  in either direction.
- **NEITHER HALF NEEDED A PROTOCOL VERSION -- this is signaling only, and the capability is
  negotiated by DATA.** The host declares `shots:1` in its `list` frame and the server pulls only
  declaring rooms; the client sends `shotget` only for a listing entry that carried a non-zero
  `shot` seq, which an old server never emits. Both directions matter: an old host answering an
  unknown pull would take the server's `bad` reply through `fail()` and LOSE ITS LISTING, and a new
  client asking an old server would show "Browse failed". Either deploy order is safe.
- **The carousel prefers the thumbnail and falls through to the card-0d166364 `EnsureArt` chain**
  when there is none, when it is stale (>180 s, both ends agree) or when the room drops off the
  list. `NetGameBrowser` holds the pixels keyed by CODE, not on `GameEntry` -- the entries are
  rebuilt every ~4 s browse refresh and a thumbnail must outlive that.
- **Verify with `eaGameBrowserShots()` / `eval GameBrowserShots`, not a screenshot** -- a row whose
  thumbnail silently failed to install draws exactly what a row that never had one draws.
  `?gamebrowser=thumbs` gives two of the four fake rooms a synthetic thumbnail (both branches in
  one shot, no server); `eaRoomShot()` captures the live frame through the real pull path and
  reports dimensions / alphaMin / a distinct-colour count that separates a real picture from a
  blank one, and `eaRoomShot.inject('CODE')` puts a REAL captured frame in the carousel offline.
  Pinned by `tools/headless/probes/room_thumbnail_capture.txt` + `room_thumbnail_carousel.txt`.
  **Neither the real pull nor the JPEG round trip is reachable headlessly** (no WS, no DOM) -- that
  leg is a local `uvicorn` + Chrome pass.

## THE AUTOMATED JOIN-IN-PROGRESS SUITE (card 054947f3) -- and what it found

`python tools/sim/net_jip_sync.py` drives **two eahl PROCESSES** -- a real listed HOST playing a
level and a real menu-session JOINER that attaches to it mid-level -- and DIFFS the two worlds
once the attach has settled. It replaces the manual two-window Chrome pass as the REGRESSION
gate; the manual pass stays the final real-network check, since nothing here reproduces WebRTC or
signaling.

```sh
dotnet build tools/headless -c Debug
python tools/sim/net_jip_sync.py                     # the three story levels, 600s each
python tools/sim/net_jip_sync.py --level Level2 --cap 120 --verbose
python tools/sim/net_jip_sync.py --selftest          # the tool's own vacuity control
```

**IT IS GREEN ON `main` (card d108c459), AND HOW IT GOT THERE IS THE POINT.** It was red twice:
first on a real defect -- a joiner purging the host's catch-up burst, fixed by card 9a7ee4c0 below
-- and then on **measurement**, a handful of joins per soak failing on classes where the two ends
are dumped at slightly different world instants. **Not one of those was retired by widening a
tolerance to fit.** Three were CATEGORY ERRORS in the oracle and were deleted outright; the two
that were genuinely staleness got tolerances measured over 223 joins across 6 seeds. The
calibration section below has each rule, its evidence and its mutation.

**A GREEN RUN PRINTS WHAT IT DECLINED TO FAIL ON.** Every rule that explains a disagreement rather
than reporting it is counted on the run's last line --
`N join(s) compared; converged= transit= released= skipped= hpwire=` -- and every mismatch the
re-settle clears is printed under its join. That is deliberate: a rule that quietly deletes a class
is indistinguishable from a differ that stopped looking, so the numbers are on screen even at exit
0. A `converged=` that jumps relative to `joins` is worth reading before believing the tick.
It is a standalone tool, not a `run_probes.py` probe, so a red run is a finding rather than
broken CI -- and `prov=` / `owner=` remain exact, first-sample and never softened.

**THE CALIBRATION, CLASS BY CLASS (card d108c459).** Measured on `main` at `7018c62` over a
6-seed zero-tolerance sweep (188 joins) plus a 110-join raw-dump capture that re-dumped each join
2 s later, which is what separated "converges" from "persists". **Green is judged over 10 seeds
(370 joins), not one run** -- the classes are seed-dependent and a single soak proves nothing.

| residual | measured before | what retired it | after |
|---|---|---|---|
| `dead` / `dying` | the largest class -- 5 of 8 failing joins on the default soak | **not compared at all.** `dead` is on NO wire field: on the host it means "removal queued this tick", on the client "I killed it locally". `dying` does ride the wire but legitimately LEADS on whichever peer's bullet landed. An entity in transition on either end skips its SAMPLED keys too (hp mid-death reads 0 against full) -- `prov=`/`owner=` are not skipped, they are identity | `transit=` |
| `is on the HOST and not on the joiner` | 14 in 188 | **evidence, not tolerance.** dump v4's client-side `gone` line is the joiner's own removal ledger -- "I had it and let it go", which `ReleaseDyingPuppet` produces constantly (it drops the puppet from every map while the host keeps the entity for the whole 2.5-5 s animation). A host-side death is the second form, for a release older than the ledger's cap. NEITHER -> still a hard failure, which is the defect class: a joiner that never got an entity | `released=` |
| `hp` under fire | `Boss 210 vs 180`, then `211 vs 179`; worst 32 | **compared WIRE-TO-WIRE: the host's last SENT hp (`NetIdRegistry.Entry.LastSentHp`) against the joiner's last RECEIVED one (`PuppetInfo.LastAppliedHp`) -- both printed as `hpwire=` in the dump, one key because they are one quantity -- and NEITHER side's live `hp`.** The two live values are different quantities -- a client hit-tests puppets with its own bullets, and the host has moved on since that entity's round-robin turn. Only a tolerance near 40 covers the raw class, which is wide enough to pass a UFO at 2 against a host's 10 | **exact.** `--hp-tol` 5 -> **0**: over 227 joins the two wire values differed **0 times**, and the clamp invariant was violated 0 times |
| `score` staleness | 5500, and 94001db7's "do not treat 4850 as a bound" | **SUPERSEDED by card af96bcc2 (one writer per slot)**: the `uns`-keyed limit and the pts-minus-uns oracle reconciled TWO writers, both deleted. The compare is DIRECTIONAL now, ownership read off the host dump's seat field: a replica ABOVE its owner fails with NO tolerance (totals only grow -- the hpwire clamp-invariant shape), a replica BEHIND its owner is staleness. Under continuous fire that lag STANDS (each sample is one ~100 ms packet behind a moving total), so the re-settle cannot clear it and the tolerance covers it | `--score-tol` **1500**, from a measured worst of 600 over 111 joins x2.5; the exceed side is exact |
| vacuous join (`0 replicated entities`) | 7 in 188 | **the rig, as the card said.** A real level is empty between waves, so the join is SKIPPED and counted rather than failed -- with a per-level floor of real joins, or an all-skipped level would be the vacuous green again. (Waiting for a populated world BEFORE attaching was tried and DECLINED: the world at attach is not the world at the dump 10 s later, and the wait cost 24 of 39 joins per soak) | `skipped=` |
| `pos` / `rot` | 17.0 px / 0.209 rad | **shrink the artifact first.** The interleave chunk dominates (one peer has always stepped last), so `--approach` steps the last 6 frames ONE AT A TIME: 17.0 -> **13.5 px**. Tolerances then set from that with margin | `--pos-tol` 12 -> **20**, `--rot-tol` 0.1 -> **0.3** (every one of the top ten a `Ball`) |
| `lv` / `opt`, joiner-only ids, transient extras | 4 in 188, plus a class per soak | **the re-settle confirm** (below). They stay compared EXACTLY -- there is no sane tolerance on a ladder position or a count of Option ships | `converged=` |
| scenery line | 1 in ~300 | compared FIELD BY FIELD, not as one string: the doodad carries a POSITION and got `-239.6` vs `-239.2` reported as a desync. Its name stays exact and so does everything discrete (which is what makes the missing-catch-up mutation unmistakable) | -- |
| the `uns` run-level control itself | (history) | **RETIRED with the ledger (card af96bcc2)** -- there is no `uns` field to control any more (dump v5); the hp leg's `hpwire=` control carries the silent-deletion guard | -- |

- **The hp compare reads the RECEIVED value rather than the entity, because the two are different
  quantities.** `hpwire` is recorded as `state.Hp` at the moment it is applied
  (`PuppetInfo.LastAppliedHp`), never by reading the killable back afterwards -- a read-back
  reports whatever `NetApplyHp` decided to do with it, which is not what the host sent. Under the
  ORIGINAL downward-only clamp that gap was large and one-directional: measured **132 gaps across
  6 seeds, 132 client-lower, 0 higher, 0 equal** -- the clamp's signature, not a replication
  fault. Card 87310afa narrowed it (see the next bullet) but did not close it: the apply can still
  be refused whole by the floor at 1 or by a dead puppet. The relationship is then asserted
  SEPARATELY as an invariant with no tolerance at all -- **a puppet's live hp may never EXCEED its
  `hpwire`** -- which holds under either direction, since a raise assigns exactly `state.Hp` and
  local damage only ever goes down from there. Re-measured after the change: `--level Level2`,
  `hpwire=40` compares, **0 violations**.
  - **COVERAGE BOUNDARY, stated because a tolerance would have hidden it:** a wrong-but-LOWER
    apply is indistinguishable from ordinary local damage seen from the host's side, so this
    suite does NOT prove `NetApplyHp` assigns the value it received. Nothing did -- `NetEntityTest`
    calls the seam directly and `NetWireTest` round-trips the byte through a frame, but neither
    drove a real snapshot entry into a real puppet. **`eaNetSnap` section 7 now does** (card
    d108c459; `net_selftests.txt` tally 40 -> 45, then 45 -> 48 with card 87310afa), driving
    `NetPuppets.ApplySnapshotState` and reading the killable back.
- **`NetApplyHp` IS TWO-WAY: the host is authoritative for a puppet's hp in BOTH directions (card
  87310afa).** It used to refuse every raise (`if (hp >= hitpoints) return;`), and that was not
  free. A client's bullets run the real `HitBy` against puppets locally -- they are
  `Enabled=false` but stay hit-testable through `NetPuppets.CollidableOverride`, which is what
  client-owned kill claims ARE -- while the host's own **per-entity 35 ms `hittimer`** may refuse
  those same hits, the two peers running independent gates over hit sequences ~100 ms apart. Every
  such over-prediction was permanent, so a client's copy ratcheted below the host's for the rest
  of a fight; and since the client kills locally at `hitpoints<=0` and files an unconditional
  `EvClaim` (`HandleClaim` -> `NetKill` bypasses the hittimer -- a claim is already a confirmed
  kill), **a boss could be claimed dead while the host's copy still had HP**.
  - **What the old direction was NOT doing:** it is not what stops two players draining a boss at
    double rate. That is host authority plus the 35 ms gate at the top of `HitBy` -- the host's
    boss is ONE real entity and both players' bullets (the peer's re-spawned from the replicated
    cumulative shot count) contend for that one gate. Card a5c2a39b's closing note credited the
    clamp; the conclusion held, the mechanism cited did not.
  - **THE COST, accepted deliberately (cosmetic).** An in-order but ~half-RTT-stale snapshot
    legitimately lacks the hits the client just landed, so the DRAW-side readers of hp -- the
    colorize redden and `BattleSkull`'s Draw-time hue remap -- can nudge back up mid-burst,
    bounded by the snapshot turn. **`MarsBoss`'s `fps = Lerp(32, 16, HitPointsNormalized)` is
    NOT one of them**, though it looks like the obvious third: it is re-derived at the top of
    `MarsBoss.Update`, which a frozen puppet never runs -- which is exactly why that type opts
    out of `NetFrameLocal` and takes the replicated frame instead.
  - **Do NOT "fix" the raise by capping it at `initialhitpoints`.** `HitPointsNormalized <= 1`
    reads like an invariant the raise breaks, and it is not one this class ever had: `Initialize`
    sets `hitpoints = initialhitpoints * DifficultyFactorized(0.5f)`, which is above 1 on every
    tier past the floor, so a `scaleWithDifficulty` type (`Boss`, 225) is already over its
    initial at full health in ordinary single-player. A cap would cut those types' replicated hp
    to the unscaled number on every snapshot -- a real desync traded for a cosmetic one the raise
    does not cause, since both peers share the session difficulty and compute the same pool.
  - **The REORDER case is a different guard and is untouched.** Card f5cf7a5c's per-netId monotone
    seq refuses an older entry whole, before `ApplySnapshotState` reaches the hp read, so a late or
    reordered packet still cannot raise hp. `eaNetSnap` section 7 pins the two separately, and its
    stale leg asserts the guard's own `stale` flag as a PRECONDITION -- without that it would pass
    on a seq that was simply accepted and applied as a no-op. Mutation-tested both ways:
    `?netstaleguard=0` fails only the stale leg (hp raised 9 -> 110), and restoring the early-out
    fails only the two raise legs while the floor leg still passes.
  - **The floor at 1 and the `dead` guard are unchanged**, so deaths still arrive exclusively as
    events or local kills and no snapshot can resurrect a dead puppet. The floor simply had no
    leg of its own until now (it was reachable before -- the old early-out did not shadow it);
    it is pinned separately so the direction change cannot quietly take it with it.
- **(HISTORY, superseded by card af96bcc2.) Comparing score wire-to-wire was measured and
  declined under the ledger design** -- a joiner booked settled awards continuously and
  `EvScoreSync` was only a true-up, so the wire-to-wire version went RED on 7 of 10 seeds.
  Moot now: the sync IS the source of truth under one writer (the owner's declared total,
  adopted verbatim), which is exactly the design the old measurement said could not work while
  two writers existed.
- **THE RE-SETTLE CONFIRM, and why it is not a way of hiding things.** A join that reports
  mismatches is stepped `--resettle` frames (default 300 = 5 s, past one 1 Hz score sync, several
  snapshot turns and the whole 3 s settle window -- the derivation is the sub-bullet below) and
  diffed AGAIN; only a disagreement present in BOTH samples is a failure. It is
  keyed by COMPARISON, not by message text, since a position or a score never repeats a value.
  Evidence it is measuring staleness rather than muffling defects: in the capture, every score gap
  over the limit (1900, 1550) read **exactly 0** after the re-settle, and every host-only id that
  was not a deferred-death release had resolved. **`--resettle 0` is its own mutation control and
  is RED on 6 of 6 seeds** -- so the confirm is doing real work, not decoration. Everything it clears
  is PRINTED under its join and counted into `converged=`, deliberately: a class that always clears
  on the second look is a finding about the game, not something to disappear into a green run.
  It never applies to `prov=` / `owner=` / type / extras length, which fail on the first sample.
  **What it does cost:** a first-sample mismatch on an entity that leaves the world within the
  window is unconfirmable and therefore dropped.
  - **`--resettle` is DERIVED FROM THE SLOWEST MECHANISM IT MUST OUTLAST, not picked: 300 frames
    (5 s).** Every remaining replication cadence -- the 1 Hz lives sync, a big world's ~1.2 s
    snapshot turn, the ~100 ms HUD/score packet -- fits several times over. (The original anchor
    was the ledger's 3 s `AwardSettleWindowMs`, under which a 2 s confirm left residuals on 2 of
    8 seeds; card af96bcc2 deleted the ledger, and 5 s stays because everything left is faster.)
    Anything that needs a LONGER confirm than this is not staleness and should fail.
- **THE SUITE NEVER RESETS, SO NONE OF ITS RESIDUALS IS RESET-CAUSED (card d6372279).** The host
  boots `?invuln` and `LoseLife` is host-authoritative, so `AllShipsDead` never fires and neither
  peer ever enters `GameState.Resetting`: measured `resets=0` on **every** `[net]` line of a
  `--level Level3 --cap 200 --cadence 40 --verbose` run (22 of 22). **This is the fact the
  tolerance-calibration card (`d108c459`) needs**, because it rules out the reading that the
  `is on the HOST and not on the joiner` residual is "reset-adjacent" -- it clusters on
  BattleSkull/EvilSkull because Level 3 is where those live, not because a checkpoint revert wiped
  them. Two dumps sampling different world instants is the remaining explanation.
  - **Consequence: the reset choreography is UNCOVERED by this suite.** Reaching it needs
    `eval KillShips` on BOTH peers (a client's own ship dying is not enough -- the host's
    invulnerable ship keeps `AllShipsDead` false) **and Medium+**, since `ApplyDifficultyPolicy`
    turns on `DirectRespawn` at Easy and that branch of `UpdateResetting` purges nothing and
    reverts no checkpoint. Both peers agree on the tier -- `MenuScene.NetLaunchMirror` calls
    `SetDifficultyTo` off `EvLaunch` -- so the two reset paths cannot diverge on it.
  - **What a real two-peer reset does, measured (same card, Level 2 and Level 3, Very_Hard).**
    Host time: `EvReset` applied on both at t; `UpdateResetting`'s ADC purge fires on the host AND
    the client at **t+4533 ms, the same millisecond** (`NetApplyReset` zeroes `_timer`, then both
    run the same 3 s + 1500 ms `Background.XFade` on game time, so the skew is RTT); the startup
    purge likewise; the host's FIRST post-reset spawn lands **2850 ms (L2) / 3850 ms (L3)** later.
    The client holds **zero** puppets at its own purge -- the host purges first and announces an
    `EvDeath` per removal -- and the settled post-reset diff is clean (11 ids vs 11, `prov` empty).
    So the "client wipes host-spawned entities inside the revert skew" hypothesis is REFUTED;
    see the comment at `GameScene.UpdateResetting` for why that purge must keep running anyway.
- **WHY TWO PROCESSES, and why that is not over-engineering.** The claim is "the joiner ends up
  with the host's world", which is a DIFF and needs both worlds to exist -- and one process holds
  one `Game.Components` (see the "TWO PEERS WITH INDEPENDENT WORLDS IN ONE PROCESS IS
  UNREACHABLE" bullet). Everything else automated here scripts a peer onto a wire and covers one
  leg; this is the only rig where the joiner runs `MenuScene.NetLaunchMirror`, warms and
  `Initialize`s the level itself, and sends its own `EvReady` -- i.e. the first half of an attach.
- **THE TRANSPORT IS THE EXISTING ONE.** `BroadcastChannelTransport` already IS the project's
  instant-local transport; what it lacked headlessly was a backend, since eahl stubbed
  `eaNet.open/send/close` as no-ops. `tools/headless/LocalSocketNet.cs` backs those three calls
  with a localhost TCP socket -- **eahl-only, nothing under `INetTransport` changed, nothing
  shipped changed**, and every existing two-tab `?net=` recipe becomes two-process-runnable for
  free. The port is derived from `?room=` so both sides agree with no configuration
  (`--net-port` overrides); a bind clash is REPORTED and survived, so a room-name collision
  between two agents reads as "the host never paired" with the cause named above it.
- **`?net=jiphost` / `?net=jipjoin` are the two boot roles.** The host holds an open transport
  with NO session (a listed game is plain single-player until a stranger arrives -- starting one
  early would fire every `NetSession.Active` branch for the whole run) and the first inbound
  frame arms the REAL `StartListedSession`; the frame itself is dropped, which is free because
  the hello repeats at 1 Hz. The joiner is a real `StartMenuSession` client, and
  `MenuScene.Initialize` puts it in `netMode` or `NetUpdate` would never reach
  `TakePendingLaunch`. **The joiner also needs `?netallowdebug`** -- `?net=` sets
  `DebugFlags.Active` and a menu session refuses its own pairing while that is set; `DebugFlags`
  says so out loud rather than waiving it. **The host RE-ARMS after each match**, which is what
  makes a soak of repeated joins possible at all -- a listed host dropping back to single-player
  and re-listing is production behaviour, not a rig affordance.
- **`eahl --nettime game` is REQUIRED for a two-process run.** `--nodraw` runs ~17x real time, so
  on the wall clock the wire's cadences (60 ms snapshots, 30 Hz ship, 1 Hz score sync) fire ~17x
  too rarely PER UNIT OF WORLD MOTION and the diff measures that artifact. Off by default so
  every existing probe is unchanged.
- **THE ORACLE IS GENERIC, and `NetJipDump` is the observable.** One dump per peer, keyed by
  netId: the base state (position, rotation, scale, frame, hp, dying) plus each entity's spawn
  and state extras RE-ENCODED through its own descriptor, plus the scenery/music line and the
  per-slot HUD. A key is SKIPPED when the entity's own declared seam says the game simulates it
  locally (`NetFrameLocal` / `NetSpinPerMs` / `NetScaleLocal` / `NetPathOffset`) -- so a skip is
  the GAME's statement, never a type name in the tool.
  - **AN EXTRAS RE-ENCODE IS NOT A CONSTANT, and the differ leans on knowing it.**
    `FlyingSpiderDescriptor`'s spawn anchor carries the swivel PHASE and `UfoDescriptor`'s spawn
    flags carry `hasbonus`, both of which drift in play -- so two correctly-built ends
    legitimately re-encode different spawn bytes and a byte compare cries wolf on every wasp.
    The extras are therefore compared for LENGTH (a structural mismatch is still real) and the
    dimension they were meant to cover is reported DIRECTLY as **`prov=`**, off the puppet
    layer's own `SelfHealed` flag -- and, one hop downstream, as **`owner=`**, the emitter's netId
    off the generic `INetEntity.NetOwner` seam (card 9a7ee4c0). Both are exact and compared
    exactly; `owner=` reads `-` on BOTH ends for a legitimately unowned beam. **Its one benign
    disagreement is an emitter's REMOVAL**, which is not simultaneous on the two peers: a beam
    drops its owner reference when its emitter leaves the world (`Lazer.OnComponentRemoved`, both
    ends), so a dump landing inside that lag reads `owner N vs -` with nothing wrong. One
    snapshot lane wide -- a persistent disagreement is the defect, a single join's is worth
    re-running before believing.
  - Continuous replicated values get tolerances and the tool PRINTS `maxpos=` on every join so a
    tightening regression is visible even while passing. Measured across 39 joins: 0.0-17.9 px
    at the default `--chunk 3`, which is the interleave's own skew (one peer has always stepped
    last) -- at `--chunk 30` a 0.16 px/ms UFO reads 79 px apart with nothing wrong.
  - **`--settle-after` is MEASURED, not picked: 300 frames.** That is past the joiner's own 1.3 s
    `GameScene.UpdateStartup` plus the 3 s `RecentRemovalWindowMs` the self-heal waits out, and
    it is where the id sets first agree (Level2 seed 7: 4/4 joins match at 300, 0/4 at 120).
    Going much higher stops testing the ATTACH -- at 600 the population has turned over and
    deleting BOTH `ReplayLive` calls changes nothing.
  - **The HUD is compared per FIELD, and a seat is a MIRROR rather than a match** -- slots are
    identity-mapped, so "host slot 0 = Keyboard, joiner slot 0 = Remote" is the pairing being
    correct. Levels and option counts are discrete and owner-authoritative, so they must agree
    EXACTLY (this is card c5228350's join-in-progress catch-up); score is owner-declared and
    adopted verbatim (card af96bcc2), so the compare is DIRECTIONAL: a replica above its owner
    is exact-fail (invented points), a replica behind it is one ~100 ms packet of staleness,
    covered by `--score-tol` -- a lag that STANDS under continuous fire rather than converging,
    which is why it is a tolerance and not a re-settle case.
  - **(HISTORY, superseded by card af96bcc2.)** Under the two-writer ledger design the score
    had to be compared as `pts - uns` (dump v3, card 94001db7 -- a client's display carried its
    provisional credits by design, so raw `pts` was a category error), with an `uns`-keyed
    limit and a run-level `peak joiner unsettled` control on top (card d108c459). One writer
    per slot deleted the quantity those corrections existed for; dump v5 dropped `uns=` and the
    control with it, and `pts` is compared directly above.
  - **A host whose level has ENDED reports `scene none`, and the loop stops there** rather than
    grinding out the remaining cadence against an empty world. The settle frames are charged to
    `--cap` too: a join steps the host through the joiner's whole level warm, so charging only
    the cadence made the cap under-count by ~3x and a 600 s run played the level out and then
    reported a dozen vacuous joins.

**THE DEFECT IT FOUND, NOW FIXED: A JOINER PURGED THE HOST'S CATCH-UP BURST** (card 9a7ee4c0).
It cost ~12 of 39 joins across all three story levels, and the mechanism is pinned, not inferred.

- **`GameScene.UpdateStartup` ran `Collection.Purge<AlienDrawableGameComponent>(standing: false)`
  on a CLIENT.** The joiner's scene comes up, `EvReady` goes out, the host replays -- and 1300 ms
  later the joiner deletes the lot. Nothing repairs it: a purge is not a gameplay death, so
  `NetPuppets.Components_ComponentRemoved` files no claim (the card-9ccfe295 re-announce path
  never runs), the id only `MarkRemoved`s, reads `LeftDead` for `RecentRemovalWindowMs` and then
  self-heals PROVISIONAL on default spawn extras -- permanently. Card 74403f83's exemption does
  NOT cover it: that one spares puppet ADDS from a STANDING purge; this one REMOVES what is
  already there. **The fix is that a client does not clear a field it does not own.**
- **The evidence, one id's whole life on the joiner's clock** (`--nettime game`, so both peers
  share it): self-healed at 2566 ms, correctly REBUILT by the reliable `EvSpawn` at 2616,
  `startuppurge client=True` at 3866 wiping it, and self-healed provisional again at 6916 --
  exactly `RecentRemovalWindowMs` later. Joiner metrics matched: `dupLive=0` (no rebuild ever got
  the chance) with a large `snapDead` (a batch removed LOCALLY).
- **THE PURGE IS A TICK BOUNDARY, NOT A BLANKET WIPE**, which is what explained the one join that
  split the `MarsBoss` twins (`prov=1` and `prov=0`). Everything the joiner holds at that ONE tick
  dies; everything arriving from that tick onward survives (the rx drain runs after `base.Update`
  in the same tick, and `standing: false` arms no filter). `Level2.spawnBosses` adds both twins in
  one HOST tick, but their two `EvSpawn` frames need not land in one joiner drain -- the transport
  delivers per socket read / per channel message -- so the boundary can fall between them. Moot
  now: the fix removes the boundary.
- **It cascaded into card 9ccfe295's precondition**, which is why `owner=` is in the dump. In the
  same join both `Lazer` puppets read `spawn=0000` against the host's `6f00`/`7000` -- **owner = no
  emitter**, because `FindPuppet` could not resolve a MarsBoss that did not exist yet: the
  ownerless beam that made a big laser UFO shoot itself dead on the joiner.
- **`ReplayLive` on `EvReady` is REAL again.** Before the fix, deleting it changed nothing
  measurable -- the burst was being thrown away regardless. Deleting it now costs 8 provisional
  puppets on a default soak, so it discriminates and the matrix row below is updated.
- **The `owner=` leg's positive control is NOT here** -- it is `eaNetIdReuse` section 7, pinned by
  `tools/headless/probes/net_id_reuse.txt`, which builds an owned beam on a host world and on a
  client world and asserts the dump names the emitter through each of the two registries. Only
  `Boss` and `MarsBoss` fire an owned beam, and over a full soak (~39 joins) every `Lazer` that
  reached a settle dump was a `GameScene` warm-up prime, legitimately ownerless on both ends -- so
  a run-level "an owner was compared" assertion here would be red on a sampling coincidence. The
  suite REPORTS its material instead (`owners=` per join and per run).

**MUTATION MATRIX, including the three that did NOT discriminate** -- each of those is a finding
about the code, not a weak assertion, and re-deriving them is a waste of an afternoon:

| mutation | result |
|---|---|
| delete `NetScene.Current?.NetReplayCatchUp()` from the `EvReady` handler | **RED**, naming the scenery line (host `speed=-0.012` vs joiner `speed=-0.6`). Needs a LATE join -- `--cadence 200 --cap 500`; at 20 s Level 2's scenery has not moved off its initial state and there is nothing to replay |
| the tool's own `--selftest` (a joiner on a dead port; a `--cap` at the cadence) | **RED** on both arms -- the vacuity control, committed rather than performed once |
| restore the unconditional `Purge<AlienDrawableGameComponent>` in `GameScene.UpdateStartup` (i.e. undo card 9a7ee4c0) | **RED**, 19 `PROVISIONAL` lines on a default soak against 0 with the fix. The card's own mutation |
| delete `NetIdRegistry.ReplayLive()` from the `EvReady` handler | **RED since card 9a7ee4c0**: 8 `PROVISIONAL` lines on a default soak. It used to read "no change", because the burst was purged either way -- the row was measuring the defect, not the call |
| drop the owner resolve in `LazerDescriptor.CreatePuppet`, or the host-side resolve in `NetJipDump.OwnerId` | **RED in `net_id_reuse.txt`**, one arm each and independently (client arm / host arm) -- that probe is where the `owner=` leg is proven, since this suite rarely sees an owned beam |
| force `CreatePuppet`'s extras length to 0 (every puppet built on defaults) | **no change.** The differ dropped extras-CONTENT comparison for the drift reason above, and `prov=` reads the self-heal flag rather than the bytes -- so an artificially defaulted puppet built from a real `EvSpawn` is invisible. Closing it means caching the bytes that CROSSED THE WIRE on both ends |
| revert `Wall.NetScaleLocal` to false (`--level Level3 --host-extra "&wallsonly"`) | **no change**, and the reason is that another card already fixed it: protocol v19 raised the wire scale to 1/4096 with rounding, so the error `NetScaleLocal` defends against is now **0.090%**, far under any sane scale tolerance. `eaNetWalls` remains the pin |
| drop `ReplayLive`'s `EvDying` re-announce | **not reached.** It needs a join timed into a 2.5-5 s deferred-death window, which the orchestrator does not control. `eaNetDeathFx` pins that beat directly |
| ~~force `NetJipDump`'s `uns=` to a constant 0~~ | **RETIRED with the field (card af96bcc2)** -- dump v5 carries no `uns=` and the compare reads `pts` directly. Kept so its absence is not mistaken for an oversight; the score leg's live mutation is the row below |
| stop writing the score into `MsgHudState` (`hudTxScores` left at 0) | **RED, 3/3 seeds** (card af96bcc2, `--level Level2 --cap 240`, every join with a nonzero host score): the joiner adopts 0 and the directional compare reads `replica N behind its owner=host` far past the limit. The verbatim-adoption policy's deterministic pin is `eaNetScore.test` |
| drop the `gone` line (`NetPuppets.RemovedIds` returning empty) | **RED, 3/3 seeds** (card d108c459). It is the ONLY evidence that excuses a host-only id, which is why the host-side `dying` arm was dropped: with both in place this mutation did not even bring the class back |
| halve the received hp before it is applied AND recorded (`state.Hp / 2` folded in at the top of the hp block) | **RED, 3/3 seeds** (re-confirmed in this exact form during review) -- the wire-to-wire compare catches a value that did not survive delivery, and the clamp invariant fires with it. Halving ONLY `NetApplyHp`'s argument would trip neither leg: `LastAppliedHp` records what was received, so both `hpwire` values still agree and the halved live hp sits safely BELOW them -- that is the wrong-but-lower APPLY this suite structurally cannot see (see the clamp bullet above); `eaNetSnap` section 7 owns it |
| stop recording `hpwire` (`PuppetInfo.LastAppliedHp`) | **RED, 3/3 seeds**, on the run-level `no entity was compared on hpwire` control. Without it the field reads `-`, which SKIPS the hp comparison rather than failing it -- the same silent-deletion shape the `uns` control exists for |
| ~~force `NetApplyHp` to assign a wrong-but-lower value~~ | **RETIRED, and here rather than deleted so its absence is not mistaken for an oversight.** Under the wire-to-wire compare it cannot bite: from the host's side a lower value is indistinguishable from the local damage a joiner deals with its own bullets. Replaced by `eaNetSnap` section 7, which asserts the assignment in-process |
| `--resettle 0` (the confirm disabled) | **RED, 6/6 seeds** -- the confirm carries real weight, not decoration |
| restore the card-9a7ee4c0 purge, re-run against the CONFIRM | **RED, 3/3 seeds, 23-29 `PROVISIONAL` lines.** The row above proves the confirm bites; this one proves it does not swallow a real defect on the second look |
| revert `ScoreVisualiser.NetSetScore` to a `max(local, adopted)` ratchet | Under one writer the replica never credits anything locally, so max(0-or-stale, declared) == declared and the ratchet is a NO-OP here by construction -- **the suite cannot and need not see it**. `eaNetScore.test`'s ratchet control is the deterministic pin (it drives the ratchet over a genuinely two-writer stream, where it still drifts) |

**WHAT IT DELIBERATELY DOES NOT COVER**, each because something else already pins it: the
background APPLY path (`netbg_catchup.txt`), the slot-grant NEGOTIATION (`eaSlotTest`), the
wall's collision-tile derivation (`eaNetWalls`), the ship-puppet lane (`eaNetFire` /
`eaNetMotion` / `eaNetResetSpawn`), and the option-count catch-up's own arithmetic
(`eaNetPickup` leg 6). It also never sees WebRTC, the signaling server or the room-code flow.

`tools/headless/probes/net_jip_dump.txt` is the half that fits `run_probes.py` -- that the dump
reports at all, and that `?net=jiphost` arms a listed host -- so a dump that silently stopped
reporting cannot make the tool green.
