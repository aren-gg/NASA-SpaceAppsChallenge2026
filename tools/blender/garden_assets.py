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


# ------------------------------------------------------------------ hub centrepiece and water pump
def oxygen_tree():
    """The hub's centrepiece, a spirit tree that makes the base's oxygen: a real gnarled tree (Poly
    Haven's "Jacaranda Tree", Rico Cilliers, CC0, cut down to tools/blender/assets/jacaranda_lite.glb)
    drawn up to ~7.5 m under the skylight, with glowing teal leaves, glowing blossoms on the branch
    tips, light motes rising and glowing veins in the planter's soil. Origin on the floor."""
    import random
    rnd = random.Random(11)
    root = empty("OxygenTree")
    GUN, WHITE, SOIL = mat("Metal", "3A3D44", 0.35), mat("Mat", "E4E1DA", 0.5), mat("Mat", "2B211C", 0.95)
    CYAN, VEIN, MOTE = mat("Glow", "3FE0FF"), mat("Glow", "5FFFD0"), mat("Glow", "C8FFF0")
    obj("Planter", cyl(1.9, 0.6, (0, 0, 0.3), seg=64), WHITE, root)
    obj("Planter rim", cyl(1.95, 0.08, (0, 0, 0.62), seg=64), GUN, root)
    obj("Rim light", cyl(1.97, 0.03, (0, 0, 0.5), seg=64), CYAN, root)
    obj("Soil", cyl(1.85, 0.03, (0, 0, 0.665), seg=64), SOIL, root)   # top 0.68, clear of the rim top (0.66): no z-fight
    for k in range(12):   # glowing veins in the soil, out from the trunk
        a = k * 2 * math.pi / 12 + rnd.uniform(-0.1, 0.1)
        v = rbox((1.3, 0.035, 0.012), (0.9 * math.cos(a), 0.9 * math.sin(a), 0.69), 0)
        bmesh.ops.rotate(v, matrix=Matrix.Rotation(a, 3, "Z"), verts=v.verts, cent=Vector((0.9 * math.cos(a), 0.9 * math.sin(a), 0.69)))
        obj("Root vein", v, VEIN, root)
    for k in range(6):
        a = k * math.pi / 3   # 0, 60, 120...: clear of the OXYGEN lettering at 90 and 270
        v = rbox((0.6, 0.06, 0.18), (1.92 * math.cos(a), 1.92 * math.sin(a), 0.3), 0.01)
        bmesh.ops.rotate(v, matrix=Matrix.Rotation(a + math.pi / 2, 3, "Z"), verts=v.verts, cent=Vector((1.92 * math.cos(a), 1.92 * math.sin(a), 0.3)))
        obj("Vent", v, CYAN, root)
        bpy.context.view_layer.update()
    # The tree, drawn up tall and slender to rise toward the skylight.
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, "tools/blender/assets/jacaranda_lite.glb"))
    tree = []
    for o in [o for o in bpy.data.objects if o not in before]:
        if o.type != "MESH": bpy.data.objects.remove(o, do_unlink=True); continue
        o.data.transform(Matrix.Translation((0, 0, 0.62)) @ Matrix.Diagonal((1.0, 1.0, 1.35, 1)))
        o.parent = root; o.matrix_parent_inverse.identity(); o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
        tree.append(o)
        if "leaves" in o.name.lower():
            for m in o.data.materials:
                m.name = "Spirit leaves"   # the game lights these teal (MarsWorldTasks.Model)
                b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
                b.inputs["Emission Color"].default_value = (0.2, 1.0, 0.8, 1); b.inputs["Emission Strength"].default_value = 0.8
    # Glowing blossoms nestled in the crown, on the branch tips.
    crown = [v.co.copy() for o in tree if "branch" in o.name.lower() for v in o.data.vertices]
    crown = [p for p in crown if p.z > 3.6]
    rnd.shuffle(crown)
    for p in crown[:90]:
        obj("Blossom", ellipsoid((rnd.uniform(0.04, 0.08),) * 3, p, seg=8), MOTE, root)
    for k in range(60):   # light motes drifting up through the crown
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(0.4, 3.0)
        obj("Light mote", ellipsoid((0.045,) * 3, (r * math.cos(a), r * math.sin(a), rnd.uniform(1.2, 8.0)), seg=6), MOTE, root)
    for side in (-1, 1):   # OXYGEN on the planter, front and back
        bpy.ops.object.text_add(location=(0, side * 1.91, 0.32)); t = bpy.context.object
        t.data.body = "OXYGEN"; t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.size = 0.22; t.data.extrude = 0.004
        t.rotation_euler = (math.radians(90), 0, 0 if side < 0 else math.pi); t.data.materials.append(CYAN); t.parent = root
        bpy.ops.object.select_all(action="DESELECT"); t.select_set(True); bpy.context.view_layer.objects.active = t
        bpy.ops.object.convert(target="MESH")
    return root


