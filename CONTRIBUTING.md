# Contributing: Tackling a Trello Card

Step-by-step workflow for picking up and completing any card from the **local** Trello board
`RotEA26 — Evil Aliens Web Port` (board id `10989a3d`). The board is offline/file-backed (NOT
trello.com) — every command needs `--backend local --board 10989a3d`. Columns and their list ids:
**`Backlog` `79158996`** · **`In Progress` `3b43cba3`** · **`Done` `9c204b80`**.

Global prefs still apply: `rtk`-prefix git/gh commands, `python` (not `python3`), and the
`edit_unicode.py` helper for any edit touching `→`/`—`/curly-quotes.

---

## Quick ship (no card / small change)

Not every change is a Trello card. For a quick fix or doc tweak that doesn't warrant the full
runbook below, the default ship flow is **PR + auto self-merge** off `main`:

```
rtk git checkout -b <prefix>/<short-name>      # off main
rtk git add <files> && rtk git commit -m "..." # only the files you touched
rtk git push -u origin <branch>
rtk gh pr create --fill                         # PR record + URL, no clicking
rtk gh pr merge --merge                         # self-merge (see note); use --merge, not --squash
rtk git checkout main && rtk git pull origin main
```

**No approval needed.** `main` on `Coamithra/RotEA26` is unprotected (solo repo), so GitHub
disabling "Approve" on your own PR is irrelevant — a required review only applies under a
branch-protection rule, and this repo has none. Don't stop to ask the user to approve. (If the user
says "just merge / direct", skip the PR and push straight to `main`.)

