using System;
using UnityEngine;

namespace DotRPG
{
    public static partial class SunkenSanctumArt
    {
        // A single contour drives the stone cap, exposed bank, contact shadow and water depth.
        // Vertical faces extend into blocked water; only the bright upper surface is walkable.
        static Sprite PaintShore(char[,] cells,int w,int h,SunkenSanctumShore shore)
        {
            var p=new PixelCanvas(w*32,h*32);
            var material=Asset("floor").texture;
            var materialPixels=material.GetPixels32();
            var cliffMaterial=Asset("cliff_face").texture;
            var cliffPixels=cliffMaterial.GetPixels32();
            var cliffDrops=BuildCliffDrops(shore);
            Color32 Stone(int x,int y)=>materialPixels[((y*2)%material.height)*material.width+(x*2)%material.width];
            for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++)
            {
                float wx=(x+.5f)/32,wy=h-(y+.5f)/32;
                int tx=x/32,ty=h-1-y/32;
                var stone=Stone(x,y);
                float d=shore.LandDistance(wx,wy);
                float waterDistance=shore.WaterDistance(wx,wy);
                // Rasterize the distance-field contour at the game's native 32 px/tile.
                // Collision uses the same mask at 1/8 tile; the interpolated corner bevel is <2 px.
                bool land=d>=0,water=!land&&waterDistance>=0;
                float light=SanctumLight(wx,wy);
                Color32 color;
                if(land)
                {
                    // Keep the existing slab art, with restrained highlights away from the bank.
                    color=C((int)(stone.r*.94f+light*11),(int)(stone.g*.94f+light*10),(int)(stone.b*.93f+light*7));
                    if(cells[tx,ty]=='L')
                    {
                        int step=y%16;
                        color=step<3?C(185,173,134):step<11?C(139,136,110):C(63,70,55);
                    }
                    else if(d<.48f&&waterDistance>-.6f)
                    {
                        float variation=ShoreNoise(wx*1.2f,wy*.85f);
                        float cap=.23f+variation*.15f;
                        float nx=shore.LandDistance(wx+.125f,wy)-shore.LandDistance(wx-.125f,wy);
                        float ny=shore.LandDistance(wx,wy+.125f)-shore.LandDistance(wx,wy-.125f);
                        float lit=Mathf.Clamp01(.55f+nx*1.2f-ny*.85f);
                        if(d<cap)
                        {
                            // Broad chipped rock planes, rather than a uniform luminous outline.
                            int face=Hash((x+(y/21)*5)/29,y/23)%13-6;
                            color=C((int)(stone.r*.38f+81+lit*20+face),(int)(stone.g*.38f+78+lit*17+face),(int)(stone.b*.34f+57+lit*12+face));
                            if(d<.045f)color=C((int)(color.r*.69f),(int)(color.g*.71f),(int)(color.b*.65f));
                            // A few cracks continue the floor joints through the rock cap.
                            if(stone.r<68&&stone.g<74)color=C(64,69,52);
                        }
                        if(d>cap*.75f&&variation>.64f)
                        {
                            int tuft=(Hash(x/4,y/3)%11)-5;
                            color=C(79+tuft,91+tuft,47+tuft/2);
                            if(d<cap&&Hash(x/7,y/5)%4==0)color=C(114,123,66);
                        }
                    }
                }
                else if(water)
                {
                    float depth=-d;
                    float waterEdge=waterDistance;
                    float shallows=Mathf.Clamp01(1-depth/1.5f);
                    float shadow=Mathf.Clamp01(1-waterEdge/.6f)*Mathf.Clamp01((depth-.35f)*2);
                    // Continuous world-space depth and light: no per-tile search window or rectangular light patches.
                    float caustic=ShoreNoise(wx*1.7f+2,wy*.7f)*2-1;
                    float level=Mathf.Floor((shallows+caustic*.035f)*18)/18;
                    color=C((int)(17+level*16+light*9-shadow*7),(int)(46+level*24+light*15-shadow*13),(int)(38+level*15+light*9-shadow*10));
                    // Sparse submerged stone shapes in the shallows, low contrast and never land-colored.
                    if(depth>.42f&&depth<1.25f&&ShoreNoise(wx*2.7f,wy*3.1f)>.71f)
                        color=C(color.r+3,color.g+4,color.b+1);

                    float height=.46f+ShoreNoise(wx*1.1f,wy*.55f)*.19f;
                    bool frontFace=shore.LandDistance(wx,wy+height)>=0;
                    bool sideFace=depth<.12f;
                    if(frontFace||sideFace)
                    {
                        // The south-facing bank exposes a deep, segmented wet rock wall.
                        float under=Mathf.Clamp01(depth/height);
                        int jointX=x+(int)(ShoreNoise(wx*.8f,wy*1.4f)*7);
                        int block=Hash(jointX/22,(y-10)/30);
                        int split=jointX%22;
                        int shade=(block%13)-6;
                        color=C((int)(stone.r*.23f+47-under*14+shade),(int)(stone.g*.23f+49-under*12+shade),(int)(stone.b*.22f+35-under*9+shade));
                        if(split<2)color=C(35,43,33);
                        else if(split<4)color=C(color.r+13,color.g+11,color.b+7);
                        if(under>.8f)color=C((int)(color.r*.66f),(int)(color.g*.73f),(int)(color.b*.72f));
                    }
                    else
                    {
                        bool belowBank=shore.LandDistance(wx,wy+height+.18f)>=0;
                        if(belowBank||depth<.23f)color=C((int)(color.r*.62f),(int)(color.g*.69f),(int)(color.b*.73f));
                        // Short, subdued contact reflections follow the actual contour, not tile edges.
                        else if(depth>.26f&&depth<.40f&&ShoreNoise(wx*1.8f,wy*.6f)>.60f)
                            color=C(color.r+7,color.g+12,color.b+7);
                    }
                }
                else
                {
                    // W is exposed vertical rock, never a dark copy of the walkable floor.
                    // The shoulder west of the altar neck descends from the land above; its
                    // lighting follows that silhouette rather than a rectangular patch boundary.
                    color=CliffFaceColor(wx,wy,d,waterDistance,shore,cliffDrops,
                        cliffPixels,cliffMaterial.width,cliffMaterial.height);
                    // A chipped 3-pixel overhang joins the western floor to its wall.
                    // It remains on the blocked side of the contour.
                    if(wx>13&&wx<21&&wy>24&&wy<30&&d>-.10f&&stone.r>64)
                    {
                        float edge=Mathf.Floor(Mathf.Clamp01(1+d/.10f)*4)/4;
                        var lip=C((int)(stone.r*.79f+12),(int)(stone.g*.79f+10),(int)(stone.b*.74f+7));
                        color=C((int)Mathf.Lerp(color.r,lip.r,edge),(int)Mathf.Lerp(color.g,lip.g,edge),(int)Mathf.Lerp(color.b,lip.b,edge));
                    }
                }
                p.Set(x,y,color);
            }
            PaintAltarApproach(p,shore,h);
            PaintBasinRubble(p,materialPixels,material.width,material.height);
            return Raster(p,"sanctum_terrain",Vector2.zero);
        }

