using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Stand-in solar panel. Dust covers its six cells one by one (clay-coloured cells)
    /// and cuts its output; weather damage stops it and blinks an orange warning light.
    /// Cleaned with the panel duster and repaired with the field wrench; aim at it to see
    /// its output. Simulated by <see cref="MarsBaseCamp"/>.
    /// </summary>
    public sealed class MarsSolarPanel : MonoBehaviour
    {
        public const float PeakWatts = 1000f;

        [Range(0f, 1f)] public float Dust;
        public bool Damaged;
        public float OutputWatts { get; set; }

        private MeshRenderer[] cells;
        private MeshRenderer warningLight;

        public void Build()
        {
            LowPolyMesh frame = new LowPolyMesh();
            frame.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f), Paint.DeepPurple);
            frame.Prism(new Vector3(0f, 0.08f, 0f), 0.06f, 0.95f, 6, Quaternion.identity, Paint.Mauve);
            Quaternion tilt = Quaternion.Euler(28f, 0f, 0f); // back edge raised so the cells face the sky and the camp (+Z)
            Vector3 pivot = new Vector3(0f, 1.08f, 0f);
            frame.Box(pivot, new Vector3(1.72f, 0.06f, 1.12f), tilt, Paint.Mauve);
            frame.Build("Frame", transform);

            cells = new MeshRenderer[6];
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 3 - 1) * 0.55f;
                float z = (i / 3 - 0.5f) * 0.52f;
                LowPolyMesh cell = new LowPolyMesh();
                cell.Box(Vector3.zero, new Vector3(0.5f, 0.03f, 0.46f), Paint.DeepPurple);
                GameObject go = cell.Build("Cell " + (i + 1), transform);
                go.transform.localPosition = pivot + tilt * new Vector3(x, 0.04f, z);
                go.transform.localRotation = tilt;
                cells[i] = go.GetComponent<MeshRenderer>();
            }

            LowPolyMesh light = new LowPolyMesh();
            light.Box(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), Paint.WarmOrange);
            GameObject lightObject = light.Build("Warning Light", transform, false, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.7f, 0.08f);
            warningLight = lightObject.GetComponent<MeshRenderer>();

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 1.2f);
        }

        public void Clean()
        {
            Dust = 0f;
        }

        private void Update()
        {
            if (cells == null) return;
            int dusty = Mathf.Clamp(Mathf.FloorToInt(Dust * 6.999f), 0, 6);
            for (int i = 0; i < cells.Length; i++)
            {
                Paint paint = Damaged && i == 4 ? Paint.Terracotta : (i < dusty ? Paint.ClaySand : Paint.DeepPurple);
                Material material = MarsLowPoly.Material(paint);
                if (cells[i].sharedMaterial != material) cells[i].sharedMaterial = material;
            }
            warningLight.enabled = Damaged && Mathf.Repeat(Time.time, 0.8f) < 0.45f;
        }
    }
}
