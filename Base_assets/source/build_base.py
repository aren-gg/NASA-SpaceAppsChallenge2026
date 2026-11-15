"""Mars outpost base kit - low-poly, flat-shaded, 8-colour palette.
Units: metres, Y up, origin at ground centre of each piece. Doors face +Z. Tunnels run along X."""
import json, os, sys
import numpy as np
from lowpoly import *

TERRA, ROSE, OLIVE, ORANGE, PURPLE, PINK, MAUVE, SAND = (
    "terracotta", "dusty_rose", "muted_olive", "warm_orange", "deep_purple", "pale_pink", "mauve", "clay_sand")
APO = np.cos(np.pi / 8)          # octagon apothem / radius
TUNNEL_Y = 1.15                  # tunnel centre height
TUNNEL_R = 0.95


def door(m, z, width=1.7, height=2.1, center_x=0.0, R=None):
    """Chamfer-topped door frame + orange double door on a wall facing +Z at depth z."""
    w = width / 2
    frame = np.array([[-w, 0], [w, 0], [w, height * 0.83], [w * 0.62, height],
                      [-w * 0.62, height], [-w, height * 0.83]])
    inner = np.array([[-w * 0.7, 0], [w * 0.7, 0], [w * 0.7, height * 0.72], [w * 0.42, height * 0.86],
                      [-w * 0.42, height * 0.86], [-w * 0.7, height * 0.72]])
    parts = [slab(frame, 0.30, MAUVE, t=(center_x, 0.2, z)),
             slab(inner, 0.36, ORANGE, t=(center_x, 0.2, z)),
             box((0.05, height * 0.80, 0.38), (center_x, 0.2 + height * 0.40, z), PURPLE)]
    m.add([p.xf(R) for p in parts] if R is not None else parts)


def port_collar(m, x, sign):
    """Octagonal ring where a tunnel meets a module wall (on the +/-X face)."""
    m.add(cyl(8, TUNNEL_R * 1.18, 0.35, PINK, R=rz(-90), center=(x + sign * 0.12, TUNNEL_Y, 0), phase=np.pi / 8))


def foundation(m, r, h=0.2):
    m.add(cyl(8, r * 1.05, h, PURPLE, center=(0, h / 2, 0)))


# ------------------------------------------------------------------ pieces
def base_hub():
    """Command hub: the main octagonal module with the airlock door, roof cap and comms mast."""
    m = Model("base_hub")
    r, wall = 3.2, 2.4
    foundation(m, r)
    m.add(cyl(8, r, wall, ROSE, center=(0, 0.2 + wall / 2, 0)))
    m.add(cyl(8, r * 1.01, 0.18, PINK, center=(0, 0.2 + wall - 0.09, 0)))            # eave band
    m.add(cyl(8, r, 0.6, ROSE, center=(0, 0.2 + wall + 0.3, 0), r_top=2.3))          # chamfered roof
    m.add(cyl(8, 2.3, 0.25, ORANGE, center=(0, 0.2 + wall + 0.725, 0), r_top=2.0))   # orange roof cap
    top = 0.2 + wall + 0.85
    # comms mast
    m.add(box((0.6, 0.45, 0.6), (0.6, top + 0.22, -0.4), PURPLE, chamfer=0.1))
    m.add(cyl(6, 0.07, 2.0, MAUVE, center=(0.6, top + 1.4, -0.4)))
    m.add(cyl(6, 0.14, 0.18, ORANGE, center=(0.6, top + 2.45, -0.4)))
    m.add(box((0.5, 0.06, 0.06), (0.6, top + 1.8, -0.4), MAUVE))
    # airlock door + ramp on +Z
    z = r * APO
    door(m, z)
    ramp = np.array([[0, 0], [1.1, 0], [0, 0.2]])
    m.add(slab(ramp, 1.5, SAND, R=ry(-90), t=(0, 0, z + 0.15)))
    # vents and small windows on the angled faces
    for ang in (45, -45):
        a = np.radians(ang)
        d = r * APO + 0.02
        m.add(box((0.9, 0.45, 0.08), (0, 1.9, 0), PURPLE, chamfer=0.0).xf(ry(ang), (d * np.sin(a), 0, d * np.cos(a))))
    port_collar(m, r * APO, +1)
    port_collar(m, -r * APO, -1)
    return m


def base_quarters():
    """Living quarters: smaller octagon with a tall chamfered roof and two windows."""
    m = Model("base_quarters")
    r, wall = 2.4, 2.0
    foundation(m, r)
    m.add(cyl(8, r, wall, ROSE, center=(0, 0.2 + wall / 2, 0)))
    m.add(cyl(8, r * 1.01, 0.16, PINK, center=(0, 0.2 + wall - 0.08, 0)))
    m.add(cyl(8, r, 0.9, ROSE, center=(0, 0.2 + wall + 0.45, 0), r_top=1.45))
    m.add(cyl(8, 1.45, 0.12, PINK, center=(0, 0.2 + wall + 0.96, 0)))
    d = r * APO + 0.02
    for ang in (0, -45, 45):
        a = np.radians(ang)
        m.add(box((0.8, 0.5, 0.08), (0, 1.55, 0), PURPLE).xf(ry(ang), (d * np.sin(a), 0, d * np.cos(a))))
        m.add(box((0.9, 0.08, 0.12), (0, 1.26, 0), ORANGE).xf(ry(ang), (d * np.sin(a), 0, d * np.cos(a))))
    port_collar(m, r * APO, +1)
    return m


