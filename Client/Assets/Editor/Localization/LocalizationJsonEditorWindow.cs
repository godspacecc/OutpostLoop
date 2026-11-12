using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public sealed class LocalizationJsonLanguage
    {
        public LocalizationJsonLanguage(string code, string label)
        {
            Code = code;
            Label = label;
        }

        public string Code { get; set; }
        public string Label { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Code : $"{Label} ({Code})";
    }

    public sealed class LocalizationJsonRow
    {
        private readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public LocalizationJsonRow(string key)
        {
            Key = key;
            MemberName = string.Empty;
        }

        public string Key { get; set; }
        public string MemberName { get; set; }

        public string GetText(string languageCode)
        {
            return texts.TryGetValue(languageCode, out var text) ? text : string.Empty;
        }

        public void SetText(string languageCode, string text)
        {
            texts[languageCode] = text ?? string.Empty;
        }

        public IReadOnlyDictionary<string, string> Texts => texts;
    }

    public sealed class LocalizationJsonEditorState
    {
        public LocalizationJsonEditorState(
            List<LocalizationJsonLanguage> languages,
            List<LocalizationJsonRow> rows,
            string defaultLanguageCode,
            string commentLanguageCode)
        {
            Languages = languages;
            Rows = rows;
            DefaultLanguageCode = defaultLanguageCode;
            CommentLanguageCode = commentLanguageCode;
        }

        public List<LocalizationJsonLanguage> Languages { get; }
        public List<LocalizationJsonRow> Rows { get; }
        public string DefaultLanguageCode { get; set; }
        public string CommentLanguageCode { get; set; }

        public IReadOnlyDictionary<string, string> ToJsonByLanguage()
        {
            return Languages.ToDictionary(
                language => language.Code,
                language => LocalizationJsonEditorUtility.BuildLanguageJson(Rows, language.Code),
                StringComparer.Ordinal);
        }
    }

    public static class LocalizationJsonEditorUtility
    {
        public const string LocalizationAssetRoot = LocalizationEditorPaths.ConfigFolder;
        public const string LanguageConfigPath = LocalizationEditorPaths.LanguageConfigPath;

        private static readonly Regex LanguageCodeRegex = new Regex(@"^[a-z]{2,3}(?:-[A-Z]{2})?$", RegexOptions.Compiled);

        public static LocalizationJsonEditorState LoadStateFromProject()
        {
            var tables = LocalizationValidatorMenu.LoadJsonTables();
            foreach (var pair in tables)
                if (!RuntimeJson.TryParseStringTable(pair.Value, out _))
                    throw new InvalidDataException($"语言文件JSON无效：{pair.Key}.json");
            return LoadState(File.ReadAllText(LanguageConfigPath), tables);
        }

        public static LocalizationJsonEditorState LoadStateForTests(
            string languagesJson,
            IReadOnlyDictionary<string, string> jsonByLanguage)
        {
            return LoadState(languagesJson, jsonByLanguage);
        }

        public static IList FilterRowsForTests(IList rows, string query)
        {
            return FilterRows(rows.Cast<LocalizationJsonRow>(), query).Cast<object>().ToList();
        }

        public static List<LocalizationJsonRow> FilterRows(IEnumerable<LocalizationJsonRow> rows, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return rows.ToList();
            }

            var queryTokens = query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeSearchText)
                .Where(token => token.Length > 0)
                .ToArray();

            if (queryTokens.Length == 0)
            {
                return rows.ToList();
            }

            return rows
                .Where(row => MatchesRow(row, queryTokens))
                .ToList();
        }

        public static void RefreshMemberNames(IEnumerable<LocalizationJsonRow> rows)
        {
            var rowList = rows.ToList();
            var nameByKey = LocalizationTextGenerator.BuildMemberNameMap(rowList.Select(row => row.Key));
            foreach (var row in rowList)
            {
                row.MemberName = nameByKey.TryGetValue(row.Key, out var name) ? name : string.Empty;
            }
        }

        public static void SaveStateToProject(LocalizationJsonEditorState state)
        {
            SaveStateWithSnapshot(state, CaptureDiskSnapshot());
        }

        /// <summary>窗口传入载入时基线，准备文件后再次核对，避免覆盖准备期间的外部变更。</summary>
        public static void SaveStateWithSnapshot(LocalizationJsonEditorState state, string expectedSnapshot)
        {
            ValidateStateForSave(state);
            var jsonByLanguage = state.ToJsonByLanguage();
            var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);
            if (!report.IsValid)
            {
                throw new InvalidDataException(string.Join("\n", report.Issues.Select(issue => issue.ToString())));
            }

            var files = jsonByLanguage.ToDictionary(pair => GetLanguageAssetPath(pair.Key), pair => pair.Value, StringComparer.Ordinal);
            files.Add(LanguageConfigPath, BuildLanguagesJson(state));
            WriteLanguageFiles(files, expectedSnapshot);

            // 语言文件已落盘；后续失败需如实报告阶段，不能声称代码已生成。
            try
            {
                AssetDatabase.Refresh();
                LocalizationTextGenerator.GenerateTextsOrThrow();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"文本表已保存，但文本常量同步未完成：{exception.Message}", exception);
            }
        }

        /// <summary>保存前比较全部语言JSON的字节快照，包含未启用语言，防止覆盖外部修改。</summary>
        public static string CaptureDiskSnapshot()
        {
            if (!Directory.Exists(LocalizationAssetRoot))
                throw new DirectoryNotFoundException(LocalizationAssetRoot);
            var snapshot = new JObject();
            foreach (string path in Directory.GetFiles(LocalizationAssetRoot, "*.json").OrderBy(path => path, StringComparer.Ordinal))
                snapshot[path] = Convert.ToBase64String(File.ReadAllBytes(path));
            return snapshot.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>先准备所有临时文件，再替换；异常时逆序恢复已替换项并报告恢复失败。</summary>
        private static void WriteLanguageFiles(Dictionary<string, string> files, string expectedSnapshot)
        {
            var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var temporary = new Dictionary<string, string>(StringComparer.Ordinal);
            var written = new List<string>();
            try
            {
                foreach (var pair in files)
                {
                    originals[pair.Key] = File.Exists(pair.Key) ? File.ReadAllBytes(pair.Key) : null;
                    string temp = pair.Key + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    temporary.Add(pair.Key, temp);
                    File.WriteAllText(temp, pair.Value, Encoding.UTF8);
                }
                if (expectedSnapshot != CaptureDiskSnapshot())
                    throw new IOException("准备保存期间磁盘文件发生变化，已取消写入。");
                foreach (var pair in temporary)
                {
                    if (File.Exists(pair.Key)) File.Replace(pair.Value, pair.Key, null);
                    else File.Move(pair.Value, pair.Key);
                    written.Add(pair.Key);
                }
            }
            catch (Exception exception)
            {
                var failures = new List<string>();
                foreach (string path in written.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (originals[path] == null) File.Delete(path);
                        else
                        {
                            File.WriteAllBytes(temporary[path], originals[path]);
                            if (File.Exists(path)) File.Replace(temporary[path], path, null);
                            else File.Move(temporary[path], path);
                            if (!File.ReadAllBytes(path).SequenceEqual(originals[path]))
                                throw new IOException("恢复后内容不一致");
                        }
                    }
                    catch (Exception recovery) { failures.Add(path + "：" + recovery.Message); }
                }
                throw new IOException("语言文件保存失败：" + exception.Message +
                    (failures.Count == 0 ? "；已恢复本次已替换项。" : "；恢复未完成：" + string.Join("；", failures)), exception);
            }
            finally
            {
                foreach (string temp in temporary.Values)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception exception) { Debug.LogWarning($"[本地化] 临时文件未清理：{temp}；{exception.Message}"); }
                }
            }
        }

        public static string BuildLanguagesJson(LocalizationJsonEditorState state)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"default\": \"{EscapeJson(state.DefaultLanguageCode)}\",");
            builder.AppendLine($"  \"codeComment\": \"{EscapeJson(state.CommentLanguageCode)}\",");
            builder.AppendLine("  \"languages\": [");
            for (var i = 0; i < state.Languages.Count; i++)
            {
                var language = state.Languages[i];
                builder.AppendLine("    {");
                builder.AppendLine($"      \"code\": \"{EscapeJson(language.Code)}\",");
                builder.AppendLine($"      \"label\": \"{EscapeJson(language.Label)}\"");
                builder.Append("    }");
                if (i < state.Languages.Count - 1)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            builder.AppendLine("  ]");
            builder.Append("}");
            return builder.ToString();
        }

        public static string BuildLanguageJson(IEnumerable<LocalizationJsonRow> rows, string languageCode)
        {
            var orderedRows = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Key))
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("{");
            for (var i = 0; i < orderedRows.Count; i++)
            {
                var row = orderedRows[i];
                builder.Append($"  \"{EscapeJson(row.Key)}\": \"{EscapeJson(row.GetText(languageCode))}\"");
                if (i < orderedRows.Count - 1)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            builder.Append("}");
            return builder.ToString();
        }

        private static LocalizationJsonEditorState LoadState(
            string languagesJson,
            IReadOnlyDictionary<string, string> jsonByLanguage)
        {
            var languageConfig = ParseLanguageConfig(languagesJson);
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var language in languageConfig.Languages)
            {
                jsonByLanguage.TryGetValue(language.Code, out var json);
                tables[language.Code] = ParseTable(json);
            }

            var keys = tables.Values
                .SelectMany(table => table.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var rows = new List<LocalizationJsonRow>();
            foreach (var key in keys)
            {
                var row = new LocalizationJsonRow(key);
                foreach (var language in languageConfig.Languages)
                {
                    var text = tables.TryGetValue(language.Code, out var table) && table.TryGetValue(key, out var value)
                        ? value
                        : string.Empty;
                    row.SetText(language.Code, text);
                }

                rows.Add(row);
            }

            RefreshMemberNames(rows);
            return new LocalizationJsonEditorState(
                languageConfig.Languages,
                rows,
                languageConfig.DefaultLanguageCode,
                languageConfig.CommentLanguageCode);
        }

        private static LanguageConfig ParseLanguageConfig(string json)
        {
            if (!RuntimeJson.TryParseObject(json, out var root) || root["languages"] is not JArray languageNodes)
            {
                return CreateFallbackLanguageConfig();
            }

            var languages = new List<LocalizationJsonLanguage>();
            var usedCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in languageNodes)
            {
                if (token is not JObject node)
                {
                    continue;
                }

                var code = RuntimeJson.GetString(node, "code");
                var label = RuntimeJson.GetString(node, "label");
                if (string.IsNullOrWhiteSpace(code) || usedCodes.Contains(code))
                {
                    continue;
                }

                languages.Add(new LocalizationJsonLanguage(code, string.IsNullOrWhiteSpace(label) ? code : label));
                usedCodes.Add(code);
            }

            if (languages.Count == 0)
            {
                return CreateFallbackLanguageConfig();
            }

            var defaultCode = NormalizeConfiguredLanguage(RuntimeJson.GetString(root, "default"), languages, languages[0].Code);
            var commentCode = NormalizeConfiguredLanguage(RuntimeJson.GetString(root, "codeComment"), languages, defaultCode);
            return new LanguageConfig(languages, defaultCode, commentCode);
        }

        private static LanguageConfig CreateFallbackLanguageConfig()
        {
            var languages = new List<LocalizationJsonLanguage>
            {
                new LocalizationJsonLanguage("zh-CN", "简体中文"),
                new LocalizationJsonLanguage("en-US", "English")
            };
            return new LanguageConfig(languages, "zh-CN", "zh-CN");
        }

        private static string NormalizeConfiguredLanguage(
            string languageCode,
            IReadOnlyCollection<LocalizationJsonLanguage> languages,
            string fallback)
        {
            return languages.Any(language => language.Code == languageCode) ? languageCode : fallback;
        }

        private static Dictionary<string, string> ParseTable(string json)
        {
            if (!RuntimeJson.TryParseStringTable(json, out var table))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(table, StringComparer.Ordinal);
        }

        private static void ValidateStateForSave(LocalizationJsonEditorState state)
        {
            if (state.Languages.Count == 0)
            {
                throw new InvalidDataException("至少需要一种语言。");
            }

            var languageCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var language in state.Languages)
            {
                ValidateLanguageCode(language.Code);
                if (!languageCodes.Add(language.Code))
                {
                    throw new InvalidDataException($"语言 code 重复：{language.Code}");
                }
            }

            if (!languageCodes.Contains(state.DefaultLanguageCode))
            {
                throw new InvalidDataException($"默认语言不存在：{state.DefaultLanguageCode}");
            }

            if (!languageCodes.Contains(state.CommentLanguageCode))
            {
                throw new InvalidDataException($"注释语言不存在：{state.CommentLanguageCode}");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in state.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                {
                    throw new InvalidDataException("存在空 key。");
                }

                if (!keys.Add(row.Key))
                {
                    throw new InvalidDataException($"key 重复：{row.Key}");
                }
            }
        }

        public static void ValidateLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode) || !LanguageCodeRegex.IsMatch(languageCode))
            {
                throw new InvalidDataException($"语言 code 格式不正确：{languageCode}");
            }
        }

        private static bool MatchesRow(LocalizationJsonRow row, string[] queryTokens)
        {
            var searchFields = new List<string>
            {
                row.Key,
                row.MemberName,
                $"Texts.{row.MemberName}"
            };
            searchFields.AddRange(row.Texts.Values);

            var normalizedFields = searchFields.Select(NormalizeSearchText).ToArray();
            var combined = string.Concat(normalizedFields);

            return queryTokens.All(token => normalizedFields.Any(field => MatchesNormalized(field, token)) ||
                MatchesNormalized(combined, token));
        }

        private static bool MatchesNormalized(string value, string query)
        {
            return value.Contains(query) || IsSubsequence(value, query);
        }

        private static string NormalizeSearchText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }

        private static bool IsSubsequence(string value, string query)
        {
            if (query.Length == 0)
            {
                return true;
            }

            var queryIndex = 0;
            foreach (var ch in value)
            {
                if (ch != query[queryIndex])
                {
                    continue;
                }

                queryIndex++;
                if (queryIndex == query.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetLanguageAssetPath(string languageCode)
        {
            return $"{LocalizationAssetRoot}/{languageCode}.json";
        }

        private static string LoadAssetText(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            return asset == null ? string.Empty : asset.text;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        private sealed class LanguageConfig
        {
            public LanguageConfig(
                List<LocalizationJsonLanguage> languages,
                string defaultLanguageCode,
                string commentLanguageCode)
            {
                Languages = languages;
                DefaultLanguageCode = defaultLanguageCode;
                CommentLanguageCode = commentLanguageCode;
            }

            public List<LocalizationJsonLanguage> Languages { get; }
            public string DefaultLanguageCode { get; }
            public string CommentLanguageCode { get; }
        }
    }

    public sealed class LocalizationJsonEditorWindow : EditorWindow
    {
        private const float KeyWidth = 300f;
        private const float MemberWidth = 220f;
        private const float TextWidth = 280f;
        private const float RowHeight = 38f;
        private const float WindowMinWidth = 1160f;
        private const float WindowMinHeight = 620f;
        private const string MenuPath = "Tools/本地化/多语言文本编辑器";

        private LocalizationJsonEditorState state;
        private Vector2 scroll;
        private string search = string.Empty;
        private string newKey = string.Empty;
        private string newLanguageCode = string.Empty;
        private string newLanguageLabel = string.Empty;
        private string status = string.Empty;
        // 草稿与磁盘基线分别记录；SessionState跨窗口关闭和脚本重载保留，退出Unity后不保留。
        private string diskBaseline;
        private string cleanDraft;
        private string DraftKey => "OutpostLoop.LocalizationDraft." + Application.dataPath;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<LocalizationJsonEditorWindow>();
            ConfigureWindowForOpen(window);
            window.Show();
        }

        private static void ConfigureWindowForOpen(LocalizationJsonEditorWindow window)
        {
            var minimumSize = new Vector2(WindowMinWidth, WindowMinHeight);
            window.titleContent = new GUIContent("多语言文本编辑器");
            window.minSize = minimumSize;

            var currentPosition = window.position;
            if (currentPosition.width >= minimumSize.x && currentPosition.height >= minimumSize.y)
            {
                return;
            }

            window.position = new Rect(
                currentPosition.x,
                currentPosition.y,
                Mathf.Max(currentPosition.width, minimumSize.x),
                Mathf.Max(currentPosition.height, minimumSize.y));
        }

        private void OnEnable()
        {
            string cached = SessionState.GetString(DraftKey, string.Empty);
            if (!string.IsNullOrEmpty(cached))
            {
                try
                {
                    var draft = JObject.Parse(cached);
                    diskBaseline = draft.Value<string>("baseline");
                    cleanDraft = draft.Value<string>("clean");
                    var data = (JObject)draft["data"];
                    var languages = ((JArray)data["languages"]).Select(item => new LocalizationJsonLanguage(item.Value<string>("code"), item.Value<string>("label"))).ToList();
                    var rows = new List<LocalizationJsonRow>();
                    foreach (JObject item in (JArray)data["rows"])
                    {
                        var row = new LocalizationJsonRow(item.Value<string>("key"));
                        foreach (var text in ((JObject)item["texts"]).Properties()) row.SetText(text.Name, text.Value.Value<string>());
                        rows.Add(row);
                    }
                    state = new LocalizationJsonEditorState(languages, rows, data.Value<string>("default"), data.Value<string>("comment"));
                    LocalizationJsonEditorUtility.RefreshMemberNames(rows);
                    status = "已恢复本次Unity会话的未保存草稿；保存前将检查外部变化。";
                    return;
                }
                catch (Exception exception) { status = "失败：草稿恢复失败，请重载磁盘文件。" + exception.Message; return; }
            }
            Reload();
        }

        /// <summary>完整序列化草稿，保留尚未通过校验的空键或重复键，不写正式文件。</summary>
        private JObject DraftData()
        {
            return new JObject {
                ["languages"] = new JArray(state.Languages.Select(item => new JObject { ["code"] = item.Code, ["label"] = item.Label })),
                ["rows"] = new JArray(state.Rows.Select(row => new JObject { ["key"] = row.Key, ["texts"] = JObject.FromObject(row.Texts) })),
                ["default"] = state.DefaultLanguageCode, ["comment"] = state.CommentLanguageCode
            };
        }

        /// <summary>标记并缓存未保存草稿；不将运行时缓存当作跨Unity退出的备份。</summary>
        private void PersistDraft()
        {
            if (state == null) return;
            var data = DraftData();
            bool dirty = data.ToString(Newtonsoft.Json.Formatting.None) != cleanDraft;
            titleContent = new GUIContent("多语言文本编辑器" + (dirty ? " *" : ""));
            if (!dirty) { SessionState.EraseString(DraftKey); return; }
            SessionState.SetString(DraftKey, new JObject { ["baseline"] = diskBaseline, ["clean"] = cleanDraft, ["data"] = data }.ToString(Newtonsoft.Json.Formatting.None));
        }

        private void OnDisable() { PersistDraft(); }

        private void OnGUI()
        {
            if (state == null)
            {
                EditorGUILayout.HelpBox(status, MessageType.Error);
                if (GUILayout.Button("重新加载磁盘文件")) Reload();
                return;
            }
            try
            {
            DrawToolbar();
            if (state == null) return;
            DrawLanguageBar();
            DrawAddKeyBar();
            DrawTable();
            DrawStatusBar();
            }
            finally { PersistDraft(); }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重载", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                Reload();
                if (state == null) { EditorGUILayout.EndHorizontal(); return; }
            }

            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                Save();
            }

            if (GUILayout.Button("校验文本表", EditorStyles.toolbarButton, GUILayout.Width(88f)))
            {
                ValidateTables();
            }

            GUILayout.Space(8f);
            GUILayout.Label("默认", GUILayout.Width(32f));
            state.DefaultLanguageCode = DrawLanguagePopup(state.DefaultLanguageCode, 150f);
            GUILayout.Label("注释", GUILayout.Width(32f));
            state.CommentLanguageCode = DrawLanguagePopup(state.CommentLanguageCode, 150f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("搜索", GUILayout.Width(34f));
            search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.Width(280f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLanguageBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("新增语言", GUILayout.Width(60f));
            newLanguageCode = EditorGUILayout.TextField(newLanguageCode, GUILayout.Width(92f));
            newLanguageLabel = EditorGUILayout.TextField(newLanguageLabel, GUILayout.Width(140f));
            if (GUILayout.Button("添加", GUILayout.Width(52f)))
            {
                AddLanguage();
            }

            GUILayout.Space(12f);
            for (var i = 0; i < state.Languages.Count; i++)
            {
                var language = state.Languages[i];
                language.Label = EditorGUILayout.TextField(language.Label, GUILayout.Width(120f));
                GUILayout.Label(language.Code, EditorStyles.miniLabel, GUILayout.Width(56f));
                if (language.Code != state.DefaultLanguageCode && GUILayout.Button("删", GUILayout.Width(28f)))
                {
                    RemoveLanguage(language.Code);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAddKeyBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("新增 key", GUILayout.Width(58f));
            newKey = EditorGUILayout.TextField(newKey);
            if (GUILayout.Button("添加", GUILayout.Width(52f)))
            {
                AddKey();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTable()
        {
            var rows = LocalizationJsonEditorUtility.FilterRows(state.Rows, search);
            EditorGUILayout.LabelField($"显示 {rows.Count}/{state.Rows.Count}", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Texts", EditorStyles.boldLabel, GUILayout.Width(MemberWidth));
            GUILayout.Label("Key", EditorStyles.boldLabel, GUILayout.Width(KeyWidth));
            foreach (var language in state.Languages)
            {
                GUILayout.Label(language.DisplayName, EditorStyles.boldLabel, GUILayout.Width(TextWidth));
            }

            GUILayout.Label(string.Empty, GUILayout.Width(36f));
            EditorGUILayout.EndHorizontal();

            foreach (var row in rows)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel($"Texts.{row.MemberName}", GUILayout.Width(MemberWidth), GUILayout.Height(RowHeight));

                EditorGUI.BeginChangeCheck();
                row.Key = EditorGUILayout.TextField(row.Key, GUILayout.Width(KeyWidth), GUILayout.Height(RowHeight));
                if (EditorGUI.EndChangeCheck())
                {
                    LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                }

                foreach (var language in state.Languages)
                {
                    row.SetText(
                        language.Code,
                        EditorGUILayout.TextArea(row.GetText(language.Code), GUILayout.Width(TextWidth), GUILayout.Height(RowHeight)));
                }

                if (GUILayout.Button("删", GUILayout.Width(32f), GUILayout.Height(RowHeight)))
                {
                    state.Rows.Remove(row);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, status.StartsWith("失败", StringComparison.Ordinal) ? MessageType.Error : MessageType.Info);
            }
        }

        private string DrawLanguagePopup(string currentCode, float width)
        {
            var codes = state.Languages.Select(language => language.Code).ToArray();
            var labels = state.Languages.Select(language => language.DisplayName).ToArray();
            var index = Mathf.Max(0, Array.IndexOf(codes, currentCode));
            index = EditorGUILayout.Popup(index, labels, GUILayout.Width(width));
            return codes.Length == 0 ? currentCode : codes[index];
        }

        private void AddLanguage()
        {
            try
            {
                var code = newLanguageCode.Trim();
                LocalizationJsonEditorUtility.ValidateLanguageCode(code);
                if (state.Languages.Any(language => language.Code == code))
                {
                    throw new InvalidDataException($"语言已存在：{code}");
                }

                var label = string.IsNullOrWhiteSpace(newLanguageLabel) ? code : newLanguageLabel.Trim();
                state.Languages.Add(new LocalizationJsonLanguage(code, label));
                foreach (var row in state.Rows)
                {
                    row.SetText(code, row.GetText(state.DefaultLanguageCode));
                }

                newLanguageCode = string.Empty;
                newLanguageLabel = string.Empty;
                status = $"已添加语言 {code}，复制了 {state.DefaultLanguageCode} 文本作为待翻译内容，尚未完成翻译。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }

        private void RemoveLanguage(string code)
        {
            state.Languages.RemoveAll(language => language.Code == code);
            if (state.CommentLanguageCode == code)
            {
                state.CommentLanguageCode = state.DefaultLanguageCode;
            }

            status = $"已从语言列表移除 {code}。保存后运行时不会再列出它，原语言文件保留。";
        }

        private void AddKey()
        {
            var key = newKey.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                status = "失败：key 不能为空。";
                return;
            }

            if (state.Rows.Any(row => row.Key == key))
            {
                status = $"失败：key 已存在：{key}";
                return;
            }

            var row = new LocalizationJsonRow(key);
            foreach (var language in state.Languages)
            {
                row.SetText(language.Code, string.Empty);
            }

            state.Rows.Add(row);
            state.Rows.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
            LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
            newKey = string.Empty;
            status = $"已添加 key：{key}";
        }

        /// <summary>有草稿时先确认；严格加载失败后停止编辑，不回退为空表。</summary>
        private void Reload()
        {
            if ((!string.IsNullOrEmpty(SessionState.GetString(DraftKey, string.Empty)) ||
                 (state != null && DraftData().ToString(Newtonsoft.Json.Formatting.None) != cleanDraft)) &&
                !EditorUtility.DisplayDialog("重载文本表", "重载成功后将丢弃未保存草稿，是否继续？", "重载", "取消")) return;
            try
            {
                string before = LocalizationJsonEditorUtility.CaptureDiskSnapshot();
                var loaded = LocalizationJsonEditorUtility.LoadStateFromProject();
                if (before != LocalizationJsonEditorUtility.CaptureDiskSnapshot()) throw new IOException("加载期间语言文件发生变化，请重试。");
                state = loaded;
                diskBaseline = before;
                cleanDraft = DraftData().ToString(Newtonsoft.Json.Formatting.None);
                SessionState.EraseString(DraftKey);
                status = $"已载入 {state.Languages.Count} 种语言，{state.Rows.Count} 条文本。草稿仅在本次Unity会话内保留，退出前请保存。";
            }
            catch (Exception exception)
            {
                PersistDraft();
                state = null;
                status = $"失败：加载未完成，已停止编辑；已有缓存草稿仍保留。{exception.Message}";
            }
        }

        private void Save()
        {
            try
            {
                LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                if (diskBaseline == null || diskBaseline != LocalizationJsonEditorUtility.CaptureDiskSnapshot())
                    throw new IOException("磁盘语言文件已被外部修改，未写入；草稿仍保留，请核对并重载后重新应用修改。");
                LocalizationJsonEditorUtility.SaveStateWithSnapshot(state, diskBaseline);
                diskBaseline = LocalizationJsonEditorUtility.CaptureDiskSnapshot();
                cleanDraft = DraftData().ToString(Newtonsoft.Json.Formatting.None);
                SessionState.EraseString(DraftKey);
                status = $"已保存 {state.Languages.Count} 种语言，{state.Rows.Count} 条文本，文本常量已同步（内容相同时不重写）。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }

        private void ValidateTables()
        {
            try
            {
                // 原始文件与窗口草稿分别检查，不能用合并后的草稿替代磁盘校验。
                var diskTables = LocalizationValidatorMenu.LoadJsonTables();
                var diskReport = LocalizationValidator.ValidateJsonTables(diskTables);
                string scope = $"磁盘范围：{LocalizationEditorPaths.ConfigFolder}；语言：{string.Join(", ", diskTables.Keys)}。";
                string limitation = "仅检查JSON、键集合和简单{0}占位符；不完整验证格式说明符或翻译质量。";
                if (!diskReport.IsValid)
                {
                    status = $"磁盘校验失败（{diskReport.Issues.Count}个问题）：{scope}\n{string.Join("\n", diskReport.Issues.Select(issue => issue.ToString()))}\n{limitation}\n窗口草稿尚未校验，未保存任何文件。";
                    return;
                }
                LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                var report = LocalizationValidator.ValidateJsonTables(state.ToJsonByLanguage());
                if (!report.IsValid)
                {
                    status = $"磁盘校验通过；窗口草稿校验失败（{report.Issues.Count}个问题）：{scope}\n{string.Join("\n", report.Issues.Select(issue => issue.ToString()))}\n{limitation}";
                    return;
                }

                status = $"磁盘与窗口草稿校验通过，0个问题。{scope}\n窗口草稿：{state.Languages.Count} 种语言，{state.Rows.Count} 条文本；未保存。\n{limitation}";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }
    }
}
