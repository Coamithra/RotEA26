#!/usr/bin/env python
"""Publish Revenge of the Evil Aliens and upload it to the website over SFTP.

The game serves from https://haraldmaassen.com/RotEA26/ -- a SIBLING of
https://haraldmaassen.com/meridian/ on the shared Apache host, which is what
lets Meridian's launcher resolve it as a relative path. Credentials come from
the repo-root ``.env`` (SFTP_HOST / SFTP_USER / SFTP_PASS / SFTP_PATH); the
password is never printed. The site lands in ``SFTP_PATH/<subdir>`` (default
``RotEA26``).

    python tools/deploy_web.py --list        # inspect the remote target, exit
    python tools/deploy_web.py --dry-run     # publish + stamp + show the manifest, upload NOTHING
    python tools/deploy_web.py               # publish + stamp + incremental upload
    python tools/deploy_web.py --site DIR    # skip the publish, upload a prepared wwwroot
    python tools/deploy_web.py --rm          # guarded recursive delete of the remote target

Read ``docs/DEPLOY.md`` before the first real run -- step 0 there is a MANUAL
hosting-quota check that this script can only partially automate.

Requires paramiko (already installed on the dev machine) and the .NET 8 SDK
with the wasm-tools workload.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import posixpath
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
ENV_KEYS = ("SFTP_HOST", "SFTP_USER", "SFTP_PASS", "SFTP_PATH")

# Where the game serves from. The published <base href> must match this path or
# every relative asset fetch 404s.
BASE_HREF = "/RotEA26/"
DEFAULT_SUBDIR = "RotEA26"

# Uploaded alongside the site so the NEXT deploy knows what is already there and
# which files a new build orphaned. Public and harmless -- it holds hashes of
# files that are themselves public.
MANIFEST_NAME = ".deploy-manifest.json"
MANIFEST_VERSION = 1

# The CI recipe hashed paths as spelled on its own command line, i.e. rooted at
# "release/wwwroot". Reproduced verbatim below -- see build_hash().
CI_SITE_PREFIX = "release/wwwroot"


# --------------------------------------------------------------------------
# .env
# --------------------------------------------------------------------------

def find_env(explicit: str | None) -> Path:
    """Locate the credentials file.

    ``.env`` is gitignored, so a ``.claude/worktrees/wt<k>`` worktree does not
    have one -- fall back to the root checkout's copy three levels up.
    """
    if explicit:
        return Path(explicit)
    env_path = REPO_ROOT / ".env"
    if not env_path.exists() and REPO_ROOT.parent.name == "worktrees":
        # .claude/worktrees/wt<k> -> .claude/worktrees -> .claude -> repo root
        env_path = REPO_ROOT.parent.parent.parent / ".env"
    return env_path


def load_env(path: Path) -> dict[str, str]:
    """Read ONLY the four SFTP keys. Anything else in the file is ignored."""
    env: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line.lower().startswith("export "):
            line = line[7:]
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        k = k.strip()
        if k not in ENV_KEYS:
            continue
        v = v.strip()
        if len(v) >= 2 and v[0] == v[-1] and v[0] in ("'", '"'):
            v = v[1:-1]
        env[k] = v
    return env


# --------------------------------------------------------------------------
# publish
# --------------------------------------------------------------------------

def run_step(cmd: list[str], cwd: Path, what: str) -> None:
    """Run a build step, failing with a readable message rather than a traceback.

    A `CalledProcessError` stack dumped at a user mid-deploy buries the actual
    cause under six frames of subprocess internals.
    """
    try:
        subprocess.run(cmd, cwd=cwd, check=True)
    except subprocess.CalledProcessError as ex:
        sys.exit(f"{what}\n  (exit {ex.returncode}: {' '.join(cmd[:3])} ...)")
    except OSError as ex:
        sys.exit(f"{what}\n  ({cmd[0]} could not be run: {ex})")


def publish(ref: str, workdir: Path) -> Path:
    """Release-publish ``ref`` from a throwaway checkout; return its wwwroot.

    Publishing from the working tree would ship whatever untracked files happen
    to sit under ``wwwroot/`` -- generated probe output, half-built assets, a
    stray ``Content/sfx/peaks/``. ``dotnet publish`` copies the directory, not
    the git index, so it cannot tell the difference. A detached worktree at
    ``ref`` has exactly the committed files and nothing else.
    """
    src = workdir / "src"
    print(f"[publish] checking out {ref} -> {src}")
    run_step(["git", "worktree", "add", "--detach", str(src), ref], REPO_ROOT,
             f"could not check out {ref!r} -- is it a real commit or branch?")
    out = workdir / "release"
    print(f"[publish] dotnet publish -c Release -> {out}")
    run_step(["dotnet", "publish", "web/EvilAliensWeb", "-c", "Release", "-o", str(out)],
             src, "the Release publish failed -- see the build output above")
    site = out / "wwwroot"
    if not (site / "index.html").exists():
        sys.exit(f"publish produced no index.html at {site}")
    return site


def resolve_ref(ref: str) -> str | None:
    """The commit ``ref`` names, so the manifest records what is actually live.

    Storing ``args.ref`` verbatim would write ``"HEAD"`` into every manifest --
    true, useless, and exactly the question the field exists to answer.
    """
    try:
        out = subprocess.run(["git", "rev-parse", ref], cwd=REPO_ROOT,
                             check=True, capture_output=True, text=True)
        return out.stdout.strip()
    except (subprocess.CalledProcessError, OSError):
        return None


def drop_publish_worktree(workdir: Path) -> None:
    """Unregister the throwaway checkout so git does not keep a dead entry."""
    src = workdir / "src"
    if not src.exists():
        return
    subprocess.run(
        ["git", "worktree", "remove", "--force", str(src)],
        cwd=REPO_ROOT, check=False,
    )
    subprocess.run(["git", "worktree", "prune"], cwd=REPO_ROOT, check=False)


# --------------------------------------------------------------------------
# stamping
# --------------------------------------------------------------------------

def iter_site_files(site: Path) -> list[Path]:
    return sorted(p for p in site.rglob("*") if p.is_file())


def build_hash(site: Path) -> str:
    """The online co-op build fingerprint.

    THIS IS THE CO-OP COMPATIBILITY KEY. NetSession's hello handshake compares
    it and refuses a peer whose hash differs, and the game browser filters the
    room list on it -- so two players only ever meet when their builds hash the
    same. Do not "improve" the recipe: it is ported VERBATIM from
    .github/workflows/deploy.yml, which is now DELETED (card 54c2a8f2) -- so this
    function plus --selftest are the only surviving statement of it, and a site
    published by this script stays indistinguishable from one CI published.

    The shell it replaces was:

        HASH=$( (sha256sum release/wwwroot/_framework/blazor.boot.json; \\
                 find release/wwwroot/Content -type f -print0 | sort -z | xargs -0 sha256sum) \\
                | sha256sum | cut -c1-16)

    Three details that must survive the port or the hash silently changes:
      * ``sha256sum`` writes ``<hex><2 spaces><path>\\n`` -- it is that TEXT
        that gets hashed, not the file bytes.
      * the paths in that text are as spelled on the CI command line, rooted at
        ``release/wwwroot`` -- so they are re-synthesised here rather than taken
        from wherever this publish happens to live.
      * ``find -print0 | sort -z`` orders the NUL-terminated paths. Every name
        under Content/ is plain ASCII, so byte order and the runner's C.UTF-8
        collation agree; sorting the reconstructed CI-relative paths reproduces
        it. (Python's codepoint sort is locale-independent, so this is if
        anything steadier than the shell it replaces.)

    This function is PURE -- same tree in, same hash out, which is what
    ``--selftest`` pins. The published hash still differs between two builds of
    one commit, because ``blazor.boot.json`` embeds per-assembly integrity hashes
    and ``dotnet publish`` is not byte-reproducible. That is inherited behaviour
    and it is the intended semantics (the check asks "are we running the same
    BITS", not "the same commit") -- see docs/DEPLOY.md, "The hash identifies a
    PUBLISH, not a commit".
    """
    def line(path: Path, ci_path: str) -> bytes:
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        return f"{digest}  {ci_path}\n".encode()

    stream = bytearray()
    boot = site / "_framework" / "blazor.boot.json"
    if not boot.exists():
        sys.exit(f"missing {boot} -- cannot compute the build hash")
    stream += line(boot, f"{CI_SITE_PREFIX}/_framework/blazor.boot.json")

    content = site / "Content"
    if not content.is_dir():
        sys.exit(f"missing {content} -- cannot compute the build hash")
    ci_paths = sorted(
        f"{CI_SITE_PREFIX}/Content/{p.relative_to(content).as_posix()}"
        for p in content.rglob("*") if p.is_file()
    )
    for ci_path in ci_paths:
        rel = ci_path[len(f"{CI_SITE_PREFIX}/Content/"):]
        stream += line(content / rel, ci_path)

    return hashlib.sha256(bytes(stream)).hexdigest()[:16]


# A fixed tree and the hash the SHELL recipe produces for it, captured while
# .github/workflows/deploy.yml still existed to be compared against. THAT WORKFLOW IS
# NOW DELETED, so this vector is the only surviving evidence of the original recipe and
# `--selftest` is what stops a well-meaning edit from silently redefining the co-op
# compatibility key. Treat a FAIL as "I am about to split the player base", never as a
# stale test -- there is nothing left to re-derive the recipe from. Each name earns its
# place:
#   Z / a          uppercase sorts BEFORE lowercase (byte order, not locale collation)
#   a10 / a2       plain lexicographic, NOT natural/numeric ordering
#   'd e.txt'      a space in the name
#   b-x vs b/c     '-' (0x2D) < '/' (0x2F), so the FILE precedes the DIRECTORY's
#                  contents -- an implementation that walks dirs first fails here
SELFTEST_FILES = {
    "_framework/blazor.boot.json": b'{"resources":{"assembly":{}}}',
    "Content/Z.txt": b"Z",
    "Content/a.txt": b"a",
    "Content/a10.txt": b"a10",
    "Content/a2.txt": b"a2",
    "Content/b-x.txt": b"bx",
    "Content/b/c.txt": b"bc",
    "Content/b/d e.txt": b"de",
}
SELFTEST_HASH = "81e6338c0c74dca6"


def selftest() -> int:
    """Prove build_hash() still reproduces the CI shell recipe."""
    with tempfile.TemporaryDirectory(prefix="rotea-selftest-") as tmp:
        site = Path(tmp) / "release" / "wwwroot"
        for rel, body in SELFTEST_FILES.items():
            p = site / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(body)
        got = build_hash(site)
    ok = got == SELFTEST_HASH
    print(f"build hash selftest: got {got}, want {SELFTEST_HASH} -- "
          + ("PASS" if ok else "FAIL"))
    if not ok:
        print("The build fingerprint recipe CHANGED. Every peer on the old recipe\n"
              "becomes incompatible with every peer on the new one, and the game\n"
              "browser stops listing rooms across the boundary. Revert, or accept\n"
              "the break deliberately and re-baseline this constant.")
    return 0 if ok else 1


def stamp(site: Path) -> str:
    """Rewrite index.html for the live host. Returns the stamped build hash.

    Two edits, both of which the (now deleted) Pages workflow did:
      * ``<base href="/" />`` -> ``/RotEA26/`` (the dev build keeps "/" so
        ``dotnet run`` works at a domain root).
      * ``window.eaBuildHash = 'dev'`` -> the real fingerprint. Leaving it at
        'dev' would ALSO leave the frame-profiler HUD visible on the live site,
        which keys off that same value.

    IDEMPOTENT, because the documented one-publish workflow re-stamps a tree that
    is already stamped: ``--build-only --keep-build`` then ``--dry-run --site``
    then ``--site`` all hand the same directory back here. Re-stamping verifies
    the existing hash rather than rejecting the tree.
    """
    # stamp() EDITS THE TREE IT IS GIVEN, in place. That is harmless for a
    # throwaway publish but would rewrite a source checkout, so refuse anything
    # that is not a publish output. The absent _framework/ is the reliable tell:
    # web/EvilAliensWeb/wwwroot is the authored site and has no built runtime.
    if not (site / "_framework").is_dir():
        sys.exit(f"{site} has no _framework/ -- that is a SOURCE wwwroot, not a "
                 "publish output. This tool rewrites index.html in place; pass a "
                 "published directory (or omit --site and let it build one).")

    # Validate and hash BEFORE touching anything, so a failure cannot leave a
    # half-stamped index.html behind and the log never announces an edit it did
    # not go on to make.
    index = site / "index.html"
    # Byte-exact round trip: read_text/write_text would normalise the file's line
    # endings on the way through, silently rewriting every line of a file we only
    # meant to touch twice.
    html = index.read_bytes().decode("utf-8")
    already_based = f'<base href="{BASE_HREF}" />' in html
    if not already_based and '<base href="/" />' not in html:
        sys.exit('index.html has no recognisable <base href="/" /> to rewrite')
    prior = re.search(r"window\.eaBuildHash = '([0-9a-f]{16})'", html)
    if prior is None and "window.eaBuildHash = 'dev'" not in html:
        sys.exit("index.html has neither window.eaBuildHash = 'dev' nor an "
                 "already-stamped hash -- is this the game's index.html?")

    digest = build_hash(site)

    if already_based:
        print(f"[stamp] base href already {BASE_HREF}")
    else:
        html = html.replace('<base href="/" />', f'<base href="{BASE_HREF}" />', 1)
        print(f"[stamp] base href -> {BASE_HREF}")
    if prior is not None:
        # Re-stamping a tree an earlier run already stamped. The hash is a pure
        # function of that tree, so a DIFFERENT value means the files changed
        # under us between the rehearsal and the upload -- exactly the mix-up
        # this workflow exists to avoid, so refuse rather than quietly restamp.
        if prior.group(1) != digest:
            sys.exit(f"{index} is already stamped {prior.group(1)} but this tree "
                     f"hashes {digest} -- the directory changed since it was "
                     "stamped; rebuild rather than uploading a mixed tree")
        print(f"[stamp] eaBuildHash already {digest}")
    else:
        html = html.replace(
            "window.eaBuildHash = 'dev'", f"window.eaBuildHash = '{digest}'", 1
        )
        print(f"[stamp] eaBuildHash -> {digest}")

    index.write_bytes(html.encode("utf-8"))
    check = index.read_bytes().decode("utf-8")
    if f"eaBuildHash = '{digest}'" not in check or f'<base href="{BASE_HREF}"' not in check:
        sys.exit("stamping did not take -- refusing to upload")
    return digest


# --------------------------------------------------------------------------
# SFTP
# --------------------------------------------------------------------------

def connect(env: dict[str, str], port: int, insecure: bool = False):
    """Open an SFTP session, verifying the host key first.

    This hands a PLAINTEXT ACCOUNT PASSWORD to whatever answers on port 22, so
    the host key is checked against the machine's known_hosts before the
    password is offered. A bare ``paramiko.Transport`` performs no such check --
    anything that can win a DNS race or sit on the path (captive wifi) would
    collect the credentials and could then serve a forged site.
    """
    import paramiko
    missing = [k for k in ENV_KEYS if not env.get(k)]
    if missing:
        sys.exit("missing in .env: " + ", ".join(missing))

    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(
        paramiko.AutoAddPolicy() if insecure else paramiko.RejectPolicy()
    )
    if insecure:
        print("[ssh] WARNING: --insecure-host-key skips host verification; the "
              "account password is being offered to an unauthenticated host")
    try:
        client.connect(
            hostname=env["SFTP_HOST"], port=port, username=env["SFTP_USER"],
            password=env["SFTP_PASS"], allow_agent=False, look_for_keys=False,
        )
    except paramiko.SSHException as ex:
        # Never let the exception text carry the password onward.
        if "not found in known_hosts" in str(ex) or isinstance(ex, paramiko.BadHostKeyException):
            sys.exit(
                f"host key for {env['SFTP_HOST']} is not in known_hosts, so the "
                f"password was NOT sent.\nRecord it once (verify the fingerprint "
                f"against your host's control panel):\n"
                f"    ssh-keyscan -p {port} {env['SFTP_HOST']} >> ~/.ssh/known_hosts\n"
                f"or connect interactively: ssh {env['SFTP_USER']}@{env['SFTP_HOST']}\n"
                f"(--insecure-host-key bypasses this; only for a key you have "
                f"just rotated and verified out of band)"
            )
        sys.exit(f"ssh connection failed: {type(ex).__name__}: {ex}")

    sftp = client.open_sftp()
    if sftp is None:
        sys.exit("could not open an SFTP session")
    return client, sftp


def remote_free_bytes(sftp, path: str) -> int | None:
    """Best-effort free space via the OpenSSH ``statvfs@openssh.com`` extension.

    paramiko has no public statvfs, and shared hosts commonly refuse the
    extension or answer for the whole underlying filesystem rather than the
    account's quota -- so a number here is a smoke test, never the authority.
    The hosting control panel is. Returns None when it cannot tell.
    """
    try:
        from paramiko.sftp import CMD_EXTENDED, CMD_EXTENDED_REPLY
        t, msg = sftp._request(  # noqa: SLF001 - no public API for this
            CMD_EXTENDED, "statvfs@openssh.com", path.encode()
        )
        if t != CMD_EXTENDED_REPLY:
            return None
        msg.get_int64()                       # f_bsize
        frsize = msg.get_int64()              # f_frsize (fragment size)
        msg.get_int64()                       # f_blocks
        msg.get_int64()                       # f_bfree
        bavail = msg.get_int64()              # f_bavail (free to non-root)
        return frsize * bavail
    except Exception:
        return None


def mkdir_p(sftp, remote_dir: str, made: set[str]) -> None:
    """Create ``remote_dir`` and its parents, failing loudly if it cannot.

    A swallowed mkdir surfaces hundreds of files later as a bare paramiko
    traceback out of ``sftp.put``, mid-upload, with the real cause (a permission
    or quota problem on one directory) already thrown away.
    """
    if remote_dir in made:
        return
    parts = [p for p in remote_dir.split("/") if p]
    cur = "/" if remote_dir.startswith("/") else ""
    for p in parts:
        cur = (cur.rstrip("/") + "/" + p) if cur else p
        if cur in made:
            continue
        try:
            sftp.stat(cur)
        except IOError:
            try:
                sftp.mkdir(cur)
            except IOError as ex:
                # A racing creation is fine; anything else is not.
                try:
                    sftp.stat(cur)
                except IOError:
                    sys.exit(f"could not create remote directory {cur}: {ex}")
        made.add(cur)


def remote_sizes(sftp, base: str) -> dict[str, int]:
    """Recursive {relative path: size} of the remote target ({} if absent)."""
    out: dict[str, int] = {}

    def walk(rel: str) -> None:
        full = posixpath.join(base, rel) if rel else base
        try:
            entries = sftp.listdir_attr(full)
        except IOError:
            return
        for e in entries:
            child = posixpath.join(rel, e.filename) if rel else e.filename
            if stat.S_ISDIR(e.st_mode or 0):
                walk(child)
            else:
                out[child] = e.st_size or 0

    walk("")
    return out


def read_remote_manifest(sftp, base: str) -> dict | None:
    try:
        with sftp.open(posixpath.join(base, MANIFEST_NAME), "r") as fh:
            data = json.loads(fh.read().decode("utf-8"))
    except Exception:
        return None
    if not isinstance(data, dict) or data.get("version") != MANIFEST_VERSION:
        return None
    if not isinstance(data.get("files"), dict):
        return None
    return data


def check_rm_target(base: str, target: str, subdir: str) -> None:
    """Refuse a --rm that would take out more than the game's own directory.

    ``SFTP_PATH`` is the account's WEB ROOT: it holds ``/meridian/`` and every
    other site on this host. ``--subdir ""`` is a legitimate way to *deploy* to
    that root, which makes ``target == base`` reachable -- and a path-depth check
    does not catch it, because a perfectly ordinary ``/home/<user>/public_html``
    has three components and sails through. Hence the explicit identity test.

    Pure and side-effect free so it can be tested without a connection, and
    called before one is opened.
    """
    if target == base or not subdir.strip("/"):
        sys.exit(f"refusing --rm on the web root itself ({target}) -- --rm only "
                 "ever removes the game's own subdirectory, and deleting the root "
                 "would take /meridian/ and every sibling site with it")
    if len([p for p in target.split("/") if p]) < 2:
        sys.exit(f"refusing --rm on a top-level path: {target}")


def rmtree(sftp, path: str) -> None:
    try:
        entries = sftp.listdir_attr(path)
    except IOError:
        return
    for e in entries:
        full = posixpath.join(path, e.filename)
        if stat.S_ISDIR(e.st_mode or 0):
            rmtree(sftp, full)
        else:
            sftp.remove(full)
    sftp.rmdir(path)


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------

def human(n: float) -> str:
    return f"{n / 1e6:.1f} MB" if n < 1e9 else f"{n / 1e9:.2f} GB"


def local_manifest(site: Path) -> tuple[dict[str, str], dict[str, int]]:
    """{rel: sha256} and {rel: size} for everything that will be uploaded."""
    hashes: dict[str, str] = {}
    sizes: dict[str, int] = {}
    for p in iter_site_files(site):
        rel = p.relative_to(site).as_posix()
        if rel == MANIFEST_NAME:
            continue
        hashes[rel] = hashlib.sha256(p.read_bytes()).hexdigest()
        sizes[rel] = p.stat().st_size
    return hashes, sizes


def main() -> None:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--env", help="path to the credentials file (default: repo-root .env)")
    ap.add_argument("--subdir", default=DEFAULT_SUBDIR,
                    help=f"target folder under SFTP_PATH (default {DEFAULT_SUBDIR!r}; "
                         "empty uploads to SFTP_PATH itself)")
    ap.add_argument("--ref", default="HEAD",
                    help="commit/branch to publish (default HEAD); ignored with --site")
    ap.add_argument("--site", help="upload this already-published wwwroot instead of building")
    ap.add_argument("--port", type=int, default=22)
    ap.add_argument("--dry-run", action="store_true",
                    help="publish + stamp + print the upload manifest; write NOTHING remotely")
    ap.add_argument("--build-only", action="store_true",
                    help="publish + stamp + report the payload, then stop. Never opens a "
                         "connection and needs no credentials -- run this first to get the "
                         "payload size for the quota check in docs/DEPLOY.md step 0")
    ap.add_argument("--list", action="store_true", help="list the remote target and exit")
    ap.add_argument("--rm", action="store_true",
                    help="recursively delete the remote target and exit (refuses top-level paths)")
    ap.add_argument("--force-all", action="store_true",
                    help="re-upload every file, ignoring the remote manifest")
    ap.add_argument("--no-prune", action="store_true",
                    help="keep remote files this build orphaned (default: delete them)")
    ap.add_argument("--keep-build", action="store_true",
                    help="do not delete the temporary checkout / publish output")
    ap.add_argument("--selftest", action="store_true",
                    help="check the build-hash recipe against its pinned value and exit")
    ap.add_argument("--insecure-host-key", action="store_true",
                    help="skip SSH host-key verification (UNSAFE: offers the account "
                         "password to an unauthenticated host). Only for a key you have "
                         "just rotated and verified out of band")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(selftest())

    # --build-only never talks to the host, so it must not demand credentials --
    # that is what makes it runnable on a machine that cannot deploy.
    env: dict[str, str] = {}
    base = target = ""
    if not args.build_only:
        env_path = find_env(args.env)
        if not env_path.exists():
            sys.exit(f"no credentials file at {env_path} (see docs/DEPLOY.md)")
        env = load_env(env_path)
        for k in ENV_KEYS:
            if not env.get(k):
                sys.exit(f"missing {k} in {env_path}")
        base = env["SFTP_PATH"].rstrip("/")
        target = posixpath.join(base, args.subdir.strip("/")) if args.subdir.strip("/") else base
        print(f"host={env['SFTP_HOST']} user={env['SFTP_USER']} target={target} "
              f"(password: <{len(env['SFTP_PASS'])} chars, hidden>)")

    # Decide whether --rm is even permissible BEFORE opening a connection, so a
    # refused delete never offers the account password to anything.
    if args.rm:
        check_rm_target(base, target, args.subdir)

    # --- remote-only modes ------------------------------------------------
    if args.list or args.rm:
        client, sftp = connect(env, args.port, args.insecure_host_key)
        try:
            if args.rm:
                # Demand the FULL path, not the basename: a basename prompt reads
                # as routine ("public_html") on exactly the target that matters.
                confirm = input(f"delete EVERYTHING under {target}?\n"
                                f"type the full path to confirm: ")
                if confirm.strip() != target:
                    sys.exit("aborted")
                rmtree(sftp, target)
                print("removed")
                return
            print(f"\nlisting {target}:")
            try:
                for e in sorted(sftp.listdir_attr(target), key=lambda x: x.filename):
                    kind = "d" if stat.S_ISDIR(e.st_mode or 0) else "-"
                    print(f"  {kind} {e.st_size or 0:>12}  {e.filename}")
            except IOError as ex:
                print(f"  (cannot list -- may not exist yet: {ex})")
            free = remote_free_bytes(sftp, base)
            print(f"\nremote free space: "
                  + (human(free) if free is not None else
                     "could not determine -- verify manually in the hosting panel"))
            return
        finally:
            sftp.close()
            client.close()

    # --- build ------------------------------------------------------------
    # The publish itself lives INSIDE the try: `git worktree add` and
    # `dotnet publish` are the likeliest things here to fail, and a failure
    # outside it would leak both the temp dir and a REGISTERED git worktree into
    # the real repo's `git worktree list`.
    workdir: Path | None = None
    try:
        if args.site:
            site = Path(args.site).resolve()
            if not (site / "index.html").exists():
                sys.exit(f"{site} has no index.html")
            print(f"[publish] skipped -- using {site}")
            ref_sha = None
        else:
            workdir = Path(tempfile.mkdtemp(prefix="rotea-deploy-"))
            site = publish(args.ref, workdir)
            ref_sha = resolve_ref(args.ref)

        digest = stamp(site)
        hashes, sizes = local_manifest(site)
        payload = sum(sizes.values())
        print(f"[site] {len(hashes)} files, {human(payload)}")

        if args.build_only:
            biggest = sorted(sizes.items(), key=lambda kv: -kv[1])[:5]
            print("[site] largest files:")
            for rel, n in biggest:
                print(f"         {human(n):>9}  {rel}")
            top: dict[str, int] = {}
            for rel, n in sizes.items():
                head = rel.split("/")[0] if "/" in rel else "(root)"
                top[head] = top.get(head, 0) + n
            print("[site] by top-level directory:")
            for d, n in sorted(top.items(), key=lambda kv: -kv[1]):
                print(f"         {human(n):>9}  {d}")
            print(f"\nbuild hash {digest}")
            print(f"payload {human(payload)} in {len(hashes)} files -- check this fits the "
                  "hosting quota (docs/DEPLOY.md step 0) before deploying")
            return

        client, sftp = connect(env, args.port, args.insecure_host_key)
        try:
            # --- quota gate (advisory) --------------------------------
            free = remote_free_bytes(sftp, base)
            if free is None:
                print("[quota] could not determine remote free space -- verify manually "
                      "in the hosting panel (docs/DEPLOY.md step 0)")
            else:
                print(f"[quota] remote free {human(free)} vs payload {human(payload)}")
                if free < payload:
                    sys.exit("[quota] not enough free space reported -- aborting before any write")

            # --- what needs uploading ---------------------------------
            prev = None if args.force_all else read_remote_manifest(sftp, target)
            orphans: list[str] = []
            if prev is not None:
                old = prev["files"]
                todo = [r for r, h in hashes.items() if old.get(r) != h]
                # Files a previous run left behind (--no-prune, or a delete that
                # failed) are recorded in "stale" and stay candidates. Without
                # that they would be orphaned AND forgotten on the first run, so
                # nothing could ever reclaim them.
                known = set(old) | set(prev.get("stale") or [])
                orphans = sorted(known - set(hashes))
                print(f"[plan] manifest {prev.get('buildHash', '?')} -> {digest}: "
                      f"{len(todo)} changed/new, {len(orphans)} orphaned")
            else:
                # First deploy after this script landed (or --force-all): there is
                # no manifest to diff against, so fall back to a SIZE-only compare.
                # mtime is useless here -- publishing from a fresh checkout stamps
                # every file with today's date, so everything would look newer.
                rsizes = {} if args.force_all else remote_sizes(sftp, target)
                todo = [r for r, s in sizes.items() if rsizes.get(r) != s]
                if not args.force_all and rsizes:
                    print("[plan] no remote manifest -- SIZE-only comparison; "
                          "use --force-all to re-upload everything")
                print(f"[plan] {len(todo)} of {len(hashes)} files to upload")
            todo.sort()
            upload_bytes = sum(sizes[r] for r in todo)

            if args.dry_run:
                for r in todo:
                    print(f"  (dry) {r}  ({sizes[r]} B)")
                for r in orphans:
                    print(f"  (dry) DELETE {r}")
                print(f"\n(dry) would upload {len(todo)} files, {human(upload_bytes)} -> {target}")
                print(f"(dry) would {'keep' if args.no_prune else 'delete'} "
                      f"{len(orphans)} orphaned remote files")
                print(f"(dry) build hash {digest}")
                return

            # --- upload -----------------------------------------------
            made: set[str] = set()
            mkdir_p(sftp, target, made)

            # Invalidate the manifest BEFORE the first byte moves. If this run is
            # interrupted, the host is left with no manifest, so the next run
            # falls back to the size compare and re-checks every file. Leaving
            # the old one in place would be actively dangerous under --force-all:
            # an interrupted re-upload can truncate a file whose content did NOT
            # change between builds, and the stale manifest would then vouch for
            # its old hash forever, so no later deploy would ever fix it.
            try:
                sftp.remove(posixpath.join(target, MANIFEST_NAME))
            except IOError:
                pass  # absent is the normal first-deploy case

            done_bytes = 0
            for i, rel in enumerate(todo, 1):
                remote = posixpath.join(target, rel)
                mkdir_p(sftp, posixpath.dirname(remote), made)
                sftp.put(str(site / rel), remote)
                done_bytes += sizes[rel]
                if i % 25 == 0 or i == len(todo):
                    print(f"  {i}/{len(todo)}  {human(done_bytes)}/{human(upload_bytes)}  {rel}")

            # Anything still on the host that this build does not include, and
            # that we did not manage to remove, is carried forward so a later run
            # can try again.
            stale = sorted(orphans) if args.no_prune else []
            if orphans and not args.no_prune:
                # Only ever delete files a PREVIOUS manifest claims we put there.
                # Anything else on the host is somebody else's and stays.
                for rel in orphans:
                    try:
                        sftp.remove(posixpath.join(target, rel))
                        print(f"  deleted {rel}")
                    except IOError as ex:
                        print(f"  could not delete {rel}: {ex}")
                        stale.append(rel)

            # Manifest LAST, and only once every upload and delete has succeeded:
            # it is the claim "the host holds exactly this", so writing it early
            # would let a later run skip a file that never actually landed.
            body = json.dumps(
                {"version": MANIFEST_VERSION, "buildHash": digest,
                 "ref": ref_sha, "files": hashes, "stale": sorted(set(stale))},
                indent=0, sort_keys=True,
            ).encode()
            with sftp.open(posixpath.join(target, MANIFEST_NAME), "w") as fh:
                fh.write(body)

            print(f"\nuploaded {len(todo)} files ({human(upload_bytes)}) -> {target}")
            print(f"build hash {digest}")
            print("now verify:  python tools/check_deploy.py --hash " + digest)
        finally:
            sftp.close()
            client.close()
    finally:
        if workdir is not None and not args.keep_build:
            drop_publish_worktree(workdir)
            shutil.rmtree(workdir, ignore_errors=True)
        elif workdir is not None:
            print(f"[publish] kept {workdir} (remove its git worktree with "
                  f"`git worktree remove --force {workdir / 'src'}`)")


if __name__ == "__main__":
    main()
