using UnityEngine;

namespace DotRPG
{
    // Original, integer-raster art. References inform silhouette and choreography only.
    // All primary shapes fit a 43px disc, matching CareerEffect's world-radius conversion.
    public static partial class CareerVfxArt
    {
        static readonly Color32 Wine=PixelCanvas.Hex("60213d"), SteelDark=PixelCanvas.Hex("243958"),
            SteelMid=PixelCanvas.Hex("486989"), Silver=PixelCanvas.Hex("c1dce6"),
            ArcaneDark=PixelCanvas.Hex("352748"), ArcaneMid=PixelCanvas.Hex("65549c"),
            Ochre=PixelCanvas.Hex("927048"), Cream=PixelCanvas.Hex("f4e7b7"), GreenDark=PixelCanvas.Hex("51877b");
        static float Out(float t)=>1-Mathf.Pow(1-Mathf.Clamp01(t),3);
        static Vector2 V(float x,float y)=>new Vector2(x,y);
        static void Poly(CareerRaster p,Color32 col,params Vector2[] points)
        {
            // Scan conversion also supports concave flame tips and the irregular rift contour.
            float min=96,max=0;foreach(var v in points){min=Mathf.Min(min,v.y);max=Mathf.Max(max,v.y);}
            var crossings=new float[points.Length];
            for(int y=I(min*2);y<=I(max*2);y++){
                int n=0;float scan=(y+.5f)/2;
                for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];
                    if((a.y<=scan&&b.y>scan)||(b.y<=scan&&a.y>scan))crossings[n++]=a.x+(scan-a.y)*(b.x-a.x)/(b.y-a.y);}
                System.Array.Sort(crossings,0,n);
                for(int i=0;i+1<n;i+=2){int x0=Mathf.CeilToInt(crossings[i]*2),x1=Mathf.FloorToInt(crossings[i+1]*2);if(x0<=x1)p.Raw.HLine(x0,x1,y,col);}
            }
        }
        public static PixelCanvas Plane(PixelCanvas art,bool back)
        {
            var p=new PixelCanvas(CareerRaster.Size,CareerRaster.Size);
            for(int y=0;y<CareerRaster.Size;y++)if((y<CareerRaster.Size/2)==back)
                System.Array.Copy(art.Pixels,y*CareerRaster.Size,p.Pixels,y*CareerRaster.Size,CareerRaster.Size);
            return p;
        }
        public static PixelCanvas CutArt(string id,int frame)
        {
            var p=new CareerRaster();float t=frame/11f;var c=V(48,48);
            Blade(p,c,t,false,id=="f_flurry"?-.35f:0);
            if(id=="f_flurry"){
                // Parallel wake ribs identify the faster triple strike without inventing extra hits.
                for(int k=0;k<2;k++)Arc(p,c,23-k*5,23-k*5,-1.8f+Out(t*3)*1.8f,1.4f,k==0?Red:Wine,2);
            }
            if(id=="f_awake")Facet(p,c+V(-18,4),c+V(27,-8),3,Wine,Red,Silver);
            return Finish(p,t);
        }
        static void Facet(CareerRaster p,Vector2 a,Vector2 b,float width,Color32 dark,Color32 mid,Color32 light)
        {
            var d=(b-a).normalized;var n=V(-d.y,d.x)*width;var shoulder=Vector2.Lerp(a,b,.43f);
            Poly(p,dark,a-n*.25f,shoulder-n,b,shoulder+n,a+n*.25f);
            Poly(p,mid,a,shoulder-n*.7f,b,shoulder+n*.38f);
            Line(p,a,b,light);
            Line(p,shoulder-n*.72f,Vector2.Lerp(shoulder,b,.72f),light,.5f);
        }
        static void Shards(CareerRaster p,Vector2 c,float t,int count,Color32 dark,Color32 color,int seed)
        {
            for(int i=0;i<count;i++){
                float a=Hash(seed+i*19)*Tau,r=8+Out(t)*(17+Hash(seed+i)*12);
                var at=c+Polar(a,r);float len=(2+Hash(i+seed)*4)*(1-t*.7f);
                Facet(p,at-Polar(a,len),at+Polar(a,len),1.5f,dark,color,Pearl);
            }
        }
        // A broad blade-shaped smear with five discrete colour bands and torn trailing ribs.
        static void Blade(CareerRaster p,Vector2 c,float t,bool finish=false,float offset=0)
        {
            float sweep=Out(Mathf.Min(1,t*2.8f)),angle=-1.25f+sweep*2.25f+offset;
            float length=2.3f*(1-Mathf.Max(0,t-.35f)*1.1f),radius=35+3*sweep;
            for(int i=0;i<280;i++){
                float u=i/279f,a=angle-length*(1-u),r=radius-(1-u)*5;
                float w=Mathf.Sin(u*Mathf.PI)*((finish?14:10)*(1-t*.55f));
                for(int j=0;j<=I(w*2);j++){
                    float q=(j/2f)/Mathf.Max(.5f,w);var at=c+Polar(a,r-j/2f);
                    var col=q<.13f?Silver:q<.32f?Pearl:q<.52f?Amber:q<.82f?Red:Wine;
                    if(t>.6f&&((i/18+j/6)%5==0))continue;
                    p.Set(at.x,at.y,col);
                }
                if(i%38==0&&u<.65f)Facet(p,c+Polar(a-.1f,r-w-5),c+Polar(a+.12f,r-w+1),2,Wine,Red,Amber);
            }
            for(int i=0;i<3;i++)Arc(p,c,radius-7-i*5,radius-7-i*5,angle-length-.2f+i*.1f,length*.62f, i==0?Wine:Red);
            if(t<.38f)Facet(p,c+Polar(angle,19),c+Polar(angle,40),4,Wine,Amber,Pearl);
            if(finish){float a=-.6f;Facet(p,c-Polar(a,29),c+Polar(a,34),6,SteelDark,Silver,Pearl);}
            if(t>.25f)Shards(p,c,t,5,Wine,Red,21);
        }
        // Bevelled kite shield; the split face and cut-in emblem remain readable without glow.
        static void Crest(CareerRaster p,Vector2 c,float size,float unfold=1)
        {
            float sx=size*.36f*Mathf.Max(.12f,unfold),h=size*.5f;
            Poly(p,SteelDark,c+V(-sx,-h*.7f),c+V(0,-h),c+V(sx,-h*.7f),c+V(sx*.86f,h*.34f),c+V(0,h),c+V(-sx*.86f,h*.34f));
            Poly(p,Silver,c+V(-sx+2,-h*.65f),c+V(0,-h+2),c+V(sx-2,-h*.65f),c+V(sx*.76f,h*.28f),c+V(0,h-3),c+V(-sx*.76f,h*.28f));
            Poly(p,SteelMid,c+V(-sx+4,-h*.57f),c+V(0,-h+5),c+V(sx-4,-h*.57f),c+V(sx*.62f,h*.2f),c+V(0,h-7),c+V(-sx*.62f,h*.2f));
            Poly(p,SteelDark,c,c+V(sx-4,-h*.57f),c+V(sx*.62f,h*.2f),c+V(0,h-7));
            Facet(p,c+V(0,-h*.55f),c+V(0,h*.42f),size*.1f,Azure,Cyan,Pearl);
            for(int side=-1;side<=1;side+=2){Line(p,c+V(side*sx*.54f,-h*.25f),c+V(0,2),Cyan,2);p.Rect(I(c.x+side*sx*.65f)-1,I(c.y-h*.46f),2,2,Pearl);}
            // Native one-pixel engraving, bevel seams and rivets in the new density.
            for(int side=-1;side<=1;side+=2){
                Line(p,c+V(side*(sx-2),-h*.62f),c+V(side*sx*.74f,h*.24f),Pearl,.5f);
                Line(p,c+V(side*sx*.72f,h*.27f),c+V(0,h-4),Azure,.5f);
                for(int k=0;k<3;k++){
                    var at=c+V(side*(sx*.48f-k*.65f),h*(.03f+k*.16f));
                    Line(p,at,at+V(-side*2.4f,1.2f),Silver,.5f);
                }
                p.Circle(c.x+side*sx*.65f,c.y-h*.48f,1.2f,SteelDark);
                p.Circle(c.x+side*sx*.65f-.3f,c.y-h*.48f-.3f,.65f,Pearl);
            }
        }
        static void Dome(CareerRaster p,Vector2 c,float t,bool grand)
        {
            float q=Out(t*3),rx=32,ry=26*q;
            // Arched ribs and separated facets leave the character and danger zones visible.
            Arc(p,c,rx,ry,Mathf.PI,Mathf.PI,SteelDark,3);
            Arc(p,c+V(0,-1),rx,ry,Mathf.PI,Mathf.PI,Cyan);
            Arc(p,c,rx,10,0,Mathf.PI,Azure,2);
            for(int k=0;k<5;k++){
                float x=-24+k*12,y=-Mathf.Sqrt(Mathf.Max(0,1-x*x/(rx*rx)))*ry;
                Vector2 a=c+V(x,y),b=c+V(x*.9f,4);
                Line(p,a,b,Alpha(Azure,.42f));
                Facet(p,a+V(-3,4),a+V(2,0),2,SteelMid,Silver,Pearl);
                if(grand&&k%2==0)Crest(p,c+V(x*.93f,-6),14,q);
            }
            for(int side=-1;side<=1;side+=2){var b=c+V(side*30,3);Facet(p,b+V(0,3),b+V(0,-17*q),4,SteelDark,Azure,Silver);}
        }
        static void Sigil(CareerRaster p,Vector2 c,float radius,float phase,Color32 color,bool grand=false)
        {
            // Broken bands / rune tablets, not identical nested circles on every skill.
            for(int k=0;k<6;k++){
                float a=k*Tau/6+phase;Arc(p,c,radius,radius*.46f,a,.78f,ArcaneDark,3);Arc(p,c,radius,radius*.46f,a,.78f,color);
                var at=c+V(Mathf.Cos(a+.3f)*(radius-5),Mathf.Sin(a+.3f)*(radius-5)*.46f);
                Line(p,at+V(-2,-2),at+V(2,2),Cream);Line(p,at+V(2,-2),at+V(2,2),color);
                Line(p,at+V(-2.5f,1.5f),at+V(-.5f,-1.5f),color,.5f);
                p.Set(at.x+3.5f,at.y-1.5f,Cream);
                if(grand){var v=c+V(Mathf.Cos(a)*radius*.7f,Mathf.Sin(a)*radius*.32f);Diamond(p,v,2,3,color);}
            }
        }
        static void Plume(CareerRaster p,Vector2 basePt,float height,float bend,int seed)
        {
            for(int layer=0;layer<4;layer++){
                float h=height*(1-layer*.18f),w=height*(.25f-layer*.045f);
                Vector2 tip=basePt+V(bend,-h),elbow=basePt+V(-bend*.22f,-h*.47f);
                Poly(p,layer==0?Wine:layer==1?Red:layer==2?Amber:Pearl,
                    basePt+V(-w,0),basePt+V(-w*.9f,-h*.3f),elbow+V(-w*.4f,0),tip,
                    basePt+V(bend*.5f+w*.38f,-h*.6f),basePt+V(w,-h*.23f),basePt+V(w*.7f,0));
            }
            // Broken hot ridges follow the flame, rather than adding unrelated particles.
            for(int k=0;k<3;k++){
                float u=.22f+k*.17f;var at=basePt+V(bend*u-Mathf.Sin(u*5)*height*.08f,-height*u);
                Line(p,at,at+V(1.2f,-height*.1f),k==0?Pearl:Amber,.5f);
            }
        }
        static void Feather(CareerRaster p,Vector2 a,Vector2 tip,float width,Color32 tint)
        {
            var d=(tip-a).normalized;var n=V(-d.y,d.x);var shoulder=Vector2.Lerp(a,tip,.4f);
            Poly(p,Ochre,a-n*width*.2f,shoulder-n*width,Vector2.Lerp(a,tip,.75f)-n*width*.6f,tip,shoulder+n*width*.7f,a+n*width*.3f);
            Poly(p,tint,a,shoulder-n*(width-1),tip,shoulder+n*width*.4f);
            Line(p,a,tip,Pearl);
            for(int k=1;k<=6;k++){
                float u=k*.095f+.15f;var mid=Vector2.Lerp(a,tip,u);
                float barb=width*(1-Mathf.Max(0,u-.4f)*1.3f);
                Line(p,mid,mid-n*barb*.75f+d*1.5f,Cream,.5f);
                Line(p,mid+d*.5f,mid+n*barb*.36f+d*1.7f,Gold,.5f);
            }
        }
        static void Pinions(CareerRaster p,Vector2 root,float open,float fade,bool grand)
        {
            for(int side=-1;side<=1;side+=2){
                var elbow=root+V(side*(10+12*open),-12-7*open);
                for(int k=0;k<6;k++){
                    var a=Vector2.Lerp(root,elbow,k*.11f);
                    var tip=root+V(side*(13+open*(19-k*2.8f)),-31+k*7+fade*5);
                    Feather(p,a,tip,grand?4.5f:3.5f,k%2==0?Cream:Gold);
                }
                Feather(p,root+V(side*3,-1),elbow+V(side*3,-4),5,Cream);
                for(int k=0;k<4;k++)Diamond(p,root+V(side*(8+k*3),-7-k*2),2,3,Gold);
            }
        }
        static void Petals(CareerRaster p,Vector2 c,float t,bool gather)
        {
            float r=gather?26*(1-Out(t*.9f))+4:9+Out(t)*19;
            for(int i=0;i<6;i++){float a=i*Tau/6+t*.5f;var at=c+V(Mathf.Cos(a)*r,Mathf.Sin(a)*r*.55f-t*7);
                Facet(p,at-Polar(a,3),at+Polar(a,4),2.5f,GreenDark,Leaf,Cream);}
        }
        static PixelCanvas ReforgedEffect(CareerSkill s,int frame)
        {
            var p=new CareerRaster();float t=frame/11f;var c=V(48,48);
            if(s.career==Career.Fighter)ForgedFighter(p,s.effect,c,t);
            else if(s.career==Career.Guardian)ForgedGuardian(p,s.effect,c,t);
            else if(s.career==Career.Arcanist)ForgedMage(p,s.effect,c,t);
            else ForgedBishop(p,s.effect,c,t);
            return Finish(p,t);
        }
        static PixelCanvas Finish(CareerRaster p,float t)
        {
            // Retire edges in small pixel clusters, not a uniform scale-down of the entire symbol.
            if(t>.7f)for(int y=0;y<CareerRaster.Size;y++)for(int x=0;x<CareerRaster.Size;x++){
                int i=y*CareerRaster.Size+x;var col=p.Pixels[i];if(col.a==0)continue;
                if(Hash((x/3)*179+(y/3)*37)<(t-.7f)*2.1f)p.Pixels[i]=PixelCanvas.Clear;
            }
            return p;
        }
        static void ForgedFighter(CareerRaster p,string id,Vector2 c,float t)
        {
            if(id=="focus"){
                for(int k=0;k<5;k++){float a=k*Tau/5-.3f;float r=29-Out(t)*15;
                    Facet(p,c+Polar(a,r+6),c+Polar(a,r-8),3,Wine,Red,Amber);}
                Diamond(p,c,4,10,Red);Diamond(p,c,2,7,Pearl);return;
            }
            if(id=="rush"||id=="execute"){
                float reach=Out(t*4);var tip=c+V(28+10*reach,0);
                Facet(p,c+V(-26,0),tip,id=="execute"?7:4,Wine,Red,Pearl);
                for(int side=-1;side<=1;side+=2){
                    Poly(p,Amber,c+V(-14,side*3),tip,c+V(-4,side*7));
                    Facet(p,c+V(-27,side*15),c+V(21,side*7),2,Wine,Red,Amber);
                    Line(p,c+V(-33,side*10),c+V(-10,side*5),Silver);
                }Facet(p,c+V(-21,0),tip,1,Silver,Pearl,Pearl);return;
            }
            Blade(p,c,t,id=="five");
            if(id=="break"){
                for(int k=0;k<5;k++){float a=k*Tau/5+t*.25f;var at=c+Polar(a,9+Out(t)*19);
                    var d=Polar(a,4);Poly(p,SteelDark,at-d,at+V(3,-3),at+d,at+V(-2,4));Line(p,at-d,at+V(3,-3),Silver);}
                Facet(p,c+V(-20,20),c+V(23,-23),4,Wine,Red,Pearl);
            }
        }
        static void ForgedGuardian(CareerRaster p,string id,Vector2 c,float t)
        {
            float open=Out(t*4+.3f);
            if(id=="bash"){
                for(int i=0;i<4;i++)Facet(p,c+V(-32,-15+i*10),c+V(-7,-10+i*7),2,SteelDark,Azure,Silver);
                Crest(p,c+V(8,0),43,open);Arc(p,c+V(8,0),23,28,-1.1f,2.2f,Cyan,2);return;
            }
            if(id=="counter"){
                for(int k=0;k<7;k++){float a=k*Tau/7;Facet(p,c+Polar(a,9+Out(t)*10),c+Polar(a,24+Out(t)*15),4,SteelDark,Azure,Silver);}
                Crest(p,c,27,1);return;
            }
            if(id=="taunt"){
                for(int k=0;k<3;k++){float r=12+Out(Mathf.Clamp01(t*1.5f-k*.14f))*27;Arc(p,c+V(0,5),r,r*.55f,0,Tau,k==0?Cyan:SteelMid,2);}
                // Banner, not a stun star: the matching small pennant appears on affected enemies.
                Poly(p,SteelDark,c+V(-9,-25),c+V(9,-25),c+V(9,-6),c+V(0,1),c+V(-9,-6));
                Poly(p,Azure,c+V(-6,-22),c+V(6,-22),c+V(6,-7),c+V(0,-2),c+V(-6,-7));Line(p,c+V(0,-21),c+V(0,-8),Pearl);return;
            }
            if(id=="guard"){Crest(p,c+V(0,-6),44,open);Arc(p,c+V(0,18),29,9,0,Tau,Azure,2);return;}
            Dome(p,c+V(0,15),t,id=="citadel");
            if(id=="shield"){
                Crest(p,c+V(-17,-1),28,open);Crest(p,c+V(17,-1),28,open);
                Facet(p,c+V(-10,6),c+V(10,6),2,SteelDark,Azure,Silver);
            }else{
                Crest(p,c+V(0,-8),id=="citadel"?45:33,open);
                if(id=="citadel")for(int side=-1;side<=1;side+=2)for(int k=0;k<3;k++){
                    Vector2 a=c+V(side*(10+k*2),-10+k*5),b=c+V(side*(23+k*4),-24+k*7);
                    Facet(p,a,b,3,SteelDark,Azure,Silver);
                }
            }
        }
        static void ForgedMage(CareerRaster p,string id,Vector2 c,float t)
        {
            float grow=Out(t*3+.25f);var floor=c+V(0,17);
            Sigil(p,floor,31,t*.22f,Violet,id=="eclipse");
            if(id=="fire"){
                // A hot lance rises from a broken rune socket, with separate small tongues.
                p.Ellipse(c.x,c.y+12,15,5,Wine);p.Ellipse(c.x,c.y+11,10,3,Red);
                for(int k=0;k<3;k++)Plume(p,c+V((k-1)*12,13), (k==1?44:27)*grow*(1-t*.35f),Mathf.Sin(t*6+k)*7,k);
                Facet(p,c+V(-4,8),c+V(3,-25*grow),3,Wine,Amber,Pearl);
                for(int k=0;k<4;k++)Diamond(p,c+V(-16+k*10,-9-t*17+k%2*4),1,3,Amber);return;
            }
            if(id=="ice"){
                for(int k=0;k<5;k++){float a=-Mathf.PI*.88f+k*.6f;Vector2 a0=floor+V((k-2)*4,0),b=c+V(Mathf.Cos(a)*(22+k%2*7),Mathf.Sin(a)*(28+k%2*5));
                    Facet(p,a0,Vector2.Lerp(a0,b,grow),k==2?7:5,SteelMid,Azure,Ice);}
                for(int k=0;k<6;k++){var a=floor+V((k-3)*7,1);Line(p,a,a+V((k-3)*2,4),Ice);}return;
            }
            if(id=="storm"){
                // Dark segmented conductor behind a sharp, branched discharge.
                for(int k=0;k<3;k++)Crystal(p,c+V(-14+k*14,-24+k%2*4),5,-Mathf.PI/2,ArcaneMid);
                Bolt(p,c+V(8,-34),c+V(-3,15),31+I(t*5),Gold);
                Bolt(p,c+V(1,-7),c+V(25,8),17+I(t*4),Violet);
                Bolt(p,c+V(0,-8),c+V(-27,4),31+I(t*3),Azure);
                Diamond(p,c+V(-3,13),6,3,Pearl);return;
            }
            if(id=="orbit"){
                for(int k=0;k<3;k++){float a=k*Tau/3+t*3;var at=c+V(Mathf.Cos(a)*26,Mathf.Sin(a)*16-5);
                    Arc(p,c+V(0,-5),26,16,a-.9f,.8f,ArcaneMid,3);Crystal(p,at,8,a,Violet);Diamond(p,at,2,4,Pearl);}
                Crystal(p,c,15,-Mathf.PI/2,ArcaneMid);return;
            }
            if(id=="blink"){
                for(int k=0;k<3;k++){var at=c+V((k-1)*18,0);Arc(p,at,7,24,0,Tau,k==2?Ice:ArcaneMid,2);
                    Facet(p,at+V(0,12),at+V(2,-16),3,ArcaneDark,Violet,Ice);}
                for(int k=0;k<4;k++)Line(p,c+V(-33,-18+k*12),c+V(28,-18+k*12),Alpha(Violet,.4f));return;
            }
            if(id=="rift"){
                float w=7+Mathf.Sin(t*Mathf.PI)*5;
                Poly(p,ArcaneDark,c+V(2,-32),c+V(w,-12),c+V(3,2),c+V(w-3,14),c+V(-4,28),c+V(-w,10),c+V(-3,-3),c+V(-w,-18));
                Line(p,c+V(2,-32),c+V(-w,-18),Violet,2);Line(p,c+V(-w,-18),c+V(-3,-3),Ice);Line(p,c+V(3,2),c+V(w-3,14),Violet,3);
                for(int k=0;k<5;k++){
                    float y=-21+k*9;var at=c+V(k%2==0?-3:2,y);
                    Line(p,at,at+V(3,-2),ArcaneMid,.5f);Line(p,at+V(3,-2),at+V(1,3),Violet,.5f);
                }
                for(int k=0;k<5;k++){float a=k*Tau/5+t*.5f;Crystal(p,c+Polar(a,25),4,a,Violet);}return;
            }
            // The story's three elements surround an eclipse core; no borrowed reaper or curse.
            p.Circle(c.x,c.y-4,18,ArcaneDark);Arc(p,c+V(0,-4),20,20,-.8f+t,4.4f,Violet,3);
            Arc(p,c+V(0,-4),18,18,.3f+t,2.9f,Ice,2);
            Plume(p,c+V(-22,6),22,4,0);Facet(p,c+V(20,9),c+V(27,-18),6,SteelMid,Azure,Ice);
            Bolt(p,c+V(-9,-30),c+V(12,-22),3+I(t*4),Gold);
            for(int k=0;k<3;k++){float a=k*Tau/3+t;Arc(p,c+V(0,-3),29,25,a,.8f,k==0?Amber:k==1?Ice:Gold,2);}
            Diamond(p,c+V(0,-4),5*(1-t)+2,13*(1-t)+2,Pearl);
        }
        static void ForgedBishop(CareerRaster p,string id,Vector2 c,float t)
        {
            float open=Out(t*3+.2f);
            if(id=="light"){
                Facet(p,c+V(-30,0),c+V(36,0),5,Ochre,Gold,Pearl);
                for(int side=-1;side<=1;side+=2)Feather(p,c+V(-10,side*2),c+V(-23,side*14),4,Cream);
                Poly(p,Cream,c+V(22,-9),c+V(36,0),c+V(22,9),c+V(26,0));return;
            }
            if(id=="hot"){
                Petals(p,c,t,false);Line(p,c+V(0,16),c+V(0,-14),GreenDark,3);
                for(int side=-1;side<=1;side+=2)for(int k=0;k<2;k++)Feather(p,c+V(0,5-k*10),c+V(side*(12-k*3),-3-k*12),4,Leaf);
                Arc(p,c+V(0,17),24,7,0,Tau,Gold);return;
            }
            if(id=="cleanse"){
                var bell=c+V(0,-11);
                Poly(p,Ochre,bell+V(-11,12),bell+V(-7,6),bell+V(-6,-8),bell+V(0,-12),bell+V(6,-8),bell+V(7,6),bell+V(11,12));
                Poly(p,Gold,bell+V(-8,9),bell+V(-5,4),bell+V(-4,-7),bell+V(0,-9),bell+V(4,-7),bell+V(5,5),bell+V(8,9));
                Line(p,bell+V(-2,-7),bell+V(-3,7),Pearl,2);Line(p,bell+V(-11,12),bell+V(11,12),Cream,2);Diamond(p,bell+V(0,16),3,3,Gold);
                for(int k=0;k<2;k++)Arc(p,c+V(0,12),18+Out(Mathf.Clamp01(t-k*.2f))*18,7+Out(t)*7,0,Tau,k==0?Gold:Leaf);
                for(int k=0;k<6;k++){float a=k*Tau/6;var at=c+Polar(a,23+Out(t)*14);p.Rect(I(at.x),I(at.y),2,2,Alpha(ArcaneMid,1-t));}return;
            }
            if(id=="heal"){
                Petals(p,c,t,true);Pinions(p,c+V(0,6),open*.6f,t,false);
                Diamond(p,c+V(0,-4),4,12,Pearl);Arc(p,c+V(0,-23),11,4,0,Tau,Gold,2);return;
            }
            if(id=="bless"){
                Arc(p,c+V(0,-21),22,6,0,Tau,Ochre,3);Arc(p,c+V(0,-22),22,6,0,Tau,Gold);
                for(int k=0;k<5;k++){var at=c+V(-20+k*10,-8+Out(t)*20+k%2*5);Feather(p,at+V(-2,-8),at+V(2,4),2.5f,Cream);}return;
            }
            Pinions(p,c+V(0,5),open,t,id=="dawn");
            var floor=c+V(0,20);Arc(p,floor,30,9,0,Tau,Gold,2);Arc(p,floor,24,6,0,Tau,Leaf);
            Arc(p,c+V(0,-28),id=="dawn"?19:12,4,0,Tau,Gold,2);
            if(id=="wings"){Diamond(p,c+V(0,1),5,12,Ochre);Diamond(p,c+V(0,0),3,10,Cream);}
            else{
                // Dawn's sanctuary: carved arch, open interior, feathered wings, living floor.
                for(int side=-1;side<=1;side+=2){var at=c+V(side*11,8);Facet(p,at,at+V(0,-26*open),3,Ochre,Gold,Cream);}
                Arc(p,c+V(0,-15),11,10,Mathf.PI,Mathf.PI,Cream,2);
                Diamond(p,c+V(0,-12),3,9,Pearl);Petals(p,floor+V(0,-4),t,false);
            }
        }
        static PixelCanvas ForgedDetail(Career career,bool charge,int frame)
        {
            var p=new CareerRaster();var c=V(48,48);float t=frame/11f;
            if(!charge){Shards(p,c,t,7,career==Career.Fighter?Wine:SteelDark,career==Career.Fighter?Amber:Cyan,61);return Finish(p,t);}
            if(career==Career.Fighter){
                for(int k=0;k<4;k++){float a=k*Tau/4+.4f;float r=28-18*Out(t);Facet(p,c+Polar(a,r+7),c+Polar(a,r-5),2,Wine,Red,Silver);}
            }else if(career==Career.Guardian){Crest(p,c+V(0,-5),18+10*t,.5f+t*.5f);Arc(p,c+V(0,18),26,7,0,Tau,Azure);}
            else if(career==Career.Arcanist){Sigil(p,c+V(0,15),23+6*t,t*.6f,Violet);for(int k=0;k<3;k++){float a=k*Tau/3+t;Crystal(p,c+Polar(a,23-12*t),4,a,Violet);}}
            else {Petals(p,c,t,true);Arc(p,c+V(0,-22),9+5*t,4,0,Tau,Gold);}
            return p;
        }
        static PixelCanvas ForgedImpact(CareerSkill skill,int frame)
        {
            var p=new CareerRaster();var c=V(48,48);float t=frame/11f;
            if(skill.career==Career.Fighter){
                Facet(p,c+V(-24,-14)*(1-t*.6f),c+V(25,15)*(1-t*.4f),5*(1-t)+1,Wine,Amber,Pearl);
                Facet(p,c+V(-10,20)*(1-t*.6f),c+V(13,-23)*(1-t*.4f),3,Wine,Red,Silver);
                Shards(p,c,t,6,Wine,skill.effect=="break"?Silver:Red,13);return Finish(p,t);
            }
            if(skill.career==Career.Guardian){
                if(skill.effect=="shield"||skill.effect=="ward"||skill.effect=="citadel")Crest(p,c,30,1);
                else {Crest(p,c,21,1);Shards(p,c,t,6,SteelDark,Cyan,24);}return Finish(p,t);
            }
            if(skill.career==Career.Arcanist){
                if(skill.effect=="fire"){for(int i=0;i<3;i++)Plume(p,c+V((i-1)*10,12),28*(1-t*.65f)+i%2*6,(i-1)*5,0);}
                else if(skill.effect=="ice"){for(int i=0;i<6;i++){float a=i*Tau/6;Facet(p,c+Polar(a,6+t*15),c+Polar(a,20+t*12),4*(1-t)+1,SteelMid,Azure,Ice);}}
                else if(skill.effect=="storm"){for(int i=0;i<3;i++)Bolt(p,c,c+Polar(i*Tau/3,25-t*7),i+I(t*6),Gold);}
                else {Poly(p,ArcaneDark,c+V(0,-20),c+V(12,0),c+V(0,21),c+V(-12,0));Shards(p,c,t,5,ArcaneDark,Violet,31);Diamond(p,c,3,13,Ice);}
                return Finish(p,t);
            }
            if(skill.effect=="light"){
                Facet(p,c+V(-28,0),c+V(29,0),4,Ochre,Gold,Pearl);Shards(p,c,t,5,Ochre,Gold,12);
            }else if(skill.effect=="cleanse"){
                Petals(p,c,t,false);for(int i=0;i<7;i++){var a=c+Polar(i*Tau/7,12+Out(t)*22);p.Rect(I(a.x),I(a.y),2,2,Alpha(ArcaneMid,1-t));}Arc(p,c,12+Out(t)*16,8+Out(t)*10,0,Tau,Gold);
            }else if(skill.effect=="wings") {Pinions(p,c+V(0,5),.7f,t,false);}
            else{
                // The bright central seed exists on frame zero: actual healing has already happened.
                Petals(p,c,t,false);Diamond(p,c,3+4*(1-t),10*(1-t)+3,Pearl);
                for(int side=-1;side<=1;side+=2)Feather(p,c+V(side*3,6),c+V(side*(15+t*8),-9+t*5),3,Leaf);
                Arc(p,c+V(0,-17),12,4,0,Tau,Gold);
            }
            return Finish(p,t);
        }
    }
}
