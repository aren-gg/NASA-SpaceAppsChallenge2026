"""Tiny low-poly mesh kit: extruded/lofted prisms, transforms, OBJ export, flat-shaded preview renderer."""
import numpy as np
from PIL import Image, ImageDraw, ImageFont

PALETTE = {
    "terracotta": "#B96D55",
    "dusty_rose": "#C78D87",
    "muted_olive": "#7D8865",
    "warm_orange": "#E6A06E",
    "deep_purple": "#40334F",
    "pale_pink": "#E9B9BC",
    "mauve": "#806886",
    "clay_sand": "#D8AC8F",
}


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], float) / 255.0


# ---------------------------------------------------------------- transforms
def rx(d):
    a = np.radians(d); c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def ry(d):
    a = np.radians(d); c, s = np.cos(a), np.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rz(d):
    a = np.radians(d); c, s = np.cos(a), np.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


class Part:
    def __init__(self, V, F, color):
        self.V = np.asarray(V, float)
        self.F = F          # list of polygons (index lists), CCW seen from outside
        self.color = color

    def xf(self, R=None, t=(0, 0, 0)):
        V = self.V if R is None else self.V @ np.asarray(R).T
        return Part(V + np.asarray(t, float), self.F, self.color)


class Model:
    def __init__(self, name):
        self.name = name
        self.parts = []

    def add(self, *parts):
        for p in parts:
            if isinstance(p, (list, tuple)):
                self.add(*p)
            else:
                self.parts.append(p)
        return self

    def triangles(self):
        """Return (tris[n,3,3], colors[n]) with degenerate triangles removed."""
        tris, cols = [], []
        for p in self.parts:
            for poly in p.F:
                for k in range(1, len(poly) - 1):
                    t = p.V[[poly[0], poly[k], poly[k + 1]]]
                    if np.linalg.norm(np.cross(t[1] - t[0], t[2] - t[0])) > 1e-10:
                        tris.append(t); cols.append(p.color)
        return np.array(tris), cols

    def transformed(self, R=None, t=(0, 0, 0)):
        m = Model(self.name)
        m.parts = [p.xf(R, t) for p in self.parts]
        return m


# ---------------------------------------------------------------- primitives
def _ccw(pts):
    pts = np.asarray(pts, float)
    x, y = pts[:, 0], pts[:, 1]
    area = 0.5 * np.sum(x * np.roll(y, -1) - np.roll(x, -1) * y)
    return pts if area > 0 else pts[::-1]


