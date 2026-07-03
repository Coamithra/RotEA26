# Build the two small art assets for the "I Made This!" webcam challenge level
# (Stage: webcam-aliens). Offline, Pillow-only, deterministic — CI just ships the
# committed outputs, like the other tools/ asset steps.
#
#   1. webcamss.png  — the Challenges-carousel screenshot. Cropped straight out of
#      the "I made this!" meme splash (the embedded mini-screenshot of the ORIGINAL
#      2004 webcam game this level remakes), upscaled to the 800x600 the carousel
#      expects. The soft upscale is period-authentic; the carousel draws it at
#      <= 0.4x anyway.
#   2. heart.png     — the lives HUD heart (the original game showed a row of pink
#      hearts top-left). Drawn procedurally: a chunky pixel heart, upscaled with
#      NEAREST so it keeps the 2004-webcam-game chic, straight (non-premultiplied)
#      alpha per the project's alpha convention.
#
# Re-run after changing the knobs:  python tools/webcam/build_webcam_assets.py
# Don't hand-edit the outputs.
import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
CONTENT = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx")

# The embedded original-game screenshot inside the meme splash (measured: the dense
# bright block; rows 221..369 are the framed image, the caption starts ~y394).
SPLASH = os.path.join(CONTENT, "splash", "uglysplash22.png")
CROP = (295, 221, 501, 370)  # left, top, right, bottom (exclusive)

SCREENSHOT_OUT = os.path.join(CONTENT, "screenshots", "webcamss.png")
HEART_OUT = os.path.join(CONTENT, "sprites", "heart.png")


def build_screenshot():
    im = Image.open(SPLASH).convert("RGB")
    shot = im.crop(CROP)
    # The carousel sizes entries as 800/width x 600/height, so emit exactly 800x600.
    shot = shot.resize((800, 600), Image.LANCZOS)
    shot.save(SCREENSHOT_OUT)
    print("wrote", os.path.relpath(SCREENSHOT_OUT, ROOT), shot.size)


# 12x11 pixel-heart mask (1 = filled). Classic two-lobe heart.
HEART_ROWS = [
    "..XXX..XXX..",
    ".XXXXXXXXXX.",  # widened row below fixes the lobe join
    "XXXXXXXXXXXX",
    "XXXXXXXXXXXX",
    "XXXXXXXXXXXX",
    ".XXXXXXXXXX.",
    ".XXXXXXXXXX.",
    "..XXXXXXXX..",
    "...XXXXXX...",
    "....XXXX....",
    ".....XX.....",
]


def build_heart():
    w, h = len(HEART_ROWS[0]), len(HEART_ROWS)
    base = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    body = (255, 64, 120, 255)     # the meme's pink
    shade = (196, 24, 84, 255)
    for y, row in enumerate(HEART_ROWS):
        for x, c in enumerate(row):
            if c != "X":
                continue
            # right/bottom edge pixels get the darker shade for a hint of depth
            edge = (x + 1 >= w or row[x + 1] != "X") or (
                y + 1 >= h or (x < len(HEART_ROWS[y + 1]) and HEART_ROWS[y + 1][x] != "X"))
            d.point((x, y), shade if edge else body)
    # two-pixel highlight on the left lobe
    d.point((2, 1), (255, 170, 200, 255))
    d.point((3, 1), (255, 170, 200, 255))
    heart = base.resize((w * 4, h * 4), Image.NEAREST)  # 48x44, chunky
    heart.save(HEART_OUT)
    print("wrote", os.path.relpath(HEART_OUT, ROOT), heart.size)


if __name__ == "__main__":
    build_screenshot()
    build_heart()
