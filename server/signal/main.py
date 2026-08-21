"""RotEA online co-op signaling server.

Pairs browsers via a 5-char room code, then relays their WebRTC
SDP/ICE messages verbatim until the DataChannels connect and the
clients hang up. See README.md for the protocol table.

Stage 11.7 (transport addressing + N-peer room) grows a room from
exactly host+joiner to host + up to 3 joiners, at HOST-REQUESTED
capacity ({"t":"host","max":N}, clamped to [2,4]). The default stays 2,
so a shipped 2-peer client's whole protocol -- join/full, verbatim
relay, teardown -- is byte-for-byte unaffected. Joiners get monotone
per-room seat ids (never reused); in a max>2 room the host addresses a
relay frame with `to` and receives joiner frames stamped with `from`,
and a joiner leaving frees its seat instead of killing the room.

Card 2001fbd8 (public game browser) grows this into a lightweight
registry: a host may LIST its room (level/difficulty/players + a
protocol+build fingerprint), refresh it with a heartbeat, and a
third kind of socket -- a *browser* -- can list build-compatible open
rooms and PING each host through the relay to measure a real RTT.
Listing never constructs a game session; it is metadata on the same
room object the relay already owns.

Card e7404647 (room thumbnails) adds one more piece of that metadata: a
small JPEG of the host's game in progress, so the browse carousel shows
what a room actually looks like right now instead of stock level art.
THE SERVER OWNS THE SCHEDULE -- clients never upload unsolicited, they
only answer a pull -- and the schedule is a GLOBAL budget (one pull per
second across ALL rooms, round-robin), so the cost of more rooms is
staleness, never load. Shots live in memory on the Room and die with it.

Card 97b31562 bounds WHEN that schedule runs at all: no pull is ever sent
while no browser socket is connected (nobody is looking at the carousel,
so no host should pay for a capture), and no single room is re-pulled
within SHOT_ROOM_MIN_INTERVAL_SECONDS -- without that floor the global
budget degenerates at ONE listed room into pulling that room every
second, which on the host is a per-second GPU readback + JPEG encode,
i.e. a visible 1 Hz stutter reported from exactly that configuration.
"""

import asyncio
import contextlib
import json
import logging
import secrets
import time

from fastapi import FastAPI, WebSocket, WebSocketDisconnect

# No 0/O/1/I to keep codes unambiguous when read aloud / typed.
CODE_ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
CODE_LEN = 5
ROOM_TTL_SECONDS = 600
MAX_ROOMS = 200
MAX_BROWSERS = 200
MAX_MESSAGE_BYTES = 64 * 1024
SWEEP_INTERVAL_SECONDS = 30
RELAY_TYPES = {"sdp", "ice"}
# Ping abuse bound: a browser re-pings every listed room on each browse refresh
# (~15/min at BROWSE_REFRESH_MS=4s in webrtc.js), so a full list of MAX_ROOMS
# rooms is 200 pings x ~3 refreshes = ~600 legit pings per window. This ceiling
# leaves headroom above that yet still caps a socket that just floods pings.
PING_RATE_WINDOW = 10.0
PING_RATE_MAX = 1200

