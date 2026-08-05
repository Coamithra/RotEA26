#!/usr/bin/env python
"""net_jip_sync.py -- the automated join-in-progress suite (card 054947f3).

Drives TWO eahl processes -- a real listed HOST playing a level, and a real menu-session
JOINER that attaches to it mid-level -- over the localhost loopback the `eaNet` facade is
backed by headlessly, and DIFFS the two worlds once the attach has settled.

    dotnet build tools/headless -c Debug
    python tools/sim/net_jip_sync.py --level Level2
    python tools/sim/net_jip_sync.py --level Level1 --level Level2 --level Level3

Exit 0 = every join produced a matching world; 1 = a mismatch (or a join that never
attached); 2 = the rig could not run (no eahl, bad arguments).

WHY TWO PROCESSES. One process holds one `Game.Components` -- `ComponentBin`'s only ctor
binds it, `Oracle`/`CollisionHandler` bind the same collection, `ServiceHelper` is a
process-global registry -- so two independent worlds in one process is unreachable (net
CLAUDE.md). Everything already automated about join-in-progress therefore covers ONE leg
each against a scripted peer; the claim nothing could make was "the joiner ends up with the
host's world", which is a diff and needs both worlds to exist.

WHY IT IS NOT A `--script` PROBE. `run_probes.py` runs one `eahl --script`; this needs two
processes stepped in lockstep. `tools/headless/probes/net_jip_dump.txt` covers the half that
DOES fit there -- that the dump reports a non-vacuous world at all -- so a dump that silently
stopped reporting cannot make this tool green.

THE INTERLEAVE, and why it is not optional. Each process advances only when told to, and
`--nettime game` ties the net layer's clock to its own frame count, so two peers that are not
stepped alike drift apart in net time and the far one is timed out (3 s + 5 s grace = 480
frames). Chunks stay well under that. It is also what makes the diff meaningful: both worlds
are dumped having advanced the same number of virtual milliseconds.
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

# The dump's own format version. Bumped in NetJipDump.cs when a line's SHAPE changes, and
# checked rather than assumed -- a differ silently comparing half a world is the failure this
# tool exists to catch one layer down.
FORMAT_VERSION = 4

# A port deliberately outside the range PortForRoom derives into (49152..61151), so --no-pair
# dials somewhere nothing can be listening whatever room name is in play.
PORT_NOWHERE = 62999

# run_join's "stop, there is nothing left to join" answer, distinct from a list of problems.
LEVEL_OVER = "level-over"

# ...and its "this join proved nothing, do not count it either way" answer: the host world was
# empty at the dump, so there was nothing to compare (card d108c459).
VACUOUS = "vacuous"

# Joins a soak needs before the `uns` control is asserted rather than skipped -- see main.
# Well under the default three-level soak's ~30 and well over what a --cap 120 spot check runs.
UnsettledControlMinJoins = 10


def new_stats():
    """The per-run counters. ONE definition: run_join indexes every key, and a call site that
    built the dict by hand would KeyError the moment a counter was added."""
    return {"owners": 0, "unsettled": 0.0, "joins": 0,
            # Card d108c459's three calibration counters. All REPORTED at run level, because
            # each of them is a mismatch this tool decided not to fail on -- and a rule that
            # silently deletes a class is indistinguishable from a differ that stopped looking.
            "dropped": 0,       # mismatches the re-settle cleared (converging staleness)
            "transit": 0,       # entities skipped because a death was in progress on one end
            "released": 0,      # host-only ids the joiner explained (its own removal ledger)
            "skipped": 0,       # joins not counted: the host world was empty at the dump
            # ...and the hp leg's MATERIAL, asserted at run level for the same reason `uns` is:
            # `hpwire` reading `-` SKIPS the comparison, so a build that stopped recording it
            # would delete the whole hp dimension and every soak would still read green.
            "hpwire": 0}


DUMP_END_RE = re.compile(r"\[netjip\] dump v(\d+) role=(\S+) active=(\d) peer=(\d) ids=(\d+) end")
ENT_RE = re.compile(r"\[netjip\] ent (.*)")
STEP_FRAME_RE = re.compile(r"ok step frame=(\d+)")


# ---------------------------------------------------------------------------- peers


class Peer(object):
    """One eahl --repl process, spoken to over stdin/stdout."""

    def __init__(self, name, flags, port, verbose):
        args = [EAHL, "--repl", "--nodraw", "--nettime", "game", "--flags", flags]
        if port:
            args += ["--net-port", str(port)]
        self.name = name
        self.verbose = verbose
        self.log = []
        self.proc = subprocess.Popen(
            args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, universal_newlines=True, bufsize=1, cwd=REPO)
        # Popen's pipes are Optional in the type sense (they are None unless PIPE was asked
        # for). They were, so this is a rig-invariant check rather than error handling -- it
        # exists so a later `self.proc.stdout.readline()` cannot be a None dereference.
        self.stdout = self.proc.stdout
        self.stdin = self.proc.stdin
        if self.stdout is None or self.stdin is None:
            raise RuntimeError("%s: the process was spawned without pipes" % name)
        # A READER THREAD, so the timeout below is REAL. `readline()` blocks, so checking a
        # deadline after it returns bounds nothing: a peer that wedges without printing (a
        # blocked socket send, a hung eahl) would hang the driver forever -- and running
        # unattended is this tool's whole job.
        self.lines = queue.Queue()
        threading.Thread(target=self._pump, daemon=True).start()
        self._read_until(lambda ln: ln.startswith("ok ready") or ln.startswith("err "))

    def _pump(self):
        for line in self.stdout:
            self.lines.put(line)
        self.lines.put(None)            # EOF sentinel

    def _read_until(self, done, timeout=180.0):
        """Collect stdout until `done(line)`. Returns the lines consumed.

        The game prints its own diagnostics between replies, so a reply is recognised by
        shape, never by position.
        """
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
        lines = self._read_until(lambda ln: ln.startswith("ok step") or ln.startswith("err "))
        for line in lines:
            m = STEP_FRAME_RE.search(line)
            if m:
                self.frame = int(m.group(1))

    # eahl's own frame counter, read back off its `ok step frame=` reply rather than accumulated
    # here -- the boot tick and any `--frames` are already in it, and the caller wants "how much
    # world has gone by", which is exactly what that number is.
    frame = 0

    def frame_mark(self):
        return self.frame

    def frames_since(self, mark):
        return self.frame - mark

    def dump(self):
        self._send("eval NetJipDump")
        lines = self._read_until(lambda ln: DUMP_END_RE.search(ln) is not None)
        return parse_dump(lines)

    def quit(self):
        try:
            self._send("quit")
            self.proc.wait(timeout=20)
        except Exception:
            try:
                self.proc.kill()
            except Exception:
                pass


# ---------------------------------------------------------------------------- parsing


def parse_kv(rest):
    """`k=v k=v ...` -> dict. Values never contain spaces, by the format's own contract."""
    out = {}
    for tok in rest.split():
        if "=" in tok:
            k, v = tok.split("=", 1)
            out[k] = v
    return out


