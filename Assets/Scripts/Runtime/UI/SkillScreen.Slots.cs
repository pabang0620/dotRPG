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
        static Sprite SkillIcon(SkillGem g) => g == null ? null : Game.Art.Get(CareerMoves.IconKey(g.icon));

        /// <summary>A career node card becomes draggable once the skill can be put on a key.</summary>
        void MakeDraggable(GameObject card, CareerSkill s, Progression p)
        {
            var drag = card.AddComponent<DragSource>();
            drag.payload = () => s.kind != CareerSkillKind.Passive && p.CareerUnlocked(s) ? s.id : null;
            drag.ghostSprite = () => CareerMoves.Icon(s.Icon);
        }

        /// <summary>The key badge ("Q") on a node card whose skill is on a key.</summary>
        void KeyBadge(Transform card, string id, Progression p, float y = -60f)
        {
            int slot = p.SlotOf(id);
            if (slot < 0) return;
            // [UI 2026-10-30] The badge grows with the key name ("Space") instead of shrinking it below the readable floor.
            string keyName = Game.Input.GetBindingLabel(SkillGems.ActionFor(slot));
            float badgeW = Mathf.Clamp(12f + keyName.Length * 9f, 34f, 64f);
            var badge = Panel(card, "KeyBadge", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, y), new Vector2(badgeW, 26), new Color32(255, 211, 74, 235));
            var key = Label(badge.transform, "Key", "<b>" + keyName + "</b>", 16,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(badgeW - 2f, 24), TextAnchor.MiddleCenter);
            key.color = new Color32(30, 24, 10, 255);
            key.resizeTextForBestFit = true;
            key.resizeTextMinSize = UIFactory.ReadableFloor;
            key.resizeTextMaxSize = key.fontSize;
            key.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // [UI 2026-10-29] The key bar sits under the scrolling skill list (left column, was at the bottom of the detail panel).
        const float KeyBarTop = 412f, KeyBox = 52f, KeyStep = 58f;

        void BuildKeyBar(Transform side, Progression p, CareerSkill chosen)
        {
            CareerText(side, "KeyBarTitle", L.T("<b>장착 슬롯</b>  <size=14><color=#b8c4d8>우클릭: 해제</color></size>\n<size=14><color=#b8c4d8>스킬을 끌어다 놓거나, 스킬을 고른 뒤 칸을 누르세요</color></size>"), 16, 14, KeyBarTop, 330, 66);
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                int slot = i;
                var gem = p.Active(slot);
                bool ultimate = slot == 4;
                bool holdsChosen = chosen != null && gem != null && gem.id == chosen.id;
                // [SKILL 8] Eight smaller keys in bar order (Q W E R A S D T) instead of five 68 px ones.
                int pos = System.Array.IndexOf(SkillGems.BarOrder, slot);
                var box = Panel(side, "Key" + slot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(330 + pos * KeyStep, -(KeyBarTop + 8f)), new Vector2(KeyBox, KeyBox),
                    holdsChosen ? new Color32(90, 74, 30, 255) : new Color32(14, 20, 32, 255));
                box.raycastTarget = true;
                var icon = UIFactory.Image(box.transform, "Icon", SkillIcon(gem), gem != null ? Color.white : new Color(1, 1, 1, 0));
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                UIFactory.Stretch(icon.rectTransform, 4, 4, 4, 4);
                // [UI 2026-10-29] The key on a dark tag (bottom-left): over the skill icon a bare label was unreadable, so
                // only the empty keys seemed to have one.
                // [UI 2026-10-30] The tag grows with the key name (up to the box width) so the key stays at the readable floor.
                string keyName = Game.Input.GetBindingLabel(SkillGems.ActionFor(slot));
                float tagW = Mathf.Clamp(8f + keyName.Length * 9f, 22f, KeyBox - 2f);
                var keyTag = Panel(box.transform, "KeyTag", new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 1), new Vector2(tagW, 19), new Color(0.03f, 0.04f, 0.08f, 0.85f));
                keyTag.raycastTarget = false;
                var key = Label(keyTag.transform, "KeyLabel", "<b>" + keyName + "</b>", 16,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(tagW - 2f, 19), TextAnchor.MiddleCenter);
                key.color = new Color32(255, 224, 102, 255);
                key.resizeTextForBestFit = true;
                key.resizeTextMinSize = UIFactory.ReadableFloor;
                key.resizeTextMaxSize = key.fontSize;
                key.raycastTarget = false;
                if (!p.IsSlotOpen(slot))
                {
                    var lockImg = UIFactory.Image(box.transform, "Lock", Game.Art.Get("ui_lock"), new Color(1, 1, 1, .8f));
                    UIFactory.Place(lockImg.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.one * 18);
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
                GameEvents.RaiseToast(s != null && !p.CareerUnlocked(s) ? L.T("먼저 이 스킬을 배워야 합니다.") : slot == 4 ? L.T("T 칸에는 각성 기술만 넣을 수 있습니다.") : L.T("이 칸에는 넣을 수 없습니다."));
                Game.Audio.PlaySfx("cancel");
                return;
            }
            Game.Audio.PlaySfx("confirm");
            Game.Flow.Autosave();
            Refresh();
        }

        // ---------- 추천 배치 ----------

        static (int[,] ranks, int[] keys) Recommended(Career c) => SkillRecommend.Plan(c);

        void ApplyRecommended(Progression p)
        {
            var (learned, placed) = SkillRecommend.Apply(p);
            GuideFlags.Mark(GuideFlags.Recommend); // [GUIDE]
            Game.Flow.Autosave();
            GameEvents.RaiseToast(learned > 0 ? L.F("추천 배치: {0}단계를 배우고 스킬 {1}개를 장착했습니다.", learned, placed) : L.F("추천 배치: 스킬 {0}개를 장착했습니다. (남은 포인트로 더 배울 기술 없음)", placed));
            Game.Audio.PlaySfx("quest");
            Refresh();
        }

        void RecommendedButton(Progression p)
        {
            CareerButton("Recommended", L.T("추천 배치"), 290, 75, 240, 40, () =>
            {
                var (_, keys) = Recommended(p.Career);
                var skills = CareerCatalog.For(p.Career);
                var names = string.Join(" · ", System.Array.ConvertAll(keys, k => L.T(skills[k].name)));
                Game.UI.Confirm(L.F("<b>{0} 추천 배치</b>\n<size=18>남은 포인트로 정석 기술을 배우고\nQ W E R (A S D)에 {1}를 넣습니다.\n이미 배운 기술은 그대로 둡니다.</size>", L.T(CareerCatalog.Name(p.Career)), names), () => ApplyRecommended(p), true);
            });
        }
    }
}
