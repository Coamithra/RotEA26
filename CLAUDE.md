# CLAUDE.md — Revenge of the Evil Aliens (web port)

Porting a recovered 2008 **XBLIG** (XNA 3.x, C#) to run in the browser via **KNI**
(a MonoGame fork with a Blazor WebAssembly / WebGL backend). Output = a static site,
**deployed publicly at https://coamithra.github.io/RotEA26/**.

This file is how to *work* in the repo. Detail lives next to the code:

| Doc | What |
|---|---|
| [`web/EvilAliensWeb/CLAUDE.md`](web/EvilAliensWeb/CLAUDE.md) | game/engine architecture + per-feature notes (render path, input, saves, debug flags, webcam, walls, bosses, ...) |
| [`tools/CLAUDE.md`](tools/CLAUDE.md) | the offline asset pipelines (audio, shaders, textures, font, backgrounds, ...) |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | the step-by-step card → worktree → PR runbook |
| `plans/plan.md` | historical artifact — the archived staged plan (full per-stage detail) |

## Project tracking (Trello — local backend)

A **local** (offline, file-backed) Trello board is the live tracker. It is NOT on trello.com — it
lives in the `trello` CLI's local store at `C:\Users\coami\Dropbox\Programming\FakeTrelloData`.

- **Board:** `RotEA26 — Evil Aliens Web Port` · id `10989a3d`.
- **Always pass `--backend local --board 10989a3d`** (the CLI's default backend is `trello`, and
  the active board is a *different* one). e.g. `trello --backend local --board 10989a3d board`,
  `... list ls`, `... card ls <listId>`. Browse visually with `... serve`.
- **Columns (list ids):** `Backlog` `79158996` · `In Progress` `3b43cba3` · `Done` `9c204b80`.
- Cards = the plan's stages + follow-up tasks; **check the board for live status** (don't trust a
  stale summary here). Notable: the original Stages 1–10, 12–15 are done; Stage 11 (online co-op)
  is designed — see `plans/stage11-online-coop.md` (distributed-authority state replication over
  P2P WebRTC, NOT lockstep) — with implementation split into sequential cards `Stage 11.1`–`11.5`.
  When a card's status changes, `card move <id> <listId>` it and keep the description in sync.
- **When picking up a card/task, FOLLOW [`CONTRIBUTING.md`](CONTRIBUTING.md)**: claim the card, a
  per-card worktree (mandatory; slot `wt1`..`wt8`, dev server on port `528<k>`), research → design
  → implement, the visual+console verification gate (no unit tests here), PR self-merge (deploy to
  Pages is MANUAL — `workflow_dispatch`, not on push), and the card-close paperwork.

## Build / run

```sh
dotnet build web/EvilAliensWeb -c Debug
dotnet run --project web/DevServer -c Debug --urls http://localhost:5280   # then open the URL
```

- **Serve via `web/DevServer`, NOT `dotnet run` on the WASM client.** The DevServer stamps
  `Cache-Control: no-store` on every response, so nothing (index.html, wwwroot JS, `Content/**`
  textures/shaders/`.dat`/json) is ever served stale — the stock `blazor-devserver` leaves those
  with no cache header and the browser heuristically caches them (the "I keep hitting long-fixed
  bugs / a regenerated asset doesn't take effect" trap; `_framework/*` C# was never the culprit).
  It also force-loads static web assets and serves the custom `.mgfxo`/`.dds`/`.rtex`/`.dat`
  extensions. The `eaweb` (5280), `eaweb-fresh` (5290) and worktree (`eaweb-wtN`) launch configs
  in `.claude/launch.json` all run the DevServer, so `preview_start` is cache-proof by default.
  If you serve any other way the cache trap is back — bust with DevTools → Network → "Disable
  cache", or `fetch('Content/...',{cache:'reload'})` (a plain reload, even Ctrl+Shift+R, does NOT
  reliably refetch late-loading content). Production self-heals via ETag revalidation.

## Verification — the rules

