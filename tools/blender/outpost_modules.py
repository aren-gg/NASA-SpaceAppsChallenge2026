"""
Marsivity outpost modules v3: a hub (reception) and three wings, built roomy.

Plan (game x right, game z forward):           Airlock - EXIT
                                                   |
                        Storage ------------- HUB (reception) ------------- Utilities
                                                   |
                                               Greenhouse
Doorways are 2 m wide and tunnels 3 m, so the astronaut never threads a needle.

Replaces outpost_shells.py (outer skins laid over the code-built rooms). Every module here is
a closed, manifold printed shell whose inner face IS the room's wall and ceiling, plus the
interior lining (deck, ribs, light coves, wainscot, wayfinding) and the exterior kit.
Doorways are built into the end walls (no booleans on open surfaces, which is what sealed the
tunnel mouths in v1).

Look: NASA printed habitats (MARSHA hub, Dune Alpha style vaults) with a NASA-punk finish.
Interior language (HUD / sci-fi wayfinding): printed basalt left visible above a light
composite wainscot, gunmetal frames every 1.5 m, cyan = information (cove light, module IDs),
amber = guidance and caution (floor edge lights, door thresholds), red only for danger.

Coordinates: Blender X = game x, Blender Y = game z, Blender Z = game y (up). Outpost-local
metres, floor at 0. The interior floor rectangles are the game's FloorRects unchanged, so the
code-built furniture, tools and room logic still fit.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

ROOT = "/Users/andrewhuangjin/Documents/code/nasa"
EXPORT = ROOT + "/My project/Assets/Resources/OutpostModules"
PREVIEW = ROOT + "/tools/blender/preview"
os.makedirs(PREVIEW, exist_ok=True)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for block in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
    for b in list(block):
        block.remove(b)
for c in list(bpy.data.collections):
    bpy.data.collections.remove(c)
scene = bpy.context.scene

# ------------------------------------------------------------------ materials (names = Unity contract)
def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexrgb, rough=0.6, metal=0.0, emit=None, strength=0.0, alpha=1.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*srgb(hexrgb), 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*srgb(emit), 1)
        b.inputs["Emission Strength"].default_value = strength
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
        try: m.surface_render_method = "BLENDED"
        except Exception: pass
    return m

PRINT = mat("Shell Printed", "D8CFC2", 0.85)
PRINT_DARK = mat("Shell Printed Dark", "8C8278", 0.9)
PRINT_IN = mat("Int Printed", "CFC4B4", 0.9)            # printed wall left bare inside
PANEL = mat("Int Panel", "E9E6E0", 0.45)                 # composite wainscot / ceiling panels
METAL = mat("Shell Gunmetal", "3A3D44", 0.35, 0.8)
STEEL = mat("Shell Steel", "8E949C", 0.3, 0.9)
DECK = mat("Int Deck", "474B53", 0.5, 0.7)              # deck plates
RUBBER = mat("Int Rubber", "1A1B1F", 0.9)
HAZARD = mat("Int Hazard", "E2A21B", 0.5)
GLASS = mat("Shell Window", "0E141C", 0.05, 0.2, emit="FFC27A", strength=1.4)
GREEN_GLASS = mat("Shell Greenhouse Glass", "9FD8B4", 0.05, 0.0, emit="CFF5D8", strength=0.4, alpha=0.4)
DOME_GLASS = mat("Shell Dome Glass", "A8C8E0", 0.05, 0.0, emit="CFE6FF", strength=0.2, alpha=0.3)
SOLAR = mat("Shell Solar", "1C2A55", 0.2, 0.4)
CYAN = mat("Shell Light Cyan", "000000", 0.4, emit="3FE0FF", strength=14)
AMBER = mat("Shell Light Amber", "000000", 0.4, emit="FFB547", strength=12)
WARM = mat("Int Light Warm", "000000", 0.4, emit="FFE9CF", strength=10)
RED = mat("Shell Signal Red", "6E1E22", 0.5)
SOIL = mat("Int Soil", "3B2A22", 0.95)
SAND = mat("Preview Regolith", "A65F43", 0.95)
PROP = mat("Preview Prop", "5B5F68", 0.5, 0.4)

pieces = {}
current = None

def piece(name):
    global current
    c = bpy.data.collections.new(name); scene.collection.children.link(c); pieces[name] = c
    current = c
    return c

def link(o):
    for c in list(o.users_collection):
        c.objects.unlink(o)
    current.objects.link(o)
    return o

def mesh_obj(bm, name, materials, smooth=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in materials:
        me.materials.append(m)
    if smooth:
        for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); current.objects.link(o)
    return o

def box(name, material, size, loc, rot=(0, 0, 0), bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    o = mesh_obj(bm, name, [material])
    o.location = loc; o.rotation_euler = rot
    return o

def fbox(name, material, size, frame, loc, rot=(0, 0, 0), bevel=0.0):
    """A box in a module's local frame (rotates with the module)."""
    o = box(name, material, size, (0, 0, 0), bevel=bevel)
    from mathutils import Euler
    o.matrix_world = frame @ Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4()
    return o

def cylinder(name, material, r, depth, loc, rot=(0, 0, 0), verts=32):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=depth)
    o = mesh_obj(bm, name, [material], smooth=True)
    o.location = loc; o.rotation_euler = rot
    return o

def text(body, material, size, loc, rot):
    curve = bpy.data.curves.new("Sign", "FONT"); curve.body = body
    curve.size = size; curve.extrude = 0.012; curve.align_x = "CENTER"; curve.align_y = "CENTER"
    o = bpy.data.objects.new("Sign " + body, curve); current.objects.link(o)
    o.data.materials.append(material); o.location = loc; o.rotation_euler = rot
    return o

def boolean(target, cutters, op="DIFFERENCE"):
    for c in cutters:
        m = target.modifiers.new("Cut", "BOOLEAN"); m.operation = op; m.object = c; m.solver = "EXACT"
    bpy.context.view_layer.objects.active = target
    for m in list(target.modifiers):
        if m.type == "BOOLEAN":
            with bpy.context.temp_override(object=target):
                bpy.ops.object.modifier_apply(modifier=m.name)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)

def sweep(name, material, frame, section, path, closed=False):
    """Sweep a closed 2D section [(a, b)] along a path of (point, side, up) frames -> solid."""
    bm = bmesh.new()
    rings = []
    for p, side, up in path:
        rings.append([bm.verts.new(p + side * a + up * b) for a, b in section])
    n = len(section)
    for j in range(len(rings) - (0 if closed else 1)):
        r0, r1 = rings[j], rings[(j + 1) % len(rings)]
        for i in range(n):
            bm.faces.new((r0[i], r0[(i + 1) % n], r1[(i + 1) % n], r1[i]))
    if not closed:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(bm, name, [material])
    o.matrix_world = frame
    return o

