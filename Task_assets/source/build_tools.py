"""Mars expedition tool set - low-poly, flat-shaded, 8-colour palette.
Units: metres, Y up. Origin = the hand grip point for held tools, base centre for placed tools."""
import json, os, sys
import numpy as np
from lowpoly import *

TERRA, ROSE, OLIVE, ORANGE, PURPLE, PINK, MAUVE, SAND = (
    "terracotta", "dusty_rose", "muted_olive", "warm_orange", "deep_purple", "pale_pink", "mauve", "clay_sand")

TOOLS = []


def tool(name, title, system, use, hold, display=None):
    def deco(fn):
        TOOLS.append(dict(name=name, title=title, system=system, use=use, hold=hold, fn=fn,
                          display=display or (0, 0, 0)))
        return fn
    return deco


# ------------------------------------------------------------------ farming / health
@tool("regolith_shovel", "Regolith Shovel", "Health / Farming",
      "Dig regolith for greenhouse beds and shelter berms; clear drifted dust after storms.", "two-hand",
      display=(-90, 20, 0))
def regolith_shovel():
    m = Model("regolith_shovel")
    m.add(cyl(8, 0.022, 0.80, MAUVE, center=(0, 0.40, 0)))                     # shaft
    m.add(cyl(8, 0.030, 0.16, ORANGE, center=(0, 0.20, 0)))                    # grip sleeve
    # D-handle
    m.add(rod((0, 0.80, 0), (-0.065, 0.92, 0), 0.016, PURPLE),
          rod((0, 0.80, 0), (0.065, 0.92, 0), 0.016, PURPLE),
          rod((-0.08, 0.93, 0), (0.08, 0.93, 0), 0.022, ORANGE))
    m.add(cyl(6, 0.036, 0.07, PURPLE, center=(0, -0.01, 0)))                   # collar
    blade = np.array([[-0.13, 0.0], [0.13, 0.0], [0.12, -0.20], [0.0, -0.30], [-0.12, -0.20]])
    m.add(slab(blade, 0.025, SAND, t=(0, -0.04, 0)))
    m.add(slab(np.array([[-0.13, 0.0], [0.13, 0.0], [0.13, 0.035], [-0.13, 0.035]]), 0.04, ROSE, t=(0, -0.05, 0)))  # step
    return m


@tool("hydro_sprayer", "Hydroponic Sprayer", "Health / Farming",
      "Water and feed crop trays; efficiency improves with Farming research.", "one-hand")
def hydro_sprayer():
    m = Model("hydro_sprayer")
    m.add(cyl(8, 0.11, 0.24, OLIVE, center=(0, 0.12, 0)))                      # tank
    m.add(cyl(8, 0.115, 0.03, PURPLE, center=(0, 0.015, 0)))                   # foot ring
    m.add(cyl(8, 0.11, 0.05, OLIVE, center=(0, 0.265, 0), r_top=0.07))         # shoulder
    m.add(cyl(6, 0.045, 0.05, ORANGE, center=(0, 0.31, 0)))                    # cap
    # handle arch over the top
    m.add(rod((-0.08, 0.27, 0), (-0.06, 0.38, 0), 0.014, PURPLE),
          rod((-0.06, 0.38, 0), (0.06, 0.38, 0), 0.016, ORANGE),
          rod((0.06, 0.38, 0), (0.08, 0.27, 0), 0.014, PURPLE))
    # spout
    m.add(rod((0.09, 0.08, 0), (0.27, 0.27, 0), 0.016, MAUVE))
    m.add(cyl(6, 0.022, 0.05, ORANGE, R=rz(-45), center=(0.28, 0.285, 0), r_top=0.035))
    return m


@tool("oxygen_canister", "Portable O2 Canister", "Health",
      "Refill suit oxygen in the field; extends exploration range.", "one-hand")
def oxygen_canister():
    m = Model("oxygen_canister")
    m.add(cyl(8, 0.075, 0.40, ROSE, center=(0, 0.22, 0)))
    m.add(cyl(8, 0.075, 0.06, ROSE, center=(0, 0.45, 0), r_top=0.035))
    m.add(cyl(8, 0.078, 0.03, PURPLE, center=(0, 0.015, 0)))
    m.add(cyl(8, 0.078, 0.035, ORANGE, center=(0, 0.33, 0)))                   # band
    m.add(cyl(6, 0.022, 0.05, PURPLE, center=(0, 0.505, 0)))                   # neck
    m.add(cyl(6, 0.042, 0.022, ORANGE, center=(0, 0.535, 0)))                  # valve wheel
    m.add(box((0.05, 0.03, 0.035), (0.045, 0.505, 0), ORANGE))                 # outlet
    m.add(box((0.02, 0.22, 0.05), (-0.085, 0.24, 0), PURPLE))                  # strap
    return m


