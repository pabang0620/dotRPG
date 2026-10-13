using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [I18N] Display text in the player's language (Docs/PLAN_I18N.md). The Korean source sentence is the key:
    /// <c>L.T("돌아가기")</c>, <c>L.F("{0}을(를) 샀습니다.", name)</c>. Tables are Resources/Data/Lang/&lt;code&gt;/*.json
    /// ({ "한국어": "translation" }), merged at start. A missing entry shows the Korean text, so nothing breaks.
    /// Names (items, monsters, skills) are translated where they are shown, never where they are defined, so name
    /// comparisons and saves keep working. The language is chosen once at start; changing it applies on restart.
    /// </summary>
    public static class L
    {
        public const string Korean = "ko";
        public const string English = "en";
        const string TableRoot = "Data/Lang/";

        /// <summary>A playable language: code (also the X-Lang header and table folder), its own name, the device language.</summary>
        public readonly struct Language
        {
            public readonly string code, native;
            public readonly SystemLanguage device;
            public Language(string code, string native, SystemLanguage device) { this.code = code; this.native = native; this.device = device; }
        }

        /// <summary>The settings menu cycles in this order. Thai and right-to-left scripts need text shaping uGUI lacks.</summary>
        public static readonly Language[] Languages =
        {
            new Language(Korean, "한국어", SystemLanguage.Korean), // i18n-ignore language name
            new Language(English, "English", SystemLanguage.English),
            new Language("ja", "日本語", SystemLanguage.Japanese),
            new Language("zh-Hans", "中文(Simplified)", SystemLanguage.ChineseSimplified),
            new Language("zh-Hant", "中文(繁體)", SystemLanguage.ChineseTraditional),
            new Language("es", "Español", SystemLanguage.Spanish),
            new Language("pt-BR", "Português (Brasil)", SystemLanguage.Portuguese),
            new Language("de", "Deutsch", SystemLanguage.German),
            new Language("fr", "Français", SystemLanguage.French),
            new Language("ru", "Русский", SystemLanguage.Russian),
            new Language("vi", "Tiếng Việt", SystemLanguage.Vietnamese),
            new Language("id", "Bahasa Indonesia", SystemLanguage.Indonesian),
        };

        static string lang;
        public static string Lang { get { Ensure(); return lang; } }
        /// <summary>Korean: the source language, no table.</summary>
        public static bool Ko => Lang == Korean;
        /// <summary>Chinese and Japanese count big numbers in 万 / 億 like Korean.</summary>
        public static bool CjkNumbers => Ko || Lang == "ja" || Lang.StartsWith("zh");
        /// <summary>Simplified and Traditional Chinese need hanzi the pixel font lacks: UIFont uses the device's CJK font.</summary>
        public static bool NeedsOsFont => Lang.StartsWith("zh");

        public static bool IsSupported(string code)
        {
            foreach (var l in Languages) if (l.code == code) return true;
            return false;
        }

        public static string NativeName(string code)
        {
            foreach (var l in Languages) if (l.code == code) return l.native;
            return code;
        }

        /// <summary>The next language in the settings cycle (step +1 or -1).</summary>
        public static string Next(string code, int step)
        {
            int i = 0;
            for (int k = 0; k < Languages.Length; k++) if (Languages[k].code == code) i = k;
            int n = Languages.Length;
            return Languages[((i + step) % n + n) % n].code;
        }

        static readonly Dictionary<string, string> table = new Dictionary<string, string>();
        static readonly Regex PlusSuffix = new Regex(@"^(.*?)( \+\d+)$");

        /// <summary>The language for a fresh install: the device language when supported, otherwise English.</summary>
        public static string DeviceDefault()
        {
            var device = Application.systemLanguage == SystemLanguage.Chinese ? SystemLanguage.ChineseSimplified : Application.systemLanguage;
            foreach (var l in Languages) if (l.device == device) return l.code;
            return English;
        }

        /// <summary>Called once by GameBootstrap after the settings are loaded.</summary>
        public static void Init(string language)
        {
            lang = IsSupported(language) ? language : Korean;
            table.Clear();
            if (lang == Korean) return;
            foreach (var asset in Resources.LoadAll<TextAsset>(TableRoot + lang))
            {
                if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> d)) continue;
                foreach (var kv in d)
                    if (kv.Value is string s && s.Length > 0) table[kv.Key] = s;
            }
        }

        /// <summary>Text built before GameBootstrap (static tables, early hooks) still gets the saved language.</summary>
        static void Ensure()
        {
            if (lang != null) return;
            var settings = new SettingsManager();
            settings.Load();
            Init(settings.Data.language);
        }

        /// <summary>The sentence in the current language (Korean when untranslated).</summary>
        public static string T(string ko)
        {
            if (Ko || string.IsNullOrEmpty(ko)) return ko;
            if (table.TryGetValue(ko, out var en)) return en;
            // Enhanced gear names ("뼈손잡이 장검 +12") translate by their base name.
            var m = PlusSuffix.Match(ko);
            if (m.Success && table.TryGetValue(m.Groups[1].Value, out en)) return en + m.Groups[2].Value;
            return ko;
        }

        /// <summary>Same Korean with a different meaning: the table key is "context|한국어".</summary>
        public static string T(string ko, string context)
        {
            if (Ko || string.IsNullOrEmpty(ko)) return ko;
            return table.TryGetValue(context + "|" + ko, out var en) ? en : T(ko);
        }

        /// <summary>A sentence with {0} {1} slots; the translation decides the word order.</summary>
        public static string F(string koFormat, params object[] args)
        {
            var format = T(koFormat);
            try { return string.Format(CultureInfo.InvariantCulture, format, args); }
            catch (FormatException)
            {
                // A broken translation must not cost the message: fall back to the Korean pattern.
                return string.Format(CultureInfo.InvariantCulture, koFormat, args);
            }
        }

        /// <summary>Korean particle (이/가, 을/를) after a word; nothing in other languages.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal) =>
            Ko ? PlayerController.Josa(word, withFinal, withoutFinal) : "";
    }
}
