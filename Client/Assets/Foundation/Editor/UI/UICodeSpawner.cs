using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public static class UICodeSpawner
    {
        private const string DefaultNamespace = "Game";
        private const string UiSuffix = "UI";
        private const string UiCtrlSuffix = "UICtrl";
        private const string GameAssetRoot = "Assets/Game/";
        private const string ResourceUiRoot = "Assets/Game/Prefabs/UI/";
        private const string UiScriptRoot = "Assets/Game/Scripts/Controller/UI/";
        private const string UiCtrlScriptRoot = "Assets/Game/Scripts/Controller/UICtrl/";
        private const string PendingPrefabPathKey = "OutpostLoop.PendingUiPrefabPath";
        private const string PendingAssemblyTypeKey = "OutpostLoop.PendingUiAssemblyType";

        private static bool _pendingBindingCheckScheduled;

        public static void AutoUpdateUI(GameObject gameObject)
        {
            if (!TryValidateUiPrefab(gameObject, out var prefabPath, out var referenceCollector))
            {
                return;
            }

            WriteUiScript(gameObject, prefabPath, referenceCollector);
            WriteUiCtrlScript(gameObject, prefabPath);

            SetPendingUiBinding(prefabPath, DefaultNamespace + "." + gameObject.name + UiSuffix);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SchedulePendingBindingCheck();
        }

        private static bool TryValidateUiPrefab(
            GameObject gameObject,
            out string prefabPath,
            out ReferenceCollector referenceCollector)
        {
            prefabPath = AssetDatabase.GetAssetPath(gameObject);
            referenceCollector = null;

            if (gameObject == null)
            {
                Debug.LogError("[UICodeSpawner] 请选择有效对象，当前对象为空。");
                return false;
            }

            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[UICodeSpawner] 请在 Project 视图选择 UI Prefab。");
                return false;
            }

            if (!prefabPath.StartsWith(ResourceUiRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[UICodeSpawner] 仅支持 {ResourceUiRoot} 下的 UI Prefab。");
                return false;
            }

            referenceCollector = gameObject.GetComponent<ReferenceCollector>();
            if (referenceCollector == null)
            {
                Debug.LogError($"[UICodeSpawner] 缺少 ReferenceCollector：{gameObject.name}");
                return false;
            }

            return true;
        }

        private static void WriteUiScript(GameObject gameObject, string prefabPath, ReferenceCollector referenceCollector)
        {
            var assetPath = GetUiScriptAssetPath(prefabPath);
            EnsureDirectoryExists(assetPath);

            var builder = new StringBuilder();
            builder.AppendLine("using UnityEngine;");
            AppendOptionalUsings(builder, referenceCollector);
            builder.AppendLine();
            builder.AppendLine($"namespace {DefaultNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"    public class {gameObject.name}{UiSuffix} : BaseUI");
            builder.AppendLine("    {");

            foreach (var info in ValidReferences(referenceCollector))
            {
                builder.AppendLine($"        public {GetShortTypeName(info.TypeName)} {info.key};");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            File.WriteAllText(GetAbsolutePath(assetPath), builder.ToString(), new UTF8Encoding(false));
        }

        private static void WriteUiCtrlScript(GameObject gameObject, string prefabPath)
        {
            var assetPath = GetUiCtrlScriptAssetPath(prefabPath);
            if (File.Exists(GetAbsolutePath(assetPath)))
            {
                return;
            }

            EnsureDirectoryExists(assetPath);
            var resourcePath = GetUiPrefabResourcePath(prefabPath);

            var builder = new StringBuilder();
            builder.AppendLine("namespace Game");
            builder.AppendLine("{");
            builder.AppendLine($"    public class {gameObject.name}{UiCtrlSuffix} : BaseUICtrl");
            builder.AppendLine("    {");
            builder.AppendLine($"        private {gameObject.name}{UiSuffix} UI => BaseUI as {gameObject.name}{UiSuffix};");
            builder.AppendLine();
            builder.AppendLine("        protected override string Path()");
            builder.AppendLine("        {");
            builder.AppendLine($"            return \"{resourcePath}\";");
            builder.AppendLine("        }");
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
            builder.AppendLine();
            builder.AppendLine("        public override UILayer Layer()");
            builder.AppendLine("        {");
            builder.AppendLine("            return UILayer.Dialog;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            File.WriteAllText(GetAbsolutePath(assetPath), builder.ToString(), new UTF8Encoding(false));
        }

        internal static string GetUiPrefabResourcePath(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath) ||
                !prefabPath.StartsWith(ResourceUiRoot, StringComparison.OrdinalIgnoreCase) ||
                !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"UI Prefab 路径无效：{prefabPath}", nameof(prefabPath));
            }

            return prefabPath
                .Substring(GameAssetRoot.Length, prefabPath.Length - GameAssetRoot.Length - ".prefab".Length)
                .Replace("\\", "/");
        }

        private static string GetUiScriptAssetPath(string prefabPath)
        {
            return GetGeneratedScriptPath(prefabPath, UiScriptRoot, UiSuffix);
        }

        private static string GetUiCtrlScriptAssetPath(string prefabPath)
        {
            return GetGeneratedScriptPath(prefabPath, UiCtrlScriptRoot, UiCtrlSuffix);
        }

        internal static string GetGeneratedScriptPath(string prefabPath, string scriptRoot, string suffix)
        {
            var relativePath = prefabPath.Substring(ResourceUiRoot.Length);
            var directory = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(prefabPath) + suffix + ".cs";
            return string.IsNullOrEmpty(directory) ? scriptRoot + fileName : scriptRoot + directory + "/" + fileName;
        }

        internal static void WriteViewScript(
            GameObject gameObject,
            string assetPath,
            ReferenceCollector referenceCollector,
            string suffix,
            string baseClass)
        {
            EnsureDirectoryExists(assetPath);

            var builder = new StringBuilder();
            builder.AppendLine("using UnityEngine;");
            AppendOptionalUsings(builder, referenceCollector);
            builder.AppendLine();
            builder.AppendLine($"namespace {DefaultNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"    public class {gameObject.name}{suffix} : {baseClass}");
            builder.AppendLine("    {");

            foreach (var info in ValidReferences(referenceCollector))
            {
                builder.AppendLine($"        public {GetShortTypeName(info.TypeName)} {info.key};");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            File.WriteAllText(GetAbsolutePath(assetPath), builder.ToString(), new UTF8Encoding(false));
        }

        internal static void AppendOptionalUsings(StringBuilder builder, ReferenceCollector referenceCollector)
        {
            if (referenceCollector.data.Any(item =>
                    item != null &&
                    !string.IsNullOrWhiteSpace(item.TypeName) &&
                    item.TypeName.StartsWith("TMPro.", StringComparison.Ordinal)))
            {
                builder.AppendLine("using TMPro;");
            }

            if (referenceCollector.data.Any(item =>
                    item != null &&
                    !string.IsNullOrWhiteSpace(item.TypeName) &&
                    item.TypeName.StartsWith("UnityEngine.UI.", StringComparison.Ordinal)))
            {
                builder.AppendLine("using UnityEngine.UI;");
            }

            // 生成的字段使用短类型名，触屏控件需要其独立命名空间。
            if (referenceCollector.data.Any(item =>
                    item != null &&
                    !string.IsNullOrWhiteSpace(item.TypeName) &&
                    item.TypeName.StartsWith("UnityEngine.InputSystem.OnScreen.", StringComparison.Ordinal)))
            {
                builder.AppendLine("using UnityEngine.InputSystem.OnScreen;");
            }
        }

        internal static ReferenceCollectorData[] ValidReferences(ReferenceCollector referenceCollector)
        {
            return referenceCollector.data
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.key))
                .ToArray();
        }

        internal static string GetShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return nameof(GameObject);
            }

            var index = fullTypeName.LastIndexOf('.');
            return index >= 0 ? fullTypeName.Substring(index + 1) : fullTypeName;
        }

        internal static string GetAbsolutePath(string assetPath)
        {
            var assetsRoot = Application.dataPath.Replace("\\", "/");
            return assetPath.Replace("Assets", assetsRoot).Replace("/", Path.DirectorySeparatorChar.ToString());
        }

        internal static void EnsureDirectoryExists(string assetPath)
        {
            var directory = Path.GetDirectoryName(GetAbsolutePath(assetPath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        internal static Object ResolveReferenceObject(Object source, Type expectedType)
        {
            if (source == null || expectedType == null)
            {
                return null;
            }

            if (expectedType.IsInstanceOfType(source))
            {
                return source;
            }

            if (source is GameObject gameObject)
            {
                if (expectedType == typeof(GameObject))
                {
                    return gameObject;
                }

                if (typeof(Component).IsAssignableFrom(expectedType))
                {
                    return gameObject.GetComponent(expectedType);
                }
            }

            if (source is Component component)
            {
                if (expectedType == typeof(GameObject))
                {
                    return component.gameObject;
                }

                if (typeof(Component).IsAssignableFrom(expectedType))
                {
                    return component.GetComponent(expectedType);
                }
            }

            return null;
        }

        private static void SetPendingUiBinding(string prefabPath, string assemblyType)
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
            BindGeneratedUiComponent(prefabPath, assemblyType);
        }

        private static void BindGeneratedUiComponent(string assetPath, string assemblyType)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var generatedType = ResolveGeneratedType(assemblyType);
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
                var field = generatedType.GetField(info.key, BindingFlags.Public | BindingFlags.Instance);
                var reference = ResolveReferenceObject(info.gameObject, field?.FieldType);
                if (property != null && reference != null)
                {
                    property.objectReferenceValue = reference;
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        private static Type ResolveGeneratedType(string assemblyType)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(assemblyType, false))
                .FirstOrDefault(type => type != null);
        }
    }
}
