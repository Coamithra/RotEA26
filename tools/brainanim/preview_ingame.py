#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
preview_ingame.py - render the Brain boss + its animated overlays EXACTLY as the
game composites them, into the 800x600 design framing the player sees. This is the
no-browser verification gate: it mirrors BrainBoss.Draw's placement math so seams,
on-screen size, the top-of-screen cutoff and the motion can all be checked from a
generated image (+ a gif), without booting the game.

Math mirrored from the C#:
  textureScale = 1448/850 ; drawScale = scale/textureScale
  boss centred at design (400,100), sprite drawn centre-origin
  overlay centre = boss + (texCenter - (724,543)) * drawScale
  overlay footprint = (texW,texH) * drawScale   (pinned to the brain-texel crop)
  overlay frame ping-pongs 0..N-1..0

Outputs (gitignored): tools/brainanim/_ingame_contact.png (static vs overlay + a
few animated phases) and tools/brainanim/_ingame.gif (the boss animating on space).

Run with the AnimGen venv (PIL). Reads the committed sheets + manifest.
  C:/Programming/animgen/.venv/Scripts/python.exe tools/brainanim/preview_ingame.py
"""
import json
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
WWW = REPO / "web/EvilAliensWeb/wwwroot/Content"
BRAIN = WWW / "gfx/sprites/brainbosshd.png"
MANIFEST = WWW / "data/brainoverlays.json"

DESIGN_W, DESIGN_H = 800, 600
BOSS_POS = (400.0, 100.0)
TEXTURE_SCALE = 1448.0 / 850.0
REF_W, REF_H = 1448.0, 1086.0


def space_bg():
    """Dark space backdrop; use a real space tile if present, else a vignette."""
    for name in ("gfx/game/space/space06.png", "gfx/game/space/space00.png"):
        p = WWW / name
        if p.exists():
            bg = Image.open(p).convert("RGBA").resize((DESIGN_W, DESIGN_H), Image.LANCZOS)
            return bg
    bg = Image.new("RGBA", (DESIGN_W, DESIGN_H), (6, 4, 14, 255))
    return bg


def paste_centre(canvas, img, cx, cy):
    canvas.alpha_composite(img, (int(round(cx - img.width / 2)), int(round(cy - img.height / 2))))


def cell_rect(ov, frame):
    c = frame % ov["cols"]
    r = frame // ov["cols"]
    sep = ov.get("sep", 1)
    x = c * (ov["cellW"] + sep)
    y = r * (ov["cellH"] + sep)
    return (x, y, x + ov["cellW"], y + ov["cellH"])


def pingpong_frame(ov, phase):
    """phase 0..1 -> integer frame with ping-pong. FLOORS (int) to match the engine's
    frame SELECTION (BrainBossOverlays.FramePair f0 = (int)pos); the engine additionally
    cross-fades f0->f1 via the interpolation shader, which this static preview omits, so
    the preview is a slightly steppier lower bound on smoothness."""
    n = ov["frames"]
    if n <= 1:
        return 0
    span = 2 * (n - 1)
    t = (phase * span) % span
    pos = span - t if t > (n - 1) else t
    return min(n - 1, int(pos))


def render(scale, phase, overlays, brain, sheets, with_overlays=True):
    drawScale = scale / TEXTURE_SCALE
    canvas = space_bg()
    bw = max(1, round(brain.width * drawScale))
    bh = max(1, round(brain.height * drawScale))
    paste_centre(canvas, brain.resize((bw, bh), Image.LANCZOS), *BOSS_POS)
    if with_overlays:
        sx, sy = brain.width / REF_W, brain.height / REF_H
        for ov in overlays:
            sheet = sheets[ov["name"]]
            f = pingpong_frame(ov, phase)
            cell = sheet.crop(cell_rect(ov, f))
            pw = max(1, round(ov["texW"] * sx * drawScale))
            ph = max(1, round(ov["texH"] * sy * drawScale))
            cell = cell.resize((pw, ph), Image.LANCZOS)
            cx = BOSS_POS[0] + (ov["texCenterX"] * sx - brain.width / 2) * drawScale
            cy = BOSS_POS[1] + (ov["texCenterY"] * sy - brain.height / 2) * drawScale
            paste_centre(canvas, cell, cx, cy)
    return canvas


def main():
    if not MANIFEST.exists():
        raise SystemExit(f"no manifest at {MANIFEST}; run build_brain_overlays.py first")
    overlays = json.loads(MANIFEST.read_text(encoding="utf-8"))["overlays"]
    brain = Image.open(BRAIN).convert("RGBA")
    sheets = {ov["name"]: Image.open(WWW / "gfx/sprites" / (Path(ov["sheet"]).name + ".png")).convert("RGBA")
              for ov in overlays}
    print(f"{len(overlays)} overlays: " + ", ".join(o["name"] for o in overlays))

    # contact sheet: static (no overlays) | overlays at 4 phases
    tiles = [("static", render(1.0, 0.0, overlays, brain, sheets, with_overlays=False))]
    for i, ph in enumerate([0.0, 0.25, 0.5, 0.75]):
        tiles.append((f"phase {ph}", render(1.0, ph, overlays, brain, sheets)))
    cols = len(tiles)
    pad = 6
    cw, ch = DESIGN_W, DESIGN_H
    contact = Image.new("RGBA", (cols * cw + (cols + 1) * pad, ch + 2 * pad), (24, 24, 28, 255))
    for i, (_, img) in enumerate(tiles):
        contact.alpha_composite(img, (pad + i * (cw + pad), pad))
    out = HERE / "_ingame_contact.png"
    contact.convert("RGB").save(out)
    print(f"-> {out.name}  ({tiles[0][1].size} tiles: static + 4 phases)")

    # animated gif over one ping-pong loop (crop to the boss region for a tighter view)
    N = 48
    frames = [render(1.0, k / N, overlays, brain, sheets).convert("RGB")
              for k in range(N)]
    crop = (60, 0, 740, 470)   # design-space region around the on-screen boss
    frames = [f.crop(crop) for f in frames]
    g = HERE / "_ingame.gif"
    frames[0].save(g, save_all=True, append_images=frames[1:], duration=70, loop=0)
    print(f"-> {g.name}  ({N} frames, cropped {crop})")


if __name__ == "__main__":
    main()
