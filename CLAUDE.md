# CLAUDE.md — Revenge of the Evil Aliens (web port)

Porting a recovered 2008 **XBLIG** (XNA 3.x, C#) to run in the browser via **KNI**
(a MonoGame fork with a Blazor WebAssembly / WebGL backend). Output = a static site.

**Hosting: LIVE at https://haraldmaassen.com/RotEA26/** (first deployed 2026-08-01, build hash
`c591c9dfe4f4948e`) — a sibling of Meridian under the shared Hetzner host's web root
`/public_html/`, which is what lets the game link to Meridian as the relative `../meridian/`.
`tools/deploy_web.py` ships there. **The stale GitHub Pages build is still up and NOT yet
decommissioned** (card `54c2a8f2`) — it is the copy a stranger still finds, and its build hash
makes it unable to co-op with the new host. See [`docs/DEPLOY.md`](docs/DEPLOY.md).

This file is how to *work* in the repo. Detail lives next to the code:

| Doc | What |
|---|---|
| [`web/EvilAliensWeb/CLAUDE.md`](web/EvilAliensWeb/CLAUDE.md) | game/engine architecture + per-feature notes (render path, input, saves, debug flags, webcam, walls, bosses, ...) |
| [`web/EvilAliensWeb/Compat/Net/CLAUDE.md`](web/EvilAliensWeb/Compat/Net/CLAUDE.md) | the online co-op net layer, split out of the file above (loads automatically under `Compat/Net/`) |
| [`tools/CLAUDE.md`](tools/CLAUDE.md) | the offline asset pipelines (audio, shaders, textures, font, backgrounds, ...) |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | the step-by-step card → worktree → PR runbook |
| [`docs/DEPLOY.md`](docs/DEPLOY.md) | publishing the game + the signaling server (neither happens on merge) |
| `plans/plan.md` | historical artifact — the archived staged plan (full per-stage detail) |

## Project tracking (Trello — local backend)

A **local** (offline, file-backed) Trello board is the live tracker. It is NOT on trello.com — it
lives in the `trello` CLI's local store at `C:\Users\coami\Dropbox\Programming\TrelloBoards`
(`trello local root` prints the authoritative path if it moves again).

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
  publishing is MANUAL and separate from merging — see `docs/DEPLOY.md`), and the card-close
  paperwork.

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
  - a PURE DECISION in C# (a seating rule, a predicate, a resolver) → the **headless logic
    oracle**, `dotnet run --project tools/sim/logic_probe -- web/EvilAliensWeb/bin/Debug/net8.0`:
    it `AssemblyLoadContext`-loads the built `EvilAliensWeb.dll` into the DESKTOP CLR and calls the
    real static method, so the decision is verified with no browser, no WASM and no rig (add a
    `Probe*` case set per card; details + limits in tools/CLAUDE.md);
  - tuning values → the matching **live slider panel** (`?wctune`, `?lazershot`, `eaWalls`, ...);
  - a change that should alter NOTHING (rename, reformat, decompiler-artifact cleanup) → the
    **IL-identity oracle**, `python tools/verify_il_identical.py` — see below.
  If no tool covers the change, BUILD one — that is part of the fix, not extra scope. Boot the real
  game only for (a) the FINAL smoke check (boots, change in, zero console errors) after a tool
  already proved it, or (b) boot/menu/scene-flow changes themselves — and even then use fast-boot
  flags to land next to the thing under test.
