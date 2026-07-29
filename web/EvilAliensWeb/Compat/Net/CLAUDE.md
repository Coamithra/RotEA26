# CLAUDE.md — web/EvilAliensWeb/Compat/Net (the online co-op net layer)

Moved out of `web/EvilAliensWeb/CLAUDE.md` so it loads only when working on the net layer.
The parent file has the rest of the game/engine notes; `NetStatusMenu.cs` lives in
`Game/EvilAliens/` but belongs to this feature. Design doc: `plans/stage11-online-coop.md`.

Path bases below, since the text was written one level up: `plans/`, `tools/` and `server/`
are repo-root-relative; `Compat/`, `Game/` and `wwwroot/` are relative to `web/EvilAliensWeb/`.

Distributed-authority state replication (NOT lockstep): each peer owns its own ship
completely (input read untouched, zero added latency); the wire carries ship STATE, never
inputs; the other peer's ship is an interpolated puppet.
**Shipped so far** -- grounded in the board's Done list; the card ids point at the bullets below.

- **Stages 11.1-11.4:** net skeleton + ship mirroring over a BroadcastChannel loopback (11.1);
  host world authority -- client enemy puppets, world snapshots, generous claims, score sync
  (11.2); level-script beat replication, host-broadcast reset/victory, replicated pause,
  TeamChallenge soft tether (11.3); real WebRTC transport, room-code signaling on the shared
  VPS, menu-driven Host/Join lobby, build-hash handshake, match-end semantics (11.4).
- **Stage 11.5 round 1** (card `4717d3cf`): the hardening pass -- powerup pickups replicate to
  the collector's HUD slot, ONE match-end path, a drop-verdict grace window with a
  waiting-for-peer banner, a graceful reject, and the WebcamAliens net-lobby refusal explains
  itself.
- **Public game browser + join-in-progress** (`2001fbd8`; two-window pass `c0398370`): a running
  single-player game LISTS itself, strangers browse and join mid-level, ping is measured; a late
  joiner is caught up on deep mid-level background/doodad state (`45a4e48d`).
- **Four seats -- local co-op AND online co-op at once** (`4d904410`): host-allocated,
  identity-mapped roster slots, couch players on either peer, 4-wide score sync. Hardened by the
  slot-grant negotiation (`c0229c57`, protocol v8) and the stale-menu-roster reset (`ee96ea61`).
  **4-player online already works** as two peers with a couch partner each (`2e0f908b`); 3-4
  separate MACHINES do not.
- **Host kick / kick+block** (`0b8a300b`): the host's only agency under a remote pause, on a
  self-reported peer-identity token (protocol v6).
- **Score + per-slot HUD correctness:** the awarded AMOUNT is replicated, not the combo
  (`b0ab09ec`, v7); a slot's combo and powerup progression belong to its OWNER (`1a3ad45a`, v9,
  `MsgHudState`); a late powerup claim no longer strands a HUD icon (`a8c92fb9`).
- **Diagnostics + rigs:** fake lag/loss/jitter (`40334a8f`), the snapshot unknown-id split and
  `snapTurn` (`48ab9b2f`), decorative swarms as one on/off beat (`9a3175d0`, v10), the
  standing-purge-filter races (`74403f83`), the signaling server deployed (`8c3c18da`).

**Remaining.** The TURN go/no-go and interpolation/jitter feel are the only Stage 11.5 pieces
still open, and both are gated on real-network playtests this rig cannot run -- card `4717d3cf`
sits in the board's "For me" column for exactly that, and card `6fb406bc` (Stage 11.11) carries
the same TURN question. N-peer online (3-4 separate MACHINES) is designed but unbuilt:
`plans/4p-online-coop.md`, Stages 11.7-11.11 in "Later". Open net cards in Backlog: `1cd47879`
and `ac375753` (live two-window passes), `25ad0659` (headless net sim + de-static refactor).
Mid-level whole-scene switches (InsaneBossI) remain a known JIP gap -- card `ca4fd94f` is marked
Done on the board but no such code shipped; see the JIP gaps bullet.

## Debug flags

