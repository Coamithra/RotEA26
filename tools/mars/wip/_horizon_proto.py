"""
07_horizon.py -- horizon-continuity blend prototype.

Demonstrates combining two OVERLAPPING terrain quarters so their horizon LINE
transitions smoothly across the overlap with NO vertical drop at either join.

Core idea (height-domain blend, not alpha-domain):
  - each terrain = a height field h(x) = y of the top edge per column
  - over the overlap [xL, xR], blend the two horizons:
        w(x)  = smoothstep((x-xL)/(xR-xL))
        h(x)  = (1-w) * h_left(x) + w * h_right(x)
    w is pinned to the overlap boundaries (w=0 where the RIGHT side starts,
    w=1 where the LEFT side ends), so h(x) == the surviving neighbour's
    horizon exactly at each join -> C0 continuity, no drop.
  - drive pixels to h(x): per-column vertical warp of each side + cross-fade
    by the same w; where h rises above a side's own terrain, source from the
    side that actually has terrain there ("carry the taller").

Run from new_assets_raw/mars_quarters_magenta/.  Outputs into wip/horizon_out/.
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "horizon_out")
os.makedirs(OUT, exist_ok=True)

FEATHER = 80   # vertical cross-fade depth (px) below each side's horizon


def load_mask(name, key):
    im = np.asarray(Image.open(os.path.join(HERE, f"{name}.png")).convert("RGB")).astype(int)
    R, G, B = im[:, :, 0], im[:, :, 1], im[:, :, 2]
    if key == "blue":
        m = (B > 120) & (R < 120) & (G < 120)
    else:
        m = (R > 120) & (G < 120) & (B < 120)
    return im.astype(np.uint8), m


def top_edge(mask):
    """y of the top-most terrain pixel per column; NaN where the column is empty."""
    H, W = mask.shape
    top = np.full(W, np.nan)
    for x in range(W):
        col = np.where(mask[:, x])[0]
        if len(col):
            top[x] = col[0]
    return top


def smooth_curve(h, k=9):
    """light box-smooth over the defined (non-NaN) span; leaves NaNs as-is."""
    out = h.copy()
    idx = np.where(~np.isnan(h))[0]
    if len(idx) == 0:
        return out
    a, b = idx.min(), idx.max()
    seg = h[a:b + 1].copy()
    # forward/back fill any interior gaps so the box filter is clean
    for i in range(1, len(seg)):
        if np.isnan(seg[i]):
            seg[i] = seg[i - 1]
    ker = np.ones(k) / k
    pad = k // 2
    segp = np.pad(seg, pad, mode="edge")
    out[a:b + 1] = np.convolve(segp, ker, mode="valid")
    return out


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def main():
    rgb_l, m_l = load_mask("horizon2", "red")    # LEFT quarter  (x 0..644)
    rgb_r, m_r = load_mask("horizon1", "blue")   # RIGHT quarter (x 171..799)
    H, W = m_l.shape

    h_l = smooth_curve(top_edge(m_l))
    h_r = smooth_curve(top_edge(m_r))

    # overlap = where BOTH sides have terrain
    both = (~np.isnan(h_l)) & (~np.isnan(h_r))
    xs = np.where(both)[0]
    xL, xR = xs.min(), xs.max()
    print(f"overlap band: x={xL}..{xR}  ({xR - xL}px)")

    # --- the blend: weight pinned to the overlap boundaries -------------------
    x = np.arange(W)
    w = smoothstep((x - xL) / float(xR - xL))     # 0 at xL, 1 at xR
    # outside the overlap, snap to whichever side actually exists
    only_left = (~np.isnan(h_l)) & (np.isnan(h_r))
    only_right = (np.isnan(h_l)) & (~np.isnan(h_r))
    w[only_left] = 0.0
    w[only_right] = 1.0

    # blended horizon (use each side's own curve where the other is absent)
    hl = np.where(np.isnan(h_l), h_r, h_l)
    hr = np.where(np.isnan(h_r), h_l, h_r)
    h_blend = (1 - w) * hl + w * hr

    # --- composite pixels onto h_blend ---------------------------------------
    # Silhouette is h_blend in BOTH modes (continuous -> no drop). The modes
    # differ only in where each side's COLOUR comes from:
    #   stretch : translate each side's column so its edge lands on h_blend,
    #             then cross-fade. Near the horizon you get the OTHER side's
    #             rock slid in from elsewhere (the (420,270) purple).
    #   carry   : use each side's terrain IN PLACE (no shift); weight by w
    #             gated by in-place alpha. Above a side's true horizon it has
    #             no rock, so the taller side is carried pure; both sides only
    #             blend lower down where both genuinely have ground.
    yy = np.arange(H)
    out_stretch = np.zeros((H, W, 4), np.uint8)
    out_carry = np.zeros((H, W, 4), np.uint8)
    out_avg = np.zeros((H, W, 4), np.uint8)  # OLD (buggy) alpha=average, for A/B

    def warp_col(rgb, mask, x, dy):
        ys = np.clip(yy - dy, 0, H - 1).astype(int)
        return rgb[ys, x, :], mask[ys, x].astype(float)

    for xi in range(W):
        present_l = not np.isnan(h_l[xi])
        present_r = not np.isnan(h_r[xi])
        if not present_l and not present_r:
            continue
        target = h_blend[xi]
        # 1px-feathered top edge at the blended horizon (clips taller peak too)
        below = np.clip(yy - target + 0.5, 0.0, 1.0)

        # ---- STRETCH (translate-to-h_blend then cross-fade) ----
        cols = []
        if present_l:
            cols.append((*warp_col(rgb_l, m_l, xi, int(round(target - h_l[xi]))), 1 - w[xi]))
        if present_r:
            cols.append((*warp_col(rgb_r, m_r, xi, int(round(target - h_r[xi]))), w[xi]))
        tw = sum(c[2] for c in cols) or 1.0
        acc, wsum, a_max, a_avg = np.zeros((H, 3)), np.zeros(H), np.zeros(H), np.zeros(H)
        for rgbc, ac, wt in cols:
            acc += (wt / tw) * rgbc * ac[:, None]
            wsum += (wt / tw) * ac
            a_max = np.maximum(a_max, ac)
            a_avg += (wt / tw) * ac
        s = wsum > 1e-4
        acc[s] /= wsum[s, None]
        rgb8 = np.clip(acc, 0, 255).astype(np.uint8)
        out_stretch[:, xi, :3] = rgb8
        out_stretch[:, xi, 3] = (np.clip(a_max, 0, 1) * 255).astype(np.uint8)
        out_avg[:, xi, :3] = rgb8
        out_avg[:, xi, 3] = (np.clip(a_avg, 0, 1) * 255).astype(np.uint8)

        # ---- CARRY THE TALLER, with a vertical cross-fade band ----
        # Each side fades IN over FEATHER px below its own horizon, so the
        # transition from "pure taller side" to "both sides blended" is a soft
        # ramp through the band where both genuinely have rock -- not a hard line.
        al = m_l[:, xi].astype(float) if present_l else np.zeros(H)
        ar = m_r[:, xi].astype(float) if present_r else np.zeros(H)
        gl = (smoothstep((yy - h_l[xi]) / FEATHER) if present_l else np.zeros(H))
        gr = (smoothstep((yy - h_r[xi]) / FEATHER) if present_r else np.zeros(H))
        # tiny alpha-gated floor so the taller side wins right at the silhouette
        # (where both ramps are still ~0) instead of dividing by zero.
        wl = (1 - w[xi]) * np.maximum(gl, 1e-3 * al)
        wr = w[xi] * np.maximum(gr, 1e-3 * ar)
        den = wl + wr
        cc = (wl[:, None] * rgb_l[:, xi, :] + wr[:, None] * rgb_r[:, xi, :])
        s2 = den > 1e-9
        cc[s2] /= den[s2, None]
        a_carry = np.maximum(al, ar) * below   # opaque below h_blend (taller side authentic there)
        out_carry[:, xi, :3] = np.clip(cc, 0, 255).astype(np.uint8)
        out_carry[:, xi, 3] = (np.clip(a_carry, 0, 1) * 255).astype(np.uint8)

    out = out_carry  # primary result is now carry-the-taller
    Image.fromarray(out_carry, "RGBA").save(os.path.join(OUT, "combined.png"))
    Image.fromarray(out_stretch, "RGBA").save(os.path.join(OUT, "combined_stretch.png"))

    # --- prove the fringe: composite over a mid-grey background ---------------
    def over(rgba, bg=(128, 128, 128)):
        a = rgba[:, :, 3:4].astype(float) / 255.0
        comp = rgba[:, :, :3].astype(float) * a + np.array(bg) * (1 - a)
        return comp.astype(np.uint8)
    Image.fromarray(over(out_avg)).save(os.path.join(OUT, "over_grey_OLD_avg_alpha.png"))
    Image.fromarray(over(out_stretch)).save(os.path.join(OUT, "over_grey_NEW_max_alpha.png"))
    Image.fromarray(over(out_carry)).save(os.path.join(OUT, "over_grey_carry.png"))

    def partial_count(rgba):
        a = rgba[:, :, 3]
        return int(np.count_nonzero((a > 12) & (a < 243)))
    print(f"\npartial-alpha pixels (translucent fringe):")
    print(f"  OLD  alpha=average : {partial_count(out_avg):6d}")
    print(f"  stretch alpha=max  : {partial_count(out_stretch):6d}")
    print(f"  carry   alpha=max  : {partial_count(out_carry):6d}")

    px = (420, 270)
    print(f"\npixel {px} (x,y)  RGBA:")
    print(f"  stretch (blend) : {tuple(int(v) for v in out_stretch[px[1], px[0]])}")
    print(f"  carry the taller: {tuple(int(v) for v in out_carry[px[1], px[0]])}")

    # --- diagnostic overlay: the 3 curves + join markers ----------------------
    diag = np.full((H, W, 3), 255, np.uint8)
    diag[out[:, :, 3] > 8] = [210, 210, 210]  # combined fill (grey)

    def draw(curve, color):
        for xi in range(W):
            if np.isnan(curve[xi]):
                continue
            y = int(round(curve[xi]))
            if 0 <= y < H:
                diag[max(0, y - 1):y + 2, xi] = color

    draw(h_l, [220, 40, 40])     # left horizon (red)
    draw(h_r, [40, 40, 230])     # right horizon (blue)
    draw(h_blend, [20, 160, 20]) # blended horizon (green)
    for xj in (xL, xR):
        diag[:, xj] = [120, 120, 120]
    Image.fromarray(diag, "RGB").save(os.path.join(OUT, "diagnostic.png"))

    # --- report continuity at the joins --------------------------------------
    print("\ncontinuity check (px gap between blended horizon and the surviving neighbour):")
    print(f"  left join  x={xL}:  h_blend={h_blend[xL]:.1f}  h_left={hl[xL]:.1f}   gap={abs(h_blend[xL]-hl[xL]):.2f}")
    print(f"  right join x={xR}:  h_blend={h_blend[xR]:.1f}  h_right={hr[xR]:.1f}  gap={abs(h_blend[xR]-hr[xR]):.2f}")
    # max single-column vertical jump in the combined horizon (the 'drop' metric)
    hb = h_blend[~np.isnan(h_blend)]
    print(f"  max adjacent-column step in combined horizon: {np.max(np.abs(np.diff(hb))):.2f}px")
    print(f"\nwrote {OUT}/combined.png and {OUT}/diagnostic.png")


if __name__ == "__main__":
    main()
