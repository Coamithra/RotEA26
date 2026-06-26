# Tracker: feature/channel-swap-sound

Card: "need a 'static channel swap' sound for when the 'I made this' screen switches"
(id `5c2d875d667fa112686ee8c1`)

## Phase 1: Pick Up the Card
- [x] Claim top card (Backlog -> In Progress) via atomic `grab`
- [x] Pull latest main
- [x] Read the card (empty desc -> investigate channel-flip splash)
- [x] Create worktree wt2 + branch feature/channel-swap-sound

## Phase 2: Research
- [x] SplashScene channel-flip: fires at stateTimer >= holdMs (FlipProgress()>0), splash index 1, gated by variantPicked (shader+reveal present)
- [x] SFX path: Scene.SoundManager.PlayCue("cue") -> Content/sfx/<cue>.wav (SoundEffect.FromStream, PCM WAV)
- [x] No static sound asset exists; must synthesize one (port addition, not from XACT banks)
- [x] Autoplay caveat: splash is pre-gesture; static sounds only once AudioContext unlocked (any click/key). Acceptable; don't add a click-gate.

## Phase 3: Design
- [x] New offline generator tools/audio/build_channelswap.py -> wwwroot/Content/sfx/channelswap.wav (deterministic, numpy)
- [x] Play once when the flip fires: guard flag reset in BeginDisplay, triggered in Update displaying case
- [x] Optional SoundManager _cfg entry for volume/no-vary

## Phase 4: Implement
- [x] Write generator + produce channelswap.wav
- [x] Wire SplashScene to play it at flip start
- [x] SoundManager cue config
- [x] Update CLAUDE.md (new SFX + generator)

## Phase 5: Verify
- [x] dotnet build -c Debug clean
- [x] Run, real Chrome, watch splash flip; confirm sfx/channelswap.wav fetched + no console exceptions
- [ ] Note autoplay caveat for manual check

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main, rebuild
- [ ] PR + self-merge
- [ ] Clean worktree/branch
- [ ] Delete tracker
- [ ] Card -> Done + comment + follow-ups
- [ ] User overview
