"""
Build a magenta CONTACT SHEET of not-yet-upscaled sprites, for a single
ChatGPT/Gemini "redraw at higher res" pass over many small sprites at once.

Why this exists (Trello "small sprites upscale effort"): the per-sprite pipeline
in UPSCALING.md does one sheet at a time. The little static sprites (bullets,
blood drops, debris, ...) are cheap to batch — pack them all into ONE gridded
image, redraw the whole thing, then slice each cell back out and repack with the
existing tools. This builds the INPUT sheet (part 1); the JSON manifest it writes
is what lets part 2 slice + invert the scale + align each result to its original.

Layout (per cell): a dark label strip on top (cell id + filename + native WxH —
junk the keyer ignores) over a flat #FF00FF sprite area with the sprite centered,
nearest-neighbour upscaled to fill `fit` px with a generous magenta margin (so any
AI-added decoration lands in the croppable border, per UPSCALING.md). The grid is
uniform, so part 2 slices by geometry, not by reading the labels back.

Usage:
    python tools/upscale/build_contact_sheet.py smalls
    python tools/upscale/build_contact_sheet.py mediums
    python tools/upscale/build_contact_sheet.py all

Run from the repo root (Content paths are resolved relative to the web project).
Outputs: tools/upscale/contact_sheets/<set>.png  +  <set>.json
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

MAGENTA = (255, 0, 255)
LABEL_BG = (32, 32, 32)
LABEL_FG = (235, 235, 235)
GRID_LINE = (70, 70, 70)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites")
OUT_DIR = os.path.join(HERE, "contact_sheets")

# Sprite sets — Content/gfx/sprites base names, confirmed still at original .xnb
# resolution (current png == original xnb dims) and in-use in Game/.
SETS = {
    # The genuinely small single-frame gameplay sprites ("bullets and what not").
    "smalls": dict(
        names=[
            "bulletevil", "bulletgood", "blooddrop", "blooddrop_green",
            "option", "braingoo", "arrow", "photocamera", "shadow",
            "block", "singleconnectorglow", "connector",
            "spiderdebris1", "spiderdebris2", "spiderdebris3",
        ],
        cols=5, fit=224, area=288, label_h=44, font_px=18,
    ),
    # The bigger still-original single frames — shown near-native with margin.
    "mediums": dict(
        names=["parachute", "blast", "plasmaball2", "awardmentblade"],
        cols=2, fit=680, area=760, label_h=52, font_px=26,
    ),
}


def load_font(px):
    for name in ("arial.ttf", "DejaVuSans.ttf", "segoeui.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            continue
    return ImageFont.load_default()


def build(set_name):
    cfg = SETS[set_name]
    names, cols = cfg["names"], cfg["cols"]
    fit, area, label_h = cfg["fit"], cfg["area"], cfg["label_h"]
    rows = (len(names) + cols - 1) // cols
    cell_w, cell_h = area, area + label_h
    sheet_w, sheet_h = cols * cell_w, rows * cell_h

    sheet = Image.new("RGB", (sheet_w, sheet_h), MAGENTA)
    draw = ImageDraw.Draw(sheet)
    font = load_font(cfg["font_px"])

    cells = []
    for i, name in enumerate(names):
        c, r = i % cols, i // cols
        cx, cy = c * cell_w, r * cell_h
        # label strip
        draw.rectangle([cx, cy, cx + cell_w - 1, cy + label_h - 1], fill=LABEL_BG)
        cell_id = "%s%d" % (chr(ord("A") + r), c + 1)

        src = os.path.join(SPRITES, name + ".png")
        im = Image.open(src).convert("RGBA")
        w, h = im.size
        # integer nearest-neighbour upscale to fill `fit`; never downscale below 1
        scale = max(1, fit // max(w, h))
        sw, sh = w * scale, h * scale
        if max(sw, sh) > fit:  # big medium sprite — fit exactly (lanczos down)
            ratio = fit / max(w, h)
            sw, sh = round(w * ratio), round(h * ratio)
            up = im.resize((sw, sh), Image.LANCZOS)
            scale = round(ratio, 4)
        else:
            up = im.resize((sw, sh), Image.NEAREST)

        # area origin (below the label strip) and centered placement
        ax, ay = cx, cy + label_h
        px = ax + (area - sw) // 2
        py = ay + (area - sh) // 2
        # composite straight-alpha sprite over magenta
        sheet.paste(up, (px, py), up)

        draw.text((cx + 6, cy + (label_h - cfg["font_px"]) // 2 - 2),
                  "%s  %s  %dx%d" % (cell_id, name, w, h), fill=LABEL_FG, font=font)

        cells.append(dict(
            id=cell_id, name=name, src="gfx/sprites/%s.png" % name,
            native=[w, h], scale=scale,
            cell_xy=[cx, cy],
            sprite_area=[ax, ay, area, area],
            placed=[px, py, sw, sh],
        ))

    # grid lines between cells (drawn last, over the magenta margins)
    for c in range(1, cols):
        draw.line([(c * cell_w, 0), (c * cell_w, sheet_h)], fill=GRID_LINE, width=2)
    for r in range(1, rows):
        draw.line([(0, r * cell_h), (sheet_w, r * cell_h)], fill=GRID_LINE, width=2)

    os.makedirs(OUT_DIR, exist_ok=True)
    png = os.path.join(OUT_DIR, set_name + ".png")
    sheet.save(png)
    manifest = dict(
        sheet=set_name, background="#FF00FF",
        grid=dict(cols=cols, rows=rows),
        cell=dict(w=cell_w, h=cell_h, area=area, label_h=label_h, fit=fit),
        size=[sheet_w, sheet_h], cells=cells,
    )
    with open(os.path.join(OUT_DIR, set_name + ".json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print("%-8s %dx%d  %d sprites (%dx%d grid) -> %s"
          % (set_name, sheet_w, sheet_h, len(names), cols, rows,
             os.path.relpath(png, REPO)))


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    todo = list(SETS) if which == "all" else [which]
    for s in todo:
        build(s)
