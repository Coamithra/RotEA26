# Tracker: fix/death-crossfade-fadeout

Card 9d76f246: "When you die, the crossfade effect is broken. It causes some additive
drawing or something to light up the screen. The effect we want is that the background
remains as-is but the objects (enemies, bullets, etc) fade out."

## Phase 1: Pick Up the Card
- [x] Claim the top card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree (wt3) + branch fix/death-crossfade-fadeout

## Phase 2: Research
- [x] Find the death crossfade / fade code (GameScene.UpdateResetting -> Background.CrossFade)
- [x] Trace how the fade is drawn (XFade RT snapshot -> DrawForeground overlay)
- [x] Identify why it lights up the screen: RGBA8 RT (was Bgr565) inherits patchy/additive alpha
- [x] Summarize root cause

## Phase 3: Design
- [x] Draft approach: Clear RT opaque so alpha stays 1 (Xbox alpha-less parity); keep fade dir
- [x] Align with user (user tested + confirmed "perfect")

## Phase 4: Implement
- [x] Make the change (Background.cs: Clear(Color.Black) on the XFade RT)
- [x] CLAUDE.md: no new contract to document (one-line internal fix)

## Phase 5: Verify
- [x] dotnet build -c Debug clean
- [x] Run + look in real Chrome (died on Medium, watched the fade), console clean; user confirmed

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main into branch
- [ ] PR + self-merge
- [ ] Clean up worktree/branch
- [ ] Delete tracker
- [ ] Move card to Done + comment
- [ ] Overview for user
