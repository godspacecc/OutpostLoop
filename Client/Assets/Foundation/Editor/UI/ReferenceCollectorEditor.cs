using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    [CustomEditor(typeof(ReferenceCollector))]
    public sealed class ReferenceCollectorEditor : UnityEditor.Editor
    {
        private ReferenceCollector _referenceCollector;
        private Object _searchObject;
        private string _searchKey = string.Empty;

        private void OnEnable()
        {
            _referenceCollector = (ReferenceCollector)target;
        }

        public override void OnInspectorGUI()
        {
            Undo.RecordObject(_referenceCollector, "ReferenceCollector");
            var dataProperty = serializedObject.FindProperty("data");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加"))
            {
                AddReference(dataProperty, Guid.NewGuid().GetHashCode().ToString(), null);
            }

            if (GUILayout.Button("清空"))
            {
                _referenceCollector.Clear();
            }

            if (GUILayout.Button("移除空引用"))
            {
                RemoveNullReference(dataProperty);
            }

            if (GUILayout.Button("排序"))
            {
                _referenceCollector.Sort();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _searchKey = EditorGUILayout.TextField(_searchKey);
            _searchObject = _referenceCollector.Get<Object>(_searchKey);
            EditorGUILayout.ObjectField(_searchObject, typeof(Object), false);
            if (GUILayout.Button("删除"))
            {
                _referenceCollector.Remove(_searchKey);
                _searchObject = null;
            }
            EditorGUILayout.EndHorizontal();

            var deleteIndices = new List<int>();
            for (var index = _referenceCollector.data.Count - 1; index >= 0; index--)
            {
                EditorGUILayout.BeginHorizontal();
                var element = dataProperty.GetArrayElementAtIndex(index);
                var keyProperty = element.FindPropertyRelative("key");
                var objectProperty = element.FindPropertyRelative("gameObject");
                var typeProperty = element.FindPropertyRelative("TypeName");

                keyProperty.stringValue = EditorGUILayout.TextField(keyProperty.stringValue, GUILayout.Width(150));
                objectProperty.objectReferenceValue =
                    EditorGUILayout.ObjectField(objectProperty.objectReferenceValue, typeof(Object), true);
                DrawTypeSelector(objectProperty.objectReferenceValue, typeProperty);

                if (GUILayout.Button("删除"))
                {
                    deleteIndices.Add(index);
                }
                EditorGUILayout.EndHorizontal();
            }

            foreach (var index in deleteIndices)
            {
                dataProperty.DeleteArrayElementAtIndex(index);
            }

            HandleDragAndDrop(dataProperty);

            serializedObject.ApplyModifiedProperties();
            serializedObject.UpdateIfRequiredOrScript();
        }

        private static void DrawTypeSelector(Object reference, SerializedProperty typeProperty)
        {
            if (reference == null)
            {
                SyncNonSelectableTypeName(reference, typeProperty);
                EditorGUILayout.LabelField("未绑定");
                return;
            }

            if (reference is Component component)
            {
                DrawSelectableTypePopup(component.gameObject, reference, typeProperty);
                return;
            }

            if (reference is not GameObject gameObject)
            {
                SyncNonSelectableTypeName(reference, typeProperty);
                EditorGUILayout.LabelField(typeProperty.stringValue);
                return;
            }

            DrawSelectableTypePopup(gameObject, reference, typeProperty);
        }

        private static void DrawSelectableTypePopup(GameObject gameObject, Object reference, SerializedProperty typeProperty)
        {
            var componentNames = GetSelectableTypeNames(gameObject);
            typeProperty.stringValue = ResolveSelectableTypeName(reference, typeProperty.stringValue);

            var selectedIndex = Array.FindIndex(componentNames, name => name == typeProperty.stringValue);
            selectedIndex = EditorGUILayout.Popup(selectedIndex, componentNames);
            typeProperty.stringValue = componentNames[selectedIndex];
        }

        internal static string[] GetSelectableTypeNames(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return new[] { typeof(GameObject).FullName };
            }

            return new[] { typeof(GameObject).FullName }
                .Concat(gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().FullName))
                .Distinct()
                .ToArray();
        }

        private static void HandleDragAndDrop(SerializedProperty dataProperty)
        {
            var eventType = Event.current.type;
            if (eventType != EventType.DragUpdated && eventType != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (eventType == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var draggedObject in DragAndDrop.objectReferences)
                {
                    AddReference(dataProperty, draggedObject.name, draggedObject);
                }
            }

            Event.current.Use();
        }

        private static void AddReference(SerializedProperty dataProperty, string key, Object obj)
        {
            var index = dataProperty.arraySize;
            dataProperty.InsertArrayElementAtIndex(index);
            var element = dataProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("key").stringValue = key;
            element.FindPropertyRelative("gameObject").objectReferenceValue = obj;
            element.FindPropertyRelative("TypeName").stringValue = ResolveDefaultTypeName(obj);
        }

        internal static string ResolveDefaultTypeName(Object obj)
        {
            if (obj is GameObject)
            {
                return typeof(GameObject).FullName;
            }

            return obj != null ? obj.GetType().FullName : string.Empty;
        }

        internal static string ResolveStoredTypeName(Object reference)
        {
            return reference != null ? reference.GetType().FullName : string.Empty;
        }

        internal static void SyncNonSelectableTypeName(Object reference, SerializedProperty typeProperty)
        {
            typeProperty.stringValue = ResolveStoredTypeName(reference);
        }

        internal static string ResolveSelectableTypeName(Object reference, string storedTypeName)
        {
            var gameObject = reference as GameObject;
            if (reference is Component component)
            {
                gameObject = component.gameObject;
            }

            if (gameObject == null)
            {
                return ResolveStoredTypeName(reference);
            }

            var candidates = GetSelectableTypeNames(gameObject);
            if (candidates.Contains(storedTypeName))
            {
                return storedTypeName;
            }

            return reference is Component
                ? reference.GetType().FullName
                : typeof(GameObject).FullName;
        }

        private static void RemoveNullReference(SerializedProperty dataProperty)
        {
            for (var index = dataProperty.arraySize - 1; index >= 0; index--)
            {
                var gameObjectProperty = dataProperty.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("gameObject");
                if (gameObjectProperty.objectReferenceValue == null)
                {
                    dataProperty.DeleteArrayElementAtIndex(index);
                }
            }
        }
    }
}
