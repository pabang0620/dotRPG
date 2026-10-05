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
        readonly HashSet<string> elements=new HashSet<string>();
        int hp; float mana;
        public static string Title(Career c)=>c==Career.Fighter?"흩어진 검, 하나의 의지":c==Career.Guardian?"방패 뒤의 이름":c==Career.Arcanist?"세 원소의 침묵":"꺼지지 않은 새벽";
        public static string Mentor(Career c)=>c==Career.Fighter?"카엘":c==Career.Guardian?"기사단장 레오나":c==Career.Arcanist?"약초꾼 오르":"기사 아이비";
        public static string Story(Career c,int stage)
        {
            string[] f={"카엘: 마족의 칼도 누군가를 지키려 들린 것일 수 있다. 네 힘은 무엇을 향하나?\n나: 힘만으로 답하지 않겠습니다.","카엘: 균열은 힘이 아니라 박자 사이에 있다. 열린 틈에 연격을 이어라.\n나: 다섯 검흔을 하나의 의지로 잇겠습니다.","시련: 4초마다 열리는 2초의 틈에 연속 5회 적중. 45초 안에 완성하라.","나: 쓰러뜨릴 적보다 지켜야 할 이유를 먼저 보았습니다.\n카엘: 그것이 천검귀일이다.","카엘: 힘을 휘두르는 주인은 네 의지다. 그 문양을 잊지 마라.","청색 낙검과 자색 검진이 한 점으로 모여 황금빛 결정타를 만든다. T 슬롯에서 천검귀일 사용 가능."};
            string[] g={"레오나: 살아 돌아온 방패 뒤에 돌아오지 못한 이름들이 있다.\n나: 이번에는 동료와 함께 돌아오겠습니다.","레오나: 아이비에게 향하는 환영을 돌려라. 혼자 버티는 것으로는 부족하다.\n나: 방패의 자리는 동료 앞입니다.","시련: 아이비 생존, 실제 적 2회 도발, 보호막으로 피해 40 흡수. 20초 버티기.","아이비: 네가 불러준 덕분에 숨을 돌렸어.\n나: 누구도 방패 뒤에 남겨두지 않겠습니다.","레오나: 무너지지 않는 것은 철이 아니라 함께 돌아오겠다는 맹세다.","동료를 지킨 맹세가 거대한 방패에 응축되었다. 지면을 강타해 적을 물리치는 천쇄방패를 T 슬롯에서 사용할 수 있다."};
            string[] m={"오르: 봉인을 살리는 마력과 태우는 마력은 같은 근원이다.\n나: 마왕의 힘도 그 원리를 따른다는 건가요?", "오르: 세 원소를 순서대로 다뤄라. 넘치는 힘에는 대가가 따른다.\n나: 태우고, 식히고, 다시 흐르게 하겠습니다.","시련: 홍련창 → 빙결삼창 → 연쇄전격 순서로 실제 적중. 45초. Lv.22 기술 필요.","나: 세 원소가 서로를 삼키지 않고 궤도를 이루었습니다.\n오르: 힘을 지배하는 대신 이해했구나.","오르: 천체 붕괴의 룬은 봉인을 부수는 열쇠이자 지키는 고리다.","세 원소의 궤도가 완성되었다. T 슬롯에서 천체 붕괴 사용 가능."};
            string[] b={"아이비: 사람과 마족 모두 상처 앞에서는 같은 숨을 쉬더라.\n나: 누구의 상처인지 묻기 전에 살펴보겠습니다.","아이비: 저주가 회복을 막고 있어. 치료의 순서를 판단해 줘.\n나: 해로운 것을 걷어내고, 숨을 이어 지키겠습니다.","시련: 아이비 저주 정화, 실제 HP 90 회복, 보호막으로 25 피해 흡수, 20초 생존. Lv.22 정화 필요.","아이비: 다시 아침을 볼 수 있겠어.\n나: 모두가 그 문 너머의 아침을 볼 수 있기를.","아이비: 날개는 위에서 내려온 게 아니야. 끝까지 곁에 있던 손에서 피어난 거야.","새잎과 천사의 품이 성역에 피었다. T 슬롯에서 천상의 행진 사용 가능."};
            return (c==Career.Fighter?f:c==Career.Guardian?g:c==Career.Arcanist?m:b)[Mathf.Clamp(stage,0,5)];
        }
        public static string Begin()
        {
            var p=Game.Player;var prog=p?.Data.Progression;
            if(prog==null||!prog.IsPromoted)return "전직 후 이용 가능";
            if(!MapRegistry.IsTown(Game.Session.MapId))return "마을 광장의 전직 안내원에게 돌아가세요.";
            if(PartyNet.IsMember||PartyNet.IsHost)return "각성 시련은 온라인 파티에서 나간 후 혼자 진행하세요. 시련 동료가 제공됩니다.";
            if(Running!=null)return "이미 시련 진행 중";
            if(prog.AwakeningStage!=2)return "대화를 먼저 진행하세요.";
            if(prog.Career==Career.Arcanist && (prog.Rank("m_fire")==0||prog.Rank("m_ice")==0||prog.Rank("m_storm")==0))return "홍련창·빙결삼창·연쇄전격를 해금하세요. (Lv.22, 노드 4개)";
            if(prog.Career==Career.Bishop && (prog.Rank("b_cleanse")==0||prog.Rank("b_wing")==0))return "정화의 종소리·천사의 품을 해금하세요. (Lv.22, 노드 7개)";
            if(prog.Career==Career.Guardian && (prog.Rank("g_taunt")==0||prog.Rank("g_wall")==0))return "대지의 호령·회귀의 방패를 해금하세요. (Lv.18, 노드 5개)";
            Game.Flow.CloseInventory();
            var go=new GameObject("AwakeningTrial");Running=go.AddComponent<CareerTrials>();Running.Setup(p);return "";
        }
        void Setup(PlayerController p)
        {
            player=p;map=Game.Session.MapId;hp=p.Health.Current;mana=p.Data.Mana;started=Time.time;nextHazard=Time.time+2;root=gameObject;
            var data=CharacterData.CreateCompanion("career_trial_ivy","시련 동료 아이비",CharacterClass.Warrior,240);data.Look=StoryCast.Find("knight_ivy").look;
            companion=PlayerController.CreateCompanion(Game.Config,transform,data,new ScriptedInput());companion.Spawn(player.Position+Vector2.right*1.5f,Facing.Down,110,240);
            var stats=Instantiate(Game.Config.skeletonStats);stats.maxHealth=100000;stats.attackDamage=0;stats.xpReward=0;stats.knockbackSpeed=0;stats.wanderSpeed=0;stats.chaseSpeed=0;stats.enemyId="career_trial_echo";
            dummy=EnemyController.Create(stats,CharacterLook.Skeleton,p.Position+Vector2.up*2,transform);
            if(p.Data.Progression.Career==Career.Bishop)CareerCombat.For(companion).AddCurse(45);
            p.Data.Mana=p.MaxMana;status="시련 시작";
        }
        public static void Record(PlayerController actor,string what,int amount)
        {
            var r=Running;if(r==null||(actor!=r.player&&actor!=r.companion))return;
            if(what=="hit"&&actor==r.player){r.hits++;float phase=(Time.time-r.started)%4;if(phase<2){r.chain=Time.time-r.lastHit<1.5f?r.chain+1:1;r.lastHit=Time.time;}else r.chain=0;}
            if(what=="taunted"&&actor==r.player)r.taunts++;
            if(what=="absorbed")r.absorbed+=amount;
            if(what=="heal"&&actor==r.player)r.healed+=amount;
            if(what=="cleanse"&&actor==r.player)r.cleansed++;
            string[] order={"fire","ice","storm"};if(System.Array.IndexOf(order,what)>=0&&actor==r.player){if(r.sequence<3&&what==order[r.sequence])r.sequence++;else if(r.sequence<3)r.sequence=what=="fire"?1:0;}
        }
        void OnGUI(){if(Running!=this)return;GUI.Box(new Rect(Screen.width/2-300,86,600,56),status+"\n"+Mathf.Max(0,45-(Time.time-started)).ToString("0")+"초 남음 · 실패 시 재도전 가능");}
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
                CareerEffect.Play(CareerCatalog.Get("g_bash"),target.Center,.6f,Vector2.down);
                if(target.Health.Current<=1){Finish(false);return;}
            }
            bool success=c==Career.Fighter?chain>=5:c==Career.Guardian?taunts>=2&&absorbed>=40&&elapsed>=20:c==Career.Arcanist?sequence>=3:cleansed>0&&healed>=90&&absorbed>=25&&elapsed>=20;
            status=c==Career.Fighter?$"열린 틈 {((elapsed%4)<2?"공격!":"대기")} · 연격 {chain}/5":c==Career.Guardian?$"도발 {taunts}/2 · 흡수 {absorbed}/40 · 버티기 {elapsed:0}/20초":c==Career.Arcanist?$"원소 정렬 {Mathf.Min(sequence,3)}/3":$"정화 {cleansed}/1 · 회복 {healed}/90 · 흡수 {absorbed}/25";
            if(success)Finish(true);
        }
        void Finish(bool success)
        {
            if(success) {player.Data.Progression.AdvanceAwakening(2);Game.Flow.Autosave();}
            if(player!=null&&!player.IsDead){player.Health.Heal(Mathf.Max(0,hp-player.Health.Current));player.Data.Mana=mana;}
            GameEvents.RaiseToast(success?"시련 완료! 전직 안내원과 각성의 대화를 마무리하세요.":"시련 실패. 진행 단계는 유지됩니다. 다시 도전할 수 있습니다.");
            Running=null;Destroy(gameObject);
        }
        void OnDestroy(){if(Running==this)Running=null;}
    }
}
