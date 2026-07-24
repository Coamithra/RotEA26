"""Offline preview + matrix check for Wall.DrawTowerShafts3D (Trello a66fc73e).

CLAUDE.md forbids verifying wall drawing with a live screenshot: the wall scrolls, and the
canvas is black whenever its tab is backgrounded. So the 3D pass is checked here instead,
by re-implementing its exact projection against the real 756-v1.png.

Two things are proven:

1. MATRIX CHECK. Wall.cs builds a real perspective camera (View * Projection) so the GPU
   does the perspective divide and interpolates UVs correctly. That camera must reproduce
   Wall.Project(top, d) = VP + (top - VP) * d exactly, or the shafts stop lining up with the
   sprite-drawn top faces they hang from. This pushes vertices through the same XNA matrices
   (row-vector convention) and compares.

2. IMAGE. Rasterise the side faces the way the GPU will: for each pixel solve the ruled
   surface p(s,d) = VP + (lerp(A,B,s) - VP) * d for (s, d) -- which is precisely what
   perspective-correct interpolation computes -- then sample the sheet at (u,v), shade it by
   the wall's face factor, and lerp it toward the haze with the same distance fog BasicEffect
   applies. Blocks are painter-sorted by distance from the VP (see verify_tower_order.py);
   top faces are painted last, as in Wall.Draw.

   The down-the-shaft coordinate is a walk in CELL units continuing OUT of the block's cell
   (card 0f7fc977), spanning the shaft's true height in block footprints -- see Wall.AddFace.
   Wall.cs has to cut the face at every cell crossing because the GPU interpolates UVs
   linearly between vertices and the sheet has to wrap inside the LOGICAL (unpadded) region;
   here the UV is solved per pixel, so the same thing is one `c % 8`. `--mirror` reproduces
   the pre-card look (one cell, retraced backwards) for an A/B.

Run:  python tools/walls/preview_wall3d.py [--tile <f>] [--mirror] [--compare] [--ladder]
            [--nomips] [--shimmer]
Writes tools/walls/_preview_wall3d*.png (gitignored). --help for the flags.
"""
import argparse

import numpy as np
from PIL import Image

VANISH = np.array([400.0, 300.0])
W, H = 800, 600
DEPTH = 0.66
EYE = 600.0
NEAR_FRAC = 0.9
BANDS_NOTE = "bands only quantise the bottom dissolve; geometry, UV and fog are exact here"

SIDE_DARK = 0.7
# Mirrors Wall.DefaultSideTile: cells of sheet spent down a shaft, as a multiple of the top
# face's own texel density. 1 = a side texel is the same world size as a top-face texel; baked
# at 4 because honest scale reads short on a steeply foreshortened shaft (see Wall.cs).
SIDE_TILE = 4.0
FOG_AMOUNT = 0.55
FOG_COLOR = np.array([46.0, 125.0, 201.0])
DISSOLVE = 0.18
FACE_LIGHT = 0.35
FACE_ANGLE = 140.0
FACE_DIR_WEIGHT = 0.35

WALL_PNG = "web/EvilAliensWeb/wwwroot/Content/gfx/base/756-v1.png"
LEVEL3 = "web/EvilAliensWeb/wwwroot/Content/levels/level3.txt"


# ---------------------------------------------------------------- XNA matrix helpers
def m_translation(x, y, z):
    m = np.eye(4)
    m[3, :3] = (x, y, z)
    return m


def m_scale(x, y, z):
    return np.diag([x, y, z, 1.0])


def m_perspective_off_center(l, r, b, t, n, f):
    m = np.zeros((4, 4))
    m[0, 0] = 2 * n / (r - l)
    m[1, 1] = 2 * n / (t - b)
    m[2, 0] = (l + r) / (r - l)
    m[2, 1] = (t + b) / (t - b)
    m[2, 2] = f / (n - f)
    m[2, 3] = -1.0
    m[3, 2] = n * f / (n - f)
    return m


