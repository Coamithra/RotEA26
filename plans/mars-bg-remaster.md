# Plan: Remaster Level 2 (Mars) background

Card `51de2925`. Branch `feat/mars-bg-remaster`.

## Context
Level 2 (Mars) is the only level whose **background** is still the original soft
~600px-tall 2008 art. In-game (`?level=Level2`) the rocky terrain is blurry with a
visible seam and the sky is a low-res dust haze, while every foreground sprite
(UFOs, spider, landed ships) was already AI-upscaled in earlier cards. The
background is what literally defines "Level 2: Mars", so it's the high-value win.

User decisions: **re-source from real high-res public-domain NASA/JPL Mars imagery**
(not AI-upscale); **background only** (MarsBoss `mothershipA/B` + `mediumship` are
large enough and out of scope).

## The layers (from `Background.SetMars()` + per-layer alpha analysis)
Four alpha-stacked parallax layers, each 600px tall, drawn at `size=1` in 800x600
design space, tiled horizontally; the design frame then scales to `RenderScale`
(<=1440 tall) => the 600px source softens ~2.4x on a big window.

| Layer | Orig px | Scroll | Content (vertical) |
|---|---|---|---|
| `clouds-background` | 1024x600 | 0.3x | 100% opaque dusty-tan **sky**, base layer |
| `marshills` | 1000x600 | 0.7x | faint **distant-hills** band ~70-80% down (alpha<=197), else transparent |
| `mars1`-`mars6` | 1000x600 (mars6 1220) | 1.0x | rocky **foreground ground**, opaque bottom ~35%, transparent above; 6-tile panorama with `mirrorX` seamless wrap |
| `clouds-foreground2` | 1024x600 | 2.5x | very faint full-frame **dust veil** (alpha<=52), drawn on top |

Horizon math (fractions of the 600 height): sky 0..~0.70, hills band ~0.70..0.82,
ground ~0.65..1.0 (overlaps hills). The re-source MUST land content at the same
fractional Y so framing/parallax read identical.

## Design
### Sourcing (`tools/mars/sources/`)
A ground-level Mars panorama (rocky foreground + low distant hills + butterscotch
sky), public-domain NASA/JPL, downloaded with user approval (URL+size stated).
Decompose ONE coherent panorama into the bands so lighting/palette stay consistent.
Supplement procedurally only where needed (the faint dust veil).

### `tools/mars/build_mars.py` (offline, like tools/earth / tools/textures)
Reads the source(s) + knobs, writes the 4 layers to
`web/EvilAliensWeb/wwwroot/Content/Content/gfx/marsbg/...` (lowercase under capital
`Content/`; CLAUDE.md case rule). Per layer:
- **clouds-background**: crop/aspect the sky region; make horizontally tileable
  (mirror-blend the seam); opaque; supersampled.
- **marshills**: extract the distant-hills silhouette into the same Y band;
  feathered straight alpha (transparent sky); tileable.
- **mars1-6**: take the near rocky ground as one wide panorama, alpha-key the sky
  (transparent above horizon, feathered), slice into 6 tiles (5x equal + a wider
  6th matching the original 1000x5 + 1220 ratio) that tile L->R; `mirrorX` handles
  the wrap so only adjacent-tile continuity matters.
- **clouds-foreground2**: keep/regenerate the faint procedural dust veil (alpha<=~52).
- Straight (non-premultiplied) alpha out (pipeline maps AlphaBlend->NonPremultiplied).

### Resolution: supersample + size-compensation (the only code change)
Output every layer at factor **F** (default 2.0; bump toward 2.4 = the 1440 cap if
payload allows) and set each layer's `size = 1/F` in `SetMars()` (uniform scale
preserves aspect). Then `realsize = Width*size` is byte-identical to the original,
so scroll wrap, `UpperDiv` tiling, and `realsize.X *= 2` mirror math are unchanged
— the layer just has F-times the texel density and stays crisp when the design
frame scales up. `build_mars.py` PRINTS the `size` to set (mirrors build_earth's
doodadscale print).

Payload note: backgrounds load at Level-2 preload, NOT in the boot payload, so the
size delta is tolerable; if a layer bloats, add it to `textures.config` (opaque sky
-> dxt; soft gradients -> raw) like the starfield tiles.

## Verification
- `dotnet build` clean.
- Real Chrome `?level=Level2` (NOT preview_screenshot — rAF pauses when backgrounded):
  crisp terrain, no seam, sky/hills/ground at the same framing, parallax speeds
  unchanged, dust veil still subtle. Console exception-free.
- Hard cache-bust (DevTools "Disable cache") when swapping textures — content is
  cached aggressively (CLAUDE.md earth trap).
- A/B against the root `main` server (port 5280) vs worktree (5281).

## Out of scope
- MarsBoss (`mothershipA/B`) and `mediumship` sprites (already large; later optional card).
- Any sprite/enemy art, gameplay, audio. Background layers only.
