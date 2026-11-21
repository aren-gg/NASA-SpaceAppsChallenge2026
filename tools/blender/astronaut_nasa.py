"""
Astronaut v5: NASA's public astronaut model (NASA 3D Resources, "Astronaut", DigitalSpace
Corporation; https://science.nasa.gov/3d-resources/astronaut/) detailed in Blender after the look
of "Astronaut by Jungle Jim" (Sketchfab, CC BY): white suit, gold visor, gunmetal chest control box
with an orange button, orange arm and leg stripes, chest-to-pack hoses, thigh pockets, a wrist
computer, dark gloves and soles, helmet lamps and a flag patch.

build() leaves one object per part in outpost-local metres (feet at 0, facing -Y);
build_astronaut() adds the sealed helmet and the skeleton, export_astronaut() writes
Assets/Resources/Astronaut/Astronaut.fbx. Clips: astronaut_mocap.py.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

SRC = "/private/tmp/claude-501/-Users-andrewhuangjin-Documents-code-nasa/dde12ac5-72ac-40e4-ac25-0e1f0d275ab6/scratchpad/jj/dl/Astronaut.glb"
F = Vector((0, -1, 0))          # the model faces -Y

def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexrgb, rough=0.6, metal=0.0, emit=None, strength=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*srgb(hexrgb), 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*srgb(emit), 1)
        b.inputs["Emission Strength"].default_value = strength
    return m

def wipe():
    if bpy.context.object and bpy.context.object.mode != "OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.images, bpy.data.curves,
                bpy.data.lights, bpy.data.cameras):
        for b in list(blk): blk.remove(b)
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)

def obj(name, bm, m):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def rbox(size, centre, bevel=0.01, segs=3, M=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.45), segments=segs, affect="EDGES", profile=0.5)
    if M is not None: bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=bm.verts)
    return bm

def torus(R, r, seg=36, minor=8):
    bm = bmesh.new(); rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; ring = []
        for k in range(minor):
            b = 2 * math.pi * k / minor; rr = R + r * math.cos(b)
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), r * math.sin(b))))
        rings.append(ring)
    for i in range(seg):
        for k in range(minor):
            bm.faces.new((rings[i][k], rings[(i + 1) % seg][k], rings[(i + 1) % seg][(k + 1) % minor], rings[i][(k + 1) % minor]))
    return bm

def frame_z(a, b):
    z = (b - a).normalized()
    x = z.cross(Vector((0, 0, 1))) if abs(z.z) < 0.9 else z.cross(Vector((0, 1, 0)))
    x.normalize(); y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_4x4()

def tube(name, pts, r, m):
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"; cu.bevel_depth = r; cu.bevel_resolution = 3
    sp = cu.splines.new("BEZIER"); sp.bezier_points.add(len(pts) - 1)
    for bp, p in zip(sp.bezier_points, pts):
        bp.co = p; bp.handle_left_type = bp.handle_right_type = "AUTO"
    o = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(o); cu.materials.append(m)
    return o

def build():
    wipe()
    bpy.context.scene.render.use_freestyle = False
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=SRC)
    new = [o for o in bpy.data.objects if o not in before]
    body = next(o for o in new if o.type == "MESH")
    for o in new:
        if o is not body:
            mw = body.matrix_world.copy(); body.parent = None; body.matrix_world = mw
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = body; body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = body.data
    vs = [v.co.copy() for v in me.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    me.transform(Matrix.Scale(1.85 / (hi.z - lo.z), 4) @ Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
    me.update(); body.name = "Suit"

    SUIT = mat("Suit", "EEECE6", 0.72)
    VISOR = mat("Visor", "C99A3A", 0.12, 1.0)
    METAL = mat("Trim", "3B3F46", 0.35, 0.8)
    ORANGE = mat("Accent", "E8582B", 0.5)
    GLOVE = mat("Glove", "2A2C31", 0.6)
    BOOT = mat("Boot", "34363C", 0.7)
    PACK = mat("Pack", "E7E4DD", 0.7)
    LAMP = mat("Lamp", "000000", 0.4, emit="FFF1D6", strength=10)
    RED = mat("Flag Red", "B4232A", 0.6)
    visor = [p.material_index == 1 for p in me.polygons]      # NASA's second material is the visor
    me.materials.clear()
    for m in (SUIT, VISOR, GLOVE, BOOT):
        me.materials.append(m)
    pts = [v.co for v in me.vertices]
    for p, is_visor in zip(me.polygons, visor):
        c = p.center
        p.material_index = 1 if is_visor else (2 if abs(c.x) > 0.74 else (3 if c.z < 0.06 else 0))
    # landmarks
    def slice_pts(z0, z1, xmin=None, xmax=None):
        return [v for v in pts if z0 < v.z < z1 and (xmin is None or v.x > xmin) and (xmax is None or v.x < xmax)]
    chest = slice_pts(1.22, 1.36, -0.08, 0.08); front = min(v.y for v in chest); back = max(v.y for v in slice_pts(1.1, 1.45, -0.1, 0.1))
    parts = []
    # chest control box (gunmetal), with an orange button and grille
    cz = 1.29
    parts.append(obj("Chest box", rbox((0.24, 0.07, 0.16), (0, front - 0.025, cz), 0.015), METAL))
    parts.append(obj("Chest button", rbox((0.05, 0.03, 0.05), (0.06, front - 0.065, cz - 0.03), 0.008), ORANGE))
    for k in range(4):
        parts.append(obj("Chest grille", rbox((0.09, 0.012, 0.008), (-0.05, front - 0.062, cz + 0.035 - k * 0.022), 0.003, 1), SUIT))
    # hoses from the chest box round to the pack
    for s in (-1, 1):
        parts.append(tube("Hose", [Vector((s * 0.1, front - 0.03, cz - 0.07)), Vector((s * 0.2, front + 0.02, cz - 0.12)),
                                   Vector((s * 0.25, (front + back) / 2, cz - 0.05)), Vector((s * 0.2, back - 0.02, cz + 0.02))], 0.014, METAL))
    # arms: wrist computer (left), orange stripes, flag patch (right shoulder)
    for s in (-1, 1):
        arm = [v for v in pts if s * v.x > 0.3 and v.z > 1.1]
        hand = max(arm, key=lambda v: s * v.x)
        sh = Vector((s * 0.27, 0.0, 1.47)); wr = Vector((hand.x - s * 0.09, 0.0, hand.z + 0.02))
        # the arm's own axis: centre of its cross-sections near the shoulder and the wrist
        def centre_at(t):
            q = sh.lerp(wr, t); dd = (wr - sh).normalized()
            near = [v for v in arm if abs((v - q).dot(dd)) < 0.02 and ((v - q) - dd * (v - q).dot(dd)).length < 0.13]
            return sum(near, Vector()) / len(near) if near else q
        sh, wr = centre_at(0.25), centre_at(0.9)
        d = (wr - sh).normalized()
        # orange sleeve bands: rings square to the arm (shoulder to hand), fitted to the sleeve
        ax = (Vector((hand.x, hand.y, hand.z)) - Vector((s * 0.3, 0.0, 1.45))).normalized()
        for x0 in (0.43, 0.47):
            p0 = Vector((s * 0.3, 0.0, 1.45)) + ax * ((x0 - 0.3) / abs(ax.x))
            near = [v for v in arm if abs((v - p0).dot(ax)) < 0.01 and (v - p0).length < 0.16]
            cc = sum(near, Vector()) / len(near)
            rs = sorted(((v - cc) - ax * (v - cc).dot(ax)).length for v in near)
            r = rs[int(len(rs) * 0.85)] + 0.006
            bm = torus(r, 0.011, 40, 6); bmesh.ops.transform(bm, matrix=Matrix.Translation(cc) @ frame_z(cc, cc + ax), verts=bm.verts)
            parts.append(obj("Arm stripe", bm, ORANGE))
        if s < 0:
            p = sh.lerp(wr, 0.8)
            parts.append(obj("Wrist computer", rbox((0.07, 0.05, 0.09), p + Vector((0, -0.065, 0.02)), 0.01, 2, frame_z(sh, wr)), METAL))
            parts.append(obj("Wrist screen", rbox((0.05, 0.006, 0.06), p + Vector((0, -0.092, 0.02)), 0.003, 1, frame_z(sh, wr)),
                             mat("Screen", "0E141C", 0.15, 0.2, emit="5FD4FF", strength=2)))
        else:
            p = sh.lerp(wr, 0.18) + Vector((0, -0.05, 0.05))
            for k in range(3):
                parts.append(obj("Flag stripe", rbox((0.07, 0.008, 0.012), p + Vector((0, 0, 0.02 - k * 0.02)), 0.002, 1), RED if k % 2 == 0 else SUIT))
    # legs: orange stripe above the knee, thigh pockets
    for s in (-1, 1):
        leg = [v for v in pts if s * v.x > 0.02 and 0.25 < v.z < 0.85]
        for z in (0.6,):
            ring = [v for v in leg if abs(v.z - z) < 0.015]
            cx = sum(v.x for v in ring) / len(ring); cy = sum(v.y for v in ring) / len(ring)
            r = sorted(math.hypot(v.x - cx, v.y - cy) for v in ring)[int(len(ring) * 0.9)] + 0.004
            bm = torus(r, 0.012, 40, 6); bmesh.ops.translate(bm, vec=Vector((cx, cy, z)), verts=bm.verts)
            parts.append(obj("Leg stripe", bm, ORANGE))
        ring = [v for v in leg if abs(v.z - 0.72) < 0.03]
        out = max(ring, key=lambda v: s * v.x)
        parts.append(obj("Thigh pocket", rbox((0.04, 0.12, 0.15), Vector((out.x + s * 0.02, out.y * 0.3, 0.72)), 0.012), METAL))
    # helmet lamps
    top = max(v.z for v in pts)
    hv = [v for v in pts if v.z > top - 0.3]
    hx = max(abs(v.x) for v in hv)
    for s in (-1, 1):
        parts.append(obj("Lamp housing", rbox((0.045, 0.07, 0.05), Vector((s * (hx + 0.01), -0.05, top - 0.09)), 0.01), METAL))
        parts.append(obj("Lamp", rbox((0.035, 0.008, 0.035), Vector((s * (hx + 0.01), -0.088, top - 0.09)), 0.004, 1), LAMP))
    bpy.ops.object.select_all(action="DESELECT")
    return body, parts

NECK = Vector((0.0, -0.02, 1.53))      # the suit's neck ring

# ---------------------------------------------------------------- sealed helmet, smaller pack
HEAD_SCALE = 1.3      # sizes the neck seal to the helmet

def strip_helmet_and_shrink_pack(body):
    """Removes NASA's boxy helmet (and its side light blocks) and makes the PLSS pack smaller."""
    me = body.data
    bm = bmesh.new(); bm.from_mesh(me)
    kill = [f for f in bm.faces if f.material_index == 1 or
            (f.calc_center_median().z > 1.535 and abs(f.calc_center_median().x) < 0.28 and f.calc_center_median().y < 0.13)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    # NASA's pack is loose triangles that hang to the hips and crumple when skinned: drop it, a box replaces it
    pack = [f for f in bm.faces if f.calc_center_median().z > 0.6 and abs(f.calc_center_median().x) < 0.35
            and (f.calc_center_median().y > 0.115 or max(v.co.y for v in f.verts) > 0.14)]
    bmesh.ops.delete(bm, geom=pack, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # That leaves holes in the back of the torso and hips (the game culls back faces, so you saw
    # through the suit): weld the loose triangles, then close every hole on the back, facing out.
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    back = [e for e in bm.edges if e.is_boundary and sum(v.co.y for v in e.verts) / 2 > 0.0
            and sum(v.co.z for v in e.verts) / 2 < 1.51]
    made = bmesh.ops.holes_fill(bm, edges=back, sides=0)["faces"]
    made = bmesh.ops.triangulate(bm, faces=made)["faces"]
    for f in made:
        if f.normal.y < 0: f.normal_flip()
    for f in bm.faces: f.smooth = False   # the loose triangles were flat-shaded; welding must not change the look
    bm.to_mesh(me); bm.free()

def add_neck_seal_and_helmet(head_parts):
    """A soft fabric neck seal inside a gunmetal neck ring, and a rounded helmet sized to the head with
    a large gold visor and two small lamps."""
    FABRIC = mat("Comfort Cap", "4A4E57", 0.85)
    METAL = mat("Trim", "3B3F46", 0.35, 0.8)
    VISOR = mat("Visor", "C99A3A", 0.12, 1.0)
    SHELL = mat("Helmet Shell", "EEECE6", 0.5)
    LAMP = mat("Lamp", "000000", 0.4, emit="FFF1D6", strength=10)
    parts = []
    # neck seal: a soft sleeve from inside the suit up the neck
    k = HEAD_SCALE
    prof = [(0.12, NECK.z - 0.05), (0.115, NECK.z), (0.085 * k, NECK.z + 0.03 * k), (0.068 * k, NECK.z + 0.06 * k)]
    bm = bmesh.new(); steps = 40
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * i / steps), NECK.y + r * 0.9 * math.sin(2 * math.pi * i / steps), z)) for i in range(steps)] for r, z in prof]
    for j in range(len(rings) - 1):
        for i in range(steps):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % steps], rings[j + 1][(i + 1) % steps], rings[j + 1][i]))
    parts.append(obj("Neck seal", bm, FABRIC))
    bm = torus(0.135, 0.022, 48, 8); bmesh.ops.translate(bm, vec=Vector((0, NECK.y, NECK.z - 0.01)), verts=bm.verts)
    parts.append(obj("Neck ring", bm, METAL))
    # helmet sized to the head and hair
    pts = [o.matrix_world @ v.co for o in head_parts if o.name in ("Face", "Hair") for v in o.data.vertices]
    pts = [p for p in pts if p.z > NECK.z + 0.02]
    if pts:
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        hc = (lo + hi) / 2
        R = max((p - hc).length for p in pts) * 0.9 + 0.025
        R = min(max(R, 0.255), 0.3)
        hc.z = max(hc.z, NECK.z + R * 0.78)
    else:       # no head: the helmet at the size the game shipped with
        R = 0.255; hc = Vector((0, NECK.y, 1.74))
    def sphere_bm(r):
        b = bmesh.new(); bmesh.ops.create_uvsphere(b, u_segments=48, v_segments=24, radius=r)
        bmesh.ops.translate(b, vec=hc, verts=b.verts); return b
    def front(f):
        d = (f.calc_center_median() - hc).normalized()
        return d.dot(F) > 0.2 and -0.45 < d.z < 0.62
    cut_z = NECK.z + 0.005
    sb = sphere_bm(R)
    bmesh.ops.delete(sb, geom=[f for f in sb.faces if front(f) or f.calc_center_median().z < cut_z], context="FACES")
    bmesh.ops.solidify(sb, geom=list(sb.faces), thickness=0.012)
    helmet = [obj("Helmet shell", sb, SHELL)]
    vb = sphere_bm(R + 0.004)
    bmesh.ops.delete(vb, geom=[f for f in vb.faces if not front(f) or f.calc_center_median().z < cut_z], context="FACES")
    bmesh.ops.solidify(vb, geom=list(vb.faces), thickness=0.005)
    helmet.append(obj("Visor", vb, VISOR))
    for s in (-1, 1):
        p = hc + Vector((s * R * 0.83, -R * 0.35, R * 0.45))
        helmet.append(obj("Lamp housing", rbox((0.03, 0.06, 0.04), p, 0.01), METAL))
        helmet.append(obj("Lamp", rbox((0.024, 0.008, 0.028), p + Vector((0, -0.032, 0)), 0.004, 1), LAMP))
    return parts, helmet

