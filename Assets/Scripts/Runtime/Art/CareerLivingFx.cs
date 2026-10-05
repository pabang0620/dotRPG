using UnityEngine;
using UnityEngine.Rendering;

namespace DotRPG
{
    // Animated mesh artwork and low-count moving ribbons. No physics, timers or combat writes.
    [DefaultExecutionOrder(110)]
    public sealed class CareerLivingFx : MonoBehaviour
    {
        public enum Shape { Crescent, Streak, Spiral, Sword, Flame, Shield, Pulse, Crystal, Lightning, Orbit, Feather, Halo, Wings }
        public static Shape Design(string id)
        {
            switch(id){
                case "f_cross":case "f_execute":return Shape.Crescent;
                case "f_rush":case "b_light":return Shape.Streak;
                case "f_flurry":case "m_rift":return Shape.Spiral;
                case "f_break":return Shape.Sword;
                case "f_focus":case "m_fire":return Shape.Flame;
                case "f_awake":case "m_orbit":case "m_awake":return Shape.Orbit;
                case "g_guard":case "g_wall":case "g_oath":case "g_counter":return Shape.Shield;
                case "g_taunt":case "g_bash":case "g_awake":case "b_cleanse":return Shape.Pulse;
                case "m_ice":return Shape.Crystal;
                case "m_storm":return Shape.Lightning;
                case "m_veil":case "b_bloom":return Shape.Halo;
                case "b_heal":case "b_bless":return Shape.Feather;
                case "b_wing":case "b_awake":return Shape.Wings;
                default:return Shape.Pulse;
            }
        }
        const int Grid=8,Vertices=81,Segments=96;
        public static int Live {get;private set;}
        public static int MeshUpdates {get;private set;}
        public static int Peak {get;private set;}
        SpriteRenderer source;Mesh artMesh,flowMesh;MeshRenderer art,flow;MaterialPropertyBlock block;
        readonly Vector3[] vertices=new Vector3[Vertices];readonly Vector2[] uv=new Vector2[Vertices];readonly Color[] colors=new Color[Vertices];
        readonly Vector3[] stream=new Vector3[Segments*4];readonly Color[] hues=new Color[Segments*4];
        Sprite lastSprite;Shape shape;Color tint;string id;float clock;int mode,used;Vector2 extent;
        public float Signature {get;private set;}
        public static CareerLivingFx Attach(SpriteRenderer sr,CareerSkill s,int phase)
        {
            if(sr==null||s==null||Live>=80)return null;
            var fx=sr.gameObject.AddComponent<CareerLivingFx>();fx.source=sr;fx.shape=Design(s.id);fx.tint=CareerCatalog.Color(s.career);fx.id=s.id;fx.mode=phase;
            if(s.id=="f_cross"||s.id=="f_break")fx.tint=new Color(.25f,.75f,1);
            if(s.id=="f_flurry"||s.id=="m_rift")fx.tint=new Color(.74f,.35f,1);
            if(s.id=="f_rush"||s.id=="f_execute")fx.tint=new Color(1,.75f,.28f);
            if(s.id=="f_focus"||s.id=="m_fire")fx.tint=new Color(1,.36f,.08f);
            if(s.id=="m_ice")fx.tint=new Color(.4f,.85f,1);
            fx.Build();Live++;Peak=Mathf.Max(Peak,Live);return fx;
        }
        void Build()
        {
            block=new MaterialPropertyBlock();artMesh=new Mesh{name="Living career cel"};artMesh.MarkDynamic();
            var surface=new GameObject("Deforming artwork");surface.transform.SetParent(transform,false);var filter=surface.AddComponent<MeshFilter>();filter.sharedMesh=artMesh;art=surface.AddComponent<MeshRenderer>();art.sharedMaterial=FxMaterials.Alpha;art.shadowCastingMode=ShadowCastingMode.Off;art.receiveShadows=false;
            int[] triangles=new int[Grid*Grid*6];int k=0;
            for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++){int a=y*(Grid+1)+x;triangles[k++]=a;triangles[k++]=a+Grid+1;triangles[k++]=a+1;triangles[k++]=a+1;triangles[k++]=a+Grid+1;triangles[k++]=a+Grid+2;}
            artMesh.vertices=vertices;artMesh.triangles=triangles;
            var go=new GameObject("Flowing silhouette");go.transform.SetParent(transform,false);flowMesh=new Mesh{name="Career ribbons"};flowMesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=flowMesh;
            flow=go.AddComponent<MeshRenderer>();flow.sharedMaterial=FxMaterials.Alpha;flow.shadowCastingMode=ShadowCastingMode.Off;flow.receiveShadows=false;
            var white=new MaterialPropertyBlock();white.SetTexture("_MainTex",Texture2D.whiteTexture);flow.SetPropertyBlock(white);
            int[] indices=new int[Segments*6];for(int i=0;i<Segments;i++){int a=i*4,b=i*6;indices[b]=a;indices[b+1]=a+1;indices[b+2]=a+2;indices[b+3]=a+2;indices[b+4]=a+1;indices[b+5]=a+3;}
            flowMesh.vertices=stream;flowMesh.triangles=indices;flowMesh.uv=new Vector2[Segments*4];
        }
        public static Vector2 Warp(Shape kind,Vector2 p,float time)
        {
            float x=p.x,y=p.y;
            switch(kind){
                case Shape.Crescent:x+=Mathf.Sin(time*22+y*8)*.025f*(.5f-x);break;
                case Shape.Streak:y+=Mathf.Sin(time*28+x*13)*.035f*(.5f-x);break;
                case Shape.Spiral:case Shape.Orbit:case Shape.Halo:
                    float a=(kind==Shape.Spiral?time*5:0)+Mathf.Sin(time*5+p.sqrMagnitude*14)*.12f;x=p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a);y=p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a);break;
                case Shape.Flame:x+=Mathf.Sin(time*20+y*11)*.055f*(y+.5f);y+=Mathf.Sin(time*17+x*14)*.018f;break;
                case Shape.Crystal:y+=Mathf.Sin(time*11+x*9)*.025f*(y+.5f);break;
                case Shape.Lightning:x+=Mathf.Sin(Mathf.Floor(time*24)*3+y*28)*.035f;y+=Mathf.Sin(Mathf.Floor(time*24)*4+x*23)*.025f;break;
                case Shape.Feather:case Shape.Wings:y+=Mathf.Sin(time*12+Mathf.Abs(x)*7)*.07f*Mathf.Abs(x)*2;break;
                case Shape.Pulse:float r=1+.035f*Mathf.Sin(time*16-p.magnitude*10);x*=r;y*=r;break;
                case Shape.Shield:x+=Mathf.Sin(time*9)*.015f*(.25f-y*y);break;
                case Shape.Sword:x+=Mathf.Sin(time*24+y*12)*.009f;break;
            }
            return new Vector2(x,y);
        }
        void LateUpdate()
        {
            if(source==null||source.sprite==null||!source.enabled){art.enabled=flow.enabled=false;return;}
            clock+=Time.deltaTime;source.forceRenderingOff=true;
            var camera=Game.Camera?.Camera;if(camera!=null){var point=camera.WorldToViewportPoint(transform.position);if(point.x<-.8f||point.x>1.8f||point.y<-.8f||point.y>1.8f){art.enabled=flow.enabled=false;return;}}
            var sprite=source.sprite;var bounds=sprite.bounds;extent=bounds.size;
            // Original PNG remains point-filtered. Geometry changes instead of regenerating textures.
            if(lastSprite!=sprite){lastSprite=sprite;var rect=sprite.rect;var tex=sprite.texture;
                for(int y=0;y<=Grid;y++)for(int x=0;x<=Grid;x++)uv[y*(Grid+1)+x]=new Vector2((rect.x+rect.width*x/Grid)/tex.width,(rect.y+rect.height*y/Grid)/tex.height);
                artMesh.uv=uv;block.SetTexture("_MainTex",tex);art.SetPropertyBlock(block);
            }
            Signature=0;for(int y=0;y<=Grid;y++)for(int x=0;x<=Grid;x++){
                int i=y*(Grid+1)+x;var p=Warp(shape,new Vector2(x/(float)Grid-.5f,y/(float)Grid-.5f),clock);
                vertices[i]=new Vector3(p.x*extent.x+bounds.center.x,p.y*extent.y+bounds.center.y,0);colors[i]=source.color;Signature+=p.x*(i+1)+p.y*(i+2);
            }
            artMesh.vertices=vertices;artMesh.colors=colors;artMesh.RecalculateBounds();art.sortingOrder=source.sortingOrder;art.enabled=true;
            used=0;DrawFlow();for(int i=used*4;i<stream.Length;i++){stream[i]=Vector3.zero;hues[i]=Color.clear;}
            flowMesh.vertices=stream;flowMesh.colors=hues;flowMesh.RecalculateBounds();flow.sortingOrder=source.sortingOrder+1;flow.enabled=source.color.a>.01f;MeshUpdates++;
        }
        void Line(Vector2 a,Vector2 b,float width,float alpha)
        {
            if(used>=Segments)return;var delta=b-a;if(delta.sqrMagnitude<.000001f)return;
            var side=new Vector2(-delta.y,delta.x).normalized*width*.5f;int k=used++*4;
            Set(k,a-side,alpha);Set(k+1,a+side,alpha);Set(k+2,b-side,alpha*.7f);Set(k+3,b+side,alpha*.7f);
        }
        void Set(int k,Vector2 p,float alpha){stream[k]=new Vector3(Mathf.Round(p.x*extent.x*96)/96,Mathf.Round(p.y*extent.y*96)/96,0);var c=tint;c.a=Mathf.Min(.78f,source.color.a)*alpha;hues[k]=c;}
        Vector2 Ring(float a,float radius)=>new Vector2(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius*.70f);
        void Arc(float phase,float radius,float span,float width,float alpha)
        {for(int i=0;i<20;i++){float t=i/20f;Line(Ring(phase+span*t,radius),Ring(phase+span*(t+.05f),radius),width*Mathf.Sin((t+.03f)*Mathf.PI),alpha);}}
        void DrawFlow()
        {
            float t=clock;
            if(mode==2){for(int j=0;j<8;j++){float q=Mathf.Repeat(t*1.6f+j/8f,1);float a=j*Mathf.PI*.25f+t;var p=Ring(a,.5f*(1-q));Line(p,p+Ring(a,.055f),.012f,q);}return;}
            switch(shape){
                case Shape.Crescent:Arc(-1.3f+t*2,.40f,2.5f,.027f,.8f);Arc(-1.4f+t*2,.35f,2.1f,.012f,.55f);break;
                case Shape.Streak:case Shape.Flame:
                    for(int j=0;j<3;j++)for(int i=0;i<16;i++){float x=-.5f+i/16f;float y=(j-1)*.10f+Mathf.Sin(t*23+x*13+j)*.04f*(.5f-x);float xx=x+1/16f;float yy=(j-1)*.10f+Mathf.Sin(t*23+xx*13+j)*.04f*(.5f-xx);Line(new Vector2(x,y),new Vector2(xx,yy),(.014f+j*.004f)*(1+x),.7f);}break;
                case Shape.Spiral:case Shape.Orbit:case Shape.Halo:
                    for(int j=0;j<3;j++)Arc(t*(j%2==0?4:-3)+j*2.1f,.20f+j*.09f,2.4f,.020f,.65f);break;
                case Shape.Shield:
                    for(int j=0;j<6;j++){float a=j*Mathf.PI/3;float q=Mathf.Repeat(t*1.4f+j/6f,1);var p=Ring(a,.38f);Line(p,Ring(a+Mathf.PI/3*.72f,.38f),.015f,.35f+.5f*q);Line(p,Ring(a,.26f),.013f,.6f*q);}break;
                case Shape.Pulse:
                    for(int j=0;j<2;j++){float q=Mathf.Repeat(t*2+j*.5f,1);Arc(j+q,.10f+.34f*q,Mathf.PI*1.8f,.020f*(1-q),1-q);}break;
                case Shape.Crystal:case Shape.Sword:
                    for(int j=0;j<7;j++){float q=Mathf.Repeat(t*2+j*.143f,1);float x=(j-3)*.10f;var p=new Vector2(x,-.34f+q*.62f);Line(p,p+new Vector2(.012f,.13f*(1-q)),.015f,1-q);}break;
                case Shape.Lightning:
                    for(int j=0;j<3;j++)for(int i=0;i<10;i++){float tick=Mathf.Floor(t*24);float x=i/10f-.5f;float y=(j-1)*.10f+Mathf.Sin(i*11+tick*7+j)*.075f;float yy=(j-1)*.10f+Mathf.Sin((i+1)*11+tick*7+j)*.075f;Line(new Vector2(x,y),new Vector2(x+.10f,yy),j==0?.024f:.010f,.85f);}break;
                case Shape.Feather:case Shape.Wings:
                    for(int j=0;j<10;j++){float q=Mathf.Repeat(t*1.4f+j*.10f,1);float a=j*.628f+t*.65f;float r=shape==Shape.Feather?.45f*(1-q):.15f+.3f*q;var p=Ring(a,r)+Vector2.up*Mathf.Sin(q*Mathf.PI)*.10f;Line(p,p+new Vector2(.025f,.065f)*Mathf.Sin(a),.018f,Mathf.Sin(q*Mathf.PI));}break;
            }
        }
        void OnDestroy(){if(artMesh!=null)Destroy(artMesh);if(flowMesh!=null)Destroy(flowMesh);Live=Mathf.Max(0,Live-1);}
    }
}