def water_pump():
    """The water station: a pump drawing melt water up from the ice below into a clear tank, with a
    tap where you fill up. 2.2 m wide, facing -Y, origin on the floor."""
    root = empty("WaterPump")
    GUN, WHITE, DARK = mat("Metal", "3A3D44", 0.35), mat("Mat", "E4E1DA", 0.5), mat("Mat", "2A2D34", 0.6)
    WATER, CYAN, AMBER = mat("Glow", "2E8FD6"), mat("Glow", "3FE0FF"), mat("Glow", "FFB547")
    obj("Base", rbox((2.2, 1.0, 0.15), (0, 0, 0.075), 0.02), GUN, root)
    obj("Wellhead", cyl(0.32, 0.5, (-0.65, 0.05, 0.4), seg=24), GUN, root)          # pipe down to the ice
    obj("Wellhead ring", cyl(0.36, 0.06, (-0.65, 0.05, 0.6), seg=24), AMBER, root)
    obj("Pump body", rbox((0.6, 0.6, 0.9), (-0.65, 0.05, 1.1), 0.04), WHITE, root)
    obj("Motor", cyl(0.2, 0.5, (-0.65, -0.2, 1.3), seg=20, rot=Matrix.Rotation(math.pi / 2, 3, "X")), DARK, root)
    obj("Pump light", rbox((0.5, 0.01, 0.04), (-0.65, -0.255, 0.8), 0), CYAN, root)
    obj("Riser", cyl(0.06, 1.2, (-0.25, 0.25, 1.6), seg=12, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), GUN, root)
    obj("Tank", cyl(0.42, 1.6, (0.55, 0.1, 1.0), seg=32), WATER, root)            # the water itself, lit blue
    for z in (0.6, 1.0, 1.4):
        obj("Tank band", cyl(0.44, 0.05, (0.55, 0.1, z), seg=32), GUN, root)
    obj("Tank cap", cyl(0.45, 0.08, (0.55, 0.1, 1.84), seg=32), GUN, root)
    obj("Tank foot", cyl(0.45, 0.08, (0.55, 0.1, 0.2), seg=32), GUN, root)
    obj("Tap", rbox((0.14, 0.24, 0.1), (0.55, -0.4, 1.0), 0.02), GUN, root)
    obj("Tap spout", cyl(0.03, 0.12, (0.55, -0.5, 0.92), seg=10), GUN, root)
    obj("Drip tray", rbox((0.4, 0.3, 0.04), (0.55, -0.5, 0.45), 0.01), DARK, root)
    obj("Sign plate", rbox((1.0, 0.06, 0.28), (0, 0.42, 1.98), 0.02), DARK, root)   # low enough to clear the vault at 1.3x
    obj("Sign post", rbox((0.06, 0.06, 1.95), (-0.5, 0.42, 0.98), 0.01), GUN, root)
    obj("Sign post", rbox((0.06, 0.06, 1.95), (0.5, 0.42, 0.98), 0.01), GUN, root)
    bpy.ops.object.text_add(location=(0, 0.385, 1.98)); t = bpy.context.object
    t.data.body = "WATER"; t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.size = 0.18; t.data.extrude = 0.004
    t.rotation_euler = (math.radians(90), 0, 0); t.data.materials.append(CYAN); t.parent = root
    bpy.ops.object.select_all(action="DESELECT"); t.select_set(True); bpy.context.view_layer.objects.active = t
    bpy.ops.object.convert(target="MESH")
    return root


