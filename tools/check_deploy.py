#!/usr/bin/env python
"""Verify a published deployment over plain HTTP. Exits non-zero on any failure.

Everything here is a check that only a REAL host can fail -- the case-sensitive
filesystem, the base href the deploy stamped in, the build fingerprint peers
compare, the signaling server the game dials. None of it can be caught locally.

    python tools/check_deploy.py                             # check the default target
    python tools/check_deploy.py --hash 2f984f6dedcd1ec4     # also assert the build hash
    python tools/check_deploy.py --url https://coamithra.github.io/RotEA26/
    python tools/check_deploy.py --no-signal                 # skip the signaling probe

Stdlib only -- no paramiko, no credentials, nothing to configure. Run it from
anywhere, including a machine that cannot deploy.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.error
import urllib.request
from urllib.parse import urlparse

DEFAULT_URL = "https://haraldmaassen.com/RotEA26/"
SIGNAL_HEALTH = "https://notzelda.haraldmaassen.com/rotea/health"

# Lowercase asset paths the game fetches at runtime. Windows resolves any casing,
# a Linux host does not -- a mis-cased path passes every local test and then 404s
# into a black screen. Paired with a deliberately WRONG-cased probe below, so a
# green tick means "the host is case-sensitive AND these paths are right", not
# "the host happened to be forgiving".
# Deliberately spread across asset kinds -- a text level, a JSON data blob, a
# compiled shader, and one under gfx/ (241 MB of the payload, and where a casing
# slip is likeliest). Long-lived files on purpose, so this still works against an
# older published build.
CONTENT_PROBES = [
    "Content/levels/level3.txt",
    "Content/data/landed_offsets.json",
    "Content/bloom/bloomextract.mgfxo",
    "Content/gfx/base/756-v1.dds",
]
WRONG_CASE_PROBE = "content/levels/level3.txt"

failures: list[str] = []


def fetch(url: str, timeout: int = 30) -> tuple[int, bytes]:
    req = urllib.request.Request(url, headers={"User-Agent": "rotea-check-deploy"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, b""
    except Exception as e:  # DNS, TLS, timeout -- all "the site is not reachable"
        print(f"  ! {url}: {e}")
        return 0, b""


def check(label: str, ok: bool, detail: str = "") -> bool:
    """`detail` explains a FAILURE -- it is phrased that way, so only show it then."""
    print(f"  {'PASS' if ok else 'FAIL'}  {label}" + ("" if ok else f" -- {detail}"))
    if not ok:
        failures.append(label)
    return ok


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--url", default=DEFAULT_URL, help=f"site base URL (default {DEFAULT_URL})")
    ap.add_argument("--hash", help="expected window.eaBuildHash (from the deploy output)")
    ap.add_argument("--base-href", help="expected <base href> (default: the URL's own path)")
    ap.add_argument("--no-signal", action="store_true", help="skip the signaling health probe")
    args = ap.parse_args()

    base = args.url if args.url.endswith("/") else args.url + "/"
    # The site's own path IS the base href it must carry -- that is the whole
    # point of the stamp, so derive it rather than making the caller repeat it.
    expect_href = args.base_href or urlparse(base).path

    print(f"checking {base}")

    # --- index.html -------------------------------------------------------
    status, body = fetch(base)
    if not check("index.html reachable", status == 200, f"HTTP {status}"):
        sys.exit(report())
    html = body.decode("utf-8", "replace")

    m = re.search(r'<base href="([^"]*)"', html)
    check("base href", bool(m) and m.group(1) == expect_href,
          f"got {m.group(1)!r}, want {expect_href!r}" if m else "no <base href> at all")

    m = re.search(r"window\.eaBuildHash = '([^']*)'", html)
    got_hash = m.group(1) if m else None
    check("eaBuildHash stamped", got_hash not in (None, "dev"),
          f"got {got_hash!r} -- 'dev' means the deploy did not stamp it "
          "(peers cannot match it, and the FPS HUD is visible)")
    if args.hash:
        check("eaBuildHash matches this deploy", got_hash == args.hash,
              f"live {got_hash!r} != expected {args.hash!r}")

    # --- the WASM runtime -------------------------------------------------
    status, body = fetch(base + "_framework/blazor.boot.json")
    ok = check("blazor.boot.json", status == 200, f"HTTP {status}")
    if ok:
        try:
            json.loads(body)
            check("blazor.boot.json parses", True)
        except Exception as e:
            check("blazor.boot.json parses", False, str(e))

    # --- case sensitivity -------------------------------------------------
    for rel in CONTENT_PROBES:
        status, _ = fetch(base + rel)
        check(f"content path {rel}", status == 200, f"HTTP {status}")
    status, _ = fetch(base + WRONG_CASE_PROBE)
    check("host IS case-sensitive (wrong-case probe 404s)", status == 404,
          f"HTTP {status} for {WRONG_CASE_PROBE} -- a forgiving host makes the "
          "checks above meaningless; a real deploy would still break")

    # --- online co-op signaling ------------------------------------------
    if not args.no_signal:
        status, body = fetch(SIGNAL_HEALTH)
        ok = check("signaling /health", status == 200, f"HTTP {status}")
        if ok:
            try:
                check("signaling reports ok", json.loads(body).get("ok") is True,
                      body.decode("utf-8", "replace")[:120])
            except Exception as e:
                check("signaling reports ok", False, str(e))

    sys.exit(report())


def report() -> int:
    if failures:
        print(f"\n{len(failures)} FAILED: " + ", ".join(failures))
        return 1
    print("\nall checks passed")
    return 0


if __name__ == "__main__":
    main()
