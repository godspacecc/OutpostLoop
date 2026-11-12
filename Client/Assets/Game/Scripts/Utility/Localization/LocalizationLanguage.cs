using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 描述支持语言的代码和显示名，代码用于持久化与翻译表匹配。
    /// </summary>
    public sealed class LocalizationLanguage
    {
        public LocalizationLanguage(string code, string label)
        {
            Code = code;
            Label = label;
        }

        public string Code { get; }
        public string Label { get; }
    }

    /// <summary>
    /// 提供支持语言集合和回退查找逻辑，异常语言代码会回到默认语言。
    /// </summary>
    public static class LocalizationLanguages
    {
        public const string LanguagesAddress = "languages.json";

        private static readonly List<LocalizationLanguage> FallbackLanguages = new List<LocalizationLanguage>
        {
            new LocalizationLanguage("zh-CN", "简体中文"),
            new LocalizationLanguage("en-US", "English")
        };

        private static LanguageConfig cachedConfig;

        public static IReadOnlyList<LocalizationLanguage> All => LoadLanguages().Languages;

        public static LocalizationLanguage Default => LoadLanguages().DefaultLanguage;

        public static LocalizationLanguage FindOrDefault(string code)
        {
            var config = LoadLanguages();
            return config.Languages.FirstOrDefault(language => language.Code == code) ?? config.DefaultLanguage;
        }

        public static void ResetForTests()
        {
            ResetCache();
        }

        public static void ResetCache()
        {
            cachedConfig = null;
        }

        private static LanguageConfig LoadLanguages()
        {
            if (cachedConfig != null)
            {
                return cachedConfig;
            }

            var json = ResourceLoader.LoadText(LanguagesAddress);
            if (string.IsNullOrWhiteSpace(json) || !RuntimeJson.TryParseObject(json, out var root))
            {
                cachedConfig = new LanguageConfig(FallbackLanguages, FallbackLanguages[0]);
                return cachedConfig;
            }

            var languagesNode = root["languages"];
            if (!(languagesNode is JArray languagesArray))
            {
                cachedConfig = new LanguageConfig(FallbackLanguages, FallbackLanguages[0]);
                return cachedConfig;
            }

            var languages = new List<LocalizationLanguage>();
            var usedCodes = new HashSet<string>();
            foreach (var child in languagesArray)
            {
                var code = RuntimeJson.GetString(child, "code");
                var label = RuntimeJson.GetString(child, "label");
                if (string.IsNullOrWhiteSpace(code) || !usedCodes.Add(code))
                {
                    continue;
                }

                languages.Add(new LocalizationLanguage(code, string.IsNullOrWhiteSpace(label) ? code : label));
            }

            if (languages.Count == 0)
            {
                cachedConfig = new LanguageConfig(FallbackLanguages, FallbackLanguages[0]);
                return cachedConfig;
            }

            var defaultCode = RuntimeJson.GetString(root, "default");
            var defaultLanguage = languages.FirstOrDefault(language => language.Code == defaultCode) ?? languages[0];
            cachedConfig = new LanguageConfig(languages, defaultLanguage);
            return cachedConfig;
        }

        private sealed class LanguageConfig
        {
            public LanguageConfig(IReadOnlyList<LocalizationLanguage> languages, LocalizationLanguage defaultLanguage)
            {
                Languages = languages;
                DefaultLanguage = defaultLanguage;
            }

            public IReadOnlyList<LocalizationLanguage> Languages { get; }
            public LocalizationLanguage DefaultLanguage { get; }
        }
    }
}