def parse_dump(lines):
    d = {"ents": {}, "scene": None, "hud": None, "meta": None, "gone": set()}
    for line in lines:
        m = DUMP_END_RE.search(line)
        if m:
            d["meta"] = {
                "version": int(m.group(1)), "role": m.group(2),
                "active": m.group(3) == "1", "peer": m.group(4) == "1",
                "ids": int(m.group(5)),
            }
            continue
        m = ENT_RE.search(line)
        if m:
            kv = parse_kv(m.group(1))
            d["ents"][kv.get("id")] = kv
            continue
        if "[netjip] gone " in line:
            # The client's REMOVAL LEDGER -- the ids it once held and let go (dump v4). `-` is
            # the empty set and is also what a host prints, so the parse is the same either way.
            rest = line.split("[netjip] gone ", 1)[1].strip()
            if rest != "-":
                d["gone"] = set(rest.split(","))
        elif "[netjip] scene " in line:
            d["scene"] = line.split("[netjip] scene ", 1)[1].strip()
        elif "[netjip] hud " in line:
            d["hud"] = line.split("[netjip] hud ", 1)[1].strip()
    return d


# ---------------------------------------------------------------------------- the diff


def fnum(kv, key):
    try:
        return float(kv[key])
    except (KeyError, ValueError):
        return None


def in_transition(e):
    """Is this end of the pair part-way through a death?

    `dead` means the component's removal is already queued; `dying` means a deferred death
    animation is running. Either one says the entity is LEAVING, which is a state the two peers
    reach at different moments by construction -- see diff_entity for why that is not a defect.
    """
    return e.get("dead") == "1" or e.get("dying") == "1"


def diff_entity(h, c, tol, stats):
    """Per-id key comparison. Returns a list of (key, message) pairs.

    The KEY identifies the comparison rather than its values, so the re-settle confirm in
    run_join can ask "is this same disagreement still here" without being fooled by a
    continuously-varying number that never repeats exactly.

    A key is SKIPPED when the entity's own `local=` seam says the game simulates it locally
    (NetFrameLocal / NetSpinPerMs / NetScaleLocal / NetPathOffset) -- so a skip is the GAME's
    statement, never a type name on a list in this tool. The union of the two ends is taken:
    either end declaring a key local is enough.
    """
    bad = []
    local = set()
    for side in (h, c):
        v = side.get("local", "-")
        if v != "-":
            local.update(v.split(","))

    if h.get("type") != c.get("type"):
        bad.append(("type", "type %s != %s" % (h.get("type"), c.get("type"))))
        return bad          # nothing below means anything across two different types
    if h.get("idx") != c.get("idx"):
        bad.append(("idx", "typeIdx %s != %s" % (h.get("idx"), c.get("idx"))))

    # A DEATH IN PROGRESS ON EITHER END STOPS THE SAMPLED COMPARISON (card d108c459), and the
    # two flags that say so are not compared at all. Both statements need defending, since this
    # is the tool declining to look at something.
    #
    # `dead` IS ON NO WIRE FIELD. On the host it means "this component's removal was queued this
    # tick" -- a one-tick state the client cannot mirror in time and is never told about as such;
    # on the client it means "I killed it locally with my own bullet". Comparing them asks two
    # peers to agree about a word that means different things on each. Measured: it was the
    # single largest residual class on the default soak, 5 of 8 failing joins.
    #
    # `dying` IS an event and does ride the wire (EvDying), but it legitimately LEADS on
    # whichever peer's bullet landed: a joiner that kills a BattleSkull on its own lagged puppet
    # begins the deferred death immediately, while the host's copy is still at full hp for
    # another lane's worth of time -- and the reverse for the host's own AI ship. What the beat
    # guarantees is that it arrives, not that it arrives simultaneously.
    #
    # The other sampled keys go with them because they are meaningless mid-death: hp is 0 on the
    # end that has begun and full on the end that has not (measured: gaps of 19-25 on a
    # BattleSkull, against 0-7 for the same type alive).
    #
    # WHAT NOW COVERS THE DEATH CHOREOGRAPHY: `eaNetDeathFx` and the EvDying/EvDeath probes, not
    # this suite -- it never had a way to time a join into a 2.5-5 s death window anyway (see the
    # "not reached" row of the mutation matrix in net CLAUDE.md).
    # `prov=` and `owner=` below are NOT gated by it: they are identity, not a sample, and a
    # provisional puppet is just as wrong for dying while provisional.
    transit = in_transition(h) or in_transition(c)
    if transit:
        stats["transit"] += 1

    if not transit:
        # `pos` is one token "x,y" -- split here rather than in the C# format, so every dump field
        # stays a single `key=value` with no spaces and the tokeniser above needs no special case.
        try:
            hx, hy = [float(v) for v in h["pos"].split(",")]
            cx, cy = [float(v) for v in c["pos"].split(",")]
            dist = ((hx - cx) ** 2 + (hy - cy) ** 2) ** 0.5
        except (KeyError, ValueError):
            bad.append(("pos", "pos unparseable (%s vs %s)" % (h.get("pos"), c.get("pos"))))
        else:
            # A path-anchored type carries a locally-evaluated periodic offset, so its position
            # legitimately swings by that offset's amplitude between corrections.
            limit = tol["pos_path"] if "path" in local else tol["pos"]
            if dist > limit:
                bad.append(("pos", "pos %.1fpx apart (%s vs %s, limit %.1f)"
                            % (dist, h.get("pos"), c.get("pos"), limit)))

        if "rot" not in local:
            a, b = fnum(h, "rot"), fnum(c, "rot")
            # ANGULAR difference, wrapped: rotation is an angle, so -0.014 and 6.269 are the same
            # heading and a plain subtraction reports 6.28 rad of disagreement for a perfect match.
            if a is not None and b is not None:
                d = abs(a - b) % 6.283185307179586
                d = min(d, 6.283185307179586 - d)
                if d > tol["rot"]:
                    bad.append(("rot", "rot %.3f vs %.3f (%.3f rad apart)" % (a, b, d)))
        if "scale" not in local:
            a, b = fnum(h, "scale"), fnum(c, "scale")
            # Relative, and loose: several types PULSATE their scale (a Powerup's throb), which is
            # an ordinary continuously-varying replicated value corrected once per snapshot turn --
            # the same class as position, not a construction constant.
            if (a is not None and b is not None
                    and abs(a - b) > max(tol["scale_abs"], abs(a) * tol["scale_rel"])):
                bad.append(("scale", "scale %.4f vs %.4f" % (a, b)))
        if "frame" not in local:
            a, b = fnum(h, "frame"), fnum(c, "frame")
            if a is not None and b is not None and abs(a - b) > tol["frame"]:
                bad.append(("frame", "frame %.2f vs %.2f" % (a, b)))

        # hp IS COMPARED WIRE-TO-WIRE: the hp the host last SENT for this entity against the hp
        # the joiner last APPLIED -- both sides' `hpwire` -- and NEITHER side's live `hp` (card
        # d108c459, the same correction `uns` is for score). The two live values are not the same
        # quantity: a client hit-tests puppets with its own bullets, so its copy carries damage
        # the host has not credited; and the host has moved on since this entity's round-robin
        # turn came up. Measured that way, a `Boss` read 210 vs 180 and then 211 vs 179 -- only a
        # tolerance near 40 covers it, which is wide enough to pass a UFO at 2 against a host's
        # 10, i.e. wide enough to assert nothing.
        #
        # Comparing what crossed the wire cancels BOTH terms, so the leg gets sharper rather than
        # looser: it is an exact delivery assertion instead of a tolerance around two quantities
        # that were never the same thing.
        #
        # `-` means no hp has crossed the wire for this entity yet (it may have entered the world
        # this turn), and a non-killable reads `-` forever; both are the same skip here.
        hs, hw = h.get("hpwire", "-"), c.get("hpwire", "-")
        if hs != "-" and hw != "-":
            stats["hpwire"] += 1
            try:
                sent, got = int(hs), int(hw)
            except ValueError:
                bad.append(("hp", "hp unparseable (%s vs %s)" % (hs, hw)))
            else:
                # 1. DELIVERY, and it is EXACT apart from a packet in flight. Both numbers are
                #    "what crossed the wire" -- the host's last broadcast value against the last
                #    one the joiner took off it -- so they are the same quantity and neither
                #    carries the terms that made the live values incomparable.
                if abs(sent - got) > tol["hp"]:
                    bad.append(("hp", "hp %d sent vs %d received off the wire (live %s vs %s, "
                                      "limit %d)"
                                % (sent, got, h.get("hp"), c.get("hp"), tol["hp"])))
                # 2. THE CLAMP'S OWN INVARIANT, exact and with no tolerance at all.
                #    `KillableAlien.NetApplyHp` ONLY EVER LOWERS -- it refuses a value at or above
                #    the puppet's current hp so an older snapshot cannot resurrect hits this
                #    client has already landed -- and local damage only lowers too. So a puppet
                #    holding MORE than the last value it was told is a state no path produces:
                #    either it adopted something it should have refused, or something raised it
                #    locally. The measured direction confirms the rule is the right shape: over
                #    132 gaps across 6 seeds, 132 were client-lower, 0 higher, 0 equal.
                #
                #    WHAT THIS DOES NOT COVER, stated plainly because a tolerance would have
                #    hidden it: a wrong-but-LOWER apply is indistinguishable from ordinary local
                #    damage seen from the host's side, so "NetApplyHp assigns the received value"
                #    is not this suite's to prove -- see the suite section in net CLAUDE.md.
                live = fnum(c, "hp")
                if live is not None and live > got:
                    bad.append(("hp-clamp",
                                "the joiner's puppet holds hp %s, ABOVE the %d it last received "
                                "off the wire -- NetApplyHp only ever lowers, so nothing should "
                                "raise it" % (c.get("hp"), got)))

    # CARD de4d5d65's PROVISIONAL SHAPE, and one of the two assertions this whole tool exists
    # for: a puppet the snapshot self-heal built on DEFAULT spawn extras and no later EvSpawn
    # rebuilt. It is the wrong powerup type / saucer sheet / bonus tint, permanently, and it
    # looks like a perfectly ordinary entity from every other angle.
    if c.get("prov") == "1":
        bad.append(("prov",
                    "the joiner's copy is PROVISIONAL (self-healed on default spawn extras and "
                    "never rebuilt by the reliable EvSpawn -- card de4d5d65)"))

    # THE EMITTER, and the second of the two assertions this tool exists for (card 9a7ee4c0).
    # It is `prov=` one hop downstream and the VISIBLE half of it: a beam whose owner the joiner
    # could not resolve -- because the emitter puppet did not exist when the beam was built --
    # is card 9ccfe295's ownerless shape, the one that let a big laser UFO shoot itself dead on
    # the joiner. Compared EXACTLY: `owner=` is a netId, netIds are identity-mapped across the
    # pair, and the legitimately unowned beams (every SetupSingleShot shooter, GameScene's
    # warm-up prime) report "-" on BOTH ends. This leg's positive control lives in
    # `eaNetIdReuse` section 7, not here -- see run_join for why.
    #
    # THE ONE BENIGN DISAGREEMENT: an emitter's REMOVAL is not simultaneous on the two peers, and
    # a beam drops its owner reference when its emitter leaves the world (Lazer.OnComponentRemoved,
    # both ends). A dump landing inside that lag reads `owner N vs -` with nothing wrong. It is
    # narrow -- one snapshot lane's worth -- so it is named rather than tolerated: a persistent
    # disagreement is the defect, a single join's is worth re-running before believing.
    if h.get("owner") != c.get("owner"):
        bad.append(("owner",
                    "owner %s vs %s -- the two ends disagree about this entity's emitter "
                    "(card 9ccfe295's ownerless beam)" % (h.get("owner"), c.get("owner"))))

    # EXTRAS ARE COMPARED FOR STRUCTURE (length), NOT CONTENT, and it is worth knowing why
    # rather than rediscovering it. Both blocks are RE-ENCODED off the live entity, and at
    # least two shipped descriptors read drifting state there -- FlyingSpider's spawn anchor
    # carries the swivel phase, UFO's spawn flags carry `hasbonus` -- so byte equality is not a
    # property a correct pair has (measured: every wasp in a Level-2 soak). A LENGTH difference
    # is still a real fault: it means the two ends disagree about the block's SHAPE, which no
    # amount of drift produces. The dimension the content was meant to cover is `prov=` above,
    # which is exact.
    for key in ("spawn", "state"):
        hv, cv = h.get(key, "-"), c.get(key, "-")
        if len(hv) != len(cv):
            bad.append(("extras-" + key,
                        "%s extras length %d vs %d (%s vs %s)"
                        % (key, len(hv), len(cv), hv, cv)))
    return bad