# ---- room thumbnails (card e7404647) ---------------------------------------
# The GLOBAL pull budget: one room per tick, so the whole server costs one
# thumbnail per second no matter how many games are open. At <=15 listed rooms
# that is the ~15 s per-room refresh the card asks for; beyond that the per-room
# interval stretches on its own (30 rooms -> ~30 s), which is the intended
# degradation. Never scale this with the room count.
SHOT_PULL_INTERVAL_SECONDS = 1.0
# The per-ROOM refresh floor (card 97b31562): however few candidates there are,
# one host is never asked more often than this. The global budget above caps the
# server's total rate; this caps what any ONE host pays, so the ~15 s per-room
# refresh the card asked for holds at ONE listed room as well as at fifteen.
SHOT_ROOM_MIN_INTERVAL_SECONDS = 15.0
# A 200x150 quality-60 JPEG is ~10-25 KB; base64 inflates it by 4/3. This is the
# ceiling for one stored shot (and so, at MAX_ROOMS, ~9.6 MB of worst-case
# server memory). Over it, the frame is dropped rather than truncated.
MAX_SHOT_BYTES = 48 * 1024
# A shot older than this is not worth serving -- the room has almost certainly
# moved on. Deliberately generous compared with the pull interval: under load
# the rotation stretches, and a two-minute-old REAL picture still beats generic
# stock art. The client applies the same bound (NetGameBrowser.StaleAfterSec).
SHOT_MAX_AGE_SECONDS = 180.0
# Thumbnails are far bigger than pings, so browser fetches get their own, much
# tighter allowance: a carousel only fetches a code when its seq CHANGES, so a
# well-behaved browser sends a handful per minute, not per refresh.
SHOTGET_RATE_WINDOW = 10.0
SHOTGET_RATE_MAX = 60

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
log = logging.getLogger("rotea.signal")


def _as_int(v, default: int) -> int:
    try:
        return int(v)
    except (TypeError, ValueError):
        return default


def _as_str(v) -> str:
    return "" if v is None else str(v)


def _room_max(v) -> int:
    """Host-requested room capacity: total machines INCLUDING the host.

    Garbage / absent / non-numeric falls back to the shipped default of 2 --
    anything a shipped 2-peer client could send lands there. Clamped to
    [2, 4]: the co-op epic tops out at 4 machines per session.
    """
    return max(2, min(4, _as_int(v, 2)))


