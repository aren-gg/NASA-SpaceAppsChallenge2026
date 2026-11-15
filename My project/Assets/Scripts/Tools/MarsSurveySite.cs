using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// A science stop near the camp, based on real kinds of rock and sand that Curiosity
    /// studied in Gale Crater. Each site gives one rock sample (rock hammer), one core
    /// (core drill) and research for a photo (survey camera). The model is decoration
    /// made in code; it is not mapped from data.
    /// </summary>
    public sealed class MarsSurveySite : MonoBehaviour
    {
        public enum Kind { LayeredMudstone, MineralVeins, PebbleStreambed, SandRipples, DarkDune, IronRidge, WindCarved }

        public static readonly List<MarsSurveySite> All = new List<MarsSurveySite>();

        public Kind kind;
        [Tooltip("Real metres around the centre where the core drill counts as being at the site.")]
        public float radiusMeters = 5f;
        public float discoverMeters = 12f;

        public bool Discovered, RockTaken, CoreTaken, Photographed;
        public bool Finished { get { return RockTaken && CoreTaken && Photographed; } }
        public int TasksDone { get { return (RockTaken ? 1 : 0) + (CoreTaken ? 1 : 0) + (Photographed ? 1 : 0); } }
        public string DisplayName { get { return Title(kind); } }
        public string Fact { get { return FactFor(kind); } }

        private MarsExpedition expedition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
        }

        public static string Title(Kind kind)
        {
            switch (kind)
            {
                case Kind.LayeredMudstone: return "Layered mudstone";
                case Kind.MineralVeins: return "White mineral veins";
                case Kind.PebbleStreambed: return "Pebble streambed";
                case Kind.SandRipples: return "Sand ripples";
                case Kind.DarkDune: return "Dark dune sand";
                case Kind.IronRidge: return "Iron-rich ridge";
                default: return "Wind-carved rocks";
            }
        }

        public static string FactFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.LayeredMudstone: return "Thin layers of mud settled here in a lake that filled Gale Crater billions of years ago.";
                case Kind.MineralVeins: return "Bright veins of calcium sulfate formed when groundwater flowed through cracks in the rock.";
                case Kind.PebbleStreambed: return "Rounded pebbles like these showed Curiosity that a stream once flowed here.";
                case Kind.SandRipples: return "Wind piles Mars sand into ripples, just like in deserts on Earth.";
                case Kind.DarkDune: return "Dark sand from the Bagnold Dunes, which slowly creep across Gale Crater.";
                case Kind.IronRidge: return "Rock rich in hematite, an iron mineral, like Vera Rubin Ridge that Curiosity climbed.";
                default: return "Rocks sand-blasted by the wind for millions of years into sharp shapes.";
            }
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        /// <summary>Places a ring of sites at walking distance around the start.</summary>
        public static void SpawnAround(MarsExpedition owner, Vector3 start, Vector3 forward, int seed)
        {
            System.Random random = new System.Random(seed);
            int count = System.Enum.GetValues(typeof(Kind)).Length;
            float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                float distance = 40f + i * 22f;                 // 40 m to about 170 m
                float angle = baseAngle + 25f + i * (360f / count) + (float)random.NextDouble() * 20f;
                Vector3 position = start;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    float a = (angle + attempt * 17f) * Mathf.Deg2Rad;
                    position = start + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * owner.Units(distance);
                    if (!owner.Terrain.IsInsideTile(position)) continue;
                    if (owner.Terrain.GetSlopeDegrees(position) < 12f) break;
                }
                position.y = owner.Terrain.GetGroundY(position);

                GameObject go = new GameObject("Survey Site: " + Title((Kind)i));
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * owner.Units(1f);
                MarsSurveySite site = go.AddComponent<MarsSurveySite>();
                site.kind = (Kind)i;
                site.expedition = owner;
                site.Build(new System.Random(seed + 97 * i));
            }
        }

        public static MarsSurveySite Nearest(Vector3 position, float maxUnits, bool unfinishedOnly = false)
        {
            MarsSurveySite best = null;
            float bestDistance = maxUnits;
            foreach (MarsSurveySite site in All)
            {
                if (unfinishedOnly && site.Finished) continue;
                Vector3 d = site.transform.position - position;
                d.y = 0f;
                float distance = d.magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = site;
                }
            }
            return best;
        }

        /// <summary>True if a world position is within the site's working radius.</summary>
        public bool Contains(Vector3 position)
        {
            Vector3 d = position - transform.position;
            d.y = 0f;
            return d.magnitude <= radiusMeters * transform.lossyScale.x;
        }

        private void Build(System.Random random)
        {
            LowPolyMesh mesh = new LowPolyMesh();
            Paint main = Paint.Mauve, second = Paint.Terracotta;
            if (kind == Kind.IronRidge) { main = Paint.DeepPurple; second = Paint.Terracotta; }
            if (kind == Kind.DarkDune) { main = Paint.DeepPurple; second = Paint.Mauve; }

            // An outcrop of a few chunky boulders.
            int boulders = kind == Kind.SandRipples || kind == Kind.DarkDune ? 2 : 4;
            for (int i = 0; i < boulders; i++)
            {
                float a = (float)random.NextDouble() * Mathf.PI * 2f;
                float r = (float)random.NextDouble() * 1.2f;
                float size = 0.35f + (float)random.NextDouble() * 0.5f;
                mesh.Boulder(new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r), size, 0.7f,
                             (float)random.NextDouble() * 360f, random, i % 2 == 0 ? main : second);
            }

            switch (kind)
            {
                case Kind.LayeredMudstone:
                    for (int i = 0; i < 5; i++)
                    {
                        float w = 2.2f - i * 0.32f;
                        mesh.Box(new Vector3(1.4f, 0.08f + i * 0.15f, -0.6f), new Vector3(w, 0.15f, w * 0.6f),
                                 Quaternion.Euler(0f, 12f * i, 0f), i % 2 == 0 ? Paint.ClaySand : Paint.Terracotta);
                    }
                    break;
                case Kind.MineralVeins:
                    mesh.Box(new Vector3(1.3f, 0.3f, -0.5f), new Vector3(1.6f, 0.6f, 1.1f), Quaternion.Euler(0f, 20f, 4f), Paint.Terracotta);
                    for (int i = 0; i < 4; i++)
                    {
                        mesh.Box(new Vector3(1.3f + (i - 1.5f) * 0.3f, 0.62f, -0.5f), new Vector3(0.05f, 0.05f, 1.15f),
                                 Quaternion.Euler(0f, 20f + i * 9f, 0f), Paint.PalePink);
                    }
                    break;
                case Kind.PebbleStreambed:
                    for (int i = 0; i < 28; i++)
                    {
                        float a = (float)random.NextDouble() * Mathf.PI * 2f;
                        float r = 0.5f + (float)random.NextDouble() * 2f;
                        float size = 0.05f + (float)random.NextDouble() * 0.07f;
                        mesh.Boulder(new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r), size, 0.6f,
                                     (float)random.NextDouble() * 360f, random, i % 3 == 0 ? Paint.ClaySand : Paint.DustyRose);
                    }
                    break;
                case Kind.SandRipples:
                case Kind.DarkDune:
                    Paint sand = kind == Kind.DarkDune ? Paint.DeepPurple : Paint.ClaySand;
                    if (kind == Kind.DarkDune) mesh.Boulder(new Vector3(1.2f, 0.1f, 0f), 1.6f, 0.3f, 30f, random, Paint.DeepPurple);
                    for (int i = 0; i < 6; i++)
                    {
                        mesh.Box(new Vector3(-1.5f + i * 0.55f, 0f, 1.2f), new Vector3(0.16f, 0.16f, 2.6f),
                                 Quaternion.Euler(0f, 0f, 45f), sand);
                    }
                    break;
                case Kind.IronRidge:
                    mesh.Box(new Vector3(0.8f, 0.25f, -1f), new Vector3(3f, 0.5f, 0.9f), Quaternion.Euler(0f, 15f, 3f), Paint.Terracotta);
                    mesh.Box(new Vector3(0.8f, 0.55f, -1f), new Vector3(2.4f, 0.12f, 0.7f), Quaternion.Euler(0f, 15f, 3f), Paint.DeepPurple);
                    break;
                default:
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 basePoint = new Vector3(0.8f + i * 0.45f, 0.1f, -0.8f + (i % 2) * 0.6f);
                        mesh.Spike(basePoint, basePoint + new Vector3(0.7f, 0.5f + 0.15f * i, 0.1f), 0.22f, Paint.Mauve);
                    }
                    break;
            }

            // A short survey stake so sites can be found; its orange tip marks a science stop.
            mesh.Prism(new Vector3(-1.4f, 0f, 1.1f), 0.03f, 1.1f, 6, Quaternion.identity, Paint.Mauve);
            mesh.Prism(new Vector3(-1.4f, 1.1f, 1.1f), 0.06f, 0.12f, 6, Quaternion.identity, Paint.WarmOrange, 0f);
            mesh.Build("Outcrop", transform, true);
        }

        private void Update()
        {
            if (Discovered || expedition == null) return;
            Vector3 d = expedition.PlayerPosition - transform.position;
            d.y = 0f;
            if (d.magnitude <= expedition.Units(discoverMeters))
            {
                Discovered = true;
                expedition.Say("You found " + DisplayName + "! " + Fact, MarsExpedition.Tone.Good);
                MarsSfx.Play(MarsSfx.Sound.Success);
            }
        }
    }
}
