"""
Astronaut v3: Quaternius "Universal Base Characters" male body (CC0) dressed as a NASA-punk EVA
suit in the outpost modules' language. The body gives realistic proportions and a standard
humanoid rig (65 bones, Unreal naming); the suit parts are bone-parented hard pieces.

Run after importing Superhero_Male_FullBody.gltf (see SOURCE). Materials keep the game's
astronaut colour-slot names (Suit, Joint, Accent, Trim, Glove, Boot, Visor, Pack, Screen, Lamp).
"""
import bpy, bmesh, math
from mathutils import Vector, Matrix

SOURCE = "Quaternius Universal Base Characters [Standard], Superhero_Male_FullBody (CC0 1.0)"

exec(open("/Users/andrewhuangjin/Documents/code/nasa/tools/blender/astronaut_kit.py").read())

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH" and o.parent == arm and "superhero" in o.name.lower())
body.name = "Body"
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o, do_unlink=True)      # the pack's preview sphere
    elif o.type == "MESH" and o.parent == arm and o is not body:
        o.name = "Eyebrows" if o.dimensions.x > 0.11 or "brow" in o.name.lower() else "Eyes"
SKIN = mat("Skin", "E0AC8A", 0.55)
EYE = mat("Eye", "2B1D16", 0.2)
HAIR = mat("Hair", "3B2A20", 0.6)
for o in bpy.data.objects:
    if o.name.startswith("Eyes"):
        o.data.materials.clear(); o.data.materials.append(EYE)
    elif o.name.startswith("Eyebrows"):
        o.data.materials.clear(); o.data.materials.append(HAIR)

def bone(name):
    return arm.matrix_world @ arm.data.bones[name].head_local
def tail(name):
    return arm.matrix_world @ arm.data.bones[name].tail_local

eye_y = (bone("Head") - bone("neck_01")).y
FWD = 1.0 if tail("foot_l").y > bone("foot_l").y else -1.0   # the foot bone points to the toes
F = Vector((0, FWD, 0))

# ---- the suit: the body itself, inflated into a pressurised cover layer
body.data.materials.clear()
for m in (SUIT, SKIN):
    body.data.materials.append(m)
# Smooth away the muscle detail (a suit hides it), then puff it out evenly. The face and neck
# above the suit's neck seal keep their shape (vertex group "Suit").
NECK_Z = bone("neck_01").z
vg = body.vertex_groups.new(name="Suit")
vg.add([v.index for v in body.data.vertices if (body.matrix_world @ v.co).z < NECK_Z - 0.01], 1.0, "REPLACE")
smooth = body.modifiers.new("Suit smooth", "SMOOTH"); smooth.factor = 1.0; smooth.iterations = 25; smooth.vertex_group = "Suit"
disp = body.modifiers.new("Inflate", "DISPLACE"); disp.strength = 0.045; disp.mid_level = 0.0; disp.vertex_group = "Suit"
body.modifiers.move(len(body.modifiers) - 1, 0)   # both before the armature modifier
body.modifiers.move(len(body.modifiers) - 1, 0)
mw = body.matrix_world
joints = [(bone(n), r) for n, r in (("calf_l", 0.09), ("calf_r", 0.09), ("lowerarm_l", 0.07), ("lowerarm_r", 0.07),
                                     ("upperarm_l", 0.09), ("upperarm_r", 0.09), ("thigh_l", 0.1), ("thigh_r", 0.1))]
for p in body.data.polygons:
    c = mw @ p.center
    p.material_index = 1 if c.z > NECK_Z - 0.005 else 0
    p.use_smooth = True

def attach(o, bone_name):
    """Bone-parent o keeping its world transform."""
    mat = o.matrix_world.copy()
    o.parent = arm; o.parent_type = "BONE"; o.parent_bone = bone_name
    o.matrix_world = mat

def piece(name, bm, m, bone_name, smooth=True):
    o = obj(name, bm, m, smooth)
    attach(o, bone_name)
    return o

def axis_frame(a, b):
    """Matrix whose local Z runs from a to b (for rings and sleeves around a limb)."""
    z = (b - a).normalized()
    x = z.cross(Vector((0, 0, 1))) if abs(z.z) < 0.9 else z.cross(Vector((0, 1, 0)))
    x.normalize(); y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_4x4()

def ring_on(name, a, b, t, r, minor, m, bone_name):
    """A torus around the limb segment a->b at fraction t."""
    bm = torus(r, minor, (0, 0, 0))
    M = Matrix.Translation(a.lerp(b, t)) @ axis_frame(a, b)
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return piece(name, bm, m, bone_name)

