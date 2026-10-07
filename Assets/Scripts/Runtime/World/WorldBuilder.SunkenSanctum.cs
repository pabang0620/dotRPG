using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        SunkenSanctumShore sanctumShore;
        static Tile sanctumColliderTile;
        public SunkenSanctumShore SanctumShore => MapId == MapRegistry.Sanctum ? sanctumShore : null;

        // Composite tilemaps may request a sprite outline even for Grid collision. Keep this
        // sanctuary-only blank readable and FullRect rather than using the shared Tight sprite.
        static Tile SanctumColliderTile()
        {
            if (sanctumColliderTile != null) return sanctumColliderTile;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "Sanctum collision blank", filterMode = FilterMode.Point
            };
            texture.SetPixels32(new Color32[4]);
            texture.Apply(false, false);
            sanctumColliderTile = ScriptableObject.CreateInstance<Tile>();
            sanctumColliderTile.name = "Sanctum invisible solid";
            sanctumColliderTile.sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2),
                Vector2.one * .5f, 2, 0, SpriteMeshType.FullRect);
            sanctumColliderTile.colliderType = Tile.ColliderType.Grid;
            return sanctumColliderTile;
        }

        void BuildSunkenSanctum()
        {
            sanctumShore = new SunkenSanctumShore(cells, width, height);
            for (int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                char c=cells[x,y];var at=new Vector3Int(x,y,0);
                if(c=='W')cliffMap.SetTile(at,SanctumColliderTile());
                if(c=='P')PlayerSpawn=new Vector2(x+.5f,y+.5f);
                if(c=='<'||c=='>'){
                    var pos=new Vector2(x+.5f,y+.5f);
                    string target=WorldRoutes.Target(map,c);
                    MapPortal.Create(pos,target,objectsRoot);
                    if(!portalCells.ContainsKey(target))portalCells[target]=new System.Collections.Generic.List<Vector2>();
                    portalCells[target].Add(pos);
                }
            }
            // The visible land edge and foot collision use exactly the same subcell mask.
            waterMap.transform.localScale = Vector3.one / SunkenSanctumShore.SamplesPerTile;
            var positions = new List<Vector3Int>();
            for (int y = 0; y < sanctumShore.SampleHeight; y++) for (int x = 0; x < sanctumShore.SampleWidth; x++)
                if (sanctumShore.NeedsWaterCollision(x,y)) positions.Add(new Vector3Int(x,y,0));
            var solid = new TileBase[positions.Count];
            for (int i = 0; i < solid.Length; i++) solid[i] = SanctumColliderTile();
            waterMap.SetTiles(positions.ToArray(), solid);
            var body = waterMap.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = waterMap.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.vertexDistance = .001f;
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            var waterCollider = waterMap.GetComponent<TilemapCollider2D>();
            waterCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            // Resolve collision in the load frame, before an arrival or path probe can run.
            waterCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();
            cliffMap.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>().ProcessTilemapChanges();
            SunkenSanctumArt.Build(objectsRoot,cells,width,height,sanctumShore);
            CreateBoundaryWalls();
            PointsOfInterest.Add(PlayerSpawn);PointsOfInterest.Add(new Vector2(21,35));PointsOfInterest.Add(new Vector2(24,51));
            BuildMinimap();
        }
    }
}

