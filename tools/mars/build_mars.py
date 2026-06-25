#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_mars.py - rebuild Level 2's Mars parallax background from a real NASA
# Mars Pathfinder (1997) panorama.
#
# WHY: GFX/MarsBG/{clouds-background,marshills,mars1..6,clouds-foreground2} were
# the original soft ~600px-tall 2008 art. In-game (?level=Level2) the rocky
# terrain is blurry and the sky a low-res dust haze, while every foreground sprite
# was already AI-upscaled. This re-sources the four alpha-stacked parallax layers
# from the authentic source the originals were based on -- the Mars Pathfinder
# "Twin Peaks" landing-site panorama -- at the SAME framing, supersampled so it
# stays crisp when the 800x600 design frame scales up to the window (RenderScale,
# <=1440 tall).
#
# SOURCE (public domain, NASA/JPL):
#   sources/pathfinder_presidential_pan.jpg   6230x1079
#   https://commons.wikimedia.org/wiki/File:Mars_Pathfinder_Presidential_Panorama.jpg
#   A 360-deg cylindrical color pano of the Ares Vallis landing site: smooth pink
#   sky, low distant hills (incl. Twin Peaks), a rock-strewn plain, and -- in the
#   near field only -- the lander petals/airbags + Sojourner. We use ONLY the
#   horizon band (well above that hardware), exactly how the OG tiles were cut.
#
# LAYER CONSTRUCTION (each output is 600-design tall, drawn at size=1/F so its
# design footprint -- hence scroll-wrap + mirror math in Background.SetMars -- is
# byte-identical to the original; only texel density changes):
#   clouds-background : opaque dusty-tan SKY, synthesized from the source sky's
#                       vertical color gradient + subtle tileable dust mottling.
#   marshills         : the low distant-hill silhouette band, hazy straight alpha.
#   mars1..6          : the distant rocky-plain HORIZON band, sliced into 6 tiles
#                       (5x equal + a wider 6th, matching the OG 1000x5+1220 ratio)
#                       that tile L->R; feathered-transparent top so it dissolves
#                       into the haze. Background.SetMars mirrorX-wraps the strip.
#   clouds-foreground2: a very faint full-frame dust veil (procedural, tileable).
#
# Straight (non-premultiplied) alpha out (the pipeline maps AlphaBlend ->
# NonPremultiplied; CLAUDE.md). Outputs go to wwwroot/Content/gfx/marsbg
# (lowercase under capital Content/ -- the case rule; WebContentManager lowercases
# every request).
#
# Re-run after changing the source or knobs:  python tools/mars/build_mars.py
# Optional crisper source: pass --src <upscaled.png> (e.g. a Real-ESRGAN x2 of the
# pano) -- the band fractions are resolution-independent. Don't hand-edit outputs.
#
# After running, set every re-sourced layer's `size` in Background.SetMars to the
# value this PRINTS (= 1/F), mirroring build_earth.py's doodadscale print.
# ---------------------------------------------------------------------------
import os
import argparse
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_SRC = os.path.join(HERE, "sources", "pathfinder_pia01466.tif")
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "marsbg"))

LANCZOS = getattr(Image, "Resampling", Image).LANCZOS  # Pillow <9.1 vs >=9.1

# --- supersample factor. The OG game cut its mars1..6 strip 1:1 from THIS panorama
#     (the OG strip is 6220px wide; the source is 6222), so the source IS the
#     resolution ceiling -- F=1 (native) is the honest setting and needs NO code
#     change (Background.SetMars keeps size=1). F>1 (with size=1/F in SetMars) only
#     buys real crispness if --src is an AI-upscaled pano; otherwise it just
#     Lanczos-enlarges the source for no gain. Default native. ---------------------
F = 1.0

# --- original design footprints (px @ size=1 in 800x600 design space) ----------
SKY_W, LAYER_H = 1024, 600            # clouds-background / clouds-foreground2
HILLS_W = 1000                        # marshills
GROUND_TILE_W = [1000, 1000, 1000, 1000, 1000, 1220]   # mars1..6 (sum = 6220)