# ------------------------------------------------------------------ solar / energy
@tool("panel_brush", "Solar Panel Duster", "Solar",
      "Sweep dust off panels after a duststorm to restore energy gain.", "two-hand",
      display=(-90, 20, 0))
def panel_brush():
    m = Model("panel_brush")
    m.add(cyl(8, 0.02, 1.0, MAUVE, center=(0, 0.5, 0)))
    m.add(cyl(8, 0.028, 0.16, ORANGE, center=(0, 0.88, 0)))
    m.add(cyl(6, 0.03, 0.04, PURPLE, center=(0, 1.02, 0)))
    m.add(box((0.46, 0.05, 0.07), (0, -0.02, 0), PURPLE, chamfer=0.015))       # head
    m.add(cyl(6, 0.034, 0.06, PURPLE, center=(0, 0.02, 0)))                    # neck
    for i in range(7):                                                          # chunky bristle blocks
        x = -0.195 + i * 0.065
        m.add(box((0.05, 0.08, 0.055), (x, -0.085, 0), SAND, taper=(1.0, 1.0)))
    m.add(box((0.46, 0.02, 0.075), (0, -0.05, 0), ORANGE))
    return m


@tool("power_wrench", "Field Wrench", "Solar / Storage",
      "Repair panels and battery cabinets damaged by heat storms and cold nights.", "one-hand",
      display=(-90, 20, 0))
def power_wrench():
    m = Model("power_wrench")
    m.add(box((0.05, 0.30, 0.026), (0, 0.0, 0), MAUVE, chamfer=0.008))         # handle
    m.add(box((0.058, 0.13, 0.034), (0, -0.07, 0), ORANGE, chamfer=0.01))      # grip
    # open jaw head (two prongs + bridge), angled 15 deg like a real spanner
    head = Model("h")
    head.add(box((0.12, 0.05, 0.03), (0, 0, 0), SAND, chamfer=0.01))
    head.add(box((0.032, 0.075, 0.03), (-0.044, 0.06, 0), SAND, taper=(0.8, 1.0)))
    head.add(box((0.032, 0.075, 0.03), (0.044, 0.06, 0), SAND, taper=(0.8, 1.0)))
    m.add([p.xf(rz(15), (0, 0.17, 0)) for p in head.parts])
    # ring end
    m.add(cyl(8, 0.045, 0.03, SAND, R=rx(90), center=(0, -0.17, 0)))
    m.add(cyl(6, 0.02, 0.034, PURPLE, R=rx(90), center=(0, -0.17, 0)))
    return m


@tool("multimeter", "Energy Meter", "Storage / Efficiency",
      "Read battery charge and find energy leaks; unlocks Efficiency research points.", "one-hand",
      display=(0, 25, 0))
def multimeter():
    m = Model("multimeter")
    m.add(box((0.12, 0.19, 0.045), (0, 0.095, 0), SAND, chamfer=0.015))
    m.add(box((0.13, 0.20, 0.03), (0, 0.095, -0.012), ORANGE, chamfer=0.02))   # bumper
    m.add(box((0.085, 0.06, 0.01), (0, 0.145, 0.025), PURPLE))                 # screen
    for i in range(3):                                                         # charge bars on screen
        m.add(box((0.018, 0.022, 0.006), (-0.025 + i * 0.025, 0.145, 0.031), ORANGE))
    m.add(cyl(8, 0.024, 0.014, PURPLE, R=rx(90), center=(0, 0.075, 0.027)))   # dial
    m.add(box((0.006, 0.02, 0.006), (0, 0.083, 0.035), ORANGE))
    m.add(cyl(6, 0.008, 0.02, PURPLE, center=(-0.03, 0.025, 0.02)),
          cyl(6, 0.008, 0.02, ORANGE, center=(0.03, 0.025, 0.02)))            # probe jacks
    # probes clipped to the sides
    m.add(rod((-0.078, 0.03, 0.0), (-0.078, 0.17, 0.0), 0.009, PURPLE),
          rod((-0.078, 0.17, 0.0), (-0.078, 0.21, 0.0), 0.004, SAND),
          rod((0.078, 0.03, 0.0), (0.078, 0.17, 0.0), 0.009, ORANGE),
          rod((0.078, 0.17, 0.0), (0.078, 0.21, 0.0), 0.004, SAND))
    return m