- **Booting the actual game to test a change is almost NEVER the right call.** The running game is
  the worst test rig here — slow to reach the code under test, everything moves, the moment of
  interest can't be timed. Nearly every kind of change has (or deserves) a purpose-built
  verification tool behind a URL flag (shipped builds unchanged):
  - how something DRAWS → the **sprite harness** (`?harness=<Obj>&frame=<n>` — one object, frozen,
    real pipeline, reliable screenshots; picker `wwwroot/harness.html`; details + how to add an
    object in one registry line: web CLAUDE.md);
  - a time-varying VISUAL (fade, transition, death FX, glint, pulse) → a **scrub/showcase scene or
    freeze flag** (`?textshot` / `?lazershot` / `?spiderphase=` / `?wcmothershipfreeze=` pattern)
    that parks the effect at any chosen phase, then screenshot at leisure;
  - BEHAVIOUR / timing / feel over time → an **isolation sim**: stub the game, tick the object's
    `Update`/pure core in a plain loop, read the DATA (or plot it), not a frame — the
    `ApplyLifecycle`/`HarnessApplyPhase` pattern (see `Blast`, `Spider`, `tools/sim/`);
  - tuning values → the matching **live slider panel** (`?wctune`, `?lazershot`, `eaWalls`, ...);
  - a change that should alter NOTHING (rename, reformat, decompiler-artifact cleanup) → the
    **IL-identity oracle**, `python tools/verify_il_identical.py` — see below.
  If no tool covers the change, BUILD one — that is part of the fix, not extra scope. Boot the real
  game only for (a) the FINAL smoke check (boots, change in, zero console errors) after a tool
  already proved it, or (b) boot/menu/scene-flow changes themselves — and even then use fast-boot
  flags to land next to the thing under test.
- **A no-op refactor is PROVEN, not spot-checked — `python tools/verify_il_identical.py`.** Local
  variable names live only in the PDB, so a build with `-p:DebugType=none -p:Deterministic=true`
  must produce a **byte-identical `EvilAliensWeb.dll`** for any change that is genuinely cosmetic.
  Run it bare while editing (uncommitted work vs `HEAD`) and `--ref main` once committed (the
  whole branch vs its **merge-base** — not the tip, since worktrees merge into `main`
  concurrently here); it refuses the vacuous clean-tree-vs-`HEAD` case rather than returning a
  meaningless green tick. The hash is path-independent, so you can baseline at any point, even
  after you have started editing. Sound AND sensitive: 19 locals renamed across a 160-line method
  hashed identically, while flipping one constant `128`→`129` did not. It covers the WHOLE assembly, so a
  stray edit in an unrelated file is caught too. Use it for renames, reformatting and decompiler-
  artifact cleanup — a harness or screenshot cannot prove what the hash proves, so don't build one
  for this class of change. It does **not** judge whether a new name is a *good* name; a
  misleading-but-compiling rename hashes identically, so name quality stays a human review job.
- **Never verify motion with timed live screenshots.** A raw screenshot is only valid for STATIC
  appearance; anything time-varying needs a parked/scrubbed frame or a data sim. If you genuinely
  must see it live, build a break/pause (`DebugFlags` seam) that freezes at the moment of interest.
- **A clean `dotnet build` does NOT mean it runs.** WASM runtime errors only appear in the
  **browser console** — always verify visually AND read the console.
- **Verify in real Chrome (`claude-in-chrome` MCP), not `preview_screenshot`** — the built-in
  preview wedges when its tab is backgrounded (the rAF loop pauses, so it never paints). Flow:
  `preview_start` → Chrome `navigate` to `http://localhost:5280` → `wait` ~10s for WASM →
  screenshot + `read_console_messages`. Automated input: `eaPress(...)` from the console, not
  synthetic key events (web CLAUDE.md → Input).

## Debug boot shortcuts (URL flags)

Parsed once at boot in `Compat/DebugFlags.cs`; no query = normal boot. Combine with `&`:

- `?menu` (straight to main menu) · `?noattract` (no idle attract demo) · `?level=<Name>` (boot
  into a level; a `Levels` value, e.g. `Level1`/`ClassicAliens`/`WebcamAliens`) · `?invuln` (aka
  `?god`) · `?unlockall` · `?skipsplash` / `?autostart`.