class Room:
    __slots__ = ("code", "host", "max", "joiners", "next_joiner_id",
                 "created", "last_beat",
                 "listed", "level", "difficulty", "players", "proto", "hash",
                 "shots", "shot", "shot_at", "shot_seq", "last_pull_seq",
                 "last_pull_at")

    def __init__(self, code: str, host: WebSocket, max_members: int = 2):
        self.code = code
        self.host = host
        # Stage 11.7: capacity incl. the host, host-requested at {t:host}.
        self.max = max_members
        # Joiner seats, keyed by a per-room id that is MONOTONE and never
        # reused -- a leave+rejoin gets a fresh id, so a stale `to` from the
        # host can never route to the wrong (newer) socket. Insertion-ordered
        # by construction (dict), which keeps members() deterministic.
        self.joiners: dict[int, WebSocket] = {}
        self.next_joiner_id = 1
        self.created = time.monotonic()
        # TTL counts from the last sign of life. Unlisted (11.4 lobby) rooms
        # never beat, so last_beat stays == created and their expiry is the
        # unchanged 10-minute-from-creation behaviour. A listed game beats
        # while it lives, so a long level never vanishes mid-play.
        self.last_beat = self.created
        # Listing metadata (card 2001fbd8). `listed` + a free joiner seat is
        # what makes a room show in a browse; the rest is display + build filter.
        self.listed = False
        self.level = 0
        self.difficulty = 0
        self.players = 1
        self.proto = ""
        self.hash = ""
        # Thumbnail state (card e7404647). `shots` is the host's own declaration
        # that it understands a pull -- an older client never sends it, and the
        # puller skips those rooms, because that client's signaling layer would
        # answer an unknown {"t":"shot"} with nothing and take our `bad` reply as
        # a fatal listing error. So this flag IS the capability negotiation, and
        # it is what makes either deploy order (server first or game first) safe.
        self.shots = False
        self.shot = ""            # base64 JPEG as the host sent it, "" = none
        self.shot_at = 0.0        # monotonic stamp of the stored shot
        self.shot_seq = 0         # bumped per stored shot; 0 = never got one.
                                  # Monotone -- see drop_shot.
        # Where this room sits in the rotation: the value of `pull_counter` when
        # it was last PULLED, not when it last answered -- which is what keeps a
        # wedged host from starving anyone. It spends its own turn and the
        # oldest-first pick moves straight on.
        #
        # A COUNTER, not a clock, on purpose. Two pulls inside one clock tick
        # would tie (time.monotonic() is a 15.6 ms GetTickCount64 on Windows),
        # and min() breaks a tie by insertion order -- i.e. by re-picking the
        # room it just pulled. At one pull a second that would never bite in
        # production, but "the rotation is exactly round-robin" then holds only
        # by luck of the platform's clock, and could not be asserted at all.
        self.last_pull_seq = 0
        # ...and WHEN (monotonic). The counter above orders the rotation; this
        # stamp enforces the per-room refresh floor (card 97b31562). -inf =
        # never pulled, unconditionally past the floor -- 0.0 would read as
        # "pulled at boot", which on a server started at machine boot blocks
        # every fresh room for the first floor's worth of uptime.
        self.last_pull_at = float("-inf")

    def shot_age(self) -> float:
        return time.monotonic() - self.shot_at

    def shot_fresh(self) -> bool:
        return self.shot != "" and self.shot_age() <= SHOT_MAX_AGE_SECONDS

    def drop_shot(self) -> None:
        """Forget the stored thumbnail (unlisted, or aged out by the sweeper).

        `shot_seq` is deliberately NOT reset: it is what a client caches its
        decoded copy under, so a room that unlists and re-lists inside one
        browse refresh (~4 s -- one flick of the pause menu's room toggle)
        would otherwise hand out a fresh picture under the seq the client
        already holds, and be skipped. Monotone per room, like NetIdRegistry's
        own counter across a Disable/Enable.
        """
        self.shot = ""
        self.shot_at = 0.0

    def expired(self) -> bool:
        return time.monotonic() - self.last_beat > ROOM_TTL_SECONDS

    def age_seconds(self) -> float:
        return time.monotonic() - self.created

    def listable(self) -> bool:
        # A free joiner seat is the join-eligibility invariant, mirrored
        # server-side: a full room is never advertised (11.4 "join -> full").
        # For max == 2 this is exactly the old `joiner is None`, so shipped
        # 2-peer rooms list and delist identically.
        return self.listed and len(self.joiners) < self.max - 1

    def listing_entry(self) -> dict:
        return {
            "code": self.code,
            "level": self.level,
            "difficulty": self.difficulty,
            "players": self.players,
            "ageSec": int(self.age_seconds()),
            # Card e7404647: the thumbnail is NOT inlined here -- a browse
            # refresh every 4 s carrying every room's JPEG would be exactly the
            # load this design exists to avoid. The browser sees only the
            # sequence number and fetches (shotget) the codes whose seq it does
            # not already hold, so an unchanged thumbnail costs nothing.
            # seq 0 = this room has no servable shot -> draw the stock art.
            "shot": self.shot_seq if self.shot_fresh() else 0,
            "shotAge": int(self.shot_age()) if self.shot_fresh() else 0,
        }

    def members(self) -> list[WebSocket]:
        return [ws for ws in (self.host, *self.joiners.values()) if ws is not None]


# All mutation happens on the single event loop with no awaits between
# check and update, so a plain dict is safe without locking.
rooms: dict[str, Room] = {}

# Browser sockets (card 2001fbd8) -- a third role that belongs to no room and
# only lists + pings. Keyed by an opaque server-assigned id so a host can be
# told which browser to route a pong back to without learning anything about it
# (a random token, not a counter, so it leaks neither identity nor a count).
browsers: dict[int, WebSocket] = {}

# Monotone tick of the thumbnail pull rotation (card e7404647). A fresh room's
# 0 sorts ahead of every already-pulled room, so a game that has just been
# listed is asked first -- which is what the player expects: their room appears
# in the carousel with a real picture rather than stock art.
pull_counter = 0


def _new_browser_id() -> int:
    while True:
        bid = secrets.randbits(53)  # <= 2**53, so it survives the JS number round-trip
        if bid not in browsers:
            return bid


async def _send_json(ws: WebSocket, payload: dict) -> None:
    """Best-effort send; the peer may already be gone."""
    with contextlib.suppress(Exception):
        await ws.send_json(payload)


