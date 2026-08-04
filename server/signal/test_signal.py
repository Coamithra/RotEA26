"""Self-contained protocol test for the RotEA signaling server.

Run:  python test_signal.py
Starts the app in-process on an ephemeral port (uvicorn), drives it with
the `websockets` client (already a dependency via uvicorn[standard]),
prints PASS/FAIL per case, exits nonzero on any failure.

Covers the 11.4 relay protocol AND the card-2001fbd8 registry: list/
browse/build-filter/unlist/full->delist/ping-relay over the socket, plus
deterministic unit checks of the TTL-from-last_beat + listable() logic
(no 600s waits -- the Room object is exercised directly).

Card e7404647 (room thumbnails) adds the pull schedule. Its cases run in
two halves: ONE case proves the background loop pulls unattended (with
the interval shortened at startup), and then the loop is parked at an
hour so every later case can drive `main.pull_once()` by hand and assert
an exact rotation. Every room used after that point is created after the
parking, so no stray automatic pull can reach it.
"""

import asyncio
import json
import sys
import time

import uvicorn
import websockets

import main

TIMEOUT = 3.0
# What the pull loop runs at while case 17 proves it works unattended. Parked at
# an hour immediately afterwards -- see the module docstring.
SHORT_PULL_INTERVAL = 0.2
results: list[tuple[str, bool, str]] = []


def record(name: str, ok: bool, detail: str = "") -> None:
    results.append((name, ok, detail))
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"  ({detail})" if detail and not ok else ""))


async def recv_json(ws):
    raw = await asyncio.wait_for(ws.recv(), TIMEOUT)
    return json.loads(raw)


async def send(ws, obj) -> None:
    await ws.send(json.dumps(obj))


async def host_room(url):
    """Open a socket, host a room, return (ws, code)."""
    ws = await websockets.connect(url)
    await send(ws, {"t": "host"})
    msg = await recv_json(ws)
    assert msg.get("t") == "code", msg
    return ws, msg["code"]


async def barrier(ws) -> None:
    """Block until the server has consumed everything sent on `ws` so far.

    `list` draws no reply, so a test that lists and then calls pull_once()
    directly can run before the server has read the frame at all -- which does
    not fail loudly, it makes the assertion VACUOUS (the room is simply not a
    candidate yet). A junk frame does draw a reply, and frames on one socket are
    served in order, so its arrival proves the `list` before it was applied.
    """
    await send(ws, {"t": "__barrier__"})
    msg = await recv_json(ws)
    assert msg == {"t": "error", "reason": "bad"}, msg


async def listed_host(url, shots: bool, proto="4", hash="h1"):
    """Host a room and list it, optionally declaring thumbnail support."""
    ws, code = await host_room(url)
    frame = {"t": "list", "level": 1, "difficulty": 0, "players": 1,
             "proto": proto, "hash": hash}
    if shots:
        frame["shots"] = 1
    await send(ws, frame)
    await barrier(ws)
    return ws, code


async def browse(url, proto="4", hash="h1"):
    """Open a browser socket, return (ws, rooms-list)."""
    ws = await websockets.connect(url)
    await send(ws, {"t": "browse", "proto": proto, "hash": hash})
    msg = await recv_json(ws)
    assert msg.get("t") == "rooms", msg
    return ws, msg["rooms"]