**Every push to `main` auto-deploys.** `.github/workflows/deploy.yml` runs `dotnet publish -c Release`
in CI and publishes to GitHub Pages (https://coamithra.github.io/RotEA26/). So merging to `main` =
shipping to production. There are no unit tests gating this — **your visual + console verification
(Phase 5) IS the gate.** If the change touches publish/trim config, do the local Release check in
Phase 5 *before* you push, because trimming breakage only surfaces at runtime in the browser.

---

## Before You Start: Create a Tracker Doc

**This is mandatory.** Before doing anything else, create a file `plans/tracker_<branch>.md` (create
the `plans/` directory if needed) with every step from this runbook as a checkbox list. Example:

```markdown
# Tracker: fix/some-bug

## Phase 1: Pick Up the Card
- [ ] Claim the top card (move Backlog -> In Progress), before anything else
- [ ] Pull latest main
- [ ] Read the card (description, linked plan stage)
- [ ] Create worktree/branch

## Phase 2: Research
- [ ] Read the referenced code
- [ ] Trace the call/draw chain
...
```

Check off each step as you complete it — it's your source of truth if context is lost. Delete the
tracker file after the card ships. (`plans/plan.md` is the archived staged plan and stays; the
tracker is per-card scratch.)

---

## Worktree Quick Reference (optional — usually single-dev)

This is a solo project with one dev server, so you'll often just work on a branch in the root
checkout. Reach for a worktree only when you need to keep `main` clean while iterating, or when two
agents run in parallel.

| Command | What it does |
|---------|-------------|
| `rtk git worktree add .claude/worktrees/<branch> -b <branch> main` | Worktree + branch off main |
| `rtk git worktree list` | Show active worktrees |
| `rtk git worktree remove .claude/worktrees/<branch>` | Remove a worktree |
| `rtk git worktree prune` | Clean stale references |

**Key rules:**
- Put worktrees under `.claude/worktrees/` — it's gitignored at the repo root. Keep the root
  checkout on `main`; never switch it to a feature branch.
- `bin/`/`obj/` are gitignored and regenerate on first `dotnet build` in the new worktree — no
  install step (unlike an npm project). The first build is slow (WASM workload restore); that's
  expected.
- **Only ONE dev port is provisioned** (`eaweb` = `http://localhost:5280` in `.claude/launch.json`).
  If you run a second server from a worktree, pass a different port: `dotnet run -c Debug --urls
  http://localhost:5281`. **Never edit `.claude/launch.json`** — the preview tooling keys off it.
- **Kill your dev server before `git worktree remove`.** On Windows a running server (or the Bash
  tool's own cwd sitting inside the worktree) holds the directory lock and the remove fails with
  "Permission denied" — `cd` back to the repo root, kill the server, then retry. `git worktree
  remove --force` still unregisters it from git even when the physical folder can't be deleted.

---

## Phase 1: Pick Up the Card

> **The board is local and single-user — claiming is just a move.** Unlike a shared remote board,
> there's no claim-comment race to win here. View the top of Backlog and move it to In Progress:
>
> ```
> trello --backend local --board 10989a3d card ls 79158996      # Backlog (top card = next)
> trello --backend local --board 10989a3d card move <card_id> 3b43cba3   # -> In Progress
> ```
>
> Do the move *before* reading the card or pulling main, so the board reflects what's being worked.
> If two agents are genuinely running in tandem, fall back to the comment handshake: post
> `trello --backend local --board 10989a3d comment add <card_id> "I am doing this now — claim <id>"`,
> wait ~10–30s, re-read `--json comment ls <card_id>`, and the earliest claim wins (back off and take
> the next card otherwise). For solo work, skip the handshake.

1. **Claim the top Backlog card (do this first)** — move it to In Progress, as above.
2. **Pull latest main** — `rtk git pull origin main` so you start from the newest code.
3. **Read the card** — Read the card description, then any linked detail in the archived
   `plans/plan.md` (the per-stage source of truth; the card is a pointer/summary). Keep the card
   description in sync if the work changes its scope.
4. **Create a branch (or worktree)** — Branch off `main` with a descriptive prefix:
    - Bugs: `fix/<short-name>` (e.g. `fix/premult-alpha-fades`)
    - Features: `feature/<short-name>` (e.g. `feature/metal-font-sheen`)
    - Refactoring: `refactor/<short-name>`
    - Docs / plans only: `docs/<short-name>`
    ```
    rtk git checkout -b <branch> main                  # in the root checkout
    # OR, for an isolated worktree:
    rtk git worktree add .claude/worktrees/<branch> -b <branch> main && cd .claude/worktrees/<branch>
    rtk git push -u origin <branch>
    ```

## Phase 2: Research

Dig into the problem before proposing solutions. Use `/research` for anything needing external
context (KNI/MonoGame API quirks, XNA 3.x→4.0 differences, Blazor WASM/WebGL behaviour, the original
XNA `.fx`/XACT formats, browser GL/ANGLE→D3D11 texture rules).

5. **Read the referenced code** — Cards and `plans/plan.md` cite specific files. Read them —
   descriptions drift. The ported game lives in `web/EvilAliensWeb/Game/`; the Xbox/XNA shims in
   `web/EvilAliensWeb/Compat/`; the host page + JS glue in `web/EvilAliensWeb/wwwroot/`;
   decompiled reference (read-only) in `src_decompiled/`; asset-deriving scripts in `tools/`.
6. **Trace the call / draw chain** — For bugs, trace how the code is invoked. For a *drawing* bug,
   trace the path through `SpriteBatchWrapper` → `RenderScale` → blend mapping → bloom/gamma. For a
   feature, trace the existing system it plugs into.
7. **Identify the blast radius** — Does it touch the unified presenter / `RenderScale` (Stage 10)?
   The blend mapping (straight alpha → `NonPremultiplied`)? A `tools/*` asset pipeline
   (shaders / audio / font / textures)? Content paths (**case-sensitive on the live Linux host** —
   capital `Content/`, lowercase under it)? Publish/trim config in the csproj? Each has a known
   gotcha documented in `CLAUDE.md` — re-read the relevant bullet before changing it.
8. **Research unknowns** — `/research` for KNI internals, MGFX shader compilation, WebAudio loop
   points, StbImageSharp decode cost, DXT/`texconv` block-size rules, etc.
9. **Summarize findings** — Brief writeup: root cause (bugs), design options (features), or risk
   areas (refactors). Feeds the design phase.

## Phase 3: Design

10. **Draft the approach** — Update or add a note under `plans/` (or in the tracker). Include:
    - **Context**: what the card is about and why it matters.
    - **Design**: file-by-file changes; whether `Game/` or `Compat/` is the right home; any
      `tools/*` source (`.fx`, audio banks, font sheets, `textures.config`) that must be re-run.
    - **Verification**: which URL/flag proves it (a `?harness=<Obj>` view, a `?level=<Name>` boot,
      a Release publish round-trip) — see Phase 5.
    - **Out of scope**: what you're explicitly *not* doing.
11. **Check for reusable patterns** — Prefer existing conventions: the debug-flag seam
    (`Compat/DebugFlags.cs`), the sprite harness (`Compat/HarnessRegistry.cs`), `eaPress`/`eaHold`
    automation input, the `DrawStringScaled` font path, the outside-`#app` pattern for new HUD/overlay
    buttons. Don't reinvent a shim that already exists in `Compat/`.
12. **Align with the user** — Present the plan, get approval before writing code.

## Phase 4: Implement

13. **Make the changes** — Edit files per the approved plan. Follow project conventions:
    - **C# / .NET 8 / KNI** (engine = `nkast.Xna.Framework.*`, API is XNA **4.0**). The game was
      XNA **3.x** — mind the gap (`SpriteBatch.Begin(effect)`, not `effect.Begin()`; `SpriteBlendMode`
      maps to `BlendState`).
    - **`Game/` is GENERATED** from `src_decompiled/` by `tools/*.py`. **Edit `Game/` directly; do
      NOT re-run those scripts** — they'd clobber your hand edits by regenerating from scratch.
    - **Asset pipelines ARE meant to be re-run** after you change their *source*: shaders
      (`tools/shaders/build_shaders.py` after a `.fx`), audio (`tools/audio/build_audio.py` after a
      bank/render), font (`tools/font/build_revenge_font.py` after a sheet), textures
      (`tools/textures/build_textures.py` after a source PNG / `textures.config`). Don't hand-edit
      their outputs (`.mgfxo`, `.wav`/`.ogg`, `menufont.fnt(.png)`, `.dds`/`.rtex`).
    - **Straight (non-premultiplied) alpha** — use `BlendState.NonPremultiplied`, never
      `BlendState.AlphaBlend` (KNI's premultiplied trap). Straight tints like `new Color(1,1,1,a)`
      are correct as written; don't premultiply tints or exports.
    - **Content requests use a capital `Content/` root, lowercase under it** — case-sensitive on the
      live host. Never reintroduce a lowercase `content/` path.
    - **Culture-invariant** (`InvariantGlobalization=true`) — don't add culture-dependent
      parse/format, even in Debug.
    - **Comments**: default to none; add only when the *why* is non-obvious. Match the surrounding
      code's density and idiom.
14. **Document new conventions** — Update `CLAUDE.md` if the change introduces a new shim, debug
    flag, harness entry, asset-pipeline step, or modifies a documented contract. `CLAUDE.md` is the
    source of truth for *how to work in the repo*.

## Phase 5: Verify

There are **no unit tests** — verification is build-clean + visual + console-error-free. Don't ask
the user to check manually; verify and share proof.

15. **Build clean** — `cd web/EvilAliensWeb && dotnet build -c Debug` must succeed. **A clean build
    does NOT mean it runs** — WASM runtime errors only appear in the browser console.
16. **Run and look** — `dotnet run -c Debug --urls http://localhost:5280`, then verify in a **real
    foreground Chrome tab via the claude-in-chrome MCP** (not `preview_screenshot` — the built-in
    renderer wedges when its tab is backgrounded and the rAF loop pauses). Flow: `preview_start` to
    serve → in Chrome `navigate` to `http://localhost:5280` → `wait` ~10s for WASM → screenshot +
    `read_console_messages`. **Zero console exceptions is the bar.**
    - **Drawing change?** Use the **sprite harness** — don't chase a moving enemy.
      `…:5280/?harness=<Obj>&frame=<n>` boots frozen onto a space background, drawn by the real
      pipeline, so a screenshot is pixel-reliable. Picker at `wwwroot/harness.html`.
    - **Booting deep into the game?** Use the debug flags: `?menu`, `?level=<Name>`, `?invuln`,
      `?unlockall`, `?noattract` (combine with `&`).
    - **Graphics/texture change? DO AN AGGRESSIVE CACHE BUST** — DevTools → Network → "Disable
      cache" then reload, or right-click reload → "Empty Cache and Hard Reload". A plain (even
      Ctrl+Shift+R) reload serves the STALE asset because textures load late, after the
      hard-reload bypass window closes. Symptom: an edit "doesn't take effect" or draws at the
      wrong size.
    - **For scripted input** use `eaPress('Enter')` / `eaPress('Left', 30)` / `eaHold(key, down)` —
      synthetic `KeyboardEvent`s do NOT work with KNI's WASM keyboard interop.
17. **Trim / publish changes — verify a LOCAL Release publish before pushing.** If you touched the
    csproj (`PublishTrimmed`/`TrimMode`/`InvariantGlobalization`/`TrimmerRootAssembly`) or anything
    reflection-loaded (save types, KNI factories): `dotnet publish -c Release`, serve the published
    `wwwroot` at localhost root, open in real Chrome, and confirm it boots AND saves round-trip
    (settings/unlockables persist across reload). Trimming breakage only shows at runtime — a green
    build is not enough. (`TrimMode=partial`, not full — full white-screens.)
18. **Spot-check the diff** — Read it once more for typos, a stray lowercase `content/`, an
    accidental `BlendState.AlphaBlend`, a re-run of a `Game/`-generating script, dead code.
19. **Flag what needs manual testing** — Leave a note for anything you couldn't fully verify (e.g.
    "verify on the LIVE Pages URL, not just locally — content paths are case-sensitive there",
    "needs a real click to trigger fullscreen; automation can't").

## Phase 6: Review & Ship

20. **Commit** — Descriptive message in the project's style (imperative, single-line subject; body
    explains *why*). `rtk git add <files> && rtk git commit -m "..."` (only files you touched), then
    `rtk git push` to the feature branch.
