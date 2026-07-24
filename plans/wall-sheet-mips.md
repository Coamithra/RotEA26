# Mip chain for the wall sheet 756-v1

Trello card `110153c7`. Follow-up to `0f7fc977` (tower side texturing).

## Context

`Wall.DefaultSideTile` was baked at 4 by card `0f7fc977`, so a tower shaft now spends
`4 * shaftWorld / blockW` ≈ **10.8 cells** of the sheet over its length instead of exactly one.
The far end of a shaft is therefore minified hard — and `756-v1.dds` ships with
`dwMipMapCount = 1`, so the only filtering it gets is bilinear. That aliases, and because the
wall SCROLLS it shimmers, which no still frame can show.

Facts established during research (all measured, not assumed):

| Fact | Value |
|---|---|
| `756-v1.dds` | logical **1248x1248**, padded **1348x1348**, `mips=1` |
| Why padded at all | the `--padtest 100` over-pad canary, **deliberate** (web CLAUDE.md). 1248 is already a mult-of-4, so its *minimal* pad is zero |
| Game canvas GL context | **WebGL 2** (probed live: `getContext('webgl2')` non-null, `webgl` null), `WEBGL_compressed_texture_s3tc` present |
| NPOT + mipmaps | legal on WebGL 2 (would be invalid on WebGL 1 — this was the feasibility gate) |
| KNI mip filtering | **automatic**: `ConcreteGraphicsContext` passes `useMipmaps = LevelCount > 1`, and `TextureFilter.Linear` then selects `LINEAR_MIPMAP_LINEAR`. No sampler change needed |
| KNI per-level upload | `Texture2D.SetData(level, null, bytes, off, n)` -> `compressedTexImage2D(level, ...)`; expected `n` is exactly `ceil(w/4)*ceil(h/4)*16`, matching the DDS layout |
| KNI level sizing | `GetSizeForLevel` = successive `/2` (min 1) — identical to texconv/D3D |
| Sole consumer of the sheet | `Wall` only (`LoadAnimation` at `Wall.cs:248`) — tops drawn as sprites, shafts as 3D geometry |

### The pad/mip interaction, measured

The card warned that "mip levels of a padded texture blend content with pad as they shrink".
Simulating texconv's box chain on the padded canvas and reading the **alpha** delta between the
last content texel and the first pad texel (RGB deltas are just the sheet's own detail — which
is exactly why `check_pad_bleed` calibrates per texture):

| gutter | L0 | L1 | L2 | L3 | L4 | L5 |
|---|---|---|---|---|---|---|
| **4 px (what ships today)** | 0 | 0 | 0 | **127.5** | **191.2** | **223.1** |
| whole pad edge-replicated | 0 | 0 | 0 | 0 | 0 | 0 |

So a naive `texconv -m 0` on the current padded canvas **reintroduces exactly the pad bleed
`check_pad_bleed.py` guards against**, starting at level 3. A 4 px gutter covers `log2(4) = 2`
levels and no more.

Second, subtler failure: the runtime UV of the logical edge is `1248/1348 = 0.92582`, but the
logical/padded *ratio* drifts per level (`156/168` at L3, `19/21` at L6). From **level 6** that
UV lands one texel into the pad — harmless only if that texel is a replica of the edge.

## Design

**The pad is metadata, not content — so it must be re-derived per mip level, not filtered along
with the content.** That is the one option that keeps everything: correct mips, the edge gutter's
clamp property at every level, and the over-pad canary intact.

### 1. `tools/textures/build_textures.py` — opt-in per-level mip build

- `textures.config` gains an optional `mip` keyword on a `dxt` line. Only `gfx/base/756-v1`
  takes it; the other ~123 `.dds` are untouched (mipping everything would cost +33% bytes
  project-wide and change sprite appearance everywhere for no benefit).
- For a mipped entry, `build_dxt` builds the chain itself rather than passing `-m 0`:
  for each level *k*, downsample the **logical** image to `logical >> k` (successive `//2`,
  `Image.BOX` — area average, the standard mip filter and what `GetSizeForLevel` expects),
  pad it to `padded >> k`, run `edge_gutter()` on *that* level, compress it with a single
  `texconv -m 1`, and splice the payloads into one DDS with `dwMipMapCount` / `DDSD_MIPMAPCOUNT`
  / `DDSCAPS_COMPLEX|MIPMAP` set.
