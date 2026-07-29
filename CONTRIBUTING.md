# Contributing — RotEA26 specifics

**Follow the generic runbook at `~/.claude/CONTRIBUTING.md`
(`C:\Users\coami\.claude\CONTRIBUTING.md`) end to end** (quick-ship flow, tracker doc, worktree rules, Phases 1–7, merge-conflict rules).
This file is only what's specific to this repo: the substitutions for the global doc's
placeholders, plus the local verification gate and deploy behaviour. Where they differ, this
file wins.

## Substitutions

| Global placeholder | Here |
|---|---|
| Default branch | `main` |
| Trello backend / board | **always** `--backend local --board 10989a3d` (offline file-backed board, NOT trello.com) |
| Columns (list ids) | `Backlog` `79158996` → `In Progress` `3b43cba3` → `Done` `9c204b80` |
| Branch prefixes | `fix/` `feature/` `refactor/` `docs/` (note: `feature/`, not `feat/`) |
| Tracker doc | `plans/tracker_<branch>.md` — **gitignored, never committed** (see "Tracker docs"); `plans/plan.md` is the archived staged plan and STAYS |
| Worktree layout | fixed slots `.claude/worktrees/wt1`..`wt8` (gitignored) |
| Plan detail | cards are pointers; long-form per-stage detail is in the archived `plans/plan.md` |

Board commands, copy-paste ready:

```
trello --backend local --board 10989a3d grab --from 79158996 --to 3b43cba3   # claim top Backlog card
trello --backend local --board 10989a3d card ls 79158996                     # view Backlog
trello --backend local --board 10989a3d card move <card_id> <listId>
trello --backend local --board 10989a3d comment add <card_id> "<text>"       # real newlines, not \n
trello --backend local --board 10989a3d card add 79158996 "<title>" "<desc>" # follow-up card
trello --backend local --board 10989a3d search --partial "<keyword>"         # dedupe BEFORE filing
```

`--from`/`--to` are REQUIRED on this board (the CLI's `To Do`/`Doing` defaults don't exist here).
On the local backend `grab` is **truly atomic** (store lock) — the global doc's claim-comment
handshake is never needed here.

## Worktrees & dev servers

- **Picking a slot: check the DIRECTORY, not just `git worktree list`.** A worktree whose
  `git worktree remove` hit the Windows permission-denied path is unregistered but still on disk,
  so the slot looks free and the `add` fails anyway — **after git has already created the
  branch** (verified: the failed add leaves the new branch behind). Check both, from the ROOT
  checkout, and take the lowest `wt<k>` in neither:

  ```
  git worktree list          # registered slots
  ls .claude/worktrees       # actual dirs — may include unregistered leftovers
  ```

  If the `add` still fails, a parallel agent took the slot in that same instant (the documented
  race): **delete the branch git just created** before retrying on the next slot, or you leave a
  stray branch that reads as someone else's in-flight work. Use `git branch -d <branch>` — it has
  no commits of its OWN, so `-d` succeeds; if `-d` REFUSES, you have grabbed the wrong branch, so
  stop and look (never `-D` your way past that).

  A leftover dir holding only `bin/`/`obj/` is dead litter, but look before you delete — an
  unregistered dir can hold uncommitted source from a failed cleanup. If it is only build output:
  `powershell.exe -Command "Remove-Item -LiteralPath '<abs path>' -Recurse -Force"`, then
  `git worktree prune`, then take the slot.
