using System;
using UnityEngine;

namespace MarsTrainer.UI
{
    /// <summary>The saved size of the walking HUD (vitals, sol block, location, warnings, plan and minimap), 80-130%. Stored in PlayerPrefs.</summary>
    public static class HudPrefs
    {
        public const float Min = 0.8f, Max = 1.3f, Step = 0.1f;
        private const string Key = "hud.scale";
        private static bool loaded;
        private static float scale = 1f;

        public static event Action Changed;

        public static float Scale
        {
            get
            {
                if (!loaded) { loaded = true; scale = Mathf.Clamp(PlayerPrefs.GetFloat(Key, 1f), Min, Max); }
                return scale;
            }
            set
            {
                float v = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, Min, Max);
                if (loaded && Mathf.Approximately(v, scale)) return;
                loaded = true; scale = v;
                PlayerPrefs.SetFloat(Key, v);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static string Text => Mathf.RoundToInt(Scale * 100f) + "%";
    }
}