- **RUN that tool HEADLESSLY FIRST — `tools/headless/` (`eahl`) is the DEFAULT, Chrome is the
  exception.** The list above is *which* tool; this is *how you run it*. `eahl` is a desktop exe
  that links the same `Game/` + `Compat/` sources (KNI SDL2, hidden window) and takes the **URL
  query verbatim**, so every flag, harness and showcase scene above is reachable with **no Chrome,
  no dev server, no rAF** — and it writes the PNG straight to disk:
  ```sh
  dotnet build tools/headless -c Debug
  tools/headless/bin/Debug/net8.0/eahl.exe --flags "?harness=spider&frame=3" --frames 150 --out shot.png
  ```
  Reach for it before `preview_start` + claude-in-chrome, because it is strictly better on the
  four things that make browser verification painful here: it can't paint black from a
  backgrounded tab, it needs no dev server or cache-busting, it is ~3x real time rendered and ~17x
  with `--nodraw` (so an `eaAiBench.soak`-style run that needed a FOREGROUNDED tab now just runs in
  the background while the user works), and it is scriptable — `--repl` boots once then takes
  `step`/`shot`/`eval` lines, where `eval` reflects into `DebugInput`'s console surface (`Press`,
  `AiBench`, `TexProbe`, `TeamSeat`, ...), i.e. everything `eaPress(...)` and friends give you in
  devtools. `--script <file>` makes that a repeatable probe that exits non-zero on the first
  failure. Full options + the design: `tools/headless/README.md`.
  - **It does NOT replace the Chrome pass, and is not evidence about the shipped build.** It runs
    the same C# on desktop GL, not WASM on WebGL. Trimming, IndexedDB saves, WebGL-specific
    shader behaviour, the `index.html` JS layer (incl. the FPS HUD and the whole `ea*` JS facade)
    and real WebRTC can ONLY fail in the browser — as can a case-sensitive `Content/` path, which
    a local filesystem happily resolves. The Phase-5 gate (foreground Chrome, zero console
    exceptions) is unchanged; `eahl` is what you use to get the frame or the number *before* it.
    Even for a genuinely browser-only gate, eahl is still the cheapest way to FIND the browser
    leg's inputs: rehearse the menu key sequence headlessly (`--repl`, `eval Press ...`,
    screenshots), then Chrome only replays a known-good script (card 2c3499f3). In Chrome, focus
    the game tab with `window.focus()`, NEVER a synthetic click — every click on the canvas is
    also a menu-select / fire input.
  - **A check worth RE-RUNNING later becomes a committed probe** (card 1e476668):
    `tools/headless/probes/*.txt` are `--script` files that assert, driven by
    `python tools/headless/probes/run_probes.py` (exit 1 on any failure). `mark` / `expect` /
    `expect-not` match per line against everything the run printed, the game's own
    `[loadprofile]` / `[hitch]` / `[net]` lines included — which is the only way to defend a
    change whose failure is SILENT (a data file, a manifest, a host default). Add one when a
    regression would otherwise go unnoticed until someone plays the game; mutation-test it
    first, and assert the POSITIVE too or it passes on a run that never got there. Conventions
    + the menu-navigation crib: `tools/headless/probes/README.md`.
    **The runner REFUSES a stale binary (exit 2, not a probe-failure 1)** -- it checks eahl
    against the newest `Game/`/`Compat/`/`tools/headless` source first, because a failed
    `dotnet build` otherwise leaves the probes testing the previous binary and reporting green
    (card 74998f22). Rebuild, or pass `--build`; `--allow-stale` is the deliberate override.
    A probe's PRECONDITION must be pinned and asserted separately, not waited out -- unseeded
    RNG turns "stepped far enough" into a coin flip (card af4c3694).
  - **`eahl` does NOT read your desktop mouse, and putting that back breaks the suite** (card
    83054936). KNI's SDL2 backend answers `Mouse.GetState()` from `SDL_GetGlobalMouseState` —
    the real pointer AND the real buttons, focus-independent — so every headless run used to
    sample whatever your hand was doing. `MenuSub1.HandleMouse` hover-selects on cursor movement
    *and* swallows that tick's keypress, so scripted menu navigation landed somewhere else
    (`menu_backtip.txt` failed **15 of 20** runs); a physically-held button separately ate
    scripted clicks and scripted fire releases (`net_single_tap.txt`). `HeadlessHost.Boot` now
    suppresses it: cursor parked off-screen, buttons released, printed as
    `[eahl] input    physical mouse suppressed`, read back with `eval MouseState`, restored by
    `--real-mouse` (which is the mutation control, not a normal option). Scripted input
    (`MouseAt`/`Press`/`Hold`) is unaffected, and the browser never sets it.
  - **GOTCHA — a screenshot in the first ~2 s is a WHITE RECTANGLE and nothing is broken.** Every
    scene calling `Background.Reset()` (level entry AND `?harness=`/`?textshot`) starts in
    `LeavingHyperspace` with `fadeFactor = 0.998`, decaying over ~120 frames. Settle first
    (`--frames 150`, or `step 150 nodraw`). It prints a `NOTE` on a near-white frame inside that
    window; don't lean on it. This cost a full investigation once.
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
- **A refactor that DELETES a local is a different problem — the hash oracle may not cover it.**
  The default build keeps every local for the debugger, so removing one changes the IL.
  `--optimize` folds dead temporaries away and restores the byte-identical claim for some of that
  class, but not all: collapsing `bool num = held; held = num | X;` to `held |= X` moves the
  `ldloc` across the neighbouring property read, and collapsing repeated `x.Position - y.Position`
  recomputations removes real `get_Position` calls (Roslyn never CSEs a property call). For those,
  the question is **"is the difference confined to the methods I edited"** — answered by
  `python tools/verify_decompiled_diff.py --ref main`, which decompiles both assemblies and diffs
  the C#. Caveat: ILSpy NORMALISES, so an absent method means "same construct", not "identical IL".
  Never read a raw IL diff of such a change directly — deleting a local renumbers every later slot
  and the diff mispairs them, inventing changes in code you never touched. Details: tools/CLAUDE.md.
- **Never verify motion with timed live screenshots.** A raw screenshot is only valid for STATIC
  appearance; anything time-varying needs a parked/scrubbed frame or a data sim. If you genuinely
  must see it live, build a break/pause (`DebugFlags` seam) that freezes at the moment of interest.
- **A clean `dotnet build` does NOT mean it runs.** WASM runtime errors only appear in the
  **browser console** — always verify visually AND read the console.
- **When you DO need a browser, use real Chrome (`claude-in-chrome` MCP), not `preview_screenshot`**
  — the built-in preview wedges when its tab is backgrounded (the rAF loop pauses, so it never
  paints). Reach for this once `eahl` has taken you as far as it can (see the headless rule above),
  and always for the final smoke check. Flow: `preview_start` → Chrome `navigate` to
  `http://localhost:5280` → `wait` ~10s for WASM → screenshot + `read_console_messages`. Automated
  input: `eaPress(...)` from the console, not synthetic key events (web CLAUDE.md → Input).

## Debug boot shortcuts (URL flags)

Parsed once at boot in `Compat/DebugFlags.cs`; no query = normal boot. Combine with `&`:

