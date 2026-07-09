# Tracker: feature/loading-keepalive-idb-screenshots

Bundles two cards. Orchestrator overrides: NO live browser verification; skip /review;
PAUSE before merge (stop after gh pr create --fill).

## Card a5145e9e - Migrate screenshot storage to IndexedDB
- [x] Read card + touch points (SaveInterop, eaSave, StorageStub)
- [x] JS: add eaSaveBlob (IndexedDB) store in index.html
- [x] JS: eaSave routes .dat -> IndexedDB, else localStorage; load merges both
- [x] C#: stays backend-agnostic (comments updated)
- [x] First-boot migration of existing localStorage .dat (delete only after IDB commit)
- [x] Review fixes: onblocked handler + 3s timeout race (boot can never hang)
- [x] dotnet build -c Debug clean
- [x] dotnet publish -c Release clean (runtime round-trip = orchestrator live-test)
- [x] Commit (card a5145e9e)

## Card fe25712a - Keep browser responsive during level preload
- [x] Orchestrator steer received: Option A driven by preload manifest (no hand lists)
- [x] LoadProfiler.ManifestAssets accessor (shipped + ?loadlog localStorage set)
- [x] Game1.WarmThenLaunch/PumpLevelWarm: one manifest decode per tick before scene Add
- [x] All launch paths gated (MenuFinished incl. attract demos, LaunchLevelDirect)
- [x] Menu frozen during warm (Enabled=false; re-fire prevented; re-Add re-enables)
- [x] LoadProfiler accounting: warm bracketed BeginPreload/EndPreload (watchdog quiet)
- [x] CLAUDE.md bullets (both cards)
- [x] dotnet build -c Debug clean
- [x] Commit (card fe25712a)

## Ship
- [ ] gh pr create --fill  -> PAUSE, report to orchestrator
- [ ] (after approval) pull main into branch, re-build, merge, cards -> Done, cleanup
