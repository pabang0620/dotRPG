using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] On-screen controls for phones (built only when <see cref="TouchUi.Enabled"/>): floating stick (lower left),
    /// attack / dodge / five skills / interact / potions (lower right), menu and pause (top right). Everything reports to
    /// <see cref="InputReader"/>, so the game itself does not know about touch. Positions are in canvas units measured
    /// from the bottom-right corner of the HUD (the HUD already follows the safe area).
    /// </summary>
    public sealed partial class TouchControls : MonoBehaviour
    {
        static readonly Color Dark = new Color(0.07f, 0.09f, 0.14f, 0.55f);
        static readonly Color AttackTint = new Color(0.78f, 0.27f, 0.22f, 0.62f);
        static readonly Color UltTint = new Color(0.9f, 0.7f, 0.25f, 0.62f);

        // Centres from the bottom-right corner (x to the left is negative) and sizes (all >= 64, the touch minimum is ~70 with the gap).
        static readonly Vector2 AttackPos = new Vector2(-92f, 92f);
        const float AttackSize = 104f;
        static readonly Vector2 DodgePos = new Vector2(-214f, 52f);
        const float DodgeSize = 70f;
        static readonly Vector2[] SkillPos = { new Vector2(-228f, 136f), new Vector2(-196f, 212f), new Vector2(-130f, 262f), new Vector2(-60f, 270f), new Vector2(-46f, 178f) };
        const float SkillSize = 66f, UltSize = 78f;
        static readonly Vector2 InteractPos = new Vector2(-300f, 112f);
        const float InteractSize = 80f;
        const float PotionSize = 58f;

        RectTransform root;
        TouchStick stick;
        Image zone;

        readonly Image[] skillIcons = new Image[SkillGems.Slots];
        readonly Image[] skillCooldowns = new Image[SkillGems.Slots];
        readonly Image[] skillBgs = new Image[SkillGems.Slots];
        readonly Text[] skillTimers = new Text[SkillGems.Slots];
        readonly TouchButton[] skillButtons = new TouchButton[SkillGems.Slots];
        readonly int[] timerShown = new int[SkillGems.Slots];
        Image dodgeBg;
        Text dodgeTimer;
        int dodgeShown = int.MinValue;
        GameObject interactButton;
        readonly Image[] potionIcons = new Image[3];
        readonly Text[] potionCounts = new Text[3];
        static readonly string[] PotionIds = { ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll };
        static readonly GameAction[] PotionActions = { GameAction.UseItem, GameAction.UseMana, GameAction.TownScroll };

        /// <summary>Builds the controls inside the HUD (called at the end of HudView.Build).</summary>
        public static TouchControls Create(RectTransform hud)
        {
            var area = UIFactory.Stretch(UIFactory.Rect(hud, "TouchControls"));

            // The stick zone sits behind every HUD element (first child), so the chat tabs and other HUD buttons keep their taps.
            var zoneRt = UIFactory.Rect(hud, "TouchStickZone");
            zoneRt.SetAsFirstSibling();
            zoneRt.anchorMin = Vector2.zero;
            zoneRt.anchorMax = new Vector2(0.46f, 0.64f);
            zoneRt.offsetMin = zoneRt.offsetMax = Vector2.zero;
            // This object never switches off (the buttons' root does), so Update keeps running while the controls are hidden.
            var tc = zoneRt.gameObject.AddComponent<TouchControls>();
            tc.root = area;
            tc.zone = zoneRt.gameObject.AddComponent<Image>();
            tc.zone.color = Color.clear;
            tc.zone.raycastTarget = false;
            tc.stick = zoneRt.gameObject.AddComponent<TouchStick>();
            tc.stick.area = area;
            tc.stick.ring = Circle(area, "StickRing", 128f, new Color(1f, 1f, 1f, 0.22f));
            tc.stick.knob = Circle(area, "StickKnob", 60f, new Color(1f, 1f, 1f, 0.45f));
            tc.stick.ring.gameObject.SetActive(false);
            tc.stick.knob.gameObject.SetActive(false);

            tc.BuildRight();
            tc.BuildTop();
            tc.BuildMenu();
            return tc;
        }

        static RectTransform Circle(Transform parent, string name, float size, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get("ui_circle"), color);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        /// <summary>A round touch button anchored to the bottom-right corner.</summary>
        Image Round(Transform parent, string name, Vector2 pos, float size, Color tint)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get("ui_circle"), tint);
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            return img;
        }

        Text FittedLabel(Transform parent, string text, int size)
        {
            var label = UIFactory.Text(parent, "Label", text, size, Color.white, TextAnchor.MiddleCenter, true);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = label.fontSize;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(label.rectTransform, 4f, 4f, 4f, 4f);
            return label;
        }

        static TouchButton AddButton(Image bg, GameAction action)
        {
            var b = bg.gameObject.AddComponent<TouchButton>();
            b.action = action;
            return b;
        }

        void BuildRight()
        {
            var attack = Round(root, "Attack", AttackPos, AttackSize, AttackTint);
            AddButton(attack, GameAction.Attack).repeat = true;
            FittedLabel(attack.transform, "공격", 26);

            dodgeBg = Round(root, "Dodge", DodgePos, DodgeSize, Dark);
            AddButton(dodgeBg, GameAction.Mobility);
            FittedLabel(dodgeBg.transform, "회피", 22);
            dodgeTimer = UIFactory.Text(dodgeBg.transform, "Timer", "", 22, Color.white, TextAnchor.LowerCenter, true);
            UIFactory.Stretch(dodgeTimer.rectTransform, 0f, 2f, 0f, 0f);

            for (int i = 0; i < SkillGems.Slots; i++)
            {
                bool ult = i == SkillGems.UltimateSlot;
                var bg = Round(root, "Skill" + i, SkillPos[i], ult ? UltSize : SkillSize, ult ? UltTint : Dark);
                skillBgs[i] = bg;
                skillButtons[i] = AddButton(bg, SkillGems.ActionFor(i));
                skillIcons[i] = UIFactory.Image(bg.transform, "Icon", null, Color.white);
                UIFactory.Place(skillIcons[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2((ult ? UltSize : SkillSize) * 0.7f, (ult ? UltSize : SkillSize) * 0.7f));
                var cd = UIFactory.Image(bg.transform, "Cooldown", Game.Art.Get("ui_circle"), new Color(0f, 0f, 0f, 0.6f));
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                cd.fillAmount = 0f;
                UIFactory.Stretch(cd.rectTransform);
                skillCooldowns[i] = cd;
                skillTimers[i] = UIFactory.Text(bg.transform, "Timer", "", 24, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(skillTimers[i].rectTransform);
                timerShown[i] = int.MinValue;
            }

            var interact = Round(root, "Interact", InteractPos, InteractSize, new Color(0.25f, 0.6f, 0.35f, 0.65f));
            AddButton(interact, GameAction.Interact);
            FittedLabel(interact.transform, "상호작용", 22);
            interactButton = interact.gameObject;
            interactButton.SetActive(false);

            for (int i = 0; i < PotionIds.Length; i++)
            {
                var bg = Round(root, "Quick" + i, new Vector2(-300f - i * (PotionSize + 8f), 36f), PotionSize, Dark);
                AddButton(bg, PotionActions[i]);
                potionIcons[i] = UIFactory.Image(bg.transform, "Icon", Game.Art.Get(Game.Config.GetItem(PotionIds[i]).iconKey), Color.white);
                UIFactory.Place(potionIcons[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PotionSize * 0.68f, PotionSize * 0.68f));
                potionCounts[i] = UIFactory.Text(bg.transform, "Count", "", 22, Color.white, TextAnchor.LowerRight, true);
                UIFactory.Stretch(potionCounts[i].rectTransform, 2f, 0f, 4f, 0f);
            }
        }

        void Update()
        {
            var input = Game.Input;
            var player = Game.Player;
            bool visible = input != null && player != null && Game.Session != null && Game.IsPlaying && !InputReader.TextInputActive
                && (Game.Cutscenes == null || !Game.Cutscenes.IsPlaying) && (Game.Dungeon == null || !Game.Dungeon.ReviveOpen);
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (zone.raycastTarget != visible) zone.raycastTarget = visible;
            if (!visible)
            {
                stick.Release();
                CloseMenu();
                return;
            }
            UpdateSkills(player);
            UpdateDodge(player);
            UpdateQuick();
            bool canInteract = player.Interactor != null && player.Interactor.Current != null;
            if (interactButton.activeSelf != canInteract) interactButton.SetActive(canInteract);
        }

        void UpdateSkills(PlayerController player)
        {
            if (player.Skills == null) return;
            var prog = Game.Session.Progression;
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                var gem = prog.Active(i);
                var shown = gem ?? (i == SkillGems.UltimateSlot ? prog.IsPromoted ? CareerCatalog.For(prog.Career)[8].Gem : null : SkillGems.ForSlot(player.Class, i));
                bool open = gem != null;
                skillButtons[i].locked = !open;
                skillIcons[i].enabled = shown != null;
                if (shown != null) skillIcons[i].sprite = Game.Art.Get(shown.icon);
                if (!open)
                {
                    skillIcons[i].color = new Color(0.45f, 0.45f, 0.52f, 0.8f);
                    skillCooldowns[i].fillAmount = 0f;
                    skillTimers[i].text = "";
                    timerShown[i] = int.MinValue;
                    continue;
                }
                var n = player.Skills.Numbers(i);
                float p = player.Skills.CooldownProgress(i, out float left);
                skillCooldowns[i].fillAmount = 1f - p;
                int key = left > 0.05f ? (left >= 10f ? Mathf.RoundToInt(left) * 10 + 100000 : Mathf.RoundToInt(left * 10f)) : -1;
                if (key != timerShown[i])
                {
                    timerShown[i] = key;
                    skillTimers[i].text = left > 0.05f ? (left >= 10f ? $"{left:0}" : $"{left:0.0}") : "";
                }
                bool affordable = n.usesLife ? player.Health.Current > n.manaCost : player.Mana >= n.manaCost;
                skillIcons[i].color = affordable ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f);
            }
        }

        void UpdateDodge(PlayerController player)
        {
            float remaining = player.MobilityCooldownRemaining;
            int key = player.IsDashing ? -2 : Mathf.CeilToInt(remaining * 10f);
            if (key == dodgeShown) return;
            dodgeShown = key;
            dodgeTimer.text = remaining > 0f && !player.IsDashing ? remaining.ToString("0.0") : "";
            dodgeBg.color = remaining > 0f ? new Color(0.05f, 0.06f, 0.09f, 0.7f) : Dark;
        }

        void UpdateQuick()
        {
            var inventory = Game.Session.Inventory;
            for (int i = 0; i < PotionIds.Length; i++)
            {
                int count = inventory.Count(PotionIds[i]);
                potionCounts[i].text = count.ToString();
                potionIcons[i].color = count > 0 ? Color.white : new Color(0.4f, 0.4f, 0.46f, 0.8f);
                potionCounts[i].color = count > 0 ? Color.white : new Color32(255, 120, 120, 255);
            }
        }
    }
}