def unit_tests() -> None:
    """Deterministic Room-object checks -- no server, no timing waits."""
    # expired() counts from last_beat, not created.
    r = main.Room("AAAAA", None)
    r.created = time.monotonic() - (main.ROOM_TTL_SECONDS + 100)
    r.last_beat = time.monotonic()  # a recent beat keeps a long-lived room alive
    record("beat keeps a long-lived room from expiring", not r.expired())

    r.last_beat = time.monotonic() - (main.ROOM_TTL_SECONDS + 1)
    record("no beat within TTL -> expired", r.expired())

    # An unlisted room never beats: last_beat == created == old expiry.
    u = main.Room("BBBBB", None)
    u.created = u.last_beat = time.monotonic() - (main.ROOM_TTL_SECONDS + 1)
    record("unlisted room keeps from-creation expiry", u.expired())

    # listable() == listed AND empty joiner slot (mirrors join-eligibility).
    l = main.Room("CCCCC", None)
    record("fresh room is not listable", not l.listable())
    l.listed = True
    record("listed + empty slot is listable", l.listable())
    l.joiner = object()
    record("full room is not listable", not l.listable())

    # ---- card e7404647: thumbnail freshness on the Room object --------------
    s = main.Room("DDDDD", None)
    record("a room with no shot is not fresh", not s.shot_fresh())
    s.shot = "x"
    s.shot_seq = 1
    s.shot_at = time.monotonic()
    record("a just-stored shot is fresh", s.shot_fresh())
    s.shot_at = time.monotonic() - (main.SHOT_MAX_AGE_SECONDS + 1)
    record("a shot past SHOT_MAX_AGE_SECONDS is stale", not s.shot_fresh())
    # A stale shot must also stop being ADVERTISED, or the client fetches a
    # thumbnail the server will refuse to serve and shows nothing at all.
    s.listed = True
    record("a stale shot advertises as seq 0", s.listing_entry()["shot"] == 0)
    s.shot_at = time.monotonic()
    record("a fresh shot advertises its seq", s.listing_entry()["shot"] == 1)
    s.drop_shot()
    record("drop_shot clears the stored bytes", s.shot == "" and s.shot_seq == 0)


