using UnityEditor;
using UnityEngine;

namespace Game
{
    public static class UIEditorHelper
    {
        [MenuItem("Assets/智能更新UI", false, -1)]
        public static void SmartUpdateUI()
        {
            if (Selection.activeObject is GameObject gameObject)
            {
                UICodeSpawner.AutoUpdateUI(gameObject);
            }
            else
            {
                Debug.LogError("请选择一个 UI Prefab。");
            }
        }

        [MenuItem("Assets/智能更新BoxUI", false, -3)]
        public static void SmartUpdateBoxUI()
        {
            if (Selection.activeObject is GameObject gameObject)
            {
                BoxUICodeSpawner.AutoUpdateBoxUI(gameObject);
            }
            else
            {
                Debug.LogError("请选择一个 BoxUI Prefab。");
            }
        }

        [MenuItem("Assets/智能更新Page", false, -4)]
        public static void SmartUpdatePage()
        {
            if (Selection.activeObject is GameObject gameObject)
            {
                PageCodeSpawner.AutoUpdatePage(gameObject);
            }
            else
            {
                Debug.LogError("请选择一个 Page Prefab。");
            }
        }
    }
}