def strip(name, material, frame, pts, w=0.05, h=0.03):
    """A thin lit strip along local points (z-up section)."""
    path = []
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(len(pts) - 1, i + 1)]
        t = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((0, 1, 0))
        side = t.cross(up).normalized(); up = side.cross(t).normalized()
        path.append((p, side, up))
    sec = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    return sweep(name, material, frame, sec, path)

# ------------------------------------------------------------------ module vault (local frame)
# Local frame: u across (-w..w), v along the vault (0..L), z up. Interior is |u| <= w, 0..L.
T_WALL = 0.38

def arch(w, spring, apex, n=2.4, steps=22):
    """Inner arch from (+w, spring) over the apex to (-w, spring)."""
    pts = []
    for i in range(steps + 1):
        t = math.pi * i / steps
        c, s = math.cos(t), math.sin(t)
        pts.append((w * math.copysign(abs(c) ** (2 / n), c), spring + (apex - spring) * abs(s) ** (2 / n)))
    return pts

def section(w, spring, apex, layer=0.12):
    """Inner and outer profile of the printed shell, same parameter per index (for quads).
    Walls carry printed layer beads (outside strongly, inside faintly)."""
    inner, outer = [], []
    walls = [i * layer for i in range(int(spring / layer) + 1)]
    right = [(w, z) for z in walls]
    top = arch(w, spring, apex)
    left = [(-w, z) for z in reversed(walls)]
    path = right + top[1:-1] + left
    for k, (u, z) in enumerate(path):
        wall = abs(abs(u) - w) < 1e-6
        bead = (0.03 if (k % 2 == 0) else 0.0) if wall else 0.0
        inner.append((u - math.copysign(0.006 if k % 2 == 0 and wall else 0.0, u), z))
        # outer: offset outward along the profile normal; walls batter in slightly with height
        if wall:
            lean = 0.06 * z / spring
            outer.append((u + math.copysign(T_WALL + bead - lean, u), z))
        else:
            dx, dz = u, (z - spring) * 1.4
            ln = math.hypot(dx, dz) or 1
            outer.append((u + dx / ln * (T_WALL - 0.06), z + dz / ln * (T_WALL - 0.06)))
    # foundation: drop both outer ends into the ground
    outer[0] = (outer[0][0], -0.3); outer[-1] = (outer[-1][0], -0.3)
    inner[0] = (inner[0][0], -0.3 + 0.0); inner[-1] = (inner[-1][0], -0.3)
    return inner, outer

