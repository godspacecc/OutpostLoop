#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 提供资源更新公告的受限编辑窗口，自动绑定版本和语言，不允许手动输入版本号。
    /// </summary>
    public sealed class ResourceUpdateNoticeEditorWindow : EditorWindow
    {
#pragma warning disable CS0649
        internal static System.Func<object> ResolveVersionBindingForTests;
        internal static System.Func<object, string, string, string, string> SaveForTests;
#pragma warning restore CS0649

        private IReadOnlyList<ResourceUpdateNoticeEditorUtility.LanguageInfo> languages;
        private ResourceUpdateNoticeEditorUtility.VersionBinding binding;
        private ResourceUpdateNoticeEditorUtility.NoticeEditState state;
        private string defaultLanguage;
        private Vector2 scroll;
        private string message;
        // 有未保存修改或待重试同步时，不允许焦点和项目事件覆盖当前草稿。
        private bool dirty;
        private bool messageIsError;
        // 仅随编辑器窗口序列化草稿，不写公告源文件或发布目录。
        [SerializeField] private string serializedDraft;

        // 保留脚本重载前的编辑目标、语言和未保存文本。
        private sealed class DraftState
        {
            // 编辑开始时固定的保存目标。
            public ResourceUpdateNoticeEditorUtility.VersionBinding Binding;
            // 用可写字段保存原稿，避免依赖只读运行时内容结构的反序列化。
            public string Version;
            public bool Enabled;
            public Dictionary<string, DraftText> Texts;
            // 草稿对应的语言列表和默认语言。
            public List<ResourceUpdateNoticeEditorUtility.LanguageInfo> Languages;
            public string DefaultLanguage;
        }

        // 草稿允许空标题和空条目，保留编辑过程中的原始输入。
        private sealed class DraftText
        {
            public string Title;
            public string Summary;
            public List<string> Items;
        }

        [MenuItem("Tools/服务器/资源更新公告", false, 340)]
        public static void Open()
        {
            GetWindow<ResourceUpdateNoticeEditorWindow>("资源更新公告");
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += Reload;
            RestoreDraft();
            Reload();
        }

        private void OnDisable()
        {
            CacheDraft();
            EditorApplication.projectChanged -= Reload;
        }

        private static ResourceUpdateNoticeEditorUtility.VersionBinding ResolveCurrentBinding()
        {
            var testBinding = ResolveVersionBindingForTests?.Invoke();
            return testBinding != null
                ? (ResourceUpdateNoticeEditorUtility.VersionBinding)testBinding
                : ResourceUpdateNoticeEditorUtility.ResolveVersionBinding();
        }

        private void OnFocus()
        {
            Reload();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重新加载", EditorStyles.toolbarButton, GUILayout.Width(72)))
            {
                RequestReload();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (binding == null || !binding.CanSave || state == null)
            {
                EditorGUILayout.HelpBox(message ?? binding?.Error ?? "未找到有效资源版本，无法保存公告。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("目标版本", $"{binding.Platform} / {binding.Version}");
            EditorGUILayout.LabelField("状态", binding.Status + (dirty ? "（有未保存修改或待重试同步）" : string.Empty));
            EditorGUI.BeginChangeCheck();
            state.Enabled = EditorGUILayout.Toggle("启用", state.Enabled);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var language in languages)
            {
                DrawLanguage(language);
            }

            foreach (var inactive in state.Texts.Keys.Where(key => languages.All(item => item.Code != key)).ToArray())
            {
                EditorGUILayout.HelpBox($"已停用语言保留历史文本：{inactive}", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck())
            {
                dirty = true;
                CacheDraft();
            }

            // 沿用现有提示区域，说明实际写入范围和两平台共用源文件的约定。
            var context = $"公告源文件（PC/Android 共用，同版本共用内容）：{ResourceUpdateNoticeEditorUtility.NoticeFilePath}\n" +
                $"同步发布目录：{binding.LatestBundle ?? "无，本次仅保存源文件"}";
            EditorGUILayout.HelpBox(context + (string.IsNullOrWhiteSpace(message) ? string.Empty : "\n" + message),
                messageIsError ? MessageType.Warning : MessageType.Info);

            using (new EditorGUI.DisabledScope(!binding.CanSave))
            {
                if (GUILayout.Button("保存公告"))
                {
                    Save();
                }
            }
        }

        private void DrawLanguage(ResourceUpdateNoticeEditorUtility.LanguageInfo language)
        {
            if (!state.Texts.TryGetValue(language.Code, out var content))
            {
                content = new ResourceUpdateNoticeContent(binding.Version, string.Empty, string.Empty, new string[0]);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"{language.Code}  {language.Label}", EditorStyles.boldLabel);
            var title = EditorGUILayout.TextField("标题", content.Title);
            var summary = EditorGUILayout.TextField("概要", content.Summary);
            var items = (content.Items ?? new string[0]).ToList();
            var remove = -1;
            for (var i = 0; i < items.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                items[i] = EditorGUILayout.TextField($"条目 {i + 1}", items[i]);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    remove = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (remove >= 0)
            {
                items.RemoveAt(remove);
                GUI.changed = true;
            }

            if (items.Count < 20 && GUILayout.Button("添加条目", GUILayout.Width(96)))
            {
                items.Add(string.Empty);
                GUI.changed = true;
            }

            state.Texts[language.Code] = new ResourceUpdateNoticeContent(binding.Version, title, summary, items);
        }

        private void Reload()
        {
            try
            {
                var current = ResolveCurrentBinding();
                if (dirty)
                {
                    if (!ResourceUpdateNoticeEditorUtility.IsSameTarget(binding, current))
                    {
                        message = "当前平台、版本或发布目录已变化，保留原草稿并停止保存。请先复制需要保留的文字，再重新加载，或切回原目标。";
                        messageIsError = true;
                    }
                    return;
                }

                LoadCurrent(current);
            }
            catch (System.Exception exception)
            {
                message = $"公告重新加载失败，现有内容未替换：{exception.Message}";
                messageIsError = true;
            }
            finally { Repaint(); }
        }

        // 完整读取成功后才替换窗口内容；失败不会清除原稿。
        private void LoadCurrent(ResourceUpdateNoticeEditorUtility.VersionBinding current)
        {
            if (current == null || !current.CanSave)
                throw new System.InvalidOperationException(current?.Error ?? "当前没有可编辑的资源版本。");
            var newLanguages = ResourceUpdateNoticeEditorUtility.LoadLanguages(out var newDefault);
            var newState = ResourceUpdateNoticeEditorUtility.LoadEditState(current.Version, newLanguages);
            if (!ResourceUpdateNoticeEditorUtility.IsSameTarget(binding, current))
            {
                message = null;
                messageIsError = false;
            }
            binding = current;
            languages = newLanguages;
            defaultLanguage = newDefault;
            state = newState;
            dirty = false;
            CacheDraft();
        }

        // 显式重新加载才允许放弃草稿，并先确认；读取失败仍保留草稿。
        private void RequestReload()
        {
            if (dirty && !EditorUtility.DisplayDialog("重新加载公告", "重新加载会丢弃当前未保存内容及待重试状态。是否继续？", "丢弃并重新加载", "保留编辑"))
                return;
            try
            {
                LoadCurrent(ResolveCurrentBinding());
                message = "已重新加载当前目标公告。";
                messageIsError = false;
            }
            catch (System.Exception exception)
            {
                message = $"重新加载失败，现有内容未替换：{exception.Message}";
                messageIsError = true;
            }
            Repaint();
        }

        // 保存窗口草稿以承受脚本重载，并让关闭窗口时提示未保存内容。
        private void CacheDraft()
        {
            hasUnsavedChanges = dirty;
            saveChangesMessage = "公告还有未保存内容或发布目录同步尚未完成。";
            if (!dirty)
                serializedDraft = null;
            else if (state != null)
            {
                serializedDraft = JsonConvert.SerializeObject(new DraftState
                {
                    Binding = binding, Version = state.Version, Enabled = state.Enabled,
                    Texts = state.Texts.ToDictionary(pair => pair.Key, pair => new DraftText
                    {
                        Title = pair.Value.Title, Summary = pair.Value.Summary, Items = pair.Value.Items?.ToList()
                    }),
                    Languages = languages?.ToList(), DefaultLanguage = defaultLanguage
                });
            }
        }

        // 还原草稿不重新绑定目标，也不写入任何公告。
        private void RestoreDraft()
        {
            if (string.IsNullOrWhiteSpace(serializedDraft))
                return;
            try
            {
                var draft = JsonConvert.DeserializeObject<DraftState>(serializedDraft);
                if (draft?.Binding == null || draft.Texts == null || draft.Languages == null || draft.Version != draft.Binding.Version)
                    throw new System.InvalidOperationException("窗口草稿缺少目标或文本。");
                var restoredState = new ResourceUpdateNoticeEditorUtility.NoticeEditState
                {
                    Version = draft.Version, Enabled = draft.Enabled
                };
                foreach (var pair in draft.Texts)
                {
                    if (pair.Value == null)
                        throw new System.InvalidOperationException("窗口草稿存在无效语言文本。");
                    restoredState.Texts[pair.Key] = new ResourceUpdateNoticeContent(
                        draft.Version, pair.Value.Title, pair.Value.Summary, pair.Value.Items);
                }
                binding = draft.Binding;
                state = restoredState;
                languages = draft.Languages;
                defaultLanguage = draft.DefaultLanguage;
                dirty = true;
                hasUnsavedChanges = true;
                saveChangesMessage = "公告还有未保存内容或发布目录同步尚未完成。";
            }
            catch (System.Exception exception)
            {
                dirty = true;
                message = $"窗口草稿恢复失败，请保留窗口并检查：{exception.Message}";
                messageIsError = true;
            }
        }

        // 关闭窗口的保存操作复用相同目标检查，失败不标记为已保存。
        public override void SaveChanges()
        {
            Save();
            if (!dirty)
                base.SaveChanges();
        }

        // 仅在用户明确放弃时清除窗口草稿。
        public override void DiscardChanges()
        {
            dirty = false;
            serializedDraft = null;
            base.DiscardChanges();
        }

        private void Save()
        {
            try
            {
                var current = ResolveCurrentBinding();
                if (current == null || !current.CanSave)
                    throw new System.InvalidOperationException(current?.Error ?? "当前没有可保存的资源版本。");
                if (!ResourceUpdateNoticeEditorUtility.IsSameTarget(binding, current) || state == null || state.Version != binding.Version)
                    throw new System.InvalidOperationException("当前平台、版本或发布目录已变化，未保存也未重新绑定草稿。请先保留文字并重新加载，或切回原目标。");
                var currentLanguages = ResourceUpdateNoticeEditorUtility.LoadLanguages(out var currentDefault);
                if (currentDefault != defaultLanguage || !currentLanguages.Select(item => item.Code).OrderBy(code => code)
                    .SequenceEqual(languages.Select(item => item.Code).OrderBy(code => code)))
                    throw new System.InvalidOperationException("语言配置已变化，未保存。请先保留文字并重新加载。");

                if (SaveForTests != null)
                {
                    message = SaveForTests(state, defaultLanguage, binding.LatestBundle, binding.Status);
                    dirty = false;
                    messageIsError = false;
                }
                else
                {
                    var result = ResourceUpdateNoticeEditorUtility.SaveDetailed(state, defaultLanguage, binding.LatestBundle, null, binding.Status);
                    message = result.Message;
                    dirty = !result.Success;
                    messageIsError = !result.Success;
                }
            }
            catch (System.Exception exception)
            {
                message = $"{exception.Message}\n编辑内容已保留。";
                dirty = true;
                messageIsError = true;
            }
            CacheDraft();
            Repaint();
        }
    }
}
#endif
