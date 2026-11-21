"""
Retargets the astronaut clips onto the NASA-suit skeleton in Blender, so the game plays the
original Quaternius motion (Universal Animation Library, CC0) as authored instead of through a
second retarget in Unity, then exports Assets/Resources/Astronaut/Astronaut_Clips.fbx on that
skeleton (Unity's Humanoid round trip between identical skeletons is then near exact).

Each target bone takes the source bone's armature-space rotation, offset so that the two rest
poses line up bone for bone (the source is in a T-pose, the suit in its own pose): the bone
points where the source bone points, with the source's twist. Only the pelvis moves, by the
source's pelvis offset scaled to the suit's hip height. Curves then get a light smoothing pass
(loop-aware for the cycles) so the steps read smooth rather than snappy.

Run inside Blender (see run() at the bottom): astronaut_build.build() makes the source rig with
its clips and the hand-made Helmet_On, astronaut_nasa.build_character() makes the target.
"""
import bpy, os
from mathutils import Matrix, Vector, Quaternion

TOOLS = "/Users/andrewhuangjin/Documents/code/nasa/tools/blender/"
EXPORT = "/Users/andrewhuangjin/Documents/code/nasa/My project/Assets/Resources/Astronaut"
LOOPS = {"Idle", "Walk", "Run", "Sprint", "Fall"}
CLIPS = ["Idle", "Walk", "Run", "Sprint", "Fall", "Jump_Start", "Jump_Land", "Helmet_On"]
KERNEL = [1, 4, 6, 4, 1]        # binomial smoothing, one pass
ARM_OUT = 0.1                   # extra outward swing of the upper arms (radians) to clear the suit


def order(arm):
    out = []
    def walk(b):
        out.append(b.name)
        for c in b.children: walk(c)
    for b in arm.data.bones:
        if b.parent is None: walk(b)
    return out


def offsets(src, tgt):
    """Per bone: the rotation that maps the source's posed frame onto the target's frame."""
    O = {}
    for name in order(tgt):
        rs = src.data.bones[name].matrix_local.to_3x3().normalized()
        rt = tgt.data.bones[name].matrix_local.to_3x3().normalized()
        ds, dt = rs.col[1].normalized(), rt.col[1].normalized()
        align = ds.rotation_difference(dt).to_matrix()
        O[name] = (align @ rs).inverted() @ rt
    return O


def sample(src, tgt, action, O, use):
    """Target basis (loc, quat) per bone per frame."""
    scene = bpy.context.scene
    use(src, action)
    f0, f1 = int(action.frame_range[0]), int(action.frame_range[1])
    names = order(tgt)
    rest = {n: tgt.data.bones[n].matrix_local.copy() for n in names}
    srest_pelvis = src.data.bones["pelvis"].head_local.copy()
    ratio = tgt.data.bones["pelvis"].head_local.z / srest_pelvis.z
    frames = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        P = {}
        basis = {}
        for n in names:
            sp = src.pose.bones[n].matrix
            rot = sp.to_3x3().normalized() @ O[n]
            if ARM_OUT and n.startswith("upperarm_"):
                side = 1 if n.endswith("_l") else -1
                rot = Matrix.Rotation(-ARM_OUT * side, 3, "Y") @ rot
            b = tgt.data.bones[n]
            if b.parent is None:
                pos = rest[n].translation + (sp.translation - srest_pelvis) * ratio
            else:
                pos = (P[b.parent.name] @ rest[b.parent.name].inverted() @ rest[n]).translation
            M = Matrix.Translation(pos) @ rot.to_4x4()
            P[n] = M
            if b.parent is None:
                B = rest[n].inverted() @ M
            else:
                B = (rest[b.parent.name].inverted() @ rest[n]).inverted() @ P[b.parent.name].inverted() @ M
            basis[n] = (B.translation.copy(), B.to_quaternion())
        frames.append(basis)
    return frames


def smooth(frames, loop):
    n = len(frames)
    cycle = n - 1 if loop else n          # a loop's last frame repeats its first
    out = []
    half = len(KERNEL) // 2
    for i in range(n):
        if loop and i == n - 1:
            out.append(out[0]); continue
        basis = {}
        for name in frames[i]:
            q0 = frames[i][name][1]
            acc_q = Vector((0, 0, 0, 0)); acc_l = Vector(); tot = 0
            for k, w in enumerate(KERNEL):
                j = i + k - half
                j = j % cycle if loop else min(max(j, 0), n - 1)
                l, q = frames[j][name]
                if q.dot(q0) < 0: q = -q
                acc_q += Vector(q) * w; acc_l += l * w; tot += w
            q = Quaternion(acc_q / tot); q.normalize()
            basis[name] = (acc_l / tot, q)
        out.append(basis)
    return out


def bake(tgt, name, frames, use):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    tgt.animation_data_create(); use(tgt, act)
    for pb in tgt.pose.bones: pb.rotation_mode = "QUATERNION"
    for f, basis in enumerate(frames):
        for n, (l, q) in basis.items():
            pb = tgt.pose.bones[n]
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if tgt.data.bones[n].parent is None:
                pb.location = l
                pb.keyframe_insert("location", frame=f)
    use(tgt, None)
    return act


def retarget_all(src, tgt, use, smooth_on=True):
    bpy.context.scene.render.fps = 30
    for a in list(bpy.data.actions):
        if a.name in CLIPS: a.name = "SRC_" + a.name
    O = offsets(src, tgt)
    made = []
    for name in CLIPS:
        frames = sample(src, tgt, bpy.data.actions["SRC_" + name], O, use)
        if smooth_on: frames = smooth(frames, name in LOOPS)
        made.append(bake(tgt, name, frames, use))
    use(src, None)
    return made


def export_clips(tgt, use):
    use(tgt, None)
    for pb in tgt.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
    for o in list(bpy.data.objects):
        if o.name == "SRC": bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.name.startswith("SRC_") or a.name not in CLIPS: bpy.data.actions.remove(a)
    bpy.ops.object.select_all(action="DESELECT"); tgt.select_set(True); bpy.context.view_layer.objects.active = tgt
    path = os.path.join(EXPORT, "Astronaut_Clips.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    return path


def setup(kind="Male"):
    """Source rig with clips (named SRC) plus the NASA character (named TGT) in one scene."""
    g = {}
    exec(open(TOOLS + "astronaut_build.py").read(), g)
    src, fwd, _ = g["build"]("Male", do_export=False)
    for o in list(bpy.data.objects):
        if o is not src: bpy.data.objects.remove(o, do_unlink=True)
    src.name = "SRC"
    n = {}
    exec(open(TOOLS + "astronaut_nasa.py").read(), n)
    def keep_wipe():
        if bpy.context.object and bpy.context.object.mode != "OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
        for o in list(bpy.data.objects):
            if o.name != "SRC": bpy.data.objects.remove(o, do_unlink=True)
        for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
            for b in list(blk): blk.remove(b)
        for a in list(bpy.data.armatures):
            if a != src.data: bpy.data.armatures.remove(a)
    n["wipe"] = keep_wipe
    tgt, helmet = n["build_character"](kind)
    return src, tgt, g["use"], n


def run():
    """Exports both characters (with the fixed rig) and the retargeted clips."""
    for kind in ("Male", "Female"):
        src, tgt, use, n = setup(kind)
        n["export_character"](tgt, kind)
    retarget_all(src, tgt, use)
    return export_clips(tgt, use)
