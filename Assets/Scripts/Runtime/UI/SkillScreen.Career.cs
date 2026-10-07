using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class SkillScreen
    {
        RectTransform careerRoot; Career browsing=Career.None; string selectedSkill=""; Career confirmation=Career.None;
        public void ShowAwakening(){tab=TabId.Tree;var p=Game.Session.Progression;if(p.IsPromoted)selectedSkill=CareerCatalog.For(p.Career)[8].id;Game.Flow.OpenWindow(this);}
        void BuildCareer(){careerRoot=UIFactory.Stretch(UIFactory.Rect(treePage,"Career"));}
        void CareerText(Transform parent,string name,string text,int size,float x,float y,float w,float h)
        {Label(parent,name,text,size,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(w,h));}
        void CareerButton(string name,string text,float x,float y,float w,float h,System.Action action)
        {Button(careerRoot,name,text,"ui_btngray",new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(w,h),action,18);}
        void RefreshCareer()
        {
            for(int i=careerRoot.childCount-1;i>=0;i--){var child=careerRoot.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            var p=Game.Session.Progression;
            if(p.IsPromoted)browsing=p.Career;
            else if(browsing==Career.None||CareerCatalog.Base(browsing)!=Game.Session.PlayerClass)browsing=Game.Session.PlayerClass==CharacterClass.Warrior?Career.Fighter:Career.Arcanist;
            CareerText(careerRoot,"CareerTitle",$"<b>{p.ClassLabel}</b>  Lv.{p.Level}  ·  전직 포인트 <color=#ffe0a0>{p.CareerPoints}</color>",24,12,4,780,32);
            CareerText(careerRoot,"PointRule",p.IsPromoted?$"전직 시 4포인트 + 이후 레벨당 1포인트 · 단계별 비용 1/2/3포인트 · 돌려받은 포인트 {p.RefundedPoints}":"15레벨 전직 후 이용 가능 · 기본 스킬은 레벨만 충족하면 사용 가능",16,12,40,780,28);
            if(!p.IsPromoted)
            {
                Career a=Game.Session.PlayerClass==CharacterClass.Warrior?Career.Fighter:Career.Arcanist,b=Game.Session.PlayerClass==CharacterClass.Warrior?Career.Guardian:Career.Bishop;
                CareerButton("CareerA",CareerCatalog.Name(a)+" · "+CareerCatalog.Role(a),10,75,380,44,()=>{browsing=a;confirmation=Career.None;selectedSkill="";Refresh();});
                CareerButton("CareerB",CareerCatalog.Name(b)+" · "+CareerCatalog.Role(b),400,75,380,44,()=>{browsing=b;confirmation=Career.None;selectedSkill="";Refresh();});
            }
            else
            {
                RecommendedButton(p);
                CareerButton("ResetCareer","전직 기술 초기화",540,75,240,40,()=>Game.UI.Confirm("배운 전직 기술을 모두 초기화합니다.\n<size=18>쓴 포인트는 전부 돌아오고, 장착한 직업 스킬은 해제됩니다.</size>\n초기화할까요?",()=>{p.ResetCareerNodes();Game.Flow.Autosave();Refresh();},true));
            }
            var branch=CareerCatalog.Branches(browsing);var skills=CareerCatalog.For(browsing);
            for(int b=0;b<2;b++)
            {
                CareerText(careerRoot,"Branch"+b,branch[b],19,16,130+b*150,750,26);
                for(int tier=0;tier<4;tier++)
                {
                    var s=skills[b*4+tier];float x=14+tier*195,y=162+b*150;
                    var bg=NodeCard(s,p,x,y,selectedSkill==s.id); // [UX] styled card (frame by state, ring, rank pips)
                    if(tier<3)NodeLink(p,skills[b*4+tier+1],x+183,y+58);
                    if(p.IsPromoted&&p.Career==browsing){MakeDraggable(bg.gameObject,s,p);KeyBadge(bg.transform,s.id,p);} // [UX] drag onto the key bar; badge = its key
                }
            }
            CareerText(careerRoot,"Awakening",$"<color=#ffe0a0><b>별도 각성 영역 · {skills[8].name}</b></color>\n{(p.Awakened?"각성 완료 · T 슬롯에서 사용":"포인트로 배울 수 없음 · 직업 시련과 마지막 대화를 마치면 각성")}",18,16,436,740,50);
            CareerButton("AwakeningInfo","각성 이야기 · 진행",16,492,290,42,()=>{selectedSkill=skills[8].id;Refresh();});
            CareerButton("BaseSkillTab","기본 스킬 · 장착 / 보조 젬",325,492,340,42,()=>{tab=TabId.Gems;Refresh();});
            var chosen=CareerCatalog.Get(selectedSkill);if(chosen==null||chosen.career!=browsing)chosen=skills[1];
            var side=Panel(careerRoot,"CareerDetail",new Vector2(0,1),new Vector2(0,1),new Vector2(802,-4),new Vector2(414,530),new Color32(20,29,45,255));
            if(!p.IsPromoted)
            {
                // [UI] The career's card art beside its name and signature skills (text only when no card is drawn).
                var card=Game.Art.Optional("Careers/career_card_"+browsing.ToString().ToLowerInvariant());
                if(card!=null)
                {
                    var img=UIFactory.SharpIcon(side.transform,"Card",Color.white);img.sprite=card;img.raycastTarget=false;
                    UIFactory.Place(img.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(18,-18),new Vector2(128,192));
                    CareerText(side.transform,"Head",$"<b>{CareerCatalog.Name(browsing)}</b>\n{CareerCatalog.Role(browsing)}\n\n대표 기술\n{skills[1].name}\n{skills[5].name}\n\n각성: {skills[8].name}",19,160,20,236,192);
                    CareerText(side.transform,"Compare",$"{skills[1].description}\n\n{skills[5].description}\n\n<color=#ff9f7a>전직은 되돌릴 수 없습니다.</color>",17,20,222,374,226);
                }
                else CareerText(side.transform,"Compare",$"<b>{CareerCatalog.Name(browsing)}</b>\n{CareerCatalog.Role(browsing)}\n\n대표: {skills[1].name} / {skills[5].name}\n각성: {skills[8].name}\n\n{skills[1].description}\n\n{skills[5].description}\n\n전직은 되돌릴 수 없습니다.\n전직 선택을 누르면 한 번 더 확인합니다.",21,20,20,374,420);
                CareerButton("Promote","전직 선택",824,467,365,52,()=>{if(!p.CanPromote(browsing)){GameEvents.RaiseToast("15레벨 이상, 자신의 기본 직업 계열만 전직할 수 있습니다.");return;}var pick=browsing;Game.UI.Confirm($"<b>{CareerCatalog.Name(pick)}</b>{Ro(CareerCatalog.Name(pick))} 전직합니다.\n<size=18>전직은 되돌릴 수 없습니다.</size>\n전직할까요?",()=>CareerClient.Promote(pick,(ok,msg)=>{if(!ok){GameEvents.RaiseToast(msg);return;}p.Promote(pick);Game.Quest.SetFlag(NpcController.CareerFlag);GameEvents.RaiseToast(CareerCatalog.Name(pick)+" 전직 완료 · 전직 포인트 4");Game.Flow.Autosave();Refresh();}),true);});
            }
            else if(chosen.kind==CareerSkillKind.Awakening)
            {
                CareerText(side.transform,"Story",$"<b>{CareerTrials.Title(p.Career)}</b>\n마을 광장 · 전직 안내원\n{CareerTrials.Mentor(p.Career)}의 시련 기록\n\n{CareerTrials.Story(p.Career,p.AwakeningStage)}\n\n{CareerTrials.Status}",19,20,20,374,390);
                CareerButton("QuestAction",p.AwakeningStage==2?"시련 시작 / 재도전":p.Awakened?"각성 완료":"대화 계속",824,445,365,48,()=>{if(p.AwakeningStage==2){string reason=CareerTrials.Begin();if(reason!="")GameEvents.RaiseToast(reason);}else if(!p.Awakened){if(!MapRegistry.IsTown(Game.Session.MapId)){GameEvents.RaiseToast("마을 광장의 전직 안내원에게 돌아가세요.");return;}int from=p.AwakeningStage;CareerClient.Advance(from,(ok,msg,stage)=>{if(ok)p.AdvanceAwakening(from);else{GameEvents.RaiseToast(msg);p.SyncAwakening(stage);}Game.Flow.Autosave();Refresh();});return;}Game.Flow.Autosave();Refresh();});
            }
            else
            {
                CareerText(side.transform,"SkillDetail",DescribeCareer(chosen,p),17,20,20,374,350);
                string why=p.NodeLock(chosen);
                CareerButton("Learn",why==""?$"습득 / 강화 ({p.Rank(chosen.id)+1}P)":why,824,380,365,42,()=>{if(!p.Learn(chosen.id))GameEvents.RaiseToast(p.NodeLock(chosen));Game.Flow.Autosave();Refresh();});
                BuildKeyBar(side.transform,p,chosen); // [UX] drag-and-drop key bar instead of four "Q 장착" buttons
            }
        }
        /// <summary>"로" after a vowel or ㄹ, "으로" after any other final consonant.</summary>
        static string Ro(string word)
        {
            char c=string.IsNullOrEmpty(word)?'a':word[word.Length-1];
            if(c<0xAC00||c>0xD7A3)return "로";
            int jong=(c-0xAC00)%28;
            return jong==0||jong==8?"로":"으로";
        }
        /// <summary>One short line under a node: what it takes to learn it, or that it is learned.</summary>
        string NodeState(CareerSkill s,Progression p)
        {
            if(p.Career==browsing&&p.Rank(s.id)>0)return p.Rank(s.id)>=3?"<color=#ffe0a0>최대 단계</color>":"<color=#8fe28f>습득</color> · 눌러서 강화";
            if(p.Career!=browsing)return "<color=#b8c4d8>전직 후 배울 수 있음</color>";
            if(p.Level<s.level)return $"<color=#ff9f7a>Lv.{s.level} 필요</color>";
            var pre=s.Prerequisite==null?null:CareerCatalog.Get(s.Prerequisite);
            if(pre!=null&&p.Rank(pre.id)==0)return $"<color=#ff9f7a>{pre.name} 먼저</color>";
            return "<color=#ffe066>배울 수 있음</color>";
        }
        static string DescribeCareer(CareerSkill s,Progression p)
        {
            if(s.kind==CareerSkillKind.Passive)return $"<b>{s.name}</b> · 패시브\n\n{s.description}\n{CareerNumbers.Passive(s,p.Rank(s.id))}\n\n단계 {p.Rank(s.id)}/3 · {CareerCatalog.Branches(s.career)[s.branch]}\n\n조건: 전직 + Lv.{s.level}\n선행: {(s.Prerequisite==null?"없음":CareerCatalog.Get(s.Prerequisite).name)}";
            var n=CharacterData.Session.Stats.Skill(Game.Session.PlayerClass,0,s.Gem,new SkillGem[0]);
            return $"<b>{s.name}</b> · 액티브\n\n{s.description}\n\n{CareerNumbers.Summary(s,CharacterData.Session,n)}\n사거리 {s.range:0.#} · 반경 {n.radius:0.#}\n지속 {s.duration:0.#}초 · 최대 {s.hits}회\n{(s.synergy=="해당 없음"?"":s.synergy+"\n")}조건: Lv.{s.level} · 선행: {(s.Prerequisite==null?"없음":CareerCatalog.Get(s.Prerequisite).name)}";
        }
    }
}