async def run_tests(url: str) -> None:
    # 1. host -> code shape
    try:
        ws, code = await host_room(url)
        alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
        ok = len(code) == 5 and all(c in alphabet for c in code)
        record("host returns 5-char code from alphabet", ok, repr(code))
        await ws.close()
    except Exception as e:
        record("host returns 5-char code from alphabet", False, repr(e))
        return  # nothing else can work

    # 2. join with bad code -> nocode
    try:
        ws = await websockets.connect(url)
        await send(ws, {"t": "join", "code": "ZZZZZ"})
        msg = await recv_json(ws)
        record("join unknown code -> error nocode",
               msg == {"t": "error", "reason": "nocode"}, repr(msg))
        await ws.close()
    except Exception as e:
        record("join unknown code -> error nocode", False, repr(e))

    # 3. happy path: host + join, both get peer (case-insensitive, padded code)
    try:
        host, code = await host_room(url)
        joiner = await websockets.connect(url)
        await send(joiner, {"t": "join", "code": f"  {code.lower()} "})
        j = await recv_json(joiner)
        h = await recv_json(host)
        record("host+join both receive peer",
               j == {"t": "peer"} and h == {"t": "peer"}, f"joiner={j} host={h}")
    except Exception as e:
        record("host+join both receive peer", False, repr(e))
        return

    # 4. sdp/ice relay, both directions, verbatim
    try:
        sdp = json.dumps({"t": "sdp", "desc": {"type": "offer", "sdp": "v=0\r\n..."}, "x": 1})
        await host.send(sdp)
        got = await asyncio.wait_for(joiner.recv(), TIMEOUT)
        ok1 = got == sdp
        ice = json.dumps({"t": "ice", "cand": "candidate:1 1 udp 2122 1.2.3.4 5 typ host"})
        await joiner.send(ice)
        got2 = await asyncio.wait_for(host.recv(), TIMEOUT)
        ok2 = got2 == ice
        record("sdp/ice relayed verbatim both directions", ok1 and ok2,
               f"h->j={got!r} j->h={got2!r}")
    except Exception as e:
        record("sdp/ice relayed verbatim both directions", False, repr(e))

    # 5. third joiner -> full
    try:
        third = await websockets.connect(url)
        await send(third, {"t": "join", "code": code})
        msg = await recv_json(third)
        record("third joiner -> error full",
               msg == {"t": "error", "reason": "full"}, repr(msg))
        await third.close()
    except Exception as e:
        record("third joiner -> error full", False, repr(e))

    # 6. unknown type -> bad (and socket stays usable)
    try:
        await send(host, {"t": "frobnicate"})
        msg = await recv_json(host)
        record("unknown type -> error bad",
               msg == {"t": "error", "reason": "bad"}, repr(msg))
    except Exception as e:
        record("unknown type -> error bad", False, repr(e))

    # 7. disconnect -> survivor gets gone
    try:
        await joiner.close()
        msg = await recv_json(host)
        record("peer disconnect -> survivor gets gone", msg == {"t": "gone"}, repr(msg))
        await host.close()
    except Exception as e:
        record("peer disconnect -> survivor gets gone", False, repr(e))

    # 8. room is deleted after teardown: old code now -> nocode
    try:
        ws = await websockets.connect(url)
        await send(ws, {"t": "join", "code": code})
        msg = await recv_json(ws)
        record("closed room's code -> error nocode",
               msg == {"t": "error", "reason": "nocode"}, repr(msg))
        await ws.close()
    except Exception as e:
        record("closed room's code -> error nocode", False, repr(e))

    # ---- card 2001fbd8: registry + browse + ping relay ----------------------

    # 9. list -> the room shows in a build-compatible browse
    try:
        host, code = await host_room(url)
        await send(host, {"t": "list", "level": 3, "difficulty": 2,
                          "players": 1, "proto": "4", "hash": "h1"})
        bws, rooms = await browse(url, proto="4", hash="h1")
        entry = next((r for r in rooms if r.get("code") == code), None)
        ok = (entry is not None and entry.get("level") == 3
              and entry.get("difficulty") == 2 and entry.get("players") == 1
              and "ageSec" in entry)
        record("listed room appears in matching browse", ok, repr(entry))
        await bws.close()
    except Exception as e:
        record("listed room appears in matching browse", False, repr(e))
        return

    # 10. build filter: a mismatched hash hides the room
    try:
        bws, rooms = await browse(url, proto="4", hash="OTHER")
        hidden = all(r.get("code") != code for r in rooms)
        record("incompatible build hash filters the room out", hidden,
               repr([r.get("code") for r in rooms]))
        await bws.close()
    except Exception as e:
        record("incompatible build hash filters the room out", False, repr(e))

    # 10b. build filter: a mismatched proto hides the room
    try:
        bws, rooms = await browse(url, proto="99", hash="h1")
        hidden = all(r.get("code") != code for r in rooms)
        record("incompatible protocol filters the room out", hidden,
               repr([r.get("code") for r in rooms]))
        await bws.close()
    except Exception as e:
        record("incompatible protocol filters the room out", False, repr(e))

    # 11. ping relay: browser -> server -> host -> server -> browser
    try:
        bws = await websockets.connect(url)
        await send(bws, {"t": "browse", "proto": "4", "hash": "h1"})
        await recv_json(bws)  # the rooms list
        await send(bws, {"t": "ping", "code": code, "id": "corr-7"})
        fwd = await recv_json(host)  # host receives the forwarded ping
        ok_fwd = fwd.get("t") == "ping" and fwd.get("id") == "corr-7" and isinstance(fwd.get("ref"), int)
        # host auto-pongs (the real one is JS; here we do it manually)
        await send(host, {"t": "pong", "id": fwd.get("id"), "ref": fwd.get("ref")})
        pong = await recv_json(bws)
        ok_pong = pong.get("t") == "pong" and pong.get("id") == "corr-7"
        record("ping relayed to host and pong routed back", ok_fwd and ok_pong,
               f"fwd={fwd} pong={pong}")
        await bws.close()
    except Exception as e:
        record("ping relayed to host and pong routed back", False, repr(e))

    # 12. ping before browse -> bad
    try:
        stray = await websockets.connect(url)
        await send(stray, {"t": "ping", "code": code, "id": "x"})
        msg = await recv_json(stray)
        record("ping without browse -> error bad",
               msg == {"t": "error", "reason": "bad"}, repr(msg))
        await stray.close()
    except Exception as e:
        record("ping without browse -> error bad", False, repr(e))

    # 13. unlist -> gone from browse, still joinable by code
    try:
        await send(host, {"t": "unlist"})
        bws, rooms = await browse(url, proto="4", hash="h1")
        gone = all(r.get("code") != code for r in rooms)
        await bws.close()
        joiner = await websockets.connect(url)
        await send(joiner, {"t": "join", "code": code})
        j = await recv_json(joiner)
        record("unlist hides from browse but code still joins",
               gone and j == {"t": "peer"}, f"gone={gone} join={j}")
    except Exception as e:
        record("unlist hides from browse but code still joins", False, repr(e))

    # 14. full -> delisted: a paired room never advertises
    try:
        host2, code2 = await host_room(url)
        await send(host2, {"t": "list", "level": 1, "difficulty": 0,
                           "players": 1, "proto": "4", "hash": "h1"})
        bws, rooms = await browse(url, proto="4", hash="h1")
        listed_before = any(r.get("code") == code2 for r in rooms)
        await bws.close()
        j2 = await websockets.connect(url)
        await send(j2, {"t": "join", "code": code2})
        await recv_json(j2)   # peer
        await recv_json(host2)  # peer
        bws, rooms = await browse(url, proto="4", hash="h1")
        listed_after = any(r.get("code") == code2 for r in rooms)
        record("a full room is delisted from browse",
               listed_before and not listed_after,
               f"before={listed_before} after={listed_after}")
        await bws.close()
        await j2.close()
        await host2.close()
    except Exception as e:
        record("a full room is delisted from browse", False, repr(e))

    # 15. host-only messages from a non-hosting socket -> bad (and the socket survives)
    try:
        stray = await websockets.connect(url)
        all_bad = True
        for m in ({"t": "list"}, {"t": "beat"}, {"t": "unlist"}, {"t": "pong", "ref": 1}):
            await send(stray, m)
            r = await recv_json(stray)
            if r != {"t": "error", "reason": "bad"}:
                all_bad = False
        record("list/beat/unlist/pong require hosting -> bad", all_bad)
        await stray.close()
    except Exception as e:
        record("list/beat/unlist/pong require hosting -> bad", False, repr(e))

    # 16. two browsers pinging one host each get THEIR OWN pong (ref routing, not hard-wired)
    try:
        host, code = await host_room(url)
        await send(host, {"t": "list", "level": 1, "difficulty": 0,
                          "players": 1, "proto": "4", "hash": "h1"})
        ba, _ = await browse(url, proto="4", hash="h1")
        bb, _ = await browse(url, proto="4", hash="h1")
        await send(ba, {"t": "ping", "code": code, "id": "AA"})
        p1 = await recv_json(host)   # A's ping, forwarded
        await send(bb, {"t": "ping", "code": code, "id": "BB"})
        p2 = await recv_json(host)   # B's ping, forwarded
        await send(host, {"t": "pong", "id": p1.get("id"), "ref": p1.get("ref")})
        await send(host, {"t": "pong", "id": p2.get("id"), "ref": p2.get("ref")})
        ra = await recv_json(ba)
        rb = await recv_json(bb)
        ok = (p1.get("ref") != p2.get("ref")
              and ra == {"t": "pong", "id": "AA"}
              and rb == {"t": "pong", "id": "BB"})
        record("pongs route to the browser that pinged", ok,
               f"p1={p1} p2={p2} ra={ra} rb={rb}")
        await ba.close()
        await bb.close()
        await host.close()
    except Exception as e:
        record("pongs route to the browser that pinged", False, repr(e))

    # ---- card e7404647: server-pulled room thumbnails -----------------------

    # 17. the background schedule pulls with nobody driving it. Runs FIRST, while
    #     SHOT_PULL_INTERVAL_SECONDS is still the short test value; everything
    #     below then parks the loop and drives pull_once() by hand.
    live_host = None
    try:
        # Listed WITHOUT the barrier helper: an automatic pull may land between
        # the list and any barrier reply, and the arriving pull is itself the
        # proof that the list was applied.
        live_host, live_code = await host_room(url)
        await send(live_host, {"t": "list", "level": 1, "difficulty": 0,
                               "players": 1, "proto": "4", "hash": "h1", "shots": 1})
        msg = await recv_json(live_host)
        record("the pull loop asks a listed host unattended",
               msg == {"t": "shot"}, repr(msg))
    except Exception as e:
        record("the pull loop asks a listed host unattended", False, repr(e))
    # Park the automatic loop for the rest of the run, then let it reach the long
    # sleep. Every room below is created afterwards, so no stray pull can reach it.
    main.SHOT_PULL_INTERVAL_SECONDS = 3600
    await asyncio.sleep(SHORT_PULL_INTERVAL * 3)
    if live_host is not None:
        await live_host.close()

    # 18. a host that does not declare `shots` is never pulled
    try:
        quiet, _ = await listed_host(url, shots=False)
        picked = await main.pull_once()
        record("a host that does not declare shots is never pulled",
               picked is None, f"picked={picked and picked.code}")
        await quiet.close()
    except Exception as e:
        record("a host that does not declare shots is never pulled", False, repr(e))

    # 19. THE ROTATION IS NOT STARVED BY A WEDGED HOST. Neither of these two ever
    #     answers; the pull is stamped when it is SENT, so each simply forfeits
    #     its own turn and the budget keeps moving.
    try:
        ha, ca = await listed_host(url, shots=True)
        hb, cb = await listed_host(url, shots=True)
        order = []
        for _ in range(4):
            picked = await main.pull_once()
            order.append(picked.code if picked else None)
        alternates = (order[0] != order[1] and order[1] != order[2]
                      and order[2] != order[3] and set(order) == {ca, cb})
        record("a non-answering host does not starve the rotation", alternates, repr(order))
        await ha.close()
        await hb.close()
    except Exception as e:
        record("a non-answering host does not starve the rotation", False, repr(e))

    # 20. answer a pull -> the shot is advertised and servable
    try:
        host, code = await listed_host(url, shots=True)
        await main.pull_once()
        await recv_json(host)  # the {"t":"shot"} pull
        await send(host, {"t": "shot", "data": "SHOT-ONE"})
        bws, rooms = await browse(url, proto="4", hash="h1")
        entry = next((r for r in rooms if r.get("code") == code), None)
        await send(bws, {"t": "shotget", "code": code})
        got = await recv_json(bws)
        ok = (entry is not None and entry.get("shot") == 1 and "shotAge" in entry
              and got.get("t") == "shot" and got.get("code") == code
              and got.get("seq") == 1 and got.get("data") == "SHOT-ONE")
        record("an answered pull is advertised and served to a browser", ok,
               f"entry={entry} got={got}")
        await bws.close()
    except Exception as e:
        record("an answered pull is advertised and served to a browser", False, repr(e))
        return

    # 21. an oversized frame is dropped WHOLE -- the previous good shot survives
    try:
        await send(host, {"t": "shot", "data": "X" * (main.MAX_SHOT_BYTES + 1)})
        bws, rooms = await browse(url, proto="4", hash="h1")
        await send(bws, {"t": "shotget", "code": code})
        got = await recv_json(bws)
        record("an oversized shot is dropped and the last good one kept",
               got.get("seq") == 1 and got.get("data") == "SHOT-ONE", repr(got.get("seq")))
        await bws.close()
    except Exception as e:
        record("an oversized shot is dropped and the last good one kept", False, repr(e))

    # 22. unlist drops the stored thumbnail
    try:
        await send(host, {"t": "unlist"})
        bws, _ = await browse(url, proto="4", hash="h1")
        await send(bws, {"t": "shotget", "code": code})
        got = await recv_json(bws)
        record("unlist drops the stored thumbnail",
               got.get("seq") == 0 and got.get("data") == "", repr(got))
        await bws.close()
        await host.close()
    except Exception as e:
        record("unlist drops the stored thumbnail", False, repr(e))

    # 23. role enforcement + the empty answer for a code with nothing stored
    try:
        stray = await websockets.connect(url)
        await send(stray, {"t": "shot", "data": "nope"})
        r1 = await recv_json(stray)
        await send(stray, {"t": "shotget", "code": "ZZZZZ"})
        r2 = await recv_json(stray)
        await stray.close()
        bws, _ = await browse(url, proto="4", hash="h1")
        await send(bws, {"t": "shotget", "code": "ZZZZZ"})
        r3 = await recv_json(bws)
        await bws.close()
        bad = {"t": "error", "reason": "bad"}
        record("shot needs a host, shotget needs a browse, unknown code -> empty",
               r1 == bad and r2 == bad and r3.get("seq") == 0 and r3.get("data") == "",
               f"r1={r1} r2={r2} r3={r3}")
    except Exception as e:
        record("shot needs a host, shotget needs a browse, unknown code -> empty",
               False, repr(e))


async def main_async() -> int:
    unit_tests()
    # Shorten the pull schedule BEFORE the app starts, so case 17 can watch the
    # real background loop do its job without a one-second wait per assertion.
    main.SHOT_PULL_INTERVAL_SECONDS = SHORT_PULL_INTERVAL
    config = uvicorn.Config("main:app", host="127.0.0.1", port=0, log_level="warning")
    server = uvicorn.Server(config)
    server_task = asyncio.create_task(server.serve())
    try:
        # Wait for the listener, then read the ephemeral port it landed on.
        for _ in range(100):
            if server.started:
                break
            await asyncio.sleep(0.05)
        else:
            raise RuntimeError("server failed to start")
        port = server.servers[0].sockets[0].getsockname()[1]
        await run_tests(f"ws://127.0.0.1:{port}/ws")
    finally:
        server.should_exit = True
        await asyncio.wait_for(server_task, TIMEOUT * 2)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main_async()))
