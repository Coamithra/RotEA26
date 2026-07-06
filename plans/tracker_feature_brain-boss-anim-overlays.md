# Tracker: feature/brain-boss-anim-overlays

Card: f5a7b6f8 "Animated overlay patches on the Brain final boss" (In Progress)

## Phase 1: Pick up
- [x] Create card + move to In Progress
- [x] Pull main
- [x] Worktree wt3 + branch + push

## Phase 2/3: Research + design (done inline)
- brainbosshd.png = 1448x1086, DesignFrameWidth 850 -> textureScale ~1.703.
- Fight anchor: Position (400,100), scale pulses 1.0..1.1. Top ~373px of texture is OFF-SCREEN.
  -> Only animate crops with ty0 >= ~400. Keep tx within ~[150,1298] (sides clip near edges).
- Plumbing: ../animgen comfy_client.generate(local-wan14b-lightning: workflow FLF_wan14b_lightning.json,
  node_roles start=9 prompt=7 seed=[12,13] size=11; sets 11.width/height/length; no end_img -> I2V).
  ensure_server() launches ComfyUI with the safe flags. Frames via pipeline.extract.extract_frames.
- Game side: data-driven overlay manifest (Content/data/brainoverlays.json, LandedOffsets pattern),
  BrainBoss.Draw composites feathered patches at texCenter offsets, tracking Position+scale.

## Phase 4: Implement
- [ ] tools/brainanim/regions.json (crop boxes + prompts + settings)
- [ ] tools/brainanim/gen_brain_anims.py (crop -> comfy generate -> extract frames)
- [ ] Run generations; triage motion
- [ ] tools/brainanim/build_brain_overlays.py (feathered sheets + manifest)
- [ ] BrainBoss.cs overlay draw + BrainOverlay loader (Compat or Game)
- [ ] tools/brainanim/preview_ingame.py (bespoke compositor for verification)
- [ ] CLAUDE.md bullet

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Python in-game composite proof (no browser, per user)

## Phase 6: Ship
- [ ] commit / push / /review / PR / self-merge / cleanup / card Done
