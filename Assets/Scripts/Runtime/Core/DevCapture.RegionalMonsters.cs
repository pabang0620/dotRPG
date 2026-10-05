using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator RegionalMonsterChecks()
        {
            AudioListener.volume = 0;
            Game.Player.Health.SetInvulnerable(600);
            foreach (string species in new[] { RegionalMonsterArt.Rock, RegionalMonsterArt.Rock+"_guard", RegionalMonsterArt.Rock+"_thrower", RegionalMonsterArt.Yeti, RegionalMonsterArt.Yeti+"_guard", RegionalMonsterArt.Yeti+"_thrower" })
            foreach (string direction in new[] { "down", "downside", "side", "upside", "up" })
            foreach (string frame in new[] { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt" })
            {
                var sprite = RegionalMonsterArt.Get(species, direction, frame);
                DCheck(species+"/"+direction+"/"+frame, sprite != null && sprite.name == species+"_"+direction+"_"+frame && sprite.texture.filterMode == FilterMode.Point && sprite.bounds.size.y > 1 && sprite.bounds.size.y < 2);
            }
            foreach (var zone in HuntingGrounds.All)
            {
                Game.World.Load(zone.id);
                Game.Player.Place(Game.World.PlayerSpawn, Facing.Right);
                yield return Wait(.2f);
                var enemies = EnemyController.Active.Where(e=>e!=null && e.isActiveAndEnabled && !e.IsDead).ToArray();
                string species = zone.theme == MapTheme.Canyon ? RegionalMonsterArt.Rock : zone.theme == MapTheme.Winter ? RegionalMonsterArt.Yeti : null;
                DCheck(zone.id+" populated", enemies.Length > 0);
                DCheck(zone.id+" regional appearances", enemies.All(e=>e.Def!=null && (species==null ? !RegionalMonsterArt.Supports(e.Def.look.id) : e.Def.look.id==RegionalMonsterArt.LookFor(species,e.Def.kind))));
                DCheck(zone.id+" balance unchanged", enemies.All(e=>e.Level==zone.monsterLevel && e.Stats.xpReward==zone.KillXp && e.Def.hp==MonsterDatabase.Get(e.Def.id=="skeleton"?MonsterDatabase.Warrior:e.Def.id).hp && e.Def.damage==MonsterDatabase.Get(e.Def.id=="skeleton"?MonsterDatabase.Warrior:e.Def.id).damage));
                if(species==null)continue;
                DCheck(zone.id+" renderer uses new sheet", enemies.All(e=>e.GetComponentsInChildren<SpriteRenderer>().Any(sr=>sr.sprite!=null && sr.sprite.name.StartsWith(species))));
                DCheck(zone.id+" global catalog unaffected", zone.monsters.All(id=>!RegionalMonsterArt.Supports(MonsterDatabase.Get(id).look.id)));
                var archer = enemies.FirstOrDefault(e=>e.Def.kind==MonsterKind.Archer);
                if(archer!=null)DCheck(zone.id+" themed projectile",RegionalMonsterArt.Projectile(archer.Def)?.name==species+"_projectile");
                var first = enemies[0];
                Game.Player.Place(first.Position + new Vector2(2,0), Facing.Left);
                yield return Wait(.12f);
                var center = first.Position;
                RenderRegion(Path.Combine(folder,zone.id+".png"),new Rect(center.x-5,center.y-3,10,7),96);
                if(zone.id=="canyon_pass" || zone.id=="winter_edge")
                    for(int i=0;i<12;i++){
                        yield return Wait(.09f);
                        RenderRegion(Path.Combine(folder,zone.id+"_"+i.ToString("D2")+".png"),new Rect(center.x-5,center.y-3,10,7),64);
                    }
            }
            DCheck("validation is muted", AudioListener.volume == 0);
            Log($"REGIONAL MONSTERS RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
