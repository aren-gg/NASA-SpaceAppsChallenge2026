using UnityEngine;

namespace MarsSurvival.Vehicles
{
    /// <summary>
    /// Tyre marks behind the rover that fade away: each wheel lays a dark strip on the ground as it
    /// rolls, and every strip fades out over <see cref="Life"/> seconds. One world-space mesh in a
    /// ring buffer, so the oldest marks are reused once it is full.
    /// </summary>
    public sealed class MarsRoverTracks : MonoBehaviour
    {
        public const float Life = 45f;            // seconds until a mark has faded completely
        private const int Capacity = 800;         // marks kept at once (all wheels)
        private const float Step = 0.35f;         // metres of rolling per mark
        private const float Darkness = 0.65f;     // alpha of a fresh mark
        private static readonly Color Ground = new Color(0.16f, 0.08f, 0.05f);

        private Transform[] wheels;
        private Transform rover;
        private Vector3?[] last;
        private Mesh mesh;
        private Material material;
        private GameObject marks;
        private Vector3[] verts;
        private Color[] colors;
        private float[] born;
        private int next;

        /// <summary>Marks still visible (not yet faded).</summary>
        public int Marks { get { int n = 0; foreach (float b in born ?? new float[0]) if (Time.time - b < Life) n++; return n; } }

        public void Init(Transform[] wheelPivots)
        {
            wheels = wheelPivots;
            rover = transform;
            last = new Vector3?[wheels.Length];
            verts = new Vector3[Capacity * 4];
            colors = new Color[Capacity * 4];
            born = new float[Capacity];
            for (int i = 0; i < Capacity; i++) born[i] = -Life;
            int[] tris = new int[Capacity * 6];
            for (int i = 0; i < Capacity; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            mesh = new Mesh { name = "Rover tracks" };
            mesh.MarkDynamic();
            mesh.vertices = verts; mesh.colors = colors; mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // world-space marks anywhere
            material = new Material(Shader.Find("Sprites/Default")) { name = "Rover tracks" };   // unlit, vertex colour alpha
            marks = new GameObject("Rover tracks");   // not under the rover: marks stay where they were laid
            marks.AddComponent<MeshFilter>().sharedMesh = mesh;
            marks.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void LateUpdate()
        {
            if (wheels == null) return;
            bool changed = false;
            for (int w = 0; w < wheels.Length; w++)
            {
                if (!OnGround(wheels[w], out Vector3 point, out Vector3 normal)) { last[w] = null; continue; }
                if (last[w] == null) { last[w] = point; continue; }
                Vector3 from = last[w].Value;
                if ((point - from).sqrMagnitude < Step * Step) continue;
                Lay(from, point, normal, 0.42f * wheels[w].lossyScale.x);
                last[w] = point;
                changed = true;
            }
            // Fade: a few times a second is plenty.
            if (changed || Time.frameCount % 6 == 0)
            {
                float now = Time.time;
                for (int i = 0; i < Capacity; i++)
                {
                    Color c = Ground;
                    c.a = Darkness * Mathf.Clamp01(1f - (now - born[i]) / Life);
                    int v = i * 4;
                    colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;
                }
                mesh.colors = colors;
            }
        }

        /// <summary>The ground under a wheel (not the rover itself), if the tyre is touching it.</summary>
        private bool OnGround(Transform wheel, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float reach = (MarsRoverModel.WheelRadius + 0.2f) * wheel.lossyScale.y;
            foreach (RaycastHit hit in Physics.RaycastAll(wheel.position, Vector3.down, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(rover)) continue;
                point = hit.point; normal = hit.normal;
                return true;
            }
            return false;
        }

        private void Lay(Vector3 from, Vector3 to, Vector3 normal, float width)
        {
            Vector3 side = Vector3.Cross(normal, to - from).normalized * (width * 0.5f);
            Vector3 lift = normal * 0.02f;   // just above the ground, so it doesn't flicker
            int v = next * 4;
            verts[v] = from - side + lift; verts[v + 1] = from + side + lift;
            verts[v + 2] = to - side + lift; verts[v + 3] = to + side + lift;
            born[next] = Time.time;
            next = (next + 1) % Capacity;
            mesh.vertices = verts;
        }

        private void OnDestroy()
        {
            if (marks != null) Destroy(marks);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
