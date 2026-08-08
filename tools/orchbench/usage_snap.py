#!/usr/bin/env python3
"""Token-spend meter for orchestration benchmarking (tools/orchbench).

Claude Code writes every API call's usage block into per-session transcript
files (~/.claude/projects/<project-slug>/*.jsonl), subagents included. This
tool sums them (deduplicated by requestId -- streamed responses rewrite the
same record several times), so a snapshot before and after a run yields the
run's exact token spend, like checking an API balance.

Usage:
  python tools/orchbench/usage_snap.py snap -o start.json
  ... run the strategy ...
  python tools/orchbench/usage_snap.py diff start.json
  python tools/orchbench/usage_snap.py record start.json \
      --strategy fable-oracle --rep 1 --tickets T1,T2

`diff` prints the delta; `record` appends one batch row to
tools/orchbench/runs.csv. Per-ticket quality goes in scores.csv (scoring
pass), keyed by the same (strategy, rep).

Scope caveat: only transcripts on THIS machine under --claude-dir are seen.
Run all strategies for a comparison from the same session/container. By
default only project dirs whose name contains --project (default: the repo
directory name) are scanned; pass --all to sum every project.
"""

import argparse
import csv
import datetime as dt
import json
import sys
import time
from pathlib import Path

# List prices, $ per 1M tokens (input, output), matched by model-id prefix.
# Cache read bills at 0.1x the input rate; cache WRITES depend on TTL --
# 1.25x for 5-minute entries, 2x for 1-hour entries. The usage blocks carry
# a per-TTL breakdown (usage.cache_creation.ephemeral_{5m,1h}_input_tokens),
# so each bucket is priced at its own rate rather than assuming a TTL.
# Measured 2026-08-08 (a subagent reading its own transcript vs the root's):
# the root session writes 1h entries exclusively, subagents write 5m
# exclusively -- so a mixed run genuinely needs the per-record split.
PRICES = {
    "claude-fable-5": (10.0, 50.0),
    "claude-mythos-5": (10.0, 50.0),
    "claude-opus-5": (5.0, 25.0),
    "claude-opus-4": (5.0, 25.0),
    "claude-sonnet-5": (3.0, 15.0),
    "claude-sonnet-4": (3.0, 15.0),
    "claude-haiku-4-5": (1.0, 5.0),
}
CACHE_READ_MULT = 0.1
CACHE_WRITE_5M_MULT = 1.25
CACHE_WRITE_1H_MULT = 2.0

# A prefix RE-WRITE is a turn whose cache read lands well below the previous
# turn's cached prefix (read+writes) while its own writes cover the gap: the
# entry expired (a subagent idling >5m past its TTL waiting on a review or a
# SendMessage answer) or the context was rebuilt (compaction). Those tokens
# were paid for twice; the detector prices the waste as (write - read) rate.
# Threshold ignores harness prefix jitter; a real stall re-writes 50k+.
REWRITE_MIN_TOKENS = 4096

# Internal accumulation fields. Cache writes are split by TTL; a record with
# no cache_creation breakdown falls back to the 5m bucket (cheapest claim).
FIELDS = ("input_tokens", "output_tokens", "cache_read_input_tokens",
          "cache_write_5m", "cache_write_1h")

# One row per (strategy x rep) batch run. Per-ticket quality lives in
# scores.csv, filled by the scoring pass -- see README -> Ledgers.
CSV_COLUMNS: tuple[str, ...] = (
    "strategy", "rep", "tickets", "started_at", "wall_s",
    "cost_usd", "out_tok", "cw_5m_tok", "cw_1h_tok",
    "cache_read_tok", "in_tok", "rw_events", "rw_tok", "notes")


def rates_for(model):
    for prefix, (inp, out) in PRICES.items():
        if model.startswith(prefix):
            return inp, out
    return None


def scan(claude_dir, project_filter):
    """Sum usage per model across matching transcript files, deduped by request."""
    root = Path(claude_dir).expanduser()
    totals = {}   # model -> {field: int}
    seen = set()
    files = 0
    for proj in sorted(root.glob("*")) if root.is_dir() else []:
        if not proj.is_dir():
            continue
        if project_filter and project_filter.lower() not in proj.name.lower():
            continue
        for path in proj.glob("**/*.jsonl"):
            files += 1
            try:
                lines = path.read_text(errors="replace").splitlines()
            except OSError:
                continue
            for line in lines:
                try:
                    rec = json.loads(line)
                except (json.JSONDecodeError, ValueError):
                    continue
                msg = rec.get("message") or {}
                usage = msg.get("usage")
                if not isinstance(usage, dict):
                    continue
                key = rec.get("requestId") or msg.get("id")
                if key is not None:
                    if key in seen:
                        continue
                    seen.add(key)
                model = msg.get("model") or "unknown"
                bucket = totals.setdefault(model, dict.fromkeys(FIELDS, 0))
                for f in ("input_tokens", "output_tokens",
                          "cache_read_input_tokens"):
                    v = usage.get(f)
                    if isinstance(v, int):
                        bucket[f] += v
                cc = usage.get("cache_creation")
                if isinstance(cc, dict):
                    bucket["cache_write_5m"] += cc.get("ephemeral_5m_input_tokens", 0) or 0
                    bucket["cache_write_1h"] += cc.get("ephemeral_1h_input_tokens", 0) or 0
                else:
                    v = usage.get("cache_creation_input_tokens")
                    if isinstance(v, int):
                        bucket["cache_write_5m"] += v
    return {"v": 2, "ts": time.time(), "files": files, "models": totals}