# ------------------------------------------------------------------ protection / weather
@tool("dosimeter", "Radiation Dosimeter", "Protection",
      "Shows live dose rate (~210 uGy/day background, per Curiosity RAD); spikes during solar flares.", "one-hand",
      display=(0, 25, 0))
def dosimeter():
    m = Model("dosimeter")
    m.add(box((0.10, 0.16, 0.05), (0, 0.08, 0), ROSE, chamfer=0.015, taper=(0.9, 1.0)))
    m.add(box((0.072, 0.055, 0.01), (0, 0.115, 0.027), PURPLE))
    # warning triangle on screen (geometric, no text)
    tri = np.array([[-0.018, -0.014], [0.018, -0.014], [0.0, 0.017]])
    m.add(slab(tri, 0.006, ORANGE, t=(0, 0.115, 0.033)))
    for i in range(3):
        m.add(box((0.02, 0.014, 0.01), (-0.026 + i * 0.026, 0.05, 0.027), ORANGE if i == 1 else PURPLE))
    m.add(cyl(6, 0.022, 0.07, PURPLE, center=(0.03, 0.195, 0)))               # GM tube housing
    m.add(cyl(6, 0.026, 0.02, ORANGE, center=(0.03, 0.235, 0)))
    m.add(box((0.105, 0.03, 0.055), (0, 0.0, 0), MAUVE, chamfer=0.01))         # base grip
    return m


@tool("work_lamp", "Habitat Work Lamp", "Protection",
      "Light for cold nights and duststorm low-visibility; also used for light signalling.", "one-hand",
      display=(0, 0, -70))
def work_lamp():
    m = Model("work_lamp")
    m.add(cyl(8, 0.032, 0.20, PURPLE, center=(0, 0.10, 0)))                    # body
    for y in (0.04, 0.08, 0.12):
        m.add(cyl(8, 0.035, 0.012, MAUVE, center=(0, y, 0)))                   # grip rings
    m.add(cyl(8, 0.034, 0.08, ROSE, center=(0, 0.24, 0), r_top=0.062))         # flared head
    m.add(cyl(8, 0.064, 0.02, ORANGE, center=(0, 0.29, 0)))                    # bezel
    m.add(cyl(8, 0.052, 0.012, PINK, center=(0, 0.302, 0)))                    # lens (matte)
    m.add(box((0.02, 0.03, 0.02), (0.034, 0.17, 0), ORANGE))                   # switch
    m.add(cyl(8, 0.034, 0.02, ORANGE, center=(0, -0.01, 0)))                   # tail cap
    return m


@tool("comm_radio", "Comm Radio", "Protection / Exploration",
      "Talk to the habitat; receives solar-flare and duststorm alerts while exploring.", "one-hand",
      display=(0, 25, 0))
def comm_radio():
    m = Model("comm_radio")
    m.add(box((0.085, 0.17, 0.045), (0, 0.085, 0), MAUVE, chamfer=0.012))
    for i in range(4):                                                         # speaker slots
        m.add(box((0.055, 0.008, 0.008), (0, 0.045 + i * 0.016, 0.024), PURPLE))
    m.add(box((0.06, 0.035, 0.008), (0, 0.135, 0.024), PURPLE))                # mini screen
    m.add(box((0.022, 0.016, 0.006), (-0.012, 0.135, 0.029), ORANGE))
    m.add(box((0.012, 0.04, 0.02), (-0.048, 0.10, 0), ORANGE))                 # push-to-talk
    m.add(cyl(6, 0.012, 0.11, PURPLE, center=(0.025, 0.225, 0)))               # antenna
    m.add(cyl(6, 0.018, 0.02, ORANGE, center=(0.025, 0.285, 0)))
    m.add(cyl(8, 0.013, 0.02, ORANGE, center=(-0.022, 0.18, 0)))               # knob
    return m


# ------------------------------------------------------------------ exploration / research
@tool("rock_hammer", "Geologist's Rock Hammer", "Exploration / Research",
      "Break outcrops at survey sites to collect research points.", "one-hand",
      display=(-90, 20, 0))
