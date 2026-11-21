"""
Greenhouse art: the seed shop kiosk (for storage) and the three crops, plus their shop icons.

Exports to Assets/Resources/Garden: SeedKiosk.fbx, ToolShed.fbx, Lettuce.fbx, Tomato.fbx, Watermelon.fbx,
<Crop>Item.fbx (held in the hand) and Icons/<id>.png. Crops stand on their origin (the soil
surface) with three stage children (Bud, Unripe, Ripe) the game switches between. Every material is named
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
# Each crop has three stage children, shown one at a time by the game: "Bud" (a sprout), "Unripe"
# (the plant with green or small fruit) and "Ripe" (full grown, ready to pick). "<Crop>Item" is the
# picked crop held in the hand.
STEM = "6FA048"

def sprout(parent, leaf_hex, n, size, height):
    """Bud stage: a short stem with a few small leaves opening at its top."""
    obj("Stem", cyl(0.008, height, (0, 0, height / 2)), mat("Mat", STEM), parent)
    for i in range(n):
        obj("Leaf", leaf(size, size * 0.7, (0, 0, height), 2 * math.pi * i / n + 0.3, 0.55), mat("Mat", leaf_hex), parent)


def rosette(parent, rings, light, mid, heart):
    for ring, (n, ln, tilt, h) in enumerate(rings):
        for i in range(n):
            a = 2 * math.pi * i / n + ring * 0.4
            obj("Leaf", leaf(ln, ln * 0.75, (0, 0, h), a, tilt), mat("Mat", mid if ring == 0 else light), parent)
    if heart: obj("Heart", ellipsoid((heart, heart, heart), (0, 0, 0.14)), mat("Mat", light), parent)


def lettuce():
    root = empty("Lettuce")
    sprout(empty("Bud", root), "9BD068", 4, 0.09, 0.05)
    rosette(empty("Unripe", root), ((7, 0.17, 0.45, 0.03), (5, 0.12, 0.85, 0.06)), "7DB552", "4F8E37", 0)
    rosette(empty("Ripe", root), ((9, 0.26, 0.35, 0.03), (7, 0.2, 0.75, 0.07), (5, 0.13, 1.1, 0.12)), "9BD068", "5FA341", 0.07)
    return root


def tomato_plant(parent, fruit_hex, r):
    obj("Stake", cyl(0.012, 0.95, (0.12, 0, 0.475)), mat("Mat", "A8835A"), parent)
    obj("Stem", cyl(0.016, 0.82, (0, 0, 0.41)), mat("Mat", "4E7A33"), parent)
    for k in range(7):
        z = 0.15 + k * 0.1; a = k * 2.3
        obj("Leaf", leaf(0.2, 0.13, (0, 0, z), a, 0.25), mat("Mat", "3F8A3A"), parent)
        obj("Leaf", leaf(0.17, 0.11, (0, 0, z + 0.04), a + math.pi, 0.35), mat("Mat", "3F8A3A"), parent)
    for (x, y, z) in ((0.09, 0.05, 0.42), (-0.08, 0.07, 0.55), (0.07, -0.08, 0.66), (-0.06, -0.05, 0.32), (0.04, 0.09, 0.75)):
        obj("Tomato", ellipsoid((r, r, r * 0.87), (x, y, z)), mat("Mat", fruit_hex), parent)
        obj("Calyx", ellipsoid((0.025, 0.025, 0.008), (x, y, z + r * 0.85), seg=8), mat("Mat", "4E7A33"), parent)


def tomato():
    root = empty("Tomato")
    sprout(empty("Bud", root), "3F8A3A", 4, 0.08, 0.15)
    tomato_plant(empty("Unripe", root), "7FB24A", 0.038)
    tomato_plant(empty("Ripe", root), "E0362A", 0.055)
    return root


def melon(parent, size, centre, striped):
    bm = ellipsoid(size, centre, seg=32)
    me = bpy.data.meshes.new("Melon"); bm.to_mesh(me); bm.free()
    me.materials.append(mat("Mat", "2E6B2A" if striped else "A6CF7A")); me.materials.append(mat("Mat", "8CC266"))
    for p in me.polygons:
        ang = math.atan2(p.center.z - centre[2], p.center.y - centre[1])
        p.material_index = 1 if striped and int((ang + math.pi) / (2 * math.pi) * 16) % 2 else 0
        p.use_smooth = True
    o = bpy.data.objects.new("Melon", me); bpy.context.scene.collection.objects.link(o); o.parent = parent
    return o


def melon_vine(parent, n, size):
    for i in range(n):
        a = 2 * math.pi * i / n
        obj("Leaf", leaf(size, size * 0.83, (0.1 * math.cos(a), 0.1 * math.sin(a), 0.02), a, 0.15), mat("Mat", "3E8C3A"), parent)
    obj("Vine", cyl(0.012, 0.5, (0, 0, 0.02), rot=Matrix.Rotation(math.pi / 2, 3, "Y")), mat("Mat", "4E7A33"), parent)


def watermelon():
    root = empty("Watermelon")
    sprout(empty("Bud", root), "3E8C3A", 3, 0.1, 0.04)
    unripe = empty("Unripe", root)
    melon_vine(unripe, 6, 0.2)
    melon(unripe, (0.12, 0.08, 0.075), (0, -0.12, 0.08), False)
    obj("Flower", ellipsoid((0.03, 0.03, 0.012), (0.14, 0.08, 0.06), seg=8), mat("Mat", "F2D14B"), unripe)
    ripe = empty("Ripe", root)
    melon_vine(ripe, 8, 0.24)
    melon(ripe, (0.26, 0.17, 0.16), (0, -0.12, 0.15), True)
    obj("Stalk", cyl(0.01, 0.06, (0.27, -0.12, 0.17), rot=Matrix.Rotation(math.pi / 2, 3, "Y")), mat("Mat", "4E7A33"), ripe)
    return root


def crop_items():
    """The picked crops, held in the hand (origin at their middle)."""
    lettuce_item = empty("LettuceItem")
    for i in range(7):
        a = 2 * math.pi * i / 7
        obj("Leaf", leaf(0.09, 0.08, (0, 0, -0.03), a, 0.9), mat("Mat", "7DB552"), lettuce_item)
    obj("Heart", ellipsoid((0.065, 0.065, 0.06), (0, 0, 0.0)), mat("Mat", "9BD068"), lettuce_item)
    tomato_item = empty("TomatoItem")
    obj("Tomato", ellipsoid((0.045, 0.045, 0.04), (0, 0, 0)), mat("Mat", "E0362A"), tomato_item)
    obj("Calyx", ellipsoid((0.022, 0.022, 0.007), (0, 0, 0.038), seg=8), mat("Mat", "4E7A33"), tomato_item)
    melon_item = empty("WatermelonItem")
    melon(melon_item, (0.15, 0.1, 0.095), (0, 0, 0), True)
    return {"LettuceItem": lettuce_item, "TomatoItem": tomato_item, "WatermelonItem": melon_item}


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


# ------------------------------------------------------------------ tool shed
def toolshed():
    """Tool shed for the greenhouse's far end: three labelled bays (sprayer, shovel hanging
    on two wall hooks in the middle, lamp), 3 m wide, facing -Y, origin on the floor."""
    root = empty("ToolShed")
    WHITE, GUN, PANEL = mat("Mat", "E4E1DA", 0.5), mat("Metal", "3A3D44", 0.35), mat("Mat", "2A2D34", 0.6)
    CYAN, AMBER = mat("Glow", "3FE0FF"), mat("Glow", "FFB547")
    # Dark slate bay backs, so the shed stands out from the pale greenhouse wall; a warm light strip
    # lights each bay from the top so the tools still read.
    obj("Pegboard", rbox((3.0, 0.06, 2.0), (0, 0.27, 1.3), 0.02), mat("Mat", "3E434C", 0.7), root)
    for x in (-1.0, 0.0, 1.0):
        obj("Bay light", rbox((0.84, 0.04, 0.025), (x, 0.2, 1.93), 0), mat("Glow", "FFE9CF"), root)
    obj("Frame", rbox((3.1, 0.12, 0.08), (0, 0.25, 2.32), 0.01), GUN, root)
    for x in (-1.52, -0.5, 0.5, 1.52):        # bay dividers
        obj("Divider", rbox((0.06, 0.5, 2.1), (x, 0.05, 1.25), 0.01), GUN, root)
    # One white cabinet per bay: shelf height for the sprayer and lamp, a low platform under the
    # hanging shovel (its blade clears it).
    for x, h in ((-1.0, 0.9), (0.0, 0.4), (1.0, 0.9)):
        obj("Cabinet", rbox((0.96, 0.5, h), (x, 0.03, h / 2), 0.03), WHITE, root)
        obj("Shelf", rbox((1.0, 0.56, 0.05), (x, 0.03, h + 0.025), 0.01), GUN, root)
        obj("Shelf light", rbox((0.92, 0.015, 0.02), (x, -0.255, h - 0.02), 0), CYAN, root)
    for x in (-0.12, 0.12):                   # two pegs the shovel hangs on, below its handle
        obj("Hook", cyl(0.012, 0.12, (x, 0.18, 1.62), rot=Matrix.Rotation(math.pi / 2, 3, "X")), GUN, root)
    obj("Awning", rbox((3.2, 0.75, 0.06), (0, -0.07, 2.42), 0.02), WHITE, root)
    obj("Awning light", rbox((3.2, 0.02, 0.025), (0, -0.445, 2.4), 0), CYAN, root)
    for x, label in ((-1.0, "SPRAYER"), (0.0, "SHOVEL"), (1.0, "LAMP")):
        obj("Bay plate", rbox((0.84, 0.03, 0.2), (x, 0.22, 2.08), 0.01), GUN, root)
        bpy.ops.object.text_add(location=(x, 0.2, 2.08)); t = bpy.context.object
        t.data.body = label; t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.size = 0.11; t.data.extrude = 0.003
        t.rotation_euler = (math.radians(90), 0, 0); t.data.materials.append(AMBER); t.parent = root
        bpy.ops.object.select_all(action="DESELECT"); t.select_set(True); bpy.context.view_layer.objects.active = t
        bpy.ops.object.convert(target="MESH")
        if x != 0.0:   # shelf bays: an amber line where the tool sits
            obj("Bay outline", rbox((0.7, 0.01, 0.015), (x, 0.235, 1.0), 0), AMBER, root)
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
        icon(next(c for c in root.children if c.name.startswith("Ripe")), OUT + "/Icons/" + cid + ".png")
    for cid, root in crops.items():
        x = root.location.x; root.location.x = 0; bpy.context.view_layer.update()
        export(root, cid.capitalize())
        root.location.x = x
    for name, item in crop_items().items():
        item.location.x = 6; bpy.context.view_layer.update()
        item.location.x = 0; bpy.context.view_layer.update()
        export(item, name)
        item.location.x = 6 + len(name) * 0.1
    shed = toolshed(); bpy.context.view_layer.update()
    export(shed, "ToolShed")
    shed.location.x = 3.5
    k = kiosk(); k.location.x = -2.5
    bpy.context.view_layer.update()
    k.location.x = 0; bpy.context.view_layer.update()
    export(k, "SeedKiosk")
    k.location.x = -2.5
    return sorted(os.listdir(OUT))