def vault(name, material, frame, w, L, spring, apex, ext0=None):
    """Closed printed vault from v=-ext to L+ext (ext reaches over the end walls)."""
    inner, outer = section(w, spring, apex)
    ext = T_WALL
    bm = bmesh.new()
    def ring(v, prof):
        return [bm.verts.new((u, v, z)) for u, z in prof]
    e0 = ext if ext0 is None else ext0
    i0, o0 = ring(-e0, inner), ring(-e0, outer)
    i1, o1 = ring(L + ext, inner), ring(L + ext, outer)
    n = len(inner)
    for k in range(n - 1):
        bm.faces.new((i0[k], i1[k], i1[k + 1], i0[k + 1]))       # inner face
        bm.faces.new((o0[k], o0[k + 1], o1[k + 1], o1[k]))       # outer face
        bm.faces.new((i0[k], i0[k + 1], o0[k + 1], o0[k]))       # end rims
        bm.faces.new((i1[k], o1[k], o1[k + 1], i1[k + 1]))
    bm.faces.new((i0[0], o0[0], o1[0], i1[0]))                    # foundation soles
    bm.faces.new((i0[-1], i1[-1], o1[-1], o0[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(bm, name, [material], smooth=False)
    o.matrix_world = frame
    return o, inner

def end_wall(name, material, frame, w, spring, apex, v0, v1, door=None, window=None):
    """Printed end wall filling the arch between v0 and v1, with a doorway / window cut."""
    prof = [(w + 0.01, -0.3)] + arch(w + 0.01, spring, apex + 0.01) + [(-w - 0.01, -0.3)]
    bm = bmesh.new()
    a = [bm.verts.new((u, v0, z)) for u, z in prof]
    b = [bm.verts.new((u, v1, z)) for u, z in prof]
    bm.faces.new(a); bm.faces.new(list(reversed(b)))
    n = len(prof)
    for k in range(n):
        bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(bm, name, [material]); o.matrix_world = frame
    cutters = []
    if door:
        width, height = door
        c = fbox("cut", PRINT, (width, (v1 - v0) + 0.4, height + 0.31), frame, (0, (v0 + v1) / 2, (height - 0.31) / 2))
        cutters.append(c)
    if window:
        u, z0, z1, width = window
        c = fbox("cut", PRINT, (width, (v1 - v0) + 0.4, z1 - z0), frame, (u, (v0 + v1) / 2, (z0 + z1) / 2))
        cutters.append(c)
    if cutters:
        bpy.context.view_layer.update()
        boolean(o, cutters)
    return o

def portal(frame, v, facing, width=1.3, height=2.15, label=None, arrow=""):
    """Gunmetal chamfered portal frame, amber threshold, hazard jambs, cyan ID sign (inside face)."""
    f = frame
    t = 0.12
    sec = [(-0.09, -t), (0.09, -t), (0.09, t), (-0.09, t)]
    hw = width / 2 + 0.09
    ch = 0.28   # chamfered top corners
    pts = [Vector((hw, v, 0)), Vector((hw, v, height - ch + 0.09)), Vector((hw - ch, v, height + 0.09)),
           Vector((-hw + ch, v, height + 0.09)), Vector((-hw, v, height - ch + 0.09)), Vector((-hw, v, 0))]
    path = []
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(len(pts) - 1, i + 1)]
        tng = (b - a).normalized()
        side = Vector((0, 1, 0)); up = tng.cross(side).normalized()
        path.append((p, up, side))
    o = sweep("Portal frame", METAL, f, sec, path)
    fbox("Threshold", HAZARD, (width, 0.36, 0.02), f, (0, v, 0.01))
    fbox("Threshold light", AMBER, (width - 0.1, 0.03, 0.012), f, (0, v - 0.16 * facing, 0.022))
    fbox("Threshold light", AMBER, (width - 0.1, 0.03, 0.012), f, (0, v + 0.16 * facing, 0.022))
    if label:
        sv = v - 0.14 * facing
        body = (arrow + " " + label).strip()
        plate = max(1.3, 0.115 * len(body) + 0.3)
        fbox("Sign plate", METAL, (plate, 0.03, 0.3), f, (0, sv, height + 0.42))
        rot = f.to_euler(); rot.x += math.radians(90); rot.z += math.radians(0 if facing > 0 else 180)
        s = text((arrow + " " + label).strip(), CYAN, 0.17, f @ Vector((0, sv - 0.02 * facing, height + 0.42)), rot)
        fbox("Sign underline", CYAN, (plate - 0.2, 0.012, 0.012), f, (0, sv - 0.02 * facing, height + 0.29))
    return o

def module_interior(frame, w, L, spring, apex, ribs=True, wainscot=True, tag=None):
    """Deck, ribs, cove lights, ceiling spine, wainscot, floor guide lights."""
    f = frame
    # Deck: dark plates with gaps on a base, rubber runner down the middle.
    fbox("Deck base", RUBBER, (2 * w, L, 0.04), f, (0, L / 2, -0.04))
    plate = 1.0
    k = 0
    v = 0.0
    while v < L - 0.01:
        lv = min(plate, L - v)
        fbox("Deck plate", DECK, (2 * w - 0.06, lv - 0.02, 0.012), f, (0, v + lv / 2, -0.006))
        v += lv
    fbox("Runner", RUBBER, (0.7, L - 0.2, 0.004), f, (0, L / 2, 0.002))
    for side in (-1, 1):
        fbox("Guide light", AMBER, (0.025, L - 0.3, 0.01), f, (side * (w - 0.06), L / 2, 0.006))
        # Cove: a gunmetal ledge at the springline with a cyan strip in it.
        fbox("Cove ledge", METAL, (0.14, L, 0.08), f, (side * (w - 0.07), L / 2, spring - 0.04))
        fbox("Cove light", CYAN, (0.03, L - 0.1, 0.02), f, (side * (w - 0.13), L / 2, spring + 0.005))
        if wainscot:
            fbox("Wainscot", PANEL, (0.03, L, 1.15), f, (side * (w - 0.015), L / 2, 0.575))
            fbox("Wainscot cap", METAL, (0.05, L, 0.04), f, (side * (w - 0.025), L / 2, 1.17))
            fbox("Kick plate", METAL, (0.035, L, 0.12), f, (side * (w - 0.02), L / 2, 0.06))
    # Ceiling spine: raceway with a warm light panel underneath.
    fbox("Spine", METAL, (0.5, L - 0.2, 0.12), f, (0, L / 2, apex - 0.06))
    fbox("Spine light", WARM, (0.32, L - 0.6, 0.015), f, (0, L / 2, apex - 0.125))
    if ribs:
        prof = arch(w - 0.01, spring, apex - 0.01, steps=22)
        nrib = max(2, int(L / 1.5))
        for r in range(nrib + 1):
            vv = 0.12 + (L - 0.24) * r / nrib
            pts = [Vector((w - 0.01, vv, 0.0)), Vector((w - 0.01, vv, spring))] + \
                  [Vector((u, vv, z)) for u, z in prof[1:-1]] + [Vector((-w + 0.01, vv, spring)), Vector((-w + 0.01, vv, 0.0))]
            path = []
            for i, p in enumerate(pts):
                a = pts[max(0, i - 1)]; b = pts[min(len(pts) - 1, i + 1)]
                tng = (b - a).normalized(); side = Vector((0, 1, 0)); inward = side.cross(tng).normalized()
                path.append((p, side, inward))
            sweep("Rib", METAL, f, [(-0.06, 0.0), (0.06, 0.0), (0.06, 0.07), (-0.06, 0.07)], path)

def module_exterior(frame, w, L, spring, apex, fins=True, spine=True, berm=True, side_label=None):
    f = frame
    ow = w + T_WALL
    if fins:
        nf = max(2, int(L / 1.7))
        for r in range(nf + 1):
            vv = -0.1 + (L + 0.2) * r / nf
            for side in (-1, 1):
                # printed buttress fin: tapered wedge against the wall
                bm = bmesh.new()
                base_out, top_out, hgt = 0.75, 0.12, spring + 0.25
                th = 0.22
                ps = [(0, -0.3), (base_out, -0.3), (top_out, hgt), (0, hgt + 0.2)]
                a = [bm.verts.new((side * (ow - 0.05 + x), vv - th / 2, z)) for x, z in ps]
                b = [bm.verts.new((side * (ow - 0.05 + x), vv + th / 2, z)) for x, z in ps]
                bm.faces.new(a); bm.faces.new(list(reversed(b)))
                for i in range(4):
                    bm.faces.new((a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i]))
                bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
                o = mesh_obj(bm, "Buttress", [PRINT_DARK]); o.matrix_world = f
        for side in (-1, 1):
            fbox("Fin light", CYAN, (0.03, L + 0.4, 0.03), f, (side * (ow + 0.02), L / 2, 0.12))
    if spine:
        # raised clerestory spine: gunmetal ridge with solar louvres and an amber ridge light
        fbox("Ridge cap", METAL, (0.9, L + 0.3, 0.16), f, (0, L / 2, apex + T_WALL - 0.02))
        fbox("Ridge light", AMBER, (0.04, L + 0.2, 0.03), f, (0, L / 2, apex + T_WALL + 0.075))
        for r in range(int(L / 1.1)):
            vv = 0.55 + r * 1.1
            for side in (-1, 1):
                fbox("Solar louvre", SOLAR, (0.9, 0.95, 0.04), f, (side * 0.95, vv, apex + T_WALL - 0.16),
                    rot=(0, side * math.radians(-14), 0))
    if berm:
        for side in (-1, 1):
            bm = bmesh.new()
            ps = [(0, -0.3), (1.1, -0.3), (0.0, 0.45)]
            a = [bm.verts.new((side * (ow - 0.02 + x), -0.2, z)) for x, z in ps]
            b = [bm.verts.new((side * (ow - 0.02 + x), L + 0.2, z)) for x, z in ps]
            bm.faces.new(a); bm.faces.new(list(reversed(b)))
            for i in range(3):
                bm.faces.new((a[i], a[(i + 1) % 3], b[(i + 1) % 3], b[i]))
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            o = mesh_obj(bm, "Regolith berm", [PRINT_DARK]); o.matrix_world = f
    # Neon kit after the hub: lit ribs over the vault (cyan and amber alternating, like the dome's
    # spines), a glowing window ribbon down each side, and a light halo round both end arches.
    nr = max(2, int(L / 1.7))
    for r in range(nr + 1):
        vv = -0.1 + (L + 0.2) * r / nr
        prof = arch(ow + 0.03, spring, apex + T_WALL + 0.03, steps=26)
        strip("Neon rib", CYAN if r % 2 == 0 else AMBER, f, [Vector((u, vv, z)) for u, z in prof], 0.06, 0.04)
    for side in (-1, 1):
        fbox("Window ribbon", GLASS, (0.03, L - 0.4, 0.42), f, (side * (ow + 0.01), L / 2, 1.55))
        fbox("Ribbon light", AMBER, (0.04, L - 0.2, 0.035), f, (side * (ow + 0.03), L / 2, 1.83))
        fbox("Ribbon light", CYAN, (0.04, L - 0.2, 0.035), f, (side * (ow + 0.03), L / 2, 1.27))
    for vv in (-T_WALL - 0.02, L + T_WALL + 0.02):
        prof = arch(ow + 0.01, spring, apex + T_WALL + 0.01, steps=30)
        strip("End halo", CYAN, f, [Vector((u, vv, z)) for u, z in [(ow + 0.01, 0.05)] + prof + [(-ow - 0.01, 0.05)]], 0.07, 0.04)
    if side_label:
        rot = f.to_euler(); rot.x += math.radians(90); rot.z += math.radians(90)
        nf = max(2, int(L / 1.7)); gap = (L + 0.2) / nf          # between the first two fins
        mid, width = -0.1 + gap / 2, gap - 0.4
        t = text(side_label, CYAN, 0.34, f @ Vector((ow + 0.02, mid, spring - 0.05)), rot)
        bpy.context.view_layer.update()
        fit = (width - 0.2) / max(t.dimensions.x, 1e-3)
        if fit < 1:
            t.data.size *= fit
        fbox("Label plate", METAL, (0.03, width, 0.5), f, (ow + 0.005, mid, spring - 0.05))

def frame_for(rect, along_x):
    """Local frame whose v runs along the vault axis. Returns (frame, w, L)."""
    x0, z0, x1, z1 = rect
    if along_x:   # v along +x, u along -y (right-handed: u = v x z ... keep u = -Y)
        m = Matrix(((0, 1, 0, x0), (-1, 0, 0, (z0 + z1) / 2), (0, 0, 1, 0), (0, 0, 0, 1)))
        return m, (z1 - z0) / 2, x1 - x0
    m = Matrix(((1, 0, 0, (x0 + x1) / 2), (0, 1, 0, z0), (0, 0, 1, 0), (0, 0, 0, 1)))
    return m, (x1 - x0) / 2, z1 - z0

# ------------------------------------------------------------------ the plan
SPRING, APEX = 2.45, 3.55
DOOR_W, DOOR_H = 2.0, 2.4
GARAGE_DOOR = (5.0, 3.6)      # the hangar's outer door: the rover (3.45 m wide, 2.45 m tall) drives through
# name: rect, along_x, doors at v0 / v1 (label shown inside above it), extra
MODULES = {
    "Storage":    ((-16, -4, -7.5, 4), True, (None, "COMMAND-01"), "RESOURCES-02"),
    "Utilities":  ((7.5, -5.5, 21, 5.5), True, ("COMMAND-01", "GARAGE DOOR"), "GARAGE-03"),
    "Greenhouse": ((-3.5, -18, 3.5, -7), False, (None, "COMMAND-01"), "GREENHOUSE-04"),
    "Airlock":    ((-1.7, 13.25, 1.7, 16.75), False, ("COMMAND-01", "EXIT"), "AIRLOCK-05"),
}

# Garden grid: must match the plot spots in MarsWorldTasks.Build (2 beds x 2 columns x 6 rows).
CELL_X, CELL_Z, BED_TOP = 0.9, 1.3, 0.83
BED_COLS = ((-2.75, -1.85), (1.85, 2.75))
ROW_Z = [-15.55 + r * CELL_Z for r in range(6)]

def garden():
    """Two raised planters, each a gunmetal grid of 12 soil cells (one plot per cell), with a cyan
    edge light, amber cell markers and a grow-light bar over each bed."""
    for cols in BED_COLS:
        x0, x1 = cols[0] - CELL_X / 2, cols[1] + CELL_X / 2
        z0, z1 = ROW_Z[0] - CELL_Z / 2, ROW_Z[-1] + CELL_Z / 2
        cx, cz, w, d = (x0 + x1) / 2, (z0 + z1) / 2, x1 - x0, z1 - z0
        box("Planter", PRINT_DARK, (w + 0.12, d + 0.12, BED_TOP - 0.06), (cx, cz, (BED_TOP - 0.06) / 2), bevel=0.03)
        box("Planter kick", METAL, (w + 0.14, d + 0.14, 0.1), (cx, cz, 0.05))
        for c in cols:
            for z in ROW_Z:
                box("Soil", SOIL, (CELL_X - 0.08, CELL_Z - 0.08, 0.03), (c, z, BED_TOP - 0.03))
        for k in range(3):     # rails between and around the columns
            x = x0 + k * CELL_X
            box("Grid rail", METAL, (0.05, d + 0.05, 0.05), (x, cz, BED_TOP))
        for k in range(7):     # rails between and around the rows
            z = z0 + k * CELL_Z
            box("Grid rail", METAL, (w + 0.05, 0.05, 0.05), (cx, z, BED_TOP))
        for side in (-1, 1):
            box("Planter light", CYAN, (0.025, d, 0.02), (cx + side * (w / 2 + 0.065), cz, BED_TOP - 0.12))
        for c in cols:          # amber marker at the aisle end of every cell
            for z in ROW_Z:
                box("Cell marker", AMBER, (0.12, 0.02, 0.012), (c, z - CELL_Z / 2 + 0.06, BED_TOP + 0.03))
        box("Grow bar", METAL, (0.16, d - 0.2, 0.06), (cx, cz, 2.35))
        box("Grow bar light", WARM, (0.12, d - 0.3, 0.015), (cx, cz, 2.315))
        for z in (z0 + 0.3, z1 - 0.3):
            box("Grow bar hanger", STEEL, (0.02, 0.02, 1.2), (cx, z, 2.95))


def slab(name, material, frame, quad, v0, v1):
    """A flat prism from a (u, z) outline, between v0 and v1, in a module's frame (hazard stripes)."""
    bm = bmesh.new()
    a = [bm.verts.new((u, v0, z)) for u, z in quad]; b = [bm.verts.new((u, v1, z)) for u, z in quad]
    bm.faces.new(a); bm.faces.new(list(reversed(b)))
    for k in range(len(quad)):
        bm.faces.new((a[k], a[(k + 1) % len(quad)], b[(k + 1) % len(quad)], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(bm, name, [material]); o.matrix_world = frame
    return o

def hangar(f, w, L):
    """The garage's grand door outside (a deep gunmetal portal with hazard-striped jambs, beacons, a
    light bar and a big GARAGE-03 sign) and the hangar floor inside (parking bay, charging pylon). The roll-up door itself is the game's (MarsWorldTasks)."""
    dw, dh = GARAGE_DOOR
    vo = L + T_WALL                     # outer face of the end wall
    jw, depth = 0.6, 0.7                # jamb width, how far the portal stands proud
    hw = dw / 2
    for side in (-1, 1):
        u = side * (hw + jw / 2)
        fbox("Hangar jamb", METAL, (jw, depth, dh + 0.9), f, (u, vo + depth / 2, (dh + 0.9) / 2), bevel=0.03)
        for k in range(9):   # diagonal amber and black chevrons on the jamb's face
            z = 0.15 + k * 0.42
            quad = [(u - jw / 2 + 0.04, z), (u + jw / 2 - 0.04, z + 0.2), (u + jw / 2 - 0.04, z + 0.4), (u - jw / 2 + 0.04, z + 0.2)]
            if side < 0: quad = [(2 * u - q[0], q[1]) for q in reversed(quad)]
            slab("Hangar chevron", HAZARD, f, quad, vo + depth, vo + depth + 0.015)
        fbox("Hangar beacon base", METAL, (0.3, 0.3, 0.12), f, (u, vo + depth / 2, dh + 0.96))
        cylinder_f = fbox("Hangar beacon", AMBER, (0.18, 0.18, 0.22), f, (u, vo + depth / 2, dh + 1.13), bevel=0.06)
        fbox("Hangar jamb light", CYAN, (0.03, 0.03, dh - 0.2), f, (side * (hw + 0.02), vo + depth - 0.05, dh / 2))
    fbox("Hangar header", METAL, (dw + 2 * jw + 0.4, depth + 0.1, 0.9), f, (0, vo + (depth + 0.1) / 2, dh + 0.45), bevel=0.03)
    fbox("Hangar light bar", CYAN, (dw, 0.04, 0.05), f, (0, vo + depth + 0.07, dh + 0.06))
    fbox("Hangar sign plate", RUBBER, (3.6, 0.04, 0.6), f, (0, vo + depth + 0.07, dh + 0.47))
    rot = f.to_euler(); rot.x += math.radians(90); rot.z += math.radians(180)
    text("GARAGE-03", AMBER, 0.46, f @ Vector((0, vo + depth + 0.1, dh + 0.47)), rot)
    for side in (-1, 1):   # amber guide lights out onto the apron
        for k in range(4):
            fbox("Apron light", AMBER, (0.25, 0.08, 0.02), f, (side * (hw - 0.2), vo + depth + 0.6 + k * 1.2, 0.01))
    # Inside: a parking bay outline and a charging pylon.
    bay_w, bay_l, bay_v = 4.2, 5.6, L * 0.55
    for (du, dv, su, sv) in ((0, -bay_l / 2, bay_w, 0.08), (0, bay_l / 2, bay_w, 0.08), (-bay_w / 2, 0, 0.08, bay_l), (bay_w / 2, 0, 0.08, bay_l)):
        fbox("Bay line", HAZARD, (su, sv, 0.006), f, (du, bay_v + dv, 0.006))
    for k in range(6):
        fbox("Bay chevron", HAZARD, (0.5, 0.1, 0.006), f, (0, bay_v - bay_l / 2 + 0.5 + k * 0.3, 0.006), rot=(0, 0, 0))
    fbox("Charger", PANEL, (0.6, 0.5, 1.7), f, (w - 0.6, bay_v - 1.5, 0.85), bevel=0.04)
    fbox("Charger screen", CYAN, (0.02, 0.36, 0.3), f, (w - 0.91, bay_v - 1.5, 1.35))
    fbox("Charger light", AMBER, (0.02, 0.4, 0.03), f, (w - 0.91, bay_v - 1.5, 0.95))
    fbox("Tool wall", PANEL, (0.06, 2.6, 1.5), f, (-w + 0.08, L * 0.3, 1.5))
    for k in range(6):
        fbox("Tool wall tool", METAL, (0.06, 0.12, 0.5 + 0.1 * (k % 3)), f, (-w + 0.13, L * 0.3 - 1.0 + k * 0.4, 1.5))

def build_module(name, rect, along_x, labels, tag):
    piece(name)
    f, w, L = frame_for(rect, along_x)
    spring, apex = SPRING, APEX
    if name == "Airlock": spring, apex = 2.3, 3.0
    if name == "Greenhouse": apex = 3.9
    if name == "Utilities": spring, apex = 3.6, 5.6   # the hangar: tall enough to feel like one
    shell, _ = vault(name + " shell", PRINT, f, w, L, spring, apex)
    shell.data.materials.append(PRINT_IN)
    # inner faces get the interior material: faces whose normal points to the axis
    inv = f.inverted()
    for p in shell.data.polygons:
        c = p.center
        if abs(c.x) < w + 0.02 and c.z > -0.25 and -0.05 < c.y < L + 0.05 and (abs(c.x) < w + 0.02):
            p.material_index = 1
    ends = []
    for which, (v0, v1) in enumerate(((-T_WALL, 0.0), (L, L + T_WALL))):
        lab = labels[which]
        door = (DOOR_W, DOOR_H) if lab else None
        if name == "Utilities" and which == 1: door = GARAGE_DOOR   # wide enough for the rover
        window = None
        if door is None and name != "Greenhouse":   # the greenhouse's back wall holds the tool shed
            window = (0.0, 1.0, 2.0, 1.6)
        ends.append(end_wall(name + " end", PRINT, f, w, spring, apex, v0, v1, door, window))
        if door:
            facing = -1 if which == 0 else 1
            vv = v0 if which == 0 else v1
            portal(f, 0.0 if which == 0 else L, facing, width=door[0], height=door[1], label=lab)
        if window:
            vv = (v0 + v1) / 2
            fbox("Window glass", GLASS, (1.6, 0.04, 1.0), f, (0, vv, 1.5))
            fbox("Window frame", METAL, (1.75, T_WALL + 0.06, 0.08), f, (0, vv, 0.98))
            fbox("Window frame", METAL, (1.75, T_WALL + 0.06, 0.08), f, (0, vv, 2.02))
    module_interior(f, w, L, spring, apex, wainscot=(name != "Greenhouse"))
    module_exterior(f, w, L, spring, apex, spine=(name != "Greenhouse"), berm=(name != "Greenhouse"),
                    side_label=tag)
    if name == "Greenhouse":
        # The vault above the springline is glass, held by printed arches outside.
        shell.data.materials.append(GREEN_GLASS)
        for p in shell.data.polygons:
            if p.center.z > spring + 0.15 and -0.05 < p.center.y < L + 0.05:
                p.material_index = 2
        nrib = int(L / 1.4)
        for r in range(nrib + 1):
            vv = 0.1 + (L - 0.2) * r / nrib
            prof = arch(w + T_WALL - 0.02, spring, apex + T_WALL - 0.02, steps=22)
            pts = [Vector((u, vv, z)) for u, z in prof]
            path = []
            for i, p_ in enumerate(pts):
                a_ = pts[max(0, i - 1)]; b_ = pts[min(len(pts) - 1, i + 1)]
                tng = (b_ - a_).normalized(); side_ = Vector((0, 1, 0)); out = tng.cross(side_).normalized()
                path.append((p_, side_, out))
            sweep("Glass arch", PRINT, f, [(-0.08, 0.0), (0.08, 0.0), (0.08, 0.14), (-0.08, 0.14)], path)
        fbox("Grow light", CYAN, (0.04, L - 0.4, 0.03), f, (0, L / 2, apex - 0.2))
        garden()
    if name == "Utilities":
        hangar(f, w, L)
    return shell

for name, (rect, along_x, labels, tag) in MODULES.items():
    build_module(name, rect, along_x, labels, tag)

# ------------------------------------------------------------------ tunnels (manifold, flat floor)
TW, TS, TA = 1.5, 2.4, 3.0        # half width, springline, apex inside
def tunnel(i, along_x, a, b):
    piece("Tunnel%d" % i)
    lo, hi = min(a, b), max(a, b)
    if along_x:
        f = Matrix(((0, 1, 0, lo), (-1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))
    else:
        f = Matrix(((1, 0, 0, 0), (0, 1, 0, lo), (0, 0, 1, 0), (0, 0, 0, 1)))
    L = hi - lo
    global T_WALL
    keep = T_WALL; T_WALL = 0.28
    o, _ = vault("Tunnel tube", PRINT, f, TW, L - 2 * 0.28, TS, TA)
    o.matrix_world = f @ Matrix.Translation((0, 0.28, 0))
    T_WALL = keep
    fbox("Deck base", RUBBER, (2 * TW, L, 0.04), f, (0, L / 2, -0.04))
    fbox("Deck plate", DECK, (2 * TW - 0.06, L - 0.02, 0.012), f, (0, L / 2, -0.006))
    for side in (-1, 1):
        fbox("Guide light", AMBER, (0.025, L - 0.1, 0.01), f, (side * (TW - 0.06), L / 2, 0.006))
        fbox("Cove light", CYAN, (0.03, L, 0.02), f, (side * (TW - 0.05), L / 2, TS))
    fbox("Spine light", WARM, (0.25, L - 0.2, 0.015), f, (0, L / 2, TA - 0.03))
    for r in range(int(L / 0.6) + 1):
        vv = 0.1 + (L - 0.2) * r / max(1, int(L / 0.6))
        prof = arch(TW + 0.33, TS, TA + 0.3, steps=16)
        pts = [Vector((TW + 0.33, vv, -0.2))] + [Vector((u, vv, z)) for u, z in prof] + [Vector((-TW - 0.33, vv, -0.2))]
        path = []
        for k, p in enumerate(pts):
            a_ = pts[max(0, k - 1)]; b_ = pts[min(len(pts) - 1, k + 1)]
            tng = (b_ - a_).normalized(); side_ = Vector((0, 1, 0)); out = tng.cross(side_).normalized()
            path.append((p, side_, out))
        sweep("Tunnel rib", PRINT_DARK, f, [(-0.06, 0), (0.06, 0), (0.06, 0.06), (-0.06, 0.06)], path)

T_ = 0.38
HUB_R_IN = 5.2
TUNNELS = [
    (True, -7 - T_, -HUB_R_IN - 0.3), (True, -14.5 - T_, -12 - T_ + 0.76), (True, -22 - T_ + 0.76, -19.5 - T_),
    (True, HUB_R_IN + 0.3, 7 + T_), (True, 12 + T_, 14.5 - T_),
    (False, -7 + T_, -HUB_R_IN - 0.3), (False, HUB_R_IN + 0.3, 7 - T_),
]
TUNNELS = [  # wall outer face to wall outer face: storage, utilities, greenhouse, airlock
    (True, -7.5 + T_, -HUB_R_IN - 0.2), (True, HUB_R_IN + 0.2, 7.5 - T_),
    (False, -7 + T_, -HUB_R_IN - 0.2), (False, HUB_R_IN + 0.2, 13.25 - T_),
]
for i, (ax, a, b) in enumerate(TUNNELS):
    tunnel(i, ax, a, b)

# ------------------------------------------------------------------ hub (MARSHA double shell)
piece("Hub")
H_TOP, R_BASE, R_TOP = 9.2, 6.05, 2.3

def shell_radius(z):
    t = (z - 0.45) / (H_TOP - 0.45)
    return R_TOP + (R_BASE - R_TOP) * (1 - min(1, max(0, t)) ** 2.2) ** 0.5

IN_SPRING, IN_TOP, IN_R_TOP = 3.0, 8.7, 1.9
def inner_radius(z):
    if z <= IN_SPRING: return HUB_R_IN
    t = (z - IN_SPRING) / (IN_TOP - IN_SPRING)
    return IN_R_TOP + (HUB_R_IN - IN_R_TOP) * (1 - t ** 1.8) ** 0.5

def lathe_closed(name, outer, inner, materials, steps=160):
    """Closed solid of revolution from an outer profile (bottom->top) and inner (top->bottom)."""
    prof = outer + inner
    bm = bmesh.new()
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * i / steps), r * math.sin(2 * math.pi * i / steps), z)) for r, z in prof]
             for i in range(steps)]
    n = len(prof)
    for i in range(steps):
        r0, r1 = rings[i], rings[(i + 1) % steps]
        for j in range(n):
            f = bm.faces.new((r0[j], r1[j], r1[(j + 1) % n], r0[(j + 1) % n]))
            f.material_index = 0 if j < len(outer) - 1 else 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(bm, name, materials, smooth=True)

