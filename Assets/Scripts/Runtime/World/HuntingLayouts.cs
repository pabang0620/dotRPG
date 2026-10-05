using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Hand-shaped encounter spaces connected by broad, readable routes. Stable across visits.</summary>
    public static class HuntingLayouts
    {
        public static string Build(HuntingZone zone)
        {
            int w=56,h=44;bool forest=zone.theme==MapTheme.Forest,snow=zone.theme==MapTheme.Winter;
            char wall=forest?'%':'W',floor=forest?'.':snow?'.':',';
            char[,] a;
            if(zone.id=="forest"){
                var rows=Resources.Load<TextAsset>("Maps/Forest").text.Split('\n').Select(s=>s.TrimEnd('\r')).Where(s=>s.Length>0&&!s.StartsWith("//")).ToArray();
                w=rows.Max(s=>s.Length);h=rows.Length;a=new char[w,h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)a[x,y]=x<rows[h-1-y].Length?rows[h-1-y][x]:wall;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(a[x,y]=='>'||a[x,y]=='<')a[x,y]=wall;else if(a[x,y]=='P')a[x,y]=floor;
            }else{
                a=new char[w,h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                    float inset=2+2*Mathf.Sin(y*.23f+zone.variant);
                    bool edge=x<inset||x>=w-inset-1||y<3+1.5f*Mathf.Sin(x*.2f)||y>=h-3-1.5f*Mathf.Cos(x*.19f);
                    // Large shoulders frame the clearing; routes below cut through deliberately.
                    edge |= (x<10||x>w-11)&&(y<9||y>h-10);
                    char c=edge?wall:floor;
                    float dx=x-28,dy=y-22;
                    if(!edge){
                        if(forest){
                            if(zone.variant==1){if((Mathf.Abs(dx)<12&&Mathf.Abs(dy)<9)&&(Mathf.Abs(dx)>9||Mathf.Abs(dy)>6))c='%';if(dx>5&&dy>7)c='~';}
                            if(zone.variant==2){float stream=25+5*Mathf.Sin(y*.18f);if(Mathf.Abs(x-stream)<4||((x-43)*(x-43)+(y-11)*(y-11)<36))c='~';if(x<12&&y>28)c='%';}
                            if(zone.variant==3){if(Mathf.Abs(x-(15+y*.36f))<2.5f||Mathf.Abs(x-(43-y*.34f))<2.5f)c='~';}
                        }else if(snow){
                            if(zone.variant==0&&((x-22)*(x-22)+(y-29)*(y-29)<47||(x-39)*(x-39)+(y-11)*(y-11)<30))c='~';
                            if(zone.variant==1&&dx*dx/196+dy*dy/100<1)c='~';
                            if(zone.variant==2&&((x<19&&y>15)||(x>37&&y<31)||(x>23&&x<32&&y>30)))c='W';
                            if(zone.variant==3&&(Mathf.Abs(y-(13+4*Mathf.Sin(x*.12f)))<2||Mathf.Abs(y-32)<2&&x<39))c='~';
                        }else{
                            if(zone.variant==0&&Mathf.Abs(y-(21+6*Mathf.Sin(x*.15f)))<3)c='~';
                            if(zone.variant==1&&((Mathf.Abs(dx)<15&&Mathf.Abs(dy)<11)&&(Mathf.Abs(dx)>10||Mathf.Abs(dy)>7)))c='W';
                            if(zone.variant==2&&((x<18&&y>11)||(x>37&&y<32)||(x>24&&x<31&&y>31)))c='W';
                            if(zone.variant==3&&((x>17&&x<23&&y>8&&y<35)||(x>34&&x<41&&y>8&&y<35)))c='W';
                        }
                    }a[x,y]=c;
                }
            }
            var lanes=new bool[w,h];
            void Lane(int x,int y){if(x<0||y<0||x>=w||y>=h)return;char old=a[x,y];a[x,y]=old=='~'&&!(snow&&zone.variant==1)?'d':forest?'=':',';lanes[x,y]=true;}
            void Road(Vector2Int p,Vector2Int q,int radius=2){int steps=Math.Max(Math.Abs(p.x-q.x),Math.Abs(p.y-q.y));for(int i=0;i<=steps;i++){
                int xx=Mathf.RoundToInt(Mathf.Lerp(p.x,q.x,steps==0?0:i/(float)steps)),yy=Mathf.RoundToInt(Mathf.Lerp(p.y,q.y,steps==0?0:i/(float)steps));
                for(int y=yy-radius;y<=yy+radius;y++)for(int x=xx-radius;x<=xx+radius;x++)Lane(x,y);
            }}
            int mid=h/2,cx=w/2;
            var center=new Vector2Int(cx,mid);
            if(zone.variant==0){
                Road(new Vector2Int(0,mid),new Vector2Int(cx-8,mid));Road(new Vector2Int(cx-8,mid),center);
                Road(center,new Vector2Int(cx, h-1));Road(center,new Vector2Int(cx,0));
            }else if(zone.variant==3){
                Road(new Vector2Int(cx,h-1),new Vector2Int(cx-2,mid+7));Road(new Vector2Int(cx-2,mid+7),center);
                Road(new Vector2Int(cx,0),new Vector2Int(cx+2,mid-7));Road(new Vector2Int(cx+2,mid-7),center);Road(center,new Vector2Int(w-1,mid));
            }else{
                // Upper route arcs north of its landmark; lower route winds south, then both exit east.
                int bend=zone.variant==1?mid+12:mid-12;
                Road(new Vector2Int(0,mid),new Vector2Int(12,mid));Road(new Vector2Int(12,mid),new Vector2Int(18,bend));
                Road(new Vector2Int(18,bend),new Vector2Int(38,bend));Road(new Vector2Int(38,bend),new Vector2Int(45,mid));Road(new Vector2Int(45,mid),new Vector2Int(w-1,mid));
                // Secondary interior loop produces two encounter approaches without changing the world graph.
                Road(new Vector2Int(12,mid),new Vector2Int(18,h-bend),1);Road(new Vector2Int(18,h-bend),new Vector2Int(38,h-bend),1);Road(new Vector2Int(38,h-bend),new Vector2Int(45,mid),1);
            }
            if(zone.id!="forest"){
                var rng=new System.Random(761+Array.IndexOf(HuntingGrounds.All,zone)*217);
                for(int y=5;y<h-5;y+=3)for(int x=5;x<w-5;x+=3){
                    if(a[x,y]!=floor||lanes[x,y])continue;
                    bool nearLane=false;for(int yy=y-2;yy<=y+2;yy++)for(int xx=x-2;xx<=x+2;xx++)nearLane|=lanes[xx,yy];
                    if(!nearLane&&rng.NextDouble()<.5)a[x,y]=forest?(zone.variant==1?'i':rng.Next(2)==0?'T':'q'):snow?(rng.Next(3)==0?'R':'y'):(rng.Next(3)==0?'T':'R');
                }
                // Twenty-four camp markers wherever a 3x3 footprint is clear, away from all exits.
                var camps=new List<Vector2Int>();var candidates=new List<Vector2Int>();
                for(int y=7;y<h-7;y+=2)for(int x=7;x<w-7;x+=2)candidates.Add(new Vector2Int(x,y));
                foreach(var candidate in candidates.OrderBy(v=>unchecked((v.x*73856093 ^ v.y*19349663 ^ zone.variant*83492791)&0x7fffffff))){
                    if(camps.Count>=24)break;int x=candidate.x,y=candidate.y;
                    bool free=true;for(int yy=y-1;yy<=y+1;yy++)for(int xx=x-1;xx<=x+1;xx++)free&=a[xx,yy]==floor||a[xx,yy]=='='||a[xx,yy]==',';
                    if(!free||camps.Any(p=>Vector2Int.Distance(p,new Vector2Int(x,y))<4))continue;
                    camps.Add(new Vector2Int(x,y));a[x,y]='k';
                }
            }
            // Seal old edges, then open only the intended graph ports (three tiles wide).
            for(int x=0;x<w;x++){a[x,0]=wall;a[x,h-1]=wall;}for(int y=0;y<h;y++){a[0,y]=wall;a[w-1,y]=wall;}
            if(zone.variant==0){for(int i=-1;i<=1;i++){a[0,mid+i]='<';a[cx+i,h-1]='[';a[cx+i,0]=']';}a[3,mid]='P';}
            else if(zone.variant==3){for(int i=-1;i<=1;i++){a[cx+i,h-1]='[';a[cx+i,0]=']';a[w-1,mid+i]='>';}a[cx,mid]='P';}
            else{for(int i=-1;i<=1;i++){a[0,mid+i]='<';a[w-1,mid+i]='>';}a[3,mid]='P';}
            var text=new StringBuilder();for(int y=h-1;y>=0;y--){for(int x=0;x<w;x++)text.Append(a[x,y]);text.Append('\n');}return text.ToString();
        }
    }
}

