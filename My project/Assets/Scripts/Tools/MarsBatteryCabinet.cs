using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Stand-in battery cabinet: a purple cabinet whose five orange bars show the charge
    /// (as in the art guide). Weather damage cuts its capacity until the field wrench
    /// fixes it.
    /// </summary>
    public sealed class MarsBatteryCabinet : MonoBehaviour
    {
        public float capacityKWh = 30f;
        [Tooltip("Fraction of capacity left while damaged.")]
        [Range(0.1f, 1f)] public float damagedCapacity = 0.6f;

        public float ChargeKWh { get; private set; }
        public bool Damaged;

        public float CapacityKWh { get { return capacityKWh * (Damaged ? damagedCapacity : 1f); } }
        public float Fraction { get { return CapacityKWh > 0f ? ChargeKWh / CapacityKWh : 0f; } }

        private MeshRenderer[] bars;
        private MeshRenderer warningLight;

        public void Build()
        {
            ChargeKWh = capacityKWh * 0.7f;

            LowPolyMesh body = new LowPolyMesh();
            body.Box(new Vector3(0f, 0.05f, 0f), new Vector3(1.0f, 0.1f, 0.7f), Paint.Mauve);
            body.Box(new Vector3(0f, 0.75f, 0f), new Vector3(0.9f, 1.3f, 0.6f), Paint.DeepPurple);
            body.Box(new Vector3(0f, 1.43f, 0f), new Vector3(0.96f, 0.06f, 0.66f), Paint.Mauve);
            // Cooling fins on the sides.
            for (int i = 0; i < 4; i++)
            {
                float y = 0.4f + i * 0.22f;
                body.Box(new Vector3(0.48f, y, 0f), new Vector3(0.06f, 0.08f, 0.5f), Paint.Mauve);
                body.Box(new Vector3(-0.48f, y, 0f), new Vector3(0.06f, 0.08f, 0.5f), Paint.Mauve);
            }
            body.Build("Cabinet", transform);

            bars = new MeshRenderer[5];
            for (int i = 0; i < bars.Length; i++)
            {
                LowPolyMesh bar = new LowPolyMesh();
                bar.Box(Vector3.zero, new Vector3(0.6f, 0.12f, 0.03f), Paint.WarmOrange);
                GameObject go = bar.Build("Charge Bar " + (i + 1), transform, false, false);
                go.transform.localPosition = new Vector3(0f, 0.32f + i * 0.2f, 0.31f);
                bars[i] = go.GetComponent<MeshRenderer>();
            }

            LowPolyMesh light = new LowPolyMesh();
            light.Box(Vector3.zero, new Vector3(0.12f, 0.1f, 0.12f), Paint.WarmOrange);
            GameObject lightObject = light.Build("Warning Light", transform, false, false);
            lightObject.transform.localPosition = new Vector3(0.3f, 1.51f, 0.2f);
            warningLight = lightObject.GetComponent<MeshRenderer>();

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(1.05f, 1.5f, 0.75f);
        }

        public void Charge(float kWh)
        {
            ChargeKWh = Mathf.Clamp(ChargeKWh + kWh, 0f, CapacityKWh);
        }

        private void Update()
        {
            if (bars == null) return;
            int lit = Mathf.CeilToInt(Fraction * bars.Length - 0.01f);
            for (int i = 0; i < bars.Length; i++)
            {
                Material material = MarsLowPoly.Material(i < lit ? Paint.WarmOrange : Paint.Mauve);
                if (bars[i].sharedMaterial != material) bars[i].sharedMaterial = material;
            }
            warningLight.enabled = Damaged && Mathf.Repeat(Time.time, 0.8f) < 0.45f;
        }
    }
}
