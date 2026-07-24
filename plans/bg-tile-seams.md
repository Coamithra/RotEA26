# Background tile seams + bright ridge line (card `4ddcd13f`)

## Context

Levels 2/3 grew thin vertical lines at every background tile boundary after the move to
DXT-compressed textures (commit `189fae8`). Two visible flavours, one cause:

- a **dark** hairline running the full sky height (the card's screenshot);
- a **bright** hairline inside the far-hills band (the card's "bright vertical line on the
  right hand side", which it guessed was "the fog or darkening texture being too small").

Reproduced live at `?level=Level2`: dark seam at design x≈41 (top → the ground line),
bright seam at design x≈625 confined to the hills band (design y≈383..428).

## Root cause

`build_textures.py` pads every `.dds` up to the mult-of-4 that BC3/ANGLE requires, filling the
pad with **transparent black**, and stamps the pre-pad ("logical") size into the DDS header.
Draw sites correctly clamp their source rect to `LogicalBounds()` — but `SamplerState.LinearClamp`
only clamps at the *texture* border, not at the source rect. A destination pixel whose centre
maps to `u ∈ (LW-0.5, LW)` texels bilinearly blends the last content texel with texel `LW`, and
texel `LW` is the transparent pad — a real texel *inside* the texture. So the last ~1 screen px
of every tile loses up to 50% of its RGB **and** its alpha.

The sign of the artifact is just what sits behind the layer:

| Layer (`Background.SetMars`) | Content at its edge | Seam reads as |
|---|---|---|
| `clouds-background` sky | opaque (A=255) over a cleared frame | **dark** |
| `marshills1-3` ridges | dark silhouette over the brighter sky | **bright** |
| `marsloop1..12` floor strip | opaque ground | dark |
| `clouds-foreground2` haze | light haze (A≈24) | faint dark |
| Level 3 `756` floor / `2331-v5` fog | same tiling path | dark |

Verified against the shipped bytes: for `clouds-background` the last content column (x=1023) is
`A=255`, and the very next column (x=1024) is `A=0, RGB=0`.

Non-mult-of-4 assets have a second, smaller contribution: `marsloop*` are 1587/1588 px wide, so
the final BC3 block mixes content with transparent-black texels and its endpoints get dragged
dark even before filtering.

This is *not* the ~100px gap class of bug fixed by card `680f91f8` — those sites were converted
to logical dims and are correct. This is sub-texel filtering, invisible to any size audit.

## Design

### 1. Pipeline fix — replicate the logical edge into the pad gutter

`tools/textures/build_textures.py`: after pasting the source at (0,0), fill the first
`min(pad, 4)` px of the pad by **replicating the logical edge** — last column rightward, last row
downward, corner pixel into the corner block. The rest of the pad stays transparent.

Why this is sufficient and complete:

- With `-m 1` (no mipmaps) and `LinearClamp`, a source rect of `[0, LW)` can only ever reach
  texel `LW`. Making texel `LW` a copy of texel `LW-1` makes the filtered result **identical to a
  true clamp**, so the seam cannot exist — at *any* pad size, including the 0–3 px ship pad.
- Rounding the gutter up to a full 4×4 BC3 block means every block the sampler can touch contains
  only content, so the edge-block endpoint contamination on `marsloop*` goes away too.

Why not the alternatives: insetting the source rect by half a texel loses content and still leaves
a wrap seam; cropping to a mult-of-4 changes the tiling period and throws away pixels.

### 2. Canary preserved

Card `f2621e52` (**Later**) deliberately ships `--padtest 100` as a live canary for code that
reads the padded size instead of the logical one. Only the first 4 px of that 100 px pad become
opaque, so the canary still shows an obvious transparent hole for a genuine size bug — it just
stops manufacturing seams of its own. **Rebuilding at `--padtest 0` stays out of scope** (that
*is* card `f2621e52`).

### 3. Verification tools (new, part of this card)

- **`tools/textures/check_pad_bleed.py`** — offline regression guard. Decodes every shipped
  `.dds`, and for each asserts the texel immediately outside the logical edge equals the edge
  texel (column, row, corner). A pass is a deterministic proof that bilinear == clamp at the
  logical edge; it fails loudly if a future rebuild regresses. `--verbose` lists per-asset deltas.
- **`?bgfreeze=<designX>`** — freezes background scroll and parks *every* layer's tile boundary at
  design x. The layers scroll at different speeds (0.3 / 0.33 / 0.53 / 0.85 / 1.0 / 2.5), so a
  timed live screenshot can never be reproduced; with this flag the sky, hills, floor and haze
  seams stack at one screen column and before/after shots are pixel-comparable.

## Verification

1. `check_pad_bleed.py` fails on the current assets (proves the diagnosis), passes after rebuild.
2. `?level=Level2&bgfreeze=400&invuln` in real Chrome, before vs after: seam column gone in both
   the sky and the hills band. Same for `?level=Level3&bgfreeze=400&invuln`.
3. Clean `dotnet build -c Debug`; zero console errors on the Level 2 smoke boot.
4. Spot-check that unrelated padded sprites still draw correctly (`?harness=spider`, `?menu`).

## Out of scope

- Rebuilding at `--padtest 0` — card `f2621e52` in **Later**.
- Any dxt-vs-png format re-decisions (`textures.config` is untouched).
- The `.rtex` path (zero entries ship today).
