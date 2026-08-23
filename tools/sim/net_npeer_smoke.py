#!/usr/bin/env python3
"""FOUR-MACHINE online co-op smoke + soak (cards 87242257 / 6fb406bc, Stages 11.9 + 11.11).

Drives FOUR eahl PROCESSES over LocalSocketNet -- one listed host playing Level 2 and THREE real
menu-session joiners (`--net-peers 3`, the full star) -- and asserts the N-peer session facts the
in-process suite (eaNetNPeer) structurally cannot reach, because they need real scenes and live
ships:

  * all four consoles print CONSISTENT four-seat rosters (host pri=0/1+2+3, joiners pri=N/0,
    each holding the other three players' ships as Remote / RemoteFriend puppets);
  * every peer's world actually HOLDS four ships (the host relay is what puts client A's ship
    on client B's screen -- there is no other route);
  * the host prints the per-peer `[netpeers]` metrics line, and nobody logs `dupBad` != 0;
  * the BANDWIDTH soak (card 6fb406bc): ~30 sim-seconds at full population, then the host's
    `[net]` line's txBps/rxBps -- the design doc's N=4 host-uplink figure, MEASURED (payload;
    real wire cost adds SCTP/DTLS/UDP/IP framing, ~2-3x at these packet sizes). Asserted
    nonzero and sane, printed for the record;
  * killing one joiner mid-level frees exactly its seats on the host AND on BOTH surviving
    joiners (EvPeerLeft), and the match PLAYS ON for the other three -- the card's match-end
    policy, mid-level, which the menu-runnable suite can only cover at the menus.

This is a SMOKE, not the JIP world-differ: tools/sim/net_jip_sync.py owns entity-level
convergence (and stays 2-process, its own header's rule).

Usage:
    dotnet build web/EvilAliensWeb -c Debug && dotnet build tools/headless -c Debug
    python tools/sim/net_npeer_smoke.py [--verbose]

Exit 0 = every assertion held; 1 = a finding (the report names it); 2 = the rig itself failed.
`--nettime game` ties each process's net clock to its own frame count, so four processes that
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
BPS_RE = re.compile(r"txB=(\d+) rxB=(\d+) txBps=(\d+) rxBps=(\d+)")


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
    # `&invuln` on the joiners too: their Keyboard ships get no input, so on the un-invulnerable
    # boot they die to Level 2's spawners partway through the soak and stay dead (a dead player
    # does not respawn until AllShipsDead, which the invulnerable HOST blocks forever) -- and the
    # four-ship structural check then fails on a timing lottery rather than on the session.
    join_flags = "?menu&noattract&netallowdebug&invuln&net=jipjoin&room=%s" % room

    problems = []
    peers = []

    def check(what, ok):
        print("  %s %s" % ("PASS" if ok else "FAIL", what))
        if not ok:
            problems.append(what)

    try:
        print("[npsmoke] booting the host (Level 2, listed, --net-peers 3, room %s)" % room)
        host = Peer("host", host_flags, args.port, ["--net-peers", "3"], args.verbose)
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
        for _ in range(30):
            host.step(30)
            j1.step(30)
            j2.step(30)

        print("[npsmoke] joiner3 attaches -- the full four-machine star (card 6fb406bc)")
        j3 = Peer("join3", join_flags, args.port, [], args.verbose)
        peers.append(j3)
        for _ in range(30):     # ~15 sim-seconds with all four live
            host.step(30)
            j1.step(30)
            j2.step(30)
            j3.step(30)

        print("[npsmoke] phase 1: four-machine rosters")
        rh, r1, r2, r3 = host.roster(), j1.roster(), j2.roster(), j3.roster()
        check("all four report a live session",
              rh is not None and r1 is not None and r2 is not None and r3 is not None)
        if rh and r1 and r2 and r3:
            hs, h_pri = seats(rh[0]), rh[1]
            check("host pri=0/1+2+3 (got %s)" % h_pri, h_pri == "0/1+2+3")
            check("host seats: 0 ours, 1+2+3 Remote (got %s)" % rh[0],
                  hs.get(0, "").endswith("*") and hs.get(1) == "Remote"
                  and hs.get(2) == "Remote" and hs.get(3) == "Remote")
            for n, (rj, own) in enumerate(((r1, 1), (r2, 2), (r3, 3)), start=1):
                sj, pj = seats(rj[0]), rj[1]
                want_pri = "%d/0" % own
                others_rf = all(sj.get(k) == "RemoteFriend" for k in (1, 2, 3) if k != own)
                check("joiner%d pri=%s (got %s)" % (n, want_pri, pj), pj == want_pri)
                check("joiner%d seats: 0 Remote, %d ours, the other two RemoteFriend (got %s)"
                      % (n, own, rj[0]),
                      sj.get(0) == "Remote" and sj.get(own, "").endswith("*") and others_rf)
            check("every world holds all FOUR ships (host %s / j1 %s / j2 %s / j3 %s)"
                  % (rh[2], r1[2], r2[2], r3[2]),
                  all(ship_owners(r[2]) == [0, 1, 2, 3] for r in (rh, r1, r2, r3)))
        check("the host printed the per-peer [netpeers] line",
              any("[netpeers] n=3" in ln for ln in host.log))
        for p in (host, j1, j2, j3):
            vals = [m.group(1) for m in map(DUPBAD_RE.search, p.log) if m is not None]
            check("%s logged dupBad=0 throughout (%d [net] lines)" % (p.name, len(vals)),
                  len(vals) > 0 and all(v == "0" for v in vals))

        print("[npsmoke] phase 2: the N=4 bandwidth soak (~30 sim-seconds, card 6fb406bc)")
        for _ in range(60):
            host.step(30)
            j1.step(30)
            j2.step(30)
            j3.step(30)
        # The LAST [net] line's rate covers a full-population report interval. Payload bytes
        # only -- real wire cost adds SCTP/DTLS/UDP/IP framing, ~2-3x at these packet sizes.
        rates = {}
        for p in (host, j1, j2, j3):
            ms = [m for m in map(BPS_RE.search, p.log) if m is not None]
            rates[p.name] = ms[-1] if ms else None
        check("every peer reports txB/rxB/txBps/rxBps on its [net] line",
              all(rates[p.name] is not None for p in (host, j1, j2, j3)))
        if rates["host"] is not None:
            tx = int(rates["host"].group(3))
            rx = int(rates["host"].group(4))
            print("  [npsmoke] MEASURED N=4 host payload: tx %d B/s, rx %d B/s"
                  % (tx, rx))
            for name in ("join1", "join2", "join3"):
                if rates[name] is not None:
                    print("  [npsmoke] %s payload: tx %s B/s, rx %s B/s"
                          % (name, rates[name].group(3), rates[name].group(4)))
            # The design doc's estimate was ~33 KB/s payload up at N=4. Bound it loosely --
            # this asserts the MEASUREMENT works and the magnitude is sane, not a tuning value.
            check("host uplink is live and within sanity bounds (1KB/s..200KB/s, got %d)" % tx,
                  1000 <= tx <= 200000)
            check("host downlink is live too (got %d)" % rx, rx >= 1000)

        print("[npsmoke] phase 3: joiner2 dies mid-level -- seats free, the match plays on")
        # Anchor the phase-3 log assertions HERE: a "session stop" from before the kill (a
        # jiphost re-arm, a joiner's own lobby teardown) is not what phase 3 is about.
        host_mark = len(host.log)
        j1_mark = len(j1.log)
        j3_mark = len(j3.log)
        j2.kill()
        peers.remove(j2)
        for _ in range(40):     # ~20 sim-seconds: past the 3+5s drop verdict, well settled
            host.step(30)
            j1.step(30)
            j3.step(30)
        rh2, r12, r32 = host.roster(), j1.roster(), j3.roster()
        check("host session SURVIVED the departure (role=host peer=up)",
              rh2 is not None and rh2[3] == "host" and rh2[4] == "up")
        check("joiner1 session survived too", r12 is not None and r12[4] == "up")
        check("joiner3 session survived too", r32 is not None and r32[4] == "up")
        if rh2 and r12 and r32:
            check("host freed exactly joiner2's seat (roster %s, pri %s)" % (rh2[0], rh2[1]),
                  2 not in seats(rh2[0]) and 1 in seats(rh2[0]) and 3 in seats(rh2[0])
                  and rh2[1] == "0/1+3")
            check("joiner1 freed it too -- the EvPeerLeft apply (roster %s)" % r12[0],
                  2 not in seats(r12[0]) and s_has(r12[0], 0, "Remote") and 1 in seats(r12[0])
                  and 3 in seats(r12[0]))
            check("joiner3 freed it too (roster %s)" % r32[0],
                  2 not in seats(r32[0]) and s_has(r32[0], 0, "Remote") and 3 in seats(r32[0])
                  and 1 in seats(r32[0]))
        check("the host said it plays on ('peer(s) remain, playing on')",
              any("peer(s) remain, playing on" in ln for ln in host.log[host_mark:]))
        check("nobody called the MATCH ended (no 'session stop' on host/j1/j3 after the kill)",
              not any("session stop" in ln
                      for ln in host.log[host_mark:] + j1.log[j1_mark:] + j3.log[j3_mark:]))
    except RuntimeError as ex:
        print("RIG FAILURE: %s" % ex)
        return 2
    finally:
        for p in peers:
            p.quit()

    if problems:
        print("[npsmoke] FAILURES: %d" % len(problems))
        return 1
    print("[npsmoke] ALL PASS -- four machines, one match, a measured uplink, one survivable departure")
    return 0


def s_has(roster_field, slot, dev):
    return seats(roster_field).get(slot) == dev


if __name__ == "__main__":
    sys.exit(main())