21. **Peer review** — Run `/review` (spawns a fresh agent against the branch diff vs `main` with no
    prior context). Fix every finding before proceeding — even minor ones — unless a fix is a major
    undertaking, in which case track it as a follow-up card.
22. **Pull main into the branch** — `rtk git pull origin main` to pick up anything that landed.
    Resolve conflicts using the rules below.

### Merge Conflict Rules

22.1. **Default to main's version.** If a conflict is in code you didn't intentionally change, accept
main's side — someone else fixed a bug or added a feature; don't silently revert it.
22.2. **Assume incoming changes are important.** Treat every conflict as "main has a critical fix"
until you've read the diff and confirmed otherwise.
22.3. **Only keep your side for lines you specifically wrote.** If you and main both changed a
function, read both, merge surgically — keep their fix, layer your change on top.
22.4. **If the merge is messy, restart from main.** A clean re-apply of your change beats a botched
merge.
22.5. **Re-read the final result.** After resolving, read every conflicted file in full — don't just
trust the markers.

23. **Re-build (and re-verify if behaviour changed)** — `dotnet build -c Debug` after the merge; if
    the merge touched runtime behaviour, re-do the relevant Phase 5 check.
24. **Return to the root checkout** — `cd` back to the repo root (where `main` lives) for the
    remaining steps.
