using UnityEditor;
using UnityEngine;

namespace Game
{
    [CustomEditor(typeof(CustomGridLayoutGroup))]
    public sealed class CustomGridLayoutGroupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("列表项仅在运行时创建和复用", MessageType.Info);
        }
    }
}
