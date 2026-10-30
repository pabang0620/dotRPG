using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class SkillScreen
    {
        RectTransform careerRoot; Career browsing=Career.None; string selectedSkill=""; Career confirmation=Career.None;
        // [UI 2026-10-29] Scrolling skill list (career nodes, awakening note, base skills) with the key bar under it.
        RectTransform nodeParent, careerList; float careerListY;
        const float ListTop=120f, ListWidth=790f, ListHeightPromoted=288f, ListHeightPlain=366f;
        public void ShowAwakening(){tab=TabId.Tree;var p=Game.Session.Progression;if(p.IsPromoted)selectedSkill=CareerCatalog.For(p.Career)[8].id;Game.Flow.OpenWindow(this);}
        void BuildCareer(){careerRoot=UIFactory.Stretch(UIFactory.Rect(treePage,"Career"));}

        /// <summary>[STYLE] 스타일 1·2·3 (Docs/PLAN_SKILL_STYLES.md): saved builds of career nodes, skill keys and passives.</summary>
        void StyleButtons(Progression p)
        {
            string[] shortName={L.T("1 파티"),L.T("2 솔로"),L.T("3 자유")};
            for(int i=0;i<Progression.StyleCount;i++)
            {
                int k=i;bool on=p.ActiveStyle==i,open=Progression.StyleOpen(i);
                string label=on?$"<b><color=#9fe8ff>{shortName[i]}</color></b>":open?shortName[i]:L.T("<color=#8c96a8>3 잠김</color>");
                CareerButton("Style"+i,label,10+i*92,75,88,40,()=>
                {
                    if(!ContentUnlocks.Check(ContentUnlocks.Feature.SkillStyle))return; // [UNLOCK]
                    if(!Progression.StyleOpen(k))
                    {
                        Game.UI.Confirm(L.F("<b>스타일 3</b>을 열까요?\n<size=18>별조각 {0:N0} · 계정의 모든 캐릭터가 세 번째 빌드를 쓸 수 있습니다.</size>", SkillStyleClient.Price) + "\n" + PolicyClient.RefundNotice,()=>SkillStyleClient.Buy((ok,msg)=>{GameEvents.RaiseToast(msg);Refresh();}),true);
                        return;
                    }
                    if(p.ActiveStyle==k)return;
                    string why=p.SwitchStyle(k);
                    if(why!=null){GameEvents.RaiseToast(why);Game.Audio.PlaySfx("cancel");return;}
                    GameEvents.RaiseToast(L.F("{0}로 바꿨습니다.", L.T(Progression.StyleName(k)))+(p.StyleUsed(k)?"":L.T(" 처음 쓰는 스타일이라 빈 배분에서 시작합니다.")));
                    Game.Flow.Autosave();Refresh();
                });
            }
        }
        Text CareerText(Transform parent,string name,string text,int size,float x,float y,float w,float h)
        {
            var label = Label(parent,name,text,size,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(w,h));
            if (name == "Awakening") FitCareerCopy(label, UIFactory.ReadableFloor);
            return label;
        }
        static void FitCareerCopy(Text label, int minimum)
        {
            label.lineSpacing = 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMaxSize = label.fontSize;
            label.resizeTextMinSize = minimum;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }
        void CareerButton(string name,string text,float x,float y,float w,float h,System.Action action)
        {Button(careerRoot,name,text,"ui_btngray",new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(w,h),action,18);}
        void RefreshCareer()
        {
            // [UX] The page is rebuilt on every change: keep the gamepad selection on the button of the same name.
            var es=UnityEngine.EventSystems.EventSystem.current;
            var was=es!=null?es.currentSelectedGameObject:null;
            string keep=was!=null&&was.transform.IsChildOf(careerRoot)?was.name:null;
            if(careerList!=null)careerListY=careerList.anchoredPosition.y; // [UI 2026-10-29] keep the list's scroll through the rebuild
            for(int i=careerRoot.childCount-1;i>=0;i--){var child=careerRoot.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            careerList=null;nodeParent=null;
            BuildCareerPage();
            if(careerList!=null)careerList.anchoredPosition=new Vector2(0f,careerListY); // the ScrollRect clamps it
            if(keep==null||es==null)return;
            foreach(var sel in careerRoot.GetComponentsInChildren<Selectable>(false))
                if(sel.name==keep&&sel.IsInteractable()){es.SetSelectedGameObject(sel.gameObject);break;}
        }
        void BuildCareerPage()
        {
            var p=Game.Session.Progression;
            if(p.IsPromoted)browsing=p.Career;
            else if(browsing==Career.None||CareerCatalog.Base(browsing)!=Game.Session.PlayerClass)browsing=CareerCatalog.Pair(Game.Session.PlayerClass).a;
            CareerText(careerRoot,"CareerTitle",L.F("<b>{0}</b>  Lv.{1}  ·  전직 포인트 <color=#ffe0a0>{2}</color>{3}", L.T(p.ClassLabel), p.Level, p.CareerPoints, (p.IsPromoted?$"  ·  <color=#9fe8ff>{L.T(Progression.StyleName(p.ActiveStyle))}</color>":"")),24,12,4,736,32);
            // [UI 2026-10-30] The point rule (shrank to 10 px on one line) is a "?" at the end of the title row.
            UiHelpTip.Create(careerRoot,"PointRule",new Vector2(0,1),new Vector2(752,-4),p.IsPromoted?(p.Level>=22?L.T("새 전직 기술은 Lv.22까지 모두 열렸습니다 · 이후 레벨은 단계 강화(레벨당 1포인트)와 키 칸(Lv.25 S · Lv.30 D)"):L.F("전직 시 4포인트 + 레벨당 1포인트 · 단계 비용 1/2/3 · 돌려받은 {0} · 새 기술은 Lv.15/18/22에 열림", p.RefundedPoints)):L.T("15레벨 전직 후 이용 가능 · 기본 스킬은 레벨만 충족하면 사용 가능"));
            if(!p.IsPromoted)
            {
                var (a,b)=CareerCatalog.Pair(Game.Session.PlayerClass); // [ROGUE] 도적: 어쌔신 · 닌자
                CareerButton("CareerA",CareerCatalog.Name(a)+" · "+CareerCatalog.Role(a),10,75,380,44,()=>{browsing=a;confirmation=Career.None;selectedSkill="";Refresh();});
                CareerButton("CareerB",CareerCatalog.Name(b)+" · "+CareerCatalog.Role(b),400,75,380,44,()=>{browsing=b;confirmation=Career.None;selectedSkill="";Refresh();});
            }
            else
            {
                StyleButtons(p);
                RecommendedButton(p);
                CareerButton("ResetCareer",L.T("전직 기술 초기화"),540,75,240,40,()=>Game.UI.Confirm(L.T("배운 전직 기술을 모두 초기화합니다.\n<size=18>쓴 포인트는 전부 돌아오고, 장착한 직업 스킬은 해제됩니다.</size>\n초기화할까요?"),()=>{p.ResetCareerNodes();Game.Flow.Autosave();Refresh();},true));
            }
            var branch=CareerCatalog.Branches(browsing);var skills=CareerCatalog.For(browsing);
            bool keys=p.IsPromoted&&p.Career==browsing;
            // [UI 2026-10-29] Every skill in one scrolling list (wheel, drag on empty space or the bar): the 2x4 career tree,
            // the extra node and the awakening note, then the base skills of the class. The key bar sits under it.
            float listH=keys?ListHeightPromoted:ListHeightPlain;
            var list=SkillScroll(careerRoot,"SkillList",new Vector2(0f,-ListTop),new Vector2(ListWidth,listH),listH);
            careerList=list;nodeParent=list;
            const float Y0=ListTop; // the rows keep their old page coordinates, shifted to the top of the list
            for(int b=0;b<2;b++)
            {
                CareerText(list,"Branch"+b,branch[b],19,16,122+b*148-Y0+2,750,20);
                for(int tier=0;tier<4;tier++)
                {
                    var s=skills[b*4+tier];float x=14+tier*195,y=148+b*148-Y0+2;
                    var bg=NodeCard(s,p,x,y,selectedSkill==s.id); // [UX] styled card (frame by state, ring, rank pips)
                    if(tier<3)NodeLink(p,skills[b*4+tier+1],x+183,y+58);
                    if(keys){MakeDraggable(bg.gameObject,s,p);KeyBadge(bg.transform,s.id,p);} // [UX] drag onto the key bar; badge = its key
                }
            }
            // [FIGHTER] A node outside the 2x4 tree (검기 태세) sits at the bottom right; the awakening row makes room for it.
            bool extra=skills.Length>CareerSkill.ExtraIndex;
            CareerText(list,"Awakening",L.F("<color=#ffe0a0><b>별도 각성 영역 · {0}</b></color>\n{1}", L.T(skills[8].name), (p.Awakened?L.T("각성 완료 · T 슬롯에서 사용"):L.T("포인트로 배울 수 없음 · 직업 시련과 마지막 대화를 마치면 각성"))),18,16,436-Y0+2,extra?570:740,50);
            float bottom=436-Y0+2+56;
            if(extra)
            {
                var s=skills[CareerSkill.ExtraIndex];
                var bg=NodeCard(s,p,599,420-Y0+2,selectedSkill==s.id);
                if(keys){MakeDraggable(bg.gameObject,s,p);KeyBadge(bg.transform,s.id,p);}
                bottom=Mathf.Max(bottom,420-Y0+2+120);
            }
            if(keys)bottom=BaseSkillCards(list,p,bottom+6);
            nodeParent=null;
            list.sizeDelta=new Vector2(list.sizeDelta.x,Mathf.Max(listH,bottom+8));
            CareerButton("AwakeningInfo",L.T("각성 이야기 · 진행"),16,492,290,42,()=>{selectedSkill=skills[8].id;Refresh();});
            CareerButton("BaseSkillTab",L.T("기본 스킬 · 장착 / 보조 젬"),325,492,340,42,()=>{tab=TabId.Gems;Refresh();});
            if(keys)
            {
                var keyChosen=CareerCatalog.Get(selectedSkill);if(keyChosen==null||keyChosen.career!=browsing)keyChosen=skills[1];
                BuildKeyBar(careerRoot,p,keyChosen); // [UI 2026-10-29] under the list (was in the detail panel)
            }
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
                    CareerText(side.transform,"Head",L.F("<b>{0}</b>\n{1}\n\n대표 기술\n{2}\n{3}\n\n각성: {4}", L.T(CareerCatalog.Name(browsing)), L.T(CareerCatalog.Role(browsing)), L.T(skills[1].name), L.T(skills[5].name), L.T(skills[8].name)),19,160,20,236,192);
                    CareerText(side.transform,"Compare",L.F("{0}\n\n{1}\n\n<color=#ff9f7a>전직은 되돌릴 수 없습니다.</color>", L.T(skills[1].description), L.T(skills[5].description)),17,20,222,374,226);
                }
                else CareerText(side.transform,"Compare",L.F("<b>{0}</b>\n{1}\n\n대표: {2} / {3}\n각성: {4}\n\n{5}\n\n{6}\n\n전직은 되돌릴 수 없습니다.\n전직 선택을 누르면 한 번 더 확인합니다.", L.T(CareerCatalog.Name(browsing)), L.T(CareerCatalog.Role(browsing)), L.T(skills[1].name), L.T(skills[5].name), L.T(skills[8].name), L.T(skills[1].description), L.T(skills[5].description)),21,20,20,374,420);
                CareerButton("Promote",L.T("전직 선택"),824,467,365,52,()=>{if(!p.CanPromote(browsing)){GameEvents.RaiseToast(L.T("15레벨 이상, 자신의 기본 직업 계열만 전직할 수 있습니다."));return;}var pick=browsing;Game.UI.Confirm(L.F("<b>{0}</b>{1} 전직합니다.\n<size=18>전직은 되돌릴 수 없습니다.</size>\n전직할까요?", L.T(CareerCatalog.Name(pick)), Ro(CareerCatalog.Name(pick))),()=>CareerClient.Promote(pick,(ok,msg)=>{if(!ok){GameEvents.RaiseToast(msg);return;}p.Promote(pick);Game.Quest.SetFlag(NpcController.CareerFlag);GameEvents.RaiseToast(L.F("{0} 전직 완료 · 전직 포인트 4", L.T(CareerCatalog.Name(pick))));Game.Flow.Autosave();Refresh();}),true);});
            }
            else if(chosen.kind==CareerSkillKind.Awakening)
            {
                CareerText(side.transform,"Story",L.F("<b>{0}</b>\n마을 광장 · 전직 안내원\n{1}의 시련 기록\n\n{2}\n\n{3}", L.T(CareerTrials.Title(p.Career)), L.T(CareerTrials.Mentor(p.Career)), L.T(CareerTrials.Story(p.Career,p.AwakeningStage)), L.T(CareerTrials.Status)),19,20,20,374,390);
                CareerButton("QuestAction",p.AwakeningStage==2?L.T("시련 시작 / 재도전"):p.Awakened?L.T("각성 완료"):L.T("대화 계속"),824,445,365,48,()=>{if(p.AwakeningStage==2){string reason=CareerTrials.Begin();if(reason!="")GameEvents.RaiseToast(L.T(reason));}else if(!p.Awakened){if(!MapRegistry.IsTown(Game.Session.MapId)){GameEvents.RaiseToast(L.T("마을 광장의 전직 안내원에게 돌아가세요."));return;}int from=p.AwakeningStage;CareerClient.Advance(from,(ok,msg,stage)=>{if(ok)p.AdvanceAwakening(from);else{GameEvents.RaiseToast(msg);p.SyncAwakening(stage);}Game.Flow.Autosave();Refresh();});return;}Game.Flow.Autosave();Refresh();});
            }
            else
            {
                CareerText(side.transform,"SkillDetail",DescribeCareer(chosen,p),17,20,20,374,350);
                string why=p.NodeLock(chosen);
                CareerButton("Learn",why==""?L.F("습득 / 강화 ({0}P)", p.Rank(chosen.id)+1):L.T(why),824,380,365,42,()=>{if(!p.Learn(chosen.id))GameEvents.RaiseToast(L.T(p.NodeLock(chosen)));Game.Flow.Autosave();Refresh();});
            }
        }
        /// <summary>"로" after a vowel or ㄹ, "으로" after any other final consonant.</summary>
        static string Ro(string word)
        {
            if(!L.Ko)return ""; // [I18N] Korean particle only
            char c=string.IsNullOrEmpty(word)?'a':word[word.Length-1];
            if(c<0xAC00||c>0xD7A3)return "로"; // i18n-ignore particle
            int jong=(c-0xAC00)%28;
            return jong==0||jong==8?"로":"으로"; // i18n-ignore particle
        }
        /// <summary>One short line under a node: what it takes to learn it, or that it is learned.</summary>
        string NodeState(CareerSkill s,Progression p)
        {
            if(p.Career==browsing&&p.Rank(s.id)>0)return p.Rank(s.id)>=3?L.T("<color=#ffe0a0>최대 단계</color>"):L.T("<color=#8fe28f>습득</color> · 눌러서 강화");
            if(p.Career!=browsing)return L.T("<color=#b8c4d8>전직 후 배울 수 있음</color>");
            if(p.Level<s.level)return L.F("<color=#ff9f7a>Lv.{0} 필요</color>", s.level);
            var pre=s.Prerequisite==null?null:CareerCatalog.Get(s.Prerequisite);
            if(pre!=null&&p.Rank(pre.id)==0)return L.F("<color=#ff9f7a>{0} 먼저</color>", L.T(pre.name));
            return L.T("<color=#ffe066>배울 수 있음</color>");
        }
        static string DescribeCareer(CareerSkill s,Progression p)
        {
            if(s.kind==CareerSkillKind.Passive)return L.F("<b>{0}</b> · 패시브\n\n{1}\n{2}\n\n단계 {3}/3 · {4}\n\n조건: 전직 + Lv.{5}\n선행: {6}", L.T(s.name), L.T(s.description), L.T(CareerNumbers.Passive(s,p.Rank(s.id))), p.Rank(s.id), L.T(CareerCatalog.Branches(s.career)[s.branch]), s.level, (s.Prerequisite==null?L.T("없음"):L.T(CareerCatalog.Get(s.Prerequisite).name)));
            var n=CharacterData.Session.Stats.Skill(Game.Session.PlayerClass,0,s.Gem,new SkillGem[0]);
            string synergy=s.synergy=="해당 없음"?"":L.T(s.synergy)+"\n"; // i18n-ignore data key compare
            return L.F("<b>{0}</b> · 액티브\n\n{1}\n\n{2}\n사거리 {3:0.#} · 반경 {4:0.#}\n지속 {5:0.#}초 · 최대 {6}회\n{7}조건: Lv.{8} · 선행: {9}", L.T(s.name), L.T(s.description), L.T(CareerNumbers.Summary(s,CharacterData.Session,n)), s.range, n.radius, s.duration, s.hits, synergy, s.level, (s.Prerequisite==null?L.T("없음"):L.T(CareerCatalog.Get(s.Prerequisite).name)));
        }

        /// <summary>
        /// [UI 2026-10-29] The class's base skills under the career tree (the rogue has six): drag one onto a key like a
        /// career skill. Returns the bottom of the rows inside the list.
        /// </summary>
        float BaseSkillCards(RectTransform list,Progression p,float top)
        {
            var cls=Game.Player!=null?Game.Player.Class:Game.Session.PlayerClass;
            var gems=new System.Collections.Generic.List<SkillGem>();
            foreach(var g in SkillGems.All)
                if(g.kind==GemKind.Active&&g.UsableBy(cls)&&!g.IsUltimate&&CareerCatalog.Get(g.id)==null)gems.Add(g);
            if(gems.Count==0)return top;
            gems.Sort((a,b)=>a.slot.CompareTo(b.slot));
            CareerText(list,"BaseTitle",L.T("<color=#ffe0a0><b>기본 스킬</b></color>  <size=14><color=#b8c4d8>끌어다 아래 키에 놓을 수 있습니다</color></size>"),18,16,top,740,24);
            const float W=183f,H=64f;
            float y=top+28f;
            for(int i=0;i<gems.Count;i++)
            {
                var g=gems[i];float x=14+(i%4)*195,cy=y+(i/4)*(H+8f);
                bool open=p.IsUnlocked(g);
                var bg=Panel(list,"Base_"+g.id,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-cy),new Vector2(W,H),NodeFace);
                bg.raycastTarget=true;
                var icon=UIFactory.Image(bg.transform,"Icon",SkillIcon(g),open?Color.white:new Color(.45f,.45f,.5f,1f));
                icon.raycastTarget=false;icon.preserveAspect=true;
                UIFactory.Place(icon.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(8,0),new Vector2(48,48));
                FitCareerCopy(CareerText(bg.transform,"Name",$"<b>{L.T(g.name)}</b>",16,62,6,112,30),UIFactory.ReadableFloor);
                FitCareerCopy(CareerText(bg.transform,"State",open?L.T("<color=#8fe28f>사용 가능</color>"):L.F("<color=#ff9f7a>Lv.{0} 필요</color>",g.unlockLevel),16,62,38,80,22),UIFactory.ReadableFloor);
                var drag=bg.gameObject.AddComponent<DragSource>();
                var id=g.id;
                drag.payload=()=>p.IsUnlocked(SkillGems.Get(id))?id:null;
                drag.ghostSprite=()=>SkillIcon(SkillGems.Get(id));
                KeyBadge(bg.transform,g.id,p,-34f);
            }
            return y+((gems.Count+3)/4)*(H+8f);
        }
    }
}
