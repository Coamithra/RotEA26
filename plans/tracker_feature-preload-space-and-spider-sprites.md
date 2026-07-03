# Tracker: feature/preload-space-and-spider-sprites

Batch of FOUR texture-preload-hitch cards, one branch/PR:
- 97727578 Preload space-background tiles (SetSpace cold decodes)
- f75e6f25 flying spiders not preloaded (hitch)
- 2956da8a boss spider gfx not preloaded it seems (gliotch)
- c5d91d6e spiderboss debris sprites loadign causes hitch (not preloaded?)

Orchestrator overrides: no live verification (build + diff re-read + reasoning);
pause BEFORE merge; report live-test items.

## Phase 1: Pick Up
- [x] Move all four cards to In Progress
- [x] Pull latest main (root)
- [x] Read cards (3 spider cards have empty descriptions - work from titles + code)
- [x] Worktree wt1 + branch feature/preload-space-and-spider-sprites
- [x] Pull main into branch post-outage (credits PRs merged, no overlap)

## Phase 2: Research
- [x] LoadProfiler mechanism (BeginPreload/ApplyManifest/EndPreload; manifest.txt)
- [x] Background.SetSpace -> ProceduralStarfield (space00..11) + DriftingStars (star00..07)
- [x] space00..11 are dxt in textures.config, .dds ARE committed; stars PNG-only
- [x] WebContentManager.LoadTexture: profiler times DDS read+SetData GPU upload too
      -> card's "~40ms dxt" = stream read + GPU upload, not PNG decode (finding, note on card)
- [x] Timing: SetSpace runs in level Initialize() BEFORE base.Initialize() -> LoadContent
      -> preload bracket. All within ONE ComponentBin.Update tick (loading tick).
      So decodes land on the loading tick but BEFORE BeginPreload => logged COLD,
      attributed to previous level. Shared ContentManager never Unloads => first
      space scene per session pays it once; later scenes are cache hits.
- [x] Spider cards: found ALL load sites. FlyingSpider: spider_sheet2 + wing1.
      Spider: spiderjump + spider_sheet2. SpiderBoss: blank + GFX/Spider/spiderfly|
      jump|land|stand (via AnimatedSprite) + spiderdebris1-3.
- [x] Level2 + InsaneBossI code preloads cover ALL of those (verified line by line);
      Demo2 covers what it spawns (grounded spiders). Shared manager, lowercased cache
      keys, never Unloaded mid-session => in-level spider hitches are IMPOSSIBLE in
      current code. The ONLY cold site: **CastDisplayer** (credits cast roll, shown
      ONLY after beating Level3 on Hard+ — SetupLevel3 castWillBeDisplayed).
      CastDisplayer uses the SHARED manager; added mid-crawl at the "Well done." line;
      its LoadContent decodes wing1+debris1-3+spiderfly (+alienboss etc.) on that tick,
      and each cast state's EnsureAnimation decodes its sheet the frame the enemy
      appears. Cast sheets NOT in Level3's code preload = spider_sheet2, spiderfly,
      wing1, spiderdebris1-3, mothershipA, mothershipB. These map 1:1 onto the three
      card titles (Spider Wasp = flying spider look; Spider Stag = boss spider;
      debris shower on asplode).

## Phase 3: Design (decided)
- [x] SPACE (97727578): boot-time LOW-PRIORITY warm queue in Game1 (second queue,
      pumped one/tick only after the menu warmQueue empties; NOT drained pre-menu).
      Warm space00..11 + star00..07 + the starwindow effect. Rationale: SetSpace runs
      in each level's Initialize BEFORE base.Initialize -> LoadContent -> preload
      bracket, so manifest/bracket warms are ALWAYS too late; the shared CM never
      unloads, so a one-time boot warm makes every SetSpace a session-long cache hit.
      Race-safe: if a level loads first (?level= boot), SetSpace decodes and the queue
      entries become free cache hits; either order safe.
      Card investigation findings (report on card): (1) dxt tiles timing ~40ms is NOT
      a png fallback — space00..11.dds are committed + listed in PrecompiledTextures.
      Siblings; the profiler stopwatch wraps OpenStream fetch + CopyTo + SetData (GPU
      upload), which is the ~40ms. (2) the decodes are NOT spread over gameplay frames
      — SetSpace runs synchronously inside the level-add tick (same tick as the preload
      bracket); they're logged COLD only because SetSpace precedes BeginPreload. Real
      cost = ~0.5s extra on the FIRST space scene's loading tick per session.
- [x] SPIDER cards (f75e6f25, 2956da8a, c5d91d6e): manifest entries under Level3
      (the scene that always immediately precedes the cast credits): spider_sheet2,
      gfx/spider/spiderfly, wing1, spiderdebris1-3, mothershipa, mothershipb.
      Warmed inside Level3's preload bracket => cast add-tick + all cast states are
      cache hits. Zero code. Residual (note on card): spiderfly.dat (267 B) is still
      a cold TitleContainer stream read at cast add — sub-hitch, acceptable.
- [x] No genuine unresolvable fork; conservative designs chosen. NOTE for report:
      the space card's premise ("spread across first gameplay frames") is slightly
      off per code reading; fix still worthwhile + implemented as above.

## Phase 4: Implement
- [x] Code changes (Game1 idleWarmQueue; manifest Level3 cast block)
- [x] CLAUDE.md updated (idle-warm convention appended to the menu-warm bullet)
- [x] Post-review fixes: [warm] log prefix, UpdateInner call-site comment,
      ?level= non-space edge documented on idleWarmQueue, manifest tradeoff note

## Phase 5: Verify (no live)
- [x] dotnet build -c Debug clean (0 errors, pre-existing warnings only; rebuilt after main pull)
- [x] Full diff re-read vs origin/main (exactly 4 files, all mine; NOTE: local `main`
      ref in the root checkout is stale — always diff against origin/main)
- [x] Live-test items listed in the final report / card comments

## Phase 6: Ship (PAUSE before merge)
- [x] Commit + push
- [ ] Peer review: PENDING — cold reviewer agent spawned but not finished at ship time;
      per orchestrator instruction no self-review substituted. Orchestrator reviews
      before merge; triage any late reviewer findings then too.
- [x] Pull main again (rebased cleanly onto latest; no conflicts in my files)
- [ ] rtk gh pr create --fill
- [ ] Card comments (all four cards)
- [ ] STOP: report to orchestrator (no merge, no Done moves, no worktree cleanup)
