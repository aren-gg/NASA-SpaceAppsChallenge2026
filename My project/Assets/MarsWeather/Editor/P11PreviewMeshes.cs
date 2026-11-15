using UnityEditor;
using UnityEngine;

namespace MarsTrainer.Look.Editor
{
    public static class P11PreviewMeshes
    {
        public static Mesh Globe()
        {
            const string path = "Assets/MarsWeather/Materials/P11/FacetedGlobe.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved != null) return saved;
            const int longitude = 12, latitude = 6;
            var vertices = new Vector3[(longitude + 1) * (latitude + 1)];
            var triangles = new int[longitude * latitude * 6];
            for (int y = 0; y <= latitude; y++)
                for (int x = 0; x <= longitude; x++)
                {
                    float a = x * Mathf.PI * 2 / longitude, b = y * Mathf.PI / latitude;
                    vertices[y * (longitude + 1) + x] = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a)) / 2;
                }
            int t = 0;
            for (int y = 0; y < latitude; y++)
                for (int x = 0; x < longitude; x++)
                {
                    int k = y * (longitude + 1) + x, next = k + longitude + 1;
                    triangles[t++] = k; triangles[t++] = k + 1; triangles[t++] = next;
                    triangles[t++] = k + 1; triangles[t++] = next + 1; triangles[t++] = next;
                }
            var flat = new Vector3[triangles.Length]; var indices = new int[flat.Length];
            for (int i = 0; i < flat.Length; i++) { flat[i] = vertices[triangles[i]]; indices[i] = i; }
            var mesh = new Mesh { name = "Faceted globe", vertices = flat, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
    }
}