def shaft_height(depth):
    return EYE * (1.0 - depth) / depth


def camera(depth):
    e, n = EYE, EYE * NEAR_FRAC
    view = m_translation(-VANISH[0], -VANISH[1], 0.0) @ m_scale(1.0, -1.0, -1.0) @ m_translation(0.0, 0.0, -e)
    proj = m_perspective_off_center(
        -400 * NEAR_FRAC, 400 * NEAR_FRAC, -300 * NEAR_FRAC, 300 * NEAR_FRAC, n, e + shaft_height(depth) + 1.0
    )
    return view @ proj


def to_screen(pts3, vp):
    """pts3: (N,3) design-space (x, y, z). Returns (N,2) screen px via the real pipeline."""
    v = np.concatenate([pts3, np.ones((len(pts3), 1))], axis=1)
    clip = v @ vp
    ndc = clip[:, :2] / clip[:, 3:4]
    sx = (ndc[:, 0] * 0.5 + 0.5) * W
    sy = (0.5 - ndc[:, 1] * 0.5) * H
    return np.stack([sx, sy], axis=1)


def project_2d(pt, d):
    return VANISH + (np.asarray(pt) - VANISH) * d


def matrix_check(depth=DEPTH):
    vp = camera(depth)
    h = shaft_height(depth)
    rng = np.random.default_rng(1)
    xs = rng.uniform(-200, 1000, 400)
    ys = rng.uniform(-200, 800, 400)
    zs = rng.uniform(0.0, h, 400)
    got = to_screen(np.stack([xs, ys, zs], axis=1), vp)
    d = EYE / (EYE + zs)
    want = np.stack([VANISH[0] + (xs - VANISH[0]) * d, VANISH[1] + (ys - VANISH[1]) * d], axis=1)
    err = np.abs(got - want).max()
    print(f"[matrix] shaftHeight={h:.3f}  max |camera - Project()| over 400 random pts = {err:.3e} px")
    # the two extremes that actually matter
    for z, name in ((0.0, "gameplay plane (d=1)"), (h, f"ground (d={depth})")):
        p = to_screen(np.array([[800.0, 600.0, z]]), vp)[0]
        q = project_2d([800.0, 600.0], EYE / (EYE + z))
        print(f"           corner (800,600) at {name:24s}: camera {p.round(4)}  Project {q.round(4)}")
    assert err < 1e-3, "camera does NOT reproduce Wall.Project()"
    print("[matrix] OK -- View*Projection reproduces Wall.Project() exactly\n")


# ---------------------------------------------------------------- shading
def shaft_alpha(f):
    """Coverage at height fraction f (0 = cap, 1 = ground). Mirrors Wall.ShaftAlpha."""
    t = 1.0 - f
    s = np.clip(t / DISSOLVE, 0.0, 1.0)
    return np.where(t < DISSOLVE, s * s * (3 - 2 * s), 1.0)


def face_factors(light, angle_deg):
    """(north, south, east, west) flat shades. Mirrors Wall.FaceFactors."""
    a = np.radians(angle_deg)
    lx, ly = np.cos(a), np.sin(a)
    d = FACE_DIR_WEIGHT * light
    north = 1.0 - d * (1.0 + ly) * 0.5
    south = 1.0 - d * (1.0 - ly) * 0.5
    east = (1.0 - light) * (1.0 - d * (1.0 - lx) * 0.5)
    west = (1.0 - light) * (1.0 - d * (1.0 + lx) * 0.5)
    return north, south, east, west


def fog_factor(d, depth):
    """BasicEffect distance fog, keyed on eye distance e/d. Mirrors DrawTowerShafts3D."""
    if FOG_AMOUNT <= 0.001:
        return np.zeros_like(d)
    start = EYE / 1.0                      # the cap (topD == 1 here)
    end = EYE / depth
    if FOG_AMOUNT < 1.0:
        end = start + (end - start) / FOG_AMOUNT
    return np.clip((EYE / np.clip(d, 1e-6, None) - start) / (end - start), 0.0, 1.0)