outer = [(R_BASE + 0.04, -0.3)]
for i in range(73):
    z = 0.45 + i / 72 * (H_TOP - 0.45)
    outer.append((shell_radius(z) + (0.04 if i % 2 == 0 else 0.0), z))
inner = []
zs = [IN_TOP] + [IN_TOP - (IN_TOP - IN_SPRING) * k / 48 for k in range(1, 49)] + [IN_SPRING - k * 0.12 for k in range(1, int(IN_SPRING / 0.12) + 1)] + [-0.3]
for z in zs:
    bead = 0.012 if (len(inner) % 2 == 0 and 0.3 < z < IN_TOP - 0.3) else 0.0
    inner.append(((inner_radius(max(z, 0)) if z >= 0 else HUB_R_IN) + bead, z))
# close the oculus: the outer top meets the inner top through a ring
hub = lathe_closed("Hub shell", outer, inner, [PRINT, PRINT_IN])
# Four portals through the double wall (manifold now, so the cut is clean).
cut = []
for k in range(4):
    a = k * math.pi / 2
    c = box("cut", PRINT, (DOOR_W, 1.4, DOOR_H + 0.31), (math.cos(a) * 5.75, math.sin(a) * 5.75, (DOOR_H - 0.31) / 2), rot=(0, 0, a + math.pi / 2))
    cut.append(c)
