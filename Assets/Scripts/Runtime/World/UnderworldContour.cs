using UnityEngine;

namespace DotRPG
{
    /// <summary>Continuous cave silhouette shared by its 32px artwork and 1/8-tile physics mask.</summary>
    public sealed class UnderworldContour
    {
        public const int Scale=8;
        public readonly int Width,Height,Columns,Rows;
        readonly byte[] kind;
        readonly float[] floorDistance,waterDistance;
        public UnderworldContour(char[,] cells,int w,int h,string mapId=null)
        {
            Width=w;Height=h;Columns=w*Scale;Rows=h*Scale;kind=new byte[Columns*Rows];
            int authored=UnderworldCompositionNav.Variant(mapId);
            float Value(int x,int y,int type)=>x<0||y<0||x>=w||y>=h?-1:((cells[x,y]=='W'?0:cells[x,y]=='~'?2:1)==type?1:-1);
            float Blend(float x,float y,int type)
            {
                x-=.5f;y-=.5f;int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;
                return Mathf.Lerp(Mathf.Lerp(Value(ix,iy,type),Value(ix+1,iy,type),fx),Mathf.Lerp(Value(ix,iy+1,type),Value(ix+1,iy+1,type),fx),fy);
            }
            for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
            {
                float wx=(x+.5f)/Scale,wy=(y+.5f)/Scale;int cx=x/Scale,cy=y/Scale;
                char c=cells[cx,cy];byte original=(byte)(c=='W'?0:c=='~'?2:1);
                if(authored>=0)
                {
                    bool sealedEdge=c=='W'&&(cx==0||cy==0||cx==w-1||cy==h-1);
                    kind[y*Columns+x]=WorldRoutes.Portal(c)?(byte)1:sealedEdge?(byte)0:UnderworldCompositionNav.At(authored,wx,wy);
                    continue;
                }
                float sx=wx+.10f*Mathf.Sin(wy*1.13f+wx*.41f),sy=wy+.10f*Mathf.Sin(wx*.89f-wy*.32f);
                bool centre=Mathf.Abs(wx-cx-.5f)<.23f&&Mathf.Abs(wy-cy-.5f)<.23f;
                if(centre||WorldRoutes.Portal(c)||c=='P'||c=='k')kind[y*Columns+x]=original;
                else kind[y*Columns+x]=Blend(sx,sy,1)>0?(byte)1:Blend(sx,sy,2)>0?(byte)2:(byte)0;
            }
            // A curved footing tangent to the perimeter can leave one isolated 1/8-tile
            // sample. It cannot hold a character and must not become a phantom ledge.
            if(authored>=0)for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
                if(At(x,y)==1&&At(x-1,y)!=1&&At(x+1,y)!=1&&At(x,y-1)!=1&&At(x,y+1)!=1)kind[y*Columns+x]=0;
            floorDistance=Distance(1);waterDistance=Distance(2);
        }
        public byte At(int x,int y)=>x<0||y<0||x>=Columns||y>=Rows?(byte)0:kind[y*Columns+x];
        public bool Land(float x,float y)=>At(Mathf.FloorToInt(x*Scale),Mathf.FloorToInt(y*Scale))==1;
        public bool Water(float x,float y)=>At(Mathf.FloorToInt(x*Scale),Mathf.FloorToInt(y*Scale))==2;
        public float FloorDistance(float x,float y)=>Sample(floorDistance,x,y);
        public float WaterDistance(float x,float y)=>Sample(waterDistance,x,y);
        float Sample(float[] field,float x,float y)
        {
            x=Mathf.Clamp(x*Scale-.5f,0,Columns-1);y=Mathf.Clamp(y*Scale-.5f,0,Rows-1);
            int ix=(int)x,iy=(int)y,nx=Mathf.Min(ix+1,Columns-1),ny=Mathf.Min(iy+1,Rows-1);
            return Mathf.Lerp(Mathf.Lerp(field[iy*Columns+ix],field[iy*Columns+nx],x-ix),Mathf.Lerp(field[ny*Columns+ix],field[ny*Columns+nx],x-ix),y-iy);
        }
        float[] Distance(byte type)
        {
            var d=new float[kind.Length];
            for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
            {
                bool inside=At(x,y)==type;
                d[y*Columns+x]=(At(x-1,y)==type)!=inside||(At(x+1,y)==type)!=inside||(At(x,y-1)==type)!=inside||(At(x,y+1)==type)!=inside?.5f:64;
            }
            for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
            {
                int i=y*Columns+x;
                if(x>0)d[i]=Mathf.Min(d[i],d[i-1]+1);
                if(y>0){d[i]=Mathf.Min(d[i],d[i-Columns]+1);if(x>0)d[i]=Mathf.Min(d[i],d[i-Columns-1]+1.4142f);if(x+1<Columns)d[i]=Mathf.Min(d[i],d[i-Columns+1]+1.4142f);}
            }
            for(int y=Rows-1;y>=0;y--)for(int x=Columns-1;x>=0;x--)
            {
                int i=y*Columns+x;
                if(x+1<Columns)d[i]=Mathf.Min(d[i],d[i+1]+1);
                if(y+1<Rows){d[i]=Mathf.Min(d[i],d[i+Columns]+1);if(x>0)d[i]=Mathf.Min(d[i],d[i+Columns-1]+1.4142f);if(x+1<Columns)d[i]=Mathf.Min(d[i],d[i+Columns+1]+1.4142f);}
            }
            for(int i=0;i<d.Length;i++)d[i]=Mathf.Min(64,d[i])/Scale*(kind[i]==type?1:-1);
            return d;
        }
    }
}
