"""Runs INSIDE Blender headless: `blender -b -P blender_render.py -- <job.json>`.

Imports a model, normalizes + frames it, and renders `frames` PNGs (transparent film)
to `out_dir/frame_###.png` -- a single hero pose, or an N-step turntable spin. The parent
`build_models.py` trims + packs those into the AnimatedSprite atlas + `.dat`.

Kept deliberately vanilla (EEVEE, sun key + world ambient, TRACK_TO framing) so it works
across Blender 3.x/4.x. Tune look via the job's `camera` / `light` / `samples` fields.
"""

import json
import math
import sys

import bpy
from mathutils import Vector


def load_job():
    argv = sys.argv[sys.argv.index("--") + 1:]
    with open(argv[0], encoding="utf-8") as fh:
        return json.load(fh)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            coll.remove(block)


def import_model(path):
    p = path.lower()
    if p.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif p.endswith(".obj"):
        # Blender 4.x: import_scene.obj was replaced by wm.obj_import
        if hasattr(bpy.ops.wm, "obj_import"):
            bpy.ops.wm.obj_import(filepath=path)
        else:
            bpy.ops.import_scene.obj(filepath=path)
    elif p.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    elif p.endswith(".stl"):
        (bpy.ops.wm.stl_import if hasattr(bpy.ops.wm, "stl_import")
         else bpy.ops.import_mesh.stl)(filepath=path)
    else:
        raise RuntimeError(f"unsupported model format: {path}")
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def world_bbox(meshes):
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for o in meshes:
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector(min(a, b) for a, b in zip(lo, w))
            hi = Vector(max(a, b) for a, b in zip(hi, w))
    return lo, hi


def normalize(meshes):
    """Center the model at the origin and scale so its max extent == 2. Parent it all to
    an empty 'pivot' at the origin so a turntable can spin the whole thing."""
    lo, hi = world_bbox(meshes)
    center = (lo + hi) / 2.0
    extent = max((hi - lo).x, (hi - lo).y, (hi - lo).z) or 1.0
    scale = 2.0 / extent

    pivot = bpy.data.objects.new("pivot", None)
    bpy.context.scene.collection.objects.link(pivot)
    pivot.location = (0, 0, 0)
    for o in meshes:
        if o.parent is None:
            o.parent = pivot
    pivot.scale = (scale, scale, scale)
    pivot.location = -center * scale
    return pivot  # radius after normalize ~= 1.0


def setup_render(scene, job):
    scene.render.resolution_x = job["width"]
    scene.render.resolution_y = job["height"]
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    # EEVEE across versions: Next (4.2+), classic, then Workbench as a last resort.
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    ee = getattr(scene, "eevee", None)
    if ee is not None:
        try:
            ee.taa_render_samples = int(job.get("samples", 64))
        except Exception:
            pass


def setup_world(scene, strength):
    world = bpy.data.worlds.new("world") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (1, 1, 1, 1)
        bg.inputs[1].default_value = strength


def add_key_light(energy):
    light = bpy.data.lights.new("key", type="SUN")
    light.energy = energy
    obj = bpy.data.objects.new("key", light)
    bpy.context.scene.collection.objects.link(obj)
    obj.rotation_euler = (math.radians(55), 0, math.radians(35))
    return obj


def add_camera(scene, cam_cfg, radius):
    cam = bpy.data.cameras.new("cam")
    ortho = bool(cam_cfg.get("ortho", False))
    fov = math.radians(float(cam_cfg.get("fov_deg", 35)))
    margin = float(cam_cfg.get("margin", 1.25))
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = 2.0 * radius * margin
        dist = radius * 4.0
    else:
        cam.type = "PERSP"
        cam.angle = fov
        dist = (radius * margin) / math.tan(fov / 2.0)

    elev = math.radians(float(cam_cfg.get("elevation_deg", 20)))
    azim = math.radians(float(cam_cfg.get("azimuth_deg", 0)))
    cam_obj = bpy.data.objects.new("cam", cam)
    scene.collection.objects.link(cam_obj)
    cam_obj.location = (
        dist * math.cos(elev) * math.sin(azim),
        -dist * math.cos(elev) * math.cos(azim),
        dist * math.sin(elev),
    )
    target = bpy.data.objects.new("target", None)
    scene.collection.objects.link(target)
    target.location = (0, 0, 0)
    track = cam_obj.constraints.new("TRACK_TO")
    track.target = target
    track.track_axis = "TRACK_NEGATIVE_Z"
    track.up_axis = "UP_Y"
    scene.camera = cam_obj
    return cam_obj


def main():
    job = load_job()
    clear_scene()
    meshes = import_model(job["model"])
    if not meshes:
        raise RuntimeError("no mesh objects imported from " + job["model"])
    pivot = normalize(meshes)

    scene = bpy.context.scene
    setup_render(scene, job)
    setup_world(scene, float(job.get("light", {}).get("environment", 0.4)))
    add_key_light(float(job.get("light", {}).get("key_energy", 4.0)))
    add_camera(scene, job.get("camera", {}), radius=1.0)

    frames = int(job["frames"])
    axis = {"X": 0, "Y": 1, "Z": 2}[job.get("turntable_axis", "Z").upper()]
    turntable = job.get("mode") == "turntable" and frames > 1

    for i in range(frames):
        if turntable:
            rot = list(pivot.rotation_euler)
            rot[axis] = 2.0 * math.pi * i / frames
            pivot.rotation_euler = rot
        scene.render.filepath = f"{job['out_dir']}/frame_{i:03d}.png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
