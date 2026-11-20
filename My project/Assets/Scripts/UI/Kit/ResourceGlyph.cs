using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    // Original polygon medallions inspired by the supplied reference; no image textures.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ResourceGlyph : MaskableGraphic
    {
        [SerializeField] private ResourceSymbol symbol;
        private static readonly Vector2[] Heart = { new(-.9f,.25f), new(-.7f,.7f), new(-.3f,.75f), new(0,.4f), new(.3f,.75f), new(.7f,.7f), new(.9f,.25f), new(.6f,-.2f), new(0,-.9f), new(-.6f,-.2f) };
        private static readonly Vector2[] Drop = { new(0,1), new(.65f,0), new(.55f,-.6f), new(0,-.9f), new(-.55f,-.6f), new(-.65f,0) };
        private static readonly Vector2[] Leaf = { new(-.8f,-.5f), new(-.85f,.1f), new(-.4f,.75f), new(.85f,.85f), new(.75f,-.4f), new(.15f,-.8f) };
        private static readonly Vector2[] Bolt = { new(.1f,1), new(-.75f,-.1f), new(-.1f,-.1f), new(-.3f,-1), new(.8f,.2f), new(.15f,.2f) };
        private static readonly Vector2[] Flask = { new(-.25f,1), new(.25f,1), new(.25f,.3f), new(.8f,-.65f), new(.6f,-.85f), new(-.6f,-.85f), new(-.8f,-.65f), new(-.25f,.3f) };
        private static readonly Vector2[] House = { new(-.8f,-.8f), new(.8f,-.8f), new(.8f,0), new(1,0), new(0,.9f), new(-1,0), new(-.8f,0) };
        private static readonly Vector2[] Tank = { new(-.21f,.95f), new(.21f,.95f), new(.21f,.74f), new(.42f,.74f), new(.53f,.53f), new(.53f,-.84f), new(.42f,-.95f), new(-.42f,-.95f), new(-.53f,-.84f), new(-.53f,.53f), new(-.42f,.74f), new(-.21f,.74f) };
        private static readonly Vector2[] Cross = { new(-.32f,.95f), new(.32f,.95f), new(.32f,.32f), new(.95f,.32f), new(.95f,-.32f), new(.32f,-.32f), new(.32f,-.95f), new(-.32f,-.95f), new(-.32f,-.32f), new(-.95f,-.32f), new(-.95f,.32f), new(-.32f,.32f) };
        [SerializeField] private bool plain;
        private VertexHelper mesh;
        private Vector2 center;
        private float radius;
        public ResourceSymbol Symbol { get => symbol; set { symbol = value; SetVerticesDirty(); } }
        /// <summary>Just the pictogram in its colour, with no medallion: the NASA-punk HUD style.</summary>
        public bool Plain { get => plain; set { plain = value; SetVerticesDirty(); } }
        /// <summary>Overrides the pictogram colour (a vital running low turns red).</summary>
        public Color? Tint { get => tint; set { tint = value; SetVerticesDirty(); } }
        private Color? tint;

        protected override void OnPopulateMesh(VertexHelper output)
        {
            mesh = output; mesh.Clear(); var rect = GetPixelAdjustedRect(); center = rect.center;
            radius = Mathf.Min(rect.width, rect.height) / 2;
            var face = tint ?? StatBar.ColorOf(ColorFor(symbol)); var ink = StatBar.ColorOf(GameConstants.UiIconInk);
            if (plain) { radius *= .92f; PlainShape(face); mesh = null; return; }
            Circle(Vector2.zero, 1, ink); Circle(Vector2.zero, .9f, face * .65f); Circle(new Vector2(0,.035f), .77f, face);
            radius *= .52f;
            switch (symbol)
            {
                case ResourceSymbol.Health: Polygon(Heart, ink); break;
                case ResourceSymbol.Water: Polygon(Drop, ink); break;
                case ResourceSymbol.Food: Polygon(Leaf, ink); Line(new Vector2(-.5f,-.5f),new Vector2(.5f,.5f),.12f,face); break;
                case ResourceSymbol.Power: Polygon(Bolt, ink); break;
                case ResourceSymbol.Rest: Circle(Vector2.zero,.9f,ink); Circle(new Vector2(.4f,.25f),.78f,face); break;
                case ResourceSymbol.Air:
                    Circle(new Vector2(-.23f,-.2f),.55f,ink); Circle(new Vector2(-.23f,-.2f),.31f,face);
                    Circle(new Vector2(.5f,.5f),.3f,ink); Circle(new Vector2(.6f,-.55f),.18f,ink); break;
                case ResourceSymbol.Radiation:
                    for (int i=0;i<3;i++)
                    {
                        float a=i*Mathf.PI*2/3; Line(new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.35f,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.85f,.48f,ink);
                    }
                    Circle(Vector2.zero,.23f,ink); break;
                case ResourceSymbol.Research: Polygon(Flask,ink); Line(new Vector2(-.45f,-.4f),new Vector2(.45f,-.4f),.14f,face); break;
                case ResourceSymbol.Home: Polygon(House,ink); Line(new Vector2(0,-.8f),new Vector2(0,-.2f),.35f,face); break;
                case ResourceSymbol.Menu:
                    for(int i=-1;i<=1;i++) Line(new Vector2(-.7f,i*.5f),new Vector2(.7f,i*.5f),.22f,ink); break;
                default:
                    Line(new Vector2(-.45f,.5f),new Vector2(.45f,.5f),.8f,ink);
                    Line(new Vector2(-.6f,-.2f),new Vector2(.6f,-.2f),1,ink);
                    Line(new Vector2(-.4f,-.25f),new Vector2(.4f,-.25f),.15f,face); break;
            }
            mesh = null;
        }
        private void PlainShape(Color face)
        {
            switch (symbol)
            {
                case ResourceSymbol.Air: Polygon(Tank, face); break;
                case ResourceSymbol.Health: Polygon(Cross, face); break;
                case ResourceSymbol.Water: Polygon(Drop, face); break;
                case ResourceSymbol.Food: Polygon(Leaf, face); break;
                case ResourceSymbol.Rest: Crescent(face); break;
                case ResourceSymbol.Power: Polygon(Bolt, face); break;
                case ResourceSymbol.Radiation:
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Mathf.PI / 2 + i * Mathf.PI * 2 / 3; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(d * .32f, d * .92f, .62f, face);
                    }
                    Circle(Vector2.zero, .2f, face); break;
                default: Polygon(Cross, face); break;
            }
        }

        /// <summary>A crescent moon: the strip between an outer circle and an offset inner one.</summary>
        private void Crescent(Color tint)
        {
            Vector2 c0 = Vector2.zero, c1 = new Vector2(.4f, .24f);
            float r0 = .9f, r1 = .74f;
            float d = Vector2.Distance(c0, c1), a = (r0 * r0 - r1 * r1 + d * d) / (2 * d), h = Mathf.Sqrt(Mathf.Max(0, r0 * r0 - a * a));
            Vector2 dir = (c1 - c0) / d, mid = c0 + dir * a, perp = new Vector2(-dir.y, dir.x);
            Vector2 upper = mid + perp * h, lower = mid - perp * h;
            float o0 = Angle(upper - c0), o1 = Angle(lower - c0), i0 = Angle(upper - c1), i1 = Angle(lower - c1);
            if (o1 < o0) o1 += Mathf.PI * 2; if (i1 < i0) i1 += Mathf.PI * 2;
            const int steps = 14;
            for (int k = 0; k < steps; k++)
            {
                float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                Vector2 a0 = c0 + Polar(Mathf.Lerp(o0, o1, t0)) * r0, a1 = c0 + Polar(Mathf.Lerp(o0, o1, t1)) * r0;
                Vector2 b0 = c1 + Polar(Mathf.Lerp(i0, i1, t0)) * r1, b1 = c1 + Polar(Mathf.Lerp(i0, i1, t1)) * r1;
                int first = mesh.currentVertCount;
                Add(a0, tint); Add(a1, tint); Add(b1, tint); Add(b0, tint);
                mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
            }
        }
        private static float Angle(Vector2 v) { float a = Mathf.Atan2(v.y, v.x); return a < 0 ? a + Mathf.PI * 2 : a; }
        private static Vector2 Polar(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));

        private void Circle(Vector2 offset, float size, Color tint)
        {
            int first=mesh.currentVertCount; Add(offset,tint);
            for(int i=0;i<GameConstants.UiBadgeSegments;i++)
            {
                float a=i*Mathf.PI*2/GameConstants.UiBadgeSegments;
                Add(offset+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*size,tint);
                mesh.AddTriangle(first,first+i+1,first+(i+1)%GameConstants.UiBadgeSegments+1);
            }
        }
        private void Polygon(Vector2[] points, Color tint)
        {
            int first=mesh.currentVertCount; Add(Vector2.zero,tint);
            for(int i=0;i<points.Length;i++) { Add(points[i],tint); mesh.AddTriangle(first,first+i+1,first+(i+1)%points.Length+1); }
        }
        private void Line(Vector2 from, Vector2 to, float width, Color tint)
        {
            var d=to-from; var n=new Vector2(-d.y,d.x).normalized*(width/2); int first=mesh.currentVertCount;
            Add(from+n,tint); Add(to+n,tint); Add(to-n,tint); Add(from-n,tint);
            mesh.AddTriangle(first,first+1,first+2); mesh.AddTriangle(first,first+2,first+3);
        }
        private void Add(Vector2 point, Color tint)
        {
            var v=UIVertex.simpleVert; v.position=center+point*radius; v.color=tint; mesh.AddVert(v);
        }
        public static uint ColorFor(ResourceSymbol value)
        {
            switch(value)
            {
                case ResourceSymbol.Health:return GameConstants.UiHealth;
                case ResourceSymbol.Water:return GameConstants.UiWater;
                case ResourceSymbol.Food:return GameConstants.UiFood;
                case ResourceSymbol.Rest:return GameConstants.UiRest;
                case ResourceSymbol.Power:return GameConstants.UiPower;
                case ResourceSymbol.Air:return GameConstants.UiOxygen;
                case ResourceSymbol.Radiation:return GameConstants.UiRadiation;
                default:return GameConstants.UiCopper;
            }
        }
        public static ResourceSymbol ForLabel(string label)
        {
            if(label.StartsWith("Health"))return ResourceSymbol.Health;
            if(label.StartsWith("Water"))return ResourceSymbol.Water;
            if(label.StartsWith("Food"))return ResourceSymbol.Food;
            if(label.StartsWith("Rest"))return ResourceSymbol.Rest;
            if(label.StartsWith("Air"))return ResourceSymbol.Air;
            if(label.StartsWith("Battery"))return ResourceSymbol.Power;
            return ResourceSymbol.Radiation;
        }
        public static ResourceGlyph Create(Transform parent, ResourceSymbol symbol, float diameter)
        {
            var rect=StatBar.Frame(parent,symbol+" badge",0,.5f,0,.5f);
            rect.pivot=new Vector2(0,.5f); rect.sizeDelta=Vector2.one*diameter;
            rect.anchoredPosition=new Vector2(GameConstants.UiBadgeInset,0);
            var icon=rect.gameObject.AddComponent<ResourceGlyph>(); icon.Symbol=symbol; icon.raycastTarget=false; return icon;
        }
        public static void Decorate(Button button, ResourceSymbol symbol)
        {
            Create(button.transform,symbol,GameConstants.UiBadgeDiameter);
            var text=button.GetComponentInChildren<TMPro.TMP_Text>();
            var rect=text.rectTransform; rect.offsetMin=new Vector2(GameConstants.UiBadgeDiameter+GameConstants.UiBadgeInset,0);
        }
    }
}
