using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class Progression
    {
        CareerSave careerState = new CareerSave();
        public Career Career => careerState.career;
        public bool IsPromoted => Career != Career.None && Level >= 15 && CareerCatalog.Base(Career) == cls;
        public bool Awakened => IsPromoted && careerState.awakened && careerState.questStage == 5;
        public int AwakeningStage => careerState.questStage;
        public int RefundedPoints => careerState.refunded;
        public IEnumerable<string> LegacyTraining => careerState.training;
        public int CareerPoints { get { int spent=0; foreach(var n in careerState.nodes) spent += n.rank*(n.rank+1)/2; return IsPromoted ? Math.Max(0,Level-11+careerState.refunded-spent) : 0; } }
        public string ClassLabel => IsPromoted ? CareerCatalog.Name(Career) : CharacterClassInfo.Get(cls).displayName;
        public bool CanPromote(Career c) => Level >= 15 && Career == Career.None && c != Career.None && Enum.IsDefined(typeof(Career),c) && CareerCatalog.Base(c) == cls;
        public bool Promote(Career c)
        {
            if(!CanPromote(c)) return false;
            careerState.career=c;
            Changed?.Invoke(); return true;
        }
        public int Rank(string id) { foreach(var n in careerState.nodes) if(n.id==id) return n.rank; return 0; }
        public string NodeLock(CareerSkill s)
        {
            if(s==null) return "없는 스킬";
            if(!IsPromoted) return "15레벨 전직 후 이용 가능";
            if(s.career!=Career) return "다른 전직 직업의 스킬";
            if(Level<s.level) return $"Lv.{s.level} 필요";
            if(s.kind==CareerSkillKind.Awakening) return Awakened ? "각성 퀘스트 완료" : "각성 퀘스트 완료 시 개방";
            int rank=Rank(s.id);
            if(rank>=3) return "최대 강화 3/3";
            if(s.Prerequisite!=null && Rank(s.Prerequisite)==0) return $"선행: {CareerCatalog.Get(s.Prerequisite).name}";
            if(CareerPoints<rank+1) return $"포인트 {rank+1} 필요";
            return "";
        }
        public bool Learn(string id)
        {
            var s=CareerCatalog.Get(id);
            if(s==null || s.kind==CareerSkillKind.Awakening || NodeLock(s)!="") return false;
            var n=careerState.nodes.Find(x=>x.id==id);
            if(n==null) careerState.nodes.Add(new CareerRank{id=id,rank=1}); else n.rank++;
            Changed?.Invoke(); return true;
        }
        public void ResetCareerNodes()
        {
            careerState.nodes.Clear();
            for(int i=0;i<4;i++) if(CareerCatalog.Get(slots[i,0])!=null) slots[i,0]=null;
            Changed?.Invoke();
        }
        public bool CareerUnlocked(CareerSkill s) => s!=null && IsPromoted && s.career==Career && Level>=s.level && (s.kind==CareerSkillKind.Awakening?Awakened:Rank(s.id)>0);
        public bool EquipSkill(int slot,string id)
        {
            if(!IsSlotOpen(slot)) return false;
            var gem=SkillGems.Get(id); var s=CareerCatalog.Get(id);
            if(gem==null || gem.kind!=GemKind.Active || !IsUnlocked(gem) || (s!=null && s.kind==CareerSkillKind.Passive) || (slot==4)!=gem.IsUltimate) return false;
            for(int i=0;i<SkillGems.Slots;i++) if(i!=slot && Active(i)?.id==id) return false;
            slots[slot,0]=id; Changed?.Invoke(); return true;
        }
        // Only the role trial may advance these stages. Rewards are derived from the final state, never added repeatedly.
        internal bool AdvanceAwakening(int expected)
        {
            if(!IsPromoted || careerState.questStage!=expected || expected<0 || expected>=5) return false;
            careerState.questStage++;
            if(careerState.questStage==5) { careerState.awakened=true; slots[4,0]=CareerCatalog.For(Career)[8].id; }
            Changed?.Invoke(); return true;
        }
        void RestoreCareer(SaveData save)
        {
            var old=save.career;
            careerState=new CareerSave();
            if(old==null || old.schema<1)
            {
                var seen=new HashSet<string>();
                foreach(var id in save.passives ?? new List<string>()) if(id!=PassiveTree.Start && PassiveTree.Get(id)!=null && seen.Add(id)) careerState.training.Add(id);
                careerState.refunded=Math.Min(Level-1,careerState.training.Count);
            }
            else
            {
                careerState.training=new List<string>(old.training ?? new List<string>());
                careerState.refunded=Math.Min(Math.Max(0,old.refunded),Math.Min(39,careerState.training.Count));
                if(Enum.IsDefined(typeof(Career),old.career) && old.career!=Career.None && Level>=15 && CareerCatalog.Base(old.career)==cls)
                {
                    careerState.career=old.career;
                    careerState.questStage=Math.Max(0,Math.Min(5,old.questStage));
                    careerState.awakened=old.awakened && old.questStage==5;
                    // Restore in tree order, applying the same prerequisites and budget as normal allocation.
                    foreach(var s in CareerCatalog.For(Career))
                    {
                        var n=old.nodes?.Find(x=>x.id==s.id);
                        for(int i=0;i<Math.Min(3,n?.rank??0);i++) if(!Learn(s.id)) break;
                    }
                }
            }
            Allocated.Clear(); Allocated.Add(PassiveTree.Start);
        }
        public CareerSave CaptureCareer() => JsonUtility.FromJson<CareerSave>(JsonUtility.ToJson(careerState));
    }
}
