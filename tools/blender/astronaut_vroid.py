"""
Cute astronauts: VRoid Studio's official sample characters (CC0, HairSample_Male / HairSample_Female
from opengameart.org/content/vroid-studio-cc0-models) as the Korean boy and Korean girl, dressed in
the NASA-punk EVA suit of the outpost modules.

- clothing becomes the white suit cover layer (boy's hood removed), shoes become boots
- the girl's cat ears are removed; both get dark brown eyes
- suit hardware is bone-parented to the VRM skeleton (J_Bip_*); the helmet sits under one
  "Helmet" node on the head so the game can carry it from the hands onto the head
Exports Assets/Resources/Astronaut/Astronaut_<Boy|Girl>.fbx with embedded textures (Unity: Humanoid).
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

SRC = "/private/tmp/claude-501/-Users-andrewhuangjin-Documents-code-nasa/dde12ac5-72ac-40e4-ac25-0e1f0d275ab6/scratchpad/vroid/"
TOOLS = "/Users/andrewhuangjin/Documents/code/nasa/tools/blender/"
EXPORT = "/Users/andrewhuangjin/Documents/code/nasa/My project/Assets/Resources/Astronaut"
B = {"head": "J_Bip_C_Head", "neck": "J_Bip_C_Neck", "chest": "J_Bip_C_UpperChest", "spine": "J_Bip_C_Spine",
     "hips": "J_Bip_C_Hips"}
for s, S in (("l", "L"), ("r", "R")):
    for k, v in (("upperarm", "UpperArm"), ("lowerarm", "LowerArm"), ("hand", "Hand"), ("thigh", "UpperLeg"),
                 ("calf", "LowerLeg"), ("foot", "Foot"), ("toe", "ToeBase")):
        B[k + "_" + s] = "J_Bip_%s_%s" % (S, v)

def wipe():
    if bpy.context.object and bpy.context.object.mode != "OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.images,
                bpy.data.lights, bpy.data.cameras):
        for b in list(blk): blk.remove(b)
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)

def build(kind):
    """kind: 'Boy' or 'Girl'."""
    wipe()
    bpy.ops.import_scene.gltf(filepath=SRC + ("male" if kind == "Boy" else "female") + ".glb")
    g = {}
    exec(open(TOOLS + "astronaut_kit.py").read(), g)
    SUIT, JOINT, ACCENT, TRIM, GLOVE, BOOT, VISOR, PACK, SCREEN, LAMP, CYAN, AMBER = (g[n] for n in (
        "SUIT", "JOINT", "ACCENT", "TRIM", "GLOVE", "BOOT", "VISOR", "PACK", "SCREEN", "LAMP", "CYAN", "AMBER"))
    sphere, torus, rbox, lathe, obj = g["sphere"], g["torus"], g["rbox"], g["lathe"], g["obj"]

    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    arm.name = "Armature"
    for o in list(bpy.data.objects):
        if o.name.startswith("Icosphere"): bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    body = next(o for o in meshes if o.name.startswith("Body")); body.name = "Body"
    face = next(o for o in meshes if o.name.startswith("Face")); face.name = "Face"
    hair = next(o for o in meshes if o.name.startswith("Hair")); hair.name = "Hair"

    def bone(k): return arm.matrix_world @ arm.data.bones[B.get(k, k)].head_local
    def tail(k): return arm.matrix_world @ arm.data.bones[B.get(k, k)].tail_local
    FWD = 1.0 if (bone("toe_l") - bone("foot_l")).y > 0 else -1.0
    F = Vector((0, FWD, 0))
    head = bone("head")

    # --- cat ears off (separate islands sitting high on the sides of the head)
    if kind == "Girl":
        bm = bmesh.new(); bm.from_mesh(hair.data); bm.verts.ensure_lookup_table()
        mw = hair.matrix_world; seen = set(); kill = []
        for v in bm.verts:
            if v in seen: continue
            island, stack = [], [v]
            while stack:
                x = stack.pop()
                if x in seen: continue
                seen.add(x); island.append(x)
                stack.extend(e.other_vert(x) for e in x.link_edges)
            ps = [mw @ x.co for x in island]
            cz = sum(p.z for p in ps) / len(ps); cx = sum(p.x for p in ps) / len(ps)
            if cz > head.z + 0.17 and abs(cx - head.x) > 0.06 and len(island) < 1500:
                kill.extend(island)
        bmesh.ops.delete(bm, geom=kill, context="VERTS"); bm.to_mesh(hair.data); bm.free()
        print("ear verts removed", len(kill))

    # --- hood off (boy): vertices driven by the hood bones
    hood = [vg.index for vg in body.vertex_groups if "Hood" in vg.name]
    if hood:
        bm = bmesh.new(); bm.from_mesh(body.data)
        dl = bm.verts.layers.deform.active
        kill = [v for v in bm.verts if sum(v[dl].get(i, 0) for i in hood) > 0.35]
        bmesh.ops.delete(bm, geom=kill, context="VERTS"); bm.to_mesh(body.data); bm.free()
        print("hood verts removed", len(kill))

    # --- the EVA suit: an original fitted suit (not the character's clothes). A smooth body is grown
    # along the skeleton with Blender's Skin modifier, sized from the character's own measurements
    # (legs below any skirt, arms, torso), weighted to the J_Bip bones, and colour-blocked in the
    # VRoid way: white panels, mint side stripes and yoke, lavender knee and elbow pads.
    src = [body.matrix_world @ v.co for v in body.data.vertices]
    def slice_radii(p, axis, cutoff, band=0.03):
        """(x, y) half-extents of the body around point p across a slice normal to axis."""
        xs, ys, rs = [], [], []
        for v in src:
            w = v - p; along = w.dot(axis)
            if abs(along) < band:
                q = w - axis * along
                if q.length < cutoff: xs.append(abs(q.x)); ys.append(abs(q.y)); rs.append(q.length)
        if not rs: return (0.05, 0.05)
        xs.sort(); ys.sort(); rs.sort()
        if cutoff < 0.15:                                   # a limb: its typical radius, not the far side of the slice
            r = rs[int(len(rs) * 0.6)]
            return (r, r)
        return (xs[int(len(xs) * 0.9)], ys[int(len(ys) * 0.9)])
    J = {k: bone(k) for k in ("hips", "spine", "chest", "neck", "upperarm_l", "lowerarm_l", "hand_l", "upperarm_r",
                                "lowerarm_r", "hand_r", "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r")}
    J["upperchest"] = arm.matrix_world @ arm.data.bones["J_Bip_C_UpperChest"].head_local
    spacing = (J["thigh_l"] - J["thigh_r"]).length
    leg_cap = 0.43 * spacing                                      # a gap between the legs from the front
    # Pressurised-suit bulk: limbs at least this thick (metres, scaled to the character's height),
    # so the suit reads puffy and cute rather than skin-tight.
    hs = J["neck"].z / 1.45
    BULK = {"shoulder": 0.062 * hs, "elbow": 0.054 * hs, "wrist": 0.047 * hs,
            "hip": 0.084 * hs, "midthigh": 0.077 * hs, "knee": 0.067 * hs, "ankle": 0.06 * hs}
    up = Vector((0, 0, 1))
    nodes, radii, edges = [], [], []
    leg_tops = []
    def node(p, r):
        nodes.append(p.copy()); radii.append(r); return len(nodes) - 1
    def seg_dir(a, b): return (J[b] - J[a]).normalized()
    # torso chain (wide, flattened later), with an extra node mid-thigh-top for the hips
    def trunk(p, lo_w):
        rx, ry = slice_radii(p, up, 0.24)
        return (max(rx, lo_w), max(ry, lo_w * 0.7))
    shoulder_w = abs(J["upperarm_l"].x - J["upperarm_r"].x) / 2
    t0 = node(J["hips"] + Vector((0, 0, -0.08)), trunk(J["hips"] + Vector((0, 0, -0.06)), shoulder_w * 0.95))
    t1 = node(J["spine"], trunk(J["spine"], shoulder_w * 0.85))
    t2 = node(J["chest"], trunk(J["chest"], shoulder_w * 0.95))
    t3 = node(J["upperchest"], trunk(J["upperchest"], shoulder_w * 1.0))
    t4 = node(J["neck"] + Vector((0, 0, 0.02)), (0.055, 0.055))
    edges += [(t0, t1), (t1, t2), (t2, t3), (t3, t4)]
    for s in ("l", "r"):
        ax = seg_dir("upperarm_" + s, "lowerarm_" + s)
        sh = node(J["upperarm_" + s], (max(BULK["shoulder"], slice_radii(J["upperarm_" + s] + ax * 0.04, ax, 0.09)[0] + 0.024),) * 2)
        el = node(J["lowerarm_" + s], (max(BULK["elbow"], slice_radii(J["lowerarm_" + s], ax, 0.08)[0] + 0.02),) * 2)
        ax2 = seg_dir("lowerarm_" + s, "hand_" + s)
        wr = node(J["hand_" + s] - ax2 * 0.01, (max(BULK["wrist"], slice_radii(J["hand_" + s] - ax2 * 0.03, ax2, 0.07)[0] + 0.016),) * 2)
        edges += [(t3, sh), (sh, el), (el, wr)]
        lx = seg_dir("thigh_" + s, "calf_" + s)
        hp = node(J["thigh_" + s] + lx * 0.05, (min(leg_cap, max(BULK["hip"], slice_radii(J["thigh_" + s] + lx * 0.12, lx, 0.1)[0] + 0.03)),) * 2)
        mt = node(J["thigh_" + s].lerp(J["calf_" + s], 0.55), (min(leg_cap, max(BULK["midthigh"], slice_radii(J["thigh_" + s].lerp(J["calf_" + s], 0.55), lx, 0.09)[0] + 0.026)),) * 2)
        kn = node(J["calf_" + s], (min(leg_cap, max(BULK["knee"], slice_radii(J["calf_" + s], lx, 0.08)[0] + 0.024)),) * 2)
        lx2 = seg_dir("calf_" + s, "foot_" + s)
        an = node(J["foot_" + s] + Vector((0, 0, 0.06)), (min(leg_cap, max(BULK["ankle"], slice_radii(J["foot_" + s] + Vector((0, 0, 0.1)), lx2, 0.08)[0] + 0.018)),) * 2)
        # Each leg is its own tube whose top sits up inside the trunk: no fabric web between the
        # legs to stretch when a knee comes up.
        tp = node(J["thigh_" + s] + Vector((0, 0, 0.07)), radii[hp])
        leg_tops.append(tp)
        edges += [(tp, hp), (hp, mt), (mt, kn), (kn, an)]
    me = bpy.data.meshes.new("Suit"); me.from_pydata([tuple(p) for p in nodes], edges, [])
    suit = bpy.data.objects.new("Suit", me); bpy.context.scene.collection.objects.link(suit)
    skin = suit.modifiers.new("Skin", "SKIN"); skin.branch_smoothing = 0.6; skin.use_smooth_shade = True
    for i, r in enumerate(radii):
        rr = max(r[0], r[1]) * 1.06 + 0.012 if t0 <= i < t4 else (max(r[0], r[1]) + 0.012 if i == t4 else r[0])
        suit.data.skin_vertices[0].data[i].radius = (rr * 1.05, rr * 1.05)
    suit.data.skin_vertices[0].data[t0].use_root = True
    for tp in leg_tops: suit.data.skin_vertices[0].data[tp].use_root = True
    sub = suit.modifiers.new("Smooth", "SUBSURF"); sub.levels = 2; sub.render_levels = 2
    bpy.context.view_layer.objects.active = suit
    for m in list(suit.modifiers):
        with bpy.context.temp_override(object=suit):
            bpy.ops.object.modifier_apply(modifier=m.name)
    # Which vertices belong to which leg tube (separate islands below the trunk).
    bm_ = bmesh.new(); bm_.from_mesh(suit.data); bm_.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v0 in bm_.verts:
        if v0.index in seen: continue
        isl, stack = [], [v0]
        while stack:
            x = stack.pop()
            if x.index in seen: continue
            seen.add(x.index); isl.append(x.index); stack.extend(e.other_vert(x) for e in x.link_edges)
        islands.append(isl)
    bm_.free()
    leg_side = {}
    for isl in islands:
        zs_ = [suit.data.vertices[i].co.z for i in isl]
        if max(zs_) < J["hips"].z + 0.12:                       # a leg, not the trunk
            xs_ = sum(suit.data.vertices[i].co.x for i in isl) / len(isl)
            sd = "l" if (xs_ - J["hips"].x) * (J["thigh_l"].x - J["hips"].x) > 0 else "r"
            for i in isl: leg_side[i] = sd
    print("leg verts", len(leg_side), "islands", len(islands))
    # flatten the torso front-to-back to the measured depth (the skin modifier makes it round)
    chain = [(nodes[i].z, radii[i]) for i in (t0, t1, t2, t3)]
    def depth_ratio(z):
        zs = sorted(chain)
        if z <= zs[0][0]: r = zs[0][1]
        elif z >= zs[-1][0]: r = zs[-1][1]
        else:
            for (za, ra), (zb, rb) in zip(zs, zs[1:]):
                if za <= z <= zb:
                    t = (z - za) / max(1e-6, zb - za); r = (ra[0] + (rb[0] - ra[0]) * t, ra[1] + (rb[1] - ra[1]) * t); break
        return max(0.68, min(1.0, (r[1] + 0.03) / (r[0] + 0.03)))
    cx, cy = J["spine"].x, J["spine"].y
    lo_z, hi_z = J["hips"].z - 0.12, J["neck"].z + 0.05
    for v in suit.data.vertices:
        if v.index in leg_side: continue
        if lo_z < v.co.z < hi_z and abs(v.co.x - cx) < 0.26:
            # only the trunk, not the arms leaving the shoulders
            k = depth_ratio(v.co.z)
            v.co.y = cy + (v.co.y - cy) * k
    # Legs fill out front-to-back instead (VRoid characters stand with their feet close together).
    for v in suit.data.vertices:
        if v.index in leg_side:
            s_ = leg_side[v.index]
            a_, b_ = J["thigh_" + s_], J["foot_" + s_]
            t_ = max(0.0, min(1.0, (v.co.z - a_.z) / (b_.z - a_.z)))
            ay = a_.y + (b_.y - a_.y) * t_
            v.co.y = ay + (v.co.y - ay) * 1.18
    # weights: each vertex to its two nearest body bones
    BONES = ["J_Bip_C_Hips", "J_Bip_C_Spine", "J_Bip_C_Chest", "J_Bip_C_UpperChest", "J_Bip_C_Neck"]
    for S in ("L", "R"):
        BONES += ["J_Bip_%s_%s" % (S, n) for n in ("UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot")]
    segs = []
    for bn in BONES:
        bb = arm.data.bones[bn]
        segs.append((bn, arm.matrix_world @ bb.head_local, arm.matrix_world @ bb.tail_local))
    # Smooth skin weights from Blender's bone heat, limited to the body bones (hair, skirt and
    # hood bones must not pull on the suit).
    keep = {bb.name: bb.use_deform for bb in arm.data.bones}
    for bb in arm.data.bones: bb.use_deform = bb.name in BONES
    bpy.ops.object.select_all(action="DESELECT")
    suit.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for bb in arm.data.bones: bb.use_deform = keep[bb.name]
    # Sharpen the heat weights (cube, renormalise) so limbs bend at the joints, not along their length.
    names = {vg.index: vg for vg in suit.vertex_groups}
    for v in suit.data.vertices:
        ws = [(g.group, g.weight ** 3) for g in v.groups if g.weight > 0]
        tot = sum(w for _, w in ws)
        if tot <= 0: continue
        for gi, w in ws: names[gi].add([v.index], w / tot, "REPLACE")
    # Leg tubes take only their own leg's bones, and their tops (inside the trunk) follow the thigh
    # rigidly, so a raised knee swings the whole tube instead of stretching anything.
    for v in suit.data.vertices:
        sd = leg_side.get(v.index)
        if sd is None:
            # the trunk never follows a leg (that is what dragged the hips into shards)
            ws = {suit.vertex_groups[ge.group].name: ge.weight for ge in v.groups}
            moved = sum(w for k, w in ws.items() if "Leg" in k or "Foot" in k)
            if moved > 0:
                ws = {k: w for k, w in ws.items() if not ("Leg" in k or "Foot" in k)}
                ws["J_Bip_C_Hips"] = ws.get("J_Bip_C_Hips", 0.0) + moved
                tot = sum(ws.values())
                for vg in suit.vertex_groups: vg.remove([v.index])
                for k, w in ws.items(): suit.vertex_groups[k].add([v.index], w / tot, "REPLACE")
            continue
        S_ = "L" if sd == "l" else "R"
        # Legs hang straight in the rest pose, so weight by height: thigh, a short knee blend,
        # shin, then the foot at the ankle (bone heat put some thigh-top vertices on the shin).
        z = v.co.z; kz = J["calf_" + sd].z; az = J["foot_" + sd].z
        up_, lo_, ft_ = "J_Bip_%s_UpperLeg" % S_, "J_Bip_%s_LowerLeg" % S_, "J_Bip_%s_Foot" % S_
        t_knee = max(0.0, min(1.0, (kz + 0.04 - z) / 0.08))          # 0 above the knee, 1 below
        t_ankle = max(0.0, min(1.0, (az + 0.05 - z) / 0.06))
        ws = {up_: 1 - t_knee, lo_: t_knee * (1 - t_ankle), ft_: t_ankle}
        ws = {k: w for k, w in ws.items() if w > 1e-4}
        tot = sum(ws.values())
        for vg in suit.vertex_groups: vg.remove([v.index])
        for k, w in ws.items(): suit.vertex_groups[k].add([v.index], w / tot, "REPLACE")
    # colour blocks
    for m in (SUIT, TRIM, JOINT):
        suit.data.materials.append(m)
    ys = [v.co.y for v in suit.data.vertices]
    for p in suit.data.polygons:
        c = p.center
        idx = 0
        trunk = lo_z < c.z < hi_z and abs(c.x - cx) < 0.24
        if trunk:
            w = max(0.05, radii[t2][0])
            side = abs(c.x - cx) / (w + 0.02)
            if 0.72 < side < 0.92: idx = 1                                   # mint side stripes
            if c.z > J["upperchest"].z + 0.02 and (c.y - cy) * FWD > -0.01: idx = 1   # mint chest yoke
        if trunk and abs(c.x - cx) < 0.012 and (c.y - cy) * FWD > 0 and c.z < J["upperchest"].z: idx = 2   # front zip
        for s in ("l", "r"):
            th, ft = J["thigh_" + s], J["foot_" + s]
            if (c - J["calf_" + s]).length < 0.085 and (c - J["calf_" + s]).dot(F) > 0: idx = 2     # knee pads
            if (c - J["lowerarm_" + s]).length < 0.06: idx = 2                                       # elbow pads
            if (c - J["upperarm_" + s]).length < 0.085 and c.z > J["upperarm_" + s].z - 0.01: idx = 1  # shoulder caps
        p.material_index = idx
        p.use_smooth = True
    bpy.data.objects.remove(body, do_unlink=True)
    body = suit
    # --- dark brown eyes: the iris texture as luminance x warm brown
    for m in face.data.materials:
        if "EyeIris" in m.name:
            tex = next((nd for nd in m.node_tree.nodes if nd.type == "TEX_IMAGE"), None)
            if tex and tex.image:
                img = tex.image; px = list(img.pixels); brown = (0.42, 0.25, 0.16)
                for k in range(0, len(px), 4):
                    lum = 0.3 * px[k] + 0.59 * px[k + 1] + 0.11 * px[k + 2]
                    lum = min(1.0, lum * 1.3)
                    px[k], px[k + 1], px[k + 2] = lum * brown[0], lum * brown[1], lum * brown[2]
                new = bpy.data.images.new("EyeIris_Brown", img.size[0], img.size[1], alpha=True)
                new.pixels = px; new.pack(); tex.image = new

    # --- helpers for the hardware
    mw = body.matrix_world
    verts = [mw @ v.co for v in body.data.vertices]
    def girth(a, b, t, pad=0.012, cutoff=0.25):
        """Radius of the body around the segment a->b at fraction t (90th percentile)."""
        p = a.lerp(b, t); d = (b - a).normalized(); rs = []
        for v in verts:
            w = v - p; along = w.dot(d)
            if abs(along) < 0.025:
                r = (w - d * along).length
                if r < cutoff: rs.append(r)
        rs.sort()
        return (rs[int(len(rs) * 0.9)] if rs else 0.06) + pad
    def surface(p, direction, band=0.05):
        """How far the body reaches from p along a horizontal direction."""
        best = 0.08
        for v in verts:
            w = v - p
            if abs(w.z) < band and abs(w.x) < 0.09:
                best = max(best, w.dot(direction))
        return best
    def attach(o, key):
        m = o.matrix_world.copy(); o.parent = arm; o.parent_type = "BONE"; o.parent_bone = B.get(key, key); o.matrix_world = m
    def piece(name, bm, m, key):
        o = obj(name, bm, m); attach(o, key); return o
    def axis_frame(a, b):
        z = (b - a).normalized()
        x = z.cross(Vector((0, 0, 1))) if abs(z.z) < 0.9 else z.cross(Vector((0, 1, 0)))
        x.normalize(); y = z.cross(x)
        return Matrix((x, y, z)).transposed().to_4x4()
    def ring_on(name, a, b, t, minor, m, key, extra=0.0, cutoff=0.13):
        r = girth(a, b, t, cutoff=cutoff) + extra
        bm = torus(r, minor, (0, 0, 0)); M = Matrix.Translation(a.lerp(b, t)) @ axis_frame(a, b)
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        return piece(name, bm, m, key)

    # --- cute gear (VRoid style): soft rounded shapes, white with mint trim, a clear bubble helmet
    for m, hexc in ((SUIT, "F4F1F7"), (TRIM, "7FD8C8"), (GLOVE, "F4F1F6"), (BOOT, "F4F1F6"), (PACK, "F7F4F8"), (JOINT, "C9C3D6")):
        b = next(nd for nd in m.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*g["srgb"](hexc), 1); b.inputs["Metallic"].default_value = 0.0
        b.inputs["Roughness"].default_value = 0.55
    hv = [hair.matrix_world @ v.co for v in hair.data.vertices] + [face.matrix_world @ v.co for v in face.data.vertices]
    lo = Vector((min(v.x for v in hv), min(v.y for v in hv), min(v.z for v in hv)))
    hi = Vector((max(v.x for v in hv), max(v.y for v in hv), max(v.z for v in hv)))
    hc = (lo + hi) / 2; hc.z = max(hc.z, head.z + 0.1)
    R = max((v - hc).length for v in hv if v.z > head.z - 0.02) * 0.98 + 0.025
    R = min(max(R, 0.17), 0.27)
    helmet = bpy.data.objects.new("Helmet", None); bpy.context.scene.collection.objects.link(helmet)
    helmet.location = hc; attach(helmet, "head")
    def hpiece(name, bm, m):
        o = obj(name, bm, m); mt = o.matrix_world.copy(); o.parent = helmet; o.matrix_world = mt; return o
    def front_opening(f):
        # The whole bubble is clear except a soft white band round its base.
        d = (f.calc_center_median() - hc).normalized()
        return d.z > -0.78
    sb = sphere(R, hc, 1, 1.03, 1.03, 48, 24)
    bmesh.ops.delete(sb, geom=[f for f in sb.faces if front_opening(f)], context="FACES")
    bmesh.ops.solidify(sb, geom=list(sb.faces), thickness=0.012)
    hpiece("Helmet shell", sb, SUIT)
    vb = sphere(R + 0.004, hc, 1, 1.03, 1.03, 48, 24)
    bmesh.ops.delete(vb, geom=[f for f in vb.faces if not front_opening(f)], context="FACES")
    bmesh.ops.solidify(vb, geom=list(vb.faces), thickness=0.004)
    hpiece("Visor", vb, VISOR)
    for s in (-1, 1):
        pod = hc + Vector((s * (R + 0.012), 0, -0.01))
        c = lathe([(0.0, -0.022), (0.05, -0.022), (0.058, 0.0), (0.05, 0.022), (0.0, 0.024)], (0, 0, 0), 32)
        bmesh.ops.transform(c, matrix=Matrix.Translation(pod) @ Matrix.Rotation(math.radians(90 * s), 4, "Y"), verts=c.verts)
        hpiece("Ear pod", c, TRIM)
        dot = sphere(0.016, pod + Vector((s * 0.026, 0, 0)), 1, 1, 1, 16, 8)
        hpiece("Ear light", dot, CYAN)
    ant = hc + Vector((-(R + 0.012), 0, 0.03))
    stalk = lathe([(0.006, 0), (0.006, 0.13)], (0, 0, 0), 8)
    bmesh.ops.transform(stalk, matrix=Matrix.Translation(ant) @ Matrix.Rotation(math.radians(-20), 4, "Y"), verts=stalk.verts)
    hpiece("Antenna", stalk, JOINT)
    tip = ant + Matrix.Rotation(math.radians(-20), 3, "Y") @ Vector((0, 0, 0.14))
    hpiece("Antenna ball", sphere(0.022, tip, 1, 1, 1, 16, 8), ACCENT)
    neck = bone("neck")
    seal = girth(bone("chest"), neck, 0.85, 0.02, cutoff=0.14)
    piece("Collar", torus(seal, 0.03, neck + Vector((0, 0, -0.01)), sy=0.95, minor=10), TRIM, "chest")

    # --- chest badge and a capsule backpack
    chest = bone("chest") + Vector((0, 0, 0.03))
    front = surface(chest, F)
    piece("Badge", rbox((0.13, 0.03, 0.09), chest + F * (front + 0.004), 0.035, 4), PACK, "chest")
    piece("Badge screen", rbox((0.09, 0.02, 0.05), chest + F * (front + 0.016) + Vector((0, 0, 0.006)), 0.02, 3), SCREEN, "chest")
    for i, x in enumerate((-0.03, 0.0, 0.03)):
        piece("Badge light", sphere(0.007, chest + F * (front + 0.02) + Vector((x, 0, -0.03)), 1, 1, 1, 12, 6), (AMBER, CYAN, ACCENT)[i], "chest")
    back = surface(chest, -F)
    pc = chest - F * (back + 0.07) - Vector((0, 0, 0.05))
    piece("Backpack", rbox((0.29, 0.14, 0.33), pc, 0.07, 6), PACK, "chest")
    piece("Backpack panel", rbox((0.17, 0.02, 0.2), pc - F * 0.068 + Vector((0, 0, 0.02)), 0.04, 4), TRIM, "chest")
    piece("Backpack star", sphere(0.018, pc - F * 0.08 + Vector((0, 0, 0.08)), 1, 1, 1, 12, 6), ACCENT, "chest")
    for s in (-1, 1):
        tank = lathe([(0.0, -0.05), (0.035, -0.045), (0.04, 0.0), (0.035, 0.045), (0.0, 0.05)], (0, 0, 0), 24)
        bmesh.ops.transform(tank, matrix=Matrix.Translation(pc + Vector((s * 0.1, 0, -0.2))), verts=tank.verts)
        piece("Thruster", tank, TRIM, "chest")
    piece("Belt", torus(girth(bone("spine"), bone("chest"), 0.0, 0.006, cutoff=0.16), 0.018, bone("spine"), sy=0.8, minor=10), TRIM, "spine")

    # --- soft cuffs, mitts and chunky boots
    for s in ("l", "r"):
        la, hd = bone("lowerarm_" + s), bone("hand_" + s)
        ft, toe = bone("foot_" + s), bone("toe_" + s)
        ring_on("Cuff", la, hd, 0.9, 0.022, TRIM, "lowerarm_" + s, extra=0.006)
        hand_dir = (tail("hand_" + s) - hd).normalized()
        gl = rbox((0.065, 0.1, 0.13), (0, 0, 0), 0.04, 5)
        bmesh.ops.transform(gl, matrix=Matrix.Translation(hd + hand_dir * 0.065) @ axis_frame(hd, hd + hand_dir), verts=gl.verts)
        piece("Mitt", gl, GLOVE, "hand_" + s)
        length = max(0.22, (toe - ft).length * 2.0)
        mid = Vector((ft.x, (ft.y + toe.y) / 2 + 0.01 * F.y, 0.0))
        bw = min(0.125, abs(bone("foot_l").x - bone("foot_r").x) * 0.9)
        piece("Boot", rbox((bw, length + 0.05, 0.11), mid + Vector((0, 0, 0.075)), min(0.05, bw * 0.4), 5), BOOT, "foot_" + s)
        k = bw / 0.125
        shaft = lathe([(0.0, 0.0), (0.058 * k, 0.0), (0.062 * k, 0.1), (0.066 * k, 0.17), (0.0, 0.17)], (ft.x, ft.y, 0.04), 28, sy=1.1)
        piece("Boot shaft", shaft, BOOT, "foot_" + s)
        piece("Boot band", torus(0.066 * k, 0.014, Vector((ft.x, ft.y, 0.21)), sy=1.1, minor=8), TRIM, "foot_" + s)
        piece("Boot sole", rbox((bw + 0.006, length + 0.06, 0.03), mid + Vector((0, 0, 0.015)), 0.014, 3), TRIM, "foot_" + s)


    bake_suit(bpy.data.objects["Suit"])
    save_textures(kind)
    return arm, FWD, (helmet, hc, R)

def bake_suit(suit, size=1024):
    """Paints the suit the way VRoid paints clothes: its panel colours, soft lavender shading in the
    folds (baked ambient occlusion) and thin dark seam lines where panels meet, baked into one
    texture on a single "Suit_CLOTH" material (the game tints it with the wardrobe's suit colour)."""
    scene = bpy.context.scene
    bpy.ops.object.select_all(action="DESELECT"); suit.select_set(True); bpy.context.view_layer.objects.active = suit
    # UVs
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")
    # seam mask: vertices on edges between different panels
    me = suit.data
    seam = me.color_attributes.new("seam", "FLOAT_COLOR", "POINT")
    for i in range(len(me.vertices)): seam.data[i].color = (0, 0, 0, 1)
    edge_faces = {}
    for p in me.polygons:
        for ek in p.edge_keys: edge_faces.setdefault(ek, set()).add(p.material_index)
    for (a, b), mats in edge_faces.items():
        if len(mats) > 1:
            seam.data[a].color = (1, 1, 1, 1); seam.data[b].color = (1, 1, 1, 1)
    # bake on copies: the gear (helmet, collar, boots) shares these materials and must stay untouched
    for i, m in enumerate(me.materials): me.materials[i] = m.copy()
    img = bpy.data.images.new("Suit_paint", size, size, alpha=False)
    shade = (0.74, 0.70, 0.86)
    for m in me.materials:
        nt = m.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        base = tuple(bsdf.inputs["Base Color"].default_value)
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.12
        ramp = nt.nodes.new("ShaderNodeMapRange"); ramp.inputs[1].default_value = 0.35; ramp.inputs[2].default_value = 0.95
        mixs = nt.nodes.new("ShaderNodeMix"); mixs.data_type = "RGBA"
        mixs.inputs[6].default_value = (base[0] * shade[0], base[1] * shade[1], base[2] * shade[2], 1); mixs.inputs[7].default_value = base
        attr = nt.nodes.new("ShaderNodeAttribute"); attr.attribute_name = "seam"
        line = nt.nodes.new("ShaderNodeMix"); line.data_type = "RGBA"; line.inputs[7].default_value = (0.36, 0.31, 0.42, 1)
        sharp = nt.nodes.new("ShaderNodeMapRange"); sharp.inputs[1].default_value = 0.55; sharp.inputs[2].default_value = 0.9
        emit = nt.nodes.new("ShaderNodeEmission"); out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        nt.links.new(ao.outputs["AO"], ramp.inputs[0]); nt.links.new(ramp.outputs[0], mixs.inputs[0])
        nt.links.new(attr.outputs["Fac"], sharp.inputs[0]); nt.links.new(sharp.outputs[0], line.inputs[0])
        nt.links.new(mixs.outputs[2], line.inputs[6]); nt.links.new(line.outputs[2], emit.inputs[0])
        nt.links.new(emit.outputs[0], out.inputs[0])
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
        for n in nt.nodes: n.select = False
        tex.select = True; nt.nodes.active = tex
    old_engine = scene.render.engine
    try: scene.render.engine = "CYCLES"
    except TypeError: pass
    scene.cycles.samples = 64
    try: scene.cycles.device = "CPU"
    except Exception: pass
    bpy.ops.object.bake(type="EMIT", margin=8)
    try: scene.render.engine = old_engine
    except TypeError: pass
    # one painted material on the whole suit
    paint = bpy.data.materials.new("Suit_CLOTH"); paint.use_nodes = True
    pb = next(n for n in paint.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    t = paint.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img
    paint.node_tree.links.new(t.outputs["Color"], pb.inputs["Base Color"])
    img.pack()
    me.materials.clear(); me.materials.append(paint)
    for p in me.polygons: p.material_index = 0

def save_textures(kind):
    """Writes each face/hair/eye texture to Resources/Astronaut/Textures/<Kind>_<name>.png and names
    the material after it, so the game loads the texture by the material's name."""
    tex_dir = os.path.join(EXPORT, "Textures"); os.makedirs(tex_dir, exist_ok=True)
    for o in bpy.data.objects:
        if o.type != "MESH": continue
        for m in o.data.materials:
            if not m or not m.use_nodes: continue
            node = next((nd for nd in m.node_tree.nodes if nd.type == "TEX_IMAGE" and nd.image), None)
            if node is None: continue
            stem = kind + "_" + "".join(c if c.isalnum() or c in "_-" else "_" for c in m.name.split(".")[0])
            path = os.path.join(tex_dir, stem + ".png")
            img = node.image
            img.filepath_raw = path; img.file_format = "PNG"; img.save()
            m.name = stem

def export(arm, kind):
    os.makedirs(EXPORT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if o == arm or o.parent == arm or (o.parent and o.parent.parent == arm): o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT, "Astronaut_" + kind + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False,
                             path_mode="COPY", embed_textures=True)
    return path