# ---------------------------------------------------------------- rig + export
EXPORT = "/Users/andrewhuangjin/Documents/code/nasa/My project/Assets/Resources/Astronaut"

def rig(body, head_parts, seal, helmet, gear):
    """A humanoid skeleton (Unreal mannequin bone names, which Unity's Humanoid maps automatically)
    fitted to the suit's own landmarks, smooth suit weights by body region, and every rigid part
    parented to its bone. The helmet sits under a "Helmet" node on the head (the game moves it)."""
    P = [v.co.copy() for v in body.data.vertices]
    def centre(cond):
        pts = [p for p in P if cond(p)]
        return sum(pts, Vector()) / len(pts)
    def mid(pts):
        """Bounding-box centre: the mesh is low-poly and uneven, so a vertex mean drifts."""
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        return (lo + hi) / 2
    # The spine runs through the middle of the hips (front of the suit to the start of the pack).
    cy = mid([p for p in P if 0.1 < abs(p.x) < 0.2 and abs(p.z - 0.88) < 0.02]).y
    J = {"pelvis": Vector((0, cy, 0.97)), "spine_01": Vector((0, cy, 1.07)), "spine_02": Vector((0, cy, 1.19)),
         "spine_03": Vector((0, cy, 1.32)), "neck_01": NECK.copy(), "Head": NECK + Vector((0, 0, 0.07))}
    tails = {"Head": NECK + Vector((0, 0, 0.3))}
    for s, L in ((1, "l"), (-1, "r")):
        arm = [p for p in P if s * p.x > 0.26 and p.z > 1.1]
        tip = max(s * p.x for p in arm)
        def arm_at(x):
            near = [p for p in arm if abs(s * p.x - x) < 0.015]
            c = mid(near); c.x = s * x; return c
        J["clavicle_" + L] = Vector((s * 0.05, cy, 1.44))
        J["upperarm_" + L] = arm_at(0.3)
        J["lowerarm_" + L] = arm_at(0.53)
        J["hand_" + L] = arm_at(tip - 0.13)
        tails["hand_" + L] = arm_at(tip - 0.01)
        leg = [p for p in P if s * p.x > 0.015 and p.z < 0.93]
        def leg_at(z):
            near = [p for p in leg if abs(p.z - z) < 0.015]
            return mid(near)
        J["thigh_" + L] = leg_at(0.88); J["thigh_" + L].y = cy     # hip joints sit on the spine's plane
        J["calf_" + L] = leg_at(0.5)
        ank = leg_at(0.13); J["foot_" + L] = ank
        foot = [p for p in leg if p.z < 0.07]
        front = min(p.y for p in foot)
        J["ball_" + L] = Vector((ank.x, front + 0.06, 0.03)); tails["ball_" + L] = Vector((ank.x, front, 0.03))
    parent = {"spine_01": "pelvis", "spine_02": "spine_01", "spine_03": "spine_02", "neck_01": "spine_03", "Head": "neck_01"}
    for L in ("l", "r"):
        parent.update({"clavicle_" + L: "spine_03", "upperarm_" + L: "clavicle_" + L, "lowerarm_" + L: "upperarm_" + L,
                       "hand_" + L: "lowerarm_" + L, "thigh_" + L: "pelvis", "calf_" + L: "thigh_" + L,
                       "foot_" + L: "calf_" + L, "ball_" + L: "foot_" + L})
    child = {"pelvis": "spine_01", "spine_01": "spine_02", "spine_02": "spine_03", "spine_03": "neck_01", "neck_01": "Head"}
    for L in ("l", "r"):
        child.update({"clavicle_" + L: "upperarm_" + L, "upperarm_" + L: "lowerarm_" + L, "lowerarm_" + L: "hand_" + L,
                      "thigh_" + L: "calf_" + L, "calf_" + L: "foot_" + L, "foot_" + L: "ball_" + L})
    ad = bpy.data.armatures.new("Armature"); arm_o = bpy.data.objects.new("Armature", ad)
    bpy.context.scene.collection.objects.link(arm_o)
    bpy.ops.object.select_all(action="DESELECT"); arm_o.select_set(True); bpy.context.view_layer.objects.active = arm_o
    bpy.ops.object.mode_set(mode="EDIT")
    for name in J:
        eb = ad.edit_bones.new(name); eb.head = J[name]
        t = tails.get(name) or (J[child[name]] if name in child else J[name] + Vector((0, 0, 0.1)))
        if (t - eb.head).length < 0.01: t = eb.head + Vector((0, 0, 0.05))
        eb.tail = t
    for name, par in parent.items():
        ad.edit_bones[name].parent = ad.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in ad.bones}
    def dist(p, a, b):
        ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
        return (p - (a + ab * t)).length
    TRUNK = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "clavicle_l", "clavicle_r"]
    groups = {n: body.vertex_groups.new(name=n) for n in segs}
    for v in body.data.vertices:
        p = v.co; s = 1 if p.x > 0 else -1; L = "l" if s > 0 else "r"
        if s * p.x > 0.27 and p.z > 1.1: allowed = ["clavicle_" + L, "upperarm_" + L, "lowerarm_" + L, "hand_" + L]
        elif p.z < 0.9 and abs(p.x) > 0.03: allowed = ["thigh_" + L, "calf_" + L, "foot_" + L, "ball_" + L]
        elif p.z < 0.95: allowed = ["pelvis", "thigh_" + L]
        else: allowed = TRUNK
        ds = sorted((dist(p, *segs[n]), n) for n in allowed)[:2]
        ws = [(1.0 / max(1e-4, d) ** 4, n) for d, n in ds]
        tot = sum(w for w, _ in ws)
        for w, n in ws: groups[n].add([v.index], w / tot, "REPLACE")
    mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = arm_o
    body.parent = arm_o
    def bone_parent(o, bone):
        mw = o.matrix_world.copy(); o.parent = arm_o; o.parent_type = "BONE"; o.parent_bone = bone; o.matrix_world = mw
    def nearest(p):
        return min(segs, key=lambda n: dist(p, *segs[n]))
    for o in gear:
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        bone_parent(o, "spine_03" if c.y > 0.1 and c.z > 0.8 and abs(c.x) < 0.31 else nearest(c))
    for o in head_parts:
        bone_parent(o, "neck_01" if o.name.startswith("Neck skin") else "Head")
    for o in seal:
        bone_parent(o, "neck_01" if o.name.startswith("Neck seal") else "spine_03")
    hc = sum((o.matrix_world @ Vector(b) for o in helmet for b in o.bound_box), Vector()) / (8 * len(helmet))
    hnode = bpy.data.objects.new("Helmet", None); bpy.context.scene.collection.objects.link(hnode)
    hnode.location = hc; bone_parent(hnode, "Head")
    for o in helmet:
        mw = o.matrix_world.copy(); o.parent = hnode; o.matrix_world = mw
    return arm_o