- `?menu` (straight to main menu) · `?noattract` (no idle attract demo) · `?level=<Name>` (boot
  into a level; a `Levels` value, e.g. `Level1`/`ClassicAliens`/`WebcamAliens`) · `?invuln` (aka
  `?god`) · `?unlockall` · `?skipsplash` / `?autostart`.
- **Level fast-boots** (replace a level's event list with one fight/section): `?spiderboss` ·
  `?marsboss` (Level 2's TWIN motherships -- otherwise ~8 sim-minutes into a Level 2 soak) ·
  `?spiders` · `?wallsonly` · `?brainboss` (bypasses the Hard+ gate). Pair with `?invuln`.
  e.g. `…:5280/?level=Level3&brainboss&invuln`.
  **`?wallsonly` serves TWO levels** (card b174b00f): on `Level3` it loops the wall sections; on
  `OwnLevel` it drops that level's `SkullSpawner`+`StarMineSpawner` and keeps its `Walls(2)`.
  Same name on purpose -- it is what makes an OwnLevel churn figure comparable with a Level-3 one.
  **`?nowalls`** is its OwnLevel-only complement and positive control (spawners live, walls gone).
- **Online game browser** (card 2001fbd8): `?gamebrowser` boots straight to the "Join Online
  Game" carousel with injected fake entries (no server) -- four real-looking games, the
  APPEARANCE rig. **`?gamebrowser=fallback`** adds two entries whose level has no bundled art
  (one with no carousel slot, one not in the `Levels` enum at all), which is the only offline
  way to reach `SubMenuOnlineGames.EnsureArt`'s no-art branch -- otherwise reachable only from a
  real stranger's build off the wire (card 0d166364). Kept apart because those two rows draw
  Mission 1's art under the generic "Mission" title and would be noise in an appearance shot;
  a value that is neither is reported and treated as bare. **`?gamebrowser=thumbs`** (card
  e7404647) instead gives two of the four entries a synthetic ROOM THUMBNAIL, so one shot shows
  both halves of the carousel's rule -- prefer the host's live picture, fall back to stock art --
  which no other offline route reaches. Console: `eaGameBrowserShots()` reads which rows drew
  which (a failed thumbnail and an absent one look identical), `eaRoomShot()` captures this frame
  through the real server-pull path. `?netjip` pairs with `?level=<Name>`
  so a debug-booted host still LISTS its game for the two-window join-in-progress test.
- **Host kick / block** (card 0b8a300b): `?netkickshot` (pair with `?level=<Name>`) parks the
  host's remote-pause kick menu over a live level with no peer, for a screenshot;
  `?netfakepeer=<s>` overrides this tab's peer-identity token and is **required** for any
  two-tab kick+block test (both dev tabs share one `localStorage`, so they otherwise present
  the same id and blocking the joiner blocks yourself). Console: `eaKickTest()`.
- **Host pause menu -- "Online Play"** (card 0d6ffe70): the host's own way to reach the kick
  above (`?netkickshot`'s menu only ever appears under a REMOTE pause) plus an open/close-room
  toggle. No new flag: `?netfakelisted=<code>` reaches the room half (it now sets `CouldList`
  and honours `Settings.AllowOnlineJoins`, so the toggle is live), and the kick half is a
  scripted-peer session. Console: **`eaHostMenu()`** dumps the LIVE decision (session/host/peer/
  couldList/allowJoins -> which rows), **`.test()`** sweeps the predicate over all 32 states
  (also `logic_probe`'s `ProbeHostMenu`), **`.live()`** drives it over a real session and kicks.
  Pinned by `tools/headless/probes/net_host_menu.txt` + `net_host_menu_absent.txt`.
- **Net UI screenshot/probe seams** (card group `fix/net-ui-smalls`): **`?netfakelisted=<code>`**
  reports the game as publicly LISTED under that code with **no socket and no server**, so the
  pause menu's "Listed online -- room XYZAB" line and `ScoreVisualiser`'s corner beacon can be
  screenshot offline (reaching either for real needs a live server AND a level the eligibility
  predicate accepts). **Since card 0d6ffe70 it HONOURS `Settings.AllowOnlineJoins`** (and sets
  `NetListing.CouldList`), so the pause menu's room toggle is a live control on it -- if the line
  does not appear, that option is off in the save, not the flag broken.
  Free-form identity string, out of `DebugFlags.Active` -- no session exists,
  so it cannot alter a shared run. Console-side: **`eaNetNotice(text)`** parks a session-ending
  notice at the menus with no peer (`|` = newline; every production writer of `MenuNotice` is
  inside `NetSession.Stop()`), **`eaMenuNetMode()`** forces the live `MenuScene` into net-lobby
  mode -- the one precondition of card 72143c11 a headless run cannot otherwise produce -- and
  **`eaMenuCensus()`** lists the LIVE menus, which is the only observable that separates "drawn
  behind" from "still taking input". Pinned by `tools/headless/probes/net_notice_menu.txt`.
  **`eaMenuNetState()`** (card c337222a) reads that flag and its three siblings BACK -- none of
  them changes a pixel, so it is the only way to see whether a level launch left the menu still
  believing it is inside the Online Co-op flow (it no longer does; net CLAUDE.md has the
  lifecycle and the `EnterNetLobby()` entry point). Pinned by
  `tools/headless/probes/net_menumode_reset.txt`.
- **`?nethitstop=1`** (card 68f62e92): let a hit-stop freeze game time inside an online co-op
  session again. Normally `Juice.AddHitStop` refuses EVERY hit-stop while `NetSession.Active` --
  the death stop, the `?hitstop=1` kill/boss stops and `eaHitstop()` alike -- because a freeze
  halts that peer's whole world while the wire keeps streaming the frozen positions, and the other
  peer's enemies are then corrected BACKWARD ("when P1 dies, the whole game rewinds a bit";
  measured at 23px by `python tools/sim/net_puppet_drive_sim.py --hoststall`). **The deliberate
  bug reproduction** (the `?teampartner=pad` idiom), and IN `DebugFlags.Active` for that reason --
  it must never reach a public lobby. Screen SHAKE is unaffected either way. Details: net CLAUDE.md.
- **`?netstaleguard=0`** (card f5cf7a5c): turn the world snapshot's staleness guard OFF, so a
  reordered or late `MsgWorldSnapshot` entry drags a puppet BACKWARDS again. The packet now
  carries a monotone seq and an entry older than the one already applied for that netId is
  refused; this restores the pre-card behaviour. **The other deliberate bug reproduction** (the
  `?nethitstop=1` idiom), and IN `DebugFlags.Active` for the same reason. Like `?netaimease`
  below it DEFAULTS TRUE, so `Active` tests its negation. The `[net]` line's new
  `snapStale=` counter reads the same either way -- the flag changes the drag, never the
  measurement. Console `eaNetStale()` / `eval NetStale` is the suite (it drives the flag through
  the injected host, so no reboot); protocol v19 also raised `NetBaseState.Scale` 1/256 -> 1/4096
  with rounding. Details: net CLAUDE.md.
- **`?netaimease=0`** (card eb057163): stop a puppet's enemy charge glow SWEEPING toward each
  newly replicated aim, so it teleports to it once per snapshot turn again -- the pre-card
  staircase (measured: 15 moving ticks of 144, 7.62px each, over one 2500ms MarsBoss windup at a
  150ms turn). **Another deliberate bug reproduction** (the `?nethitstop=1` / `?netstaleguard=0` /
  `?teampartner=pad` idiom) and IN `DebugFlags.Active` for the same reason; like `?netstaleguard`
  it turns a shipped FIX off, so it DEFAULTS TRUE and `Active` tests its negation. The glow is draw-only, so nothing desyncs either way -- the two
  peers simply disagree about where an enemy is aiming, which is what the card was reported for.
  Console `eaNetChargeAim()` / `eval NetChargeAim` is the suite (it drives the flag through the
  injected host, so no reboot); no protocol change, still v19. Details: net CLAUDE.md.
- **Two-process join-in-progress** (card 054947f3): **`?net=jiphost`** (pair with `?level=`) holds
  an open loopback with NO session and attaches a real `StartListedSession` when a peer arrives;
  **`?net=jipjoin`** (pair with `?menu&noattract&netallowdebug`) is a real menu-session joiner that
  mirrors the host's `EvLaunch`, warms the level itself and sends its own `EvReady`. Headlessly the
  `eaNet` loopback is backed by a localhost socket, so the two roles are two **eahl PROCESSES** --
  driven, and their worlds diffed, by `python tools/sim/net_jip_sync.py`. It needs
  `eahl --nettime game` (`--nodraw` is ~17x real time, which starves the wire); **it is GREEN on
  `main` since card d108c459** -- the defect it found is fixed and its residuals were calibrated
  away with oracle corrections, not widened tolerances (net CLAUDE.md has both halves).
  `eaNetJipDump()` / `eval NetJipDump` is the world dump both ends are read with (format v5).
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
  `=fg` for the un-flattened foreground variant). **`=fg` is not a flatten A/B** -- for that use
  the pinned bench `?flyspidercount=<N>` with `?flyspiderflatten=per|0|swarm` (+ `?flyspiderbox=`,
  console `eaFlySpiders()`); card 9c92962e, details in web CLAUDE.md.
- **AI bench** (card f4d1721f): `?aibench` turns on telemetry for the `ControlDevice.AI` ships —
  wall contacts (counted even under `?invuln`), the heading-reversal jitter pair, `idle%`,
  level progress and the run verdict. **Soak it HEADLESSLY with `eaAiBench.soak(<simSeconds>)`**
  (ticks the real loop at a fixed 60Hz dt with no Draw) — a backgrounded tab throttles rAF *and*
  MessageChannel to ~1Hz, so any rendered soak measures nothing. `?aiff=<n>` is the watchable
  fast-forward (n sims per drawn frame, each at a synthesised 60Hz dt). Tuning overrides
  `?aismooth= ?aismoothurgent= ?aireact= ?aigapmargin= ?aiscanrows= ?aicrosspenalty=
  ?aithreatlead= ?aibossbias= ?aigunhull= ?aiaim= ?aifieldpx= ?aifieldsize= ?aifieldfall= ?aiseekapproach=
  ?aiseekpowerup= ?aipowerupreach= ?airepeldelta= ?ainoisefloor= ?aiseekdeadzone=
  ?aiasteroidscale= ?aiasteroidrange= ?aiasteroidfall= ?aievade= ?aicone= ?aiwedge=
  ?ailaneescape= ?aiconelead= ?aiconemaxlen= ?aiconewidth= ?aiconetaper= ?aiconefallalong=
  ?aiconefallacross= ?aiconescale= ?aiconespread= ?aiconewidthmin= ?aiwedgestrength=
  ?aiwedgefall= ?aisweptmax= ?aitopedgepx= ?aitopedgestrength= ?ailazerpx= ?ailazerstrength=
  ?ailazerdodge=`. Pair with `?aiplayer`.
  **`?aiwallnav2008=1` is NOT one of those knobs -- it swaps the wall-steering ALGORITHM** (card
  d79b7ea7): `findNextTileOnMap`'s per-tick left-vs-right re-decision and its ~6.6px probe,
  transcribed verbatim from `src_decompiled/`, in place of the port's committed-gap column search.
  It swaps the APPROACH STEER only -- the slam-the-steer clamp (`ClampIntoWallSpace`) is 2008 code
  the port KEPT, so there is nothing there to switch.
  It exists because no setting of `?aireact`/`?aiscanrows`/`?aicrosspenalty`/`?aigapmargin`
  reconstitutes the original, so the wall-nav constants had no null hypothesis to be audited
  against. **Another deliberate bug reproduction** (the `?nethitstop=1` / `?netstaleguard=0` /
  `?netaimease=0` idiom) and IN `DebugFlags.Active` for that reason. Which algorithm actually ran
  is printed once per process as `[aiwallnav] steering: port|2008` -- the flags dump reports only
  the PARSE, and an arm that measured the shipped code twice prints a plausible table. Pinned by
  the probe pair `tools/headless/probes/ai_wallnav_2008.txt` + `ai_wallnav_2008_absent.txt`.
  **`?aitopedgecompose=0` is a PLACEMENT, not a magnitude** (card 13960838): it puts the top-edge
  danger band's push back where it was, into `direction` AFTER the steering low-pass, so it is
  neither damped nor eligible for the repulsion-cancel floor. It ships summed into `repel` with
  every other repellent. The band was strength 20 against a `maxSteerStrength` of 4, and 4 is also
  the CEILING of the powerup approach pull -- so under the old placement no attractor could win
  the top **129px** of the screen and a pickup there was arithmetically unreachable, which is what
  the card was reported for. **The same card took the strength 20 -> 12**, narrowing that strip to
  **102px** rather than removing it. `?aitopedgestrength=`/`?aitopedgepx=` already reach the magnitudes
  (and `=0` there is card 2248e5eb's 2008 arm); no combination of them expresses the placement.
  **The placement alone does NOT move the pickup rate** -- it fixes the composition defect that
  made the band unbeatable at any strength above 4, and the rate itself tracks the MAGNITUDE. So
  the two ship together, and only the second addresses the powerup complaint. Both rest on N=16
  directional evidence with the N=60 scoring pass as the arbiter. Table in web CLAUDE.md.
  **Another deliberate bug reproduction** (the `?nethitstop=1` / `?netstaleguard=0` /
  `?netaimease=0` idiom), IN `DebugFlags.Active` for that reason, and one-way like its siblings --
  it turns a shipped fix off, so it DEFAULTS TRUE and only an explicit off spelling assigns.
  Which placement actually ran is printed once per process as
  `[aitopedge] placement: composed|post-smoothing` -- the flags dump reports only the PARSE, and
  an arm that measured the shipped code twice prints a plausible table. Pinned by the probe pair
  `tools/headless/probes/ai_top_edge.txt` + `ai_top_edge_precard.txt` and by `logic_probe`'s
  `ProbeAiTopEdge`. Details: web CLAUDE.md.
  **`?aisweptmax=<px/ms>` is a GUARD, not a tuning knob** (card c1d783ad): above it the DEFAULT
  swept-path seam refuses the path, because a raw one-frame position delta reports an enormous
  velocity for anything repositioned in a single tick and the cone would sweep the screen for that
  frame. `0` turns it off (the A/B seam). The value is `NetSession.MaxObservedSpeedPxPerMs`,
  referenced rather than copied. **The same card found the hazard was LIVE**: `Initialize` did not
  reset the observed-velocity history per life, so every POOL-RECYCLED entity reported a phantom
  teleport on its first tick (`EvilBullet` at 14.9 px/ms against a declared 0.24). Details: web
  CLAUDE.md.
  **The bench also reports `killers=<Type>:<n>` (with `SpiderBoss(standing)` split out),
  `pickups=<n>/<spawned>(<pct>%)` and `boss=<px> bossfar=<pct>`** (cards 31ceb6ff / ada9e839) --
  which is what turned "the AI runs into the stationary spider boss" and "the AI ignores
  powerups" from impressions into numbers. **Attraction and repulsion COMPOSE; nothing
  vetoes the sum** (card ada9e839). The port ended `DoAIMove` with a 0.95 park where the 2008
  original had **0.2** -- above the 0.8 seek, so a lone seek produced no motion at all and every
  deliberate destination was silently deleted. Repellents now sum and are floored on their own
  (`?airepeldelta=`), attractors are never floored and stop inside a deadzone sized by the ship's
  11.3px stopping distance, and `?ainoisefloor=` catches the equilibrium case. `?aipark=` is gone.
  Powerup pickups 72% -> 98%. `EvadeMovingThreat` was
  re-measured and KEPT (CrazyGame deaths 3.75 with it vs 14.25 without; flat on SpaceDodge only
  because asteroids are too slow to pass its speed gate).
  **EVERY MOVER PROJECTS A DIRECTIONAL REPELLENT -- a mesa along its own velocity, not just a
  circle** (card e425781b). A circle can say "I am here" and not "I am about to be THERE", and the
  bot's mean edge distance from an asteroid (252px) sits outside the radial field's 199px warning
  perimeter, so it lived where no circle reached. The cone is full strength across the swept body,
  plateaus ALONG the trajectory and spikes away ACROSS it (so threading a gap stays possible), and
  its length scales with speed -- one rule, no per-type code. Lane-hugging sweeps additionally get
  an asymmetric WEDGE closing everything between the path and that screen edge.
  **SpaceDodge 2/16 -> 16/16 victories, 33.75 -> 3.25 deaths** (seeds 1-8 x2); Level 1 improves too
  (7.62 -> 2.00 deaths). Shipped WITH two stated regressions: CrazyGame deaths 4.75 -> 8.50 and
  `SpiderBoss(standing)` 12 -> 22, both on levels whose victory verdict is unchanged.
  **Do NOT tune the radial asteroid field -- FOUR axes were swept across three cards (magnitude,
  range, falloff, curve family) and none reaches the gate**; the `?aiasteroid*` seams exist to A/B
  against, not to ship a value.
  **THE WHOLE PORT-ERA TUNING GENERATION WAS RE-AUDITED against the 2008 original (card
  05a2b818), and the measurement bar moved with it.** Four port values were validated (the
  steering low-pass, the field curve family, its exponent, the spider lane escapes); two were
  refuted and changed (`ThreatFieldRange` base 190 -> 150 with the size scale kept; the seek
  deadzone 30 -> 15). **Every AI number predating merge f6b6504 is a hypothesis** -- phantom-era,
  and mostly N=16. **N=60 paired by seed is now the floor** (`python tools/sim/ai_sweep.py` is the
  instrument, and reports time-to-victory because deaths are a count, not a rate). The re-baseline
  reference table for all five rigs is in web CLAUDE.md.
  Details + all the tables: web CLAUDE.md.
  **Per-tier AI skill** (card c10e3e7f): the threat-field and aim-spread knobs resolve through
  `PlayerShip.AiSkillByDifficulty[]`, keyed off `Settings.EffectiveDifficulty` (the LOCK-aware
  tier -- attract demos lock Hard). `eaAiBench()` prints the resolved row; the `?ai*` overrides
  still win, which is how the per-tier values were chosen. Very_Hard == the previous constants.
  Details + the AI's own gotchas (its world model is `Oracle.GetBaddies`; a low jitter score
  alone can mean the bot is wedged, not smooth): web CLAUDE.md.
- **AI completion sweep** (card 9391f95a): **`eaAiBench.matrix()`** runs the whole "can the bot
  finish it" matrix unattended — one FRESH page load per run (plan in `sessionStorage`, resumed
  at boot), so no run inherits another's locked difficulty, lives or RNG. `.results()`
  `.status()` `.stop()`; never `await` it (each run outlives a single devtools eval).
  **TeamChallenge needs no special flag since card e6927ef8** — its second slot now resolves to
  `ControlDevice.AI` when no gamepad is plugged in (it was an unconditional `PadOne`, and a
  seated-but-disconnected pad makes `GameScene.Update` force-pause every tick, so the level could
  not be benched OR played at all). The old `?aiteam` bench seam is gone; `?teampartner=pad` brings
  the broken seating back if the force-pause itself is what you want to reach. **Eight of the
  nine challenge levels run with `score.Lives = -1`, so `GAME OVER` is unreachable on them** —
  failure shows up as the sweep's third verdict, `TIMEOUT`, never as a bad verdict. Keep the tab
  FOREGROUNDED (each run's boot is rAF-paced). Matrix + per-level caveats: web CLAUDE.md.

- **`?teampartner=ai|pad`** (card e6927ef8): override how TeamChallenge seats its SECOND slot.
  Normally the partner is the lowest connected gamepad the primary player is not using, or an
  auto-pilot `ControlDevice.AI` partner when there is none — the fix for the level being
  unplayable (permanent force-pause) on a keyboard-only machine, and a pad Start press takes that
  seat over mid-level (a browser only reveals a gamepad once a button is pressed on it, so player
  two is invisible until they join). `ai` forces the bot even with a pad attached; **`pad` forces
  the old unconditional `PadOne` verbatim, i.e. reproduces the bug** and is the only deliberate way
  to reach the disconnected-pad pause loop. Verify the decision as DATA with console **`eaTeamSeat()`** (all 16
  pad-connection masks through the real resolver + the pre-card policy as the negative control) —
  it needs no level and no gamepads. Replaces `?aiteam`.
- **`?seed=<n>`** (card d937c721): seed the gameplay RNG (`RandomHelper`) so a level-level eahl
  A/B measures the change rather than two different worlds -- unseeded, two runs of
  `?level=OwnLevel&noattract` differ by mean |diff| 0.2, **MAX 210** of 255. **Near-deterministic,
  not deterministic: a same-seed run lands in one of a handful of discrete worlds** -- 10
  consecutive runs identical on a quiet box, 4 distinct states over 10 while sibling builds loaded
  the CPU. **So capture each side of an A/B TWICE and require the same-side pair to match before
  comparing sides** (the residual is eahl's boot frame, not the RNG -- `tools/headless/README.md`
  -> "Reproducibility"). Reaches `RandomHelper` only; the
  FX/shake/splash RNGs are separate instances by design and stay unseeded. Out of
  `DebugFlags.Active` on purpose (it hijacks nothing, and `Active` would refuse the two-peer
  netplay captures it exists for), so a seeded boot announces itself on its own `[debug]` line.
  No flag => unseeded, exactly as shipped.
- **`?splashvariant=revenged|pure|glasses`** (card 57555583): pin which reveal the splash's
  channel flip lands on. The two portrait shots are a 5% branch each, and since that card the
  roll (in `SplashScene.LoadContent`) also decides which texture is DECODED at all -- so this
  is both the screenshot rig for the flip and what makes the boot decode set deterministic for
  `tools/headless/probes/boot_cold.txt`. Bad value => reported + the random roll.
- **`?demo=<1|2|3>`** (card e63601a4): pin WHICH attract demo the idle main menu drops into.
  `MenuScene.mainMenu_DemoSelected` otherwise rolls it unseeded per launch, so a chosen demo
  was unreachable on demand -- which is what made the demos' preload gaps hard to capture and
  impossible to probe. **Not the off-switch of `?nodemo`/`?noattract`** -- those disable attract
  entirely; this only pins which one the roll picks. Bad value => reported + the random roll.
  Capture ONE demo per process (the content manager is shared, so a second demo is warm).
- **Bomb ripple** (card 5f38ed35): a screen-space refraction ring radiating from every bomb
  detonation (`Compat/BombRipple.cs` + `tools/shaders/src/bombripple.fx`, applied in
  `Game1.ApplyBombRipple` on the slowmo-trail/holo-sim post seam). **`?ripplephase=<0..1>` parks
  one ring at a chosen point in its life and holds it there** -- the scrub rig, since a timed
  screenshot of a 0.75 s travelling wave proves nothing; pair with `?ripplecenter=x,y` (design
  coords) to place it over something with contrast, e.g.
  `?level=Level2&invuln&ripplecenter=400,430&ripplephase=0.25`. Tuning: `?ripple=` (master, 0 =
  off) `?rippleamp= ?rippleradius= ?rippleduration= ?ripplewidth= ?ripplefalloff= ?ripplerim=`,
  plus `?ripplemini` to let the asploding-bullet minis ripple too (off by default). Live panel
  `eaRipple` on `?rippletune`; console `eaRipple.fire(x,y,power)` / `.park(phase)` / `.state()`
  (`eval RippleFire` / `RipplePark` / `RippleState` under `eahl`). `?ripplepower=<0..4>` gives
  the parked ring a bomb powerup level (a maxed bomb is 1.88x the amplitude). Details: web
  CLAUDE.md.
- **Respawn clock ring** (card 37f3a663): the respawn countdown is a clock ring that fills, pulses
  near full and POPS into a free level-4 bomb as the ship returns (`Game/EvilAliens/
  PlayerShipSummon.cs`). **`?respawnphase=<0..1>` parks the fill at a chosen point** -- negative =
  live, the `?ripplephase=` convention -- and **`?harness=respawn`** is the frozen rig for it; a
  ~10 s fill with a 220 ms pop cannot be caught by a timed screenshot. Console `eaRespawn.park(p)`
  / `.state()` (`eval RespawnPark` / `RespawnState`), the latter reporting fill/pulse/pop as DATA,
  which is the only way to verify the pulse. **In netplay BOTH peers draw it** (`EvRespawn`,
  protocol v17). Side-fix in the same card: a death that WIPES the world no longer raises a summon
  at all -- it used to appear for one frame before `LoseLife` purged it. `eaKillShip(<slot>)` kills
  ONE ship (`eaKillShips()` takes them all in a tick, which is the suppressed case). Details: web
  CLAUDE.md; net half: net CLAUDE.md.
- **BrainBoss overlay rigs** (cards 391e11d2 / 9f90978c): **`?brainoverlayphase=<0..1>`** parks
  every animated overlay patch (the shipped pair: the eye and the exhaust pods) at a chosen point in
  its cycle and holds it there -- the eye rests CLOSED on frame 0 and opens only on a ~15 s random
  roll, so it is otherwise unreachable for a screenshot. Negative = live, the `?ripplephase=`
  convention. **It is also the only way to get a REPEATABLE frame out of the boss at all**: the
  overlays advance on Draw time, so two `shot`s with no `step` between them are not identical
  without it. **`?brainhitflash`** forces the hit-flash brighten on (draw-side only, nothing is
  damaged), which no rig can time inside the real 35 ms hittimer window. e.g.
  `?harness=brainboss&brainoverlayphase=0.5&brainhitflash`.
- **Post-level text crawl** (card bee8f0e0): the crawl now tapers like a Star Wars opening.
  **`?creditsshot=<1|2|3>`** boots straight into it for that level (otherwise reachable only by
  finishing a level or `?level=Level2&win` -- `?win` is LEVEL-2-ONLY, see `Compat/DebugFlags.cs`),
  **`?crawlpos=<designY>`** parks the scroll for a
  screenshot, **`?crawlskew=<f>`** dials the taper (`0` = the flat pre-card crawl). **The amount
  is CLAMPED to what keeps the widest line on screen and the shipped crawls saturate it at
  0.081-0.095, not the card's 0.2** -- +20% of a 669px line does not fit 800px at any pivot. Read
  `[crawl] skew= effective= fit=` for what is actually drawn; details in web CLAUDE.md.
- **`?skullvolley`** (card d8344c17): make every `EvilSkull` (the "evil grinning face of death")
  report each beat of its volley on a `[skull]` line -- `shot=<i>/<cap>`, the fade state, whether
  a bullet actually left, and a per-rearm line whose `fired=` must always be 0. The volley length
  is invisible in every frame and moves no metric, so this is the only observable it has. Console
  `eaSkullVolley()` / `eval SkullVolley` dumps the live skulls' state instead. **The volley CAP
  ramps 4 -> 9 with level time by design and is not a bug** (table in web CLAUDE.md); pinned by
  `tools/headless/probes/evilskull_volley.txt`.
- **`?nomips`** (card 110153c7): `WebContentManager.TryLoadDds` uploads level 0 only, so the one
  mipped asset (`gfx/base/756-v1`, the Level-3 wall sheet) falls back to plain bilinear. The live
  A/B for the tower-shaft aliasing; it is read at LOAD time, so it must be set at boot.
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
  **Consequence for ART: a fully-transparent texel's RGB is still sampled**, so it must carry the
  nearest ink's colour, not black, or every scaled sprite draws with a dark halo. Handled offline
  by `tools/imagebleed.py` via `build_textures.py` (precompiled assets) and
  `tools/textures/bleed_pngs.py` (everything else) — tools/CLAUDE.md, "Textures".
- **Content paths are CASE-SENSITIVE on the live host (not on Windows).** Asset root is
  `wwwroot/Content` (capital C), everything lowercase under it; every request must match — a
  casing mismatch passes locally and 404s on the live host (black screen). True of BOTH hosts —
  Pages and the Apache box are equally case-sensitive. Verify new assets ON THE LIVE URL, not just
  locally; `python tools/check_deploy.py` probes it, wrong-case control included.
- **Hosting — full runbook in [`docs/DEPLOY.md`](docs/DEPLOY.md); nothing deploys on merge.**
  `python tools/deploy_web.py` publishes `-c Release` from a throwaway detached checkout (so no
  untracked `wwwroot/` file can ship), rewrites `<base href>` to `/RotEA26/`, stamps
  `window.eaBuildHash`, and SFTPs the result to the shared Hetzner host incrementally. The dev
  build keeps `<base href="/" />`; don't hard-code `/RotEA26/` in `index.html`.
  **The build hash is the co-op compatibility key** (peers-run-identical-binary check; dev builds
  keep `'dev'`, which also shows the FPS HUD). Its recipe is inherited verbatim from the
  (now deleted) Pages workflow and pinned by `python tools/deploy_web.py --selftest` — THE ONLY
  record of it once that workflow goes, so treat a FAIL as "I am about to split the player base",
  not as a stale test.
  `.github/workflows/deploy.yml` is **GONE** (card `54c2a8f2`). Pages no longer builds or hosts
  the game: `.github/workflows/pages-stub.yml` publishes only a redirect page (source in
  `docs/pages-stub/`, deployed as both `index.html` and `404.html` so deep links forward too), so
  `coamithra.github.io/RotEA26/` sends every old link to the live site. **`--selftest` is now the
  SOLE specification of the eaBuildHash recipe** — there is no workflow left to diff it against,
  so a FAIL there means "I am about to split the player base", never "the test is stale".
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
  8091 (this project's `rotea` signaling server), 80/443 (nginx), plus the fighting game's
  port — **check `ss -tlnp` for the live list before claiming a port**; pick a free one
  for this project.
- **Services:** manage via systemd — `systemctl restart <service>` / `journalctl -u <service> -f`.
  Existing units: `notzelda`, `notzelda-llama`, `rotea` (this project's, port 8091).
- **nginx** serves static files / reverse-proxies. Keep each project's config in its OWN
  included file rather than editing another project's blocks in place: rotea ships
  `server/signal/nginx-location.conf`, installed as `/etc/nginx/rotea-locations.conf` and
  pulled into the shared `notzelda.haraldmaassen.com` 443 block by a one-line `include`
  (deliberately NOT a new vhost — the game is served from that same host).
- **Deploy:** `ssh` in, update `/opt/<PROJECT_NAME>`, then restart the project's service. **This
  project's `/opt/rotea` is NOT a git checkout** — it is an scp'd copy of `server/signal/`, so
  there is no `git pull` to run; follow `server/signal/README.md` → "Updating an existing
  deployment" (stage to `server.new`, run `test_signal.py` there, swap, `systemctl restart rotea`).
  **Merging a PR deploys nothing** — neither the server (manual) nor the game (manual, see
  `docs/DEPLOY.md`); a networked client feature needs both, or the live site talks to a server
  that does not speak its protocol.
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
  meridian-side — **same-origin and RELATIVE since the 2026-08-01 cutover**: `../meridian/`
  and an empty `GAME_ORIGIN`, because both now sit under the same web root). To add a game: cover art + one `games.json`
  entry in meridian, one `eaQuit`-style handoff in the game. **To edit the decoy/launcher, work in
  the meridian repo** (deploy with its `tools/deploy.py`; prefix `MSYS_NO_PATHCONV=1` in Git Bash).

## Don'ts

- Don't commit `bin/`/`obj/` or the raw 52 MB Xbox package (all `.gitignore`d).
- Don't re-run `tools/*.py` codegen against `Game/` (regenerates it from scratch).
- Don't hand-edit generated assets (`.mgfxo`, `.dds`/`.rtex`, `music.json`, packed sheets, ...) —
  re-run the owning tool (tools/CLAUDE.md).
- Don't trust a screenshot of a backgrounded tab (black canvas / paused rAF) — foreground Chrome
  only, and measure perf with the tab focused.
