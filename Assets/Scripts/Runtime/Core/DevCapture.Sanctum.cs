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
        IEnumerator SanctumRun()
        {
            ApplyRequestedResolution();yield return Wait(1);Game.Config.autosave=false;
            Game.Flow.NewGame(CharacterClass.Warrior,"성소 탐사자");yield return Wait(1.5f);
            Game.Flow.TravelTo(MapRegistry.Sanctum,true);yield return Wait(1.5f);
            while(Game.Flow.IsTransitioning)yield return null;
            yield return new WaitForFixedUpdate();
            Game.Player.Health.SetInvulnerable(600);Game.Camera.SetTarget(Game.Player.transform,true);
            Game.Audio?.SetVolumes(0,0);AudioListener.volume=0;
            bool screenshots=Array.IndexOf(Environment.GetCommandLineArgs(),"-sanctumVerify")>=0;
            bool verify=screenshots||Array.IndexOf(Environment.GetCommandLineArgs(),"-batchmode")>=0;
            if(!verify){Game.Player.Input=new LocalInput();GameEvents.RaiseToast("침수된 고대 성소 · 방향키 이동 / X 공격 / 지도 창에서 전체 지도");yield break;}
            dgnPassed=dgnFailed=0;
            DCheck("48x72 bounds",Game.World.Bounds.size==new Vector2(48,72));
            DCheck("altar spawn free",Game.World.IsFree(Game.Player.Position));
            DCheck("spawn away from return portal",Game.World.ObjectsRoot.GetComponentsInChildren<MapPortal>().All(p=>Vector2.Distance(p.transform.position,Game.Player.Position)>2));
            foreach(var pos in new[]{new Vector2(24,51),new Vector2(31,33),new Vector2(13,11),new Vector2(10,44),new Vector2(24,65)})DCheck("water/cliff blocked "+pos,!Game.World.IsFree(pos));
            foreach(var asset in new[]{"arch","tower_left","tower_right","altar","floor","terrace_wall","stairs","pillar"})DCheck("dedicated asset "+asset,SunkenSanctumArt.Asset(asset)!=null);
            var input=new ScriptedInput();Game.Player.Input=input;
            Game.Party.SetDungeonCompanions(true); // Explicit follower probe; normal outdoor hiring rules stay unchanged.
            var mate=Game.Party.AddCompanion("merc_bron") ?? Game.Party.Find("merc_bron");
            if(mate!=null)mate.Place(Game.Player.Position+Vector2.left,Facing.Up);
            var visited=new HashSet<Vector2Int>();var q=new Queue<Vector2Int>();var start=Vector2Int.FloorToInt(Game.Player.Position);q.Enqueue(start);visited.Add(start);
            while(q.Count>0){var v=q.Dequeue();foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var n=v+d;if(visited.Contains(n)||!Game.World.IsFree((Vector2)n+Vector2.one*.5f))continue;visited.Add(n);q.Enqueue(n);}}
            foreach(var pt in new[]{new Vector2Int(23,21),new Vector2Int(19,33),new Vector2Int(23,44),new Vector2Int(14,49),new Vector2Int(32,49)})DCheck("connected route "+pt,visited.Contains(pt));
            RenderRegion(Path.Combine(folder,"sanctum-overview.png"),Game.World.Bounds,32);
            RenderRegion(Path.Combine(folder,"sanctum-altar-play.png"),new Rect(14,6,20,12),64);
            if(screenshots)SanctumScreenshot("sanctum-gameplay-hud");
            var route=new[]{new Vector2(23,18),new Vector2(22.5f,22),new Vector2(22,25),new Vector2(20,29),new Vector2(19,34),new Vector2(20,38),new Vector2(23.5f,41),new Vector2(23.5f,47.2f),new Vector2(18,47.2f),new Vector2(18,49)};
            foreach(var target in route){float deadline=Time.time+10;
                while(Vector2.Distance(Game.Player.Position,target)>.3f&&Time.time<deadline){input.Move=(target-Game.Player.Position).normalized;yield return null;}
                input.Move=Vector2.zero;DCheck("physically walked to "+target,Vector2.Distance(Game.Player.Position,target)<.4f);
            }
            yield return Wait(2);
            DCheck("companion follows to upper terrace",mate!=null&&Vector2.Distance(mate.Position,Game.Player.Position)<5);
            DCheck("companion feet stay free",mate!=null&&!Physics2D.OverlapCircleAll(mate.Position+Vector2.up*.22f,.26f).Any(c=>!c.isTrigger && c.GetComponentInParent<PlayerController>()==null));
            RenderRegion(Path.Combine(folder,"sanctum-upper-play.png"),new Rect(10,42,20,12),64);
            RenderRegion(Path.Combine(folder,"sanctum-upper.png"),new Rect(8,42,32,30),32);
            if(screenshots){SanctumScreenshot("sanctum-upper-hud");Game.UI.WorldMap.Show();yield return Wait(.2f);SanctumScreenshot("sanctum-map-window");Game.UI.WorldMap.Hide();}
            for(int i=0;i<24;i++){yield return Wait(.125f);RenderRegion(Path.Combine(folder,"water-"+i.ToString("D2")+".png"),new Rect(19,48,10,19),32);}
            Game.World.Load(MapRegistry.Village);yield return Wait(.15f);
            DCheck("water removed on exit",UnityEngine.Object.FindObjectsByType<SanctumWater>(FindObjectsSortMode.None).Length==0);
            Game.World.Load(MapRegistry.Sanctum);Game.Player.Place(Game.World.PlayerSpawn,Facing.Up);yield return Wait(.15f);
            DCheck("one water system after reentry",UnityEngine.Object.FindObjectsByType<SanctumWater>(FindObjectsSortMode.None).Length==1);
            DCheck("bounded animated objects",Game.World.ObjectsRoot.GetComponentInChildren<SanctumWater>().ElementCount<=105);
            DCheck("muted verification",AudioListener.volume==0);
            Log($"SANCTUM RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }
        // A hidden window may not have a back buffer. Render the real camera and HUD explicitly.
        void SanctumScreenshot(string name)
        {
            var cam=Game.Camera.Camera;var target=cam.targetTexture;var active=RenderTexture.active;
            var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).Select(c=>(canvas:c,camera:c.worldCamera,plane:c.planeDistance)).ToArray();
            var rt=new RenderTexture(Screen.width,Screen.height,24){filterMode=FilterMode.Point};
            var tex=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            try{
                cam.targetTexture=rt;
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceCamera;item.canvas.worldCamera=cam;item.canvas.planeDistance=1;}
                Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);tex.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());
            }finally{
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceOverlay;item.canvas.worldCamera=item.camera;item.canvas.planeDistance=item.plane;}
                cam.targetTexture=target;RenderTexture.active=active;Destroy(tex);rt.Release();Destroy(rt);
            }
        }
    }
}
