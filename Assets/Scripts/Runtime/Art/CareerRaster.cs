using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Native 192px raster target using 96-unit art coordinates. Primitives rasterize directly
    /// at the final density; no low-resolution image is rendered, resized or filtered.
    /// Kept separate from PixelCanvas so base-class art retains its original pixel density.
    /// </summary>
    public sealed class CareerRaster
    {
        public const int Density=2, Size=96*Density, Frames=12, Ppu=48*Density;
        public readonly PixelCanvas Raw=new PixelCanvas(Size,Size);
        public Color32[] Pixels=>Raw.Pixels;
        static int P(float n)=>Mathf.RoundToInt(n*Density);
        public void Set(float x,float y,Color32 c)=>Raw.Set(P(x),P(y),c);
        public void Line(float x,float y,float xx,float yy,Color32 c)=>Raw.Line(P(x),P(y),P(xx),P(yy),c);
        public void HLine(float x,float xx,float y,Color32 c)=>Raw.HLine(P(x),P(xx),P(y),c);
        public void VLine(float x,float y,float yy,Color32 c)=>Raw.VLine(P(x),P(y),P(yy),c);
        public void Rect(float x,float y,float w,float h,Color32 c)=>Raw.Rect(P(x),P(y),P(w),P(h),c);
        public void Circle(float x,float y,float r,Color32 c)=>Raw.Circle(x*Density,y*Density,r*Density,c);
        public void Ellipse(float x,float y,float rx,float ry,Color32 c)=>Raw.Ellipse(x*Density,y*Density,rx*Density,ry*Density,c);
        public static implicit operator PixelCanvas(CareerRaster p)=>p.Raw;
    }
}
