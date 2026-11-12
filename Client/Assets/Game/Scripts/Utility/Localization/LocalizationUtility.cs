using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 加载并缓存本地化表，负责按当前语言取文本和格式化，不参与 UI 生命周期。
    /// </summary>
    public sealed class LocalizationUtility : IUtility
    {
        private readonly Dictionary<string, Dictionary<string, string>> tables =
            new Dictionary<string, Dictionary<string, string>>();
        private bool loadedConfiguredTables;

        public LocalizationUtility(bool loadConfiguredTables = true)
        {
            if (loadConfiguredTables)
            {
                InitializeConfiguredTables();
            }
        }

        public void InitializeConfiguredTables()
        {
            if (loadedConfiguredTables)
            {
                return;
            }

            LoadConfiguredTables();
        }

        public void LoadFromJson(string languageCode, string json)
        {
            var language = LocalizationLanguages.FindOrDefault(languageCode);
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            tables[language.Code] = table;

            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            if (!RuntimeJson.TryParseStringTable(json, out var parsedTable))
            {
                Log.Warning($"[LocalizationUtility] 本地化 JSON 不是对象：{language.Code}。");
                return;
            }

            foreach (var pair in parsedTable)
            {
                table[pair.Key] = pair.Value;
            }
        }

        public string Translate(string languageCode, string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            InitializeConfiguredTables();

            var currentLanguageCode = LocalizationLanguages.FindOrDefault(languageCode).Code;
            var text = TryGetText(currentLanguageCode, key)
                ?? TryGetText(LocalizationLanguages.Default.Code, key)
                ?? key;

            if (args == null || args.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException exception)
            {
                Log.Warning($"[LocalizationUtility] 本地化格式化失败：key={key}，底层错误：{exception.Message}");
                return text;
            }
        }

        public IReadOnlyCollection<string> GetKeys(string languageCode)
        {
            var language = LocalizationLanguages.FindOrDefault(languageCode);
            return tables.TryGetValue(language.Code, out var table)
                ? table.Keys.ToArray()
                : Array.Empty<string>();
        }

        private void LoadConfiguredTables()
        {
            loadedConfiguredTables = true;
            foreach (var language in LocalizationLanguages.All)
            {
                var json = ResourceLoader.LoadText($"{language.Code}.json");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    LoadFromJson(language.Code, json);
                }
            }
        }

        private string TryGetText(string languageCode, string key)
        {
            return tables.TryGetValue(languageCode, out var table) && table.TryGetValue(key, out var text)
                ? text
                : null;
        }
    }
}
