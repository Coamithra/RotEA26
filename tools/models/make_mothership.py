"""Procedurally builds a 3D mothership that mirrors GFX/Sprites/mothershipB and
exports it to tools/models/source/mothership.glb (the models.config `mothership` slot).

Run headless:  blender -b -P tools/models/make_mothership.py

The original 2008 sheet is itself a 16-frame turntable render of a lost 3D model:
a flattened dark-teal saucer with angular crack/panel lines, ~8 grey rim spikes,
dark hex studs (a few with blue glints), and a translucent cyan dome holding
glowing jellyfish aliens (one large central + a ring of small ones) lit from within.

The crack pattern is procedural (Voronoi distance-to-edge) which glTF cannot carry,
so it is BAKED to a 1024px image (Cycles EMIT bake) and the exported hull material
uses the baked texture. Deterministic (fixed seed). Re-run after tweaking knobs;
don't hand-edit the .glb.
"""

import math
import random
from pathlib import Path

import bpy
from mathutils import Vector

SEED = 7
HULL_R = 2.0          # saucer radius
HULL_H = 0.55         # saucer half-height (z semi-axis)
SPIKES = 8
SPIKE_LEN = 1.5
SPIKE_R = 0.14
SPIKE_TILT_DEG = -8   # droop below horizontal
DOME_R = 1.05
DOME_SQUASH = 0.80
DOME_Z = 0.30         # dome sphere centre height
BAKE_PX = 1024

OUT = Path(__file__).resolve().parent / "source" / "mothership.glb"
BAKE_PNG = Path(__file__).resolve().parent / "source" / "mothership_hull_bake.png"


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                 bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            try:
                coll.remove(block)
            except Exception:
                pass


def simple_mat(name, color, rough=0.5, metal=0.0, emit=None, emit_strength=1.0, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        # EEVEE Next (4.2+) vs legacy attribute names — set whichever exists.
        if hasattr(m, "surface_render_method"):
            m.surface_render_method = "BLENDED"
        if hasattr(m, "blend_method"):
            m.blend_method = "BLEND"
    return m


def track_rotation(direction):
    """Quaternion rotating +Z onto `direction`."""
    return Vector(direction).to_track_quat("Z", "Y")


def hull_surface_z(r):
    """Top surface height of the hull ellipsoid at radius r."""
    t = max(0.0, 1.0 - (r / HULL_R) ** 2)
    return HULL_H * math.sqrt(t)


def hull_normal(x, y, z):
    return Vector((x / HULL_R ** 2, y / HULL_R ** 2, z / HULL_H ** 2)).normalized()


# ---------------------------------------------------------------- hull + bake

def build_hull():
    bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48, radius=1.0)
    hull = bpy.context.active_object
    hull.name = "hull"
    hull.scale = (HULL_R, HULL_R, HULL_H)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.shade_smooth()

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    return hull


def bake_hull_texture(hull):
    """Voronoi crack pattern -> emission -> Cycles EMIT bake -> image."""
    m = bpy.data.materials.new("hull_bake")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = nt.nodes["Material Output"]

    coord = nt.nodes.new("ShaderNodeTexCoord")
    voro = nt.nodes.new("ShaderNodeTexVoronoi")
    voro.feature = "DISTANCE_TO_EDGE"
    voro.inputs["Scale"].default_value = 2.4
    nt.links.new(coord.outputs["Object"], voro.inputs["Vector"])

    # crack mask: thin dark line where distance-to-edge is small
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.02
    ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
    ramp.color_ramp.elements[1].position = 0.06
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    nt.links.new(voro.outputs["Distance"], ramp.inputs["Fac"])

    # subtle large-scale mottle so the teal isn't flat
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.4
    noise.inputs["Detail"].default_value = 3.0
    nt.links.new(coord.outputs["Object"], noise.inputs["Vector"])
    mottle = nt.nodes.new("ShaderNodeMix")
    mottle.data_type = "RGBA"
    mottle.inputs["Factor"].default_value = 0.25
    mottle.inputs["A"].default_value = (0.016, 0.050, 0.052, 1)   # base teal
    mottle.inputs["B"].default_value = (0.030, 0.078, 0.080, 1)   # lighter teal
    nt.links.new(noise.outputs["Fac"], mottle.inputs["Factor"])

    mixed = nt.nodes.new("ShaderNodeMix")
    mixed.data_type = "RGBA"
    mixed.inputs["A"].default_value = (0.004, 0.010, 0.010, 1)    # crack line
    nt.links.new(mottle.outputs["Result"], mixed.inputs["B"])
    nt.links.new(ramp.outputs["Color"], mixed.inputs["Factor"])

    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(mixed.outputs["Result"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])

    img = bpy.data.images.new("hull_bake", BAKE_PX, BAKE_PX, alpha=False)
    img_node = nt.nodes.new("ShaderNodeTexImage")
    img_node.image = img
    nt.nodes.active = img_node

    hull.data.materials.clear()
    hull.data.materials.append(m)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 8
    scene.cycles.device = "CPU"
    bpy.ops.object.select_all(action="DESELECT")
    hull.select_set(True)
    bpy.context.view_layer.objects.active = hull
    bpy.ops.object.bake(type="EMIT")

    BAKE_PNG.parent.mkdir(parents=True, exist_ok=True)
    img.filepath_raw = str(BAKE_PNG)
    img.file_format = "PNG"
    img.save()
    return img


def final_hull_material(hull, img):
    m = bpy.data.materials.new("hull")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.45
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    hull.data.materials.clear()
    hull.data.materials.append(m)


# ---------------------------------------------------------------- decorations

