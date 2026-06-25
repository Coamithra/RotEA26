#!/usr/bin/env python
# Experimental stitcher for the upscaled mars quarters (magenta-backed).
# Pipeline per tile: recolor->original (done upstream) -> soft sky/alpha (fringe-aware)
# -> terrain-fill the sky -> SHEAR the right half (seam edge down `step`, far edge 0)
# -> Laplacian (multiband) blend across the abutment -> recomposite clean magenta.
import numpy as np
from PIL import Image

MAG = np.array([255, 0, 255], np.float32)
try:
    import cv2
    HAVE_CV2 = True
except Exception:
    HAVE_CV2 = False


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def sky_score(rgb):
    """1 = magenta/cyan backdrop (incl. anti-alias fringe), 0 = terrain.
    Backdrop has high B AND low min(R,G); white rocks (high min(R,G)) are spared."""
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    sb = np.clip((B - 120) / 70.0, 0, 1)              # high blue
    sm = np.clip((70.0 - np.minimum(R, G)) / 35.0, 0, 1)  # low min(R,G) => magenta/cyan, not white
    return sb * sm


def alpha_from(rgb):
    return 1.0 - sky_score(rgb)                        # 1 terrain, 0 sky (soft edge)


def horizon(alpha, thr=0.5):
    H, W = alpha.shape
    return np.array([np.argmax(alpha[:, x] > thr) if (alpha[:, x] > thr).any() else H - 1
                     for x in range(W)])


def terrain_fill(rgb, alpha):
    """Replace the sky region's RGB with the terrain colour at the horizon, extended
    upward, so a pyramid blend never pulls magenta into the terrain."""
    H, W = alpha.shape
    hz = horizon(alpha)
    out = rgb.copy()
    for x in range(W):
        out[:hz[x], x] = rgb[hz[x], x]
    return out


def shear_v(img, shifts):
    """Per-column DOWNWARD vertical shift (sub-pixel, linear). Exposed top/bottom
    edge-replicate (top of a terrain-filled img is terrain colour; alpha top is 0)."""
    H, W = img.shape[:2]
    yy = np.arange(H)
    out = np.empty_like(img)
    for x in range(W):
        src = yy - shifts[x]
        s0 = np.floor(src).astype(int)
        fr = (src - s0)
        s0c = np.clip(s0, 0, H - 1)
        s1c = np.clip(s0 + 1, 0, H - 1)
        if img.ndim == 3:
            out[:, x] = img[s0c, x] * (1 - fr)[:, None] + img[s1c, x] * fr[:, None]
        else:
            out[:, x] = img[s0c, x] * (1 - fr) + img[s1c, x] * fr
    return out


def _pyr_blend_cv2(A, B, mask, levels=6):
    A = A.astype(np.float32); B = B.astype(np.float32)
    m = mask.astype(np.float32)
    GA, GB, GM = [A], [B], [m]
    for _ in range(levels):
        GA.append(cv2.pyrDown(GA[-1])); GB.append(cv2.pyrDown(GB[-1])); GM.append(cv2.pyrDown(GM[-1]))
    out = GA[-1] * (1 - GM[-1][..., None]) + GB[-1] * GM[-1][..., None]
    for i in range(levels - 1, -1, -1):
        sz = (GA[i].shape[1], GA[i].shape[0])
        LA = GA[i] - cv2.pyrUp(GA[i + 1], dstsize=sz)
        LB = GB[i] - cv2.pyrUp(GB[i + 1], dstsize=sz)
        lap = LA * (1 - GM[i][..., None]) + LB * GM[i][..., None]
        out = cv2.pyrUp(out, dstsize=sz) + lap
    return out


