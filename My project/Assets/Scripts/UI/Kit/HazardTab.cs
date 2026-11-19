using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    /// <summary>A short stencil of slanted hazard stripes, used as the mark in front of section headers.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HazardTab : MaskableGraphic
    {
        public float Stripe = 4f, Period = 8f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            float lean = r.height * 0.8f;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            for (float x = r.xMin; x + Stripe + lean <= r.xMax + 0.01f; x += Period)
            {
                int first = mesh.currentVertCount;
                v.position = new Vector2(x, r.yMin); mesh.AddVert(v);
                v.position = new Vector2(x + Stripe, r.yMin); mesh.AddVert(v);
                v.position = new Vector2(x + Stripe + lean, r.yMax); mesh.AddVert(v);
                v.position = new Vector2(x + lean, r.yMax); mesh.AddVert(v);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first, first + 2, first + 3);
            }
        }
    }
}
