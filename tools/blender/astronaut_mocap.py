"""
Real motion capture for the astronaut: CMU Graphics Lab Motion Capture Database
(mocap.cs.cmu.edu; "free for use in research and commercial projects"), BVH conversion by
Bruce Hahne (github.com/meshula/cmu-mocap): a "stiff walk" (subject 74), a jog, run and jump
(subject 16) and standing still (subject 82). Every clip then goes through a suit filter (arms held
out and stiff, back upright), so it reads as an astronaut in a pressurised suit rather than a
person in street clothes.

Each take is imported, cut to one clean cycle (left and right step for the walk, run and
sprint), turned to face -Y, put in place (the forward travel removed, the hip bob kept), copied
bone for bone onto the NASA suit's skeleton (each bone points where the performer's bone points,
the two rest poses lined up), closed into a seamless loop, lightly smoothed and resampled from
120 to 30 fps. The result is exported as Assets/Resources/Astronaut/Astronaut_Clips.fbx.

Run inside Blender:
    exec(open(".../astronaut_mocap.py").read()); run()
"""
import bpy, os, math
from mathutils import Matrix, Vector, Quaternion

TOOLS = "/Users/andrewhuangjin/Documents/code/nasa/tools/blender/"
EXPORT = "/Users/andrewhuangjin/Documents/code/nasa/My project/Assets/Resources/Astronaut"
BVH = "/private/tmp/claude-501/-Users-andrewhuangjin-Documents-code-nasa/dde12ac5-72ac-40e4-ac25-0e1f0d275ab6/scratchpad/cmu/"

# target bone <- CMU bone
MAP = {"pelvis": "Hips", "spine_01": "LowerBack", "spine_02": "Spine", "spine_03": "Spine1",
       "neck_01": "Neck", "Head": "Head"}
for _s, _S in (("l", "Left"), ("r", "Right")):
    MAP.update({"clavicle_" + _s: _S + "Shoulder", "upperarm_" + _s: _S + "Arm", "lowerarm_" + _s: _S + "ForeArm",
                "hand_" + _s: _S + "Hand", "thigh_" + _s: _S + "UpLeg", "calf_" + _s: _S + "Leg",
                "foot_" + _s: _S + "Foot", "ball_" + _s: _S + "ToeBase"})

# clip: (take, kind, frame hint). kind "cycle": one gait cycle found near the hint;
# "hold": a stretch [a, b] looped by blending its end into its start; "pingpong": [a, b] there and back.
CLIPS = {
    "Idle":   ("82_08", "hold", (100, 700)),
    "Walk":   ("74_01", "cycle", 200),   # "stiff walk": short, deliberate steps with a side-to-side sway, as in a suit
    "Run":    ("16_35", "cycle", 60),
    "Sprint": ("16_55", "cycle", 70),
    "Fall":   ("16_01", "air", None),
}
STEP = 4                      # 120 fps -> 30 fps
KERNEL = [1, 2, 3, 2, 1]      # light smoothing at 120 fps (removes marker jitter, keeps the motion)
# Suit filter: a pressurised EVA suit holds the arms out from the body with the elbows bent and
# lets them swing only a little; the hard upper torso keeps the back upright and barely twists.
SUIT_ARM_KEEP = 0.35          # share of the performer's own arm swing kept
SUIT_UPRIGHT = 0.5            # how far the spine and neck are pulled back toward upright
SUIT_ARM = {"upperarm": Vector((0.36, -0.12, -0.92)), "lowerarm": Vector((0.2, -0.6, -0.77))}   # left side; -Y is forward
SPINE = ("spine_01", "spine_02", "spine_03", "neck_01")


def suit(name, rot, rest_rot):
    """The performer's bone rotation as worn in a pressurised suit (see SUIT_*)."""
    if name in SPINE:
        q, r = rot.to_quaternion(), rest_rot.to_quaternion()
        if q.dot(r) < 0: r.negate()
        return q.slerp(r, SUIT_UPRIGHT).to_matrix()
    part = name.split("_")[0]
    if part in SUIT_ARM:
        side = 1 if name.endswith("_l") else -1
        held = SUIT_ARM[part].copy(); held.x *= side
        d = rot.col[1].normalized()
        want = held.normalized().lerp(d, SUIT_ARM_KEEP).normalized()
        return d.rotation_difference(want).to_matrix() @ rot
    return rot


def order(arm):
    out = []
    def walk(b):
        out.append(b.name)
        for c in b.children: walk(c)
    for b in arm.data.bones:
        if b.parent is None: walk(b)
    return out


def load(take):
    before = set(bpy.data.objects)
    bpy.ops.import_anim.bvh(filepath=BVH + take + ".bvh", global_scale=1.0, rotate_mode="NATIVE",
                            axis_forward="-Z", axis_up="Y", update_scene_fps=False, update_scene_duration=False)
    src = [o for o in bpy.data.objects if o not in before][0]
    src.name = "SRC_" + take
    return src