def diff_worlds(host, client, tol, stats):
    """The whole-world diff. Returns a list of (key, message) pairs -- see diff_entity."""
    problems = []
    hents, cents = host["ents"], client["ents"]
    only_host = sorted(set(hents) - set(cents), key=int)
    only_client = sorted(set(cents) - set(hents), key=int)
    for i in only_host:
        # AN ABSENCE THE JOINER EXPLAINS IS NOT A MISMATCH (card d108c459). The joiner's removal
        # ledger (`gone`, dump v4) is its own statement that it HELD this id and let it go, which
        # is a different fact from never having received it -- and the difference is the whole
        # question this leg asks. The case that produces it constantly:
        # NetPuppets.ReleaseDyingPuppet hands a puppet its own death animation and drops it from
        # every map, while the host keeps the entity in NetIdRegistry for the whole 2.5-5 s of
        # that animation. Measured: every host-only id that survived a re-settle was of exactly
        # this shape (BattleSkull, host dying=1), and every id NOT of this shape converged.
        #
        # THE EVIDENCE MUST COME FROM THE JOINER, and that is measured rather than assumed. A
        # second arm -- excusing an id whose HOST copy reports dying/dead -- was implemented and
        # DECLINED: over 151 joins it was the sole explanation 0 times, while the ledger was sole
        # 1 time, so it added nothing and cost the rule its teeth (with both arms in place,
        # deleting the `gone` line did not even bring the class back, because the host's own flag
        # still excused everything). One arm, from the peer that is missing the entity.
        #
        # NO LEDGER ENTRY -> IT FAILS, and that is where the teeth are: a joiner that simply
        # never got an entity has nothing to say about it.
        if i in client["gone"]:
            stats["released"] += 1
            continue
        problems.append(("only-host-%s" % i,
                         "id %s (%s) is on the HOST and not on the joiner, and the joiner does "
                         "not report having released it" % (i, hents[i].get("type"))))
    for i in only_client:
        problems.append(("only-join-%s" % i,
                         "id %s (%s) is on the JOINER and not on the host"
                         % (i, cents[i].get("type"))))
    for i in sorted(set(hents) & set(cents), key=int):
        for key, msg in diff_entity(hents[i], cents[i], tol, stats):
            problems.append(("ent-%s-%s" % (i, key),
                             "id %s (%s): %s" % (i, hents[i].get("type"), msg)))

    problems.extend(diff_scene(host["scene"], client["scene"], tol))
    problems.extend(diff_hud(host["hud"], client["hud"], tol))
    return problems


