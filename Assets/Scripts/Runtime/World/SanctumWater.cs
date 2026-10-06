using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed class SanctumWater : MonoBehaviour
    {
        static Sprite[] ripples, falls, foam;
        readonly List<SpriteRenderer> water = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> waterfall = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> spray = new List<SpriteRenderer>();
        float clock;int shown=-1;
        public int ElementCount => water.Count+waterfall.Count+spray.Count;
        static Sprite Make(PixelCanvas p,string name,float ppu)
        {
            var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.SetPixels32(p.ToTexturePixels());t.Apply();
            var s=Sprite.Create(t,new Rect(0,0,p.Width,p.Height),Vector2.one*.5f,ppu);s.name=name;return s;
        }
        static void Prepare()
        {
            if(ripples!=null)return;ripples=new Sprite[12];falls=new Sprite[12];foam=new Sprite[12];
            for(int f=0;f<12;f++){
                var p=new PixelCanvas(32,12);float phase=f*Mathf.PI/6;
                for(int n=0;n<3;n++){int x=4+n*7+(int)(Mathf.Sin(phase+n)*2),y=3+n*2;p.HLine(x,x+5,y,new Color32(115,149,118,(byte)(35+20*Mathf.Sin(phase+n))));}
                ripples[f]=Make(p,"sanctum_ripple_"+f,32);
                p=new PixelCanvas(24,192);
                for(int y=0;y<192;y++)for(int x=0;x<24;x++){
                    float edge=Mathf.Abs(x-12+Mathf.Sin(y*.07f+phase)*1.5f);int stripe=(y+f*16+x*7)%48;
                    if(edge<3+(y>165?2:0))p.Set(x,y,new Color32(146,185,175,(byte)(stripe<23?145:65)));
                    if(edge<1.2f&&stripe<13)p.Set(x,y,new Color32(194,214,202,190));
                }
                falls[f]=Make(p,"sanctum_fall_"+f,32);
                p=new PixelCanvas(64,28);
                for(int n=0;n<12;n++){float a=n*Mathf.PI/6;float r=5+((f+n*3)%12)*1.5f;int x=32+(int)(Mathf.Cos(a)*r),y=14+(int)(Mathf.Sin(a)*r*.35f);p.Ellipse(x,y,3,1,new Color32(176,201,175,(byte)(90-((f+n*3)%12)*5)));}
                foam[f]=Make(p,"sanctum_foam_"+f,32);
            }
        }
        SpriteRenderer Add(string name,Vector2 pos,Sprite sprite,int order,Vector2 scale)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);go.transform.position=pos;go.transform.localScale=new Vector3(scale.x,scale.y,1);
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=order;return sr;
        }
        public void Setup(char[,] cells,int w,int h,bool withWaterfall=true)
        {
            Prepare();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(cells[x,y]=='~'&&((x*73856093^y*19349663)&0x7fffffff)%7==0&&water.Count<100)
                water.Add(Add("Quiet water",new Vector2(x+.5f,y+.5f),ripples[(x+y)%12],-29400,Vector2.one*.82f));
            if(!withWaterfall)return;
            waterfall.Add(Add("Thin upper fall",new Vector2(24,62),falls[0],YSort.OrderFor(54)+1,new Vector2(.8f,2)));
            waterfall.Add(Add("Broken ledge fall",new Vector2(24,53.3f),falls[0],YSort.OrderFor(49),new Vector2(.65f,.68f)));
            spray.Add(Add("Ledge foam",new Vector2(24,55.5f),foam[0],YSort.OrderFor(54)+2,new Vector2(1.3f,1)));
            spray.Add(Add("Pool foam",new Vector2(24,51.2f),foam[0],YSort.OrderFor(49)+1,new Vector2(1.7f,1.3f)));
            // Low-opacity local spray only, using the existing subtle light sprite as mist.
            var mist=Add("Localized waterfall mist",new Vector2(24,54.3f),Game.Art.Get("fx_glow"),YSort.OrderFor(49),new Vector2(3.5f,1.2f));
            mist.color=new Color(.55f,.68f,.59f,.12f);
        }
        void Update()
        {
            if(!Game.IsWorldRunning)return;clock+=Time.deltaTime;int f=(int)(clock*8)%12;if(f==shown)return;shown=f;
            int rippleFrame=(int)(clock*4)%12;
            for(int i=0;i<water.Count;i++)water[i].sprite=ripples[(rippleFrame+i*3)%12];
            for(int i=0;i<waterfall.Count;i++)waterfall[i].sprite=falls[(f+i*4)%12];
            for(int i=0;i<spray.Count;i++)spray[i].sprite=foam[(f+i*6)%12];
        }
    }
}