def base_greenhouse(length=6.0):
    """Enclosed greenhouse: long chamfered module along X; olive grow-light roof strip marks the farm."""
    m = Model("base_greenhouse")
    w, h = 2.2, 3.0
    prof = np.array([[-w, 0], [w, 0], [w, 1.8], [1.0, h], [-1.0, h], [-w, 1.8]])     # (z, y) profile
    # extrude profile along X: local x->world z, local z->world -x
    m.add(slab(prof * [1, 1], length, ROSE, R=ry(-90), t=(0, 0.2, 0)))
    rim = np.array([[-w - 0.12, 0], [w + 0.12, 0], [w + 0.12, 1.85], [1.05, h + 0.12], [-1.05, h + 0.12], [-w - 0.12, 1.85]])
    for x in (-length / 2, length / 2):
        m.add(slab(rim, 0.3, MAUVE, R=ry(-90), t=(x, 0.2, 0)))                       # end frames
    for x in np.linspace(-length / 2 + 1.5, length / 2 - 1.5, 3):
        m.add(slab(rim * [0.985, 0.99] + [0, 0.03], 0.18, PINK, R=ry(-90), t=(x, 0.2, 0)))  # structural ribs
    m.add(box((length + 0.3, 0.2, 2 * w + 0.4), (0, 0.1, 0), PURPLE))                 # foundation
    # roof grow-light strip and side windows
    m.add(box((length - 0.8, 0.1, 1.5), (0, 0.2 + h + 0.04, 0), OLIVE))
    for s in (+1, -1):
        for x in np.linspace(-length / 2 + 0.9, length / 2 - 0.9, 4):
            if s > 0 and x > 0:      # leave room for the side door
                continue
            m.add(box((0.85, 0.7, 0.08), (x, 1.35, s * (w + 0.02)), PURPLE))
        m.add(box((length - 0.4, 0.1, 0.1), (0, 0.85, s * (w + 0.03)), ORANGE))     # guide stripe
    # end window on the far (+X) wall
    m.add(box((0.08, 0.8, 1.6), (length / 2 + 0.16, 1.6, 0), PURPLE))
    # tunnel port on -X end
    m.add(cyl(8, TUNNEL_R * 1.18, 0.35, PINK, R=rz(-90), center=(-length / 2 - 0.15, TUNNEL_Y, 0), phase=np.pi / 8))
    # small exterior door on +Z side
    door(m, w + 0.05, width=1.3, height=1.8, center_x=1.4)
    return m


def base_tunnel(length=1.8):
    """Straight connecting tunnel along X with ribbed ends and support feet."""
    m = Model("base_tunnel")
    m.add(cyl(8, TUNNEL_R, length, PINK, R=rz(-90), center=(0, TUNNEL_Y, 0), phase=np.pi / 8))
    for x in (-length / 2 + 0.25, length / 2 - 0.25):
        m.add(cyl(8, TUNNEL_R * 1.08, 0.18, ROSE, R=rz(-90), center=(x, TUNNEL_Y, 0), phase=np.pi / 8))
    m.add(box((length * 0.5, 0.25, 1.2), (0, 0.125, 0), PURPLE))
    return m


def assemble():
    hub, q, g = 3.2 * APO, 2.4 * APO, 6.0
    t = 1.8
    m = Model("base_outpost")
    m.add(base_hub().parts)
    xq = -(hub + t + q)
    m.add(base_quarters().transformed(None, (xq, 0, 0)).parts)
    m.add(base_tunnel(t + 0.3).transformed(None, (-(hub + t / 2), 0, 0)).parts)
    xg = hub + t + g / 2 + 0.15
    m.add(base_greenhouse(g).transformed(None, (xg, 0, 0)).parts)
    m.add(base_tunnel(t + 0.3).transformed(None, (hub + t / 2, 0, 0)).parts)
    return m


PIECES = [
    ("base_outpost", "Assembled Outpost", assemble, "Complete base: quarters + tunnel + command hub + tunnel + greenhouse."),
    ("base_hub", "Command Hub", base_hub, "Main module and spawn point. Airlock door faces +Z; tunnel ports on +/-X."),
    ("base_quarters", "Living Quarters", base_quarters, "Rest/sleep module (tiredness recovery). Tunnel port on +X."),
    ("base_greenhouse", "Greenhouse", base_greenhouse, "Enclosed farm module; crops go inside. Tunnel port on -X, side door on +Z."),
    ("base_tunnel", "Connecting Tunnel", base_tunnel, "1.8 m straight tunnel; ends overlap module walls by ~0.15 m."),
]

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "base_out"
    os.makedirs(f"{out}/models", exist_ok=True)
    os.makedirs(f"{out}/previews", exist_ok=True)
    export_mtl(f"{out}/models/mars_palette.mtl")
    man = []
    for pid, title, fn, note in PIECES:
        m = fn()
        n = export_obj(m, f"{out}/models/{pid}.obj")
        tris, _ = m.triangles()
        ext = tris.reshape(-1, 3).max(0) - tris.reshape(-1, 3).min(0)
        render(m, 900 if pid == "base_outpost" else 512, yaw=-32, pitch=26,
               fit=0.86 if pid == "base_outpost" else 0.72, shift=0.04).save(f"{out}/previews/{pid}.png")
        man.append(dict(id=pid, name=title, notes=note, triangles=n, size_m=[round(float(v), 2) for v in ext],
                        model=f"models/{pid}.obj"))
        print(f"{pid:16s} {n:5d} tris size {np.round(ext, 2)}")
    json.dump(dict(units="metres, Y-up", origin="ground centre", door_facing="+Z", pieces=man),
              open(f"{out}/base_manifest.json", "w"), indent=2)
