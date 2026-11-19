using System;
using UnityEngine;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// The player's wardrobe choices: body frame, skin tone, hair, suit colours, helmet and
    /// backpack. Every option is an index into the lists below, so adding an item is one more
    /// entry in a list plus (for shapes) one more case in <see cref="MarsAstronautBody"/>.
    /// Saved in PlayerPrefs; <see cref="Changed"/> fires on every edit so the body rebuilds live.
    /// </summary>
    [Serializable]
    public sealed class AstronautLook
    {
        // ------------------------------------------------------------------ option lists

        /// <summary>Body frames. Both are the same height (the camera and reach depend on it); they differ in build.</summary>
        public static readonly string[] Frames = { "A: broad shoulders", "B: narrow waist" };

        public static readonly string[] SkinNames = { "Porcelain", "Light", "Tan", "Olive", "Brown", "Deep" };
        public static readonly Color[] SkinColors =
        {
            new Color(0.96f, 0.84f, 0.76f), new Color(0.92f, 0.76f, 0.63f), new Color(0.82f, 0.63f, 0.47f),
            new Color(0.71f, 0.53f, 0.38f), new Color(0.52f, 0.36f, 0.25f), new Color(0.33f, 0.22f, 0.16f),
        };

        public static readonly string[] HairStyles = { "Short crop", "Buzz", "Side part", "Bob", "Ponytail", "High bun", "Long", "Curly" };

        public static readonly string[] HairColorNames = { "Black", "Dark brown", "Brown", "Auburn", "Blonde", "Silver", "Rose", "Teal" };
        public static readonly Color[] HairColors =
        {
            new Color(0.10f, 0.09f, 0.09f), new Color(0.24f, 0.15f, 0.10f), new Color(0.42f, 0.27f, 0.16f),
            new Color(0.58f, 0.24f, 0.13f), new Color(0.86f, 0.70f, 0.42f), new Color(0.78f, 0.78f, 0.80f),
            new Color(0.90f, 0.52f, 0.62f), new Color(0.25f, 0.62f, 0.62f),
        };

        public static readonly string[] SuitNames = { "Lunar white", "Silver", "Sand", "Sky", "Navy", "Olive" };
        public static readonly Color[] SuitColors =
        {
            new Color(0.93f, 0.93f, 0.95f), new Color(0.74f, 0.76f, 0.80f), new Color(0.85f, 0.78f, 0.64f),
            new Color(0.66f, 0.80f, 0.92f), new Color(0.23f, 0.29f, 0.45f), new Color(0.45f, 0.50f, 0.36f),
        };

        public static readonly string[] AccentNames = { "Orange", "Red", "Blue", "Teal", "Purple", "Yellow", "Pink", "Green" };
        public static readonly Color[] AccentColors =
        {
            new Color(0.93f, 0.45f, 0.18f), new Color(0.82f, 0.18f, 0.20f), new Color(0.20f, 0.42f, 0.86f),
            new Color(0.15f, 0.66f, 0.64f), new Color(0.52f, 0.30f, 0.78f), new Color(0.95f, 0.78f, 0.20f),
            new Color(0.93f, 0.45f, 0.66f), new Color(0.30f, 0.68f, 0.30f),
        };

        public static readonly string[] VisorNames = { "Gold", "Silver", "Blue", "Rose", "Clear" };
        public static readonly Color[] VisorColors =
        {
            new Color(0.88f, 0.64f, 0.22f), new Color(0.72f, 0.76f, 0.82f), new Color(0.28f, 0.48f, 0.78f),
            new Color(0.86f, 0.52f, 0.56f), new Color(0.55f, 0.70f, 0.80f),
        };

        public static readonly string[] Helmets = { "Classic", "Bubble" };
        public static readonly string[] Backpacks = { "Slim pack", "Twin tanks", "Expedition pack" };
        public static readonly string[] IndoorHelmet = { "Off indoors", "Always on" };

        // ------------------------------------------------------------------ the choices

        public int frame;
        public int skin = 2;
        public int hairStyle;
        public int hairColor = 1;
        public int suit;
        public int accent;
        public int visor;
        public int helmet;
        public int backpack;
        public int indoorHelmet;

        public Color Skin { get { return SkinColors[Mod(skin, SkinColors.Length)]; } }
        public Color Hair { get { return HairColors[Mod(hairColor, HairColors.Length)]; } }
        public Color Suit { get { return SuitColors[Mod(suit, SuitColors.Length)]; } }
        public Color Accent { get { return AccentColors[Mod(accent, AccentColors.Length)]; } }
        public Color Visor { get { return VisorColors[Mod(visor, VisorColors.Length)]; } }
        public bool ClearVisor { get { return Mod(visor, VisorColors.Length) == VisorColors.Length - 1; } }
        public bool HelmetOffIndoors { get { return Mod(indoorHelmet, IndoorHelmet.Length) == 0; } }

        public static int Mod(int value, int count) { return ((value % count) + count) % count; }

        public AstronautLook Clone() { return (AstronautLook)MemberwiseClone(); }

        // ------------------------------------------------------------------ saving

        private const string Key = "astronaut.look";
        private static AstronautLook current;

        /// <summary>Fires after <see cref="Current"/> changes.</summary>
        public static event Action Changed;

        /// <summary>The player's saved look (loaded once from PlayerPrefs).</summary>
        public static AstronautLook Current
        {
            get
            {
                if (current == null)
                {
                    current = new AstronautLook();
                    string json = PlayerPrefs.GetString(Key, "");
                    if (!string.IsNullOrEmpty(json))
                    {
                        try { JsonUtility.FromJsonOverwrite(json, current); }
                        catch (Exception) { current = new AstronautLook(); }
                    }
                }
                return current;
            }
        }

        /// <summary>Replaces the current look, saves it and tells the body to rebuild.</summary>
        public static void Apply(AstronautLook look)
        {
            current = look.Clone();
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(current));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