def diff_scene(hline, cline, tol):
    """The scenery/music line, compared FIELD BY FIELD rather than as one string.

    Almost all of it is discrete replicated state -- the scroll speed, the base variant, the belt,
    the song, the cosmetic swarm -- and those are compared exactly, which is what makes a missing
    catch-up unmistakable (the mutation row below reads `speed=-0.012` against `speed=-0.6`).

    THE DOODAD IS NOT. It carries a POSITION, which drifts continuously on both peers and is
    sampled at two slightly different world instants like every other position in this diff -- so
    an exact string compare on the whole line was reporting `-239.6` against `-239.2` as a
    scenery desync (measured once in ~300 joins, and it does not converge, because the doodad
    keeps moving). Its NAME still has to match exactly; only the coordinates get the position
    tolerance the rest of the world already gets.
    """
    if hline == cline:
        return []
    if hline is None or cline is None:
        return [("scene", "one end reported no scenery state at all (host=%r join=%r)"
                 % (hline, cline))]
    hk, ck = parse_kv(hline), parse_kv(cline)
    bad = []
    for key in sorted(set(hk) | set(ck)):
        hv, cv = hk.get(key, "-"), ck.get(key, "-")
        if hv == cv:
            continue
        if key == "doodad" and "@" in hv and "@" in cv:
            hname, hpos = hv.rsplit("@", 1)
            cname, cpos = cv.rsplit("@", 1)
            if hname == cname:
                try:
                    hx, hy = [float(v) for v in hpos.split(",")]
                    cx, cy = [float(v) for v in cpos.split(",")]
                except ValueError:
                    pass
                else:
                    if ((hx - cx) ** 2 + (hy - cy) ** 2) ** 0.5 <= tol["pos"]:
                        continue
        bad.append(("scene-" + key,
                    "scenery/music state differs on %s:\n    host  %s\n    join  %s"
                    % (key, hline, cline)))
    return bad


def parse_hud(line):
    """`lives=N | s0 k=v ... | s1 ...` -> (lives, {slot: {k: v}})."""
    if not line:
        return None, {}
    parts = [p.strip() for p in line.split("|")]
    lives = parse_kv(parts[0]).get("lives")
    slots = {}
    for part in parts[1:]:
        toks = part.split(None, 1)
        if len(toks) == 2 and toks[0].startswith("s"):
            slots[toks[0]] = parse_kv(toks[1])
    return lives, slots


# A seat reads LOCAL on one peer and REMOTE on the other -- slots are identity-mapped and
# host-allocated (card 4d904410), so "host slot 0 = Keyboard, joiner slot 0 = Remote" is the
# pairing being CORRECT, not a disagreement. What must hold is that the two are mirror images:
# every seat is filled on both ends, and exactly the local/remote sense is swapped.
REMOTE_SEATS = ("Remote", "RemoteFriend")


def seat_mirrors(a, b):
    if a == "-" or b == "-":
        return a == b
    return (a in REMOTE_SEATS) != (b in REMOTE_SEATS)


def diff_hud(hline, cline, tol):
    hl, hs = parse_hud(hline)
    cl, cs = parse_hud(cline)
    if hl is None or cl is None:
        return [("hud", "one end reported no HUD state at all (host=%r join=%r)"
                 % (hline, cline))]
    bad = []
    if hl != cl:
        bad.append(("lives", "lives %s vs %s" % (hl, cl)))
    for slot in sorted(set(hs) | set(cs)):
        h, c = hs.get(slot, {}), cs.get(slot, {})
        if not seat_mirrors(h.get("seat", "-"), c.get("seat", "-")):
            bad.append(("seat-%s" % slot, "%s seat %s vs %s -- not a mirror image"
                        % (slot, h.get("seat"), c.get("seat"))))
        # DISCRETE and owner-authoritative: the powerup ladder and the Option population are
        # replicated as VALUES, so they must agree exactly. This is the pair card c5228350's
        # join-in-progress catch-up exists for -- a joiner replays no claims, so a broken
        # catch-up leaves it permanently short and nothing else here would say so.
        # Both ride the ~10 Hz MsgHudState, so a pickup claimed in the last tenth of a second is
        # legitimately not on the other end yet -- measured 4 times in 188 joins. They stay
        # compared EXACTLY (a permanently short joiner is card c5228350's defect and must fail);
        # what absorbs the staleness is the re-settle confirm, not a tolerance, because there is
        # no sane tolerance on a ladder position or a count of Option ships.
        for key in ("lv", "opt"):
            if h.get(key) != c.get(key):
                bad.append(("%s-%s" % (key, slot),
                            "%s %s %s vs %s" % (slot, key, h.get(key), c.get(key))))
        # CONTINUOUS, and COMPARED AFTER SUBTRACTING `uns` -- which is the difference between
        # comparing what the two peers DISPLAY and comparing what they actually disagree about
        # (card 94001db7). A client's displayed score is the host's authoritative figure PLUS its
        # own unsettled provisional credits, by design (card b0ab09ec gives the player instant
        # credit on their own kills); the host's display carries no such term. So a raw `pts`
        # compare is a category error, and it fired hardest on DEFERRED-DEATH types: the joiner
        # kills a `BattleSkull` on its lagged puppet and books the award, while the host's copy is
        # still `dying=1` two seconds into its death animation and has neither credited it nor
        # broadcast the `EvDeath` that would settle it. Measured: 2000-5550 points, permanently,
        # because on a dense wave the 3 s AwardSettleWindowMs is never empty.
        #
        # Subtracting `uns` reduces that to the ordinary staleness case (host credited, our
        # EvDeath not applied yet), which converges within a sync and is what the tolerance is
        # for. The failure the ledger design exists to stop is a one-way DRIFT, and a tolerance on
        # the AUTHORITATIVE figures still catches it -- more sharply than before, since the
        # provisional term no longer masks it. The policy itself is eaNetScore.test's subject.
        #
        # `combo` and `pu=<type>@<progress>` are DUMP-ONLY, for reading by hand: both ride the
        # ~10 Hz MsgHudState and are re-derived per peer, so any threshold tight enough to catch
        # a real disagreement would flag ordinary staleness. eaNetCombo.test owns that policy.
        #
        # THE LIMIT IS KEYED TO THE AWARDS IN FLIGHT, NOT A BIGGER CONSTANT (card d108c459).
        # Subtracting `uns` leaves one legitimate asymmetry: an award the joiner is still holding
        # provisionally may ALREADY have been settled authoritatively by the host, whose EvDeath
        # is what settles it -- so between the host crediting it and the 1 Hz EvScoreSync landing,
        # that credit is counted on neither authoritative figure and the host reads high by up to
        # the joiner's whole outstanding ledger. Measured: gap 2400 against `uns` 2250 on a dense
        # Level-3 wave, and it does NOT converge, because a joiner that keeps killing keeps the
        # ledger occupied (the mirror of card 94001db7's steady-state churn).
        #
        # So the allowance in the HOST-AHEAD direction is `uns` plus the ordinary staleness
        # constant. That is unbounded-safe in the way a constant cannot be: `comboModify` has no
        # ceiling, and a boss award group at a routine 40x combo dwarfs any number that could be
        # written here -- but it also sits in `uns` while it is in flight, so the allowance grows
        # with it and only with it.
        #
        # THE OTHER DIRECTION KEEPS THE BARE CONSTANT, and that is where the teeth are: a joiner
        # reading HIGHER than the host authoritatively is points invented locally and never
        # settled -- the one-way drift the ledger design exists to stop, and the shape a ratchet
        # produces (eaNetScore.test is that policy's deterministic pin).
        # Every name the report needs is bound BEFORE the try, so a token this tool cannot parse
        # cannot leave one unbound on the failure path.
        cuns = cauth = gap = 0.0
        try:
            cuns = float(c.get("uns", 0))
            hauth = float(h.get("pts", 0)) - float(h.get("uns", 0))
            cauth = float(c.get("pts", 0)) - cuns
            gap = hauth - cauth
        except ValueError:
            cuns = cauth = gap = 0.0
        limit = tol["score"] + (cuns if gap > 0 else 0.0)
        gap = abs(gap)
        if gap > limit:
            bad.append(("score-%s" % slot,
                        "%s score %s vs %s (%.0f apart authoritative, limit %.0f = %.0f + the "
                        "joiner's %.0f in flight; unsettled %s / %s)"
                        % (slot, h.get("pts"), c.get("pts"), gap, limit, tol["score"], cuns,
                           h.get("uns"), c.get("uns"))))
    return bad


