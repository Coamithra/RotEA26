"""RotEA online co-op signaling server.

Pairs two browsers via a 5-char room code, then relays their WebRTC
SDP/ICE messages verbatim until the DataChannel connects and the
clients hang up. See README.md for the protocol table.
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
MAX_MESSAGE_BYTES = 64 * 1024
SWEEP_INTERVAL_SECONDS = 30
RELAY_TYPES = {"sdp", "ice"}

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
log = logging.getLogger("rotea.signal")


class Room:
    __slots__ = ("code", "host", "joiner", "created")

    def __init__(self, code: str, host: WebSocket):
        self.code = code
        self.host = host
        self.joiner: WebSocket | None = None
        self.created = time.monotonic()

    def expired(self) -> bool:
        return time.monotonic() - self.created > ROOM_TTL_SECONDS

    def members(self) -> list[WebSocket]:
        return [ws for ws in (self.host, self.joiner) if ws is not None]

    def other(self, ws: WebSocket) -> WebSocket | None:
        if ws is self.host:
            return self.joiner
        return self.host


# All mutation happens on the single event loop with no awaits between
# check and update, so a plain dict is safe without locking.
rooms: dict[str, Room] = {}


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
    return {"ok": True, "rooms": len(rooms)}


@app.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    room: Room | None = None  # the room this socket is a member of, if any

    async def bad() -> None:
        await _send_json(ws, {"t": "error", "reason": "bad"})

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
                if room is not None:  # one room per socket
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
                if room is not None:
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
        # Tear down the room this socket belonged to; guard against the
        # room having already been replaced/expired under the same code.
        if room is not None and rooms.get(room.code) is room:
            del rooms[room.code]
            log.info("room %s closed (%d rooms open)", room.code, len(rooms))
            survivor = room.other(ws)
            if survivor is not None:
                await _send_json(survivor, {"t": "gone"})
