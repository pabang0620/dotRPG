using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
namespace DotRPG {
 public partial class WorldBuilder {
  void BuildSanctumField(){
   var camps=new List<Vector2>();
   for(int y=0;y<height;y++)for(int x=0;x<width;x++){
    char c=cells[x,y];var at=new Vector3Int(x,y,0);var p=new Vector2(x+.5f,y+.5f);
    if(c=='~')waterMap.SetTile(at,ColliderTile());if(c=='W')cliffMap.SetTile(at,ColliderTile());
    if(c=='P')PlayerSpawn=p;if(c=='k')camps.Add(p);
    if(WorldRoutes.Portal(c)){string target=WorldRoutes.Target(map,c);if(string.IsNullOrEmpty(target))continue;MapPortal.Create(p,target,objectsRoot);if(!portalCells.ContainsKey(target))portalCells[target]=new List<Vector2>();portalCells[target].Add(p);}
   }
   waterMap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();cliffMap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
   HuntingScenery.BuildSanctum(objectsRoot,cells,width,height,HuntingGrounds.Get(MapId));
   CreateBoundaryWalls();PointsOfInterest.AddRange(camps);
   var spawner=new GameObject("Sanctuary sentinels").AddComponent<EnemySpawner>();spawner.transform.SetParent(objectsRoot,false);spawner.SetupField(HuntingGrounds.Get(MapId),HuntingGrounds.PackPoints(camps));
   AddTreeFades();ApplySurfaceComposition();BuildMinimap();
  }
 }
}