def solve_sd(A, B, px, py):
    a = A - VANISH
    e = B - A
    det = a[0] * e[1] - a[1] * e[0]
    if abs(det) < 1e-9:
        return None
    qx, qy = px - VANISH[0], py - VANISH[1]
    u = (qx * e[1] - qy * e[0]) / det
    v = (a[0] * qy - a[1] * qx) / det
    with np.errstate(divide="ignore", invalid="ignore"):
        s = np.where(np.abs(u) > 1e-12, v / u, -1.0)
    ok = (u >= DEPTH - 1e-6) & (u <= 1.0) & (s >= 0) & (s <= 1)
    return u, s, ok


def sample(tex, u, v):
    """BILINEAR CLAMP -- exactly what DrawGeometry3D binds (SamplerState.LinearClamp).

    Bilinear because point sampling shows a moire the game does not. CLAMP rather than wrap
    because the game clamps: at the sheet's own 8->0 wrap the caller emits u == 1 exactly, and
    clamping there taps the last texel twice instead of blending into the first. That half-texel
    is the game's real behaviour and must not be prettied up here -- on a PADDED .dds the same
    tap reaches the pad's edge gutter, which replicates the edge, so clamp is what it resolves to
    either way (tools/textures/check_pad_bleed.py asserts exactly that, at every mip level).

    This is ONE level. Trilinear over a pyramid is sample_tri below."""
    th, tw = tex.shape[:2]
    x = u * tw - 0.5
    y = v * th - 0.5
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    x1, y1 = np.clip(x0 + 1, 0, tw - 1), np.clip(y0 + 1, 0, th - 1)
    x0, y0 = np.clip(x0, 0, tw - 1), np.clip(y0, 0, th - 1)
    top = tex[y0, x0] * (1 - fx) + tex[y0, x1] * fx
    bot = tex[y1, x0] * (1 - fx) + tex[y1, x1] * fx
    return top * (1 - fy) + bot * fy


