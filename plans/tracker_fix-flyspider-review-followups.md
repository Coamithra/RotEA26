# Tracker: fix/flyspider-review-followups

Card `6eb8dc9e` — Review follow-ups: feature/flyspider-flatten-cost (9 checklist items).
User constraint this session: **no live/browser testing** (user is overwatching) — verify via
build + IL/decompiled-diff oracle + code reading, and flag what still needs a browser pass.

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab`) -> 6eb8dc9e
- [x] Pull latest main
- [x] Read card + checklist (no comments, no linked plan)
- [x] Create worktree `.claude/worktrees/wt1` + branch `fix/flyspider-review-followups`

## Phase 2: Research
- [ ] Read FlyingSpider.cs (Setup, bench grid)
- [ ] Read FlyingSpiderSwarm.cs (ComponentRemoved, members clear)
- [ ] Read Level2.cs (call-site comment, field decls)
- [ ] Read DebugFlags.cs (IsOn/IsExplicitlyOff, flyspidercount/box parsing)
- [ ] Read IComponentWatcher + Floor reference impl
- [ ] Read web CLAUDE.md console QA helper index

## Phase 3: Design
- [ ] Write plan `plans/flyspider-review-followups.md`
- [ ] Present to user, get approval
- [ ] Post TLDR comment on card

## Phase 4: Implement
- [ ] 1. FlyingSpider.Setup clears benchIndex/benchCount/netForcedColorIndex (should-fix)
- [ ] 2. Level2.cs ?flyspiders call-site comment fix (should-fix)
- [ ] 3. DebugFlags IsOn/IsExplicitlyOff doc-comment orphan (should-fix)
- [ ] 4. FlyingSpiderSwarm -> IComponentWatcher instead of raw += (should-fix)
- [ ] 5. bench grid startheight saturation (nit)
- [ ] 6. ?flyspidercount= fg Collides caveat (nit)
- [ ] 7. members cleared at top of CollectMembers (nit)
- [ ] 8. malformed ?flyspidercount=/?flyspiderbox= warning (nit)
- [ ] 9. web CLAUDE.md helper index + Level2 field block (nit)
- [ ] Update docs (web CLAUDE.md) as needed

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Behaviour-preserving parts proven where applicable (IL / decompiled diff)
- [ ] Diff spot-check (no lowercase content/, no BlendState.AlphaBlend, no codegen re-run)
- [ ] NOTE: browser/console gate deferred — user asked for no live testing this session

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] /review, fix every finding
- [ ] Pull main into branch, resolve conflicts
- [ ] Re-verify
- [ ] PR + self-merge
- [ ] Worktree/branch cleanup, delete plan + tracker
- [ ] Card -> Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview

## Phase 7: Clean up
- [ ] Stop dev servers / close tabs (none expected — no live testing)