bpy.context.view_layer.update()
boolean(hub, cut)
# A ring of 12 glass panels high in the dome, between the ribs, above the window ribbon.
WIN_Z0, WIN_Z1, WIN_W, WIN_N = 6.35, 8.1, 1.3, 12
cut = []
for k in range(WIN_N):
    a = k * 2 * math.pi / WIN_N
    cut.append(box("cut", PRINT, (WIN_W, 3.4, WIN_Z1 - WIN_Z0), (math.cos(a) * 4.6, math.sin(a) * 4.6, (WIN_Z0 + WIN_Z1) / 2), rot=(0, 0, a + math.pi / 2)))
bpy.context.view_layer.update()
boolean(hub, cut)
for k in range(WIN_N):
    a = k * 2 * math.pi / WIN_N
    bm = bmesh.new(); rows = []
    for i in range(9):
        z = WIN_Z0 - 0.1 + (WIN_Z1 - WIN_Z0 + 0.2) * i / 8
        r = shell_radius(z) - 0.06; h = (WIN_W / 2 + 0.12) / r
        rows.append([bm.verts.new((r * math.cos(a + h * j), r * math.sin(a + h * j), z)) for j in (-1, 0, 1)])
    for i in range(8):
        for j in range(2):
            bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
    mesh_obj(bm, "Dome glass", [DOME_GLASS], smooth=True)
    # Gunmetal border round each opening, outside and in, hugging the shell.
    rad, tan = Vector((math.cos(a), math.sin(a), 0)), Vector((-math.sin(a), math.cos(a), 0))
    for R, off in ((shell_radius, 0.04), (inner_radius, -0.04)):
        at = lambda t, z: rad * ((R(z) + off) ** 2 - t * t) ** 0.5 + tan * t + Vector((0, 0, z))
        e, hw = 0.07, WIN_W / 2 + 0.07
        for t in (-hw, hw):
            strip("Dome window frame", METAL, Matrix.Identity(4), [at(t, WIN_Z0 - e + (WIN_Z1 - WIN_Z0 + 2 * e) * i / 8) for i in range(9)], 0.14, 0.07)
        for z in (WIN_Z0 - e, WIN_Z1 + e):
            strip("Dome window frame", METAL, Matrix.Identity(4), [at(-hw - e + 2 * (hw + e) * i / 6, z) for i in range(7)], 0.14, 0.07)

