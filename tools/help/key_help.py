# Chroma-key the two hi-res "Controls" help screens (new controllerhelp / new
# keyboardhelp). TRICKY: the AI used the magenta key colour as a real foreground
# colour too -- BUT measurement shows the background is pure BRIGHT magenta with
# green ~= 1-2, while every foreground "magenta-ish" element is actually VIOLET
# (green ~= 45+) or DARK grunge (low brightness). So we key on:
#
#   background  <=>  bright (min(R,B) high)  AND  near-zero green
#
# - foreground purple/violet  -> green too high  -> kept
# - dark grunge outlines      -> brightness too low -> kept (this also protects the
#                                AA band between bg and a dark edge from over-keying)
# - enclosed magenta pockets (letter counters, gaps) -> same colour test, no
#                                connectivity needed, so they key correctly
#
# Despill is weighted by background-likeness, so interior foreground purple (bg~0)
# is left untouched while only the keyed/edge pixels get their magenta spill removed.
# Output: straight-alpha RGBA at native res (drawn scale-to-fit in-engine).
import os
import sys
import numpy as np
from PIL import Image

RAW = os.path.join(os.path.dirname(__file__), "..", "..", "new_assets_raw")
OUTDIR = os.path.join(os.path.dirname(__file__), "..", "..",
                      "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "help")
PREVIEW = os.path.join(os.path.dirname(__file__), "preview")

# source -> destination filename (note the SPACE; load string is "GFX/Help/Controls ...")
JOBS = {
    "new keyboardhelp.png":   "controls keyboard.png",
    "new controllerhelp.png": "controls joypad.png",
}

# --- tunables ---
# green ramp is wide on the high end so AA "halo" px (green ~25-40, between the bg's
# ~2 and real foreground violet's ~45+) get PARTIAL alpha and thus get decontaminated.
G_LO, G_HI   = 6.0, 42.0     # green: <=G_LO fully bg-eligible, >=G_HI never bg
BR_LO, BR_HI = 80.0, 170.0   # min(R,B): <=BR_LO never bg (dark fg), >=BR_HI fully bright
BAL          = 70.0          # |R-B| above this is not balanced magenta (kept)


def smooth(x):  # smoothstep 0..1
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def key(arr, C):
    R, G, B = arr[..., 0], arr[..., 1], arr[..., 2]
    lo = np.minimum(R, B)
    f_green  = smooth((G_HI - G) / (G_HI - G_LO))        # 1 when green near 0
    f_bright = smooth((lo - BR_LO) / (BR_HI - BR_LO))    # 1 when bright magenta
    f_bal    = smooth((BAL - np.abs(R - B)) / 30.0)      # 1 when R~=B
    bg = f_green * f_bright * f_bal                       # 1 = background, 0 = foreground
    alpha = 1.0 - bg
    # UNMULTIPLY: a partial-alpha pixel P is foreground F over the bg colour C:
    #   P = a*F + (1-a)*C   ->   F = (P - (1-a)*C) / a
    # This removes the magenta contribution from every AA edge px exactly, while
    # full-opacity interior px (a=1) are returned unchanged -> foreground violet kept.
    a = np.clip(alpha, 0.0, 1.0)[..., None]
    safe = np.maximum(a, 0.10)                            # avoid blow-up where ~transparent
    F = (arr - (1.0 - a) * np.array(C, np.float32)) / safe
    F = np.where(a > 0.02, F, 0.0)
    out = np.concatenate([np.clip(F, 0, 255),
                          np.clip(alpha[..., None] * 255.0, 0, 255)], axis=-1)
    return out.astype(np.uint8), bg


def composite(rgba, bg_color):
    a = rgba[..., 3:4].astype(np.float32) / 255.0
    rgb = rgba[..., :3].astype(np.float32)
    return (rgb * a + np.array(bg_color, np.float32) * (1 - a)).astype(np.uint8)


def main():
    write = "--write" in sys.argv
    os.makedirs(PREVIEW, exist_ok=True)
    for src, dst in JOBS.items():
        im = Image.open(os.path.join(RAW, src)).convert("RGB")
        arr = np.asarray(im).astype(np.float32)
        H, W, _ = arr.shape
        # background colour C = mean of the four corners (pure bright magenta)
        C = np.stack([arr[2, 2], arr[2, W - 3], arr[H - 3, 2], arr[H - 3, W - 3]]).mean(0)
        rgba, _bg = key(arr, C)
        kept = (rgba[..., 3] > 16).sum()
        print(f"{src:24s} {im.size}  kept {100*kept/(H*W):5.1f}%  "
              f"-> {dst}{' [WROTE]' if write else ' [preview only]'}")
        # previews: over black (in-game look) + over green (spot leftover magenta halos)
        base = os.path.splitext(dst)[0].replace(" ", "_")
        Image.fromarray(composite(rgba, (0, 0, 0))).save(
            os.path.join(PREVIEW, base + "_on_black.png"))
        Image.fromarray(composite(rgba, (0, 160, 0))).save(
            os.path.join(PREVIEW, base + "_on_green.png"))
        if write:
            Image.fromarray(rgba, "RGBA").save(os.path.join(OUTDIR, dst), optimize=True)


if __name__ == "__main__":
    main()