async def _close(ws: WebSocket) -> None:
    with contextlib.suppress(Exception):
        await ws.close()


def _new_code() -> str:
    while True:
        code = "".join(secrets.choice(CODE_ALPHABET) for _ in range(CODE_LEN))
        if code not in rooms:
            return code


async def _expire_room(room: Room) -> None:
    """Remove an expired room and tell whoever is still connected."""
    if rooms.get(room.code) is room:
        del rooms[room.code]
    log.info("room %s expired", room.code)
    for ws in room.members():
        await _send_json(ws, {"t": "error", "reason": "expired"})
        await _close(ws)


def _pull_candidates() -> list[Room]:
    """Listed, joinable, thumbnail-capable rooms due a refresh -- the rotation.

    The `last_pull_at` term is the per-room floor (card 97b31562): a room asked
    within the last SHOT_ROOM_MIN_INTERVAL_SECONDS is not a candidate, so a
    lone listed room is refreshed every ~15 s, not on every 1 s budget tick.
    """
    now = time.monotonic()
    return [r for r in rooms.values()
            if r.listable() and r.shots and not r.expired()
            and now - r.last_pull_at >= SHOT_ROOM_MIN_INTERVAL_SECONDS]


async def pull_once() -> Room | None:
    """Ask ONE host for a fresh thumbnail; return the room picked, if any.

    NOTHING is pulled while no browser is connected (card 97b31562): the
    thumbnails exist only for the browse carousel, so with nobody browsing no
    host anywhere should pay the capture (a GPU readback + JPEG encode on the
    game's own frame). Once a browser connects the next budget tick resumes the
    rotation, but a room still inside its per-room floor waits that floor out
    first -- so worst case ~SHOT_ROOM_MIN_INTERVAL_SECONDS plus a browse
    refresh of stock art before the real picture lands, the trade the card
    made. The floor is deliberately NOT reset on the empty->non-empty browser
    transition: a flapping browser socket could otherwise re-arm a lone host's
    every-second pulls, the exact cost the floor exists to bound.

    Round-robin by construction: always the candidate whose last pull is
    oldest. Because `last_pull_seq` is stamped HERE rather than when an answer
    lands, a host that never answers simply forfeits its own turn -- it can
    never hold the rotation up for anyone else. Split out of the loop below so
    test_signal.py can drive a tick directly instead of sleeping for one.
    """
    global pull_counter
    if not browsers:
        return None
    candidates = _pull_candidates()
    if not candidates:
        return None
    room = min(candidates, key=lambda r: r.last_pull_seq)
    pull_counter += 1
    room.last_pull_seq = pull_counter
    room.last_pull_at = time.monotonic()
    await _send_json(room.host, {"t": "shot"})
    return room


async def _shot_puller() -> None:
    while True:
        await asyncio.sleep(SHOT_PULL_INTERVAL_SECONDS)
        try:
            await pull_once()
        except Exception:  # a wedged socket must never kill the schedule
            log.exception("shot pull failed")


async def _sweeper() -> None:
    while True:
        await asyncio.sleep(SWEEP_INTERVAL_SECONDS)
        # Snapshot first: _expire_room awaits, so don't iterate the live dict.
        expired = [room for room in rooms.values() if room.expired()]
        for room in expired:
            await _expire_room(room)
        # Release the memory of a thumbnail that has aged past being servable
        # (card e7404647). `shot_fresh` already stops it being advertised and
        # served, so this changes no behaviour -- it stops a host that listed,
        # answered one pull and went quiet from holding up to 48 KB for the
        # rest of the room's 10-minute TTL.
        for room in rooms.values():
            if room.shot and not room.shot_fresh():
                room.drop_shot()


@contextlib.asynccontextmanager
async def _lifespan(app: FastAPI):
    tasks = [asyncio.create_task(_sweeper()), asyncio.create_task(_shot_puller())]
    try:
        yield
    finally:
        for task in tasks:
            task.cancel()
        for task in tasks:
            with contextlib.suppress(asyncio.CancelledError):
                await task