def parse_ts(s):
    try:
        return dt.datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()
    except (ValueError, AttributeError):
        return None


def usage_of(rec):
    """(usage dict, dedupe key) or (None, None) for a non-usage record."""
    msg = rec.get("message") or {}
    usage = msg.get("usage")
    if not isinstance(usage, dict):
        return None, None
    return usage, (rec.get("requestId") or msg.get("id"))


def write_buckets(usage):
    cc = usage.get("cache_creation")
    if isinstance(cc, dict):
        return (cc.get("ephemeral_5m_input_tokens", 0) or 0,
                cc.get("ephemeral_1h_input_tokens", 0) or 0)
    v = usage.get("cache_creation_input_tokens")
    return (v if isinstance(v, int) else 0), 0


def scan_rewrites(claude_dir, project_filter, t0, t1):
    """Prefix re-write events in [t0, t1], across matching transcript files.

    Each file is one conversation = one cache lineage, so the walk is
    per-file: track the cached prefix (read+writes) turn over turn; a turn
    that reads far less than the previous prefix while writing the gap back
    re-paid for those tokens. Files are walked in full (the event just
    before t0 needs the prefix history), but only events inside the window
    are reported."""
    root = Path(claude_dir).expanduser()
    events = []
    for proj in sorted(root.glob("*")) if root.is_dir() else []:
        if not proj.is_dir():
            continue
        if project_filter and project_filter.lower() not in proj.name.lower():
            continue
        for path in proj.glob("**/*.jsonl"):
            prev_prefix = 0
            seen = set()
            try:
                lines = path.read_text(errors="replace").splitlines()
            except OSError:
                continue
            for line in lines:
                try:
                    rec = json.loads(line)
                except (json.JSONDecodeError, ValueError):
                    continue
                usage, key = usage_of(rec)
                if usage is None:
                    continue
                if key is not None:
                    if key in seen:
                        continue
                    seen.add(key)
                read = usage.get("cache_read_input_tokens", 0) or 0
                w5, w1 = write_buckets(usage)
                writes = w5 + w1
                if prev_prefix and read + REWRITE_MIN_TOKENS < prev_prefix:
                    rewritten = min(writes, prev_prefix - read)
                    ts = parse_ts(rec.get("timestamp") or "")
                    if rewritten >= REWRITE_MIN_TOKENS and (
                            ts is None or t0 <= ts <= t1):
                        model = (rec.get("message") or {}).get("model") or "unknown"
                        rates = rates_for(model)
                        mult = (CACHE_WRITE_1H_MULT if w1 > w5
                                else CACHE_WRITE_5M_MULT)
                        extra = (rewritten / 1e6 * rates[0]
                                 * (mult - CACHE_READ_MULT)) if rates else 0.0
                        events.append({"file": path.stem, "model": model,
                                       "tokens": rewritten, "extra_usd": extra,
                                       "ts": ts})
                if read or writes:
                    prev_prefix = read + writes
    return events


def print_rewrites(events):
    if not events:
        print("prefix re-writes: none")
        return
    total_tok = sum(e["tokens"] for e in events)
    total_usd = sum(e["extra_usd"] for e in events)
    print(f"prefix re-writes: {len(events)} event(s), {total_tok} tok, "
          f"~${total_usd:.4f} extra vs cache hits")
    by_file = {}
    for e in events:
        by_file.setdefault(e["file"], []).append(e)
    for name, evs in sorted(by_file.items(),
                            key=lambda kv: -sum(e["tokens"] for e in kv[1]))[:8]:
        print(f"  {name}: {len(evs)} event(s), "
              f"{sum(e['tokens'] for e in evs)} tok ({evs[0]['model']})")


