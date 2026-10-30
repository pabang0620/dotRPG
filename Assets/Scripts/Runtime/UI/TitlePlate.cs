using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TITLE PLATE 2026-10-30] The equipped title drawn over a character's head: a small ranking mark (world boss 1st / top 10 /
    /// top 100, guild boss 1st) and the title text, on its own line above the name tag. Nothing is drawn without a title.
    /// Town peers set it from <c>town.pos</c> (<see cref="TownPeers"/>); my character and the human party members in the field
    /// are kept by <see cref="TitlePlates"/>. Marks are optional sprites (Resources/Art/UI/rank_mark_*), skipped when missing.
    /// </summary>
    public sealed class TitlePlate : MonoBehaviour
    {
        /// <summary>Title text size: a bit smaller than the name (0.045) and larger than the guild line (0.036).</summary>
        const float CharSize = 0.04f;
        const int FontSize = 48;
        /// <summary>Height of the mark in world units (about the text's cap height).</summary>
        const float MarkSize = 0.2f;
        static readonly Vector2[] OutlineDirs = { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
        const float OutlineStep = 0.012f;

        TextMesh text;
        readonly TextMesh[] outline = new TextMesh[4];
        SpriteRenderer mark;
        string shownId = "", shownTitle = "";

        /// <summary>Ranking mark sprite key for a title id (null = an achievement title, no mark).</summary>
        public static string MarkKey(string titleId) => titleId switch
        {
            "rank_world_1" => "UI/rank_mark_world_1",
            "rank_world_10" => "UI/rank_mark_world_10",
            "rank_world_100" => "UI/rank_mark_world_100",
            "rank_guild_1" => "UI/rank_mark_guild_1",
            _ => null,
        };

        /// <summary>Title colour by rank: gold 1st, orange top 10, silver top 100, guild gold; achievement titles light blue.</summary>
        public static Color32 TitleColor(string titleId) => titleId switch
        {
            "rank_world_1" => new Color32(255, 211, 74, 255),
            "rank_world_10" => new Color32(255, 159, 74, 255),
            "rank_world_100" => new Color32(214, 222, 233, 255),
            "rank_guild_1" => new Color32(255, 224, 102, 255),
            _ => new Color32(184, 216, 255, 255),
        };

        /// <summary>The plate on this body (made on first use). Null title = hide it.</summary>
        public static TitlePlate On(Transform body)
        {
            if (body == null) return null;
            var t = body.Find("TitlePlate");
            if (t != null) return t.GetComponent<TitlePlate>();
            var go = new GameObject("TitlePlate");
            go.transform.SetParent(body, false);
            return go.AddComponent<TitlePlate>();
        }

        /// <summary>Show this title at this height over the feet (line bottom). Empty id or text hides the plate.</summary>
        public void Set(string titleId, string title, float y)
        {
            transform.localPosition = new Vector3(0f, y, 0f);
            titleId ??= "";
            title ??= "";
            if (titleId == shownId && title == shownTitle) return;
            shownId = titleId;
            shownTitle = title;
            bool has = titleId.Length > 0 && title.Length > 0;
            if (!has)
            {
                if (text != null) text.gameObject.SetActive(false);
                foreach (var o in outline) if (o != null) o.gameObject.SetActive(false);
                if (mark != null) mark.gameObject.SetActive(false);
                return;
            }
            EnsureMade();
            string shown = L.T(title);
            text.text = shown;
            text.color = TitleColor(titleId);
            text.gameObject.SetActive(true);
            foreach (var o in outline) { o.text = shown; o.gameObject.SetActive(true); }

            string key = MarkKey(titleId);
            var sprite = key != null && Game.Art != null ? Game.Art.Optional(key) : null;
            if (sprite == null)
            {
                if (mark != null) mark.gameObject.SetActive(false);
                text.transform.localPosition = Vector3.zero;
                for (int i = 0; i < outline.Length; i++) outline[i].transform.localPosition = (Vector3)(OutlineDirs[i] * OutlineStep);
                return;
            }
            if (mark == null)
            {
                mark = new GameObject("Mark").AddComponent<SpriteRenderer>();
                mark.transform.SetParent(transform, false);
                mark.sortingOrder = 442;
            }
            mark.sprite = sprite;
            float scale = MarkSize / Mathf.Max(0.01f, sprite.bounds.size.y);
            mark.transform.localScale = new Vector3(scale, scale, 1f);
            mark.gameObject.SetActive(true);
            Layout();
        }

        // The text mesh's width is only certain after it is rebuilt: lay the mark and text out again every frame while a mark shows.
        void LateUpdate()
        {
            if (mark != null && mark.gameObject.activeSelf && text != null && text.gameObject.activeSelf) Layout();
        }

        /// <summary>Mark left of the text, the pair centred over the head.</summary>
        void Layout()
        {
            float textWidth = TextWidth(text);
            float markWidth = mark.sprite != null ? mark.sprite.bounds.size.x * mark.transform.localScale.x : 0f;
            const float gap = 0.04f;
            float left = -(markWidth + gap + textWidth) * 0.5f;
            mark.transform.localPosition = new Vector3(left + markWidth * 0.5f, MarkSize * 0.5f, 0f);
            var textAt = new Vector3(left + markWidth + gap + textWidth * 0.5f, 0f, 0f);
            text.transform.localPosition = textAt;
            for (int i = 0; i < outline.Length; i++) outline[i].transform.localPosition = textAt + (Vector3)(OutlineDirs[i] * OutlineStep);
        }

        void EnsureMade()
        {
            if (text != null) return;
            for (int i = 0; i < outline.Length; i++) outline[i] = MakeText("Outline", new Color32(20, 16, 24, 230), 440);
            text = MakeText("Title", new Color32(255, 255, 255, 255), 441);
        }

        TextMesh MakeText(string name, Color32 color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = CharSize;
            tm.fontSize = FontSize;
            tm.richText = false; // a title is plain text
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            var font = UIFont.Get();
            if (font != null)
            {
                tm.font = font;
                mr.sharedMaterial = font.material;
            }
            mr.sortingOrder = order;
            return tm;
        }

        /// <summary>Rendered width of a text mesh in local units (0 before it has a mesh).</summary>
        static float TextWidth(TextMesh tm)
        {
            var r = tm.GetComponent<MeshRenderer>();
            if (r == null) return 0f;
            float w = r.bounds.size.x;
            var s = tm.transform.lossyScale.x;
            return s > 0.0001f ? w / s : w;
        }
    }

    /// <summary>
    /// [TITLE PLATE 2026-10-30] Keeps the title plates of my character (the equipped title) and of the human party members in the field
    /// (their title from the party roster). Town peers are drawn by <see cref="TownPeers"/> from the server's town frames.
    /// </summary>
    public sealed class TitlePlates : MonoBehaviour
    {
        /// <summary>Height of the title line over the feet when there is no name tag (my body, party bodies): where a name would sit.</summary>
        public const float BareHeight = 1.5f;
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("TitlePlates");
            DontDestroyOnLoad(go);
            go.AddComponent<TitlePlates>();
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            bool online = OnlineSession.Playing;
            var me = Game.Player;
            if (me != null) TitlePlate.On(me.transform)?.Set(online ? AchievementClient.MyTitleId : "", online ? AchievementClient.MyTitle : "", BareHeight);
            var party = Game.Party;
            var roster = PartyClient.Instance;
            if (party == null) return;
            foreach (var body in party.Members)
            {
                if (body == null || body == me || !party.IsNetMember(body)) continue;
                PartyMemberView view = null;
                if (online && roster != null && roster.InParty)
                    foreach (var m in roster.Members) if (!m.me && m.name == body.DisplayName) { view = m; break; }
                TitlePlate.On(body.transform)?.Set(view?.titleId ?? "", view?.title ?? "", BareHeight);
            }
        }
    }
}
