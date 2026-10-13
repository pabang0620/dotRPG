using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [I18N] Display text in the player's language (Docs/PLAN_I18N.md). The Korean source sentence is the key:
    /// <c>L.T("돌아가기")</c>, <c>L.F("{0}을(를) 샀습니다.", name)</c>. Tables are Resources/Data/Lang/en/*.json
    /// ({ "한국어": "English" }), merged at start. A missing entry shows the Korean text, so nothing breaks.
    /// Names (items, monsters, skills) are translated where they are shown, never where they are defined, so name
    /// comparisons and saves keep working. The language is chosen once at start; changing it applies on restart.
    /// </summary>
    public static class L
    {
        public const string Korean = "ko";
        public const string English = "en";
        const string TablePath = "Data/Lang/en";

        static string lang;
        public static string Lang { get { Ensure(); return lang; } }
        public static bool En => Lang == English;

        static readonly Dictionary<string, string> table = new Dictionary<string, string>();
        static readonly Regex PlusSuffix = new Regex(@"^(.*?)( \+\d+)$");

        /// <summary>The language for a fresh install: Korean on a Korean device, English everywhere else.</summary>
        public static string DeviceDefault() => Application.systemLanguage == SystemLanguage.Korean ? Korean : English;

        /// <summary>Called once by GameBootstrap after the settings are loaded.</summary>
        public static void Init(string language)
        {
            lang = language == English ? English : Korean;
            table.Clear();
            if (lang != English) return;
            foreach (var asset in Resources.LoadAll<TextAsset>(TablePath))
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
            if (!En || string.IsNullOrEmpty(ko)) return ko;
            if (table.TryGetValue(ko, out var en)) return en;
            // Enhanced gear names ("뼈손잡이 장검 +12") translate by their base name.
            var m = PlusSuffix.Match(ko);
            if (m.Success && table.TryGetValue(m.Groups[1].Value, out en)) return en + m.Groups[2].Value;
            return ko;
        }

        /// <summary>Same Korean with a different meaning: the table key is "context|한국어".</summary>
        public static string T(string ko, string context)
        {
            if (!En || string.IsNullOrEmpty(ko)) return ko;
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

        /// <summary>Korean particle (이/가, 을/를) after a word; nothing in English.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal) =>
            En ? "" : PlayerController.Josa(word, withFinal, withoutFinal);
    }
}