# ---------------------------------------------------------------- v3: one astronaut, helmet always on
def build_astronaut():
    """The single playable astronaut: the detailed NASA suit with its sealed helmet (gold visor down)
    and a dark comfort-cap head form inside it. No faces; the helmet never comes off."""
    body, gear = build()
    for o in list(bpy.data.objects):
        if o.name.startswith("Lamp") and o in gear: gear.remove(o); bpy.data.objects.remove(o, do_unlink=True)
    for o in list(gear):
        if o.type == "CURVE":
            bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
            bpy.ops.object.convert(target="MESH"); gear[gear.index(o)] = bpy.context.object
    strip_helmet_and_shrink_pack(body)
    seal, helmet = add_neck_seal_and_helmet([])
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=0.1)
    bmesh.ops.scale(bm, vec=Vector((1.0, 1.15, 1.25)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector((0, NECK.y, NECK.z + 0.17)), verts=bm.verts)
    head = [obj("Head form", bm, mat("Comfort Cap", "4A4E57", 0.85))]
    gear.append(obj("Pack", rbox((0.42, 0.2, 0.56), Vector((0, 0.215, 1.24)), 0.035), mat("Pack", "E4E2DC", 0.7)))
    arm = rig(body, head, seal, helmet, gear)
    return arm

def export_astronaut(arm):
    bpy.ops.object.select_all(action="DESELECT")
    def walk(o):
        o.select_set(True)
        for c in o.children: walk(c)
    walk(arm)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT, "Astronaut.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False)
    return path
