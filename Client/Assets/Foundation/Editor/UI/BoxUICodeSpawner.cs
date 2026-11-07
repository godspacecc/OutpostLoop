using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public static class BoxUICodeSpawner
    {
        private const string DefaultNamespace = "Game";
        private const string BoxUiSuffix = "BoxUI";
        private const string ResourceUiRoot = "Assets/Game/Prefabs/UI/";
        private const string BoxFolderSegment = "/Box/";
        private const string UiScriptRoot = "Assets/Game/Scripts/Controller/UI/";
        private const string PendingPrefabPathKey = "OutpostLoop.EditorTools.PendingBoxUiPrefabPath";
        private const string PendingAssemblyTypeKey = "OutpostLoop.EditorTools.PendingBoxUiAssemblyType";

        private static bool _pendingBindingCheckScheduled;

        public static void AutoUpdateBoxUI(GameObject gameObject)
        {
            if (!TryValidateBoxUiPrefab(gameObject, out var prefabPath, out var referenceCollector))
            {
                return;
            }

            UICodeSpawner.WriteViewScript(
                gameObject,
                GetBoxUiScriptAssetPath(prefabPath),
                referenceCollector,
                BoxUiSuffix,
                "BaseBoxUI");

            SetPendingBinding(prefabPath, DefaultNamespace + "." + gameObject.name + BoxUiSuffix);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SchedulePendingBindingCheck();
        }

        private static bool TryValidateBoxUiPrefab(
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
                !normalizedPath.Contains(BoxFolderSegment, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[BoxUICodeSpawner] 仅支持 {ResourceUiRoot} 下 Box 目录内的 UI Prefab。");
                return false;
            }

            referenceCollector = gameObject.GetComponent<ReferenceCollector>();
            if (referenceCollector == null)
            {
                Debug.LogError($"[BoxUICodeSpawner] 缺少 ReferenceCollector：{gameObject.name}");
                return false;
            }

            return true;
        }

        private static string GetBoxUiScriptAssetPath(string prefabPath)
        {
            return UICodeSpawner.GetGeneratedScriptPath(prefabPath, UiScriptRoot, BoxUiSuffix);
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
