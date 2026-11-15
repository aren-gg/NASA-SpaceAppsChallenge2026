using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// The camp's supply crate with an oxygen tank beside it: hand in samples from the
    /// sample bag by walking up to it, and refill the portable O2 canister.
    /// </summary>
    public sealed class MarsSupplyCrate : MonoBehaviour
    {
        public void Build()
        {
            LowPolyMesh mesh = new LowPolyMesh();
            mesh.Box(new Vector3(0f, 0.36f, 0f), new Vector3(1.1f, 0.72f, 0.75f), Paint.DustyRose);
            mesh.Box(new Vector3(0f, 0.76f, 0f), new Vector3(1.16f, 0.08f, 0.81f), Paint.Mauve);
            mesh.Box(new Vector3(0f, 0.5f, 0.38f), new Vector3(0.7f, 0.1f, 0.02f), Paint.WarmOrange);
            mesh.Box(new Vector3(-0.4f, 0.36f, 0.38f), new Vector3(0.08f, 0.72f, 0.02f), Paint.Mauve);
            mesh.Box(new Vector3(0.4f, 0.36f, 0.38f), new Vector3(0.08f, 0.72f, 0.02f), Paint.Mauve);
            // Oxygen tank on a small stand.
            mesh.Box(new Vector3(0.95f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f), Paint.DeepPurple);
            mesh.Prism(new Vector3(0.95f, 0.08f, 0f), 0.2f, 1.1f, 8, Quaternion.identity, Paint.PalePink, 0.14f);
            mesh.Prism(new Vector3(0.95f, 1.18f, 0f), 0.05f, 0.12f, 6, Quaternion.identity, Paint.WarmOrange);
            mesh.Build("Crate", transform);

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0.25f, 0.6f, 0f);
            box.size = new Vector3(1.8f, 1.2f, 0.85f);
        }
    }
}
