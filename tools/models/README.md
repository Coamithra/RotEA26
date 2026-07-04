# tools/models — 3D model → sprite-sheet pipeline

Trello card 6a376549: *"redo the big UFO / mothership / spider boss as 3D models for
effectively infinite resolution."*

The big bosses are drawn from **2D sprite sheets** that ship at their original render
resolution (e.g. 640×480 per frame) — that's the resolution ceiling. This pipeline lets
you re-render a boss from a **3D model** at any supersample factor and drop the crisper
sheet straight into the existing draw path. The engine stays 2D; "infinite resolution"
comes from re-rendering offline whenever you want it sharper.

**Scope (per the card decision): a static hero pose or an N-angle turntable.** An online
image-to-3D tool gives ONE static mesh with no rig, so it can't reproduce the multi-pose
gameplay animations (spider fly/jump/land). Those stay on their hand-made sheets until an
animated, rigged model exists. A turntable *can* fill a rotation animation (e.g. the
mothership's 16-frame hover → 16 rotation frames).

## What you (the human) do

1. **Make a 3D model** of the boss from its sprites, using whatever you like:
   - **Online image-to-3D** (fastest): [Meshy](https://www.meshy.ai/),
     [Tripo](https://www.tripo3d.ai/), [Rodin](https://hyper3d.ai/),
     [Luma Genie](https://lumalabs.ai/genie). Feed a clean, front-on frame of the boss;
     export **`.glb`** (preferred) or `.gltf`/`.obj`/`.fbx`/`.stl`.
   - **Hand-model** in Blender for full control.
2. **Drop it** at `tools/models/source/<name>.glb` (this folder is gitignored — raw models
   stay local; only the built sheets are committed). `<name>` = the object key in
   `models.config` (`mothership`, `bigufo`, `spiderboss`).
3. **Install Blender** (the renderer). It's a heavy, dev-box-only dependency — CI never
   renders, it just ships the committed PNGs, same as `tools/textures`' `texconv`.
   `bpy` has no Python-3.12 wheel, so the tool shells out to a `blender` executable found
   via `$BLENDER`, `models.config`'s `"blender_exe"`, `PATH`, or the usual install dir.
4. **Run it:**
   ```sh
   python tools/models/build_models.py            # every object that has a source model
   python tools/models/build_models.py --only mothership
   python tools/models/build_models.py --dry-run  # plan only
   ```
   It renders the frames, trims + packs them, and writes the sheet next to `output`
   (`<name>_model.png`, plus `.dat` for atlas objects, plus a `.model.json` sidecar with
   the numbers you need to wire it).
5. **Tune the look** by editing that object's block in `models.config` (camera
   elevation/azimuth/fov, key-light energy, world ambient, supersample, turntable frames)
   and re-running. The **first render is your visual check** — open the PNG and iterate.
6. **Wire it into the game** (see the sidecar's `note`):
   - **Grid objects** (mothership, big UFO — `AlienDrawableGameComponent`): point the
     `LoadAnimation`/`AnimationData` at the new sheet and add a `DesignFrameWidth` entry
     (`AlienDrawableGameComponent.cs`) equal to the object's **current** per-cell design px,
     so `textureScale` divides the bigger cells back to the same on-screen size.
   - **Atlas objects** (spider boss — `AnimatedSprite`): construct it with the supersample
     factor, e.g. `new AnimatedSprite("GFX/Spider/spiderboss_model", supersample: 3)`.
     `AnimatedSprite`'s `supersample` arg defaults to 1, so nothing else changes.

## How it works

- `build_models.py` — reads `models.config`, drives the render, trims + packs, emits the
  sheet. `--selftest` exercises pack + `.dat` round-trip with synthetic frames (no Blender).
- `blender_render.py` — runs *inside* Blender (`blender -b -P`). Imports the model,
  centers + scales it, sets a transparent-film EEVEE render, a sun key light + world
  ambient, and a TRACK_TO camera, then renders a hero still or a turntable spin.
- `datfmt.py` — reads/writes the `AnimatedSprite` `.dat` byte-for-byte compatibly with the
  C# loader (`Game/EvilAliens/AnimatedSprite.cs`).

## Config knobs (per object, in `models.config`)

| key | meaning |
|---|---|
| `source` | model path under `tools/models/` (gitignored `source/…`). Missing ⇒ object skipped. |
| `output` | sheet base path (no extension), under `wwwroot/Content`. |
| `layout` | `"grid"` (uniform cols×rows for `AlienDrawableGameComponent`) or `"atlas"` (packed + `.dat` for `AnimatedSprite`). |
| `mode` | `"hero"` (one still) or `"turntable"` (rotation frames). |
| `columns`/`rows` | grid dimensions (grid layout, turntable fills cols×rows frames). |
| `turntable_frames` | frame count (atlas turntable). |
| `design_width`/`design_height` | the object's CURRENT on-screen frame size in design px. |
| `supersample` | crispness multiplier; render px = design × supersample. |
| `texture_key` | the `GFX/...` content key (grid layout — used in the sidecar's DesignFrameWidth hint). |
| `camera` | `elevation_deg`, `azimuth_deg`, `fov_deg`, `margin`, `ortho`. |
| `light` | `key_energy` (sun), `environment` (world ambient). |
| `samples` | EEVEE render samples. |

## Resolution ceiling

`supersample` × the design size × the frame count all land in one atlas, whose coordinates
are stored as `int16` in the `.dat` (max 32767) — and the browser's WebGL/ANGLE→D3D11
backend caps texture size (~8–16k) anyway. If you crank `supersample` too high the tool
stops with a clear error naming the knob; lower `supersample` or the turntable frame count.
For a turntable, prefer more supersample over more frames — the interpolation shader can
tween a sparse animation, but every extra frame multiplies the atlas area.

Offline + deterministic (given a fixed model), like the other `tools/` asset steps. Don't
hand-edit the built `.png`/`.dat` — re-run the tool.
