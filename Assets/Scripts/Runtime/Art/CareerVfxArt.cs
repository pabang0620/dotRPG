using UnityEngine;

namespace DotRPG
{
    /// <summary>Hand-authored raster choreography. Integer pixels, deterministic particles and no gameplay RNG.</summary>
    public static class CareerVfxArt
    {
        static readonly Color32 Pearl=PixelCanvas.Hex("fff8e8"), Red=PixelCanvas.Hex("ee454c"), Amber=PixelCanvas.Hex("ffac48"),
            Azure=PixelCanvas.Hex("3faef4"), Cyan=PixelCanvas.Hex("88f5ed"), Violet=PixelCanvas.Hex("9b6aee"),
            Ice=PixelCanvas.Hex("b8efff"), Gold=PixelCanvas.Hex("efc766"), Leaf=PixelCanvas.Hex("b6ecab");
        const float Tau=Mathf.PI*2;
        static readonly Vector2Int[] Neighbors={Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
        static int I(float x)=>Mathf.RoundToInt(x);
        static Vector2 Polar(float a,float r)=>new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;
        static float Hash(int n){unchecked{uint x=(uint)(n*747796405+2891336453);x=((x>>((int)(x>>28)+4))^x)*277803737;x=(x>>22)^x;return (x&65535)/65535f;}}
        static Color32 Alpha(Color32 c,float a)=>PixelCanvas.WithAlpha(c,(byte)Mathf.Clamp(I(a*255),0,255));
        static void Line(PixelCanvas p,Vector2 a,Vector2 b,Color32 c,int w=1)
        {for(int k=-w/2;k<=w/2;k++){p.Line(I(a.x)+k,I(a.y),I(b.x)+k,I(b.y),c);if(w>2)p.Line(I(a.x),I(a.y)+k,I(b.x),I(b.y)+k,c);}}
        static void Diamond(PixelCanvas p,Vector2 at,float rx,float ry,Color32 c)
        {for(int y=-I(ry);y<=I(ry);y++){int w=I(rx*(1-Mathf.Abs(y)/Mathf.Max(1,ry)));p.HLine(I(at.x)-w,I(at.x)+w,I(at.y)+y,c);}}
        static void Glint(PixelCanvas p,Vector2 at,int r,Color32 c)
        {Diamond(p,at,r,1,c);Diamond(p,at,1,r,c);p.Set(I(at.x),I(at.y),Pearl);if(r>3){p.Set(I(at.x)+2,I(at.y)+2,c);p.Set(I(at.x)-2,I(at.y)-2,c);}}
        static void Arc(PixelCanvas p,Vector2 c,float rx,float ry,float angle,float span,Color32 color,int w=1)
        {Vector2 prev=c+new Vector2(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry);int steps=Mathf.Max(12,I(Mathf.Abs(span)*Mathf.Max(rx,ry)));for(int i=1;i<=steps;i++){float a=angle+span*i/steps;var at=c+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry);Line(p,prev,at,color,w);prev=at;}}
        static void Ring(PixelCanvas p,Vector2 c,float r,Color32 color,float squash=1,int sides=0,float phase=0)
        {if(sides==0){Arc(p,c,r,r*squash,0,Tau,color);return;}for(int i=0;i<sides;i++){var a=Polar(phase+i*Tau/sides,r);var b=Polar(phase+(i+1)*Tau/sides,r);a.y*=squash;b.y*=squash;Line(p,c+a,c+b,color);}}
        static void Rune(PixelCanvas p,Vector2 c,float r,float phase,Color32 color,float squash=.66f,int count=10)
        {
            Ring(p,c,r,color,squash);Ring(p,c,r-3,Alpha(color,.5f),squash);
            for(int i=0;i<count;i++){float a=i*Tau/count+phase;var v=Polar(a,r-7);v.y*=squash;var at=c+v;
                var outv=Polar(a,3);var side=Polar(a+Mathf.PI/2,2);
                Line(p,at-outv,at+outv,color);Line(p,at-outv,at-outv+side,color);if(i%2==0)Line(p,at,at-side,color);
            }
        }
        static void Crystal(PixelCanvas p,Vector2 c,float size,float angle,Color32 color)
        {
            Vector2 tip=c+Polar(angle,size),basePt=c-Polar(angle,size*.65f),left=c+Polar(angle+Mathf.PI/2,size*.33f),right=c+Polar(angle-Mathf.PI/2,size*.33f);
            Triangle(p,tip,left,basePt,PixelCanvas.Shade(color,.6f));Triangle(p,tip,right,basePt,color);
            Line(p,tip,basePt,Pearl);Line(p,tip,left,Alpha(Pearl,.8f));
        }
        static void Triangle(PixelCanvas p,Vector2 a,Vector2 b,Vector2 c,Color32 color)
        {
            float Cross(Vector2 x,Vector2 y)=>x.x*y.y-x.y*y.x;
            float sign=Mathf.Sign(Cross(b-a,c-a));if(sign==0)return;
            for(int y=I(Mathf.Min(a.y,Mathf.Min(b.y,c.y)));y<=I(Mathf.Max(a.y,Mathf.Max(b.y,c.y)));y++)
            for(int x=I(Mathf.Min(a.x,Mathf.Min(b.x,c.x)));x<=I(Mathf.Max(a.x,Mathf.Max(b.x,c.x)));x++)
            {var v=new Vector2(x,y);if(Cross(b-a,v-a)*sign>=0&&Cross(c-b,v-b)*sign>=0&&Cross(a-c,v-c)*sign>=0)p.Set(x,y,color);}
        }
        static void Flecks(PixelCanvas p,float t,int seed,Color32 c,int count,bool rise=false)
        {
            for(int k=0;k<count;k++){float age=Mathf.Repeat(t+Hash(k+seed)*.65f,1);float a=Hash(k*11+seed)*Tau;
                var at=new Vector2(48,49)+Polar(a,8+age*(26+Hash(k*3+seed)*10));
                if(rise)at.y-=age*9;else at.y+=age*age*5;
                Color32 col=k%3==0?Pearl:c;
                if(k%4==0&&age<.8f)Glint(p,at,age<.55f?3:1,Alpha(col,(1-age)*.9f));
                else {var trail=at-Polar(a,2+age*3);Line(p,trail,at,Alpha(c,.3f));p.Rect(I(at.x),I(at.y),age<.55f?2:1,1,col);}
            }
        }
        static void Slash(PixelCanvas p,Vector2 c,float angle,float r,float span,float t,Color32 c1)
        {
            // A tapered crescent: dark red envelope, orange hot edge, ivory cutting core.
            int steps=75;
            for(int k=0;k<steps;k++){float u=k/(float)(steps-1),a=angle-span*.5f+u*span;
                float taper=Mathf.Sin(u*Mathf.PI);float width=(4+6*(1-t))*taper;
                for(int w=I(width);w>=0;w--){var at=c+Polar(a,r-w);Color32 color=w>width*.68f?Alpha(c1,.52f):w>width*.25f?c1:Amber;if(w<=1&&u>.35f)color=Pearl;
                    p.Set(I(at.x),I(at.y),color);}
            }
            Arc(p,c,r+3,r+3,angle-span*.45f,span*.7f,Alpha(c1,.4f));
            Glint(p,c+Polar(angle+span*.37f,r-1),3,Amber);
        }
        static void Bolt(PixelCanvas p,Vector2 from,Vector2 to,int seed,Color32 c)
        {
            var delta=to-from;var normal=new Vector2(-delta.y,delta.x).normalized;
            var points=new Vector2[8];points[0]=from;points[7]=to;
            for(int i=1;i<7;i++)points[i]=Vector2.Lerp(from,to,i/7f)+normal*(Hash(seed+i*13)-.5f)*12;
            for(int i=0;i<7;i++){Line(p,points[i],points[i+1],Alpha(c,.25f),5);Line(p,points[i],points[i+1],c,3);Line(p,points[i],points[i+1],Pearl);}
            Line(p,points[3],points[3]+normal*8+delta.normalized*5,c);
        }
        static void Plate(PixelCanvas p,Vector2 c,float size,Color32 color,float reveal=1)
        {
            int h=I(size*reveal),w=I(size*.68f);
            for(int y=-h/2;y<=h/2;y++){float q=(y+h/2f)/Mathf.Max(1,h);int half=I(w*.5f*(q<.5f?1:Mathf.Lerp(1,.05f,(q-.5f)*2)));
                for(int x=-half;x<=half;x++){bool edge=Mathf.Abs(x)>=half-1||y<=-h/2+1;
                    p.Set(I(c.x)+x,I(c.y)+y,edge?Cyan:x<0?Alpha(color,.7f):Alpha(PixelCanvas.Shade(color,.35f),.75f));}
            }
            Line(p,c+new Vector2(0,-h*.37f),c+new Vector2(0,h*.3f),Pearl);
            Line(p,c+new Vector2(-w*.32f,-h*.2f),c+new Vector2(w*.32f,-h*.2f),Gold);
            Diamond(p,c,3,5,Pearl);Glint(p,c+new Vector2(-w*.4f,-h*.4f),3,Pearl);
        }
        static void Wings(PixelCanvas p,Vector2 root,float spread,Color32 color,float t)
        {
            for(int side=-1;side<=1;side+=2)
            for(int k=7;k>=0;k--){float x=(9+k*3.5f)*spread,y=-8-Mathf.Sin(k*.25f)*17+t*7;
                Vector2 a=root+new Vector2(side*(4+k*1.2f),k*.5f),b=root+new Vector2(side*x,y),d=root+new Vector2(side*(x-4),y+16-k*.65f);
                Triangle(p,a,b,d,PixelCanvas.Shade(color,.53f));Triangle(p,a,b,d+new Vector2(-side*2,-2),color);
                Line(p,a,b,Pearl);p.Set(I(b.x)+side,I(b.y),Gold);
            }
        }
        static void Flame(PixelCanvas p,Vector2 c,float h,float lean,Color32 color)
        {
            for(int y=0;y<I(h);y++){float u=y/h;float x=c.x+Mathf.Sin(u*5)*2+lean*u;int width=I((1-u)*h*.25f);
                p.HLine(I(x)-width,I(x)+width,I(c.y)-y,Alpha(Red,.8f));
                if(width>1)p.HLine(I(x)-width+1,I(x)+width-1,I(c.y)-y,color);
                if(u<.65f&&width>2)p.HLine(I(x)-width/2,I(x)+width/2,I(c.y)-y,Amber);
                if(u<.3f)p.Set(I(x),I(c.y)-y,Pearl);
            }
        }

