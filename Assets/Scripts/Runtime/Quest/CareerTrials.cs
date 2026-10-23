using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Repeatable, non-rewarding rehearsal in the town square; only successful role objectives advance the quest.</summary>
    public sealed class CareerTrials : MonoBehaviour
    {
        public static CareerTrials Running {get;private set;}
        public static PlayerController Companion => Running!=null?Running.companion:null;
        public static string Status => Running!=null?Running.status:"";
        PlayerController player,companion; EnemyController dummy; GameObject root;
        string map,status;float started,nextHazard,opening,lastHit;int hits,chain,taunts,absorbed,healed,cleansed,sequence;
        int behindChain,assassinations,ports,roots;bool behindDone; // [ROGUE] 어쌔신: back hits in a row, 암살 with 5 marks / 닌자: smoke leaps with clones out (ports), bomb roots
        readonly HashSet<string> elements=new HashSet<string>();
        int hp; float mana;
        public static string Title(Career c)=>c==Career.Fighter?L.T("흩어진 검, 하나의 의지"):c==Career.Guardian?L.T("방패 뒤의 이름"):c==Career.Arcanist?L.T("세 원소의 침묵"):c==Career.Assassin?L.T("누구의 등을 노리는가"):c==Career.Ninja?L.T("혼자이면서 여럿"):L.T("꺼지지 않은 새벽");
        public static string Mentor(Career c)=>c==Career.Fighter?L.T("카엘"):c==Career.Guardian?L.T("기사단장 레오나"):c==Career.Arcanist?L.T("약초꾼 오르"):c==Career.Assassin?L.T("기사 돈"):c==Career.Ninja?L.T("기사 모"):L.T("기사 아이비");
        public static string Story(Career c,int stage)
        {
            string[] f={L.T("카엘: 마족의 칼도 누군가를 지키려 들린 것일 수 있다. 네 힘은 무엇을 향하나?\n나: 힘만으로 답하지 않겠습니다."),L.T("카엘: 균열은 힘이 아니라 박자 사이에 있다. 열린 틈에 연격을 이어라.\n나: 다섯 검흔을 하나의 의지로 잇겠습니다."),L.T("시련: 4초마다 열리는 2초의 틈에 연속 5회 적중. 45초 안에 완성하라."),L.T("나: 쓰러뜨릴 적보다 지켜야 할 이유를 먼저 보았습니다.\n카엘: 그것이 천검귀일이다."),L.T("카엘: 힘을 휘두르는 주인은 네 의지다. 그 문양을 잊지 마라."),L.T("청색 낙검과 자색 검진이 한 점으로 모여 황금빛 결정타를 만든다. T 슬롯에서 천검귀일 사용 가능.")};
            string[] g={L.T("레오나: 살아 돌아온 방패 뒤에 돌아오지 못한 이름들이 있다.\n나: 이번에는 동료와 함께 돌아오겠습니다."),L.T("레오나: 아이비에게 향하는 환영을 돌려라. 혼자 버티는 것으로는 부족하다.\n나: 방패의 자리는 동료 앞입니다."),L.T("시련: 아이비 생존, 실제 적 2회 도발, 보호막으로 피해 40 흡수. 20초 버티기."),L.T("아이비: 네가 불러준 덕분에 숨을 돌렸어.\n나: 누구도 방패 뒤에 남겨두지 않겠습니다."),L.T("레오나: 무너지지 않는 것은 철이 아니라 함께 돌아오겠다는 맹세다."),L.T("동료를 지킨 맹세가 거대한 방패에 응축되었다. 지면을 강타해 적을 물리치는 천쇄방패를 T 슬롯에서 사용할 수 있다.")};
            string[] m={L.T("오르: 봉인을 살리는 마력과 태우는 마력은 같은 근원이다.\n나: 마왕 볼론의 힘도 그 원리를 따른다는 건가요?"), L.T("오르: 세 원소를 순서대로 다뤄라. 넘치는 힘에는 대가가 따른다.\n나: 태우고, 식히고, 다시 흐르게 하겠습니다."),L.T("시련: 홍련구 → 빙결삼창 → 연쇄전격 순서로 실제 적중. 45초. Lv.22 기술 필요."),L.T("나: 세 원소가 서로를 삼키지 않고 궤도를 이루었습니다.\n오르: 힘을 지배하는 대신 이해했구나."),L.T("오르: 유성우의 별빛은 봉인을 부수는 열쇠이자 지키는 고리다."),L.T("세 원소의 궤도가 완성되었다. T 슬롯에서 유성우 사용 가능.")};
            string[] b={L.T("아이비: 사람과 마족 모두 상처 앞에서는 같은 숨을 쉬더라.\n나: 누구의 상처인지 묻기 전에 살펴보겠습니다."),L.T("아이비: 저주가 회복을 막고 있어. 치료의 순서를 판단해 줘.\n나: 해로운 것을 걷어내고, 숨을 이어 지키겠습니다."),L.T("시련: 아이비 저주 정화, 실제 HP 90 회복, 보호막으로 25 피해 흡수, 20초 생존. Lv.22 정화 필요."),L.T("아이비: 다시 아침을 볼 수 있겠어.\n나: 모두가 그 문 너머의 아침을 볼 수 있기를."),L.T("아이비: 날개는 위에서 내려온 게 아니야. 끝까지 곁에 있던 손에서 피어난 거야."),L.T("새잎과 천사의 품이 성역에 피었다. T 슬롯에서 천상의 행진 사용 가능.")};
            // [ROGUE] Docs/PLAN_CLASS_ROGUE.md 5: 돈 "그림자는 지키는 사람 뒤에 선다", 모 "분신은 도망이 아니라 동료가 올 때까지 버티는 법".
            string[] a={L.T("돈: 등을 노리는 칼은 비겁하다고들 하지. 그런데 너는 누구의 등을 노리나?\n나: 동료를 해치려는 자의 등입니다."),L.T("돈: 그림자는 지키는 사람 뒤에 선다. 정면이 섞이면 처음부터다. 등 뒤만 다섯 번 이어라.\n나: 다섯 번, 한 번도 앞에 서지 않겠습니다."),L.T("시련: 등 뒤 공격 5회를 끊기지 않고 성공(정면 공격이 섞이면 처음부터) + 표식 5개 상태에서 암살 1회. 45초."),L.T("나: 앞에 선 동료가 있어서 등 뒤가 열렸습니다.\n돈: 그래, 그림자는 혼자서는 생기지 않는다."),L.T("돈: 네 칼끝이 향하는 곳을 잊지 마라. 지키는 사람의 뒤, 그곳이 네 자리다."),L.T("여섯 그림자가 한 칼로 모였다. T 슬롯에서 천영참 사용 가능.")};
            string[] n={L.T("모: 분신을 도망치는 재주라고 생각하나?\n나: 혼자서 여럿을 상대하는 재주라고 생각했습니다."),L.T("모: 분신은 도망이 아니라 동료가 올 때까지 버티는 법이다. 분신이 서 있는 동안 자리를 바꾸고, 함정으로 길을 막아라.\n나: 셋이 하나처럼 움직이겠습니다."),L.T("시련: 분신술 지속 중에 연막 도약 1회 + 기폭 쿠나이로 4번 속박. 45초."),L.T("나: 혼자였지만 혼자 싸우지 않았습니다.\n모: 이제야 분신이 네 편이 되었구나."),L.T("모: 여럿이 되는 법을 배웠으니, 이제 동료가 오는 길을 지켜라."),L.T("여섯 분신이 원을 그리며 쿠나이를 던진다. T 슬롯에서 분신 난무 사용 가능.")};
            return (c==Career.Fighter?f:c==Career.Guardian?g:c==Career.Arcanist?m:c==Career.Assassin?a:c==Career.Ninja?n:b)[Mathf.Clamp(stage,0,5)];
        }
        public static string Begin()
        {
            var p=Game.Player;var prog=p?.Data.Progression;
            if(prog==null||!prog.IsPromoted)return L.T("전직 후 이용 가능");
            if(!MapRegistry.IsTown(Game.Session.MapId))return L.T("마을 광장의 전직 안내원에게 돌아가세요.");
            if(PartyNet.IsMember||PartyNet.IsHost)return L.T("각성 시련은 온라인 파티에서 나간 후 혼자 진행하세요. 시련 동료가 제공됩니다.");
            if(Running!=null)return L.T("이미 시련 진행 중");
            if(prog.AwakeningStage!=2)return L.T("대화를 먼저 진행하세요.");
            if(prog.Career==Career.Arcanist && (prog.Rank("m_fire")==0||prog.Rank("m_ice")==0||prog.Rank("m_storm")==0))return L.T("홍련구·빙결삼창·연쇄전격을 해금하세요. (Lv.22, 노드 4개)");
            if(prog.Career==Career.Bishop && (prog.Rank("b_cleanse")==0||prog.Rank("b_wing")==0))return L.T("정화의 종·천사의 품을 해금하세요. (Lv.22, 노드 7개)");
            if(prog.Career==Career.Guardian && (prog.Rank("g_taunt")==0||prog.Rank("g_wall")==0))return L.T("대지의 호령·회귀의 방패를 해금하세요. (Lv.18, 노드 5개)");
            if(prog.Career==Career.Assassin && (prog.Rank("a_kunai")==0||prog.Rank("a_assassinate")==0))return L.T("쿠나이 순간이동·암살을 해금하세요. (Lv.22, 노드 6개)");
            if(prog.Career==Career.Ninja && (prog.Rank("n_clone")==0||prog.Rank("n_bomb")==0))return L.T("분신술·기폭 쿠나이를 해금하세요. (Lv.22, 노드 6개)");
            Game.Flow.CloseInventory();
            // [ANTI-ABUSE] Online, the server opens the trial first; the stage only moves on its answer at the end.
            CareerClient.TrialStart((ok,msg)=>{
                if(!ok){GameEvents.RaiseToast(msg);return;}
                if(Running!=null)return;
                var go=new GameObject("AwakeningTrial");Running=go.AddComponent<CareerTrials>();Running.Setup(p);
            });
            return "";
        }
        void Setup(PlayerController p)
        {
            player=p;map=Game.Session.MapId;hp=p.Health.Current;mana=p.Data.Mana;started=Time.time;nextHazard=Time.time+2;root=gameObject;
            var data=CharacterData.CreateCompanion("career_trial_ivy",L.T("시련 동료 아이비"),CharacterClass.Warrior,240);data.Look=StoryCast.Find("knight_ivy").look;
            companion=PlayerController.CreateCompanion(Game.Config,transform,data,new ScriptedInput());companion.Spawn(player.Position+Vector2.right*1.5f,Facing.Down,110,240);
            var stats=Instantiate(Game.Config.skeletonStats);stats.maxHealth=100000;stats.attackDamage=0;stats.xpReward=0;stats.knockbackSpeed=0;stats.wanderSpeed=0;stats.chaseSpeed=0;stats.enemyId="career_trial_echo";
            dummy=EnemyController.Create(stats,CharacterLook.Skeleton,p.Position+Vector2.up*2,transform);
            if(p.Data.Progression.Career==Career.Bishop)CareerCombat.For(companion).AddCurse(45);
            p.Data.Mana=p.MaxMana;status=L.T("시련 시작");
        }
        public static void Record(PlayerController actor,string what,int amount)
        {
            var r=Running;if(r==null||(actor!=r.player&&actor!=r.companion))return;
            if(what=="hit"&&actor==r.player){r.hits++;float phase=(Time.time-r.started)%4;if(phase<2){r.chain=Time.time-r.lastHit<1.5f?r.chain+1:1;r.lastHit=Time.time;}else r.chain=0;}
            if(what=="taunted"&&actor==r.player)r.taunts++;
            if(what=="absorbed")r.absorbed+=amount;
            if(what=="heal"&&actor==r.player)r.healed+=amount;
            if(what=="cleanse"&&actor==r.player)r.cleansed++;
            if(actor==r.player){if(what=="behind")r.behindChain++;else if(what=="front")r.behindChain=0;if(what=="assassinate5")r.assassinations++;if(what=="veil")r.ports++;if(what=="rooted")r.roots++;} // [ROGUE]
            string[] order={"fire","ice","storm"};if(System.Array.IndexOf(order,what)>=0&&actor==r.player){if(r.sequence<3&&what==order[r.sequence])r.sequence++;else if(r.sequence<3)r.sequence=what=="fire"?1:0;}
        }
        void OnGUI()
        {
            if(Running!=this)return;
            string message=status+"\n"+L.F("{0}초 남음 · 실패 시 재도전 가능", Mathf.Max(0,45-(Time.time-started)).ToString("0"));
            if(TouchUi.Enabled)
            {
                // [TOUCH] The 600x56 banner is tiny on a phone: bigger text and box, inside the safe area.
                float k=Mathf.Max(1.6f,Screen.height/450f);
                float width=Mathf.Min(Screen.width-40f,600f*k);
                var style=new GUIStyle(GUI.skin.box){fontSize=Mathf.RoundToInt(15*k),wordWrap=true,alignment=TextAnchor.MiddleCenter};
                float top=Screen.height-Screen.safeArea.yMax+16f*k;
                GUI.Box(new Rect(Screen.width/2f-width/2f,top,width,56f*k),message,style);
                return;
            }
            GUI.Box(new Rect(Screen.width/2-300,86,600,56),message);
        }
        void Update()
        {
            if(player==null||player.IsDead||companion==null||companion.IsDead||Game.Session.MapId!=map){Finish(false);return;}
            if(!Game.IsWorldRunning)return;
            float elapsed=Time.time-started;
            if(elapsed>45){Finish(false);return;}
            var c=player.Data.Progression.Career;
            if((c==Career.Guardian||c==Career.Bishop)&&Time.time>=nextHazard)
            {
                nextHazard=Time.time+2.5f;
                bool taunting=ThreatTable.For(dummy).Forced==player;
                var target=taunting?player:companion;int damage=CareerCombat.For(target).Absorb(18);target.Health.Drain(Mathf.Min(damage,target.Health.Current-1));
                SkillVisuals.SlashHit(target.Center,new Color(.9f,.4f,.9f,.9f));
                if(target.Health.Current<=1){Finish(false);return;}
            }
            bool success=c==Career.Fighter?chain>=5:c==Career.Guardian?taunts>=2&&absorbed>=40&&elapsed>=20:c==Career.Arcanist?sequence>=3
                :c==Career.Assassin?behindDone&&assassinations>0:c==Career.Ninja?ports>=1&&roots>=4:cleansed>0&&healed>=90&&absorbed>=25&&elapsed>=20;
            if(behindChain>=5)behindDone=true;
            status=c==Career.Assassin?L.F("등 뒤 연속 {0}/5 · 표식 5개 암살 {1}/1", (behindDone?5:Mathf.Min(behindChain,5)), Mathf.Min(assassinations,1)):c==Career.Ninja?L.F("분신 중 연막 도약 {0}/1 · 기폭 속박 {1}/4", Mathf.Min(ports,1), Mathf.Min(roots,4)):c==Career.Fighter?L.F("열린 틈 {0} · 연격 {1}/5", ((elapsed%4)<2?L.T("공격!"):L.T("대기")), chain):c==Career.Guardian?L.F("도발 {0}/2 · 흡수 {1}/40 · 버티기 {2:0}/20초", taunts, absorbed, elapsed):c==Career.Arcanist?L.F("원소 정렬 {0}/3", Mathf.Min(sequence,3)):L.F("정화 {0}/1 · 회복 {1}/90 · 흡수 {2}/25", cleansed, healed, absorbed);
            if(success)Finish(true);
        }
        void Finish(bool success)
        {
            var prog=player!=null?player.Data.Progression:null;
            CareerClient.TrialFinish(success,(ok,msg,stage)=>{
                if(prog==null)return;
                if(ok&&success){prog.AdvanceAwakening(2);Game.Flow.Autosave();}
                else if(!ok){GameEvents.RaiseToast(msg);prog.SyncAwakening(stage);}
            });
            if(player!=null&&!player.IsDead){player.Health.Heal(Mathf.Max(0,hp-player.Health.Current));player.Data.Mana=mana;}
            GameEvents.RaiseToast(success?L.T("시련 완료! 전직 안내원과 각성의 대화를 마무리하세요."):L.T("시련 실패. 진행 단계는 유지됩니다. 다시 도전할 수 있습니다."));
            Running=null;Destroy(gameObject);
        }
        void OnDestroy(){if(Running==this)Running=null;}
    }
}
