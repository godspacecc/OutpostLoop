using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>保留兼容调用与测试接口，读取磁盘原始语言表；不再注册独立菜单。</summary>
    public static class LocalizationValidatorMenu
    {
        /// <summary>供代码调用的只读校验，读取失败明确返回false，不使用编辑器合并数据。</summary>
        public static bool ValidateTables()
        {
            try
            {
                var jsonByLanguage = LoadJsonTables();
                var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);
                foreach (var issue in report.Issues)
                    Debug.LogError($"[本地化] {issue}");
                string summary = $"磁盘原始文本表：{jsonByLanguage.Count} 种语言，{report.Issues.Count} 个问题。";
                if (report.IsValid)
                    Debug.Log($"[本地化] 校验通过：{summary}");
                else
                    Debug.LogError($"[本地化] 校验失败：{summary}");
                return report.IsValid;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[本地化] 校验未完成：{exception.Message}");
                return false;
            }
        }

        /// <summary>保留现有测试接口，返回未经补键或重新序列化的语言文件。</summary>
        public static IReadOnlyDictionary<string, string> LoadAllLanguageJsonForTests()
        {
            return LoadJsonTables();
        }

        /// <summary>加载已配置语言的原始磁盘文本，不采用缺省语言回退。</summary>
        internal static Dictionary<string, string> LoadJsonTables()
        {
            return LoadJsonTables(out _, out _);
        }

        /// <summary>严格读取语言清单及默认/注释语言；无效配置或文件读取失败直接抛错。</summary>
        internal static Dictionary<string, string> LoadJsonTables(out string defaultLanguage, out string commentLanguage)
        {
            string configPath = LocalizationEditorPaths.LanguageConfigPath;
            JObject config;
            try
            {
                config = JObject.Parse(File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"语言配置读取失败：{configPath}；{exception.Message}", exception);
            }
            if (config["languages"] is not JArray languages || languages.Count == 0)
                throw new InvalidDataException($"语言配置缺少非空languages数组：{configPath}");

            var codes = new List<string>();
            foreach (var token in languages)
            {
                if (token is not JObject language || language["code"]?.Type != JTokenType.String)
                    throw new InvalidDataException($"语言配置存在无效语言条目：{configPath}");
                string code = language.Value<string>("code");
                LocalizationJsonEditorUtility.ValidateLanguageCode(code);
                if (codes.Contains(code, StringComparer.Ordinal))
                    throw new InvalidDataException($"语言配置重复：{code}");
                codes.Add(code);
            }
            defaultLanguage = ReadConfiguredLanguage(config, "default", codes);
            commentLanguage = ReadConfiguredLanguage(config, "codeComment", codes);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string code in codes)
            {
                string path = Path.Combine(LocalizationEditorPaths.ConfigFolder, code + ".json");
                try
                {
                    result.Add(code, File.ReadAllText(path));
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException($"语言文件读取失败：{path}；{exception.Message}", exception);
                }
            }
            return result;
        }

        /// <summary>默认与注释语言必须明确属于语言清单，禁止悄悄回退。</summary>
        private static string ReadConfiguredLanguage(JObject config, string key, IEnumerable<string> codes)
        {
            if (config[key]?.Type != JTokenType.String)
                throw new InvalidDataException($"语言配置缺少有效的{key}字符串。");
            string value = config.Value<string>(key);
            if (!codes.Contains(value, StringComparer.Ordinal))
                throw new InvalidDataException($"语言配置{key}未包含在语言清单中：{value}");
            return value;
        }
    }
}
