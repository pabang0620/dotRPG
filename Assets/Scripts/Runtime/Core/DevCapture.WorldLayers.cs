using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        static readonly string[] LayerCaves = { "hollow_descent", "hollow_roots", "hollow_fungal", "hollow_depths" };

        IEnumerator WorldLayersRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "지하세계 탐사자");
            yield return Wait(1.5f);
            Game.Session.Progression.SetFromServer(40, 0);
            Game.Player.HealFull();
            AudioListener.volume = 0;
            Game.Audio?.SetVolumes(0, 0);
            Game.Flow.TravelTo(MapRegistry.Undergate, true);
            yield return Wait(1);
            while (Game.Flow.IsTransitioning) yield return null;
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-batchmode") < 0 && Array.IndexOf(args, "-worldLayersVerify") < 0 && Array.IndexOf(args,"-worldLayersArtOnly")<0 && Array.IndexOf(args,"-worldLayersSceneryOnly")<0)
            {
                Game.Player.Input = new LocalInput();
                GameEvents.RaiseToast("뿌리샘 마을 · 중앙 계단: Lv.40+ 지하세계 / 지도: 지상·지하 전환");
                yield break;
            }
            dgnPassed = dgnFailed = 0;
            Game.Player.Health.SetInvulnerable(3600);
            var input = new ScriptedInput();
            Game.Player.Input = input;
            if(Array.IndexOf(args,"-worldLayersArtOnly")>=0){yield return WorldLayersArtOnly();yield break;}
            if(Array.IndexOf(args,"-worldLayersSceneryOnly")>=0){yield return WorldLayersSceneryOnly(input);yield break;}
            var surfaceTowns = new[] { MapRegistry.Village, MapRegistry.Canyon, MapRegistry.Winter, MapRegistry.Sanctum };
            DCheck("four surface regions", WorldRoutes.Regions.Length == 4);
            DCheck("central village remains surface", MapRegistry.Get(MapRegistry.Undergate).worldLayer == WorldLayer.Surface);
            DCheck("four persistent underground maps", LayerCaves.All(id => MapRegistry.Get(id)?.worldLayer == WorldLayer.Underground));
            foreach (var region in WorldRoutes.Regions)
            {
                DCheck(region[0] + " four hunting fields", HuntingGrounds.All.Count(z => z.village == region[0]) == 4);
                DCheck(region[0] + " entry split", WorldRoutes.Neighbors(region[1]).OrderBy(s => s).SequenceEqual(new[] { region[0], region[2], region[3] }.OrderBy(s => s)));
                foreach (int branch in new[] { 2, 3 })
                    DCheck(region[branch] + " branch merge", WorldRoutes.Neighbors(region[branch]).OrderBy(s => s).SequenceEqual(new[] { region[1], region[4] }.OrderBy(s => s)));
                DCheck(region[4] + " connects next town", WorldRoutes.Neighbors(region[4]).Contains(region[5]));
                DCheck(region[0] + " central town path", WorldRoutes.Neighbors(region[0]).Contains(MapRegistry.Undergate));
            }
            DCheck("surface ring closes", MapRegistry.Get("sanctum_court").nextMap == MapRegistry.Village && MapRegistry.Get(MapRegistry.Village).previousMap == "sanctum_court");
            foreach (var info in MapRegistry.All)
            {
                foreach (string next in WorldRoutes.Neighbors(info.id))
                    DCheck(info.id + " reciprocal route " + next, MapRegistry.Get(next) != null && WorldRoutes.Neighbors(next).Contains(info.id));
            }
            DCheck("B1 single descent", WorldRoutes.Neighbors(LayerCaves[0]).OrderBy(s => s).SequenceEqual(new[] { MapRegistry.Undergate, LayerCaves[1] }.OrderBy(s => s)));
            DCheck("B4 terminal chamber", WorldRoutes.Neighbors(LayerCaves[3]).OrderBy(s => s).SequenceEqual(new[] { LayerCaves[2] }.OrderBy(s => s)));
            DCheck("underground depths", LayerCaves.Select(id => MapRegistry.Get(id).depth).SequenceEqual(new[] { 1, 2, 3, 4 }));
            WorldLayerArtChecks();

            // Check the edited maps' real colliders and spawn/arrival positions. Older fields'
            // geometry is unchanged; their complete reciprocal graph is checked above.
            var physicalMaps = surfaceTowns.Concat(new[] { MapRegistry.Undergate, "sanctum_court" }).Concat(LayerCaves).ToArray();
            var fieldExport = new List<object>();
            foreach (string id in physicalMaps)
            {
                var previousScenes = Game.World.GetComponentsInChildren<UnderworldCompositionScene>();
                var previousArt = previousScenes.SelectMany(s => s.Layers.Concat(s.ForegroundRenderers)).Where(r => r != null && r.sprite != null)
                    .SelectMany(r => new UnityEngine.Object[] { r.sprite, r.sprite.texture }).Distinct().ToArray();
                Game.World.Load(id);
                Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
                yield return Wait(.25f);
                Physics2D.SyncTransforms();
                DCheck(id + " previous cave images released", previousScenes.All(s => s == null) && previousArt.All(r => r == null));
                var reachable = LayerReachable(Game.World.PlayerSpawn);
                DCheck(id + " free spawn", Game.World.IsFree(Game.World.PlayerSpawn));
                DCheck(id + " connected floor", reachable.Count > 20);
                foreach (string next in WorldRoutes.Neighbors(id))
                {
                    var at = Game.World.ArrivalFrom(next, out _);
                    bool portalExists = Game.World.PortalTowards(next, out var portal);
                    DCheck(id + " actual portal " + next, portalExists);
                    DCheck(id + " safe arrival " + next, Game.World.IsFree(at) && reachable.Contains(Vector2Int.FloorToInt(at)));
                    DCheck(id + " no immediate bounce " + next, portalExists && Vector2.Distance(at, portal) > 1.3f);
                }
                var bounds = Game.World.Bounds;
                if (id == MapRegistry.Undergate)
                    fieldExport.Add(HRow(("id", id), ("width", bounds.width), ("height", bounds.height), ("fieldSpawns", new List<object>())));
                DCheck(id + " outside bounds blocked", !Game.World.IsFree(new Vector2(bounds.xMin - .5f, bounds.center.y)) && !Game.World.IsFree(new Vector2(bounds.xMax + .5f, bounds.center.y)));
                if (LayerCaves.Contains(id))
                {
                    var composition = Game.World.GetComponentInChildren<UnderworldCompositionScene>();
                    DCheck(id + " HD composition active", composition != null && composition.PixelsPerUnit == 96 && composition.UsesHighResolution);
                    if (composition != null) WorldLayerCompositionChecks(id, composition);
                    var mobs = EnemyController.Active.Where(e => e != null && e.isActiveAndEnabled && !e.IsDead && !e.Def.boss && !e.Def.raid).ToArray();
                    var zone = HuntingGrounds.Get(id);
                    fieldExport.Add(HRow(("id", id), ("width", bounds.width), ("height", bounds.height),
                        ("fieldSpawns", mobs.GroupBy(e => e.Def.id).Select(g => (object)HRow(("monsterId", g.Key), ("points", g.Count()),
                            ("level", zone.monsterLevel), ("xp", zone.KillXp), ("respawnSeconds", HuntingGrounds.RespawnSeconds))).ToList())));
                    DCheck(id + " repeatable field monsters", mobs.Length > 0);
                    DCheck(id + " three dedicated underground species",mobs.Select(m=>m.Def.id).Distinct().OrderBy(s=>s).SequenceEqual(UnderworldMonsterArt.Ids.OrderBy(s=>s)));
                    DCheck(id + " new monster visuals retain their own look",mobs.All(m=>m.Def.look.id==m.Def.id&&UnderworldMonsterArt.Supports(m.Def.look.id)));
                    DCheck(id + " reachable camps", mobs.All(e => reachable.Contains(Vector2Int.FloorToInt(e.Position))));
                    var rows = HuntingGrounds.Layout(id).Split('\n').Select(s => s.TrimEnd('\r')).Where(s => s.Length > 0 && !s.StartsWith("//")).ToArray();
                    int probes = 0, leaks = 0;
                    for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++)
                        if (rows[y][x] == '~' || rows[y][x] == 'W')
                        {
                            probes++;
                            if (Game.World.IsFree(new Vector2(x + .5f, rows.Length - y - .5f))) leaks++;
                        }
                    DCheck(id + $" blocked water/rock ({probes} probes)", probes > 0 && leaks == 0);
                    yield return LayerProjectileTerrain(id,rows);
                    File.WriteAllText(Path.Combine(folder, id + ".txt"), HuntingGrounds.Layout(id));
                    // Actual movement to a distant connected point, without portals intercepting it.
                    var portalNodes = Game.World.ObjectsRoot.GetComponentsInChildren<MapPortal>();
                    var target = reachable.Select(v => (Vector2)v + Vector2.one * .5f)
                        .Where(v => portalNodes.All(p => Vector2.Distance(p.transform.position, v) > 3))
                        .OrderByDescending(v => Vector2.Distance(v, Game.Player.Position)).FirstOrDefault();
                    var disabled = new List<Collider2D>();
                    foreach (var enemy in mobs)
                    {
                        enemy.Stun(90);
                        foreach (var col in enemy.GetComponentsInChildren<Collider2D>()) if (col.enabled) { disabled.Add(col); col.enabled = false; }
                    }
                    float until = Time.time + 50;
                    while (Vector2.Distance(Game.Player.Position, target) > .35f && Time.time < until)
                    {
                        input.Move = GridPath.Steer(Game.Player.Position, target);
                        yield return null;
                    }
                    input.Move = Vector2.zero;
                    DCheck(id + " actual walking traversal", Game.World.MapId == id && Vector2.Distance(Game.Player.Position, target) < .5f);
                    foreach (var col in disabled) if (col != null) col.enabled = true;
                    Vector2 scenic=id=="hollow_roots"?new Vector2(27,32):id=="hollow_fungal"?new Vector2(31,18):id=="hollow_depths"?new Vector2(28,34):new Vector2(28,22);
                    Game.Player.Place(MonFree(scenic),Facing.Down);
                    Game.Camera.SetTarget(Game.Player.transform, true);
                    yield return Wait(.15f);
                    WorldLayerShot(id + "-gameplay");
                }
                if (id == MapRegistry.Undergate || LayerCaves.Contains(id))
                    RenderRegion(Path.Combine(folder, id + "-overview.png"), Game.World.Bounds, 24);
            }

            File.WriteAllText(Path.Combine(folder, "field-export.json"), MiniJson.Write(fieldExport));
            // Exercise both directions of every new inter-world/hub edge through MapPortal itself.
            var pairs = new List<(string from, string to)>();
            foreach (string town in surfaceTowns) { pairs.Add((town, MapRegistry.Undergate)); pairs.Add((MapRegistry.Undergate, town)); }
            pairs.Add(("sanctum_court", MapRegistry.Village)); pairs.Add((MapRegistry.Village, "sanctum_court"));
            foreach (string id in new[] { MapRegistry.Undergate }.Concat(LayerCaves))
                foreach (string next in WorldRoutes.Neighbors(id))
                    if (LayerCaves.Contains(id) || LayerCaves.Contains(next)) pairs.Add((id, next));
            foreach (var pair in pairs.Distinct())
            {
                Game.Flow.TravelTo(pair.from, true);
                yield return Wait(.7f);
                while (Game.Flow.IsTransitioning) yield return null;
                input.Move = Vector2.zero;
                bool exists = Game.World.PortalTowards(pair.to, out var portal);
                DCheck(pair.from + " trigger exists " + pair.to, exists);
                if (!exists) continue;
                Game.Player.Place(portal, Facing.Down);
                yield return Wait(1);
                float deadline = Time.realtimeSinceStartup + 8;
                while (Game.Flow.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                DCheck(pair.from + " actual trigger -> " + pair.to, Game.World.MapId == pair.to);
                yield return Wait(.4f);
                DCheck(pair.to + " arrival stable", Game.World.MapId == pair.to && Game.World.IsFree(Game.Player.Position));
            }
            yield return WorldLayerMonsterCombat(input);
            Game.Flow.TravelTo(MapRegistry.Undergate, true);
            yield return Wait(.8f);
            while (Game.Flow.IsTransitioning) yield return null;
            var worldMap = Game.UI.WorldMap;
            Game.UI.Hud.ClearToasts();
            worldMap.Show();
            string liveMap = Game.World.MapId;
            Vector2 livePosition = Game.Player.Position;
            foreach (var layer in new[] { WorldLayer.Surface, WorldLayer.Underground })
            {
                worldMap.SelectWorld(layer);
                yield return Wait(.2f);
                DCheck(layer + " atlas visible", worldMap.AtlasVisible && worldMap.SelectedWorld == layer);
                DCheck(layer + " list filtered", worldMap.ListedRegionCount == MapRegistry.All.Count(m => m.worldLayer == layer));
                WorldLayerShot("world-atlas-" + layer.ToString().ToLowerInvariant());
            }
            worldMap.SelectMap("hollow_depths");
            yield return Wait(.2f);
            DCheck("B3 detail selected", worldMap.SelectedMap == "hollow_depths" && !worldMap.AtlasVisible);
            DCheck("map browsing never travels", Game.World.MapId == liveMap && Vector2.Distance(Game.Player.Position, livePosition) < .05f);
            WorldLayerShot("world-underground-detail");
            worldMap.Hide();
            DCheck("verification remains muted", AudioListener.volume == 0);
            Log($"WORLD LAYERS RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        void WorldLayerArtChecks()
        {
            UnderworldMonsterArt.ExportSheets(Path.Combine(folder,"monsters"));
            foreach(string species in UnderworldMonsterArt.Ids)
            {
                bool complete=true,transparent=true,aligned=true,grounded=true,consistent=true;
                var silhouettes=new HashSet<string>();float maxSpread=0;
                var reference=RegionalMonsterArt.Get(species,"down","idle0");
                for(int d=0;d<5;d++)
                {
                    float minCenter=float.MaxValue,maxCenter=float.MinValue;
                    int minBottom=int.MaxValue,maxBottom=int.MinValue;
                    for(int f=0;f<8;f++)
                    {
                        var sprite=RegionalMonsterArt.Get(species,UnderworldMonsterArt.Directions[d],UnderworldMonsterArt.Frames[f]);
                        complete &= sprite!=null&&sprite.rect.width==UnderworldMonsterArt.Width&&sprite.rect.height==UnderworldMonsterArt.Height&&sprite.texture.filterMode==FilterMode.Point;
                        consistent &= sprite!=null&&Mathf.Approximately(sprite.pixelsPerUnit,reference.pixelsPerUnit)&&sprite.pivot==reference.pivot;
                        var canvas=UnderworldMonsterArt.Draw(species,d,f);
                        for(int x=0;x<canvas.Width;x++)transparent &= canvas.Get(x,0).a==0&&canvas.Get(x,canvas.Height-1).a==0;
                        for(int y=0;y<canvas.Height;y++)transparent &= canvas.Get(0,y).a==0&&canvas.Get(canvas.Width-1,y).a==0;
                        int count=0,sumX=0,bottom=0;
                        for(int y=0;y<canvas.Height;y++)for(int x=0;x<canvas.Width;x++)if(canvas.Get(x,y).a>0){count++;sumX+=x;bottom=Mathf.Max(bottom,y);}
                        if(f!=6&&count>0){float center=sumX/(float)count;minCenter=Mathf.Min(minCenter,center);maxCenter=Mathf.Max(maxCenter,center);}
                        minBottom=Mathf.Min(minBottom,bottom);maxBottom=Mathf.Max(maxBottom,bottom);
                        if(f==0)silhouettes.Add(Convert.ToBase64String(canvas.Pixels.SelectMany(c=>new[]{c.r,c.g,c.b,c.a}).ToArray()));
                    }
                    maxSpread=Mathf.Max(maxSpread,maxCenter-minCenter);aligned &= maxCenter-minCenter<5;
                    grounded &= maxBottom-minBottom<=1;
                }
                DCheck(species+" 40 pixel animation frames",complete);
                DCheck(species+" five distinct direction poses",silhouettes.Count==5);
                DCheck(species+" transparent unclipped frame margin",transparent);
                DCheck(species+$" idle/walk/hurt body drift {maxSpread:0.00}px <5",aligned);
                DCheck(species+" common feet baseline within one pixel",grounded);
                DCheck(species+" constant scale and sprite pivot",consistent);
            }
        }

        IEnumerator WorldLayersArtOnly()
        {
            WorldLayerArtChecks();
            Game.Camera.SetTarget(Game.Player.transform,true);yield return Wait(.2f);
            RenderRegion(Path.Combine(folder,"undergate-overview.png"),Game.World.Bounds,24);
            WorldLayerShot("undergate-gameplay");
            foreach(string id in LayerCaves)
            {
                Game.World.Load(id);
                Vector2 scenic=id=="hollow_roots"?new Vector2(27,32):id=="hollow_fungal"?new Vector2(31,18):id=="hollow_depths"?new Vector2(28,34):new Vector2(28,22);
                Game.Player.Place(MonFree(scenic),Facing.Down);Game.Camera.SetTarget(Game.Player.transform,true);
                yield return Wait(.2f);
                var enemies=EnemyController.Active.Where(e=>e!=null&&!e.IsDead).ToArray();
                foreach(var enemy in enemies)HoldLayerPortrait(enemy);
                DCheck(id+" art review has three new species",enemies.Select(e=>e.Def.id).Distinct().Count()==3&&enemies.All(e=>UnderworldMonsterArt.Supports(e.Def.look.id)));
                DCheck(id+" scenic capture remains on playable floor",Game.World.IsFree(Game.Player.Position));
                RenderRegion(Path.Combine(folder,id+"-overview.png"),Game.World.Bounds,24);
                WorldLayerShot(id+"-gameplay");
            }
            MonClear();yield return null;
            MonSpot=FindArena(new Vector2(28,34));Game.Player.Place(MonSpot+Vector2.down,Facing.Up);
            for(int i=0;i<UnderworldMonsterArt.Ids.Length;i++)
            {
                var enemy=MonSpawn(UnderworldMonsterArt.Ids[i],new Vector2((i-1)*2,1.3f));
                enemy.MonsterFace(enemy.Position+Vector2.down);HoldLayerPortrait(enemy);
            }
            yield return null;
            RenderRegion(Path.Combine(folder,"underground-monsters-closeup.png"),new Rect(MonSpot.x-4,MonSpot.y-2,8,6),96);
            MonClear();yield return null;
            Game.Flow.TravelTo(MapRegistry.Undergate,true);yield return Wait(.7f);
            while(Game.Flow.IsTransitioning)yield return null;
            var worldMap=Game.UI.WorldMap;worldMap.Show();
            foreach(var layer in new[]{WorldLayer.Surface,WorldLayer.Underground})
            {
                worldMap.SelectWorld(layer);yield return Wait(.2f);
                DCheck(layer+" atlas in art review",worldMap.AtlasVisible&&worldMap.SelectedWorld==layer);
                WorldLayerShot("world-atlas-"+layer.ToString().ToLowerInvariant());
            }
            worldMap.Hide();DCheck("art verification remains muted",AudioListener.volume==0);
            Log($"WORLD LAYERS ART RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        static void HoldLayerPortrait(EnemyController enemy)
        {
            enemy.Behaviour?.Interrupt();enemy.MonsterHalt();enemy.MonsterAnim(CharacterAnim.Idle);
            var body=enemy.GetComponent<Rigidbody2D>();if(body!=null)body.SetVelocity(Vector2.zero);
            enemy.enabled=false;
        }

        IEnumerator WorldLayersSceneryOnly(ScriptedInput input)
        {
            var propSources = new List<object>();
            foreach(string id in LayerCaves)
            {
                Game.World.Load(id);Game.Player.Place(Game.World.PlayerSpawn,Facing.Down);
                input.Move=Vector2.zero;yield return Wait(.2f);
                var root=Game.World.ObjectsRoot;
                var scene=root.GetComponentInChildren<UnderworldCompositionScene>();
                DCheck(id+" authored composition active, no legacy fallback",scene!=null);
                if(scene==null)continue;
                var scenery=scene.transform;
                WorldLayerCompositionChecks(id,scene);
                var propAudit = PropAudit(id);
                propSources.Add(PropUndergroundSource(id, scene, propAudit));
                File.WriteAllText(Path.Combine(folder, "prop-integration-underground.json"), MiniJson.Write(propSources));
                var ownedProps = PropOwnedResources(Game.World);
                var renderers=scenery.GetComponentsInChildren<SpriteRenderer>(true);
                DCheck(id+$" bounded composition renderers ({renderers.Length})",renderers.Length>=3&&renderers.Length<=64);
                DCheck(id+" composition creates no extra collision",scenery.GetComponentsInChildren<Collider2D>(true).Length==0);
                var ambience=UnityEngine.Object.FindObjectsByType<UnderworldDepthAmbience>(FindObjectsInactive.Exclude);
                DCheck(id+" one contained depth ambience",ambience.Length==1&&ambience[0].transform.IsChildOf(scenery));
                DCheck(id+" one contained water animation",scenery.GetComponentsInChildren<SanctumWater>(true).Length==1);
                int rendererCount=renderers.Length,objectCount=scenery.GetComponentsInChildren<Transform>(true).Length;
                int expectedOwnedCount=3+scene.ForegroundRenderers.Length;
                var ownedSprites=scene.Layers.Concat(scene.ForegroundRenderers).Where(r=>r!=null).Select(r=>r.sprite).ToArray();
                var ownedTextures=ownedSprites.Where(s=>s!=null).Select(s=>s.texture).ToArray();
                long textureBytes=ownedTextures.Where(t=>t!=null).Distinct().Sum(t=>(long)t.width*t.height*4);
                Log($"COMPOSITION {id}: source={scene.SourceWidth}x{scene.SourceHeight}, raster={scene.RasterWidth}x{scene.RasterHeight}, ppu={scene.PixelsPerUnit}, renderers={rendererCount}, focus={scene.FocusWorld}, textureRGBA32PayloadMiB={textureBytes/1048576f:F2}, managedHeapMiB={GC.GetTotalMemory(false)/1048576f:F2}");

                // Combat AI and bodies are held only in this isolated QA session so movement
                // failures describe terrain rather than an enemy standing on a required path.
                var mobs=EnemyController.Active.Where(e=>e!=null&&!e.IsDead).ToArray();
                string combatSignature=WorldLayerCombatSignature(mobs);
                int expectedLevel=new[]{42,47,47,53}[Array.IndexOf(LayerCaves,id)];
                DCheck(id+" original three underground species retained",mobs.Select(e=>e.Def.id).Distinct().OrderBy(s=>s).SequenceEqual(UnderworldMonsterArt.Ids.OrderBy(s=>s)));
                DCheck(id+" original underground monster level retained",mobs.Length>0&&mobs.All(e=>e.Level==expectedLevel));
                foreach(var enemy in mobs)
                {
                    HoldLayerPortrait(enemy);
                    foreach(var collider in enemy.GetComponentsInChildren<Collider2D>())collider.enabled=false;
                }
                Physics2D.SyncTransforms();GridPath.Invalidate();
                var reachable=LayerReachable(Game.World.PlayerSpawn);
                DCheck(id+" safe connected spawn",reachable.Contains(Vector2Int.FloorToInt(Game.World.PlayerSpawn))&&Game.World.IsFree(Game.World.PlayerSpawn));
                var rows=HuntingGrounds.Layout(id).Split('\n').Select(s=>s.TrimEnd('\r')).Where(s=>s.Length>0&&!s.StartsWith("//")).ToArray();
                var camps=new List<Vector2>();var stairs=new List<Vector2>();var disconnected=new List<Vector2>();
                int standable=0,tested=0,leaks=0;
                for(int y=0;y<rows.Length;y++)for(int x=0;x<rows[y].Length;x++)
                {
                    char kind=rows[y][x];var at=new Vector2(x+.5f,rows.Length-y-.5f);
                    bool free=Game.World.IsFree(at);
                    if(kind=='W'||kind=='~'){tested++;if(free)leaks++;continue;}
                    if(kind=='k')camps.Add(at);
                    if(kind=='=')stairs.Add(at);
                    if(free){standable++;if(!reachable.Contains(Vector2Int.FloorToInt(at)))disconnected.Add(at);}
                }
                DCheck(id+$" all standable tile centres connected ({standable})",standable>0&&disconnected.Count==0);
                if(disconnected.Count>0)Log("Disconnected floor: "+string.Join(", ",disconnected.Take(12)));
                DCheck(id+" authored transition cells, if present, remain walkable",stairs.All(p=>Game.World.IsFree(p)&&reachable.Contains(Vector2Int.FloorToInt(p))));
                DCheck(id+" all 18 encounter camps retained",camps.Count==18);
                var packPoints=HuntingGrounds.PackPoints(camps);
                var blockedPacks=packPoints.Where(p=>!Game.World.IsFree(p)||!reachable.Contains(Vector2Int.FloorToInt(p))).ToArray();
                DCheck(id+" encounter pack footprints fit connected floor",packPoints.Count==camps.Count*HuntingGrounds.PackSize&&blockedPacks.Length==0);
                if(blockedPacks.Length>0)Log("Blocked pack points: "+string.Join(", ",blockedPacks.Take(12)));
                DCheck(id+" camps preserve quiet spawn clearance",camps.All(p=>Vector2.Distance(p,Game.World.PlayerSpawn)>=5));
                DCheck(id+" actual spawned monsters stand on reachable ground",mobs.All(e=>Game.World.IsFree(e.Position)&&reachable.Contains(Vector2Int.FloorToInt(e.Position))));
                DCheck(id+$" water and rock remain blocked ({tested} probes)",tested>0&&leaks==0);
                WorldLayerCompositionFloorCheck(id,scene,reachable);
                File.WriteAllText(Path.Combine(folder,id+".txt"),HuntingGrounds.Layout(id));
                var arrivals=new List<Vector2>();
                foreach(string next in WorldRoutes.Neighbors(id))
                {
                    bool hasPortal=Game.World.PortalTowards(next,out var portal);
                    var arrival=Game.World.ArrivalFrom(next,out _);
                    arrivals.Add(arrival);
                    DCheck(id+" scenery exit exists "+next,hasPortal);
                    DCheck(id+" scenery exit reachable "+next,Game.World.IsFree(arrival)&&reachable.Contains(Vector2Int.FloorToInt(arrival)));
                    DCheck(id+" scenery arrival avoids portal bounce "+next,hasPortal&&Vector2.Distance(arrival,portal)>1.3f);
                    // Check the centre and both shoulders of the inner part of each exit approach.
                    var direction=(portal-arrival).normalized;var side=new Vector2(-direction.y,direction.x)*.32f;
                    bool shoulders=hasPortal;
                    for(int step=0;step<=4;step++)
                    {
                        var at=arrival+direction*(step*.18f);
                        shoulders &= Game.World.IsFree(at)&&Game.World.IsFree(at+side)&&Game.World.IsFree(at-side);
                    }
                    DCheck(id+" clear cardinal exit approach "+next,shoulders);
                }
                var bounds=Game.World.Bounds;
                DCheck(id+" all four outer bounds blocked",new[]{new Vector2(bounds.xMin-.5f,bounds.center.y),new Vector2(bounds.xMax+.5f,bounds.center.y),new Vector2(bounds.center.x,bounds.yMin-.5f),new Vector2(bounds.center.x,bounds.yMax+.5f)}.All(p=>!Game.World.IsFree(p)));
                yield return LayerProjectileTerrain(id,rows);

                // A real player crosses between widely separated exit arrivals on the same floor.
                Vector2 from=arrivals.Count>0?arrivals[0]:Game.World.PlayerSpawn;
                Vector2 to=arrivals.OrderByDescending(p=>Vector2.Distance(p,from)).FirstOrDefault();
                Game.Player.Place(from,Facing.Down);Game.Camera.SetTarget(Game.Player.transform,true);
                float deadline=Time.time+65;
                while(Vector2.Distance(Game.Player.Position,to)>.35f&&Time.time<deadline&&Game.World.MapId==id)
                {
                    input.Move=GridPath.Steer(Game.Player.Position,to);yield return null;
                }
                input.Move=Vector2.zero;
                DCheck(id+" actual walk between exits after scenery",Game.World.MapId==id&&Vector2.Distance(Game.Player.Position,to)<.5f);

                Vector2 focus=scene.FocusWorld;
                DCheck(id+" composition focal point fits actual player collision",Game.World.IsFree(focus)&&reachable.Contains(Vector2Int.FloorToInt(focus)));
                if(!Game.World.IsFree(focus))focus=MonFree(focus);
                Game.Player.Place(focus,Facing.Up);Game.Camera.SetTarget(Game.Player.transform,true);
                yield return Wait(.2f);
                DCheck(id+" focal player stands on walkable floor",Game.World.IsFree(focus));
                WorldLayerSceneryCapture(id,bounds,focus,rows);
                if(id=="hollow_fungal")yield return WorldLayerFungalCorner(id,reachable,input);
                if(id=="hollow_depths")yield return WorldLayerForegroundCapture(id,scene,reachable,input);

                // Rebuild the same map after the first capture: old objects/components must be
                // gone, and deterministic decoration budgets must not grow with repeated visits.
                Game.World.Load(id);Game.Player.Place(Game.World.PlayerSpawn,Facing.Down);
                yield return Wait(.2f);
                var again=Game.World.ObjectsRoot.GetComponentInChildren<UnderworldCompositionScene>();
                DCheck(id+" previous scenery and world root destroyed",scenery==null&&root==null);
                DCheck(id+" repeat-load scenery count stable",again!=null&&again.GetComponentsInChildren<SpriteRenderer>(true).Length==rendererCount&&again.GetComponentsInChildren<Transform>(true).Length==objectCount);
                DCheck(id+" prior composition sprites and textures released",ownedSprites.Length==expectedOwnedCount&&ownedTextures.Length==expectedOwnedCount&&ownedSprites.All(s=>s==null)&&ownedTextures.All(t=>t==null));
                DCheck(id+" prior prop resources released",ownedProps.All(p=>p==null));
                var active=UnityEngine.Object.FindObjectsByType<UnderworldDepthAmbience>(FindObjectsInactive.Exclude);
                DCheck(id+" repeat-load ambience not duplicated",again!=null&&active.Length==1&&active[0].transform.IsChildOf(again.transform));
                DCheck(id+" composition does not change monster combat values",WorldLayerCombatSignature(EnemyController.Active.Where(e=>e!=null&&!e.IsDead))==combatSignature);
            }
            Game.Flow.TravelTo(MapRegistry.Undergate,true);yield return Wait(.7f);
            while(Game.Flow.IsTransitioning)yield return null;
            DCheck("underground composition removed on surface return",UnityEngine.Object.FindObjectsByType<UnderworldCompositionScene>(FindObjectsInactive.Exclude).Length==0);
            DCheck("underground ambience removed on surface return",UnityEngine.Object.FindObjectsByType<UnderworldDepthAmbience>(FindObjectsInactive.Exclude).Length==0);
            var map=Game.UI.WorldMap;map.Show();map.SelectWorld(WorldLayer.Underground);yield return Wait(.2f);
            WorldLayerShot("world-atlas-underground");map.Hide();
            DCheck("scenery verification remains muted",AudioListener.volume==0);
            Log($"WORLD LAYERS SCENERY RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }

        static string WorldLayerCombatSignature(IEnumerable<EnemyController> enemies)
            =>string.Join("|",enemies.Select(e=>$"{e.Def.id}:{e.Level}:{e.Health.Max}:{e.Stats.attackDamage}").OrderBy(s=>s));

        void WorldLayerCompositionChecks(string id,UnderworldCompositionScene scene)
        {
            float ppu=scene.PixelsPerUnit;
            bool hd=Path.GetFileNameWithoutExtension(scene.SourcePath??"").EndsWith("_hd",StringComparison.OrdinalIgnoreCase);
            DCheck(id+" source artwork exists",!string.IsNullOrEmpty(scene.SourcePath)&&MapArtCache.Get(scene.SourcePath)!=null&&scene.SourceWidth>0&&scene.SourceHeight>0);
            DCheck(id+" exactly three registered artwork layers",scene.LayerCount==3&&scene.Layers.All(r=>r!=null&&r.sprite!=null));
            DCheck(id+" source density preserves the same 56 by 48 world",Mathf.Approximately(ppu,hd?96:32)&&Game.World.Bounds==new Rect(0,0,56,48)&&scene.WorldBounds==Game.World.Bounds&&scene.RasterWidth==Mathf.RoundToInt(Game.World.Bounds.width*ppu)&&scene.RasterHeight==Mathf.RoundToInt(Game.World.Bounds.height*ppu));
            // [MAP ART] -1 = compressed artwork without a CPU copy: pixel partition checks are skipped.
            if(scene.UncoveredPixels>=0)DCheck(id+" layer masks cover every source pixel once",scene.UncoveredPixels==0&&scene.OverlappingPixels==0&&scene.MismatchedPixels==0&&scene.LayerPixelCounts.Length==3&&scene.LayerPixelCounts.All(n=>n>0)&&scene.LayerPixelCounts.Sum()==scene.RasterOpaquePixelCount);
            if(scene.UncoveredPixels>=0)DCheck(id+" three partition masks cover the complete raster",scene.LayerRasterPixelCounts.Length==3&&scene.LayerRasterPixelCounts.Sum()==scene.RasterWidth*scene.RasterHeight);
            DCheck(id+" vertical masonry never assigned over walkable ground",scene.FaceOnWalkablePixels==0);
            var b=Game.World.Bounds;
            bool registered=scene.LayerCount==3;
            foreach(var layer in scene.Layers)
            {
                if(layer==null||layer.sprite==null){registered=false;continue;}
                var s=layer.sprite;var area=layer.bounds;
                registered &= s.texture.filterMode==FilterMode.Point&&s.texture.isReadable&&Mathf.Approximately(s.pixelsPerUnit,ppu)&&s.pivot==Vector2.zero;
                registered &= layer.transform.lossyScale==Vector3.one&&layer.transform.position==Vector3.zero&&layer.transform.rotation==Quaternion.identity;
                registered &= Mathf.Abs(area.min.x-b.xMin)<.01f&&Mathf.Abs(area.min.y-b.yMin)<.01f&&Mathf.Abs(area.size.x-b.width)<.01f&&Mathf.Abs(area.size.y-b.height)<.01f;
            }
            DCheck(id+" same origin scale bounds and sharp pixels on all layers",registered);
            DCheck(id+" deep scenery masonry and floor have distinct ascending sort",scene.LayerCount==3&&scene.Layers.All(r=>r!=null)&&scene.Layers[0].sortingOrder<scene.Layers[1].sortingOrder&&scene.Layers[1].sortingOrder<scene.Layers[2].sortingOrder&&scene.Layers[2].sortingOrder<YSort.OrderFor(0));
            DCheck(id+" no legacy texture plate underneath new composition",Game.World.ObjectsRoot.Find("Underworld scenery")==null);
            // These intentional column overlays reproduce the same source silhouette above
            // actors. They are additional to, not members of, the three exclusive base masks.
            var foreground=scene.ForegroundRenderers;
            DCheck(id+" intentional foreground column overlays",foreground.Length==(id=="hollow_depths"?7:0));
            if(foreground.Length==0)return;
            // Original source-coordinate anchors are frozen independently of replacement
            // image dimensions and raster density. Only their pixel sampling may change.
            var originalFeet=new[]{new Vector2(420,401),new Vector2(588,540),new Vector2(856,515),
                new Vector2(363,782),new Vector2(334,346),new Vector2(634,410),new Vector2(692,686)};
            bool crops=true,footings=true,sorting=true;
            for(int i=0;i<foreground.Length;i++)
            {
                var column=foreground[i];
                if(column==null||column.sprite==null){crops=footings=sorting=false;continue;}
                var s=column.sprite;var area=column.bounds;var foot=(Vector2)column.transform.position;
                crops &= s.texture.filterMode==FilterMode.Point&&s.texture.isReadable&&Mathf.Approximately(s.pixelsPerUnit,ppu);
                crops &= column.transform.lossyScale==Vector3.one&&column.transform.rotation==Quaternion.identity;
                crops &= area.size.x<b.width*.25f&&area.size.y<b.height*.5f;
                crops &= area.min.x>=b.xMin-.01f&&area.max.x<=b.xMax+.01f&&area.min.y>=b.yMin-.01f&&area.max.y<=b.yMax+.01f;
                footings &= i<originalFeet.Length&&Vector2.Distance(foot,new Vector2(originalFeet[i].x/1355f*56,(1-originalFeet[i].y/1161f)*48))<.002f&&!Game.World.IsFree(foot);
                sorting &= column.sortingOrder==YSort.OrderFor(foot.y)&&column.GetComponent<TreeFade>()!=null;
            }
            DCheck(id+" column crops retain sharp registered bounded pixels",crops);
            DCheck(id+" seven original column anchors remain fixed on blocked terrain",footings);
            DCheck(id+" foreground columns use foot depth and occlusion fading",sorting);
        }

        IEnumerator WorldLayerForegroundCapture(string id,UnderworldCompositionScene scene,HashSet<Vector2Int> reachable,ScriptedInput input)
        {
            var body=Game.Player.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
            SpriteRenderer chosen=null;Vector2 behind=default,front=default;
            foreach(var column in scene.ForegroundRenderers)
            {
                if(column==null||column.sprite==null||column.GetComponent<TreeFade>()==null)continue;
                var foot=(Vector2)column.transform.position;
                bool foundBehind=false,foundFront=false;Vector2 a=default,b=default;
                // Find real safe floor, rather than putting the player inside a pillar just
                // to demonstrate fading. Body sampling mirrors TreeFade's 3-by-5 stencil.
                for(float dy=.5f;dy<=3f&&!foundBehind;dy+=.25f)
                    foreach(float dx in new[]{0f,-.25f,.25f,-.5f,.5f,-.75f,.75f})
                    {
                        var p=foot+new Vector2(dx,dy);
                        if(!Game.World.IsFree(p)||!reachable.Contains(Vector2Int.FloorToInt(p))||WorldLayerColumnCover(column,p)<3)continue;
                        a=p;foundBehind=true;break;
                    }
                for(float dy=.5f;dy<=3f&&!foundFront;dy+=.25f)
                    foreach(float dx in new[]{0f,-.25f,.25f,-.5f,.5f,-.75f,.75f})
                    {
                        var p=foot+new Vector2(dx,-dy);
                        if(!Game.World.IsFree(p)||!reachable.Contains(Vector2Int.FloorToInt(p)))continue;
                        b=p;foundFront=true;break;
                    }
                if(foundBehind&&foundFront){chosen=column;behind=a;front=b;break;}
            }
            DCheck(id+" foreground demonstration has reachable floor on both sides",chosen!=null&&body!=null);
            if(chosen==null||body==null)yield break;
            var fade=chosen.GetComponent<TreeFade>();input.Move=Vector2.zero;
            Game.Player.Place(behind,Facing.Down);Game.Camera.SetTarget(Game.Player.transform,true);
            yield return Wait(.25f);
            DCheck(id+" column in front of northern player fades on actual overlap",Game.World.IsFree(Game.Player.Position)&&chosen.sortingOrder>body.sortingOrder&&fade.Alpha<=TreeFade.FadedAlpha+.02f);
            Log($"FOREGROUND {chosen.name}: foot={chosen.transform.position}, behind={behind}, front={front}, behindAlpha={fade.Alpha:F3}");
            WorldLayerShot(id+"-column-behind");
            Game.Player.Place(front,Facing.Up);Game.Camera.SetTarget(Game.Player.transform,true);
            yield return Wait(.25f);
            DCheck(id+" southern player appears in front while column returns opaque",Game.World.IsFree(Game.Player.Position)&&body.sortingOrder>chosen.sortingOrder&&fade.Alpha>=.98f);
            WorldLayerShot(id+"-column-front");
        }

        static int WorldLayerColumnCover(SpriteRenderer column,Vector2 feet)
        {
            var s=column.sprite;var rect=s.rect;int covered=0;
            for(int row=0;row<5;row++)for(int col=0;col<3;col++)
            {
                var world=feet+new Vector2(-.22f+col*.22f,.12f+row*.22f);
                var p=column.transform.InverseTransformPoint(world);
                float x=(column.flipX?-p.x:p.x)*s.pixelsPerUnit+s.pivot.x;
                float y=(column.flipY?-p.y:p.y)*s.pixelsPerUnit+s.pivot.y;
                if(x>=0&&y>=0&&x<rect.width&&y<rect.height&&s.texture.GetPixel((int)(rect.x+x),(int)(rect.y+y)).a>=.5f)covered++;
            }
            return covered;
        }

        IEnumerator WorldLayerFungalCorner(string id,HashSet<Vector2Int> reachable,ScriptedInput input)
        {
            // Final artwork's inner southern rim bends down through (23.7,19.1) and
            // (25.1,19.2). The old mask incorrectly allowed feet near (24.5,19.5)
            // over the drop. Select the demonstration floor from the final mask.
            int variant=UnderworldCompositionNav.Variant(id);
            var drop=new Vector2(24.5f,19.5f);
            DCheck(id+" inner corner drop is blocked in authored mask and physics",
                UnderworldCompositionNav.At(variant,drop.x,drop.y)==0&&!Game.World.IsFree(drop));
            Vector2 start=default;bool found=false;
            for(float south=.75f;south<=2.5f;south+=.125f)
            {
                var p=drop+Vector2.down*south;
                if(!UnderworldCompositionNav.FootClear(variant,p.x,p.y+.5f)||!Game.World.IsFree(p)||!reachable.Contains(Vector2Int.FloorToInt(p)))continue;
                start=p;found=true;break;
            }
            DCheck(id+" inner corner demonstration starts on connected safe floor",found);
            if(!found)yield break;
            Game.Player.Place(start,Facing.Up);Game.Camera.SetTarget(Game.Player.transform,true);
            input.Move=Vector2.zero;yield return Wait(.2f);
            Log($"FUNGAL CORNER: safeFloor={start}, blockedDrop={drop}");
            WorldLayerShot(id+"-inner-corner-gameplay");
        }

        void WorldLayerCompositionFloorCheck(string id,UnderworldCompositionScene scene,HashSet<Vector2Int> reachable)
        {
            if(scene.LayerCount!=3||scene.Layers[2]?.sprite==null){DCheck(id+" playable floor has registered artwork",false);return;}
            var texture=scene.Layers[2].sprite.texture;var pixels=texture.GetPixels32();
            float ppu=scene.PixelsPerUnit;
            int missing=0;
            foreach(var p in reachable)
            {
                int x=Mathf.Clamp(Mathf.FloorToInt((p.x+.5f)*ppu),0,texture.width-1);
                int y=Mathf.Clamp(Mathf.FloorToInt((p.y+.5f)*ppu),0,texture.height-1);
                if(pixels[y*texture.width+x].a==0)missing++;
            }
            // Alpha registration cannot establish that the authored image depicts safe floor;
            // compare the separate collision overlay and gameplay images visually for that.
            DCheck(id+$" connected floor has registered nontransparent source pixels ({reachable.Count} points)",missing==0);
        }

        void WorldLayerSceneryCapture(string id,Rect bounds,Vector2 focus,string[] rows)
        {
            // Remove only world enemy health bars for the landscape plate; the gameplay
            // capture keeps the actual HUD and visible enemies, and restores bars immediately.
            // Held EnemyControllers unregister from Active, but their health bar components
            // remain enabled on the scene objects and are still rendered by the camera.
            var bars=UnityEngine.Object.FindObjectsByType<EnemyHealthBar>(FindObjectsInactive.Exclude)
                .Select(b=>b.transform.Find("HealthBar")).Where(t=>t!=null&&t.gameObject.activeSelf).ToArray();
            foreach(var bar in bars)bar.gameObject.SetActive(false);
            try
            {
                RenderRegion(Path.Combine(folder,id+"-overview.png"),bounds,24);
                float x=Mathf.Clamp(focus.x-10,bounds.xMin,bounds.xMax-20);
                float y=Mathf.Clamp(focus.y-5,bounds.yMin,bounds.yMax-14);
                RenderRegion(Path.Combine(folder,id+"-composition-detail.png"),new Rect(x,y,20,14),64);
                WorldLayerCollisionOverlay(id,bounds,rows);
            }
            finally{foreach(var bar in bars)if(bar!=null)bar.gameObject.SetActive(true);}
            WorldLayerShot(id+"-gameplay");
        }

        void WorldLayerCollisionOverlay(string id,Rect bounds,string[] rows)
        {
            // QA only: green is safe for the real player's feet circle; cyan/orange is
            // blocked water/stone. This overlay is never retained by normal gameplay.
            const int samples=4;int w=Mathf.RoundToInt(bounds.width*samples),h=Mathf.RoundToInt(bounds.height*samples);
            var pixels=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                var p=new Vector2(bounds.xMin+(x+.5f)/samples,bounds.yMin+(y+.5f)/samples);
                if(Game.World.IsFree(p))pixels[y*w+x]=new Color32(70,235,125,80);
                else
                {
                    int row=Mathf.Clamp(rows.Length-1-Mathf.FloorToInt(p.y),0,rows.Length-1);
                    int col=Mathf.Clamp(Mathf.FloorToInt(p.x),0,rows[row].Length-1);
                    pixels[y*w+x]=rows[row][col]=='~'?new Color32(35,180,255,125):new Color32(255,100,50,55);
                }
            }
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false){name="QA actual collision mask",filterMode=FilterMode.Point};
            texture.SetPixels32(pixels);texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,w,h),Vector2.zero,samples,0,SpriteMeshType.FullRect);
            var go=new GameObject("QA collision overlay");var renderer=go.AddComponent<SpriteRenderer>();
            go.transform.position=new Vector3(bounds.xMin,bounds.yMin,0);renderer.sprite=sprite;renderer.sortingOrder=32000;
            try{RenderRegion(Path.Combine(folder,id+"-collision-overlay.png"),bounds,24);}
            finally{renderer.enabled=false;Destroy(go);Destroy(sprite);Destroy(texture);}
        }

        IEnumerator WorldLayerMonsterCombat(ScriptedInput input)
        {
            Game.Flow.TravelTo("hollow_descent",true);
            yield return Wait(.6f);
            while(Game.Flow.IsTransitioning)yield return null;
            input.Move=Vector2.zero;Game.Player.Input=input;
            MonClear();yield return null;
            MonSpot=FindArena(new Vector2(27.5f,24.5f));
            var player=Game.Player;
            int oldMax=player.Health.Max,oldHp=player.Health.Current;
            // Isolated test save: a large health pool permits checking actual damage receipts.
            player.Health.Init(20000,20000,.5f);
            player.Place(MonSpot,Facing.Right);Game.Camera.SetTarget(player.transform,true);
            var scarab=MonSpawn("hollow_scarab",Vector2.right*4,5);
            var charge=scarab.Behaviour as ChargerBehaviour;
            int received=0;
            Action<DamageInfo> onCharge=hit=>{if(hit.attacker==scarab.gameObject)received++;};
            player.Health.Damaged+=onCharge;
            yield return null;
            Vector2 start=scarab.Position;
            charge.DevCharge(Vector2.left);
            yield return Wait(.45f);
            bool line=Telegraph.Active.Any(t=>t!=null&&t.Owner==scarab&&t.Contains(player.Position));
            WorldLayerShot("monster-scarab-charge");
            float end=Time.time+6;
            while(charge.Busy&&Time.time<end)yield return null;
            player.Health.Damaged-=onCharge;
            DCheck("scarab warned charge moves and actually hits",line&&Vector2.Distance(start,scarab.Position)>2.5f&&charge.ChargeHits>0&&received>0);

            MonClear();yield return null;player.Place(MonSpot,Facing.Right);
            var guard=MonSpawn("hollow_guard",Vector2.right*3.5f,5);
            var shield=guard.Behaviour as ShieldGuardBehaviour;
            yield return Wait(.3f);
            int hp=guard.Health.Current;
            guard.TakeDamage(FromLocal(50,guard.Position+shield.GuardDirection*1.5f,0));
            int front=hp-guard.Health.Current;
            bool resisted=!guard.IsStaggered;
            yield return Wait(.3f);
            hp=guard.Health.Current;
            guard.TakeDamage(FromLocal(50,guard.Position-shield.GuardDirection*1.5f,0));
            int rear=hp-guard.Health.Current;
            DCheck($"guard actual frontal/rear damage {front}/{rear}",front==10&&rear==50&&resisted&&guard.IsStaggered);
            yield return Wait(.6f);
            player.Place(guard.Position+shield.GuardDirection*.8f,Facing.Left);
            int bashHits=0;Action<DamageInfo> onBash=hit=>{if(hit.attacker==guard.gameObject)bashHits++;};
            player.Health.Damaged+=onBash;
            end=Time.time+6;bool droppedGuard=false;
            while(Time.time<end&&bashHits==0){droppedGuard|=!shield.Guarding;yield return null;}
            player.Health.Damaged-=onBash;
            DCheck("guard lowers shield to land an actual bash",droppedGuard&&bashHits>0);
            WorldLayerShot("monster-guard-bash");

            MonClear();yield return null;player.Place(MonSpot,Facing.Right);
            var hexer=MonSpawn("hollow_hexer",Vector2.right*4.5f,5);
            var ranged=hexer.Behaviour as ArcherBehaviour;
            end=Time.time+9;
            bool aim=false,projectile=false,travelled=false;Vector2 boltAt=Vector2.zero;MonsterProjectile observed=null;
            while(Time.time<end&&(ranged.Shots==0||!travelled))
            {
                aim|=Telegraph.Active.Any(t=>t!=null&&t.Owner==hexer);
                var shot=MonsterProjectile.Active.FirstOrDefault(p=>p!=null&&p.Owner==hexer);
                if(shot!=null)
                {
                    projectile|=shot.GetComponent<SpriteRenderer>().sprite.texture.name=="hollow_hexer_bolt";
                    if(observed!=shot){observed=shot;boltAt=shot.transform.position;}
                    else travelled|=Vector2.Distance(boltAt,shot.transform.position)>.3f;
                }
                yield return null;
            }
            DCheck("hexer AI aims and launches its moving void projectile",aim&&ranged.Shots>0&&projectile&&travelled);
            WorldLayerShot("monster-hexer-projectile");
            MonClear();yield return null;
            DCheck("monster attack telegraphs and bolts clean up",!Telegraph.Active.Any(t=>t!=null&&!t.Cancelled&&!t.Resolved)&&MonsterProjectile.Active.Count==0);
            player.Health.Init(oldMax,oldHp,.5f);player.Health.SetInvulnerable(3600);
        }

        IEnumerator LayerProjectileTerrain(string id,string[] rows)
        {
            // Sample actual runtime colliders and projectiles, not just layout characters.
            // Interior 3x3 patches leave enough clearance from both banks and player bodies.
            int h=rows.Length,w=rows[0].Length;
            foreach(char kind in new[]{'~','W'})
            {
                Vector2 point=Vector2.zero;bool found=false;
                for(int y=2;y<h-2&&!found;y++)for(int x=2;x<w-2&&!found;x++)
                {
                    bool patch=true;
                    for(int yy=-1;yy<=1;yy++)for(int xx=-1;xx<=1;xx++)if(rows[h-1-(y+yy)][x+xx]!=kind)patch=false;
                    if(patch){point=new Vector2(x+.5f,y+.5f);found=true;}
                }
                DCheck(id+" projectile test patch "+kind,found);
                if(!found)continue;
                Physics2D.SyncTransforms();
                var colliders=Physics2D.OverlapCircleAll(point,.08f);
                DCheck(id+" distinct static terrain mask "+kind,colliders.Any(c=>c is CompositeCollider2D&&c.attachedRigidbody!=null&&c.attachedRigidbody.bodyType==RigidbodyType2D.Static&&(c.gameObject.name=="Water")== (kind=='~')));
                var shot=MonsterProjectile.Fire(null,"mon_bolt",point+Vector2.up*.4f,Vector2.right,1.5f,3,.05f,0,0,Color.clear);
                Vector2 start=shot.transform.position;
                yield return Wait(.3f);
                if(kind=='~')
                    DCheck(id+" water blocks feet but passes a moving bolt",!Game.World.IsFree(point)&&shot!=null&&Vector2.Distance(start,shot.transform.position)>.25f);
                else DCheck(id+" static composite rock stops a bolt",shot==null);
                if(shot!=null)Destroy(shot.gameObject);
                yield return null;
            }
        }

        void WorldLayerShot(string name)
        {
            // Capture only the test session; unrelated live chat is not part of an art review.
            var chats=UnityEngine.Object.FindObjectsByType<ChatView>(FindObjectsInactive.Exclude);
            foreach(var chat in chats)chat.gameObject.SetActive(false);
            try{Game.UI.Hud.ClearToasts();SanctumScreenshot(name);}
            finally{foreach(var chat in chats)if(chat!=null)chat.gameObject.SetActive(true);}
        }

        HashSet<Vector2Int> LayerReachable(Vector2 origin)
        {
            var seen = new HashSet<Vector2Int>();
            var tested = new HashSet<Vector2Int>();
            var pending = new Queue<Vector2Int>();
            var start = Vector2Int.FloorToInt(origin);
            if (!Game.World.IsFree((Vector2)start + Vector2.one * .5f)) return seen;
            seen.Add(start); tested.Add(start); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                foreach (var direction in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var next = at + direction;
                    if (!tested.Add(next) || !Game.World.IsFree((Vector2)next + Vector2.one * .5f)) continue;
                    seen.Add(next); pending.Enqueue(next);
                }
            }
            return seen;
        }
    }
}
