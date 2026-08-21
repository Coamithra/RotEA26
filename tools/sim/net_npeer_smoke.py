#!/usr/bin/env python3
"""THREE-MACHINE online co-op smoke (card 87242257, Stage 11.9).

Drives three eahl PROCESSES over LocalSocketNet -- one listed host playing Level 2 and TWO real
menu-session joiners (`--net-peers 2`) -- and asserts the N-peer session facts the in-process
suite (eaNetNPeer) structurally cannot reach, because they need real scenes and live ships:

  * all three consoles print CONSISTENT three-seat rosters (host pri=0/1+2, joiners pri=1/0 and
    2/0, each holding the other two players' ships as Remote / RemoteFriend puppets);
  * every peer's world actually HOLDS three ships (the host relay is what puts client A's ship
    on client B's screen -- there is no other route);
  * the host prints the per-peer `[netpeers]` metrics line, and nobody logs `dupBad` != 0;
  * killing one joiner mid-level frees exactly its seats on the host AND on the surviving
    joiner (EvPeerLeft), and the match PLAYS ON for the other two -- the card's match-end
    policy, mid-level, which the menu-runnable suite can only cover at the menus.

This is a SMOKE, not the JIP world-differ: tools/sim/net_jip_sync.py owns entity-level
convergence (and stays 2-process); the full N=4 soak/bandwidth pass is card 6fb406bc (11.11).

Usage:
    dotnet build web/EvilAliensWeb -c Debug && dotnet build tools/headless -c Debug
    python tools/sim/net_npeer_smoke.py [--verbose]

Exit 0 = every assertion held; 1 = a finding (the report names it); 2 = the rig itself failed.
`--nettime game` ties each process's net clock to its own frame count, so three processes that
are not stepped in lockstep still see each other's cadences at game rate (the net_jip_sync rule:
--nodraw runs ~17x real time, which would otherwise starve the wire).
"""

import argparse
import os
import queue
import re
import subprocess
import sys
import threading
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
EAHL = os.path.join(REPO, "tools", "headless", "bin", "Debug", "net8.0", "eahl.exe")

ROSTER_RE = re.compile(r"\[net\] roster=(\S+) pri=(\S+) ships=(\S+) .*role=(\S+) peer=(\S+)")
DUPBAD_RE = re.compile(r"dupBad=(\d+)")


class Peer(object):
    """One eahl --repl process, spoken to over stdin/stdout (the net_jip_sync shape)."""

    frame = 0

    def __init__(self, name, flags, port, extra_args, verbose):
        args = [EAHL, "--repl", "--nodraw", "--nettime", "game", "--flags", flags]
        args += ["--net-port", str(port)]
        args += extra_args
        self.name = name
        self.verbose = verbose
        self.log = []
        self.proc = subprocess.Popen(
            args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, universal_newlines=True, bufsize=1, cwd=REPO)
        stdout = self.proc.stdout
        stdin = self.proc.stdin
        if stdout is None or stdin is None:
            raise RuntimeError("%s: the process was spawned without pipes" % name)
        self.stdout = stdout
        self.stdin = stdin
        self.lines = queue.Queue()
        threading.Thread(target=self._pump, args=(stdout,), daemon=True).start()
        self._read_until(lambda ln: ln.startswith("ok ready") or ln.startswith("err "))

    def _pump(self, stdout):
        for line in stdout:
            self.lines.put(line)
        self.lines.put(None)

    def _read_until(self, done, timeout=180.0):
        out = []
        deadline = time.time() + timeout
        while True:
            try:
                line = self.lines.get(timeout=max(0.1, deadline - time.time()))
            except queue.Empty:
                raise RuntimeError("%s: timed out waiting for a reply (log tail: %s)"
                                   % (self.name, " | ".join(self.log[-6:])))
            if line is None:
                raise RuntimeError("%s: the process exited (log tail: %s)"
                                   % (self.name, " | ".join(self.log[-6:])))
            line = line.rstrip("\r\n")
            self.log.append(line)
            if self.verbose:
                sys.stderr.write("[%s] %s\n" % (self.name, line))
            out.append(line)
            if done(line):
                return out

    def _send(self, cmd):
        self.stdin.write(cmd + "\n")
        self.stdin.flush()

    def step(self, frames):
        self._send("step %d nodraw" % frames)
        self._read_until(lambda ln: ln.startswith("ok step") or ln.startswith("err "))

    def roster(self):
        """The RosterDump line, parsed: (seats, pri, ships, role, peer)."""
        self._send("eval NetRoster")
        lines = self._read_until(lambda ln: ROSTER_RE.search(ln) is not None
                                 or "no session" in ln or ln.startswith("err "))
        m = ROSTER_RE.search(lines[-1])
        return m.groups() if m else None

    def kill(self):
        # A hard process kill on purpose: no pagehide bye, no EvLeave -- the wifi-drop shape the
        # per-peer timeout verdict exists for.
        self.proc.kill()

    def quit(self):
        try:
            self._send("quit")
            self.proc.wait(timeout=15)
        except Exception:
            self.proc.kill()


def seats(roster_field):
    """'0:Keyboard*,1:Remote' -> {0: 'Keyboard*', 1: 'Remote'} ('-' -> {})."""
    out = {}
    if roster_field and roster_field != "-":
        for part in roster_field.split(","):
            slot, dev = part.split(":", 1)
            out[int(slot)] = dev
    return out