def world(src, f):
    bpy.context.scene.frame_set(f)
    return {b: src.matrix_world @ src.pose.bones[b].matrix for b in set(MAP.values()) | {"LeftUpLeg", "RightUpLeg"}}


def offsets(src, tgt):
    O = {}
    for t, s in MAP.items():
        rs = (src.matrix_world @ src.data.bones[s].matrix_local).to_3x3().normalized()
        rt = tgt.data.bones[t].matrix_local.to_3x3().normalized()
        ds, dt = rs.col[1].normalized(), rt.col[1].normalized()
        O[t] = (ds.rotation_difference(dt).to_matrix() @ rs).inverted() @ rt
    return O


def cycle_range(src, hint):
    """Frames [a, b] of one full gait cycle: from one left-foot-forward peak to the next but one
    in the left foot's forward reach (relative to the hips, along the travel)."""
    f0, f1 = map(int, src.animation_data.action.frame_range); f0 += 2   # frame 1 is the T-pose
    hips = [world(src, f)["Hips"].translation.copy() for f in range(f0, f1 + 1)]
    travel = (hips[-1] - hips[0]); travel.z = 0; travel.normalize()
    sig = []
    for i, f in enumerate(range(f0, f1 + 1)):
        w = world(src, f)
        sig.append((w["LeftFoot"].translation - w["RightFoot"].translation).dot(travel))
    pairs = []
    for sg in (sig, [-x for x in sig]):     # left foot furthest ahead, or furthest behind
        top = max(sg[2:])
        peaks = [i for i in range(6, len(sg) - 6) if sg[i] == max(sg[i - 6:i + 7]) and sg[i] > 0.5 * top]
        pairs += [(p, min(q for q in peaks if q > p + 30)) for p in peaks if any(q > p + 30 for q in peaks)]
    start, end = min(pairs, key=lambda pq: abs(pq[0] + f0 - hint))
    return f0 + start, f0 + end


def air_range(src):
    """The airborne stretch of a jump: both feet off the ground."""
    f0, f1 = map(int, src.animation_data.action.frame_range); f0 += 2   # frame 1 is the T-pose
    toes = []
    for f in range(f0, f1 + 1):
        w = world(src, f)
        toes.append(min(w["LeftToeBase"].translation.z, w["RightToeBase"].translation.z))
    ground = min(toes)
    air = [i for i, z in enumerate(toes) if z > ground + 1.2]
    return f0 + air[0], f0 + air[-1], ground


