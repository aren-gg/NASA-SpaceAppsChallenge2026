# Mars Expedition Tools: low-poly asset pack

There are 15 handheld or placeable tools. They follow `game-design-references.md`: flat-shaded hard normals, 6–8-sided cylinders, chamfered boxes, eight palette colours only, no textures, and 116–344 triangles each.

## Unity setup
1. Copy `models/` (all `.obj` files and `mars_palette.mtl`) to `Assets/MarsTools/Models/`.
2. Copy `Editor/MarsToolsImporter.cs` to any `Editor/` folder.
3. Run **Tools ▸ Mars Tools ▸ Create Palette Materials**. This creates 8 URP Lit materials in `Assets/MarsTools/Materials/` with Metallic 0, Smoothness 0, and specular and reflections off. It then re-imports the models so they use those materials and keep their authored flat normals.

## Conventions
- Units are metres and Y is up. Sizes are realistic, with heads slightly chunky so they read at gameplay distance.
- Held tools have their origin at the hand grip, so parenting one to a hand bone needs no offset. Placed props (survey marker, canister rack) have their origin at the base centre.
- `tools_manifest.json` lists each tool's id, name, research path, gameplay use, grip type, triangle count and size.

## Regenerating or editing
`source/build_tools.py` defines each tool as a short function. To regenerate the models and previews after editing, run `python3 build_tools.py out` (it needs numpy and Pillow).