- **A branch NAME collision on `git worktree add -b` can mean duplicate WORK.** Rule out the
  stray-branch litter above first (a doomed add, yours or another agent's). If the name is held
  by a branch you did not create, another session is (or was) doing your card — investigate
  before renaming past it.
- **Per-worktree bootstrap: none.** `bin/`/`obj/` regenerate on the first `dotnet build` (slow —
  WASM workload restore; expected). No `.env`, no package install.
- **Most cards need no dev server at all** — verify through `tools/headless/` (`eahl`) instead
  (see the Phase-5 gate below). It resolves `wwwroot` from its own build output, so a worktree
  build automatically reads *that worktree's* content with no port, no launch config and nothing
  to kill afterwards. The rest of this section applies once you escalate to a browser.
- **One dev PORT per slot: `5280 + k`** (wt1 = 5281 … wt8 = 5288; the root checkout is 5280).
  Slots **wt1/wt2 have provisioned launch configs** (`eaweb-wt1`/`eaweb-wt2` in
  `.claude/launch.json`) usable via `preview_start`; for wt3+ run the server yourself:
  ```
  dotnet run --project .claude/worktrees/wt<k>/web/DevServer -c Debug --urls http://localhost:528<k>
  ```
  Either way, **serve through the worktree's own `web/DevServer`** — never raw `dotnet run` on the
  WASM client (the stale-asset cache trap, see root `CLAUDE.md`). Verify by pointing Chrome at
  *your slot's* port; a bare `preview_start`/`eaweb` serves the ROOT checkout and you'd be driving
  the wrong code.
- **Never edit `.claude/launch.json`** — it's shared; concurrent per-card edits cause port
  collisions and lost-update races between parallel agents.
- **Kill your dev server before `git worktree remove`** — besides the Windows directory lock (see
  global doc), a stale server squats the slot's port and blocks the next agent who claims it.

## Tracker docs

`plans/tracker_<branch>.md` (the global runbook's durable per-card tracker) is **gitignored here —
never commit it.** Phase 6 step 8's "delete the tracker" was missed often enough to need repeated
sweep commits clearing strays off `main`; the ignore rule makes that hard to do by accident rather
than something to remember. Flatten the branch's `/` when naming it (`docs/foo` ->
`tracker_docs-foo.md`): a literal slash would make a *directory*, which the ignore rule has to
cover separately.

So it is local convenience only, not a handoff channel — anything a successor genuinely needs
goes in a **card comment**, the only durable shared surface here. It dies with the worktree; for a
card spanning sessions the ROOT checkout's `plans/` works too (same ignore rule, survives
`git worktree remove`), and that copy is yours to delete at ship.

The card's DESIGN doc (`plans/<name>.md`) is unaffected: still committed, still deleted in Phase 6.

## Verification gate (Phase 5)

**There are no unit tests — the gate is: clean Debug build + tool-driven visual verification +
zero console exceptions in real Chrome.** Don't ask the user to check manually; verify and share
proof.

- The *how* lives in root `CLAUDE.md` → "Verification — the rules": drive the purpose-built tool
  that isolates the change (sprite harness / scrub-showcase scene / isolation sim / slider panel —
  building one is part of the card if none exists), boot the real game only as the final smoke
  check, never time a live screenshot of anything that moves, verify in foreground Chrome via
  claude-in-chrome (not `preview_screenshot`), script input with `eaPress(...)`.
- **Drive that tool through the headless host FIRST — `tools/headless/` (`eahl`).** It is the
  default route for a card's verification work; a browser is what you escalate to, not what you
  start with. It runs the real game as a desktop exe with **no Chrome, no dev server and no
  visible window**, takes the **URL query verbatim** (so every harness/showcase/fast-boot flag
  works unchanged), and writes the PNG to disk:
  ```sh
  dotnet build tools/headless -c Debug
  tools/headless/bin/Debug/net8.0/eahl.exe --flags "?level=Level3&brainboss&invuln" --frames 400 --out shot.png
  tools/headless/bin/Debug/net8.0/eahl.exe --repl     # step / shot / eval / info / quit
  ```
  Practical consequences for a card: **no worktree dev server needed** for most verification (so
  no port-slot dance, and nothing to kill before `git worktree remove`); soaks run in the
  background at ~17x real time instead of needing a foregrounded tab; and `--script <file>` turns
  the check into a repeatable probe that exits non-zero on the first failure, which is worth
  committing alongside the card when the behaviour is worth re-checking later. Settle ~150 frames
  before any screenshot (the intro white fade). Details: `tools/headless/README.md`.