- The logical size stamp (`poke_dds_logical`) is unchanged — it still describes level 0, which is
  what `TextureDims` registers and every pixel-space consumer reads.
- **The full chain must be shipped** (11 levels for 1348): KNI allocates
  `CalculateMipLevels(...)` levels and GL requires a mipmap-**complete** texture, so a short
  chain renders black.

### 2. `tools/textures/check_pad_bleed.py` — extend the guard across mip levels

Same property, asserted at every level: the texel one step outside the logical edge equals the
edge texel, calibrated against that level's own column-to-column step. Mip levels are decoded by
splicing the level's blocks behind a synthesized single-level DDS header and handing it to
Pillow — no new BC decoder, and it reuses the exact decode path the level-0 check already trusts
(verified working on a real 11-level chain, including the non-mult-of-4 and 2x2/1x1 levels).

This guard is **non-vacuous**: it fails on a naive `texconv -m 0` build and passes on the
per-level build. I will demonstrate both.

### 3. `Compat/WebContentManager.cs` — upload the levels

Without this nothing changes: `TryLoadDds` currently hard-codes `new Texture2D(gd, w, h, false, fmt)`
and a single `SetData(0, ...)`. Read `dwMipMapCount`, construct with `mipMap: levels > 1`, and
loop `SetData(level, null, data, off, blockBytes(level))`. Unmipped `.dds` keep the exact
current path.

### 4. `Compat/DebugFlags.cs` — `?nomips`

Uploads level 0 only, so mips can be A/B'd live in the real game. Baked behaviour is unchanged
when absent (the project's standard flag convention).

## Verification

Per the repo rule, the game is the *last* check, not the first.

1. **`check_pad_bleed.py`** over the rebuilt sheet — clean at every level; and shown failing on a
   naive `-m 0` build so the guard is proven to bite.
2. **`preview_wall3d.py --ladder` gains a mip-aware sampler.** Today its `sample()` is bilinear
   clamp and the tool "deliberately models no mips", so it cannot show the fix. Add trilinear:
   per-pixel LOD from screen-space UV derivatives (finite-difference the UV at `px+1`/`py+1`,
   `lod = log2(max(|d(uv)/dx|, |d(uv)/dy|) * texsize)`), sample two levels, lerp.
   **Gotcha to respect:** the derivative must be taken on the *unwrapped* cell-walk coordinate —
   differencing after the `% 8` wrap spikes a full cell at every crossing and fakes a huge LOD.
   Then re-run the ladder (tiling 1 / 2 / 4 / 8) with and without mips. This is also what
   answers the card's fallback question — whether mips are worth it versus dropping
   `Wall.DefaultSideTile` from 4 toward 2 — as data rather than opinion.
3. **Live Level 3** (`?level=Level3&wallsonly&invuln`) in real Chrome: clean Debug build, the
   change visible, zero console exceptions, and an A/B against `?nomips`.

Note the blast radius includes the tower **tops**, not just the shafts: they are sprite-drawn at
~5x minification (156 px cell -> ~32 px block), so they will now pick a mip too. That should be
an improvement (they alias today as well), but it is a visual change and gets checked, not
assumed.

## Cost

The mipped padded chain is ~2.43 MB vs 1.82 MB today: **+610 KB (+33%)** on one Level-3 asset.
33% is the standard full-chain overhead; the padded-vs-logical part of it is the canary's
already-accepted price.

**The alternative considered and rejected:** build this one sheet *unpadded* (its minimal pad is
zero, so mips would be trivially clean and the chain only +260 KB). Rejected because it silently
drops the over-pad canary from the sheet whose UV code web CLAUDE.md names as the primary
padded-vs-logical consumer — for a saving the repo has already decided it does not want
(the canary is deliberately left on at a project-wide 17% cost). Flagging it as the cheaper
option if the byte cost turns out to matter more than the canary.

## Out of scope

- Mipping any other texture.
- Re-tuning `Wall.DefaultSideTile` — the ladder will produce the data, but changing the baked
  value is a separate decision (and the card's *fallback*, not its ask).
- Anisotropic filtering: KNI's `ConcreteSamplerState` throws `NotImplementedException` if
  `SupportsTextureFilterAnisotropic`, so it is not reachable on this backend.