        public static PixelCanvas Effect(CareerSkill s,int frame)
        {
            var p=new PixelCanvas(96,96);float t=Mathf.Clamp01(frame/11f);var c=new Vector2(48,48);
            switch(s.career){case Career.Fighter:Fighter(p,s.effect,c,t);break;case Career.Guardian:Guardian(p,s.effect,c,t);break;case Career.Arcanist:Mage(p,s.effect,c,t);break;case Career.Bishop:Bishop(p,s.effect,c,t);break;}
            // Sparse, hard-edged halo. No blur/bilinear texture, no opaque full-screen plate.
            var copy=(Color32[])p.Pixels.Clone();
            for(int y=2;y<94;y++)for(int x=2;x<94;x++)if(copy[y*96+x].a==0)
            {foreach(var d in Neighbors){var color=copy[(y+d.y)*96+x+d.x];if(color.a>180){p.Set(x,y,Alpha(color,.13f));break;}}}
            // Reserve two pixels on every side for rotated sprites and tightly packed atlas frames.
            for(int y=0;y<96;y++)for(int x=0;x<96;x++)if(x<2||x>93||y<2||y>93)p.Pixels[y*96+x]=PixelCanvas.Clear;
            return p;
        }
        public static PixelCanvas Detail(Career career,bool charge,int frame)
        {
            var p=new PixelCanvas(96,96);var c=new Vector2(48,48);float t=frame/11f;
            Color32 color=career==Career.Fighter?Red:career==Career.Guardian?Cyan:career==Career.Arcanist?Violet:Gold;
            if(charge){
                Rune(p,c+new Vector2(0,7),35-t*14,-t,color,career==Career.Fighter?1:.55f,career==Career.Guardian?6:10);
                for(int i=0;i<12;i++){float a=i*Tau/12+t*.4f;var at=c+Polar(a,38-t*28);Line(p,at,at+Polar(a,5),Alpha(color,.5f));if(i%3==0)Glint(p,at,2,Pearl);}
                Glint(p,c,2+I(t*6),color);
            }else{
                Flecks(p,t,303,color,15,career==Career.Bishop);
                if(career==Career.Fighter){Slash(p,c,-.8f,12+t*20,2.8f,t,Red);Glint(p,c,9-I(t*7),Amber);}
                if(career==Career.Guardian){Ring(p,c,10+t*28,Cyan,1,6);for(int i=0;i<6;i++)Crystal(p,c+Polar(i*Tau/6,8+t*22),4,i*Tau/6,Azure);}
                if(career==Career.Arcanist){for(int i=0;i<4;i++)Bolt(p,c,c+Polar(i*Tau/4+t,12+t*18),i+9,Violet);Glint(p,c,8-I(t*5),Ice);}
                if(career==Career.Bishop){Arc(p,c,8+t*28,5+t*12,0,Tau,Gold);Glint(p,c,9-I(t*6),Leaf);}
            }
            return p;
        }
        static void Fighter(PixelCanvas p,string id,Vector2 c,float t)
        {
            float grow=1-Mathf.Pow(1-t,3),r=19+23*grow;
            Flecks(p,t,11,Amber,id=="five"?32:17);
            if(id=="focus"){
                Rune(p,c,34-12*t,t*2,Red,.6f,8);Ring(p,c,18-10*t,Amber);
                for(int k=0;k<8;k++){float a=k*Tau/8+t;Line(p,c+Polar(a,38-15*t),c+Polar(a,22-16*t),k%2==0?Pearl:Red);}
                Diamond(p,c,3+5*t,9+8*t,Red);Glint(p,c,7,Amber);return;
            }
            if(id=="rush"){
                for(int k=0;k<8;k++){float yy=26+k*6,xx=20+grow*32+(k%2)*5;Line(p,new Vector2(7,yy),new Vector2(xx,yy),Alpha(Red,.4f));Line(p,new Vector2(xx-18,yy),new Vector2(xx,yy),Amber);}
                Triangle(p,new Vector2(18,42),new Vector2(87,48),new Vector2(18,54),Red);Triangle(p,new Vector2(30,45),new Vector2(87,48),new Vector2(30,50),Pearl);
                Arc(p,new Vector2(49+grow*12,48),15,27,-1.3f,2.6f,Amber,3);return;
            }
            if(id=="break"){
                for(int k=0;k<7;k++){float a=k*Tau/7;var at=c+Polar(a,12+t*25);Crystal(p,at,4+3*(1-t),a,PixelCanvas.Hex("8facbb"));}
                Slash(p,c,-.8f+t*.8f,r,3.3f,t,Red);Bolt(p,c+new Vector2(-15,22),c+new Vector2(15,-22),31,Amber);return;
            }
            if(id=="execute"){
                for(int k=0;k<3;k++){float a=-.7f+k*.18f;var v=Polar(a,37);Line(p,c-v,c+v,Alpha(Red,.5f),7-k*2);Line(p,c-v*.8f,c+v,Pearl);}
                Glint(p,c+new Vector2(9,-7),9,Amber);Arc(p,c,r,r,.2f,2.8f,Red,2);return;
            }
            int blades=id=="five"?5:id=="flurry"?3:2;
            for(int k=0;k<blades;k++){
                float lag=k*.13f,phase=Mathf.Clamp01((t-lag)/.6f);if(t<lag)continue;
                float angle=-1.8f+phase*3.4f+(k%2)*1.7f;
                Slash(p,c+new Vector2(-k%2*3,k%3*2),angle,r-k*4,2.8f,phase,k%2==0?Red:PixelCanvas.Hex("ff7950"));
            }
            if(id=="five"){
                Rune(p,c,36,t,Alpha(Red,.6f),1,5);
                if(t>.55f){float q=(t-.55f)/.45f;Line(p,c+new Vector2(-28,28),c+new Vector2(30,-30),Red,7);Line(p,c+new Vector2(-26,26),c+new Vector2(30,-30),Pearl,3);Ring(p,c,8+q*32,Amber);Glint(p,c,9,Amber);}
            }
        }
        static void Guardian(PixelCanvas p,string id,Vector2 c,float t)
        {
            float rise=Mathf.Min(1,t*3+.35f),r=18+24*(1-Mathf.Pow(1-t,3));
            Rune(p,c+new Vector2(0,15),r,t*.2f,Azure,.5f,6);Flecks(p,t,61,Cyan,id=="citadel"?30:14);
            if(id=="taunt"){
                for(int k=0;k<3;k++){float rad=10+Mathf.Repeat(t+k*.28f,1)*32;Ring(p,c,rad,k==0?Pearl:Azure,.8f,6);}
                Plate(p,c,25,Azure,rise);for(int k=0;k<6;k++){var a=Polar(k*Tau/6,32);Line(p,c+a,c+a*.8f,Cyan,3);}return;
            }
            if(id=="bash"){
                var at=c+new Vector2((t-.3f)*23,0);for(int k=0;k<5;k++){float y=31+k*8;Line(p,new Vector2(9,y),new Vector2(at.x-12,y),Alpha(Azure,.5f),3);}
                Plate(p,at,49,Azure);Arc(p,at,24,30,-1.4f,2.8f,Cyan,3);Glint(p,at+new Vector2(25,0),7,Pearl);return;
            }
            if(id=="counter"){
                Plate(p,c,30,Azure,rise);for(int k=0;k<8;k++){float a=k*Tau/8;Crystal(p,c+Polar(a,r-3),7,a,Azure);Line(p,c+Polar(a,13),c+Polar(a,r),Cyan);}Glint(p,c,6,Pearl);return;
            }
            if(id=="guard"){
                Ring(p,c,35,Azure,1,6);Ring(p,c,39,Alpha(Cyan,.5f),1,6);Plate(p,c,48,Azure,rise);
                for(int k=0;k<6;k++)Glint(p,c+Polar(k*Tau/6,36),3,Cyan);return;
            }
            if(id=="shield"){
                Plate(p,c+new Vector2(-18,0),32,Azure,rise);Plate(p,c+new Vector2(18,0),32,Azure,rise);
                Arc(p,c,39,29,Mathf.PI,Mathf.PI,Cyan,2);Line(p,c+new Vector2(-17,10),c+new Vector2(17,10),Gold);return;
            }
            // Faceted wall segments rise in perspective; awakening adds a central heraldic shield.
            for(int k=0;k<7;k++){float a=-Mathf.PI+k*Mathf.PI/6;var pos=c+new Vector2(Mathf.Cos(a)*32,Mathf.Sin(a)*14+12);
                int h=I((id=="citadel"?33:23)*rise);p.Rect(I(pos.x)-4,I(pos.y)-h,8,h,Alpha(PixelCanvas.Hex("28578d"),.7f));
                p.VLine(I(pos.x)-4,I(pos.y)-h,I(pos.y),Cyan);p.HLine(I(pos.x)-4,I(pos.x)+4,I(pos.y)-h,Pearl);
                p.Rect(I(pos.x)-5,I(pos.y)-h-3,3,5,Azure);p.Rect(I(pos.x)+2,I(pos.y)-h-3,3,5,Azure);}
            Ring(p,c,39,Cyan,.88f,6);Ring(p,c,35,Alpha(Azure,.65f),.88f,6);
            Plate(p,c+new Vector2(0,-5),id=="citadel"?42:27,Azure,rise);
            if(id=="citadel"){Wings(p,c+new Vector2(0,-2),.95f,Cyan,t*.4f);Plate(p,c+new Vector2(0,-4),32,Azure);Glint(p,c+new Vector2(0,-28),6,Pearl);}
        }
        static void Mage(PixelCanvas p,string id,Vector2 c,float t)
        {
            float r=18+24*(1-Mathf.Pow(1-t,3));Color32 color=id=="fire"?Amber:id=="ice"?Ice:id=="storm"?Gold:Violet;
            Rune(p,c+new Vector2(0,12),r,-t*.7f,Violet,.65f,12);Flecks(p,t,131,color,id=="eclipse"?36:18,true);
            if(id=="fire"){
                Arc(p,c,30,19,t*2,5.3f,Red,3);
                for(int k=0;k<9;k++){float a=k*Tau/9+t*.8f;var at=c+new Vector2(Mathf.Cos(a)*(12+t*15),Mathf.Sin(a)*(8+t*9)+12);Flame(p,at,12+Hash(k+50)*15*(1-t*.5f),Mathf.Cos(a)*8,Amber);}
                Flame(p,c+new Vector2(0,14),33*(1-t*.4f),Mathf.Sin(t*7)*5,Amber);return;
            }
            if(id=="ice"){
                for(int k=0;k<8;k++){float a=k*Tau/8+t*.18f;var at=c+Polar(a,r*.62f);Crystal(p,at,10+4*Mathf.Sin(t*Mathf.PI),a,Ice);}
                Crystal(p,c,25*(.5f+Mathf.Min(t*3,.5f)),-Mathf.PI/2,Ice);Ring(p,c,r,Ice,.75f,6);return;
            }
            if(id=="storm"){
                Bolt(p,new Vector2(53,7),c+new Vector2(0,13),I(t*5)+31,Gold);
                for(int k=0;k<4;k++){float a=k*Tau/4+t*.5f;Bolt(p,c,c+Polar(a,r-2),k*31+I(t*6),Violet);}
                Glint(p,c,9,Gold);Ring(p,c+new Vector2(0,12),r,Azure,.4f);return;
            }
            if(id=="orbit"){
                for(int k=0;k<3;k++){float a=t*4+k*Tau/3;Arc(p,c,30,20,a-1.2f,1.2f,Violet,3);var at=c+new Vector2(Mathf.Cos(a)*30,Mathf.Sin(a)*20);p.Circle(at.x,at.y,5,Violet);p.Circle(at.x-1,at.y-1,3,Ice);Glint(p,at,4,Pearl);}
                Crystal(p,c,13+t*5,-Mathf.PI/2,Violet);Ring(p,c,18,Alpha(Ice,.6f));return;
            }
            if(id=="blink"){
                for(int k=0;k<3;k++){var at=c+new Vector2((k-1)*18,0);Arc(p,at,8,27,0,Tau,Alpha(Violet,.35f+k*.23f),2);Crystal(p,at,16,-Mathf.PI/2,Alpha(Ice,.25f+k*.25f));}
                for(int k=0;k<6;k++)Line(p,new Vector2(13,24+k*8),new Vector2(77,24+k*8),Alpha(Violet,.3f));return;
            }
            if(id=="rift"){
                Vector2 a=c+new Vector2(5,-31),b=c+new Vector2(-6,30);Line(p,a,b,Alpha(Violet,.3f),13);Bolt(p,a,b,15,Violet);
                for(int k=0;k<8;k++){float angle=k*Tau/8;Crystal(p,c+Polar(angle,r*.7f),5+4*Hash(k),angle+t,Ice);}
                Arc(p,c,r*.7f,r,t,4.8f,Violet,2);return;
            }
            // The eclipse combines fire, frost and lightning at once rather than recoloring one circle.
            Ring(p,c,30,Ice);Ring(p,c,26,Violet);Arc(p,c,25,25,t*3,4.8f,Pearl,2);
            p.Circle(c.x,c.y,18,Alpha(PixelCanvas.Hex("281944"),.8f));Arc(p,c+new Vector2(2,-2),17,17,-.4f,3.5f,Violet,3);
            for(int k=0;k<3;k++){float a=-Mathf.PI/2+k*Tau/3+t*.5f;var at=c+Polar(a,33);
                if(k==0)Flame(p,at+new Vector2(0,5),18,4,Amber);
                if(k==1)Crystal(p,at,11,a,Ice);
                if(k==2)Bolt(p,at-Polar(a,8),at+Polar(a,8),2+I(t*4),Gold);
                Arc(p,c,35,35,a+.25f,1.3f,k==0?Amber:k==1?Ice:Gold,2);
            }
            Glint(p,c,5+I(Mathf.Sin(t*Mathf.PI)*5),Pearl);
        }
        static void Bishop(PixelCanvas p,string id,Vector2 c,float t)
        {
            float spread=.6f+Mathf.Min(t*2,.4f),r=16+26*(1-Mathf.Pow(1-t,3));
            Rune(p,c+new Vector2(0,16),r,t*.3f,Gold,.43f,12);Flecks(p,t,227,id=="hot"?Leaf:Gold,id=="dawn"?30:17,true);
            if(id=="light"){
                Vector2 a=c+new Vector2(-28,28),b=c+new Vector2(29,-29);Line(p,a,b,Alpha(Gold,.3f),9);Line(p,a,b,Gold,5);Line(p,a,b,Pearl,2);
                Line(p,b,b+new Vector2(-17,4),Pearl,2);Line(p,b,b+new Vector2(-4,17),Pearl,2);
                for(int k=0;k<3;k++)Glint(p,Vector2.Lerp(a,b,k*.3f+t*.1f),3,Gold);return;
            }
            if(id=="hot"){
                for(int k=0;k<6;k++){float a=k*Tau/6+t*2;var at=c+new Vector2(Mathf.Cos(a)*19,Mathf.Sin(a)*10-t*14);Crystal(p,at,7,a,Leaf);}
                Line(p,c+new Vector2(0,19),c+new Vector2(0,-16),Leaf,2);Arc(p,c+new Vector2(0,-14),11,4,0,Tau,Gold);Glint(p,c+new Vector2(0,-12),6,Pearl);return;
            }
            if(id=="cleanse"){
                var bell=c+new Vector2(0,-7-t*6);p.Circle(bell.x,bell.y-5,9,Gold);p.Rect(I(bell.x)-8,I(bell.y)-5,16,15,Gold);
                p.Rect(I(bell.x)-5,I(bell.y)-7,3,14,Pearl);p.HLine(I(bell.x)-12,I(bell.x)+12,I(bell.y)+10,Pearl);p.Circle(bell.x,bell.y+13,3,Gold);
                for(int k=0;k<3;k++)Ring(p,c,12+Mathf.Repeat(t+k*.3f,1)*28,k==0?Leaf:Gold,.65f);return;
            }
            if(id=="heal"){
                Wings(p,c+new Vector2(0,6),spread*.8f,Leaf,t);
                p.Rect(44,27-I(t*5),8,33,Pearl);p.Rect(34,37-I(t*5),28,7,Pearl);Arc(p,c+new Vector2(0,-23),14,5,0,Tau,Gold,2);return;
            }
            if(id=="bless"){
                for(int k=0;k<7;k++){float x=19+k*10;int y=I(12+t*23+(k%3)*4);p.VLine(I(x),y,y+17,Alpha(Gold,.3f));p.VLine(I(x),y+8,y+18,Pearl);Glint(p,new Vector2(x,y+20),3,Gold);}
                Arc(p,c+new Vector2(0,-24),26,7,0,Tau,Gold,2);Arc(p,c+new Vector2(0,-24),21,5,0,Tau,Pearl);return;
            }
            Wings(p,c+new Vector2(0,3),spread,id=="dawn"?Pearl:Gold,t*.5f);
            if(id=="wings"){Plate(p,c+new Vector2(0,9),26,Gold);Arc(p,c+new Vector2(0,-25),14,4,0,Tau,Gold,2);}
            else {
                for(int k=0;k<5;k++){float x=24+k*12;p.VLine(I(x),I(14+t*10),66,Alpha(Gold,.25f));Glint(p,new Vector2(x,23+t*26),3,Pearl);}
                Arc(p,c+new Vector2(0,-27),22,5,0,Tau,Gold,2);Arc(p,c+new Vector2(0,17),36,14,0,Tau,Leaf,2);
                Diamond(p,c,5,15,Pearl);Glint(p,c+new Vector2(0,-6),8,Gold);
            }
        }
    }
}
