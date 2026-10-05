using UnityEngine;

namespace DotRPG
{
    /// <summary>Hand-authored raster choreography. Integer pixels, deterministic particles and no gameplay RNG.</summary>
    public static partial class CareerVfxArt
    {
        static readonly Color32 Pearl=PixelCanvas.Hex("fff8e8"), Red=PixelCanvas.Hex("ee454c"), Amber=PixelCanvas.Hex("ffac48"),
            Azure=PixelCanvas.Hex("3faef4"), Cyan=PixelCanvas.Hex("88f5ed"), Violet=PixelCanvas.Hex("9b6aee"),
            Ice=PixelCanvas.Hex("b8efff"), Gold=PixelCanvas.Hex("efc766"), Leaf=PixelCanvas.Hex("b6ecab");
        const float Tau=Mathf.PI*2;
        static int I(float x)=>Mathf.RoundToInt(x);
        static Vector2 Polar(float a,float r)=>new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;
        static float Hash(int n){unchecked{uint x=(uint)(n*747796405+2891336453);x=((x>>((int)(x>>28)+4))^x)*277803737;x=(x>>22)^x;return (x&65535)/65535f;}}
        static Color32 Alpha(Color32 c,float a)=>PixelCanvas.WithAlpha(c,(byte)Mathf.Clamp(I(a*255),0,255));
        static void Line(CareerRaster p,Vector2 a,Vector2 b,Color32 c,float w=1)
        {
            int width=Mathf.Max(1,I(w*CareerRaster.Density));
            int x0=I(a.x*2),y0=I(a.y*2),x1=I(b.x*2),y1=I(b.y*2);
            bool horizontal=Mathf.Abs(x1-x0)>=Mathf.Abs(y1-y0);
            for(int k=-(width/2);k<width-width/2;k++)
                p.Raw.Line(x0+(horizontal?0:k),y0+(horizontal?k:0),x1+(horizontal?0:k),y1+(horizontal?k:0),c);
        }
        static void Diamond(CareerRaster p,Vector2 at,float rx,float ry,Color32 c)
        {
            for(int y=-I(ry*2);y<=I(ry*2);y++){
                int w=I(rx*2*(1-Mathf.Abs(y)/Mathf.Max(1,ry*2)));
                p.Raw.HLine(I(at.x*2)-w,I(at.x*2)+w,I(at.y*2)+y,c);
            }
        }
        static void Glint(CareerRaster p,Vector2 at,int r,Color32 c)
        {Diamond(p,at,r,1,c);Diamond(p,at,1,r,c);p.Set(I(at.x),I(at.y),Pearl);if(r>3){p.Set(I(at.x)+2,I(at.y)+2,c);p.Set(I(at.x)-2,I(at.y)-2,c);}}
        static void Arc(CareerRaster p,Vector2 c,float rx,float ry,float angle,float span,Color32 color,int w=1)
        {Vector2 prev=c+new Vector2(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry);int steps=Mathf.Max(12,I(Mathf.Abs(span)*Mathf.Max(rx,ry)*2));for(int i=1;i<=steps;i++){float a=angle+span*i/steps;var at=c+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry);Line(p,prev,at,color,w);prev=at;}}
        static void Ring(CareerRaster p,Vector2 c,float r,Color32 color,float squash=1,int sides=0,float phase=0)
        {if(sides==0){Arc(p,c,r,r*squash,0,Tau,color);return;}for(int i=0;i<sides;i++){var a=Polar(phase+i*Tau/sides,r);var b=Polar(phase+(i+1)*Tau/sides,r);a.y*=squash;b.y*=squash;Line(p,c+a,c+b,color);}}

        static void Crystal(CareerRaster p,Vector2 c,float size,float angle,Color32 color)
        {
            Vector2 tip=c+Polar(angle,size),basePt=c-Polar(angle,size*.65f),left=c+Polar(angle+Mathf.PI/2,size*.33f),right=c+Polar(angle-Mathf.PI/2,size*.33f);
            Triangle(p,tip,left,basePt,PixelCanvas.Shade(color,.6f));Triangle(p,tip,right,basePt,color);
            Line(p,tip,basePt,Pearl);Line(p,tip,left,Alpha(Pearl,.8f));
        }
        static void Triangle(CareerRaster p,Vector2 a,Vector2 b,Vector2 c,Color32 color)
        {Poly(p,color,a,b,c);}


        static void Bolt(CareerRaster p,Vector2 from,Vector2 to,int seed,Color32 c)
        {
            var delta=to-from;var normal=new Vector2(-delta.y,delta.x).normalized;
            var points=new Vector2[8];points[0]=from;points[7]=to;
            for(int i=1;i<7;i++)points[i]=Vector2.Lerp(from,to,i/7f)+normal*(Hash(seed+i*13)-.5f)*12;
            for(int i=0;i<7;i++){Line(p,points[i],points[i+1],Alpha(c,.25f),5);Line(p,points[i],points[i+1],c,3);Line(p,points[i],points[i+1],Pearl);}
            Line(p,points[3],points[3]+normal*8+delta.normalized*5,c);
        }
        static void Plate(CareerRaster p,Vector2 c,float size,Color32 color,float reveal=1)
        {Crest(p,c,size,reveal);}



