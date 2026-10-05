using System.Collections;
using System.Linq;
using UnityEngine;
namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator CareerLivingChecks()
        {
            foreach(var s in CareerCatalog.All.Where(x=>x.kind!=CareerSkillKind.Passive)){
                var design=CareerLivingFx.Design(s.id);var a=CareerLivingFx.Warp(design,new Vector2(.17f,.23f),.05f);var b=CareerLivingFx.Warp(design,new Vector2(.17f,.23f),.21f);
                DCheck(s.id+" deforms its internal silhouette",Vector2.Distance(a,b)>.00001f&&a.magnitude<1&&b.magnitude<1);
            }
            yield return CareerMotionChecks();
            var p=Game.Player;var skill=CareerCatalog.Get("b_awake");CareerCombat.For(p).ResetState();yield return Wait(.15f);
            DCheck("all previous mesh resources released",CareerLivingFx.Live==0);
            for(int i=0;i<120;i++)CareerRenewalFx.Make(skill,p.Center,3,Vector2.one,5,p);
            yield return Wait(.1f);DCheck("effect mesh limit is 80",CareerLivingFx.Live==80&&CareerRenewalFx.Count==80);
            CareerCombat.For(p).ResetState();yield return Wait(.1f);DCheck("stress reset releases every mesh",CareerLivingFx.Live==0);
            CareerRenewalFx.Make(skill,p.Center,3,Vector2.one,5,p);yield return Wait(.05f);string map=Game.Session.MapId;Game.Session.MapId="living_fx_map";yield return Wait(.1f);
            DCheck("map exit releases animated meshes",CareerLivingFx.Live==0);Game.Session.MapId=map;
            CareerRenewalFx.Make(skill,p.Center,3,Vector2.one,5,p);yield return Wait(.05f);p.Health.Init(p.Health.Max,p.Health.Max,0);p.TakeDamage(new DamageInfo(999999,p.Position,0,Team.Enemy,null){unblockable=true});yield return Wait(.1f);
            DCheck("death releases animated meshes",p.IsDead&&CareerLivingFx.Live==0);
            Log($"LIVING FX RESULTS: {dgnPassed} passed, {dgnFailed} failed; peak={CareerLivingFx.Peak}; mesh updates={CareerLivingFx.MeshUpdates}");
        }
    }
}
