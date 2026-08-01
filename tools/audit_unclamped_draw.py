#!/usr/bin/env python
"""Audit SpriteBatchWrapper's raw draws for a missing source-rect clamp (card b7e9b106).

`.dds` textures are PADDED up to a mult-of-4 -- grossly, while the `--padtest 100` canary is on:
`blank` is a 10x10 white pixel inside a 112x112 canvas -- and the logical size lives only in the
DDS header. So a whole-texture draw that passes NO source rectangle spans the pad as well: the
transparent pad reads BLACK under Opaque blend, and a STRETCHED one covers only the logical
fraction of the destination it was asked to fill.

That is not hypothetical. `SealAlpha` was written with the un-clamped
`spriteBatch.Draw(texture, dest, color)` overload, so it sealed only the top-left ~100x75 px of
the death cross-fade's 800x600 snapshot. The dissolve overlay then had no alpha to blend with
outside that corner, and the fade silently did nothing -- active game objects stayed fully solid
for the whole 1.5 s and then vanished at the purge. Nothing threw, nothing logged, and the timer,
the blend state and the tint alpha were all provably correct.

THE RULE, and it is exactly the claim web CLAUDE.md makes ("Whole-texture draws MUST clamp their
source to LogicalBounds() -- the wrapper's Draw overloads do"): inside `SpriteBatchWrapper`, every
`spriteBatch.Draw(...)` carries a source rectangle. `LogicalBounds()` is the usual one; a
caller-supplied `Rectangle` (the tile walk's `b`, the group-flatten `used` box) is equally fine --
the point is that SOMETHING bounds the sample.

THE ONE EXEMPTION: a batch begun with a custom `Effect` may sample the full padded texture,
because that is the `ContentScale` (= logical/padded) contract -- `DrawEffect` hands the shader
the ratio and the shader does its [0,1] frame math in `tc/ContentScale` (web CLAUDE.md, "DXT
textures are PADDED"). Clamping there would double-correct.

SCOPE is deliberately this ONE file: it is the wrapper whose overloads the docs vouch for, and
its `spriteBatch` field is private to it. `Game1`'s own raw batch (the present blit, the slow-mo
trail, the holo-sim ping-pong) only ever draws render targets, which are never padded.

    python tools/audit_unclamped_draw.py              # exit 1 if suspects found
    python tools/audit_unclamped_draw.py --selftest   # pin the rule, no repo needed
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TARGET = ROOT / "web" / "EvilAliensWeb" / "Game" / "EvilAliens" / "SpriteBatchWrapper.cs"

DRAW_RE = re.compile(r"\bspriteBatch\.Draw\(")
BEGIN_RE = re.compile(r"\bspriteBatch\.Begin\(")
# a member declaration at class-nesting depth 1 (one tab), attribute lines included
MEMBER_RE = re.compile(r"^\t(?:\[|(?:public|private|protected|internal)\s)")

# Argument 3 of SpriteBatch.Draw is EITHER the source rect OR the colour -- there is no third
# possibility across XNA's overload set. So the test is "is it a colour", and anything that is
# not recognised as one is taken to be a source rect. That polarity is the safe one HERE: a new
# colour spelling makes the lint MISS a site (loud in review, and the probe still covers the
# cross-fade), whereas a new rect spelling would make it cry wolf on correct code forever.
COLOR_RE = re.compile(r"^(?:Color\.\w+|new\s+Color\b.*|\w*[Cc]olor|\w*[Tt]int|composite)$")


def split_args(call):
    """Top-level comma split of an argument list (parens/brackets aware)."""
    args, depth, start = [], 0, 0
    for i, ch in enumerate(call):
        if ch in "([":
            depth += 1
        elif ch in ")]":
            depth -= 1
        elif ch == "," and depth == 0:
            args.append(call[start:i].strip())
            start = i + 1
    args.append(call[start:].strip())
    return args


def call_args(text, open_paren):
    """The argument text of a call whose '(' sits at open_paren, spanning newlines."""
    depth = 0
    for i in range(open_paren, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[open_paren + 1:i]
    return ""


def _line_offsets(text):
    offs, acc = [], 0
    for l in text.splitlines(keepends=True):
        offs.append(acc)
        acc += len(l)
    return offs


def method_body(text, idx):
    """Source of the member declaration enclosing offset idx."""
    lines = text.splitlines(keepends=True)
    offs = _line_offsets(text)
    line_of = max(i for i, o in enumerate(offs) if o <= idx)
    start = 0
    for i in range(line_of, -1, -1):
        if MEMBER_RE.match(lines[i]):
            start = offs[i]
            break
    end = len(text)
    for i in range(line_of + 1, len(lines)):
        if MEMBER_RE.match(lines[i]):
            end = offs[i]
            break
    return text[start:end]


def has_source_rect(args):
    if len(args) < 3:
        return False
    return COLOR_RE.match(args[2]) is None


def uses_custom_effect(body):
    """True if some Begin in this member passes a non-null effect (the ContentScale contract)."""
    for b in BEGIN_RE.finditer(body):
        bargs = split_args(call_args(body, b.end() - 1))
        # Begin(sortMode, blend, sampler, depth, raster, effect, matrix)
        if len(bargs) >= 6 and bargs[5] != "null":
            return True
    return False


def audit(text):
    """[(line, args)] for every unclamped, non-exempt Draw."""
    suspects, scanned, exempt = [], 0, 0
    for m in DRAW_RE.finditer(text):
        scanned += 1
        args = split_args(call_args(text, m.end() - 1))
        if has_source_rect(args):
            continue
        if uses_custom_effect(method_body(text, m.start())):
            exempt += 1
            continue
        suspects.append((text[:m.start()].count("\n") + 1, args))
    return suspects, scanned, exempt


SELFTEST_SRC = """\
public class Fake
{
\tpublic void Clamped(Texture2D texture, Vector2 position, Color color)
\t{
\t\tspriteBatch.Begin(SpriteSortMode.Deferred, blend, null, null, null, null, Matrix.Identity);
\t\tspriteBatch.Draw(texture, position, (Rectangle?)texture.LogicalBounds(), color, 0f, o, 1f, e, 0f);
\t\tspriteBatch.End();
\t}

\tpublic void CallerRect(Texture2D tex, Rectangle b, Color color)
\t{
\t\tspriteBatch.Draw(tex, new Vector2(wx, wy), b, color, rotation, Vector2.Zero, s, effects, d);
\t}

\tpublic void Unclamped(Texture2D whitePixel, int width, int height)
\t{
\t\tspriteBatch.Begin(SpriteSortMode.Deferred, WriteAlphaOne, null, null, null, null, Matrix.Identity);
\t\tspriteBatch.Draw(whitePixel, new Rectangle(0, 0, width, height), Color.White);
\t\tspriteBatch.End();
\t}

\tpublic void UnclampedTintedLocal(Texture2D tex, Rectangle dest, Color premultTint)
\t{
\t\tspriteBatch.Draw(tex, dest, premultTint);
\t}

\tpublic void EffectExempt(Texture2D texture, Rectangle designRect, Effect effect)
\t{
\t\teffect.Parameters["ContentScale"]?.SetValue(ratio);
\t\tspriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, effect, m);
\t\tspriteBatch.Draw(texture, designRect, Color.White);
\t\tspriteBatch.End();
\t}
}
"""


def selftest():
    suspects, scanned, exempt = audit(SELFTEST_SRC)
    lines = {s[0] for s in suspects}
    fails = []
    if scanned != 5:
        fails.append(f"expected 5 Draw sites, saw {scanned}")
    if exempt != 1:
        fails.append(f"expected 1 custom-effect exemption, saw {exempt}")
    # the two unclamped ones, and ONLY those: the stretched white pixel (the real defect's
    # shape) and a colour arg that is a plain local rather than `Color.Something`
    want = {SELFTEST_SRC[:SELFTEST_SRC.index(sig)].count("\n") + 1
            for sig in ("spriteBatch.Draw(whitePixel", "spriteBatch.Draw(tex, dest, premultTint")}
    if lines != want:
        fails.append(f"expected suspects on lines {sorted(want)}, got {sorted(lines)}")
    for f in fails:
        print("SELFTEST FAIL: " + f)
    print("selftest: " + ("FAIL" if fails else "ok -- 2 unclamped, 1 effect-exempt, 2 clamped"))
    return 1 if fails else 0


def main():
    if "--selftest" in sys.argv:
        return selftest()
    if not TARGET.exists():
        print(f"not found: {TARGET}")
        return 2
    text = TARGET.read_text(encoding="utf-8", errors="replace")
    suspects, scanned, exempt = audit(text)
    rel = TARGET.relative_to(ROOT)
    for line, args in suspects:
        print(f"SUSPECT {rel}:{line} spriteBatch.Draw({', '.join(args)})"
              f" -- no source rect; clamp it to LogicalBounds() or a caller rect")
    print(f"\n{scanned} spriteBatch.Draw sites scanned in {rel.name}, {exempt} exempt "
          f"(custom-effect ContentScale contract), {len(suspects)} unclamped.")
    return 1 if suspects else 0


if __name__ == "__main__":
    sys.exit(main())