# Exterior kit kept from v1 (the part you liked): deck, window ribbon, light spines, halo, mast.
def ringmesh(name, material, R, r, z, seg=128):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=seg, minor_segments=8, location=(0, 0, z))
    o = bpy.context.object; o.name = name; o.data.materials.append(material)
    return link(o)

def lathe(profile, name, material, steps=160):
    bm = bmesh.new()
    rings_ = [[bm.verts.new((r * math.cos(2 * math.pi * i / steps), r * math.sin(2 * math.pi * i / steps), z)) for r, z in profile]
              for i in range(steps)]
    for i in range(steps):
        r0, r1 = rings_[i], rings_[(i + 1) % steps]
        for j in range(len(profile) - 1):
            bm.faces.new((r0[j], r1[j], r1[j + 1], r0[j + 1]))
    return mesh_obj(bm, name, [material], smooth=True)

DECK_Z = 4.8
R0 = shell_radius(DECK_Z)
lathe([(R0 - 0.1, DECK_Z), (R0 + 0.75, DECK_Z), (R0 + 0.75, DECK_Z + 0.1), (R0 - 0.1, DECK_Z + 0.1), (R0 - 0.1, DECK_Z)], "Walkway deck", METAL)
ringmesh("Deck light", CYAN, R0 + 0.75, 0.03, DECK_Z - 0.02)
ringmesh("Deck rail", STEEL, R0 + 0.7, 0.035, DECK_Z + 1.0)
for k in range(28):
    a = math.radians(k * 360 / 28)
    cylinder("Rail post", STEEL, 0.025, 0.95, (math.cos(a) * (R0 + 0.7), math.sin(a) * (R0 + 0.7), DECK_Z + 0.55), verts=8)