- **Flags:** `?net=host` / `?net=join` opt a session in (in `Active`); `?room=<name>` picks
  the loopback room (BroadcastChannel `eanet-<room>`, default `dev` -- parallel test pairs
  must use distinct rooms); `?netlog` = verbose per-event logging; `?aiplayer` forces the
  LOCAL ship onto the existing AI branch (`PlayerShip.EffectiveController`) for unattended
  soak tests; `?aifriends=<0-3>` (pair with a `?level=` boot) seeds `Settings.Friends` so the
  host's Mechanical-Friends AI ships auto-join without the cheats menu -- the two-tab seam for
  AI-friend replication (note the budget is `Friends+1` TOTAL ships incl. the remote, so with a
  peer connected you need `aifriends>=2` to spawn any AI friend); `?netscript` (pair with `?level=Level1`) replaces the level's event list with
  a compressed ~60s script firing every replicated beat type (message, warning, background
  ops, checkpoints, music switch, victory) -- the purpose-built two-tab verification for
  script replication (`GameScene.PopulateNetScriptTest`). Card 11.4 adds `?rtc` (a
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
  free Google servers, NO TURN in v1 (~10-15% of NAT pairs get a clean "could not
  connect"; the TURN go/no-go is still OPEN and needs a real-world connect-failure rate --
  carried by card `4717d3cf` (Stage 11.5, "For me") and card `6fb406bc` (Stage 11.11)).
  Nothing above the interface may assume loopback
  reliability.
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
- **Menu lobby (card 11.4):** main menu "Online Co-op" -> Host Game (shows the room code
  + "waiting") / Join Game (HTML code-entry overlay outside `#app` -- `eaRtc.promptCode`).
  `Compat/Net/NetLobby` owns the pre-session flow (JS phase queue drained by
  `MenuScene.NetUpdate` on the game tick; `NetStatusMenu` = the re-textable
  ConfirmationMenu panel). On connect the HOST picks level+difficulty through the NORMAL
  select screens (netPickMenu -> the shared selectors; their OnExit reroutes in net mode;
  WebcamAliens selection is refused, and the carousel swaps its briefing for the reason) and `EvLaunch` mirrors the launch on the client
  (`MenuScene.NetLaunchMirror` -- same fade/warm path, difficulty locked, starter
  Keyboard). Turbo is forced to 100 while a session is Active (`Game1.Update`).
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
  Match-end: any player leaving a MENU session (quit, tab close, drop,
  victory/game-over wind-down) ends it for both -- scene-down edge or `PeerLost` sends
  `EvLeave`/notice, `NetSession.Stop()` tears down (registries disabled, state reset,
  restartable), `GameScene.NetApplyPeerLeft` force-exits a running level (except in
  Victory/GameOver, which finish locally), and the menus surface `TakeMenuNotice()`.
  `EvReady` (client scene-up edge -> host `ReplayLive`) covers the lobby launch race
  where one peer out-warms the other; world messages are gated client-side while no
  GameScene is up. URL `?net=` sessions keep the old semantics (session survives peer
  loss, reconnect works).
## Protocol, NetIds & the replicable set

- **Protocol (`Compat/Net/NetProtocol`, little-endian binary, 1-byte type, v10):** the 3
  layers -- `MsgShipState` (~30 Hz real-time cadence: pos, vel px/ms, last-fire aim,
  alive|firing flags, shotsPerSec, bulletLife -- 31 B), `MsgWorldSnapshot` (see the
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
  per-entity spawns, i.e. see empty scenery -- a real incompatibility, hence the version move).
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
  `ControlDevice.RemoteFriend` puppets -- see the AI-friend bullet below). Because the script never runs on a client,
  `GameScene.spawnPlayerNormally` reads as true on a join peer -- a scripted no-ship phase
  (Level1's intro hands the ship spawn to its `demo_OnFinished` beat) would otherwise
  leave the client shipless forever; the client's ship always uses the generic
  startup/respawn path and the intro choreography stays host-only. Initial
  background/music are local; mid-level script beats (messages, music switches,
  boss-phase choreography) do NOT replicate yet -- that is the next card.
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

- **GENEROUS at-least-once claims -- no arbitration, no rejection path.** Kills: local
  hit-testing runs the REAL per-type death on whichever peer observed it (explosion,
  sound, score, combo paid locally); the client's removal seam sends `EvClaim(netId,
  killerSlot)` for every gameplay death (`IsDead` distinguishes `Die()` from teardown
  purges). Host on a claim: entity alive -> real kill via `KillableAlien.NetKill` with a
  scratch-Bullet killer carrying the claimant's slot (authoritative children spawn there
  and replicate); already dead -> pay the claimant once from a bounded recent-death
  record. Host broadcasts `EvDeath(netId, killerSlot, pos, points)` for every replicable
  removal (killerSlot from the `NetSession.NoteKill` hook in KillableAlien.HitBy);
  client: live puppet + killer -> local NetKill (FX + credit), no killer -> silent
  despawn, already dead -> pay the killer once. Per-(netId, slot) paid ledgers both sides
  = every distinct claimant credited, nobody credited twice. Powerups are the same claim
  shape: the real PlayerShip pickup runs instantly on the collector (a
  `NetSession.NotePowerupTaken` hook attributes it), first claim despawns the entity,
  overlapping collectors inside the RTT window BOTH keep it.
- **Score/lives: the AWARDED AMOUNT is replicated, not the combo (card b0ab09ec).** `EvDeath`
  carries what the host actually credited, per slot (`f32 x MaxSlots`), and that figure is
  authoritative on the client in every branch. Lives stay verbatim off `EvScoreSync`.
  - **Why: every kill is credited on BOTH peers, each with its own combo multiplier.**
    `comboModify = amount * (1 + combo/20)`, and the combo counter is a purely local
    simulation -- the only thing that raises it is a local bullet's first hit
    (`Bullet.CollidesWith` -> `SustainCombo`), and on a client those bullets hit frozen
    puppets interpolated ~100ms behind the host's real entities. So the same kill is worth a
    different number on each screen.
  - **`max(local, host)` adoption made that unbounded, and is GONE.** It kept every positive
    excursion of the error and discarded every negative one, so even a perfectly *unbiased*
    per-kill difference integrated into one-way drift (measured in the 11.5 playtest: a slot
    the host had at 294 read 304 on the joiner, and climbing). The old note here claimed this
    "self-corrects upward" -- it did not; it was a ratchet.
  - **Replicating the COMBO COUNTER instead would not have worked**: combo changes up to
    ~10x/second, so any replicated copy is stale by at least the latency and the credited
    numbers would still differ. The award is the only thing that can be exact.
  - **A client's own kill is credited instantly but PROVISIONALLY** (`NetScoreLedger`): the
    amount is booked until the host's `EvDeath` for that netId replaces it with the
    authoritative figure. `EvScoreSync` then adopts `host + unsettled`. Both ride the ORDERED
    reliable lane, which is what makes that sum exact either way round -- an `EvDeath` seen
    before a sync is inside that sync's number and off the books, one seen after is outside it
    and still on them. Carrying `unsettled` is also what stops verbatim adoption from
    sawtoothing: the host's 1Hz number never contains the client's in-flight claims, so
    adopting it bare would erase the last second of their own kills once a second.
  - Provisional entries EXPIRE after `AwardSettleWindowMs` (3s) because one path never echoes
    a figure back: if the host's copy was already dead when our claim landed it pays us from
    its recent-death record without re-broadcasting. Expiring lets the next sync land on the
    host's exact number instead of staying inflated forever.
  - The real death path still runs on the client for the FX, but `NetSuppressAward()` claims
    the award slot FIRST so its `AwardScore`/`AwardScoreToAll` no-ops -- otherwise it would
    re-derive the amount from this peer's combo. **Any new client-side death path must do the
    same**, or it silently reintroduces the divergence.
  - `AwardScoreToAll` (every boss) pays each seated slot with THAT slot's own multiplier,
    which is why the wire carries a per-slot array rather than one number. Wire width: the
    field went `u16` base-points -> `f32` per slot (protocol **v7**) because a combo-modified
    award overflows a ushort -- a 10000-point boss at a routine 40x combo is 30000, and
    `comboModify` has no ceiling.
  - **The combo COUNTER is no longer local -- see the per-slot HUD state bullet below.** It was
    left local by this card ("cosmetic, only the score is reconciled"); card `1a3ad45a` found
    that framing was wrong and replicated it.
  - **Verify with `eaNetScore.test()`, not two windows** (`NetScoreLedger.SelfTest` +
    `NetPuppets.WireRoundTripTest`). It drives the real policy on a virtual clock, and runs
    the OLD `max()` adoption over the identical kill stream first -- a green tick means
    nothing unless the same input is shown to break the old policy, because the failure is a
    slow drift no frame or screenshot can show. It also asserts the injected per-kill error is
    UNBIASED, so the drift it demonstrates is the ratchet and not a stacked deck. The second
    section round-trips a real `EncodeDeathEvent` through `ApplyAwards` against the live
    `ScoreVisualiser` (wire offsets, fresh-pay vs settle, at-most-once).
  - `eaScore()` dumps per-slot score/combo/unsettled -- the readable way to compare two peers.
    The `[net]` line gains `scSkew`/`scSkewMax` on the JOIN side only (the host is the
    authority and never adopts): displayed minus `host + unsettled` at each sync, worst ACROSS
    the slots, which should sit at 0. (Recording it per slot instead would leave the LAST one
    standing -- slot 3, unseated in any 2-peer session, so a hard-coded 0.0 that looks like
    proof.) Measured over a two-peer run: `scSkew=0.0` steady state, and `scSkewMax` held at
    10.0 while `clTx` grew 20 -> 67 -- i.e. the worst deviation is one kill's correction and
    does NOT accumulate with kill count, which is exactly the property max() lacked.
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
    `NetSetHudState` therefore also refreshes that slot's `combotimer` while the owner reports a
    live combo, because the readout's alpha is driven by its `TimeLeft`.
    It asks the ROSTER, not a live ship -- a slot's combo and levels outlive its ship (they
    persist across a death and respawn), so a ship-keyed test would flip while the player waits
    to come back. **Offline it is true for every slot**, which is what keeps single-player and
    local co-op byte-identical. The decision is split into a pure `OwnsSlotCore(active, seat)`
    so the test can table-drive `Remote`/`RemoteFriend`/unseated -- offline the predicate is
    unconditionally true, so a live-roster-only test could never reach those cases at all.
  - **`MsgHudState` (0x12, stream lane, ~10 Hz, BIDIRECTIONAL) carries the owner's version**:
    `[type][count]` then `[slot][combo:2][activeType][progress][level x 5]` per owned slot.
    Protocol **v9**. **combo is a USHORT and that is load-bearing** -- the host SPENDS the
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
    there and must stay so** -- slow motion is deliberately local, which is the same reason the
    puppet driver dead-reckons on real time.
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
  - `MsgFriendState` is now BIDIRECTIONAL and carries every locally-owned non-primary ship
    (AI friends *and* couch players) -- `ControlDevice.RemoteFriend` means "network-driven
    extra ship", whoever owns it. `EvBlast` gained a slot byte (a couch player's bomb used to
    detonate on the peer's PRIMARY puppet) and `EvScoreSync` widened from 2 slots to 4.
  - `DriveFriendShip` ADOPTS a ship the scene spawned into its slot (`SpawnAllPlayers` respawns
    every seated slot after a reset, puppet slots included) -- without it the re-spawned puppet
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
- **Remote ship:** `ControlDevice.Remote` (APPEND-ONLY enum position). Joins via
  `oracle.AddPlayer(Remote)` on the first alive stream (or is spawned by the GameScene's
  own SpawnAllPlayers reset flow -- NetSession adopts either). `PlayerShip.Update` case
  Remote -> `NetSession.DriveRemoteShip`: position sampled from `ShipStateBuffer`
  ~100 ms behind the newest sample (velocity-extrapolated max 250 ms on underrun), speed
  zeroed; shots re-fired locally through the real `FireAt` path from the replicated firing
  state; bombs arrive as EvBlast -> `NetDoBlast` (no local bomb-count gate). Remote ships
  take NO local damage (owner decides its own hits; death arrives as the alive-flag edge ->
  local explosion FX, slot stays reserved for respawn) and CANNOT take powerups locally --
  the owning peer collects on its own screen and the pickup arrives as a claim. Hues need no
  fixing up since card 4d904410: slots are host-allocated and identity-mapped, so a slot's
  colour is the same on both screens by construction and the old join-side hue swap is gone.
  (Caveat: `MenuScene.changeColor` lets a player recolour a slot and `PlayerInfo.Reset` doesn't
  restore it, so "host white / joiner purple" holds for DEFAULT colours; nothing normalises the
  two peers' hue tables.) The puppet's render clock advances on REAL time (never turbo/slowmo/
  hit-stop-scaled game time) -- a local hit-stop must not drag the interpolation point.
## Metrics & verification

- **Verify with LOGGED METRICS, not screenshots** (`Compat/Net/NetMetrics`): a parseable
  `[net] role=... pops=... snapTx=... clRx=...` line every 5s. Healthy: buf ~100ms,
  extrap ~0, pops 0 (pop = a step no ship could physically make: > 2x MaxSpeed x realDt
  + 3px), drop/dup/ordViol/seqGap 0; on the world side, host `snapTx` climbing, client
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
  **`snapUnk` climbing is not by itself a leak -- read the SPLIT, never the total** (card
  48ab9b2f). Three unrelated things make a snapshot entry "unknown", and the `[net]` line breaks
  them out as `snapNew`/`snapDead`/`snapBad` (`snapUnk` remains their sum):
  - `snapNew` = an id we had never seen, which the self-heal REBUILT from the snapshot. The
    unreliable stream lane routinely outruns the ordered reliable one, so a fresh spawn's first
    correction can beat its `EvSpawn`. **Benign, and it tracks the world's SPAWN rate** -- in a
    continuously spawning fight it never stops climbing, which is not a fault.
  - `snapDead` = an id removed HERE inside the 3s `RecentRemovalWindowMs`, deliberately left
    dead. **Benign, and it tracks the world's TOTAL removal rate.** The old note here tied this
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
  rate you hold), and the roster simply does not depend on cadence -- once `PeerStalled` the
  friend timeout stretches to `PeerTimeoutMs + PeerGraceMs`, and a timed-out friend **keeps its
  seat** by design (`NetSession.Friends.cs`). It does NOT extend to anything timing-derived:
  `pops`/`pupPops`/`buf`/`extrap` off a hidden or unfocused tab are meaningless (the FPS HUD
  says so on its own readout), so every smoothness or feel verdict still needs two focused
  windows.
## Level-script beats, reset & victory

- **Script beats replicate at the side-effect PRIMITIVES (card 11.3), never per level:**
  the level script only runs on the host, so its observable side effects are hooked where
  they happen and mirrored as reliable events -- `MessageEvent`/`UnlockEvent` at their
  banner spawns (the unlock is also GRANTED on the join peer -- it played the level too),
  the mid-level `Background` ops (`SetSpeed`/`Queue*`/belt slowdowns/`SetAlienBase2..6`;
  the wire opcode enum `NetBackgroundOp` is APPEND-ONLY; Initialize-time setters are NOT
  hooked -- both peers run their own scene Initialize), `SoundManager.PlayMusic/StopMusic`
  (client applies via `NetApplyMusic`, deduped against the playing cue so the boot-time
  track never restarts), and the checkpoint callback (client mirrors `score.Save()` so a
  later reset restores the same baseline). Any future boss code calling these primitives
  replicates for free. `CrossFade` is deliberately NOT hooked (it belongs to the reset
  flow, which each side runs itself).
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
## Pause, tether & coverage gaps

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
- **World-authority coverage gaps (follow-up to card 11.2):** the replicable set was extended
  to the enemy/boss types 11.2 left host-only -- PlasmaBall, the paratrooper family
  (ParatrooperAlien/ParatrooperBrain/Parachute), FakeBoss, SpiderBoss, BrainBoss,
  SpiderHelperMothership -- as `NetTypeRegistry` descriptors 21-28 (append-only;
  `Compat/Net/Descriptors/DescriptorsCoverage.cs`). The enemy laser-CHARGE glow (a child
  `LazerGenerator` the emitter draws by hand) now replicates too: rather than making
  LazerGenerator itself replicable (it is also the player-summon glow), the SweepUFO / MarsBoss /
  SpiderHelperMothership descriptors stream a tiny charge state and the puppet rebuilds a local,
  silent copy into the emitter's own generator field (`AlienDrawableGameComponent.NetDriveExtras`
  driver hook + `Compat/Net/NetChargeGlow`). The fired beam already replicated as its own `Lazer`.
- **AI "friend" ships replicate (host-authoritative), follow-up to card 11.2:** the Mechanical
  Friends cheat is re-enabled in net sessions -- but ONLY the host adds AI friends (it runs the
  real AI, whose enemy kills already replicate), and only after the client's Remote ship has
  taken its slot. The host streams each friend (`MsgFriendState`, slot-tagged) and the client
  shows it as a `ControlDevice.RemoteFriend` puppet (`Compat/Net/NetSession.Friends.cs`): its own
  per-slot jitter buffer/interpolation clock (a copy of the single-remote path, kept ISOLATED so
  it can't regress it), IDENTITY slot mapping (the puppet lands in the host's slot so per-slot
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
    while that slot's `powerupactive` is set. Only the INDICATOR is mirrored -- the Blast/bomb
    count deliberately is not, because the spend side (`NetDoBlast`) does not decrement it
    either. A slot off the wire must be bounded by `ScoreVisualiser.SlotCount` (4), NOT the 8 of
    the claim ledgers' PaidMask.
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
  remote puppet lives (LoseLife triggers on AllShipsDead); the session is exactly two PEERS
  (see the sub-bullet below); DevCommentEvent commentary is not replicated (profile-local
  setting).
  - **Two PEERS is not two PLAYERS -- 4-player online co-op already works today** (card
    2e0f908b), as two consoles with a couch partner each; the four-seat roster in the
    `?netlocal` bullet above IS that, measured. What does not exist is 3-4 separate MACHINES.
    The player dimension is already 4-wide everywhere (`Oracle.MaxPlayers`,
    `ScoreVisualiser.SlotCount`, slot-keyed `MsgFriendState`, `EvScoreSync`, the claim
    ledgers); only the peer dimension is 2-wide, across five layers. Feasibility answer,
    per-layer blocker list and the N-peer design (star/host-relay, forced by the no-TURN
    connection math) are in `plans/4p-online-coop.md`. Boss puppets are
  best-effort (the harness caveat): deep Update-reached attack poses may diverge until their
  state extras grow (the SpiderBoss debris death + BrainBoss/FakeBoss multi-phase asplode do not
  play on the client -- an attributed remote death removes the puppet). The time-scaling half of
  the old first-wipe `pupPops` burst is FIXED (the puppet driver now dead-reckons on real time,
  above); if a residual first-wipe burst ever shows, it's the reset/id-churn transition (purge +
  checkpoint replay), reproducible in the headless two-peer net sim's reset scenario, not the
  puppet clock.
## Public game browser & join-in-progress

- **Public game browser + join-in-progress (card 2001fbd8, design `plans/net-game-browser.md`):**
  a running single-player game can be LISTED so strangers find + join it, with NO `NetSession`
  constructed until someone actually arrives.
  - **One eligibility predicate drives everything** (`Compat/Net/NetListing.ComputeEligible`):
    any empty player slot (`oracle.Players < Oracle.MaxPlayers` -- card 4d904410 relaxed this
    from `== 1`, so a COUCH game with a spare seat lists too and the browser's players column
    genuinely varies 1..3) + `Settings.AllowOnlineJoins` (new Option,
    **default ON**) + no cheats/`DebugFlags.Active` + level not `WebcamAliens`/`TeamChallenge`
    + no session already up. The SAME predicate gates the listing, the beacon, and the pause
    indicator, so they can't disagree. `NetListing.Tick` runs each tick from
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
    third string `Room code: XYZAB` while listed, and its 4-cycle stop is suppressed, so the
    code surfaces ~every 15 s (the existing intermittent rhythm, never a static banner). The
    `bool showPressStart` became an index `promptPhase` (drawn `% (listed ? 3 : 2)`).
  - **Flags:** `?gamebrowser` boots straight to the carousel with injected FAKE entries (no
    server) for a screenshot; `?netjip` lets a `?level=` (`DebugFlags.Active`) host list anyway
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
    remoteShip=1` and `buf=` ~100ms BOTH sides, `drop`/`sgap`/`ordViol`/`seqGap`/`extrap` 0,
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
    best-effort puppet limit; public-list abuse surface (rate limiting / hiding a room). (The
    deep mid-level background/doodad gap is largely closed -- see the catch-up bullet below --
    but a RESIDUAL piece remains: the whole-scene setters `SetSpace`/`SetMars`/`SetAlienBase`
    are Initialize-time and unhooked, yet `InsaneBossI` calls them MID-level (`GoAlienBase`/
    `GoSpace`/`GoMars`), and that level is listable. A peer joining after one of those still
    sees the scene the level started in.)
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
    a leg the level never fired is simply absent, and a PASS must not be read as covering it (the
    `SetAlienBaseN` leg has no rig: `?netscript` is Level 1, whose `SetSpace` scene has no base
    layer to switch). `eaNetBg()` alone dumps the live state for a two-window comparison. Both are
    console-only; the self-test is destructive (Reset re-runs the hyperspace entry).
  - Music RATE (`SetMusicRate`, the BrainBoss HP sweep) still does NOT replicate -- it is driven
    per-tick from a client-frozen boss `Update`, so it belongs to the mid-boss puppet-fidelity
    follow-up, not here.
