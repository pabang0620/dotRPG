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
        public const int StandardPixelsPerUnit=32,HighPixelsPerUnit=48,UltraPixelsPerUnit=96,HdPixelsPerUnit=128;
        const int MaxDimension=12288;
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
                int ppu=sw>=width*HdPixelsPerUnit&&sh>=height*HdPixelsPerUnit?HdPixelsPerUnit
                    :sw>=width*UltraPixelsPerUnit&&sh>=height*UltraPixelsPerUnit?UltraPixelsPerUnit
                    :sw>=width*HighPixelsPerUnit&&sh>=height*HighPixelsPerUnit?HighPixelsPerUnit:StandardPixelsPerUnit;
                int pw=checked(width*ppu),ph=checked(height*ppu);
                if(pw>MaxDimension||ph>MaxDimension||(SystemInfo.maxTextureSize>0&&(pw>SystemInfo.maxTextureSize||ph>SystemInfo.maxTextureSize)))
                    throw new InvalidDataException("Surface raster exceeds the supported texture size");
                var original=source.GetPixels32();
                byte[] mask=BuildMask(terrainCells,width,height);
                // Exact registered 32/48/96ppu files keep their original GPU texture.
                // Older approximate-aspect inputs retain one nearest-neighbour registration.
                bool registered=sw==pw&&sh==ph;
                var raster=registered?original:new Color32[checked(pw*ph)];
                var visiblePerCell=new int[width*height];
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
                        Color32 color=original[registered?index:sy*sw+sx];
                        if(!registered)raster[index]=color;
                        assignedCounts[layer]++;
                        if(color.a==0)continue;
                        visiblePerCell[cellY*width+cellX]++;
                        rasterOpaque++;opaqueCounts[layer]++;
                        if(color.a<255)rasterPartial++;
                        left[layer]=Math.Min(left[layer],x);right[layer]=Math.Max(right[layer],x);
                        bottom[layer]=Math.Min(bottom[layer],y);top[layer]=Math.Max(top[layer],y);
                        if(layer==1&&!Blocked(terrainCells[cellX,cellY])&&!Water(terrainCells[cellX,cellY]))edgeOnLand++;
                    }
                }
                if(rasterOpaque==0)throw new InvalidDataException("Surface PNG has no visible pixels at map resolution");
                // Each layer is an exclusive mesh over one shared, unmodified texture.
                // No three full RGBA layer copies, upload buffers or per-pixel coverage array.
                var coverage=new byte[width*height];
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
                scene.LayerVertexCounts=new int[3];scene.LayerTriangleCounts=new int[3];
                Texture2D shared;
                if(registered){shared=source;source=null;}
                else shared=new Texture2D(pw,ph,TextureFormat.RGBA32,false);
                scene.SharedTexture=shared;scene.Own(shared);
                if(!registered)
                {
                    shared.SetPixels32(raster);
                    UnityEngine.Object.Destroy(source);source=null;
                }
                shared.name=mapId+" surface shared artwork";shared.filterMode=FilterMode.Point;shared.wrapMode=TextureWrapMode.Clamp;
                shared.Apply(false,true);original=null;raster=null;
                for(int layer=0;layer<3;layer++)
                {
                    // Slots stay fixed (background / support / floor) even when one is empty.
                    // An all-transparent layer contributes nothing and owns no GPU texture.
                    if(opaqueCounts[layer]==0)continue;
                    scene.LayerOpaquePixelBounds[layer]=new RectInt(left[layer],bottom[layer],right[layer]-left[layer]+1,top[layer]-bottom[layer]+1);
                    var vertices=new List<Vector2>();var triangles=new List<ushort>();
                    for(int y=0;y<height;y++)for(int x=0;x<width;)
                    {
                        if(mask[y*width+x]!=layer){x++;continue;}
                        int start=x;while(x<width&&mask[y*width+x]==layer)x++;
                        AddRun(vertices,triangles,start,x,y,ppu);
                    }
                    var sprite=Sprite.Create(shared,new Rect(0,0,pw,ph),Vector2.zero,ppu,0,SpriteMeshType.FullRect);
                    sprite.name=mapId+"_surface_composition_"+layer;scene.Own(sprite);
                    // OverrideGeometry accepts pixel coordinates in Sprite.rect space;
                    // returned vertices are in local world units and UVs are normalized.
                    sprite.OverrideGeometry(vertices.ToArray(),triangles.ToArray());
                    AuditMesh(sprite,vertices,triangles,mask,coverage,visiblePerCell,width,height,ppu,layer,
                        ref mismatch,ref wrongOwner,ref boundsErrors);
                    scene.LayerVertexCounts[layer]=sprite.vertices.Length;scene.LayerTriangleCounts[layer]=sprite.triangles.Length/3;
                    var go=new GameObject(LayerNames[layer]);go.transform.SetParent(root.transform,false);
                    var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=Color.white;
                    renderer.sortingOrder=LayerOrders[layer];scene.Layers[layer]=renderer;
                }
                for(int i=0;i<coverage.Length;i++)
                {
                    if(coverage[i]==0)uncovered+=visiblePerCell[i];
                    if(coverage[i]>1)overlap+=visiblePerCell[i];
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

        static void AddRun(List<Vector2> vertices,List<ushort> triangles,int left,int right,int y,int ppu)
        {
            if(vertices.Count+4>ushort.MaxValue)throw new InvalidDataException("Surface mesh exceeds sprite index capacity");
            ushort first=(ushort)vertices.Count;
            vertices.Add(new Vector2(left*ppu,y*ppu));vertices.Add(new Vector2(right*ppu,y*ppu));
            vertices.Add(new Vector2(right*ppu,(y+1)*ppu));vertices.Add(new Vector2(left*ppu,(y+1)*ppu));
            triangles.Add(first);triangles.Add((ushort)(first+1));triangles.Add((ushort)(first+2));
            triangles.Add((ushort)(first+2));triangles.Add((ushort)(first+3));triangles.Add(first);
        }

        static void AuditMesh(Sprite sprite,List<Vector2> supplied,List<ushort> requested,byte[] mask,byte[] coverage,
            int[] visible,int width,int height,int ppu,int layer,ref int mismatch,ref int wrongOwner,ref int boundsErrors)
        {
            var vertices=sprite.vertices;var uv=sprite.uv;var triangles=sprite.triangles;
            if(vertices.Length!=supplied.Count||uv.Length!=vertices.Length||triangles.Length!=requested.Count)
                throw new InvalidDataException("Surface sprite geometry changed unexpectedly");
            for(int i=0;i<vertices.Length;i++)
            {
                Vector2 expected=supplied[i]/ppu;
                Vector2 expectedUv=new Vector2(supplied[i].x/sprite.rect.width,supplied[i].y/sprite.rect.height);
                if((vertices[i]-expected).sqrMagnitude>1e-8f||(uv[i]-expectedUv).sqrMagnitude>1e-10f)mismatch++;
                if(vertices[i].x<-.0001f||vertices[i].y<-.0001f||vertices[i].x>width+.0001f||vertices[i].y>height+.0001f)boundsErrors++;
            }
            for(int i=0;i<triangles.Length;i++)if(triangles[i]!=requested[i])mismatch++;
            // The returned quads are integer-tile aligned and use the two audited
            // triangles above; cell coverage therefore proves coverage of every pixel.
            for(int i=0;i<vertices.Length;i+=4)
            {
                int x0=Mathf.RoundToInt(vertices[i].x),x1=Mathf.RoundToInt(vertices[i+1].x),y=Mathf.RoundToInt(vertices[i].y);
                if(y<0||y>=height||x0<0||x1>width||x0>=x1){boundsErrors++;continue;}
                for(int x=x0;x<x1;x++)
                {
                    int cell=y*width+x;coverage[cell]++;
                    if(mask[cell]!=layer)wrongOwner+=Math.Max(visible[cell],1);
                }
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
            var file=new FileInfo(path);if(file.Length<24||file.Length>96L*1024*1024)return false;
            var bytes=new byte[24];
            using(var stream=File.OpenRead(path))
            {
                int read=0;while(read<24){int count=stream.Read(bytes,read,24-read);if(count==0)return false;read+=count;}
            }
            if(bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||bytes[4]!=13||bytes[5]!=10||bytes[6]!=26||bytes[7]!=10
                ||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)return false;
            int w=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];
            int h=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
            int gpuLimit=SystemInfo.maxTextureSize;
            return w>=32&&h>=32&&w<=MaxDimension&&h<=MaxDimension&&(long)w*h<=64L*1024*1024
                &&(gpuLimit<=0||(w<=gpuLimit&&h<=gpuLimit));
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
        public Texture2D SharedTexture {get;internal set;}
        public int[] LayerVertexCounts {get;internal set;}=Array.Empty<int>();
        public int[] LayerTriangleCounts {get;internal set;}=Array.Empty<int>();
        public int LayerSlotCount=>Layers.Length;
        public int LayerCount {get{int n=0;foreach(var layer in Layers)if(layer!=null)n++;return n;}}
        public long TexturePayloadBytes=>SharedTexture==null?0:(long)SharedTexture.width*SharedTexture.height*4;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public int OwnedResourceCount=>owned.Count;
        public UnityEngine.Object[] OwnedResources=>owned.ToArray();
        internal void Own(UnityEngine.Object resource)=>owned.Add(resource);
        void OnDestroy(){foreach(var resource in owned)if(resource!=null)Destroy(resource);owned.Clear();}
    }
}
