using UnityEngine;
using UnityEngine.Rendering;

namespace MarsTrainer.Gameplay.World
{
    public static class FlatShadedMesh
    {
        public static Mesh Create(Vector3[] vertices, int[] triangles)
        {
            var flat = new Vector3[triangles.Length];
            var normals = new Vector3[triangles.Length];
            var indices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                flat[i] = a; flat[i + 1] = b; flat[i + 2] = c;
                normals[i] = normals[i + 1] = normals[i + 2] = normal;
                indices[i] = i; indices[i + 1] = i + 1; indices[i + 2] = i + 2;
            }
            var mesh = new Mesh { name = "Flat shaded mesh", indexFormat =
                flat.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = flat;
            mesh.normals = normals;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Rock()
        {
            return Create(new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.forward, Vector3.right, Vector3.back },
                new[] { 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2, 1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5 });
        }
    }
}
