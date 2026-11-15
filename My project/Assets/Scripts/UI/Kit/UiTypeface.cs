using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MarsTrainer.UI
{
    public static class UiTypeface
    {
        public const string ResourcePath = "Fonts & Materials/CounterStrike UI SDF";
        /// <summary>Electrolize (SIL OFL 1.1, Cyreal / Gaslight), the UI typeface. License: Assets/Fonts/Electrolize-OFL.txt.</summary>
        public const string SourceResource = "Fonts/Electrolize-Regular";
        private static TMP_FontAsset font;

        /// <summary>
        /// The UI font: a dynamic TextMesh Pro asset made at runtime from the bundled Electrolize TTF, so glyphs are
        /// added as text needs them. Symbols Electrolize lacks fall back to Liberation Sans.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;
                var source = Resources.Load<Font>(SourceResource);
                if (source != null)
                {
                    font = TMP_FontAsset.CreateFontAsset(source, 72, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                    if (font != null)
                    {
                        font.name = "Electrolize SDF";
                        var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                        if (fallback != null) font.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                    }
                }
                if (font == null) font = Resources.Load<TMP_FontAsset>(ResourcePath);
                if (font == null) font = TMP_Settings.defaultFontAsset;
                return font;
            }
        }
#if UNITY_EDITOR
        public static void EnsureAsset()
        {
            var existing = Resources.Load<TMP_FontAsset>(ResourcePath);
            if (existing != null)
            {
                // This display font maps a soldier silhouette to the hyphen character.
                if (existing.characterTable.RemoveAll(character => character.unicode == '-') > 0)
                {
                    existing.ReadFontAssetDefinition();
                    UnityEditor.EditorUtility.SetDirty(existing);
                    UnityEditor.AssetDatabase.SaveAssets();
                }
                return;
            }
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/CounterStrikeUI.ttf");
            if (source == null) throw new System.InvalidOperationException("CounterStrikeUI.ttf is missing.");
            var font = TMP_FontAsset.CreateFontAsset(source);
            font.name = "CounterStrike UI SDF";
            var characters = new System.Text.StringBuilder();
            for (int i = 32; i < 127; i++) if (i != '*' && i != '@' && i != '-') characters.Append((char)i);
            font.TryAddCharacters(characters.ToString(), true);
            font.fallbackFontAssetTable = new List<TMP_FontAsset>
            {
                Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")
            };
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Resources/Fonts & Materials"))
                UnityEditor.AssetDatabase.CreateFolder("Assets/Resources", "Fonts & Materials");
            UnityEditor.AssetDatabase.CreateAsset(font, "Assets/Resources/" + ResourcePath + ".asset");
            foreach (var atlas in font.atlasTextures) UnityEditor.AssetDatabase.AddObjectToAsset(atlas, font);
            UnityEditor.AssetDatabase.AddObjectToAsset(font.material, font);
            UnityEditor.EditorUtility.SetDirty(font);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}
