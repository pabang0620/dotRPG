using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UX] The key bar of the career tab: the five skill keys with the skill each one holds. Drag a learned skill
    /// card onto a key (or click a key while a skill is chosen) to set it; a skill already on another key swaps
    /// places; right-click a key to clear it back to its basic skill. 추천 배치 learns the standard build with the
    /// points left and puts its skills on the keys.
    /// </summary>
    public partial class SkillScreen
    {
        static Sprite SkillIcon(SkillGem g) => g == null ? null : CareerMoves.Icon(g.icon) ?? Game.Art.Get(g.icon);

        /// <summary>A career node card becomes draggable once the skill can be put on a key.</summary>
        void MakeDraggable(GameObject card, CareerSkill s, Progression p)
        {
            var drag = card.AddComponent<DragSource>();
            drag.payload = () => s.kind != CareerSkillKind.Passive && p.CareerUnlocked(s) ? s.id : null;
            drag.ghostSprite = () => CareerMoves.Icon(s.Icon);
        }

        /// <summary>The key badge ("Q") on a node card whose skill is on a key.</summary>
        void KeyBadge(Transform card, string id, Progression p)
        {
            int slot = p.SlotOf(id);
            if (slot < 0) return;
            var badge = Panel(card, "KeyBadge", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -60), new Vector2(34, 26), new Color32(255, 211, 74, 235));
            Label(badge.transform, "Key", "<b>" + Game.Input.GetBindingLabel(SkillGems.ActionFor(slot)) + "</b>", 16,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(34, 26), TextAnchor.MiddleCenter).color = new Color32(30, 24, 10, 255);
        }

        void BuildKeyBar(Transform side, Progression p, CareerSkill chosen)
        {
            CareerText(side, "KeyBarTitle", "<b>장착 슬롯</b>  <size=14><color=#b8c4d8>우클릭: 해제</color></size>\n<size=14><color=#b8c4d8>스킬을 끌어다 놓거나, 스킬을 고른 뒤 칸을 누르세요</color></size>", 16, 18, 422, 390, 40);
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                int slot = i;
                var gem = p.Active(slot);
                bool ultimate = slot == 4;
                bool holdsChosen = chosen != null && gem != null && gem.id == chosen.id;
                var box = Panel(side, "Key" + slot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18 + slot * 76, -460), new Vector2(68, 68),
                    holdsChosen ? new Color32(90, 74, 30, 255) : new Color32(14, 20, 32, 255));
                box.raycastTarget = true;
                var icon = UIFactory.Image(box.transform, "Icon", SkillIcon(gem), gem != null ? Color.white : new Color(1, 1, 1, 0));
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                UIFactory.Stretch(icon.rectTransform, 6, 6, 6, 6);
                var key = Label(box.transform, "KeyLabel", "<b>" + Game.Input.GetBindingLabel(SkillGems.ActionFor(slot)) + "</b>", 15,
                    new Vector2(0, 0), new Vector2(0, 0), new Vector2(3, 1), new Vector2(30, 20), TextAnchor.LowerLeft);
                key.raycastTarget = false;
                if (!p.IsSlotOpen(slot))
                {
                    var lockImg = UIFactory.Image(box.transform, "Lock", Game.Art.Get("ui_lock"), new Color(1, 1, 1, .8f));
                    UIFactory.Place(lockImg.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.one * 22);
                }
                var drop = box.gameObject.AddComponent<DropTarget>();
                drop.highlight = box;
                drop.accepts = o => o is string id && CanPut(p, slot, id);
                drop.onDrop = o => Put(p, slot, (string)o);
                // [UX] A Button so the gamepad can reach the key: left click / Submit puts the chosen skill, right click
                // (or Cancel / UseItem while selected, see ClearSelectedKey) clears it.
                var btn = box.gameObject.AddComponent<Button>();
                btn.targetGraphic = box;
                btn.onClick.AddListener(() => { if (LongPress.TakeFired(box.gameObject)) return; if (chosen != null && chosen.kind != CareerSkillKind.Passive) Put(p, slot, chosen.id); });
                var relay = box.gameObject.AddComponent<PointerRelay>();
                relay.onClick = button =>
                {
                    if (button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) ClearKey(p, slot);
                };
                LongPress.Add(box.gameObject, () => ClearKey(p, slot)); // touch: hold = right click
            }
        }

        void ClearKey(Progression p, int slot)
        {
            if (slot == 4) return; // the awakening key stays
            p.ClearSkill(slot);
            Game.Audio.PlaySfx("cancel");
            Game.Flow.Autosave();
            Refresh();
        }

        /// <summary>
        /// Cancel or UseItem with a key box selected (gamepad) clears that key. Cancel only takes this when the key holds a
        /// career skill, so a second Cancel still closes the window. True when the key press was used here.
        /// </summary>
        bool ClearSelectedKey()
        {
            var input = Game.Input;
            if (!input.CancelPressed && !input.UseItemPressed) return false;
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null || !go.activeInHierarchy || !go.transform.IsChildOf(careerRoot) || !go.name.StartsWith("Key")) return false;
            if (!int.TryParse(go.name.Substring(3), out int slot) || slot < 0 || slot >= SkillGems.Slots) return false;
            var p = Game.Session.Progression;
            var gem = p.Active(slot);
            bool career = gem != null && CareerCatalog.Get(gem.id) != null;
            if (input.CancelPressed && (!career || slot == 4)) return false;
            ClearKey(p, slot);
            return true;
        }

        static bool CanPut(Progression p, int slot, string id)
        {
            var gem = SkillGems.Get(id);
            return gem != null && p.IsSlotOpen(slot) && (slot == 4) == gem.IsUltimate && p.IsUnlocked(gem);
        }

        void Put(Progression p, int slot, string id)
        {
            var s = CareerCatalog.Get(id);
            if (!p.EquipSkill(slot, id))
            {
                GameEvents.RaiseToast(s != null && !p.CareerUnlocked(s) ? "먼저 이 스킬을 배워야 합니다." : slot == 4 ? "T 칸에는 각성 기술만 넣을 수 있습니다." : "이 칸에는 넣을 수 없습니다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            Game.Audio.PlaySfx("confirm");
            Game.Flow.Autosave();
            Refresh();
        }

        // ---------- 추천 배치 ----------

        /// <summary>The standard build per career: (skill index, target rank) in learning order, then the Q W E R skills.</summary>
        static (int[,] ranks, int[] keys) Recommended(Career c)
        {
            switch (c)
            {
                case Career.Fighter: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 1 }, { 5, 3 }, { 3, 3 }, { 1, 2 }, { 0, 3 }, { 4, 3 }, { 2, 2 }, { 1, 3 }, { 2, 3 }, { 6, 1 }, { 7, 1 } }, new[] { 1, 2, 5, 3 });
                case Career.Guardian: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 3 }, { 4, 1 }, { 5, 1 }, { 6, 1 }, { 7, 1 }, { 0, 3 }, { 4, 3 }, { 3, 1 }, { 5, 2 }, { 6, 2 }, { 7, 2 } }, new[] { 2, 5, 6, 7 });
                // [2026-10-11] Q W E R all at rank 1 first (성운 폭발 used to wait behind 홍련구 3 and leave R empty), then the
                // spammable 연쇄전격 and the charge shot.
                case Career.Arcanist: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 1 }, { 5, 1 }, { 3, 3 }, { 5, 3 }, { 1, 3 }, { 2, 2 }, { 0, 3 }, { 4, 3 }, { 6, 1 }, { 7, 1 } }, new[] { 1, 2, 3, 5 });
                case Career.Bishop: return (new[,] { { 0, 1 }, { 1, 2 }, { 2, 1 }, { 3, 1 }, { 4, 1 }, { 5, 3 }, { 6, 2 }, { 0, 3 }, { 1, 3 }, { 7, 1 }, { 4, 2 } }, new[] { 5, 1, 2, 6 });
                default: return (new int[0, 2], new int[0]);
            }
        }

        void ApplyRecommended(Progression p)
        {
            var (ranks, keys) = Recommended(p.Career);
            var skills = CareerCatalog.For(p.Career);
            int learned = 0;
            for (int r = 0; r < ranks.GetLength(0); r++)
            {
                var s = skills[ranks[r, 0]];
                while (p.Rank(s.id) < ranks[r, 1] && p.NodeLock(s) == "" && p.Learn(s.id)) learned++;
            }
            int placed = 0;
            for (int k = 0; k < keys.Length; k++)
            {
                var s = skills[keys[k]];
                if (p.CareerUnlocked(s) && p.EquipSkill(k, s.id)) placed++;
            }
            if (p.Awakened) p.EquipSkill(4, skills[8].id);
            Game.Flow.Autosave();
            GameEvents.RaiseToast(learned > 0 ? $"추천 배치: {learned}단계를 배우고 스킬 {placed}개를 장착했습니다." : $"추천 배치: 스킬 {placed}개를 장착했습니다. (남은 포인트로 더 배울 기술 없음)");
            Game.Audio.PlaySfx("quest");
            Refresh();
        }

        void RecommendedButton(Progression p)
        {
            CareerButton("Recommended", "추천 배치", 290, 75, 240, 40, () =>
            {
                var (_, keys) = Recommended(p.Career);
                var skills = CareerCatalog.For(p.Career);
                var names = string.Join(" · ", System.Array.ConvertAll(keys, k => skills[k].name));
                Game.UI.Confirm($"<b>{CareerCatalog.Name(p.Career)} 추천 배치</b>\n<size=18>남은 포인트로 정석 기술을 배우고\nQ W E R에 {names}를 넣습니다.\n이미 배운 기술은 그대로 둡니다.</size>", () => ApplyRecommended(p), true);
            });
        }
    }
}
