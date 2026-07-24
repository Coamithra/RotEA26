#!/usr/bin/env python
"""Audit ComponentBin.Add call sites for config-after-Add (card 02d9ad67).

ComponentBin.Add is INSTANT: the component enters Game.Components inside the Add call and
KNI runs component.Initialize() synchronously right there. Initialize implementations
(AlienDrawableGameComponent / KillableAlien) re-derive state from what Setup() configured
(initialhitpoints, timers, colorize, ...), so every call site must fully configure the
object BEFORE Add. Under the old deferred birthList the flush ran Initialize at end of
tick, which silently forgave `Add(x); x.Setup(...);` orderings -- with instant adds those
would initialize an unconfigured object (e.g. 1-hp enemies).

This script flags any statement after an `.Add((GameComponent)...x)` line -- within the
same method -- that still configures x (Setup/Make*/property assignment). Event
subscriptions (`x.OnDeath += ...`) are fine (Initialize doesn't read them) and are ignored.

Run from repo root:  python tools/audit_add_order.py   (exit 1 if suspects found)
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "web" / "EvilAliensWeb"

ADD_RE = re.compile(r"\b[\w.]+\.Add\(\(GameComponent\)(?:\(object\))?(\w+)\)")
# next type/member declaration = out of the current method for sure
MEMBER_RE = re.compile(r"^\s*(public|private|protected|internal)\s")


def method_end(lines, add_idx):
    """Index one past the end of the method containing add_idx (brace tracking)."""
    depth = 0
    # walk backward to the opening brace of the enclosing method: find the line where
    # cumulative depth from file start drops to the method-body level. Simpler: walk
    # forward from add_idx tracking depth; the method ends when depth goes below the
    # depth at the Add line (we start counting relative).
    for i in range(add_idx, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        if i > add_idx and depth < 0:
            return i
        if i > add_idx and MEMBER_RE.match(lines[i]):
            return i
    return len(lines)


CONFIG_RE_TMPL = r"\b{ident}\.(Setup|Make\w*|Load\w*|Set[A-Z]\w*)\s*\(|\b{ident}\.\w+\s*=[^=]"


def audit_file(path):
    suspects = []
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    for idx, line in enumerate(lines):
        m = ADD_RE.search(line)
        if not m:
            continue
        ident = m.group(1)
        if ident == "this":
            continue  # self-adds (menus' Show()) configure via their own fields, not post-Add
        cfg = re.compile(CONFIG_RE_TMPL.format(ident=re.escape(ident)))
        # a plain reassignment (`explosion = Explosion.NewExplosion(...)`) starts a NEW
        # object's config block -- later config lines belong to it, not the Add we tracked
        reassign = re.compile(r"(^|[^\w.]){ident}\s*=[^=]".format(ident=re.escape(ident)))
        end = method_end(lines, idx)
        for j in range(idx + 1, end):
            l = lines[j]
            if reassign.search(l):
                break
            if "+=" in l or "-=" in l:
                continue  # event wiring is Initialize-independent
            if l.strip().startswith("//"):
                continue
            if cfg.search(l):
                suspects.append((idx + 1, ident, j + 1, l.strip()))
    return suspects


def main():
    total_sites = 0
    bad = 0
    for path in sorted(SRC.rglob("*.cs")):
        if "\\obj\\" in str(path) or "\\bin\\" in str(path):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        total_sites += len(ADD_RE.findall(text))
        suspects = audit_file(path)
        for add_line, ident, cfg_line, code in suspects:
            bad += 1
            rel = path.relative_to(ROOT)
            print(f"SUSPECT {rel}:{add_line} Add({ident}) ... line {cfg_line}: {code}")
    print(f"\n{total_sites} Add sites scanned, {bad} config-after-Add suspects.")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
