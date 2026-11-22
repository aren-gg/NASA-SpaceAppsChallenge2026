using System.Collections.Generic;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>
    /// The outpost's modules, modelled in Blender (tools/blender/outpost_modules.py, exported to
    /// Assets/Resources/OutpostModules): one model per module and tunnel that is both the
    /// outside and the inside. Each is a closed printed shell whose inner face is the room's wall
    /// and ceiling, with the interior lining (deck, ribs, light coves, wainscot, door frames and
    /// lit signs) and the exterior kit (buttresses, ridge, berms, ID plates).
    ///
    /// Style: NASA printed habitats. The command hub is a MARSHA-style double shell (AI
    /// SpaceFactory, NASA 3D-Printed Habitat Challenge); the other modules are printed vaults
    /// after Mars Dune Alpha (ICON / BIG for NASA's CHAPEA). Inside, cyan marks information,
    /// amber marks the way and caution, warm white does the lighting.
    ///
    /// The models are authored in outpost-local metres around the game's floor rectangles, so
    /// the code-built furniture, tools, sliding doors and room logic fit inside unchanged. When
    /// the modules are present <see cref="MarsOutpostBuilder"/> stops drawing its own walls,
    /// roofs and tunnels. Each file holds a "&lt;piece&gt; solid" mesh (shell, end walls, tube,
    /// fins, berms, door frames), which gets a mesh collider, and a "&lt;piece&gt; trim" mesh.
    /// </summary>
    public static class OutpostModules
    {
        public const string ResourceFolder = "OutpostModules";

        private static readonly string[] Pieces =
        {
            "Hub", "Storage", "Utilities", "Greenhouse", "Airlock", "Tunnel0", "Tunnel1", "Tunnel2", "Tunnel3",
        };

        /// <summary>
        /// How the FBX files land in Unity: Blender's exporter plus Unity's importer turn the
        /// authored layout half a turn about the vertical, so it is turned back here.
        /// </summary>
        public static readonly Quaternion ImportFix = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>True when the module models are in Resources (the hub is the one that must be there).</summary>
        public static bool Available => Resources.Load<GameObject>(ResourceFolder + "/Hub") != null;

        /// <summary>Places every module under <paramref name="root"/>. Returns how many were found.</summary>
        public static int Attach(Transform root, List<Object> owned)
        {
            var materials = new Dictionary<string, Material>();
            int placed = 0;
            foreach (string name in Pieces)
            {
                GameObject prefab = Resources.Load<GameObject>(ResourceFolder + "/" + name);
                if (prefab == null) continue;
                GameObject module = Object.Instantiate(prefab, root, false);
                module.name = name + " module";
                module.transform.localPosition = Vector3.zero;
                module.transform.localRotation = ImportFix;
                module.transform.localScale = Vector3.one;
                foreach (MeshRenderer renderer in module.GetComponentsInChildren<MeshRenderer>())
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = Material(materials, slots[i] != null ? slots[i].name : "", owned);
                    renderer.sharedMaterials = slots;
                    bool solid = renderer.gameObject.name.EndsWith(" solid");
                    renderer.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.TwoSided
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                    if (!solid) continue;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable)
                        renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                placed++;
            }
            return placed;
        }

        private static Material Material(Dictionary<string, Material> cache, string imported, List<Object> owned)
        {
            string key = Key(imported);
            if (cache.TryGetValue(key, out Material existing)) return existing;
            Material m;
            switch (key)
            {
                case "Shell Printed Dark": m = Lit(key, Hex(0x8C8278), 0.1f, 0f); break;
                case "Int Printed": m = Lit(key, Hex(0xCFC4B4), 0.1f, 0f); break;
                case "Int Panel": m = Lit(key, Hex(0xE9E6E0), 0.55f, 0f); break;
                case "Int Deck": m = Lit(key, Hex(0x474B53), 0.5f, 0.7f); break;
                case "Int Rubber": m = Lit(key, Hex(0x1A1B1F), 0.1f, 0f); break;
                case "Int Hazard": m = Lit(key, Hex(0xE2A21B), 0.5f, 0f); break;
                case "Shell Gunmetal": m = Lit(key, Hex(0x3A3D44), 0.6f, 0.8f); break;
                case "Shell Steel": m = Lit(key, Hex(0x8E949C), 0.7f, 0.9f); break;
                case "Shell Window": m = Lit(key, Hex(0x0E141C), 0.95f, 0.2f, Hex(0xFFC27A) * 1.2f); break;
                case "Shell Dome Glass": m = Glass(key, new Color(0.66f, 0.78f, 0.88f, 0.3f), Hex(0xCFE6FF) * 0.1f); break;
                case "Shell Greenhouse Glass": m = Glass(key, new Color(0.62f, 0.85f, 0.70f, 0.38f), Hex(0xCFF5D8) * 0.25f); break;
                case "Shell Solar": m = Lit(key, Hex(0x1C2A55), 0.8f, 0.4f); break;
                case "Shell Light Cyan": m = Lit(key, Color.black, 0.6f, 0f, Hex(0x3FE0FF) * 1.1f); break;
                case "Shell Light Amber": m = Lit(key, Color.black, 0.6f, 0f, Hex(0xFFB547) * 1.0f); break;
                case "Int Light Warm": m = Lit(key, Color.black, 0.6f, 0f, Hex(0xFFE9CF) * 1.5f); break;
                case "Shell Signal Red": m = Lit(key, Hex(0x6E1E22), 0.5f, 0f); break;
                case "Int Soil": m = Lit(key, Hex(0x3B2A22), 0.05f, 0f); break;
                default: m = Lit("Shell Printed", Hex(0xD8CFC2), 0.15f, 0f); break;   // printed basalt shell
            }
            owned.Add(m);
            cache[key] = m;
            return m;
        }

        /// <summary>The Blender material name without importer suffixes such as " (Instance)" or ".001".</summary>
        private static string Key(string imported)
        {
            string name = imported.Replace(" (Instance)", "");
            int dot = name.LastIndexOf('.');
            if (dot > 0 && dot >= name.Length - 4) name = name.Substring(0, dot);
            return name.Trim();
        }

        private static Material Lit(string name, Color color, float smoothness, float metallic, Color? emission = null)
        {
            Material m = MarsPalette.CreateMatteMaterial(name, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        private static Material Glass(string name, Color color, Color emission)
        {
            Material m = Lit(name, color, 0.9f, 0f, emission);
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        private static Color Hex(uint rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }
    }
}
