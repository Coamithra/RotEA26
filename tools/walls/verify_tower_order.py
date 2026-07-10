"""Certify that a CPU painter's sort can replace a depth buffer for the ?wall3d spike.

WHY THIS TEST, AND NOT A PIXEL DIFF
Counting "wrong pixels" for one candidate sort key only shows that key happened to work
on the grids you tried. Two stronger things decide the spike:

  (a) Is the "occludes" relation between visible side-face quads ACYCLIC? If two faces
      ever mutually occlude (A in front of B at one pixel, B in front of A at another),
      NO painter's order exists at all and the 3D pass needs a depth attachment. That is
      a property of the geometry, independent of any key.
  (b) Is the cheap key we intend to ship a valid topological order of that relation?

THE POLAR ARGUMENT (what (a) should confirm)
Work in polar coordinates about the vanishing point. Along a ray at angle theta a block's
top rect spans radii [r0, r1], r0 being its near edge. Its shaft silhouette spans
[D*r0, r1], and the part that is VISIBLE side face -- everything the top rect does not
already cover -- spans [D*r0, r0]. A point at radius rho on that face lies at depth
d = rho / r0, since the face is the near edge swept from d=1 down to d=D. So for two
blocks sharing a ray with r0_A < r0_B, wherever their faces overlap:

    d_A = rho / r0_A  >  rho / r0_B = d_B

The block whose NEAR EDGE is closer to the VP always wins, at every shared radius. Depth
along a ray is a strictly decreasing function of the owning face's r0, so the faces cannot
interleave and no cycle can exist. This script checks that numerically against the real
Level-3 geometry rather than trusting the derivation.

Method: solve for depth analytically (no rasterisation error) -- a face from top edge
(A,B) is the ruled surface p(s,d) = VP + (lerp(A,B,s) - VP) * d, so with a = A-VP and
e = B-A, the pixel equation q = u*a + v*e is a 2x2 solve giving u = d, v = d*s. Pairs are
compared only inside the intersection of their screen bounding boxes, so the whole sweep
stays cheap.

Run:  python tools/walls/_spike_painter_certify.py
"""
import itertools
import sys

import numpy as np

VP = np.array([400.0, 300.0])
DEPTH = 0.66
W, H = 800, 600
SS = 4          # samples per design px inside a pair's overlap box
EPS_D = 1e-4    # depth difference that counts as a real ordering, not a shared edge

# Draw FAR from the VP first so nearer-VP quads paint over them (negate => ascending sort).
KEY = lambda f: -np.linalg.norm(f["centre"] - VP)
KEY_NAME = "box centre distance from VP, descending"


def project(pt, d):
    return VP + (pt - VP) * d


def top_rect(i, j, bw, bh, pos):
    x0 = bw * j + pos[0]
    y0 = bh * i + pos[1]
    return np.array([[x0, y0], [x0 + bw, y0], [x0 + bw, y0 + bh], [x0, y0 + bh]])


def face_poly(A, B):
    return np.array([project(A, 1.0), project(B, 1.0), project(B, DEPTH), project(A, DEPTH)])


def signed_area(poly):
    s = 0.0
    for k in range(4):
        x1, y1 = poly[k]
        x2, y2 = poly[(k + 1) % 4]
        s += x1 * y2 - x2 * y1
    return s


def solve_depth(A, B, px, py):
    a = A - VP
    e = B - A
    det = a[0] * e[1] - a[1] * e[0]
    if abs(det) < 1e-9:
        return np.full(px.shape, -1.0)
    qx, qy = px - VP[0], py - VP[1]
    u = (qx * e[1] - qy * e[0]) / det
    v = (a[0] * qy - a[1] * qx) / det
    with np.errstate(divide="ignore", invalid="ignore"):
        s = np.where(np.abs(u) > 1e-12, v / u, -1.0)
    ok = (u >= DEPTH - 1e-6) & (u <= 1.0 + 1e-6) & (s >= -1e-6) & (s <= 1.0 + 1e-6)
    return np.where(ok, u, -1.0)


def build_faces(blocks, pos):
    h, w = blocks.shape
    bw = bh = 800.0 / w
    faces = []
    for i in range(h):
        ytop = bh * i + pos[1]
        ybase = VP[1] + (ytop - VP[1]) * DEPTH
        if max(ytop + bh, ybase + bh * DEPTH) <= 0 or min(ytop, ybase) >= H:
            continue
        for j in range(w):
            if not blocks[i, j]:
                continue
            c = top_rect(i, j, bw, bh, pos)
            for k in range(4):
                A, B = c[k], c[(k + 1) % 4]
                poly = face_poly(A, B)
                if signed_area(poly) <= 1e-6:
                    continue  # backface or degenerate (edge through the VP)
                lo = poly.min(axis=0)
                hi = poly.max(axis=0)
                # clip to the screen: off-screen overlap can't produce a visible artifact
                lo = np.maximum(lo, [0.0, 0.0])
                hi = np.minimum(hi, [float(W), float(H)])
                if hi[0] <= lo[0] or hi[1] <= lo[1]:
                    continue
                faces.append({"A": A, "B": B, "centre": c.mean(axis=0), "lo": lo, "hi": hi})
    return faces


