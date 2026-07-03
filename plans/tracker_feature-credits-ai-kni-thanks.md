# Tracker: feature/credits-ai-kni-thanks

Card 25594cb6 "Add Claude Opus 4.6 to 'additional programming' credit"
(+ ChatGPT/Gemini image models to additional graphics; + special thanks to KNI)

## Phase 1
- [x] Move card Backlog -> In Progress
- [x] Pull main
- [x] Read card
- [x] Worktree wt2 + branch feature/credits-ai-kni-thanks

## Phase 2 (research)
- [x] Credits live in Game/EvilAliens/CreditsScene.cs SetupCredits() (lines.Add strings)
- [x] Draw path: base.SpriteBatch is SpriteBatchWrapper -> DrawStringScaled (supersample-safe)
- [x] No separate "ADDITIONAL PROGRAMMING" section exists yet; "PROGRAMMING AND DESIGN" + "GRAPHICS:" + "SPECIAL THANKS TO:" do
- [x] Port-era humble style already present: "voices synthesized by ElevenLabs", "IN LOVING MEMORY OF: Microsoft Sam"
- [x] Longest existing line ~35 chars (x=100, scale 1, no wrap) -> keep additions within
- [x] KNI author verified: Nikos Kastellanos (nkast), MonoGame fork

## Phase 3/4 (design + implement)
- [x] Add "ADDITIONAL PROGRAMMING:" section w/ "Claude Opus 4.6" after PROGRAMMING AND DESIGN
- [x] Add AI image models to GRAPHICS section (tasteful, short)
- [x] Add KNI thanks under SPECIAL THANKS TO (next to XNA team line)

## Phase 5 (verify)
- [x] dotnet build -c Debug clean (0 errors)
- [x] Re-read diff (9 pure-text additions, no stray changes)
- [x] NOTE: no live browser verification per orchestrator override

## Phase 6 (ship - PAUSE before merge per orchestrator)
- [ ] Commit + push
- [ ] /review or self-review
- [ ] PR create --fill
- [ ] STOP - report back