- **The gate itself still ends in real Chrome.** `eahl` proves the frame or the number; it runs
  desktop GL, not WASM/WebGL, so it cannot see a trimming break, an IndexedDB save failure, a
  WebGL-specific shader difference, an `index.html` JS-layer error, a real WebRTC problem, or a
  lowercase `content/` path that only 404s on a case-sensitive host. Finish with the foreground
  Chrome smoke check and zero console exceptions, as below.
- **A clean build does NOT mean it runs** — WASM runtime errors only appear in the browser
  console. Zero console exceptions is the bar.
- **Touched the csproj / anything reflection-loaded (save types, KNI factories)?** Do a LOCAL
  Release publish + saves round-trip in real Chrome **before pushing** — trimming breakage only
  shows at runtime (root `CLAUDE.md` → "Publish trimming").
- **Diff spot-check specials for this repo:** a stray lowercase `content/` path, an accidental
  `BlendState.AlphaBlend`, a re-run of the `Game/`-generating codegen, a hand-edited pipeline
  output.

## Ship & deploy (Phase 6)

- **Merging does NOT deploy.** `.github/workflows/deploy.yml` fires on `workflow_dispatch` only.
  When ready to ship: `rtk gh workflow run "Deploy to GitHub Pages"`, confirm green with
  `rtk gh run list`, and for anything content/path-sensitive **spot-check the LIVE URL**
  (https://coamithra.github.io/RotEA26/) — content paths are case-sensitive there, localhost
  isn't.
- **Docs are split** — when a change adds a convention/flag/gotcha, update the right file:
  root `CLAUDE.md` (workflow/cross-cutting), `web/EvilAliensWeb/CLAUDE.md` (game/engine features),
  `tools/CLAUDE.md` (asset pipelines).

## Filing a follow-up card (Phase 6 step 11)

**Search the board first — every time.** `grab` dedupes *cards*, not *work*: nothing stops two
reviews filing two cards for one task, and that has already cost a duplicated claim-and-research
cycle plus a session left holding a branch deleted underneath it.

```
trello --backend local --board 10989a3d search --partial "<keyword>"   # all lists, incl. Done
```

Search a distinctive noun from the work (a file, a symbol, a flag), not your card title's phrasing
— a duplicate filed off a different review will be worded differently. Matching is WHOLE-WORD
without `--partial` (`--substring` for mid-word), so an unprefixed search misses the near-miss
wordings you are hunting. Found one? Comment on it rather than filing a second; file new only if
the scope genuinely differs.

## Per-card-type routing

Read the matching doc section *before* designing (each has known gotchas):

| Card touches | Read first |
|---|---|
| Drawing / sprites / shaders / textures | root `CLAUDE.md` verification rules + `web/…/CLAUDE.md` (harness, straight alpha) + `tools/CLAUDE.md` (shaders, textures) |
| Fades / transitions / timed FX / feel | root `CLAUDE.md` "Verification" (scrub scenes, isolation sims) |
| Audio / music | `web/…/CLAUDE.md` "Audio runtime" + `tools/CLAUDE.md` "Audio" |
| Text / font | `web/…/CLAUDE.md` "Rendering / text" |
| Input / menus / overlays | `web/…/CLAUDE.md` "Input" (outside-`#app` pattern, `eaPress`) |
| Resolution / present / post-FX | `web/…/CLAUDE.md` "Architecture" + "Feel / post FX" |
| Saves / trim / hosting | root `CLAUDE.md` gotchas (trim, case-sensitivity) + `web/…/CLAUDE.md` (saves) |
| Generated `Game/` code | root `CLAUDE.md` gotchas (Xbox build, codegen) |
| Exit / "boss key" launcher | root `CLAUDE.md` "Related repos" — edit the separate `meridian` repo, not this tree |