# ---- helmet: its own bone under the head (the helmet-on clip moves it from the hands onto the
# head); bubble with an open front behind the visor so the face shows, halo ring with lamps
head = bone("Head"); hc = head + Vector((0, 0.02 * FWD, 0.11))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
hb = arm.data.edit_bones.new("helmet")
inv = arm.matrix_world.inverted()
hb.head = inv @ hc; hb.tail = inv @ (hc + Vector((0, 0, 0.2))); hb.parent = arm.data.edit_bones["Head"]
bpy.ops.object.mode_set(mode="OBJECT")
def front_opening(f):
    d = (f.calc_center_median() - hc).normalized()
    return d.dot(F) > 0.35 and -0.55 < d.z < 0.6
sb = sphere(0.165, hc, 1, 1.04, 1.06, 40, 20)
bmesh.ops.delete(sb, geom=[f for f in sb.faces if front_opening(f)], context="FACES")
bmesh.ops.solidify(sb, geom=list(sb.faces), thickness=0.008)
piece("Helmet shell", sb, SUIT, "helmet")
vb = sphere(0.171, hc, 1, 1.04, 1.06, 40, 20)
kill = [f for f in vb.faces if not front_opening(f)]
bmesh.ops.delete(vb, geom=kill, context="FACES"); bmesh.ops.solidify(vb, geom=list(vb.faces), thickness=0.006)
piece("Visor", vb, VISOR, "helmet")
piece("Halo", torus(0.165, 0.015, hc + Vector((0, 0, 0.1)), sy=1.04), TRIM, "helmet")
piece("Halo light", torus(0.165, 0.006, hc + Vector((0, 0, 0.115)), sy=1.04), CYAN, "helmet")
for s in (-1, 1):
    piece("Lamp housing", rbox((0.035, 0.07, 0.05), hc + Vector((s * 0.148, 0.08 * FWD, 0.1)), 0.01, 2), TRIM, "helmet")
    piece("Lamp", rbox((0.028, 0.008, 0.035), hc + Vector((s * 0.148, 0.117 * FWD, 0.1)), 0.004, 1), LAMP, "helmet")
piece("Neck seal", torus(0.13, 0.024, bone("neck_01") + Vector((0, 0, -0.01)), sy=0.95), TRIM, "neck_01")
piece("Neck light", torus(0.13, 0.008, bone("neck_01") + Vector((0, 0, 0.015)), sy=0.95), CYAN, "neck_01")

# ---- hard upper torso (spine_03): chest frame, display, controls; waist bearing (pelvis)
chest = bone("spine_03")
front = 0.14
piece("Chest frame", rbox((0.26, 0.03, 0.15), chest + Vector((0, front * FWD, 0.04)), 0.03, 2), TRIM, "spine_03")
piece("Display", rbox((0.18, 0.02, 0.08), chest + Vector((0, (front + 0.016) * FWD, 0.055)), 0.012, 2), SCREEN, "spine_03")
piece("Display line", rbox((0.15, 0.008, 0.008), chest + Vector((0, (front + 0.026) * FWD, 0.0)), 0.002, 1), CYAN, "spine_03")
for i, x in enumerate((-0.08, -0.053, 0.053, 0.08)):
    piece("Switch", rbox((0.016, 0.02, 0.016), chest + Vector((x, (front + 0.02) * FWD, -0.015)), 0.004, 1), AMBER if i == 3 else TRIM, "spine_03")
waist = bone("spine_01")
piece("Waist bearing", torus(0.165, 0.022, waist + Vector((0, 0, -0.02)), sx=1.05, sy=0.78), TRIM, "pelvis")
piece("Waist light", torus(0.165, 0.008, waist + Vector((0, 0, -0.02)), sx=1.09, sy=0.84), CYAN, "pelvis")

# ---- PLSS backpack (spine_03): printed like a module
back = chest + Vector((0, -0.2 * FWD, -0.04))
piece("PLSS", rbox((0.38, 0.16, 0.46), back, 0.06, 4), PACK, "spine_03")
for k in range(9):
    piece("PLSS layer", rbox((0.33, 0.006, 0.012), back + Vector((0, -0.082 * FWD, -0.18 + k * 0.042)), 0.003, 1), PACK, "spine_03")
for s in (-1, 1):
    piece("PLSS fin", rbox((0.025, 0.12, 0.38), back + Vector((s * 0.19, 0, 0)), 0.01, 2), PACK, "spine_03")
