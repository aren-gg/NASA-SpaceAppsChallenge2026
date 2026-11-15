using System;
using UnityEngine;

namespace MarsSurvival.TimeAndWeather
{
    /// <summary>
    /// The bundled solar-event list made by tools/data/fetch_donki_events.py from
    /// NASA CCMC's DONKI catalogue. Loaded from
    /// Assets/Data/Weather/Resources/donki_mars_events.json; see docs/data/weather-events.md.
    /// </summary>
    [Serializable]
    public sealed class MarsWeatherArchive
    {
        public const string ResourceName = "donki_mars_events";

        public string source;
        public string sourceUrl;
        public string retrievedUtc;
        public string windowStartUtc;
        public long windowStartUnix;
        public string windowEndUtc;
        public long windowEndUnix;
        public long downloadedBytes;
        public int downloadRequests;
        public SolarEvent[] events;

        [Serializable]
        public sealed class SolarEvent
        {
            /// <summary>"flare" (light, day side only), "cme" (predicted arrival) or "sep" (particles).</summary>
            public string kind;
            public string id;
            public string startUtc;
            /// <summary>When the event reaches Mars, Unix seconds UTC.</summary>
            public long startUnix;
            public float durationHours;
            /// <summary>Display intensity 0-1 for the weather visuals. Not a radiation dose.</summary>
            public float strength;
            public string label;
            public string basis;
            public string link;
        }

        private static MarsWeatherArchive cached;
        private static bool loaded;

        /// <summary>The archive, or null when the file is missing or unreadable.</summary>
        public static MarsWeatherArchive Load()
        {
            if (loaded) return cached;
            loaded = true;
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                Debug.LogWarning("No DONKI event archive found (Resources/" + ResourceName +
                                 ".json). Run: python tools/data/fetch_donki_events.py");
                return null;
            }
            try
            {
                cached = JsonUtility.FromJson<MarsWeatherArchive>(asset.text);
                if (cached != null && cached.events == null) cached.events = new SolarEvent[0];
            }
            catch (Exception exception)
            {
                Debug.LogError("The DONKI event archive could not be read: " + exception.Message);
                cached = null;
            }
            return cached;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            cached = null;
            loaded = false;
        }
    }
}