def mip_pyramid(tex):
    """The chain the GPU samples, modelled from the PNG.

    Mirrors build_textures.py's build_mip_chain: successive integer halving (floor, min 1) with
    Pillow's BOX/area average, built from the LOGICAL content alone. It uses the SAME resampler
    as the pipeline rather than a hand-rolled 2x2 mean -- those differ once a level is odd (39 ->
    19 for this sheet, level 6 down), because a 2x2 mean drops the trailing row/column while BOX
    area-averages it in. The shipped .dds re-derives its pad per level, so the pad never filters
    into the content and the levels here are the same surfaces the GPU holds, minus BC3's lossy
    step -- which is not what aliasing is about. Modelling it from the PNG keeps this tool a pure
    read of the art, with no BC decoder."""
    pyr = [tex.astype(np.float64)]
    while pyr[-1].shape[0] > 1 or pyr[-1].shape[1] > 1:
        a = pyr[-1]
        h2, w2 = max(1, a.shape[0] // 2), max(1, a.shape[1] // 2)
        small = Image.fromarray(a.round().clip(0, 255).astype(np.uint8)).resize(
            (w2, h2), Image.Resampling.BOX)
        pyr.append(np.asarray(small).astype(np.float64))
    return pyr


def sample_tri(pyr, u, v, lod):
    """TRILINEAR CLAMP: bilinear within two adjacent levels, lerped by the fractional LOD.

    What SamplerState.LinearClamp resolves to once the texture has levels -- KNI maps
    TextureFilter.Linear to LINEAR_MIPMAP_LINEAR whenever LevelCount > 1, so this needs no
    engine-side opt-in. Levels are grouped and sampled in one pass each rather than sampling all
    11 everywhere; a face typically spans only three or four."""
    maxl = len(pyr) - 1
    lod = np.clip(lod, 0.0, float(maxl))
    l0 = np.floor(lod).astype(int)
    frac = (lod - l0)[..., None]
    out = np.zeros(np.shape(u) + (pyr[0].shape[2],), float)
    for lv in np.unique(l0):
        m = l0 == lv
        lo = sample(pyr[lv], u[m], v[m])
        hi = sample(pyr[min(lv + 1, maxl)], u[m], v[m])
        out[m] = lo * (1.0 - frac[m]) + hi * frac[m]
    return out


def lod_from(uv, uv_dx, uv_dy, tw, th):
    """log2 of the worst screen-space texel footprint -- the GL/D3D LOD selection rule.

    The derivative MUST be taken on the UNWRAPPED cell walk. Differencing after the `% 8` wrap
    steps a whole sheet at every crossing, which reads as enormous minification and would slam
    the one pixel row where the sheet wraps to the coarsest level -- a bug that looks exactly
    like a seam."""
    scale = np.array([tw, th], float)
    dx = (uv_dx - uv) * scale
    dy = (uv_dy - uv) * scale
    rho = np.maximum(np.hypot(dx[..., 0], dx[..., 1]), np.hypot(dy[..., 0], dy[..., 1]))
    return np.log2(np.maximum(rho, 1e-6))


def grid_from_level3(path):
    with open(path) as fh:
        n = int(fh.readline()[6:])
        rows = []
        for line in fh:
            if "end" in line:
                break
            rows.append(line.rstrip("\n"))
    g = np.zeros((len(rows), n), dtype=bool)
    for i, r in enumerate(rows):
        for j in range(n):
            g[i, j] = j < len(r) and r[j] != " "
    return g


def face_uv(A, B, alongA, alongB, c_cap, c_base, along_is_x, px, py, shaftH):
    """(uv, d, ok, heightFrac) for one face at the given screen points, cell walk UNWRAPPED.

    Split out of render() so the same solve can be re-run at px+1 / py+1 for the screen-space
    derivative the LOD needs. Wrapping is applied by the caller, at sample time only."""
    r = solve_sd(A, B, px, py)
    if r is None:
        return None
    d, s, ok = r
    z = EYE * (1.0 / np.clip(d, 1e-6, None) - 1.0)
    f = np.clip(z / shaftH, 0.0, 1.0)
    along = alongA + (alongB - alongA) * s
    down = (c_cap + (c_base - c_cap) * f) / 8.0     # unwrapped: may run below 0 or past 1
    uv = np.stack([along, down] if along_is_x else [down, along], axis=-1)
    return uv, d, ok, f


def render(blocks, pos, tex, side_tile=SIDE_TILE, mirror=False, pyr=None, want_mask=False):
    """`pyr` (from mip_pyramid) switches sampling from bilinear to trilinear -- the A/B for card
    110153c7. None reproduces the no-mip status quo.

    `want_mask` also returns a bool image of SHAFT coverage (side faces only, tops excluded).
    That is what shimmer() scores on: the tops are an axis-aligned blit whose pixel grid snaps to
    whole pixels, so they carry a mode-independent jitter that would dilute any measurement of
    the shafts."""
    h, w = blocks.shape
    shaft_mask = np.zeros((H, W), bool)
    bw = bh = 800.0 / w
    cw, ch = tex.shape[1] // 8, tex.shape[0] // 8
    img = np.zeros((H, W, 3), float)
    img[:] = (10, 30, 55)  # stand-in for the alien-base floor
    ys, xs = np.mgrid[0:H, 0:W]
    px, py = xs + 0.5, ys + 0.5

    vis = []
    for i in range(h):
        ytop = bh * i + pos[1]
        ybase = VANISH[1] + (ytop - VANISH[1]) * DEPTH
        if max(ytop + bh, ybase + bh * DEPTH) <= 0 or min(ytop, ybase) >= H:
            continue
        for j in range(w):
            if blocks[i, j]:
                vis.append((i, j))
    # painter's order: far from the VP first
    vis.sort(key=lambda t: -((bw * (t[1] + .5) + pos[0] - VANISH[0]) ** 2
                             + (bh * (t[0] + .5) + pos[1] - VANISH[1]) ** 2))

    shaftH = shaft_height(DEPTH)
    n_f, s_f, e_f, w_f = face_factors(FACE_LIGHT, FACE_ANGLE)

    # Cells of sheet spent down the shaft = its true height in block footprints, so a side texel
    # is the world size of a top-face one. `mirror` reproduces the pre-card look: exactly one
    # cell, retraced back ACROSS the block's own cell (hence `walk` flipping the direction).
    rep_u = 1.0 if mirror else side_tile * shaftH / bw
    rep_v = 1.0 if mirror else side_tile * shaftH / bh
    walk = 1.0 if mirror else -1.0

    def free(jj, ii):
        """Mirrors Wall.isfree EXACTLY, asymmetry included: out-of-range x reads SOLID (the wall
        spans the full screen width, so its leftmost/rightmost columns sit at x=0/800 and their
        outer walls are off-screen), while out-of-range y reads FREE (a section's first and last
        rows are genuinely exposed ends)."""
        if jj < 0 or jj >= w:
            return False
        if ii < 0 or ii >= h:
            return True
        return not blocks[ii, jj]

    for (i, j) in vis:
        x0 = bw * j + pos[0]
        y0 = bh * i + pos[1]
        x1, y1 = x0 + bw, y0 + bh
        jc, ic = j % 8, i % 8
        u0, u1 = jc / 8, (jc + 1) / 8
        v0, v1 = ic / 8, (ic + 1) / 8

        # (A, B, alongA, alongB, cCap, cBase, alongIsX) -- along-edge follows the axis the edge
        # runs along (vertical edge spans rows -> v; horizontal edge spans columns -> u), which is
        # what makes coplanar neighbouring walls continue instead of seaming. Down the shaft is a
        # CELL coordinate starting at the edge the wall hangs from and running AWAY from the cell,
        # so the sheet continues past the rim instead of mirroring it. See Wall.cs.
        # Emitted only when the side is an OUTER edge (no neighbouring block) AND faces the eye.
        faces = []
        if x0 > VANISH[0] and free(j - 1, i): faces.append((np.array([x0, y0]), np.array([x0, y1]), v0, v1, jc, jc + walk * rep_u, False, w_f))
        if x1 < VANISH[0] and free(j + 1, i): faces.append((np.array([x1, y1]), np.array([x1, y0]), v1, v0, jc + 1, jc + 1 - walk * rep_u, False, e_f))
        if y0 > VANISH[1] and free(j, i - 1): faces.append((np.array([x1, y0]), np.array([x0, y0]), u1, u0, ic, ic + walk * rep_v, True, n_f))
        if y1 < VANISH[1] and free(j, i + 1): faces.append((np.array([x0, y1]), np.array([x1, y1]), u0, u1, ic + 1, ic + 1 - walk * rep_v, True, s_f))

        for (A, B, alongA, alongB, c_cap, c_base, along_is_x, shade) in faces:
            args = (A, B, alongA, alongB, c_cap, c_base, along_is_x)
            r = face_uv(*args, px, py, shaftH)
            if r is None:
                continue
            uv, d, ok, f = r
            if not ok.any():
                continue
            # The cell walk, wrapped into the sheet. Wall.AddFace has to CUT the face at every
            # integer crossing (the GPU lerps UVs between vertices, and the wrap has to stay
            # inside the logical region of a padded .dds); solving per pixel here, it is one mod.
            # Only the DOWN axis wraps -- along-edge already spans one cell.
            uvw = uv.copy()
            uvw[..., 1 if along_is_x else 0] %= 1.0
            u, v = uvw[..., 0], uvw[..., 1]
            if pyr is None:
                texel = sample(tex, u, v).astype(float)
            else:
                # solve_sd only degenerates on A/B, which these share with the base solve, so a
                # non-None r above guarantees both of these are non-None too.
                rx = face_uv(*args, px + 1.0, py, shaftH)
                ry = face_uv(*args, px, py + 1.0, shaftH)
                lod = lod_from(uv, rx[0], ry[0], tex.shape[1], tex.shape[0])
                texel = sample_tri(pyr, u, v, lod)
            src = texel * (SIDE_DARK * shade)
            # Real distance fog: LERP toward the haze colour (a sprite tint could only multiply).
            fw = fog_factor(d, DEPTH)[..., None]
            src = src * (1.0 - fw) + FOG_COLOR * fw
            a = (shaft_alpha(f) * ok)[..., None]
            img = img * (1 - a) + src * a
            shaft_mask |= a[..., 0] > 0.5

    # Top faces last (d == 1, nearest the eye). Sampled through the SAME sampler as the shafts so
    # the two modes stay comparable, and because the tops are minified too: a 156px cell lands in
    # a ~32px block, so they pick a mip level as surely as a shaft does (card 110153c7 changes
    # their look as well, which is worth being able to see here rather than only in game).
    for (i, j) in vis:
        x0 = int(round(bw * j + pos[0]))
        y0 = int(round(bh * i + pos[1]))
        x1, y1 = int(round(x0 + bw)), int(round(y0 + bh))
        sx0, sy0 = max(0, x0), max(0, y0)
        sx1, sy1 = min(W, x1), min(H, y1)
        if sx1 <= sx0 or sy1 <= sy0:
            continue
        gy, gx = np.mgrid[sy0:sy1, sx0:sx1]
        fu = (gx + 0.5 - x0) / max(1, x1 - x0)
        fv = (gy + 0.5 - y0) / max(1, y1 - y0)
        u = ((j % 8) + fu) / 8.0
        v = ((i % 8) + fv) / 8.0
        if pyr is None:
            img[sy0:sy1, sx0:sx1] = sample(tex, u, v)
        else:
            # Axis-aligned blit, so the footprint is constant over the face -- one analytic LOD
            # rather than a per-pixel derivative.
            lod = np.log2(max(cw / max(1, x1 - x0), ch / max(1, y1 - y0), 1e-6))
            img[sy0:sy1, sx0:sx1] = sample_tri(pyr, u, v, np.full(u.shape, lod))
    img = np.clip(img, 0, 255).astype(np.uint8)
    return (img, shaft_mask) if want_mask else img


CROP = (slice(410, 600), slice(470, 655))


def tower_crop(tex, tile, mirror, pyr=None, dy=0.0, want_mask=False):
    """One isolated tower, framed tight, for the side-by-side sheets. A lone block shows both
    of its faces and its full shaft, which is what the side texturing has to be judged on --
    in the contiguous level3 grid the shafts merge into a mass and hide it."""
    iso = np.zeros((9, 9), dtype=bool)
    iso[6, 6] = True
    r = render(iso, np.array([0.0, -40.0 + dy]), tex, tile, mirror, pyr, want_mask)
    if want_mask:
        return r[0][CROP], r[1][CROP]
    return r[CROP]


def shimmer(tex, tile, pyr, mask, steps=8):
    """Aliasing as a NUMBER: mean per-pixel temporal stddev over a sub-pixel scroll sweep.

    The card's complaint is that the shaft shimmers *because the wall scrolls*, which no still
    frame can show -- so measure the thing itself. The tower is nudged across ONE screen pixel in
    `steps` sub-pixel increments: over that span its geometry is essentially unchanged, but at
    high tiling the texture slides many texels under every screen pixel. A well-filtered surface
    therefore barely moves in value (low stddev) while an under-sampled one jitters (high).

    Scored over the caller's SHAFT mask only, and the caller passes the SAME mask for both modes.
    Two things would otherwise corrupt the number: the flat background has zero variance in both
    modes and merely dilutes, and the tower TOPS are an axis-aligned blit whose destination rect
    snaps to whole pixels, so they jitter by an equal, mode-independent amount that shrinks the
    measured gap. Excluding them is what makes this a measurement of filtering."""
    if not mask.any():
        return float("nan")
    frames = np.stack([tower_crop(tex, tile, False, pyr, k / steps) for k in range(steps)])
    return float(frames.astype(float).std(axis=0).mean(axis=-1)[mask].mean())


def filmstrip(panels, path=None, scale=2):
    """Panels side by side, upscaled. `path=None` returns the image without writing it (the
    ladder composites two strips and only saves the pair)."""
    gap = np.full((panels[0].shape[0], 6, 3), 255, np.uint8)
    row = []
    for i, p in enumerate(panels):
        if i:
            row.append(gap)
        row.append(p)
    img = Image.fromarray(np.concatenate(row, axis=1))
    img = img.resize((img.size[0] * scale, img.size[1] * scale), Image.LANCZOS)
    if path:
        img.save(path)
    return img


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--tile", type=float, default=SIDE_TILE,
                    help=f"cells of sheet down a shaft, as a multiple of the top face's texel density (default {SIDE_TILE}, = Wall.DefaultSideTile)")
    ap.add_argument("--mirror", action="store_true",
                    help="render the pre-card-0f7fc977 side texturing (one cell, mirrored about the rim)")
    ap.add_argument("--compare", action="store_true",
                    help="also write the before/after A/B sheet (doubles the run)")
    ap.add_argument("--ladder", action="store_true",
                    help="also write the tiling ladder (mirror | 1 | 2 | 4 | 8), bilinear-only on top "
                         "and trilinear below -- how the aliasing grows with density, and what mips do to it")
    ap.add_argument("--nomips", action="store_true",
                    help="sample bilinear-only, ignoring the mip pyramid -- the pre-card look. "
                         "Named and polarised to match the game's own ?nomips; the DEFAULT is "
                         "trilinear, because that is what the shipped mipped 756-v1.dds gets")
    ap.add_argument("--shimmer", action="store_true",
                    help="measure aliasing as a NUMBER instead of by eye: mean per-pixel temporal "
                         "stddev across a sub-pixel scroll sweep, per tiling, with and without mips")
    args = ap.parse_args()
    tile, mirror = args.tile, args.mirror

    matrix_check()
    tex = np.array(Image.open(WALL_PNG).convert("RGB"))
    pyr = mip_pyramid(tex)
    print(f"[image] {BANDS_NOTE}")
    print(f"[image] mip pyramid: {len(pyr)} levels, "
          + " ".join(f"{p.shape[1]}x{p.shape[0]}" for p in pyr[:5]) + " ...")

    # Row 1: isolated blocks on a 9-wide grid, so each tower's shaft is legible on its own and
    # the lean-toward-the-VP / painter's-order behaviour can be read by eye.
    iso = np.zeros((9, 9), dtype=bool)
    iso[::2, ::2] = True
    mp = None if args.nomips else pyr
    row1 = [render(iso, np.array([0.0, py]), tex, tile, mirror, mp) for py in (-150.0, -40.0, 60.0)]

    # Row 2: the real level3.txt, where blocks are contiguous and the towers merge into masses.
    g = grid_from_level3(LEVEL3)
    row2 = [render(g, np.array([0.0, py]), tex, tile, mirror, mp) for py in (-260.0, -120.0, 20.0)]

    sheet = Image.fromarray(np.concatenate([np.concatenate(row1, axis=1),
                                            np.concatenate(row2, axis=1)], axis=0))
    sheet.save("tools/walls/_preview_wall3d.png")
    print(f"[image] wrote tools/walls/_preview_wall3d.png  ({sheet.size[0]}x{sheet.size[1]})")
    print("[image]   row 1: isolated towers (3 scroll positions)   row 2: real level3.txt")
    print(f"[image]   sideTile={tile}" + ("   MIRROR (pre-card look)" if mirror else "")
          + ("   bilinear only (--nomips)" if args.nomips else "   TRILINEAR (mipped, as shipped)"))

    # A/B: the same three framings before and after card 0f7fc977. Top row is the old side
    # texturing (one cell, mirrored about the rim); bottom row is the tile continuing out of the
    # cell for the shaft's real height. This is the sheet that shows the card is fixed.
    if args.compare:
        cases = [(iso, -40.0), (g, -260.0), (g, 20.0)]
        old = [render(bl, np.array([0.0, py]), tex, tile, True) for (bl, py) in cases]
        new = [render(bl, np.array([0.0, py]), tex, tile, False) for (bl, py) in cases]
        cmp_sheet = Image.fromarray(np.concatenate([np.concatenate(old, axis=1),
                                                    np.concatenate(new, axis=1)], axis=0))
        cmp_sheet.save("tools/walls/_preview_wall3d_compare.png")
        print(f"[image] wrote tools/walls/_preview_wall3d_compare.png  ({cmp_sheet.size[0]}x{cmp_sheet.size[1]})")
        print(f"[image]   row 1: BEFORE (1 cell, mirrored)   row 2: AFTER (continues, sideTile={tile})")

    # The density ladder: one tower at each tiling, so the "reads taller" win and the aliasing it
    # buys can be weighed against each other on one image rather than by re-running. Two rows,
    # because since card 110153c7 the sheet HAS a mip chain and the question is what it costs
    # with one -- top row is bilinear-only (the pre-card look), bottom is the shipped trilinear.
    if args.ladder:
        rungs = [("mirror", 1.0, True)] + [(str(t), t, False) for t in (1.0, 2.0, 4.0, 8.0)]
        top = filmstrip([tower_crop(tex, t, m) for (_, t, m) in rungs])
        bot = filmstrip([tower_crop(tex, t, m, pyr) for (_, t, m) in rungs])
        both = Image.new("RGB", (top.size[0], top.size[1] * 2 + 8), (255, 255, 255))
        both.paste(top, (0, 0))
        both.paste(bot, (0, top.size[1] + 8))
        both.save("tools/walls/_preview_wall3d_ladder.png")
        print(f"[image] wrote tools/walls/_preview_wall3d_ladder.png  ({both.size[0]}x{both.size[1]})")
        print("[image]   panels: " + " | ".join(n for (n, _, _) in rungs))
        print("[image]   row 1: bilinear only (no mip chain)   row 2: TRILINEAR (mipped .dds)")

    # Aliasing as data. A still frame cannot show a shimmer, so this is the honest read on both
    # the fix AND the card's fallback question (mips at tile 4 vs simply dropping the tiling).
    if args.shimmer:
        print("[shimmer] mean per-pixel temporal stddev over a 1px sub-pixel scroll sweep "
              "(lower = steadier under scroll)")
        print(f"  {'sideTile':>9} {'bilinear':>10} {'trilinear':>10} {'change':>9}")
        for t in (1.0, 2.0, 4.0, 8.0):
            # One mask, from one render, used for BOTH modes -- geometry does not depend on the
            # sampler, so scoring them on different pixel sets would be comparing two things.
            _, mask = tower_crop(tex, t, False, None, 0.0, want_mask=True)
            a, b = shimmer(tex, t, None, mask), shimmer(tex, t, pyr, mask)
            mark = "  <-- Wall.DefaultSideTile" if t == SIDE_TILE else ""
            print(f"  {t:>9} {a:>10.3f} {b:>10.3f} {(b / a - 1) * 100:>8.1f}%{mark}")
