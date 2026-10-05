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
            CareerText(careerRoot,"PointRule",p.IsPromoted?$"전직 4P + 이후 레벨당 1P · 단계별 비용 1/2/3P · 이전 노드 환급 {p.RefundedPoints}P":"15레벨 전직 후 이용 가능 · 기본 스킬은 레벨만 충족하면 사용 가능",16,12,40,800,28);
            if(!p.IsPromoted)
            {
                Career a=Game.Session.PlayerClass==CharacterClass.Warrior?Career.Fighter:Career.Arcanist,b=Game.Session.PlayerClass==CharacterClass.Warrior?Career.Guardian:Career.Bishop;
                CareerButton("CareerA",CareerCatalog.Name(a)+" · "+CareerCatalog.Role(a),10,75,380,44,()=>{browsing=a;confirmation=Career.None;selectedSkill="";Refresh();});
                CareerButton("CareerB",CareerCatalog.Name(b)+" · "+CareerCatalog.Role(b),400,75,380,44,()=>{browsing=b;confirmation=Career.None;selectedSkill="";Refresh();});
            }
            else CareerButton("ResetCareer","노드 초기화 · 전액 반환",540,75,240,40,()=>{p.ResetCareerNodes();Game.Flow.Autosave();Refresh();});
            var branch=CareerCatalog.Branches(browsing);var skills=CareerCatalog.For(browsing);
            for(int b=0;b<2;b++)
            {
                CareerText(careerRoot,"Branch"+b,branch[b],19,16,130+b*150,750,26);
                for(int tier=0;tier<4;tier++)
                {
                    var s=skills[b*4+tier];float x=14+tier*195,y=162+b*150;
                    var bg=Panel(careerRoot,"Node_"+s.id,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(183,116),new Color32(26,35,54,255));
                    bg.raycastTarget=true;bg.gameObject.AddComponent<PointerRelay>().onClick=_=>{selectedSkill=s.id;Refresh();};
                    var icon=UIFactory.Image(bg.transform,"Icon",CareerArt.Get(s.Icon),Color.white);
                    UIFactory.Place(icon.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(10,-10),Vector2.one*48);
                    CareerText(bg.transform,"Name",$"<b>{s.name}</b>\nLv.{s.level} · {p.Rank(s.id)}/3",16,65,10,115,52);
                    CareerText(bg.transform,"State",p.Career==browsing&&p.Rank(s.id)>0?"습득 · 클릭하여 강화/장착":tier==0?"패시브 · 진입 노드":"← 선행 노드 1단계 필요",13,9,76,168,32);
                    if(p.Rank(s.id)==0){var lockImg=UIFactory.Image(bg.transform,"Lock",Game.Art.Get("ui_lock"),new Color(1,1,1,.7f));UIFactory.Place(lockImg.rectTransform,Vector2.one,Vector2.one,new Vector2(-5,-5),Vector2.one*16);}
                }
            }
            CareerText(careerRoot,"Awakening",$"<color=#ffe0a0><b>별도 각성 영역 · {skills[8].name}</b></color>\n{(p.Awakened?"각성 완료 · T 슬롯에서 사용":"노드 구매 불가 · 직업 시련과 마지막 대화 완료 필요")}",18,16,445,740,60);
            CareerButton("AwakeningInfo","각성 이야기 · 진행",16,510,290,42,()=>{selectedSkill=skills[8].id;Refresh();});
            CareerButton("BaseSkillTab","기본 스킬 · 장착 / 보조 젬",325,510,340,42,()=>{tab=TabId.Gems;Refresh();});
            var chosen=CareerCatalog.Get(selectedSkill);if(chosen==null||chosen.career!=browsing)chosen=skills[1];
            var side=Panel(careerRoot,"CareerDetail",new Vector2(0,1),new Vector2(0,1),new Vector2(802,-4),new Vector2(414,550),new Color32(20,29,45,255));
            if(!p.IsPromoted)
            {
                CareerText(side.transform,"Compare",$"<b>{CareerCatalog.Name(browsing)}</b>\n{CareerCatalog.Role(browsing)}\n\n대표: {skills[1].name} / {skills[5].name}\n각성: {skills[8].name}\n\n{skills[1].description}\n\n{skills[5].description}\n\n전직은 되돌릴 수 없습니다.\n선택 후 한 번 더 확정하세요.",21,20,20,374,420);
                CareerButton("Promote",confirmation==browsing?"이 직업으로 전직 확정":"전직 선택",824,467,365,52,()=>{if(!p.CanPromote(browsing)){GameEvents.RaiseToast("15레벨 이상, 자신의 기본 직업 계열만 전직할 수 있습니다.");return;}if(confirmation!=browsing){confirmation=browsing;Refresh();return;}p.Promote(browsing);Game.Quest.SetFlag(NpcController.CareerFlag);GameEvents.RaiseToast(CareerCatalog.Name(browsing)+" 전직 완료 · 초기 4포인트");Game.Flow.Autosave();Refresh();});
            }
            else if(chosen.kind==CareerSkillKind.Awakening)
            {
                CareerText(side.transform,"Story",$"<b>{CareerTrials.Title(p.Career)}</b>\n마을 광장 · 전직 안내원\n{CareerTrials.Mentor(p.Career)}의 시련 기록\n\n{CareerTrials.Story(p.Career,p.AwakeningStage)}\n\n{CareerTrials.Status}",19,20,20,374,390);
                CareerButton("QuestAction",p.AwakeningStage==2?"시련 시작 / 재도전":p.Awakened?"각성 완료":"대화 계속",824,445,365,48,()=>{if(p.AwakeningStage==2){string reason=CareerTrials.Begin();if(reason!="")GameEvents.RaiseToast(reason);}else if(!p.Awakened){if(!MapRegistry.IsTown(Game.Session.MapId)){GameEvents.RaiseToast("마을 광장의 전직 안내원에게 돌아가세요.");return;}p.AdvanceAwakening(p.AwakeningStage);}Game.Flow.Autosave();Refresh();});
            }
            else
            {
                CareerText(side.transform,"SkillDetail",DescribeCareer(chosen,p),17,20,20,374,378);
                string why=p.NodeLock(chosen);
                CareerButton("Learn",why==""?$"습득 / 강화 ({p.Rank(chosen.id)+1}P)":why,824,405,365,45,()=>{if(!p.Learn(chosen.id))GameEvents.RaiseToast(p.NodeLock(chosen));Game.Flow.Autosave();Refresh();});
                if(chosen.kind==CareerSkillKind.Active)for(int i=0;i<4;i++){int slot=i;CareerButton("Equip"+i,Game.Input.GetBindingLabel(SkillGems.ActionFor(i))+" 장착",824+i*93,468,86,42,()=>{if(!p.EquipSkill(slot,chosen.id))GameEvents.RaiseToast("스킬 습득·슬롯 레벨을 확인하세요. 중복 장착은 불가합니다.");Game.Flow.Autosave();Refresh();});}
            }
        }
        static string DescribeCareer(CareerSkill s,Progression p)
        {
            if(s.kind==CareerSkillKind.Passive)return $"<b>{s.name}</b> · 패시브\n\n{s.description}\n{CareerNumbers.Passive(s,p.Rank(s.id))}\n\n단계 {p.Rank(s.id)}/3 · {CareerCatalog.Branches(s.career)[s.branch]}\n\n조건: 전직 + Lv.{s.level}\n선행: {(s.Prerequisite==null?"없음":CareerCatalog.Get(s.Prerequisite).name)}\n\n강화 수치는 스킬 명세와 동일하게 적용됩니다.";
            var n=CharacterData.Session.Stats.Skill(Game.Session.PlayerClass,0,s.Gem,new SkillGem[0]);
            return $"<b>{s.name}</b> · 액티브\n\n{s.description}\n\n{CareerNumbers.Summary(s,CharacterData.Session,n)}\n사거리 {s.range:0.#} · 반경 {n.radius:0.#}\n지속 {s.duration:0.#}초 · 최대 {s.hits}회\n{(s.synergy=="해당 없음"?"":s.synergy+"\n")}조건: Lv.{s.level} · 선행: {(s.Prerequisite==null?"없음":CareerCatalog.Get(s.Prerequisite).name)}";
        }
    }
}
