# Tracker: feature/classic-clean-vs-lyrics

Card `255e3c7c` — Difficulty-gated classic music: clean instrumental vs lyrics reward.
Follow-up cards: `7329fcd4` (TeamChallenge difficulty), `8fcc7a8e` (Webcam difficulty).

## Phase 1: Pick Up the Card
- [x] Create + claim card (Backlog -> In Progress)
- [x] Create the two follow-up cards
- [x] Pull latest main
- [x] Create worktree wt5 + branch feature/classic-clean-vs-lyrics (port 5285)
- [x] Create this tracker doc

## Phase 2/3: Research + Design (done inline before card)
- [x] Traced PlayMusic(Songs.Classic) -> songFiles cue -> MusicInterop/eaMusic -> music.json
- [x] Confirmed SongInstance is dead except songFiles[] lookup table; no exhaustive Songs switch
- [x] Confirmed CurrentDifficulty is authoritative for difficulty-selected challenges
- [x] Reviewed install_classic.py asset pipeline

Design:
- New Songs.ClassicClean (index 8) + songFiles[8]="classicclean"
- SoundManager.ClassicForDifficulty(): lyrics if CurrentDifficulty >= Hard, else clean
- Tutorial -> ClassicClean (forced); AsteroidChase/ClassicAliens/Braineroids/CrazyGame -> helper
- TeamChallenge -> keep Songs.Classic (lyrics, per user); Webcam -> ClassicClean for now
- install_classic.py -> install both cues from two sources; write both music.json entries

## Phase 4: Implement
- [ ] Songs.cs: add ClassicClean
- [ ] SongInstance.cs: songFiles append "classicclean", size 9
- [ ] SoundManager.cs: ClassicForDifficulty() helper
- [ ] TutorialLevel.cs -> ClassicClean
- [ ] AsteroidChase / ClassicAliens / BraineroidsLevel / CrazyGame -> ClassicForDifficulty()
- [ ] WebcamLevel.cs -> ClassicClean
- [ ] TeamChallenge.cs -> stays Classic (no change / confirm)
- [ ] install_classic.py: parameterize to both cues
- [ ] CLAUDE.md: document the two-variant classic cue + ClassicForDifficulty rule
- [ ] Provisional classicclean.ogg + music.json entry so build compiles/runs (placeholder = copy of classic until user drops real source)

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on port 5285, real Chrome, boot a challenge at Easy (clean) and Hard (lyrics), console clean
- [ ] Tutorial plays clean

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main, resolve conflicts
- [ ] PR + self-merge, confirm Pages green
- [ ] Clean up worktree/branch, delete tracker
- [ ] Move card to Done + comment
- [ ] Overview for user

## BLOCKER
- Real clean instrumental source OGG from the user (new_assets_raw/). Until then, classicclean.ogg
  is a placeholder copy of classic.ogg so the code path is exercised.