def rock_hammer():
    m = Model("rock_hammer")
    m.add(cyl(8, 0.018, 0.36, MAUVE, center=(0, 0.10, 0)))
    m.add(cyl(8, 0.025, 0.14, ORANGE, center=(0, -0.01, 0)))
    m.add(cyl(8, 0.027, 0.02, PURPLE, center=(0, -0.09, 0)))
    m.add(box((0.06, 0.06, 0.045), (0, 0.29, 0), SAND, chamfer=0.012))         # eye block
    m.add(box((0.07, 0.05, 0.045), (-0.06, 0.29, 0), SAND, chamfer=0.01))      # striking face
    # pick end tapers to a point, along +X
    pick = loft(rect(0.045, 0.045), 0, 0.11, SAND, top_scale=(0.3, 0.35)).xf(ry(90), (0.03, 0.29, 0))
    m.add(pick)
    m.add(box((0.02, 0.064, 0.05), (-0.098, 0.29, 0), PURPLE))                 # face plate
    return m


@tool("core_drill", "Core Sample Drill", "Exploration / Research",
      "Drill regolith cores; core samples are the main research currency.", "one-hand",
      display=(0, 0, 0))
def core_drill():
    m = Model("core_drill")
    m.add(box((0.24, 0.10, 0.075), (0.02, 0.15, 0), ROSE, chamfer=0.02, taper=(1.0, 0.85)))  # body
    m.add(box((0.05, 0.08, 0.06), (-0.11, 0.155, 0), MAUVE, chamfer=0.012))   # rear motor cap
    m.add(box((0.055, 0.15, 0.06), (-0.04, 0.04, 0), MAUVE, chamfer=0.012).xf(None, (0, 0, 0)))  # grip
    m.add(box((0.02, 0.035, 0.03), (0.0, 0.08, 0), ORANGE))                    # trigger
    m.add(box((0.11, 0.05, 0.085), (-0.035, -0.055, 0), PURPLE, chamfer=0.01))  # battery
    for i in range(3):
        m.add(box((0.018, 0.022, 0.006), (-0.065 + i * 0.03, -0.055, 0.045), ORANGE))
    m.add(cyl(6, 0.03, 0.05, PURPLE, R=rz(-90), center=(0.165, 0.15, 0)))     # chuck
    m.add(cyl(6, 0.012, 0.20, SAND, R=rz(-90), center=(0.29, 0.15, 0)))       # hollow core bit
    m.add(cyl(6, 0.012, 0.04, ORANGE, R=rz(-90), center=(0.41, 0.15, 0), r_top=0.0))
    m.add(box((0.10, 0.012, 0.08), (0.03, 0.205, 0), ORANGE))                  # top stripe
    return m


@tool("sample_scoop", "Sample Scoop", "Exploration / Research",
      "Collect loose soil and dust samples for Farming and Health research.", "one-hand",
      display=(-90, 20, 0))
def sample_scoop():
    m = Model("sample_scoop")
    m.add(cyl(8, 0.016, 0.22, MAUVE, center=(0, 0.06, 0)))                     # shaft
    m.add(cyl(8, 0.022, 0.11, ORANGE, center=(0, -0.02, 0)))                   # grip
    m.add(cyl(8, 0.024, 0.015, PURPLE, center=(0, -0.08, 0)))                  # end cap
    m.add(cyl(6, 0.022, 0.03, PURPLE, center=(0, 0.18, 0)))                    # ferrule
    # open tray: floor in XY, walls rise along +Z, open at the far (+Y) end
    m.add(box((0.10, 0.14, 0.012), (0, 0.27, 0.0), SAND, chamfer=0.0))
    m.add(box((0.012, 0.14, 0.05), (-0.05, 0.27, 0.02), SAND),
          box((0.012, 0.14, 0.05), (0.05, 0.27, 0.02), SAND),
          box((0.10, 0.012, 0.05), (0, 0.20, 0.02), SAND))
    m.add(box((0.10, 0.012, 0.014), (0, 0.338, 0.0), ORANGE))                 # lip
    return m


@tool("sample_canisters", "Sample Canister Rack", "Exploration / Research",
      "Carry up to three sealed samples back to base to cash in research points.", "carried")
def sample_canisters():
    m = Model("sample_canisters")
    m.add(box((0.24, 0.05, 0.09), (0, 0.025, 0), PURPLE, chamfer=0.02))        # rack base
    m.add(box((0.24, 0.02, 0.09), (0, 0.11, 0), MAUVE, chamfer=0.02))          # top collar
    caps = [ORANGE, OLIVE, ROSE]
    for i, x in enumerate((-0.075, 0, 0.075)):
        m.add(cyl(8, 0.028, 0.17, PINK, center=(x, 0.11, 0)))
        m.add(cyl(8, 0.031, 0.03, caps[i], center=(x, 0.21, 0)))
        m.add(cyl(6, 0.014, 0.012, PURPLE, center=(x, 0.231, 0)))
    m.add(rod((-0.11, 0.12, 0), (-0.09, 0.30, 0), 0.01, PURPLE),
          rod((0.11, 0.12, 0), (0.09, 0.30, 0), 0.01, PURPLE),
          rod((-0.09, 0.30, 0), (0.09, 0.30, 0), 0.014, ORANGE))
    return m


