"""RotEA online co-op signaling server.

Pairs two browsers via a 5-char room code, then relays their WebRTC
SDP/ICE messages verbatim until the DataChannel connects and the
clients hang up. See README.md for the protocol table.

Card 2001fbd8 (public game browser) grows this into a lightweight
registry: a host may LIST its room (level/difficulty/players + a
protocol+build fingerprint), refresh it with a heartbeat, and a
third kind of socket -- a *browser* -- can list build-compatible open
rooms and PING each host through the relay to measure a real RTT.
Listing never constructs a game session; it is metadata on the same
room object the relay already owns.
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

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
log = logging.getLogger("rotea.signal")


def _as_int(v, default: int) -> int:
    try:
        return int(v)
    except (TypeError, ValueError):
        return default


def _as_str(v) -> str:
    return "" if v is None else str(v)


class Room:
    __slots__ = ("code", "host", "joiner", "created", "last_beat",
                 "listed", "level", "difficulty", "players", "proto", "hash")

    def __init__(self, code: str, host: WebSocket):
        self.code = code
        self.host = host
        self.joiner: WebSocket | None = None
        self.created = time.monotonic()
        # TTL counts from the last sign of life. Unlisted (11.4 lobby) rooms
        # never beat, so last_beat stays == created and their expiry is the
        # unchanged 10-minute-from-creation behaviour. A listed game beats
        # while it lives, so a long level never vanishes mid-play.
        self.last_beat = self.created
        # Listing metadata (card 2001fbd8). `listed` + an empty joiner slot is
        # what makes a room show in a browse; the rest is display + build filter.
        self.listed = False
        self.level = 0
        self.difficulty = 0
        self.players = 1
        self.proto = ""
        self.hash = ""

    def expired(self) -> bool:
        return time.monotonic() - self.last_beat > ROOM_TTL_SECONDS

    def age_seconds(self) -> float:
        return time.monotonic() - self.created

    def listable(self) -> bool:
        # An empty joiner slot is the join-eligibility invariant, mirrored
        # server-side: a full room is never advertised (11.4 "join -> full").
        return self.listed and self.joiner is None

    def listing_entry(self) -> dict:
        return {
            "code": self.code,
            "level": self.level,
            "difficulty": self.difficulty,
            "players": self.players,
            "ageSec": int(self.age_seconds()),
        }

    def members(self) -> list[WebSocket]:
        return [ws for ws in (self.host, self.joiner) if ws is not None]

    def other(self, ws: WebSocket) -> WebSocket | None:
        if ws is self.host:
            return self.joiner
        return self.host


# All mutation happens on the single event loop with no awaits between
# check and update, so a plain dict is safe without locking.
rooms: dict[str, Room] = {}

# Browser sockets (card 2001fbd8) -- a third role that belongs to no room and
# only lists + pings. Keyed by an opaque server-assigned id so a host can be
# told which browser to route a pong back to without learning anything about it
# (a random token, not a counter, so it leaks neither identity nor a count).
browsers: dict[int, WebSocket] = {}


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


async def _sweeper() -> None:
    while True:
        await asyncio.sleep(SWEEP_INTERVAL_SECONDS)
        # Snapshot first: _expire_room awaits, so don't iterate the live dict.
        expired = [room for room in rooms.values() if room.expired()]
        for room in expired:
            await _expire_room(room)


@contextlib.asynccontextmanager
async def _lifespan(app: FastAPI):
    task = asyncio.create_task(_sweeper())
    try:
        yield
    finally:
        task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await task


app = FastAPI(lifespan=_lifespan)


@app.get("/health")
async def health():
    listed = sum(1 for r in rooms.values() if r.listable())
    return {"ok": True, "rooms": len(rooms), "listed": listed, "browsers": len(browsers)}


@app.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    room: Room | None = None       # the room this socket hosts/joined, if any
    browser_id: int | None = None  # set once this socket sends {t:browse}
    ping_times: list[float] = []    # per-socket ping timestamps (rate limiting)

    async def bad() -> None:
        await _send_json(ws, {"t": "error", "reason": "bad"})

    def ping_allowed() -> bool:
        now = time.monotonic()
        # Drop stamps outside the window in place, then admit if under the cap.
        ping_times[:] = [t for t in ping_times if now - t < PING_RATE_WINDOW]
        if len(ping_times) >= PING_RATE_MAX:
            return False
        ping_times.append(now)
        return True

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
                    room = Room(code, ws)
                    rooms[code] = room
                    log.info("room %s created (%d rooms open)", code, len(rooms))
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
                elif target.joiner is not None:
                    await _send_json(ws, {"t": "error", "reason": "full"})
                else:
                    target.joiner = ws
                    room = target
                    log.info("room %s paired", code)
                    for member in room.members():
                        await _send_json(member, {"t": "peer"})

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

            elif t == "unlist":
                # Hide from browse; the room stays joinable by its code.
                if room is None or ws is not room.host:
                    await bad()
                else:
                    room.listed = False

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
                peer = room.other(ws) if (room is not None and room.joiner is not None) else None
                if peer is None:
                    await bad()
                else:
                    # Verbatim: forward the original text frame untouched.
                    with contextlib.suppress(Exception):
                        await peer.send_text(text)

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
            del rooms[room.code]
            log.info("room %s closed (%d rooms open)", room.code, len(rooms))
            survivor = room.other(ws)
            if survivor is not None:
                await _send_json(survivor, {"t": "gone"})