25. **Open a PR and self-merge** — `rtk gh pr create --fill` then `rtk gh pr merge --merge` (real
    merge commit, not `--squash`, so the branch's commits stay reachable and step 26's
    `git branch -d` works), then `rtk git pull origin main` to fast-forward the root checkout.
    **No approval needed** (unprotected solo repo). **The merge auto-deploys to GitHub Pages** —
    confirm the Pages run goes green (`rtk gh run list`) and, for anything content/path-sensitive,
    **spot-check the LIVE URL** (https://coamithra.github.io/RotEA26/), not just localhost.
26. **Clean up the worktree and branch** — kill your dev server FIRST (it holds the directory lock):
    ```
    rtk git worktree remove .claude/worktrees/<branch>   # if you used one
    rtk git worktree prune
    rtk git branch -d <branch>
    rtk git push origin --delete <branch>
    ```
27. **Delete the tracker file** — `rtk git rm plans/tracker_<branch>.md && rtk git commit -m "..." &&
    rtk git push`. (Leave `plans/plan.md` — it's the archived staged plan, not per-card scratch.)
28. **Move the card to Done** — `trello --backend local --board 10989a3d card move <card_id> 9c204b80`.
29. **Comment on the card** — `trello --backend local --board 10989a3d comment add <card_id>
    "<summary>"`. Include: what changed, which files, what it fixes/adds, the commit hash(es), and
    what needs manual testing. Real newlines, not `\n` escapes.
30. **Create follow-up cards** — If review/implementation/testing surfaced out-of-scope issues,
    add Backlog cards (`trello --backend local --board 10989a3d card add 79158996 "<title>"
    "<desc>"`). Reference the original so there's a trail — don't let follow-up work disappear into
    commit messages.
31. **Write an overview for the user** — Final step: a concise handoff — what changed (the
    user-facing behaviour delta, not a file dump), which files were touched, anything still needing
    manual testing or follow-up, the commit hash(es), the merged branch, and that it's live on
    Pages. This is how the user picks the session up cold.

## Phase 7: Clean up

Stop any dev servers you started. :)

---

## Quick Reference: Card Categories

| Category | Key concerns |
|----------|-------------|
| **Drawing / sprites / shaders** | Verify with the **sprite harness** (`?harness=<Obj>&frame=<n>`), never a screenshot of a moving enemy. Straight alpha → `NonPremultiplied` (not `AlphaBlend`). Re-run `tools/shaders/build_shaders.py` after a `.fx`; don't hand-edit `.mgfxo`. Aggressive cache-bust for texture edits. |
| **Audio** | Replace, don't port XACT. Re-run `tools/audio/build_audio.py` after changing banks/renders; SFX/speech on KNI `SoundEffect`, music via the WebAudio `eaMusic` layer for loop points. Don't hand-edit the `.wav`/`.ogg`/`music.json` outputs. |
| **Font / text** | `menufont` atlas is **3× supersampled** — never route it through stock `DrawString`; use `SpriteBatchWrapper.DrawStringScaled`. Re-run `tools/font/build_revenge_font.py` after a sheet; per-glyph tweaks live in `overrides.json`. |
| **Textures / load stutter** | PNG decode (StbImageSharp, main thread) is the hitch. Precompile hot sprites with `tools/textures/build_textures.py` → `.dds`/`.rtex` (loader prefers `.dds` → `.rtex` → `.png`). DXT dims must be a mult-of-4 or ANGLE→D3D11 draws black. |
| **Resolution / present** | Stage-10 unified presenter — one offscreen `sceneTarget` sized to the window's 4:3 letterbox, scaled to `RenderScale`. Don't re-pin `PreferredBackBuffer`. Full-screen overlays use `(0,0,800,600)` design coords. |
| **Saves / persistence** | `StorageStub.PersistentSave` mirrors to localStorage via `SaveInterop`/`eaSave`. Save types are reflection-loaded — **any csproj trim change must be Release-published and round-trip-tested locally** before pushing. |
| **Content paths / hosting** | Case-sensitive on the live Linux host: capital `Content/`, lowercase under it. Verify new assets/scenes on the LIVE Pages URL, not just locally. CI keeps `<base href="/">` for dev and flips it to `/RotEA26/`. |
| **Generated `Game/` code** | `Game/` is derived by `tools/*.py` from `src_decompiled/`. Edit `Game/` directly; **never re-run those scripts** against it. `#if WINDOWS` code was stripped from the Xbox build — recreate PC behaviour, don't hunt for it. |
| **Meridian decoy / Exit** | The "boss key" launcher is the SEPARATE private `meridian` repo, not this tree. This repo keeps only the tiny `eaQuit` handoff in `wwwroot/index.html`. Edit the decoy in the meridian repo. |
