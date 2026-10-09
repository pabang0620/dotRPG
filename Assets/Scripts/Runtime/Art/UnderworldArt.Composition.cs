using System;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        const int CompositionPpu = 32;
        const int CompositionHdPpu = 48;
        const int CompositionHdMaxDimension = 4096;
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
        /// A single authored composition is partitioned into three perfectly registered layers.
        /// This selects pixels, never repaints the cliff or clips the source into a replacement
        /// terrain shape. Collision remains owned by WorldBuilder's existing shared contour.
        /// </summary>
        static bool TryBuildComposition(Transform root,string id,char[,] cells,int w,int h,UnderworldContour contour)
        {
            int variant = id=="hollow_descent"?0:id=="hollow_roots"?1:id=="hollow_fungal"?2:id=="hollow_depths"?3:-1;
            if(variant<0||w<=0||h<=0)return false;
            string basePath=Path.Combine(Application.streamingAssetsPath,"Underworld","composition-"+CompositionKinds[variant]+".png");
            string hdPath=Path.Combine(Application.streamingAssetsPath,"Underworld","composition-"+CompositionKinds[variant]+"_hd.png");
            // The old renderer remains available while original art is absent or incomplete.
            if(!File.Exists(basePath)&&!File.Exists(hdPath))return false;
            Texture2D source=null;
            GameObject sceneObject=null;
            try
            {
                source=LoadCompositionSource(basePath,hdPath,w,h,out string path,out bool highResolution,out string fallbackReason);
                if(source==null)return false;
                int ppu=highResolution?CompositionHdPpu:CompositionPpu;
                int sourceWidth=source.width,sourceHeight=source.height;
                var sourcePixels=source.GetPixels32();
                int pw=w*ppu,ph=h*ppu;
                int sourceOpaque=0;
                for(int i=0;i<sourcePixels.Length;i++)if(sourcePixels[i].a>0)sourceOpaque++;
                contour=contour??new UnderworldContour(cells,w,h,id);
                byte[] mask=CompositionMask(contour);
                var raster=new Color32[pw*ph];
                var layers=new[]{new Color32[raster.Length],new Color32[raster.Length],new Color32[raster.Length]};
                var counts=new int[3];var assignedCounts=new int[3];
                int faceOnLand=0,opaque=0;
                // Unity image pixels and world coordinates both begin at bottom-left. A single
                // nearest-neighbour resample is shared by all layers, with no independent offsets.
                for(int y=0;y<ph;y++)
                {
                    int sy=Math.Min(sourceHeight-1,(int)((y+.5f)*sourceHeight/ph));
                    int my=Math.Min(contour.Rows-1,y*UnderworldContour.Scale/ppu);
                    for(int x=0;x<pw;x++)
                    {
                        int sx=Math.Min(sourceWidth-1,(int)((x+.5f)*sourceWidth/pw));
                        int mx=Math.Min(contour.Columns-1,x*UnderworldContour.Scale/ppu);
                        int at=y*pw+x,layer=mask[my*contour.Columns+mx];
                        Color32 color=sourcePixels[sy*sourceWidth+sx];
                        raster[at]=color;layers[layer][at]=color;assignedCounts[layer]++;
                        if(color.a==0)continue;
                        opaque++;counts[layer]++;
                        if(layer==1&&contour.At(mx,my)==1)faceOnLand++;
                    }
                }
                int uncovered=0,overlap=0,mismatched=0;
                // Audit actual written buffers, not just the classification decisions.
                for(int i=0;i<raster.Length;i++)
                {
                    Color32 expected=raster[i],found=default;
                    int matches=0;
                    for(int layer=0;layer<3;layer++)if(layers[layer][i].a>0){matches++;found=layers[layer][i];}
                    if(expected.a>0&&matches==0)uncovered++;
                    if(matches>1)overlap++;
                    if(expected.a>0&&(matches!=1||found.r!=expected.r||found.g!=expected.g||found.b!=expected.b||found.a!=expected.a))mismatched++;
                }
                sceneObject=new GameObject("Underworld composition");sceneObject.transform.SetParent(root,false);
                sceneObject.transform.position=Vector3.zero;
                var scene=sceneObject.AddComponent<UnderworldCompositionScene>();
                scene.SourcePath=path;scene.SourceWidth=sourceWidth;scene.SourceHeight=sourceHeight;
                scene.PixelsPerUnit=ppu;scene.UsesHighResolution=highResolution;
                scene.HighResolutionCandidatePath=hdPath;scene.ResolutionFallbackReason=fallbackReason;
                scene.RasterWidth=pw;scene.RasterHeight=ph;scene.WorldBounds=new Rect(0,0,w,h);
                scene.FocusWorld=CompositionFocusOnLand(contour,CompositionFocus[variant]);
                scene.LayerPixelCounts=counts;scene.LayerRasterPixelCounts=assignedCounts;
                scene.SourceOpaquePixelCount=sourceOpaque;scene.RasterOpaquePixelCount=opaque;
                scene.UncoveredPixels=uncovered;scene.OverlappingPixels=overlap;
                scene.MismatchedPixels=mismatched;scene.FaceOnWalkablePixels=faceOnLand;
                scene.MasonryDepthTiles=CompositionFaceDepth;
                scene.Layers=new SpriteRenderer[3];
                for(int layer=0;layer<3;layer++)
                {
                    var texture=new Texture2D(pw,ph,TextureFormat.RGBA32,false)
                    {name=id+" composition layer "+layer,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                    scene.Own(texture);
                    texture.SetPixels32(layers[layer]);texture.Apply(false,false);
                    var sprite=Sprite.Create(texture,new Rect(0,0,pw,ph),Vector2.zero,ppu,0,SpriteMeshType.FullRect);
                    sprite.name=id+"_composition_"+layer;scene.Own(sprite);
                    var go=new GameObject(CompositionNames[layer]);go.transform.SetParent(sceneObject.transform,false);
                    var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;
                    renderer.sortingOrder=CompositionOrders[layer];renderer.color=Color.white;
                    scene.Layers[layer]=renderer;
                }
                if(variant==3)BuildCompositionColumnOcclusion(raster,pw,ph,ppu,scene);
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
                UnityEngine.Object.Destroy(source);
                return true;
            }
            catch(Exception error)
            {
                if(source!=null)UnityEngine.Object.Destroy(source);
                if(sceneObject!=null)UnityEngine.Object.Destroy(sceneObject);
                Debug.LogWarning("Could not load cave composition for "+id+"; using existing cave renderer. "+error.Message);
                return false;
            }
        }

        // HD art is opt-in per map. Merely renaming a low-resolution image must not cause
        // a more expensive raster without supplying additional source samples. Check the PNG
        // header before decoding to cap accidental oversized allocations, then verify decoded size.
        static Texture2D LoadCompositionSource(string basePath,string hdPath,int w,int h,
            out string path,out bool highResolution,out string fallbackReason)
        {
            path=basePath;highResolution=false;fallbackReason="HD source not present";
            if(File.Exists(hdPath))
            {
                Texture2D candidate=null;
                try
                {
                    if(CompositionHdHeaderValid(hdPath,w,h,out fallbackReason))
                    {
                        candidate=DecodeCompositionSource(hdPath);
                        if(candidate!=null&&CompositionHdSizeValid(candidate.width,candidate.height,w,h,out fallbackReason))
                        {
                            path=hdPath;highResolution=true;fallbackReason="";
                            return candidate;
                        }
                        if(candidate==null)fallbackReason="HD PNG could not be decoded";
                    }
                }
                catch(Exception error){fallbackReason="HD source rejected: "+error.Message;}
                if(candidate!=null)UnityEngine.Object.Destroy(candidate);
                Debug.LogWarning("Cave HD artwork rejected; using original artwork. "+fallbackReason+" ("+hdPath+")");
            }
            return File.Exists(basePath)?DecodeCompositionSource(basePath):null;
        }

        static Texture2D DecodeCompositionSource(string path)
        {
            Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false)
            {name="Underworld composition source "+Path.GetFileNameWithoutExtension(path),filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            try
            {
                if(ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)&&texture.width>=32&&texture.height>=32)return texture;
                UnityEngine.Object.Destroy(texture);return null;
            }
            catch{UnityEngine.Object.Destroy(texture);throw;}
        }

        static bool CompositionHdHeaderValid(string path,int w,int h,out string reason)
        {
            var file=new FileInfo(path);
            if(file.Length<24||file.Length>64L*1024*1024){reason="HD PNG file size is outside the supported range";return false;}
            var header=new byte[24];
            using(var stream=File.OpenRead(path))
            {
                int read=0;
                while(read<header.Length){int count=stream.Read(header,read,header.Length-read);if(count==0)break;read+=count;}
                if(read<header.Length){reason="HD PNG header is incomplete";return false;}
            }
            if(header[0]!=137||header[1]!=80||header[2]!=78||header[3]!=71||header[4]!=13||header[5]!=10||header[6]!=26||header[7]!=10
                ||header[12]!=73||header[13]!=72||header[14]!=68||header[15]!=82)
            {reason="HD source is not a PNG with an IHDR header";return false;}
            int width=(header[16]<<24)|(header[17]<<16)|(header[18]<<8)|header[19];
            int height=(header[20]<<24)|(header[21]<<16)|(header[22]<<8)|header[23];
            return CompositionHdSizeValid(width,height,w,h,out reason);
        }

        static bool CompositionHdSizeValid(int width,int height,int w,int h,out string reason)
        {
            if(width<w*CompositionHdPpu||height<h*CompositionHdPpu)
            {reason="HD source must provide at least "+CompositionHdPpu+" pixels per world unit";return false;}
            if(width>CompositionHdMaxDimension||height>CompositionHdMaxDimension)
            {reason="HD source exceeds the "+CompositionHdMaxDimension+" pixel dimension limit";return false;}
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