def ship_owners(ships_field):
    if not ships_field or ships_field == "-":
        return []
    return sorted(int(part.split(":", 1)[0]) for part in ships_field.split(","))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=53291)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(EAHL):
        print("RIG FAILURE: build eahl first (dotnet build tools/headless -c Debug)")
        return 2

    room = "npsmoke%d" % (os.getpid() % 1000)
    host_flags = ("?level=Level2&invuln&aiplayer&noattract&netjip&seed=7"
                  "&net=jiphost&room=%s" % room)
    join_flags = "?menu&noattract&netallowdebug&net=jipjoin&room=%s" % room

    problems = []
    peers = []

    def check(what, ok):
        print("  %s %s" % ("PASS" if ok else "FAIL", what))
        if not ok:
            problems.append(what)

    try:
        print("[npsmoke] booting the host (Level 2, listed, --net-peers 2, room %s)" % room)
        host = Peer("host", host_flags, args.port, ["--net-peers", "2"], args.verbose)
        peers.append(host)
        host.step(240)  # settle the level past the boot fade

        print("[npsmoke] joiner1 attaches")
        j1 = Peer("join1", join_flags, args.port, [], args.verbose)
        peers.append(j1)
        for _ in range(30):     # ~15 sim-seconds interleaved: attach + warm + EvReady + settle
            host.step(30)
            j1.step(30)

        print("[npsmoke] joiner2 attaches -- the wire goes past two peers")
        j2 = Peer("join2", join_flags, args.port, [], args.verbose)
        peers.append(j2)
        for _ in range(40):     # ~20 sim-seconds with all three live
            host.step(30)
            j1.step(30)
            j2.step(30)

        print("[npsmoke] phase 1: three-machine rosters")
        rh, r1, r2 = host.roster(), j1.roster(), j2.roster()
        check("all three report a live session", rh is not None and r1 is not None and r2 is not None)
        if rh and r1 and r2:
            hs, h_pri = seats(rh[0]), rh[1]
            s1, p1 = seats(r1[0]), r1[1]
            s2, p2 = seats(r2[0]), r2[1]
            check("host pri=0/1+2 (got %s)" % h_pri, h_pri == "0/1+2")
            check("host seats: 0 ours, 1+2 Remote (got %s)" % rh[0],
                  hs.get(0, "").endswith("*") and hs.get(1) == "Remote" and hs.get(2) == "Remote")
            check("joiner1 pri=1/0 (got %s)" % p1, p1 == "1/0")
            check("joiner1 seats: 0 Remote (the host), 1 ours, 2 RemoteFriend (got %s)" % r1[0],
                  s1.get(0) == "Remote" and s1.get(1, "").endswith("*") and s1.get(2) == "RemoteFriend")
            check("joiner2 pri=2/0 (got %s)" % p2, p2 == "2/0")
            check("joiner2 seats: 0 Remote, 1 RemoteFriend (the relay), 2 ours (got %s)" % r2[0],
                  s2.get(0) == "Remote" and s2.get(1) == "RemoteFriend" and s2.get(2, "").endswith("*"))
            check("every world holds all THREE ships (host %s / j1 %s / j2 %s)"
                  % (rh[2], r1[2], r2[2]),
                  ship_owners(rh[2]) == [0, 1, 2] and ship_owners(r1[2]) == [0, 1, 2]
                  and ship_owners(r2[2]) == [0, 1, 2])
        check("the host printed the per-peer [netpeers] line",
              any("[netpeers] n=2" in ln for ln in host.log))
        for p in (host, j1, j2):
            vals = [m.group(1) for m in map(DUPBAD_RE.search, p.log) if m is not None]
            check("%s logged dupBad=0 throughout (%d [net] lines)" % (p.name, len(vals)),
                  len(vals) > 0 and all(v == "0" for v in vals))

        print("[npsmoke] phase 2: joiner2 dies mid-level -- seats free, the match plays on")
        # Anchor the phase-2 log assertions HERE: a "session stop" from before the kill (a
        # jiphost re-arm, a joiner's own lobby teardown) is not what phase 2 is about.
        host_mark = len(host.log)
        j1_mark = len(j1.log)
        j2.kill()
        peers.remove(j2)
        for _ in range(40):     # ~20 sim-seconds: past the 3+5s drop verdict, well settled
            host.step(30)
            j1.step(30)
        rh2, r12 = host.roster(), j1.roster()
        check("host session SURVIVED the departure (role=host peer=up)",
              rh2 is not None and rh2[3] == "host" and rh2[4] == "up")
        check("joiner1 session survived too", r12 is not None and r12[4] == "up")
        if rh2 and r12:
            check("host freed exactly joiner2's seat (roster %s, pri %s)" % (rh2[0], rh2[1]),
                  2 not in seats(rh2[0]) and 1 in seats(rh2[0]) and rh2[1] == "0/1")
            check("joiner1 freed it too -- the EvPeerLeft apply (roster %s)" % r12[0],
                  2 not in seats(r12[0]) and s_has(r12[0], 0, "Remote") and 1 in seats(r12[0]))
        check("the host said it plays on ('peer(s) remain, playing on')",
              any("peer(s) remain, playing on" in ln for ln in host.log[host_mark:]))
        check("nobody called the MATCH ended (no 'session stop' on host/j1 after the kill)",
              not any("session stop" in ln for ln in host.log[host_mark:] + j1.log[j1_mark:]))
    except RuntimeError as ex:
        print("RIG FAILURE: %s" % ex)
        return 2
    finally:
        for p in peers:
            p.quit()

    if problems:
        print("[npsmoke] FAILURES: %d" % len(problems))
        return 1
    print("[npsmoke] ALL PASS -- three machines, one match, one survivable departure")
    return 0


def s_has(roster_field, slot, dev):
    return seats(roster_field).get(slot) == dev


if __name__ == "__main__":
    sys.exit(main())
