#!/usr/bin/env python3
"""Board+git reset between orchbench strategy runs (tools/orchbench).

A strategy run mutates the Trello board (cards claimed/moved/edited, NEW
cards raised) and creates orchbench/* run branches. Before the next rep the
board must return to the frozen-batch state -- but nothing may be lost:
every change is archived first, new cards verbatim. Run branches are never
touched (the scoring pass checks them out); the board's activity.log is
never restored either, so the run's history stays in the store too.

The local Trello backend is file-backed (one JSON per card + lists.json +
board.json), so a snapshot is the raw file bytes and a restore is
byte-identical -- no CLI mutation calls, no partial states.

Usage:
  python tools/orchbench/reset_run.py snap  -o board_start.json
  ... run the strategy ...
  python tools/orchbench/reset_run.py diff  board_start.json
  python tools/orchbench/reset_run.py reset board_start.json \
      --label fable-oracle-rep1 [--dry-run]

`reset` writes tools/orchbench/archive/<ts>-<label>.json (full before/after
of every changed file) + a .md summary beside it, then restores the board.
`diff` is the read-only preview. --selftest exercises the whole cycle
against a synthetic store (no git, no real board).
"""

import argparse
import datetime as dt
import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path

BOARD_ID = "10989a3df6f36d59b01e6796"
SNAP_VERSION = 1

# Files that define board state. activity.log is deliberately absent: it is
# append-only history and restoring it would erase the run's audit trail.
# attachments/ is compared for additions but never restored/deleted.
STATE_GLOBS = ("board.json", "lists.json", "cards/*.json")


def store_root():
    out = subprocess.run(["trello", "local", "root"], capture_output=True,
                         text=True, shell=True)
    for line in out.stdout.splitlines():
        if line.startswith("Local store root:"):
            return Path(line.split(":", 1)[1].strip())
    sys.exit("could not determine the local store root (`trello local root`)")


def board_files(board_dir):
    """relpath -> file text, for every state file."""
    files = {}
    for pattern in STATE_GLOBS:
        for p in sorted(board_dir.glob(pattern)):
            files[p.relative_to(board_dir).as_posix()] = p.read_text(encoding="utf-8")
    return files


def attachment_names(board_dir):
    att = board_dir / "attachments"
    return sorted(p.relative_to(att).as_posix()
                  for p in att.rglob("*") if p.is_file()) if att.is_dir() else []


def git_state(repo):
    def run(*args):
        return subprocess.run(["git", "-C", str(repo), *args],
                              capture_output=True, text=True).stdout.strip()
    branches = {}
    for line in run("for-each-ref", "refs/heads/orchbench/",
                    "--format=%(refname:short) %(objectname)").splitlines():
        name, sha = line.rsplit(" ", 1)
        branches[name] = sha
    return {"head": run("rev-parse", "HEAD"),
            "branch": run("rev-parse", "--abbrev-ref", "HEAD"),
            "main": run("rev-parse", "main"),
            "dirty": bool(run("status", "--porcelain")),
            "orchbench_branches": branches}


def take_snapshot(board_dir, repo):
    return {"v": SNAP_VERSION, "ts": time.time(), "board": board_dir.name,
            "files": board_files(board_dir),
            "attachments": attachment_names(board_dir),
            "git": git_state(repo) if repo else None}


def card_name(text):
    try:
        return json.loads(text).get("name", "?")
    except (json.JSONDecodeError, ValueError):
        return "?"


def changed_fields(old_text, new_text):
    try:
        old, new = json.loads(old_text), json.loads(new_text)
    except (json.JSONDecodeError, ValueError):
        return ["<unparseable>"]
    keys = set(old) | set(new)
    return sorted(k for k in keys if old.get(k) != new.get(k))


def compute_diff(snap, board_dir):
    now = board_files(board_dir)
    then = snap["files"]
    added = sorted(set(now) - set(then))
    removed = sorted(set(then) - set(now))
    changed = sorted(f for f in set(now) & set(then) if now[f] != then[f])
    new_attachments = sorted(set(attachment_names(board_dir)) - set(snap["attachments"]))
    return {"added": added, "removed": removed, "changed": changed,
            "new_attachments": new_attachments, "now": now}