def add_spikes(mat):
    for i in range(SPIKES):
        ang = 2 * math.pi * i / SPIKES + math.pi / SPIKES
        tilt = math.radians(SPIKE_TILT_DEG)
        d = Vector((math.cos(ang) * math.cos(tilt),
                    math.sin(ang) * math.cos(tilt),
                    math.sin(tilt)))
        base = Vector((math.cos(ang), math.sin(ang), 0)) * (HULL_R - 0.35)
        centre = base + d * (SPIKE_LEN / 2)
        bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=SPIKE_R, radius2=0.0,
                                        depth=SPIKE_LEN, location=centre)
        o = bpy.context.active_object
        o.name = f"spike_{i}"
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = track_rotation(d)
        bpy.ops.object.shade_smooth()
        o.data.materials.append(mat)


def add_studs(stud_mat, glint_mat):
    rng = random.Random(SEED)
    spots = []
    for ring_r, count in ((1.20, 8), (1.62, 6)):
        for i in range(count):
            ang = 2 * math.pi * i / count + rng.uniform(-0.18, 0.18)
            r = ring_r + rng.uniform(-0.06, 0.06)
            spots.append((r * math.cos(ang), r * math.sin(ang)))

    glinted = set(random.Random(SEED + 1).sample(range(len(spots)), 5))
    for idx, (x, y) in enumerate(spots):
        z = hull_surface_z(math.hypot(x, y))
        n = hull_normal(x, y, z)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.13, depth=0.10,
                                            location=(x, y, z))
        o = bpy.context.active_object
        o.name = f"stud_{idx}"
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = track_rotation(n)
        o.data.materials.append(stud_mat)
        if idx in glinted:
            gpos = Vector((x, y, z)) + n * 0.06 + Vector((0.16, 0, 0))
            bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8,
                                                 radius=0.05, location=gpos)
            g = bpy.context.active_object
            g.name = f"glint_{idx}"
            bpy.ops.object.shade_smooth()
            g.data.materials.append(glint_mat)


def add_jelly(x, y, z, head_r, tendrils, tendril_len, mat, rng):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=head_r,
                                         location=(x, y, z))
    head = bpy.context.active_object
    head.scale = (1.0, 1.0, 0.85)
    bpy.ops.object.shade_smooth()
    head.data.materials.append(mat)
    for _ in range(tendrils):
        ang = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.2, 0.75) * head_r
        tx, ty = x + rr * math.cos(ang), y + rr * math.sin(ang)
        tl = tendril_len * rng.uniform(0.7, 1.1)
        bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=head_r * 0.16, radius2=0.0,
                                        depth=tl, location=(tx, ty, z - head_r * 0.4 - tl / 2))
        t = bpy.context.active_object
        t.rotation_euler = (math.pi + rng.uniform(-0.15, 0.15),
                            rng.uniform(-0.15, 0.15), 0)
        bpy.ops.object.shade_smooth()
        t.data.materials.append(mat)


def add_dome_and_crew():
    rng = random.Random(SEED + 2)
    jelly = simple_mat("jelly", (0.30, 0.70, 0.95), rough=0.4,
                       emit=(0.45, 0.85, 1.0), emit_strength=2.2)

    # central big jellyfish (hot — it IS the light source visually) + ring of small ones
    core_jelly = simple_mat("jelly_core", (0.30, 0.80, 1.0), rough=0.4,
                            emit=(0.55, 0.95, 1.0), emit_strength=3.5)
    add_jelly(0, 0, 0.70, 0.36, 9, 0.50, core_jelly, rng)
    for i in range(6):
        ang = 2 * math.pi * i / 6 + 0.3
        add_jelly(0.58 * math.cos(ang), 0.58 * math.sin(ang), 0.55,
                  0.20, 4, 0.26, jelly, rng)

    # translucent dome — strongly cyan, self-lit (the sheet's dome glow is baked-in)
    dome_mat = simple_mat("dome", (0.02, 0.40, 0.60), rough=0.15,
                          emit=(0.06, 0.50, 0.70), emit_strength=1.2, alpha=0.60)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=DOME_R,
                                         location=(0, 0, DOME_Z))
    dome = bpy.context.active_object
    dome.name = "dome"
    dome.scale = (1.0, 1.0, DOME_SQUASH)
    bpy.ops.object.shade_smooth()
    dome.data.materials.append(dome_mat)

    # lip ring where dome meets hull
    lip = simple_mat("lip", (0.05, 0.11, 0.11), rough=0.5)
    z_lip = DOME_Z + 0.13
    r_lip = DOME_R * math.sqrt(max(0.0, 1 - ((z_lip - DOME_Z) / (DOME_R * DOME_SQUASH)) ** 2))
    bpy.ops.mesh.primitive_torus_add(major_radius=r_lip, minor_radius=0.05,
                                     major_segments=48, minor_segments=12,
                                     location=(0, 0, z_lip))
    t = bpy.context.active_object
    t.name = "dome_lip"
    bpy.ops.object.shade_smooth()
    t.data.materials.append(lip)


def main():
    random.seed(SEED)
    clear_scene()

    hull = build_hull()
    img = bake_hull_texture(hull)
    final_hull_material(hull, img)

    spike = simple_mat("spike", (0.42, 0.44, 0.46), rough=0.55, metal=0.1)
    stud = simple_mat("stud", (0.055, 0.035, 0.095), rough=0.5)
    # keep glint emission ~1.5: higher clips ALL channels past 1.0 and the dot turns white
    glint = simple_mat("glint", (0.02, 0.05, 0.30), emit=(0.15, 0.30, 1.0), emit_strength=1.5)

    add_spikes(spike)
    add_studs(stud, glint)
    add_dome_and_crew()

    OUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(OUT), export_format="GLB")
    print(f"[make_mothership] wrote {OUT}")


if __name__ == "__main__":
    main()
