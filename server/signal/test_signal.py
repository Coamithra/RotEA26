"""Self-contained protocol test for the RotEA signaling server.

Run:  python test_signal.py
Starts the app in-process on an ephemeral port (uvicorn), drives it with
the `websockets` client (already a dependency via uvicorn[standard]),
prints PASS/FAIL per case, exits nonzero on any failure.
"""

import asyncio
import json
import sys

import uvicorn
import websockets

TIMEOUT = 3.0
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


async def main() -> int:
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
    sys.exit(asyncio.run(main()))