@tool("survey_camera", "Survey Camera", "Exploration / Research",
      "Photograph landmarks and terrain features (MarsTrek-sourced sites) for research points.", "two-hand",
      display=(0, 60, 0))
def survey_camera():
    m = Model("survey_camera")
    m.add(box((0.17, 0.11, 0.08), (0, 0.055, 0), ROSE, chamfer=0.02))         # body
    m.add(box((0.05, 0.12, 0.085), (-0.075, 0.06, 0), MAUVE, chamfer=0.015))  # hand grip
    m.add(box((0.06, 0.035, 0.05), (0.025, 0.125, -0.005), PURPLE, chamfer=0.01))  # viewfinder hump
    m.add(cyl(8, 0.042, 0.04, PURPLE, R=rx(90), center=(0.02, 0.055, 0.06)))  # lens barrel
    m.add(cyl(8, 0.046, 0.025, ORANGE, R=rx(90), center=(0.02, 0.055, 0.09)))  # focus ring
    m.add(cyl(8, 0.04, 0.035, PURPLE, R=rx(90), center=(0.02, 0.055, 0.12), r_top=0.048))  # hood
    m.add(cyl(8, 0.03, 0.006, MAUVE, R=rx(90), center=(0.02, 0.055, 0.138)))  # glass (matte)
    m.add(cyl(6, 0.012, 0.015, ORANGE, center=(-0.07, 0.125, 0.01)))           # shutter
    m.add(box((0.03, 0.012, 0.03), (0.065, 0.115, 0.0), ORANGE))               # mode dial
    return m


@tool("survey_flag", "Survey Marker", "Exploration",
      "Plant to mark a discovered site on the map; doubles as a return waypoint.", "placed")
def survey_flag():
    m = Model("survey_flag")
    for a in (90, 210, 330):                                                    # tripod feet
        ang = np.radians(a)
        m.add(rod((0, 0.12, 0), (0.12 * np.cos(ang), 0.0, 0.12 * np.sin(ang)), 0.01, PURPLE))
        m.add(box((0.03, 0.012, 0.03), (0.12 * np.cos(ang), 0.006, 0.12 * np.sin(ang)), MAUVE))
    m.add(cyl(6, 0.019, 0.70, MAUVE, center=(0, 0.45, 0)))
    m.add(cyl(6, 0.022, 0.03, ORANGE, center=(0, 0.12, 0)))
    flag = np.array([[0, 0], [0.28, -0.075], [0, -0.16]])
    m.add(slab(flag, 0.008, ORANGE, t=(0.014, 0.78, 0)))
    m.add(slab(np.array([[0.0, 0.0], [0.07, -0.0175], [0.0, -0.038]]), 0.012, PURPLE, t=(0.03, 0.735, 0)))
    m.add(cyl(6, 0.022, 0.03, ORANGE, center=(0, 0.805, 0), r_top=0.0))
    return m


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "out"
    os.makedirs(f"{out}/models", exist_ok=True)
    os.makedirs(f"{out}/previews", exist_ok=True)
    export_mtl(f"{out}/models/mars_palette.mtl")
    manifest = []
    for t in TOOLS:
        m = t["fn"]()
        n = export_obj(m, f"{out}/models/{t['name']}.obj")
        tris, _ = m.triangles()
        ext = tris.reshape(-1, 3).max(0) - tris.reshape(-1, 3).min(0)
        dx, dy, dz = t["display"]
        disp = m.transformed(ry(dy) @ rx(dx) @ rz(dz))
        render(disp, 512).save(f"{out}/previews/{t['name']}.png")
        manifest.append(dict(id=t["name"], name=t["title"], research_path=t["system"], gameplay_use=t["use"],
                             hold=t["hold"], triangles=n, size_m=[round(float(v), 3) for v in ext],
                             model=f"models/{t['name']}.obj", preview=f"previews/{t['name']}.png"))
        print(f"{t['name']:20s} {n:5d} tris  size {np.round(ext, 2)}")
    json.dump(dict(palette=PALETTE, units="metres, Y-up", origin="grip point (held) / base centre (placed)",
                   tools=manifest), open(f"{out}/tools_manifest.json", "w"), indent=2)
