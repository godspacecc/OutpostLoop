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
            if (GUILayout.Button("Add"))
            {
                AddReference(dataProperty, Guid.NewGuid().GetHashCode().ToString(), null);
            }

            if (GUILayout.Button("Clear"))
            {
                _referenceCollector.Clear();
            }

            if (GUILayout.Button("Remove Null"))
            {
                RemoveNullReference(dataProperty);
            }

            if (GUILayout.Button("Sort"))
            {
                _referenceCollector.Sort();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _searchKey = EditorGUILayout.TextField(_searchKey);
            _searchObject = _referenceCollector.Get<Object>(_searchKey);
            EditorGUILayout.ObjectField(_searchObject, typeof(Object), false);
            if (GUILayout.Button("Delete"))
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

                if (GUILayout.Button("X"))
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
            if (reference is not GameObject gameObject)
            {
                return;
            }

            var componentNames = gameObject.GetComponents<Component>()
                .Where(component => component != null)
                .Select(component => component.GetType().FullName)
                .ToArray();

            if (componentNames.Length == 0)
            {
                return;
            }

            var selectedIndex = Array.FindIndex(componentNames, name => name == typeProperty.stringValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            selectedIndex = EditorGUILayout.Popup(selectedIndex, componentNames);
            typeProperty.stringValue = componentNames[selectedIndex];
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

            if (obj is GameObject gameObject)
            {
                var component = gameObject.GetComponents<Component>().FirstOrDefault(item => item != null);
                if (component != null)
                {
                    element.FindPropertyRelative("TypeName").stringValue = component.GetType().FullName;
                }
            }
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
