"""Build the laser glow sprites -> wwwroot/Content/gfx/sprites/lazerglow.png + lazerbeam.png.

Why this exists (Trello-adjacent fix: "the lazer's emitted light weirdly cuts off"):
Quad.Draw used GFX/Sprites/singleconnectorglow for its muzzle/tip flares and rounded
end-caps, but that texture is the PlayerShip's decorative connector ORB - concentric
rings whose halo stops dead at ~65% of the frame (measured radial profile: ring bumps
at r~0.4 / r~0.6, then an abrupt drop to zero). Scaled up 2-3x the beam width and drawn
additively, that hard ring edge reads as the emitted light "cutting off" in a circle
around the muzzle. The player-ship glow keeps the ringed orb (intentional art at small
scale); the laser gets THIS texture instead - a clean radially-symmetric glow whose
falloff is smooth all the way to exact zero, so the flare light dissolves into space.

Profile (contribution I(r), r in half-frame units 0..1):
  - saturated white-hot core out to r~0.15 (sum of gaussians clamps at 1),
  - gaussian halo calibrated to the OLD texture's apparent size (half brightness at
    r~0.3, ~15% at r~0.5) so on-screen flare size is unchanged,
  - a faint wide tail replacing the old hard stop, windowed to EXACT zero by r=1.

Emission trick: additive contribution is RGB * A, so both channels get sqrt(I) - the
product is I, and the low end of the gradient gets finer effective quantisation than
putting the whole profile in one 8-bit channel (less banding in the dim tail). The
texture is only ever drawn additively (Quad.DrawFlare); it is not authored for
NonPremultiplied use.

lazerbeam.png is the companion BEAM capsule texture: a radial CONE (linear 1 -> 0)
whose centre row reproduces lazermiddle's measured across profile (a near-linear
triangle, ~0.95 peak). BOTH beam layers (wide blue glow + hot core) draw as capsules
of this texture, so each keeps the exact across profile the old lazermiddle strip gave
it - same apparent glow width, same core body - while the ends fade radially to zero
instead of stopping at the strip's flat edge (the residual hard line at the tip/muzzle
row - additively-drawn caps brighten a step but can never erase it). The gaussian
lazerglow above is ONLY for the round flares; using it for the beam glow visibly
narrowed the halo (its energy hugs the centre vs the triangle's linear spread).

Deterministic, offline (numpy + Pillow), like the sibling tools/ asset steps - CI just
ships the committed PNGs. Re-run after changing knobs; don't hand-edit the outputs.
"""

from pathlib import Path

import numpy as np
from PIL import Image

# --- knobs -------------------------------------------------------------------
SIZE = 512  # frame px; a smooth gradient needs no more

# I(r) = min(1, sum of gaussians) * edge window. Amplitudes/sigmas calibrated so the
# apparent disc matches the old singleconnectorglow footprint (see module docstring).
GAUSSIANS = [
    (1.05, 0.16),  # hot core
    (0.62, 0.34),  # main halo (half-brightness ~ r 0.3)
    (0.18, 0.55),  # faint wide tail - the "emitted light" that used to cut off
]
WINDOW_START, WINDOW_END = 0.80, 1.00  # smoothstep to exact 0 at the frame edge

# lazerbeam: linear cone, peak matched to lazermiddle's measured across profile
# (edge->centre->edge samples 6..243..5 of 255, i.e. a ~0.95-peak triangle).
CONE_PEAK = 0.95

SPRITES = (
    Path(__file__).resolve().parents[2]
    / "web/EvilAliensWeb/wwwroot/Content/gfx/sprites"
)


def radial() -> np.ndarray:
    half = SIZE / 2.0
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    # pixel-centre radius in half-frame units
    return np.hypot(yy + 0.5 - half, xx + 0.5 - half) / half


def to_image(intensity: np.ndarray) -> Image.Image:
    # sqrt split across RGB and A: additive contribution RGB*A == intensity
    chan = np.round(np.sqrt(np.clip(intensity, 0.0, 1.0)) * 255.0).astype(np.uint8)
    rgba = np.stack([chan, chan, chan, chan], axis=-1)
    return Image.fromarray(rgba, "RGBA")


def build_glow(r: np.ndarray) -> Image.Image:
    intensity = np.zeros_like(r)
    for amp, sigma in GAUSSIANS:
        intensity += amp * np.exp(-((r / sigma) ** 2))
    intensity = np.clip(intensity, 0.0, 1.0)
    # force a smooth landing on exact zero at the frame edge (no hard texture border)
    t = np.clip((WINDOW_END - r) / (WINDOW_END - WINDOW_START), 0.0, 1.0)
    intensity *= t * t * (3.0 - 2.0 * t)
    return to_image(intensity)


def build_beam(r: np.ndarray) -> Image.Image:
    # linear cone: centre row == lazermiddle's triangle across profile. The pixel grid
    # never samples r exactly 1.0 along the edge rows, so window the last few percent
    # to guarantee the border quantises to EXACT zero (no hard texture edge).
    intensity = CONE_PEAK * np.clip(1.0 - r, 0.0, 1.0)
    t = np.clip((1.0 - r) / 0.06, 0.0, 1.0)
    intensity *= t * t * (3.0 - 2.0 * t)
    return to_image(intensity)


def main() -> None:
    r = radial()
    SPRITES.mkdir(parents=True, exist_ok=True)
    for name, img in (("lazerglow.png", build_glow(r)), ("lazerbeam.png", build_beam(r))):
        out = SPRITES / name
        img.save(out)
        a = np.asarray(img).astype(float)
        contrib = (a[..., 0] / 255.0) * (a[..., 3] / 255.0)
        print(f"wrote {out} ({SIZE}x{SIZE})")
        print(f"  edge max contribution: {contrib[0].max():.4f} / {contrib[-1].max():.4f}"
              f" / {contrib[:, 0].max():.4f} / {contrib[:, -1].max():.4f} (must all be 0)")


if __name__ == "__main__":
    main()
