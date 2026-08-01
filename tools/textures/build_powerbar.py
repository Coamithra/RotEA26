"""Rebuild the HUD powerbar sprites -> wwwroot/Content/gfx/hud/{barlit,barlitedge}.png.

Why (the powerup/gamma bar's "ugly line"): PowerupData.drawPowerbar (and the since-removed GammaMenu's
copy) fill the bar by source-rect-clipping BarLit at the progress column and stamping
the 20px BarLitEdge cap at the cut. BarLitEdge is literally BarLit's right 20 columns,
BUT the pill's baked-in glow halo is NOT constant along the body - it starts fading
~15px before the right cap (it's a blur of the rounded pill). So at any partial fill,
an interior cut (halo alpha ~51) sits next to the cap's first column (halo alpha ~35):
a ~19% instant drop in the glow above/below the pill = a hard vertical line that rides
the fill point. The cap's darker inner shading (185 -> 168 on the body rows) adds a
smaller dip on the pill itself.

Fix, texture-space only (draw code untouched, covers PowerupData; GammaMenu has since been removed):
- barlit: columns from just after the left cap's rim to the end are replaced by the
  interior cross-section V(y) (mean of cols 40..70), with a short crossfade after the
  rim so the left cap art keeps its look. Any cut in the fill range (col 21..96) now
  lands on identical columns.
- barlitedge: synthesized as a RADIAL SWEEP of V - pixel (x,y) samples V at
  cy +/- sqrt((y-cy)^2 + (k*x)^2), i.e. the cross-section revolved around the pill end
  (slightly squashed, k tuned so the halo tail fills the 20px). Column 0 of the sweep
  IS V(y), so the seam with the fill is continuous BY CONSTRUCTION at every row: body,
  rim and halo all carry straight through the joint. (Same capsule-dome idea as the
  laser's tools/textures/build_lazer_glow.py.)
- barunlit2 is drawn whole (never cut) and stays byte-identical - and it must, so the
  lit fill keeps sitting on the exact original dark silhouette.

Reads the committed PNGs as source; the derivation is IDEMPOTENT (V is taken from
columns the tool itself writes as V), so re-running is safe. Deterministic, offline
(numpy + Pillow); CI ships the committed outputs. Don't hand-edit the PNGs.
"""

from pathlib import Path

import numpy as np
from PIL import Image

HUD = (
    Path(__file__).resolve().parents[2]
    / "web/EvilAliensWeb/wwwroot/Content/gfx/hud"
)

# barlit layout (116x47): left cap art [0..RIM_END], crossfade (RIM_END..CONST_START),
# constant interior [CONST_START..), and the fill is only ever cut at cols 21..96.
RIM_END = 15       # last column of the left cap's bright rim (kept verbatim)
CONST_START = 21   # first column guaranteed identical to V (== the minimum fill cut)
V_COLS = (40, 70)  # interior columns averaged into the cross-section V

# Cap sweep squash: vertical halo reach is the full half-height (23.5 rows); the cap
# must fit it into its 20 columns. k = 23.5/20 also puts the body rounding (~7.5 rows)
# at ~6.4 columns, matching the original cap's footprint.
SWEEP_K = 23.5 / 20.0


def radial_sweep(v: np.ndarray, width: int, k: float) -> np.ndarray:
    """Revolve cross-section v (H x 4) around the pill end into a (H x width x 4) cap.

    Pixel (x, y) samples v at cy +/- sqrt((y-cy)^2 + (k*(x+0.5))^2) (linear interp,
    same side as y so a vertically asymmetric v would be preserved). x is measured
    from the seam column; the sweep's column 0 sits at half a pixel out, so the drawn
    cap continues the fill's last column smoothly.
    """
    h = v.shape[0]
    cy = (h - 1) / 2.0
    yy, xx = np.mgrid[0:h, 0:width].astype(float)
    d = np.sqrt((yy - cy) ** 2 + (k * (xx + 0.5)) ** 2)
    ys = cy + np.sign(yy - cy + 1e-9) * d
    lo = np.clip(np.floor(ys).astype(int), 0, h - 1)
    hi = np.clip(lo + 1, 0, h - 1)
    t = np.clip(ys - lo, 0.0, 1.0)[..., None]
    out = v[lo] * (1.0 - t) + v[hi] * t
    # past the cross-section's ends there is nothing left to sweep -> transparent
    out[ys > h - 1] = 0.0
    out[ys < 0] = 0.0
    return out


def main() -> None:
    lit_img = Image.open(HUD / "barlit.png").convert("RGBA")
    lit = np.asarray(lit_img).astype(float)
    h, w = lit.shape[:2]

    v = lit[:, V_COLS[0]:V_COLS[1]].mean(axis=1)  # (H x 4) interior cross-section

    out = lit.copy()
    # crossfade the columns between the left rim and the constant interior so the cap
    # art meets V without a step (the original rises toward its plateau here anyway)
    for x in range(RIM_END + 1, CONST_START):
        t = (x - RIM_END) / float(CONST_START - RIM_END)
        out[:, x] = lit[:, x] * (1.0 - t) + v * t
    out[:, CONST_START:] = np.broadcast_to(v[:, None, :], (h, w - CONST_START, 4))

    edge_w = np.asarray(Image.open(HUD / "barlitedge.png")).shape[1]
    cap = radial_sweep(v, edge_w, SWEEP_K)
    # barlit's own last columns are never rendered (fill cuts stop at 96), but keep
    # the standalone texture looking like a complete pill
    out[:, w - edge_w:] = cap

    Image.fromarray(np.clip(np.round(out), 0, 255).astype(np.uint8), "RGBA").save(HUD / "barlit.png")
    Image.fromarray(np.clip(np.round(cap), 0, 255).astype(np.uint8), "RGBA").save(HUD / "barlitedge.png")

    # continuity report: max per-row |fill col - cap col 0| contribution step
    fill_last = out[:, CONST_START]
    step = np.abs((fill_last[:, :3].mean(axis=1) * fill_last[:, 3]) -
                  (cap[:, 0, :3].mean(axis=1) * cap[:, 0, 3])) / 255.0
    print(f"wrote {HUD / 'barlit.png'} and {HUD / 'barlitedge.png'}")
    print(f"  max seam step (0..255 luminance): {step.max():.2f}")


if __name__ == "__main__":
    main()
