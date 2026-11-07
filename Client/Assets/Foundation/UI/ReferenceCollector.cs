using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 保存一个可序列化引用项，使用 key 定位 Unity 对象并记录组件类型名。
    /// </summary>
    [Serializable]
    public class ReferenceCollectorData
    {
        public string key;
        public Object gameObject;
        public string TypeName;
    }

    /// <summary>
    /// 按 key 对引用项排序，保证序列化列表在编辑器工具中稳定展示。
    /// </summary>
    public sealed class ReferenceCollectorDataComparer : IComparer<ReferenceCollectorData>
    {
        public int Compare(ReferenceCollectorData x, ReferenceCollectorData y)
        {
            return string.Compare(x?.key, y?.key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 维护 key 到 Unity 对象的序列化引用映射，运行时按 key 和类型安全取回对象。
    /// </summary>
    public class ReferenceCollector : MonoBehaviour, ISerializationCallbackReceiver
    {
        public List<ReferenceCollectorData> data = new List<ReferenceCollectorData>();

        private readonly Dictionary<string, Object> _dict = new Dictionary<string, Object>();

#if UNITY_EDITOR
        public void Add(string key, Object obj)
        {
            var serializedObject = new UnityEditor.SerializedObject(this);
            var dataProperty = serializedObject.FindProperty("data");

            var index = 0;
            for (; index < data.Count; index++)
            {
                if (data[index].key == key)
                {
                    break;
                }
            }

            if (index != data.Count)
            {
                var element = dataProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("gameObject").objectReferenceValue = obj;
                element.FindPropertyRelative("TypeName").stringValue = ResolveTypeName(obj);
            }
            else
            {
                dataProperty.InsertArrayElementAtIndex(index);
                var element = dataProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("key").stringValue = key;
                element.FindPropertyRelative("gameObject").objectReferenceValue = obj;
                element.FindPropertyRelative("TypeName").stringValue = ResolveTypeName(obj);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            serializedObject.ApplyModifiedProperties();
            serializedObject.UpdateIfRequiredOrScript();
        }

        public void Remove(string key)
        {
            var serializedObject = new UnityEditor.SerializedObject(this);
            var dataProperty = serializedObject.FindProperty("data");

            var index = 0;
            for (; index < data.Count; index++)
            {
                if (data[index].key == key)
                {
                    break;
                }
            }

            if (index != data.Count)
            {
                dataProperty.DeleteArrayElementAtIndex(index);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            serializedObject.ApplyModifiedProperties();
            serializedObject.UpdateIfRequiredOrScript();
        }

        public void Clear()
        {
            var serializedObject = new UnityEditor.SerializedObject(this);
            serializedObject.FindProperty("data").ClearArray();
            UnityEditor.EditorUtility.SetDirty(this);
            serializedObject.ApplyModifiedProperties();
            serializedObject.UpdateIfRequiredOrScript();
        }

        public void Sort()
        {
            data.Sort(new ReferenceCollectorDataComparer());
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private static string ResolveTypeName(Object obj)
        {
            if (obj is GameObject)
            {
                return typeof(GameObject).FullName;
            }

            return obj != null ? obj.GetType().FullName : string.Empty;
        }
#endif

        public T Get<T>(string key) where T : class
        {
            return _dict.TryGetValue(key, out var value) ? value as T : null;
        }

        public Object GetObject(string key)
        {
            return _dict.TryGetValue(key, out var value) ? value : null;
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            _dict.Clear();
            foreach (var item in data)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.key) || _dict.ContainsKey(item.key))
                {
                    continue;
                }

                _dict.Add(item.key, item.gameObject);
            }
        }
    }
}
