#!/usr/bin/env python
"""
07_horizon.py -- horizon-continuity pass (the VERTICAL-band blend).

Runs AFTER 06_assemble.py. 06 owns the HORIZONTAL-band work (multiband tone
match + graph-cut rock seam + horizontal feather) and produces the terrain
BODY. But 06's silhouette comes from `a = min(Lov, Rov)` (intersection), which
DROPS the horizon wherever a tall feature on one side meets lower ground on the
other (the J7 cliff / J11 step). This pass fixes ONLY the silhouette + the
near-horizon colour at the OVL>0 seams, leaving 06's body untouched.

Separation of concerns (so the two blend families never fight -- see
STITCH_ALGORITHM.md "Horizon continuity"):
  * ALPHA  : owned here. Union along a BLENDED horizon h_blend(x) (continuous,
             no drop), never min.
  * NEAR-HORIZON RGB : owned here. "Carry the taller" -- above the lower of the
             two horizons only the taller side has real rock, so it is drawn
             pure; both sides cross-fade only in the band where both have ground
             (vertical smoothstep feather). Sourced from the PRE-multiband
             `sheared/` quarters, so 06's horizontal tone cross-fade can't bleed
             sky/clean-brown UP into the horizon.
  * DEEP-BODY RGB : left to 06's graph-cut. A vertical HANDOFF feather hands
             from the carry zone (above) to 06's body (below).

Only OVL>0 seams need this (OVL=0 seams just abut after multiband -- their
silhouette is each quarter's own, already continuous from step 04's shear).

Inputs : wip/strip_graphcut.png (06), wip/sheared/mars{n}_{bl,br}.png (pre-mb).
Output : wip/strip_horizon.png  (+ before/after junction crops, sanity report).
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "horizon_out")
os.makedirs(OUT, exist_ok=True)

# MUST match 06_assemble.py
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
MANUAL_OVL = [0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 80, 200]   # J0..J11
BG = np.array([45, 55, 70], np.float32)

# knobs (px, in the 1619x971 quarter space)
FEATHER = 110   # vertical cross-fade depth below each side's horizon (carry->blend)
HANDOFF = 90    # vertical feather from the carry zone down into 06's body
H_SMOOTH = 9    # light horizontal smoothing of the extracted horizon curves


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def col_top(alpha2d, thr=0.5):
    """top-most opaque row per column; NaN where the column is empty. alpha in [0,1]."""
    O = alpha2d.shape[1]
    out = np.full(O, np.nan)
    m = alpha2d > thr
    has = m.any(0)
    out[has] = m.argmax(0)[has]
    return out


def hsmooth(curve, k=H_SMOOTH):
    """box-smooth a per-column curve over its defined span (NaNs preserved)."""
    out = curve.copy()
    idx = np.where(~np.isnan(curve))[0]
    if len(idx) == 0:
        return out
    a, b = idx.min(), idx.max()
    seg = curve[a:b + 1].copy()
    for i in range(1, len(seg)):          # fill interior gaps before smoothing
        if np.isnan(seg[i]):
            seg[i] = seg[i - 1]
    pad = k // 2
    segp = np.pad(seg, pad, mode="edge")
    out[a:b + 1] = np.convolve(segp, np.ones(k) / k, mode="valid")
    return out


def blend_seam(strip, Q, k, m, ovl, c0, Wt):
    """Overwrite the alpha + near-horizon RGB of one OVL>0 seam IN PLACE.

    strip : the assembled RGBA strip (float32, modified in place)
    Q     : list of pre-multiband sheared quarters (float32 RGBA)
    k, m  : left / right quarter indices; ovl : overlap width
    c0    : output column where the overlap starts (== pos[m]); Wt : strip width
    """
    H = strip.shape[0]
    Lov = Q[k][:, -ovl:]               # left quarter's right edge  (raw RGB + true alpha)
    Rov = Q[m][:, :ovl]                # right quarter's left edge
    Lrgb, Rrgb = Lov[..., :3], Rov[..., :3]
    aL = Lov[..., 3] / 255.0           # the two true silhouettes (H x ovl)
    aR = Rov[..., 3] / 255.0

    hL = hsmooth(col_top(aL))
    hR = hsmooth(col_top(aR))
    # presence-aware: where one side is empty in a column, use the other's curve
    hL_e = np.where(np.isnan(hL), hR, hL)
    hR_e = np.where(np.isnan(hR), hL, hR)

    # weight 0..1 across the overlap; pinned so i=0 -> pure left, i=ovl-1 -> pure right
    # (and snapped to whichever side actually exists outside the shared span).
    w = smoothstep(np.arange(ovl) / max(ovl - 1, 1))
    w[np.isnan(hL) & ~np.isnan(hR)] = 1.0
    w[np.isnan(hR) & ~np.isnan(hL)] = 0.0

    h_blend = (1 - w) * hL_e + w * hR_e            # continuous horizon, no drop

    yy = np.arange(H)[:, None]                     # H x 1
    # carry-the-taller colour, each side faded in over FEATHER px below its horizon
    gl = smoothstep((yy - hL_e[None, :]) / FEATHER)
    gr = smoothstep((yy - hR_e[None, :]) / FEATHER)
    wl = (1 - w)[None, :] * np.maximum(gl, 1e-3 * aL)   # alpha-gated: never sample transparent
    wr = w[None, :] * np.maximum(gr, 1e-3 * aR)
    den = wl + wr
    carry = wl[..., None] * Lrgb + wr[..., None] * Rrgb
    s = den > 1e-9
    carry[s] /= den[s, None]

    # silhouette: opaque below h_blend (1px feathered top), union of the two -> no fringe
    below = np.clip(yy - h_blend[None, :] + 0.5, 0.0, 1.0)
    a_new = np.maximum(aL, aR) * below

    # hand off from the carry zone (near horizon) down into 06's body RGB
    h_low = np.fmax(hL_e, hR_e)                    # the LOWER horizon per column (larger y)
    body_w = smoothstep((yy - h_low[None, :]) / HANDOFF)

    out_cols = [(c0 + i) % Wt for i in range(ovl)]
    body_rgb = strip[:, out_cols, :3]              # 06's graph-cut body (already in the strip)
    rgb = carry * (1 - body_w[..., None]) + body_rgb * body_w[..., None]

    strip[:, out_cols, :3] = np.clip(rgb, 0, 255)
    strip[:, out_cols, 3] = np.clip(a_new, 0, 1) * 255.0


def main():
    strip = np.asarray(Image.open(os.path.join(HERE, "strip_graphcut.png")).convert("RGBA")).astype(np.float32)
    H, Wt = strip.shape[:2]
    Q = [np.asarray(Image.open(os.path.join(HERE, "sheared", f"mars{n}_{h}.png")).convert("RGBA")).astype(np.float32)
         for n, h in order]
    W = Q[0].shape[1]
    OVL = list(MANUAL_OVL)
    pos = [0]
    for k in range(1, 12):
        pos.append(pos[-1] + W - OVL[k - 1])
    Wtot = 12 * W - sum(OVL)
    assert Wtot == Wt, f"strip width {Wt} != expected {Wtot} (OVL mismatch vs 06)"

    seams = [(j, (j + 1) % 12, OVL[j]) for j in range(12) if OVL[j] > 0]

    def crop_seam(strip_in, c0, ovl, tag):
        cols = [(c0 + i) % Wt for i in range(-90, ovl + 90)]
        band = strip_in[:, cols]
        m = (band[..., 3] / 255.0) > 0.5
        hz = np.where(m.any(0), m.argmax(0), H - 1)
        cy = int(np.median(hz))
        y0, y1 = max(0, cy - 110), min(H, cy + 110)
        aa = band[y0:y1, :, 3:4] / 255.0
        img = (BG * (1 - aa) + band[y0:y1, :, :3] * aa).astype(np.uint8)
        Image.fromarray(img).save(os.path.join(OUT, tag))

    for j, m, ovl in seams:
        crop_seam(strip, pos[m], ovl, f"before_J{j}.png")         # 06 state (min-alpha drop)
        blend_seam(strip, Q, j, m, ovl, pos[m], Wt)
        crop_seam(strip, pos[m], ovl, f"after_J{j}.png")
        print(f"J{j:<2} {order[j][0]}{order[j][1]}|{order[m][0]}{order[m][1]}  ovl={ovl}  cols [{pos[m]}..{pos[m]+ovl})  blended")

    Image.fromarray(np.clip(strip, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(HERE, "strip_horizon.png"))
    print(f"\nwrote wip/strip_horizon.png  {Wt}x{H}")

    # --- sanity: the pass must not ADD interior translucency or floating terrain.
    # (The matte's horizon edge is a multi-px soft ramp, so a strict "transparent
    # directly above" test mis-flags legit AA; tolerate a few px, and judge by the
    # DELTA vs 06's baseline rather than an absolute count.)
    def metrics(a):
        partial = (a > 3) & (a < 252)
        soft_edge = np.zeros_like(partial)
        for d in (1, 2, 3, 4):                              # any transparent pixel within 4px above = silhouette AA
            e = np.zeros_like(partial); e[d:, :] = a[:-d, :] < 10; soft_edge |= e
        interior = partial & (~soft_edge)
        opaque = a > 200
        trans_below = np.zeros_like(opaque); trans_below[:-1, :] = a[1:, :] < 10
        floating = opaque & trans_below; floating[-1, :] = False
        return int(interior.sum()), int(floating.sum())

    base = np.asarray(Image.open(os.path.join(HERE, "strip_graphcut.png")).convert("RGBA"))[..., 3]
    bi, bf = metrics(base)
    hi, hf = metrics(strip[..., 3])
    print("\nsanity (interior translucency / floating terrain, vs 06 baseline):")
    print(f"  interior semi-transparent : {hi:6d}  (baseline {bi:6d}, delta {hi - bi:+d})")
    print(f"  floating-terrain pixels   : {hf:6d}  (baseline {bf:6d}, delta {hf - bf:+d})")
    print("  RESULT:", "PASS -- no translucency/floaters added" if hi <= bi and hf <= bf + 8
          else "CHECK -- pass increased translucency/floaters")


if __name__ == "__main__":
    main()
