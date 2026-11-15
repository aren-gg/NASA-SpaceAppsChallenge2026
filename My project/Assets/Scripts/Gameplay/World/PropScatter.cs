using System.Collections.Generic;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.World
{
    public sealed class PropScatter : MonoBehaviour
    {
        // Bounded seeded dart throwing: every accepted point obeys the Poisson distance.
        // Generation only; no per-frame work or physics overlap queries.
        public static List<Vector2> Sample(int seed, IReadOnlyList<Vector2> caves)
        {
            var rng = new System.Random(seed);
            var points = new List<Vector2>(GameConstants.PropCount);
            float extent = GameConstants.WorldSize / 2 - GameConstants.PropSpacing;
            for (int attempt = 0; attempt < GameConstants.PropCount * GameConstants.PropAttemptsPerPoint &&
                points.Count < GameConstants.PropCount; attempt++)
            {
                var point = new Vector2((float)(rng.NextDouble() * 2 - 1) * extent,
                    (float)(rng.NextDouble() * 2 - 1) * extent);
                if (point.sqrMagnitude < GameConstants.BaseClearRadius * GameConstants.BaseClearRadius) continue;
                bool clear = true;
                for (int i = 0; clear && i < caves.Count; i++)
                {
                    clear = (point - caves[i]).sqrMagnitude >= GameConstants.CaveClearRadius * GameConstants.CaveClearRadius;
                    float t = Mathf.Clamp01(Vector2.Dot(point, caves[i]) / caves[i].sqrMagnitude);
                    clear &= (point - caves[i] * t).sqrMagnitude >= GameConstants.PathClearWidth * GameConstants.PathClearWidth;
                }
                for (int i = 0; clear && i < points.Count; i++)
                    clear = (point - points[i]).sqrMagnitude >= GameConstants.PropSpacing * GameConstants.PropSpacing;
                if (clear) points.Add(point);
            }
            return points;
        }

        public void Build(TerrainGenerator terrain, Material material, Mesh rock, int seed)
        {
            var points = Sample(seed, terrain.CavePositions);
            var rng = new System.Random(seed);
            var rim = CreateRim();
            terrain.Own(rim);
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                bool crater = i < GameConstants.CraterCount;
                var prop = new GameObject(crater ? "Crater rim" : "Rock");
                prop.transform.SetParent(transform, false);
                prop.transform.localPosition = new Vector3(point.x, terrain.SampleHeight(point.x, point.y), point.y);
                prop.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * GameConstants.FullOrbitDegrees, 0);
                prop.AddComponent<MeshFilter>().sharedMesh = crater ? rim : rock;
                prop.AddComponent<MeshRenderer>().sharedMaterial = material;
                if (crater) continue;
                float scale = Mathf.Lerp(GameConstants.PropMinScale, GameConstants.PropMaxScale, (float)rng.NextDouble());
                if (rng.NextDouble() < GameConstants.BoulderChance) scale *= GameConstants.BoulderScale;
                prop.transform.localScale = new Vector3(scale, scale / 2, scale);
                prop.AddComponent<BoxCollider>().size = Vector3.one * 2;
            }
            // Shared palette props are immobile; combine once to bound runtime draw calls.
            if (Application.isPlaying) StaticBatchingUtility.Combine(gameObject);
        }

        private static Mesh CreateRim()
        {
            int count = GameConstants.CraterSegments;
            var vertices = new Vector3[count * 3];
            var indices = new int[count * 12];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 3] = direction * (GameConstants.CraterRadius - GameConstants.CraterRimWidth);
                vertices[i * 3 + 1] = direction * GameConstants.CraterRadius + Vector3.up * GameConstants.CraterRimHeight;
                vertices[i * 3 + 2] = direction * (GameConstants.CraterRadius + GameConstants.CraterRimWidth);
                int a = i * 3, b = (i + 1) % count * 3, t = i * 12;
                indices[t] = a; indices[t + 1] = b + 1; indices[t + 2] = b;
                indices[t + 3] = a; indices[t + 4] = a + 1; indices[t + 5] = b + 1;
                indices[t + 6] = a + 1; indices[t + 7] = b + 2; indices[t + 8] = b + 1;
                indices[t + 9] = a + 1; indices[t + 10] = a + 2; indices[t + 11] = b + 2;
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                int swap = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = swap;
            }
            return FlatShadedMesh.Create(vertices, indices);
        }
    }
}