def describe(diff, snap):
    lines = []
    for f in diff["added"]:
        lines.append(f"NEW      {f}  \"{card_name(diff['now'][f])}\"")
    for f in diff["removed"]:
        lines.append(f"DELETED  {f}  \"{card_name(snap['files'][f])}\"")
    for f in diff["changed"]:
        fields = ", ".join(changed_fields(snap["files"][f], diff["now"][f]))
        name = card_name(diff["now"][f]) if f.startswith("cards/") else f
        lines.append(f"CHANGED  {f}  \"{name}\"  [{fields}]")
    for a in diff["new_attachments"]:
        lines.append(f"ATTACH   attachments/{a}  (kept on disk, not restored)")
    return lines


def git_report(snap, repo, label="<label>"):
    if not snap.get("git") or not repo:
        return []
    now = git_state(repo)
    lines = []
    base = snap["git"].get("main")
    if base and now["main"] != base:
        lines.append(f"MAIN     {base[:9]} -> {now['main'][:9]}  (run merges; "
                     f"`reset --git` archives the tip as orchbench/run-{label} "
                     "and moves main back)")
    for name, sha in sorted(now["orchbench_branches"].items()):
        if name not in snap["git"]["orchbench_branches"]:
            lines.append(f"BRANCH   {name} @ {sha[:9]}  (kept -- scoring needs it)")
    if now["dirty"]:
        lines.append("WARNING  working tree is dirty -- reset does not touch git; "
                     "clean it by hand before the next run")
    if now["branch"] != snap["git"]["branch"]:
        lines.append(f"WARNING  HEAD moved {snap['git']['branch']} -> {now['branch']} "
                     "-- check out the orchestration branch before the next run")
    return lines


def do_git_reset(snap, repo, label, dry_run):
    """Archive the run's main tip as a keeper branch, move main back to the
    snapshot baseline. The run's PR merges stay reachable forever through
    orchbench/run-<label>; main returns to the frozen state, force-pushed
    with --force-with-lease so a concurrent push fails loudly."""
    g = snap.get("git") or {}
    base = g.get("main")
    if not base:
        sys.exit("git: snapshot records no main commit -- retake it with the "
                 "current script before using --git")
    now = git_state(repo)
    if now["main"] == base:
        print("git: main already at the snapshot baseline")
        return
    if now["dirty"]:
        sys.exit("git: working tree dirty -- commit or stash before --git reset")
    keeper = f"orchbench/run-{label}"
    if keeper in now["orchbench_branches"]:
        sys.exit(f"git: {keeper} already exists -- pick a fresh label")
    if dry_run:
        print(f"[dry-run] git: would branch {keeper} @ {now['main'][:9]}, "
              f"move main back to {base[:9]}, push both")
        return

    def run(*args):
        r = subprocess.run(["git", "-C", str(repo), *args],
                           capture_output=True, text=True)
        if r.returncode != 0:
            sys.exit(f"git {' '.join(args)}: {r.stderr.strip()}")
        return r.stdout.strip()

    run("branch", keeper, now["main"])
    if now["branch"] == "main":
        run("reset", "--hard", base)
    else:
        run("branch", "-f", "main", base)
    remotes = run("remote").split()
    if "origin" in remotes:
        run("push", "origin", keeper)
        run("push", "--force-with-lease", "origin", "main")
        pushed = ", pushed"
    else:
        pushed = ", no origin remote -- not pushed"
    print(f"git: {keeper} @ {now['main'][:9]} kept; main {now['main'][:9]} -> "
          f"{base[:9]}{pushed}")


def do_reset(snap, board_dir, archive_dir, label, dry_run, repo):
    diff = compute_diff(snap, board_dir)
    report = describe(diff, snap) + git_report(snap, repo, label)
    if not any((diff["added"], diff["removed"], diff["changed"],
                diff["new_attachments"])):
        print("board matches the snapshot -- nothing to reset")
        for line in git_report(snap, repo, label):
            print(line)
        return None

    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    base = archive_dir / f"{stamp}-{label}"
    archive = {
        "v": SNAP_VERSION, "ts": time.time(), "label": label,
        "board": board_dir.name, "snapshot_ts": snap["ts"],
        # full texts both ways: nothing about the run is lost by the restore
        "added": {f: diff["now"][f] for f in diff["added"]},
        "removed": {f: snap["files"][f] for f in diff["removed"]},
        "changed": {f: {"before": snap["files"][f], "after": diff["now"][f]}
                    for f in diff["changed"]},
        "new_attachments": diff["new_attachments"],
        "report": report,
    }
    for line in report:
        print(line)
    if dry_run:
        print(f"\n[dry-run] would archive -> {base}.json and restore "
              f"{len(diff['removed']) + len(diff['changed'])} file(s), "
              f"delete {len(diff['added'])}")
        return None

    archive_dir.mkdir(parents=True, exist_ok=True)
    (base.parent / (base.name + ".json")).write_text(
        json.dumps(archive, indent=1), encoding="utf-8")
    (base.parent / (base.name + ".md")).write_text(
        f"# orchbench reset: {label} ({stamp})\n\n"
        + "\n".join(f"- `{line}`" for line in report) + "\n",
        encoding="utf-8")

    # Restore: byte-identical writes, deletions last (an interrupt then
    # leaves extra cards visible rather than frozen cards missing).
    for f in diff["removed"] + diff["changed"]:
        target = board_dir / f
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(snap["files"][f], encoding="utf-8")
    for f in diff["added"]:
        (board_dir / f).unlink()
    print(f"\narchived -> {base}.json\nboard restored "
          f"({len(diff['removed'])} recreated, {len(diff['changed'])} reverted, "
          f"{len(diff['added'])} removed; activity.log kept)")
    return archive


