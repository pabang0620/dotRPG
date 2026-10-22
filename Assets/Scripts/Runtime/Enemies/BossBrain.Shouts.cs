using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Boss lines of the chapter 3 raid bosses (Docs/ch3/RAIDS.md 외침): two or three per pattern, one picked at random,
    /// shown over the boss and on the HP bar and relayed to party members like the gimmick lines (RaidSeal.RaiseLine).
    /// Plus the 지바크 / 카마츠 rift and brand lines, the one-time phase 3 line, and the per-frame hook of their gimmicks.
    /// </summary>
    public sealed partial class BossBrain
    {
        const string ShoutVoidRift = "VoidRift", ShoutGreyBrand = "GreyBrand", ShoutPhase3 = "Phase3";

        static readonly Color ShoutColor = new Color(.86f, .8f, 1f);

        // Korean source lines (still translation keys: "// i18n-ignore"), wrapped in L.T where they are shown.
        static readonly Dictionary<string, Dictionary<string, string[]>> Shouts = new Dictionary<string, Dictionary<string, string[]>>
        {
            [MonsterDatabase.XibakBoss] = new Dictionary<string, string[]>
            {
                [nameof(PatternKind.Slash3)] = new[] { "갈라져라!", "셋이면 충분하다" }, // i18n-ignore
                [nameof(PatternKind.Fields)] = new[] { "발밑을 보아라", "땅이 운다" }, // i18n-ignore
                [nameof(PatternKind.Donut)] = new[] { "가까이 와라", "멀면 죽는다" }, // i18n-ignore
                [nameof(PatternKind.Repel)] = new[] { "밀려나라!", "...돌아오지 마라" }, // i18n-ignore
                [nameof(PatternKind.Annihilate)] = new[] { "모두 사라져라", "숨어 봐야 소용없다", "...빛의 원으로" }, // i18n-ignore
                [ShoutVoidRift] = new[] { "틈이 열린다", "메워 봐라, 용사의 피" }, // i18n-ignore
                [ShoutPhase3] = new[] { "...아푸크. 그분이 깨어난다" }, // i18n-ignore
            },
            [MonsterDatabase.KamatzBoss] = new Dictionary<string, string[]>
            {
                [nameof(PatternKind.Slash3)] = new[] { "잿빛 손은 멈추지 않는다", "셋 다 받아라" }, // i18n-ignore
                [nameof(PatternKind.Charge)] = new[] { "비켜라!", "길을 열겠다" }, // i18n-ignore
                [nameof(PatternKind.ArrowVolley)] = new[] { "쏴라", "하늘을 봐라" }, // i18n-ignore
                [nameof(PatternKind.Guard)] = new[] { "막는다", "...무의미하다" }, // i18n-ignore
                [nameof(PatternKind.Annihilate)] = new[] { "전부 치워라", "방패 뒤로 숨어 봐라" }, // i18n-ignore
                [nameof(PatternKind.RuinRitual)] = new[] { "의식을 시작하라", "토템을 지켜라" }, // i18n-ignore
                [ShoutGreyBrand] = new[] { "낙인을 받아라", "혼자서는 못 버틴다", "둘이 모이면 지워지지" }, // i18n-ignore
                [ShoutPhase3] = new[] { "카엘 경... 이 아이는 내가 데려가겠습니다" }, // i18n-ignore
            },
        };

        bool phase3Shouted;

        /// <summary>Says one of the boss's lines for <paramref name="key"/> (a pattern name or a gimmick), if it has any.</summary>
        void Shout(string key)
        {
            if (!Shouts.TryGetValue(Def.id, out var table) || !table.TryGetValue(key, out var lines) || lines.Length == 0) return;
            string line = L.T(lines[E.Rng.Next(lines.Length)]);
            WorldPopupText.Show(E.transform, line, ShoutColor, 2.2f, 1.4f + 1.1f * E.Size);
            Announce?.Invoke(this, line, ShoutColor);
            RaidSeal.RaiseLine(E, line); // party members draw it like a gimmick line
        }

        void ShoutPattern(PatternKind kind) => Shout(kind.ToString());

        /// <summary>
        /// Chapter 3 raid gimmick clocks (rifts, brand) and the one-time phase 3 line: every frame where the boss is simulated,
        /// also while it is groggy or stunned (Tick is skipped then).
        /// </summary>
        void Chapter3Tick()
        {
            if (Def.chapter < 3 || !Def.raid) return;
            if (Phase >= 3 && !phase3Shouted)
            {
                phase3Shouted = true;
                Shout(ShoutPhase3);
            }
            RiftTick();
            BrandTick();
        }

        /// <summary>The boss went away without dying (room left, run over): no rift or brand stays behind.</summary>
        void OnDestroy() => EndChapter3Gimmicks();

        void EndChapter3Gimmicks()
        {
            EndRifts();
            if (BrandActive || brandCircle != null) EndBrand();
        }
    }
}
