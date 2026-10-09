using System;

namespace DotRPG
{
    /// <summary>Connected excavation terraces. Geometry is shared by gameplay and the art generation guide.</summary>
    public static class UnderworldComposition
    {
        public const int Width=56,Height=48;

        public static char[,] Create(int variant)
        {
            var a=new char[Width,Height];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)a[x,y]='W';
            if(variant==0)Descent(a);
            else if(variant==1)Roots(a);
            else if(variant==2)Fungal(a);
            else Depths(a);
            return a;
        }

        static void Descent(char[,] a)
        {
            // A small arrival court opens onto a bent mine junction; the two workshops are separate terraces.
            Floor(a,7,19,10,17,17,18,20,21,19,29,15,31,8,29);
            Floor(a,21,22,25,20,30,23,30,28,26,31,21,29);
            Floor(a,25,34,28,31,36,32,39,36,38,42,30,44,25,41);
            Floor(a,30,9,34,6,42,8,44,12,43,18,39,21,31,19,29,14);
            Path(a,0,24,13,24,6);
            Path(a,16,24,25,25,6);
            Path(a,27,27,31,35,6);
            Path(a,30,40,28,47,6);
            Path(a,27,23,34,17,6);
            Path(a,35,10,28,5,6);
            Path(a,28,5,28,0,6);
            Water(a,40,9,43,10,43,13,41,15,39,12);
            Steps(a,27,29,5,3);Steps(a,29,19,4,3);
        }

        static void Roots(char[,] a)
        {
            // Two uneven cloister walks pass above and below a long open ravine.
            Floor(a,7,20,10,17,16,18,19,22,17,28,11,30,7,27);
            Floor(a,18,33,21,30,29,31,32,35,30,41,23,43,18,40);
            Floor(a,19,9,24,6,31,8,33,13,29,19,22,18,18,14);
            Floor(a,37,22,40,18,47,19,50,24,48,30,42,32,37,28);
            Path(a,0,24,12,24,6);Path(a,15,27,22,35,6);
            Path(a,28,35,42,27,6);Path(a,15,21,23,13,6);
            Path(a,29,13,42,22,6);Path(a,44,24,55,24,6);
            Water(a,20,9,24,8,27,9,26,12,22,12);
            Steps(a,17,27,4,3);Steps(a,32,17,4,3);
        }

        static void Fungal(char[,] a)
        {
            // A low wet gallery bends south while an elevated mushroom alcove gives a second route.
            Floor(a,6,22,9,18,16,19,19,24,17,29,11,32,6,29);
            Floor(a,20,11,23,7,31,8,35,13,32,19,26,21,20,18);
            Floor(a,36,24,39,20,47,21,50,25,49,32,43,35,37,32);
            Floor(a,24,36,28,32,34,34,37,39,33,44,27,43,24,40);
            Path(a,0,24,12,24,6);Path(a,16,22,25,14,6);
            Path(a,30,17,41,26,7);Path(a,46,25,55,24,6);
            Path(a,16,28,28,37,6);Path(a,33,37,43,30,6);
            Water(a,24,12,27,11,30,12,29,15,25,15);
            Water(a,45,29,49,28,49,32,46,34,44,32);
            Steps(a,20,29,4,3);Steps(a,34,21,5,3);
        }

        static void Depths(char[,] a)
        {
            // Descending offset halls and a side crypt; negative space reveals the lower ruined city.
            Floor(a,12,34,15,30,23,31,27,35,25,41,19,43,13,40);
            Floor(a,24,23,28,19,35,20,39,25,36,30,29,32,24,28);
            Floor(a,32,11,35,7,42,8,46,13,44,18,37,21,32,17);
            Floor(a,8,18,12,14,18,16,20,21,16,26,10,25,8,22);
            Path(a,28,47,28,40,6);Path(a,28,40,20,36,6);
            Path(a,20,33,29,26,6);Path(a,29,24,15,21,6);
            Path(a,33,22,40,14,6);Path(a,37,10,28,5,6);
            Path(a,28,5,28,0,6);
            Water(a,11,19,13,17,16,19,15,22,11,23);
            Steps(a,23,29,4,3);Steps(a,33,18,5,3);
        }

        static void Floor(char[,] a,params int[] p)=>Polygon(a,',',p);
        static void Water(char[,] a,params int[] p)=>Polygon(a,'~',p);
        static void Polygon(char[,] a,char value,int[] p)
        {
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                float px=x+.5f,py=y+.5f;bool inside=false;
                for(int i=0,j=p.Length-2;i<p.Length;j=i,i+=2)
                    if((p[i+1]>py)!=(p[j+1]>py)&&px<(p[j]-p[i])*(py-p[i+1])/(p[j+1]-p[i+1])+p[i])inside=!inside;
                if(inside)a[x,y]=value;
            }
        }
        static void Path(char[,] a,float ax,float ay,float bx,float by,float width)
        {
            // Cell-centred route segments keep every required passage 5–7 tiles wide before contour smoothing.
            ax+=.5f;ay+=.5f;bx+=.5f;by+=.5f;
            float dx=bx-ax,dy=by-ay,length=dx*dx+dy*dy,r=width*.5f;
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                float px=x+.5f-ax,py=y+.5f-ay;
                float t=length>0?Math.Max(0,Math.Min(1,(px*dx+py*dy)/length)):0;
                float ex=px-t*dx,ey=py-t*dy;
                if(ex*ex+ey*ey<=r*r)a[x,y]=',';
            }
        }
        static void Steps(char[,] a,int x,int y,int w,int h)
        {
            // Visual tread zone only: no stacked traversal or new height mechanic.
            for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++)if(a[xx,yy]==',')a[xx,yy]='=';
        }
    }
}
