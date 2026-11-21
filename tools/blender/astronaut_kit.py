"""Shared materials and mesh helpers for the astronaut scripts."""
import bpy, bmesh, math
from mathutils import Vector, Matrix

def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexrgb, rough=0.6, metal=0.0, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*srgb(hexrgb), 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*srgb(emit), 1)
        b.inputs["Emission Strength"].default_value = strength
    return m

# Slot names = MarsAstronautBody colour slots (default look colours for the preview).
SUIT = mat("Suit", "E4DED3", 0.8)                  # cover layer, printed-basalt white
JOINT = mat("Joint", "6F737C", 0.7)                # grey joint patches / convolutes
ACCENT = mat("Accent", "D9822B", 0.55)             # wardrobe accent (ID bands, trim)
TRIM = mat("Trim", "3A3D44", 0.35, 0.8)            # gunmetal hardware
GLOVE = mat("Glove", "8A8E96", 0.75)
BOOT = mat("Boot", "9A9DA4", 0.8)
VISOR = mat("Visor", "B8862E", 0.08, 0.9)          # gold sun visor
PACK = mat("Pack", "D8CFC2", 0.85)                 # printed PLSS shell
SCREEN = mat("Screen", "0E141C", 0.15, 0.2, emit="3FE0FF", strength=2.0)
LAMP = mat("Lamp", "000000", 0.4, emit="FFF1D6", strength=14)
CYAN = mat("Light Cyan", "000000", 0.4, emit="3FE0FF", strength=14)
AMBER = mat("Light Amber", "000000", 0.4, emit="FFB547", strength=12)


def obj(name, bm, m, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(m)
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def lathe(profile, centre=(0, 0, 0), steps=32, sx=1.0, sy=1.0, cap_top=True, cap_bottom=True):
    """Solid of revolution about the vertical through centre; profile [(r, z)] bottom->top.
    sx/sy squash it into an ellipse (wider than deep)."""
    bm = bmesh.new()
    cx, cy, cz = centre
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new((cx + r * sx * math.cos(2 * math.pi * i / steps),
                                    cy + r * sy * math.sin(2 * math.pi * i / steps), cz + z)) for i in range(steps)])
    for j in range(len(rings) - 1):
        for i in range(steps):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % steps], rings[j + 1][(i + 1) % steps], rings[j + 1][i]))
    if cap_bottom: bm.faces.new(list(reversed(rings[0])))
    if cap_top: bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm

def rbox(size, centre, bevel=0.02, segs=3, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.45), segments=segs, affect="EDGES", profile=0.5)
    if rot:
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=bm.verts)
    return bm

def torus(R, r, centre, sx=1.0, sy=1.0, seg=32, minor=8):
    bm = bmesh.new()
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for k in range(minor):
            b = 2 * math.pi * k / minor
            rr = R + r * math.cos(b)
            ring.append(bm.verts.new((centre[0] + rr * sx * math.cos(a), centre[1] + rr * sy * math.sin(a), centre[2] + r * math.sin(b))))
        rings.append(ring)
    for i in range(seg):
        for k in range(minor):
            bm.faces.new((rings[i][k], rings[(i + 1) % seg][k], rings[(i + 1) % seg][(k + 1) % minor], rings[i][(k + 1) % minor]))
    return bm

def sphere(r, centre, sx=1, sy=1, sz=1, seg=32, rings=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=bm.verts)
    return bm

