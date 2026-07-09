# Tracker: fix/audio-loops-warning-draworder-walls

Bundles 4 small cards into one branch/PR (per orchestrator instructions).

## Phase 1: Pick Up the Cards
- [x] Claim all 4 cards (Backlog -> In Progress)
- [x] Pull latest main
- [x] Create worktree wt8 + branch fix/audio-loops-warning-draworder-walls

## Phase 2-4: Per-card research + design + implement

### Card 10175c7b - eyeboss suck-in SFX loops forever
- [x] Read JunkBoss.cs UpdateEyeAnim state machine
- [x] Find suck-in SFX start (looped SoundEffectInstance? -- LazerGenerator.Initialize plays
      "lazercharge", CueConfig loop:true)
- [x] Find where attracting state ends / boss death / level exit
- [x] Fix: KilledBy now removes+nulls suckeffect (mirrors the attracting->normal transition);
      level exit already worked via Game1.Reset()'s per-component Components.Remove
- [x] Check particle/animation state also fails to exit -- UpdateEyeAnim already self-heals back
      to idle regardless of exit path (state != attracting -> eyeFinishing -> idle on frame wrap),
      not actually broken; the "particle effect" the card suspected was the orphaned suckeffect

### Card 7deda68d - spiderhelper mothership warning missing voice
- [x] Read SpiderHelperMothership.cs entrance
- [x] Find generic Warning banner/voice mechanism (compare Boss/MarsBoss entrances) -- it's
      SpiderBoss.WarnHelperIncoming, already using AnimatedMessage/redwarning, just passing
      SoundManager.Texts.Nothing instead of Texts.Warning
- [x] Wire helper entrance to same warning banner+voice -- one-line change to Texts.Warning

### Card 02c0e9c0 - nebula doodad drawn above ClassicAliens grid effect
- [x] Find ClassicAliens grid effect draw + DrawOrder -- Background.SetSimpleSpace's backgroundLayers
      (holoGrid far+near), drawn in Background.Draw's single DrawOrder-0 component
- [x] Find Background doodad draw + DrawOrder -- same Draw() method, doodad drawn after
      backgroundLayers previously
- [x] Fix ordering so doodads draw below grid in ClassicAliens ONLY -- added holoGridFar tracking,
      both grid layers held back and drawn after the doodad
- [x] Verify Level1 earth/andromeda unaffected -- Level1/Demo1 use SetSpace (backgroundLayers stays
      empty, procedural starfield/nearStars instead), so the loop skip is a no-op there

### Card a54cc13a - wall edge line sprites drawn too close to center
- [x] Read Wall.cs edge-sprite placement math
- [x] Check git history/blame around uprez scale change -- single-commit repo, so no diff to see;
      confirmed via measuring the actual committed 756-v1.png (1248px, not 512px) and the line
      texture's alpha bbox (inset near its own right edge, 256px canvas)
- [x] Fix uncompensated term -- line draw scale now derives from texture.Width/line.Width instead
      of a bare `scale * 2f` tuned for the old 512px sheet

## Phase 5: Verify
- [x] dotnet build -c Debug clean (0 errors, only pre-existing warnings)
- [x] Re-read full diff carefully (straight alpha, Content/ casing, no hand-edited outputs -- none
      of these fixes touch content paths or blend modes)
- [x] Write precise live-test checklist per card (in final report to orchestrator)

## Phase 6: Ship (PAUSED before merge per orchestrator override)
- [x] Commit per-card (separate commits)
- [ ] Push branch
- [ ] rtk gh pr create --fill (all 4 card ids in body)
- [ ] STOP - report to orchestrator, do not merge