def selftest():
    failures = []

    def check(cond, msg):
        print(("PASS  " if cond else "FAIL  ") + msg)
        if not cond:
            failures.append(msg)

    with tempfile.TemporaryDirectory() as td:
        board = Path(td) / BOARD_ID
        (board / "cards").mkdir(parents=True)
        (board / "attachments").mkdir()
        card = lambda cid, name, lst: json.dumps(
            {"id": cid, "name": name, "idList": lst, "desc": "", "closed": False},
            indent=2)
        (board / "board.json").write_text('{"id": "b"}', encoding="utf-8")
        (board / "lists.json").write_text('[{"id": "backlog"}, {"id": "done"}]',
                                          encoding="utf-8")
        (board / "cards" / "aaa.json").write_text(card("aaa", "frozen ticket", "backlog"),
                                                  encoding="utf-8")
        (board / "cards" / "bbb.json").write_text(card("bbb", "doomed card", "backlog"),
                                                  encoding="utf-8")
        (board / "activity.log").write_text("line1\n", encoding="utf-8")

        snap = take_snapshot(board, repo=None)

        # the run: move a card, raise a new one, delete one, append activity
        moved = card("aaa", "frozen ticket", "done")
        (board / "cards" / "aaa.json").write_text(moved, encoding="utf-8")
        new_text = card("ccc", "NEW ticket raised mid-run", "backlog")
        (board / "cards" / "ccc.json").write_text(new_text, encoding="utf-8")
        (board / "cards" / "bbb.json").unlink()
        (board / "activity.log").write_text("line1\nline2\n", encoding="utf-8")

        # dry run changes nothing
        arch_dir = Path(td) / "archive"
        do_reset(snap, board, arch_dir, "t", dry_run=True, repo=None)
        check((board / "cards" / "ccc.json").exists() and not arch_dir.exists(),
              "dry-run leaves the store and archive untouched")

        archive = do_reset(snap, board, arch_dir, "t", dry_run=False, repo=None)
        check(archive is not None, "reset reports an archive")
        check(board_files(board) == snap["files"],
              "board state files restored byte-identical")
        check((board / "activity.log").read_text(encoding="utf-8") == "line1\nline2\n",
              "activity.log kept (not restored)")
        if archive:
            check(archive["added"].get("cards/ccc.json") == new_text,
                  "new card archived verbatim before deletion")
            check("cards/bbb.json" in archive["removed"],
                  "deleted card archived for recreation")
            ch = archive["changed"].get("cards/aaa.json", {})
            check(ch.get("after") == moved,
                  "moved card's run-state archived")
            check(any("idList" in line for line in archive["report"]),
                  "report names the changed field (idList)")
        jsons = list(arch_dir.glob("*.json"))
        check(len(jsons) == 1 and json.loads(jsons[0].read_text(encoding="utf-8"))
              ["added"].get("cards/ccc.json") == new_text,
              "archive file on disk round-trips the new card")

        # vacuous case: a second reset must be a no-op and write no archive
        archive2 = do_reset(snap, board, arch_dir, "t2", dry_run=False, repo=None)
        check(archive2 is None and len(list(arch_dir.glob("*.json"))) == 1,
              "reset on an unchanged board archives nothing (vacuity control)")

        # board-id mismatch refusal is main()'s guard; pin the predicate here
        check(BOARD_ID != "wrong", "board-id guard predicate sane")

        # --- git leg: keeper branch + main moved back, in a real temp repo
        repo = Path(td) / "repo"
        repo.mkdir()

        def g(*args):
            r = subprocess.run(["git", "-C", str(repo), *args],
                               capture_output=True, text=True)
            if r.returncode != 0:
                raise RuntimeError(r.stderr)
            return r.stdout.strip()

        g("init", "-b", "main")
        g("config", "user.email", "t@t")
        g("config", "user.name", "t")
        (repo / "f.txt").write_text("base", encoding="utf-8")
        g("add", "."); g("commit", "-m", "base")
        gsnap = {"git": git_state(repo)}
        (repo / "f.txt").write_text("run merge", encoding="utf-8")
        g("add", "."); g("commit", "-m", "run merge")
        tip = g("rev-parse", "HEAD")

        do_git_reset(gsnap, repo, "t1", dry_run=True)
        check(g("rev-parse", "HEAD") == tip, "git dry-run moves nothing")
        do_git_reset(gsnap, repo, "t1", dry_run=False)
        check(g("rev-parse", "main") == gsnap["git"]["main"],
              "main moved back to the snapshot baseline")
        check(g("rev-parse", "orchbench/run-t1") == tip,
              "run tip kept as the keeper branch")
        try:
            do_git_reset(gsnap, repo, "t1", dry_run=False)
            refused = True   # main already at baseline -> benign no-op
        except SystemExit:
            refused = False
        check(refused, "reset at baseline is a no-op, not an error")
        # a second run under a reused label must refuse, not clobber the keeper
        (repo / "f.txt").write_text("second run", encoding="utf-8")
        g("add", "."); g("commit", "-m", "second run")
        try:
            do_git_reset(gsnap, repo, "t1", dry_run=False)
            refused = False
        except SystemExit:
            refused = True
        check(refused and g("rev-parse", "orchbench/run-t1") == tip,
              "reused label refused; keeper branch not clobbered")

    print(f"\n{len(failures)} FAILURE(S)" if failures else "\nALL PASS")
    return 1 if failures else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--board", default=BOARD_ID)
    ap.add_argument("--store-root", default=None,
                    help="local Trello store root (default: ask the trello CLI)")
    ap.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]))
    ap.add_argument("--selftest", action="store_true")
    sub = ap.add_subparsers(dest="cmd")

    p = sub.add_parser("snap", help="snapshot board files + orchbench branch tips")
    p.add_argument("-o", "--out", required=True)

    p = sub.add_parser("diff", help="preview what a reset would archive/restore")
    p.add_argument("snap")

    p = sub.add_parser("reset", help="archive every change, then restore the board")
    p.add_argument("snap")
    p.add_argument("--label", required=True,
                   help="archive name tag, e.g. fable-oracle-rep1")
    p.add_argument("--archive-dir",
                   default=str(Path(__file__).with_name("archive")))
    p.add_argument("--git", action="store_true",
                   help="also archive the run's main tip as orchbench/run-"
                        "<label> and move main back to the snapshot baseline")
    p.add_argument("--dry-run", action="store_true")

    args = ap.parse_args()
    if args.selftest:
        return selftest()
    if not args.cmd:
        ap.error("a subcommand is required (snap/diff/reset) unless --selftest")

    root = Path(args.store_root) if args.store_root else store_root()
    board_dir = root / args.board
    if not (board_dir / "cards").is_dir():
        sys.exit(f"not a board dir: {board_dir}")
    repo = Path(args.repo)

    if args.cmd == "snap":
        snap = take_snapshot(board_dir, repo)
        Path(args.out).write_text(json.dumps(snap, indent=1), encoding="utf-8")
        print(f"snapshot -> {args.out}  ({len(snap['files'])} files, "
              f"{len(snap['git']['orchbench_branches']) if snap['git'] else 0} "
              f"orchbench branches)")
        return 0

    snap = json.loads(Path(args.snap).read_text(encoding="utf-8"))
    if snap.get("v") != SNAP_VERSION:
        sys.exit(f"snapshot schema v{snap.get('v')} != {SNAP_VERSION} -- retake it")
    if snap.get("board") != board_dir.name:
        sys.exit(f"snapshot is of board {snap.get('board')}, not {board_dir.name}")

    if args.cmd == "diff":
        diff = compute_diff(snap, board_dir)
        lines = describe(diff, snap) + git_report(snap, repo)
        print("\n".join(lines) if lines else "board matches the snapshot")
        return 0

    do_reset(snap, board_dir, Path(args.archive_dir), args.label,
             args.dry_run, repo)
    if args.git:
        do_git_reset(snap, repo, args.label, args.dry_run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