def label(text, loc, size, m, parent, yaw=0.0):
    bpy.ops.object.text_add(location=loc); t = bpy.context.object
    t.data.body = text; t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.size = size; t.data.extrude = 0.004
    t.rotation_euler = (math.radians(90), 0, yaw); t.data.materials.append(m); t.parent = parent
    bpy.ops.object.select_all(action="DESELECT"); t.select_set(True); bpy.context.view_layer.objects.active = t
    bpy.ops.object.convert(target="MESH")
    return t


def resources_decor():
    """Dressing for the resources wing around the (enlarged) seed kiosk and water pump: water reserve
    tanks piped from the pump, a sleep pod (the bunk), a supply rack, crate stacks, a work console under
    the far window and floor markings. Origin = the room's floor centre; X along the room (+X toward the
    hub door), Y across it, walls at about Y = +-3.95, far wall at X = -4.25."""
    import random
    rnd = random.Random(5)
    root = empty("ResourcesDecor")
    GUN, WHITE, DARK = mat("Metal", "3A3D44", 0.35), mat("Mat", "E4E1DA", 0.5), mat("Mat", "2A2D34", 0.6)
    WOOD, SACK, STEEL = mat("Mat", "A8835A"), mat("Mat", "8A6F4E", 0.9), mat("Metal", "8E949C", 0.3)
    WATER, CYAN, AMBER, SCREEN = mat("Glow", "2E8FD6"), mat("Glow", "3FE0FF"), mat("Glow", "FFB547"), mat("Glow", "0F4C5C")
    HAZ, RED = mat("Mat", "E2A21B", 0.5), mat("Mat", "B23A3A", 0.5)
    # Water reserve: two tall tanks in the south-west corner, piped from the pump along the wall.
    for k, x in enumerate((-3.55, -2.6)):
        y = -3.35
        obj("Reserve tank", cyl(0.42, 2.3, (x, y, 1.2), seg=32), WHITE, root)
        obj("Reserve level", rbox((0.06, 0.02, 1.5 - 0.35 * k), (x, y + 0.42, 0.35 + (1.5 - 0.35 * k) / 2), 0), WATER, root)
        for z in (0.1, 2.35):
            obj("Reserve band", cyl(0.44, 0.08, (x, y, z), seg=32), GUN, root)
    obj("Reserve pipe", cyl(0.06, 3.4, (-2.0, -3.75, 2.15), seg=12, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), STEEL, root)
    obj("Reserve pipe light", rbox((3.4, 0.01, 0.02), (-2.0, -3.68, 2.05), 0), CYAN, root)
    label("WATER RESERVE", (-3.07, -2.92, 2.62), 0.16, CYAN, root, yaw=math.pi)   # faces into the room
    # Crew quarters (the bed) in the north-west corner: a capsule bunk open to the room, with a made
    # bed, a warm reading strip, a wall screen, a half-drawn curtain and a step.
    cx, cy = -3.05, 3.35
    BLANKET, SHEET, CLOTH = mat("Mat", "2F4B6B", 0.9), mat("Mat", "C9CED6", 0.9), mat("Mat", "596B7A", 0.9)
    WARM = mat("Glow", "FFE2B8")
    obj("Pod plinth", rbox((2.3, 1.1, 0.32), (cx, cy, 0.16), 0.03), GUN, root)
    obj("Pod back", rbox((2.3, 0.08, 1.3), (cx, cy + 0.51, 0.97), 0.03), WHITE, root)
    obj("Pod roof", rbox((2.4, 1.16, 0.1), (cx, cy - 0.01, 1.62), 0.05), WHITE, root)
    for sx in (-1, 1):
        obj("Pod end", rbox((0.1, 1.16, 1.36), (cx + sx * 1.15, cy - 0.01, 0.98), 0.04), WHITE, root)
    obj("Pod lip", rbox((2.4, 0.05, 0.06), (cx, cy - 0.6, 1.55), 0.01), GUN, root)
    obj("Pod sill light", rbox((2.2, 0.02, 0.025), (cx, cy - 0.56, 0.31), 0), AMBER, root)
    obj("Mattress", rbox((2.16, 0.98, 0.18), (cx, cy - 0.02, 0.41), 0.06), SHEET, root)
    obj("Blanket", rbox((1.45, 1.0, 0.06), (cx + 0.33, cy - 0.02, 0.52), 0.03), BLANKET, root)
    obj("Blanket fold", rbox((0.16, 1.0, 0.07), (cx - 0.36, cy - 0.02, 0.54), 0.03), SHEET, root)
    obj("Pillow", rbox((0.42, 0.72, 0.12), (cx - 0.76, cy + 0.05, 0.56), 0.05), WHITE, root)
    obj("Reading strip", rbox((1.9, 0.12, 0.02), (cx, cy + 0.1, 1.56), 0), WARM, root)
    obj("Pod screen", rbox((0.42, 0.02, 0.26), (cx - 0.55, cy + 0.46, 1.0), 0.01), SCREEN, root)
    obj("Pod screen line", rbox((0.3, 0.01, 0.02), (cx - 0.55, cy + 0.448, 0.93), 0), CYAN, root)
    for k in range(4):   # a half-drawn curtain gathered at the foot end
        obj("Curtain", rbox((0.1, 0.04, 1.12), (cx + 0.75 + k * 0.095, cy - 0.53 + (k % 2) * 0.04, 0.95), 0.02), CLOTH, root)
    obj("Curtain rail", rbox((2.2, 0.03, 0.03), (cx, cy - 0.55, 1.52), 0), GUN, root)
    obj("Pod step", rbox((0.7, 0.3, 0.12), (cx - 0.3, cy - 0.72, 0.06), 0.02), GUN, root)
    label("CREW-01", (cx, cy - 0.635, 1.55), 0.09, CYAN, root)
    label("REST", (cx - 0.3, cy - 0.875, 0.06), 0.07, AMBER, root)
    # Supply rack against the south wall, east of the pump: three shelves of crates, sacks and canisters.
    for x in (1.1, 3.6):
        obj("Rack post", rbox((0.05, 0.05, 2.2), (x, -3.85, 1.1), 0.01), GUN, root)
        obj("Rack post", rbox((0.05, 0.05, 2.2), (x, -3.25, 1.1), 0.01), GUN, root)
    for z in (0.15, 0.85, 1.55):
        obj("Rack shelf", rbox((2.55, 0.65, 0.04), (2.35, -3.55, z), 0.01), GUN, root)
        x = 1.3
        while x < 3.4:
            kind = rnd.random()
            if kind < 0.4:
                obj("Crate", rbox((0.42, 0.45, 0.32), (x + 0.21, -3.55, z + 0.18), 0.02), WOOD, root); x += 0.5
            elif kind < 0.7:
                obj("Seed sack", ellipsoid((0.22, 0.2, 0.17), (x + 0.22, -3.55, z + 0.18)), SACK, root); x += 0.48
            else:
                for j in range(2):
                    obj("Canister", cyl(0.09, 0.34, (x + 0.1 + j * 0.2, -3.5, z + 0.19), seg=14), WHITE if j else STEEL, root)
                    obj("Canister cap", cyl(0.095, 0.04, (x + 0.1 + j * 0.2, -3.5, z + 0.37), seg=14), AMBER if j else CYAN, root)
                x += 0.45
    # Crate stacks by the hub door and under the kiosk's flank.
    for (x, y, n) in ((3.55, 3.25, 3), (2.95, 3.35, 2), (-1.0, 3.4, 2)):
        for j in range(n):
            obj("Crate", rbox((0.55, 0.55, 0.42), (x, y, 0.21 + j * 0.43), 0.02), WOOD if j % 2 == 0 else GUN, root)
            obj("Crate stripe", rbox((0.56, 0.56, 0.05), (x, y, 0.3 + j * 0.43), 0.0), HAZ, root)
    # Work console under the far window: a desk with a screen showing the reserves.
    obj("Console desk", rbox((0.7, 1.8, 0.9), (-3.85, -0.0, 0.45), 0.03), WHITE, root)
    obj("Console top", rbox((0.78, 1.9, 0.05), (-3.85, 0, 0.92), 0.02), GUN, root)
    scr = rbox((0.04, 0.9, 0.5), (0, 0, 0), 0.01)
    bmesh.ops.rotate(scr, matrix=Matrix.Rotation(math.radians(-15), 3, "Y"), verts=scr.verts)
    bmesh.ops.translate(scr, vec=Vector((-3.95, 0, 1.25)), verts=scr.verts)
    obj("Console screen", scr, SCREEN, root)
    obj("Console stool", cyl(0.2, 0.5, (-3.1, 0.3, 0.25), seg=16), GUN, root)
    # Floor markings: an amber walkway from the hub door, a cyan pad at the pump, an amber pad at the kiosk.
    for y in (-1.15, 1.15):
        obj("Walkway line", rbox((7.6, 0.05, 0.006), (0.3, y, 0.003), 0), HAZ, root)
    for (cx, cy, w, d, m) in ((-0.75, -1.95, 2.6, 0.9, CYAN), (1.15, 2.05, 2.2, 0.8, AMBER)):
        for (dx, dy, sx, sy) in ((0, -d / 2, w, 0.06), (0, d / 2, w, 0.06), (-w / 2, 0, 0.06, d), (w / 2, 0, 0.06, d)):
            obj("Floor pad", rbox((sx, sy, 0.006), (cx + dx, cy + dy, 0.004), 0), m, root)
    for k in range(5):   # hazard chevrons on the floor by the reserve tanks
        obj("Hazard", rbox((0.3, 0.08, 0.006), (-3.6 + k * 0.32, -2.65, 0.004), 0), HAZ, root)
    return root


