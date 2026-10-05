using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator CareerPulseChecks()
        {
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop}){
                for(int row=0;row<4;row++)for(int cel=0;cel<4;cel++){
                    var s=CareerPulseArt.Frame(c,row,cel);
                    DCheck(c+" pulse row "+row+" cel "+cel,s!=null&&s.name==c+"_"+row+"_"+cel&&s.texture.filterMode==FilterMode.Point&&s.texture.mipmapCount==1&&!s.texture.isReadable&&s.rect.width>=80&&s.rect.height>=24);
                }
            }
            var p=Game.Player;var skill=CareerCatalog.Get("f_cross");
            for(int row=0;row<4;row++)DCheck("fighter reforge atlas row "+row,CareerPulseArt.Frame(Career.Fighter,row,2).texture.name.StartsWith("FighterReforge"));
            var rng=Random.state;float expected=Random.value;Random.state=rng;
            int hp=p.Health.Current;float mp=p.Data.Mana,clock=Time.timeScale;int changes=CareerPaintEffect.FrameChanges;
            CareerEffect.Play(skill,p.Center,1,Vector2.right,.32f,p);
            DCheck("pulse spawn does not consume combat RNG",Random.value==expected);Random.state=rng;
            yield return Wait(.2f);
            DCheck("native cel animation advances",CareerPaintEffect.FrameChanges>changes);
            DCheck("pulse playback never pauses combat or changes resources",Time.timeScale==clock&&p.Health.Current==hp&&p.Data.Mana==mp);
            yield return Wait(.25f);DCheck("pulse animation cleans renderers",CareerPaintEffect.ActiveCount==0);
            CareerPaintEffect.Ground(CareerCatalog.Get("b_bloom"),p.Center,3.5f,6,p);CareerCombat.For(p).ResetState();
            yield return null;yield return null;DCheck("reset removes sustained painted effects",CareerPaintEffect.ActiveCount==0);
            yield return RebornSetup(Career.Fighter);p=Game.Player;
            var enemy=RebornDummy(p.Center+Vector2.right*1.8f);enemy.Health.SetInvulnerable(2);
            int before=enemy.Health.Current,hits=CareerImpact.ConfirmedHits;
            yield return CareerCombat.For(p).Cast(skill,RebornNumbers(skill.id));yield return Wait(.1f);
            DCheck("blocked strikes do not create hit feedback",enemy.Health.Current==before&&CareerImpact.ConfirmedHits==hits);
            yield return Wait(2);
            hits=CareerImpact.ConfirmedHits;yield return CareerCombat.For(p).Cast(skill,RebornNumbers(skill.id));
            DCheck("successful wide cleave creates exactly one impact reaction",enemy.Health.Current<before&&CareerImpact.ConfirmedHits==hits+1);
            yield return Wait(.4f);DCheck("impact silhouettes expire",CareerImpact.Count==0);
            hits=CareerImpact.ConfirmedHits;enemy.transform.position=p.Position+Vector2.left*8;enemy.GetComponent<Rigidbody2D>().position=enemy.transform.position;
            yield return CareerCombat.For(p).Cast(skill,RebornNumbers(skill.id));
            DCheck("missed swings do not create hit reactions",CareerImpact.ConfirmedHits==hits);
            Destroy(enemy.gameObject);
            Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(.5f);Game.Player.Input=new ScriptedInput();
            Log("PULSE ART: 64 original temporal cels; Point/Clamp; combat clock unaffected; AudioListener.volume="+AudioListener.volume);
        }
    }
}
