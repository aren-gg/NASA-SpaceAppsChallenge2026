using UnityEngine;
using UnityEngine.Rendering;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// A burst of small flat-shaded chips that fly out and fall under Mars gravity,
    /// shrinking away: feedback for digging, hammering, drilling and dusting.
    /// </summary>
    public sealed class MarsDebris : MonoBehaviour
    {
        private static Mesh chip;

        private Vector3[] velocity;
        private Vector3[] spin;
        private Transform[] pieces;
        private float age;
        private float life;
        private float size;
        private float gravity;

        public static void Burst(Vector3 position, Vector3 direction, Paint paint, int count, float sizeMeters, float units)
        {
            if (chip == null)
            {
                LowPolyMesh builder = new LowPolyMesh();
                builder.Box(Vector3.zero, Vector3.one, Paint.ClaySand);
                Material[] unused;
                chip = builder.ToMesh("Debris Chip", out unused);
            }
            GameObject root = new GameObject("Debris");
            root.transform.position = position;
            MarsDebris debris = root.AddComponent<MarsDebris>();
            debris.life = 1.1f;
            debris.size = sizeMeters * units;
            debris.gravity = 3.71f * units;
            debris.pieces = new Transform[count];
            debris.velocity = new Vector3[count];
            debris.spin = new Vector3[count];
            Material material = MarsLowPoly.Material(paint);
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                GameObject piece = new GameObject("Chip");
                piece.transform.SetParent(root.transform, false);
                piece.transform.localRotation = Random.rotation;
                piece.transform.localScale = Vector3.one * debris.size * Random.Range(0.6f, 1.3f);
                piece.AddComponent<MeshFilter>().sharedMesh = chip;
                MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                debris.pieces[i] = piece.transform;
                Vector3 v = (direction + Random.insideUnitSphere * 0.8f + Vector3.up * 0.6f).normalized;
                debris.velocity[i] = v * Random.Range(0.8f, 2.2f) * units;
                debris.spin[i] = Random.insideUnitSphere * 720f;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float shrink = Mathf.Clamp01(1f - (age - life * 0.5f) / (life * 0.5f));
            for (int i = 0; i < pieces.Length; i++)
            {
                velocity[i] += Vector3.down * gravity * dt;
                pieces[i].position += velocity[i] * dt;
                pieces[i].Rotate(spin[i] * dt, Space.Self);
                if (age > life * 0.5f) pieces[i].localScale = Vector3.one * size * shrink;
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