- **Level fast-boots** (replace a level's event list with one fight/section): `?spiderboss` ·
  `?spiders` · `?wallsonly` · `?brainboss` (bypasses the Hard+ gate). Pair with `?invuln`.
  e.g. `…:5280/?level=Level3&brainboss&invuln`.
- **Online game browser** (card 2001fbd8): `?gamebrowser` boots straight to the "Join Online
  Game" carousel with injected fake entries (no server); `?netjip` pairs with `?level=<Name>`
  so a debug-booted host still LISTS its game for the two-window join-in-progress test.
- **Host kick / block** (card 0b8a300b): `?netkickshot` (pair with `?level=<Name>`) parks the
  host's remote-pause kick menu over a live level with no peer, for a screenshot;
  `?netfakepeer=<s>` overrides this tab's peer-identity token and is **required** for any
  two-tab kick+block test (both dev tabs share one `localStorage`, so they otherwise present
  the same id and blocking the joiner blocks yourself). Console: `eaKickTest()`.
- **Local co-op + online co-op together** (card 4d904410): `?netlocal=<1-3>` queues that many
  synthetic COUCH joins on this peer once the session is live — a real one is a gamepad Start
  press the rig can't produce. Pair with `?net=host`/`?net=join`; the `[net]` line's new
  `roster=` field must read as mirror images on the two consoles.
- **Frame profiler / FPS HUD** (card 22e655b5): shown by default in every DEV build (keyed off
  `window.eaBuildHash === 'dev'`; the published site never shows it). Compact fps + headroom;
  click the mode tag for the per-phase ms breakdown, frame-time sparkline and GL draw calls.
  Auto-suppressed on the screenshot-verification pages (`?harness=`, `?textshot`, `?texviewer`,
  ...) so it never lands in a harness capture; `?nofps` hides it anywhere, `?fpshud[=full]`
  forces it on even on the live site, `?fpsuncapped` unhooks the loop from rAF so the measured
  rate is not vsync-capped, `?fpsgpu` folds GPU time in via `gl.finish()`. **These are the one
  flag group parsed in JS (`index.html`), not `DebugFlags.cs`** -- the HUD is JS-owned. `?fps=`
  is a DIFFERENT flag (the sprite harness' playback rate). Console: `eaFps()`, `eaFps.stats()`,
  `eaFps.test()`.
  **A frame rate alone is vsync-capped and cannot see a regression -- read the ms and the
  headroom, and only trust either with the window FOCUSED.** Details: web CLAUDE.md.
- Level fast-boot added with it: `?level=Level2&flyspiders` (dense flying-spider swarm;
  `=fg` for the un-flattened foreground variant).
- Dozens more per-feature tuning/diagnostic flags exist — see web CLAUDE.md ("Debug flags &
  tuning conventions" + each feature's bullet).

## Toolchain (already installed)

- .NET 8 SDK + `wasm-tools` workload (Emscripten / mono browser-wasm).
- KNI `4.1.9001.*` (`nkast.Xna.Framework.*`) — **this is the engine**; namespace is
  `Microsoft.Xna.Framework` and the API is XNA **4.0** (the game was 3.x → mind the gap).
- `ilspycmd` decompiler: run as `DOTNET_ROLL_FORWARD=LatestMajor ilspycmd ...`.

## Layout

| Path | What |
|---|---|
| `web/EvilAliensWeb/Game/` | the ported game code — **edit here** |
| `web/EvilAliensWeb/Compat/` | Xbox-API + XNA-3.x→4.0 shims, debug/harness scenes |
| `web/EvilAliensWeb/wwwroot/` | host page + JS glue (input, music, webcam, overlays) |
| `web/DevServer/` | the no-store dev static host (never shipped) |
| `src_decompiled/` | decompiled reference source (read-only) |
| `extracted/584E07D1/Content/` | game assets unpacked from the package |
| `tools/` | offline asset pipelines + the scripts that DERIVED `Game/` |
| `plans/` | design docs (stage 11 co-op, walls 3D, texviewer, juice, ...) |

## Critical cross-cutting gotchas

- **The recovered code is the Xbox BUILD.** Anything under `#if WINDOWS` / `[Conditional]` was
  stripped at compile time and is unrecoverable. Re-create PC-only behaviour; don't hunt for it.
- **`Game/` is GENERATED** from `src_decompiled/` by `tools/*.py`. They're already applied —
  **do NOT re-run them** (they'd clobber every hand edit). Edit `Game/` directly.
- **Alpha is STRAIGHT (non-premultiplied) project-wide.** `AlphaBlend` maps to
  `BlendState.NonPremultiplied`; **never use `BlendState.AlphaBlend`** — that's KNI's
  *premultiplied* variant (same-name trap: fades go additive-bright instead of dissolving; tried
  and reverted). Don't premultiply exports or tints; `new Color(1,1,1,a)` is correct. The two
  deliberate premultiplied-INTERMEDIATE exceptions (text/group flatten RTs) are in web CLAUDE.md.
  Evidence: `plans/plan.md` Stage 3.
- **Content paths are CASE-SENSITIVE on the live host (not on Windows).** Asset root is
  `wwwroot/Content` (capital C), everything lowercase under it; every request must match — a
  casing mismatch passes locally and 404s on GitHub Pages (black screen). Verify new assets ON THE
  LIVE URL, not just locally.
- **Hosting:** `.github/workflows/deploy.yml` does `dotnet publish -c Release` in CI, rewrites
  `<base href>` to `/RotEA26/`, adds `.nojekyll` + `404.html`, stamps `window.eaBuildHash`
  (online co-op's peers-run-identical-binary check — dev builds keep `'dev'`), deploys via
  `actions/deploy-pages` — triggered MANUALLY (`workflow_dispatch`). The dev build keeps
  `<base href="/" />`; don't hard-code `/RotEA26/` in `index.html`.
- **Publish trimming:** `PublishTrimmed=true` + `TrimMode=partial` (NOT full — full strips the
  XmlSerializer save types + KNI's reflection factories → white screen);
  `InvariantGlobalization=true` (so even Debug is culture-invariant — no culture-dependent
  parse/format); `System.Private.Xml` pinned via `<TrimmerRootAssembly>`. **Verify any trim change
  with a LOCAL Release publish in real Chrome (saves round-trip) before pushing** — trimming
  breakage only shows at runtime in the browser.

## Dedicated server hosting (Hetzner Cloud VPS — shared with NotZelda)

The online co-op signaling server (Stage 11.4+) lives on a shared Hetzner VPS:

- **Server:** Hetzner CX22, Ubuntu 24.04 — IP `46.225.218.207`
- **SSH:** `ssh root@46.225.218.207`
- **Code:** `/opt/<PROJECT_NAME>/` (this project's server code goes in `/opt/rotea/`; NotZelda
  lives at `/opt/NotZelda/` on the same box — don't touch it)
- **Ports already in use:** 8080 (NotZelda game server), 8081 (`notzelda-llama` llama-server),
  80/443 (nginx), plus the fighting game's port — **check `ss -tlnp` for the live list before
  claiming a port**; pick a free one for this project.
- **Services:** manage via systemd — `systemctl restart <service>` / `journalctl -u <service> -f`.
  Existing units: `notzelda`, `notzelda-llama`.
- **nginx** serves static files / reverse-proxies; add a new server block or location for this
  project rather than editing NotZelda's (or any other project's) config.
- **Deploy:** `ssh` in, `cd /opt/<PROJECT_NAME> && git pull`, then restart the project's service.
- **Shared box etiquette:** it's a small CPU-only VPS (2 vCPU / 4GB RAM) also running an LLM
  server — keep resource usage modest and never stop/restart the `notzelda*` (or other
  projects') services from this project.

## Related repos

- **Meridian Workspace** (`github.com/Coamithra/meridian`, private; deployed at
  `https://haraldmaassen.com/meridian/`) is the "boss key" decoy + multi-game launcher the main
  menu's Exit hands off to (`MenuScene.mainMenu_ExitSelected` → `Compat/ExitInterop.Quit()` →
  `eaQuit` in `index.html` → `<MERIDIAN_BASE>index.html?from=evilaliens`; `?from=` is what its
  "Shut Down" uses to return). Architecture is hub-and-spoke: each game is a standalone repo/site;
  the only coupling is that URL contract (`MERIDIAN_BASE` game-side, `CONFIG.GAME_ORIGIN`
  meridian-side — currently absolute, cross-origin). To add a game: cover art + one `games.json`
  entry in meridian, one `eaQuit`-style handoff in the game. **To edit the decoy/launcher, work in
  the meridian repo** (deploy with its `tools/deploy.py`; prefix `MSYS_NO_PATHCONV=1` in Git Bash).

## Don'ts

- Don't commit `bin/`/`obj/` or the raw 52 MB Xbox package (all `.gitignore`d).
- Don't re-run `tools/*.py` codegen against `Game/` (regenerates it from scratch).
- Don't hand-edit generated assets (`.mgfxo`, `.dds`/`.rtex`, `music.json`, packed sheets, ...) —
  re-run the owning tool (tools/CLAUDE.md).
- Don't trust a screenshot of a backgrounded tab (black canvas / paused rAF) — foreground Chrome
  only, and measure perf with the tab focused.