def loft(poly2d, z0, z1, color, top_scale=1.0, top_offset=(0, 0)):
    """Extrude a convex-or-concave simple 2D polygon (XY) along +Z from z0 to z1.
    Top ring is scaled about the polygon centroid (frustum / cone / taper)."""
    P = _ccw(poly2d)
    n = len(P)
    c = P.mean(0)
    T = (P - c) * (np.array(top_scale) if np.ndim(top_scale) else top_scale) + c + np.asarray(top_offset)
    V = [(x, y, z0) for x, y in P] + [(x, y, z1) for x, y in T]
    F = [list(range(n))[::-1], list(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        F.append([i, j, n + j, n + i])
    if np.all(np.abs(T - c) < 1e-9):          # cone: collapse top to an apex
        V = V[:n] + [(c[0] + top_offset[0], c[1] + top_offset[1], z1)]
        F = [list(range(n))[::-1]] + [[i, (i + 1) % n, n] for i in range(n)]
    return Part(V, F, color)


def ngon(n, r, phase=None):
    phase = np.pi / n if phase is None else phase
    a = np.arange(n) * 2 * np.pi / n + phase
    return np.c_[r * np.cos(a), r * np.sin(a)]


def rect(w, h, cx=0, cy=0):
    return np.array([[-w / 2, -h / 2], [w / 2, -h / 2], [w / 2, h / 2], [-w / 2, h / 2]]) + [cx, cy]


def chamfer_rect(w, h, c):
    x, y = w / 2, h / 2
    return np.array([[-x + c, -y], [x - c, -y], [x, -y + c], [x, y - c],
                     [x - c, y], [-x + c, y], [-x, y - c], [-x, -y + c]])


# Axis-aligned helpers (Y is up in the final model).
_TO_Y = rx(-90)   # maps local +Z extrusion to +Y


def box(size, center, color, taper=1.0, chamfer=0.0):
    """Box with optional chamfered vertical edges and top taper. size=(x,y,z)."""
    sx, sy, sz = size
    poly = chamfer_rect(sx, sz, chamfer) if chamfer > 0 else rect(sx, sz)
    # build along Z, then rotate so that extrusion is +Y; local y -> -Z, fix by negating
    p = loft(np.c_[poly[:, 0], -poly[:, 1]], -sy / 2, sy / 2, color, top_scale=taper)
    return p.xf(_TO_Y, center)


def cyl(n, r, h, color, center=(0, 0, 0), R=None, r_top=None, phase=None):
    """n-sided prism/frustum along local +Y, centred on `center` after rotation R."""
    s = 1.0 if r_top is None else (r_top / r if r > 0 else 0)
    p = loft(ngon(n, r, phase), -h / 2, h / 2, color, top_scale=s).xf(_TO_Y)
    return p.xf(R, center) if R is not None else p.xf(None, center)


def slab(poly2d, thickness, color, R=None, t=(0, 0, 0)):
    """Flat extruded profile in the XY plane, thickness along Z (centred)."""
    p = loft(poly2d, -thickness / 2, thickness / 2, color)
    return p.xf(R, t)


def rod(a, b, r, color, n=6):
    """n-sided rod between two 3D points."""
    a, b = np.asarray(a, float), np.asarray(b, float)
    d = b - a
    L = np.linalg.norm(d)
    y = d / L
    x = np.cross(y, [0, 0, 1]) if abs(y[2]) < 0.9 else np.cross(y, [1, 0, 0])
    x /= np.linalg.norm(x)
    z = np.cross(x, y)
    R = np.c_[x, y, z]
    return cyl(n, r, L, color).xf(R, (a + b) / 2)


# ---------------------------------------------------------------- OBJ export
def export_obj(model, path_obj, mtl_name="mars_palette.mtl"):
    tris, cols = model.triangles()
    lines = [f"# {model.name} - low-poly Mars expedition tool", "# Y-up, metres, flat normals, palette materials",
             f"mtllib {mtl_name}", f"o {model.name}"]
    vlines, nlines, flines = [], [], {}
    vi = 1
    for t, c in zip(tris, cols):
        n = np.cross(t[1] - t[0], t[2] - t[0]); n /= np.linalg.norm(n)
        for v in t:
            vlines.append(f"v {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}")
        nlines.append(f"vn {n[0]:.5f} {n[1]:.5f} {n[2]:.5f}")
        ni = len(nlines)
        flines.setdefault(c, []).append(f"f {vi}//{ni} {vi + 1}//{ni} {vi + 2}//{ni}")
        vi += 3
    lines += vlines + nlines
    for c, fl in flines.items():
        lines += [f"g {model.name}_{c}", f"usemtl {c}", "s off"] + fl
    with open(path_obj, "w") as f:
        f.write("\n".join(lines) + "\n")
    return len(tris)


def export_mtl(path):
    out = ["# Mars outpost 8-colour palette: matte, no textures"]
    for k, h in PALETTE.items():
        r, g, b = hex_rgb(h)
        out += [f"newmtl {k}", f"Kd {r:.4f} {g:.4f} {b:.4f}", "Ka 0 0 0", "Ks 0 0 0", "Ns 0", "d 1", "illum 1", ""]
    with open(path, "w") as f:
        f.write("\n".join(out))


# ---------------------------------------------------------------- renderer
LIGHT_DIR = None


def render(model, size=512, yaw=-38, pitch=28, ss=2, bg="#D4908A", floor_shadow=True, fit=0.68, shift=0.07):
    """Orthographic 3/4 render, warm low sun, purple ambient, projected floor shadow."""
    W = size * ss
    tris, cols = model.triangles()
    # model sits on floor y=min
    ymin = tris[..., 1].min()
    tris = tris - [0, ymin, 0]
    cam = rx(pitch) @ ry(yaw)

    sun = np.array([-0.55, 0.62, 0.62]); sun /= np.linalg.norm(sun)      # ~21 deg elevation
    sun_col = np.array([1.0, 0.86, 0.74])
    amb_col = np.array([0.52, 0.40, 0.55])
    fill = np.array([0.4, 0.5, -0.6]); fill /= np.linalg.norm(fill)

    # shadow polygons: project along -sun onto y=0
    shadow = tris - tris[..., 1:2] / sun[1] * sun
    P = tris.reshape(-1, 3) @ cam.T
    lo, hi = P[:, :2].min(0), P[:, :2].max(0)
    scale = fit * W / max(hi - lo)
    off = W / 2 - (lo + hi) / 2 * scale - np.array([0, shift * W])

    def proj(pts):
        q = pts @ cam.T
        return np.c_[q[:, 0] * scale + off[0], W - (q[:, 1] * scale + off[1]), q[:, 2]]

    img = np.ones((W, W, 3)) * hex_rgb(bg)
    # floor shadow
    if floor_shadow:
        mask = Image.new("L", (W, W), 0)
        d = ImageDraw.Draw(mask)
        for t in shadow:
            q = proj(t)
            d.polygon([tuple(p[:2]) for p in q], fill=255)
        from PIL import ImageFilter
        mask = mask.filter(ImageFilter.GaussianBlur(3 * ss))
        m = np.asarray(mask, float)[..., None] / 255.0
        img = img * (1 - 0.33 * m) + (img * np.array([0.62, 0.50, 0.66])) * 0.33 * m

    zbuf = np.full((W, W), -np.inf)
    for t, c in zip(tris, cols):
        n = np.cross(t[1] - t[0], t[2] - t[0]); n /= np.linalg.norm(n)
        q = proj(t)
        # backface cull in view space (camera looks down -z; visible if view-normal z > 0)
        vn = n @ cam.T
        if vn[2] <= 0:
            continue
        base = hex_rgb(PALETTE[c])
        lit = base * (amb_col * 0.95 + sun_col * 0.62 * max(0, n @ sun) + 0.12 * max(0, n @ fill) + 0.10 * max(0, n[1]))
        lit = np.clip(lit, 0, 1)
        x0, y0 = np.floor(q[:, :2].min(0)).astype(int)
        x1, y1 = np.ceil(q[:, :2].max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, W - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay, az), (bx, by, bz), (cx, cy, cz) = q
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12:
            continue
        w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
        w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        z = w0 * az + w1 * bz + w2 * cz
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        upd = inside & (z > sub)
        sub[upd] = z[upd]
        img[y0:y1 + 1, x0:x1 + 1][upd] = lit

    # warm grade + light vignette
    yy, xx = np.mgrid[0:W, 0:W] / W - 0.5
    vig = 1 - 0.10 * (xx ** 2 + yy ** 2) * 2
    img = np.clip(img * vig[..., None] * np.array([1.02, 0.99, 0.97]), 0, 1)
    im = Image.fromarray((img * 255).astype(np.uint8))
    return im.resize((size, size), Image.LANCZOS)