# --- SOURCE band fractions (of source height) ----------------------------------
# The usable vista is the top strip y=0..300 of PIA01466 (1086 tall) -- everything
# below is the lander deck / airbags / Sojourner. Within it: black border y0..6,
# smooth sky y6..135, the Twin-Peaks crest ~y135..195, distant plain y195..300.
# (tunable; fractions are resolution-independent so they survive an upscaled --src.)
SKY_S0, SKY_S1       = 0.010, 0.120   # y~11..130  clean sky -> color ramp
HILLS_SRC0, HILLS_SRC1 = 0.055, 0.225 # y~60..244  sky+peaks, fed to the silhouette cut
GROUND_S0, GROUND_S1 = 0.170, 0.283   # y~185..307 distant rocky plain -> mars1..6
HILLS_SLICE_CX = 0.50                 # center (src-width frac) of the 1000-wide hills slice
# horizontal window of the source actually used (0..1); trims the extreme edges.
SRC_X0, SRC_X1 = 0.0, 1.0

# --- OUTPUT framing (design fractions of the 600-tall layer) -------------------
HORIZON_D   = 0.71                    # where land meets sky
G_TOP_D     = 0.685                   # ground alpha starts feathering in here
G_SOLID_D   = 0.83                    # ...fully opaque by here
H_TOP_D     = 0.585                   # hills band top (alpha 0)
H_PEAK_D    = 0.70                    # hills peak alpha here
H_BOT_D     = 0.83                    # hills faded out by here (ground covers)
HILLS_PEAK_ALPHA = 0.62              # distant hills are hazy, not opaque
HILLS_HAZE  = 0.45                    # blend hills toward sky color (atmosphere)
FG_MAX_ALPHA = 0.20                  # clouds-foreground2 dust veil peak alpha


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def load_src(path):
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    h, w, _ = rgb.shape
    x0, x1 = int(round(SRC_X0 * w)), int(round(SRC_X1 * w))
    return rgb[:, x0:x1, :]


def band(src, s0, s1):
    """Crop a horizontal band [s0,s1) of source height, full (windowed) width."""
    h = src.shape[0]
    return src[int(round(s0 * h)):int(round(s1 * h)), :, :]


def resize(arr, w, h):
    """arr: HxWx{3|4} float 0..1 -> resized float 0..1 (Lanczos)."""
    c = arr.shape[2]
    mode = "RGB" if c == 3 else "RGBA"
    im = Image.fromarray(np.clip(arr * 255.0 + 0.5, 0, 255).astype(np.uint8), mode)
    im = im.resize((w, h), LANCZOS)
    return np.asarray(im, dtype=np.float32) / 255.0


def seamless_x(arr, frac=0.12):
    """Cross-fade the horizontal wrap seam so the layer tiles cleanly: blend the
    last `frac` columns over the first, with a linear ramp."""
    w = arr.shape[1]
    n = max(1, int(round(w * frac)))
    out = arr.copy()
    ramp = np.linspace(0.0, 1.0, n).reshape(1, n, 1)   # 0 at far edge -> 1 inside
    head = out[:, :n, :]
    tail = out[:, w - n:, :]
    out[:, :n, :] = head * ramp + tail * (1.0 - ramp)
    out[:, w - n:, :] = arr[:, w - n:, :] * ramp[:, ::-1, :] + arr[:, :n, :] * (1.0 - ramp[:, ::-1, :])
    return out


