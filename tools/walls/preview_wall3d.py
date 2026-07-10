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
   perspective-correct interpolation computes -- then sample the block's real 8x8 cell at
   (u,v), shade it by the wall's face factor, and lerp it toward the haze with the same
   distance fog BasicEffect applies. Blocks are painter-sorted by distance from the VP (see
   verify_tower_order.py); top faces are painted last, as in Wall.Draw.

Run:  python tools/walls/preview_wall3d.py
Writes tools/walls/_preview_wall3d.png (gitignored).
"""
import numpy as np
from PIL import Image

VANISH = np.array([400.0, 300.0])
W, H = 800, 600
DEPTH = 0.66
EYE = 600.0
NEAR_FRAC = 0.9
BANDS_NOTE = "bands only quantise the bottom dissolve; geometry, UV and fog are exact here"

SIDE_DARK = 0.7
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
    th, tw = tex.shape[:2]
    xi = np.clip((u * tw).astype(int), 0, tw - 1)
    yi = np.clip((v * th).astype(int), 0, th - 1)
    return tex[yi, xi]


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


def render(blocks, pos, tex):
    h, w = blocks.shape
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
        u0, u1 = (j % 8) / 8, (j % 8 + 1) / 8
        v0, v1 = (i % 8) / 8, (i % 8 + 1) / 8

        # (A, B, alongA, alongB, down0, down1, alongIsX) -- along-edge follows the axis the edge
        # runs along (vertical edge spans rows -> v; horizontal edge spans columns -> u), which is
        # what makes coplanar neighbouring walls continue instead of seaming. See Wall.cs.
        # Emitted only when the side is an OUTER edge (no neighbouring block) AND faces the eye.
        faces = []
        if x0 > VANISH[0] and free(j - 1, i): faces.append((np.array([x0, y0]), np.array([x0, y1]), v0, v1, u0, u1, False, w_f))
        if x1 < VANISH[0] and free(j + 1, i): faces.append((np.array([x1, y1]), np.array([x1, y0]), v1, v0, u1, u0, False, e_f))
        if y0 > VANISH[1] and free(j, i - 1): faces.append((np.array([x1, y0]), np.array([x0, y0]), u1, u0, v0, v1, True, n_f))
        if y1 < VANISH[1] and free(j, i + 1): faces.append((np.array([x0, y1]), np.array([x1, y1]), u0, u1, v1, v0, True, s_f))

        for (A, B, alongA, alongB, down0, down1, along_is_x, shade) in faces:
            r = solve_sd(A, B, px, py)
            if r is None:
                continue
            d, s, ok = r
            if not ok.any():
                continue
            # d = EYE/(EYE+z) -> z, and the height fraction along the face
            z = EYE * (1.0 / np.clip(d, 1e-6, None) - 1.0)
            f = np.clip(z / shaftH, 0.0, 1.0)
            along = alongA + (alongB - alongA) * s
            down = down0 + (down1 - down0) * f
            u, v = (along, down) if along_is_x else (down, along)
            texel = sample(tex, u, v).astype(float)
            src = texel * (SIDE_DARK * shade)
            # Real distance fog: LERP toward the haze colour (a sprite tint could only multiply).
            fw = fog_factor(d, DEPTH)[..., None]
            src = src * (1.0 - fw) + FOG_COLOR * fw
            a = (shaft_alpha(f) * ok)[..., None]
            img = img * (1 - a) + src * a

    # top faces last (d == 1, nearest the eye) -- unchanged from the sprite pass
    for (i, j) in vis:
        x0 = int(round(bw * j + pos[0]))
        y0 = int(round(bh * i + pos[1]))
        x1, y1 = int(round(x0 + bw)), int(round(y0 + bh))
        cx, cy = (j % 8) * cw, (i % 8) * ch
        cell = np.array(Image.fromarray(tex[cy:cy + ch, cx:cx + cw]).resize((max(1, x1 - x0), max(1, y1 - y0))))
        sx0, sy0 = max(0, x0), max(0, y0)
        sx1, sy1 = min(W, x1), min(H, y1)
        if sx1 <= sx0 or sy1 <= sy0:
            continue
        img[sy0:sy1, sx0:sx1] = cell[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0, :3]
    return np.clip(img, 0, 255).astype(np.uint8)


if __name__ == "__main__":
    matrix_check()
    tex = np.array(Image.open(WALL_PNG).convert("RGB"))
    print(f"[image] {BANDS_NOTE}")

    # Row 1: isolated blocks on a 9-wide grid, so each tower's shaft is legible on its own and
    # the lean-toward-the-VP / painter's-order behaviour can be read by eye.
    iso = np.zeros((9, 9), dtype=bool)
    iso[::2, ::2] = True
    row1 = [render(iso, np.array([0.0, py]), tex) for py in (-150.0, -40.0, 60.0)]

    # Row 2: the real level3.txt, where blocks are contiguous and the towers merge into masses.
    g = grid_from_level3(LEVEL3)
    row2 = [render(g, np.array([0.0, py]), tex) for py in (-260.0, -120.0, 20.0)]

    sheet = Image.fromarray(np.concatenate([np.concatenate(row1, axis=1),
                                            np.concatenate(row2, axis=1)], axis=0))
    sheet.save("tools/walls/_preview_wall3d.png")
    print(f"[image] wrote tools/walls/_preview_wall3d.png  ({sheet.size[0]}x{sheet.size[1]})")
    print("[image]   row 1: isolated towers (3 scroll positions)   row 2: real level3.txt")
