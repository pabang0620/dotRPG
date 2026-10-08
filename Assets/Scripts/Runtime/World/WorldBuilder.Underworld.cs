using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        void BuildUnderworld()
        {
            bool town = MapId == MapRegistry.Undergate;
            var contour=town?null:new UnderworldContour(cells,width,height,map.id);
            var camps = new List<Vector2>();
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                char c=cells[x,y]; var at=new Vector3Int(x,y,0); var p=new Vector2(x+.5f,y+.5f);
                if(town&&c=='W')cliffMap.SetTile(at,SanctumColliderTile());
                if(town&&c=='~')waterMap.SetTile(at,SanctumColliderTile());
                if(c=='P')PlayerSpawn=p;
                if(c=='k')camps.Add(p);
                if(WorldRoutes.Portal(c))
                {
                    string target=WorldRoutes.Target(map,c); if(string.IsNullOrEmpty(target))continue;
                    MapPortal.Create(p,target,objectsRoot);
                    if(!portalCells.TryGetValue(target,out var group))portalCells[target]=group=new List<Vector2>();
                    group.Add(p);
                    if(town&&c=='{')portalArrivals[target]=p+Vector2.down*2.4f;
                }
            }
            if(contour!=null)
            {
                // Keep the exact shared silhouette, but preserve Water's projectile/pass-over semantics.
                var rocks=new List<Vector3Int>();var water=new List<Vector3Int>();
                for(int y=0;y<contour.Rows;y++)for(int x=0;x<contour.Columns;x++)
                {
                    byte kind=contour.At(x,y);
                    if(kind==0)rocks.Add(new Vector3Int(x,y,0));
                    else if(kind==2)water.Add(new Vector3Int(x,y,0));
                }
                FillUnderworldCollider(cliffMap,rocks);
                FillUnderworldCollider(waterMap,water);
            }
            waterMap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
            cliffMap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
            if(contour!=null)
            {
                cliffMap.GetComponent<CompositeCollider2D>().GenerateGeometry();
                waterMap.GetComponent<CompositeCollider2D>().GenerateGeometry();
            }
            UnderworldArt.Build(objectsRoot,MapId,cells,width,height,contour);
            foreach(var pair in portalCells)
            {
                Vector2 p=Vector2.zero;foreach(var v in pair.Value)p+=v;p/=pair.Value.Count;
                UnderworldArt.Passage(objectsRoot,p,MapRegistry.Get(pair.Key).displayName,town&&pair.Key==map.downMap);
            }
            if(town)
            {
                // Existing full-size village architecture, with individually walkable entrances.
                string[] keys={"town_store","town_smithy","town_warehouse"};
                Vector2[] feet={new Vector2(10,29),new Vector2(42,25),new Vector2(10,13)};
                for(int i=0;i<keys.Length;i++)
                {
                    var d=VillageBuildingArt.Find(keys[i]);
                    var house=StaticProp("House",keys[i],feet[i],d.ColliderSize,d.ColliderOffset);
                    var sr=house.GetComponent<SpriteRenderer>();sr.color=new Color(.82f,.91f,.80f);
                    var door=ServiceDoor.Attach(house,MapRegistry.IndoorServices[i],MapRegistry.ServiceName(MapRegistry.IndoorServices[i])+" 들어가기");
                    door.transform.localPosition=d.Entrance;
                    TreeFade.Attach(house);
                }
                SpawnDungeonGuide();
                Game.Cutscenes?.OnWorldRebuilt();
            }
            else if(camps.Count>0)
            {
                var spawner=new GameObject("Underworld cave encounters").AddComponent<EnemySpawner>();
                spawner.transform.SetParent(objectsRoot,false);
                spawner.SetupField(HuntingGrounds.Get(MapId),HuntingGrounds.PackPoints(camps));
            }
            CreateBoundaryWalls();PointsOfInterest.Add(PlayerSpawn);PointsOfInterest.Add(new Vector2(width*.5f,height*.5f));
            ApplySharpMaterial();BuildMinimap();
        }

        static void FillUnderworldCollider(Tilemap tilemap,List<Vector3Int> positions)
        {
            tilemap.transform.localScale=Vector3.one/UnderworldContour.Scale;
            var tiles=new TileBase[positions.Count];
            for(int i=0;i<tiles.Length;i++)tiles[i]=SanctumColliderTile();
            tilemap.SetTiles(positions.ToArray(),tiles);
            var body=tilemap.gameObject.AddComponent<Rigidbody2D>();body.bodyType=RigidbodyType2D.Static;
            var composite=tilemap.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType=CompositeCollider2D.GeometryType.Polygons;composite.vertexDistance=.001f;
            composite.generationType=CompositeCollider2D.GenerationType.Manual;
            tilemap.GetComponent<TilemapCollider2D>().compositeOperation=Collider2D.CompositeOperation.Merge;
        }

        /// <summary>Local plaza passages preserve authored town silhouettes and existing save positions.</summary>
        void BuildWorldHubGates()
        {
            if(!string.IsNullOrEmpty(map.hubMap)) AddPlazaGate(map.hubMap,PlayerSpawn+new Vector2(5,1));
            // The old first village has no western edge exit; its returning route gets a signed plaza passage.
            if(MapId==MapRegistry.Village&&!string.IsNullOrEmpty(map.previousMap)&&!portalCells.ContainsKey(map.previousMap))
                AddPlazaGate(map.previousMap,PlayerSpawn+new Vector2(-6,-2));
        }
        void AddPlazaGate(string target,Vector2 desired)
        {
            if(portalCells.ContainsKey(target))return;
            Vector2 center=Vector2.zero;bool found=false;
            for(int r=0;r<14&&!found;r++)for(int y=-r;y<=r&&!found;y++)for(int x=-r;x<=r&&!found;x++)
            {
                if(Mathf.Max(Mathf.Abs(x),Mathf.Abs(y))!=r)continue;
                Vector2 p=new Vector2(Mathf.Floor(desired.x)+x+.5f,Mathf.Floor(desired.y)+y+.5f);
                if(Vector2.Distance(p,PlayerSpawn)<3)continue;
                bool clear=true;
                for(int dy=-3;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    if(!IsFree(p+new Vector2(dx,dy)))clear=false;
                foreach(var group in portalCells.Values)foreach(var q in group)if(Vector2.Distance(q,p)<4)clear=false;
                if(clear){center=p;found=true;}
            }
            if(!found){Debug.LogError("No safe plaza passage for "+MapId+" -> "+target);return;}
            MapPortal.Create(center,target,objectsRoot);
            portalCells[target]=new List<Vector2>{center};portalArrivals[target]=center+Vector2.down*2.4f;
            UnderworldArt.Passage(objectsRoot,center,MapRegistry.Get(target).displayName,false,true);
        }
    }
}
