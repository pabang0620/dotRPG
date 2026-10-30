using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class GachaScreen
    {
        /// <summary>모두 열기 (multi-draws): every closed card turns up at once, 유니크 / 레전더리 too, without the charge-up,
        /// the clip or the spotlight (bonghwa reveal() allBtn). One burst for the best of them.</summary>
        void RevealAll()
        {
            if (!Revealing || spot != null) return;
            EndIntro();
            bool top = false, epic = false;
            var opened = new List<int>();
            for (int i = 0; i < shown.Count; i++)
            {
                if (i >= revealed || held[i]) opened.Add(i);
                else continue;
                held[i] = false;
                chargeEnd[i] = 0f;
                if (IsTop(shown[i])) top = true;
                else if (IsEpic(shown[i])) epic = true;
                else continue;
                waiting = i;
            }
            revealed = shown.Count;
            openQueue.Clear();
            if (top || epic)
            {
                flashAt = punchAt = Time.unscaledTime;
                flashColor = top ? new Color(1f, .85f, .45f) : new Color(.78f, .55f, 1f);
                Game.Audio.PlaySfx(top ? "quest" : "confirm");
                Game.Camera?.Shake(top ? 0.14f : 0.08f, 0.3f);
                foreach (int i in opened)
                    if (IsTop(shown[i])) Burst(i, new Color(1f, .82f, .35f), 20);
                    else if (IsEpic(shown[i])) Burst(i, new Color(.78f, .55f, 1f), 16);
            }
            else Game.Audio.PlaySfx("select");
            DrawCells();
        }

        /// <summary>One big card for a single draw; two rows of five plus the separate bonus card for 10+1.</summary>
        void LayoutCells(int count)
        {
            bool single = count <= 1;
            float rowsW = 5 * CellW + 4 * 14f, gap = 60f;
            float startX = single ? (1000f - 190f) / 2f : (1000f - (rowsW + gap + BonusW)) / 2f;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                Vector2 pos, size;
                if (single) { pos = new Vector2(startX, -60f); size = new Vector2(190f, 250f); }
                else if (i < 10) { pos = new Vector2(startX + (i % 5) * (CellW + 14f), -40f - (i / 5) * (CellH + 18f)); size = new Vector2(CellW, CellH); }
                else { pos = new Vector2(startX + rowsW + gap, -40f - (2 * CellH + 18f - BonusH) / 2f); size = new Vector2(BonusW, BonusH); }
                UIFactory.Place(c.rt, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), pos + new Vector2(size.x / 2f, -size.y / 2f), size);
                cellBase[i] = c.rt.anchoredPosition; // [GACHA VFX] the nudge shakes around this
                UIFactory.Place(c.halo.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), pos + new Vector2(size.x / 2f, -size.y / 2f), size * 2f);
                if (i == 10) UIFactory.Place(bonusLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), pos + new Vector2(0f, 6f), new Vector2(BonusW, 28f));
            }
        }

        /// <summary>The announced results: 유니크 (aura / skin) or Unique / Legendary equipment.</summary>
        static bool IsTop(StarPullResult r) => r.rarity == "unique" || r.rarity == "legend" || r.rarity == "legendary" || (!r.gear && !r.cash && r.hidden && r.rarity == "epic");
        /// <summary>An 에픽 aura or skin: its own purple reveal (에픽 equipment is common enough to stay blue).</summary>
        static bool IsEpic(StarPullResult r) => !r.gear && r.rarity == "epic";
        static bool IsGood(StarPullResult r) => r.rarity == "rare" || r.rarity == "epic";
        static bool Special(StarPullResult r) => IsTop(r) || IsEpic(r);

        /// <summary>Result i sits in cell i; a single draw lands in the first cell, a 10+1 fills ten plus the bonus.</summary>
        int CellOf(int i) => i;

        /// <summary>Shrinks the fixed-size shop layout to the window (never enlarges it).</summary>
        void FitMain()
        {
            if (main == null) return;
            var r = content.rect;
            float k = Mathf.Min(1f, r.width / MainW, r.height / MainH);
            if (!Mathf.Approximately(main.localScale.x, k)) main.localScale = new Vector3(k, k, 1f);
        }

        protected override void Update()
        {
            FitMain();
            PadNavigate(); // first: a result page or modal clears the pad selection before A is read below
            if (rateModal != null && rateModal.gameObject.activeSelf && Game.Input.CancelPressed) { CloseRates(); return; }
            if (choiceModal != null && choiceModal.gameObject.activeSelf && Game.Input.CancelPressed) { choiceModal.gameObject.SetActive(false); return; }
            if (resultPage != null && resultPage.gameObject.activeSelf && (Game.Input.CancelPressed || Game.Input.SubmitPressed))
            { ResultKey(Game.Input.SubmitPressed); Animate(); return; } // [GACHA VFX] never opens the top cards by itself
            base.Update();
            if (!gameObject.activeSelf) return;
            Animate();
            UpdateAuto(); // [GACHA 2026-10-29] 자동 뽑기: the next draw once this reveal is over
            UpdateIntro(); // [GACHA VFX] the opening clip: tap to skip, 2 s at most
            UpdateCards(); // [GACHA VFX] charge-ups, the spotlight
            // [GACHA VFX] Dealing (bonghwa deal()): the lower grades turn up by themselves, 0.35 s + 0.09 s per card;
            // 유니크 / 레전더리 stay face-down with the gold glow until they are clicked (자동 뽑기 opens them itself).
            if (revealed >= shown.Count || Time.unscaledTime < revealAt + DealFirst + revealed * DealStep) return;
            int at = revealed;
            var r = shown[at];
            revealed++;
            if (ClipTier(r) > 0)
            {
                held[at] = true;
                if (batchAuto) QueueOpen(at);
                DrawCells();
                return;
            }
            waiting = at; // the punch (Animate) lands on an 에픽 / hidden card
            Announce(CellOf(at), r);
            DrawCells();
        }

        /// <summary>The flash, sparks, sound and toast of one card turning face up.</summary>
        void Announce(int cell, StarPullResult r)
        {
            if (IsTop(r))
            {
                flashAt = punchAt = Time.unscaledTime;
                bool legend = r.rarity == "legend";
                flashColor = legend ? new Color(1f, .45f, .4f) : new Color(1f, .85f, .45f);
                Burst(cell, legend ? new Color(1f, .5f, .42f) : new Color(1f, .82f, .35f), legend ? 40 : 26);
                Game.Audio.PlaySfx("quest");
                Game.Camera?.Shake(0.14f, 0.3f);
                string grade = r.rarity == "legendary" ? L.T("레전더리") : r.rarity == "legend" ? L.T("전설") : r.rarity == "epic" ? L.T("에픽") : L.T("유니크");
                if (r.hidden) grade = L.F("{0} 히든", grade);
                GameEvents.RaiseToast($"<color=#ffb347>{grade}!</color> {NameOf(r)}" + (r.duplicate ? "" : L.T(" 획득")));
            }
            else if (IsEpic(r))
            {
                flashAt = punchAt = Time.unscaledTime;
                flashColor = new Color(.78f, .55f, 1f);
                Burst(cell, new Color(.78f, .55f, 1f), 20);
                Game.Audio.PlaySfx("quest");
                Game.Camera?.Shake(0.08f, 0.25f);
                GameEvents.RaiseToast(L.F("<color=#c58cff>에픽!</color> {0}", NameOf(r)) + (r.duplicate ? "" : L.T(" 획득")));
            }
            else if (r.hidden && !r.gear && !r.cash)
            {
                Burst(cell, new Color(1f, .89f, .48f), 16);
                Game.Audio.PlaySfx("quest");
                GameEvents.RaiseToast(L.F("<color=#ffe27a>히든!</color> {0}", NameOf(r)) + (r.duplicate ? "" : L.T(" 획득")));
            }
            else if (IsGood(r)) { Burst(cell, new Color(.5f, .7f, 1f), 12); Game.Audio.PlaySfx("confirm"); }
            else Game.Audio.PlaySfx("select");
        }

        /// <summary>Sparks flying out of a card.</summary>
        void Burst(int cell, Color color, int count)
        {
            if (!GachaClip.Enabled) return; // [GACHA VFX] reduce motion (screen shake off): no sparks
            var c = cells[cell];
            var center = (Vector2)c.rt.anchoredPosition;
            for (int i = 0; i < count; i++)
            {
                var img = UIFactory.Image(cellRoot, "Spark", Game.Art.Get("ui_white"), color);
                img.raycastTarget = false;
                float a = i * Mathf.PI * 2f / count + Random.Range(-.2f, .2f);
                float speed = Random.Range(160f, 360f);
                float size = Random.Range(4f, 9f);
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), center, new Vector2(size, size));
                sparks.Add(new Spark { img = img, vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed, life = Random.Range(.5f, .9f) });
            }
        }

        void Animate()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (chooseBtn != null && chooseBtn.gameObject.activeSelf)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(t * 5f);
                gaugeFill.color = Color.Lerp(UiTheme.Accent, Color.white, k * 0.6f);
                chooseBtn.transform.localScale = Vector3.one * (1f + 0.05f * k);
            }
            else if (gaugeFill != null) gaugeFill.color = UiTheme.Accent;
            float f = Mathf.Clamp01(1f - (t - flashAt) / 0.8f);
            flash.color = new Color(flashColor.r, flashColor.g, flashColor.b, f * f * .8f);
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.age += dt;
                if (s.age >= s.life || s.img == null) { if (s.img != null) Destroy(s.img.gameObject); sparks.RemoveAt(i); continue; }
                s.vel *= 1f - 2.2f * dt;
                s.img.rectTransform.anchoredPosition += s.vel * dt;
                var col = s.img.color; col.a = 1f - s.age / s.life; s.img.color = col;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                float scale = 1f;
                c.rt.localRotation = Quaternion.identity;
                float nudge = t - nudgeAt[i];
                c.rt.anchoredPosition = cellBase[i] + new Vector2(nudge < 0.4f ? Mathf.Sin(nudge / 0.4f * Mathf.PI * 2f) * 6f : 0f, 0f);
                if (i < shown.Count && held[i])
                {
                    // [GACHA VFX] face-down 유니크 / 레전더리: one gold glow for both (.card.hi); charging or a teased 레전더리
                    // glows brighter and trembles hard (.charging)
                    bool hot = teased[i] || chargeEnd[i] > 0f;
                    float k = 0.5f + 0.5f * Mathf.Sin(t * (hot ? 26f : 6f));
                    c.halo.color = new Color(1f, .8f, .3f, .55f + .45f * k);
                    c.bg.color = Color.Lerp(HeadRow, new Color32(150, 108, 30, 255), k);
                    scale = (chargeEnd[i] > 0f ? 1.12f : 1.04f) + .04f * k;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * (hot ? 60f : 18f)) * (hot ? 3f : 1.2f));
                }
                else if (i == waiting && i >= revealed && i < shown.Count)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 26f);
                    bool gold = IsTop(shown[i]);
                    c.halo.color = gold ? new Color(1f, .8f, .3f, .55f + .45f * k) : new Color(.75f, .5f, 1f, .5f + .4f * k);
                    c.bg.color = Color.Lerp(HeadRow, gold ? new Color32(150, 108, 30, 255) : new Color32(96, 54, 150, 255), k);
                    scale = 1.08f + .05f * k;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 3f);
                }
                else if (i < revealed && i < shown.Count)
                {
                    var r = shown[i];
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 3f + i);
                    c.halo.color = IsTop(r) ? new Color(1f, .78f, .3f, .5f + .35f * k) : IsEpic(r) ? new Color(.75f, .5f, 1f, .4f + .3f * k)
                        : IsGood(r) ? new Color(.45f, .65f, 1f, .2f + .15f * k) : Color.clear;
                    if (Special(r) && i == waiting) scale = 1f + 0.4f * Mathf.Clamp01(1f - (t - punchAt) / 0.35f);
                    if (!r.gear)
                    {
                        var p = CosmeticCatalog.Find(r.itemId);
                        if (p != null && p.Effect != CosmeticEffect.None) c.icon.color = p.ColorAt(t);
                    }
                }
                else c.halo.color = Color.clear;
                c.rt.localScale = new Vector3(scale, scale, 1f);
            }
        }

        static string NameOf(StarPullResult r)
        {
            if (r.cash) return L.T(DungeonDatabase.ItemName(r.itemId)) + (r.count > 1 ? $" x{r.count}" : "");
            if (r.gear)
            {
                var g = EquipmentDatabase.Get(r.itemId);
                return L.T(g != null ? g.name : DungeonDatabase.ItemName(r.itemId));
            }
            return L.T(CosmeticCatalog.Find(r.itemId)?.Name ?? r.itemId);
        }

        static string GradeHex(string r) =>
            r == "legendary" ? "#ff8c3a" : r == "legend" ? "#ff6b5e" : r == "unique" ? "#ffb347" : r == "epic" ? "#c58cff" : r == "rare" ? "#9fc4ff" : r == "uncommon" ? "#8fe28f" : "#cfd6e2";

        void DrawCells()
        {
            int count = shown.Count;
            bonusLabel.gameObject.SetActive(count > 1);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool used = i < count;
                c.bg.gameObject.SetActive(used);
                c.halo.gameObject.SetActive(c.bg.gameObject.activeSelf);
                bool on = used && IsOpen(i);
                c.icon.enabled = on;
                c.frame.color = Color.clear;
                if (!on)
                {
                    bool wait = used && held[i];
                    c.bg.color = used ? HeadRow : new Color32(20, 28, 42, 160);
                    // a face-down top card says it waits for a tap (two lines fit the name area in every language)
                    c.name.text = wait ? "<color=#ffd34a><b>" + L.T("눌러서 열기") + "</b></color>" : used ? "<color=#8c96a8>?</color>" : "";
                    c.note.text = "";
                    continue;
                }
                var r = shown[i];
                string hex = GradeHex(r.rarity);
                c.bg.color = IsTop(r) ? new Color32(110, 76, 22, 245) : IsEpic(r) ? new Color32(72, 40, 112, 245) : IsGood(r) ? new Color32(36, 56, 104, 245) : RowB;
                c.frame.color = PixelHex(hex);
                if (r.cash)
                {
                    var item = ConsumableDatabase.Get(r.itemId);
                    c.icon.sprite = Game.Art.Get(item != null ? item.iconKey : DungeonDatabase.ItemIcon(r.itemId));
                    c.icon.color = Color.white;
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = r.boosted ? L.T("<color=#ffd34a><b>부스터 x2</b></color>") : "";
                }
                else if (r.gear)
                {
                    var g = EquipmentDatabase.Get(r.itemId);
                    c.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(r.itemId));
                    c.icon.color = Color.white;
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = g != null ? $"<color={hex}>{EquipmentDatabase.RarityName(g.rarity)}</color>" : "";
                }
                else
                {
                    var p = CosmeticCatalog.Find(r.itemId);
                    var look = p != null && p.IsSkin ? SkinCatalog.LookFor(p.Skin.cls, p.Id) : null;
                    var card = CosmeticAura.Card(p);
                    c.icon.sprite = card ?? (look != null ? Game.Art.GetCharacter(look, "down", "idle0") : CosmeticAura.ForProduct(p));
                    c.icon.color = card != null || look != null ? Color.white : p != null ? p.Color : Color.white;
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = (r.hidden ? L.T("<color=#ffe27a><b>히든</b></color> ") : "") + (r.duplicate ? L.T("<color=#b8c4d8>여분 +1</color>")
                        : r.byPity ? L.T("<color=#ffd34a>선택 · NEW</color>") : "<color=#8fe28f>NEW</color>");
                }
            }
            RefreshLocks(); // [GACHA VFX]
        }

        static Color PixelHex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