def cost_usd(models):
    """Dollar cost of a per-model usage dict at list prices (None-priced models cost 0)."""
    total = 0.0
    for model, u in models.items():
        rates = rates_for(model)
        if rates is None:
            continue
        inp, out = rates
        total += u["input_tokens"] / 1e6 * inp
        total += u["output_tokens"] / 1e6 * out
        total += u["cache_write_5m"] / 1e6 * inp * CACHE_WRITE_5M_MULT
        total += u["cache_write_1h"] / 1e6 * inp * CACHE_WRITE_1H_MULT
        total += u["cache_read_input_tokens"] / 1e6 * inp * CACHE_READ_MULT
    return total


def subtract(now, then):
    models = {}
    for model, u in now["models"].items():
        base = then["models"].get(model, dict.fromkeys(FIELDS, 0))
        d = {f: u[f] - base.get(f, 0) for f in FIELDS}
        if any(d.values()):
            models[model] = d
    return models


def print_delta(models, wall_s):
    if not models:
        print("no usage delta")
        return
    hdr = (f"{'model':30} {'in':>8} {'out':>10} {'cw_5m':>10} {'cw_1h':>10} "
           f"{'cache_r':>12} {'$':>9}")
    print(hdr)
    for model, u in sorted(models.items()):
        c = cost_usd({model: u})
        tag = "" if rates_for(model) else "  (unpriced)"
        print(f"{model:30} {u['input_tokens']:>8} {u['output_tokens']:>10} "
              f"{u['cache_write_5m']:>10} {u['cache_write_1h']:>10} "
              f"{u['cache_read_input_tokens']:>12} {c:>9.4f}{tag}")
    print(f"{'TOTAL':30} {'':8} {'':10} {'':10} {'':10} {'':12} "
          f"{cost_usd(models):>9.4f}   wall {wall_s:.0f}s")


def selftest():
    import tempfile
    failures = []

    def check(cond, msg):
        print(("PASS  " if cond else "FAIL  ") + msg)
        if not cond:
            failures.append(msg)

    def rec(model, read, w5, w1, ts, req):
        return json.dumps({"requestId": req, "timestamp": ts,
                           "message": {"model": model, "usage": {
                               "input_tokens": 1, "output_tokens": 100,
                               "cache_read_input_tokens": read,
                               "cache_creation": {
                                   "ephemeral_5m_input_tokens": w5,
                                   "ephemeral_1h_input_tokens": w1}}}})

    t0 = parse_ts("2026-01-01T12:00:00Z")
    t1 = parse_ts("2026-01-01T13:00:00Z")
    IN = "2026-01-01T12:30:00Z"
    BEFORE = "2026-01-01T11:00:00Z"
    with tempfile.TemporaryDirectory() as td:
        proj = Path(td) / "C--Fake-RotEA26"
        proj.mkdir()
        # normal growth: never an event
        (proj / "grow.jsonl").write_text("\n".join([
            rec("claude-opus-5", 0, 60000, 0, IN, "g1"),
            rec("claude-opus-5", 60000, 500, 0, IN, "g2")]))
        # expiry: 60k 5m prefix read drops to 0, written back; the duplicate
        # requestId line is the streamed rewrite of the same record
        (proj / "expire.jsonl").write_text("\n".join([
            rec("claude-opus-5", 0, 60000, 0, IN, "e1"),
            rec("claude-opus-5", 0, 60000, 0, IN, "e2"),
            rec("claude-opus-5", 0, 60000, 0, IN, "e2")]))
        # same shape but before the window: excluded
        (proj / "early.jsonl").write_text("\n".join([
            rec("claude-opus-5", 0, 60000, 0, BEFORE, "y1"),
            rec("claude-opus-5", 0, 60000, 0, BEFORE, "y2")]))
        # sub-threshold dip: prefix jitter, not a stall
        (proj / "jitter.jsonl").write_text("\n".join([
            rec("claude-opus-5", 0, 60000, 0, IN, "j1"),
            rec("claude-opus-5", 58000, 2000, 0, IN, "j2")]))
        # 1h rebuild (root compaction shape): priced at the 2x mult
        (proj / "root.jsonl").write_text("\n".join([
            rec("claude-fable-5", 0, 0, 100000, IN, "r1"),
            rec("claude-fable-5", 10000, 0, 90000, IN, "r2")]))
        # a different project: filtered out
        other = Path(td) / "C--Other-Proj"
        other.mkdir()
        (other / "x.jsonl").write_text("\n".join([
            rec("claude-opus-5", 0, 60000, 0, IN, "o1"),
            rec("claude-opus-5", 0, 60000, 0, IN, "o2")]))

        evs = scan_rewrites(td, "rotea", t0, t1)
        by_file = {e["file"]: e for e in evs}
        check(len(evs) == 2, f"exactly the two real events fire (got {len(evs)})")
        check("grow" not in by_file, "normal growth is not an event")
        check("early" not in by_file, "event before the window excluded")
        check("jitter" not in by_file, "sub-threshold dip ignored")
        check("x" not in by_file, "other project filtered out")
        e = by_file.get("expire")
        check(e is not None and e["tokens"] == 60000,
              "expiry rewrites the full 60k prefix once (duplicate deduped)")
        check(e is not None and abs(e["extra_usd"] - 60000 / 1e6 * 5.0 * 1.15) < 1e-9,
              "5m rewrite priced at (1.25-0.1)x opus input")
        r = by_file.get("root")
        check(r is not None and r["tokens"] == 90000
              and abs(r["extra_usd"] - 90000 / 1e6 * 10.0 * 1.9) < 1e-9,
              "1h rebuild priced at (2.0-0.1)x fable input")
        # negative control: widen the window and the early event appears
        evs_all = scan_rewrites(td, "rotea", 0, t1)
        check(len(evs_all) == 3, "window is what excluded the early event")

    print(f"\n{len(failures)} FAILURE(S)" if failures else "\nALL PASS")
    return 1 if failures else 0


