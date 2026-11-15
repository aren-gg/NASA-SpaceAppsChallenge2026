# Mars Outpost Base Kit

These are modular low-poly base pieces in the same style as the tool pack, following `game-design-references.md`. They use hard normals, the eight-colour palette only, and no textures.

| Piece | Tris | Notes |
| --- | --- | --- |
| base_outpost | 1148 | Fully assembled base (quarters, tunnel, hub, tunnel, greenhouse), about 20 × 6 × 7 m |
| base_hub | 360 | Command hub: airlock door facing +Z, entry ramp, comms mast, tunnel ports on ±X |
| base_quarters | 240 | Living quarters (rest/tiredness); tunnel port on +X |
| base_greenhouse | 356 | Enclosed farm module with an olive grow-light roof, tunnel port on −X and a side door on +Z |
| base_tunnel | 96 | 1.8 m straight connector along X |

## Unity
- Put the files from `models/` in `Assets/MarsTools/Models/` and the script in an `Editor/` folder.
- Run **Tools ▸ Mars Tools ▸ Create Palette Materials**. If you already installed the tool pack, this script is the same one, so don't add it twice.
- Units are metres, Y is up, the origin is at the ground centre, and doors face +Z.
- To build a custom layout, place modules along X. The gap between walls is the tunnel length (1.8 m). Each wall sits at the module's apothem: 2.96 m for the hub, 2.22 m for the quarters and 3.0 m (half its length) for the greenhouse.
- Colliders: use a Mesh Collider (convex off) on the static modules, or a few Box Colliders for cheaper physics. The door is a separate orange material group, so you can find it for interaction triggers.

## Regenerating
Run `python3 source/build_base.py out` (needs numpy and Pillow). Sizes, tunnel length and the greenhouse length are parameters in the script.