lathe([(shell_radius(5.45) + 0.035, 5.45), (shell_radius(6.1) + 0.035, 6.1)], "Window ribbon", GLASS)
ringmesh("Ribbon light", AMBER, shell_radius(6.15) + 0.05, 0.04, 6.15)
ringmesh("Base light", CYAN, 6.3, 0.035, 0.12)
lathe([(R_TOP + 0.02, H_TOP), (R_TOP + 0.25, H_TOP + 0.25), (R_TOP - 0.1, H_TOP + 0.35)], "Oculus lip", METAL)
cylinder("Oculus glass", GLASS, R_TOP + 0.05, 0.12, (0, 0, H_TOP + 0.05), verts=64)
ringmesh("Oculus halo", AMBER, R_TOP + 0.12, 0.05, H_TOP + 0.34)
for k in range(4):
    a = math.radians(k * 90 + 45)
    pts = [Vector((math.cos(a) * (shell_radius(z) + 0.05), math.sin(a) * (shell_radius(z) + 0.05), z)) for z in [0.6 + i * (H_TOP - 1.0) / 39 for i in range(40)]]
    strip("Light spine", CYAN if k % 2 == 0 else AMBER, Matrix.Identity(4), pts, 0.07, 0.04)
lathe([(R_TOP + 0.6, H_TOP + 0.9), (R_TOP + 2.0, H_TOP + 0.75), (R_TOP + 2.0, H_TOP + 0.82), (R_TOP + 0.6, H_TOP + 0.97)], "Solar halo", SOLAR)
cylinder("Mast", METAL, 0.06, 2.4, (0.9, 0.3, H_TOP + 1.9), verts=12)
text("COMMAND-01", CYAN, 0.55, (math.cos(math.radians(45)) * (shell_radius(7.0) + 0.08), math.sin(math.radians(45)) * (shell_radius(7.0) + 0.08), 6.9),
     (math.radians(90), 0, math.radians(135)))

# Hub interior: deck rings, wainscot, radial ribs up the cone, halo light, oculus glow, portals.
lathe([(0, -0.04), (HUB_R_IN, -0.04), (HUB_R_IN, 0.0), (0, 0.0)], "Hub deck", DECK)
for r in (1.6, 3.2, 4.6):
    ringmesh("Deck seam light" if r == 1.6 else "Deck seam", AMBER if r == 1.6 else RUBBER, r, 0.015, 0.0)