piece("PLSS frame top", rbox((0.4, 0.03, 0.03), back + Vector((0, 0, 0.24)), 0.01, 2), TRIM, "spine_03")
piece("PLSS frame low", rbox((0.4, 0.03, 0.03), back + Vector((0, 0, -0.23)), 0.01, 2), TRIM, "spine_03")
piece("PLSS spine light", rbox((0.012, 0.012, 0.36), back + Vector((0, -0.088 * FWD, 0.01)), 0.004, 1), CYAN, "spine_03")
piece("PLSS ID", rbox((0.05, 0.012, 0.05), back + Vector((-0.11, -0.088 * FWD, 0.17)), 0.005, 1), ACCENT, "spine_03")
piece("PLSS status", rbox((0.012, 0.012, 0.012), back + Vector((-0.11, -0.096 * FWD, 0.17)), 0.003, 1), AMBER, "spine_03")

# ---- limbs: scye/elbow/knee bearings, accent ID bands, wrist rings, gloves, boots
for s, L in ((1, "l"), (-1, "r")):
    ua, la, hd = bone("upperarm_" + L), bone("lowerarm_" + L), bone("hand_" + L)
    th, cf, ft = bone("thigh_" + L), bone("calf_" + L), bone("foot_" + L)
    ring_on("Scye bearing", ua, la, 0.1, 0.086, 0.018, TRIM, "upperarm_" + L)
    ring_on("ID band", ua, la, 0.45, 0.074, 0.011, ACCENT, "upperarm_" + L)
    for k in range(3):
        ring_on("Elbow rib", la, hd, -0.06 + k * 0.05, 0.07, 0.011, JOINT, "lowerarm_" + L)
    ring_on("Wrist ring", la, hd, 0.93, 0.058, 0.015, TRIM, "lowerarm_" + L)
    # glove: a padded mitt over the hand, built along the hand bone
    hand_dir = (tail("hand_" + L) - hd).normalized()
    g = rbox((0.07, 0.11, 0.16), (0, 0, 0), 0.03, 3)
    M = Matrix.Translation(hd + hand_dir * 0.08) @ axis_frame(hd, hd + hand_dir)
    bmesh.ops.transform(g, matrix=M, verts=g.verts)
    piece("Glove", g, GLOVE, "hand_" + L)
    ring_on("Hip bearing", th, cf, 0.06, 0.12, 0.02, TRIM, "thigh_" + L)
    ring_on("Thigh band", th, cf, 0.45, 0.1, 0.012, ACCENT, "thigh_" + L)
    for k in range(4):
        ring_on("Knee rib", cf, ft, -0.05 + k * 0.035, 0.09, 0.012, JOINT, "calf_" + L)
    ring_on("Boot ring", cf, ft, 0.74, 0.085, 0.013, TRIM, "calf_" + L)
    # boot: shaft round the ankle, foot block, gunmetal sole, amber toe light
    piece("Boot shaft", lathe([(0.075, ft.z - 0.03), (0.08, ft.z + 0.05), (0.078, ft.z + 0.14)], (ft.x, ft.y - 0.02 * FWD, 0), 28), BOOT, "foot_" + L)
    piece("Boot", rbox((0.13, 0.29, 0.1), Vector((ft.x, ft.y + 0.06 * FWD, 0.06)), 0.045, 4), BOOT, "foot_" + L)
    piece("Sole", rbox((0.14, 0.305, 0.03), Vector((ft.x, ft.y + 0.06 * FWD, 0.015)), 0.012, 2), TRIM, "foot_" + L)
    piece("Toe light", rbox((0.07, 0.006, 0.01), Vector((ft.x, ft.y + 0.212 * FWD, 0.03)), 0.002, 1), AMBER, "foot_" + L)

# ---- preview pose: arms down at the sides (the rig rests in a T-pose)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
for L, s in (("l", 1), ("r", -1)):
    pb = arm.pose.bones["upperarm_" + L]; pb.rotation_mode = "XYZ"
    pb.rotation_euler = (0, 0, 0)
    # rotate about the world forward axis so the arm swings down to the side
    a = bone("upperarm_" + L); d = (bone("lowerarm_" + L) - a).normalized()
    target = Vector((0.12 * s, 0.0, -1)).normalized()
    q = d.rotation_difference(target)
    rest = arm.data.bones["upperarm_" + L].matrix_local.to_3x3()
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()
    fb = arm.pose.bones["lowerarm_" + L]; fb.rotation_mode = "XYZ"; fb.rotation_euler = (0, 0, 0)
bpy.ops.object.mode_set(mode="OBJECT")
print("FWD", FWD, "parts", len([o for o in bpy.data.objects if o.parent == arm]))