app = FastAPI(lifespan=_lifespan)


@app.get("/health")
async def health():
    listed = sum(1 for r in rooms.values() if r.listable())
    shots = [r for r in rooms.values() if r.shot_fresh()]
    return {"ok": True, "rooms": len(rooms), "listed": listed, "browsers": len(browsers),
            "shots": len(shots), "shotBytes": sum(len(r.shot) for r in shots)}


@app.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    room: Room | None = None       # the room this socket hosts/joined, if any
    joiner_id: int | None = None   # this socket's seat id, if it joined
    browser_id: int | None = None  # set once this socket sends {t:browse}
    ping_times: list[float] = []    # per-socket ping timestamps (rate limiting)
    shotget_times: list[float] = []  # ditto for thumbnail fetches (card e7404647)

    async def bad() -> None:
        await _send_json(ws, {"t": "error", "reason": "bad"})

    def rate_allowed(stamps: list[float], window: float, cap: int) -> bool:
        now = time.monotonic()
        # Drop stamps outside the window in place, then admit if under the cap.
        stamps[:] = [t for t in stamps if now - t < window]
        if len(stamps) >= cap:
            return False
        stamps.append(now)
        return True

    def ping_allowed() -> bool:
        return rate_allowed(ping_times, PING_RATE_WINDOW, PING_RATE_MAX)

    try:
        while True:
            text = await ws.receive_text()
            if len(text.encode("utf-8", errors="ignore")) > MAX_MESSAGE_BYTES:
                await bad()
                continue
            msg: dict = {}
            try:
                parsed = json.loads(text)
                if isinstance(parsed, dict):
                    msg = parsed
                t = msg.get("t")
            except ValueError:
                t = None
            if not isinstance(t, str):
                await bad()
                continue

            if t == "host":
                if room is not None or browser_id is not None:  # one role per socket
                    await bad()
                elif len(rooms) >= MAX_ROOMS:
                    await _send_json(ws, {"t": "error", "reason": "busy"})
                else:
                    code = _new_code()
                    room = Room(code, ws, _room_max(msg.get("max")))
                    rooms[code] = room
                    log.info("room %s created, max %d (%d rooms open)",
                             code, room.max, len(rooms))
                    await _send_json(ws, {"t": "code", "code": code})

            elif t == "join":
                if room is not None or browser_id is not None:
                    await bad()
                    continue
                code = str(msg.get("code", "")).strip().upper()
                target = rooms.get(code)
                if target is not None and target.expired():
                    await _expire_room(target)
                    target = None
                if target is None:
                    await _send_json(ws, {"t": "error", "reason": "nocode"})
                elif len(target.joiners) >= target.max - 1:
                    await _send_json(ws, {"t": "error", "reason": "full"})
                else:
                    joiner_id = target.next_joiner_id
                    target.next_joiner_id += 1
                    target.joiners[joiner_id] = ws
                    room = target
                    log.info("room %s joiner %d seated (%d/%d)", code,
                             joiner_id, len(room.joiners) + 1, room.max)
                    # The joiner's frame stays byte-identical to the shipped
                    # protocol. The HOST's frame now carries the joiner's seat
                    # id -- even in a max==2 room. This is the ONE deliberate
                    # wire-visible delta of Stage 11.7: the shipped host's JS
                    # reads only the fields it knows, so the extra field is
                    # inert there, and one peer-frame shape keeps the host
                    # path un-forked.
                    await _send_json(ws, {"t": "peer"})
                    await _send_json(room.host, {"t": "peer", "id": joiner_id})

            elif t == "list":
                # Set metadata + advertise. Idempotent: also the update path when
                # the host's level/difficulty/player count changes mid-session.
                if room is None or ws is not room.host:
                    await bad()
                else:
                    room.listed = True
                    room.last_beat = time.monotonic()
                    room.level = _as_int(msg.get("level"), 0)
                    room.difficulty = _as_int(msg.get("difficulty"), 0)
                    room.players = _as_int(msg.get("players"), 1)
                    room.proto = _as_str(msg.get("proto"))
                    room.hash = _as_str(msg.get("hash"))
                    # Opt-in, and only ever set here: a host that stops claiming
                    # the capability on a later list stops being pulled.
                    room.shots = bool(msg.get("shots"))

            elif t == "unlist":
                # Hide from browse; the room stays joinable by its code.
                if room is None or ws is not room.host:
                    await bad()
                else:
                    room.listed = False
                    # The picture outlives its listing nowhere: an unlisted room
                    # is one nobody can see, so keeping its thumbnail would only
                    # mean serving a stale frame if it ever re-lists.
                    room.drop_shot()

            elif t == "beat":
                if room is None or ws is not room.host:
                    await bad()
                else:
                    room.last_beat = time.monotonic()

            elif t == "browse":
                if room is not None:  # a room member can't also browse
                    await bad()
                else:
                    proto = _as_str(msg.get("proto"))
                    bhash = _as_str(msg.get("hash"))
                    if browser_id is None:
                        if len(browsers) >= MAX_BROWSERS:
                            await _send_json(ws, {"t": "error", "reason": "busy"})
                            continue
                        browser_id = _new_browser_id()
                        browsers[browser_id] = ws
                    listing = [
                        r.listing_entry()
                        for r in rooms.values()
                        if r.listable() and r.proto == proto and r.hash == bhash
                    ]
                    await _send_json(ws, {"t": "rooms", "rooms": listing})

            elif t == "ping":
                # browser -> server -> host. `id` is the browser's own opaque
                # correlation token (echoed back); `ref` is the server-assigned
                # browser id so the host's pong can be routed home.
                if browser_id is None:
                    await bad()  # must browse before pinging
                elif not ping_allowed():
                    pass  # silently drop; the browser times the entry out to "--"
                else:
                    code = str(msg.get("code", "")).strip().upper()
                    target = rooms.get(code)
                    if target is not None and not target.expired() and target.host is not None:
                        await _send_json(target.host, {"t": "ping", "id": msg.get("id"), "ref": browser_id})

            elif t == "shot":
                # Card e7404647. From a HOST this is the answer to a pull; from
                # a browser it is nothing (a browser fetches with `shotget`), so
                # the direction is never ambiguous. Unsolicited answers are
                # accepted rather than policed -- the pull IS the rate limiter,
                # and a client sending more just overwrites its own one slot.
                if room is None or ws is not room.host:
                    await bad()
                else:
                    data = _as_str(msg.get("data"))
                    if not data or len(data) > MAX_SHOT_BYTES:
                        # Dropped whole, never truncated: half a JPEG is not a
                        # smaller JPEG, it is a broken one. Silent -- an `error`
                        # reply is fatal to the host's listing (see fail() in
                        # webrtc.js) and an oversized frame is not worth that.
                        log.info("room %s shot rejected (%d bytes)", room.code, len(data))
                    else:
                        room.shot = data
                        room.shot_at = time.monotonic()
                        room.shot_seq += 1

            elif t == "shotget":
                # browser -> the stored thumbnail for one room code. Only ever
                # sent for a code whose listing entry carried a non-zero `shot`,
                # which is what keeps a new client safe against an old server:
                # that server never emits the field, so this is never sent.
                if browser_id is None:
                    await bad()  # must browse before fetching
                else:
                    code = str(msg.get("code", "")).strip().upper()
                    target = rooms.get(code)
                    # A rate-limited fetch is ANSWERED EMPTY, not dropped. The
                    # client retires a request when the answer lands, so a
                    # silent drop would wedge that code on stock art for the
                    # rest of the browse session -- the cap is meant to bound
                    # bandwidth, not to blacklist a room.
                    if not rate_allowed(shotget_times, SHOTGET_RATE_WINDOW, SHOTGET_RATE_MAX):
                        await _send_json(ws, {"t": "shot", "code": code, "seq": 0, "data": ""})
                    elif target is not None and target.shot_fresh():
                        await _send_json(ws, {"t": "shot", "code": code,
                                              "seq": target.shot_seq, "data": target.shot})
                    else:
                        # An explicit empty answer, so the client can retire an
                        # in-flight request instead of asking again forever.
                        await _send_json(ws, {"t": "shot", "code": code, "seq": 0, "data": ""})

            elif t == "pong":
                # host -> server -> the browser identified by `ref`.
                if room is None or ws is not room.host:
                    await bad()
                else:
                    ref = msg.get("ref")
                    # bool is a subclass of int -- reject it so {"ref": true} can't map to id 1.
                    target_ws = browsers.get(ref) if (isinstance(ref, int) and not isinstance(ref, bool)) else None
                    if target_ws is not None:
                        await _send_json(target_ws, {"t": "pong", "id": msg.get("id")})

            elif t in RELAY_TYPES:
                if room is None or not room.joiners:
                    # Not in a room, or a host with nobody seated yet -- the
                    # shipped "refused until paired" rule. A joiner passes by
                    # construction (its own seat makes joiners non-empty).
                    await bad()
                elif ws is room.host:
                    if room.max == 2:
                        # Shipped 2-peer path: the ORIGINAL text, verbatim, to
                        # the sole joiner. A stray `to` from a new-style host
                        # rides along harmlessly (the shipped joiner reads only
                        # the fields it knows); byte-identity is the contract.
                        peer = next(iter(room.joiners.values()))
                        with contextlib.suppress(Exception):
                            await peer.send_text(text)
                    else:
                        # max > 2: the host must address a seat. bool is a
                        # subclass of int -- reject it so {"to": true} can't
                        # route to seat 1 (the `ref` pattern in pong).
                        to = msg.get("to")
                        peer = (room.joiners.get(to)
                                if isinstance(to, int) and not isinstance(to, bool)
                                else None)
                        if peer is None:
                            await bad()
                        else:
                            # Still the ORIGINAL text: the addressed joiner
                            # gets the frame verbatim, `to` included.
                            with contextlib.suppress(Exception):
                                await peer.send_text(text)
                else:
                    if room.max == 2:
                        # Shipped 2-peer path, byte-identical.
                        with contextlib.suppress(Exception):
                            await room.host.send_text(text)
                    else:
                        # max > 2: stamp WHICH seat this came from. A max>2
                        # host is by definition a new client, so forwarding the
                        # re-serialized parsed frame (send_json) is safe here.
                        msg["from"] = joiner_id
                        await _send_json(room.host, msg)

            else:
                await bad()

    except WebSocketDisconnect:
        pass
    finally:
        if browser_id is not None:
            browsers.pop(browser_id, None)
        # Tear down the room this socket belonged to; guard against the
        # room having already been replaced/expired under the same code.
        if room is not None and rooms.get(room.code) is room:
            if ws is room.host:
                # Host gone -> the room dies; every joiner is told. The
                # per-recipient frame is the shipped bare {t:gone}.
                del rooms[room.code]
                log.info("room %s closed (%d rooms open)", room.code, len(rooms))
                for peer in room.joiners.values():
                    await _send_json(peer, {"t": "gone"})
            elif room.max == 2:
                # Shipped 2-peer behaviour, exactly: joiner gone -> room dies,
                # host gets the bare {t:gone}.
                del rooms[room.code]
                log.info("room %s closed (%d rooms open)", room.code, len(rooms))
                await _send_json(room.host, {"t": "gone"})
            else:
                # max > 2: the seat is freed and the room SURVIVES. listable()
                # recomputes, so a listed room re-advertises the free seat on
                # its own; the id is never reused (next_joiner_id is monotone).
                room.joiners.pop(joiner_id, None)
                log.info("room %s joiner %s left (%d/%d)", room.code,
                         joiner_id, len(room.joiners) + 1, room.max)
                await _send_json(room.host, {"t": "gone", "id": joiner_id})
