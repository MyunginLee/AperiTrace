using UnityEngine;
using UnityEngine.UI;

namespace XRSuperspace
{
    // Code-native rounded surface. No bitmap, font glyph icon, blur or package.
    public sealed class DesktopSurface : Image
    {
        public float Radius=10;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var rect=GetPixelAdjustedRect();float radius=Mathf.Clamp(Radius,0,Mathf.Min(rect.width,rect.height)/2);
            var center=rect.center;mesh.AddVert(center,color,Vector2.zero);
            const int segments=8;int count=4*(segments+1);
            for(int corner=0;corner<4;corner++)
            {
                var origin=new Vector2(corner==0||corner==3?rect.xMax-radius:rect.xMin+radius,corner<2?rect.yMax-radius:rect.yMin+radius);
                for(int step=0;step<=segments;step++)
                {
                    float angle=(corner*90+step*90f/segments)*Mathf.Deg2Rad;
                    mesh.AddVert(origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);
                }
            }
            for(int i=1;i<=count;i++) mesh.AddTriangle(0,i,i==count?1:i+1);
        }
    }
}
