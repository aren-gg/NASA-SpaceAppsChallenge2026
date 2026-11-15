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
        private VertexHelper mesh;
        private Vector2 center;
        private float radius;
        public ResourceSymbol Symbol { get => symbol; set { symbol = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper output)
        {
            mesh = output; mesh.Clear(); var rect = GetPixelAdjustedRect(); center = rect.center;
            radius = Mathf.Min(rect.width, rect.height) / 2;
            var face = StatBar.ColorOf(ColorFor(symbol)); var ink = StatBar.ColorOf(GameConstants.UiIconInk);
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
                case ResourceSymbol.Air:return GameConstants.UiSafe;
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