def _pyr_blend_np(A, B, mask, levels=6):
    # separable-gaussian fallback; good enough for a smooth multiband blend
    def blur(x):
        k = np.array([1, 4, 6, 4, 1], np.float32); k /= k.sum()
        import numpy as _np
        def conv1(a, ax):
            pad = [(0, 0)] * a.ndim; pad[ax] = (2, 2)
            ap = _np.pad(a, pad, mode="edge")
            acc = _np.zeros_like(a)
            for i, w in enumerate(k):
                sl = [slice(None)] * a.ndim; sl[ax] = slice(i, i + a.shape[ax])
                acc += w * ap[tuple(sl)]
            return acc
        return conv1(conv1(x, 0), 1)
    A = A.astype(np.float32); B = B.astype(np.float32); m = mask.astype(np.float32)
    out = np.zeros_like(A); cur_m = m.copy()
    rA, rB = A.copy(), B.copy()
    for _ in range(levels):
        bA, bB, bm = blur(rA), blur(rB), blur(cur_m)
        out += (rA - bA) * (1 - cur_m[..., None]) + (rB - bB) * cur_m[..., None]
        rA, rB, cur_m = bA, bB, bm
    out += rA * (1 - cur_m[..., None]) + rB * cur_m[..., None]
    return out


def pyr_blend(A, B, mask, levels=6):
    return _pyr_blend_cv2(A, B, mask, levels) if HAVE_CV2 else _pyr_blend_np(A, B, mask, levels)


def smoothmask(W, seam, width):
    x = np.arange(W, dtype=np.float32)
    t = np.clip((x - (seam - width / 2)) / width, 0, 1)
    return (t * t * (3 - 2 * t))[None, :].repeat(1, 0)  # 1 x W -> broadcast later


def stitch_two(bl_path, br_path, out_path, levels=6, alpha_blend_w=140):
    bl, br = load(bl_path), load(br_path)
    H, lw = bl.shape[:2]; rw = br.shape[1]
    al_bl, al_br = alpha_from(bl), alpha_from(br)
    # seam step: bl right-edge horizon vs br left-edge horizon
    hb, hr = horizon(al_bl), horizon(al_br)
    step = float(hb[-80:].mean() - hr[:80].mean())
    # SHEAR br: left edge (x=0, seam side) down `step`, far edge (x=rw-1) -> 0
    shifts = step * (1.0 - np.arange(rw) / (rw - 1))
    br_tf = shear_v(terrain_fill(br, al_br), shifts)
    al_br_s = shear_v(al_br, shifts)
    bl_tf = terrain_fill(bl, al_bl)
    # abut on a full canvas; pad each side by edge-extension for the pyramid
    W = lw + rw
    seam = lw
    A = np.concatenate([bl_tf, np.repeat(bl_tf[:, -1:], rw, axis=1)], axis=1)
    B = np.concatenate([np.repeat(br_tf[:, :1], lw, axis=1), br_tf], axis=1)
    mA = np.concatenate([al_bl, np.repeat(al_bl[:, -1:], rw, axis=1)], axis=1)
    mB = np.concatenate([np.repeat(al_br_s[:, :1], lw, axis=1), al_br_s], axis=1)
    mask = (np.arange(W) >= seam).astype(np.float32)[None, :].repeat(H, 0)
    rgb = pyr_blend(A, B, mask, levels)
    # alpha: linear blend with a soft seam mask (keeps horizon crisp, no hard alpha edge)
    x = np.arange(W, dtype=np.float32)
    sm = np.clip((x - (seam - alpha_blend_w / 2)) / alpha_blend_w, 0, 1)
    sm = (sm * sm * (3 - 2 * sm))[None, :]
    alpha = mA * (1 - sm) + mB * sm
    alpha = np.clip(alpha, 0, 1)[..., None]
    out = np.clip(rgb, 0, 255) * alpha + MAG * (1 - alpha)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").save(out_path)
    return W, H, step


if __name__ == "__main__":
    import sys
    bl, br, out = sys.argv[1], sys.argv[2], sys.argv[3]
    W, H, step = stitch_two(bl, br, out)
    print(f"wrote {out}  {W}x{H}  (cv2={HAVE_CV2}, seam step={step:.1f}px)")
