"""
Greenhouse art: the seed shop kiosk (for storage) and the three crops, plus their shop icons.

Exports to Assets/Resources/Garden: SeedKiosk.fbx, Lettuce.fbx, Tomato.fbx, Watermelon.fbx and
Icons/<id>.png. Crops stand on their origin (the soil surface), full grown; the game scales them
with growth and shows the child named "Fruit" only when ripe. Every material is named
"<kind> #RRGGBB" (kind: Mat, Glow or Metal) and the game builds its own URP material from that.

Run inside Blender: exec(open(".../garden_assets.py").read()); build_all()
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

ROOT = "/Users/andrewhuangjin/Documents/code/nasa"
OUT = ROOT + "/My project/Assets/Resources/Garden"


def wipe():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
        for b in list(blk): blk.remove(b)


def mat(kind, hexrgb, rough=0.6):
    name = "%s #%s" % (kind, hexrgb)
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    c = [int(hexrgb[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    b.inputs["Base Color"].default_value = (*lin, 1)
    b.inputs["Roughness"].default_value = rough
    if kind == "Metal": b.inputs["Metallic"].default_value = 0.8
    if kind == "Glow":
        b.inputs["Emission Color"].default_value = (*lin, 1); b.inputs["Emission Strength"].default_value = 6
    return m


def obj(name, bm, m, parent=None, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(m)
    if smooth:
        for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    if parent: o.parent = parent
    return o


def empty(name, parent=None):
    o = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(o)
    if parent: o.parent = parent
    return o


def ellipsoid(r, loc, rot=(0, 0, 0), seg=16):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(6, seg // 2), radius=1)
    bmesh.ops.scale(bm, vec=Vector(r), verts=bm.verts)
    bmesh.ops.rotate(bm, matrix=Matrix.Rotation(rot[0], 3, "X") @ Matrix.Rotation(rot[1], 3, "Y") @ Matrix.Rotation(rot[2], 3, "Z"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return bm


def rbox(size, loc, bevel=0.02):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.45), segments=2, affect="EDGES", profile=0.5)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return bm


def cyl(r, h, loc, seg=12, rot=None):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=h)
    if rot: bmesh.ops.rotate(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return bm


def leaf(length, width, loc, yaw, tilt):
    """A cupped leaf: a flattened, bent ellipsoid pointing outwards at yaw, tilted up by tilt."""
    bm = ellipsoid((length / 2, width / 2, 0.018), (length / 2, 0, 0), seg=12)
    for v in bm.verts:   # cup it upward along its length
        v.co.z += 0.35 * (v.co.x / length) ** 2 * length
    bmesh.ops.rotate(bm, matrix=Matrix.Rotation(-tilt, 3, "Y"), verts=bm.verts)
    bmesh.ops.rotate(bm, matrix=Matrix.Rotation(yaw, 3, "Z"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return bm


# ------------------------------------------------------------------ crops
def lettuce():
    root = empty("Lettuce")
    leaves = empty("Leaves", root)
    LIGHT, MID = mat("Mat", "9BD068"), mat("Mat", "5FA341")
    for ring, (n, ln, tilt, h) in enumerate(((9, 0.26, 0.35, 0.03), (7, 0.2, 0.75, 0.07), (5, 0.13, 1.1, 0.12))):
        for i in range(n):
            a = 2 * math.pi * i / n + ring * 0.4
            obj("Leaf", leaf(ln, ln * 0.75, (0, 0, h), a, tilt), MID if ring == 0 else LIGHT, leaves)
    obj("Heart", ellipsoid((0.07, 0.07, 0.07), (0, 0, 0.14)), LIGHT, leaves)
    empty("Fruit", root)   # the head itself is the crop
    return root


def tomato():
    root = empty("Tomato")
    leaves = empty("Leaves", root)
    STEM, LEAF, RED = mat("Mat", "4E7A33"), mat("Mat", "3F8A3A"), mat("Mat", "E0362A")
    obj("Stake", cyl(0.012, 0.95, (0.12, 0, 0.475)), mat("Mat", "A8835A"), leaves)
    obj("Stem", cyl(0.016, 0.82, (0, 0, 0.41)), STEM, leaves)
    for k in range(7):
        z = 0.15 + k * 0.1; a = k * 2.3
        obj("Leaf", leaf(0.2, 0.13, (0, 0, z), a, 0.25), LEAF, leaves)
        obj("Leaf", leaf(0.17, 0.11, (0, 0, z + 0.04), a + math.pi, 0.35), LEAF, leaves)
    fruit = empty("Fruit", root)
    for k, (x, y, z) in enumerate(((0.09, 0.05, 0.42), (-0.08, 0.07, 0.55), (0.07, -0.08, 0.66), (-0.06, -0.05, 0.32), (0.04, 0.09, 0.75))):
        obj("Tomato", ellipsoid((0.055, 0.055, 0.048), (x, y, z)), RED, fruit)
        obj("Calyx", ellipsoid((0.025, 0.025, 0.008), (x, y, z + 0.047), seg=8), STEM, fruit)
    return root


def watermelon():
    root = empty("Watermelon")
    leaves = empty("Leaves", root)
    LEAF, VINE = mat("Mat", "3E8C3A"), mat("Mat", "4E7A33")
    for i in range(8):
        a = 2 * math.pi * i / 8
        obj("Leaf", leaf(0.24, 0.2, (0.1 * math.cos(a), 0.1 * math.sin(a), 0.02), a, 0.15), LEAF, leaves)
    obj("Vine", cyl(0.012, 0.5, (0, 0, 0.02), rot=Matrix.Rotation(math.pi / 2, 3, "Y")), VINE, leaves)
    fruit = empty("Fruit", root)
    # striped rind: faces alternate dark and light by angle around the melon's long axis
    bm = ellipsoid((0.26, 0.17, 0.16), (0, -0.12, 0.15), seg=32)
    me = bpy.data.meshes.new("Melon"); bm.to_mesh(me); bm.free()
    me.materials.append(mat("Mat", "2E6B2A")); me.materials.append(mat("Mat", "8CC266"))
    for p in me.polygons:
        ang = math.atan2(p.center.z - 0.15, p.center.y + 0.12)
        p.material_index = 1 if int((ang + math.pi) / (2 * math.pi) * 16) % 2 else 0
        p.use_smooth = True
    o = bpy.data.objects.new("Melon", me); bpy.context.scene.collection.objects.link(o); o.parent = fruit
    obj("Stalk", cyl(0.01, 0.06, (0.27, -0.12, 0.17), rot=Matrix.Rotation(math.pi / 2, 3, "Y")), VINE, fruit)
    return root


# ------------------------------------------------------------------ kiosk
def kiosk():
    """Seed shop kiosk, 1.8 m wide, facing -Y (its customer side), origin on the floor at its centre."""
    root = empty("SeedKiosk")
    WHITE, GUN, PANEL = mat("Mat", "E4E1DA", 0.5), mat("Metal", "3A3D44", 0.35), mat("Mat", "2A2D34", 0.4)
    CYAN, AMBER, SCREEN = mat("Glow", "3FE0FF"), mat("Glow", "FFB547"), mat("Glow", "0F4C5C")
    obj("Counter", rbox((1.8, 0.7, 1.0), (0, 0, 0.5), 0.04), WHITE, root)
    obj("Counter top", rbox((1.9, 0.78, 0.06), (0, -0.02, 1.03), 0.02), GUN, root)
    obj("Kick", rbox((1.82, 0.72, 0.1), (0, 0, 0.05), 0.01), GUN, root)
    obj("Front stripe", rbox((1.7, 0.01, 0.03), (0, -0.355, 0.82), 0), CYAN, root)
    obj("Front stripe", rbox((1.7, 0.01, 0.02), (0, -0.355, 0.2), 0), AMBER, root)
    obj("Back panel", rbox((1.8, 0.14, 2.3), (0, 0.32, 1.15), 0.03), WHITE, root)
    obj("Display", rbox((1.5, 0.02, 0.9), (0, 0.245, 1.55), 0.01), PANEL, root)
    # seed packets on the display: three columns, one per crop colour
    for c, col in enumerate(("7CC456", "E0362A", "2E6B2A")):
        for r in range(3):
            x, z = -0.5 + c * 0.5, 1.25 + r * 0.27
            obj("Packet", rbox((0.16, 0.02, 0.22), (x - 0.09, 0.23, z), 0.01), mat("Mat", col), root)
            obj("Packet", rbox((0.16, 0.02, 0.22), (x + 0.09, 0.23, z), 0.01), mat("Mat", col), root)
    # angled till screen on the counter
    scr = rbox((0.5, 0.04, 0.32), (0, 0, 0), 0.01)
    bmesh.ops.rotate(scr, matrix=Matrix.Rotation(math.radians(-25), 3, "X"), verts=scr.verts)
    bmesh.ops.translate(scr, vec=Vector((0.45, 0.05, 1.25)), verts=scr.verts)
    obj("Till", scr, GUN, root)
    glass = rbox((0.44, 0.01, 0.26), (0, 0, 0), 0)
    bmesh.ops.rotate(glass, matrix=Matrix.Rotation(math.radians(-25), 3, "X"), verts=glass.verts)
    bmesh.ops.translate(glass, vec=Vector((0.45, 0.025, 1.25)), verts=glass.verts)
    obj("Till screen", glass, SCREEN, root)
    # produce crates on the counter
    for x, col in ((-0.6, "9BD068"), (-0.25, "E0362A")):
        obj("Crate", rbox((0.3, 0.3, 0.12), (x, -0.05, 1.12), 0.01), mat("Mat", "A8835A"), root)
        for k in range(4):
            obj("Produce", ellipsoid((0.055, 0.055, 0.05), (x - 0.07 + (k % 2) * 0.14, -0.12 + (k // 2) * 0.14, 1.2)), mat("Mat", col), root)
    # canopy with neon edge and sign
    obj("Post", rbox((0.08, 0.08, 2.5), (-0.88, -0.3, 1.25), 0.01), GUN, root)
    obj("Post", rbox((0.08, 0.08, 2.5), (0.88, -0.3, 1.25), 0.01), GUN, root)
    obj("Canopy", rbox((2.0, 0.95, 0.08), (0, -0.08, 2.52), 0.02), WHITE, root)
    for y in (-0.555, 0.395):
        obj("Canopy light", rbox((2.0, 0.02, 0.025), (0, y, 2.5), 0), CYAN, root)
    for x in (-1.0, 1.0):
        obj("Canopy light", rbox((0.02, 0.95, 0.025), (x, -0.08, 2.5), 0), CYAN, root)
    obj("Sign plate", rbox((1.1, 0.06, 0.3), (0, -0.56, 2.25), 0.02), PANEL, root)
    bpy.ops.object.text_add(location=(0, -0.595, 2.25)); t = bpy.context.object
    t.data.body = "SEEDS"; t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.size = 0.2; t.data.extrude = 0.005
    t.rotation_euler = (math.radians(90), 0, 0); t.data.materials.append(AMBER); t.parent = root
    bpy.ops.object.select_all(action="DESELECT"); t.select_set(True); bpy.context.view_layer.objects.active = t
    bpy.ops.object.convert(target="MESH")
    return root


# ------------------------------------------------------------------ export and icons
def export(root, name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    def walk(o):
        o.select_set(True)
        for c in o.children: walk(c)
    walk(root)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False)


def icon(root, path, size=256):
    """Renders the crop alone, three-quarter view, on a transparent background."""
    sc = bpy.context.scene
    def under(o):
        while o is not None:
            if o == root: return True
            o = o.parent
        return False
    for o in bpy.data.objects:
        o.hide_render = not (under(o) or o.type in ("LIGHT", "CAMERA"))
    pts = [o.matrix_world @ Vector(c) for o in bpy.data.objects if not o.hide_render and o.type == "MESH" for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c, r = (lo + hi) / 2, (hi - lo).length / 2
    cam = bpy.data.objects.get("Icon camera") or bpy.data.objects.new("Icon camera", bpy.data.cameras.new("Icon camera"))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = r * 2.15
    d = Vector((0.9, -1.4, 0.95)).normalized()
    cam.location = c + d * (r * 4); cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    for n, e, rot in (("Key", 4.0, (50, 0, 30)), ("Fill", 1.5, (60, 0, -120))):
        l = bpy.data.objects.get(n) or bpy.data.objects.new(n, bpy.data.lights.new(n, "SUN"))
        if l.name not in sc.collection.objects: sc.collection.objects.link(l)
        l.data.energy = e; l.rotation_euler = [math.radians(a) for a in rot]
    sc.camera = cam
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in bpy.data.objects: o.hide_render = False


def build_all():
    wipe()
    os.makedirs(OUT + "/Icons", exist_ok=True)
    crops = {"lettuce": lettuce(), "tomato": tomato(), "watermelon": watermelon()}
    for i, (cid, root) in enumerate(crops.items()):
        root.location.x = i * 1.2
    bpy.context.view_layer.update()
    for cid, root in crops.items():
        icon(root, OUT + "/Icons/" + cid + ".png")
    for cid, root in crops.items():
        x = root.location.x; root.location.x = 0; bpy.context.view_layer.update()
        export(root, cid.capitalize())
        root.location.x = x
    k = kiosk(); k.location.x = -2.5
    bpy.context.view_layer.update()
    k.location.x = 0; bpy.context.view_layer.update()
    export(k, "SeedKiosk")
    k.location.x = -2.5
    return sorted(os.listdir(OUT))
