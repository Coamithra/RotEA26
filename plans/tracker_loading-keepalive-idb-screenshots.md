# Tracker: feature/loading-keepalive-idb-screenshots

Bundles two cards. Orchestrator overrides: NO live browser verification; skip /review;
PAUSE before merge (stop after `gh pr create --fill`).

## Card a5145e9e — Migrate screenshot storage to IndexedDB
- [x] Read card + touch points (SaveInterop, eaSave, StorageStub)
- [ ] JS: add eaSaveBlob (IndexedDB) store in index.html
- [ ] JS: eaSave.load surfaces migration of any existing .dat from localStorage
- [ ] C#: SaveInterop blob-store methods (async preload at boot -> sync reads)
- [ ] C#: StorageStub routes .dat -> IndexedDB, .xml -> localStorage
- [ ] First-boot migration of existing localStorage .dat
- [ ] dotnet build -c Debug clean
- [ ] dotnet publish -c Release clean (reflection/trim safety)
- [ ] Commit (card 1)

## Card fe25712a — Keep browser responsive during level preload
- [ ] Decide approach (design fork — see notes); confirm with orchestrator if invasive
- [ ] Implement warm-across-ticks + loading overlay
- [ ] Keep LoadProfiler bracketing correct
- [ ] dotnet build -c Debug clean
- [ ] Commit (card 2)

## Ship
- [ ] gh pr create --fill  -> PAUSE, report to orchestrator
- [ ] (after approval) merge, cards -> Done, cleanup
