"""HYBRID mothership: Hunyuan image-to-3D hull + procedural glass dome/crew.

Run headless:  blender -b -P tools/models/make_mothership_hybrid.py

Image-to-3D (Hunyuan) nails the saucer hull/spikes/studs but fuses the squid-aliens
INTO the translucent dome shell (it can only model the surface it sees). This script
takes the generated mesh at tools/models/source/mothership_hunyuan.fbx, cuts away the
confused dome region (faces above CUT_Z within CUT_R of the axis), caps the hole with
a dark floor disc, and grafts the procedural translucent dome + glowing jellyfish crew
from make_mothership.py (same material recipes, scaled to the FBX's coordinate frame).
Exports to source/mothership.glb — the models.config `mothership` slot.

Cut/graft numbers come from the radial max-z profile of the mesh (dome bump: r<0.23,
z 0.09->0.26; hull surface just outside it: z~0.09). Tune CUT_* if a re-generated
model frames differently, then verify with a pipeline render.
"""

import math
import random
import sys
from pathlib import Path

import bpy
import bmesh

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from make_mothership import simple_mat, add_jelly  # noqa: E402  (shared recipes)

SEED = 11
FBX = HERE / "source" / "mothership_hunyuan.fbx"
OUT = HERE / "source" / "mothership.glb"

CUT_R = 0.26    # xy-radius of the dome region to remove
CUT_Z = 0.075   # only faces above this height are removed (spares the underside)
DOME_R = 0.27   # graft dome sphere radius (meets the hull just outside the hole)
DOME_Z = 0.045  # graft dome sphere centre height
DOME_SQUASH = 0.78


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(coll):
            try:
                coll.remove(block)
            except Exception:
                pass


def cut_dome(obj):
    """Delete faces whose centre lies in the dome cylinder (r<CUT_R, z>CUT_Z)."""
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [f for f in bm.faces
              if (lambda c: math.hypot(c.x, c.y) < CUT_R and c.z > CUT_Z)
              (f.calc_center_median())]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    print(f"[hybrid] cut {len(doomed)} dome faces")


def graft_dome_and_crew():
    rng = random.Random(SEED)

    # dark floor capping the hole (barely visible behind the glow)
    floor = simple_mat("dome_floor", (0.010, 0.022, 0.022), rough=0.6)
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=CUT_R + 0.015, depth=0.012,
                                        location=(0, 0, CUT_Z))
    cap = bpy.context.active_object
    cap.name = "dome_floor"
    cap.data.materials.append(floor)

    # crew — same recipes as make_mothership, scaled to the FBX frame
    # keep small-jelly emission ~1.4: higher clips all channels and they turn white
    jelly = simple_mat("jelly", (0.30, 0.70, 0.95), rough=0.4,
                       emit=(0.40, 0.80, 1.0), emit_strength=1.4)
    core_jelly = simple_mat("jelly_core", (0.30, 0.80, 1.0), rough=0.4,
                            emit=(0.55, 0.95, 1.0), emit_strength=3.5)
    add_jelly(0, 0, 0.145, 0.075, 9, 0.10, core_jelly, rng)
    for i in range(6):
        ang = 2 * math.pi * i / 6 + 0.3
        add_jelly(0.135 * math.cos(ang), 0.135 * math.sin(ang), 0.108,
                  0.042, 4, 0.05, jelly, rng)

    # translucent dome
    dome_mat = simple_mat("dome", (0.01, 0.35, 0.55), rough=0.15,
                          emit=(0.05, 0.50, 0.70), emit_strength=1.5, alpha=0.60)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=DOME_R,
                                         location=(0, 0, DOME_Z))
    dome = bpy.context.active_object
    dome.name = "dome"
    dome.scale = (1.0, 1.0, DOME_SQUASH)
    bpy.ops.object.shade_smooth()
    dome.data.materials.append(dome_mat)
    # trim the sphere's lower half — a full sphere pokes out below the saucer
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bm = bmesh.new()
    bm.from_mesh(dome.data)
    below = [f for f in bm.faces if f.calc_center_median().z < CUT_Z - 0.005]
    bmesh.ops.delete(bm, geom=below, context="FACES")
    bm.to_mesh(dome.data)
    bm.free()

    # lip ring hiding the cut seam
    lip = simple_mat("lip", (0.05, 0.11, 0.11), rough=0.5)
    bpy.ops.mesh.primitive_torus_add(major_radius=CUT_R - 0.005, minor_radius=0.014,
                                     major_segments=64, minor_segments=12,
                                     location=(0, 0, CUT_Z + 0.022))
    t = bpy.context.active_object
    t.name = "dome_lip"
    bpy.ops.object.shade_smooth()
    t.data.materials.append(lip)


def main():
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=str(FBX))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"expected 1 mesh in {FBX}, got {len(meshes)}")
    cut_dome(meshes[0])
    graft_dome_and_crew()

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(OUT), export_format="GLB")
    print(f"[hybrid] wrote {OUT}")


if __name__ == "__main__":
    main()
