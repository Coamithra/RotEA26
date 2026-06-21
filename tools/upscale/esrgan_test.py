"""
Path-A test: faithful Real-ESRGAN super-resolution of a grid sprite sheet.

Slices the sheet on the SAME grid the engine uses
(frame = (W-(cols-1)*sep)/cols, placed at col*(frame+sep)), upscales each frame
through Real-ESRGAN x4 (anime 6B weights), and repacks with a 1px separator so
the runtime's `texture.Width/columns` math auto-derives the new frame size with
ZERO code changes. Alpha is kept STRAIGHT (repo rule) and RGB is colour-bled into
transparent regions first so edges don't pick up a dark halo when upscaled.

Outputs (under tools/upscale/out/):
  <name>_x4_esrgan.png   the repacked, upscaled sheet (the deliverable)
  <name>_compare.png     3 sample frames x [nearest | lanczos | esrgan], for eyeballing
"""
import sys, os
import numpy as np
import cv2
from PIL import Image
import torch
from spandrel import ModelLoader, ImageModelDescriptor

MODEL = os.path.join(os.path.dirname(__file__), "models", "RealESRGAN_x4plus_anime_6B.pth")
OUT   = os.path.join(os.path.dirname(__file__), "out")

def load_model():
    m = ModelLoader().load_from_file(MODEL)
    assert isinstance(m, ImageModelDescriptor)
    m.cuda().eval()
    print("model loaded: scale=%dx  arch=%s" % (m.scale, m.architecture.name))
    return m

def slice_grid(img, rows, cols, sep):
    """img: HxWx4 uint8 -> list of (r,c,frame) 48x48 RGBA crops, engine-exact."""
    H, W = img.shape[:2]
    fw = (W - (cols - 1) * sep) // cols
    fh = (H - (rows - 1) * sep) // rows
    frames = []
    for r in range(rows):
        for c in range(cols):
            x = c * (fw + sep); y = r * (fh + sep)
            frames.append((r, c, img[y:y+fh, x:x+fw].copy()))
    return frames, fw, fh

def bleed_rgb(rgba):
    """Flood opaque colour into transparent areas so 4x upscale gets no dark halo."""
    rgb = rgba[:, :, :3]
    a   = rgba[:, :, 3]
    holes = (a == 0).astype(np.uint8) * 255
    if holes.any() and (a > 0).any():
        rgb = cv2.inpaint(rgb, holes, 3, cv2.INPAINT_TELEA)
    out = rgba.copy(); out[:, :, :3] = rgb
    return out

@torch.no_grad()
def esrgan_rgb_batch(model, rgb_list):
    """rgb_list: list of HxWx3 uint8 (all same size) -> list of 4Hx4Wx3 uint8."""
    t = np.stack(rgb_list).astype(np.float32) / 255.0      # N,H,W,3
    t = torch.from_numpy(t).permute(0, 3, 1, 2).cuda()      # N,3,H,W RGB 0..1
    out = model(t).clamp(0, 1).cpu().numpy()                # N,3,4H,4W
    out = (out.transpose(0, 2, 3, 1) * 255.0 + 0.5).astype(np.uint8)
    return list(out)

def up_alpha(a, scale):
    """Lanczos upscale of the straight alpha channel."""
    im = Image.fromarray(a, "L").resize((a.shape[1]*scale, a.shape[0]*scale), Image.LANCZOS)
    return np.asarray(im)

def main(sheet_path, rows, cols, sep, scale=4):
    os.makedirs(OUT, exist_ok=True)
    name = os.path.splitext(os.path.basename(sheet_path))[0]
    img = np.asarray(Image.open(sheet_path).convert("RGBA"))
    H, W = img.shape[:2]
    print("sheet %s  %dx%d  grid %dx%d sep %d" % (name, W, H, cols, rows, sep))

    frames, fw, fh = slice_grid(img, rows, cols, sep)
    print("  frame size %dx%d  (%d frames)" % (fw, fh, len(frames)))

    model = load_model()

    bled = [bleed_rgb(f) for (_, _, f) in frames]
    rgb_up = esrgan_rgb_batch(model, [b[:, :, :3] for b in bled])
    a_up   = [up_alpha(f[:, :, 3], scale) for (_, _, f) in frames]

    up = []
    for rgb, a in zip(rgb_up, a_up):
        rgba = np.dstack([rgb, a])           # straight alpha, no premultiply
        up.append(rgba)

    # repack: frame = fw*scale, separator stays 1px (engine constant), exact layout
    nfw, nfh = fw * scale, fh * scale
    nsep = sep                                # engine's separatingspace is unchanged
    NW = cols * nfw + (cols - 1) * nsep
    NH = rows * nfh + (rows - 1) * nsep
    sheet = np.zeros((NH, NW, 4), np.uint8)
    for (r, c, _), rgba in zip(frames, up):
        x = c * (nfw + nsep); y = r * (nfh + nsep)
        sheet[y:y+nfh, x:x+nfw] = rgba
    out_path = os.path.join(OUT, "%s_x%d_esrgan.png" % (name, scale))
    Image.fromarray(sheet, "RGBA").save(out_path)
    print("  -> %s  (%dx%d)" % (out_path, NW, NH))

    # verify engine's slicing formula reproduces our frame size exactly
    chk_w = (NW - (cols - 1) * nsep) // cols
    chk_h = (NH - (rows - 1) * nsep) // rows
    print("  engine recompute: frame=%dx%d  %s" %
          (chk_w, chk_h, "OK" if (chk_w, chk_h) == (nfw, nfh) else "MISMATCH!"))

    # comparison strip: 3 sample frames x [nearest | lanczos | esrgan]
    picks = [0, len(frames)//2, len(frames)-1]
    rows_img = []
    for i in picks:
        _, _, f = frames[i]
        near = np.asarray(Image.fromarray(f, "RGBA").resize((nfw, nfh), Image.NEAREST))
        lanc = np.asarray(Image.fromarray(f, "RGBA").resize((nfw, nfh), Image.LANCZOS))
        esr  = up[i]
        gap = np.zeros((nfh, 6, 4), np.uint8)
        rows_img.append(np.hstack([near, gap, lanc, gap, esr]))
    vgap = np.zeros((6, rows_img[0].shape[1], 4), np.uint8)
    strip = rows_img[0]
    for r in rows_img[1:]:
        strip = np.vstack([strip, vgap, r])
    # checkerboard so transparency is visible
    bg = np.zeros_like(strip); s = 12
    for y in range(0, bg.shape[0], s):
        for x in range(0, bg.shape[1], s):
            bg[y:y+s, x:x+s, :3] = 90 if ((x//s + y//s) % 2) else 140
    bg[:, :, 3] = 255
    a = strip[:, :, 3:4].astype(np.float32)/255.0
    comp = (strip[:, :, :3].astype(np.float32)*a + bg[:, :, :3].astype(np.float32)*(1-a)).astype(np.uint8)
    cmp_path = os.path.join(OUT, "%s_compare.png" % name)
    Image.fromarray(comp, "RGB").save(cmp_path)
    print("  -> %s  (cols: NEAREST | LANCZOS | ESRGAN)" % cmp_path)

if __name__ == "__main__":
    sheet = sys.argv[1]
    rows  = int(sys.argv[2]); cols = int(sys.argv[3]); sep = int(sys.argv[4])
    main(sheet, rows, cols, sep)
