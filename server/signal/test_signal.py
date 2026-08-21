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

Card 97b31562 gates the schedule twice: no pull at all while no browser
is connected, and no single room re-pulled within the per-room floor.
The gates get their own cases; the by-hand rotation half keeps a browser
socket open and zeroes the floor, so its exact-rotation assertions still
mean what they always did.

Stage 11.7 (N-peer rooms) adds cases 25-32 at the end: host-requested
capacity, seat ids, addressed/stamped relay in max>2 rooms, and the
seat-freeing teardown -- with the untouched 2-peer cases above standing
as the byte-for-byte shipped-protocol pins (see the section banner).
"""

import asyncio
import contextlib
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


async def host_n(url, max_):
    """Host a room with an explicit capacity request; return (ws, code)."""
    ws = await websockets.connect(url)
    await send(ws, {"t": "host", "max": max_})
    msg = await recv_json(ws)
    assert msg.get("t") == "code", msg
    return ws, msg["code"]


async def join_ok(url, code):
    """Join a room; assert the joiner-side frame is the shipped bare peer."""
    ws = await websockets.connect(url)
    await send(ws, {"t": "join", "code": code})
    msg = await recv_json(ws)
    assert msg == {"t": "peer"}, msg
    return ws


async def join_refused(url, code, reason):
    """Attempt a join, return True iff it is refused with exactly `reason`."""
    ws = await websockets.connect(url)
    await send(ws, {"t": "join", "code": code})
    msg = await recv_json(ws)
    await ws.close()
    return msg == {"t": "error", "reason": reason}


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

    # listable() == listed AND a free joiner seat (mirrors join-eligibility).
    # These two max==2 rows ARE the old-behaviour equivalence: 0 joiners is the
    # old `joiner is None`, 1 joiner is the old occupied slot -- shipped 2-peer
    # rooms must list and delist identically.
    l = main.Room("CCCCC", None)
    record("fresh room is not listable", not l.listable())
    l.listed = True
    record("listed + empty slot is listable", l.listable())
    l.joiners[1] = object()
    record("full room is not listable", not l.listable())

    # ---- Stage 11.7: listable() truth table over (listed, max, joiners) -----
    # The rule is `listed and len(joiners) < max - 1`. Rows with cap == 2
    # restate the equivalence above; rows with cap > 2 are the new capacity
    # awareness (a max=4 room with 1 or 2 joiners still advertises).
    bad_rows = []
    for listed in (False, True):
        for cap in (2, 3, 4):
            for n in range(4):
                r = main.Room("EEEEE", None, cap)
                r.listed = listed
                for _ in range(min(n, cap - 1)):
                    r.joiners[r.next_joiner_id] = object()
                    r.next_joiner_id += 1
                seated = len(r.joiners)
                expect = listed and seated < cap - 1
                if r.listable() != expect:
                    bad_rows.append(f"(listed={listed},max={cap},joiners={seated})")
    record("listable() truth table over (listed, max, joiners)",
           not bad_rows, " ".join(bad_rows))

    # _room_max: garbage/absent -> 2, clamped to [2, 4].
    got = [main._room_max(v) for v in (None, "x", 0, 1, 2, 3, 4, 9, "3", True)]
    record("_room_max clamps to [2,4] and defaults garbage to 2",
           got == [2, 2, 2, 2, 2, 3, 4, 4, 3, 2], repr(got))

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
    record("drop_shot clears the stored bytes", s.shot == "" and not s.shot_fresh())
    # ...but KEEPS the sequence number. A client caches its decoded copy under
    # (code, seq), so restarting at 1 after an unlist/re-list inside one browse
    # refresh would hand it a new picture under a seq it already holds.
    s.shot = "y"
    s.shot_at = time.monotonic()
    s.shot_seq += 1
    record("a re-listed room's next shot gets a HIGHER seq", s.listing_entry()["shot"] == 2)


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
        # Stage 11.7's ONE deliberate wire-visible delta: the HOST's peer frame
        # now carries the joiner's seat id (yes, even in a default max==2 room
        # -- the shipped host's JS reads only the fields it knows). The JOINER
        # frame stays byte-identical, so its exact-equality assert stays.
        record("host+join both receive peer",
               j == {"t": "peer"} and h.get("t") == "peer"
               and isinstance(h.get("id"), int),
               f"joiner={j} host={h}")
    except Exception as e:
        record("host+join both receive peer", False, repr(e))
        return

    # 4. sdp/ice relay, both directions, verbatim. UNMODIFIED since Stage 11.7
    #    on purpose: this byte-equality is the mutation control proving max==2
    #    rooms are never augmented (no `from` stamp, no re-serialization).
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

    # 5. third joiner -> full. Since Stage 11.7 this is ALSO the default-
    #    capacity pin: the host above sent no `max`, so its room must refuse a
    #    second joiner exactly like every shipped 2-peer build expects.
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

    # 7. disconnect -> survivor gets gone. With case 8 this pins the max==2
    #    teardown (room dies whole, bare {t:gone}) -- unchanged by Stage 11.7.
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

    # 16b. NO BROWSER, NO PULL (card 97b31562). Runs first, while the live loop
    #      is still ticking at the short interval and before any browser socket
    #      of this section exists: an eligible listed host must hear nothing,
    #      and a by-hand tick must decline. Earlier cases' browser sockets are
    #      waited out of the registry first -- their disconnects are processed
    #      asynchronously, and one still registered would make this vacuous the
    #      other way (a pull WOULD be legal).
    idle_host = None
    try:
        for _ in range(40):
            if not main.browsers:
                break
            await asyncio.sleep(0.05)
        assert not main.browsers, f"browser sockets still registered: {len(main.browsers)}"
        idle_host, _ = await listed_host(url, shots=True)
        picked = await main.pull_once()
        pulled = None
        with contextlib.suppress(asyncio.TimeoutError):
            pulled = await asyncio.wait_for(idle_host.recv(), SHORT_PULL_INTERVAL * 4)
        record("no pull is sent while nobody is browsing",
               picked is None and pulled is None, f"picked={picked and picked.code} pulled={pulled!r}")
    except Exception as e:
        record("no pull is sent while nobody is browsing", False, repr(e))
    if idle_host is not None:
        await idle_host.close()

    # 17. the background schedule pulls with nobody driving it -- once someone
    #     is browsing. Runs while SHOT_PULL_INTERVAL_SECONDS is still the short
    #     test value; everything below then parks the loop and drives
    #     pull_once() by hand.
    live_host = None
    live_bws = None
    try:
        live_bws, _ = await browse(url, proto="4", hash="h1")
        # Listed WITHOUT the barrier helper: an automatic pull may land between
        # the list and any barrier reply, and the arriving pull is itself the
        # proof that the list was applied.
        live_host, live_code = await host_room(url)
        await send(live_host, {"t": "list", "level": 1, "difficulty": 0,
                               "players": 1, "proto": "4", "hash": "h1", "shots": 1})
        msg = await recv_json(live_host)
        record("the pull loop asks a listed host while a browser is connected",
               msg == {"t": "shot"}, repr(msg))
    except Exception as e:
        record("the pull loop asks a listed host while a browser is connected", False, repr(e))
    # Park the automatic loop for the rest of the run, then let it reach the long
    # sleep. Every room below is created afterwards, so no stray pull can reach it.
    main.SHOT_PULL_INTERVAL_SECONDS = 3600
    await asyncio.sleep(SHORT_PULL_INTERVAL * 3)
    if live_host is not None:
        await live_host.close()
    if live_bws is not None:
        await live_bws.close()

    # The by-hand half below asserts EXACT rotations, which the per-room floor
    # would veto (a re-pull inside 15 s is precisely what it exists to refuse),
    # so it runs with the floor zeroed and one browser socket held open to
    # satisfy the browser gate. The floor gets its own case (19b). Guarded like
    # every other acquisition: a raise here must record a FAIL, not abort the
    # suite, and the close at the end must survive it.
    main.SHOT_ROOM_MIN_INTERVAL_SECONDS = 0.0
    pull_bws = None
    try:
        pull_bws, _ = await browse(url, proto="4", hash="h1")
    except Exception as e:
        record("the by-hand half's browser socket opens", False, repr(e))

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

    # 19b. THE PER-ROOM FLOOR (card 97b31562): a just-pulled room is not a
    #      candidate again until SHOT_ROOM_MIN_INTERVAL_SECONDS has passed --
    #      driven by backdating the stamp rather than sleeping the floor out.
    try:
        main.SHOT_ROOM_MIN_INTERVAL_SECONDS = 3600.0
        fh, fc = await listed_host(url, shots=True)
        # Precondition, pinned: `second is None` below only means "the floor
        # refused a re-pull" if fc is the SOLE candidate -- a stray listed
        # shots-room left behind by an earlier case would fail this legibly
        # instead of turning 19b into a confusing rotation assertion.
        stray = [r.code for r in main._pull_candidates() if r.code != fc]
        assert not stray, f"stray pull candidates: {stray}"
        first = await main.pull_once()
        second = await main.pull_once()
        main.rooms[fc].last_pull_at = time.monotonic() - 3601.0
        third = await main.pull_once()
        record("a just-pulled room is not re-pulled inside the floor",
               first is not None and first.code == fc and second is None
               and third is not None and third.code == fc,
               f"first={first and first.code} second={second and second.code} "
               f"third={third and third.code}")
        await fh.close()
    except Exception as e:
        record("a just-pulled room is not re-pulled inside the floor", False, repr(e))
    finally:
        main.SHOT_ROOM_MIN_INTERVAL_SECONDS = 0.0

    # 20. answer a pull -> the shot is advertised and servable
    host = None
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
        host = None  # 21/22 need this host; 23 does not, so it still runs

    # 21. an oversized frame is dropped WHOLE -- the previous good shot survives
    try:
        assert host is not None, "no host from case 20"
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
        assert host is not None, "no host from case 20"
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

    # 24. a RATE-LIMITED shotget is answered empty, never dropped. A silent drop
    #     would wedge that code on stock art for the rest of the browse session,
    #     because the client only retires a request when an answer lands.
    try:
        saved = main.SHOTGET_RATE_MAX
        main.SHOTGET_RATE_MAX = 1
        bws, _ = await browse(url, proto="4", hash="h1")
        await send(bws, {"t": "shotget", "code": "ZZZZZ"})
        await recv_json(bws)                       # spends the one allowance
        await send(bws, {"t": "shotget", "code": "ZZZZZ"})
        limited = await recv_json(bws)             # over the cap -- must still answer
        main.SHOTGET_RATE_MAX = saved
        record("a rate-limited shotget still gets an answer",
               limited.get("t") == "shot" and limited.get("seq") == 0, repr(limited))
        await bws.close()
    except Exception as e:
        main.SHOTGET_RATE_MAX = saved
        record("a rate-limited shotget still gets an answer", False, repr(e))

    # The browser socket the by-hand half held open for the card-97b31562 gate.
    if pull_bws is not None:
        await pull_bws.close()

    # ---- Stage 11.7: N-peer rooms (host + up to 3 joiners) ------------------
    # The shipped 2-peer protocol is pinned by UNMODIFIED cases above:
    #  - case 5 is the default-capacity pin (a host that sends no `max` still
    #    refuses a second joiner with `full`);
    #  - case 4's byte-equality is the mutation control against augmenting
    #    max==2 relay frames;
    #  - cases 7/8 pin the max==2 teardown (room dies whole, bare {t:gone});
    #  - case 14 pins the max==2 delist-at-one-joiner listable() equivalence.
    # Cases 25/27/28/30/31 share ONE max=4 room, built in 25 and torn down by
    # 31's host drop; 26/29/32 are self-contained.

    # 25. max=4 happy path: three joins seat with distinct MONOTONE host-side
    #     ids, every joiner-side frame stays the shipped bare peer, and the
    #     fourth join is refused exactly like a full 2-peer room.
    n_host = None
    n_code = None
    n_joiners: list = []
    try:
        n_host, n_code = await host_n(url, 4)
        ids = []
        for _ in range(3):
            n_joiners.append(await join_ok(url, n_code))
            hmsg = await recv_json(n_host)
            ids.append(hmsg.get("id") if hmsg.get("t") == "peer" else None)
        full4 = await join_refused(url, n_code, "full")
        record("max=4 seats 3 joiners with monotone ids, 4th -> full",
               ids == [1, 2, 3] and full4, f"ids={ids} full={full4}")
    except Exception as e:
        record("max=4 seats 3 joiners with monotone ids, 4th -> full", False, repr(e))

    # 26. capacity clamps: 9 behaves as 4 (three joiners land, the fourth is
    #     refused), 1 and garbage behave as the shipped 2 (the SECOND joiner
    #     is refused).
    try:
        h9, c9 = await host_n(url, 9)
        nine = [await join_ok(url, c9) for _ in range(3)]
        for _ in range(3):
            await recv_json(h9)  # drain the host-side peer frames
        ok9 = await join_refused(url, c9, "full")
        for ws_ in (h9, *nine):
            await ws_.close()
        ok_low = True
        for m in (1, "x"):
            hm, cm = await host_n(url, m)
            jm = await join_ok(url, cm)
            await recv_json(hm)
            if not await join_refused(url, cm, "full"):
                ok_low = False
            await jm.close()
            await hm.close()
        record("max clamps: 9 -> 4, 1 and garbage -> 2", ok9 and ok_low,
               f"nine={ok9} low={ok_low}")
    except Exception as e:
        record("max clamps: 9 -> 4, 1 and garbage -> 2", False, repr(e))

    # 27. addressed relay host->joiner (max>2): the frame lands on EXACTLY the
    #     addressed seat, VERBATIM (`to` included -- byte equality); the other
    #     seat hears nothing (the negative control); a missing, boolean or
    #     unknown `to` -> bad.
    try:
        assert n_host is not None and len(n_joiners) == 3, "no room from case 25"
        frame = json.dumps({"t": "sdp", "desc": {"type": "offer", "sdp": "v=0"}, "to": 2})
        await n_host.send(frame)
        got = await asyncio.wait_for(n_joiners[1].recv(), TIMEOUT)
        hit = got == frame
        silent = True  # seat 1 must NOT receive the frame addressed to seat 2
        with contextlib.suppress(asyncio.TimeoutError):
            await asyncio.wait_for(n_joiners[0].recv(), 0.5)
            silent = False
        all_bad = True
        for extra in ({}, {"to": True}, {"to": 99}):
            await send(n_host, {"t": "sdp", "d": 1, **extra})
            r = await recv_json(n_host)
            if r != {"t": "error", "reason": "bad"}:
                all_bad = False
        record("host->joiner relay is addressed, verbatim, and validated",
               hit and silent and all_bad,
               f"hit={hit} silent={silent} bad={all_bad} got={got!r}")
    except Exception as e:
        record("host->joiner relay is addressed, verbatim, and validated", False, repr(e))

    # 28. augmented relay joiner->host (max>2): the parsed frame arrives with
    #     `from` == the sender's seat id and every original field intact.
    try:
        assert n_host is not None and len(n_joiners) == 3, "no room from case 25"
        await send(n_joiners[1], {"t": "sdp", "d": "answer-blob", "x": 5})
        got = await recv_json(n_host)
        record("joiner->host relay is stamped with from=<seat id>",
               got == {"t": "sdp", "d": "answer-blob", "x": 5, "from": 2}, repr(got))
    except Exception as e:
        record("joiner->host relay is stamped with from=<seat id>", False, repr(e))

    # 29. a stray `to` from a new-style host in a max==2 room is IGNORED and
    #     the frame relayed verbatim to the sole joiner -- never `bad`. (The
    #     no-`to` verbatim path is case 4.)
    try:
        h2, c2 = await host_room(url)
        j2 = await join_ok(url, c2)
        await recv_json(h2)  # the host-side peer frame
        frame = json.dumps({"t": "sdp", "d": "x", "to": 1})
        await h2.send(frame)
        got = await asyncio.wait_for(j2.recv(), TIMEOUT)
        record("a stray `to` in a max==2 room rides along verbatim",
               got == frame, repr(got))
        await j2.close()
        await recv_json(h2)  # the {t:gone} from the joiner's close
        await h2.close()
    except Exception as e:
        record("a stray `to` in a max==2 room rides along verbatim", False, repr(e))

    # 30. mid-session joiner leave (max=4): the seat is FREED, the room
    #     SURVIVES (a later addressed relay still lands), a listed room
    #     re-appears in browse, and a new join gets a FRESH id -- never a
    #     reused one.
    try:
        assert n_host is not None and len(n_joiners) == 3, "no room from case 25"
        await send(n_host, {"t": "list", "level": 1, "difficulty": 0,
                            "players": 3, "proto": "4", "hash": "h1"})
        await barrier(n_host)
        bws, listing = await browse(url)
        full_hidden = all(r.get("code") != n_code for r in listing)
        await bws.close()
        await n_joiners[0].close()  # seat 1 leaves
        gone = await recv_json(n_host)
        ok_gone = gone == {"t": "gone", "id": 1}
        bws, listing = await browse(url)
        reappeared = any(r.get("code") == n_code for r in listing)
        await bws.close()
        frame = json.dumps({"t": "ice", "cand": "candidate:1", "to": 3})
        await n_host.send(frame)
        survives = (await asyncio.wait_for(n_joiners[2].recv(), TIMEOUT)) == frame
        n_joiners.append(await join_ok(url, n_code))
        fresh = await recv_json(n_host)
        ok_fresh = fresh == {"t": "peer", "id": 4}  # NOT 1: ids are never reused
        record("joiner leave frees the seat: gone+id, room survives, fresh id",
               full_hidden and ok_gone and reappeared and survives and ok_fresh,
               f"hidden={full_hidden} gone={gone} reappeared={reappeared} "
               f"survives={survives} fresh={fresh}")
    except Exception as e:
        record("joiner leave frees the seat: gone+id, room survives, fresh id",
               False, repr(e))

    # 31. host drop (max=4): the room dies and EVERY seated joiner gets the
    #     shipped bare {t:gone}; the code then answers nocode.
    try:
        assert n_host is not None and len(n_joiners) == 4, "no room from case 25/30"
        await n_host.close()
        gones = [await recv_json(jw) for jw in n_joiners[1:]]
        fan = all(g == {"t": "gone"} for g in gones)
        dead = await join_refused(url, n_code, "nocode")
        record("host drop fans {t:gone} out to every joiner, room deleted",
               fan and dead, f"gones={gones} dead={dead}")
        for jw in n_joiners[1:]:
            await jw.close()
    except Exception as e:
        record("host drop fans {t:gone} out to every joiner, room deleted",
               False, repr(e))

    # 32. capacity-aware listable(): a max=4 listed room with ONE joiner still
    #     appears in browse AND is still a thumbnail-pull candidate (it is
    #     still advertised, so it should keep being pulled); with all three
    #     seats taken it delists and stops being a candidate. Case 14 is the
    #     unmodified max==2 control.
    try:
        ph, pcode = await host_n(url, 4)
        await send(ph, {"t": "list", "level": 2, "difficulty": 1, "players": 1,
                        "proto": "4", "hash": "h1", "shots": 1})
        await barrier(ph)
        pj = [await join_ok(url, pcode)]
        await recv_json(ph)
        bws, listing = await browse(url)
        open_listed = any(r.get("code") == pcode for r in listing)
        await bws.close()
        cand_open = any(r.code == pcode for r in main._pull_candidates())
        pj.append(await join_ok(url, pcode))
        await recv_json(ph)
        pj.append(await join_ok(url, pcode))
        await recv_json(ph)
        bws, listing = await browse(url)
        full_hidden = all(r.get("code") != pcode for r in listing)
        await bws.close()
        cand_full = any(r.code == pcode for r in main._pull_candidates())
        record("a part-full max=4 room browses + pulls; a full one delists",
               open_listed and cand_open and full_hidden and not cand_full,
               f"browse@1={open_listed} cand@1={cand_open} "
               f"hidden@3={full_hidden} cand@3={cand_full}")
        for ws_ in (ph, *pj):
            await ws_.close()
    except Exception as e:
        record("a part-full max=4 room browses + pulls; a full one delists",
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