def max_pos_gap(host, client):
    worst = 0.0
    for i in set(host["ents"]) & set(client["ents"]):
        try:
            hx, hy = [float(v) for v in host["ents"][i]["pos"].split(",")]
            cx, cy = [float(v) for v in client["ents"][i]["pos"].split(",")]
        except (KeyError, ValueError):
            continue
        worst = max(worst, ((hx - cx) ** 2 + (hy - cy) ** 2) ** 0.5)
    return worst


# ---------------------------------------------------------------------------- the run


def note_unsettled(stats, client_dump):
    """Fold this dump's joiner-side `uns` into the run's peak.

    CALLED AT EVERY DUMP A JOIN TAKES, not just the settle one (card d108c459). `uns` is
    non-zero only while a joiner's own kill is inside the 3 s AwardSettleWindowMs, so sampling
    one instant per join made the run-level control a coin flip -- measured RED on 1 of 5
    healthy soaks, and 0 vs 1000 on two runs of the SAME seed. Every extra sample is free: the
    dumps are taken anyway.

    Parsed tolerantly and taken RAW rather than through abs(), for the reasons at the assertion.
    """
    _, slots = parse_hud(client_dump["hud"])
    for kv in slots.values():
        try:
            stats["unsettled"] = max(stats["unsettled"], float(kv.get("uns", 0)))
        except ValueError:
            pass


def approach(host, client, args):
    """The last few frames before a dump, stepped ONE AT A TIME.

    The interleave chunk is the dominant term in the measured position gap -- one peer has
    always stepped last -- so the tolerance the diff needs is set by whatever the final step
    was, not by the chunk the settle ran at. Stepping the run-in at 1 frame shrinks that term
    without slowing the soak: it costs `--approach` extra round-trips PER JOIN, against the
    thousands the settle already spends.
    """
    for _ in range(args.approach):
        host.step(1)
        client.step(1)


def sample(host, client, args, stats):
    """One (host, joiner) dump pair, taken after the fine-grained approach above."""
    approach(host, client, args)
    hd = host.dump()
    cd = client.dump()
    note_unsettled(stats, cd)
    return hd, cd


def run_join(host, room, port, args, index, stats):
    """Attach ONE fresh joiner process, settle it, dump both ends, diff. Returns problems."""
    join_flags = "?menu&noattract&netallowdebug&net=jipjoin&room=%s" % room
    client = Peer("join%d" % index, join_flags, port, args.verbose)
    try:
        # Interleave until the joiner has a world, or the settle budget runs out. The budget
        # is generous on purpose: the attach is EvLaunch -> a real level warm -> Initialize ->
        # EvReady -> ReplayLive, and a level warm is dozens of frames of texture decode.
        attached = False
        settled = 0
        while settled < args.settle:
            host.step(args.chunk)
            client.step(args.chunk)
            settled += args.chunk
            if not attached and settled % (args.chunk * 8) == 0:
                d = client.dump()
                note_unsettled(stats, d)
                attached = d["meta"] is not None and d["meta"]["ids"] > 0
                if attached:
                    # Once the world is up, keep stepping for the catch-up burst to be applied
                    # and a few snapshot rounds to correct it, then stop.
                    # Rounds UP -- the settle length is a MEASURED number (see the flag's
                    # help), and truncating would quietly settle for less than the figure
                    # net CLAUDE.md quotes whenever --chunk does not divide it.
                    #
                    # DUMPED PERIODICALLY ON THE WAY, purely to sample `uns` (card d108c459).
                    # The ledger is non-empty only for the 3 s AwardSettleWindowMs after a
                    # joiner's own kill, so reading it at ONE instant per join makes the
                    # run-level control a lottery -- measured 0 across 36 joins on a healthy
                    # soak. Every dump taken here is another chance to catch a real ledger, and
                    # the dumps are the cheap part of a join.
                    stepped = 0
                    while stepped < args.settle_after:
                        host.step(args.chunk)
                        client.step(args.chunk)
                        stepped += args.chunk
                        if stepped % (args.chunk * 8) == 0:
                            note_unsettled(stats, client.dump())
                    break
        hd, cd = sample(host, client, args, stats)
        problems = judge(hd, cd, host, client, args, index, stats)
    finally:
        client.quit()
    return problems