def arc_solid(name, material, section, a0, a1, steps=40):
    """Closed (r, z) section swept around the axis from angle a0 to a1 (a solid with end caps)."""
    path = []
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        path.append((Vector((0, 0, 0)), Vector((math.cos(a), math.sin(a), 0)), Vector((0, 0, 1))))
    return sweep(name, material, Matrix.Identity(4), section, path)
GAP = math.radians(13)    # half-angle kept clear around each portal (about 1.2 m at the wall)
for k in range(4):
    a0, a1 = k * math.pi / 2 + GAP, (k + 1) * math.pi / 2 - GAP
    arc_solid("Wainscot", PANEL, [(HUB_R_IN - 0.05, 0.0), (HUB_R_IN - 0.01, 0.0), (HUB_R_IN - 0.01, 1.15), (HUB_R_IN - 0.05, 1.15)], a0, a1)
    arc_solid("Wainscot cap", METAL, [(HUB_R_IN - 0.07, 1.15), (HUB_R_IN - 0.01, 1.15), (HUB_R_IN - 0.01, 1.19), (HUB_R_IN - 0.07, 1.19)], a0, a1)
    arc_solid("Kick plate", METAL, [(HUB_R_IN - 0.06, 0.0), (HUB_R_IN - 0.01, 0.0), (HUB_R_IN - 0.01, 0.12), (HUB_R_IN - 0.06, 0.12)], a0, a1)
ringmesh("Cove light", CYAN, HUB_R_IN - 0.12, 0.025, IN_SPRING)
lathe([(HUB_R_IN - 0.02, IN_SPRING - 0.06), (HUB_R_IN - 0.2, IN_SPRING - 0.06), (HUB_R_IN - 0.2, IN_SPRING - 0.02), (HUB_R_IN - 0.02, IN_SPRING - 0.02)], "Cove ledge", METAL)
for k in range(12):
    a = math.radians(k * 30 + 15)
    pts = [Vector((math.cos(a) * (inner_radius(z) - 0.02), math.sin(a) * (inner_radius(z) - 0.02), z)) for z in [0.0] + [IN_SPRING + (IN_TOP - 0.2 - IN_SPRING) * i / 24 for i in range(25)]]
    strip("Hub rib", METAL, Matrix.Identity(4), pts, 0.14, 0.1)
# (No hanging halo ring: the spirit tree fills the dome; see garden_assets.oxygen_tree.)
cylinder("Oculus glow", WARM, IN_R_TOP - 0.1, 0.04, (0, 0, IN_TOP - 0.02), verts=64)
DIRS = ["GARAGE-03", "AIRLOCK-05 / EXIT", "RESOURCES-02", "GREENHOUSE-04"]   # +x, +y(game +z), -x, -y
for k in range(4):
    a = k * math.pi / 2
    fr = Matrix.Translation((math.cos(a) * HUB_R_IN, math.sin(a) * HUB_R_IN, 0)) @ Matrix.Rotation(a - math.pi / 2, 4, "Z")
    portal(fr, 0.0, 1, width=DOOR_W, height=DOOR_H, label=DIRS[k], arrow="")

# ------------------------------------------------------------------ preview props (not exported)
piece("PreviewProps")
# Reception: one round console, the floor left clear.
cylinder("Console ring", PROP, 0.9, 0.9, (0, 0, 0.45), verts=48)
# Storage: seed shelves along both walls, tool rack, food lockers, bunk at the far end.
for side in (-1, 1):
    for k in range(3):
        box("Seed shelf", PANEL, (1.6, 0.5, 1.9), (-14.6 + k * 2.3, side * 3.6, 0.95))
box("Tool rack", PROP, (0.4, 2.4, 1.8), (-15.7, 0, 0.9))
box("Bunk", PROP, (2.0, 0.9, 0.5), (-9.0, -3.4, 0.25))
# Text and curve to mesh (for FBX).
bpy.ops.object.select_all(action="DESELECT")
conv = [o for o in scene.objects if o.type in ("CURVE", "FONT")]
for o in conv: o.select_set(True)
if conv:
    bpy.context.view_layer.objects.active = conv[0]
    bpy.ops.object.convert(target="MESH")

# Ground and light for previews.
piece("Preview")
g = box("Ground", SAND, (160, 160, 0.1), (0, 0, -0.35))
world = bpy.data.worlds.get("Mars dusk") or bpy.data.worlds.new("Mars dusk")
scene.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (*srgb("7A5A6E"), 1); bg.inputs["Strength"].default_value = 0.5
sun_data = bpy.data.lights.new("Sun", "SUN"); sun_data.energy = 3.4; sun_data.color = srgb("FFD2AE"); sun_data.angle = math.radians(6)
sun = bpy.data.objects.new("Sun", sun_data); current.objects.link(sun); sun.rotation_euler = (math.radians(55), 0, math.radians(25))
cam_data = bpy.data.cameras.new("Camera"); cam = bpy.data.objects.new("Camera", cam_data); current.objects.link(cam); scene.camera = cam
print("built", {k: len(v.all_objects) for k, v in pieces.items()})


# ------------------------------------------------------------------ export
# Each piece becomes two meshes: "<piece> solid" (printed shell, end walls, tunnel tube, fins,
# berms, door frames: Unity gives it a mesh collider) and "<piece> trim" (everything else).
SOLID_KEYS = (" shell", " end", "Tunnel tube", "Buttress", "Regolith berm", "Portal frame", "Oculus glass", "Dome glass", "Hangar jamb", "Hangar header")

def export_all():
    os.makedirs(EXPORT, exist_ok=True)
    for name, coll in pieces.items():
        if name.startswith("Preview"):
            continue
        objs = [o for o in coll.all_objects if o.type == "MESH"]
        groups = {"solid": [o for o in objs if any(k in o.name for k in SOLID_KEYS)]}
        groups["trim"] = [o for o in objs if o not in groups["solid"]]
        joined = []
        for kind, members in groups.items():
            if not members:
                continue
            bpy.ops.object.select_all(action="DESELECT")
            for o in members:
                o.select_set(True)
            bpy.context.view_layer.objects.active = members[0]
            bpy.ops.object.join()
            o = bpy.context.view_layer.objects.active
            o.name = o.data.name = name + " " + kind
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            joined.append(o)
        bpy.ops.object.select_all(action="DESELECT")
        for o in joined:
            o.select_set(True)
        bpy.context.view_layer.objects.active = joined[0]
        bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT, name + ".fbx"), use_selection=True,
                                 object_types={"MESH"}, use_mesh_modifiers=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False)
    return sorted(os.listdir(EXPORT))

if globals().get("EXPORT_NOW"):
    print(export_all())
