using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace DotRPG {
 public partial class DevCapture {
  IEnumerator SanctumFieldsRun(){
   ApplyRequestedResolution();yield return Wait(1);Game.Config.autosave=false;Game.Flow.NewGame(CharacterClass.Warrior,"성소 사냥 탐사자");yield return Wait(1.5f);
   Game.Flow.TravelTo("sanctum_hall",true);yield return Wait(1);while(Game.Flow.IsTransitioning)yield return null;
   Game.Session.Progression.SetFromServer(40,0);Game.Player.HealFull();Game.Player.Health.SetInvulnerable(3600);Game.Audio?.SetVolumes(0,0);AudioListener.volume=0;
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"-batchmode")<0){Game.Player.Input=new LocalInput();GameEvents.RaiseToast("성소 사냥터 · 방향키 이동 / X 공격 / 지도에서 지역 보기");yield break;}
   dgnPassed=dgnFailed=0;Game.Player.Input=new ScriptedInput();
   foreach(string species in new[]{RegionalMonsterArt.Sanctum})foreach(string role in new[]{"","_guard","_thrower"})foreach(string dir in new[]{"down","downside","side","upside","up"})foreach(string pose in new[]{"idle0","idle1","walk0","walk1","walk2","walk3","attack","hurt"})DCheck(species+role+dir+pose,RegionalMonsterArt.Get(species+role,dir,pose)!=null);
   foreach(var z in HuntingGrounds.All.Where(z=>z.theme!=MapTheme.Underground)){
    Game.World.Load(z.id);Game.Player.Place(Game.World.PlayerSpawn,Facing.Right);yield return Wait(.2f);Physics2D.SyncTransforms();
    if(z.theme==MapTheme.SanctumField){
     var seen=new HashSet<Vector2Int>();var tested=new HashSet<Vector2Int>();var q=new Queue<Vector2Int>();var start=Vector2Int.FloorToInt(Game.Player.Position);seen.Add(start);tested.Add(start);q.Enqueue(start);
     while(q.Count>0){var v=q.Dequeue();foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var n=v+d;if(!tested.Add(n)||!Game.World.IsFree((Vector2)n+Vector2.one*.5f))continue;seen.Add(n);q.Enqueue(n);}}
     var mobs=EnemyController.Active.Where(e=>e!=null&&e.isActiveAndEnabled&&!e.IsDead).ToArray();
     DCheck(z.id+" free spawn",Game.World.IsFree(Game.Player.Position));DCheck(z.id+" reachable mobs",mobs.Length>20&&mobs.All(e=>seen.Contains(Vector2Int.FloorToInt(e.Position))));
     DCheck(z.id+" three statue roles",mobs.Select(e=>e.Def.look.id).Distinct().Count()==3&&mobs.All(e=>RegionalMonsterArt.Species(e.Def.look.id)==RegionalMonsterArt.Sanctum));
     DCheck(z.id+" retained XP",mobs.All(e=>e.Stats.xpReward==z.KillXp));
     foreach(var dest in WorldRoutes.Neighbors(z.id)){var pos=Game.World.ArrivalFrom(dest,out _);DCheck(z.id+" arrival "+dest,seen.Contains(Vector2Int.FloorToInt(pos)));}
     var lines=HuntingGrounds.Layout(z.id).Trim().Split('\n');int blocked=0;for(int y=0;y<lines.Length;y++)for(int x=0;x<lines[y].Length;x++)if(lines[y][x]=='~'||lines[y][x]=='W'){if(!Game.World.IsFree(new Vector2(x+.5f,lines.Length-y-.5f)))blocked++;}
     DCheck(z.id+" water and walls blocked",blocked==lines.Sum(l=>l.Count(c=>c=='~'||c=='W')));
     // Walk using the actual input/physics pathfinder; keep combat bodies still during this geometry probe.
     foreach(var e in mobs){e.Stun(90);foreach(var col in e.GetComponentsInChildren<Collider2D>())col.enabled=false;}
     var walking=new ScriptedInput();Game.Player.Input=walking;
     Vector2 destination=z.variant==0?new Vector2(30.5f,40.5f):z.variant==3?new Vector2(30.5f,40.5f):new Vector2(52.5f,24.5f);
     float until=Time.time+45;while(Vector2.Distance(Game.Player.Position,destination)>.4f&&Time.time<until){walking.Move=GridPath.Steer(Game.Player.Position,destination);yield return null;}walking.Move=Vector2.zero;
     DCheck(z.id+" actual terrain traversal",Vector2.Distance(Game.Player.Position,destination)<.5f);
     foreach(var e in mobs)foreach(var col in e.GetComponentsInChildren<Collider2D>())col.enabled=true;yield return Wait(3.5f);
     Game.Camera.SetTarget(Game.Player.transform,true);SanctumScreenshot(z.id+"-play");
    }else if(z.theme==MapTheme.Forest)DCheck(z.id+" decorative corner doors absent",!Game.World.ObjectsRoot.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Fantasy landmark")));
    else DCheck(z.id+" scenery placed",Game.World.ObjectsRoot.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("Fantasy landmark")));
    RenderRegion(Path.Combine(folder,z.id+"-overview.png"),Game.World.Bounds,24);
   }
   foreach(var pair in new[]{(MapRegistry.Sanctum,"sanctum_hall"),("sanctum_hall","sanctum_archive"),("sanctum_hall","sanctum_roots"),("sanctum_archive","sanctum_court"),("sanctum_roots","sanctum_court"),("sanctum_court","sanctum_archive"),("sanctum_court","sanctum_roots"),("sanctum_hall",MapRegistry.Sanctum)}){
    Game.Flow.TravelTo(pair.Item1,true);yield return Wait(.6f);while(Game.Flow.IsTransitioning)yield return null;Game.World.PortalTowards(pair.Item2,out var portal);Game.Player.Place(portal,Facing.Right);yield return Wait(1);while(Game.Flow.IsTransitioning)yield return null;DCheck(pair.Item1+" portal "+pair.Item2,Game.World.MapId==pair.Item2);yield return Wait(.5f);DCheck("no bounce "+pair.Item2,Game.World.MapId==pair.Item2);
   }
   Game.World.Load("sanctum_court");Game.Player.Place(Game.World.PlayerSpawn,Facing.Right);yield return Wait(.2f);Game.UI.WorldMap.Show();yield return Wait(.2f);SanctumScreenshot("sanctum-map-ui");Game.UI.WorldMap.Hide();
   Game.World.Load(MapRegistry.Village);yield return Wait(.2f);DCheck("water cleaned on exit",UnityEngine.Object.FindObjectsByType<SanctumWater>().Length==0);DCheck("muted",AudioListener.volume==0);
   Log($"SANCTUM FIELDS RESULTS: {dgnPassed} passed, {dgnFailed} failed");
  }
 }
}
