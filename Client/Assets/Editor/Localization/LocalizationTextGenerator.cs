using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public static class LocalizationTextGenerator
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{(\d+)(?:[^}]*)?\}", RegexOptions.Compiled);

        /// <summary>手动生成入口统一报告失败；供其他工具调用的入口会传播异常。</summary>
        [MenuItem("Tools/本地化/生成文本常量", false, 321)]
        public static void GenerateTexts()
        {
            try
            {
                bool changed = GenerateTextsOrThrow();
                Debug.Log(changed
                    ? $"[本地化] 已生成：{LocalizationEditorPaths.GeneratedTextsPath}"
                    : "[本地化] 文本常量内容未变化，已跳过写入和刷新。");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[本地化] 无法完成文本常量生成：{exception.Message}");
            }
        }

        /// <summary>校验原始语言表并安全写入；失败抛给调用方，返回值表示文件是否更新。</summary>
        public static bool GenerateTextsOrThrow()
        {
            // 与手动校验共用严格磁盘读取，禁止无效语言配置被自动回退。
            var jsonByLanguage = LocalizationValidatorMenu.LoadJsonTables(out string defaultLanguage, out string commentLanguage);
            var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);
            if (!report.IsValid)
                throw new InvalidDataException(string.Join("\n", report.Issues.Select(issue => issue.ToString())));

            string source = GenerateSource(jsonByLanguage, defaultLanguage, commentLanguage);
            if (!WriteSourceIfChanged(LocalizationEditorPaths.GeneratedTextsPath, source))
                return false;
            try
            {
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("生成文件已更新，但Unity资源刷新失败；请检查编辑器状态。", exception);
            }
            return true;
        }

        /// <summary>先写同目录临时文件再原子替换；写入失败不截断旧文件，不触碰meta。</summary>
        private static bool WriteSourceIfChanged(string targetPath, string source)
        {
            if (File.Exists(targetPath) && string.Equals(File.ReadAllText(targetPath), source, StringComparison.Ordinal))
                return false;

            string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, source, Encoding.UTF8);
                if (File.Exists(targetPath))
                    File.Replace(temporaryPath, targetPath, null);
                else
                    File.Move(temporaryPath, targetPath);
                return true;
            }
            finally
            {
                // 清理失败单独提示，不能覆盖写入原始异常或误报已替换文件未更新。
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[本地化] 临时文件清理失败：{temporaryPath}；{exception.Message}");
                }
            }
        }

        public static string GenerateSourceForTests(string json)
        {
            return GenerateSource(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["zh-CN"] = json
                },
                "zh-CN",
                "zh-CN");
        }

        public static string GenerateSourceForTests(
            IReadOnlyDictionary<string, string> jsonByLanguage,
            string defaultLanguageCode,
            string commentLanguageCode)
        {
            return GenerateSource(jsonByLanguage, defaultLanguageCode, commentLanguageCode);
        }

        public static IReadOnlyDictionary<string, string> BuildMemberNameMap(IEnumerable<string> keys)
        {
            return BuildNameMap(keys);
        }

        private static string GenerateSource(
            IReadOnlyDictionary<string, string> jsonByLanguage,
            string defaultLanguageCode,
            string commentLanguageCode)
        {
            if (jsonByLanguage == null ||
                !jsonByLanguage.TryGetValue(defaultLanguageCode, out var defaultJson) ||
                !RuntimeJson.TryParseStringTable(defaultJson, out var defaultTable))
            {
                throw new InvalidDataException($"{defaultLanguageCode} localization json is invalid.");
            }

            var commentTable = defaultTable;
            if (!string.IsNullOrWhiteSpace(commentLanguageCode) &&
                jsonByLanguage.TryGetValue(commentLanguageCode, out var commentJson) &&
                RuntimeJson.TryParseStringTable(commentJson, out var parsedCommentTable))
            {
                commentTable = parsedCommentTable;
            }

            var nameByKey = BuildMemberNameMap(defaultTable.Keys);
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("namespace Game");
            builder.AppendLine("{");
            builder.AppendLine("    public static class Texts");
            builder.AppendLine("    {");

            foreach (var pair in defaultTable.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var memberName = nameByKey[pair.Key];
                var argumentCount = GetArgumentCount(pair.Value);
                var comment = commentTable.TryGetValue(pair.Key, out var commentText)
                    ? commentText
                    : pair.Value;

                AppendComment(builder, comment);
                if (argumentCount == 0)
                {
                    builder.AppendLine($"        public static string {memberName} => Loc.T(\"{EscapeCSharpString(pair.Key)}\");");
                    continue;
                }

                var indexes = Enumerable.Range(0, argumentCount).ToArray();
                var parameters = string.Join(", ", indexes.Select(index => $"object arg{index}"));
                var arguments = string.Join(", ", indexes.Select(index => $"arg{index}"));
                builder.AppendLine(
                    $"        public static string {memberName}({parameters}) => Loc.T(\"{EscapeCSharpString(pair.Key)}\", {arguments});");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static Dictionary<string, string> BuildNameMap(IEnumerable<string> keys)
        {
            var usedNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var nameByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in keys ?? Enumerable.Empty<string>())
            {
                var baseName = ToMemberName(key);
                var name = baseName;
                var suffix = 2;
                while (usedNames.TryGetValue(name, out var existingKey) && existingKey != key)
                {
                    name = $"{baseName}{suffix++}";
                }

                usedNames[name] = key;
                nameByKey[key] = name;
            }

            return nameByKey;
        }

        private static string ToMemberName(string key)
        {
            var parts = (key ?? string.Empty).Split('.');
            if (parts.Length > 0 && parts[0] == "ui")
            {
                parts = parts.Skip(1).ToArray();
            }

            var name = string.Concat(parts.Select(ToPascalPart));
            return !string.IsNullOrEmpty(name) && (char.IsLetter(name, 0) || name[0] == '_') ? name : $"Text{name}";
        }

        private static string ToPascalPart(string part)
        {
            var pieces = Regex.Split(part ?? string.Empty, @"[^A-Za-z0-9]+")
                .Where(piece => piece.Length > 0)
                .ToArray();
            if (pieces.Length == 0)
            {
                return "Text";
            }

            return string.Concat(pieces.Select(piece =>
            {
                if (piece.All(char.IsDigit))
                {
                    return piece;
                }

                var normalized = piece.ToLowerInvariant();
                return char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
            }));
        }

        private static int GetArgumentCount(string text)
        {
            var max = -1;
            foreach (Match match in PlaceholderRegex.Matches(text ?? string.Empty))
            {
                max = Math.Max(max, int.Parse(match.Groups[1].Value));
            }

            return max + 1;
        }

        private static void AppendComment(StringBuilder builder, string text)
        {
            builder.AppendLine("        /// <summary>");
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var commentLine = SecurityElement.Escape(line.TrimEnd());
                builder.Append("        ///");
                if (!string.IsNullOrEmpty(commentLine))
                {
                    builder.Append(' ');
                    builder.Append(commentLine);
                }

                builder.AppendLine();
            }

            builder.AppendLine("        /// </summary>");
        }

        private static string EscapeCSharpString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}
