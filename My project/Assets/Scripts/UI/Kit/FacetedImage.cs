using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace MarsTrainer.UI
{
    // Carved utility plates: flat polygon layers, with no bitmap textures or blur.
    public sealed class FacetedImage : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = GetPixelAdjustedRect();
            if (Mathf.Min(rect.width, rect.height) < GameConstants.UiBadgeDiameter / 2)
            { Plate(mesh,rect,color); return; }
            Plate(mesh,rect,Color.Lerp(color,StatBar.ColorOf(GameConstants.UiIconInk),.6f));
            rect = Inset(rect,GameConstants.UiPlateInset / 2);
            Plate(mesh,rect,Color.Lerp(color,StatBar.ColorOf(GameConstants.UiText),.22f));
            rect = Inset(rect,GameConstants.UiPlateInset);
            Plate(mesh,rect,color);
        }
        private static Rect Inset(Rect rect, float inset) => new Rect(rect.x+inset,rect.y+inset,
            Mathf.Max(0,rect.width-inset*2),Mathf.Max(0,rect.height-inset*2));
        private static void Plate(VertexHelper mesh, Rect rect, Color tint)
        {
            int first=mesh.currentVertCount;
            float cut=Mathf.Min(GameConstants.UiFacetCut,Mathf.Min(rect.width,rect.height)/4);
            var v=UIVertex.simpleVert; v.color=tint; v.position=rect.center; mesh.AddVert(v);
            for(int i=0;i<8;i++)
            {
                switch(i)
                {
                    case 0:v.position=new Vector2(rect.xMin+cut,rect.yMin);break;
                    case 1:v.position=new Vector2(rect.xMax-cut,rect.yMin);break;
                    case 2:v.position=new Vector2(rect.xMax,rect.yMin+cut);break;
                    case 3:v.position=new Vector2(rect.xMax,rect.yMax-cut);break;
                    case 4:v.position=new Vector2(rect.xMax-cut,rect.yMax);break;
                    case 5:v.position=new Vector2(rect.xMin+cut,rect.yMax);break;
                    case 6:v.position=new Vector2(rect.xMin,rect.yMax-cut);break;
                    default:v.position=new Vector2(rect.xMin,rect.yMin+cut);break;
                }
                mesh.AddVert(v); mesh.AddTriangle(first,first+i+1,first+(i+1)%8+1);
            }
        }
    }
}
