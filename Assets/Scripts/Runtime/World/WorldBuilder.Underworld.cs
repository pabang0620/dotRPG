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
            ApplySharpMaterial();ApplySurfaceComposition();BuildMinimap();
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
            if(!string.IsNullOrEmpty(map.hubMap)) AddHubEdgeGate(map.hubMap);
            // The old first village has no western edge exit; its returning route gets a signed plaza passage.
            if(MapId==MapRegistry.Village&&!string.IsNullOrEmpty(map.previousMap)&&!portalCells.ContainsKey(map.previousMap))
                AddPlazaGate(map.previousMap,PlayerSpawn+new Vector2(-6,-2));
        }
        void AddHubEdgeGate(string target)
        {
            if (portalCells.ContainsKey(target)) return;
            var desired = WorldRoutes.HubApproach(MapId);
            var inward = WorldRoutes.HubInward(MapId);
            var tangent = new Vector2(-inward.y, inward.x);
            var visited = new HashSet<Vector2Int>(); var reachable = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            var start = Vector2Int.FloorToInt(PlayerSpawn); visited.Add(start); reachable.Add(start); queue.Enqueue(start);
            var directions = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var n = p + direction;
                    if (!visited.Add(n) || !IsFree((Vector2)n + Vector2.one * .5f)) continue;
                    reachable.Add(n); queue.Enqueue(n);
                }
            }
            Vector2 center = Vector2.zero; float best = float.PositiveInfinity;
            foreach (var cell in reachable)
            {
                Vector2 p = (Vector2)cell + Vector2.one * .5f;
                float score = (p - desired).sqrMagnitude;
                if (score >= best || score > 64) continue;
                bool clear = true;
                for (int across = -1; across <= 1; across++)
                    for (int depth = 0; depth <= 6; depth++)
                        if (!IsFree(p + tangent * across + inward * (depth * .5f))) clear = false;
                foreach (var group in portalCells.Values) foreach (var other in group)
                    if (Vector2.Distance(p, other) < 5) clear = false;
                if (!clear) continue;
                best = score; center = p;
            }
            if (float.IsPositiveInfinity(best)) { Debug.LogError("No reachable edge approach: " + MapId); return; }
            var points = portalCells[target] = new List<Vector2>();
            for (int n = -1; n <= 1; n++)
            {
                var p = center + tangent * n;
                MapPortal.Create(p, target, objectsRoot); points.Add(p);
            }
            portalArrivals[target] = center + inward * 2.4f;
            Decoration(inward == Vector2.down ? "arrow_up" : "arrow_left", center, -19990);
            if (MapId == MapRegistry.Sanctum)
            {
                // Recess the upper ruin into the north wall so the new side passage
                // remains a continuous floor, rather than slicing a hole through columns.
                var tower = System.Array.Find(objectsRoot.GetComponentsInChildren<SpriteRenderer>(), r => r.name == "tower_left" && Mathf.Abs(r.transform.position.y - 46) < .1f);
                if (tower != null)
                {
                    tower.transform.position = new Vector2(9.5f, 56.5f);
                    var scale = tower.transform.localScale; scale.y *= 13.5f / 24f; tower.transform.localScale = scale;
                    tower.sortingOrder = YSort.OrderFor(56.5f);
                }
            }

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