        static float[] BuildCliffDrops(SunkenSanctumShore shore)
        {
            int width=shore.SampleWidth,height=shore.SampleHeight;
            float step=1f/SunkenSanctumShore.SamplesPerTile;
            var drops=new float[width*height];
            // First land above each W column gives the visible upper ledge. Water breaks the
            // extrusion, so a distant island never casts a vertical face across a whole pond.
            for(int x=0;x<width;x++)
            {
                float roof=float.NaN,wx=(x+.5f)*step;
                for(int y=height-1;y>=0;y--)
                {
                    float wy=(y+.5f)*step;
                    if(shore.IsLand(wx,wy))roof=wy;
                    else if(shore.IsWater(wx,wy))roof=float.NaN;
                    drops[y*width+x]=float.IsNaN(roof)?6:Mathf.Clamp(roof-wy-step*.5f,0,6);
                }
            }
            return drops;
        }

        static float CliffDrop(float wx,float wy,SunkenSanctumShore shore,float[] drops)
        {
            int width=shore.SampleWidth,height=shore.SampleHeight;
            float x=Mathf.Clamp(wx*SunkenSanctumShore.SamplesPerTile-.5f,0,width-1);
            float y=Mathf.Clamp(wy*SunkenSanctumShore.SamplesPerTile-.5f,0,height-1);
            int left=(int)x,bottom=(int)y,right=Mathf.Min(left+1,width-1),top=Mathf.Min(bottom+1,height-1);
            return Mathf.Lerp(Mathf.Lerp(drops[bottom*width+left],drops[bottom*width+right],x-left),
                Mathf.Lerp(drops[top*width+left],drops[top*width+right],x-left),y-bottom);
        }

