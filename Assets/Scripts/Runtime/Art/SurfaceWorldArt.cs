using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Optional surface-map artwork. The caller retains terrain colliders, NPCs, buildings,
    /// resources and portals, and supplies resolved terrain cells (for example GroundAt).
    /// A successful result replaces only the caller's old ground/static background painting.
    /// </summary>
    public static class SurfaceWorldArt
    {
        public const int StandardPixelsPerUnit=32,HighPixelsPerUnit=48;
        public const string SceneName="Surface world composition";
        const int SupportDepthTiles=3;
        static readonly string[] LayerNames={"Surface distant scenery","Surface supporting edges","Surface floor and water"};
        static readonly int[] LayerOrders={-31000,-30500,-30000};

        public static string SourcePath(string mapId)
            =>Path.Combine(Application.streamingAssetsPath,"SurfaceWorld",mapId+".png");

        /// <summary>
        /// Does not hide or mutate any existing renderer, collider, cell, route or gameplay object.
        /// False means the existing map renderer should be retained. All generated resources are
        /// map-owned; no static texture cache survives a map transition.
        /// </summary>
        public static bool TryBuild(Transform parent,string mapId,char[,] terrainCells,int width,int height,out SurfaceWorldScene scene)
        {
            scene=null;
            var info=MapRegistry.Get(mapId);
            if(parent==null||info==null||info.worldLayer!=WorldLayer.Surface||info.IsInterior||info.instanced
                ||width<=0||height<=0||terrainCells==null||terrainCells.GetLength(0)!=width||terrainCells.GetLength(1)!=height)return false;
            string path=SourcePath(info.id);
            if(!File.Exists(path))return false;
            var existing=parent.Find(SceneName)?.GetComponent<SurfaceWorldScene>();
            if(existing!=null&&existing.MapId==mapId){scene=existing;return true;}

            Texture2D source=null;GameObject root=null;
            try
            {
                // Bound source decoding; destination dimensions depend only on the real map.
                if(!SourceHeaderSupported(path))throw new InvalidDataException("Unsupported or oversized surface PNG");
                source=new Texture2D(2,2,TextureFormat.RGBA32,false)
                {name=mapId+" surface source",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(source,File.ReadAllBytes(path),false)||source.width<32||source.height<32)
                    throw new InvalidDataException("Surface PNG could not be decoded");
                int sw=source.width,sh=source.height;
                float aspectError=Mathf.Abs(((float)sw/sh)/((float)width/height)-1f);
                if(aspectError>.02f)throw new InvalidDataException("Surface PNG aspect ratio differs from the world by more than 2%");
                int ppu=sw>=width*HighPixelsPerUnit&&sh>=height*HighPixelsPerUnit?HighPixelsPerUnit:StandardPixelsPerUnit;
                int pw=checked(width*ppu),ph=checked(height*ppu);
                if(pw>4096||ph>4096||(SystemInfo.maxTextureSize>0&&(pw>SystemInfo.maxTextureSize||ph>SystemInfo.maxTextureSize)))
                    throw new InvalidDataException("Surface raster exceeds the supported texture size");
                var original=source.GetPixels32();
                byte[] mask=BuildMask(terrainCells,width,height);
                var raster=new Color32[checked(pw*ph)];
                int[] opaqueCounts=new int[3],assignedCounts=new int[3];
                int[] left={pw,pw,pw},right={-1,-1,-1},bottom={ph,ph,ph},top={-1,-1,-1};
                int sourceOpaque=0,rasterOpaque=0,sourcePartial=0,rasterPartial=0,edgeOnLand=0;
                for(int i=0;i<original.Length;i++)if(original[i].a>0){sourceOpaque++;if(original[i].a<255)sourcePartial++;}
                if(sourceOpaque==0)throw new InvalidDataException("Surface PNG is entirely transparent");
                for(int y=0;y<ph;y++)
                {
                    int sy=Math.Min(sh-1,(int)((y+.5f)*sh/ph)),cellY=y/ppu;
                    for(int x=0;x<pw;x++)
                    {
                        int sx=Math.Min(sw-1,(int)((x+.5f)*sw/pw)),cellX=x/ppu,index=y*pw+x;
                        int layer=mask[cellY*width+cellX];
                        Color32 color=original[sy*sw+sx];
                        raster[index]=color;assignedCounts[layer]++;
                        if(color.a==0)continue;
                        rasterOpaque++;opaqueCounts[layer]++;
                        if(color.a<255)rasterPartial++;
                        left[layer]=Math.Min(left[layer],x);right[layer]=Math.Max(right[layer],x);
                        bottom[layer]=Math.Min(bottom[layer],y);top[layer]=Math.Max(top[layer],y);
                        if(layer==1&&!Blocked(terrainCells[cellX,cellY])&&!Water(terrainCells[cellX,cellY]))edgeOnLand++;
                    }
                }
                if(rasterOpaque==0)throw new InvalidDataException("Surface PNG has no visible pixels at map resolution");
                // Keep only the raster and one reusable upload buffer, rather than three full
                // layer buffers. The tiny ownership counter audits translucent pixels too.
                original=null;source.Apply(false,true);UnityEngine.Object.Destroy(source);source=null;
                var upload=new Color32[raster.Length];var coverage=new byte[raster.Length];
                int uncovered=0,overlap=0,mismatch=0,wrongOwner=0,boundsErrors=0;
                root=new GameObject(SceneName);root.transform.SetParent(parent,false);root.transform.position=Vector3.zero;
                scene=root.AddComponent<SurfaceWorldScene>();scene.MapId=mapId;scene.SourcePath=path;
                scene.SourceWidth=sw;scene.SourceHeight=sh;scene.PixelsPerUnit=ppu;
                scene.RasterWidth=pw;scene.RasterHeight=ph;scene.WorldBounds=new Rect(0,0,width,height);
                scene.SourceAspectRatioError=aspectError;
                scene.SourceOpaquePixelCount=sourceOpaque;scene.RasterOpaquePixelCount=rasterOpaque;
                scene.SourcePartialAlphaPixelCount=sourcePartial;scene.RasterPartialAlphaPixelCount=rasterPartial;
                scene.LayerPixelCounts=opaqueCounts;scene.LayerRasterPixelCounts=assignedCounts;
                scene.LayerOpaquePixelBounds=new RectInt[3];
                scene.SupportOnWalkablePixels=edgeOnLand;scene.Layers=new SpriteRenderer[3];
                for(int layer=0;layer<3;layer++)
                {
                    // Slots stay fixed (background / support / floor) even when one is empty.
                    // An all-transparent layer contributes nothing and owns no GPU texture.
                    if(opaqueCounts[layer]==0)continue;
                    scene.LayerOpaquePixelBounds[layer]=new RectInt(left[layer],bottom[layer],right[layer]-left[layer]+1,top[layer]-bottom[layer]+1);
                    Array.Clear(upload,0,upload.Length);
                    for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
                    {
                        int i=y*pw+x;
                        if(mask[(y/ppu)*width+x/ppu]==layer)upload[i]=raster[i];
                    }
                    // Audit the actual buffer before releasing its CPU-readable texture copy.
                    // Exact straight RGBA equality is required, including 0<alpha<255; no
                    // opacity is multiplied between layers because every pixel has one owner.
                    for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
                    {
                        int i=y*pw+x,owner=mask[(y/ppu)*width+x/ppu];
                        Color32 actual=upload[i],expected=owner==layer?raster[i]:default;
                        if(actual.r!=expected.r||actual.g!=expected.g||actual.b!=expected.b||actual.a!=expected.a)mismatch++;
                        if(actual.a==0)continue;
                        coverage[i]++;
                        if(owner!=layer)wrongOwner++;
                        if(!scene.LayerOpaquePixelBounds[layer].Contains(new Vector2Int(x,y)))boundsErrors++;
                    }
                    var texture=new Texture2D(pw,ph,TextureFormat.RGBA32,false)
                    {name=mapId+" surface layer "+layer,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                    scene.Own(texture);texture.SetPixels32(upload);texture.Apply(false,true);
                    var sprite=Sprite.Create(texture,new Rect(0,0,pw,ph),Vector2.zero,ppu,0,SpriteMeshType.FullRect);
                    sprite.name=mapId+"_surface_composition_"+layer;scene.Own(sprite);
                    var go=new GameObject(LayerNames[layer]);go.transform.SetParent(root.transform,false);
                    var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=Color.white;
                    renderer.sortingOrder=LayerOrders[layer];scene.Layers[layer]=renderer;
                }
                for(int i=0;i<raster.Length;i++)
                {
                    if(raster[i].a>0&&coverage[i]==0)uncovered++;
                    if(coverage[i]>1)overlap++;
                }
                scene.UncoveredPixels=uncovered;scene.OverlappingPixels=overlap;scene.MismatchedPixels=mismatch;
                scene.WrongOwnerPixels=wrongOwner;scene.PixelBoundsErrors=boundsErrors;
                if(uncovered!=0||overlap!=0||mismatch!=0||wrongOwner!=0||boundsErrors!=0)
                    throw new InvalidDataException("Surface composition pixel registration failed");
                return true;
            }
            catch(Exception error)
            {
                if(source!=null)UnityEngine.Object.Destroy(source);
                if(root!=null){root.SetActive(false);UnityEngine.Object.Destroy(root);}
                scene=null;
                Debug.LogWarning("Surface composition unavailable for "+mapId+"; retain existing map artwork. "+error.Message);
                return false;
            }
        }

        static bool Blocked(char c)=>c=='W'||c=='%'||c=='\0';
        static bool Water(char c)=>c=='~'||c=='V';
        static byte[] BuildMask(char[,] cells,int width,int height)
        {
            var mask=new byte[width*height];
            // Background remains fixed. A supporting front extends south only into blocked
            // cells; neither a water cell nor a playable floor cell receives that front layer.
            for(int x=0;x<width;x++)
            {
                int drop=SupportDepthTiles;
                for(int y=height-1;y>=0;y--)
                {
                    char c=cells[x,y];
                    if(Water(c)){mask[y*width+x]=2;drop=SupportDepthTiles;}
                    else if(!Blocked(c)){mask[y*width+x]=2;drop=0;}
                    else if(drop<SupportDepthTiles){mask[y*width+x]=1;drop++;}
                }
            }
            return mask;
        }

        static bool SourceHeaderSupported(string path)
        {
            var file=new FileInfo(path);if(file.Length<24||file.Length>64L*1024*1024)return false;
            var bytes=new byte[24];
            using(var stream=File.OpenRead(path))
            {
                int read=0;while(read<24){int count=stream.Read(bytes,read,24-read);if(count==0)return false;read+=count;}
            }
            if(bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||bytes[4]!=13||bytes[5]!=10||bytes[6]!=26||bytes[7]!=10
                ||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)return false;
            int w=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];
            int h=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
            return w>=32&&h>=32&&w<=4096&&h<=4096;
        }
    }

    /// <summary>Scene-owned surface artwork and source registration evidence for QA.</summary>
    public sealed class SurfaceWorldScene : MonoBehaviour
    {
        public string MapId {get;internal set;}
        public string SourcePath {get;internal set;}
        public int SourceWidth {get;internal set;}
        public int SourceHeight {get;internal set;}
        public int PixelsPerUnit {get;internal set;}
        public int RasterWidth {get;internal set;}
        public int RasterHeight {get;internal set;}
        public Rect WorldBounds {get;internal set;}
        public float SourceAspectRatioError {get;internal set;}
        public int SourceOpaquePixelCount {get;internal set;}
        public int RasterOpaquePixelCount {get;internal set;}
        public int SourcePartialAlphaPixelCount {get;internal set;}
        public int RasterPartialAlphaPixelCount {get;internal set;}
        public int[] LayerPixelCounts {get;internal set;}=Array.Empty<int>();
        public int[] LayerRasterPixelCounts {get;internal set;}=Array.Empty<int>();
        public RectInt[] LayerOpaquePixelBounds {get;internal set;}=Array.Empty<RectInt>();
        public int UncoveredPixels {get;internal set;}
        public int OverlappingPixels {get;internal set;}
        public int MismatchedPixels {get;internal set;}
        public int WrongOwnerPixels {get;internal set;}
        public int PixelBoundsErrors {get;internal set;}
        public int SupportOnWalkablePixels {get;internal set;}
        public SpriteRenderer[] Layers {get;internal set;}=Array.Empty<SpriteRenderer>();
        public int LayerSlotCount=>Layers.Length;
        public int LayerCount {get{int n=0;foreach(var layer in Layers)if(layer!=null)n++;return n;}}
        public long TexturePayloadBytes=>(long)LayerCount*RasterWidth*RasterHeight*4;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public int OwnedResourceCount=>owned.Count;
        internal void Own(UnityEngine.Object resource)=>owned.Add(resource);
        void OnDestroy(){foreach(var resource in owned)if(resource!=null)Destroy(resource);owned.Clear();}
    }
}
