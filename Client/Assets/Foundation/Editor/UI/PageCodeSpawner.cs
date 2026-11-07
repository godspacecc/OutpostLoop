using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public static class PageCodeSpawner
    {
        private const string DefaultNamespace = "Game";
        private const string PageUiSuffix = "PageUI";
        private const string PageCtrlSuffix = "PageCtrl";
        private const string ResourceUiRoot = "Assets/Game/Prefabs/UI/";
        private const string PageFolderSegment = "/Page/";
        private const string UiScriptRoot = "Assets/Game/Scripts/Controller/UI/";
        private const string UiCtrlScriptRoot = "Assets/Game/Scripts/Controller/UICtrl/";
        private const string PendingPrefabPathKey = "OutpostLoop.EditorTools.PendingPageUiPrefabPath";
        private const string PendingAssemblyTypeKey = "OutpostLoop.EditorTools.PendingPageUiAssemblyType";

        private static bool _pendingBindingCheckScheduled;

        public static void AutoUpdatePage(GameObject gameObject)
        {
            if (!TryValidatePagePrefab(gameObject, out var prefabPath, out var referenceCollector))
            {
                return;
            }

            UICodeSpawner.WriteViewScript(
                gameObject,
                GetPageUiScriptAssetPath(prefabPath),
                referenceCollector,
                PageUiSuffix,
                "BaseUI");
            WritePageCtrlScript(gameObject, prefabPath);

            SetPendingBinding(prefabPath, DefaultNamespace + "." + gameObject.name + PageUiSuffix);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SchedulePendingBindingCheck();
        }

        private static bool TryValidatePagePrefab(
            GameObject gameObject,
            out string prefabPath,
            out ReferenceCollector referenceCollector)
        {
            prefabPath = AssetDatabase.GetAssetPath(gameObject);
            referenceCollector = null;
            var normalizedPath = prefabPath.Replace("\\", "/");
            if (gameObject == null ||
                string.IsNullOrEmpty(prefabPath) ||
                !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                !normalizedPath.StartsWith(ResourceUiRoot, StringComparison.OrdinalIgnoreCase) ||
                !normalizedPath.Contains(PageFolderSegment, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[PageCodeSpawner] 仅支持 {ResourceUiRoot} 下 Page 目录内的 Prefab。");
                return false;
            }

            referenceCollector = gameObject.GetComponent<ReferenceCollector>();
            if (referenceCollector == null)
            {
                Debug.LogError($"[PageCodeSpawner] 缺少 ReferenceCollector：{gameObject.name}");
                return false;
            }

            return true;
        }

        private static void WritePageCtrlScript(GameObject gameObject, string prefabPath)
        {
            var assetPath = GetPageCtrlScriptAssetPath(prefabPath);
            if (File.Exists(UICodeSpawner.GetAbsolutePath(assetPath)))
            {
                return;
            }

            UICodeSpawner.EnsureDirectoryExists(assetPath);
            var builder = new StringBuilder();
            builder.AppendLine("namespace Game");
            builder.AppendLine("{");
            builder.AppendLine($"    public class {gameObject.name}{PageCtrlSuffix} : BasePageCtrl");
            builder.AppendLine("    {");
            builder.AppendLine($"        private {gameObject.name}{PageUiSuffix} UI => BaseUI as {gameObject.name}{PageUiSuffix};");
            builder.AppendLine();
            builder.AppendLine("        protected override void OnInit()");
            builder.AppendLine("        {");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        protected override void OnShow()");
            builder.AppendLine("        {");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        protected override void OnHide()");
            builder.AppendLine("        {");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            File.WriteAllText(UICodeSpawner.GetAbsolutePath(assetPath), builder.ToString(), new UTF8Encoding(false));
        }

        private static string GetPageUiScriptAssetPath(string prefabPath)
        {
            return UICodeSpawner.GetGeneratedScriptPath(prefabPath, UiScriptRoot, PageUiSuffix);
        }

        private static string GetPageCtrlScriptAssetPath(string prefabPath)
        {
            return UICodeSpawner.GetGeneratedScriptPath(prefabPath, UiCtrlScriptRoot, PageCtrlSuffix);
        }

        private static void SetPendingBinding(string prefabPath, string assemblyType)
        {
            EditorPrefs.SetString(PendingPrefabPathKey, prefabPath);
            EditorPrefs.SetString(PendingAssemblyTypeKey, assemblyType);
        }

        private static void SchedulePendingBindingCheck()
        {
            if (_pendingBindingCheckScheduled)
            {
                return;
            }

            _pendingBindingCheckScheduled = true;
            EditorApplication.delayCall += ProcessPendingBindingsWhenReady;
        }

        private static void ProcessPendingBindingsWhenReady()
        {
            _pendingBindingCheckScheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                SchedulePendingBindingCheck();
                return;
            }

            var prefabPath = EditorPrefs.GetString(PendingPrefabPathKey);
            var assemblyType = EditorPrefs.GetString(PendingAssemblyTypeKey);
            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(assemblyType))
            {
                return;
            }

            EditorPrefs.DeleteKey(PendingPrefabPathKey);
            EditorPrefs.DeleteKey(PendingAssemblyTypeKey);
            BindGeneratedComponent(prefabPath, assemblyType);
        }

        private static void BindGeneratedComponent(string assetPath, string assemblyType)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var generatedType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(assemblyType, false))
                .FirstOrDefault(type => type != null);
            if (prefab == null || generatedType == null)
            {
                return;
            }

            var referenceCollector = prefab.GetComponent<ReferenceCollector>();
            if (referenceCollector == null)
            {
                return;
            }

            var oldComponent = prefab.GetComponent(generatedType);
            if (oldComponent != null)
            {
                Object.DestroyImmediate(oldComponent, true);
            }

            var component = prefab.AddComponent(generatedType);
            var serializedObject = new SerializedObject(component);
            foreach (var info in referenceCollector.data)
            {
                var property = serializedObject.FindProperty(info.key);
                var field = generatedType.GetField(info.key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var reference = UICodeSpawner.ResolveReferenceObject(info.gameObject, field?.FieldType);
                if (property != null && reference != null)
                {
                    property.objectReferenceValue = reference;
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }
    }
}
