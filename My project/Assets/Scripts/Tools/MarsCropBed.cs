using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Stand-in raised crop bed. Fill it with two bags of regolith (shovel), water it
    /// (sprayer) and its six angular plants grow; harvest with the sprayer when grown.
    /// Wet soil shows as darker terracotta, dry soil as clay sand.
    /// </summary>
    public sealed class MarsCropBed : MonoBehaviour
    {
        [Tooltip("Real seconds from seedling to harvest while watered.")]
        public float growSeconds = 150f;
        [Tooltip("Real seconds one watering lasts.")]
        public float waterSeconds = 120f;

        [Range(0f, 1f)] public float Soil;
        [Range(0f, 1f)] public float Water;
        [Range(0f, 1f)] public float Growth;

        public bool NeedsSoil { get { return Soil < 0.999f; } }
        public bool Grown { get { return Growth >= 1f; } }

        private MarsExpedition expedition;
        private Transform soil;
        private MeshRenderer soilRenderer;
        private Transform[] plants;
        private bool thirstWarned;

        public void Build(MarsExpedition owner)
        {
            expedition = owner;
            const float W = 1.8f, D = 0.9f, H = 0.45f, T = 0.08f;
            LowPolyMesh frame = new LowPolyMesh();
            frame.Box(new Vector3(0f, 0.03f, 0f), new Vector3(W, 0.06f, D), Paint.Terracotta);
            frame.Box(new Vector3(0f, H * 0.5f, D * 0.5f - T * 0.5f), new Vector3(W, H, T), Paint.DustyRose);
            frame.Box(new Vector3(0f, H * 0.5f, -D * 0.5f + T * 0.5f), new Vector3(W, H, T), Paint.DustyRose);
            frame.Box(new Vector3(W * 0.5f - T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D - 2f * T), Paint.DustyRose);
            frame.Box(new Vector3(-W * 0.5f + T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D - 2f * T), Paint.DustyRose);
            frame.Build("Frame", transform);

            LowPolyMesh soilMesh = new LowPolyMesh();
            soilMesh.Box(new Vector3(0f, 0.5f, 0f), new Vector3(W - 2f * T, 1f, D - 2f * T), Paint.ClaySand);
            GameObject soilObject = soilMesh.Build("Soil", transform, false, false);
            soil = soilObject.transform;
            soil.localPosition = new Vector3(0f, 0.06f, 0f);
            soilRenderer = soilObject.GetComponent<MeshRenderer>();

            plants = new Transform[6];
            System.Random random = new System.Random(name.GetHashCode());
            for (int i = 0; i < plants.Length; i++)
            {
                LowPolyMesh plant = new LowPolyMesh();
                plant.Prism(Vector3.zero, 0.025f, 0.3f, 5, Quaternion.identity, Paint.MutedOlive);
                for (int leaf = 0; leaf < 3; leaf++)
                {
                    float a = (leaf / 3f + (float)random.NextDouble() * 0.2f) * Mathf.PI * 2f;
                    Vector3 basePoint = new Vector3(0f, 0.12f + leaf * 0.08f, 0f);
                    Vector3 tip = basePoint + new Vector3(Mathf.Cos(a) * 0.2f, 0.12f, Mathf.Sin(a) * 0.2f);
                    plant.Spike(basePoint, tip, 0.06f, Paint.MutedOlive);
                }
                GameObject go = plant.Build("Plant " + (i + 1), transform);
                go.transform.localPosition = new Vector3((i % 3 - 1) * 0.5f, H - 0.05f, (i / 3 - 0.5f) * 0.36f);
                go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                plants[i] = go.transform;
            }

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.4f, 0f);
            box.size = new Vector3(W, 0.8f, D);
            Refresh();
        }

        public void AddSoil(float amount)
        {
            Soil = Mathf.Clamp01(Soil + amount);
        }

        public void WaterUp()
        {
            Water = 1f;
            thirstWarned = false;
        }

        public void Harvest()
        {
            Growth = 0f;
        }

        private void Update()
        {
            if (plants == null) return;
            if (!NeedsSoil && Water > 0f && !Grown)
            {
                Growth = Mathf.Clamp01(Growth + Time.deltaTime / Mathf.Max(1f, growSeconds));
                Water = Mathf.Clamp01(Water - Time.deltaTime / Mathf.Max(1f, waterSeconds));
                if (Water <= 0f && !thirstWarned && expedition != null)
                {
                    thirstWarned = true;
                    expedition.Say(name + " is thirsty! Water it with " + MarsTool.Named("hydro_sprayer", "Sprayer") + ".");
                }
                if (Grown && expedition != null)
                {
                    expedition.Say(name + " is ready to harvest! Use " + MarsTool.Named("hydro_sprayer", "Sprayer") + " on it.", MarsExpedition.Tone.Good);
                    MarsSfx.Play(MarsSfx.Sound.Success);
                }
            }
            Refresh();
        }

        private void Refresh()
        {
            // Soil fills the bed up to just below the rim.
            float height = Mathf.Max(0.001f, Soil * 0.32f);
            soil.localScale = new Vector3(1f, height, 1f);
            soilRenderer.enabled = Soil > 0.01f;
            Material wet = MarsLowPoly.Material(Water > 0.02f ? Paint.Terracotta : Paint.ClaySand);
            if (soilRenderer.sharedMaterial != wet) soilRenderer.sharedMaterial = wet;

            float scale = NeedsSoil ? 0f : Mathf.Lerp(0.15f, 1f, Growth);
            for (int i = 0; i < plants.Length; i++)
            {
                plants[i].gameObject.SetActive(scale > 0f);
                // Plants sit on the soil surface, grow smoothly and sway a little; ripe plants bounce.
                float sway = Mathf.Sin(Time.time * 1.3f + i * 1.7f);
                float bounce = Grown ? 1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.time * 3f + i)) : 1f;
                plants[i].localScale = new Vector3(scale, scale * bounce, scale);
                plants[i].localRotation = Quaternion.Euler(sway * 4f, i * 60f, sway * 3f);
                Vector3 p = plants[i].localPosition;
                p.y = 0.06f + height;
                plants[i].localPosition = p;
            }
        }
    }
}
