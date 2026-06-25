# Tracker: feature/trailers-youtube-embed

Card: `f3b384f7` — Stage 14 — Trailers (re-add via embedded YouTube player)

## Phase 1: Pick Up the Card
- [x] Claim top card (grab Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card + linked machinery
- [x] Create worktree (wt3) + branch feature/trailers-youtube-embed + push

## Phase 2: Research
- [x] Read TrailerScene.cs (video-based, dead) + MenuScene trailer wiring
- [x] Read interop pattern (FullscreenInterop / MusicInterop / ExitInterop)
- [x] Read index.html outside-#app UI + eaFullscreen/eaQuit/eaMusic + Index.razor.cs Init wiring
- Findings:
  - TrailerScene (Game/EvilAliens/TrailerScene.cs) uses XNA Media Video/VideoPlayer +
    Content.Load("VFX/..") — the crash path. Leave it dead; bypass it.
  - MenuScene: trailerMenu (2 entries + Back) + handlers exist, unwired. Options "Trailers"
    AddEntry was removed (comment at ~L281). optionsMenu_OnTrailersSelected dead at ~L768.
  - eaMusic has no pause/resume — add ctx.suspend()/resume() for seamless music pause.

## Phase 3: Design
- [x] Draft approach (below)
- [ ] BLOCKER: get the two YouTube video IDs from user (EvilAliens trailer, Rocket Riot trailer)
- [ ] Align with user / approval

### Approach
1. Compat/TrailerInterop.cs (new) — Init(IJSRuntime) + Play(string youtubeId) -> eaTrailer(id).
2. Pages/Index.razor.cs — TrailerInterop.Init(JsRuntime) alongside the others.
3. wwwroot/index.html:
   - eaTrailer(id)/eaTrailerClose() JS (sibling of eaFullscreen), iframe overlay OUTSIDE #app,
     youtube-nocookie.com/embed?autoplay=1&rel=0, Close/Back button, Esc-to-close, refocus canvas.
   - eaMusic.pause()/resume() (ctx.suspend/resume); call on open/close.
   - overlay CSS.
4. Game/EvilAliens/MenuScene.cs — re-add Options "Trailers" entry; wire trailerMenu_*Selected to
   TrailerInterop.Play(<mapped id>) + re-show trailerMenu (no scene add). Update the removed-comment.
5. CLAUDE.md — document eaTrailer/TrailerInterop seam.
- Out of scope: porting the WMV videos / a real video loader; deleting dead TrailerScene.

## Phase 4: Implement
- [ ] TrailerInterop.cs
- [ ] Index.razor.cs Init
- [ ] index.html eaTrailer/eaTrailerClose + eaMusic pause/resume + CSS
- [ ] MenuScene wiring
- [ ] CLAUDE.md

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on :5283, real Chrome: ?menu -> Options -> Trailers -> pick -> overlay plays, music pauses
- [ ] Esc/Back closes, music resumes, canvas refocuses, 0 console exceptions
- [ ] No VFX/* Content.Load reintroduced; no lowercase content/ path

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review + fix findings
- [ ] Pull main, rebuild
- [ ] PR + self-merge, confirm Pages green
- [ ] Worktree cleanup, delete tracker
- [ ] Card -> Done + comment + follow-ups