def tileable_noise(h, w, octaves=(3, 6, 12), amp=(1.0, 0.5, 0.25), seed=7):
    """Sum of low-freq sinusoids periodic in x AND y -> seamless mottling in -1..1."""
    rng = np.random.RandomState(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    acc = np.zeros((h, w), np.float32)
    for k, a in zip(octaves, amp):
        for _ in range(3):
            fx, fy = rng.randint(1, k + 1), rng.randint(0, k + 1)
            ph = rng.uniform(0, 2 * np.pi)
            acc += a * np.sin(2 * np.pi * (fx * xx / w + fy * yy / h) + ph)
    acc /= np.abs(acc).max() + 1e-6
    return acc


def write(name, rgba):
    im = Image.fromarray(np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA")
    im.save(os.path.join(OUT_DIR, name + ".png"))
    return im.size


# --- layers -----------------------------------------------------------------
def make_sky(src):
    """Opaque dusty-tan sky. Build a vertical color ramp from the source sky and
    horizon haze, fill the frame, add subtle tileable dust mottling up high."""
    tw, th = int(SKY_W * F), int(LAYER_H * F)
    sky = band(src, SKY_S0, SKY_S1)
    ramp = sky.mean(axis=1)                              # source sky rows -> mean RGB
    # map output rows 0..HORIZON to the sky ramp top..bottom; below horizon hold
    # the horizon color (it's hidden behind hills/ground anyway).
    hz = HORIZON_D
    ys = np.linspace(0.0, 1.0, th)
    idx = np.clip(ys / hz, 0.0, 1.0) * (ramp.shape[0] - 1)
    lo = np.floor(idx).astype(int); fr = (idx - lo).reshape(-1, 1)
    hi = np.clip(lo + 1, 0, ramp.shape[0] - 1)
    col = ramp[lo] * (1 - fr) + ramp[hi] * fr           # th x 3
    rgb = np.repeat(col[:, None, :], tw, axis=1)
    # subtle dust mottling, fading out toward the horizon
    n = tileable_noise(th, tw, seed=11)[:, :, None]
    fade = (1.0 - smoothstep(0.15, hz, ys)).reshape(-1, 1, 1)
    rgb = np.clip(rgb + n * 0.025 * fade, 0, 1)
    a = np.ones((th, tw, 1), np.float32)
    return np.concatenate([rgb, a], axis=2)


def _smooth1d(x, k):
    k = int(k) | 1                                       # force odd
    if k < 3:
        return x
    xp = np.pad(x, k // 2, mode="edge")
    return np.convolve(xp, np.ones(k) / k, mode="valid")


def detect_horizon(gray):
    """gray: Hb x W luminance (0..1) of a band with smooth SKY on top, textured
    LAND below. Return a per-column sky->land boundary row, found by brightness
    DROP or vertical TEXTURE -- so the low brown-on-brown Twin Peaks still cut
    cleanly where a colour threshold would smear them into the sky."""
    hb, w = gray.shape
    sky_ref = gray[:max(3, hb // 6)].mean(axis=0)         # per-column sky brightness
    tex = np.zeros_like(gray)
    tex[1:-1] = np.abs(gray[2:] - gray[:-2])              # vertical gradient
    dark = np.clip(sky_ref[None, :] - gray, 0.0, 1.0)     # darker-than-sky
    score = np.maximum(dark / 0.10, tex / 0.05)           # ~>=1 where land begins
    score[1:-1] = (score[:-2] + score[1:-1] + score[2:]) / 3.0   # de-speckle
    start = max(2, hb // 8)
    horizon = np.full(w, float(hb - 1), np.float32)
    for x in range(w):
        col = score[start:, x]
        i = int(np.argmax(col > 1.0))
        if col[i] > 1.0:
            horizon[x] = start + i
    return _smooth1d(horizon, max(5, w // 150))           # de-jitter, keep peak shapes


def make_hills(src):
    """The distant-hill silhouette -> marshills. Cut sky->land by TEXTURE (not
    colour) so the low Twin Peaks separate cleanly from the brown sky; haze for
    atmospheric distance; tile a single native-scale slice as the OG layer did."""
    tw, th = int(HILLS_W * F), int(LAYER_H * F)
    full = band(src, HILLS_SRC0, HILLS_SRC1)              # Hb x Wsrc x 3 (sky + peaks)
    ws = full.shape[1]
    slice_w = int(round(HILLS_W * (ws / sum(GROUND_TILE_W))))  # ~native (1:1) horizontal scale
    cx = int(round(HILLS_SLICE_CX * ws))
    x0 = int(np.clip(cx - slice_w // 2, 0, ws - slice_w))
    seg = full[:, x0:x0 + slice_w, :]
    hb = seg.shape[0]
    gray = 0.299 * seg[:, :, 0] + 0.587 * seg[:, :, 1] + 0.114 * seg[:, :, 2]
    hz = detect_horizon(gray)
    yy = np.arange(hb)[:, None]
    feather = max(2, int(hb * 0.07))
    a = np.clip((yy - hz[None, :]) / feather, 0.0, 1.0)   # 0 above the silhouette, 1 below
    a *= (1.0 - smoothstep(0.6, 1.0, yy / hb))            # fade out low (the plain takes over)
    a *= HILLS_PEAK_ALPHA
    sky_col = band(src, SKY_S0, SKY_S1).mean(axis=(0, 1))
    rgb = seg * (1 - HILLS_HAZE) + sky_col[None, None, :] * HILLS_HAZE
    seg_rgba = np.concatenate([rgb, a[:, :, None]], axis=2)
    top, bot = int(H_TOP_D * th), int(H_BOT_D * th)
    placed = np.zeros((th, tw, 4), np.float32)
    placed[top:bot] = resize(seg_rgba, tw, bot - top)
    return seamless_x(placed, 0.10)


def make_ground(src):
    """Distant rocky-plain horizon band -> the mars1..6 strip, then slice."""
    strip_design_w = sum(GROUND_TILE_W)                 # 6220
    tw, th = int(strip_design_w * F), int(LAYER_H * F)
    g = band(src, GROUND_S0, GROUND_S1)
    top = int(G_TOP_D * th)
    gh = th - top
    gi = resize(g, tw, gh)                              # rocky plain -> bottom band
    rgb = np.zeros((th, tw, 3), np.float32)
    rgb[top:] = gi
    # alpha: feather in from G_TOP -> G_SOLID, solid below
    ys = np.linspace(0.0, 1.0, th)
    a = smoothstep(G_TOP_D, G_SOLID_D, ys).reshape(-1, 1)
    rgba = np.concatenate([rgb, np.repeat(a[:, None, :], tw, axis=1).reshape(th, tw, 1)], axis=2)
    # slice into 6 tiles (continuous; SetMars mirrorX-wraps the strip end)
    sizes = []
    x = 0
    for i, dw in enumerate(GROUND_TILE_W):
        wpx = int(round((x + dw) * F)) - int(round(x * F))
        x0 = int(round(x * F))
        tile = rgba[:, x0:x0 + wpx, :]
        sizes.append(write(f"mars{i+1}", tile))
        x += dw
    return sizes


def make_fg_dust(src):
    """Faint full-frame tileable dust veil -> clouds-foreground2."""
    tw, th = int(SKY_W * F), int(LAYER_H * F)
    dust_col = band(src, SKY_S0, SKY_S1).mean(axis=(0, 1))
    rgb = np.repeat(np.repeat(dust_col[None, None, :], th, 0), tw, 1)
    n = tileable_noise(th, tw, octaves=(2, 4, 8), amp=(1.0, 0.6, 0.3), seed=23)
    n = (n - n.min()) / (n.max() - n.min() + 1e-6)      # 0..1
    a = (n ** 1.6 * FG_MAX_ALPHA).reshape(th, tw, 1)
    return np.concatenate([rgb, a], axis=2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=DEFAULT_SRC, help="source panorama (default: the committed Pathfinder pano; pass an upscaled PNG for crisper output)")
    args = ap.parse_args()
    os.makedirs(OUT_DIR, exist_ok=True)
    src = load_src(args.src)
    print(f"source {os.path.basename(args.src)}  band-fractions are resolution-independent")

    s_sky = write("clouds-background", make_sky(src))
    s_hills = write("marshills", make_hills(src))
    s_ground = make_ground(src)
    s_fg = write("clouds-foreground2", make_fg_dust(src))

    print(f"  clouds-background {s_sky}")
    print(f"  marshills         {s_hills}")
    for i, s in enumerate(s_ground):
        print(f"  mars{i+1}            {s}")
    print(f"  clouds-foreground2 {s_fg}")
    print(f"\n==> set every re-sourced layer's `size` in Background.SetMars to {1.0/F:.4f}  (= 1/F, F={F})")


if __name__ == "__main__":
    main()