def main():
    default_project = Path(__file__).resolve().parents[2].name  # repo dir name
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--claude-dir", default="~/.claude/projects")
    ap.add_argument("--project", default=default_project,
                    help="substring filter on project dir names (default: repo name)")
    ap.add_argument("--all", action="store_true", help="scan every project dir")
    ap.add_argument("--selftest", action="store_true",
                    help="test the rewrite detector against synthetic transcripts")
    sub = ap.add_subparsers(dest="cmd")

    p = sub.add_parser("snap", help="write a snapshot of current cumulative usage")
    p.add_argument("-o", "--out", required=True)

    p = sub.add_parser("diff", help="usage since SNAP (against SNAP2 or live state)")
    p.add_argument("snap")
    p.add_argument("snap2", nargs="?")

    p = sub.add_parser("record", help="diff vs SNAP and append a row to runs.csv")
    p.add_argument("snap")
    p.add_argument("--strategy", required=True)
    p.add_argument("--rep", required=True)
    p.add_argument("--tickets", required=True,
                   help="comma-separated ticket ids covered by this batch run")
    p.add_argument("--notes", default="")
    p.add_argument("--csv", default=str(Path(__file__).with_name("runs.csv")))

    args = ap.parse_args()
    if args.selftest:
        return selftest()
    if not args.cmd:
        ap.error("a subcommand is required (snap/diff/record) unless --selftest")
    project_filter = None if args.all else args.project

    if args.cmd == "snap":
        state = scan(args.claude_dir, project_filter)
        Path(args.out).write_text(json.dumps(state, indent=1))
        print(f"snapshot -> {args.out}  ({state['files']} transcript files, "
              f"{len(state['models'])} models)")
        return

    then = json.loads(Path(args.snap).read_text())
    snap2 = getattr(args, "snap2", None)  # only the diff subcommand has it
    if snap2:
        now = json.loads(Path(snap2).read_text())
    else:
        now = scan(args.claude_dir, project_filter)
    for name, s in ((args.snap, then), (snap2 or "<live>", now)):
        if s.get("v") != 2:
            sys.exit(f"{name}: snapshot schema v{s.get('v', 1)} != 2 -- "
                     "re-take it with the current script (a stale snapshot "
                     "would silently misprice the delta)")
    delta = subtract(now, then)
    wall_s = now["ts"] - then["ts"]

    rewrites = scan_rewrites(args.claude_dir, project_filter,
                             then["ts"], now["ts"])

    if args.cmd == "diff":
        print_delta(delta, wall_s)
        print_rewrites(rewrites)
        return

    # record
    agg = dict.fromkeys(FIELDS, 0)
    for u in delta.values():
        for f in FIELDS:
            agg[f] += u[f]
    row = {
        "strategy": args.strategy, "rep": args.rep, "tickets": args.tickets,
        "started_at": dt.datetime.fromtimestamp(then["ts"]).isoformat(timespec="seconds"),
        "wall_s": f"{wall_s:.0f}",
        "cost_usd": f"{cost_usd(delta):.4f}",
        "out_tok": agg["output_tokens"],
        "cw_5m_tok": agg["cache_write_5m"],
        "cw_1h_tok": agg["cache_write_1h"],
        "cache_read_tok": agg["cache_read_input_tokens"],
        "in_tok": agg["input_tokens"],
        "rw_events": len(rewrites),
        "rw_tok": sum(e["tokens"] for e in rewrites),
        "notes": args.notes,
    }
    csv_path = Path(args.csv)
    write_header = not csv_path.exists()
    with csv_path.open("a", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=CSV_COLUMNS)
        if write_header:
            w.writeheader()
        w.writerow(row)
    print_delta(delta, wall_s)
    print_rewrites(rewrites)
    print(f"recorded -> {csv_path}")


if __name__ == "__main__":
    sys.exit(main())
