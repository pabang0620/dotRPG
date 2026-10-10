using System;
using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        const int CompositionPpu = 32;
        const int CompositionHdPpu = 96;
        const int CompositionFaceDepth = 3;
        static readonly string[] CompositionKinds = { "descent", "roots", "fungal", "depths" };
        static readonly string[] CompositionNames =
        {
            "Composition deep scenery", "Composition supporting masonry", "Composition floor and water"
        };
        static readonly int[] CompositionOrders = { -31000, -30500, -30000 };
        static readonly Vector2[] CompositionFocus =
        {
            new Vector2(25.5f,25.5f),new Vector2(25.5f,35.5f),
            new Vector2(31.5f,37.5f),new Vector2(31.5f,25.5f)
        };

        /// <summary>
        /// A single authored composition drawn as three registered layers: one shared texture (MapArtCache,
        /// GPU-compressed, never read back) under three meshes cut from the contour mask. Nothing is repainted
        /// or copied. Collision remains owned by WorldBuilder's existing shared contour.
        /// </summary>
        static bool TryBuildComposition(Transform root,string id,char[,] cells,int w,int h,UnderworldContour contour)
        {
            int variant = id=="hollow_descent"?0:id=="hollow_roots"?1:id=="hollow_fungal"?2:id=="hollow_depths"?3:-1;
            if(variant<0||w<=0||h<=0)return false;
            string basePath=MapArtCache.CompositionPath(CompositionKinds[variant],false);
            string hdPath=MapArtCache.CompositionPath(CompositionKinds[variant],true);
            GameObject sceneObject=null;
            try
            {
                // The old renderer remains available while original art is absent.
                var source=LoadCompositionSource(basePath,hdPath,w,h,out string path,out bool highResolution,out string fallbackReason);
                if(source==null)return false;
                int sourceWidth=source.width,sourceHeight=source.height;
                float ppuX=(float)sourceWidth/w,ppuY=(float)sourceHeight/h;
                contour=contour??new UnderworldContour(cells,w,h,id);
                byte[] mask=CompositionMask(contour);
                sceneObject=new GameObject("Underworld composition");sceneObject.transform.SetParent(root,false);
                sceneObject.transform.position=Vector3.zero;
                var scene=sceneObject.AddComponent<UnderworldCompositionScene>();
                scene.SourcePath=path;scene.SourceWidth=sourceWidth;scene.SourceHeight=sourceHeight;
                scene.PixelsPerUnit=Mathf.RoundToInt(ppuX);scene.UsesHighResolution=highResolution;
                scene.HighResolutionCandidatePath=hdPath;scene.ResolutionFallbackReason=fallbackReason;
                scene.RasterWidth=sourceWidth;scene.RasterHeight=sourceHeight;scene.WorldBounds=new Rect(0,0,w,h);
                scene.FocusWorld=CompositionFocusOnLand(contour,CompositionFocus[variant]);
                // Pixel statistics need a CPU-readable copy, which compressed artwork no longer has (-1 = not measured).
                // The mask never assigns masonry to land (CompositionMask), so no face lies on walkable ground.
                scene.SourceOpaquePixelCount=scene.RasterOpaquePixelCount=-1;
                scene.UncoveredPixels=scene.OverlappingPixels=scene.MismatchedPixels=-1;
                scene.FaceOnWalkablePixels=0;
                scene.MasonryDepthTiles=CompositionFaceDepth;
                scene.SharedTexture=source;
                scene.Layers=new SpriteRenderer[3];
                for(int layer=0;layer<3;layer++)
                {
                    var rects=MapArtMesh.Rects(mask,contour.Columns,contour.Rows,layer);
                    var sprite=MapArtMesh.LayerSprite(source,rects,contour.Columns,contour.Rows,ppuX);
                    var go=new GameObject(CompositionNames[layer]);go.transform.SetParent(sceneObject.transform,false);
                    go.transform.localScale=MapArtMesh.Registration(ppuX,ppuY);
                    var renderer=go.AddComponent<SpriteRenderer>();renderer.color=Color.white;
                    renderer.sortingOrder=CompositionOrders[layer];
                    if(sprite!=null){sprite.name=id+"_composition_"+layer;scene.Own(sprite);renderer.sprite=sprite;}
                    scene.Layers[layer]=renderer;
                }
                if(variant==3)BuildCompositionColumnOcclusion(source,ppuX,ppuY,scene);
                // Effects are local to this map. Distant composition sprites remain fixed;
                // translating any layer with the camera would break their shared registration.
                var water=new GameObject("Cave spring ripples");water.transform.SetParent(sceneObject.transform,false);
                // A ripple is .82 tiles wide: keep its center clear of the precise stone rim.
                // This visual-only mask never changes the authored collision/navigation cells.
                var visualRippleCells=new char[w,h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    visualRippleCells[x,y]=contour.WaterDistance(x+.5f,y+.5f)>.45f?'~':'W';
                water.AddComponent<SanctumWater>().Setup(visualRippleCells,w,h,false);
                sceneObject.AddComponent<UnderworldDepthAmbience>().Setup(variant,scene.FocusWorld,contour,w,h);
                return true;
            }
            catch(Exception error)
            {
                if(sceneObject!=null)UnityEngine.Object.Destroy(sceneObject);
                Debug.LogWarning("Could not load cave composition for "+id+"; using existing cave renderer. "+error.Message);
                return false;
            }
        }

        // HD art is preferred. Phones receive it capped at 4096 px by the importer, which is still
        // denser than the original picture, so only the aspect ratio and the GPU size limit are checked.
        static Texture2D LoadCompositionSource(string basePath,string hdPath,int w,int h,
            out string path,out bool highResolution,out string fallbackReason)
        {
            path=basePath;highResolution=false;fallbackReason="HD source not present";
            var candidate=MapArtCache.Get(hdPath);
            if(candidate!=null)
            {
                if(CompositionHdSizeValid(candidate.width,candidate.height,w,h,out fallbackReason))
                {
                    path=hdPath;highResolution=true;fallbackReason="";
                    return candidate;
                }
                Debug.LogWarning("Cave HD artwork rejected; using original artwork. "+fallbackReason+" ("+hdPath+")");
            }
            return MapArtCache.Get(basePath);
        }

        static bool CompositionHdSizeValid(int width,int height,int w,int h,out string reason)
        {
            if(width<w*CompositionPpu||height<h*CompositionPpu)
            {reason="HD source must provide at least "+CompositionPpu+" pixels per world unit";return false;}
            if(SystemInfo.maxTextureSize>0&&(width>SystemInfo.maxTextureSize||height>SystemInfo.maxTextureSize))
            {reason="HD source exceeds the GPU texture size limit";return false;}
            float ratio=(float)width/height,expected=(float)w/h;
            if(Mathf.Abs(ratio/expected-1f)>.005f)
            {reason="HD source aspect ratio differs from the world by more than 0.5%";return false;}
            reason="";return true;
        }

        static byte[] CompositionMask(UnderworldContour contour)
        {
            int columns=contour.Columns,rows=contour.Rows;
            var mask=new byte[columns*rows];
            int maxDepth=CompositionFaceDepth*UnderworldContour.Scale;
            // Scan from north to south. A face can begin only below actual land; water and
            // newly reached floor terminate it. No foreground masonry is ever laid on a path.
            for(int x=0;x<columns;x++)
            {
                int drop=maxDepth;
                for(int y=rows-1;y>=0;y--)
                {
                    byte kind=contour.At(x,y);
                    if(kind==1){mask[y*columns+x]=2;drop=0;}
                    else if(kind==2){mask[y*columns+x]=2;drop=maxDepth;}
                    else if(drop<maxDepth){mask[y*columns+x]=1;drop++;}
                }
            }
            return mask;
        }

        static Vector2 CompositionFocusOnLand(UnderworldContour contour,Vector2 desired)
        {
            Vector2 best=Vector2.zero;float score=float.MaxValue;
            for(int y=0;y<contour.Height;y++)for(int x=0;x<contour.Width;x++)
            {
                var p=new Vector2(x+.5f,y+.5f);
                if(!contour.Land(p.x,p.y)||!contour.Land(p.x-.5f,p.y)||!contour.Land(p.x+.5f,p.y)
                    ||!contour.Land(p.x,p.y-.5f)||!contour.Land(p.x,p.y+.5f))continue;
                float distance=(p-desired).sqrMagnitude;
                if(distance<score){score=distance;best=p;}
            }
            if(score<float.MaxValue)return best;
            for(int y=0;y<contour.Rows;y++)for(int x=0;x<contour.Columns;x++)
                if(contour.At(x,y)==1)return new Vector2((x+.5f)/UnderworldContour.Scale,(y+.5f)/UnderworldContour.Scale);
            return desired;
        }
    }

    /// <summary>Map-owned composition and audit data. No static texture cache or parallax.</summary>
    public sealed class UnderworldCompositionScene : MonoBehaviour
    {
        public string SourcePath {get;internal set;}
        public int SourceWidth {get;internal set;}
        public int SourceHeight {get;internal set;}
        public int PixelsPerUnit {get;internal set;}
        public bool UsesHighResolution {get;internal set;}
        public string HighResolutionCandidatePath {get;internal set;}
        public string ResolutionFallbackReason {get;internal set;}
        public int RasterWidth {get;internal set;}
        public int RasterHeight {get;internal set;}
        public Rect WorldBounds {get;internal set;}
        public Vector2 FocusWorld {get;internal set;}
        public int[] LayerPixelCounts {get;internal set;}=Array.Empty<int>();
        public int[] LayerRasterPixelCounts {get;internal set;}=Array.Empty<int>();
        public int SourceOpaquePixelCount {get;internal set;}
        public int RasterOpaquePixelCount {get;internal set;}
        public int UncoveredPixels {get;internal set;}
        public int OverlappingPixels {get;internal set;}
        public int MismatchedPixels {get;internal set;}
        public int FaceOnWalkablePixels {get;internal set;}
        public int MasonryDepthTiles {get;internal set;}
        public SpriteRenderer[] Layers {get;internal set;}=Array.Empty<SpriteRenderer>();
        /// <summary>[MAP ART] The cache-owned texture all layers and column overlays draw from (not destroyed with the scene).</summary>
        public Texture2D SharedTexture {get;internal set;}
        public SpriteRenderer[] ForegroundRenderers {get;internal set;}=Array.Empty<SpriteRenderer>();
        public int LayerCount=>Layers.Length;
        readonly System.Collections.Generic.List<UnityEngine.Object> owned=new System.Collections.Generic.List<UnityEngine.Object>();
        internal void Own(UnityEngine.Object resource){owned.Add(resource);}
        void OnDestroy()
        {
            foreach(var resource in owned)if(resource!=null)Destroy(resource);
            owned.Clear();
        }
    }
}