def sample(src, tgt, O, a, b, ground=None, flat=False):
    """Target basis (loc, quat) per bone for frames a..b (inclusive), in place, facing -Y."""
    frames = list(range(a, b + 1))
    W = [world(src, f) for f in frames]
    if ground is None:
        ground = min(min(w["LeftToeBase"].translation.z, w["RightToeBase"].translation.z) for w in W) - 0.45
    # facing: the performer's left (hip line) -> +X; travel removed linearly over the stretch
    lat = sum(((w["LeftUpLeg"].translation - w["RightUpLeg"].translation) for w in W), Vector()); lat.z = 0
    yaw = Matrix.Rotation(-math.atan2(lat.y, lat.x), 3, "Z")
    h0, h1 = W[0]["Hips"].translation, W[-1]["Hips"].translation
    rest_hip = src.data.bones["Hips"].head_local.z - min(src.data.bones["LeftToeBase"].head_local.z,
                                                          src.data.bones["RightToeBase"].head_local.z) + 0.45
    scale = tgt.data.bones["pelvis"].head_local.z / rest_hip
    names = order(tgt)
    rest = {n: tgt.data.bones[n].matrix_local.copy() for n in names}
    mean_z = sum(w["Hips"].translation.z for w in W) / len(W)
    # Feet: performers' skeletons hold the foot at different rest angles, so a planted foot can come
    # out tilted up. Turn both feet so the planted foot (the lowest third of the frames) sits at the
    # suit's own rest angle; the toes then just follow the foot.
    def pitch(m3): d = m3.col[1]; return math.atan2(d.z, -d.y)
    planted = sorted(W, key=lambda w: w["LeftFoot"].translation.z)[:max(1, len(W) // 3)]
    seen = sorted(pitch(yaw @ w["LeftFoot"].to_3x3().normalized() @ O["foot_l"]) for w in planted)
    foot_fix = Matrix.Rotation(seen[len(seen) // 2] - pitch(rest["foot_l"].to_3x3()), 3, "X")
    out = []
    for i, w in enumerate(W):
        t = i / max(1, len(W) - 1)
        drift = h0.lerp(h1, t)
        P, basis = {}, {}
        for n in names:
            bone = tgt.data.bones[n]
            if n.startswith("ball_"):
                rot = P[bone.parent.name].to_3x3() @ (rest[bone.parent.name].inverted() @ rest[n]).to_3x3()
            else:
                rot = suit(n, yaw @ w[MAP[n]].to_3x3().normalized() @ O[n], rest[n].to_3x3().normalized())
                if n.startswith("foot_"): rot = foot_fix @ rot
            if bone.parent is None:
                d = w["Hips"].translation - drift
                d = yaw @ Vector((d.x, d.y, 0))
                # airborne clips: the game's jump arc does the height, the clip only its own small bob
                lift = (w["Hips"].translation.z - mean_z) if flat else (w["Hips"].translation.z - ground) - rest_hip
                pos = rest[n].translation + Vector((d.x, d.y, lift)) * scale
            else:
                pos = (P[bone.parent.name] @ rest[bone.parent.name].inverted() @ rest[n]).translation
            M = Matrix.Translation(pos) @ rot.to_4x4()
            P[n] = M
            B = rest[n].inverted() @ M if bone.parent is None else \
                (rest[bone.parent.name].inverted() @ rest[n]).inverted() @ P[bone.parent.name].inverted() @ M
            basis[n] = (B.translation.copy(), B.to_quaternion())
        out.append(basis)
    return out


def close_loop(frames):
    """Spreads the end-to-start mismatch over the clip so the last frame equals the first."""
    n = len(frames) - 1
    first, last = frames[0], frames[-1]
    out = []
    for i, basis in enumerate(frames):
        t = i / n
        nb = {}
        for k, (l, q) in basis.items():
            ql = last[k][1]
            if ql.dot(first[k][1]) < 0: ql = -ql
            corr = ql.rotation_difference(first[k][1])
            nb[k] = (l + (first[k][0] - last[k][0]) * t, (Quaternion().slerp(corr, t) @ q).normalized())
        out.append(nb)
    return out


def smooth(frames, loop):
    n = len(frames); cycle = n - 1 if loop else n; half = len(KERNEL) // 2
    out = []
    for i in range(n):
        if loop and i == n - 1: out.append(out[0]); continue
        nb = {}
        for k in frames[i]:
            q0 = frames[i][k][1]; aq = Vector((0, 0, 0, 0)); al = Vector(); tot = 0
            for j, w in enumerate(KERNEL):
                m = i + j - half
                m = m % cycle if loop else min(max(m, 0), n - 1)
                l, q = frames[m][k]
                if q.dot(q0) < 0: q = -q
                aq += Vector(q) * w; al += l * w; tot += w
            q = Quaternion(aq / tot); q.normalize(); nb[k] = (al / tot, q)
        out.append(nb)
    return out


def bake(tgt, name, frames, use):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    tgt.animation_data_create(); use(tgt, act)
    for pb in tgt.pose.bones: pb.rotation_mode = "QUATERNION"
    for f, basis in enumerate(frames[::STEP] if (len(frames) - 1) % STEP == 0 else frames[::STEP] + [frames[-1]]):
        for n, (l, q) in basis.items():
            pb = tgt.pose.bones[n]
            pb.rotation_quaternion = q; pb.keyframe_insert("rotation_quaternion", frame=f)
            if tgt.data.bones[n].parent is None:
                pb.location = l; pb.keyframe_insert("location", frame=f)
    use(tgt, None)
    return act


def use(arm, action):
    arm.animation_data_create()
    arm.animation_data.action = action
    if action is not None and hasattr(arm.animation_data, "action_slot") and len(getattr(action, "slots", [])):
        arm.animation_data.action_slot = action.slots[0]


def make_clip(tgt, name):
    take, kind, hint = CLIPS[name]
    src = load(take)
    O = offsets(src, tgt)
    if kind == "cycle":
        a, b = cycle_range(src, hint)
        frames = smooth(close_loop(sample(src, tgt, O, a, b)), True)
    elif kind == "hold":
        a, b = hint
        frames = smooth(close_loop(sample(src, tgt, O, a, b)), True)
    else:   # air: the rise to the apex and back, looped there and back
        a, b, ground = air_range(src)
        mid = (a + b) // 2
        part = sample(src, tgt, O, mid - 12, mid + 12, ground - 0.45, flat=True)
        frames = smooth(part + part[-2:0:-1] + [part[0]], True)
    act = bake(tgt, name, frames, use)
    print(name, take, kind, a, b, "->", len(frames), "frames at 120 fps")
    bpy.data.objects.remove(src, do_unlink=True)
    return act


def export_clips(tgt):
    use(tgt, None)
    for pb in tgt.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
    for a in list(bpy.data.actions):
        if a.name not in CLIPS: bpy.data.actions.remove(a)
    bpy.ops.object.select_all(action="DESELECT"); tgt.select_set(True); bpy.context.view_layer.objects.active = tgt
    path = os.path.join(EXPORT, "Astronaut_Clips.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    return path


def run(export=True):
    bpy.context.scene.render.fps = 30
    n = {}
    exec(open(TOOLS + "astronaut_nasa.py").read(), n)
    tgt = n["build_astronaut"]()
    if export: n["export_astronaut"](tgt)
    for name in CLIPS: make_clip(tgt, name)
    return (export_clips(tgt) if export else None), tgt
