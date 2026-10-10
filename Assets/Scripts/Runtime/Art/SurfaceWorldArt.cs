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
                public const string SceneName="Surface world composition";
        const int SupportDepthTiles=3;
        static readonly string[] LayerNames={"Surface distant scenery","Surface supporting edges","Surface floor and water"};
        static readonly int[] LayerOrders={-31000,-30500,-30000};

        /// <summary>[MAP ART] Resource path of a map's artwork (Resources/WorldArt/Surface, loaded through MapArtCache).</summary>
        public static string SourcePath(string mapId)=>MapArtCache.SurfacePath(mapId);
        public static bool HasArtwork(string mapId)=>MapArtCache.Get(SourcePath(mapId))!=null;

        /// <summary>
        /// Does not hide or mutate any existing renderer, collider, cell, route or gameplay object.
        /// False means the existing map renderer should be retained. Generated sprites are map-owned;
        /// the shared texture belongs to MapArtCache (GPU-compressed, never read back on the CPU).
        /// </summary>
        public static bool TryBuild(Transform parent,string mapId,char[,] terrainCells,int width,int height,out SurfaceWorldScene scene)
        {
            scene=null;
            var info=MapRegistry.Get(mapId);
            if(parent==null||info==null||info.worldLayer!=WorldLayer.Surface||info.IsInterior||info.instanced
                ||width<=0||height<=0||terrainCells==null||terrainCells.GetLength(0)!=width||terrainCells.GetLength(1)!=height)return false;
            var existing=parent.Find(SceneName)?.GetComponent<SurfaceWorldScene>();
            if(existing!=null&&existing.MapId==mapId){scene=existing;return true;}
            var shared=MapArtCache.Get(SourcePath(info.id));
            if(shared==null)return false;

            GameObject root=null;
            try
            {
                int sw=shared.width,sh=shared.height;
                if(sw<32||sh<32)throw new InvalidDataException("Surface artwork is too small");
                float aspectError=Mathf.Abs(((float)sw/sh)/((float)width/height)-1f);
                if(aspectError>.02f)throw new InvalidDataException("Surface artwork aspect ratio differs from the world by more than 2%");
                if(SystemInfo.maxTextureSize>0&&(sw>SystemInfo.maxTextureSize||sh>SystemInfo.maxTextureSize))
                    throw new InvalidDataException("Surface artwork exceeds the supported texture size");
                // Density follows the imported texture: full size on PC, capped per platform (Android 4096) elsewhere.
                float ppuX=(float)sw/width,ppuY=(float)sh/height;
                byte[] mask=BuildMask(terrainCells,width,height);
                root=new GameObject(SceneName);root.transform.SetParent(parent,false);root.transform.position=Vector3.zero;
                scene=root.AddComponent<SurfaceWorldScene>();scene.MapId=mapId;scene.SourcePath=SourcePath(info.id);
                scene.SourceWidth=sw;scene.SourceHeight=sh;scene.PixelsPerUnit=Mathf.RoundToInt(ppuX);
                scene.RasterWidth=sw;scene.RasterHeight=sh;scene.WorldBounds=new Rect(0,0,width,height);
                scene.SourceAspectRatioError=aspectError;
                // Pixel statistics need a CPU-readable copy, which compressed artwork no longer has (-1 = not measured).
                scene.SourceOpaquePixelCount=scene.RasterOpaquePixelCount=-1;
                scene.SourcePartialAlphaPixelCount=scene.RasterPartialAlphaPixelCount=-1;
                scene.UncoveredPixels=scene.OverlappingPixels=scene.MismatchedPixels=scene.WrongOwnerPixels=scene.PixelBoundsErrors=-1;
                scene.SupportOnWalkablePixels=-1;
                scene.Layers=new SpriteRenderer[3];scene.LayerVertexCounts=new int[3];scene.LayerTriangleCounts=new int[3];
                scene.LayerOpaquePixelBounds=new RectInt[3];
                scene.SharedTexture=shared;
                for(int layer=0;layer<3;layer++)
                {
                    // Slots stay fixed (background / support / floor) even when one is empty.
                    var rects=MapArtMesh.Rects(mask,width,height,layer);
                    var sprite=MapArtMesh.LayerSprite(shared,rects,width,height,ppuX);
                    if(sprite==null)continue;
                    sprite.name=mapId+"_surface_composition_"+layer;scene.Own(sprite);
                    scene.LayerVertexCounts[layer]=sprite.vertices.Length;scene.LayerTriangleCounts[layer]=sprite.triangles.Length/3;
                    var go=new GameObject(LayerNames[layer]);go.transform.SetParent(root.transform,false);
                    go.transform.localScale=MapArtMesh.Registration(ppuX,ppuY);
                    var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=Color.white;
                    renderer.sortingOrder=LayerOrders[layer];scene.Layers[layer]=renderer;
                }
                return true;
            }
            catch(Exception error)
            {
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
        /// <summary>GPU size of the shared artwork (compressed formats included).</summary>
        public long TexturePayloadBytes=>SharedTexture==null?0:UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(SharedTexture);
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public int OwnedResourceCount=>owned.Count;
        public UnityEngine.Object[] OwnedResources=>owned.ToArray();
        internal void Own(UnityEngine.Object resource)=>owned.Add(resource);
        void OnDestroy(){foreach(var resource in owned)if(resource!=null)Destroy(resource);owned.Clear();}
    }
}
