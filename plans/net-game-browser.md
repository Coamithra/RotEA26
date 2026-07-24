# Net: public game browser + join-in-progress (card 2001fbd8)

Follow-up to Stage 11.4 (`f74a2317`, branch `feature/net-real-transport`). Builds on the
WebRTC transport, the room-code signaling server, and the 11.2/11.3 replication layer.

## The core idea

**A game is listable exactly when it has an empty player slot** — the same condition
(`i >= oracle.Players`) that makes that slot draw a prompt instead of a score. Slot free ⟺
joinable ⟺ beacon eligible, so the listing, the beacon and the pause indicator can't disagree.

Careful about what this does *not* say: the empty slot persists for the whole level, but the
`PRESS START` prompt in it does **not**. `ShowStartMessages()` starts a 5 s repeating timer
(text visible ~2 s per cycle: 500 ms fade in, hold, fade out, 3 s blank), and after 4 cycles
`showPressStartTimes >= 4` stops it — after which the empty slot draws nothing for the rest of
the level. What the prompt gives us is the right slot, position, fade cadence and cached-chrome
draw path; the beacon has to *create* its own persistence by suppressing that stop.

## Listing ≠ session (the key architectural decision)

`NetSession` is **not** constructed while a game is merely listed. A listed-but-unjoined game
is a plain single-player game with one lightweight signaling WebSocket open. This preserves
every single-player behaviour (AI mechanical friends, no score sync, no pause replication,
no Turbo lock) until a stranger actually arrives. `NetSession.Start(host)` happens
mid-level, at the moment of pairing.

It also keeps the spirit of the "no `?net` flag = the net layer is never constructed"
invariant: what a listed game opens is a WS to the signaling server, not the replication
layer.

**Note it does break 11.4's "single-player never touches any server" invariant** — knowingly,
per the card's cheeky default-on. That trade is the card's whole premise; the mitigation is
the Options toggle + the pause-menu indicator below.

## Eligibility — when is a game listed?

All of these, re-evaluated whenever they change:

- `Settings.AllowOnlineJoins` is on (new, **default true**).
- Exactly one active player, slot 2 free. A second *local* player pressing Start delists.
- No cheats (`Settings.CheckForCheats()`) and no `DebugFlags.Active` — a cheating or
  debug-flagged host would change the joiner's game (Turbo is forced to 100 in a net
  session, which would alter the host's own run mid-level).
- Level is net-eligible: **not** `WebcamAliens` (camera is the controller — excluded by
  11.4) and **not** `TeamChallenge` (needs two seats already).
- No net session already active (a 11.4 private lobby host is not double-listed).

The same predicate gates the beacon and the pause-menu indicator, so they can never disagree.

## Server (`server/signal/main.py`) — registry on top of the relay

`Room` grows listing fields: `listed`, `level`, `difficulty`, `players`, `proto`, `hash`,
`last_beat`. The server also keeps a registry of **browser sockets** (see Ping below) — sockets
that belong to no room and only list and ping.

New messages (host → server):