def loft(sections, cap=True):
    """Lofts closed rings (lists of (x, y, z) points, same count) into a solid, capping both ends."""
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in r] for r in sections]
    n = len(sections[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    if cap:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def section(y, hw, bottom, top, lc, uc, ins=0.0):
    """A chamfered octagon across the rover at forward position y (Blender Y = game +Z forward)."""
    hw -= ins; bottom += ins; top -= ins
    return [(-hw + lc, y, bottom), (hw - lc, y, bottom), (hw, y, bottom + lc), (hw, y, top - uc),
            (hw - uc, y, top), (-hw + uc, y, top), (-hw, y, top - uc), (-hw, y, bottom + lc)]


def rover_body():
    """The rover's body, everything but the wheels and their arms (the game builds those and springs
    them): a low pearl-white wedge cabin with a wrap-around smoked canopy, angular wheel pods, cyan
    light lines, a front light bar, a lidar dome and solar fins. Rover space: origin on the ground
    between the wheels, +Y forward (game +Z), wheels at x +-1.45, y +-1.75, radius 0.62."""
    root = empty("RoverBody")
    PEARL, GUN, DARK = mat("Mat", "E8ECEF", 0.35), mat("Metal", "3A3D44", 0.35), mat("Mat", "1E2128", 0.6)
    GLASS = mat("Metal", "0C1218", 0.05)
    CYAN, AMBER, RED, WARM = mat("Glow", "3FE0FF"), mat("Glow", "FFB547"), mat("Glow", "FF3B3B"), mat("Glow", "FFF1DC")
    SOLAR = mat("Metal", "1C2A55", 0.2)
    # Skateboard chassis under everything.
    obj("Chassis", rbox((1.7, 5.4, 0.42), (0, 0, 0.76), 0.06), GUN, root, smooth=False)
    obj("Skid plate", rbox((1.4, 4.6, 0.06), (0, 0, 0.54), 0.02), DARK, root, smooth=False)
    # Cabin: a wedge, tall at the back, raked down to a low nose.
    body = [section(-3.05, 0.95, 1.0, 1.95, 0.25, 0.3), section(-2.6, 1.18, 0.92, 2.3, 0.3, 0.42),
            section(0.3, 1.18, 0.92, 2.36, 0.3, 0.45), section(1.6, 1.14, 0.95, 1.95, 0.28, 0.4),
            section(2.7, 1.0, 0.98, 1.45, 0.25, 0.2), section(3.15, 0.85, 1.02, 1.3, 0.18, 0.12)]
    obj("Cabin", loft(body), PEARL, root, smooth=False)
    # Smoked canopy wrapping the front of the cabin (a skin just proud of it).
    can = [section(-0.4, 1.2, 1.55, 2.38, 0.3, 0.46), section(0.3, 1.2, 1.55, 2.38, 0.3, 0.46),
           section(1.6, 1.16, 1.5, 1.97, 0.28, 0.41), section(2.45, 1.06, 1.45, 1.6, 0.26, 0.24)]
    obj("Canopy", loft(can), GLASS, root, smooth=False)
    for sx in (-1, 1):
        obj("Canopy line", rbox((0.02, 2.7, 0.025), (sx * 1.21, 1.0, 1.52), 0), CYAN, root, smooth=False)
    obj("Canopy rim", loft([section(-0.45, 1.205, 1.5, 2.385, 0.3, 0.46), section(-0.38, 1.205, 1.5, 2.385, 0.3, 0.46)]), GUN, root, smooth=False)
    # Dark lower skirt along each side with a cyan light line above it, and a hatch outline.
    for sx in (-1, 1):
        obj("Skirt", rbox((0.05, 5.2, 0.32), (sx * 1.17, -0.15, 1.12), 0.01), DARK, root, smooth=False)
        obj("Side light", rbox((0.02, 4.8, 0.03), (sx * 1.2, -0.2, 1.32), 0), CYAN, root, smooth=False)
        obj("Hatch window", rbox((0.02, 0.9, 0.35), (sx * 1.19, -1.3, 1.95), 0.01), GLASS, root, smooth=False)
        label("MR-01", (sx * 1.19, -1.3, 1.6), 0.12, AMBER, root, yaw=math.pi / 2 * sx)   # under the hatch window, clear of the wheel pod
    # Angular wheel pods over each wheel, with a cyan lip and a dark underside.
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 1.45, sy * 1.75
            arc = [(-0.92, 0.95), (-0.72, 1.3), (-0.35, 1.47), (0.35, 1.47), (0.72, 1.3), (0.92, 0.95)]   # (along, top)
            ring = lambda dy, z: [(x - 0.37, y + dy, z - 0.13), (x + 0.37, y + dy, z - 0.13), (x + 0.37, y + dy, z), (x - 0.37, y + dy, z)]
            obj("Wheel pod", loft([ring(dy, z) for dy, z in arc]), PEARL, root, smooth=False)
            obj("Pod lip", rbox((0.03, 0.7, 0.03), (x + sx * 0.38, y, 1.4), 0), CYAN, root, smooth=False)
            obj("Pod strut", rbox((0.3, 0.3, 0.4), (sx * 1.1, y, 1.2), 0.03), GUN, root, smooth=False)
    # Front: a full-width light bar, two headlight slits and a bumper; back: red bar and a ladder.
    obj("Light bar", rbox((1.5, 0.03, 0.05), (0, 3.16, 1.2), 0), WARM, root, smooth=False)
    for sx in (-1, 1):
        obj("Headlight", rbox((0.4, 0.03, 0.07), (sx * 0.5, 3.12, 1.1), 0), CYAN, root, smooth=False)
    obj("Bumper", rbox((1.9, 0.3, 0.22), (0, 3.0, 0.82), 0.04), GUN, root, smooth=False)
    obj("Tail bar", rbox((1.6, 0.03, 0.05), (0, -3.09, 1.68), 0), RED, root, smooth=False)   # above the door, not through it
    obj("Rear bumper", rbox((1.8, 0.3, 0.22), (0, -2.95, 0.82), 0.04), GUN, root, smooth=False)
    obj("Rear door", rbox((1.0, 0.03, 0.55), (0, -3.07, 1.3), 0.01), DARK, root, smooth=False)
    # Roof: lidar dome on a mast, two solar fins and amber marker lights.
    obj("Lidar mast", cyl(0.07, 0.25, (0, 1.0, 2.45), seg=12), GUN, root)
    obj("Lidar", ellipsoid((0.18, 0.18, 0.12), (0, 1.0, 2.6), seg=16), DARK, root)
    obj("Lidar ring", cyl(0.185, 0.025, (0, 1.0, 2.58), seg=24), CYAN, root)
    for sx in (-1, 1):
        fin = rbox((0.55, 2.2, 0.03), (0, 0, 0), 0.01)
        bmesh.ops.rotate(fin, matrix=Matrix.Rotation(sx * math.radians(12), 3, "Y"), verts=fin.verts)
        bmesh.ops.translate(fin, vec=Vector((sx * 0.38, -1.35, 2.42)), verts=fin.verts)
        obj("Solar fin", fin, SOLAR, root, smooth=False)
        obj("Roof marker", rbox((0.08, 0.08, 0.04), (sx * 0.75, -2.55, 2.31), 0), AMBER, root, smooth=False)
    obj("Antenna", cyl(0.012, 0.7, (-0.6, -2.7, 2.6), seg=6), GUN, root)
    return root


def lathe_x(profile, seg=48, closed=True):
    """A solid of revolution about the X axis from an open (x, r) profile; caps the ends if closed."""
    bm = bmesh.new()
    rings = [[bm.verts.new((x, r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg))) for x, r in profile] for i in range(seg)]
    for i in range(seg):
        a, b = rings[i], rings[(i + 1) % seg]
        for j in range(len(profile) - 1):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
    if closed:
        for j in (0, len(profile) - 1):
            if profile[j][1] > 0: bm.faces.new([rings[i][j] for i in range(seg)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def rover_wheel():
    """One rover wheel (the game spins and springs it): an off-road tyre with rounded shoulders and
    staggered chevron lugs, a gunmetal beadlock rim with six spokes and a bolted hub with a cyan ring.
    Axle along X, centre at the origin, radius 0.62, 0.56 wide."""
    root = empty("RoverWheel")
    RUB, RUB2 = mat("Mat", "1A1B1F", 0.9), mat("Mat", "26272B", 0.85)
    RIM, GUN, CYAN = mat("Metal", "8E949C", 0.3), mat("Metal", "3A3D44", 0.35), mat("Glow", "3FE0FF")
    h = 0.26
    prof = [(-h + 0.02, 0.36), (-h, 0.42), (-h - 0.01, 0.5), (-h + 0.01, 0.55), (-h + 0.05, 0.585),
            (h - 0.05, 0.585), (h - 0.01, 0.55), (h + 0.01, 0.5), (h, 0.42), (h - 0.02, 0.36)]
    obj("Tyre", lathe_x(prof, seg=64, closed=False), RUB, root)   # open: the rim fills the middle
    n = 22
    for k in range(n):   # chevron lugs, each half offset so they interlock round the tread
        for side in (-1, 1):
            a = 2 * math.pi * (k + (0.25 if side > 0 else 0)) / n
            lug = rbox((0.2, 0.15, 0.05), (0, 0, 0), 0.012)
            bmesh.ops.rotate(lug, matrix=Matrix.Rotation(side * math.radians(28), 3, "Z"), verts=lug.verts)
            bmesh.ops.translate(lug, vec=Vector((side * 0.13, 0, 0.6)), verts=lug.verts)
            bmesh.ops.rotate(lug, matrix=Matrix.Rotation(a, 3, "X"), verts=lug.verts)
            obj("Lug", lug, RUB, root, smooth=False)
            # shoulder block wrapping onto the sidewall
            sh = rbox((0.05, 0.12, 0.09), (side * (h + 0.005), 0, 0.53), 0.01)
            bmesh.ops.rotate(sh, matrix=Matrix.Rotation(a, 3, "X"), verts=sh.verts)
            obj("Shoulder lug", sh, RUB2, root, smooth=False)
    for side in (-1, 1):
        x = side * (h - 0.05)
        obj("Rim", lathe_x([(x, 0.37), (x + side * 0.03, 0.37), (x + side * 0.03, 0.3), (x - side * 0.02, 0.3), (x, 0.37)], seg=48, closed=False), RIM, root, smooth=False)
        for k in range(12):   # beadlock bolts
            a = 2 * math.pi * k / 12
            obj("Bead bolt", cyl(0.014, 0.03, (x + side * 0.04, 0.335 * math.cos(a), 0.335 * math.sin(a)), seg=6, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), GUN, root)
    obj("Rim barrel", lathe_x([(-h + 0.05, 0.3), (h - 0.05, 0.3)], seg=48, closed=False), GUN, root)
    for side in (-1, 1):   # a dished face and six spokes on both faces (the game mirrors left and right)
        obj("Dish", lathe_x([(side * 0.12, 0.3), (side * 0.18, 0.12)], seg=48, closed=False), RIM, root)
        for k in range(6):
            a = 2 * math.pi * (k + 0.5 * (side > 0)) / 6
            sp = rbox((0.06, 0.08, 0.2), (side * 0.19, 0, 0.21), 0.012)
            bmesh.ops.rotate(sp, matrix=Matrix.Rotation(a, 3, "X"), verts=sp.verts)
            obj("Spoke", sp, GUN, root, smooth=False)
    for side in (-1, 1):
        obj("Hub", cyl(0.12, 0.08, (side * 0.2, 0, 0), seg=20, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), RIM, root)
        obj("Hub ring", lathe_x([(side * 0.242, 0.1), (side * 0.242, 0.125)], seg=24, closed=False), CYAN, root)
        obj("Hub cap", cyl(0.05, 0.05, (side * 0.25, 0, 0), seg=12, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), GUN, root)
        for k in range(6):
            a = 2 * math.pi * k / 6
            obj("Lug nut", cyl(0.016, 0.03, (side * 0.245, 0.085 * math.cos(a), 0.085 * math.sin(a)), seg=6, rot=Matrix.Rotation(math.pi / 2, 3, "Y")), GUN, root)
    return root


# ------------------------------------------------------------------ export and icons
def flatten(root):
    """Joins each child group's meshes into one mesh named after the group, directly under the root.
    The FBX exporter turns Z-up into Y-up only one level down, so deeper meshes came in on their side."""
    for group in [c for c in root.children if c.type == "EMPTY"]:
        meshes = [o for o in group.children_recursive if o.type == "MESH"]
        if not meshes: continue
        bpy.ops.object.select_all(action="DESELECT")
        for o in meshes: o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        mw = joined.matrix_world.copy(); joined.parent = root; joined.matrix_world = mw
        joined.name = joined.data.name = group.name.split(".")[0]
        for o in list(group.children_recursive): bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.objects.remove(group, do_unlink=True)


def join_all(root):
    """Joins every mesh under the root into one multi-material mesh (one renderer in the game, not hundreds)."""
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active; j.name = j.data.name = root.name + " mesh"
    return j


def export(root, name):
    os.makedirs(OUT, exist_ok=True)
    flatten(root)
    bpy.ops.object.select_all(action="DESELECT")
    def walk(o):
        o.select_set(True)
        for c in o.children: walk(c)
    walk(root)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             path_mode="COPY", embed_textures=True,   # the jacaranda keeps its own textures
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
    for name, fn, x in (("OxygenTree", oxygen_tree, 10), ("WaterPump", water_pump, 16)):
        piece = fn(); bpy.context.view_layer.update()
        export(piece, name)
        piece.location.x = x
    for name, fn in (("ResourcesDecor", resources_decor), ("RoverBody", rover_body), ("RoverWheel", rover_wheel)):
        piece = fn(); join_all(piece); bpy.context.view_layer.update()
        export(piece, name)
        piece.location.x = -8
    shed = toolshed(); bpy.context.view_layer.update()
    export(shed, "ToolShed")
    shed.location.x = 3.5
    k = kiosk(); k.location.x = -2.5
    bpy.context.view_layer.update()
    k.location.x = 0; bpy.context.view_layer.update()
    export(k, "SeedKiosk")
    k.location.x = -2.5
    return sorted(os.listdir(OUT))
