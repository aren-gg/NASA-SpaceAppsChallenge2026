using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.World
{
    /// <summary>
    /// The eight base colours and the matte material recipe from
    /// docs/game-design-references.md (section 5).
    /// </summary>
    public static class MarsPalette
    {
        public static readonly Color Terracotta = Hex(0xB96D55);
        public static readonly Color DustyRose = Hex(0xC78D87);
        public static readonly Color MutedOlive = Hex(0x7D8865);
        public static readonly Color WarmOrange = Hex(0xE6A06E);
        public static readonly Color DeepPurple = Hex(0x40334F);
        public static readonly Color PalePink = Hex(0xE9B9BC);
        public static readonly Color Mauve = Hex(0x806886);
        public static readonly Color ClaySand = Hex(0xD8AC8F);

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        /// <summary>
        /// Solid colour, metallic 0, smoothness 0, no specular highlights, no
        /// environment reflections, no textures. Uses URP Lit when a render
        /// pipeline asset is active, otherwise the built-in Standard shader.
        /// </summary>
        public static Material CreateMatteMaterial(string materialName, Color color)
        {
            Material material = new Material(MatteShader());
            material.name = materialName;
            ApplyMatte(material, color);
            return material;
        }

        /// <summary>URP Lit when a render pipeline asset is active, otherwise the built-in Standard shader.</summary>
        public static Shader MatteShader()
        {
            Shader shader = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            return shader;
        }

        /// <summary>
        /// Puts an existing material on the shader for the active pipeline and reapplies
        /// the matte settings. Fixes materials made under the built-in pipeline (which
        /// render pink under URP). Returns true if anything changed.
        /// </summary>
        public static bool ConvertToMatte(Material material, Color color)
        {
            Shader shader = MatteShader();
            if (material.shader == shader && material.color == color) return false;
            material.shader = shader;
            ApplyMatte(material, color);
            return true;
        }

        private static void ApplyMatte(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_SpecularHighlights"))
            {
                material.SetFloat("_SpecularHighlights", 0f);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            if (material.HasProperty("_EnvironmentReflections"))
            {
                material.SetFloat("_EnvironmentReflections", 0f);
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            if (material.HasProperty("_GlossyReflections"))
            {
                material.SetFloat("_GlossyReflections", 0f);
                material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            }
            material.enableInstancing = true;
        }
    }
}