        static int MirroredCliffPixel(int pixel,int size)
        {
            int period=size*2;pixel=((pixel%period)+period)%period;
            return pixel<size?pixel:period-pixel-1;
        }

        static Color32 CliffFaceColor(float wx,float wy,float landDistance,float waterDistance,
            SunkenSanctumShore shore,float[] drops,Color32[] pixels,int width,int height)
        {
            // Match the stone material's sampling density. Reflect at texture boundaries so
            // the generated fractures never restart at a visible horizontal or vertical seam.
            int sx=MirroredCliffPixel(Mathf.FloorToInt(wx*64),width);
            int sy=MirroredCliffPixel(Mathf.FloorToInt(wy*64),height);
            Color32 rock=pixels[sy*width+sx];
            float drop=CliffDrop(wx,wy,shore,drops);
            float northFace=Mathf.Clamp01((5.5f-drop)/1.2f);
            float sideFace=Mathf.Clamp01(1+landDistance/1.35f)*.60f;
            float exposure=Mathf.Max(northFace,sideFace);
            exposure=exposure*exposure*(3-2*exposure);
            float downShade=Mathf.Clamp01(drop/5.5f);
            float scale=Mathf.Lerp(.20f,.81f-downShade*.20f,exposure);
            float lipWidth=.14f+ShoreNoise(wx*.9f,wy*.7f)*.10f;
            float upperLip=Mathf.Clamp01(1-drop/lipWidth)*northFace;
            // The dark damp foot follows the shared basin contour, not a fixed y coordinate.
            float wet=Mathf.Clamp01(1+waterDistance/.6f);
            float damp=1-wet*.24f;
            float center=Mathf.Clamp01(1-Mathf.Abs(wx-24)/24);
            return C((int)((rock.r*scale+upperLip*25+center*2)*damp),
                (int)((rock.g*(scale+.015f)+upperLip*20+center*2)*damp+wet*2),
                (int)((rock.b*scale+upperLip*10+center)*damp));
        }

        static float SanctumLight(float x,float y)
        {
            // Diffuse openings above the altar and upper basin, with no sharp lighting rectangles.
            float altar=Mathf.Clamp01(1-((x-24)*(x-24)/230+(y-13)*(y-13)/190));
            float upper=Mathf.Clamp01(1-((x-24)*(x-24)/155+(y-51)*(y-51)/80));
            return .18f+altar*.8f+upper*.25f;
        }

        static float ShoreNoise(float x,float y)
        {
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);
            float fx=x-ix,fy=y-iy;fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
            float At(int a,int b)=>(Hash(a+121,b+793)%1024)/1023f;
            return Mathf.Lerp(Mathf.Lerp(At(ix,iy),At(ix+1,iy),fx),Mathf.Lerp(At(ix,iy+1),At(ix+1,iy+1),fx),fy);
        }

        static void PaintBasinRubble(PixelCanvas p,Color32[] material,int width,int height)
        {
            for(int i=0;i<52;i++)
            {
                float a=i*Mathf.PI*2/52,ringX=24+19*Mathf.Cos(a),ringY=58+12*Mathf.Sin(a);
                if(ringY<50||ringY>70)continue;
                int x=(int)(ringX*32),y=(int)(ringY*32),size=19+Hash(i,5)%15;
                for(int yy=-size/2;yy<size/2;yy++)for(int xx=-size;xx<size;xx++)
                {
                    int cut=Math.Abs(yy)*2/5;if(Math.Abs(xx)>size-cut||Hash(i,5)%7==0)continue;
                    var stone=material[((y+yy+height)%height)*width+(x+xx+width)%width];
                    float shade=yy>size/7?.43f:.83f;
                    p.Set(x+xx,y+yy,C((int)(stone.r*shade),(int)(stone.g*shade),(int)(stone.b*shade)));
                }
            }
        }
    }
}
