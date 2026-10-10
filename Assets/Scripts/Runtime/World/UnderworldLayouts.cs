using System;
using System.Collections.Generic;
using System.Text;

namespace DotRPG
{
    /// <summary>Authored connected chambers, not rectangular dungeon rooms. Top text row is north.</summary>
    public static class UnderworldLayouts
    {
        public static string Layout(string id)
        {
            if (id == "undergate") return Town();
            int variant = Array.IndexOf(new[] { "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" }, id);
            return variant < 0 ? null : Cave(variant);
        }
        static string Text(char[,] a)
        {
            var b = new StringBuilder();
            for (int y = a.GetLength(1) - 1; y >= 0; y--) { for (int x = 0; x < a.GetLength(0); x++) b.Append(a[x,y]); b.Append('\n'); }
            return b.ToString();
        }
        static void Oval(char[,] a, float cx, float cy, float rx, float ry, char c)
        {
            for (int y = Math.Max(0,(int)(cy-ry)); y < Math.Min(a.GetLength(1),cy+ry+1); y++)
                for (int x = Math.Max(0,(int)(cx-rx)); x < Math.Min(a.GetLength(0),cx+rx+1); x++)
                    if ((x-cx)*(x-cx)/(rx*rx)+(y-cy)*(y-cy)/(ry*ry) <= 1) a[x,y]=c;
        }
        static void Path(char[,] a, int ax,int ay,int bx,int by,int r,char c)
        {
            int n = Math.Max(Math.Abs(bx-ax),Math.Abs(by-ay));
            for (int k=0;k<=n;k++) { float t=n==0?0:k/(float)n; Oval(a,ax+(bx-ax)*t,ay+(by-ay)*t,r+.3f,r+.3f,c); }
        }
        static string Town()
        {
            const int w=52,h=44; var a=new char[w,h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++) a[x,y]=x<2||y<2||x>=w-2||y>=h-2?'W':'.';
            // A broad ring plaza connects all four approach roads around the root well.
            Oval(a,26,22,18,13,','); Oval(a,26,26,8,7,'.');
            Path(a,0,22,51,22,2,',');Path(a,26,0,26,19,2,',');
            Path(a,26,43,26,38,2,',');Path(a,26,38,35,32,2,',');Path(a,35,32,35,22,2,',');
            Oval(a,10,8,5,3,'~');Oval(a,42,35,4,3,'~');
            for(int k=-1;k<=1;k++){a[0,22+k]='<';a[51,22+k]='>';a[26+k,43]='[';a[26+k,0]=']';}
            a[26,15]='P';a[26,25]='{';
            return Text(a);
        }
        static string Cave(int v)
        {
            const int w=UnderworldComposition.Width,h=UnderworldComposition.Height;
            var a=UnderworldCompositionNav.Create(v);
            // Seal all unassigned edges before opening only the actual reciprocal exits.
            for(int x=0;x<w;x++){a[x,0]='W';a[x,h-1]='W';}for(int y=0;y<h;y++){a[0,y]='W';a[w-1,y]='W';}
            for(int k=-1;k<=1;k++)
            {
                if(v==0){a[0,24+k]='}';a[28+k,47]='[';}
                else if(v==3){a[28+k,0]=']';}
                else{a[0,24+k]='<';a[55,24+k]='>';}
            }
            int spawnX=v==3?28:4,spawnY=v==3?38:24;
            a[spawnX,spawnY]='P';
            // Camp anchors require a complete clear footprint and a quiet arrival area.
            // Select across all chambers before marking: a row-major cap leaves the northern half empty.
            var candidates=new List<(int x,int y)>();
            var roominess=new int[w,h];
            for(int y=7;y<h-6;y++)for(int x=7;x<w-6;x++)
            {
                bool clear=true;for(int yy=y-1;yy<=y+1;yy++)for(int xx=x-1;xx<=x+1;xx++)
                    if((a[xx,yy]!=','&&a[xx,yy]!='=')||!UnderworldCompositionNav.FootClear(v,xx+.5f,yy+.5f))clear=false;
                if(!clear||(x-spawnX)*(x-spawnX)+(y-spawnY)*(y-spawnY)<25)continue;
                if(!UnderworldCompositionNav.FootClear(v,x+.5f,y+.5f)
                    ||!UnderworldCompositionNav.FootClear(v,x+1.25f,y+.85f)
                    ||!UnderworldCompositionNav.FootClear(v,x-.2f,y+.95f))continue;
                // Broad chambers receive a preference over the middle of connecting walks.
                for(int yy=y-3;yy<=y+3;yy++)for(int xx=x-3;xx<=x+3;xx++)
                    if(UnderworldCompositionNav.At(v,xx+.5f,yy+.5f)==1)roominess[x,y]++;
                candidates.Add((x,y));
            }
            var camps=new List<(int x,int y)>();
            while(camps.Count<18&&candidates.Count>0)
            {
                int best=0;double bestScore=double.MinValue;
                for(int i=0;i<candidates.Count;i++)
                {
                    var p=candidates[i];
                    double score=roominess[p.x,p.y]*.055-Math.Sqrt((p.x-w/2)*(p.x-w/2)+(p.y-h/2)*(p.y-h/2))*.025;
                    if(camps.Count>0)
                    {
                        int distance=int.MaxValue;
                        foreach(var c in camps)distance=Math.Min(distance,(p.x-c.x)*(p.x-c.x)+(p.y-c.y)*(p.y-c.y));
                        score=Math.Sqrt(distance)+roominess[p.x,p.y]*.055;
                        foreach(var c in camps){if(c.x==p.x)score-=.35;if(c.y==p.y)score-=.35;}
                    }
                    uint hash=unchecked((uint)(p.x*73856093^p.y*19349663^v*83492791));
                    score+=(hash%1024)/4096.0;
                    if(score>bestScore){bestScore=score;best=i;}
                }
                camps.Add(candidates[best]);candidates.RemoveAt(best);
            }
            foreach(var p in camps)a[p.x,p.y]='k';
            return Text(a);
        }
    }
}
