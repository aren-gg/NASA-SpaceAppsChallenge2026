"""
Builds the animation clips for the playable astronauts (Assets/Resources/Astronaut/Astronaut_Clips.fbx).

The playable bodies are the NASA-suit astronauts from astronaut_nasa.py. This script sets up the
source clips on the Quaternius base rig; astronaut_retarget.py copies them onto the suit's
skeleton and exports Astronaut_Clips.fbx (export_clips() here is the older, un-retargeted path).

History: this script first built realistic astronaut bodies on the Quaternius base characters
(build(); still used here to set up the rig and the hand-made Helmet_On clip).

Sources (all CC0, by Quaternius): Universal Base Characters (bodies, faces, hair) and the
Universal Animation Library (walk, jog, sprint, jump and idle clips on the same 65-bone rig).
The suit is astronaut_from_base.py; this script adds the hair styles, the clips and a
hand-made "Helmet_On" clip (both hands bring the helmet from chest height up and down onto
the head), then exports Assets/Resources/Astronaut/Astronaut_<Sex>.fbx.

Run inside Blender: exec(open(".../astronaut_build.py").read()); build("Male"); build("Female")
"""
import bpy, math, os
from mathutils import Vector, Matrix

DL = "/private/tmp/claude-501/-Users-andrewhuangjin-Documents-code-nasa/dde12ac5-72ac-40e4-ac25-0e1f0d275ab6/scratchpad/dl/x"
BODY = DL + "/Universal Base Characters[Standard]/Base Characters/Godot - UE/Superhero_{}_FullBody.gltf"
HAIR_DIR = DL + "/Universal Base Characters[Standard]/Hairstyles/Origin at 0/glTF (Godot)/"
ANIMS = DL + "/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb"
TOOLS = "/Users/andrewhuangjin/Documents/code/nasa/tools/blender/"
EXPORT = "/Users/andrewhuangjin/Documents/code/nasa/My project/Assets/Resources/Astronaut"

# Game hair style -> pack hair mesh (MarsSurvival AstronautLook.HairStyles, same order).
HAIRS = ["SimpleParted", "Buzzed", "BuzzedFemale", "Buns", "Long"]
CLIPS = {"Idle_Loop": "Idle", "Walk_Loop": "Walk", "Jog_Fwd_Loop": "Run", "Sprint_Loop": "Sprint",
         "Jump_Start": "Jump_Start", "Jump_Loop": "Fall", "Jump_Land": "Jump_Land"}

def use(arm, action):
    """Assign an action (Blender 4.4+ also needs the action's slot)."""
    arm.animation_data.action = action
    if action is not None and hasattr(arm.animation_data, "action_slot") and len(getattr(action, "slots", [])):
        arm.animation_data.action_slot = action.slots[0]

def wipe():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.lights,
                bpy.data.cameras, bpy.data.images, bpy.data.curves):
        for b in list(blk): blk.remove(b)
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)

def import_new(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

def add_hair(arm):
    head = arm.data.bones["Head"]
    for style in HAIRS:
        for o in import_new(HAIR_DIR + "Hair_" + style + ".gltf"):
            if o.type != "MESH":
                bpy.data.objects.remove(o, do_unlink=True); continue
            mw = o.matrix_world.copy()
            o.name = "Hair_" + style
            o.data.materials.clear(); o.data.materials.append(bpy.data.materials["Hair"])
            for p in o.data.polygons: p.use_smooth = True
            o.parent = arm; o.parent_type = "BONE"; o.parent_bone = "Head"; o.matrix_world = mw

def import_clips(arm):
    objs = import_new(ANIMS)
    for o in objs: bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.name in CLIPS:
            a.name = CLIPS[a.name]; a.use_fake_user = True
        elif a.name not in CLIPS.values():
            bpy.data.actions.remove(a)
    arm.animation_data_create()

def helmet_on_clip(arm, fwd):
    """Hands lift the helmet from chest height, over the head and down onto it (2 s at 30 fps)."""
    scene = bpy.context.scene; scene.render.fps = 30
    pose = arm.pose
    base = bpy.data.actions["Idle"].copy(); base.name = "Helmet_Base"
    use(arm, base)
    hb = arm.data.bones["helmet"]
    rest = arm.matrix_world @ hb.head_local
    # Hand targets ride on the helmet bone; elbows bend out and down.
    targets = {}
    for side, x in (("l", 1), ("r", -1)):
        e = bpy.data.objects.new("hand_target_" + side, None); bpy.context.scene.collection.objects.link(e)
        e.parent = arm; e.parent_type = "BONE"; e.parent_bone = "helmet"
        e.matrix_world = Matrix.Translation(rest + Vector((0.21 * x, 0, -0.02)))
        pole = bpy.data.objects.new("elbow_pole_" + side, None); bpy.context.scene.collection.objects.link(pole)
        pole.location = rest + Vector((0.6 * x, 0.1 * fwd, -0.5))
        c = pose.bones["lowerarm_" + side].constraints.new("IK")
        c.target = e; c.pole_target = pole; c.chain_count = 2; c.pole_angle = math.radians(-90 if x > 0 else -90)
        targets[side] = c
    # Helmet path (armature-space offsets from its place on the head).
    keys = [(0, Vector((0, 0.32 * fwd, -0.55))), (22, Vector((0, 0.12 * fwd, 0.22))), (40, Vector((0, 0.0, 0.03))),
            (48, Vector((0, 0, 0))), (60, Vector((0, 0, 0)))]
    pb = pose.bones["helmet"]
    for f, off in keys:
        scene.frame_set(f)
        m = pb.matrix.copy(); m.translation = arm.matrix_world.inverted() @ (rest + off)
        pb.matrix = m
        pb.keyframe_insert("location", frame=f)
        for side in targets:
            targets[side].influence = 1.0 if f <= 48 else 0.0
            targets[side].keyframe_insert("influence", frame=f)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.nla.bake(frame_start=0, frame_end=60, only_selected=False, visual_keying=True,
                     clear_constraints=True, use_current_action=False, bake_types={"POSE"})
    bpy.ops.object.mode_set(mode="OBJECT")
    a = arm.animation_data.action; a.name = "Helmet_On"; a.use_fake_user = True
    bpy.data.actions.remove(base)
    for o in list(bpy.data.objects):
        if o.type == "EMPTY": bpy.data.objects.remove(o, do_unlink=True)
    # The pack clips must not move the helmet bone: it has no keys there, so it stays on the head.

def export(arm, sex):
    os.makedirs(EXPORT, exist_ok=True)
    use(arm, None)
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if o == arm or o.parent == arm: o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT, "Astronaut_" + sex + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                             add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.5)
    return path

def build(sex, do_export=True):
    wipe()
    import_new(BODY.format(sex))
    g = {}
    exec(open(TOOLS + "astronaut_from_base.py").read(), g)
    arm, fwd = g["arm"], g["FWD"]
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    add_hair(arm)
    import_clips(arm)
    helmet_on_clip(arm, fwd)
    path = export(arm, sex) if do_export else None
    return arm, fwd, path


def export_clips():
    """Exports only the armature and its eight clips as Astronaut_Clips.fbx."""
    arm, fwd, _ = build("Male", do_export=False)
    for o in list(bpy.data.objects):
        if o is not arm: bpy.data.objects.remove(o, do_unlink=True)
    use(arm, None)
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT"); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT, "Astronaut_Clips.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    return path
