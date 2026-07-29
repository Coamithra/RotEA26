# Deploying the game

> **STATUS — the cutover has not happened yet.** The public site is still GitHub
> Pages (https://coamithra.github.io/RotEA26/), last published 2026-07-23 and by
> now hundreds of commits stale. `https://haraldmaassen.com/RotEA26/` currently
> **404s**; it starts existing the first time someone runs the flow below, and
> that first run is the user's to make. Until then `.github/workflows/deploy.yml`
> is still the live publishing route and still works. Deleting it, flipping
> `MERIDIAN_BASE` / Meridian's `GAME_ORIGIN` to relative, and taking Pages down
> are all follow-up work gated on that first deploy being verified.

Two independent public deployments, neither of which happens by merging a PR:

| What | Where | How |
|---|---|---|
| The game (static site) | https://haraldmaassen.com/RotEA26/ | `python tools/deploy_web.py` (SFTP, creds in `.env`) |
| Signaling server | `wss://notzelda.haraldmaassen.com/rotea/ws` | `server/signal/README.md` (scp + `systemctl restart rotea`) |

**Shipping a networked feature needs BOTH.** The game browser's `browse` filters
on build hash, so players only ever see each other's rooms when they are on the
same published build — and a client that speaks a protocol the deployed server
does not is a silent no-match, not an error message.

The game is a **sibling of `/meridian/`** on the shared Apache host. That is not
cosmetic: Meridian's launcher resolves each game as a relative path under its own
origin, so the directory name `RotEA26` and the `<base href="/RotEA26/">` the
deploy stamps in have to agree with `games.json` in the meridian repo.

## Step 0 — check the quota FIRST (manual, every time the payload grows)

The published site is **~322 MB in ~638 files** (Content alone is ~294 MB of
textures, music and VO). That is big enough that the hosting plan's disk quota is
a real gate, and it is the one thing the tooling cannot reliably answer for you:
`deploy_web.py` asks the server over the OpenSSH `statvfs` extension, but shared
hosts routinely refuse it or report the whole underlying filesystem rather than
the account's quota. When it cannot tell, it says so and continues.

```sh
python tools/deploy_web.py --build-only   # payload size + per-directory breakdown, no network
python tools/deploy_web.py --list         # remote listing + whatever free space it can see
```

Then confirm the real number in the hosting control panel. An upload that runs
out of quota half way leaves a **partly-updated site**, which is worse than not
deploying: the manifest is written last precisely so a half-finished upload is
retried rather than trusted, but the live site is still mixed until you finish.

## Release flow

Run these in order, from a checkout of `main` that is up to date.

```sh
python tools/deploy_web.py --selftest     # the build-hash recipe still matches its pinned value
python tools/deploy_web.py --build-only   # what would ship, and how big
python tools/deploy_web.py --dry-run      # + what the host already has, and what would be deleted
python tools/deploy_web.py                # the real upload
python tools/check_deploy.py --hash <the hash the deploy printed>
```

`deploy_web.py` publishes from a **throwaway detached checkout**, not your
working tree — `dotnet publish` copies `wwwroot/` as a directory and cannot tell a
committed asset from a stray one, so a local `Content/sfx/peaks/` or a
half-rebuilt texture would otherwise ship. Nothing you have uncommitted can reach
the live site, and `--ref` picks what does.

## What the deploy does to the build

Both edits are inherited from the retired `.github/workflows/deploy.yml`:

- **`<base href="/" />` -> `/RotEA26/`.** The dev build keeps `/` so `dotnet run`
  works at a domain root; never hard-code the deployed value in `index.html`.
- **`window.eaBuildHash = 'dev'` -> a real fingerprint** — sha256 over
  `_framework/blazor.boot.json` (which fingerprints every assembly) plus every
  file under `Content/` (levels and grids change the simulation too), truncated to
  16 hex chars.

**The build hash is the co-op compatibility key.** `NetSession`'s hello handshake
compares it and rejects a mismatched peer with an "update required" notice, and
the game browser filters the room list on it. Change the recipe and you split the
player base across the boundary; `--selftest` exists to make that impossible to do
by accident, and it is the only surviving record of the recipe now that the CI
workflow is gone. A `dev` hash on a live site is a deploy that did not stamp — it
also leaves the frame-profiler HUD visible, which keys off the same value.

### The hash identifies a PUBLISH, not a commit

**Building the same commit twice gives two different hashes.** Measured: three
`--build-only` runs at one commit produced `6aed4f725ec09cf0`, `94482a7ccfe15a71`
and `227e7f3902b2eef0` across an identical 638-file / 321.9 MB payload.

The cause is not `Content/` — that is copied verbatim from the checkout and
diffs byte-identical every time. It is `_framework/blazor.boot.json`, which
carries a sha256 integrity hash for each of the 71 assemblies, and a normal
`dotnet publish` is not byte-reproducible (the repo's own
`tools/verify_il_identical.py` has to pass `-p:Deterministic=true` explicitly to
get a stable assembly, which is the same fact from the other side).

This is inherited from the Pages workflow verbatim, not new, and it is *correct*
for the purpose — the check exists to prove two peers are running the identical
published bits, and a rebuild genuinely is different bits. But it has three
consequences worth knowing before you rely on it:

- **Never recompute a hash to check a deploy.** `check_deploy.py --hash` must be
  given the value the deploy itself printed. A locally recomputed one will not
  match, and that mismatch means nothing.
- **A rollback does not restore the old hash.** `--ref <old sha>` builds fresh, so
  it gets a new fingerprint — anyone still on the previously published build
  cannot co-op with the rolled-back one. Rolling back is a re-publish with all
  the compatibility consequences of any other publish, not an undo.
- **Every deploy re-uploads all of `_framework/`** (~17.8 MB), because every
  assembly's bytes change. `Content/` (~294 MB, 91% of the payload) is stable
  across builds, so the incremental manifest still saves most of the transfer —
  a no-op redeploy moves ~18 MB, not ~322 MB.

Two things the old Pages workflow did that this one does not, both Pages-specific:
`.nojekyll` (Apache has no Jekyll to stop eating `_framework/`) and copying
`index.html` to `404.html` (a Pages SPA fallback; the game is a single page and
`harness.html` is a real file).

## Incremental uploads and the manifest

The deploy writes `.deploy-manifest.json` at the site root: `{path: sha256}` for
everything it uploaded, plus the build hash. The next deploy reads it back and
uploads only files whose hash changed.

This also handles **orphans**, which SFTP does not do for you the way a Pages
artifact swap did: `_framework/` filenames carry content fingerprints, so every
build leaves the previous build's assemblies behind. Files listed in the previous
manifest and absent from the new one are deleted; anything the manifest never
claimed is left strictly alone, so nothing else on the host is at risk.
`--no-prune` keeps them, `--force-all` ignores the manifest entirely.

With no manifest on the host (the first deploy) the comparison falls back to file
**size**. Do not "improve" that to mtime: publishing from a fresh checkout stamps
every file with today's date, so an mtime rule would re-upload all 322 MB every
time while looking like it was being clever.

## Post-deploy smoke

`check_deploy.py` covers what only a real host can fail — reachability, the
stamped base href and build hash, `blazor.boot.json`, four lowercase `Content/`
paths **plus a deliberately wrong-cased one that must 404** (a forgiving host
would make the other four meaningless), and the signaling server's `/health`.
It is stdlib-only and takes `--url`, so it works against any target.

Then, in real Chrome, the things no HTTP check can see:

- the game boots with **zero console errors**;
- **Online Co-op -> Join Online Game** lists a real game, with both windows on the
  **same published build** (browse filters on build hash, so a stale cached tab
  silently sees an empty list rather than an error);
- **Host / Join by code** still pairs;
- saves round-trip (IndexedDB + the trimmed XmlSerializer types — trimming
  breakage only ever shows at runtime in the browser).

## Rolling back

There is no server-side history: the manifest describes only what is currently
live. To roll back, re-deploy the older commit — `--ref <sha>` publishes it, and
the manifest diff makes it a normal incremental upload rather than a full
re-push.

**A rollback is a new publish, not an undo** (see "The hash identifies a PUBLISH"
above): the rebuilt old commit gets a *new* build hash, so it is co-op
incompatible with the build players were running a minute ago, exactly as a
roll-forward would be. Record the hash the rollback prints — that, not the
commit sha, is what identifies the build in the wild.

## Credentials

`SFTP_HOST` / `SFTP_USER` / `SFTP_PASS` / `SFTP_PATH` in the repo-root `.env`
(gitignored, never printed — the tool reports only the password's length). The
same file is the credential source for the **meridian** repo's `tools/deploy.py`,
which defaults to this exact path. A `.claude/worktrees/wt<k>` worktree has no
`.env` of its own; the tool falls back to the root checkout's copy.