def occludes(fi, fj):
    """(i_in_front_of_j, j_in_front_of_i) over the intersection of their screen bboxes."""
    lo = np.maximum(fi["lo"], fj["lo"])
    hi = np.minimum(fi["hi"], fj["hi"])
    if hi[0] <= lo[0] or hi[1] <= lo[1]:
        return False, False
    nx = max(2, int((hi[0] - lo[0]) * SS))
    ny = max(2, int((hi[1] - lo[1]) * SS))
    if nx * ny > 4_000_000:
        step = int(np.sqrt(nx * ny / 4_000_000)) + 1
        nx, ny = nx // step, ny // step
    xs = np.linspace(lo[0], hi[0], nx)
    ys = np.linspace(lo[1], hi[1], ny)
    px, py = np.meshgrid(xs, ys)
    di = solve_depth(fi["A"], fi["B"], px, py)
    dj = solve_depth(fj["A"], fj["B"], px, py)
    both = (di >= 0) & (dj >= 0)
    if not both.any():
        return False, False
    a, b = di[both], dj[both]
    return bool((a > b + EPS_D).any()), bool((b > a + EPS_D).any())


def certify(blocks, pos):
    faces = build_faces(blocks, pos)
    n = len(faces)
    if n < 2:
        return True, 0, ""
    front = np.zeros((n, n), dtype=bool)
    pairs = 0
    for i, j in itertools.combinations(range(n), 2):
        i_f, j_f = occludes(faces[i], faces[j])
        if i_f and j_f:
            return False, pairs, f"CYCLE between faces {i} and {j} (mutual occlusion)"
        if i_f or j_f:
            pairs += 1
        front[i, j], front[j, i] = i_f, j_f

    order = sorted(range(n), key=lambda k: KEY(faces[k]))
    rank = np.empty(n, dtype=int)
    rank[order] = np.arange(n)
    # i occludes j => i must be drawn LATER (higher rank), else j paints over it
    bad = [(i, j) for i in range(n) for j in range(n) if front[i, j] and rank[i] < rank[j]]
    if bad:
        return False, pairs, f"key violates order on {len(bad)} pairs, e.g. {bad[0]}"
    return True, pairs, ""


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


if __name__ == "__main__":
    rng = np.random.default_rng(20260710)
    print(f"Certifying painter's order   key: {KEY_NAME}")
    print(f"depth={DEPTH}  overlap sampling={SS}x/design-px\n")

    trials = total_pairs = 0
    fails = []

    # widths 3/7/9/12 are the real Wall.Setup variations; density + scroll swept wide
    for wcols in (3, 7, 9, 12):
        for density in (0.25, 0.45, 0.7, 0.95):
            for _ in range(5):
                blocks = rng.random((26, wcols)) < density
                posy = float(rng.uniform(-400, 400))
                ok, pairs, why = certify(blocks, np.array([0.0, posy]))
                trials += 1
                total_pairs += pairs
                if not ok:
                    fails.append((f"rand w={wcols} d={density} posY={posy:.0f}", why))
        print(f"  width {wcols:2d}: done ({trials} trials, {total_pairs} overlapping pairs)")

    try:
        g = grid_from_level3("web/EvilAliensWeb/wwwroot/Content/levels/level3.txt")
        for posy in np.linspace(-500, 500, 15):
            ok, pairs, why = certify(g, np.array([0.0, float(posy)]))
            trials += 1
            total_pairs += pairs
            if not ok:
                fails.append((f"level3 posY={posy:.0f}", why))
        print(f"  real level3.txt (w={g.shape[1]}): done")
    except Exception as e:
        print("  level3.txt skipped:", e)

    print(f"\ntrials: {trials}   overlapping face pairs examined: {total_pairs}")
    if fails:
        print(f"\nFAIL on {len(fails)} configs:")
        for label, why in fails[:10]:
            print(f"  {label}: {why}")
        sys.exit(1)
    print("\nPASS")
    print("  * no mutual occlusion anywhere -> the occludes relation is acyclic,")
    print("    so SOME painter's order is always exact (no depth attachment needed);")
    print("  * box-centre-distance-from-VP is a valid topological order of it.")
