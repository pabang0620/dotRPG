using System;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        sealed class MaterialPixels
        {
            public Color32[] pixels;public int width,height;
            public Color32 At(int x,int y)
            {
                // Mirrored repeats share identical edge pixels, including non-power-of-two source images.
                int Fold(int n,int size){int v=Math.Abs(n)%(size*2-2);return v<size?v:size*2-2-v;}
                return pixels[Fold(y,height)*width+Fold(x,width)];
            }
        }
        static MaterialPixels stoneMaterial,wallMaterial;
        static MaterialPixels ReadMaterial(string file)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            ImageConversion.LoadImage(texture,StreamingFiles.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,"Underworld",file)));
            var m=new MaterialPixels{pixels=texture.GetPixels32(),width=texture.width,height=texture.height};
            UnityEngine.Object.Destroy(texture);return m;
        }
        static Color32 Tone(Color32 c,float light,float red=1,float green=1,float blue=1)
            =>new Color32((byte)Mathf.Clamp(c.r*light*red,0,255),(byte)Mathf.Clamp(c.g*light*green,0,255),(byte)Mathf.Clamp(c.b*light*blue,0,255),255);

        static float CaveNoise(float x,float y,int seed)
        {
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);x-=ix;y-=iy;
            x=x*x*(3-2*x);y=y*y*(3-2*y);
            float At(int a,int b)=>(Hash(a+seed*71,b-seed*37)%1024)/1023f;
            return Mathf.Lerp(Mathf.Lerp(At(ix,iy),At(ix+1,iy),x),Mathf.Lerp(At(ix,iy+1),At(ix+1,iy+1),x),y);
        }
        static Color32 MixStone(Color32 a,Color32 b,float t)
            =>new Color32((byte)Mathf.Lerp(a.r,b.r,t),(byte)Mathf.Lerp(a.g,b.g,t),(byte)Mathf.Lerp(a.b,b.b,t),255);
        static Color32 Mineral(Color32 material,Color32 pigment,float light,float contrast=1)
        {
            // Keep the authored stone planes and cracks; mineral colour no longer depends on a grey source tint.
            float value=Mathf.Clamp((material.r*.30f+material.g*.54f+material.b*.16f)/105f,.27f,1.4f);
            value=Mathf.Lerp(1,value,contrast);
            return Tone(MixStone(Tone(pigment,value),material,.14f),light);
        }
        static float LightPool(float x,float y,Vector2 center,float rx,float ry)
        {
            float dx=(x-center.x)/rx,dy=(y-center.y)/ry;
            float amount=Mathf.Clamp01(1-dx*dx-dy*dy);
            return amount*amount;
        }
        sealed class CaveLightField
        {
            readonly float[] light,mass;readonly int columns,rows;
            public CaveLightField(int w,int h,int variant)
            {
                columns=w*4+1;rows=h*4+1;light=new float[columns*rows];mass=new float[light.Length];
                Vector2[] sites=variant==0?new[]{new Vector2(11,24),new Vector2(29,29),new Vector2(37,10)}:
                    variant==1?new[]{new Vector2(12,24),new Vector2(26,35),new Vector2(39,24)}:
                    variant==2?new[]{new Vector2(13,24),new Vector2(30,12),new Vector2(42,27)}:
                    new[]{new Vector2(18,31),new Vector2(33,18),new Vector2(38,10)};
                for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
                {
                    float wx=x*.25f,wy=y*.25f,pool=0;
                    foreach(var site in sites)pool=Mathf.Max(pool,LightPool(wx,wy,site,8.5f,6.5f));
                    int i=y*columns+x;
                    mass[i]=CaveNoise(wx*.095f,wy*.10f,variant+8)*.70f+CaveNoise(wx*.28f,wy*.22f,variant+19)*.30f;
                    light[i]=.80f+.27f*pool+.11f*CaveNoise(wx*.13f,wy*.11f,variant+31);
                }
            }
            float Sample(float[] values,float x,float y)
            {
                x=Mathf.Clamp(x*4,0,columns-1);y=Mathf.Clamp(y*4,0,rows-1);
                int ix=(int)x,iy=(int)y,nx=Mathf.Min(ix+1,columns-1),ny=Mathf.Min(iy+1,rows-1);
                return Mathf.Lerp(Mathf.Lerp(values[iy*columns+ix],values[iy*columns+nx],x-ix),Mathf.Lerp(values[ny*columns+ix],values[ny*columns+nx],x-ix),y-iy);
            }
            public float Light(float x,float y)=>Mathf.Floor(Sample(light,x,y)*24)/24;
            public float Mass(float x,float y)=>Sample(mass,x,y);
        }
        sealed class MasonryColumns
        {
            readonly float[] north,south;readonly int columns,rows;
            public MasonryColumns(UnderworldContour contour)
            {
                columns=contour.Columns;rows=contour.Rows;
                north=new float[columns*rows];south=new float[north.Length];
                // Two linear passes locate the real supporting floor. No per-pixel ray march.
                for(int x=0;x<columns;x++)
                {
                    float found=-1000;
                    for(int y=0;y<rows;y++)
                    {
                        if(contour.At(x,y)==1)found=(y+.5f)/UnderworldContour.Scale;
                        south[y*columns+x]=found;
                    }
                    found=1000;
                    for(int y=rows-1;y>=0;y--)
                    {
                        if(contour.At(x,y)==1)found=(y+.5f)/UnderworldContour.Scale;
                        north[y*columns+x]=found;
                    }
                }
            }
            int Index(float x,float y)=>Mathf.Clamp((int)(y*UnderworldContour.Scale),0,rows-1)*columns+
                Mathf.Clamp((int)(x*UnderworldContour.Scale),0,columns-1);
            public float FloorAbove(float x,float y)=>north[Index(x,y)]-y;
            public float FloorBelow(float x,float y)=>y-south[Index(x,y)];
        }
        static float MasonryFade(float value,float start,float end)
        {
            float t=Mathf.Clamp01((value-start)/(end-start));
            return 1-t*t*(3-2*t);
        }
        static Sprite CaveTerrain(string id,char[,] cells,int w,int h,UnderworldContour contour)
        {
            if(floors.TryGetValue(id,out var cached))return cached;
            stoneMaterial=stoneMaterial??ReadMaterial("ground-masonry.png");wallMaterial=wallMaterial??ReadMaterial("retaining-masonry.png");
            int variant=id=="hollow_roots"?1:id=="hollow_fungal"?2:id=="hollow_depths"?3:0;
            int pw=w*32,ph=h*32;var pixels=new Color32[pw*ph];
            Color32[] floorsByBiome={PixelCanvas.Hex("807761"),PixelCanvas.Hex("70785b"),PixelCanvas.Hex("5b7074"),PixelCanvas.Hex("686e83")};
            Color32[] deepByBiome={PixelCanvas.Hex("141611"),PixelCanvas.Hex("101a15"),PixelCanvas.Hex("0f1a21"),PixelCanvas.Hex("131722")};
            Color32[] waterByBiome={PixelCanvas.Hex("142a28"),PixelCanvas.Hex("122d25"),PixelCanvas.Hex("102c36"),PixelCanvas.Hex("172439")};
            var rootMoss=PixelCanvas.Hex("596748");var wetPatina=PixelCanvas.Hex("405e5e");
            var field=new CaveLightField(w,h,variant);
            var supports=new MasonryColumns(contour);
            var warpX=new int[ph];var warpY=new int[pw];
            // Continuous domain bending breaks the source's mirror axes without random tile seams.
            for(int y=0;y<ph;y++){float wy=y/32f;warpX[y]=(int)(Mathf.Sin(wy*.19f)*19+Mathf.Sin(wy*.061f)*41);}
            for(int x=0;x<pw;x++)warpY[x]=(int)(Mathf.Sin(x/32f*.14f)*17+Mathf.Sin(x/32f*.043f)*37);
            for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
            {
                float wx=(x+.5f)/32,wy=(y+.5f)/32;
                float d=contour.FloorDistance(wx,wy),water=contour.WaterDistance(wx,wy);
                // Wall courses run horizontally in world space, like the masonry in the distant ruins.
                var wall=wallMaterial.At(x*5+variant*227+warpX[y]/3,y*5+warpY[x]/5);
                float mass=field.Mass(wx,wy);
                Color32 color;
                if(d>=0)
                {
                    // A coarse two-dimensional warp prevents bilateral source repeats from reading
                    // as stamped floor tiles. This samples the precomputed field, never pixel noise.
                    int crossX=(int)((mass-.5f)*144);
                    int crossY=(int)((field.Mass(w-wx,wy)-.5f)*104);
                    // Shorter north/south slabs read as a horizontal top plane at the game camera angle.
                    var baseStone=stoneMaterial.At(x*8+warpX[y]+crossX+variant*173,y*10+warpY[x]+crossY+variant*91);
                    float contact=.92f+.08f*Mathf.Clamp01(d/.75f);
                    color=Mineral(baseStone,floorsByBiome[variant],contact*field.Light(wx,wy)*.81f,.86f);
                    // Floor and wall share the same stone pigment. Only water gets a thin, dark wet contact;
                    // there is no bright contour line separating an island from a different background.
                    if(water>-.22f)color=Tone(color,.70f);
                    else if(water>-.75f)color=MixStone(color,waterByBiome[variant],.12f*(1+water/.75f));
                    if(variant==1&&d>.2f&&d<2.4f&&mass>.57f)
                        color=MixStone(color,Mineral(baseStone,rootMoss,.70f),.23f);
                    if(variant==2&&d>.2f&&d<1.4f&&mass>.48f)
                        color=MixStone(color,Mineral(baseStone,wetPatina,.70f),.20f);
                }
                else if(water>=0)
                {
                    float shallow=Mathf.Floor(Mathf.Clamp01(1+d/2.3f)*14)/14;
                    color=MixStone(waterByBiome[variant],Tone(floorsByBiome[variant],.53f),shallow*.36f);
                    // The stone's vertical face is in blocked water, never painted across walkable land.
                    if(supports.FloorAbove(wx,wy)<.55f)
                    {
                        color=Mineral(wall,floorsByBiome[variant],.29f);
                        if(d<-.40f)color=Tone(color,.72f);
                    }
                    else if(d>-.14f)color=Tone(color,.65f);
                }
                else
                {
                    float depth=-d;
                    float above=supports.FloorAbove(wx,wy),below=supports.FloorBelow(wx,wy);
                    float foundationHeight=3.7f+mass*1.4f;
                    float retainingHeight=1.7f+mass*1.3f;
                    float veil;
                    bool foundation=above<foundationHeight&&(below>1.25f||above<below);
                    if(foundation)
                    {
                        // The south face descends from the actual floor above it, rather than outlining
                        // every boundary with the same ring. Its foot sinks into the distant architecture.
                        float t=Mathf.Clamp01(above/foundationHeight);
                        float light=Mathf.Lerp(.46f,.20f,t*t)*(.91f+mass*.17f);
                        color=Mineral(wall,floorsByBiome[variant],light);
                        if(above<.10f)color=Tone(color,.77f);
                        veil=MasonryFade(above,foundationHeight*.65f,foundationHeight);
                        color=MixStone(color,deepByBiome[variant],t*t*.38f);
                    }
                    else if(below<retainingHeight)
                    {
                        // Northern faces are buried retaining masonry, with a shaded base and broken crest.
                        float t=Mathf.Clamp01(below/retainingHeight);
                        color=Mineral(wall,floorsByBiome[variant],.30f+.11f*Mathf.Clamp01(t*2));
                        veil=MasonryFade(below,retainingHeight*.57f,retainingHeight);
                        color=MixStone(color,deepByBiome[variant],t*t*.46f);
                    }
                    else
                    {
                        // A short side return joins the masonry at bends; no distant circular halo survives.
                        color=Mineral(wall,floorsByBiome[variant],.33f);
                        veil=MasonryFade(depth,.18f,.72f+mass*.55f);
                    }
                    // A remote pool still sits in a rock basin. Its bank is lower and narrower than
                    // the traversable ledges; fading solely from floor distance would leave floating water.
                    float basinDepth=Mathf.Max(0,-water);
                    if(depth>2.6f&&basinDepth<2.4f)
                    {
                        float basinWidth=.75f+mass*.55f;
                        float basinVeil=MasonryFade(basinDepth,basinWidth,basinWidth+.85f+mass*.25f);
                        var bank=Mineral(wall,floorsByBiome[variant],.28f+.08f*Mathf.Clamp01(basinDepth));
                        if(basinDepth<.13f)bank=Tone(bank,.72f);
                        color=MixStone(color,bank,basinVeil);
                        veil=Mathf.Max(veil,basinVeil);
                    }
                    color.a=(byte)(Mathf.RoundToInt(veil*15)*17);
                }
                pixels[y*pw+x]=color;
            }
            return floors[id]=Make(pixels,pw,ph,"Continuous cave terrain "+id,Vector2.zero);
        }
        static Sprite mossSprig,rootRidge;
        static void CaveGroundDetails(Transform root,UnderworldContour contour,int variant,int w,int h)
        {
            if(mossSprig==null)
            {
                var p=new PixelCanvas(48,24);
                for(int n=0;n<9;n++)
                {
                    int x=5+(n*17)%38,y=10+(n*7)%10;
                    p.Ellipse(x,y,6,3,PixelCanvas.Hex("20392e"));p.Ellipse(x-1,y-1,4,2,PixelCanvas.Hex("465b38"));
                    p.Line(x,y-1,x-1,y-5,PixelCanvas.Hex("6c7f4a"));
                }
                mossSprig=Make(p.ToTexturePixels(),48,24,"Cave moss clusters",new Vector2(.5f,.2f));
                p=new PixelCanvas(160,72);
                for(int branch=0;branch<4;branch++)
                    for(int x=0;x<154;x++)
                    {
                        int y=20+(int)(Mathf.Sin(x*.023f+branch*.6f)*(6+branch*5))+branch*5;
                        int thick=Mathf.Max(1,5-x/38);
                        p.Rect(x,y,1,thick+3,PixelCanvas.Hex("1e251e"));p.Rect(x,y,1,thick,PixelCanvas.Hex("59472e"));p.Set(x,y,PixelCanvas.Hex("7b6740"));
                    }
                rootRidge=Make(p.ToTexturePixels(),160,72,"Interwoven floor roots",new Vector2(.5f,.3f));
            }
            int count=0;
            for(int y=3;y<h-3;y++)for(int x=3;x<w-3;x++)
            {
                float d=contour.FloorDistance(x+.5f,y+.5f);
                if(d<.8f||d>2.2f||Hash(x,y)%19!=0||count++>32)continue;
                var sr=Put(root,"Low cave vegetation",mossSprig,new Vector2(x+.5f,y+.5f),.8f+Hash(x+7,y)%7*.08f,-28100);
                sr.color=variant==2?new Color(.55f,1.2f,1.2f,.8f):new Color(.8f,.88f,.67f,.8f);
            }
            if(variant==1)
            {
                foreach(var at in new[]{new Vector2(17,16),new Vector2(24,35),new Vector2(42,21)})
                {
                    bool safe=true;for(int x=-2;x<=2;x++)if(!contour.Land(at.x+x,at.y))safe=false;
                    if(safe)Put(root,"Flat ancient roots",rootRidge,at,4.2f,-28050);
                }
            }
        }
    }
}
