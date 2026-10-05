using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    // Opt-in local training mode. The dedicated player includes a marker; production does not.
    public sealed class GuardianLab : MonoBehaviour
    {
        public static bool Enabled => File.Exists(Path.Combine(Application.streamingAssetsPath, "guardian-lab.txt")) || Has("-guardianLab");
        static bool Has(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GuardianLab"));
        public static void Configure()
        {
            if (!Enabled) return;
            Directory.CreateDirectory(Root);
            SaveSystem.DirectoryOverride = Path.Combine(Root, "saves");
            GameFlow.PauseOnFocusLoss = false;
            Application.runInBackground = true;
            QuestManager.StoryEnabled = false;
            AudioListener.volume = 0;
        }
        public static bool TryStart()
        {
            if (!Enabled) return false;
            var go = new GameObject("Offline Guardian Lab");
            DontDestroyOnLoad(go); go.AddComponent<GuardianLab>(); return true;
        }
        readonly List<EnemyController> targets = new List<EnemyController>();
        readonly string[] normal = { "g_guard", "g_wall", "g_oath", "g_taunt", "g_bash", "g_counter" };
        Vector2 home;
        bool ready, resetting, captureBusy;
        string message = "수호자 연습장을 준비하고 있습니다.";
        float nextRestore;
        bool refill = true;
        Text statusText, restoreText;
        RectTransform labPanel;
        int passed, failed;
        readonly List<string> checks = new List<string>();

        IEnumerator Start()
        {
            while (Game.Flow == null || Game.UI == null) yield return null;
            Configure(); Game.Config.autosave = false;
            Game.Audio?.SetVolumes(0, 0); AudioListener.volume = 0;
            yield return new WaitForSecondsRealtime(.4f);
            Screen.SetResolution(1440, 900, false);
            yield return Setup();
            if (Has("-guardianVerify")) { yield return Verify(); Application.Quit(failed == 0 ? 0 : 1); }
        }
        IEnumerator Setup()
        {
            resetting = true; ready = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "수호자 테스트");
            yield return new WaitForSecondsRealtime(.15f);
            while (Game.Flow.IsTransitioning) yield return null;
            var player = Game.Player; var prog = player.Data.Progression;
            prog.SetFromServer(40, 0); prog.Promote(Career.Guardian);
            foreach (var s in CareerCatalog.For(Career.Guardian)) if (s.kind != CareerSkillKind.Awakening) { prog.Learn(s.id); prog.Learn(s.id); }
            for (int stage = 0; stage < 5; stage++) prog.AdvanceAwakening(stage);
            string[] loadout = { "g_wall", "g_taunt", "g_bash", "g_counter", "g_awake" };
            for (int i = 0; i < loadout.Length; i++) prog.EquipSkill(i, loadout[i]);
            foreach (var gear in new[] { "eq_sword_iron+7", "eq_top_iron", "eq_bot_leather" })
            { Game.Session.Inventory.Add(gear, 1); Game.Session.Equipment.Equip(gear, player.Class); }
            player.HealFull(); player.Data.Mana = player.MaxMana; player.Skills.ResetCooldowns();
            home = player.Position; player.Place(home, Facing.Right);
            RespawnTargets();
            Game.UI.Hud.ClearToasts();
            if (labPanel == null) BuildPanel();
            Game.Audio?.SetVolumes(0, 0); AudioListener.volume = 0;
            ready = true; resetting = false;
            message = "서버 연결 없이 준비 완료 · T로 천쇄방패";
            Game.Saves.Write(Game.Session.Capture(player.Position, player.Facing));
            File.WriteAllText(Path.Combine(Root, "ready.txt"), "Offline Guardian ready / level 40 / awakened / " + DateTime.Now.ToString("s"));
        }
        EnemyController Dummy(Vector2 at)
        {
            var stats = Instantiate(Game.Config.skeletonStats);
            stats.maxHealth = 100000; stats.attackDamage = stats.xpReward = 0;
            stats.wanderSpeed = stats.chaseSpeed = stats.knockbackSpeed = 0; stats.invulnerableTime = .001f;
            var e = EnemyController.Create(stats, CharacterLook.Skeleton, at, Game.World.ObjectsRoot);
            e.transform.position += (Vector3)(at - e.Center);
            var body = e.GetComponent<Rigidbody2D>(); if (body != null) { body.position = e.transform.position; body.simulated = false; }
            e.Health.Damaged += info => { message = "적중 피해 " + info.amount + " · F5로 즉시 재사용"; };
            targets.Add(e); Destroy(stats, 2); return e;
        }
        void ClearTargets()
        { foreach (var e in targets) if (e != null) Destroy(e.gameObject); targets.Clear(); }
        void RespawnTargets()
        {
            if (Game.Player == null) return;
            ClearTargets(); var at = Game.Player.Center;
            for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; Dummy(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.4f); }
            Dummy(at + Vector2.right * 5.7f); Dummy(at + Vector2.right * 7f);
        }
        void ResetPractice()
        {
            if (!ready || resetting) return;
            var p = Game.Player;
            if (p.Skills.IsCasting) { message = "시전이 끝난 뒤 초기화할 수 있어요."; return; }
            CareerCombat.For(p).ResetState(); p.Skills.ResetCooldowns(); p.HealFull(); p.Data.Mana = p.MaxMana;
            foreach (var e in targets) if (e != null && !e.IsDead) e.Health.Heal(e.Health.Max);
            message = "HP·MP·재사용 대기시간·표적 체력 초기화";
        }
        void Cast(string id)
        {
            if (!ready || !Game.IsPlaying || Game.Player.Skills.IsCasting) return;
            int slot = id == "g_awake" ? 4 : 0;
            var prog = Game.Player.Data.Progression; string previous = prog.Active(slot)?.id;
            // Equipping may reject a duplicate in another slot: use that slot instead.
            for (int i = 0; i < 5; i++) if (prog.Active(i)?.id == id) { Game.Player.Skills.TryCast(i); return; }
            if (!prog.EquipSkill(slot, id)) return;
            Game.Player.Skills.TryCast(slot);
            if (previous != null) prog.EquipSkill(slot, previous);
        }
        void Update()
        {
            if (statusText != null) statusText.text = message;
            if (restoreText != null) restoreText.text = refill ? "HP·MP 자동 회복: 켜짐" : "HP·MP 자동 회복: 꺼짐";
            if (Has("-guardianVerify")) return;
            if (ready && Game.IsPlaying)
            {
                if (refill && Time.unscaledTime >= nextRestore) { nextRestore = Time.unscaledTime + .3f; Game.Player.HealFull(); Game.Player.Data.Mana = Game.Player.MaxMana; }
                if (Input.GetKeyDown(KeyCode.F5)) ResetPractice();
                if (Input.GetKeyDown(KeyCode.F6)) RespawnTargets();
                if (Input.GetKeyDown(KeyCode.F7) && !Game.Player.Skills.IsCasting) { Game.Player.Place(home, Facing.Right); RespawnTargets(); }
                if (Input.GetKeyDown(KeyCode.F8) && !captureBusy) StartCoroutine(Capture("manual-" + DateTime.Now.ToString("HHmmss")));
            }
        }
        void BuildPanel()
        {
            var go = new GameObject("Guardian Lab UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900); scaler.matchWidthOrHeight = .5f;
            labPanel = UIFactory.Rect(go.transform, "Training controls");
            labPanel.anchorMin = labPanel.anchorMax = new Vector2(1, 1); labPanel.pivot = new Vector2(1, 1);
            labPanel.anchoredPosition = new Vector2(-12, -155); labPanel.sizeDelta = new Vector2(286, 560);
            var bg = labPanel.gameObject.AddComponent<Image>(); bg.color = new Color32(10, 21, 33, 249);
            PanelText("Title", "수호자 · 오프라인 연습장", 14, 24, 23, new Color32(127, 237, 249, 255));
            PanelText("Instructions", "Lv.40 / 각성 완료 / 서버 불필요\n방향키 이동 · X 공격 · Shift 이동\nQ 투척  W 도발  E 올려치기\nR 반격  T 광역 각성", 48, 90, 17, Color.white);
            AddButton("T  천쇄방패 · 광역 공격", 146, () => Cast("g_awake"), true);
            AddButton("강철의 보루", 190, () => Cast("g_guard"));
            AddButton("동행의 보루", 228, () => Cast("g_oath"));
            AddButton("F5  체력·마나·쿨다운 초기화", 270, ResetPractice);
            AddButton("F6  주변에 표적 다시 배치", 308, RespawnTargets);
            AddButton("F7  시작 위치로 복귀", 346, () => { if(ready && Game.IsPlaying){Game.Player.Place(home,Facing.Right);RespawnTargets();}else if(!resetting)StartCoroutine(Setup()); });
            AddButton("F8  화면 저장", 384, () => { if(!captureBusy)StartCoroutine(Capture("manual-"+DateTime.Now.ToString("HHmmss"))); });
            restoreText = AddButton("HP·MP 자동 회복: 켜짐", 422, () => refill = !refill);
            statusText = PanelText("Status", message, 466, 58, 16, new Color32(160, 220, 235, 255));
            PanelText("Hint", "표적 피해 0 · 범위 밖 표적 포함", 528, 22, 14, Color.gray);
        }
        Text PanelText(string name, string content, float top, float height, int size, Color color)
        {
            var text = UIFactory.Text(labPanel, name, content, size, color, TextAnchor.UpperLeft);
            var rt = text.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0,1); rt.pivot = new Vector2(0,1);
            rt.anchoredPosition = new Vector2(12,-top); rt.sizeDelta = new Vector2(262,height); return text;
        }
        Text AddButton(string caption, float top, UnityEngine.Events.UnityAction action, bool accent=false)
        {
            var rt = UIFactory.Rect(labPanel, caption); rt.anchorMin = rt.anchorMax = new Vector2(0,1); rt.pivot = new Vector2(0,1);
            rt.anchoredPosition = new Vector2(12,-top); rt.sizeDelta = new Vector2(262,34);
            var image = rt.gameObject.AddComponent<Image>(); image.color = accent ? new Color32(19,100,120,255) : new Color32(28,48,66,255);
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var text = UIFactory.Text(rt,"Label",caption,17,Color.white,TextAnchor.MiddleCenter); UIFactory.Stretch(text.rectTransform); return text;
        }
        IEnumerator Capture(string name)
        {
            captureBusy = true; Directory.CreateDirectory(Path.Combine(Root, "captures"));
            yield return new WaitForEndOfFrame();
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Root, "captures", name + ".png"), image.EncodeToPNG()); Destroy(image);
            captureBusy = false;
        }
        void Check(string name, bool ok)
        { if (ok) passed++; else failed++; checks.Add((ok ? "PASS " : "FAIL ") + name); Debug.Log("GuardianLab " + checks[checks.Count - 1]); }
        IEnumerator Verify()
        {
            yield return new WaitForSeconds(.5f);
            var p = Game.Player; var state = CareerCombat.For(p); var prog = p.Data.Progression; var s = CareerCatalog.Get("g_awake");
            Check("offline without server or party", !Game.IsOnlineWorld && !PartyNet.IsMember && OnlineSession.Current == null);
            Check("isolated save location", SaveSystem.DirectoryOverride == Path.Combine(Root, "saves"));
            Check("level40 guardian and awakening equipped", prog.Level == 40 && prog.Career == Career.Guardian && prog.Awakened && prog.Active(4)?.id == s.id);
            Check("damage awakening metadata", s.name == "천쇄방패" && s.power == 12 && s.radius == 6 && s.cooldown == 70 && s.mp == 50 && s.effect == "shieldquake");
            for (int row = 0; row < 4; row++) for (int cel = 0; cel < 4; cel++) { var sprite = CareerPulseArt.Frame(Career.Guardian, row, cel); Check("enhanced sprite " + row + ":" + cel, sprite != null && sprite.texture.filterMode == FilterMode.Point); }
            yield return Capture("01-ready");
            ClearTargets(); yield return null;
            Vector2 origin = p.Center;
            var inside = new[] { Dummy(origin + Vector2.right * 2), Dummy(origin + Vector2.left * 3), Dummy(origin + Vector2.up * 3), Dummy(origin + Vector2.down * 3), Dummy(origin + Vector2.right * 5.9f) };
            var outside = Dummy(origin + Vector2.right * 6.2f);
            var hits = new int[inside.Length]; for (int i = 0; i < inside.Length; i++) { int index = i; inside[i].Health.Damaged += info => hits[index]++; }
            var immune = Dummy(origin + new Vector2(-2, 2)); immune.Health.SetInvulnerable(4);
            p.Skills.ResetCooldowns(); p.HealFull(); p.Data.Mana = p.MaxMana; float mana = p.Data.Mana;
            p.Skills.TryCast(4);
            Check("mana paid through normal casting", p.Data.Mana < mana);
            yield return new WaitForSeconds(.25f); Check("windup has no early damage", inside[0].Health.Current == inside[0].Health.Max);
            yield return Capture("02-charge");
            yield return new WaitForSeconds(.47f); yield return Capture("03-impact");
            yield return new WaitForSeconds(.1f); yield return Capture("04-wave");
            yield return new WaitForSeconds(.75f);
            for (int i = 0; i < hits.Length; i++) Check("AOE single hit direction " + i, hits[i] == 1);
            Check("outside6m excluded", outside.Health.Current == outside.Health.Max);
            Check("invulnerability respected", immune.Health.Current == immune.Health.Max);
            Check("no obsolete shield guard or forced taunt", state.Shield == 0 && !state.GuardVisible && ThreatTable.For(inside[0]).Forced == null);
            Check("cooldown retained", !p.Skills.IsReady(4));
            yield return new WaitForSeconds(.3f); Check("awakening visuals cleaned", GuardianAwakeningFx.Count == 0);
            ResetPractice(); Check("practice reset clears cooldown", p.Skills.IsReady(4));
            p.Skills.TryCast(4); yield return new WaitForSeconds(.15f); state.ResetState();
            int before = inside[0].Health.Current;
            yield return new WaitForSeconds(.85f); Check("reset cancels pending damage and charge", before == inside[0].Health.Current && GuardianAwakeningFx.Count == 0);
            p.Skills.ResetCooldowns(); p.HealFull(); p.Data.Mana = p.MaxMana;
            p.Data.Mana = 0; p.Skills.TryCast(4); yield return null;
            Check("insufficient mana rejected", !p.Skills.IsCasting && GuardianAwakeningFx.Count == 0); p.HealFull(); p.Data.Mana = p.MaxMana;
            // Save compatibility and both actual locked-cast paths.
            var saved = Game.Session.Capture(p.Position, p.Facing); Game.Saves.Write(saved);
            var restored = new Progression(); restored.Restore(Game.Saves.Read(), CharacterClass.Warrior);
            Check("save roundtrip retains awakening", restored.Career == Career.Guardian && restored.Awakened);
            var lockSave = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(saved)); lockSave.career.awakened = false; lockSave.career.questStage = 2;
            prog.Restore(lockSave, CharacterClass.Warrior);
            yield return state.Cast(s, default); Check("unfinished quest rejects actual cast", GuardianAwakeningFx.Count == 0 && !prog.CareerUnlocked(s));
            lockSave.career = new CareerSave(); prog.Restore(lockSave, CharacterClass.Warrior);
            yield return state.Cast(s, default); Check("base class rejects actual cast", GuardianAwakeningFx.Count == 0 && !prog.CareerUnlocked(s));
            prog.Restore(saved, CharacterClass.Warrior);
            GuardianAwakeningFx.Impact(p, p.Center, 6); string map = Game.Session.MapId; Game.Session.MapId = "guardian-test-map-change";
            yield return null; yield return null; Check("map change cleans new VFX", GuardianAwakeningFx.Count == 0); Game.Session.MapId = map;
            foreach (string id in normal)
            {
                state.ResetState(); p.Skills.ResetCooldowns(); p.HealFull(); p.Data.Mana = p.MaxMana;
                var skill = CareerCatalog.Get(id); int count = CareerRenewalFx.Started;
                Cast(id); yield return new WaitForSeconds(skill.cast + .16f);
                Check("normal skill graphics connected " + id, CareerRenewalFx.Started > count);
                if (id == "g_guard") Check("guard protection retained", state.GuardVisible);
                if (id == "g_wall" || id == "g_oath") Check("normal shield retained " + id, state.Shield > 0);
                yield return Capture(id);
                yield return new WaitForSeconds(.5f);
            }
            state.ResetState(); p.Skills.ResetCooldowns(); p.HealFull(); p.Data.Mana = p.MaxMana;
            yield return new WaitForSeconds(.4f);
            Check("all guardian effects clean after reset", GuardianAwakeningFx.Count == 0 && CareerRenewalFx.Count == 0);
            Check("mute verification", AudioListener.volume == 0);
            File.WriteAllLines(Path.Combine(Root, "verification.txt"), checks);
            File.AppendAllText(Path.Combine(Root, "verification.txt"), "\nRESULT " + passed + " passed, " + failed + " failed\n");
        }
    }
}
