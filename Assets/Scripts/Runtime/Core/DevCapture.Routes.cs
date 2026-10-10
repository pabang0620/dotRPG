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
        IEnumerator RoutesRun()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-portalCheck") >= 0) { yield return PortalDestinationRun(); yield break; }
            yield return Wait(1);Game.Config.autosave=false;
            Game.Flow.NewGame(CharacterClass.Warrior,"동선 검증");yield return Wait(1.5f);
            Game.Player.Input=new ScriptedInput();Game.Player.Health.SetInvulnerable(600);
            AudioListener.volume=0;Game.Audio?.SetVolumes(0,0);dgnPassed=dgnFailed=0;
            DCheck("twenty hunting fields",HuntingGrounds.All.Length==20);
            foreach(var r in WorldRoutes.Regions){
                DCheck(r[0]+" four fields",HuntingGrounds.All.Count(z=>z.village==r[0])==4);
                DCheck(r[0]+" split",WorldRoutes.Neighbors(r[1]).OrderBy(x=>x).SequenceEqual(new[]{r[0],r[2],r[3]}.OrderBy(x=>x)));
                foreach(int i in new[]{2,3})DCheck(r[i]+" branch to merge",WorldRoutes.Neighbors(r[i]).OrderBy(x=>x).SequenceEqual(new[]{r[1],r[4]}.OrderBy(x=>x)));
                DCheck(r[4]+" next region",MapRegistry.Get(r[4]).nextMap==r[5]);
            }
            var export=new List<object>();
            foreach(var map in MapRegistry.All){
                Game.World.Load(map.id);Game.Player.Place(Game.World.PlayerSpawn,Facing.Right);yield return Wait(.15f);
                var zone=HuntingGrounds.Get(map.id);
                var seen=new HashSet<Vector2Int>();var checkedCells=new HashSet<Vector2Int>();var q=new Queue<Vector2Int>();
                var start=Vector2Int.FloorToInt(Game.Player.Position);seen.Add(start);checkedCells.Add(start);q.Enqueue(start);
                while(q.Count>0){var v=q.Dequeue();foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var n=v+d;
                    if(!checkedCells.Add(n)||!Game.World.IsFree((Vector2)n+Vector2.one*.5f))continue;seen.Add(n);q.Enqueue(n);}}
                DCheck(map.id+" spawn free",Game.World.IsFree(Game.Player.Position));
                foreach(var target in WorldRoutes.Neighbors(map.id)){
                    var arrival=Game.World.ArrivalFrom(target,out _);
                    DCheck(map.id+" arrival reachable from "+target,Game.World.IsFree(arrival)&&seen.Contains(Vector2Int.FloorToInt(arrival)));
                    DCheck(map.id+" portal to "+target,Game.World.PortalTowards(target,out var portal));
                    DCheck(map.id+" no arrival bounce "+target,Vector2.Distance(arrival,portal)>1.3f);
                }
                var enemies=EnemyController.Active.Where(e=>e!=null&&e.isActiveAndEnabled&&!e.IsDead&&!e.Def.boss&&!e.Def.raid).ToArray();
                if(zone!=null){
                    DCheck(map.id+" has monsters",enemies.Length>0);
                    var bad=enemies.Where(e=>!Game.World.IsFree(e.Position)||!seen.Contains(Vector2Int.FloorToInt(e.Position))).ToArray();
                    DCheck(map.id+" all camps reachable",bad.Length==0);foreach(var e in bad)Log("BAD CAMP "+map.id+" "+e.Position);
                    File.WriteAllText(Path.Combine(folder,map.id+".txt"),HuntingGrounds.Layout(map.id));
                    RenderRegion(Path.Combine(folder,map.id+"-overview.png"),Game.World.Bounds,20);
                    export.Add(HRow(("id",map.id),("width",Game.World.Bounds.width),("height",Game.World.Bounds.height),
                        ("fieldSpawns",enemies.GroupBy(e=>e.Def.id).Select(g=>(object)HRow(("monsterId",g.Key),("points",g.Count()),("level",zone.monsterLevel),("xp",zone.KillXp),("respawnSeconds",HuntingGrounds.RespawnSeconds))).ToList())));
                }
            }
            File.WriteAllText(Path.Combine(folder,"field-export.json"),MiniJson.Write(export));
            // Exercise the actual portal trigger and transition, including the new terminal destination.
            foreach(var r in WorldRoutes.Regions)foreach(var pair in new[]{(r[1],r[2]),(r[2],r[4]),(r[1],r[3]),(r[3],r[4]),(r[4],r[5])}){
                Game.Flow.TravelTo(pair.Item1,true);yield return Wait(.5f);
                while(Game.Flow.IsTransitioning)yield return null;
                Game.World.PortalTowards(pair.Item2,out var p);Game.Player.Place(p,Facing.Right);yield return Wait(.8f);
                while(Game.Flow.IsTransitioning)yield return null;
                DCheck(pair.Item1+" trigger -> "+pair.Item2,Game.World.MapId==pair.Item2);yield return Wait(.4f);
                DCheck(pair.Item2+" stays after arrival",Game.World.MapId==pair.Item2);
            }
            DCheck("muted routes verification",AudioListener.volume==0);
            Log($"ROUTES RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