        public static PixelCanvas Effect(CareerSkill s,int frame) => ReforgedEffect(s,frame);
        public static PixelCanvas Detail(Career career,bool charge,int frame) => ForgedDetail(career,charge,frame);
        public static PixelCanvas Impact(CareerSkill skill,int frame) => ForgedImpact(skill,frame);
        public static PixelCanvas State(Career career,string state,int frame)
        {
            var p=new CareerRaster();var c=new Vector2(48,48);float t=frame/11f;
            Color32 color=career==Career.Bishop?Gold:career==Career.Arcanist?Violet:career==Career.Fighter?Red:Cyan;
            if(state=="shield"){
                Ring(p,c,32,Alpha(color,.8f),1.1f,career==Career.Guardian?6:0);
                Arc(p,c,29,33,-1.3f,.65f,Pearl);Arc(p,c,29,33,1.9f,.65f,color);
                if(career==Career.Bishop){WingMark(p,c+new Vector2(0,-29),Gold);}
                else Diamond(p,c+new Vector2(0,-30),3,5,color);
            }else if(state=="guard"||state=="counter"){
                Ring(p,c+new Vector2(0,19),32,Azure,.35f,6);Plate(p,c+new Vector2(0,-29),14,Azure);
                if(state=="counter")for(int i=0;i<4;i++){float a=i*Tau/4;Line(p,c+Polar(a,27),c+Polar(a,20),Cyan);}
            }else if(state=="taunt"){
                // Small downward pennant over affected enemy, no stun stars or invented CC.
                Triangle(p,c+new Vector2(-8,-17),c+new Vector2(8,-17),c+new Vector2(0,-7),Cyan);p.VLine(48,17,25,Pearl);
            }else if(state=="hot"){
                Line(p,c+new Vector2(0,-31),c+new Vector2(0,-19),Leaf);Diamond(p,c+new Vector2(-4,-27),5,2,Leaf);Diamond(p,c+new Vector2(4,-23),5,2,Gold);
            }else if(state=="focus"){
                for(int i=0;i<4;i++){float a=i*Tau/4;Arc(p,c,28,28,a-.2f,.4f,Red);}Diamond(p,c+new Vector2(0,-29),2,6,Pearl);
            }else {Arc(p,c+new Vector2(0,-29),13,4,0,Tau,Gold);for(int i=0;i<3;i++)p.Set(43+i*5,25+I(Mathf.Sin(t*Tau+i)*2),Leaf);}
            return p;
        }
        static void WingMark(CareerRaster p,Vector2 at,Color32 color)
        {for(int i=0;i<3;i++){Line(p,at,at+new Vector2(-4-i*2,-3+i),color);Line(p,at,at+new Vector2(4+i*2,-3+i),color);}}
        public static PixelCanvas Boundary(int frame)
        {
            var p=new CareerRaster();var c=new Vector2(48,48);Ring(p,c,43,Pearl);
            // Interior ticks distinguish a gameplay boundary from scattered decoration.
            for(int i=0;i<16;i++){float a=i*Tau/16;Line(p,c+Polar(a,40),c+Polar(a,43),Pearl);}return p;
        }
        public static PixelCanvas Strike(int frame,bool final)
        {var p=new CareerRaster();float t=frame/11f;Blade(p,new Vector2(48,48),t,final);return Finish(p,t);}
        public static System.Collections.Generic.IEnumerable<string> VisualKeys
        {
            get {
                yield return "boundary";yield return "strike";yield return "finisher";
                yield return "f_cross_cut";yield return "f_flurry_cut";yield return "f_awake_cut";
                foreach(var skill in CareerCatalog.All)if(skill.kind!=CareerSkillKind.Passive)yield return skill.id+"_impact";
                foreach(string key in new[]{"Fighter_shield","Guardian_shield","Arcanist_shield","Bishop_shield","Guardian_guard","Guardian_counter","Guardian_taunt","Fighter_focus","Bishop_bless","Bishop_hot"})yield return key;
            }
        }
        public static PixelCanvas Visual(string key,int frame)
        {
            if(key=="boundary")return Boundary(frame);
            if(key.EndsWith("_cut"))return CutArt(key.Substring(0,key.Length-4),frame);
            if(key=="strike"||key=="finisher")return Strike(frame,key=="finisher");
            if(key.EndsWith("_impact"))return Impact(CareerCatalog.Get(key.Substring(0,key.Length-7)),frame);
            int split=key.IndexOf('_');var career=(Career)System.Enum.Parse(typeof(Career),key.Substring(0,split));
            return State(career,key.Substring(split+1),frame);
        }
    }
}
