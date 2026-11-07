using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Game
{
    /// <summary>
    /// 定义本地化校验问题类型，用于区分 JSON 错误、缺键和占位符不一致。
    /// </summary>
    public enum LocalizationValidationIssueType
    {
        InvalidJson,
        MissingKey,
        PlaceholderMismatch
    }

    /// <summary>
    /// 保存单条本地化校验问题，包含语言、键名和中文问题说明，供报告聚合。
    /// </summary>
    public sealed class LocalizationValidationIssue
    {
        public LocalizationValidationIssue(
            LocalizationValidationIssueType type,
            string languageCode,
            string key,
            string message)
        {
            Type = type;
            LanguageCode = languageCode;
            Key = key;
            Message = message;
        }

        public LocalizationValidationIssueType Type { get; }
        public string LanguageCode { get; }
        public string Key { get; }
        public string Message { get; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Key)
                ? $"[{Type}] {LanguageCode}: {Message}"
                : $"[{Type}] {LanguageCode}/{Key}: {Message}";
        }
    }

    /// <summary>
    /// 汇总本地化校验结果，调用方通过 IsValid 判断是否可以继续使用语言表。
    /// </summary>
    public sealed class LocalizationValidationReport
    {
        public LocalizationValidationReport(IReadOnlyList<LocalizationValidationIssue> issues)
        {
            Issues = issues;
        }

        public IReadOnlyList<LocalizationValidationIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;
    }

    /// <summary>
    /// 校验各语言表结构、键集合和占位符一致性，只生成报告，不修改资源文件。
    /// </summary>
    public static class LocalizationValidator
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        public static LocalizationValidationReport ValidateJsonTables(IReadOnlyDictionary<string, string> jsonByLanguage)
        {
            var issues = new List<LocalizationValidationIssue>();
            var tables = new Dictionary<string, Dictionary<string, string>>();

            foreach (var pair in jsonByLanguage)
            {
                var table = ParseTable(pair.Key, pair.Value, issues);
                if (table != null)
                {
                    tables[pair.Key] = table;
                }
            }

            ValidateKeySets(tables, issues);
            ValidatePlaceholders(tables, issues);

            return new LocalizationValidationReport(issues);
        }

        private static Dictionary<string, string> ParseTable(
            string languageCode,
            string json,
            List<LocalizationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(json) || !RuntimeJson.TryParseStringTable(json, out var parsedTable))
            {
                issues.Add(new LocalizationValidationIssue(
                    LocalizationValidationIssueType.InvalidJson,
                    languageCode,
                    string.Empty,
                    "本地化 JSON 必须是非空对象。"));
                return null;
            }

            return new Dictionary<string, string>(parsedTable, StringComparer.Ordinal);
        }

        private static void ValidateKeySets(
            Dictionary<string, Dictionary<string, string>> tables,
            List<LocalizationValidationIssue> issues)
        {
            var allKeys = tables.Values
                .SelectMany(table => table.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            foreach (var tablePair in tables)
            {
                foreach (var key in allKeys)
                {
                    if (!tablePair.Value.ContainsKey(key))
                    {
                        issues.Add(new LocalizationValidationIssue(
                            LocalizationValidationIssueType.MissingKey,
                            tablePair.Key,
                            key,
                            "语言表缺少该键。"));
                    }
                }
            }
        }

        private static void ValidatePlaceholders(
            Dictionary<string, Dictionary<string, string>> tables,
            List<LocalizationValidationIssue> issues)
        {
            var keys = tables.Values.SelectMany(table => table.Keys).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var key in keys)
            {
                var expected = default(string[]);
                var expectedLanguage = string.Empty;

                foreach (var tablePair in tables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!tablePair.Value.TryGetValue(key, out var text))
                    {
                        continue;
                    }

                    var placeholders = ExtractPlaceholders(text);
                    if (expected == null)
                    {
                        expected = placeholders;
                        expectedLanguage = tablePair.Key;
                        continue;
                    }

                    if (!placeholders.SequenceEqual(expected))
                    {
                        issues.Add(new LocalizationValidationIssue(
                            LocalizationValidationIssueType.PlaceholderMismatch,
                            tablePair.Key,
                            key,
                            $"占位符集合与 {expectedLanguage} 不一致。"));
                    }
                }
            }
        }

        private static string[] ExtractPlaceholders(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<string>();
            }

            return PlaceholderRegex.Matches(text)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
