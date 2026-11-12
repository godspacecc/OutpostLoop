using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game
{
    public sealed class SceneSwitcherWindow : EditorWindow
    {
        public const string SearchPrefsKey = "OutpostLoop.SceneSwitcher.Search";
        public const string FavoritesPrefsKey = "OutpostLoop.SceneSwitcher.Favorites";

        private readonly List<string> _scenePaths = new List<string>();
        private readonly HashSet<string> _favoriteScenePaths = new HashSet<string>(StringComparer.Ordinal);
        private Vector2 _scrollPosition;
        private string _searchText = string.Empty;

        [MenuItem("Tools/场景/场景切换", false, 310)]
        public static void OpenWindow()
        {
            var window = GetWindow<SceneSwitcherWindow>();
            window.titleContent = new GUIContent("场景切换");
            window.minSize = new Vector2(420f, 260f);
            window.RefreshSceneList();
            window.Show();
        }

        /// <summary>只列出Assets内场景，搜索范围与窗口提示保持一致。</summary>
        public static IReadOnlyList<string> GetScenePaths()
        {
            return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string[] GetFavoriteScenePathsForTests()
        {
            return DeserializeScenePaths(EditorPrefs.GetString(FavoritesPrefsKey, string.Empty));
        }

        public static void SetFavoriteScenePathsForTests(IEnumerable<string> scenePaths)
        {
            SaveFavoriteScenePaths(scenePaths);
        }

        public static string SortScenePathsForTests(IEnumerable<string> scenePaths, IEnumerable<string> favoriteScenePaths)
        {
            return string.Join("|", SortScenePathsForDisplay(scenePaths, favoriteScenePaths));
        }

        private void OnEnable()
        {
            _searchText = EditorPrefs.GetString(SearchPrefsKey, string.Empty);
            _favoriteScenePaths.Clear();
            foreach (string scenePath in LoadFavoriteScenePaths())
            {
                _favoriteScenePaths.Add(scenePath);
            }

            RefreshSceneList();
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(SearchPrefsKey, _searchText ?? string.Empty);
        }

        private void OnProjectChange()
        {
            RefreshSceneList();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_scenePaths.Count == 0)
            {
                EditorGUILayout.HelpBox("Assets 下没有找到场景文件。", MessageType.Info);
                return;
            }

            IEnumerable<string> filteredScenePaths = _scenePaths;
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filteredScenePaths = filteredScenePaths.Where(path =>
                    Path.GetFileNameWithoutExtension(path).IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            DrawBuildSceneQuickBar();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (string scenePath in filteredScenePaths)
            {
                DrawSceneRow(scenePath);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.toolbar);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("搜索", GUILayout.Width(48f));

            string newSearchText = GUILayout.TextField(
                _searchText,
                EditorStyles.toolbarTextField,
                GUILayout.ExpandWidth(true));
            if (newSearchText != _searchText)
            {
                _searchText = newSearchText;
                EditorPrefs.SetString(SearchPrefsKey, _searchText ?? string.Empty);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                RefreshSceneList();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawBuildSceneQuickBar()
        {
            string[] buildScenePaths = EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (buildScenePaths.Length == 0)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("构建", GUILayout.Width(48f));

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                foreach (string scenePath in buildScenePaths)
                {
                    if (GUILayout.Button(Path.GetFileNameWithoutExtension(scenePath), GUILayout.Height(24f)))
                    {
                        OpenScene(scenePath);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSceneRow(string scenePath)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            bool isFavorite = _favoriteScenePaths.Contains(scenePath);
            if (GUILayout.Button(isFavorite ? "已藏" : "收藏", GUILayout.Width(44f), GUILayout.Height(36f)))
            {
                ToggleFavoriteScenePath(scenePath);
                RefreshSceneList();
            }

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(scenePath), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(scenePath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndVertical();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("打开", GUILayout.Width(72f), GUILayout.Height(36f)))
                {
                    OpenScene(scenePath);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>所有打开入口共用保护；保留Unity保存询问，不主动保存、丢弃或停止运行。</summary>
        private static void OpenScene(string scenePath)
        {
            try
            {
                if (!CanOpenScene(scenePath, out string error))
                {
                    ShowOpenSceneError(error);
                    return;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;

                // 保存对话框结束后再次核对，避免目标失效或状态变化后继续切换。
                if (!CanOpenScene(scenePath, out error))
                {
                    ShowOpenSceneError(error);
                    return;
                }
                EditorSceneManager.OpenScene(scenePath);
            }
            catch (Exception exception)
            {
                ShowOpenSceneError($"打开场景失败：{scenePath}\n{exception.Message}");
            }
        }

        /// <summary>拒绝运行中切换以及不属于Assets、已删除或无法识别的场景。</summary>
        private static bool CanOpenScene(string scenePath, out string error)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                error = "运行中或准备进入运行模式时不能切换场景，请自行退出运行后重试。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(scenePath) ||
                !scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                error = $"场景不存在、无法识别或不在Assets目录：{scenePath}。请刷新列表并检查场景路径。";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>明确报告打开失败，不自动改写构建列表或尝试其他场景。</summary>
        private static void ShowOpenSceneError(string error)
        {
            Debug.LogError($"[场景切换] {error}");
            EditorUtility.DisplayDialog("场景未打开", error, "确定");
        }

        private void RefreshSceneList()
        {
            _scenePaths.Clear();
            _scenePaths.AddRange(SortScenePathsForDisplay(GetScenePaths(), _favoriteScenePaths));
        }

        private void ToggleFavoriteScenePath(string scenePath)
        {
            if (!_favoriteScenePaths.Add(scenePath))
            {
                _favoriteScenePaths.Remove(scenePath);
            }

            SaveFavoriteScenePaths(_favoriteScenePaths);
        }

        private static IEnumerable<string> LoadFavoriteScenePaths()
        {
            return DeserializeScenePaths(EditorPrefs.GetString(FavoritesPrefsKey, string.Empty));
        }

        private static void SaveFavoriteScenePaths(IEnumerable<string> scenePaths)
        {
            EditorPrefs.SetString(FavoritesPrefsKey, SerializeScenePaths(scenePaths));
        }

        private static IEnumerable<string> SortScenePathsForDisplay(
            IEnumerable<string> scenePaths,
            IEnumerable<string> favoriteScenePaths)
        {
            var favoriteSet = new HashSet<string>(favoriteScenePaths ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var buildOrder = EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select((scene, index) => new { scene.path, index })
                .GroupBy(item => item.path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

            return (scenePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(path => favoriteSet.Contains(path))
                .ThenBy(path => buildOrder.TryGetValue(path, out int index) ? index : int.MaxValue)
                .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string SerializeScenePaths(IEnumerable<string> scenePaths)
        {
            return string.Join("|", (scenePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal));
        }

        private static string[] DeserializeScenePaths(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Array.Empty<string>();
            }

            return rawValue
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