def judge(hd, cd, host, client, args, index, stats):
    """Everything from the settle dump onward -- with the joiner still alive, so the re-settle
    confirm below can take a SECOND look before anything is called a failure."""
    for name, d in (("host", hd), ("join", cd)):
        if d["meta"] is None:
            return ["%s produced no dump at all" % name]
        if d["meta"]["version"] != FORMAT_VERSION:
            return ["%s dump is format v%d, this tool speaks v%d"
                    % (name, d["meta"]["version"], FORMAT_VERSION)]

    # THE HOST'S LEVEL ENDED. `scene none` means GameScene.NetActiveScene is null, which on an
    # invulnerable AI host means it finished (or lost) the level -- not a defect, and not
    # something to report 15 more times as the loop grinds through the remaining cadence. It is
    # unambiguous: a host mid-level always reports a scene line, session or no session.
    if hd["scene"] == "none":
        return LEVEL_OVER

    problems = []
    # THE VACUITY CONTROLS. A run whose joiner never attached, or whose host world is empty,
    # produces zero mismatches and would otherwise read as a pass -- which is the exact shape
    # of "a probe that cannot fail".
    if hd["meta"]["role"] != "host" or not hd["meta"]["peer"]:
        problems.append("the host never paired (role=%s peer=%s) -- nothing was tested"
                        % (hd["meta"]["role"], hd["meta"]["peer"]))
    if cd["meta"]["role"] != "client" or not cd["meta"]["peer"]:
        problems.append("the joiner never paired (role=%s peer=%s) -- nothing was tested"
                        % (cd["meta"]["role"], cd["meta"]["peer"]))
    if problems:
        return problems
    # AN EMPTY HOST WORLD IS A JOIN THAT TESTED NOTHING -- WHICH IS NOT THE SAME AS A FAILURE
    # (card d108c459). The host is playing a real level, and a real level is empty between waves;
    # a joiner that attaches into that gap compares nothing and its clean result is worth nothing.
    # Reporting it RED said "the game is broken" about a sampling accident (measured 7 times in
    # 188 joins), and passing it silently would be the vacuous green this tool exists to refuse.
    # So it is neither: the join is SKIPPED, counted, and the run asserts that enough real joins
    # happened -- see main. (run_level CAN also wait for a populated world before attaching, but
    # --populate-wait defaults to 0 -- measured and declined, see its help -- so this skip is the
    # primary mechanism, not a rare leftover.)
    if hd["meta"]["ids"] < args.min_ids:
        return VACUOUS

    # HOW MUCH MATERIAL THE `owner=` LEG ACTUALLY HAD: shared ids whose host copy declares an
    # emitter, i.e. the ids the exact compare above ran on non-vacuously. REPORTED, NOT
    # ASSERTED, and that is a measurement rather than a soft option: only `Boss` and `MarsBoss`
    # ever fire an owned beam, and over a full three-level soak (~39 joins) every `Lazer` that
    # reached a settle dump was a GameScene warm-up prime -- legitimately ownerless on both
    # ends. A run-level "at least one owner was compared" assertion would therefore be red on a
    # sampling coincidence, not on a defect. THE LEG'S POSITIVE CONTROL LIVES WHERE ITS MATERIAL
    # IS DETERMINISTIC: `eaNetIdReuse` section 7, pinned by
    # tools/headless/probes/net_id_reuse.txt, builds an owned beam on both a host world and a
    # client world and asserts the dump names the emitter (card 9a7ee4c0).
    # EITHER end declaring an emitter is material -- the compare ran on that id whichever side
    # named one, and counting only the host's would under-report exactly the asymmetry the leg
    # exists to catch.
    owners = sum(1 for i in set(hd["ents"]) & set(cd["ents"])
                 if hd["ents"][i].get("owner", "-") != "-"
                 or cd["ents"][i].get("owner", "-") != "-")
    stats["owners"] += owners

    # HOW MUCH MATERIAL THE `uns` SUBTRACTION HAD, per join, for the same reason `owners` is
    # reported: without it a run where every joiner happened to have an empty ledger would
    # exercise the corrected compare vacuously and read exactly like a run where it worked.
    # Unlike `owners` it IS asserted, but only over a soak long enough for a zero to be
    # implausible -- see main.
    #
    # Parsed like every other HUD field, i.e. tolerantly: a token this tool cannot read is a
    # dump-format problem for FORMAT_VERSION to catch, not a reason to kill the soak. The value
    # is taken RAW rather than through abs() -- the ledger's total should never go negative, so
    # letting one inflate the run's peak would hide exactly the defect worth seeing.
    # `note_unsettled` has already folded this join's dumps into the run peak -- every one of
    # them, not just this last dump (see its header). What is printed below is this JOIN's own
    # figure, which is still the settle dump's.
    _, cslots = parse_hud(cd["hud"])
    unsettled = 0.0
    for kv in cslots.values():
        try:
            unsettled = max(unsettled, float(kv.get("uns", 0)))
        except ValueError:
            pass
    stats["joins"] += 1

    tol = {"pos": args.pos_tol, "pos_path": args.pos_tol_path, "rot": args.rot_tol,
           "scale_abs": 0.0005, "scale_rel": args.scale_tol, "frame": args.frame_tol,
           "score": args.score_tol, "hp": args.hp_tol}
    problems = diff_worlds(hd, cd, tol, stats)

    # THE RE-SETTLE CONFIRM (card d108c459). A disagreement that is GONE one settle later was
    # staleness, not a defect: the two ends are dumped at slightly different world instants, so a
    # measurement taken once cannot tell "the joiner is wrong" from "the joiner has not been told
    # yet". Measured on 110 captured joins: every score gap over the limit (1900, 1550) read
    # EXACTLY 0 after 2 s, and every host-only id that was not a deferred-death release had
    # resolved. What does NOT converge -- a ratchet drifting one way, a provisional puppet, a
    # permanently short powerup ladder -- is reported exactly as before.
    #
    # KEYED, NOT COMPARED BY TEXT: a position or a score never repeats its exact value, so the
    # question asked of the second sample is "is this same comparison still failing", not "is the
    # message identical".
    #
    # NOTHING IT DROPS IS SILENT. Every cleared mismatch is printed under the join and counted
    # into the run's `converged=` total, because a class that always clears on the second look is
    # a finding about the game (or about this tool) and must not vanish into a green run.
    if problems and args.resettle > 0:
        for _ in range(-(-args.resettle // args.chunk)):
            host.step(args.chunk)
            client.step(args.chunk)
        hd2, cd2 = sample(host, client, args, stats)
        if hd2["meta"] is not None and cd2["meta"] is not None and hd2["scene"] != "none":
            # A THROWAWAY stats dict for the second look: its `transit`/`released` counts are the
            # same entities seen twice, and folding them into the run total would double-count
            # the very numbers a reader uses to judge how much was skipped.
            again = new_stats()
            still = set(k for k, _ in diff_worlds(hd2, cd2, tol, again))
            confirmed = [(k, m) for k, m in problems if k in still]
            cleared = [m for k, m in problems if k not in still]
            for msg in cleared:
                print("      converged after %d frames (not a failure): %s"
                      % (args.resettle, msg))
            stats["dropped"] += len(cleared)
            problems = confirmed

    print("    join %d: host ids=%d joiner ids=%d maxpos=%.2fpx owners=%d uns=%.0f mismatches=%d"
          % (index, hd["meta"]["ids"], cd["meta"]["ids"], max_pos_gap(hd, cd), owners,
             unsettled, len(problems)))
    return [m for _, m in problems]


def run_level(level, args, stats):
    room = ("jip%s%d" % (level, args.seed)).lower()
    port = args.net_port
    host_flags = ("?level=%s&invuln&aiplayer&noattract&netjip&seed=%d&net=jiphost&room=%s%s"
                  % (level, args.seed, room, args.host_extra))
    print("  %s  cadence=%ds cap=%ds seed=%d room=%s"
          % (level, args.cadence, args.cap, args.seed, room))
    host = Peer("host-%s" % level, host_flags, port, args.verbose)
    failures = []
    # Joins REALLY COMPARED, read off the run counter rather than off the loop index: `index`
    # counts joins attempted, which includes one that never paired. The floor below is about how
    # much this level actually tested.
    compared_before = stats["joins"]
    try:
        cadence_frames = int(args.cadence * args.fps)
        cap_frames = int(args.cap * args.fps)
        # Let the level get past its intro before the first join -- attaching to a level that
        # has not spawned anything yet is the empty-world vacuity case, not a test.
        host.step(cadence_frames)
        elapsed = cadence_frames
        index = 0
        mark = host.frame_mark()
        # --no-pair points the JOINER at a port nothing is listening on, so it boots, warms
        # nothing and never attaches. It is the tool's own vacuity control: a run where nothing
        # was tested must come out RED, not silently 0-mismatches-green. See --selftest.
        client_port = PORT_NOWHERE if args.no_pair else port
        while elapsed < cap_frames:
            # WAIT FOR A WORLD BEFORE SPENDING A JOINER ON IT (card d108c459). The host is
            # playing a real level and a real level is empty between waves, so an attach timed
            # into one of those gaps compares nothing -- the "0 replicated entities" result that
            # used to be reported as a failure. Stepping on until something spawns turns that
            # sampling accident into a real test instead of into a verdict either way. Bounded,
            # and the bound is charged to --cap like every other frame, so a level that is
            # genuinely over still ends the loop below rather than spinning here.
            waited = 0
            while waited < args.populate_wait:
                d = host.dump()
                if d["meta"] is None or d["meta"]["ids"] >= args.min_ids or d["scene"] == "none":
                    break
                host.step(cadence_frames)
                waited += cadence_frames
            if waited:
                elapsed += waited
                print("    (waited %gs for the host world to populate)" % (waited / args.fps))
            index += 1
            problems = run_join(host, room, client_port, args, index, stats)
            if problems is LEVEL_OVER:
                index -= 1          # nothing was tested, so it was not a join
                print("    %s ended after %d join(s) -- the host finished the level"
                      % (level, index))
                break
            if problems is VACUOUS:
                index -= 1          # ...and neither was this one
                stats["skipped"] += 1
                print("    join skipped: the host world was empty at the dump -- nothing to "
                      "compare, so this join is not evidence either way")
                problems = []
            if problems:
                failures.append((level, index, problems))
                if args.stop_on_first:
                    break
            # THE SETTLE FRAMES COUNT. A join steps the host for the joiner's whole level warm
            # plus the settle, which is tens of seconds -- charging only the cadence made --cap
            # under-count by ~3x, so a 600s cap ran a 600s level out and then reported a dozen
            # vacuous joins into an empty world.
            elapsed += cadence_frames + host.frames_since(mark)
            mark = host.frame_mark()
            host.step(cadence_frames)
    finally:
        host.quit()
    # THE VACUITY GUARD, at the level scale. A --cap at or below --cadence runs the loop zero
    # times, and without this the level reports no failures and the tool exits 0 -- a green run
    # that tested nothing, which is the exact shape a suite must never have.
    if index == 0:
        failures.append((level, 0, ["no join was attempted at all -- --cap (%gs) must exceed "
                                    "--cadence (%gs)" % (args.cap, args.cadence)]))
    elif stats["joins"] - compared_before < args.min_real_joins:
        # THE SKIP'S OWN GUARD. Skipping a vacuous join is only honest while the level still
        # tested something; a level whose joins were ALL skipped is the vacuous green again,
        # wearing a different hat.
        failures.append((level, 0, ["only %d real join(s) on this level (min %d) -- the rest "
                                    "were skipped as vacuous or never paired, so the level is "
                                    "untested" % (stats["joins"] - compared_before,
                                                  args.min_real_joins)]))
    return failures


def run_selftest(args):
    """THE VACUITY CONTROL, committed rather than performed once by hand.

    Every assertion this tool makes is of the form "the two worlds agree", and the failure mode
    of that shape is a run where one world never existed: zero entities on both ends compare
    perfectly. So the control is a run whose joiner CANNOT attach -- it must come out RED, and
    for the right reason (a pairing that never happened), not merely non-zero.

    Two arms, because the tool has two independent ways to report nothing:
      1. --no-pair       -- a joiner dialling a dead port. Exercises run_join's pairing guards.
      2. --cap <= --cadence -- a level whose join loop never runs. Exercises run_level's guard.
    """
    print("net_jip_sync --selftest: a run that tests nothing must come out RED")
    ok = True

    args.no_pair = True
    args.level = ["Level2"]
    args.cap = args.cadence * 2
    args.settle = 600          # it will never attach; do not spend the full budget waiting
    failures = run_level("Level2", args, new_stats())
    paired = any("never paired" in m for _, _, msgs in failures for m in msgs)
    print("  1. joiner on a dead port -> %d failure(s), names the pairing: %s"
          % (len(failures), paired))
    ok = ok and failures and paired

    args.no_pair = False
    args.cap = args.cadence     # the loop cannot run even once
    failures = run_level("Level2", args, new_stats())
    nojoin = any("no join was attempted" in m for _, _, msgs in failures for m in msgs)
    print("  2. --cap == --cadence -> %d failure(s), names the empty loop: %s"
          % (len(failures), nojoin))
    ok = ok and failures and nojoin

    print("selftest %s" % ("PASSED -- the tool cannot pass on a run that tested nothing"
                           if ok else "FAILED -- a vacuous run came out GREEN"))
    return 0 if ok else 1


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--level", action="append", default=[],
                   help="level to soak (repeatable; default: the three story levels)")
    p.add_argument("--cadence", type=float, default=20.0,
                   help="sim seconds of host play between joins (default 20)")
    p.add_argument("--cap", type=float, default=600.0,
                   help="sim seconds of host play per level (default 600)")
    p.add_argument("--seed", type=int, default=7, help="?seed= for the host's world")
    p.add_argument("--fps", type=float, default=60.0, help="frames per sim second")
    p.add_argument("--chunk", type=int, default=3,
                   help="frames per interleave step. It bounds the SKEW between the two worlds "
                        "at the dump -- one peer has always stepped last -- so it is the "
                        "dominant term in the measured position gap: at 30 frames (500ms) a "
                        "0.16px/ms UFO reads 79px apart with nothing wrong. Must also stay well "
                        "under the 480-frame peer timeout (3s + 5s grace)")
    p.add_argument("--score-tol", type=float, default=1000.0,
                   help="points, per slot -- the BASE of the limit; the joiner's outstanding "
                        "provisional ledger is added to it in the host-ahead direction (see "
                        "diff_hud). Score is reconciled at 1 Hz against that ledger, so a "
                        "fraction of a second of staleness is normal, and `uns` is sampled at "
                        "the dump while the gap reflects awards settled a moment earlier -- that "
                        "slop is what this covers. MEASURED over 223 joins: with `uns` at 0 the "
                        "worst surviving gap is 625, and on a join whose award group is in "
                        "flight the excess over the keyed allowance is 50 and 350 (gap 6325 "
                        "against `uns` 6275, and gap 3250 against 2900) -- so 1000 is the worst "
                        "measured slop plus ~1.6x. It is NOT sized to cover an award: "
                        "comboModify has no ceiling and that term is keyed, not tolerated")
    p.add_argument("--settle", type=int, default=3600,
                   help="frame budget for the joiner to warm its level and attach")
    p.add_argument("--settle-after", type=int, default=300,
                   help="frames stepped after the joiner's world appears, before the diff. "
                        "MEASURED, not picked: 300 (5s) is past the joiner's own 1.3s "
                        "GameScene.UpdateStartup phase PLUS the 3s RecentRemovalWindowMs the "
                        "snapshot self-heal waits out, which is where the id sets first agree "
                        "(Level2 seed 7: 4/4 joins match at 300, 0/4 at 120). Going much "
                        "higher stops testing the ATTACH -- at 600 the population has turned "
                        "over and deleting BOTH ReplayLive calls changes nothing. Lower it to "
                        "120 to see the blind window itself")
    p.add_argument("--approach", type=int, default=6,
                   help="frames stepped ONE AT A TIME immediately before each dump. The "
                        "interleave chunk is the dominant term in the position gap (one peer has "
                        "always stepped last), so this shrinks the artifact instead of widening "
                        "the tolerance for it. 0 = dump straight off the --chunk interleave")
    p.add_argument("--resettle", type=int, default=300,
                   help="frames stepped before a SECOND look at a join that reported mismatches. "
                        "A disagreement that has cleared by then was staleness, not a defect -- "
                        "the two ends are dumped at slightly different world instants. 300 (5 s) "
                        "is DERIVED FROM THE SLOWEST MECHANISM IT MUST OUTLAST, not picked: "
                        "NetScoreLedger.AwardSettleWindowMs is 3 s, so a deferred-death award in "
                        "flight cannot settle inside a shorter window -- at 2 s a score residual "
                        "and an Option count were still outstanding on 2 of 8 seeds. Plus one "
                        "1 Hz score sync and several snapshot turns on top. 0 disables the "
                        "confirm, which is the mutation control for it")
    p.add_argument("--populate-wait", type=int, default=0,
                   help="frame budget for waiting out an EMPTY host world before attaching a "
                        "joiner. DEFAULT OFF, and that is measured, not lazy: the world seen at "
                        "attach time is not the world seen at the dump ten seconds later, so "
                        "waiting bought almost nothing and cost 24 of 39 joins per soak (the "
                        "wait is charged to --cap). The vacuity it was meant to stop is caught "
                        "at the dump instead, where it is real -- see run_join's VACUOUS skip")
    p.add_argument("--min-real-joins", type=int, default=1,
                   help="per LEVEL: how many joins must have been real (populated, compared) "
                        "rather than skipped as vacuous, or the level is reported as untested")
    p.add_argument("--pos-tol", type=float, default=20.0,
                   help="px. MEASURED, not chosen to make a run pass: over 223 joins across 6 "
                        "seeds the worst non-path gap is 13.5 (a Spider), against 17.0 for the "
                        "same sweep before --approach stepped the run-in one frame at a time. "
                        "20 is that measured max plus ~1.5x margin")
    p.add_argument("--pos-tol-path", type=float, default=60.0,
                   help="px, for a NetPathOffset type (its periodic offset runs locally)")
    p.add_argument("--rot-tol", type=float, default=0.3,
                   help="radians (wrapped). Same measurement as --pos-tol and the same class: a "
                        "Ball spins fast enough that one snapshot turn of staleness is a visible "
                        "angle. Worst over the 6-seed sweep is 0.204 rad, every one of the top "
                        "ten a Ball")
    p.add_argument("--scale-tol", type=float, default=0.2,
                   help="relative. Loose because several types PULSATE their scale, which is a "
                        "continuously-varying replicated value, not a construction constant")
    p.add_argument("--frame-tol", type=float, default=1.5, help="animation frames")
    p.add_argument("--hp-tol", type=int, default=0,
                   help="hit points, comparing the host's last SENT hp against the joiner's last "
                        "RECEIVED one -- see diff_entity. ZERO, i.e. exact, and that is measured: "
                        "over 227 joins the two wire values differed 0 times. The live-hp compare "
                        "this replaced needed ~40 to hold a Boss (211 vs 179) and still asserted "
                        "nothing useful. A packet genuinely in flight would show up here and is "
                        "transient, which the re-settle confirm covers")
    p.add_argument("--min-ids", type=int, default=1,
                   help="the host world must hold at least this many replicated entities, or "
                        "the join is reported as vacuous rather than passing")
    p.add_argument("--net-port", type=int, default=0,
                   help="override the port derived from the room name")
    p.add_argument("--stop-on-first", action="store_true",
                   help="stop a level at its first failing join")
    p.add_argument("--host-extra", default="",
                   help="extra query appended to the HOST's flags, e.g. \"&wallsonly\". The "
                        "joiner mirrors the host's LEVEL off the wire, not its flags, so this is "
                        "for reaching a section of a level (the wall loop, a boss fast-boot)")
    p.add_argument("--no-pair", action="store_true",
                   help="point the joiner at a dead port so it never attaches. The tool must "
                        "then report FAILURE -- see --selftest, which drives this")
    p.add_argument("--selftest", action="store_true",
                   help="the tool's own vacuity control: run one join with --no-pair and "
                        "require it to come out RED. Exit 0 = the control held")
    p.add_argument("--verbose", action="store_true", help="tee both peers' output to stderr")
    args = p.parse_args()

    if not os.path.exists(EAHL):
        sys.stderr.write("err eahl not built: %s\n"
                         "    dotnet build tools/headless -c Debug\n" % EAHL)
        return 2
    levels = args.level or ["Level1", "Level2", "Level3"]

    if args.selftest:
        return run_selftest(args)

    print("net_jip_sync: %d level(s), a fresh joiner every %gs of host play"
          % (len(levels), args.cadence))
    started = time.time()
    failures = []
    stats = new_stats()
    for level in levels:
        failures.extend(run_level(level, args, stats))

    # THE `uns` SUBTRACTION'S POSITIVE CONTROL. The score compare subtracts a client's
    # provisional ledger before comparing, so a build where `uns` silently stopped being
    # reported would read 0 everywhere and pass -- vacuously, and while quietly reverting to the
    # raw-`pts` category error the subtraction exists to fix. A joiner books a provisional credit
    # for every kill it observes locally, so a soak whose peak is 0 is that failure.
    #
    # ONLY OVER A LONG ENOUGH RUN, and the threshold is what keeps it honest rather than merely
    # quiet: `uns` is non-zero only when a joiner scored inside the 3 s AwardSettleWindowMs
    # before its dump, so a SHORT or KILL-FREE soak legitimately sees none -- `--cap 120` is ~2
    # joins, `--host-extra "&wallsonly"` runs a section with nothing to kill, `--stop-on-first`
    # truncates. Asserting there would fail a run that was fine and say the field had stopped
    # being reported, which is worse than not checking. The default three-level soak clears the
    # bar by a wide margin (~30 joins, peak measured 277-3900 over four runs). Under the bar it
    # is REPORTED as skipped rather than passing silently -- the deterministic pin for the
    # field's shape is tools/headless/probes/net_jip_dump.txt, which needs no soak at all.
    if not args.no_pair:
        if stats["joins"] >= UnsettledControlMinJoins:
            if stats["unsettled"] <= 0.0:
                failures.append(("(run)", 0, [
                    "no joiner reported ANY unsettled provisional credit across %d joins -- the "
                    "score compare's `uns` subtraction ran on nothing, so it cannot have been "
                    "tested (card 94001db7)" % stats["joins"]]))
        else:
            print("note: %d join(s) is under the %d needed for the `uns` control -- not asserted"
                  % (stats["joins"], UnsettledControlMinJoins))

        # THE hp LEG'S POSITIVE CONTROL, and it needs no minimum-join caveat: a puppet reports
        # `hpwire` as soon as it has taken one snapshot with hp on it, so any soak that compared
        # a killable entity at all has material. A zero means the field stopped being recorded,
        # which SKIPS the hp comparison rather than failing it -- the same silent-deletion shape
        # the `uns` control exists for (card d108c459).
        if stats["joins"] > 0 and stats["hpwire"] <= 0:
            failures.append(("(run)", 0, [
                "no entity was compared on `hpwire` across %d joins -- the hp leg ran on nothing, "
                "so it cannot have been tested (card d108c459)" % stats["joins"]]))

    # The `owner=` leg's material for the whole run, so a reader can see at a glance
    # whether this run exercised it. See run_join for why it is reported and not
    # asserted, and where that leg's positive control lives instead.
    print("\n%d join(s) failed, %.0fs wall clock, owner= compared on %d entit%s, "
          "peak joiner unsettled %.0f"
          % (len(failures), time.time() - started, stats["owners"],
             "y" if stats["owners"] == 1 else "ies", stats["unsettled"]))
    # WHAT THE RULES OF CARD d108c459 DECLINED TO FAIL ON, in one line, every run. Each of these
    # is a real disagreement that was explained rather than reported, so a reader can see at a
    # glance whether a green run was green because the game agrees or because the tool stopped
    # looking -- and a number that jumps relative to `joins` is worth reading even at exit 0.
    print("%d join(s) compared; converged=%d transit=%d released=%d skipped=%d hpwire=%d"
          % (stats["joins"], stats["dropped"], stats["transit"], stats["released"],
             stats["skipped"], stats["hpwire"]))
    for level, index, problems in failures:
        print("\nFAIL %s join %d:" % (level, index))
        for msg in problems:
            print("  - %s" % msg)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
