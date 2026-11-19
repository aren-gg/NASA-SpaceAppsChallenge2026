using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MarsTrainer.UI
{
    public static class UiTypeface
    {
        public const string ResourcePath = "Fonts & Materials/CounterStrike UI SDF";
        /// <summary>Barlow Condensed (SIL OFL 1.1, Jeremy Tribby), the UI text face: industrial signage, very legible. License: Assets/Fonts/BarlowCondensed-OFL.txt.</summary>
        public const string SourceResource = "Fonts/BarlowCondensed-Medium";
        public const string StrongResource = "Fonts/BarlowCondensed-SemiBold";
        /// <summary>VT323 (SIL OFL 1.1, Peter Hull), a retro CRT terminal face for readouts. License: Assets/Fonts/VT323-OFL.txt.</summary>
        public const string MonoResource = "Fonts/VT323-Regular";
        /// <summary>VT323 draws small for its size; readouts are scaled up by this much to match the text face.</summary>
        public const float MonoScale = 1.3f;
        private static TMP_FontAsset font, strong, mono;

        /// <summary>
        /// The UI text font: a dynamic TextMesh Pro asset made at runtime from the bundled Barlow Condensed TTF,
        /// so glyphs are added as text needs them. Symbols it lacks fall back to VT323, then Liberation Sans.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;
                font = Make(SourceResource, "Barlow Condensed SDF");
                if (font == null) font = Resources.Load<TMP_FontAsset>(ResourcePath);
                if (font == null) font = TMP_Settings.defaultFontAsset;
                return font;
            }
        }

        /// <summary>Barlow Condensed SemiBold, for titles and labels.</summary>
        public static TMP_FontAsset Strong
        {
            get
            {
                if (strong == null) strong = Make(StrongResource, "Barlow Condensed SemiBold SDF");
                return strong != null ? strong : Font;
            }
        }

        /// <summary>VT323, for numbers that change (clock, percentages, readouts): fixed-width, so they do not jitter.</summary>
        public static TMP_FontAsset Mono
        {
            get
            {
                if (mono == null) mono = Make(MonoResource, "VT323 SDF");
                return mono != null ? mono : Font;
            }
        }

        private static TMP_FontAsset Make(string resource, string assetName)
        {
            var source = Resources.Load<Font>(resource);
            if (source == null) return null;
            var asset = TMP_FontAsset.CreateFontAsset(source, 72, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            if (asset == null) return null;
            asset.name = assetName;
            var fallbacks = new List<TMP_FontAsset>();
            if (resource != MonoResource)
            {
                var monoSource = Resources.Load<Font>(MonoResource);
                var monoFallback = monoSource != null ? TMP_FontAsset.CreateFontAsset(monoSource, 72, 7,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024) : null;
                if (monoFallback != null) fallbacks.Add(monoFallback);
            }
            var liberation = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (liberation != null) fallbacks.Add(liberation);
            asset.fallbackFontAssetTable = fallbacks;
            return asset;
        }

        /// <summary>True when the text is mostly a number readout (digits, %, :, units), which reads best in the mono face.</summary>
        public static bool LooksNumeric(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 14) return false;
            int digits = 0, letters = 0;
            foreach (char c in text)
            {
                if (char.IsDigit(c)) digits++;
                else if (char.IsLetter(c)) letters++;
            }
            return digits > 0 && digits >= letters * 2;
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