| Msg | Effect |
|---|---|
| `{t:"list", level, difficulty, players, proto, hash}` | set metadata + mark listed (idempotent; also the update path when the host's level/difficulty/player count changes) |
| `{t:"unlist"}` | hide from the browser, room stays joinable by code |
| `{t:"beat"}` | refresh `last_beat` (~30 s cadence) |

Listings are served over the browser socket's `browse` message (below), not a REST endpoint.
The server filters on `proto`+`hash` so incompatible clients never see an unjoinable game (the
card's "filtered out server-side"). Entry: `{code, level, difficulty, players, ageSec}` — ping
arrives separately, per host.

Two lifecycle changes 11.4 must give up for this to work:

1. **The host keeps its signaling WS open for the whole session.** 11.4 hangs up once the
   DataChannels are live, and `main.py`'s `finally` deletes the room on that disconnect.
   A listed host stays connected; hanging up = delisting = room gone, which is the correct
   semantics anyway.
2. **`ROOM_TTL_SECONDS` (600) can't expire a live game.** TTL now applies from `last_beat`,
   not `created`, so a long level doesn't vanish mid-play. Unlisted 11.4 lobby rooms keep the
   old 10-minute behaviour (no beats sent → same expiry as today).

`join` already returns `full` once a peer is in, so "full → delist" is free.

### Ping — measured per host, relayed by the server

Because a listed host now keeps its signaling WS open, the browser can **actually ping each
host** rather than estimating. The browser opens its own WS in a third role (neither host nor
joiner — a socket that belongs to no room) and does everything over it:

| Msg | Direction | Effect |
|---|---|---|
| `{t:"browse", proto, hash}` | browser → server | `{t:"rooms", rooms:[…]}` — build-compatible, listed, not full |
| `{t:"ping", code, id}` | browser → server | forwarded to that room's host as `{t:"ping", id, ref}` |
| `{t:"pong", id, ref}` | host → server | routed back to the originating browser socket |

The browser fires all pings in parallel and fills each carousel entry's ping in as its pong
lands; a timeout shows `--` and is a useful signal in itself (a listed host that won't answer
is probably not joinable). RTT is browser→server→host→server→browser — not the direct P2P
number, which is unknowable before an ICE connection exists, but a *measured* round trip
through the real host rather than two summed guesses, and it moves with the thing the player
cares about. Drop the `rtt` field from `list`/`beat`; nothing reports its own latency any more.

**JS auto-pongs.** `webrtc.js` owns the WS, so it answers pings itself without touching C# —
the number then measures the network rather than the host's frame pacing.

Abuse bounds: pings are rate-limited per browser socket, the forwarded `ref` is opaque (a host
learns nothing about who pinged), and a browser socket that never sends `browse` is dropped.

This also replaces the `GET /rooms` endpoint — list and ping go over the one socket, which
closes when the player leaves the browser. (A plain `GET /health` stays for ops.)

## Client — game browser

- `wwwroot/webrtc.js`: `eaRtc.browse()` (opens the browser socket, sends `browse`, fires the
  per-host pings, reports rooms + pings back as they land) and `eaRtc.endBrowse()`; plus the
  host-side `list`/`unlist`/`beat` senders, the keep-alive, and the automatic pong.
- `Compat/Net/NetListing.cs` — **host side**: owns the eligibility predicate, the register /
  heartbeat / update / unlist lifecycle, and exposes `RoomCode` + `Listed` for the beacon and
  pause menu.
- `Compat/Net/NetGameBrowser.cs` — **joiner side**: opens/closes the browse socket, parses
  entries, collects per-host pings as they land, hands the list to the menu. Callbacks arrive
  from JS → queued and drained on the game tick, like `NetLobby`.

## Menu

- `Online Co-op` submenu gains **`Join Online Game`** (next to Host / Join by code / Back).
- New `SubMenuOnlineGames`: the carousel, one entry per open game — the **level's screenshot
  art**, with difficulty / players / ping / room code in the info text. Select → the existing
  11.4 join path with that code.
- Options gains **`Allow Online Joins: Yes/No`**.
- Pause menu shows `Listed online — room XYZAB` (or nothing when unlisted) — the card's privacy
  requirement *and* the host's easy-reference code display.

### Carousel reuse

`SubMenuLevelChoice` is level-keyed throughout (`levels[]`, unlockable gating, the
achievement-difficulty overlay), so it can't take game entries as-is. Extract its geometry —
`scroller`/`swaptimer`/`DrawEntryAt`/`RecordEntryHit` — into a `SubMenuCarousel` base;
`SubMenuLevelChoice` and `SubMenuOnlineGames` both derive. Mechanical, but it touches a
heavily-verified shipped file, so the level select gets a before/after screenshot check.

The level → (title, bundled screenshot, briefing) mapping currently lives inline in
`MenuScene`'s `AddEntryData` calls; extract to a small static `LevelArt` table both menus read.

## Join-in-progress

Host, on pairing mid-level:

1. `NetSession.Start(host)` attaches to the running `GameScene` (`GameScene.NetActiveScene`
   is already the static seam).
2. Send `EvLaunch(currentLevel, difficulty)`.
3. **Catch-up burst** — the joiner's fresh level `Initialize` starts from the level's *initial*
   state, so replay the host's current: last `Background` op, current music cue, score/lives
   (`EvScoreSync`), checkpoint baseline.
4. On the joiner's `EvReady`, `NetIdRegistry.ReplayLive` rebuilds the live entity set as puppets.

Joiner: warms + launches the level as normal, ship spawns via the generic path, host does
`oracle.AddPlayer(Remote)` on the first alive stream. All existing 11.2/11.3 machinery.

Known gaps → follow-up cards, not v1 scope:

- Deep mid-level background/doodad state (an earth fly-by or asteroid belt in progress) beyond
  the last-op replay.
- Mechanical-friend ships aren't replicated, so listing is refused while `Friends > 0` — a card
  to replicate friend ships would lift that.
- A joiner arriving mid-boss gets the boss's best-effort puppet pose (the known 11.2 limit),
  more visible now that arrival can happen at any moment.
- Public-list abuse surface (rate limiting, hiding a room) — nothing beyond MAX_ROOMS today.

## Beacon

`ScoreVisualiser.drawPressStart` alternates `Player 2` ⇄ `Press Start` on the 5 s cycle
described above, then stops after 4 cycles. While listed, a third string `Room code: XYZAB`
joins the rotation and the stop is suppressed, so a streamer can read the code out at any
time. Uses the existing cached-chrome draw path (`ParkedGlint`, no sweep).

**Persistence: full, but intermittent — the existing rhythm is the whole point.** The prompt is
already a blink, not a banner (~2 s visible per 5 s cycle), so running it forever is not a
static overlay nagging the player; it's the same occasional pop the slot already does, just
never stopping. With three strings in the rotation (`Player 2` → `Press Start` →
`Room code: XYZAB`) any given string surfaces about every 15 s, which is readable-on-demand for
a streamer without dominating the HUD.

Implementation note: the current toggle is a `bool showPressStart`; three states need a small
index-based rotation instead, and the `showPressStartTimes >= 4` stop is skipped while listed.

**Pause screen shows the room code too**, as the easy-reference display — one line, no hunting
for the next blink. This is the same line as the privacy indicator (`Listed online — room
XYZAB`), so it does double duty: a host can always find their code, and a player can always see
that their game is public.

## Verification (no booting the game to test)

| What | Tool |
|---|---|
| Server registry, heartbeat, TTL-from-beat, build filtering, full→delist | extend `server/signal/test_signal.py` (real unit tests) |
| Browser carousel appearance | `?gamebrowser` — boots straight to the carousel with injected fake entries, no server. Screenshot. |
| Beacon alternation | a scrub flag parking the rotation at a chosen phase (the `?textshot` pattern), or a beacon row added to `TextShowcaseScene` |
| Eligibility predicate | data-level: drive the predicate over the state combinations, assert listed/unlisted — no frame needed |
| JIP end to end | two Chrome windows, `?netjip`: host boots into a level solo+listed, joiner joins mid-level; read `[net]` metrics both sides (pops 0, snapRx climbing, snapUnk small) |
| Final smoke | real Chrome, menu → browser → join, zero console errors |

## File-by-file

| File | Change |
|---|---|
| `server/signal/main.py` | registry fields, `list`/`unlist`/`beat`, browser sockets (`browse`), ping relay, TTL from `last_beat` |
| `server/signal/test_signal.py` | registry/heartbeat/filter/expiry + ping-relay tests |
| `wwwroot/webrtc.js` | `browse`/`endBrowse` + per-host pings, `list`/`unlist`/`beat`, keep-alive, auto-pong |
| `Compat/Net/NetListing.cs` | NEW — host-side eligibility + listing lifecycle |
| `Compat/Net/NetGameBrowser.cs` | NEW — joiner-side fetch/parse/RTT |
| `Compat/Net/NetSession.cs` | mid-level `Start(host)`, catch-up burst |
| `Compat/DebugFlags.cs` | `?gamebrowser`, `?netjip` |
| `Game/EvilAliens/SubMenuCarousel.cs` | NEW — geometry extracted from `SubMenuLevelChoice` |
| `Game/EvilAliens/SubMenuOnlineGames.cs` | NEW — the game browser |
| `Game/EvilAliens/SubMenuLevelChoice.cs` | derive from `SubMenuCarousel` |
| `Game/EvilAliens/LevelArt.cs` | NEW — level → title/screenshot/briefing table |
| `Game/EvilAliens/MenuScene.cs` | `Join Online Game`, Options toggle, pause indicator |
| `Game/EvilAliens/Settings.cs` | `AllowOnlineJoins = true` (appended) |
| `Game/EvilAliens/ScoreVisualiser.cs` | room-code beacon rotation |
| docs | root + web `CLAUDE.md` |
