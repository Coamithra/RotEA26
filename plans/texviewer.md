# Texture-format viewer (`?texviewer`) — design

Card `00380ca2` "Revisit per-sprite texture format (dxt vs raw vs png)". The user's comment is the
spec: a dedicated in-game viewer that shows the **compressed (DXT)** and the **raw** version of each
sprite, lets you **flip** between them to scrutinise artifacts, has a **button to select** a format,
**cycles through all images**, and **locks decisions into `textures.config`**.

Decisions (confirmed with user):
- **In-game C# scene** (renders both textures through the REAL game GPU pipeline — the actual
  ANGLE→D3D11 BC3 decode, so artifacts are exactly what ships), reached via `?texviewer`.
- **Save writes the config only** (no auto-rebuild) — user re-runs `build_textures.py` afterward.

## Why "raw" == the PNG decode
`.rtex` is uncompressed straight-alpha RGBA8 = pixel-identical to the PNG's StbImageSharp decode. So
the meaningful comparison is **PNG-decoded pixels (the lossless reference) vs the DXT-decoded pixels**.
The viewer only needs a `.dds` per candidate to compare against the original `.png`.

## Components

### 1. `tools/textures/build_texviewer.py` (new, offline, dev-only)
Builds a DXT preview for every Content PNG into a SCRATCH dir the game doesn't ship or auto-load:
`wwwroot/Content/texviewer/<asset>.dds` (BC3 via texconv, reusing build_textures' mult-of-4
pitch-preserving crop) + `wwwroot/Content/texviewer/manifest.json`:
`[{ asset, w, h, cols, rows, pngBytes, ddsBytes, rawBytes, current }]` (`current` = existing
textures.config decision: dxt|raw|png). Sorted by pngBytes desc (expensive first). Outputs GITIGNORED
(local-only; never shipped — production uses the real siblings from build_textures.py). Grid defaults
1×1; known sheet grids seeded from textures.config + a small table. `--dry-run`, `--only <glob>`.

Kept SEPARATE from build_textures.py so the viewer's throwaway previews never touch the shipped
Content siblings / PrecompiledTextures.cs.

### 2. `Compat/TexViewerScene.cs` (new)
Harness-style scene (models HarnessScene). Loads manifest.json via TitleContainer. Per current asset,
loads BOTH textures directly (bypassing WebContentManager's sibling preference):
- **raw**: `<asset>.png` → Texture2D.FromStream (lossless reference)
- **dxt**: `texviewer/<asset>.dds` → the DDS block-parse (real GPU decode, same as WebContentManager.TryLoadDds)

Draws ONE of them (flip A/B) at a shared pan/zoom with POINT sampling so pixels are scrutinised 1:1,
over a checkerboard (alpha visible). HUD: name, dims, png/dds/raw sizes, decode-cost note, current
pick. `+ difference view` (abs diff ×N). Cycles all assets. Esc → menu (like the harness).

### 3. `eaTexViewer` HTML panel (`wwwroot/index.html`, outside `#app`)
Only built when `?texviewer`. Clickable controls (the "button to select" the user asked for):
prev/next + asset name/dims, **A/B flip** (+ hold-to-compare), **format radios DXT / RAW / PNG** with
live file sizes + savings, **zoom** slider, **diff** toggle, and a **Save** button. Drives
`DebugInput.SetTexViewer(...)` → the scene. Save → `POST /api/texdecide`.

### 4. `web/DevServer/Program.cs` — `POST /api/texdecide` (dev-only)
Upserts one line in `tools/textures/textures.config` (walks up from ContentRoot to find the repo).
DevServer is never shipped to Pages, so this write path is dev-only and safe. Fallback if the POST
fails (served without DevServer): `eaTexExport()` dumps the config lines to console + offers a download.

### 5. Wiring
- `DebugFlags.TexViewer` (`?texviewer`), `?texfilter=<glob>`; `Game1` boots the scene like `?castbrain`.
- `DebugInput.SetTexViewer` `[JSInvokable]` interop.
- `.gitignore`: `wwwroot/Content/texviewer/`.
- `wwwroot/texviewer.html`? No — the panel lives in index.html like the other tuners.

## Verification
- `build_texviewer.py` runs, produces dds + manifest (inspect a few dds with texconv).
- Boot `?texviewer` in real Chrome via DevServer (port 5282), zero console errors; flip PNG↔DXT on
  spider_sheet2 / a smooth-gradient glow and confirm the DXT artifacts are visible; pick + Save writes
  a textures.config line (verify the file changed).
- `dotnet build -c Debug` clean.

## Out of scope
- Making the actual per-sprite decisions (that waits on the art rescale — this card builds the TOOL).
- Auto-rebuild on Save (user chose config-only).
- Shipping texviewer previews (gitignored, local dev only).
