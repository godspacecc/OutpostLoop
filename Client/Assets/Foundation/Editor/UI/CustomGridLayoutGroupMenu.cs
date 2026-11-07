using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public static class CustomGridLayoutGroupMenu
    {
        [MenuItem("GameObject/UI/Custom Grid Layout Group", false, 2001)]
        public static void CreateCustomGridLayoutGroup(MenuCommand menuCommand)
        {
            var go = new GameObject("Custom Grid Layout Group", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var rectTransform = go.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(400, 300);

            var gridLayout = go.GetComponent<CustomGridLayoutGroup>();
            gridLayout.cellSize = new Vector2(100, 100);
            gridLayout.spacing = new Vector2(10, 10);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4;

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}
