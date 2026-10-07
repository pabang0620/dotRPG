using UnityEngine;

namespace DotRPG
{
    public static partial class SunkenSanctumArt
    {
        // Continue the altar's two flagstone courses along the actual curved approach.
        // Source coordinates select its upper WALKING SURFACE, not the raised side curbs.
        // This is atlas sampling in the terrain renderer; the original altar image is untouched.
        static void PaintAltarApproach(PixelCanvas terrain,SunkenSanctumShore shore,int mapHeight)
        {
            var altar=Asset("altar");if(altar==null)return;
            var texture=altar.texture;var pixels=texture.GetPixels32();
            for(int y=Mathf.FloorToInt((mapHeight-25.1f)*32);y<Mathf.CeilToInt((mapHeight-20.08f)*32);y++)
            {
                float wy=mapHeight-(y+.5f)/32;
                float left=28,right=18;
                for(float x=18;x<28;x+=.03125f)if(shore.LandDistance(x,wy)>=.20f){left=Mathf.Min(left,x);right=Mathf.Max(right,x);}
                if(right-left<1)continue;
                for(int x=Mathf.CeilToInt(left*32);x<=Mathf.FloorToInt(right*32);x++)
                {
                    float wx=(x+.5f)/32;
                    // The old natural slabs meet the laid stone course along a chipped, staggered joint.
                    float end=23.6f+.5f*Mathf.Sin(wx*1.87f)+.23f*Mathf.Sin(wx*4.33f);
                    if(wy>end)continue;
                    float u=Mathf.Clamp01((wx-left)/(right-left));
                    int sx=Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(598,827,u)),0,texture.width-1);
                    int fromTop=30+(int)Mathf.Repeat((wy-20.08f)*81.2f,160);
                    int sy=Mathf.Clamp(texture.height-1-fromTop,0,texture.height-1);
                    var stone=pixels[sy*texture.width+sx];
                    if(stone.a<128)continue;
                    if(end-wy<.045f)stone=C((int)(stone.r*.67f),(int)(stone.g*.69f),(int)(stone.b*.63f));
                    terrain.Set(x,y,stone);
                }
            }
        }
    }
}
