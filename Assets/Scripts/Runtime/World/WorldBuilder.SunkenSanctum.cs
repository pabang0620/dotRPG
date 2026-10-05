using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        void BuildSunkenSanctum()
        {
            for (int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                char c=cells[x,y];var at=new Vector3Int(x,y,0);
                if(c=='~')waterMap.SetTile(at,ColliderTile());
                if(c=='W')cliffMap.SetTile(at,ColliderTile());
                if(c=='P')PlayerSpawn=new Vector2(x+.5f,y+.5f);
                if(c=='<'||c=='>'){
                    var pos=new Vector2(x+.5f,y+.5f);
                    string target=WorldRoutes.Target(map,c);
                    MapPortal.Create(pos,target,objectsRoot);
                    if(!portalCells.ContainsKey(target))portalCells[target]=new System.Collections.Generic.List<Vector2>();
                    portalCells[target].Add(pos);
                }
            }
            // Resolve collision in the load frame, before an arrival or path probe can run.
            waterMap.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>().ProcessTilemapChanges();
            cliffMap.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>().ProcessTilemapChanges();
            SunkenSanctumArt.Build(objectsRoot,cells,width,height);
            CreateBoundaryWalls();
            PointsOfInterest.Add(PlayerSpawn);PointsOfInterest.Add(new Vector2(21,35));PointsOfInterest.Add(new Vector2(24,51));
            BuildMinimap();
        }
    }
}

